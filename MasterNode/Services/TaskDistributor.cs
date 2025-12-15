using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MasterNode.Models;
using MasterNode.Services;
using MasterNode.Utils;
using SharedModels.Models;
using SharedModels.Utils;

namespace MasterNode.Services
{
    public class TaskDistributor
    {
        private readonly SlaveNodeManager _slaveManager;
        private readonly NetworkCommunicator _networkCommunicator;
        private readonly TextSummarizer _fallbackSummarizer;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingTasks;
        private readonly ConcurrentDictionary<string, DistributedTask> _distributedTasks;
        private int _tasksProcessed = 0;

        public TaskDistributor(SlaveNodeManager slaveManager, NetworkCommunicator networkCommunicator)
        {
            _slaveManager = slaveManager;
            _networkCommunicator = networkCommunicator;
            _fallbackSummarizer = new TextSummarizer();
            _pendingTasks = new ConcurrentDictionary<string, TaskCompletionSource<string>>();
            _distributedTasks = new ConcurrentDictionary<string, DistributedTask>();
        }
        private async Task<string> SendDistributedTask(string text, float ratio, string fileName)
        {
            string masterTaskId = Guid.NewGuid().ToString();
            Console.WriteLine($"Starting distributed task {masterTaskId} for file: {fileName}");

            try
            {
                var textParts = SplitTextForDistribution(text);
                Console.WriteLine($"Split text into {textParts.Count} parts for distribution");

                var availableSlaves = _slaveManager.SelectMultipleSlaves(textParts.Count);
                if (availableSlaves.Count == 0)
                {
                    Console.WriteLine("No slaves available for distributed processing");
                    return _fallbackSummarizer.Summarize(text, ratio);
                }

                Console.WriteLine($"Selected {availableSlaves.Count} slaves for distributed processing");

                var subTasks = new List<Task<string>>();
                var distributedTask = new DistributedTask
                {
                    MasterTaskId = masterTaskId,
                    Ratio = ratio,
                    FileName = fileName,
                    TotalChunks = textParts.Count,
                    CreatedAt = DateTime.Now
                };

                for (int i = 0; i < textParts.Count; i++)
                {
                    var slave = availableSlaves[i % availableSlaves.Count];
                    var part = textParts[i];
                    var subTaskId = $"{masterTaskId}_part_{i}";

                    distributedTask.SubTasks.Add(new SubTask
                    {
                        TaskId = subTaskId,
                        SlaveId = slave.Id,
                        Text = part,
                        ChunkIndex = i,
                        IsProcessed = false
                    });

                    var subtask = _networkCommunicator.SendTextPartToSlave(
                        slave, part, Math.Min(ratio * 1.5f, 0.7f), fileName, subTaskId, _pendingTasks);
                    subTasks.Add(subtask);
                }

                _distributedTasks[masterTaskId] = distributedTask;

                Console.WriteLine($"Waiting for {subTasks.Count} sub-tasks to complete...");

                try
                {
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(90));
                    var completedTask = await Task.WhenAny(Task.WhenAll(subTasks), timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        Console.WriteLine($"Distributed task {masterTaskId} timeout after 90 seconds");
                        throw new TimeoutException($"Distributed task timeout");
                    }

                    var results = await Task.WhenAll(subTasks);

                    var validResults = new List<string>();
                    var failedTasks = 0;

                    for (int i = 0; i < results.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(results[i]))
                        {
                            validResults.Add(results[i]);
                        }
                        else
                        {
                            Console.WriteLine($"Sub-task {i} returned empty result");
                            failedTasks++;
                        }
                    }

                    if (validResults.Count == 0)
                    {
                        throw new Exception("All sub-tasks failed");
                    }

                    Console.WriteLine($"Successful sub-tasks: {validResults.Count}/{subTasks.Count}");

                    string combinedText = string.Join(" ", validResults);
                    string finalSummary;

                    if (combinedText.Length > text.Length * 0.5) // Если все еще слишком много текста
                    {
                        finalSummary = _fallbackSummarizer.Summarize(combinedText, ratio);
                    }
                    else
                    {
                        // Если уже достаточно сжато, просто возвращаем результат
                        finalSummary = combinedText;
                    }

                    Console.WriteLine($"✓ Distributed task {masterTaskId} completed");
                    Console.WriteLine($"  Original length: {text.Length}");
                    Console.WriteLine($"  Combined parts length: {combinedText.Length}");
                    Console.WriteLine($"  Final summary length: {finalSummary.Length}");
                    Console.WriteLine($"  Target compression: {ratio:P0}, Actual: {(1 - (float)finalSummary.Length / text.Length):P0}");

                    _distributedTasks.TryRemove(masterTaskId, out _);

                    return finalSummary;
                }
                catch (AggregateException ae)
                {
                    Console.WriteLine($"Some sub-tasks failed in distributed task {masterTaskId}");
                    foreach (var ex in ae.InnerExceptions)
                    {
                        Console.WriteLine($"- {ex.GetType().Name}: {ex.Message}");
                    }

                    var completedResults = new List<string>();
                    foreach (var task in subTasks)
                    {
                        if (task.Status == TaskStatus.RanToCompletion && !string.IsNullOrEmpty(task.Result))
                        {
                            completedResults.Add(task.Result);
                        }
                    }

                    if (completedResults.Count > 0)
                    {
                        string partialCombined = string.Join(" ", completedResults);
                        string partialSummary = _fallbackSummarizer.Summarize(partialCombined, ratio);
                        Console.WriteLine($"Using fallback summarization for partial results");
                        return partialSummary;
                    }

                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in distributed task {masterTaskId}: {ex.Message}");
                _distributedTasks.TryRemove(masterTaskId, out _);
                return _fallbackSummarizer.Summarize(text, ratio);
            }
        }

        private string RemoveDuplicateSentences(string text)
        {
            var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
            var uniqueSentences = new HashSet<string>();
            var result = new StringBuilder();

            foreach (var sentence in sentences)
            {
                var trimmed = sentence.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !uniqueSentences.Contains(trimmed))
                {
                    uniqueSentences.Add(trimmed);
                    result.Append(trimmed);
                    result.Append(". ");
                }
            }

            return result.ToString().Trim();
        }

        private List<string> SplitTextForDistribution(string text, int maxPartSize = 15000)
        {
            var parts = new List<string>();

            if (text.Length <= maxPartSize)
            {
                parts.Add(text);
                return parts;
            }

            var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
            var currentPart = new StringBuilder();

            foreach (var sentence in sentences)
            {
                if (currentPart.Length + sentence.Length > maxPartSize && currentPart.Length > 0)
                {
                    parts.Add(currentPart.ToString());
                    currentPart.Clear();
                }
                currentPart.Append(sentence).Append(" ");
            }

            if (currentPart.Length > 0)
            {
                parts.Add(currentPart.ToString());
            }

            if (parts.Count > 10)
            {
                var mergedParts = new List<string>();
                var merged = new StringBuilder();

                foreach (var part in parts)
                {
                    if (merged.Length + part.Length > maxPartSize * 1.5 && merged.Length > 0)
                    {
                        mergedParts.Add(merged.ToString());
                        merged.Clear();
                    }
                    merged.Append(part).Append(" ");
                }

                if (merged.Length > 0)
                {
                    mergedParts.Add(merged.ToString());
                }

                parts = mergedParts;
            }

            Console.WriteLine($"Split text into {parts.Count} parts: " +
                $"{string.Join(", ", parts.Select(p => p.Length))} chars each");
            return parts;
        }

        public void ProcessTaskResult(TaskResult result)
        {
            if (_pendingTasks.TryRemove(result.TaskId, out var tcs))
            {
                if (result.Success)
                {
                    tcs.SetResult(result.Summary);
                    Console.WriteLine($"Result received for task {result.TaskId}");
                    _tasksProcessed++;
                }
                else
                {
                    tcs.SetException(new Exception($"Slave failed to process task: {result.Summary}"));
                    Console.WriteLine($"Slave reported failure for task {result.TaskId}");
                }
            }
            else
            {
                Console.WriteLine($"Orphaned result for task {result.TaskId} - " +
                    $"task already completed or timed out");

                foreach (var dt in _distributedTasks.Values)
                {
                    var subtask = dt.SubTasks.FirstOrDefault(st => st.TaskId == result.TaskId);
                    if (subtask != null)
                    {
                        subtask.IsProcessed = true;
                        subtask.Summary = result.Summary;
                        Console.WriteLine($"Found orphaned task in distributed task {dt.MasterTaskId}");
                        break;
                    }
                }
            }
        }


        public void ProcessTaskCompletion(string slaveId, string taskId)
        {
            _slaveManager.DecrementSlaveTaskCount(slaveId);
        }

        public object GetSystemStatus()
        {
            var slaveStatus = _slaveManager.GetSystemStatus();
            return new
            {
                master = new
                {
                    status = "running",
                    pendingTasks = _pendingTasks.Count,
                    totalTasksProcessed = _tasksProcessed,
                    distributedTasks = _distributedTasks.Count
                },
                slaves = slaveStatus
            };
        }

        public async Task<string> WaitForTaskCompletion(string taskId, TimeSpan timeout)
        {
            if (!_pendingTasks.TryGetValue(taskId, out var tcs))
            {
                throw new InvalidOperationException($"Task {taskId} not found in pending tasks");
            }

            using (var cts = new CancellationTokenSource(timeout))
            {
                try
                {
                    var timeoutTask = Task.Delay(timeout, cts.Token);
                    var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                    if (completedTask == tcs.Task)
                    {
                        cts.Cancel();
                        return await tcs.Task;
                    }
                    else
                    {
                        throw new TimeoutException($"Task {taskId} timeout after {timeout.TotalSeconds} seconds");
                    }
                }
                catch (OperationCanceledException)
                {
                    return await tcs.Task;
                }
            }
        }


        //для времени

        private static Dictionary<string, DateTime> _taskStartTimes = new Dictionary<string, DateTime>();

        public async Task<string> DistributeTaskAsync(string text, float ratio, string fileName)
        {
            string taskId = Guid.NewGuid().ToString();
            _taskStartTimes[taskId] = DateTime.Now;

            Console.WriteLine($"\nНачало обработки задачи {taskId}");
            Console.WriteLine($"  Длина текста: {text.Length} символов");
            Console.WriteLine($"  Запрошенное сжатие: {ratio:P0}");

            try
            {
                string result;

                if (text.Length > 20000 && _slaveManager.SelectMultipleSlaves(2).Count >= 2)
                {
                    Console.WriteLine("  Большой текст -> используется распределенная обработка");
                    result = await SendDistributedTask(text, ratio, fileName);
                }
                else
                {
                    var selectedSlave = _slaveManager.SelectSlave();

                    if (selectedSlave == null)
                    {
                        Console.WriteLine("  Нет доступных slaves, используется fallback");
                        result = _fallbackSummarizer.Summarize(text, ratio);
                    }
                    else
                    {
                        Console.WriteLine($"  Используется один slave: {selectedSlave.Id}");

                        result = await _networkCommunicator.SendStandardTask(selectedSlave, text, ratio,
                            fileName, taskId, _pendingTasks);
                    }
                }

                var endTime = DateTime.Now;
                var duration = endTime - _taskStartTimes[taskId];

                Console.WriteLine($"\nЗадача {taskId} завершена");
                Console.WriteLine($"  Общее время: {duration.TotalSeconds:F3} сек");
                Console.WriteLine($"  Длина исходного текста: {text.Length}");
                Console.WriteLine($"  Длина сводки: {result?.Length ?? 0}");
                Console.WriteLine($"  Запрошенное сжатие: {ratio:P0}");
                Console.WriteLine($"  Фактическое сжатие: {(float)(result?.Length ?? 0) / text.Length:P0}");

                _taskStartTimes.Remove(taskId);
                return result;
            }
            catch (Exception ex)
            {
                var endTime = DateTime.Now;
                var duration = endTime - _taskStartTimes[taskId];

                Console.WriteLine($"\nЗадача {taskId} завершена с ошибкой");
                Console.WriteLine($"  Время выполнения: {duration.TotalSeconds:F3} сек");
                Console.WriteLine($"  Ошибка: {ex.Message}");

                _taskStartTimes.Remove(taskId);
                throw;
            }
        }

        public string ProcessInSingleThreadMode(string text, float ratio)
        {
            Console.WriteLine($"\nЗАПУСК ОДНОПОТОЧНОГО РЕЖИМА");
            Console.WriteLine($"  Длина текста: {text.Length} символов");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            string result = _fallbackSummarizer.Summarize(text, ratio);

            stopwatch.Stop();

            Console.WriteLine($"\nОднопоточная обработка завершена");
            Console.WriteLine($"  Время: {stopwatch.Elapsed.TotalSeconds:F3} сек");
            Console.WriteLine($"  Исходный текст: {text.Length} символов");
            Console.WriteLine($"  Сводка: {result?.Length ?? 0} символов");
            Console.WriteLine($"  Сжатие: { (double)(result?.Length ?? 0) / text.Length:P2}");

            return result;
        }
    }
}