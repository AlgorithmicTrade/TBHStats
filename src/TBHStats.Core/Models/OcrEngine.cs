namespace TBHStats.Core.Models;

/// <summary>
/// Движок OCR для конкретного ROI (R2 — per-ROI выбор движка).
/// </summary>
public enum OcrEngine
{
    /// <summary>Встроенный движок Windows.Media.Ocr (основной, низкая латентность).</summary>
    WindowsMediaOcr,

    /// <summary>Tesseract OCR (резервный, более гибкий для нестандартных шрифтов).</summary>
    Tesseract,
}
