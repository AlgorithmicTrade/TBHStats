using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using TBHStats.App.Services;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

namespace TBHStats.App.ViewModels;

/// <summary>
/// ViewModel живых показателей (US1, T029+T031).
/// Подписывается на <see cref="IStatsOrchestrator.SnapshotUpdated"/> и маршалирует
/// все обновления в UI-поток через <see cref="DispatcherQueue"/>.
/// </summary>
public sealed partial class LiveStatsViewModel : ObservableObject
{
    private readonly IStatsOrchestrator _orchestrator;
    private readonly IGameMechanics _gameMechanics;
    private readonly DispatcherQueue? _dispatcher;

    // ──────────────────────────────────────────────────────────────
    // Живые темпы
    // ──────────────────────────────────────────────────────────────

    /// <summary>Золото в час (числовое значение для прогресс-баров / сортировки).</summary>
    [ObservableProperty]
    private double _goldPerHour;

    /// <summary>Опыт в час (числовое значение).</summary>
    [ObservableProperty]
    private double _xpPerHour;

    /// <summary>Золото в час, отформатированное для отображения («1 234 567/ч»).</summary>
    [ObservableProperty]
    private string _goldPerHourText = "—";

    /// <summary>Опыт в час, отформатированное для отображения («1 234 567/ч»).</summary>
    [ObservableProperty]
    private string _xpPerHourText = "—";

    /// <summary>Сундуки/час по типам: «1: 12/ч, 2: 3/ч» (ключ = ChestType.Id).</summary>
    [ObservableProperty]
    private string _chestsPerHourText = "—";

    // ──────────────────────────────────────────────────────────────
    // Данные героя
    // ──────────────────────────────────────────────────────────────

    /// <summary>Уровень героя (null до первого надёжного замера).</summary>
    [ObservableProperty]
    private int? _heroLevel;

    /// <summary>Класс героя (null до первого надёжного замера).</summary>
    [ObservableProperty]
    private string? _heroClass;

    /// <summary>Урон героя (null до первого надёжного замера).</summary>
    [ObservableProperty]
    private long? _heroDamage;

    /// <summary>Уровень героя для отображения («42» или «—»).</summary>
    [ObservableProperty]
    private string _heroLevelText = "—";

    /// <summary>Класс героя для отображения (или «—»).</summary>
    [ObservableProperty]
    private string _heroClassText = "—";

    /// <summary>Урон героя для отображения («1 234 567» или «—»).</summary>
    [ObservableProperty]
    private string _heroDamageText = "—";

    // ──────────────────────────────────────────────────────────────
    // Золото и этап
    // ──────────────────────────────────────────────────────────────

    /// <summary>Текущее кумулятивное золото (null до первого надёжного замера).</summary>
    [ObservableProperty]
    private long? _gold;

    /// <summary>Золото для отображения («1 234 567» или «—»).</summary>
    [ObservableProperty]
    private string _goldText = "—";

    /// <summary>Текущий этап в виде строки («Act1/normal/5» или «—»).</summary>
    [ObservableProperty]
    private string _stageText = "—";

    // ──────────────────────────────────────────────────────────────
    // Состояния (T031)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Статус захвата: «Игра не найдена» / «Ожидание окна игры» /
    /// «Данные устаревают…» / «Активно».
    /// </summary>
    [ObservableProperty]
    private string _statusText = "Игра не найдена";

    /// <summary>true, если окно игры найдено (State != NotFound).</summary>
    [ObservableProperty]
    private bool _isGameFound;

    /// <summary>true, если состояние — Waiting (окно свёрнуто / скрыто).</summary>
    [ObservableProperty]
    private bool _isWaiting;

    /// <summary>true, если данные считаются устаревшими (по флагу снимка).</summary>
    [ObservableProperty]
    private bool _isStale = true;

    /// <summary>Время последнего надёжного замера в локальном формате («—», если нет данных).</summary>
    [ObservableProperty]
    private string _lastUpdateText = "—";

    // ──────────────────────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Инициализирует ViewModel.
    /// Должен создаваться на UI-потоке, чтобы корректно захватить <see cref="DispatcherQueue"/>.
    /// </summary>
    /// <param name="orchestrator">Фоновый оркестратор — источник снимков живой статистики.</param>
    /// <param name="gameMechanics">Конфиг механик игры — используется для имён типов сундуков в подписях (A11y §XI).</param>
    public LiveStatsViewModel(IStatsOrchestrator orchestrator, IGameMechanics gameMechanics)
    {
        _orchestrator = orchestrator;
        _gameMechanics = gameMechanics;

        // Захватываем DispatcherQueue текущего (UI) потока.
        // Если конструктор вызван не на UI-потоке (тесты, headless) — dispatcher будет null,
        // и обновления применятся синхронно.
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // Применяем начальный снимок (Empty) — наполняет свойства дефолтными значениями.
        ApplySnapshot(orchestrator.Current);

        // Подписываемся на обновления из фоновой петли оркестратора.
        _orchestrator.SnapshotUpdated += OnSnapshotUpdated;
    }

    // ──────────────────────────────────────────────────────────────
    // Event handler
    // ──────────────────────────────────────────────────────────────

    private void OnSnapshotUpdated(object? sender, LiveStatsSnapshot snapshot)
    {
        if (_dispatcher is not null)
        {
            _dispatcher.TryEnqueue(() => ApplySnapshot(snapshot));
        }
        else
        {
            ApplySnapshot(snapshot);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Core update method
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Применяет снимок к observable-свойствам.
    /// Должен вызываться на UI-потоке (либо напрямую в тестах без dispatcher'а).
    /// </summary>
    /// <param name="s">Снимок, опубликованный оркестратором.</param>
    public void ApplySnapshot(LiveStatsSnapshot s)
    {
        // Темпы
        GoldPerHour = s.Rates.GoldPerHour;
        XpPerHour   = s.Rates.XpPerHour;

        GoldPerHourText = FormatRate(s.Rates.GoldPerHour);
        XpPerHourText   = FormatRate(s.Rates.XpPerHour);
        ChestsPerHourText = BuildChestsText(s.Rates.ChestPerHourByType);

        // Герой
        HeroLevel  = s.HeroLevel;
        HeroClass  = s.HeroClass;
        HeroDamage = s.HeroDamage;

        HeroLevelText  = s.HeroLevel  is int lvl  ? lvl.ToString()                 : "—";
        HeroClassText  = s.HeroClass  is { Length: > 0 } cls ? cls               : "—";
        HeroDamageText = s.HeroDamage is long dmg ? FormatLong(dmg)               : "—";

        // Золото и этап
        Gold      = s.Gold;
        GoldText  = s.Gold  is long g ? FormatLong(g) : "—";
        StageText = s.Stage is StageRef sr ? sr.ToString() : "—";

        // Состояния (T031)
        IsGameFound = s.State != CaptureState.NotFound;
        IsWaiting   = s.State == CaptureState.Waiting;
        IsStale     = s.IsStale;

        StatusText = s.State switch
        {
            CaptureState.NotFound  => "Игра не найдена",
            CaptureState.Waiting   => "Ожидание окна игры",
            CaptureState.Capturing => s.IsStale ? "Данные устаревают…" : "Активно",
            _                      => "—"
        };

        LastUpdateText = s.LastReliableUtc is DateTime utc
            ? utc.ToLocalTime().ToString("HH:mm:ss")
            : "—";
    }

    // ──────────────────────────────────────────────────────────────
    // Formatting helpers
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Форматирует темп (золото/час, опыт/час) с разделителем тысяч и суффиксом «/ч».
    /// Например: 1234567 → «1 234 567/ч».
    /// </summary>
    private static string FormatRate(double value)
    {
        if (value <= 0) return "0/ч";
        return $"{value:N0}/ч";
    }

    /// <summary>
    /// Форматирует целое число с разделителем тысяч.
    /// Например: 1234567 → «1 234 567».
    /// </summary>
    private static string FormatLong(long value) => value.ToString("N0");

    /// <summary>
    /// Строит строку вида «Базовый: 12/ч, Редкий: 3/ч» из словаря ChestType.Id → сундуков/час.
    /// Использует <see cref="IGameMechanics"/> для получения <see cref="ChestType.DisplayName"/>
    /// вместо числового Id — тип сундука различается текстом, не только цветом (A11y §XI).
    /// Возвращает «—» если словарь пуст.
    /// </summary>
    private string BuildChestsText(IReadOnlyDictionary<int, double> byType)
    {
        if (byType.Count == 0) return "—";

        GameMechanicsConfig cfg = _gameMechanics.Current;

        return string.Join(", ", byType
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                ChestType? chestType = cfg.ChestTypes.FirstOrDefault(ct => ct.Id == kv.Key);
                string label = chestType is not null ? chestType.DisplayName : kv.Key.ToString();
                return $"{label}: {kv.Value:N1}/ч";
            }));
    }
}
