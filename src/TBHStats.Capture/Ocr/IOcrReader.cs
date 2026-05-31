using TBHStats.Core.Models;

namespace TBHStats.Capture.Ocr;

/// <summary>
/// Считывает текстовое значение из нормализованной области интереса (ROI) кадра
/// с помощью движка OCR.
/// </summary>
public interface IOcrReader
{
    /// <summary>
    /// Распознаёт значение из области, заданной <paramref name="roi"/>, в кадре <paramref name="frame"/>.
    /// </summary>
    /// <param name="frame">Кадр содержимого игрового окна.</param>
    /// <param name="roi">Конфигурация области интереса с нормализованными координатами [0..1].</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>
    /// Результат распознавания. При недоступности движка, пустом ROI или отсутствии текста
    /// возвращает <see cref="OcrResult"/> с <see cref="OcrResult.Recognized"/> == <c>false</c>
    /// и <see cref="OcrResult.Confidence"/> == 0 — исключение не выбрасывается.
    /// </returns>
    Task<OcrResult> ReadAsync(CapturedFrame frame, RoiCalibration roi, CancellationToken ct);
}
