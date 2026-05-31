# Архитектура TBHStats

**Проект**: TBHStats — десктоп-помощник по статистике для игры Task Bar Hero
**Платформа**: Windows 11 (x64/arm64), один локальный пользователь
**Дата актуализации**: 2026-05-31 (T008/T015/T014/T013/T009/T010)
**Спецификация-источник**: `specs/001-tbh-stats-helper/` (plan, research, data-model, contracts) · конституция `v2.2.0`

> TBHStats наблюдает за окном запущенной игры, **визуально** считывает игровые показатели (золото, опыт, время этапа, класс/уровень/урон героя, текущий этап, сундуки по типам), вычисляет темпы (золото/час, опыт/час, сундуки/час), накапливает историю по 60 этапам (3 акта × 2 сложности × 10) и рекомендует оптимальный этап для фарма. Режим строго **observe-only**: никаких записей в память игры и инъекций ввода.

## Содержание

1. [Технологический стек и обоснование](#1-технологический-стек-и-обоснование)
2. [Слой захвата](#2-слой-захвата)
3. [OCR](#3-ocr)
4. [ROI — области считывания](#4-roi--области-считывания)
5. [Детекция активной вкладки](#5-детекция-активной-вкладки)
6. [Хранилище](#6-хранилище)
7. [Графики](#7-графики)
8. [Структура решения](#8-структура-решения)
9. [Поток данных и оркестрация](#9-поток-данных-и-оркестрация)
10. [Машина состояний захвата](#10-машина-состояний-захвата)
11. [Ключевые внутренние контракты](#11-ключевые-внутренние-контракты)
12. [Расширяемость](#12-расширяемость)
13. [Задел под Android](#13-задел-под-android)
14. [Поставка](#14-поставка)
15. [Соответствие конституции](#15-соответствие-конституции)

---

## 1. Технологический стек и обоснование

| Слой | Технология | Роль |
|------|------------|------|
| Язык / рантайм | **C# 12 / .NET 8 (LTS)** | единый язык на десктоп + будущий Android |
| UI-фреймворк | **WinUI 3 (Windows App SDK)** | оверлей-виджет, Fluent, MVVM |
| UI fallback | **WPF** (задокументированный) | оболочка оверлея, если прозрачность/`Topmost` в WinUI 3 окажутся проблемными |
| Захват окна | **Windows.Graphics.Capture (WGC)** по HWND | кадр окна даже при перекрытии |
| OCR | **Windows.Media.Ocr** (встроенный), Tesseract — fallback | распознавание значений |
| База данных | **SQLite через EF Core** | локальная история, миграции |
| Графики | **LiveCharts2** (Skia), ScottPlot — альт. | тренды |
| MVVM | **CommunityToolkit.Mvvm** | ObservableObject / RelayCommand |

### Почему .NET / C#

- **Лучшая нативная интеграция с Windows 11**: WGC и Windows.Media.Ocr — это WinRT-API, доступные из .NET 8 через CsWinRT-проекции напрямую, без сторонних обвязок.
- **Единый язык** на всех слоях (захват, домен, хранилище, UI) — нет переключения парадигм.
- **Задел под Android**: доменное ядро (`TBHStats.Core`) без UI/WinRT-зависимостей переиспользуется в **.NET MAUI** — реальное переиспользование C#-логики, а не переписывание.

### Отклонённые альтернативы

| Альтернатива | Почему отклонена |
|--------------|------------------|
| **Tauri** (React/TS + Rust) | два языка (фронт на TS, бэкенд на Rust ≠ TS); лишняя когнитивная нагрузка для соло-разработки. |
| **Electron** (всё на TS) | единый язык, но **самый хрупкий захват перекрытого окна** и тяжёлый рантайм; не закрывает ключевое требование FR-005. |

---

## 2. Слой захвата

**Решение**: **Windows.Graphics.Capture (WGC)**, таргетинг по **HWND** окна игры через interop (`IGraphicsCaptureItemInterop.CreateForWindow`).

- WGC через **DWM/DXGI** отдаёт GPU-текстуру конкретного окна по HWND и **захватывает окно, даже когда оно перекрыто/в фоне** — обходит Z-order. Это прямое покрытие FR-005.
- Захват по HWND автоматически **«следует» за окном** при перемещении/ресайзе → основа независимости ROI (FR-005b).
- Встроено в Windows 10 1903+/Windows 11, без сторонних зависимостей.
- **Свёрнутое (`IsIconic`) / закрытое окно** не имеет валидной поверхности → состояние **«ожидание»** (`Waiting`), это не ошибка.

**Fallback**: `PrintWindow` (GDI, `PW_RENDERFULLCONTENT`) — аварийный путь; работает для части фоновых окон, но даёт чёрный кадр у ряда GPU-ускоренных приложений, поэтому не основной.

**Отклонено**: BitBlt со скриншота экрана (ломается при перекрытии, привязан к экрану) и DXGI Desktop Duplication (весь монитор, не окно).

**Реализация (T013):** `ICaptureSession` + `CaptureSession` в `TBHStats.Capture/Wgc/`:
- `ICaptureSession` (IAsyncDisposable) — `CaptureState State`, `event Action<CaptureState> StateChanged`, `Task<CapturedFrame?> TryGetFrameAsync(CancellationToken)`.
- `CaptureSession` — реальный WGC-pipeline без заглушек:
  - `Direct3D11Interop.CreateDevice()`: `D3D11CreateDevice` (d3d11.dll, `D3D11_DRIVER_TYPE_HARDWARE`, флаг `BGRA_SUPPORT`) → QI до `IDXGIDevice` → `CreateDirect3D11DeviceFromDXGIDevice` → WinRT-обёртка `IDirect3DDevice`.
  - `GraphicsCaptureItemInterop.CreateForWindow(hwnd)`: `RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem")` → QI до `IGraphicsCaptureItemInterop` (GUID `3628E81B-...`) → `CreateForWindow` → `GraphicsCaptureItem`.
  - `Direct3D11CaptureFramePool.CreateFreeThreaded(device, B8G8R8A8UIntNormalized, 2, size)` — не требует UI-диспетчера, безопасен для фоновых циклов.
  - `GraphicsCaptureSession.StartCapture()` с опциональным `IsBorderRequired = false` (Windows 11 SDK 22621+; игнорируется при недоступности).
  - При ресайзе окна — `framePool.Recreate(...)`.
  - Кадр: `framePool.TryGetNextFrame()` → `SoftwareBitmap.CreateCopyFromSurfaceAsync(surface)` → `CapturedFrame`.
- **Машина состояний** управляется в `TryGetFrameAsync`: `GetVisibility` → `Closed`/`Minimized` → `NotFound`/`Waiting` + освобождение ресурсов или сохранение (ожидание восстановления); `Visible` → инициализировать pipeline (lazy) → `Capturing`. При смене состояния — `StateChanged?.Invoke(newState)`.
- Concurrency: все операции под `SemaphoreSlim(1,1)`. `DisposeAsync` ожидает семафор перед очисткой.

---

## 3. OCR

**Решение**: **Windows.Media.Ocr** (`OcrEngine.TryCreateFromUserProfileLanguages` / `TryCreateFromLanguage`) как основной движок; **Tesseract — fallback** для «трудных» ROI.

- Встроен в Windows, **0 сторонних зависимостей**, принимает `SoftwareBitmap` (кадр WGC конвертируется напрямую, без записи на диск).
- Возвращает слова с bounding box → сопоставление с ROI и оценка достоверности по геометрии.
- **Tesseract** настраивается под цифры через whitelist (`tessedit_char_whitelist=0123456789.,KMB`); per-ROI выбор движка задаётся в калибровке (`RoiCalibration.OcrEngine`).

**Эмпирический риск**: фактическая точность на конкретном шрифте Task Bar Hero — открытый вопрос, **проверяется на реальных скриншотах** (фикстуры в `TBHStats.Capture.Tests`). Митигация: confidence-порог + sanity-проверки значений + fallback-движок per ROI.

**Реализация (T014):**
- `CapturedFrame` (sealed, IDisposable) — общий тип кадра слоя: `SoftwareBitmap Bitmap`, `SizePx ClientSize`, `DateTimeOffset TimestampUtc`. Возвращается `ICaptureSession`, потребляется OCR/детектором/экстрактором. `Dispose()` освобождает `Bitmap`.
- `OcrResult` (readonly record struct) — `(string RawText, double Confidence, bool Recognized)`.
- `IOcrReader` — `Task<OcrResult> ReadAsync(CapturedFrame, RoiCalibration, CancellationToken)`.
- `OcrReader` — реальная реализация:
  - Lazy-init `WinOcrEngine` (`TryCreateFromUserProfileLanguages` → fallback `TryCreateFromLanguage("en")`). Если оба null — возвращает `OcrResult("", 0, false)` без исключения.
  - Кроп `SoftwareBitmap` к ROI через `BitmapEncoder` (BMP, in-memory stream) + `BitmapDecoder` с `BitmapTransform.Bounds` — извлекает суб-регион без ручного попиксельного копирования. Конвертация к `Bgra8/Premultiplied` через `SoftwareBitmap.Convert` при необходимости.
  - **Confidence-эвристика**: `∑(wordBoundsArea) / roiArea`, clamp [0..1]. Windows.Media.Ocr не возвращает числовую confidence per-word, поэтому геометрическое покрытие служит прокси достоверности.
  - Возвращает `OcrResult("", 0, false)` при: недоступном движке, пустом ROI, ошибке отмены (кроме `OperationCanceledException`, который пробрасывается).

---

## 4. ROI — области считывания

**Решение**: ROI хранятся как **нормализованные доли клиентской области окна** (`x, y, w, h ∈ [0..1]`), не пиксели экрана (FR-005b).

- На каждом кадре известен размер клиентской области (из WGC item size) → ROI разворачивается в пиксели **текущего** кадра.
- Доли **инвариантны** к положению/размеру/масштабу/DPI/монитору → автоматическое следование без перекалибровки.
- Калибровка пользователем = разметка прямоугольников поверх захваченного кадра → сохраняется как доли.

Математика разворота тонкая (<50 строк), живёт в `TBHStats.Capture/Roi`. Абсолютные экранные координаты отклонены — ломаются при любом перемещении/масштабе.

**Реализация (T015):**
- `RoiPixelRect` (readonly record struct) — прямоугольник в пикселях: поля `X`, `Y`, `Width`, `Height`; свойства `Right`, `Bottom`, `IsEmpty`.
- `IRoiMapper` — интерфейс с тремя методами: `ToPixels(RoiCalibration, SizePx)`, `ToPixels(double x,y,w,h, SizePx)`, `ToNormalized(RoiPixelRect, SizePx)`.
- `RoiMapper` — реализация: клампинг входных долей к `[0..1]`; `Math.Round(MidpointRounding.AwayFromZero)` для предсказуемости; итоговый прямоугольник клампируется по границам кадра (`[0..width]` / `[0..height]`); при `SizePx.Empty` → `RoiPixelRect.Empty` (без исключения).
- `ToNormalized` — обратное преобразование для калибровки: пиксели пользователя → нормализованные доли для `RoiCalibration`.

---

## 5. Детекция активной вкладки

**Решение**: распознать **название активной вкладки** через тот же OCR в выделенной ROI (`activeTab`) и сматчить с `Tab.RecognitionText` из конфига (нормализация регистра/пробелов; при необходимости — fuzzy / расстояние Левенштейна).

- Названия вкладок «чётко читаемы» → текстовый матч надёжнее числового OCR.
- В игре **9 именованных разделов**; источниками данных служат `Hero` (золото), `Status` (level/EXP/урон), `Portal` (акт/сложность/этап). `MainZone` (основная зона) видима всегда и вкладкой не является.
- Поля привязаны к источнику (`Source = MainZone | Tab`): на каждом кадре читаются только **доступные** поля — `MainZone` всегда + поля той вкладки, что сейчас активна (FR-002b).
- **Никакого автопереключения** (observe-only): значения вкладок обновляются оппортунистически, когда игрок сам открыл раздел. Если активная вкладка не определена достоверно — поля `Source=Tab` пропускаются, `MainZone`-поля продолжают читаться.

---

## 6. Хранилище

**Решение**: **SQLite через EF Core** (`Microsoft.EntityFrameworkCore.Sqlite`), файл — `%LOCALAPPDATA%\TBHStats\tbhstats.db`.

- Объём (тысячи `StageRun`) тривиален для SQLite; данные переживают перезапуск/перезагрузку (FR-011).
- **EF Core миграции** расширяют схему под новые механики **без потери истории** (FR-013/FR-021); LINQ-агрегации для средних/лучших/темпов (FR-008).
- Справочные сущности (`ChestType`, `HeroClass`, `Act`, `Difficulty`, `Tab`) — **данные, а не enum-в-коде**; история ссылается на их id → валидна после расширений.
- **Тесты на реальном временном SQLite-файле** (не in-memory-мок БД) — чтобы проверять реальную SQL-семантику.

Отклонены: LiteDB (слабее по миграциям/агрегации) и сырой JSON (нет индексов/конкурентной записи).

### DbContext и конфигурации (реализовано в T009)

`TbhStatsDbContext` в `src/TBHStats.Data/TbhStatsDbContext.cs`:
- Конструктор `(DbContextOptions<TbhStatsDbContext>)` для DI; строка подключения задаётся снаружи (T010).
- `ApplyConfigurationsFromAssembly` — все `IEntityTypeConfiguration<T>` применяются автоматически.
- `DbSet<>` для всех агрегатных корней: `ChestTypes`, `HeroClasses`, `Tabs`, `Acts`, `Difficulties`, `Stages`, `StageRuns`, `StageRunChests`, `MetricSamples`, `MetricSampleChests`, `StageAggregates`, `RoiCalibrations`, `WidgetSettings`, `OptimizationProfiles`.

Конфигурации в `src/TBHStats.Data/Entities/`:

| Файл | Ключевые правила маппинга |
|------|--------------------------|
| `ChestTypeConfiguration` | уникальный индекс по `Key` |
| `HeroClassConfiguration` | уникальный индекс по `Key` |
| `TabConfiguration` | уникальный индекс по `Key` |
| `ActConfiguration` | — |
| `DifficultyConfiguration` | уникальный индекс по `Key` |
| `StageConfiguration` | уникальный составной индекс `(ActId, DifficultyId, Number)` |
| `StageRunConfiguration` | `OwnsOne(Hero)` — owned `HeroSnapshot`; `Ignore(GoldPerHour/XpPerHour)` |
| `StageRunChestConfiguration` | составной PK `(StageRunId, ChestTypeId)` |
| `MetricSampleConfiguration` | `NextLocation` (`StageRef?`) → TEXT через `ValueConverter` (`«ActNumber/DifficultyKey/StageNumber»` или NULL) |
| `MetricSampleChestConfiguration` | составной PK `(MetricSampleId, ChestTypeId)` |
| `StageAggregateConfiguration` | PK = FK → Stage (1:1) |
| `StageAggregateChestRateConfiguration` | составной PK `(StageId, ChestTypeId)` |
| `RoiCalibrationConfiguration` | `Source` и `OcrEngine` как `int`; nullable FK → Tab |
| `WidgetSettingsConfiguration` | синглтон, shadow PK `Id`; `Theme` как `int` |
| `OptimizationProfileConfiguration` | синглтон, shadow PK `Id`; `SelectedMetric` как `int` |

**Особые решения маппинга:**

- `HeroSnapshot` — `sealed record` с guard-валидацией в `init`. Использован `OwnsOne` с явным `Property()`-маппингом каждого поля (`Hero_HeroClassId`, `Hero_Level`, `Hero_Damage`). EF материализует owned entity через reflection, минуя primary constructor — guard не срабатывает при чтении из БД.
- `StageRef?` — `readonly record struct` с guard-валидацией. Использован `HasConversion<StageRef?, string?>` (ValueConverter). Хранится в одной TEXT-колонке `NextLocation`. При NULL в колонке свойство остаётся `null`; при парсе вызывается конструктор с корректными значениями — guard отрабатывает штатно.
- `WidgetSettings` / `OptimizationProfile` — синглтоны без PK в доменной модели. Shadow PK `Id` (int, auto-increment) добавляется через `builder.Property<int>("Id").ValueGeneratedOnAdd()`.
- Все enum-поля (`FieldSource`, `OcrEngine`, `Theme`, `OptimizationMetric`) хранятся как `int` (явный `.HasConversion<int>()`).

### Миграции и bootstrap (реализовано в T010)

Файлы в `src/TBHStats.Data/`:

| Файл | Назначение |
|------|-----------|
| `DesignTimeDbContextFactory.cs` | `IDesignTimeDbContextFactory<TbhStatsDbContext>` — создаёт контекст с `:memory:` для `dotnet ef migrations add`; рантайм не использует |
| `Migrations/20260531133324_InitialCreate.cs` | Первичная миграция: создаёт все 15 таблиц, FK, уникальные индексы (в т.ч. `IX_Stages_ActId_DifficultyId_Number`) |
| `Migrations/TbhStatsDbContextModelSnapshot.cs` | Снимок модели EF Core для сравнения при `migrations add` |
| `DatabaseInitializer.cs` | Bootstrap-сервис; `static` класс с методами: `GetDbPath()`, `GetConnectionString(dbPath)`, `ConfigureSqlite(optionsBuilder, dbPath)`, `InitializeAsync(db, ct)` |

**Путь к БД**: `DatabaseInitializer.GetDbPath()` возвращает `%LOCALAPPDATA%\TBHStats\tbhstats.db`; директория создаётся при первом вызове.

**`InitializeAsync` порядок операций**:
1. `db.Database.MigrateAsync(ct)` — применить все ожидающие миграции.
2. Идемпотентный сидинг справочников (проверка `AnyAsync()` перед вставкой): `ChestTypes` (3), `Tabs` (9), `Acts` (3), `Difficulties` (2), `Stages` (60 = 3×2×10), `HeroClasses` (пустой по умолчанию — открываются динамически).
3. Сидинг `RoiCalibrations` (14 дефолтных Field Source Bindings из `GameMechanicsConfig.CreateDefault()` с нулевыми координатами — пользователь калибрует через UI).
4. Сидинг синглтонов `WidgetSettings` и `OptimizationProfile` (если отсутствуют).

**Composition root** (TBHStats.App) регистрирует контекст так:
```csharp
string dbPath = DatabaseInitializer.GetDbPath();
services.AddDbContext<TbhStatsDbContext>(opt =>
    DatabaseInitializer.ConfigureSqlite(opt, dbPath));
// При старте:
await DatabaseInitializer.InitializeAsync(db, ct);
```

---

## 7. Графики

**Решение**: **LiveCharts2** (`LiveChartsCore.SkiaSharpView.WinUI`).

- Официальная поддержка WinUI 3, единственная зависимость — SkiaSharp; качественные анимации и интерактивные тултипы по точкам (сценарий US3).
- Один API на все .NET-UI → переиспользуем при Android-клиенте на MAUI.

**Альтернатива**: **ScottPlot** — быстр на плотных данных; держим как замену при проблемах со Skia-рендером в оверлее.

---

## 8. Структура решения

Мультипроектное .NET-решение с чистым разделением границ спеки (захват / вычисление / хранение / отображение / расширяемость):

```text
TBHStats.sln
src/
├── TBHStats.Core/      Домен: модели, оптимизация/ранжирование,
│                       game-mechanics config, парсинг чисел.
│                       БЕЗ UI и WinRT → переиспользуемо в MAUI.
│   ├── Models/  Optimization/  Mechanics/  Parsing/
│
├── TBHStats.Capture/   Windows-only: WGC по HWND, цикл захвата,
│                       ROI-маппинг, OCR-обёртка, детекция вкладки,
│                       confidence-фильтр, состояние «ожидание».
│   ├── WindowTracking/  Wgc/  Ocr/  Tabs/  Roi/
│
├── TBHStats.Data/      EF Core, SQLite, репозитории, миграции, агрегаты.
│   ├── Entities/  Repositories/  Migrations/
│
├── TBHStats.App/       WinUI 3 виджет: оверлей-оболочка, MVVM,
│                       живые показатели, экран сравнения, графики,
│                       калибровка. Сервис-композиция петли.
│   ├── Views/  ViewModels/  Services/
│
└── TBHStats.Remote/    FUTURE — локальный API для Android.
                        В v1 пустой задел (границы/DTO), сервер не поднимается.
tests/
├── TBHStats.Core.Tests/      оптимизация, парсинг, конфиг механик
├── TBHStats.Capture.Tests/   ROI-маппинг, confidence-фильтр, OCR на фикстурах-скриншотах
├── TBHStats.Data.Tests/      репозитории/агрегаты на реальном временном SQLite (без моков БД)
└── TBHStats.UiTests/         E2E-аудит ЖИВОЙ игры: FlaUI + визуальная локализация (OCR, переиспользуя Capture)
                              + SendInput (human-like), Safety-Guard, сценарии ui-test-scenarios.md.
                              НЕ Playwright (браузерный). Не входит в поставку (carve-out конституции v2.2.0).
```

Ключ к архитектуре: **`Core` без платформенных зависимостей** — точка переиспользования в MAUI; Windows-специфика изолирована в `Capture`; оверлей-оболочка спрятана за тонким интерфейсом окна-виджета в `App/Views`, поэтому смена WinUI ↔ WPF не затрагивает Core/Capture/Data.

---

## 9. Поток данных и оркестрация

Фоновая петля (`IStatsOrchestrator`, FR-004) с интервалом опроса ~1.5 c:

```text
┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│   Capture    │   │     OCR      │   │    Domain    │   │   Data / UI  │
│  (WGC/HWND)  │   │  + parsing   │   │  (compute)   │   │ (EF Core/VM) │
└──────┬───────┘   └──────┬───────┘   └──────┬───────┘   └──────┬───────┘
       │ кадр (или        │                  │                  │
       │ «ожидание»)      │                  │                  │
       ▼                  │                  │                  │
 найти окно ──► детекция активной вкладки     │                  │
       │                  ▼                  │                  │
       │          извлечь ДОСТУПНЫЕ поля      │                  │
       │          (MainZone + активн. вкладка)│                  │
       │                  ▼                  │                  │
       │          парсинг K/M/B, время        │                  │
       │                  ▼                  │                  │
       │          sanity / confidence фильтр  │                  │
       │                  ▼                  │                  │
       │          надёжные MetricSample ──────► вычисление темпов │
       │                                      (золото/опыт/      │
       │                                       сундуки в час)     │
       │                                      ▼                  │
       │                                 биндинг в виджет ───────► живые показатели (P1)
       │                                      │                  │
 завершение этапа (прогрессбар + босс) ────────► закрыть StageRun ─► AddRun → Recompute (P2)
                                              │                  │
 экран сравнения ──────────────────────────────► ранжирование + ─► рекомендация (P2)
                                            рекомендация
```

Подробно по шагам:

1. `tracker.FindGameWindow()` → нет окна → `NotFound`.
2. `session.TryGetFrameAsync()` → `Waiting` (свёрнуто/закрыто) → показать последние достоверные, ждать (FR-005).
3. `tabDetector.DetectActiveTabAsync()` → активная вкладка; `extractor.ExtractAsync(frame, rois, activeTab)` читает только доступные поля (FR-002b).
4. `parser` (сокращённые числа K/M/B, время этапа) → **sanity / confidence фильтр** (R4) → надёжные `MetricSample` (FR-005a).
5. `metrics.ComputeLiveRates()` по интервалам **между надёжными точками** (периоды недоступности окна не считаются «нулевой добычей») → биндинг в виджет.
6. Завершение этапа (прогрессбар + появление/убийство **босса этапа** в MainZone) → собрать `StageRun` → `runRepo.AddRunAsync` → `aggRepo.RecomputeForStageAsync`.
7. Экран сравнения → `aggRepo.GetAllAsync` + `optimization.RankStages / RecommendBestStage`.

**Тонкости домена** (R4):
- **EXP** показывается в пределах уровня и обнуляется при level-up: прирост считается с учётом `HeroLevel` и `XpToLevel` (добор до полного предыдущего уровня + текущий EXP), а не как убыль.
- **Сундуки** в MainZone — транзиентные «точки»: растут при выпадении, падают к 0 при открытии. «Получено за забег» = сумма положительных дельт; обнуление = открытие, не потеря. Накопленный итог забега неубывает.
- Босс этапа ↔ шанс синего сундука; босс акта (этап «-10») ↔ шанс красного; коричневый — с любого монстра.

---

## 10. Машина состояний захвата

```text
NotFound ──(найдено окно TBH)──────────────▶ Capturing
Capturing ──(окно свёрнуто/закрыто)────────▶ Waiting     (НЕ ошибка; показываем последние достоверные)
Waiting ──(окно снова видимо)──────────────▶ Capturing   (авто-возобновление ≤5 c, SC-008)
Capturing ──(низкая уверенность OCR)───────▶ Capturing   (точку отбрасываем, без записи; FR-005/010)
любое ──(процесс игры завершился)──────────▶ NotFound
```

**Ключевой инвариант**: **перекрытое окно остаётся в `Capturing`** — WGC захватывает его из фона (см. §2). `Waiting` возникает только для свёрнутого/закрытого окна.

---

## 11. Ключевые внутренние контракты

«Контракты» v1 — это **внутренние C#-интерфейсы (швы между слоями)**, а не HTTP API (полные сигнатуры — в `contracts/services.md`):

**`TBHStats.Capture`**
- `IGameWindowTracker` — поиск/отслеживание окна, размер клиентской области (FR-001, FR-005b).
  - Реализация: `GameWindowTracker` (Win32 EnumWindows + user32.dll P/Invoke). Конфигурируется через `GameWindowTrackerOptions.WindowTitleHints` (case-insensitive Contains, дефолты: `"Task Bar Hero"`, `"TaskBarHero"`, `"TBH"`).
  - Вспомогательные типы: `GameWindowHandle` (HWND + PID + заголовок), `SizePx` (ширина × высота клиентской области).
  - `GetVisibility`: `!IsWindow` → `Closed`; `IsIconic` → `Minimized`; иначе → `Visible`. Перекрытие НЕ влияет на статус.
  - `GetClientSize`: `GetClientRect` → `SizePx`; невалидный HWND → `SizePx.Empty` (0×0), без исключения.
- `ICaptureSession` / `CaptureSession` — кадры окна и `CaptureState` (FR-005). Возвращает `CapturedFrame?` — общий тип кадра (`SoftwareBitmap` + `SizePx` + timestamp), потребляемый OCR/детектором/экстрактором. Реализует `IAsyncDisposable`. Файлы: `Wgc/ICaptureSession.cs`, `Wgc/CaptureSession.cs`, `Wgc/Direct3D11Interop.cs`, `Wgc/GraphicsCaptureItemInterop.cs`.
- `IOcrReader` / `OcrReader` — распознавание значения из нормализованной ROI (FR-002/003). Кроп через `BitmapEncoder/Decoder + BitmapBounds`; confidence = геометрическое покрытие слов в ROI (см. §3).
- `ITabDetector` — распознавание активной вкладки (FR-002a).
- `IFieldExtractor` — кадр → набор доступных сырых значений по активной вкладке (FR-002b).

**`TBHStats.Core`**
- `IValueParser` / `ValueParser` — сокращённые числа K/M/B/T, время этапа («SS»/«MM:SS»/«H:MM:SS»), идентификатор этапа (R4). Реализован в `TBHStats.Core/Parsing/`; без статического состояния, `CultureInfo.InvariantCulture`, `decimal`-арифметика для точных множителей.
- `IMetricsCalculator` — темпы по надёжным интервалам (FR-006, FR-005a).
- `IOptimizationService` — ранжирование и рекомендация этапа (FR-008/009/017/019).
- `IGameMechanics` — доступ и перезагрузка `GameMechanicsConfig` (FR-021).

**`TBHStats.Data`**
- `IRunRepository` — забеги и сэмплы (FR-007).
- `IStageAggregateRepository` — агрегаты этапов, пересчёт (FR-008).
- `ISettingsRepository` — настройки виджета, профиль оптимизации, калибровки ROI (FR-016/003).

---

## 12. Расширяемость

**Декларативный `GameMechanicsConfig`** (`TBHStats.Core/Mechanics`) описывает типы сундуков, классы героев, акты/сложности/этапы, вкладки и привязки полей к источникам. Встроенный default (`GameMechanicsConfig.CreateDefault()`) + переопределение из файла/БД, сидирует справочные таблицы.

Состав дефолтного сида:
- **ChestTypes** (3): brown «Базовый», blue «Редкий», red «Легендарный».
- **Tabs** (9): hero, stash, status, runes, cube, portal, settings, tradeship, mailbox (IsDataSource: hero/status/portal).
- **Acts** (3) × **Difficulties** (2: normal/nightmare) × **Stages** (10) = **60** этапов.
- **HeroClasses** (пустой по умолчанию — классы открываются динамически и добавляются через `Reload`).
- **FieldSourceBindings** (14): gold→hero-tab, xp/xpToLevel/heroLevel/heroDamage/heroClass→status-tab, stageId→portal-tab, остальные→MainZone.

`FieldSourceBinding` (record: FieldKey, Source, TabId?) связывает поле с источником (FR-002b) — используется `IFieldExtractor` для фильтрации по активной вкладке.
`IGameMechanics` / `GameMechanics` — контракт и реализация доступа к конфигу (FR-021): `Current` + `Reload(cfg)` с guard на null.

- Новый элемент (4-й тип сундука, новый класс, новая вкладка) = запись в конфиге + ROI в калибровке, **без правки доменных типов и схемы БД**.
- `ChestType` / `HeroClass` / `Tab` — справочные сущности-данные, а не enum'ы-в-коде.
- **История остаётся валидной**: записи ссылаются на стабильные id справочников, а не на захардкоженные значения (FR-013/FR-021, SC-010).

---

## 13. Задел под Android

- **`TBHStats.Core` без UI/WinRT-зависимостей** → переиспользуется в **.NET MAUI** (Android-клиент делит .NET/C# и доменное ядро).
- **`TBHStats.Remote`** (FUTURE) — локальный **ASP.NET minimal API** на `localhost`/LAN с **парным токеном** (bearer); черновик контракта — `contracts/remote-api.openapi.yaml` (эндпоинты `/api/live`, `/api/stages`, `/api/recommendation`, `/api/stages/{...}/samples`).
- В **v1 не реализуется**: создаются только границы (интерфейсы репозиториев / DTO), сервер не поднимается. Модель данных уже проектируется пригодной к удалённой выдаче.

---

## 14. Поставка

- **Целевой TFM**: `net8.0-windows10.0.22621.0` (WinRT-проекции через CsWinRT / Windows SDK).
- **Предпочтительно MSIX** (packaged) — надёжный доступ к WinRT (WGC/OCR), идентичность приложения, автообновления.
- **Unpackaged / self-contained** (один `.exe`) — опционально для «portable»-сборки; требует установленного .NET 8 desktop runtime и тщательной проверки WinRT-вызовов.

**Целевые показатели**: кадр + OCR одной ROI < ~150 мс; живые темпы видны ≤10 c после старта (SC-001); возобновление после перекрытия ≤5 c (SC-008); низкая idle-нагрузка CPU (захват по требованию, не непрерывный видеопоток).

---

## 15. Соответствие конституции

Оценка против `constitution.md` **v2.2.0** — **PASS по всем принципам**:

| Принцип | Статус | Комментарий |
|---------|--------|-------------|
| I. Context-First | PASS | Spec + research до реализации. |
| II. Single Source of Truth | PASS | Модели/enum'ы/механики — в `Core`, импортируются остальными. |
| III. Library-First | PASS | Захват/OCR/БД/графики — платформенные API и зрелые библиотеки; кастом только для оптимизации и ROI-маппинга. |
| IV. Code Reuse / DRY | PASS | `Core` переиспользуется App'ом и будущим MAUI. |
| **V. Strict Type Safety (NON-NEG)** | PASS | **Перенацелен с TypeScript на C#/.NET** (с согласия пользователя 2026-05-31): nullable refs on, `dynamic` запрещён, analyzers clean, build обязателен до коммита. |
| VI. Atomic Task Execution | PASS | Разбивка на атомарные задачи на этапе `/speckit.tasks`. |
| VII. Quality Gates (NON-NEG) | PASS | `dotnet build` + тесты Core/Data до коммита; хардкод-секретов нет. |
| VIII. Progressive Specification | PASS | spec → plan → tasks → implement. |
| IX. Error Handling | PASS | Типизированные ошибки; «окно недоступно» = ожидание, не throw. |
| X. Observability | PASS | Структурное логирование, метрики уверенности OCR; без секретов в логах. |
| XI. Accessibility (RECOMMENDED) | PASS | Клавиатура, контраст, Light/Dark, тип сундука не только цветом. |

**Нарушений, требующих обоснования, нет.** Прежний конфликт «конституция требовала TypeScript» снят легитимной поправкой конституции (v2.0.0 → v2.1.0), а не обходом гейта.
