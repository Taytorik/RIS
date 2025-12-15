//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.IO;
//using System.Linq;
//using Newtonsoft.Json;

//namespace SharedModels.Utils
//{
//    public class PerformanceResult
//    {
//        public string TestName { get; set; }
//        public string Mode { get; set; } // "SingleThread" или "MultiThread"
//        public int TextLength { get; set; }
//        public int SlaveCount { get; set; }
//        public long TimeMs { get; set; }
//        public double TimeSeconds => TimeMs / 1000.0;
//        public bool Success { get; set; }
//        public int OriginalLength { get; set; }
//        public int SummaryLength { get; set; }
//        public double CompressionRatio => 1 - (double)SummaryLength / OriginalLength;
//        public DateTime Timestamp { get; set; } = DateTime.Now;
//    }

//    public class PerformanceMeasurer
//    {
//        private static List<PerformanceResult> _results = new List<PerformanceResult>();
//        private static Stopwatch _stopwatch = new Stopwatch();

//        public static void StartMeasurement()
//        {
//            _stopwatch.Restart();
//        }

//        public static void StopMeasurement(PerformanceResult result)
//        {
//            _stopwatch.Stop();
//            result.TimeMs = _stopwatch.ElapsedMilliseconds;
//            _results.Add(result);

//            Console.WriteLine($"\nРезультат замера:");
//            Console.WriteLine($"  Тест: {result.TestName}");
//            Console.WriteLine($"  Режим: {result.Mode}");
//            Console.WriteLine($"  Время: {result.TimeSeconds:F3} сек");
//            Console.WriteLine($"  Длина текста: {result.TextLength} символов");
//            Console.WriteLine($"  Slave узлов: {result.SlaveCount}");
//            Console.WriteLine($"  Сжатие: {result.CompressionRatio:P2}");
//        }

//        public static void SaveResults(string filePath = "performance_results.json")
//        {
//            try
//            {
//                var json = JsonConvert.SerializeObject(_results, Formatting.Indented);
//                File.WriteAllText(filePath, json);
//                Console.WriteLine($"\nРезультаты сохранены в {filePath}");

//                // Также сохранить в CSV для удобства анализа
//                SaveResultsToCsv(Path.ChangeExtension(filePath, ".csv"));
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Ошибка сохранения результатов: {ex.Message}");
//            }
//        }

//        private static void SaveResultsToCsv(string filePath)
//        {
//            try
//            {
//                using (var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
//                {
//                    writer.WriteLine("TestName;Mode;TextLength;SlaveCount;TimeMs;TimeSeconds;Success;OriginalLength;SummaryLength;CompressionRatio;Timestamp");
//                    foreach (var result in _results)
//                    {
//                        writer.WriteLine($"{result.TestName};{result.Mode};{result.TextLength};{result.SlaveCount};{result.TimeMs};{result.TimeSeconds:F3};{result.Success};{result.OriginalLength};{result.SummaryLength};{result.CompressionRatio:F4};{result.Timestamp:yyyy-MM-dd HH:mm:ss}");
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Ошибка сохранения CSV: {ex.Message}");
//            }
//        }

//        public static void PrintSummary()
//        {
//            Console.WriteLine("\nСВОДКА ПРОИЗВОДИТЕЛЬНОСТИ:");
//            Console.WriteLine("=".PadRight(80, '='));

//            var groups = _results.GroupBy(r => new { r.TestName, r.Mode });

//            foreach (var group in groups)
//            {
//                var avgTime = group.Average(r => r.TimeMs);
//                var minTime = group.Min(r => r.TimeMs);
//                var maxTime = group.Max(r => r.TimeMs);

//                Console.WriteLine($"\n{group.Key.TestName} - {group.Key.Mode}:");
//                Console.WriteLine($"  Среднее время: {avgTime / 1000.0:F3} сек");
//                Console.WriteLine($"  Минимальное: {minTime / 1000.0:F3} сек");
//                Console.WriteLine($"  Максимальное: {maxTime / 1000.0:F3} сек");
//                Console.WriteLine($"  Количество прогонов: {group.Count()}");
//            }

//            // Сравнение однопоточного и многопоточного для каждого теста
//            Console.WriteLine("\nСРАВНЕНИЕ РЕЖИМОВ:");
//            Console.WriteLine("-".PadRight(80, '-'));

//            foreach (var testName in _results.Select(r => r.TestName).Distinct())
//            {
//                var singleThread = _results.Where(r => r.TestName == testName && r.Mode == "SingleThread").ToList();
//                var multiThread = _results.Where(r => r.TestName == testName && r.Mode == "MultiThread").ToList();

//                if (singleThread.Count > 0 && multiThread.Count > 0)
//                {
//                    var singleAvg = singleThread.Average(r => r.TimeMs);
//                    var multiAvg = multiThread.Average(r => r.TimeMs);
//                    var speedup = singleAvg / multiAvg;

//                    Console.WriteLine($"{testName}:");
//                    Console.WriteLine($"  Однопоточный: {singleAvg / 1000.0:F3} сек");
//                    Console.WriteLine($"  Многопоточный: {multiAvg / 1000.0:F3} сек");
//                    Console.WriteLine($"  Ускорение: {speedup:F2}x");
//                    Console.WriteLine($"  Разница: {(singleAvg - multiAvg) / 1000.0:F3} сек");
//                }
//            }
//        }
//    }
//}