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
    private readonly IChestLayoutResolver _chestLayoutResolver;

    /// <summary>
    /// Создаёт экземпляр <see cref="FieldExtractor"/>.
    /// </summary>
    /// <param name="ocr">OCR-ридер для считывания ROI-областей.</param>
    /// <param name="parser">Парсер игровых значений (числа, время, идентификаторы этапов).</param>
    /// <param name="chestLayoutResolver">Резолвер активной раскладки @N-сундуков.</param>
    public FieldExtractor(IOcrReader ocr, IValueParser parser, IChestLayoutResolver chestLayoutResolver)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(chestLayoutResolver);
        _ocr = ocr;
        _parser = parser;
        _chestLayoutResolver = chestLayoutResolver;
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
        long? xpFromPair = null;
        long? xpToLevelFromPair = null;
        double? xpPairConfidence = null;
        int? stageTimeSeconds = null;
        int? heroLevel = null;
        long? heroDamage = null;
        string? heroClassText = null;
        string? stageText = null;
        StageRef? nextLocation = null;

        Dictionary<int, int> chests = [];
        List<ChestLayoutReading> chestReadings = [];
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

            // Игра показывает несколько разделов ОДНОВРЕМЕННО (Hero/Status/Portal видны рядом),
            // поэтому модель «одна активная вкладка» неприменима: читаем ВСЕ калиброванные поля
            // с их фиксированных позиций каждый кадр (ADR-019). Валидация значения — парсером
            // (число/время/этап обязаны корректно распарситься) и sanity-проверками в IObservationValidator;
            // когда раздел закрыт, его область не даёт валидного значения и поле остаётся прежним.
            // Источник-фильтр по activeTab (прежний FR-002b) снят как несоответствующий реальному UI.

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
            else if (fieldKey == "xpPair")
            {
                if (_parser.TryParseXpPair(res.RawText, out long cur, out long toLvl))
                {
                    xpFromPair = cur;
                    xpToLevelFromPair = toLvl;
                    xpPairConfidence = res.Confidence;
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
                // MainZone-формат «акт-этап» (например «3-2») без слова сложности.
                // Сложность в MainZone не показывается — берём дефолтную из конфига
                // (для StageRef нужна непустая сложность; отображается как «акт-этап», см. ViewModel).
                if (_parser.TryParseNextLocation(res.RawText, out int nlAct, out int nlStage))
                {
                    string diffKey = cfg.Difficulties.Count > 0 ? cfg.Difficulties[0].Key : "normal";
                    nextLocation = new StageRef(nlAct, diffKey, nlStage);
                    perFieldConfidence[fieldKey] = res.Confidence;
                }
            }
            else if (ChestFieldKey.TryParse(fieldKey, out string chestKey, out int? slotCount))
            {
                ChestType? chestType = cfg.ChestTypes.FirstOrDefault(c => c.Key == chestKey);
                if (chestType is null)
                    continue;

                if (_parser.TryParseAbbreviatedNumber(res.RawText, out long val) && val >= 0)
                {
                    if (slotCount is null)
                    {
                        // Базовый ключ «chest:brown» — обратная совместимость: одна фиксированная позиция.
                        chests[chestType.Id] = (int)val;
                        perFieldConfidence[fieldKey] = res.Confidence;
                    }
                    else
                    {
                        // Калиброванный ключ «chest:brown@N» — накапливаем для резолвера.
                        chestReadings.Add(new ChestLayoutReading(chestType.Id, slotCount.Value, (int)val));
                        perFieldConfidence[fieldKey] = res.Confidence;
                    }
                }
            }
            // Прочие неизвестные FieldKey тихо пропускаются (FR-005)
        }

        // Объединённая зона опыта xpPair перекрывает отдельные xp/xpToLevel, когда размечена.
        if (xpFromPair.HasValue)
        {
            xp = xpFromPair;
            xpToLevel = xpToLevelFromPair;
            perFieldConfidence["xp"] = xpPairConfidence!.Value;
            perFieldConfidence["xpToLevel"] = xpPairConfidence.Value;
        }

        // Разрешаем @N-раскладку и вливаем результат (имеет приоритет над базовыми ключами).
        if (chestReadings.Count > 0)
        {
            ChestLayoutResolution resolution = _chestLayoutResolver.Resolve(chestReadings);
            foreach (KeyValuePair<int, int> kv in resolution.Counts)
                chests[kv.Key] = kv.Value;
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
