using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PerformanceTester
{
    class Program
    {
        static readonly HttpClient client = new HttpClient();
        static readonly string baseUrl = "http://localhost:5000";
        static readonly Stopwatch totalStopwatch = new Stopwatch();

        static async Task Main(string[] args)
        {
            Console.WriteLine("Тестирование производительности системы суммаризации\n");

            Console.WriteLine("Перед началом убедитесь, что:");
            Console.WriteLine("1. MasterNode запущен на порту 5000");
            Console.WriteLine("2. SlaveNode(s) запущены и зарегистрированы");
            Console.WriteLine("3. Система готова к работе\n");
            Console.Write("Нажмите Enter чтобы начать...");
            Console.ReadLine();

            totalStopwatch.Start();

            try
            {
                // Размеры текстов для тестирования
                int[] textSizes = { 5000, 20000, 50000, 200000 };

                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("ТЕСТИРОВАНИЕ РАЗНЫХ РАЗМЕРОВ ТЕКСТА");
                Console.WriteLine(new string('=', 60));

                foreach (int size in textSizes)
                {
                    // Генерируем текст один раз для обоих режимов
                    string testText = GenerateText(size);

                    Console.WriteLine($"\nТекст {size} символов:");
                    Console.WriteLine(new string('-', 40));

                    // Тест в многопоточном режиме
                    Console.WriteLine("Многопоточный режим:");
                    await RunTest(size, false, testText, $"test_multi_{size}.txt");

                    // Тест в однопоточном режиме
                    Console.WriteLine("\nОднопоточный режим:");
                    await RunTest(size, true, testText, $"test_single_{size}.txt");

                    Console.WriteLine(new string('-', 40));

                    // Пауза между разными размерами
                    if (size < 200000)
                    {
                        Console.WriteLine("\nПауза перед следующим тестом...");
                        await Task.Delay(3000);
                    }
                }

                totalStopwatch.Stop();

                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine($"ВСЕ ТЕСТЫ ЗАВЕРШЕНЫ");
                Console.WriteLine($"Общее время тестирования: {totalStopwatch.Elapsed.TotalSeconds:F2} секунд");
                Console.WriteLine(new string('=', 60));

            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nОШИБКА: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        static async Task RunTest(int textLength, bool singleThread, string text, string fileName)
        {
            var url = singleThread ? $"{baseUrl}/summarize-test?mode=single" : $"{baseUrl}/summarize-test";

            var requestData = new
            {
                text,
                ratio = 0.3f,
                fileName
            };

            var json = JsonSerializer.Serialize(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var response = await client.PostAsync(url, content);
                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();

                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(result);
                        var root = doc.RootElement;

                        int originalLength = 0;
                        int summaryLength = 0;
                        float compressionRatio = 0;
                        string processingMode = singleThread ? "SINGLE_THREAD" : "MULTI_THREAD";

                        if (root.TryGetProperty("OriginalLength", out var original))
                            originalLength = original.GetInt32();
                        if (root.TryGetProperty("SummaryLength", out var summary))
                            summaryLength = summary.GetInt32();
                        if (root.TryGetProperty("CompressionRatio", out var ratio))
                            compressionRatio = ratio.GetSingle();

                        Console.WriteLine($"  Время: {stopwatch.Elapsed.TotalSeconds:F3} сек");
                        Console.WriteLine($"  Исходный: {originalLength} символов");
                        Console.WriteLine($"  Сводка: {summaryLength} символов");
                        Console.WriteLine($"  Сжатие: {compressionRatio:P2}");
                        Console.WriteLine($"  Скорость: {textLength / stopwatch.Elapsed.TotalSeconds:F0} сим/сек");
                    }
                    catch (JsonException)
                    {
                        Console.WriteLine($"  Время: {stopwatch.Elapsed.TotalSeconds:F3} сек");
                        Console.WriteLine($"  Ответ сервера: {result}");
                    }
                }
                else
                {
                    Console.WriteLine($"  ОШИБКА: {response.StatusCode}");
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"  {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ОШИБКА СОЕДИНЕНИЯ: {ex.Message}");
            }
        }

        static string GenerateText(int length)
        {
            var sentences = new[]
            {
                "Распределенная система обработки текста позволяет ускорить обработку больших объемов данных.",
                "Мастер-узел распределяет задачи между slave-узлами для параллельной обработки.",
                "Алгоритм TextRank выделяет наиболее важные предложения в тексте.",
                "Сжатие текста помогает получить краткое содержание документа.",
                "Тестирование производительности показывает эффективность системы.",
                "Каждый slave-узел обрабатывает свою часть текста независимо.",
                "Результаты от всех узлов собираются и объединяются в финальную сводку.",
                "Система автоматически определяет размер текста и выбирает стратегию обработки.",
                "Современные системы обработки естественного языка используют машинное обучение.",
                "Распределенные вычисления позволяют обрабатывать большие данные быстрее.",
                "Производительность системы зависит от количества доступных вычислительных узлов.",
                "Балансировка нагрузки между узлами обеспечивает оптимальное использование ресурсов.",
                "Алгоритмы извлечения ключевых фраз основываются на статистических методах.",
                "Качество суммаризации оценивается по сохранению смысла исходного текста.",
                "Большие текстовые документы требуют эффективных алгоритмов обработки.",
                "Параллельная обработка значительно сокращает время выполнения задач.",
                "Сетевые задержки могут влиять на общую производительность распределенной системы.",
                "Оптимизация передачи данных между узлами улучшает скорость обработки.",
                "Масштабируемость системы позволяет обрабатывать тексты любого размера.",
                "Надежность системы обеспечивается резервированием и перераспределением задач."
            };

            var rnd = new Random();
            var sb = new StringBuilder();

            // Для больших текстов используем более эффективную генерацию
            if (length > 50000)
            {
                // Создаем большие блоки текста для ускорения генерации
                while (sb.Length < length)
                {
                    int sentencesInBlock = rnd.Next(5, 15);
                    for (int i = 0; i < sentencesInBlock && sb.Length < length; i++)
                    {
                        var sentence = sentences[rnd.Next(sentences.Length)];
                        if (sb.Length + sentence.Length + 1 <= length)
                        {
                            sb.Append(sentence);
                            sb.Append(" ");
                        }
                        else
                        {
                            // Если не помещается весь sentence, берем часть
                            int remaining = length - sb.Length;
                            if (remaining > 20) // Минимальная длина для осмысленного текста
                            {
                                sb.Append(sentence.Substring(0, Math.Min(remaining - 1, sentence.Length)));
                                sb.Append(" ");
                            }
                            break;
                        }
                    }
                }
            }
            else
            {
                // Для малых и средних текстов обычная генерация
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
                        int remaining = length - sb.Length;
                        if (remaining > 20)
                        {
                            sb.Append(sentence.Substring(0, Math.Min(remaining - 1, sentence.Length)));
                            sb.Append(" ");
                        }
                        break;
                    }
                }
            }

            return sb.ToString().Substring(0, Math.Min(length, sb.Length));
        }
    }
}