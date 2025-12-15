using System.Diagnostics;
using System.Text;
using PerfomanceTester;

public class AdvancedLoadTester
{
    private readonly HttpClient _httpClient;
    private readonly string _masterUrl;

    public AdvancedLoadTester(string masterUrl = "http://localhost:5000")
    {
        _httpClient = new HttpClient();
        _masterUrl = masterUrl;
    }

    public async Task RunRampUpTest(int maxUsers, TimeSpan duration, TimeSpan rampUpTime)
    {
        Console.WriteLine($"\n=== ТЕСТ ПОСТЕПЕННОЙ НАГРУЗКИ ===");
        Console.WriteLine($"Максимум пользователей: {maxUsers}");
        Console.WriteLine($"Длительность: {duration.TotalSeconds} сек");
        Console.WriteLine($"Время нарастания: {rampUpTime.TotalSeconds} сек");

        var results = new List<TestResult>();
        var stopwatch = Stopwatch.StartNew();

        // Постепенное увеличение нагрузки
        int usersPerStep = maxUsers / 10;
        var stepDuration = TimeSpan.FromSeconds(rampUpTime.TotalSeconds / 10);

        for (int step = 0; step < 10; step++)
        {
            int currentUsers = usersPerStep * (step + 1);
            Console.WriteLine($"\nШаг {step + 1}: {currentUsers} пользователей");

            var tasks = new List<Task>();
            var stepResults = new List<TestResult>();

            for (int user = 0; user < currentUsers; user++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var result = await MakeLoadRequest(user);
                    lock (stepResults)
                    {
                        stepResults.Add(result);
                    }
                }));
            }

            await Task.WhenAll(tasks);
            results.AddRange(stepResults);

            PrintStepStatistics(stepResults, step + 1);
            await Task.Delay(stepDuration);
        }

        // Постоянная нагрузка
        var steadyStart = DateTime.Now;
        while (DateTime.Now - steadyStart < duration - rampUpTime)
        {
            var tasks = new List<Task>();
            var batchResults = new List<TestResult>();

            for (int i = 0; i < maxUsers; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var result = await MakeLoadRequest(i);
                    lock (batchResults)
                    {
                        batchResults.Add(result);
                    }
                }));
            }

            await Task.WhenAll(tasks);
            results.AddRange(batchResults);

            Console.WriteLine($"Постоянная нагрузка: обработано {batchResults.Count} запросов");
            await Task.Delay(1000); // Пауза между батчами
        }

        stopwatch.Stop();
        Console.WriteLine($"\n=== ИТОГИ ТЕСТА ===");
        PrintFinalStatistics(results, stopwatch.Elapsed);
    }

    public async Task RunStressTest(int initialUsers, int maxUsers, TimeSpan duration)
    {
        Console.WriteLine($"\n=== СТРЕСС-ТЕСТ ===");
        Console.WriteLine($"Начало: {initialUsers} пользователей");
        Console.WriteLine($"Максимум: {maxUsers} пользователей");
        Console.WriteLine($"Длительность: {duration.TotalSeconds} сек");

        var results = new List<TestResult>();
        var stopwatch = Stopwatch.StartNew();
        var endTime = DateTime.Now + duration;

        int currentUsers = initialUsers;
        int increaseStep = (maxUsers - initialUsers) / 5;

        while (DateTime.Now < endTime)
        {
            Console.WriteLine($"\nТекущая нагрузка: {currentUsers} пользователей");

            var tasks = new List<Task>();
            var batchResults = new List<TestResult>();
            var batchStopwatch = Stopwatch.StartNew();

            // Создаем нагрузку
            for (int i = 0; i < currentUsers; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var result = await MakeStressRequest(i);
                    lock (batchResults)
                    {
                        batchResults.Add(result);
                    }
                }));
            }

            await Task.WhenAll(tasks);
            batchStopwatch.Stop();

            results.AddRange(batchResults);

            // Анализ результатов батча
            var successRate = batchResults.Count(r => r.IsSuccess) * 100.0 / batchResults.Count;
            var avgTime = batchResults.Where(r => r.IsSuccess).Average(r => r.ResponseTime);

            Console.WriteLine($"  Успешно: {successRate:F1}%");
            Console.WriteLine($"  Среднее время: {avgTime:F0} мс");
            Console.WriteLine($"  Время батча: {batchStopwatch.Elapsed.TotalSeconds:F1} сек");

            // Увеличиваем нагрузку, если система справляется
            if (successRate > 95 && avgTime < 5000 && currentUsers < maxUsers)
            {
                currentUsers += increaseStep;
                Console.WriteLine($"  ↑ Увеличиваем нагрузку до {currentUsers}");
            }
            // Уменьшаем нагрузку при проблемах
            else if (successRate < 80 || avgTime > 10000)
            {
                currentUsers = Math.Max(initialUsers, currentUsers - increaseStep);
                Console.WriteLine($"  ↓ Снижаем нагрузку до {currentUsers}");
            }

            await Task.Delay(2000);
        }

        stopwatch.Stop();
        PrintFinalStatistics(results, stopwatch.Elapsed);
    }

    private async Task<TestResult> MakeLoadRequest(int userId)
    {
        // Упрощенный запрос для нагрузочного тестирования
        var text = GenerateRandomText(1000 + userId % 5000);
        var request = new
        {
            text = text,
            ratio = 0.3f,
            fileName = $"loadtest_{userId}_{DateTime.Now.Ticks}.txt"
        };

        return await SendRequest(request);
    }

    private async Task<TestResult> MakeStressRequest(int userId)
    {
        // Для стресс-теста используем тексты большего размера
        var text = GenerateRandomText(5000 + userId % 20000);
        var request = new
        {
            text = text,
            ratio = 0.2f, // Более агрессивное сжатие
            fileName = $"stresstest_{userId}_{DateTime.Now.Ticks}.txt"
        };

        return await SendRequest(request);
    }

    private async Task<TestResult> SendRequest(object request)
    {
        var result = new TestResult { StartTime = DateTime.Now };

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_masterUrl}/summarize", content);

            result.StatusCode = (int)response.StatusCode;
            result.IsSuccess = response.IsSuccessStatusCode;

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                result.ResponseLength = responseText.Length;
            }
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        result.EndTime = DateTime.Now;
        result.ResponseTime = (long)(result.EndTime - result.StartTime).TotalMilliseconds;

        return result;
    }

    private string GenerateRandomText(int length)
    {
        var words = new[]
        {
            "система", "обработка", "текст", "суммаризация", "распределенная",
            "алгоритм", "производительность", "тестирование", "нагрузка",
            "масштабируемость", "отказоустойчивость", "клиент", "сервер",
            "протокол", "данные", "информация", "сжатие", "качество",
            "эффективность", "параллелизм", "поток", "узел", "сеть"
        };

        var random = new Random();
        var sb = new StringBuilder();

        while (sb.Length < length)
        {
            sb.Append(words[random.Next(words.Length)]);
            sb.Append(" ");

            if (random.Next(10) == 0)
            {
                sb.Append(". ");
            }
        }

        return sb.ToString().Substring(0, Math.Min(length, sb.Length));
    }

    private void PrintStepStatistics(List<TestResult> results, int step)
    {
        var successful = results.Where(r => r.IsSuccess).ToList();

        if (successful.Any())
        {
            var avgTime = successful.Average(r => r.ResponseTime);
            var successRate = successful.Count * 100.0 / results.Count;

            Console.WriteLine($"  Успешность: {successRate:F1}%");
            Console.WriteLine($"  Среднее время: {avgTime:F0} мс");
        }
    }

    private void PrintFinalStatistics(List<TestResult> results, TimeSpan totalTime)
    {
        var successful = results.Where(r => r.IsSuccess).ToList();

        Console.WriteLine($"\n=== ФИНАЛЬНАЯ СТАТИСТИКА ===");
        Console.WriteLine($"Всего запросов: {results.Count}");
        Console.WriteLine($"Успешных: {successful.Count} ({successful.Count * 100.0 / results.Count:F1}%)");
        Console.WriteLine($"Общее время: {totalTime.TotalSeconds:F1} сек");
        Console.WriteLine($"Среднее RPS: {results.Count / totalTime.TotalSeconds:F2}");

        if (successful.Any())
        {
            var responseTimes = successful.Select(r => r.ResponseTime).ToList();
            Console.WriteLine($"\nРаспределение времени отклика (мс):");
            Console.WriteLine($"  50-й перцентиль: {Percentile(responseTimes, 0.5):F0}");
            Console.WriteLine($"  90-й перцентиль: {Percentile(responseTimes, 0.9):F0}");
            Console.WriteLine($"  95-й перцентиль: {Percentile(responseTimes, 0.95):F0}");
            Console.WriteLine($"  99-й перцентиль: {Percentile(responseTimes, 0.99):F0}");
        }
    }

    private double Percentile(List<long> data, double percentile)
    {
        if (!data.Any()) return 0;

        var sorted = data.OrderBy(x => x).ToList();
        int index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return index >= 0 ? sorted[index] : sorted.First();
    }
}