using System;
using System.Collections.Generic;
using System.Linq;
using MasterNode.Models;

namespace MasterNode.Services
{
    public class SlaveNodeManager
    {
        private readonly List<SlaveNodeInfo> _slaveNodes;
        private readonly object _slaveLock = new object();

        public SlaveNodeManager()
        {
            _slaveNodes = new List<SlaveNodeInfo>();
        }

        public void RegisterOrUpdateSlave(string slaveId, string address, int port)
        {
            lock (_slaveLock)
            {
                var existingSlave = _slaveNodes.FirstOrDefault(s => s.Id == slaveId);
                if (existingSlave != null)
                {
                    existingSlave.LastHeartbeat = DateTime.Now;
                    existingSlave.Port = port;
                    existingSlave.Address = address;
                    existingSlave.CurrentTasks = 0;
                    Console.WriteLine($"Slave updated: {slaveId} at {address}:{port}");
                }
                else
                {
                    _slaveNodes.Add(new SlaveNodeInfo
                    {
                        Id = slaveId,
                        Address = address,
                        Port = port,
                        CurrentTasks = 0,
                        LastHeartbeat = DateTime.Now
                    });
                    Console.WriteLine($"Slave registered: {slaveId} at {address}:{port}");
                }

                DisplaySlavesStatus();
            }
        }

        public void UpdateSlaveHeartbeat(string slaveId)
        {
            lock (_slaveLock)
            {
                var slave = _slaveNodes.FirstOrDefault(s => s.Id == slaveId);
                if (slave != null)
                {
                    slave.LastHeartbeat = DateTime.Now;
                }
            }
        }

        public SlaveNodeInfo SelectSlave()
        {
            lock (_slaveLock)
            {
                var activeSlaves = _slaveNodes
                    .Where(s => s.IsActive && s.CurrentTasks >= 0)
                    .ToList();

                if (activeSlaves.Count == 0)
                {
                    Console.WriteLine("No active slave nodes available");
                    DisplaySlavesStatus();
                    return null;
                }

                var minTaskCount = activeSlaves.Min(s => s.CurrentTasks);
                var leastLoadedSlaves = activeSlaves.Where(s => s.CurrentTasks == minTaskCount).ToList();

                var random = new Random();
                var selectedSlave = leastLoadedSlaves[random.Next(leastLoadedSlaves.Count)];

                selectedSlave.CurrentTasks++;
                Console.WriteLine($"Selected slave: {selectedSlave.Id} with {selectedSlave.CurrentTasks} tasks");

                DisplaySlavesStatus();
                return selectedSlave;
            }
        }

        public List<SlaveNodeInfo> SelectMultipleSlaves(int requiredCount)
        {
            lock (_slaveLock)
            {
                var activeSlaves = _slaveNodes
                    .Where(s => s.IsActive && s.CurrentTasks >= 0)
                    .OrderBy(s => s.CurrentTasks)
                    .ToList();

                if (activeSlaves.Count == 0)
                    return new List<SlaveNodeInfo>();

                int takeCount = Math.Min(requiredCount, Math.Min(3, activeSlaves.Count));
                var selectedSlaves = activeSlaves.Take(takeCount).ToList();

                foreach (var slave in selectedSlaves)
                {
                    slave.CurrentTasks++;
                }

                Console.WriteLine($"Selected {selectedSlaves.Count} slaves: " +
                    $"{string.Join(", ", selectedSlaves.Select(s => s.Id))}");
                DisplaySlavesStatus();

                return selectedSlaves;
            }
        }

        public void DecrementSlaveTaskCount(string slaveId)
        {
            lock (_slaveLock)
            {
                var slave = _slaveNodes.FirstOrDefault(s => s.Id == slaveId);
                if (slave != null && slave.CurrentTasks > 0)
                {
                    slave.CurrentTasks--;
                    Console.WriteLine($"Decremented task count for slave {slaveId}. " +
                        $"Current tasks: {slave.CurrentTasks}");
                }
            }
        }

        public void CleanupInactiveSlaves()
        {
            lock (_slaveLock)
            {
                var inactiveSlaves = _slaveNodes.Where(s => !s.IsActive).ToList();
                foreach (var slave in inactiveSlaves)
                {
                    Console.WriteLine($"Removing inactive slave: {slave.Id} (last seen: " +
                        $"{(DateTime.Now - slave.LastHeartbeat).TotalSeconds:F0}s ago, " +
                        $"tasks: {slave.CurrentTasks})");
                    _slaveNodes.Remove(slave);
                }

                if (inactiveSlaves.Count > 0)
                {
                    Console.WriteLine($"Removed {inactiveSlaves.Count} inactive slave nodes");
                    DisplaySlavesStatus();
                }
            }
        }

        public object GetSystemStatus()
        {
            lock (_slaveLock)
            {
                var activeSlaves = _slaveNodes.Where(s => s.IsActive).ToList();
                return new
                {
                    slaves = activeSlaves.Select(s => new
                    {
                        id = s.Id,
                        address = s.Address,
                        port = s.Port,
                        currentTasks = s.CurrentTasks,
                        lastHeartbeat = s.LastHeartbeat,
                        isActive = s.IsActive
                    }),
                    totalSlaves = _slaveNodes.Count,
                    activeSlaves = activeSlaves.Count
                };
            }
        }

        private void DisplaySlavesStatus()
        {
            lock (_slaveLock)
            {
                var activeSlaves = _slaveNodes.Where(s => s.IsActive).ToList();
                var inactiveSlaves = _slaveNodes.Where(s => !s.IsActive).ToList();

                Console.WriteLine($"Slave Nodes Status (Total: {_slaveNodes.Count}):");
                Console.WriteLine($"Active: {activeSlaves.Count}");
                foreach (var slave in activeSlaves.OrderBy(s => s.CurrentTasks))
                {
                    Console.WriteLine($"- {slave.Id} at {slave.Address}:{slave.Port} " +
                        $"(tasks: {slave.CurrentTasks}, last heartbeat: " +
                        $"{(DateTime.Now - slave.LastHeartbeat).TotalSeconds:F0}s ago)");
                }

                if (inactiveSlaves.Count > 0)
                {
                    Console.WriteLine($"Inactive: {inactiveSlaves.Count}");
                    foreach (var slave in inactiveSlaves)
                    {
                        Console.WriteLine($"- {slave.Id} (last seen: " +
                            $"{(DateTime.Now - slave.LastHeartbeat).TotalSeconds:F0}s ago)");
                    }
                }
                Console.WriteLine($"Total tasks in progress: {activeSlaves.Sum(s => s.CurrentTasks)}");
            }
        }
    }
}