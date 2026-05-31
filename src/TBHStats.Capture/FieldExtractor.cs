using TBHStats.Capture.Ocr;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Core.Parsing;

namespace TBHStats.Capture;

/// <summary>
/// Реализация <see cref="IFieldExtractor"/>: читает OCR-поля кадра,
/// применяет фильтрацию по источнику (<see cref="FieldSource"/>) и собирает <see cref="RawObservation"/>.
/// </summary>
/// <remarks>
/// Неизвестные FieldKey тихо пропускаются — поле остаётся null (FR-005).
/// Ошибки парсинга не выбрасываются — поле остаётся null.
/// Визуальные поля (<c>stageProgress</c>, <c>bossPresent</c>) в v1 оставляются null:
/// они требуют анализа изображения, а не OCR, и будут обработаны T035 StageCompletionDetector.
/// </remarks>
public sealed class FieldExtractor : IFieldExtractor
{
    private readonly IOcrReader _ocr;
    private readonly IValueParser _parser;

    /// <summary>
    /// Создаёт экземпляр <see cref="FieldExtractor"/>.
    /// </summary>
    /// <param name="ocr">OCR-ридер для считывания ROI-областей.</param>
    /// <param name="parser">Парсер игровых значений (числа, время, идентификаторы этапов).</param>
    public FieldExtractor(IOcrReader ocr, IValueParser parser)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(parser);
        _ocr = ocr;
        _parser = parser;
    }

    /// <inheritdoc/>
    public async Task<RawObservation> ExtractAsync(
        CapturedFrame frame,
        IReadOnlyList<RoiCalibration> rois,
        TabRef? activeTab,
        GameMechanicsConfig cfg,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(rois);
        ArgumentNullException.ThrowIfNull(cfg);

        long? gold = null;
        long? xp = null;
        long? xpToLevel = null;
        int? stageTimeSeconds = null;
        int? heroLevel = null;
        long? heroDamage = null;
        string? heroClassText = null;
        string? stageText = null;
        StageRef? nextLocation = null;

        Dictionary<int, int> chests = [];
        Dictionary<string, double> perFieldConfidence = [];

        foreach (RoiCalibration roi in rois)
        {
            ct.ThrowIfCancellationRequested();

            // Пропускаем activeTab — он переносится из параметра напрямую, не через OCR
            if (roi.FieldKey == "activeTab")
                continue;

            // Визуальные поля — не текстовые, OCR их не читает (v1; детекция в T035)
            if (roi.FieldKey is "stageProgress" or "bossPresent")
                continue;

            // Фильтрация по источнику (FR-002b)
            bool available = roi.Source == FieldSource.MainZone
                || (roi.Source == FieldSource.Tab
                    && activeTab.HasValue
                    && activeTab.Value.TabId == roi.TabId);

            if (!available)
                continue;

            OcrResult res = await _ocr.ReadAsync(frame, roi, ct).ConfigureAwait(false);

            if (!res.Recognized)
                continue;

            string fieldKey = roi.FieldKey;

            // Маппинг FieldKey → поле RawObservation
            if (fieldKey == "gold")
            {
                if (_parser.TryParseAbbreviatedNumber(res.RawText, out long val))
                {
                    gold = val;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (fieldKey == "xp")
            {
                if (_parser.TryParseAbbreviatedNumber(res.RawText, out long val))
                {
                    xp = val;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (fieldKey == "xpToLevel")
            {
                if (_parser.TryParseAbbreviatedNumber(res.RawText, out long val))
                {
                    xpToLevel = val;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (fieldKey == "heroLevel")
            {
                // Уровень героя — обычное целое число; прямой int.TryParse более надёжен,
                // чем TryParseAbbreviatedNumber (уровень не бывает «1.2K»)
                if (int.TryParse(res.RawText.Trim(), out int lvl) && lvl >= 1)
                {
                    heroLevel = lvl;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (fieldKey == "heroDamage")
            {
                if (_parser.TryParseAbbreviatedNumber(res.RawText, out long val))
                {
                    heroDamage = val;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (fieldKey == "heroClass")
            {
                string trimmed = res.RawText.Trim();
                if (trimmed.Length > 0)
                {
                    heroClassText = trimmed;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (fieldKey == "stageId")
            {
                // Сырой текст сохраняется в StageText; парсинг в StageRef — не здесь
                string trimmed = res.RawText.Trim();
                if (trimmed.Length > 0)
                {
                    stageText = trimmed;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (fieldKey == "stageTime")
            {
                if (_parser.TryParseStageTimeSeconds(res.RawText, out int secs))
                {
                    stageTimeSeconds = secs;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (fieldKey == "nextLocation")
            {
                // TryParseStageId возвращает StageRef? — используем напрямую
                StageRef? parsed = _parser.TryParseStageId(res.RawText, cfg);
                if (parsed.HasValue)
                {
                    nextLocation = parsed;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (fieldKey.StartsWith("chest:", StringComparison.Ordinal))
            {
                string chestKey = fieldKey["chest:".Length..];
                ChestType? chestType = cfg.ChestTypes.FirstOrDefault(c => c.Key == chestKey);
                if (chestType is null)
                    continue;

                if (_parser.TryParseAbbreviatedNumber(res.RawText, out long val) && val >= 0)
                {
                    chests[chestType.Id] = (int)val;
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            // Прочие неизвестные FieldKey тихо пропускаются (FR-005)
        }

        return new RawObservation
        {
            TakenAtUtc         = frame.TimestampUtc.UtcDateTime,
            Gold               = gold,
            Xp                 = xp,
            XpToLevel          = xpToLevel,
            StageTimeSeconds   = stageTimeSeconds,
            StageProgress      = null,   // v1: визуальное поле, детекция в T035
            BossPresent        = null,   // v1: визуальное поле, детекция в T035
            HeroLevel          = heroLevel,
            HeroDamage         = heroDamage,
            HeroClassText      = heroClassText,
            StageText          = stageText,
            NextLocation       = nextLocation,
            ActiveTab          = activeTab,
            Chests             = chests,
            PerFieldConfidence = perFieldConfidence,
        };
    }
}
