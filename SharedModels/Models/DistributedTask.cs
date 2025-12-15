using System;
using System.Collections.Generic;

namespace SharedModels.Models
{
    public class DistributedTask
    {
        public string MasterTaskId { get; set; } = string.Empty;
        public List<SubTask> SubTasks { get; set; } = new List<SubTask>();
        public float Ratio { get; set; } = 0.3f;
        public string FileName { get; set; } = string.Empty;
        public int TotalChunks { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }


}