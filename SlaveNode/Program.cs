using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SlaveNode
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Автоматически находим свободный UDP порт
                int slavePort = FindFreeUdpPort(7000, 7100);
                string masterAddress = "127.0.0.1";
                int masterPort = 6000;

                if (args.Length > 0)
                {
                    masterAddress = args[0];
                }
                if (args.Length > 1)
                {
                    int.TryParse(args[1], out masterPort);
                }

                Console.WriteLine($"Starting Slave Node on UDP port {slavePort}...");
                Console.WriteLine($"Master: {masterAddress}:{masterPort}");

                SlaveNode slave = new SlaveNode(slavePort, masterAddress, masterPort);
                slave.Start();

                Console.WriteLine("Press 'q' to stop the slave node...");
                while (Console.ReadKey().Key != ConsoleKey.Q)
                {
                    Thread.Sleep(100);
                }

                slave.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start Slave Node: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private static int FindFreeUdpPort(int startPort, int endPort)
        {
            for (int port = startPort; port <= endPort; port++)
            {
                try
                {
                    using (var udpClient = new UdpClient(port))
                    {
                        Console.WriteLine($"Found free UDP port: {port}");
                        return port;
                    }
                }
                catch (SocketException)
                {
                    Console.WriteLine($"UDP Port {port} is busy, trying next...");
                }
            }

            throw new Exception($"No free UDP ports found in range {startPort}-{endPort}");
        }
    }
}