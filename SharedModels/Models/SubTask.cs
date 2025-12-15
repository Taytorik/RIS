using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedModels.Models
{
    public class SubTask
    {
        public string TaskId { get; set; } = string.Empty;
        public string SlaveId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public bool IsProcessed { get; set; }
        public string Summary { get; set; } = string.Empty;
    }
}
