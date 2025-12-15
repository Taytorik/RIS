using SharedModels.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SlaveNode.Models
{
    public class ChunkedTaskState
    {
        public ChunkedTask Metadata { get; set; }
        public string[] Chunks { get; set; }
        public int ReceivedChunks { get; set; }
        public DateTime LastChunkTime { get; set; }
        public IPEndPoint MasterEndPoint { get; set; }
    }
}
