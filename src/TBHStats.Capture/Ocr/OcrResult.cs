namespace TBHStats.Capture.Ocr;

/// <summary>
/// Результат распознавания одной области интереса (ROI).
/// </summary>
/// <param name="RawText">
/// Необработанный текст, возвращённый движком OCR.
/// Пустая строка, если ничего не распознано или движок недоступен.
/// </param>
/// <param name="Confidence">
/// Эвристическая оценка достоверности в диапазоне [0..1].
/// Для Windows.Media.Ocr вычисляется как доля площади ROI, покрытая bounding-box'ами
/// распознанных слов: <c>∑(wordBoundsArea) / roiArea</c>.
/// 0 — текст не найден или движок недоступен; 1 — все пиксели ROI покрыты словами.
/// </param>
/// <param name="Recognized">
/// <c>true</c>, если <see cref="RawText"/> содержит непустой (не только пробелы) текст.
/// </param>
public readonly record struct OcrResult(string RawText, double Confidence, bool Recognized);
