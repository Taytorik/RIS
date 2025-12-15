using MasterNode.Models;
using Newtonsoft.Json;
using SharedModels.Models;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MasterNode.Services
{
    public class NetworkCommunicator
    {
        private readonly SlaveNodeManager _slaveManager;
        private TaskDistributor _taskDistributor;

        public NetworkCommunicator(SlaveNodeManager slaveManager)
        {
            _slaveManager = slaveManager;
        }

        public void SetTaskDistributor(TaskDistributor taskDistributor)
        {
            _taskDistributor = taskDistributor;
        }

        public void StartUdpListeners()
        {
            StartRegistrationListener();
            StartResultListener();
        }

        private void StartRegistrationListener()
        {
            var thread = new Thread(() =>
            {
                UdpClient udpListener = new UdpClient(6000);
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                Console.WriteLine("Listening for slave registrations on port 6000");

                while (true)
                {
                    try
                    {
                        byte[] data = udpListener.Receive(ref remoteEndPoint);
                        string message = Encoding.UTF8.GetString(data);

                        if (message.StartsWith("REGISTER:"))
                        {
                            string[] parts = message.Split(':');
                            if (parts.Length >= 3)
                            {
                                string slaveId = parts[1];
                                int slavePort = int.Parse(parts[2]);
                                _slaveManager.RegisterOrUpdateSlave(slaveId,
                                    remoteEndPoint.Address.ToString(), slavePort);
                            }
                        }
                        else if (message.StartsWith("HEARTBEAT:"))
                        {
                            string slaveId = message.Substring(10);
                            _slaveManager.UpdateSlaveHeartbeat(slaveId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in slave registration: {ex.Message}");
                    }
                }
            })
            { IsBackground = true };
            thread.Start();
        }

        private void StartResultListener()
        {
            var thread = new Thread(() =>
            {
                UdpClient udpListener = new UdpClient(6001);
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                Console.WriteLine("Listening for task results on port 6001");

                while (true)
                {
                    try
                    {
                        byte[] data = udpListener.Receive(ref remoteEndPoint);
                        string message = Encoding.UTF8.GetString(data);

                        if (message.StartsWith("TASK_RESULT:"))
                        {
                            string jsonData = message.Substring(12);
                            var result = JsonConvert.DeserializeObject<TaskResult>(jsonData);
                            if (result != null)
                            {
                                _taskDistributor.ProcessTaskResult(result);
                            }
                        }
                        else if (message.StartsWith("TASK_COMPLETE:"))
                        {
                            string[] parts = message.Split(':');
                            if (parts.Length == 3)
                            {
                                string slaveId = parts[1];
                                string taskId = parts[2];
                                _taskDistributor.ProcessTaskCompletion(slaveId, taskId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing task result: {ex.Message}");
                    }
                }
            })
            { IsBackground = true };
            thread.Start();
        }

        public async Task<string> SendStandardTask(SlaveNodeInfo slave, string text, float ratio,
            string fileName, string taskId,
            ConcurrentDictionary<string, TaskCompletionSource<string>> pendingTasks)
        {
            var tcs = new TaskCompletionSource<string>();

            if (!slave.IsActive)
            {
                Console.WriteLine($"Slave {slave.Id} is no longer active");
                _slaveManager.DecrementSlaveTaskCount(slave.Id);
                return null;
            }

            if (!pendingTasks.TryAdd(taskId, tcs))
            {
                Console.WriteLine($"Task {taskId} already exists in pending tasks");
                _slaveManager.DecrementSlaveTaskCount(slave.Id);
                return null;
            }

            try
            {
                using (UdpClient udpClient = new UdpClient())
                {
                    var taskData = new SlaveTask
                    {
                        TaskId = taskId,
                        Text = text,
                        Ratio = ratio,
                        MasterCallbackPort = 6001,
                        FileName = fileName
                    };

                    string jsonData = JsonConvert.SerializeObject(taskData);
                    byte[] data = Encoding.UTF8.GetBytes($"TASK:{jsonData}");

                    if (data.Length > 65507)
                    {
                        Console.WriteLine($"Packet too large ({data.Length} bytes), using chunked method");
                        return await SendChunkedTask(slave, text, ratio, fileName, taskId, pendingTasks);
                    }

                    IPEndPoint slaveEndPoint = new IPEndPoint(IPAddress.Parse(slave.Address), slave.Port);
                    await udpClient.SendAsync(data, data.Length, slaveEndPoint);

                    Console.WriteLine($"Sent standard task {taskId} to slave {slave.Id}");
                }

                return await _taskDistributor.WaitForTaskCompletion(taskId, TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending standard task to slave: {ex.Message}");
                pendingTasks.TryRemove(taskId, out _);
                _slaveManager.DecrementSlaveTaskCount(slave.Id);
                return null;
            }
        }

        public async Task<string> SendChunkedTask(SlaveNodeInfo slave, string text, float ratio,
            string fileName, string taskId,
            ConcurrentDictionary<string, TaskCompletionSource<string>> pendingTasks)
        {
            const int CHUNK_SIZE = 8000;
            const int MAX_RETRIES = 3;
            const int DELAY_BETWEEN_CHUNKS = 50;

            if (text.Length > 1000000)
            {
                Console.WriteLine($"File too large ({text.Length} chars), using fallback summarizer");
                return null;
            }

            int totalChunks = (int)Math.Ceiling((double)text.Length / CHUNK_SIZE);
            Console.WriteLine($"Sending chunked task {taskId}, {totalChunks} chunks, total size: {text.Length} chars");

            var tcs = new TaskCompletionSource<string>();
            pendingTasks[taskId] = tcs;

            try
            {
                using (UdpClient udpClient = new UdpClient())
                {
                    udpClient.Client.SendBufferSize = 65536;
                    IPEndPoint slaveEndPoint = new IPEndPoint(IPAddress.Parse(slave.Address), slave.Port);

                    var taskMetadata = new ChunkedTask
                    {
                        TaskId = taskId,
                        Ratio = ratio,
                        FileName = fileName,
                        TotalChunks = totalChunks,
                        ChunkSize = CHUNK_SIZE,
                        TextLength = text.Length
                    };

                    string metadataJson = JsonConvert.SerializeObject(taskMetadata);
                    byte[] metadataBytes = Encoding.UTF8.GetBytes($"TASK_START:{metadataJson}");

                    bool metadataSent = false;
                    for (int retry = 0; retry < MAX_RETRIES && !metadataSent; retry++)
                    {
                        try
                        {
                            await udpClient.SendAsync(metadataBytes, metadataBytes.Length, slaveEndPoint);
                            Console.WriteLine($"Sent task metadata for {taskId} (attempt {retry + 1})");
                            metadataSent = true;
                            await Task.Delay(100);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to send metadata (attempt {retry + 1}): {ex.Message}");
                            if (retry == MAX_RETRIES - 1) throw;
                        }
                    }

                    if (!metadataSent)
                    {
                        throw new Exception("Failed to send task metadata");
                    }

                    int successChunks = 0;
                    for (int i = 0; i < totalChunks; i++)
                    {
                        bool chunkSent = false;
                        for (int retry = 0; retry < MAX_RETRIES && !chunkSent; retry++)
                        {
                            try
                            {
                                int chunkSize = Math.Min(CHUNK_SIZE, text.Length - i * CHUNK_SIZE);
                                string chunkData = text.Substring(i * CHUNK_SIZE, chunkSize);

                                var chunk = new TaskChunk
                                {
                                    TaskId = taskId,
                                    ChunkIndex = i,
                                    Data = chunkData,
                                    IsLast = i == totalChunks - 1
                                };

                                string chunkJson = JsonConvert.SerializeObject(chunk);
                                byte[] chunkBytes = Encoding.UTF8.GetBytes($"TASK_CHUNK:{chunkJson}");

                                await udpClient.SendAsync(chunkBytes, chunkBytes.Length, slaveEndPoint);
                                successChunks++;
                                chunkSent = true;

                                Console.WriteLine($"✓ Sent chunk {i + 1}/{totalChunks} for task {taskId}");

                                if (i < totalChunks - 1)
                                {
                                    await Task.Delay(DELAY_BETWEEN_CHUNKS);
                                }
                            }
                            catch (Exception chunkEx)
                            {
                                Console.WriteLine($"Failed to send chunk {i} (attempt {retry + 1}): {chunkEx.Message}");
                                if (retry == MAX_RETRIES - 1)
                                {
                                    Console.WriteLine($"Failed to send chunk {i} after {MAX_RETRIES} attempts");
                                }
                            }
                        }
                    }

                    Console.WriteLine($"Successfully sent {successChunks}/{totalChunks} chunks for task {taskId}");

                    if (successChunks < totalChunks)
                    {
                        throw new Exception($"Only {successChunks}/{totalChunks} chunks were sent successfully");
                    }
                }

                return await _taskDistributor.WaitForTaskCompletion(taskId, TimeSpan.FromSeconds(60));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in chunked task {taskId}: {ex.Message}");
                pendingTasks.TryRemove(taskId, out _);
                _slaveManager.DecrementSlaveTaskCount(slave.Id);
                return null;
            }
        }

        public async Task<string> SendTextPartToSlave(SlaveNodeInfo slave, string textPart, float ratio,
    string fileName, string taskId,
    ConcurrentDictionary<string, TaskCompletionSource<string>> pendingTasks)
        {
            var tcs = new TaskCompletionSource<string>();

            if (!slave.IsActive)
            {
                Console.WriteLine($"Slave {slave.Id} is no longer active");
                _slaveManager.DecrementSlaveTaskCount(slave.Id);
                return null;
            }

            if (!pendingTasks.TryAdd(taskId, tcs))
            {
                Console.WriteLine($"Task {taskId} already exists in pending tasks");
                _slaveManager.DecrementSlaveTaskCount(slave.Id);
                return null;
            }

            try
            {
                using (UdpClient udpClient = new UdpClient())
                {
                    var taskData = new SlaveTask
                    {
                        TaskId = taskId,
                        Text = textPart,
                        Ratio = ratio,
                        MasterCallbackPort = 6001,
                        FileName = $"{fileName}_part_{taskId}"
                    };

                    string jsonData = JsonConvert.SerializeObject(taskData);
                    byte[] data = Encoding.UTF8.GetBytes($"TASK:{jsonData}");

                    IPEndPoint slaveEndPoint = new IPEndPoint(IPAddress.Parse(slave.Address), slave.Port);
                    await udpClient.SendAsync(data, data.Length, slaveEndPoint);

                    Console.WriteLine($"✓ Sent part {taskId} to slave {slave.Id}");
                }

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                {
                    try
                    {
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(45), cts.Token);
                        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                        if (completedTask == tcs.Task)
                        {
                            cts.Cancel();
                            string result = await tcs.Task;
                            return result;
                        }
                        else
                        {
                            Console.WriteLine($"Timeout for task {taskId}");
                            pendingTasks.TryRemove(taskId, out _);
                            _slaveManager.DecrementSlaveTaskCount(slave.Id);
                            return null;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        string result = await tcs.Task;
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending part to slave {slave.Id}: {ex.Message}");

                pendingTasks.TryRemove(taskId, out _);
                _slaveManager.DecrementSlaveTaskCount(slave.Id);
                return null;
            }
        }

        public async Task<string> WaitForTaskCompletion(
    ConcurrentDictionary<string, TaskCompletionSource<string>> pendingTasks,
    string taskId, TimeSpan timeout)
        {
            if (!pendingTasks.TryGetValue(taskId, out var tcs))
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
                        pendingTasks.TryRemove(taskId, out _);
                        throw new TimeoutException($"Task {taskId} timeout after {timeout.TotalSeconds} seconds");
                    }
                }
                catch (OperationCanceledException)
                {
                    return await tcs.Task;
                }
            }
        }
    }
}