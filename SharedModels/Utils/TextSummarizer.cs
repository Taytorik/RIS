using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SharedModels.Utils
{
    public class TextSummarizer
    {
        private readonly List<string> _stopWords = new List<string>
        {
            "и", "в", "во", "не", "что", "он", "на", "я", "с", "со", "как", "а", "то", "все", "она", "так",
            "его", "но", "да", "ты", "к", "у", "же", "вы", "за", "бы", "по", "только", "ее", "мне", "было",
            "вот", "от", "меня", "еще", "нет", "о", "из", "ему", "теперь", "когда", "даже", "ну", "вдруг",
            "ли", "если", "уже", "или", "ни", "быть", "был", "него", "до", "вас", "нибудь", "опять", "уж",
            "вам", "ведь", "там", "потом", "себя", "ничего", "ей", "может", "они", "тут", "где", "есть",
            "надо", "ней", "для", "мы", "тебя", "их", "чем", "была", "сам", "чтоб", "без", "будто", "чего",
            "раз", "тоже", "себе", "под", "будет", "ж", "тогда", "кто", "этот", "того", "потому", "этого",
            "какой", "совсем", "ним", "здесь", "этом", "один", "почти", "мой", "тем", "чтобы", "нее", "сейчас",
            "были", "куда", "зачем", "всех", "никогда", "можно", "при", "наконец", "два", "об", "другой", "хоть",
            "после", "над", "больше", "тот", "через", "эти", "нас", "про", "всего", "них", "какая", "много", "разве",
            "три", "эту", "моя", "впрочем", "хорошо", "свою", "этой", "перед", "иногда", "лучше", "чуть", "том",
            "нельзя", "такой", "им", "более", "всегда", "конечно", "всю", "между"
        };

        private readonly float _similarityThreshold = 0.1f; 
        private readonly float _dampingFactor = 0.85f; 
        private readonly int _maxIterations = 100; 
        private readonly float _tolerance = 0.0001f; 

        public string Summarize(string text, float compressionRatio = 0.3f)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Предобработка текста
            string normalizedText = PreprocessText(text);

            // Разделение текста на предложения
            List<string> sentences = SplitIntoSentences(normalizedText);

            if (sentences.Count <= 1)
                return text;

            // Преобразование предложений в наборы значимых слов
            List<List<string>> tokenizedSentences = TokenizeSentences(sentences);

            // Построение графа предложений
            float[,] similarityMatrix = BuildSentenceGraph(tokenizedSentences);

            // Вычисление весов предложений с использованием TextRank
            float[] sentenceWeights = CalculateSentenceWeights(similarityMatrix);

            // Выбор ключевых предложений
            List<string> keySentences = SelectKeySentences(sentences, sentenceWeights, compressionRatio);

            // Формирование итоговой суммаризации
            string summary = FormFinalSummary(keySentences);

            return summary;
        }

        /// <summary>
        /// Предобработка текста.
        /// Включает очистку от лишних пробелов, переносов строк и специальных символов.
        /// Текст нормализуется для последующей обработки.
        /// </summary>
        private string PreprocessText(string text)
        {
            text = Regex.Replace(text, @"\s+", " ");
            text = text.Replace("\n", " ").Replace("\r", " ");

            //text = text.ToLower();

            text = Regex.Replace(text, @"[^\p{L}\p{N}\s.,!?;:-]", " ");

            text = Regex.Replace(text, @"\s+", " ");

            return text.Trim();
        }

        /// <summary>
        /// Разделение текста на предложения.
        /// Происходит по точкам, восклицательным и вопросительным знакам.
        /// Каждое предложение обрабатывается отдельно.
        /// </summary>
        private List<string> SplitIntoSentences(string text)
        {
            string processedText = Regex.Replace(text, @"(?<=\s[А-ЯЁA-Z])\.", "_DOT_");
            processedText = Regex.Replace(processedText, @"т\.е\.", "т_е_");
            processedText = Regex.Replace(processedText, @"т\.д\.", "т_д_");
            processedText = Regex.Replace(processedText, @"т\.п\.", "т_п_");
            processedText = Regex.Replace(processedText, @"др\.", "др_");

            string[] sentenceArray = Regex.Split(processedText, @"(?<=[.!?])\s+");

            List<string> sentences = new List<string>();

            foreach (string sentence in sentenceArray)
            {
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    string restoredSentence = sentence
                        .Replace("_DOT_", ".")
                        .Replace("т_е_", "т.е.")
                        .Replace("т_д_", "т.д.")
                        .Replace("т_п_", "т.п.")
                        .Replace("др_", "др.")
                        .Trim();

                    sentences.Add(restoredSentence);
                }
            }

            return sentences;
        }

        /// <summary>
        /// Преобразование предложений в наборы значимых слов.
        /// Каждое предложение преобразуется в набор значимых слов,
        /// исключая стоп-слова и короткие слова.
        /// </summary>
        private List<List<string>> TokenizeSentences(List<string> sentences)
        {
            List<List<string>> tokenizedSentences = new List<List<string>>();

            foreach (string sentence in sentences)
            {
                string[] words = Regex.Split(sentence, @"\W+")
                    .Where(w => !string.IsNullOrWhiteSpace(w))
                    .Select(w => w.ToLower())
                    .ToArray();

                List<string> filteredWords = words
                    .Where(w => w.Length > 2 && !_stopWords.Contains(w))
                    .ToList();

                tokenizedSentences.Add(filteredWords);
            }

            return tokenizedSentences;
        }

        /// <summary>
        /// Вычисление TF-IDF весов для слов в предложениях.
        /// </summary>
        private Dictionary<string, float>[] CalculateTfIdfVectors(List<List<string>> tokenizedSentences)
        {
            int sentenceCount = tokenizedSentences.Count;
            Dictionary<string, float>[] tfIdfVectors = new Dictionary<string, float>[sentenceCount];

            Dictionary<string, int> documentFrequency = new Dictionary<string, int>();

            foreach (var sentence in tokenizedSentences)
            {
                HashSet<string> uniqueWords = new HashSet<string>(sentence);
                foreach (string word in uniqueWords)
                {
                    if (documentFrequency.ContainsKey(word))
                        documentFrequency[word]++;
                    else
                        documentFrequency[word] = 1;
                }
            }

            for (int i = 0; i < sentenceCount; i++)
            {
                Dictionary<string, float> tfIdfVector = new Dictionary<string, float>();
                var sentence = tokenizedSentences[i];

                Dictionary<string, int> termFrequency = new Dictionary<string, int>();
                foreach (string word in sentence)
                {
                    if (termFrequency.ContainsKey(word))
                        termFrequency[word]++;
                    else
                        termFrequency[word] = 1;
                }

                foreach (var kvp in termFrequency)
                {
                    string word = kvp.Key;
                    int tf = kvp.Value;
                    int df = documentFrequency[word];
                    float idf = (float)Math.Log((sentenceCount + 1.0) / (df + 1.0)) + 1.0f;
                    float tfidf = tf * idf;

                    tfIdfVector[word] = tfidf;
                }

                tfIdfVectors[i] = tfIdfVector;
            }

            return tfIdfVectors;
        }

        /// <summary>
        /// Построение графа предложений.
        /// Каждое отдельное предложение представляет как вершину графа.
        /// Ребра между вершинами устанавливаются на основе семантической близости предложений,
        /// вычисляемой через косинусное сходство их векторных представлений.
        /// </summary>
        private float[,] BuildSentenceGraph(List<List<string>> tokenizedSentences)
        {
            int sentenceCount = tokenizedSentences.Count;
            float[,] similarityMatrix = new float[sentenceCount, sentenceCount];

            Dictionary<string, float>[] tfIdfVectors = CalculateTfIdfVectors(tokenizedSentences);

            for (int i = 0; i < sentenceCount; i++)
            {
                similarityMatrix[i, i] = 1.0f; 

                for (int j = i + 1; j < sentenceCount; j++)
                {
                    float similarity = CalculateCosineSimilarity(tfIdfVectors[i], tfIdfVectors[j]);

                    if (similarity > _similarityThreshold)
                    {
                        similarityMatrix[i, j] = similarity;
                        similarityMatrix[j, i] = similarity;
                    }
                    else
                    {
                        similarityMatrix[i, j] = 0.0f;
                        similarityMatrix[j, i] = 0.0f;
                    }
                }
            }

            return similarityMatrix;
        }

        /// <summary>
        /// Вычисление косинусного сходства между двумя TF-IDF векторными представлениями.
        /// </summary>
        private float CalculateCosineSimilarity(Dictionary<string, float> vector1, Dictionary<string, float> vector2)
        {
            if (vector1.Count == 0 || vector2.Count == 0)
                return 0.0f;

            HashSet<string> allWords = new HashSet<string>(vector1.Keys);
            allWords.UnionWith(vector2.Keys);

            float dotProduct = 0;
            float norm1 = 0;
            float norm2 = 0;

            foreach (string word in allWords)
            {
                float weight1 = vector1.ContainsKey(word) ? vector1[word] : 0;
                float weight2 = vector2.ContainsKey(word) ? vector2[word] : 0;

                dotProduct += weight1 * weight2;
                norm1 += weight1 * weight1;
                norm2 += weight2 * weight2;
            }

            if (norm1 == 0 || norm2 == 0)
                return 0.0f;

            return (float)(dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2)));
        }

        /// <summary>
        /// Вычисление весов предложений с использованием алгоритма TextRank.
        /// Итерационный процесс вычисления весов продолжается до достижения сходимости
        /// или выполнения заданного количества итераций.
        /// </summary>
        private float[] CalculateSentenceWeights(float[,] similarityMatrix)
        {
            int n = similarityMatrix.GetLength(0);

            float[] scores = new float[n];
            for (int i = 0; i < n; i++)
            {
                scores[i] = 1.0f / n;
            }

            for (int iteration = 0; iteration < _maxIterations; iteration++)
            {
                float[] newScores = new float[n];
                float maxDifference = 0;

                for (int i = 0; i < n; i++)
                {
                    float sum = 0;

                    for (int j = 0; j < n; j++)
                    {
                        if (i != j && similarityMatrix[j, i] > 0)
                        {
                            float outSum = 0;
                            for (int k = 0; k < n; k++)
                            {
                                if (j != k)
                                {
                                    outSum += similarityMatrix[j, k];
                                }
                            }

                            if (outSum > 0)
                            {
                                sum += scores[j] * (similarityMatrix[j, i] / outSum);
                            }
                        }
                    }

                    newScores[i] = (1 - _dampingFactor) + _dampingFactor * sum;

                    float difference = Math.Abs(newScores[i] - scores[i]);
                    if (difference > maxDifference)
                        maxDifference = difference;
                }

                for (int i = 0; i < n; i++)
                {
                    scores[i] = newScores[i];
                }

                if (maxDifference < _tolerance)
                {
                    Console.WriteLine($"TextRank сошелся после {iteration + 1} итераций");
                    break;
                }
            }

            float sumWeights = scores.Sum();
            if (sumWeights > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    scores[i] /= sumWeights;
                }
            }

            return scores;
        }

        /// <summary>
        /// Выбор ключевых предложений.
        /// Предложения ранжируются по убыванию значимости,
        /// и выбирается заданное количество наиболее важных предложений
        /// в соответствии с коэффициентом сжатия.
        /// </summary>
        private List<string> SelectKeySentences(List<string> sentences, float[] weights, float compressionRatio)
        {
            if (sentences.Count == 0 || weights.Length == 0)
                return new List<string>();

            int targetCount = Math.Max(1, (int)(sentences.Count * compressionRatio));
            targetCount = Math.Min(targetCount, sentences.Count);

            List<SentenceScore> scoredSentences = new List<SentenceScore>();
            for (int i = 0; i < sentences.Count; i++)
            {
                scoredSentences.Add(new SentenceScore
                {
                    Index = i,
                    Text = sentences[i],
                    Score = weights[i]
                });
            }

            // Ранжирование по убыванию значимости и выбор топ-N
            List<SentenceScore> selectedSentences = scoredSentences
                .OrderByDescending(s => s.Score)
                .Take(targetCount)
                .OrderBy(s => s.Index) 
                .ToList();

            return selectedSentences.Select(s => s.Text).ToList();
        }

        /// <summary>
        /// Формирование итоговой суммаризации.
        /// Выполняется путем объединения выбранных предложений в том порядке,
        /// в котором они встречались в исходном тексте.
        /// </summary>
        private string FormFinalSummary(List<string> keySentences)
        {
            if (keySentences.Count == 0)
                return "Не удалось создать суммаризацию";

            StringBuilder summary = new StringBuilder();
            for (int i = 0; i < keySentences.Count; i++)
            {
                summary.Append(keySentences[i]);

                if (i < keySentences.Count - 1 &&
                    !keySentences[i].EndsWith(".") &&
                    !keySentences[i].EndsWith("!") &&
                    !keySentences[i].EndsWith("?"))
                {
                    summary.Append(". ");
                }
                else if (i < keySentences.Count - 1)
                {
                    summary.Append(" ");
                }
            }

            return summary.ToString().Trim();
        }

        /// <summary>
        /// Дополнительный метод для получения ключевых предложений с их весами
        /// </summary>
        public List<(string Sentence, float Weight)> GetKeySentencesWithWeights(string text, int count = 5)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<(string, float)>();

            string normalizedText = PreprocessText(text);
            List<string> sentences = SplitIntoSentences(normalizedText);

            if (sentences.Count <= 1)
                return sentences.Select(s => (s, 1.0f)).ToList();

            List<List<string>> tokenizedSentences = TokenizeSentences(sentences);
            float[,] similarityMatrix = BuildSentenceGraph(tokenizedSentences);
            float[] sentenceWeights = CalculateSentenceWeights(similarityMatrix);

            List<SentenceScore> scoredSentences = new List<SentenceScore>();
            for (int i = 0; i < sentences.Count; i++)
            {
                scoredSentences.Add(new SentenceScore
                {
                    Index = i,
                    Text = sentences[i],
                    Score = sentenceWeights[i]
                });
            }

            return scoredSentences
                .OrderByDescending(s => s.Score)
                .Take(count)
                .Select(s => (s.Text, s.Score))
                .ToList();
        }

        /// <summary>
        /// Вспомогательный класс для хранения предложения с его оценкой
        /// </summary>
        private class SentenceScore
        {
            public int Index { get; set; }
            public string Text { get; set; }
            public float Score { get; set; }
        }
    }
}