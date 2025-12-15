using System;
using System.IO;
using System.Net;
using System.Threading;
using MasterNode.Services;

namespace MasterNode
{
    public class MasterNode
    {
        private readonly HttpListener _listener;
        private readonly int _port;
        private readonly string _uploadDirectory;
        private readonly SlaveNodeManager _slaveManager;
        private readonly NetworkCommunicator _networkCommunicator;
        private readonly TaskDistributor _taskDistributor;
        private readonly HttpRequestHandler _requestHandler;
        private bool _isRunning;

        public MasterNode(int port)
        {
            _port = port;
            _isRunning = false;

            _slaveManager = new SlaveNodeManager();
            _networkCommunicator = new NetworkCommunicator(_slaveManager);
            _taskDistributor = new TaskDistributor(_slaveManager, _networkCommunicator);
            _networkCommunicator.SetTaskDistributor(_taskDistributor);

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            _uploadDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uploads");
            if (!Directory.Exists(_uploadDirectory))
                Directory.CreateDirectory(_uploadDirectory);

            _requestHandler = new HttpRequestHandler(_taskDistributor, _uploadDirectory);
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            // Запуск фоновых задач
            Thread cleanupThread = new Thread(CleanupInactiveSlaves);
            cleanupThread.IsBackground = true;
            cleanupThread.Start();

            // Запуск сетевых слушателей
            _networkCommunicator.StartUdpListeners();

            // Запуск HTTP сервера
            _listener.Start();
            Console.WriteLine($"Master node started on port {_port}");
            Console.WriteLine($"Listening for HTTP requests on http://localhost:{_port}/");
            Console.WriteLine($"Listening for slave registrations on UDP port 6000");
            Console.WriteLine($"Listening for task results on UDP port 6001");
            Console.WriteLine($"Upload directory: {_uploadDirectory}");

            while (_isRunning)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => _requestHandler.HandleRequestAsync(context).Wait());
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Console.WriteLine($"Error in main loop: {ex.Message}");
                    }
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();
            Console.WriteLine("Master node stopped");
        }

        private void CleanupInactiveSlaves()
        {
            while (_isRunning)
            {
                try
                {
                    _slaveManager.CleanupInactiveSlaves();
                    Thread.Sleep(15000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in cleanup: {ex.Message}");
                }
            }
        }
    }
}
