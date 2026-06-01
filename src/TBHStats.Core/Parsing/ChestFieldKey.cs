namespace TBHStats.Core.Parsing;

using System.Globalization;

/// <summary>
/// Утилита разбора FieldKey-ключей сундуков.
/// </summary>
/// <remarks>
/// <para>
/// Поддерживает два формата:
/// <list type="bullet">
///   <item><term>Базовый</term><description>«chest:brown» — итоговый семантический ключ (без раскладки).</description></item>
///   <item><term>Калиброванный @N</term><description>«chest:brown@2» — ключ ROI для раскладки из N одновременных типов.</description></item>
/// </list>
/// </para>
/// <para>
/// Все операции используют <see cref="StringComparison.Ordinal"/> и
/// <see cref="CultureInfo.InvariantCulture"/> — без зависимости от локали.
/// </para>
/// </remarks>
public static class ChestFieldKey
{
    private const string ChestPrefix = "chest:";

    /// <summary>
    /// Пытается разобрать FieldKey сундука на составляющие.
    /// </summary>
    /// <param name="fieldKey">
    /// Входная строка FieldKey, например «chest:brown», «chest:blue@2», «chest:red@3».
    /// </param>
    /// <param name="chestKey">
    /// При успехе — ключ типа сундука без суффикса раскладки, например «brown», «blue», «red».
    /// При неудаче — пустая строка.
    /// </param>
    /// <param name="slotCount">
    /// При успехе и наличии суффикса @N — число одновременно присутствующих типов (≥ 1).
    /// Для базового ключа без @N — <see langword="null"/>.
    /// При неудаче — <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/>, если строка является корректным ключом сундука
    /// (начинается с «chest:», <c>chestKey</c> не пуст, суффикс @N отсутствует или содержит целое ≥ 1);
    /// иначе <see langword="false"/>.
    /// </returns>
    public static bool TryParse(string fieldKey, out string chestKey, out int? slotCount)
    {
        chestKey = string.Empty;
        slotCount = null;

        if (!fieldKey.StartsWith(ChestPrefix, StringComparison.Ordinal))
            return false;

        // Часть после «chest:», например «brown» или «brown@2».
        ReadOnlySpan<char> rest = fieldKey.AsSpan(ChestPrefix.Length);

        if (rest.IsEmpty)
            return false;

        int atIndex = rest.IndexOf('@');

        if (atIndex < 0)
        {
            // Базовый ключ без @N — chestKey не должен содержать '@'.
            chestKey = rest.ToString();
            return true;
        }

        // Есть суффикс @N.
        ReadOnlySpan<char> keyPart = rest[..atIndex];
        ReadOnlySpan<char> nPart   = rest[(atIndex + 1)..];

        if (keyPart.IsEmpty || nPart.IsEmpty)
            return false;

        // Парсинг N: только целые ≥ 1, без знака, инвариантная культура.
        if (!int.TryParse(nPart, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1)
            return false;

        chestKey  = keyPart.ToString();
        slotCount = n;
        return true;
    }
}
