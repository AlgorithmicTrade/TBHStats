using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

namespace TBHStats.Capture.Tabs;

/// <summary>
/// Сопоставляет распознанный OCR-текст названия активной вкладки с записями
/// <see cref="Tab"/> из конфигурации механик игры по нечёткому алгоритму (fuzzy match).
/// Является чистым доменно-близким швом: не зависит от OCR-движка и кадра —
/// принимает уже распознанную строку. Это делает компонент юнит-тестируемым без WGC/OCR.
/// </summary>
/// <remarks>
/// <para><strong>Алгоритм (зафиксирован для согласованности T019 и T022):</strong></para>
/// <list type="number">
/// <item>
/// <description>
/// Нормализация строки: <see cref="string.Trim()"/> + <see cref="string.ToLowerInvariant()"/>
/// + схлопывание подряд идущих пробелов в один (regex <c>\s+</c> → <c>" "</c>).
/// </description>
/// </item>
/// <item>
/// <description>
/// Похожесть = <c>1.0 − LevenshteinDistance(normalized1, normalized2) / Max(len1, len2)</c>,
/// где <c>len1</c>/<c>len2</c> — длины нормализованных строк.
/// Если обе строки после нормализации пусты → похожесть = 0 (не считается совпадением).
/// </description>
/// </item>
/// <item>
/// <description>
/// Матч идёт против <see cref="Tab.RecognitionText"/> каждой вкладки из
/// <see cref="GameMechanicsConfig.Tabs"/> у которых <see cref="Tab.IsActive"/> == <c>true</c>.
/// Выбирается максимальная похожесть.
/// </description>
/// </item>
/// <item>
/// <description>
/// Если лучшая похожесть ≥ <paramref name="minSimilarity"/> → вернуть
/// <c>new TabRef(tab.Id, tab.Key, similarity)</c>; иначе <c>null</c>.
/// </description>
/// </item>
/// <item>
/// <description>
/// Пустой/whitespace <c>recognizedText</c> → немедленно вернуть <c>null</c>
/// (нет смысла матчить пустой результат OCR).
/// </description>
/// </item>
/// </list>
/// </remarks>
public interface ITabNameMatcher
{
    /// <summary>
    /// Сопоставляет распознанный OCR-текст названия активной вкладки с вкладками из конфига.
    /// </summary>
    /// <param name="recognizedText">
    /// Текст, распознанный OCR-движком из области activeTab.
    /// Пустая строка или <c>null</c>-эквивалент (whitespace) → возвращает <c>null</c>.
    /// </param>
    /// <param name="cfg">
    /// Конфигурация механик игры; матч идёт по <see cref="Tab.RecognitionText"/>
    /// активных вкладок (<see cref="Tab.IsActive"/> == <c>true</c>).
    /// </param>
    /// <param name="minSimilarity">
    /// Минимальный порог нормализованной похожести [0..1], при котором совпадение считается
    /// уверенным. Значение по умолчанию: 0.6 (60 %).
    /// </param>
    /// <returns>
    /// <see cref="TabRef"/> с <see cref="TabRef.Confidence"/> = нормализованная похожесть [0..1],
    /// если найдено уверенное совпадение; <c>null</c> иначе.
    /// </returns>
    TabRef? Match(string recognizedText, GameMechanicsConfig cfg, double minSimilarity = 0.6);
}
