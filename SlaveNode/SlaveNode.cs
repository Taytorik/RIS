using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using SharedModels.Utils;
using SharedModels.Models;
using SlaveNode.Models;
using System.Collections.Generic;
using System.Linq;

namespace SlaveNode
{
    public class SlaveNode
    {
        private int _port;
        private string _masterAddress;
        private int _masterPort;
        private string _id;
        private UdpClient _udpClient;
        private TextSummarizer _summarizer;
        private bool _isRunning;
        private readonly ConcurrentDictionary<string, ChunkedTaskState> _activeChunkedTasks;

        public SlaveNode(int port, string masterAddress, int masterPort)
        {
            _port = port;
            _masterAddress = masterAddress;
            _masterPort = masterPort;
            _id = $"Slave_{Guid.NewGuid().ToString().Substring(0, 8)}";

            _udpClient = new UdpClient(_port);
            _udpClient.Client.ReceiveBufferSize = 65536; // 64KB буфер
            _udpClient.Client.SendBufferSize = 65536;

            _summarizer = new TextSummarizer();
            _isRunning = false;
            _activeChunkedTasks = new ConcurrentDictionary<string, ChunkedTaskState>();
        }

        public void Start()
        {
            _isRunning = true;

            RegisterWithMaster();

            Thread heartbeatThread = new Thread(SendHeartbeat);
            heartbeatThread.IsBackground = true;
            heartbeatThread.Start();

            Thread taskProcessingThread = new Thread(ProcessTasks);
            taskProcessingThread.IsBackground = true;
            taskProcessingThread.Start();

            Thread cleanupThread = new Thread(CleanupStalledTasks);
            cleanupThread.IsBackground = true;
            cleanupThread.Start();

            Console.WriteLine($"Slave node {_id} started on port {_port}");
            Console.WriteLine($"Connected to master at {_masterAddress}:{_masterPort}");
        }

        public void Stop()
        {
            _isRunning = false;
            _udpClient?.Close();
            Console.WriteLine($"Slave node {_id} stopped");
        }

        private void RegisterWithMaster()
        {
            try
            {
                using (UdpClient client = new UdpClient())
                {
                    string registerMessage = $"REGISTER:{_id}:{_port}";
                    byte[] data = Encoding.UTF8.GetBytes(registerMessage);
                    IPEndPoint masterEndPoint = new IPEndPoint(IPAddress.Parse(_masterAddress), _masterPort);
                    client.Send(data, data.Length, masterEndPoint);
                    Console.WriteLine($"Registered with master at {_masterAddress}:{_masterPort}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registering with master: {ex.Message}");
            }
        }

        private void SendHeartbeat()
        {
            while (_isRunning)
            {
                try
                {
                    using (UdpClient client = new UdpClient())
                    {
                        string heartbeatMessage = $"HEARTBEAT:{_id}";
                        byte[] data = Encoding.UTF8.GetBytes(heartbeatMessage);
                        IPEndPoint masterEndPoint = new IPEndPoint(IPAddress.Parse(_masterAddress), _masterPort);
                        client.Send(data, data.Length, masterEndPoint);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending heartbeat: {ex.Message}");
                }

                Thread.Sleep(10000);
            }
        }

        private void ProcessTasks()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            Console.WriteLine($"Listening for tasks on port {_port}");

            while (_isRunning)
            {
                try
                {
                    byte[] data = _udpClient.Receive(ref remoteEndPoint);
                    string message = Encoding.UTF8.GetString(data);

                    if (message.StartsWith("TASK_START:"))
                    {
                        string jsonData = message.Substring(11);
                        var taskMetadata = JsonConvert.DeserializeObject<ChunkedTask>(jsonData);

                        if (taskMetadata != null)
                        {
                            Console.WriteLine($"Starting chunked task {taskMetadata.TaskId}, expecting {taskMetadata.TotalChunks} chunks");
                            InitializeChunkedTask(taskMetadata, remoteEndPoint);
                        }
                    }
                    else if (message.StartsWith("TASK_CHUNK:"))
                    {
                        string jsonData = message.Substring(11);
                        var chunk = JsonConvert.DeserializeObject<TaskChunk>(jsonData);

                        if (chunk != null)
                        {
                            ProcessTaskChunk(chunk, remoteEndPoint);
                        }
                    }
                    else if (message.StartsWith("TASK:"))
                    {
                        string jsonData = message.Substring(5);
                        var task = JsonConvert.DeserializeObject<SlaveTask>(jsonData);
                        if (task != null)
                        {
                            ThreadPool.QueueUserWorkItem(_ => ProcessSingleTask(task, remoteEndPoint));
                        }
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.ConnectionReset)
                    {
                        Console.WriteLine($"Socket error processing task: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing task: {ex.Message}");
                }
            }
        }

        private void InitializeChunkedTask(ChunkedTask metadata, IPEndPoint masterEndPoint)
        {
            var taskState = new ChunkedTaskState
            {
                Metadata = metadata,
                Chunks = new string[metadata.TotalChunks],
                ReceivedChunks = 0,
                LastChunkTime = DateTime.Now,
                MasterEndPoint = masterEndPoint
            };

            _activeChunkedTasks[metadata.TaskId] = taskState;
            Console.WriteLine($"Initialized chunked task {metadata.TaskId}");
        }

        private void ProcessTaskChunk(TaskChunk chunk, IPEndPoint masterEndPoint)
        {
            try
            {
                if (!_activeChunkedTasks.TryGetValue(chunk.TaskId, out var taskState))
                {
                    Console.WriteLine($"Received chunk for unknown task: {chunk.TaskId}");
                    return;
                }

                if (chunk.ChunkIndex >= 0 && chunk.ChunkIndex < taskState.Metadata.TotalChunks)
                {
                    taskState.Chunks[chunk.ChunkIndex] = chunk.Data;
                    taskState.ReceivedChunks++;
                    taskState.LastChunkTime = DateTime.Now;
                    taskState.MasterEndPoint = masterEndPoint; 

                    Console.WriteLine($"Received chunk {chunk.ChunkIndex + 1}/{taskState.Metadata.TotalChunks} for task {chunk.TaskId}");

                    bool allChunksReceived = taskState.ReceivedChunks >= taskState.Metadata.TotalChunks;
                    bool isLastChunk = chunk.IsLast;

                    if (allChunksReceived || isLastChunk)
                    {
                        Console.WriteLine($"All chunks received for task {chunk.TaskId}. Starting finalization...");
                        ThreadPool.QueueUserWorkItem(_ => FinalizeChunkedTask(chunk.TaskId));
                    }
                }
                else
                {
                    Console.WriteLine($"Invalid chunk index {chunk.ChunkIndex} for task {chunk.TaskId}. Total chunks: {taskState.Metadata.TotalChunks}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing chunk {chunk.ChunkIndex} for task {chunk.TaskId}: {ex.Message}");
            }
        }

        private void FinalizeChunkedTask(string taskId)
        {
            Console.WriteLine($"Finalizing chunked task {taskId}");

            if (!_activeChunkedTasks.TryGetValue(taskId, out var taskState))
            {
                Console.WriteLine($"Task {taskId} not found in active tasks");
                return;
            }

            try
            {
                Thread.Sleep(500);

                if (taskState.ReceivedChunks < taskState.Metadata.TotalChunks)
                {
                    Console.WriteLine($"Task {taskId} missing {taskState.Metadata.TotalChunks - taskState.ReceivedChunks} chunks, waiting...");

                    Thread.Sleep(1000);

                    if (taskState.ReceivedChunks < taskState.Metadata.TotalChunks)
                    {
                        Console.WriteLine($"Task {taskId} still missing chunks. Received: {taskState.ReceivedChunks}/{taskState.Metadata.TotalChunks}");
                        SendTaskResult(taskId, null, false, taskState.Metadata.FileName, taskState.MasterEndPoint);
                        _activeChunkedTasks.TryRemove(taskId, out _);
                        return;
                    }
                }

                // Собираем полный текст
                StringBuilder fullText = new StringBuilder(taskState.Metadata.TextLength);
                int missingChunks = 0;

                for (int i = 0; i < taskState.Metadata.TotalChunks; i++)
                {
                    if (taskState.Chunks[i] != null)
                    {
                        fullText.Append(taskState.Chunks[i]);
                    }
                    else
                    {
                        Console.WriteLine($"Missing chunk {i} for task {taskId}");
                        missingChunks++;
                    }
                }

                if (missingChunks > 0)
                {
                    Console.WriteLine($"Task {taskId} has {missingChunks} missing chunks");
                    SendTaskResult(taskId, null, false, taskState.Metadata.FileName, taskState.MasterEndPoint);
                    _activeChunkedTasks.TryRemove(taskId, out _);
                    return;
                }

                string completeText = fullText.ToString();
                Console.WriteLine($"Assembled task {taskId}, total text length: {completeText.Length}");

                // Проверяем длину текста
                if (completeText.Length != taskState.Metadata.TextLength)
                {
                    Console.WriteLine($"Text length mismatch: expected {taskState.Metadata.TextLength}, got {completeText.Length}");
                }

                // Обрабатываем текст
                Console.WriteLine($"Starting text summarization for task {taskId}");
                string summary = _summarizer.Summarize(completeText, taskState.Metadata.Ratio);

                if (string.IsNullOrWhiteSpace(summary))
                {
                    Console.WriteLine($"Summary generation returned empty result for task {taskId}");
                    throw new Exception("Summary generation returned empty result");
                }

                Console.WriteLine($"Summary generated for task {taskId}: {summary.Length} chars");

                // Отправляем результат
                SendTaskResult(taskId, summary, true, taskState.Metadata.FileName, taskState.MasterEndPoint);
                SendTaskCompletion(taskId, taskState.MasterEndPoint);

                // Очищаем задачу
                _activeChunkedTasks.TryRemove(taskId, out _);

                Console.WriteLine($"Chunked task {taskId} completed successfully");
                Console.WriteLine($"Original: {completeText.Length} chars, Summary: {summary.Length} chars");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finalizing chunked task {taskId}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                try
                {
                    SendTaskResult(taskId, null, false, taskState.Metadata.FileName, taskState.MasterEndPoint);
                }
                catch (Exception sendEx)
                {
                    Console.WriteLine($"Failed to send error result: {sendEx.Message}");
                }

                _activeChunkedTasks.TryRemove(taskId, out _);
            }
        }

        private void ProcessSingleTask(SlaveTask task, IPEndPoint masterEndPoint)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(task.Text))
                {
                    throw new ArgumentException("Text is empty or null");
                }

                if (task.Ratio <= 0 || task.Ratio > 1)
                {
                    throw new ArgumentException($"Invalid ratio: {task.Ratio}. Must be between 0 and 1");
                }

                Console.WriteLine($"Processing task {task.TaskId}, text length: {task.Text.Length}");

                string summary = _summarizer.Summarize(task.Text, task.Ratio);

                if (string.IsNullOrWhiteSpace(summary))
                {
                    throw new Exception("Summary generation returned empty result");
                }

                SendTaskResult(task.TaskId, summary, true, task.FileName, masterEndPoint);
                SendTaskCompletion(task.TaskId, masterEndPoint);

                Console.WriteLine($"Task {task.TaskId} completed successfully");
                Console.WriteLine($"Original: {task.Text.Length} chars, Summary: {summary.Length} chars");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing task {task.TaskId}: {ex.Message}");
                SendTaskResult(task.TaskId, null, false, task.FileName, masterEndPoint);
            }
        }

        private void SendTaskResult(string taskId, string summary, bool success, string fileName, IPEndPoint masterEndPoint)
        {
            try
            {
                using (UdpClient client = new UdpClient())
                {
                    var result = new TaskResult
                    {
                        TaskId = taskId,
                        Summary = summary ?? string.Empty,
                        Success = success,
                        FileName = fileName
                    };

                    string jsonResult = JsonConvert.SerializeObject(result);
                    byte[] data = Encoding.UTF8.GetBytes($"TASK_RESULT:{jsonResult}");

                    // Используем порт 6001 для обратной связи
                    IPEndPoint callbackEndPoint = new IPEndPoint(masterEndPoint.Address, 6001);

                    Console.WriteLine($"Sending task result for {taskId} to {callbackEndPoint}");
                    int bytesSent = client.Send(data, data.Length, callbackEndPoint);
                    Console.WriteLine($"Task result sent for {taskId}. Bytes: {bytesSent}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending task result for {taskId}: {ex.Message}");
            }
        }

        private void SendTaskCompletion(string taskId, IPEndPoint masterEndPoint)
        {
            try
            {
                using (UdpClient client = new UdpClient())
                {
                    string completeMessage = $"TASK_COMPLETE:{_id}:{taskId}";
                    byte[] data = Encoding.UTF8.GetBytes(completeMessage);

                    // Отправляем на основной порт мастера (6000)
                    IPEndPoint masterCallback = new IPEndPoint(masterEndPoint.Address, 6000);

                    Console.WriteLine($"Sending task completion for {taskId}");
                    client.Send(data, data.Length, masterCallback);
                    Console.WriteLine($"Task completion sent for {taskId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending task completion for {taskId}: {ex.Message}");
            }
        }

        

        private void CleanupStalledTasks()
        {
            while (_isRunning)
            {
                try
                {
                    var now = DateTime.Now;
                    var stalledTasks = _activeChunkedTasks
                        .Where(t => (now - t.Value.LastChunkTime).TotalMinutes > 5)
                        .ToList();

                    foreach (var stalledTask in stalledTasks)
                    {
                        Console.WriteLine($"Cleaning up stalled task: {stalledTask.Key}");
                        _activeChunkedTasks.TryRemove(stalledTask.Key, out _);
                    }

                    Thread.Sleep(30000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in task cleanup: {ex.Message}");
                }
            }
        }

        
    }
}