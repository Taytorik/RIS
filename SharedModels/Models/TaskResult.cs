using System;

namespace SharedModels.Models
{
    public class TaskResult
    {
        public string TaskId { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.Now;
    }
}