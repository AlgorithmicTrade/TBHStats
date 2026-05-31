namespace TBHStats.Core.Models;

/// <summary>
/// Лёгкий value-объект, однозначно идентифицирующий распознанную активную вкладку игры.
/// Производится слоем <c>TBHStats.Capture</c> (ITabDetector) и потребляется доменным ядром
/// для фильтрации доступных полей по источнику (FR-002a/002b).
/// </summary>
/// <param name="TabId">
/// Первичный ключ вкладки (FK → <c>Tab.Id</c> в <c>GameMechanicsConfig</c>).
/// Уникально идентифицирует вкладку в текущей конфигурации механик.
/// </param>
/// <param name="Key">
/// Стабильный строковый ключ вкладки (например, «hero», «status», «portal»).
/// Непустая строка; соответствует <c>Tab.Key</c> из конфига.
/// </param>
/// <param name="Confidence">
/// Уверенность OCR-распознавания вкладки (∈ [0.0..1.0]).
/// Значения ниже порога confidenceThreshold → вкладка считается неопределённой.
/// </param>
public readonly record struct TabRef(int TabId, string Key, double Confidence)
{
    /// <summary>Первичный ключ вкладки в GameMechanicsConfig (FK → Tab.Id).</summary>
    public int TabId { get; init; } = TabId;

    /// <summary>Стабильный строковый ключ вкладки (непустой, например «hero», «status», «portal»).</summary>
    public string Key { get; init; } = ValidateKey(Key);

    /// <summary>Уверенность распознавания вкладки OCR-движком (∈ [0.0..1.0]).</summary>
    public double Confidence { get; init; } = Confidence;

    private static string ValidateKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("TabRef.Key не должен быть пустым или состоять только из пробелов.", nameof(Key));
        return value;
    }

    /// <summary>
    /// Возвращает строковое представление в формате «Tab:{Key}(id={TabId}, conf={Confidence:F2})».
    /// </summary>
    public override string ToString() => $"Tab:{Key}(id={TabId}, conf={Confidence:F2})";
}
