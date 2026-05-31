namespace TBHStats.Core.Models;

/// <summary>
/// Состояние машины захвата окна игры (FR-001/FR-005).
/// Переходы: NotFound → Capturing → Waiting → Capturing → NotFound.
/// Перекрытое окно остаётся в <see cref="Capturing"/>; <see cref="Waiting"/> — только для свёрнутого/закрытого.
/// </summary>
public enum CaptureState
{
    /// <summary>Окно игры не найдено (процесс не запущен или HWND недоступен).</summary>
    NotFound,

    /// <summary>Окно найдено и кадры захватываются (в том числе при перекрытии другим окном).</summary>
    Capturing,

    /// <summary>Окно свёрнуто или закрыто; отображаются последние достоверные данные; авто-возобновление ≤5 с после восстановления видимости (SC-008).</summary>
    Waiting,
}
