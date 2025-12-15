using System;
using System.Text;
using System.Threading.Tasks;
using PerfomanceTester;

namespace LoadTester
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== НАГРУЗОЧНОЕ ТЕСТИРОВАНИЕ СИСТЕМЫ СУММАРИЗАЦИИ ===\n");

            var tester = new Tester();
            var advancedTester = new AdvancedLoadTester();

            try
            {
                // Тест 1: Базовая нагрузка
                Console.WriteLine("ТЕСТ 1: Базовая нагрузка (10 пользователей, 5 запросов)");
                await tester.RunBasicLoadTest(10, 5);

                // Тест 2: Постепенная нагрузка
                Console.WriteLine("\n\nТЕСТ 2: Постепенное увеличение нагрузки");
                await advancedTester.RunRampUpTest(
                    maxUsers: 20,
                    duration: TimeSpan.FromSeconds(60),
                    rampUpTime: TimeSpan.FromSeconds(30)
                );

                // Тест 3: Стресс-тест
                Console.WriteLine("\n\nТЕСТ 3: Стресс-тест");
                await advancedTester.RunStressTest(
                    initialUsers: 5,
                    maxUsers: 50,
                    duration: TimeSpan.FromSeconds(120)
                );

                // Тест 4: Длительная стабильность
                Console.WriteLine("\n\nТЕСТ 4: Длительная работа (5 минут)");
                await RunLongTermTest(tester);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }

            Console.WriteLine("\n=== ТЕСТИРОВАНИЕ ЗАВЕРШЕНО ===");
        }

        static async Task RunLongTermTest(Tester tester)
        {
            var startTime = DateTime.Now;
            var endTime = startTime.AddMinutes(5);
            var results = new System.Collections.Concurrent.ConcurrentBag<TestResult>();

            Console.WriteLine($"Тест начнется в {startTime:HH:mm:ss}, закончится в {endTime:HH:mm:ss}");

            // Запускаем непрерывную нагрузку
            var loadTask = Task.Run(async () =>
            {
                while (DateTime.Now < endTime)
                {
                    await Task.Delay(500); // Задержка между запросами

                    try
                    {
                        var testText = "Тест длительной стабильности системы. ";
                        testText += string.Concat(Enumerable.Repeat(testText, 50));

                        var request = new
                        {
                            text = testText,
                            ratio = 0.3f,
                            fileName = $"longtest_{DateTime.Now.Ticks}.txt"
                        };

                        var json = System.Text.Json.JsonSerializer.Serialize(request);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var client = new HttpClient();
                        var response = await client.PostAsync("http://localhost:5000/summarize", content);

                        var result = new TestResult
                        {
                            IsSuccess = response.IsSuccessStatusCode,
                            StatusCode = (int)response.StatusCode,
                            ResponseTime = 0 // Упрощенно
                        };

                        results.Add(result);

                        if (results.Count % 10 == 0)
                        {
                            Console.WriteLine($"  Выполнено {results.Count} запросов...");
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибки для долгого теста
                    }
                }
            });

            // Мониторинг состояния системы
            var monitorTask = Task.Run(async () =>
            {
                var client = new HttpClient();

                while (DateTime.Now < endTime)
                {
                    await Task.Delay(10000); // Проверка каждые 10 секунд

                    try
                    {
                        var response = await client.GetAsync("http://localhost:5000/status");
                        if (response.IsSuccessStatusCode)
                        {
                            var status = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"  Статус системы: OK ({DateTime.Now:HH:mm:ss})");
                        }
                    }
                    catch
                    {
                        Console.WriteLine($"  Статус системы: ОШИБКА ({DateTime.Now:HH:mm:ss})");
                    }
                }
            });

            await Task.WhenAll(loadTask, monitorTask);

            var successful = results.Count(r => r.IsSuccess);
            Console.WriteLine($"\nИтоги длительного теста:");
            Console.WriteLine($"Всего запросов: {results.Count}");
            Console.WriteLine($"Успешных: {successful} ({successful * 100.0 / results.Count:F1}%)");
        }
    }
}