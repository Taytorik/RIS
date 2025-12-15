using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace PerfomanceTester
{
    public class Tester
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public Tester(string baseUrl = "http://localhost:5000")
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _baseUrl = baseUrl;
        }

        public async Task RunBasicLoadTest(int concurrentUsers, int requestsPerUser)
        {
            Console.WriteLine($"\n=== НАГРУЗОЧНОЕ ТЕСТИРОВАНИЕ ===");
            Console.WriteLine($"Пользователей: {concurrentUsers}");
            Console.WriteLine($"Запросов на пользователя: {requestsPerUser}");
            Console.WriteLine($"Всего запросов: {concurrentUsers * requestsPerUser}");

            var tasks = new List<Task>();
            var results = new List<TestResult>();
            var stopwatch = Stopwatch.StartNew();

            // Запускаем несколько виртуальных пользователей
            for (int user = 0; user < concurrentUsers; user++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int i = 0; i < requestsPerUser; i++)
                    {
                        var result = await MakeRequest(user, i);
                        lock (results)
                        {
                            results.Add(result);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            PrintStatistics(results, stopwatch.Elapsed);
        }

        private async Task<TestResult> MakeRequest(int userId, int requestId)
        {
            var result = new TestResult
            {
                UserId = userId,
                RequestId = requestId,
                StartTime = DateTime.Now
            };

            try
            {
                // Тестовый текст разного размера
                string testText = GenerateTestText(requestId % 4); // 4 варианта размера
                var request = new
                {
                    text = testText,
                    ratio = 0.3f,
                    fileName = $"test_{userId}_{requestId}.txt"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var responseStopwatch = Stopwatch.StartNew();
                var response = await _httpClient.PostAsync($"{_baseUrl}/summarize", content);
                responseStopwatch.Stop();

                result.ResponseTime = responseStopwatch.ElapsedMilliseconds;
                result.IsSuccess = response.IsSuccessStatusCode;
                result.StatusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    result.ResponseLength = responseJson.Length;
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                result.ResponseTime = -1;
            }

            result.EndTime = DateTime.Now;
            return result;
        }

        private string GenerateTestText(int sizeLevel)
        {
            var baseText = "Распределенная система суммаризации текста позволяет эффективно обрабатывать большие объемы данных. ";

            return sizeLevel switch
            {
                0 => string.Join(" ", Enumerable.Repeat(baseText, 10)),  // ~500 символов
                1 => string.Join(" ", Enumerable.Repeat(baseText, 50)),  // ~2500 символов
                2 => string.Join(" ", Enumerable.Repeat(baseText, 200)), // ~10000 символов
                3 => string.Join(" ", Enumerable.Repeat(baseText, 500)), // ~25000 символов
                _ => string.Join(" ", Enumerable.Repeat(baseText, 100))
            };
        }

        private void PrintStatistics(List<TestResult> results, TimeSpan totalTime)
        {
            var successful = results.Where(r => r.IsSuccess).ToList();
            var failed = results.Where(r => !r.IsSuccess).ToList();

            Console.WriteLine($"\n=== РЕЗУЛЬТАТЫ ===");
            Console.WriteLine($"Общее время: {totalTime.TotalSeconds:F2} сек");
            Console.WriteLine($"Всего запросов: {results.Count}");
            Console.WriteLine($"Успешных: {successful.Count} ({successful.Count * 100.0 / results.Count:F1}%)");
            Console.WriteLine($"Неудачных: {failed.Count} ({failed.Count * 100.0 / results.Count:F1}%)");

            if (successful.Any())
            {
                var avgTime = successful.Average(r => r.ResponseTime);
                var minTime = successful.Min(r => r.ResponseTime);
                var maxTime = successful.Max(r => r.ResponseTime);
                var p95 = successful.Select(r => r.ResponseTime)
                    .OrderBy(t => t)
                    .ElementAt((int)(successful.Count * 0.95));

                Console.WriteLine($"\nВремя отклика (мс):");
                Console.WriteLine($"  Среднее: {avgTime:F1}");
                Console.WriteLine($"  Мин: {minTime}");
                Console.WriteLine($"  Макс: {maxTime}");
                Console.WriteLine($"  95-й перцентиль: {p95}");

                // Расчет RPS (запросов в секунду)
                var rps = successful.Count / totalTime.TotalSeconds;
                Console.WriteLine($"\nПропускная способность: {rps:F2} RPS");
            }

            if (failed.Any())
            {
                Console.WriteLine($"\nОшибки:");
                var errorGroups = failed.GroupBy(f => f.ErrorMessage ?? f.StatusCode.ToString())
                    .OrderByDescending(g => g.Count());

                foreach (var group in errorGroups.Take(5))
                {
                    Console.WriteLine($"  {group.Key}: {group.Count()} раз");
                }
            }
        }
    }

    public class TestResult
    {
        public int UserId { get; set; }
        public int RequestId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public long ResponseTime { get; set; } // в мс
        public bool IsSuccess { get; set; }
        public int StatusCode { get; set; }
        public int ResponseLength { get; set; }
        public string ErrorMessage { get; set; }
    }
}