using System;
using System.Diagnostics;
using System.Threading;

namespace MasterNode
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                int port = 5000;
                if (args.Length > 0 && int.TryParse(args[0], out int customPort))
                {
                    port = customPort;
                }

                Console.WriteLine("Starting Master Node...");

                // Запускаем MasterNode
                MasterNode master = new MasterNode(port);
                Thread masterThread = new Thread(() => master.Start());
                masterThread.IsBackground = true;
                masterThread.Start();

                // Даем время на запуск сервера
                Thread.Sleep(2000);

                // Автоматически открываем браузер
                OpenBrowser($"http://localhost:{port}/");

                Console.WriteLine($"Master Node started on port {port}");
                Console.WriteLine($"Browser should open automatically at http://localhost:{port}");
                Console.WriteLine("Press 'q' to quit...");

                while (Console.ReadKey().Key != ConsoleKey.Q)
                {
                    Thread.Sleep(100);
                }

                Console.WriteLine("\nShutting down Master Node...");
                master.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start Master Node: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private static void OpenBrowser(string url)
        {
            try
            {
                Console.WriteLine($"Opening browser: {url}");

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };

                Process.Start(psi);
                Console.WriteLine("Browser opened successfully");
            }
            catch (Exception ex)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd",
                        Arguments = $"/c start {url}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    Console.WriteLine("Browser opened using fallback method");
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"Could not open browser automatically: {fallbackEx.Message}");
                    Console.WriteLine($"Please open manually: {url}");
                }
            }
        }
    }
}