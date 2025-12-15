using Newtonsoft.Json;
using System;


    namespace MasterNode.Models
    {
        public class SummaryRequest
        {
            public string Text { get; set; } = string.Empty;

            [JsonProperty("ratio")]
            private float _ratio = 0.3f;

            public float Ratio
            {
                get => _ratio;
                set
                {
                    if (value > 1 && value <= 100)
                    {
                        _ratio = value / 100f;
                        Console.WriteLine($"Converted ratio from percentage: {value}% -> {_ratio}");
                    }
                    else if (value > 0 && value <= 1)
                    {
                        _ratio = value;
                    }
                    else
                    {
                        _ratio = 0.3f;
                        Console.WriteLine($"Invalid ratio {value}, using default 0.3");
                    }
                }
            }

            public string FileName { get; set; } = "text_input.txt";
        }
    }
