using System;

namespace MasterNode.Models
{
    public class SlaveNodeInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }
        public int CurrentTasks { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public bool IsActive => (DateTime.Now - LastHeartbeat).TotalSeconds < 30;
    }
}