using System;

namespace SharedModels.Models
{
    public class SlaveTask
    {
        public string TaskId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public float Ratio { get; set; } = 0.3f;
        public int MasterCallbackPort { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}