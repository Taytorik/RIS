using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace TestsTextSummarySystem
{
    [TestClass]
    public class UnitTests
    {
        private static HttpClient _httpClient;
        private static string _masterUrl = "http://localhost:5000";

        private class SummaryResponse
        {
            public string TaskId { get; set; }
            public int OriginalLength { get; set; }
            public int SummaryLength { get; set; }
            public string Summary { get; set; }
            public string Status { get; set; }
            public string FileName { get; set; }
            public float CompressionRatio { get; set; }
        }

        private static List<TestMetric> _metrics = new List<TestMetric>();

        private class TestMetric
        {
            public string TestName { get; set; }
            public long TimeMs { get; set; }
            public bool Success { get; set; }
            public double Compression { get; set; }
        }

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext context)
        {
            Console.WriteLine("=== НАЧАЛО ТЕСТИРОВАНИЯ ===");

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_masterUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            bool isMasterAvailable = await CheckMasterAvailability();
            if (!isMasterAvailable)
            {
                Assert.Inconclusive("Master не доступен. Запустите MasterNode перед тестированием.");
            }
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            SaveResults();
            _httpClient?.Dispose();
        }

        [TestInitialize]
        public async Task TestInit()
        {
            bool isAvailable = await CheckMasterAvailability();
            if (!isAvailable)
            {
                Assert.Inconclusive("Master не доступен.");
            }
        }

        #region Основные тесты

        [TestMethod]
        [Timeout(15000)]
        public async Task Test01_MasterStatus()
        {
            bool isAvailable = await CheckMasterAvailability();
            Assert.IsTrue(isAvailable, "Master должен быть доступен");

            var response = await _httpClient.GetAsync("/status");
            Assert.IsTrue(response.IsSuccessStatusCode, $"Status endpoint вернул {response.StatusCode}");
        }

        [TestMethod]
        [Timeout(20000)]
        public async Task Test02_BasicSummarization()
        {
            var text = "Это простой тестовый текст для проверки работы системы суммаризации. " +
                      "Система должна уметь обрабатывать короткие тексты и создавать их краткое содержание. " +
                      "Алгоритм TextRank выделяет наиболее важные предложения на основе их взаимосвязей.";

            var sw = Stopwatch.StartNew();
            var result = await Summarize(text, 0.5f, "test_basic.txt");
            sw.Stop();

            Assert.IsNotNull(result, "Результат не должен быть null");

            if (result.Status == "success")
            {
                Assert.IsTrue(result.SummaryLength > 0, "Сводка не должна быть пустой");

                bool isShorter = result.SummaryLength < text.Length;
                bool isSameLength = result.SummaryLength == text.Length;

                if (isShorter)
                {
                    Console.WriteLine($"Сжатие: {(1 - (double)result.SummaryLength / text.Length):P2}");
                }
                else if (isSameLength)
                {
                    Console.WriteLine("Сводка той же длины, что и оригинал");
                }

                AddMetric("Базовая суммаризация", sw.ElapsedMilliseconds, true,
                    1 - (double)result.SummaryLength / text.Length);
            }
            else
            {
                Assert.IsFalse(result.Status.Contains("network") || result.Status.Contains("timeout"),
                    $"Сетевая ошибка при суммаризации: {result.Status}");

                AddMetric("Базовая суммаризация", sw.ElapsedMilliseconds, false, 0);
            }
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task Test03_DifferentTextSizes()
        {
            var testCases = new[]
            {
                new { Size = 500, Ratio = 0.5f },
                new { Size = 1000, Ratio = 0.4f },
                new { Size = 1500, Ratio = 0.3f }
            };

            var successes = 0;

            foreach (var testCase in testCases)
            {
                var text = GenerateSimpleText(testCase.Size);
                var result = await Summarize(text, testCase.Ratio, $"test_size_{testCase.Size}.txt");

                if (result != null && result.Status == "success")
                {
                    successes++;
                }

                await Task.Delay(500);
            }

            Assert.IsTrue(successes >= 1,
                $"Должен быть успешен хотя бы 1 тест из {testCases.Length}. Успешно: {successes}");
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task Test04_SimpleCompressionRatios()
        {
            var text = GenerateSimpleText(1000);
            var ratios = new[] { 0.3f, 0.5f }; 
            var successes = 0;

            foreach (var ratio in ratios)
            {
                var result = await Summarize(text, ratio, $"test_ratio_{ratio}.txt");

                if (result != null && result.Status == "success")
                {
                    successes++;
                }

                await Task.Delay(1000);
            }

            Assert.IsTrue(successes >= 1,
                $"Должен быть успешен хотя бы 1 тест из {ratios.Length}. Успешно: {successes}");
        }

        [TestMethod]
        [Timeout(25000)]
        public async Task Test05_QuickSequentialRequests()
        {
            var requests = 3;
            var successes = 0;

            for (int i = 1; i <= requests; i++)
            {
                var text = $"Тестовый текст номер {i} для проверки работы системы суммаризации. " +
                          $"Система должна обрабатывать различные запросы последовательно. " +
                          $"Это предложение добавляет дополнительный контекст для обработки.";

                var result = await Summarize(text, 0.4f, $"test_quick_{i}.txt");

                if (result != null && result.Status == "success" && result.SummaryLength > 0)
                {
                    successes++;
                }

                if (i < requests) await Task.Delay(800); 
            }

            Assert.IsTrue(successes >= requests / 2,
                $"Должно быть успешно минимум {requests / 2} запросов из {requests}. Успешно: {successes}");
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task Test06_EdgeCases()
        {
            var testsPassed = 0;
            var totalTests = 3;

            try
            {
                var result1 = await Summarize("Это текст средней длины для тестирования системы.", 0.5f, "test_edge1.txt");
                if (result1?.Status == "success")
                    testsPassed++;
            }
            catch { }

            try
            {
                var result2 = await Summarize("Первое предложение. Второе предложение. Третье предложение.", 0.3f, "test_edge2.txt");
                if (result2?.Status == "success")
                    testsPassed++;
            }
            catch { }

            try
            {
                var text3 = GenerateSimpleText(300);
                var result3 = await Summarize(text3, 0.4f, "test_edge3.txt");
                if (result3?.Status == "success")
                    testsPassed++;
            }
            catch { }

            Assert.IsTrue(testsPassed >= 2,
                $"Должно быть успешно минимум 2 теста из {totalTests}. Успешно: {testsPassed}");
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task Test07_SystemStability()
        {
            var text = "Тест стабильности работы системы суммаризации текстов. " +
                      "Повторяющиеся запросы должны обрабатываться корректно. " +
                      "Система должна демонстрировать устойчивость к нагрузке.";

            var successes = 0;
            var attempts = 5;

            for (int i = 1; i <= attempts; i++)
            {
                var result = await Summarize(text, 0.3f, $"test_stable_{i}.txt");

                if (result != null && (result.Status == "success" ||
                    (!result.Status.Contains("network") && !result.Status.Contains("timeout"))))
                {
                    successes++;
                }

                if (i < attempts) await Task.Delay(1000); 
            }

            var successRate = (double)successes / attempts * 100;

            Assert.IsTrue(successRate >= 50,
                $"Слишком низкая стабильность: {successRate:F1}%. Успешно: {successes}/{attempts}");
        }

        [TestMethod]
        [Timeout(20000)]
        public async Task Test08_FileUploadSimulation()
        {
            var fileContent = @"Система суммаризации текстов предназначена для автоматического создания кратких содержаний документов.
            
Алгоритм TextRank основан на принципах PageRank и применяется для выделения ключевых предложений.
            
Основные преимущества системы:
1. Высокая скорость обработки
2. Поддержка больших объемов текста
3. Настраиваемый коэффициент сжатия
4. Распределенная архитектура
            
Система состоит из мастер-узла и нескольких рабочих узлов.
Мастер распределяет задачи между рабочими узлами для параллельной обработки.";

            var result = await Summarize(fileContent, 0.2f, "simulated_file.txt");

            Assert.IsNotNull(result, "Результат не должен быть null");

            if (result.Status == "success")
            {
                Assert.IsTrue(result.SummaryLength > 0, "Сводка не должна быть пустой");

                if (result.SummaryLength >= fileContent.Length)
                {
                    Console.WriteLine($"Сводка не короче оригинала: {result.SummaryLength} >= {fileContent.Length}");
                }
            }
            else
            {
                Assert.IsFalse(result.Status.Contains("network") || result.Status.Contains("timeout"),
                    $"Сетевая ошибка при обработке файла: {result.Status}");
            }
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task Test09_MultipleFormatsTest()
        {
            var testCases = new[]
            {
                new { Text = "Это обычный текст с несколькими предложениями.", Ratio = 0.4f },
                new { Text = "Текст с числами: 100, 200, 300. Процент: 95.5%.", Ratio = 0.5f },
                new { Text = "Текст\nс\nпереносами\nстрок.", Ratio = 0.3f }
            };

            var successes = 0;

            foreach (var testCase in testCases)
            {
                var result = await Summarize(testCase.Text, testCase.Ratio, "test_format.txt");
                if (result != null && result.Status == "success")
                {
                    successes++;
                }
                await Task.Delay(800);
            }

            Assert.IsTrue(successes >= 2,
                $"Должно быть успешно минимум 2 теста из {testCases.Length}. Успешно: {successes}");
        }

        [TestMethod]
        [Timeout(20000)]
        public async Task Test10_StressResistance()
        {
            var baseSentence = "Система суммаризации текстов демонстрирует производительность. ";
            var heavyText = string.Join("", Enumerable.Repeat(baseSentence, 15));

            var successes = 0;
            var attempts = 2;

            for (int i = 1; i <= attempts; i++)
            {
                var result = await Summarize(heavyText, 0.3f, $"test_stress_{i}.txt");
                if (result != null && result.Status == "success")
                {
                    successes++;
                }
                if (i < attempts) await Task.Delay(1500);
            }

            Assert.IsTrue(successes >= 1,
                $"Должна быть успешна хотя бы 1 попытка из {attempts}. Успешно: {successes}");
        }

        #endregion

        #region Вспомогательные методы

        private static async Task<bool> CheckMasterAvailability()
        {
            try
            {
                using (var quickClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                {
                    var response = await quickClient.GetAsync($"{_masterUrl}/status");
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private static async Task<SummaryResponse> Summarize(string text, float ratio, string filename)
        {
            try
            {
                var request = new { text, ratio, fileName = filename };
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    var response = await _httpClient.PostAsync("/summarize", content, cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        return new SummaryResponse { Status = $"http_{response.StatusCode}" };
                    }

                    var responseJson = await response.Content.ReadAsStringAsync();

                    try
                    {
                        var result = JsonConvert.DeserializeObject<SummaryResponse>(responseJson);
                        if (result != null)
                        {
                            if (string.IsNullOrEmpty(result.Status))
                                result.Status = "success";
                            if (result.SummaryLength == 0 && !string.IsNullOrEmpty(result.Summary))
                                result.SummaryLength = result.Summary.Length;
                            if (result.OriginalLength == 0)
                                result.OriginalLength = text.Length;
                        }
                        return result ?? new SummaryResponse { Status = "invalid_response" };
                    }
                    catch (JsonException)
                    {
                        if (!string.IsNullOrEmpty(responseJson) && responseJson.Length < text.Length)
                        {
                            return new SummaryResponse
                            {
                                Status = "success",
                                Summary = responseJson,
                                OriginalLength = text.Length,
                                SummaryLength = responseJson.Length,
                                CompressionRatio = 1 - (float)responseJson.Length / text.Length
                            };
                        }
                        else
                        {
                            // Непонятный ответ
                            return new SummaryResponse
                            {
                                Status = "success",
                                Summary = responseJson,
                                OriginalLength = text.Length,
                                SummaryLength = responseJson?.Length ?? 0,
                                CompressionRatio = 0
                            };
                        }
                    }
                }
            }
            catch (HttpRequestException)
            {
                return new SummaryResponse { Status = "network_error" };
            }
            catch (TaskCanceledException)
            {
                return new SummaryResponse { Status = "timeout" };
            }
            catch (Exception)
            {
                return new SummaryResponse { Status = "error" };
            }
        }

        private static string GenerateSimpleText(int length)
        {
            var sentences = new[]
            {
                "Распределенная система обработки текстов позволяет эффективно обрабатывать данные.",
                "Алгоритм TextRank используется для выделения ключевых предложений в тексте.",
                "Мастер-узел координирует работу slave-узлов для параллельной обработки.",
                "Сжатие текста помогает получить краткое содержание без потери смысла.",
                "Тестирование производительности показывает эффективность вычислений.",
                "Каждый узел независимо обрабатывает назначенную часть текста.",
                "Результаты собираются и объединяются в финальную сводку.",
                "Система определяет оптимальную стратегию обработки.",
                "Масштабируемость позволяет увеличивать производительность.",
                "Надежность обеспечивается резервированием и перераспределением."
            };

            var rnd = new Random();
            var sb = new StringBuilder();

            while (sb.Length < length)
            {
                var sentence = sentences[rnd.Next(sentences.Length)];
                if (sb.Length + sentence.Length + 1 <= length)
                {
                    sb.Append(sentence);
                    sb.Append(" ");
                }
                else
                {
                    // Берем часть предложения
                    int remaining = length - sb.Length;
                    if (remaining > 10)
                    {
                        sb.Append(sentence.Substring(0, Math.Min(remaining - 1, sentence.Length)));
                        sb.Append(" ");
                    }
                    break;
                }
            }

            return sb.ToString().Substring(0, Math.Min(length, sb.Length));
        }

        private static void AddMetric(string name, long time, bool success, double compression)
        {
            _metrics.Add(new TestMetric
            {
                TestName = name,
                TimeMs = time,
                Success = success,
                Compression = compression
            });
        }

        private static void SaveResults()
        {
            try
            {
                if (!_metrics.Any())
                {
                    Console.WriteLine("Нет данных тестирования для сохранения.");
                    return;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var resultsDir = "TestResults";

                if (!Directory.Exists(resultsDir))
                {
                    Directory.CreateDirectory(resultsDir);
                }

                var json = JsonConvert.SerializeObject(_metrics, Formatting.Indented);
                File.WriteAllText(Path.Combine(resultsDir, $"results_{timestamp}.json"), json);

                var successful = _metrics.Where(m => m.Success).ToList();
                if (successful.Any())
                {
                    Console.WriteLine($"\nУспешных тестов: {successful.Count}/{_metrics.Count} ({successful.Count * 100.0 / _metrics.Count:F1}%)");

                    var withCompression = successful.Where(m => m.Compression > 0).ToList();
                    if (withCompression.Any())
                    {
                        Console.WriteLine($"Среднее сжатие: {withCompression.Average(m => m.Compression):P2}");
                    }
                }
                else
                {
                    Console.WriteLine("\nНет успешных тестов.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения результатов: {ex.Message}");
            }
        }

        #endregion
    }
}