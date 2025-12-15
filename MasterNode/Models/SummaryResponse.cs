using Newtonsoft.Json;

namespace MasterNode.Models
{
    public class SummaryResponse
    {
        [JsonProperty("taskId")]
        public string TaskId { get; set; } = string.Empty;

        [JsonProperty("originalLength")]
        public int OriginalLength { get; set; }

        [JsonProperty("summaryLength")]
        public int SummaryLength { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonProperty("status")]
        public string Status { get; set; } = "success";

        [JsonProperty("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("compressionRatio")]
        public float CompressionRatio { get; set; }
    }
}