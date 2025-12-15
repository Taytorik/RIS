using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedModels.Models
{
    public class ChunkedTask
    {
        public string TaskId { get; set; } = string.Empty;
        public float Ratio { get; set; } = 0.3f;
        public string FileName { get; set; } = string.Empty;
        public int TotalChunks { get; set; }
        public int ChunkSize { get; set; } = 30000;
        public int TextLength { get; set; }
    }
}
