using System.Text.RegularExpressions;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

namespace TBHStats.Capture.Tabs;

/// <summary>
/// Реализация нечёткого сопоставления распознанного OCR-текста с названиями вкладок
/// через расстояние Левенштейна (алгоритм Вагнера–Фишера).
/// </summary>
public sealed class TabNameMatcher : ITabNameMatcher
{
    // Регулярное выражение для схлопывания подряд идущих пробельных символов в один пробел.
    private static readonly Regex WhitespaceCollapser = new(@"\s+", RegexOptions.Compiled);

    /// <inheritdoc/>
    public TabRef? Match(string recognizedText, GameMechanicsConfig cfg, double minSimilarity = 0.6)
    {
        // Шаг 5: пустой / whitespace → немедленно null.
        if (string.IsNullOrWhiteSpace(recognizedText))
            return null;

        string normalizedInput = Normalize(recognizedText);

        // После нормализации может стать пустым (например, строка из одних пробелов,
        // уже покрыто IsNullOrWhiteSpace выше, но для защиты оставляем).
        if (normalizedInput.Length == 0)
            return null;

        Tab? bestTab = null;
        double bestSimilarity = -1.0;

        // Шаг 3: перебрать все активные вкладки.
        foreach (Tab tab in cfg.Tabs)
        {
            if (!tab.IsActive)
                continue;

            string normalizedRef = Normalize(tab.RecognitionText);
            double similarity = ComputeSimilarity(normalizedInput, normalizedRef);

            // При равной похожести берём первую по порядку (детерминированность).
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestTab = tab;
            }
        }

        // Шаг 4: проверка порога.
        if (bestTab is null || bestSimilarity < minSimilarity)
            return null;

        return new TabRef(bestTab.Id, bestTab.Key, bestSimilarity);
    }

    /// <summary>
    /// Нормализует строку: Trim + ToLowerInvariant + схлопывание подряд идущих пробелов в один.
    /// </summary>
    private static string Normalize(string text)
        => WhitespaceCollapser.Replace(text.Trim().ToLowerInvariant(), " ");

    /// <summary>
    /// Вычисляет нормализованную похожесть двух строк на основе расстояния Левенштейна.
    /// Похожесть = 1.0 − distance / Max(len1, len2).
    /// Обе пустые → 0.0.
    /// </summary>
    private static double ComputeSimilarity(string a, string b)
    {
        int len1 = a.Length;
        int len2 = b.Length;

        if (len1 == 0 && len2 == 0)
            return 0.0;

        int distance = LevenshteinDistance(a, b);
        int maxLen = Math.Max(len1, len2);
        return 1.0 - (double)distance / maxLen;
    }

    /// <summary>
    /// Классический алгоритм Вагнера–Фишера: итеративный, O(n·m) по времени, O(min(n,m)) по памяти.
    /// </summary>
    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;

        if (n == 0) return m;
        if (m == 0) return n;

        // Оптимизация: держим только два ряда матрицы.
        int[] prev = new int[m + 1];
        int[] curr = new int[m + 1];

        // Инициализация первой строки.
        for (int j = 0; j <= m; j++)
            prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(
                        curr[j - 1] + 1,    // вставка
                        prev[j] + 1),       // удаление
                    prev[j - 1] + cost);    // замена
            }
            // Меняем местами prev и curr для следующей итерации.
            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }
}
