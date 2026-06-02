using TBHStats.Capture.Chests;
using TBHStats.Capture.Ocr;
using TBHStats.Capture.Progress;
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
///
/// Chest-поля (ADR-023): основной путь — зонный анализатор <see cref="IChestZoneAnalyzer"/>
/// (FieldKey «chestZone»): одна ROI охватывает всю группу плашек, тип определяется по
/// цвету фона, счёт точек масштабонезависим. Если «chestZone»-ROI нет — fallback на
/// per-ROI путь через <see cref="IChestPanelAnalyzer"/> (ADR-022, legacy).
///
/// Прогрессбар этапа (ADR-024): FieldKey <c>"stageProgress"</c> → вызывает <see cref="IStageProgressReader"/>
/// (цветовой анализ пикселей: фиолетовый = путь, синий = босс). Результат заносится в
/// <see cref="RawObservation.StageProgress"/> и <see cref="RawObservation.BossPresent"/>.
/// FieldKey <c>"bossPresent"</c> отдельно не читается — boss извлекается из stageProgress-ROI.
/// </remarks>
public sealed class FieldExtractor : IFieldExtractor
{
    private readonly IOcrReader _ocr;
    private readonly IValueParser _parser;
    private readonly IChestPanelAnalyzer _chestPanelAnalyzer;
    private readonly IChestZoneAnalyzer _chestZoneAnalyzer;
    private readonly IStageProgressReader _stageProgressReader;

    /// <summary>
    /// Признак включённости обнаружения сундуков (chest:*/chestZone).
    /// </summary>
    /// <remarks>
    /// ВРЕМЕННО ОТКЛЮЧЕНО (T062 backlog): визуальный счёт точек требует доработки
    /// (многорядность 6+, плотные ряды, живой масштаб). Детекторы
    /// <see cref="IChestZoneAnalyzer"/>/<see cref="IChestPanelAnalyzer"/> (ADR-021…023) сохранены,
    /// но не вызываются: все chest-ROI пропускаются, <c>Chests</c> остаётся пустым →
    /// виджет показывает «—» вместо неверных значений.
    /// Чтобы снова включить — выставить <c>true</c>.
    /// </remarks>
    private static readonly bool ChestDetectionEnabled = false;

    /// <summary>
    /// Признак включённости визуальной детекции прогресса этапа (ADR-024).
    /// </summary>
    /// <remarks>
    /// При <c>true</c> FieldKey <c>"stageProgress"</c> обрабатывается через
    /// <see cref="IStageProgressReader"/> (цветовой анализ фиолетовый/синий),
    /// результат записывается в <see cref="RawObservation.StageProgress"/> и
    /// <see cref="RawObservation.BossPresent"/>.
    /// При <c>false</c> поля остаются null (поведение до T063).
    /// </remarks>
    private static readonly bool StageProgressDetectionEnabled = true;

    /// <summary>
    /// Создаёт экземпляр <see cref="FieldExtractor"/>.
    /// </summary>
    /// <param name="ocr">OCR-ридер для считывания ROI-областей.</param>
    /// <param name="parser">Парсер игровых значений (числа, время, идентификаторы этапов).</param>
    /// <param name="chestPanelAnalyzer">Визуальный анализатор плашки сундука: тип по цвету + счёт точек (ADR-022, legacy per-ROI путь).</param>
    /// <param name="chestZoneAnalyzer">Зонный анализатор группы плашек: локализация по цвету + счёт по рядам (ADR-023, приоритетный путь).</param>
    /// <param name="stageProgressReader">Визуальный детектор прогрессбара этапа: фиолетовый/синий анализ (ADR-024).</param>
    public FieldExtractor(
        IOcrReader ocr,
        IValueParser parser,
        IChestPanelAnalyzer chestPanelAnalyzer,
        IChestZoneAnalyzer chestZoneAnalyzer,
        IStageProgressReader stageProgressReader)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(chestPanelAnalyzer);
        ArgumentNullException.ThrowIfNull(chestZoneAnalyzer);
        ArgumentNullException.ThrowIfNull(stageProgressReader);
        _ocr = ocr;
        _parser = parser;
        _chestPanelAnalyzer = chestPanelAnalyzer;
        _chestZoneAnalyzer = chestZoneAnalyzer;
        _stageProgressReader = stageProgressReader;
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
        double? stageProgressValue = null;
        bool? bossPresentValue = null;

        Dictionary<int, int>     chests         = [];
        Dictionary<int, double>  chestBestMatch = []; // typeId → лучший PanelMatch (legacy per-ROI)
        Dictionary<string, double> perFieldConfidence = [];
        bool chestZoneRead = false; // флаг: зонный анализатор уже отработал (ADR-023)

        foreach (RoiCalibration roi in rois)
        {
            ct.ThrowIfCancellationRequested();

            // Пропускаем activeTab — он переносится из параметра напрямую, не через OCR
            if (roi.FieldKey == "activeTab")
                continue;

            // bossPresent отдельно не читается — boss извлекается из stageProgress-ROI (ADR-024).
            if (roi.FieldKey == "bossPresent")
                continue;

            // Прогрессбар этапа: визуальный анализ цвета (фиолетовый/синий), не OCR (ADR-024).
            if (roi.FieldKey == "stageProgress")
            {
                if (StageProgressDetectionEnabled)
                {
                    StageProgressReading reading = await _stageProgressReader
                        .ReadAsync(frame, roi, cfg, ct)
                        .ConfigureAwait(false);

                    if (reading.Progress.HasValue)
                    {
                        stageProgressValue = reading.Progress;
                        bossPresentValue   = reading.BossPresent;
                        perFieldConfidence["stageProgress"] = 1.0;
                        perFieldConfidence["bossPresent"]   = 1.0;
                    }
                    // Если null — данных нет, оставляем stageProgressValue/bossPresentValue null
                }
                continue;
            }

            string fieldKey = roi.FieldKey;

            // Обнаружение сундуков ВРЕМЕННО ОТКЛЮЧЕНО (T062 backlog, см. ChestDetectionEnabled).
            // Все chest-ROI (chestZone и chest:*) пропускаются → Chests пустой, виджет показывает «—».
            if (fieldKey == "chestZone" || ChestFieldKey.TryParse(fieldKey, out _, out _))
            {
                if (!ChestDetectionEnabled)
                    continue;
            }

            // Зонный детектор сундуков (ADR-023): одна ROI «chestZone» охватывает всю группу плашек.
            // Приоритетный источник: локализация по цвету + масштабонезависимый счёт по рядам.
            if (fieldKey == "chestZone")
            {
                IReadOnlyDictionary<int, int> zoneResult = await _chestZoneAnalyzer
                    .AnalyzeZoneAsync(frame, roi, cfg, ct)
                    .ConfigureAwait(false);

                foreach ((int typeId, int dotCount) in zoneResult)
                {
                    chests[typeId] = dotCount;
                    perFieldConfidence[$"chest:{typeId}"] = 1.0;
                }

                perFieldConfidence["chestZone"] = 1.0;
                chestZoneRead = true;
                continue;
            }

            // Chest-поля: точки ГРАФИЧЕСКИЕ (не текст) → тип определяется по цвету плашки (ADR-022).
            // Используются как fallback, если «chestZone»-ROI не откалибрована (chestZoneRead == false).
            // Если зонный анализатор уже отработал — per-ROI путь пропускается (не перезаписывает зонные данные).
            if (ChestFieldKey.TryParse(fieldKey, out _, out _))
            {
                if (chestZoneRead)
                    continue; // зонный путь приоритетен

                ChestPanelReading reading = await _chestPanelAnalyzer
                    .AnalyzeChestPanelAsync(frame, roi, cfg, ct)
                    .ConfigureAwait(false);

                if (reading.ChestTypeId is null)
                    continue; // плашка не распознана — пропускаем

                int typeId = reading.ChestTypeId.Value;

                // Мёрдж: несколько ROI могут покрывать одну плашку.
                // Берём чтение с наибольшим PanelMatch (наиболее уверенное).
                if (!chestBestMatch.TryGetValue(typeId, out double prevMatch)
                    || reading.PanelMatch > prevMatch)
                {
                    chests[typeId]         = reading.DotCount;
                    chestBestMatch[typeId] = reading.PanelMatch;
                    perFieldConfidence[$"chest:{typeId}"] = reading.PanelMatch;
                }

                continue;
            }

            // Игра показывает несколько разделов ОДНОВРЕМЕННО (Hero/Status/Portal видны рядом),
            // поэтому модель «одна активная вкладка» неприменима: читаем ВСЕ калиброванные поля
            // с их фиксированных позиций каждый кадр (ADR-019). Валидация значения — парсером
            // (число/время/этап обязаны корректно распарситься) и sanity-проверками в IObservationValidator;
            // когда раздел закрыт, его область не даёт валидного значения и поле остаётся прежним.
            // Источник-фильтр по activeTab (прежний FR-002b) снят как несоответствующий реальному UI.

            OcrResult res = await _ocr.ReadAsync(frame, roi, ct).ConfigureAwait(false);

            if (!res.Recognized)
                continue;

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

        return new RawObservation
        {
            TakenAtUtc         = frame.TimestampUtc.UtcDateTime,
            Gold               = gold,
            Xp                 = xp,
            XpToLevel          = xpToLevel,
            StageTimeSeconds   = stageTimeSeconds,
            StageProgress      = stageProgressValue,
            BossPresent        = bossPresentValue,
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
