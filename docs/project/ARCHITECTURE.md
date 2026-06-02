# Архитектура TBHStats

**Проект**: TBHStats — десктоп-помощник по статистике для игры Task Bar Hero
**Платформа**: Windows 11 (x64/arm64), один локальный пользователь
**Дата актуализации**: 2026-06-02 (T068 Phase 5 завершён — рестайл CompareHostWindow/CompareView под игровой скин: borderless title bar, drag-header «STAGE COMPARISON», GameHeaderBorderStyle + OutlinedTextBlock, GamePanelBorderStyle для плашки рекомендации, GameHeaderBorderStyle для шапки таблицы, GameBodyFont/GameValueBrush для строк; персист геометрии «compare» через WindowPlacement/ISettingsRepository (OnFirstActivated + debounce 500 мс); UI переведён на английский (CompareView.xaml: 0 кириллицы, CompareViewModel.cs: статус/суффиксы/логи); §7 обновлён — добавлен раздел «Окно сравнения этапов»; ранее: персист геометрии окон: `WindowPlacement` (Core), `WindowPlacementConfiguration` (Data/Entities), `DbSet<WindowPlacement>` в TbhStatsDbContext, методы `GetWindowPlacementAsync`/`SaveWindowPlacementAsync` в `ISettingsRepository`/`SettingsRepository`, миграция `20260602174938_AddWindowPlacement`; §6 + data-model.md обновлены; ранее T068 Phase 4 — доработки дизайна виджета: DotGothic16 (заголовки) + VT323 (цифры) вместо Pixelify Sans; title bar скрыт (OverlappedPresenter.SetBorderAndTitleBar + Win32 drag); заголовок «TBHSTATS» крупный/широкий + кнопка ✕; OutlinedTextBlock (8-слойная окантовка); тёмная панель статов #3D2B1A; GameProgressBarStyle зелёный; авто-закрытие CompareHostWindow/CalibrationHostWindow при закрытии виджета; §7 обновлён; ранее T068 Phase 2 — игровой скин виджета: `GameWidgetStyles.xaml` (Pixelify Sans, палитра STATUS TBH), заголовок-плашка, пергаментная панель статов; чекбокс «Поверх окон» (FR-015, AlwaysOnTop восстановлен из WidgetSettings); кнопка «Скрыть/Вернуть игру» через `IGameWindowController`; DI-регистрация `IGameWindowController`→`GameWindowController`; §7 обновлён; ADR-026 добавлен; ранее T068 Phase 1A — `IGameWindowController`/`GameWindowController` в `WindowTracking/`: увод окна за экран через `SetWindowPos` без SW_MINIMIZE/SW_HIDE — WGC-захват не прерывается; §11 обновлён; ранее: AddAvgGainedToStageAggregate — 4 поля AvgGoldGained/AvgXpGained/Recent* в StageAggregate; миграция 20260602131050; §6 обновлён; T064 / ADR-025 — сегментно-управляемая запись забегов: триггер `StageRun` перенесён из `RunRecorder` в `StatsOrchestrator` (граница сегмента по падению прогресса после босса); gold/xp/сундуки накапливаются по сегменту; гейтинг записи по распознанному из свежего `nextLocation` этапу (иначе «—» в строке «Этап»); `RunRecorder` → тонкий `PersistSegmentRunAsync`; ретенция ≤10 забегов на этап (`PruneOldRunsAsync`, схема БД без изменений); `LiveStatsSnapshot.LastCompletedStageGold/Xp` (золото/опыт за предыдущий этап в скобках виджета); из виджета убрана кнопка «Графики» (Alt+G); в таблице сравнения номер этапа/сложность разделены (`StageNumberLabel`/`DifficultyLabel`), колонки сложности и «Сила» скрыты; §6/§7/§9/§11 обновлены; ранее T066 self-heal — `DatabaseInitializer`: сидинг `nextLocation`-ROI с `ParseHint="binarize_white"` + self-heal существующих БД без координат; §6 обновлён; ранее: T065 — бинаризация белого текста для nextLocation: `OcrReader.BinarizeWhite(threshold=160)` + `MinOcrDimensionBinarized=192`, активируется через `ParseHint="binarize_white"`; §3 обновлён; ранее: NextLocationStabilizer: оконное голосование ≥confirmCount из windowSize (дефолт 2 из 5) вместо debounce N подряд — устойчив к пропускам и флакированию; _lastKnownStage обновляется вне IsReliable-гейта (MainZone видна всегда) — §9 обновлён; ранее: NextLocationStabilizer: debounce 2 кадра + sanity по ResolveStageId против OCR-шума от фонового огня на 3-6..3-10 — §9 обновлён; ранее: FK-фикс HeroClasses: `GameMechanicsConfig.CreateDefault()` теперь содержит дефолтный класс `Id=1, Key="unknown"` — FK-якорь для `StageRun.Hero.HeroClassId`; `RunRecorder.ResolveHeroClassId` не помечает забег `IsPartial=true` при нераспознанном классе; §12 + §6 обновлены; ранее T063 Phase 2 fix — сегментный таймер синхронизирован с прогрессом: сброс по падению StageProgress вместо смены _lastKnownStage; LastCompletedStageSeconds в LiveStatsSnapshot; BuildStageElapsedText в LiveStatsViewModel; ToolTip в WidgetWindow.xaml; T063 Phase 3B — FK-выравнивание config↔БД verified + ResolverFkAlignmentTests; T063 Phase 3 — резолв StageId из NextLocation.Previous() через GameMechanicsConfig.ResolveStageId в StatsOrchestrator; сброс RunRecorder при NotFound с guard-флагом _windowLostHandled; GameMechanicsConfig.ResolveStageId (config-driven, ADR-009); тест GameMechanicsConfigResolveStageIdTests; ранее T063 Phase 2 — сегментный таймер этапа `StatsOrchestrator`, `StageProgress`/`BossPresent`/`StageElapsedSeconds` в `LiveStatsSnapshot`, строки «Прогресс этапа» + ProgressBar и «Время этапа» в виджете; ранее T063 — ADR-024: визуальный детектор прогресса этапа `IStageProgressReader`/`StageProgressReader` в `TBHStats.Capture/Progress/`; цветовой анализ фиолетовый/синий; интеграция в `FieldExtractor` (5-й параметр); §3 обновлён; ранее: Issue E — CalibrationViewModel: поддержка проверки chestZone-ROI через IChestZoneAnalyzer: показывает все найденные плашки + число точек; ранее: ADR-023 — зонный детектор плашек `IChestZoneAnalyzer`/`ChestZoneAnalyzer`: одна ROI `chestZone` охватывает всю группу плашек; горизонтальная сегментация по цвету колонок; счёт точек по отфильтрованным строкам + агрегированный профиль + run-алгоритм; приоритетный путь в `FieldExtractor`; per-ROI `IChestPanelAnalyzer`-путь сохранён как fallback; §4/§11 + Capture.Tests: 2 новых теста на chests.jpg/main.jpg; ранее: Issue C — проверка chest-ROI в калибровке: `TestSelectedRoiOcr` для FieldKey `chest:*` вызывает `IChestPanelAnalyzer` вместо OCR, показывает тип + число точек, §7; ADR-022 — тип сундука по цвету плашки `IChestPanelAnalyzer`, фикс фантомных значений, FieldExtractor убирает IChestLayoutResolver из пайплайна, §3/§4/§11; T062 — визуальный детектор точек сундуков `IChestDotCounter`/`ChestDotCounter` в `TBHStats.Capture/Chests/`: chest-ROI больше не вызывают OCR, счёт точек по яркости пикселей (run-ы тёмных колонок), §3/§4 + ADR-021; ранее: золото — расходуемый баланс, валидатор не гейтит sanity по убыванию золота, §9; ранее: короткое скользящее окно живых темпов `LiveRateWindowSeconds=90`: `ComputeLiveRates` теперь получает срез ≤90 с вместо всего буфера — быстрая сходимость и отзывчивость к смене этапа, §9; ранее: детект выброса ставки опыт/ч: `RateOutlierDetector` в Core (`raw > max(EMA×6, 5M)` → отброс кадра + сброс буфера без обнуления EMA) + проводка в `StatsOrchestrator` с guard `buffer.Count > 1`, §9; ранее: надёжный детектор смены героя: `HeroSwitchDetector` в Core (падение уровня + смена XpToLevel без level-up-сигнатуры) + диаг-лог спайков XpPerHour в `StatsOrchestrator`, §9; ранее: фикс OCR: паддинг мелких кропов перед апскейлом — `OcrPaddingPixels=6` при `min(w,h) < PaddingThreshold=40`, предотвращает misread запятой при агрессивном апскейле Fant, §3; ранее: сброс окна живых темпов при смене класса героя: `StatsOrchestrator.ResolveHeroClassKey` + belt-and-suspenders сброс `_reliableBuffer`/EMA при смене нормализованного ключа, §9; ранее: мульти-панельный UI — снятие tab-gating, чтение всех полей каждый кадр, апскейл мелких кропов OCR, порог уверенности 0.02, перезагрузка ROI каждую итерацию, кэш-кроп без перекодирования кадра, запятая=десятичная, «Этап»=nextLocation−1, сундуки-точки графические → счёт по изображению P2, отключён always-on-top, виджет-строка «До уровня», EMA-сглаживание темпов + guard level-up/невозможного xp + guard разрыва XP (смена героя / потолок уровня) — §3/§5/§9 + ADR-019; мульти-позиционные ROI сундуков по числу типов `N` и детекция раскладки `IChestLayoutResolver`, §4/§9 + ADR-018; объединённая зона опыта `xpPair` с парсингом по `/`, §3/§9; структурный guard по уровню героя: разрыв детектируется по `levelDelta != 0 && levelDelta != 1`, ловит смену героя в late-game независимо от магнитуды XP-дельты, §9; ранее: T008/T015/T014/T013/T009/T010/T022/T023/T024/T025/T026/T028/T035–T042 US2 recency-aware; T043–T046 US3 тренды/ретенция; T047 обработка ошибок и логирование; T048 конфигурация поставки MSIX/unpackaged; T053 accessibility-проход; фикс старта виджета и захвата: XAML-кисти WinUI 3, static-init `GameWindowTrackerOptions`, CsWinRT-маршалинг WGC/D3D11 interop, подсказки заголовка без `"TBH"`; калибровка ROI: живой кадр через общий ICaptureSession + ScrollViewer-зум/панорамирование + рисование рамки мышью по пикселям кадра)
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
  - `Direct3D11Interop.CreateDevice()`: `D3D11CreateDevice` (d3d11.dll, `D3D11_DRIVER_TYPE_HARDWARE`, флаг `BGRA_SUPPORT`) → QI до `IDXGIDevice` → `CreateDirect3D11DeviceFromDXGIDevice` → WinRT-обёртка `IDirect3DDevice`. **Маршалинг ABI→проекция: `WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(ptr)`** (НЕ `Marshal.GetObjectForIUnknown` и НЕ `MarshalInspectable<object>` — те дают generic `__ComObject`, который CsWinRT не может повторно замаршалить в нативный `IDirect3DDevice` при передаче во framePool: «Failed to create a CCW for `__ComObject`»).
  - `GraphicsCaptureItemInterop.CreateForWindow(hwnd)`: `RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem")` → QI до `IGraphicsCaptureItemInterop` (GUID `3628E81B-...`) → `CreateForWindow` (возвращает **ABI-указатель `out IntPtr`**) → **`GraphicsCaptureItem.FromAbi(ptr)`** + `Marshal.Release`. **`RoGetActivationFactory` использует кастомный `HStringMarshaler : ICustomMarshaler`** вместо `UnmanagedType.HString` (встроенная поддержка HSTRING-маршалинга удалена в .NET 5+; прямое `[MarshalAs(UnmanagedType.HString)]` даёт `MarshalDirectiveException`).
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
- **Объединённые зоны со встроенным разделителем.** Опыт показывается строкой «текущий / до_уровня» (`5 530 764 / 6 266 704`). Вместо двух узких ROI (которые «съезжают» при росте разрядности) поддержан единый широкий ROI `xpPair`: OCR читает строку целиком, `ValueParser.TryParseXpPair` делит её по `/` и парсит обе половины существующим `TryParseAbbreviatedNumber` (пробелы-разряды и суффиксы K/M/B/T). `xpPair` перекрывает отдельные `xp`/`xpToLevel` (которые остаются fallback). См. §9 (поток) и GAME-FACTS.md §9.

**Эмпирический риск**: фактическая точность на конкретном шрифте Task Bar Hero — открытый вопрос, **проверяется на реальных скриншотах** (фикстуры в `TBHStats.Capture.Tests`). Митигация: confidence-порог + sanity-проверки значений + fallback-движок per ROI.

**Исключение из OCR — chest-поля (ADR-021, ADR-022, T062):** поля `chest:*` и `chest:*@N` **не используют OCR** — точки под иконками сундуков графические и не содержат символов. Тип сундука определяется по цвету фона плашки; счёт точек — анализ яркости пикселей. Реализовано через `IChestPanelAnalyzer`/`ChestDotCounter` (`TBHStats.Capture/Chests/`); подробнее в §4.

**Исключение из OCR — прогрессбар этапа (ADR-024, T063):** поле `stageProgress` **не использует OCR** — прогрессбар графический, без текста. Детекция основана на цвете заливки горизонтального бара в правом-нижнем углу MainZone: фиолетовый = заполненная часть пути, синий = бой с боссом, тёмный = пустой трек. Реализовано через `IStageProgressReader`/`StageProgressReader` (`TBHStats.Capture/Progress/`). FieldKey `bossPresent` отдельно не читается — boss извлекается из того же stageProgress-ROI. Подробнее — ниже.

**Визуальная детекция прогресса этапа (ADR-024, T063):**
- `IStageProgressReader` — `Task<StageProgressReading> ReadAsync(CapturedFrame, RoiCalibration, GameMechanicsConfig, CancellationToken)`.
- `StageProgressReading` (`readonly record struct`) — `(double? Progress, bool? BossPresent)`.
- **Алгоритм:** развернуть нормализованный ROI в пиксели (`RoiMapper.ToPixels`); получить Bgra8-буфер (кэш по `ReferenceEquals`, lock — идентичен паттерну `ChestZoneAnalyzer`). Для каждой вертикальной колонки ROI агрегировать строки: доля фиолетовых (R>110, B>150, B>G, R>G, B−G>30, R−G>20) и синих (B>150, G>130, B−R>25, G≥R) пикселей ≥ `MinColorRowFraction=0.40` → колонка классифицируется. `purpleColumns / blueColumns / darkColumns` суммируются по всему ROI.
- **Логика reading:** если `blueColumns/total ≥ BlueFractionThreshold=0.20` → BossPresent=true, Progress=1.0. Иначе: BossPresent=false, Progress = Clamp(purpleFraction × 0.95, 0.0, 0.95). Полностью фиолетовый бар → Progress=0.95 (впереди бой с боссом). Если ROI нечитаем (validColumns/total < 0.05) → Progress=null, BossPresent=null.
- **Цветовые якоря** (пиксельные константы рендера, NOT из GameMechanicsConfig):
  - Фиолетовый (путь): RGB ≈ (165, 77, 213); пороги: R>110, B>150, B>G, R>G, (B−G)>30, (R−G)>20.
  - Синий (босс): RGB ≈ (95, 199, 255); пороги: B>150, G>130, (B−R)>25, G≥R.
  - Выверены на 4 фикстурах: progress_begin/progress_half/progress_full/progress_stagebossfight.
- **FieldExtractor:** при FieldKey=`"stageProgress"` и `StageProgressDetectionEnabled=true` → вызывает `IStageProgressReader`; результат в `RawObservation.StageProgress` + `BossPresent` (confidence=1.0 при успехе). FieldKey `"bossPresent"` пропускается — данные получены из одного stageProgress-ROI.
- **Семантика null:** null = «не считано» (ROI нечитаем, кадр отсутствует); `StageCompletionDetector` корректно различает null / false / true.

**Реализация (T014):**
- `CapturedFrame` (sealed, IDisposable) — общий тип кадра слоя: `SoftwareBitmap Bitmap`, `SizePx ClientSize`, `DateTimeOffset TimestampUtc`. Возвращается `ICaptureSession`, потребляется OCR/детектором/экстрактором. `Dispose()` освобождает `Bitmap`.
- `OcrResult` (readonly record struct) — `(string RawText, double Confidence, bool Recognized)`.
- `IOcrReader` — `Task<OcrResult> ReadAsync(CapturedFrame, RoiCalibration, CancellationToken)`.
- `OcrReader` — реальная реализация:
  - Lazy-init `WinOcrEngine` (`TryCreateFromUserProfileLanguages` → fallback `TryCreateFromLanguage("en")`). Если оба null — возвращает `OcrResult("", 0, false)` без исключения.
  - Кроп `SoftwareBitmap` к ROI через `BitmapEncoder` (BMP, in-memory stream) + `BitmapDecoder` с `BitmapTransform.Bounds` — извлекает суб-регион без ручного попиксельного копирования. Конвертация к `Bgra8/Premultiplied` через `SoftwareBitmap.Convert` при необходимости.
  - **Предобработка мелких кропов (ADR-019):** Windows.Media.Ocr не распознаёт слишком маленькие изображения (мелкие поля gold/xp/heroLevel высотой 18–27px → пусто). Если меньшая сторона кропа < `MinOcrDimension=96` — апскейл целочисленным множителем (cap по `OcrEngine.MaxImageDimension`, интерполяция Fant) перед `RecognizeAsync`. Подтверждено на живой игре + Microsoft Q&A. Дополнительно: если `min(w,h) < PaddingThreshold=40` (очень тесный кроп, апскейл ×3+), перед апскейлом добавляется `OcrPaddingPixels=6` пикселей однотонного тёмного паддинга вокруг кропа — это предотвращает искажение граничных глифов (запятая, точка) при агрессивном апскейле Fant-интерполяцией (эмпирически подтверждено на overall_priest.jpg: без паддинга запятая «126,9» читается как «;»).
  - **Бинаризация белого текста (T065, `ParseHint="binarize_white"`):** для полей с белым пиксельным текстом на фоне ярких цветных боевых эффектов (nextLocation — мелкие цифры вроде «3-4» рядом с синим льдом и жёлтым уроном) Windows.Media.Ocr без предобработки возвращает пустой RawText даже после апскейла. Решение: если `RoiCalibration.ParseHint == "binarize_white"` — перед апскейлом применяется `OcrReader.BinarizeWhite(threshold=160)`: пиксели с яркостью `(R+G+B)/3 ≥ 160` → белые, остальные → чёрные. Это устраняет цветовые шумы (синий/жёлтый/зелёный) и оставляет только белый текст. Дополнительно применяется более агрессивный апскейл: целевой размер `MinOcrDimensionBinarized=192` вместо стандартного `96` — пиксельный шрифт TBH (~15px высота глифов) требует большего масштаба для надёжного распознавания. Эмпирически подтверждено на `3-3.jpg` (550×244): без бинаризации — Recognized=False для всех ROI; с бинаризацией ROI (0.00,0.20,0.15,0.50) → RawText=«3-4», TryParseNextLocation: act=3, stage=4.
  - **Confidence-эвристика**: `∑(wordBoundsArea) / imageArea` (площадь реального OCR-входа, т.е. после апскейла), clamp [0..1]. Windows.Media.Ocr не возвращает числовую confidence per-word, поэтому геометрическое покрытие служит слабым прокси; для коротких чисел значения низкие (0.08–0.48), поэтому порог приёма понижен до 0.02 (ADR-019), а основная валидация — парсер+sanity.
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

**Зонная локализация плашек (ADR-023, основной путь; supersedes ADR-018 и ADR-022 как приоритетный метод):**
Основной источник данных о сундуках — **одна зонная ROI `chestZone`** (FieldKey `"chestZone"`, Source=MainZone), охватывающая всю горизонтальную группу плашек. Реализован `IChestZoneAnalyzer`/`ChestZoneAnalyzer` (`TBHStats.Capture/Chests/`).

- **Алгоритм зонного анализатора:**
  1. Кроп зоны (Bgra8, кэш на кадр). Для каждой вертикальной колонки: медиана R/G/B ярких пикселей (luminance ≥ 120) → евклидово расстояние до якорей `ChestType.PanelColor` (толеранс 60). Сегмент = смежные колонки одного типа шириной ≥ MinPanelWidthFraction.
  2. Для каждого сегмента: нижний пояс (нижние 32% высоты зоны). Строки нижнего пояса фильтруются по `darkRowFraction ∈ [0.05, 0.45)` — исключаются светлый фон (< 0.05) и тёмная рамка/сцена (≥ 0.45). По отфильтрованным строкам строится агрегированный профиль тёмных пикселей по колонкам. Run-алгоритм: run ≥ minRunPx (масштабонезависимо: segW/5 × 0.15) и ≤ maxRunPx (35% segW) = одна точка.
  3. Результат: `Dictionary<ChestTypeId, dotCount>` для всех найденных плашек.
- `FieldExtractor`: при наличии ROI с FieldKey=`"chestZone"` — вызывает `IChestZoneAnalyzer.AnalyzeZoneAsync`; результат записывается в `Chests` напрямую (`confidence=1.0`). **Это приоритетный путь.**
- **Fallback (ADR-022)**: если `chestZone`-ROI не откалибрована — `FieldExtractor` использует per-ROI путь через `IChestPanelAnalyzer` (тип по цвету плашки ROI + DotStripFraction). Сохранён для обратной совместимости.
- `GameMechanicsConfig.CreateDefault()` содержит binding `"chestZone"` → Source=MainZone. Биндинги `chest:<тип>@N` сохранены как legacy.
- DI: `IChestZoneAnalyzer` → `ChestZoneAnalyzer` (singleton); `FieldExtractor` принимает оба анализатора.

**Цветовые якоря плашек** (из GameMechanicsConfig): red → orange (236,133,41), blue → lightblue (190,220,238), brown → white (255,255,255). Расстояния между якорями: orange↔white≈190, lightblue↔white≈75, orange↔lightblue≈210. Толеранс 60 надёжно разделяет все три типа.

**Идентификация типа по цвету (ADR-022, legacy per-ROI путь):** `IChestPanelAnalyzer`/`ChestDotCounter` — резервный путь при отсутствии `chestZone`-ROI. Per-ROI chest-ROI обязан накрывать **всю плашку** (не только полосу точек). `IChestDotCounter.CountFilledDotsAsync` и инфраструктура `chest:<тип>@N`-ключей сохранены для обратной совместимости.

**Требование к калибровке**: для зонного пути — одна ROI `chestZone` охватывает всю горизонтальную группу плашек. Для per-ROI пути — ROI на каждую плашку целиком. Тип сундука определяется из цвета пикселей, независимо от FieldKey ROI.

---

## 5. Детекция активной вкладки

**Решение**: распознать **название активной вкладки** через тот же OCR в выделенной ROI (`activeTab`) и сматчить с `Tab.RecognitionText` из конфига (нормализация регистра/пробелов; при необходимости — fuzzy / расстояние Левенштейна).

- Названия вкладок «чётко читаемы» → текстовый матч надёжнее числового OCR.
- В игре **9 именованных разделов**; источниками данных служат `Hero` (золото), `Status` (level/EXP/урон), `Portal` (акт/сложность/этап). `MainZone` (основная зона) видима всегда и вкладкой не является.
- **Обновление (ADR-019, 2026-06-01):** на живой игре подтверждено, что разделы открыты **одновременно** (3 слота над MainZone + оверлей Rune — GAME-FACTS §2). Прежний gating по «единственной активной вкладке» (FR-002b) к этому UI **не применим и снят**: `FieldExtractor` читает **все** калиброванные поля каждый кадр с их фиксированных позиций, независимо от `activeTab`. Валидация — парсером (значение обязано распарситься) и sanity-проверками; закрытый раздел/чужой слот не даёт валидного значения → поле сохраняет прежнее. Детекция активной вкладки (`ITabDetector`/`activeTab`) для чтения полей не используется (поле `RawObservation.ActiveTab` — информационное).
- **Никакого автопереключения** (observe-only): значения обновляются оппортунистически, когда нужный раздел открыт игроком в своём слоте.
- *(Историческое)* Описание ниже про текстовую детекцию активной вкладки сохранено как референс реализации `ITabDetector`, но в live-петле она больше не гейтит поля.

**Реализация (T022):**
- `ITabNameMatcher` — чистый шов (без WGC/OCR-зависимостей): `TabRef? Match(string recognizedText, GameMechanicsConfig cfg, double minSimilarity = 0.6)`.
- `TabNameMatcher` — алгоритм Вагнера–Фишера (итеративный O(n·m), оптимизация на два ряда):
  1. Нормализация: `Trim()` + `ToLowerInvariant()` + regex `\s+` → `" "` (схлопывание пробелов).
  2. Похожесть = `1.0 − Lev(norm1, norm2) / Max(len1, len2)`.
  3. Перебор всех `Tab.IsActive == true`; выбирается максимум (при равных — первый по порядку).
  4. `recognizedText` пустой/whitespace → немедленно `null`; похожесть ниже `minSimilarity` → `null`.
  5. Возвращает `new TabRef(tab.Id, tab.Key, similarity)`.
- `ITabDetector` — `Task<TabRef?> DetectActiveTabAsync(CapturedFrame, RoiCalibration activeTabRoi, GameMechanicsConfig, CancellationToken)`. ROI `activeTab` передаётся вызывающим оркестратором (T026), что позволяет детектору не знать об общем наборе ROI.
- `TabDetector(IOcrReader, ITabNameMatcher)` — зависимость от `IRoiMapper` убрана (OCR.ReadAsync сам принимает `RoiCalibration`). При `OcrResult.Recognized == false` → немедленно `null` (не ошибка, FR-005).

---

## 6. Хранилище

**Решение**: **SQLite через EF Core** (`Microsoft.EntityFrameworkCore.Sqlite`), файл — `%LOCALAPPDATA%\TBHStats\tbhstats.db`.

- Объём (тысячи `StageRun`) тривиален для SQLite; данные переживают перезапуск/перезагрузку (FR-011).
- **EF Core миграции** расширяют схему под новые механики **без потери истории** (FR-013/FR-021); LINQ-агрегации для средних/лучших/темпов (FR-008).
- Справочные сущности (`ChestType`, `HeroClass`, `Act`, `Difficulty`, `Tab`) — **данные, а не enum-в-коде**; история ссылается на их id → валидна после расширений.
- **Тесты на реальном временном SQLite-файле** (не in-memory-мок БД) — чтобы проверять реальную SQL-семантику.
- **Ретенция/прореживание сэмплов (US3, T044)**: `IRunRepository.PruneSamplesAsync(stageId, olderThanUtc, ct)` удаляет `MetricSample` этапа со временем строго раньше cutoff (двухшаговый bulk `ExecuteDeleteAsync`: сначала зависимые `MetricSampleChest`, затем сами сэмплы — SQLite без `PRAGMA foreign_keys=ON` не каскадирует FK при bulk-delete). `StageRun`/`StageAggregate` при этом НЕ затрагиваются (агрегаты сохраняются как материализованный кэш). Выборка для трендов — `GetSamplesAsync(stageId, DateRange, ct)` (диапазон включительный, сортировка по `TakenAtUtc`).
- **Ретенция забегов: не более 10 последних на этап (ADR-025)**: `IRunRepository.PruneOldRunsAsync(stageId, keepLast, ct)` физически удаляет все `StageRun` этапа сверх `keepLast` самых свежих (порядок `CompletedAtUtc DESC, Id DESC` — детерминированный tie-break). Вызывается из `RunRecorder.PersistSegmentRunAsync` **сразу после записи нового забега** и **до** `RecomputeForStageAsync` → агрегат считается по ≤10 свежим забегам. Константа `RunRecorder.MaxRunsPerStage=10`. **Схема БД при этом НЕ менялась** (новой миграции нет — только bulk-delete). Это держит таблицу `StageRuns` компактной и фокусирует рекомендации на актуальной силе отряда (дополняет recency-aware агрегацию, §7).

Отклонены: LiteDB (слабее по миграциям/агрегации) и сырой JSON (нет индексов/конкурентной записи).

### DbContext и конфигурации (реализовано в T009)

`TbhStatsDbContext` в `src/TBHStats.Data/TbhStatsDbContext.cs`:
- Конструктор `(DbContextOptions<TbhStatsDbContext>)` для DI; строка подключения задаётся снаружи (T010).
- `ApplyConfigurationsFromAssembly` — все `IEntityTypeConfiguration<T>` применяются автоматически.
- `DbSet<>` для всех агрегатных корней: `ChestTypes`, `HeroClasses`, `Tabs`, `Acts`, `Difficulties`, `Stages`, `StageRuns`, `StageRunChests`, `MetricSamples`, `MetricSampleChests`, `StageAggregates`, `RoiCalibrations`, `WidgetSettings`, `OptimizationProfiles`, `WindowPlacements`.

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
| `WindowPlacementConfiguration` | реальный string PK `WindowKey` (≤64 символа); PosX/PosY/Width/Height; upsert по ключу |

**Особые решения маппинга:**

- `HeroSnapshot` — `sealed record` с guard-валидацией в `init`. Использован `OwnsOne` с явным `Property()`-маппингом каждого поля (`Hero_HeroClassId`, `Hero_Level`, `Hero_Damage`). EF материализует owned entity через reflection, минуя primary constructor — guard не срабатывает при чтении из БД.
- `StageRef?` — `readonly record struct` с guard-валидацией. Использован `HasConversion<StageRef?, string?>` (ValueConverter). Хранится в одной TEXT-колонке `NextLocation`. При NULL в колонке свойство остаётся `null`; при парсе вызывается конструктор с корректными значениями — guard отрабатывает штатно.
- `WidgetSettings` / `OptimizationProfile` — синглтоны без PK в доменной модели. Shadow PK `Id` (int, auto-increment) добавляется через `builder.Property<int>("Id").ValueGeneratedOnAdd()`.
- `WindowPlacement` — реальный string PK `WindowKey` в доменной модели (required `init`). Upsert в `SettingsRepository`: `AnyAsync` → если существует: `Attach + State=Modified`, иначе `Add`. При этом EF материализует модель через reflection, минуя required-check (init-only свойства заполняются до возврата из `FirstOrDefaultAsync`).
- Все enum-поля (`FieldSource`, `OcrEngine`, `Theme`, `OptimizationMetric`) хранятся как `int` (явный `.HasConversion<int>()`).

### Миграции и bootstrap (реализовано в T010)

Файлы в `src/TBHStats.Data/`:

| Файл | Назначение |
|------|-----------|
| `DesignTimeDbContextFactory.cs` | `IDesignTimeDbContextFactory<TbhStatsDbContext>` — создаёт контекст с `:memory:` для `dotnet ef migrations add`; рантайм не использует |
| `Migrations/20260531133324_InitialCreate.cs` | Первичная миграция: создаёт все 15 таблиц, FK, уникальные индексы (в т.ч. `IX_Stages_ActId_DifficultyId_Number`) |
| `Migrations/20260531155141_AddRecencyAwareAggregation.cs` | Аддитивная миграция (US2, T038/S1b): recency-aware поля `StageAggregates` (Recent* + power-context `RecentHeroLevel/Damage Min/Max`), `StageAggregateChestRates.RecentRatePerHour`, `OptimizationProfiles.RecentWindowSize`/`Scope`. Только `AddColumn` — история не теряется (FR-013) |
| `Migrations/20260602131050_AddAvgGainedToStageAggregate.cs` | Аддитивная миграция: 4 новых столбца REAL NOT NULL DEFAULT 0 в `StageAggregates` — `AvgGoldGained`, `AvgXpGained`, `RecentAvgGoldGained`, `RecentAvgXpGained`. Среднее абсолютное золото/опыт за один забег (all-time и recent). Существующие строки агрегатов пересчитываются при следующей записи забега |
| `Migrations/20260602174938_AddWindowPlacement.cs` | Аддитивная миграция (FR-016, T068 Phase 5): создаёт таблицу `WindowPlacement` (PK = `WindowKey` TEXT ≤64, `PosX/PosY/Width/Height` REAL NOT NULL). Применяется автоматически через `Database.MigrateAsync` при следующем запуске — существующие пользователи получат таблицу без потери данных |
| `Migrations/TbhStatsDbContextModelSnapshot.cs` | Снимок модели EF Core для сравнения при `migrations add` |
| `DatabaseInitializer.cs` | Bootstrap-сервис; `static` класс с методами: `GetDbPath()`, `GetConnectionString(dbPath)`, `ConfigureSqlite(optionsBuilder, dbPath)`, `InitializeAsync(db, ct)` |

**Путь к БД**: `DatabaseInitializer.GetDbPath()` возвращает `%LOCALAPPDATA%\TBHStats\tbhstats.db`; директория создаётся при первом вызове.

**`InitializeAsync` порядок операций**:
1. `db.Database.MigrateAsync(ct)` — применить все ожидающие миграции.
2. Идемпотентный сидинг справочников (проверка `AnyAsync()` перед вставкой): `ChestTypes` (3), `Tabs` (9), `Acts` (3), `Difficulties` (2), `Stages` (60 = 3×2×10), `HeroClasses` (1 дефолтный «неопределённый» класс `Id=1` — FK-якорь; конкретные классы сидируются динамически при обновлении конфига). Self-heal: если таблица `HeroClasses` пуста на существующей БД (напр., старая установка до фикса), класс `Id=1` засеется при следующем запуске приложения — миграция схемы не требуется.
3. Сидинг `RoiCalibrations` (14 дефолтных Field Source Bindings из `GameMechanicsConfig.CreateDefault()` с нулевыми координатами — пользователь калибрует через UI). Поле `nextLocation` засевается с `ParseHint="binarize_white"` (мелкий пиксельный шрифт, verified T066). Все остальные поля (`gold`, `heroLevel` и др.) — `ParseHint=null` (обычный UI-шрифт; для `heroLevel` бинаризация не улучшает результат).
4. Сидинг синглтонов `WidgetSettings` и `OptimizationProfile` (если отсутствуют).
5. Self-heal `nextLocation.ParseHint` (выполняется при каждом запуске, идемпотентно): если существующий ROI `nextLocation` имеет пустой/null `ParseHint` (БД создана до T066-фикса), он обновляется до `"binarize_white"` через `ExecuteUpdateAsync` (bulk SQL — обходит `init`-only модель Core). Явно заданный пользователем непустой `ParseHint` не перезаписывается. Координаты X/Y/W/H не затрагиваются.

**`BackfillStaleAggregatesAsync` (вызывается из App.xaml.cs после `InitializeAsync`)**:
Идемпотентный пост-миграционный бэкфилл. Определяет этапы, у которых есть non-partial забеги с `GoldGained > 0` или `XpGained > 0`, но соответствующий `StageAggregate` имеет `AvgGoldGained == 0 && AvgXpGained == 0` (признак устаревшего агрегата: колонки `*Gained` добавлены миграцией с дефолтом 0, а `RecomputeForStageAsync` ещё не вызывался). Для каждого такого этапа вызывает `IStageAggregateRepository.RecomputeForStageAsync`. После пересчёта условие становится ложным (gained > 0) — повторный запуск приложения не делает лишней работы. Параметр `recentWindowSize` берётся из `OptimizationProfile` текущего DI-scope (дефолт 10).

**FK-выравнивание config ↔ БД (инвариант, verified T063 Phase 3)**:
`GameMechanicsConfig.BuildStages` присваивает `Stage.Id = index + 1` (1..60) детерминированно.
`DatabaseInitializer` сидирует `db.Stages.AddRange(config.Stages)` — те же объекты, с теми же Id.
`StageConfiguration` объявляет `builder.Property(e => e.Id).ValueGeneratedOnAdd()`, **но** при явной вставке с Id=N SQLite принимает его как явный PK (AUTOINCREMENT не переопределяет указанное значение при INSERT).
Следствие: `GameMechanicsConfig.ResolveStageId(StageRef)` возвращает Id, гарантированно совпадающий с `Stage.Id` в засеянной БД. `StageRun.StageId = resolvedId` → всегда валидный FK.
Это подтверждено интеграционными тестами `ResolverFkAlignmentTests` на реальном временном SQLite (74 тестов Data.Tests, все прошли).

**Composition root** (TBHStats.App) регистрирует контекст так:
```csharp
string dbConnectionString = DatabaseInitializer.GetConnectionString(DatabaseInitializer.GetDbPath());
services.AddDbContext<TbhStatsDbContext>(
    options => options.UseSqlite(dbConnectionString),
    ServiceLifetime.Scoped);
// При старте (App.xaml.cs — один DI-scope):
await DatabaseInitializer.InitializeAsync(db, ct);
// Бэкфилл устаревших агрегатов (идемпотентен):
await DatabaseInitializer.BackfillStaleAggregatesAsync(db, aggregateRepo, profile.RecentWindowSize, logger, ct);
```

---

## 7. Графики и UI-виджет

### Виджет живой статистики (T028, US1)

Стартовое окно приложения — `WidgetWindow` (`TBHStats_App.Views.WidgetWindow`):

- Наследует `Window` (Windows App SDK), namespace `TBHStats_App` (как `MainWindow`).
- DataContext корневого Grid задаётся из DI: `App.Services.GetRequiredService<LiveStatsViewModel>()`.
- Стартовый размер 340×300 px (SC-006: ≤15% экрана); позиция/размер/AlwaysOnTop восстанавливаются из `WidgetSettings` через `AppWindow.Move/Resize` + `OverlappedPresenter.IsAlwaysOnTop`.
- **Игровой скин (T068, ADR-026; доработан T068 Phase 4)**: фиксированная палитра «STATUS TBH» — тёмный фон (`GameBackgroundBrush` #1A1512), бордовая заголовочная плашка «TBHSTATS» (`GameHeaderBorderStyle`), тёмная кожаная панель статов (`GamePanelBorderStyle`, фон `#3D2B1A`).
  - **Шрифты**: `GameHeaderFont` = DotGothic16 (широкий блочный, для заголовков/лейблов/кнопок); `GameBodyFont` = VT323 (моноширинный, максимально чёткие цифры, для значений). Оба SIL OFL; бандлируются в `Assets/Fonts/`. Pixelify Sans удалён. `GamePixelFont` — алиас на `GameBodyFont`.
  - **Заголовок**: текст «TBHSTATS», `FontSize=22`, `CharacterSpacing=150`, Bold, DotGothic16, золотой, на бордовой плашке. Содержит кнопку ✕ (закрытие) в правом углу.
  - **Title bar скрыт** (T068 Phase 4): `OverlappedPresenter.SetBorderAndTitleBar(hasBorder:true, hasTitleBar:false)` + `IsMaximizable=IsMinimizable=false`. Перемещение — через P/Invoke Win32: `ReleaseCapture() + WM_NCLBUTTONDOWN(HTCAPTION)` по `PointerPressed` на `HeaderBorder`. Позиция персистируется через `AppWindow.Changed` без изменений.
  - **Значения-строки** выводятся через `OutlinedTextBlock` (`TBHStats_App.Views.Controls`): 8-слойная тёмная окантовка (Stroke=`#201008`, OutlineThickness=1.2) поверх светлого Fill (`GameValueBrush` #FFF8E8). `ContentControl`-наследник с 9 TextBlock-слоями в Grid; DependencyProperties: `Text`, `Fill`, `Stroke`, `OutlineThickness`, `TextFontSize`, `TextFontFamily`, `TextFontWeight`, `TextAlignment`.
  - **ProgressBar**: `GameProgressBarStyle` (BasedOn Default), `Foreground=GameProgressBrush` (#4BBF40, зелёный), `Background=GameProgressTrackBrush` (#251810).
  - **Дочерние окна**: `CompareHostWindow` и `CalibrationHostWindow` открываются инстанс-методами с трекингом (`_compareWindow`, `_calibrationWindow`). При повторном открытии — `Activate()` существующего. При закрытии виджета (`OnWidgetClosed`) — оба дочерних окна закрываются явно.
  - Цветовые ресурсы — только из `GameWidgetStyles.xaml`; статус-плашки используют хардкод `#FF5A1010`/`#FF5A4500` (TBH-тёмные, замена UWP `SystemFillColor*` кистей, небезопасных для WinUI 3).
- **Чекбокс «Поверх окон» (FR-015)**: `CheckBox` `GameCheckBoxStyle`, `AutomationId="AlwaysOnTopCheckBox"`. Восстанавливается из `WidgetSettings.AlwaysOnTop`; изменение применяется через `OverlappedPresenter.IsAlwaysOnTop` и немедленно персистируется. Флаг `_applyingSettings` предотвращает срабатывание обработчика при программном задании `IsChecked`.
- **Кнопка «Скрыть/Вернуть игру» (T068)**: тоглирует окно игры между on-screen и off-screen через `IGameWindowController` (`GameWindowController`). Текст меняется: «Скрыть игру» ↔ «Вернуть игру» (при недоступном окне — «Игра не найдена» на 1,5 с). `IGameWindowController` зарегистрирован в DI как singleton (`Composition.cs`). При закрытии виджета — автоматически вызывается `Restore`, если окно было уведено.
- При изменении размера/позиции (AppWindow.Changed) — сохранение в `WidgetSettings` через отдельный scope (дедупликация, задержка 500 мс).
- При закрытии виджета — `Restore` уведённого окна игры (если нужно) + `IStatsOrchestrator.StopAsync()`.
- Кнопка «Калибровка» открывает `CalibrationHostWindow` — отдельное окно-хост с Frame.Navigate(`CalibrationView`).
  - `CalibrationViewModel` получает **общий singleton `ICaptureSession`** (тот же, что у оркестратора; доступ сериализован семафором сессии). Команда `CaptureFrame` делает снимок окна игры (`TryGetFrameAsync`), конвертирует `SoftwareBitmap`→`SoftwareBitmapSource` (BGRA8 Premultiplied) на UI-потоке и кладёт в `FrameImage` + `FrameWidthPx/FrameHeightPx`. Авто-захват при открытии страницы; кнопка «Захватить кадр» — повторный снимок. VM также получает **`IChestPanelAnalyzer`** (singleton, тот же экземпляр `ChestDotCounter`, что и в `FieldExtractor`).
  - Кадр показывается в `ScrollViewer` (`ZoomMode="Enabled"`, MinZoom 0.1 / MaxZoom 16) с панорамированием; контент `PreviewContent` имеет размер кадра в пикселях (`Width/Height ← FrameWidthPx/FrameHeightPx`), `Image` `Stretch="Fill"`. Масштаб: Ctrl+колесо / кнопки «−/Вписать/+»; при захвате кадр авто-вписывается (`FitToView` через `DispatcherQueue`).
  - ROI задаётся **рисованием рамки мышью** прямо по кадру (`CalibrationView.xaml.cs`: PointerPressed/Moved/Released на `PreviewContent`). Координаты ROI — это **доли от размера контента**: `SelectedItem.X = pixel / RoiOverlayCanvas.ActualWidth` и т.п. (letterbox не нужен — контент совпадает с кадром по пропорциям; зум/панорамирование учитываются автоматически, т.к. `GetCurrentPoint(RoiOverlayCanvas)` возвращает координаты в системе контента). Оверлей лежит внутри зумируемого контента → рамки масштабируются вместе с кадром. Числовые поля X/Y/W/H остаются для тонкой правки (живая перерисовка оверлея).
  - **Команда «Проверить» (TestSelectedRoiOcr)**: три ветки:
      - FieldKey `"chestZone"` → `IChestZoneAnalyzer.AnalyzeZoneAsync`: отображается «<Тип>: <N>, ...» для каждой найденной плашки и «Зона: распознано плашек — N»; при пустом результате — подсказка «ROI должна покрывать всю группу плашек (по горизонтали и с точками снизу)». **Рекомендуется** для калибровки основного пути ADR-023: нарисовать одну зону на всю горизонтальную группу плашек вместо отдельных `chest:*@N` ROI.
      - FieldKey с префиксом `chest:` (legacy per-плашечный путь) → `IChestPanelAnalyzer.AnalyzeChestPanelAsync`: отображается тип + число точек одной плашки.
      - Остальные поля — OCR-путь (`IOcrReader.ReadAsync`).
- Кнопка «Сравнение» открывает `CompareHostWindow` — окно-хост с Frame.Navigate(`CompareView`) (US2, T041).

### Окно сравнения этапов (T041, T068 Phase 5)

`CompareHostWindow` (`TBHStats_App.Views.CompareHostWindow`):

- **Игровой скин (T068 Phase 5)**: корневой `Grid` с `Background=GameBackgroundBrush`; бордовая заголовочная плашка «STAGE COMPARISON» (`GameHeaderBorderStyle` + `OutlinedTextBlock`; Fill=`GameHeaderForegroundBrush`, Stroke=`GameTextStrokeBrush`); кнопка ✕ (`GameCloseButtonStyle`) в правом углу.
- **Borderless title bar (T068 Phase 5)**: `OverlappedPresenter.SetBorderAndTitleBar(hasBorder:true, hasTitleBar:false)`; `IsMaximizable=IsMinimizable=true` (окно крупное, поддерживает ресайз/максимизацию). Перемещение через P/Invoke Win32 `ReleaseCapture() + WM_NCLBUTTONDOWN(HTCAPTION)` по `PointerPressed` на `HeaderBorder`; guard `IsWithinButton` для кнопки ✕.
- **Персист геометрии (T068 Phase 5)**: ключ `"compare"`, модель `WindowPlacement` (Core), через `ISettingsRepository.GetWindowPlacementAsync / SaveWindowPlacementAsync`. Загрузка — в `OnFirstActivated` (флаг `_settingsApplied`, однократно). Сохранение — в `OnAppWindowChanged` (debounce 500 мс, флаг `_savePending`). Scoped доступ через `App.Services.CreateAsyncScope()`.
- **Стартовый размер**: 820×600 px (применяется до загрузки персиста; перезаписывается сохранённым `WindowPlacement` при наличии).
- **UI на английском (T068 Phase 5)**: заголовок «STAGE COMPARISON», title «TBHStats — Stage Comparison».

`CompareView` (`TBHStats_App.Views.CompareView`):

- `Background=GameBackgroundBrush`; радиокнопки «Gold/h» / «Exp/h» с `Foreground=GameLabelBrush`, `FontFamily=GameBodyFont`.
- Плашка рекомендации: `GamePanelBorderStyle`, текст `FontFamily=GameBodyFont`, `Foreground=GameLabelBrush`.
- Разделитель: `Background=GamePanelBorderBrush`.
- Заголовок таблицы: `GameHeaderBorderStyle` + `GameLabelTextStyle` для всех колонок.
- Строки таблицы: `FontFamily=GameBodyFont`, `Foreground=GameValueBrush`; ★ в цвете `GameHeaderForegroundBrush`.
- Пустое состояние: `FontFamily=GameBodyFont`, `Foreground=GameSecondaryTextBrush`.
- Все видимые строки переведены на английский: «Stage», «Gold», «Gold/h», «Exp», «Exp/h», «Runs», «No data — play stages to accumulate history», «Optimization goal», «Gold/h», «Exp/h», «Recommended: …», «No data for recommendation».
- `CompareViewModel.FormatRate` возвращает суффикс `/h` (было `/ч`); `BuildPowerText` использует `"lv."` / `"dmg"` (было `"ур."` / `"урон"`).
- **Кнопка «Графики» из виджета убрана (T064):** функционал графиков временно отключён — окно `ChartsHostWindow`/`ChartsView`/`ChartsViewModel` остаётся в коде (см. ниже), но точки входа из виджета нет. В виджете: чекбокс «Поверх окон» + кнопки «Скрыть игру», «Сравнение», «Калибровка».
- **Keyboard accelerators (T053 A11y)**: Alt+C — сравнение, Alt+K — калибровка (Alt+G «графики» снят вместе с кнопкой). Все кнопки и чекбокс имеют `AutomationProperties.Name` и `AutomationProperties.AutomationId`.

Визуальные состояния (T031):
- `IsGameFound == false` → красная плашка «Игра не найдена».
- `IsWaiting == true` → жёлтая плашка «Ожидание».
- `IsStale == true` → метка времени последнего обновления приглушена; поле `LastUpdateText`.

Строки виджета живой статистики (T028, T063 Phase 2, T064):
- **«Золото»**: `GoldText` = «{текущее} ({золото за предыдущий пройденный этап})», напр. «126 904 (3 450)». Скобки добавляются только если есть `LastCompletedStageGold`. ToolTip: «Текущее золото; в скобках — золото, полученное за предыдущий пройденный этап (по боссу)».
- **«Опыт»**: `XpCurrentText` = «{текущий} / {до уровня} ({опыт за предыдущий пройденный этап})», напр. «1 234 567 / 2 000 000 (45 678)». ToolTip аналогичный. Значение в скобках — по тому же сегментному механизму, что и время этапа (см. §9), берётся из снапшота `LastCompletedStageXp`.
- **«Этап»**: при нераспознанном этапе (`Stage == null`) показывается прочерк «—». Этап считается распознанным, только если он получен из свежего `nextLocation` в текущем сегменте (`_currentStageRecognized`, см. §9) — без этого скобочные значения золота/опыта/времени к этапу не привязываются.
- **«Прогресс этапа»**: `StackPanel`, внутри 2-колоночный `Grid` (подпись + `StageProgressText`, значение «49 %» / «Босс» / «—») + `ProgressBar` (`Value="{Binding StageProgress}"`, диапазон [0..1]). Биндинг из `LiveStatsViewModel.StageProgressText` / `StageProgress`.
- **«Время этапа»**: 2-колоночный `Grid` (подпись + `StageElapsedText`); формат «{живой таймер} ({время предыдущего пройденного этапа})», напр. «1м 23с (2м 05с)» / «12с» / «—». Биндинг из `LiveStatsViewModel.StageElapsedText`.
- **Статус + время обновления** — последняя строка.

### Экран сравнения этапов (T041/T042, US2, recency-aware)

`CompareView` (`TBHStats_App.Views`) + `CompareViewModel`:
- Таблица всех этапов с историей, ранжированных `IOptimizationService` по выбранной метрике (золото/час ↔ опыт/час, FR-009/019) и `AggregationScope` (свежее окно / вся история).
- **Колонки таблицы (T064): «★» · «Этап» · «Золото/ч» · «Опыт/ч» · «Забегов».**
  - **Номер этапа и сложность разделены** в `CompareStageRow`: `StageNumberLabel` («1-5») отображается в колонке «Этап», `DifficultyLabel` («Normal»/«Nightmare») подготовлен в модели, но **колонка сложности визуально скрыта** — сейчас сложность всегда `Normal` (данные сложности из Portal в v1 ещё не считываются). `StageLabel` («1-5 Nightmare») сохранён для `StatusText`.
  - **Колонка «Сила» убрана из отображения**: `PowerText`/`IsStalePower` по-прежнему вычисляются и данные уровня/урона продолжают сохраняться в БД (`HeroSnapshot` в `StageRun`), но в таблицу не выводятся.
- Рекомендованный этап помечен «★»; «устаревшие» забеги (сила окна заметно ниже текущей силы отряда из `IStatsOrchestrator.Current`) детектируются (`IsStalePower`) для логики ранжирования.
- **Recency-aware (уточнение 2026-05-31)**: добыча зависит от растущей силы отряда (уровни/предметы/руны; сложность — измерение этапа) → рекомендация по умолчанию по свежему окну (последние `OptimizationProfile.RecentWindowSize` non-partial забегов), не по всей истории. Прокси силы — выбранный герой (`HeroSnapshot`); руны/предметы/герои 2–3 в v1 не считываются (см. spec.md Assumptions). Переключение метрики/scope — через `OptimizationProfileService` (T040), персистится в `OptimizationProfile`.
- **A11y (T053)**: RadioButton «Золото/час» / «Опыт/час» и «Свежее окно» / «Вся история» имеют `AutomationProperties.Name` и `AutomationProperties.AutomationId`; ListView таблицы этапов аннотирован.

### Графики

**Решение**: **LiveCharts2** (`LiveChartsCore.SkiaSharpView.WinUI`).

- Официальная поддержка WinUI 3, единственная зависимость — SkiaSharp; качественные анимации и интерактивные тултипы по точкам (сценарий US3).
- Один API на все .NET-UI → переиспользуем при Android-клиенте на MAUI.

**Альтернатива**: **ScottPlot** — быстр на плотных данных; держим как замену при проблемах со Skia-рендером в оверлее.

### Экран трендов (T045/T046, US3)

`ChartsView` (`TBHStats_App.Views`) + `ChartsViewModel` (`TBHStats.App.ViewModels`) + хост `ChartsHostWindow`:
- Три `lvc:CartesianChart` (`using:LiveChartsCore.SkiaSharpView.WinUI`): тренды **золото/час**, **опыт/час**, **время прохождения (мин)** по выбранному этапу во времени; интерактивные тултипы (`TooltipPosition="Top"`, `X/YToolTipLabelFormatter`).
- Источник данных — завершённые non-partial `StageRun` (одна точка `LineSeries<DateTimePoint>` на забег: x = `CompletedAtUtc`, y = `GoldPerHour`/`XpPerHour`/`DurationSeconds/60`). «Линии отражают реальные записи» (Independent Test US3).
- `ComboBox` выбора этапа: `StageOptions` (только этапы с историей; метка «1-5 Nightmare» через тот же join Stage+Act+Difficulty, что и `CompareViewModel`); смена `SelectedStage` перестраивает серии (partial `OnSelectedStageChanged` → fire-and-forget `RebuildSeriesAsync`).
- Ось X — `Axis.Labeler` форматирует тики как `dd.MM HH:mm` (`UnitWidth`/`MinStep` = 1 мин); пустое состояние при отсутствии истории.
- Lifetime VM — Transient; scoped `IRunRepository`/`TbhStatsDbContext` через `IServiceScopeFactory`; обновления UI маршалятся через `DispatcherQueue` (паттерн `CompareViewModel`).
- **A11y (T053)**: ComboBox выбора этапа и все три CartesianChart имеют `AutomationProperties.Name` и `AutomationProperties.AutomationId`.

### Доступность (Accessibility, T053 — §XI конституции)

Реализован accessibility-проход по UI (RECOMMENDED):

- **Keyboard operability**: кнопки «Графики» / «Сравнение» / «Калибровка» в `WidgetWindow` имеют `AutomationProperties.Name`, `AutomationProperties.AutomationId`, `IsTabStop=True`, разумные `TabIndex`. Keyboard accelerators: Alt+G, Alt+C, Alt+K; обработчики в code-behind через `KeyboardAcceleratorInvokedEventArgs`. RadioButtons в `CompareView` (метрика, scope) аннотированы и имеют `TabIndex`.
- **Подписи сундуков (не только цвет)**: `LiveStatsViewModel.BuildChestsText` теперь принимает `IGameMechanics` и использует `ChestType.DisplayName` («Базовый», «Редкий», «Легендарный») вместо числового Id — тип сундука различается текстом, не только цветом (A11y §XI + конституция «Do not rely on colour alone»).
- **ThemeResource**: все цвета в XAML используют `{ThemeResource ...}` (системные кисти); жёстко заданных `#RRGGBB` нет — виджет корректен в Light/Dark теме.
- **CalibrationView**: все интерактивные элементы (Button, ComboBox, NumberBox, TextBox) аннотированы `AutomationProperties.Name`, `AutomationProperties.AutomationId`, `AutomationProperties.LabeledBy` и `TabIndex`.
- **ChartsView**: ComboBox этапа и CartesianChart аннотированы; статус-текст с `{ThemeResource SystemControlForegroundBaseMediumBrush}`.
- Реальная визуальная проверка (скринридер Narrator, Accessibility Insights for Windows, High Contrast) требует запущенного приложения (T051).

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
 session.TryGetFrameAsync()──► null (Waiting/NotFound)          │
       │     null → публикуем IsStale-снимок, ждём интервал     │
       │     frame → State=Capturing                            │
       ▼                  │                  │                  │
 DetectActiveTabAsync()   │                  │                  │
  (ROI «activeTab»)       │                  │                  │
       │                  ▼                  │                  │
       │          ExtractAsync()             │                  │
       │          (все поля каждый кадр,      │                  │
       │           без tab-gating, ADR-019)   │                  │
       │                  ▼                  │                  │
       │          IObservationValidator      │                  │
       │          .Validate() → MetricSample │                  │
       │          (confidence ≥ 0.02 + sanity)│                 │
       │                  ▼                  │                  │
       │          IsReliable=true ───────────► скользящий буфер │
       │                                     (≤200 сэмплов)    │
       │                                      ▼                 │
       │                                 ComputeLiveRates() ────► LiveStatsSnapshot
       │                                                        │  → SnapshotUpdated event
       │                                                        │  → ViewModel биндинг (P1)
       │                                                        │
 граница сегмента этапа (падение прогресса после босса, T064) ──► PersistSegmentRunAsync → PruneOldRunsAsync → RecomputeForStageAsync (P2)
                                                                │
 экран сравнения ───────────────────────────────────────────────► GetAllAsync + RankStages (P2)
```

Подробно по шагам (`StatsOrchestrator.RunLoopAsync`):

1. Загрузить `WidgetSettings` (интервал опроса) и `RoiCalibrations` — **перечитываются каждую итерацию**, чтобы калибровка применялась без перезапуска приложения (стоимость SELECT по таблице ~21 строки ничтожна; при ошибке чтения сохраняется предыдущий набор ROI).
2. `session.TryGetFrameAsync(ct)` → `null` (состояние `session.State` == `NotFound`/`Waiting`) → `PublishStaleSnapshot(session.State)`, задержка, продолжить.
3. `frame != null` → `State=Capturing`. Найти ROI «activeTab» в калибровках → `tabDetector.DetectActiveTabAsync()`.
4. `fieldExtractor.ExtractAsync(frame, rois, activeTab, cfg, ct)` → `RawObservation`.
5. `validator.Validate(obs, _lastReliableSample, 0.02)` → `MetricSample` (порог уверенности понижен с 0.6 до 0.02, ADR-019: геометрическое покрытие — слабый прокси, основная валидация — парсер+sanity). Если `IsReliable` → обновить `_lastReliableSample`, добавить в скользящий буфер (≤200 записей), обновить «последние известные» значения.
6. `metrics.ComputeLiveRates(buffer)` (если буфер ≥2 сэмплов) → `LiveRates`.
7. Собрать `LiveStatsSnapshot`; `IsStale = (State != Capturing) || (lastReliableUtc устарел > 30 c)`. Опубликовать через событие `SnapshotUpdated`.
8. Исключения захвата/OCR → `logger.LogWarning` + `PublishStaleSnapshot`, без броска наружу. `using (frame)` — `CapturedFrame.Dispose()` гарантирован.
9. `Task.Delay(pollIntervalMs, ct)` — период петли.

**Обработка ошибок (T047, ADR-016):**
- **«Ожидание вместо throw»** — инвариант всего пайплайна захвата: временная недоступность (свёрнутое окно, OCR-недоступность, пустой ROI) возвращает `null`/пустое значение + логирование, но не бросает исключение вверх по стеку. Исключения бросаются только при нарушении инвариантов аргументов (`ArgumentNullException`, `ArgumentOutOfRangeException`).
- **Структурные шаблоны**: все вызовы `ILogger` используют именованные плейсхолдеры (`{FieldName}`), а не интерполяцию `$"..."`.
- **Уровни**: `Debug` — диагностика/частое (детекция вкладки в норме); `Information` — жизненный цикл (старт/стоп петли, успешная запись забега, смена профиля); `Warning` — восстановимая деградация (ошибка кадра, ошибка настроек, неизвестный класс героя); `Error` — невосстановимый сбой на старте (инит БД, старт оркестратора).
- **PII в логах**: путь к файлу БД и лог-директории **не логируется** на уровнях Info/Warning — только через `Debug` (или не логируется вовсе). Сырые OCR-строки не попадают в лог выше `Debug`.

**Стабилизация поля `nextLocation` (обновлено 2026-06-02, `NextLocationStabilizer`):**
- На локациях ~3-6..3-10 фоновый огонь заставляет OCR поля `nextLocation` выдавать транзиентный мусор;
  также наблюдаются флакирующие чтения (правильное значение есть, но не подряд — с шумами/пропусками между).
- Сразу после `IObservationValidator.Validate()` и ДО резолва `StageId` оркестратор вызывает
  `_nextLocationStabilizer.Observe(sample.NextLocation, cfg)` → стабильное значение записывается обратно
  в `sample.NextLocation`. Все потребители значения (резолв `StageId`, `_lastKnownStage`,
  `RunRecorder`) автоматически используют стабилизированное значение.
- **Два механизма:** (1) **Оконное голосование `≥confirmCount из windowSize` (дефолт: 2 из 5)** —
  стабильным становится значение, встретившееся ≥2 раза в окне последних 5 ВАЛИДНЫХ чтений (прошедших sanity).
  Устойчиво к пропускам (null-кадры окно не изменяют) и одиночному шуму (1 раз в окне → не проходит порог ≥2);
  флакирующее, но реально присутствующее значение (≥2 из 5, даже не подряд, с шумом/null между) → стабильно.
  При равных частотах выбирается самое недавнее (последнее по времени появления в окне).
  (2) **Sanity-фильтр** — чтение `reading` проверяется через `reading.Previous()` → `cfg.ResolveStageId()`;
  если «предыдущий этап» не резолвится в конфиге (несуществующий акт/сложность/номер) — чтение мусорное,
  игнорируется и в окно не добавляется.
- **Класс:** `TBHStats.Core.Parsing.NextLocationStabilizer` — stateful, без WinRT/EF/UI-зависимостей,
  переиспользуем в MAUI. Параметры `windowSize` (дефолт 5), `confirmCount` (дефолт 2). `Reset()` вызывается
  при `NotFound` (guard `_windowLostHandled`) — устаревшее окно не мешает определению этапа после возврата.
- **`_lastKnownStage` обновляется вне гейта `sample.IsReliable`** (2026-06-02): «Этап» берётся из
  `nextLocation` (MainZone, видна всегда) и не зависит от чтения золота/опыта. Обновление происходит
  каждый Capturing-кадр сразу после резолва `currentStageRef = sample.NextLocation?.Previous()`;
  `UpdateLastKnownValues` (вызывается только при `IsReliable`) `_lastKnownStage` больше не устанавливает.
  Это устраняет зависание строки «Этап» в виджете при ненадёжных (по золоту/опыту) кадрах.
- **Ограничение:** если огонь даёт стабильно-неверное, но РЕЗОЛВИМОЕ чтение ≥2 раз в окне 5 —
  фильтр его не отсеет. Следующий шаг при необходимости: jump-distance escalation
  (большой скачок `StageId` требует больше подтверждений) и/или улучшение OCR-предобработки nextLocation
  для акта 3 (нужна фикстура-скриншот).

**Реализация (T026, T063 Phase 2, T063 Phase 3)**:
- `LiveStatsSnapshot` (sealed record) — `src/TBHStats.App/Services/LiveStatsSnapshot.cs`: поля `CaptureState State`, `LiveRates Rates`, `long? Gold`, `int? HeroLevel`, `string? HeroClass`, `long? HeroDamage`, `StageRef? Stage`, `double? StageProgress`, `bool? BossPresent`, `int? StageElapsedSeconds`, `DateTime? LastReliableUtc`, `bool IsStale`. Статик `Empty` — начальное значение. Поля `StageProgress`/`BossPresent`/`StageElapsedSeconds` добавлены в T063 Phase 2 (проброс из `StatsOrchestrator` → `LiveStatsViewModel`).
- `IStatsOrchestrator` — `src/TBHStats.App/Services/IStatsOrchestrator.cs`: `LiveStatsSnapshot Current`, `event EventHandler<LiveStatsSnapshot>? SnapshotUpdated`, `Task StartAsync(CancellationToken)`, `Task StopAsync()`.
- `StatsOrchestrator` — `src/TBHStats.App/Services/StatsOrchestrator.cs`: singleton, зависимости через DI (вкл. `IOcrReader` для диагностики). `_current` volatile (запись через `_current = snapshot`; WinUI-приложение single-writer). Буфер `_reliableBuffer` ограничен `MaxReliableBufferSize=200`. `ConfidenceThreshold=0.02` (ADR-019). `StaleThresholdSeconds=30`. `LiveRateWindowSeconds=90` (см. «Короткое скользящее окно темпов» ниже). ROI перечитываются каждую итерацию (калибровка применяется без перезапуска).
- **Резолв `StageId` (T063 Phase 3):** после `IObservationValidator.Validate()` оркестратор вычисляет `StageRef? currentRef = sample.NextLocation?.Previous()` (тот же источник, что `_lastKnownStage` — `nextLocation − 1`, ADR-008) и вызывает `GameMechanicsConfig.ResolveStageId(currentRef.Value)` → `sample.StageId`. Резолвер — метод `GameMechanicsConfig` в `TBHStats.Core`: обход справочников `Acts`/`Difficulties`/`Stages` без хардкода (config-driven, ADR-009). `ObservationValidator` `StageId` не проставляет — у него нет конфига by design. `RunRecorder.OnFrameAsync` получает `sample` с уже заполненным `StageId`, что обеспечивает корректную привязку забега к этапу.
- **Сброс `RunRecorder` при потере окна (T063 Phase 3):** при `session.State == NotFound` и null-кадре `StatsOrchestrator` вызывает `_runRecorder?.Reset()` **один раз на эпизод** потери окна (guard-флаг `_windowLostHandled`). Флаг сбрасывается при первом успешном вызове `ProcessFrameAsync` (State=Capturing). Состояние `Waiting` (свёрнутое окно) НЕ вызывает сброс: игра ещё жива, незавершённый забег должен продолжиться после восстановления окна.

**Тонкости домена** (R4):
- **Золото — расходуемый баланс (2026-06-01):** игрок тратит золото на прокачку рун, апгрейды и магазин, поэтому баланс легитимно убывает. `ObservationValidator` **не гейтит надёжность сэмпла по убыванию золота** — убыль не является нарушением sanity. Темп «золото/час» в `MetricsCalculator` считается только по **положительным дельтам** (заработок); убыль игнорируется. Фактический (в т.ч. сниженный) баланс всегда записывается в `MetricSample.Gold` для корректного отображения. Ранее (до 2026-06-01) проверка монотонности в `ObservationValidator` блокировала весь сэмпл при любом убывании золота — виджет зависал после траты до тех пор, пока баланс не дорастал обратно.
- **EXP** показывается в пределах уровня и обнуляется при level-up: прирост считается с учётом `HeroLevel` и `XpToLevel` (добор до полного предыдущего уровня + текущий EXP), а не как убыль. **Защиты от OCR-выбросов (2026-06-01):** level-up компенсация применяется только если предыдущий `Xp` реально у потолка (`≥ 0.8·XpToLevel`) — иначе «инкремент уровня» при низком Xp трактуется как misread `heroLevel` (иначе инжектировался ~весь `XpToLevel` → наблюдался ложный темп ~1.08e9 опыт/ч); интервалы с невозможным чтением (`Xp > XpToLevel`) пропускаются. **Guard разрыва XP — два уровня (2026-06-01, уточнён 2026-06-01):** (1) **Структурный guard по уровню героя (первичный):** легитимный переход `HeroLevel` между соседними live-сэмплами — строго `levelDelta == 0` (тот же уровень) или `levelDelta == +1` (одиночный level-up). Любой иной переход при известных обоих уровнях (условие `levelDelta != 0 && levelDelta != 1`, включая отрицательный) — разрыв (смена героя, мультиуровневый скачок, misread heroLevel): пара исключается **целиком** из расчёта опыт/час (ни в `xpDeltaSum`, ни в `xpElapsedSum`). Надёжно ловит смену героя в late-game, где межгеройская XP-дельта может быть **меньше** `a.XpToLevel` (магнитудный guard в этом случае не срабатывает). (2) **Магнитудный guard (вторичный, для `levelDelta == 0`):** в не-level-up-ветке положительная дельта `b.Xp − a.Xp`, превышающая `a.XpToLevel`, физически невозможна в пределах одного уровня и ловит внутриуровневые OCR-выбросы — пара также исключается целиком. Компенсированная level-up-ветка (`nearFull && levelUpByOne`, `levelDelta == +1`) обоими guard'ами не затрагивается. Золото и сундуки в той же итерации обрабатываются как обычно.
- **Короткое скользящее окно темпов (2026-06-01):** живой темп (`ComputeLiveRates`) считается **не по всему `_reliableBuffer`** (≤200 сэмплов, ≈5 мин), а только по срезу последних `LiveRateWindowSeconds=90` секунд. Мотивация: кумулятивное среднее по всему буферу медленно сходится к истинному значению и лагает после смены этапа/героя (~313 с span → ~1.27 M/ч вместо эталонных ~1.375 M/ч к концу окна). Срез строится обходом буфера с конца: отбирается непрерывная цепочка сэмплов в пределах 90 с от последнего; порядок восстанавливается `Reverse()` для `ComputeLiveRates`. `_reliableBuffer` по-прежнему накапливает до 200 сэмплов (нужен для детекта смены героя и outlier'ов). All-time агрегаты истории (`StageAggregate`, `IRunRepository`) не затрагиваются.
- **Сглаживание темпов (EMA):** публикуемые `золото/ч` и `опыт/ч` сглаживаются EMA (α=2/(5+1), ~5 снимков) в `StatsOrchestrator` — гасит дёрганье OCR; «До уровня» виджета считается по сглаженному `опыт/ч` (`(XpToLevel − Xp)/опыт-ч`).
- **Сброс окна темпов при смене героя — два независимых сигнала (2026-06-01):** при каждом надёжном сэмпле `StatsOrchestrator` проверяет два сигнала смены героя; если хотя бы один сработал — полностью сбрасывает окно измерения (`_reliableBuffer`, `_lastReliableSample`, `_emaXpPerHour`, `_emaGoldPerHour`, `_lastChestPerHour`) — живые темпы начинают считаться «с нуля» для нового героя.
  - **Сигнал 1 — класс героя:** нормализует `obs.HeroClassText` в машинный ключ через `ResolveHeroClassKey` (case-insensitive Contains по `HeroClass.Key`/`DisplayName` из `GameMechanicsConfig.HeroClasses`). Срабатывает, если новый ключ известен (не null) и отличается от ключа текущей эпохи. Защита от OCR-шума: нераспознанный класс (null) сброс не вызывает. Зависит от чтения `HeroClassText` — на кадрах переключения это поле может отсутствовать.
  - **Сигнал 2 — структурные инварианты HeroLevel/XpToLevel (`HeroSwitchDetector` в Core):** надёжен даже когда OCR не читает `HeroClassText`. Логика чистого детектора `TBHStats.Core.Optimization.HeroSwitchDetector.IsHeroSwitch`: (a) **Падение уровня** — если оба `HeroLevel` заданы и `current.HeroLevel < previous.HeroLevel`: герой не теряет уровни, это другой герой или misread; (b) **Смена потолка без level-up-сигнатуры** — если оба `XpToLevel` заданы, `previous.Xp` задан, `XpToLevel` изменился и `previous.Xp < 0.8 × previous.XpToLevel`: потолок опыта зависит от уровня и меняется только при level-up (Xp ≥ 80 % потолка); смена потолка при не-полном опыте = другой герой. Одиночный/множественный рост уровня сам по себе сброс не вызывает (легитимный level-up или idle-набор за свёрнутое окно, FR-005a). `HeroSwitchDetector` — `sealed static` класс без состояния, переиспользуемый в тестах.
  Логирование: `LogInformation("Обнаружена смена героя (class:{Cls}, signal:{Sig}); живые темпы пересчитываются с нуля.", classSwitch, signalSwitch)`.
- **Детект выброса ставки опыт/час — защита от переходных OCR-misread XP (2026-06-01):** существующие guard'ы (`MetricsCalculator`: структурный по уровню + магнитудный `delta > XpToLevel`) не ловят ложную дельту в несколько млн, если она меньше потолка уровня и уровень не менялся (реальные случаи: `xp 4791385 → 4 → 4791695` mid-run; `xp 1276641 → 4971566` при смене этапа). Источник-агностичный признак выброса: raw опыт/ч в 100–500× выше установившейся EMA. Защита реализована в `StatsOrchestrator` как post-compute проверка перед EMA-обновлением:
  - **`RateOutlierDetector.IsXpRateOutlier(raw.XpPerHour, _emaXpPerHour)`** (`TBHStats.Core.Optimization`) — чистый статический детектор без состояния. Возвращает `true`, если EMA установлена и положительна (`ema > 0`) и `raw > max(EMA × 6, 5_000_000)`. Если EMA не установлена (старт/после сброса смены героя) — возвращает `false` (нет базы для суждения).
  - При срабатывании: EMA **не обновляется** выбросом (сохраняются последние корректные темпы для отображения); `_reliableBuffer` и `_lastReliableSample` сбрасываются (ложная дельта не держится в буфере). Логирование: `LogWarning("[XpRateOutlier] raw={Raw:F0}/ч ema={Ema:F0}/ч — выброс отброшен, окно темпов сброшено.")`.
  - **Принципиальное отличие от сброса при смене героя:** outlier-сброс сохраняет EMA (`_emaXpPerHour`/`_emaGoldPerHour` не обнуляются) — виджет продолжает показывать последние корректные темпы, окно просто перестраивается. Сброс при смене героя, напротив, обнуляет EMA до `null` → виджет показывает 0 (желаемое поведение при переключении героя). Эти два сброса намеренно различаются и не унифицируются.
  - При `buffer.Count <= 1` после любого сброса EMA не обновляется (нет `raw` для расчёта) — виджет держит последние корректные темпы до накопления ≥2 сэмплов в новом окне.
- **Сундуки** в MainZone — транзиентные «точки»: растут при выпадении, падают к 0 при открытии. «Получено за забег» = сумма положительных дельт; обнуление = открытие, не потеря. Накопленный итог забега неубывает.
- **Раскладка сундуков (ADR-018)**: иконка типа имеет до трёх позиций по числу одновременно присутствующих типов `N`. `FieldExtractor`/`IChestLayoutResolver` (инвариант `litCount == N`) выбирают активную раскладку и отдают `count` по типам в `RawObservation.Chests`. Базовые `chest:<тип>` без `@N` — прежний путь «одна позиция». См. §4.
  - **Реализовано (T062, ADR-021):** «точки» сундуков **графические, не текст** — OCR их не считает. Счёт точек реализован через `IChestDotCounter`/`ChestDotCounter` (`TBHStats.Capture/Chests/`): кроп ROI → Bgra8-буфер → доля тёмных пикселей (lum < 90) по колонкам → run-ы тёмных колонок (ширина ≥ 3px) = заполненные точки. Пороги: `DarkThreshold=90`, `BrightThreshold=210`, `MinDarkColumnFraction=0.3`, `MinRunWidthPx=3`. Паттерн «валиден» при наличии хотя бы одного светлого пикселя (пустая ячейка) или хотя бы одной точки. Инфраструктура `chest@N`+`IChestLayoutResolver` сохраняется без изменений. Тесты: 4 pass на `chests.jpg` (red=1, blue=1, brown=2, ground truth). При `Detected=false` поле пропускается (как прежде `!OCR.Recognized`).
- **Текущий этап** в виджете = `nextLocation − 1` (`StageRef.Previous`, перенос 10 этапов/акт, ADR-008).
- Босс этапа ↔ шанс синего сундука; босс акта (этап «-10») ↔ шанс красного; коричневый — с любого монстра.
- **Сегментный таймер этапа (T063 Phase 2, обновлён T063 Phase 2 fix):** `StatsOrchestrator` отслеживает `_stageSegmentStartUtc` и `_bossSeenInSegment`. Таймер привязан к **визуальному прогрессу** (`StageProgress`): заметное падение прогресса на `≥ StageProgressDropThreshold (0.10)` трактуется как граница сегмента → таймер **немедленно стартует с нуля** (вариант 2: и при рестарте/смерти, и при прохождении по боссу). Если падению предшествовал босс (`_bossSeenInSegment == true` — явный `BossPresent == true` ИЛИ `StageProgress ≥ BossProgressThreshold (0.99)`, ADR-024), то длительность завершённого сегмента сохраняется в `_lastCompletedStageSeconds` (без сброса при NotFound — историческая справка). **Время сохраняется ТОЛЬКО для сегмента, начатого от наблюдаемого сброса прогресса** (`_segmentStartedFromReset == true` — начало этапа было увидено): сегмент, стартованный «с середины» (первый кадр после запуска виджета/возврата окна) длительность при завершении по боссу НЕ сохраняет — иначе учёлся бы только наблюдаемый хвост (напр. 30 с вместо реальных 6+ мин). Каждое наблюдаемое падение помечает новый сегмент валидным (`_segmentStartedFromReset = true`). Падение без босса (рестарт/смерть) длительность не сохраняет. При потере окна (`NotFound`, guard `_windowLostHandled`) сбрасываются `_stageSegmentStartUtc`, `_bossSeenInSegment`, `_segmentStartedFromReset`, `_lastKnownStageProgress` — чтобы возврат окна не дал ложного/частичного «завершения по боссу». Результаты `_lastKnownStageElapsedSeconds` и `_lastCompletedStageSeconds` публикуются в `LiveStatsSnapshot`. На stale-кадрах (`PublishStaleSnapshot`) таймер не инкрементируется — передаются последние посчитанные значения.
- **Сегментно-управляемая запись забега (T064, ADR-025):** триггер записи `StageRun` **перенесён из `RunRecorder` в `StatsOrchestrator`** — он совпадает с границей сегмента, вычисляющей «время предыдущего этапа» (тот же `progressDropped`-блок). При падении прогресса после боя с боссом (`_bossSeenInSegment && _segmentStartedFromReset && _stageSegmentStartUtc.HasValue`) оркестратор передаёт сводные данные сегмента в `RunRecorder.PersistSegmentRunAsync` (fire-and-forget `Task.Run`). `IStageCompletionDetector` (момент гибели босса) **больше не управляет записью** — класс остаётся в DI/контрактах, но `RunRecorder` его не использует.
  - **Накопление по сегменту (`AccumulateSegment`/`StartNewSegment`):** золото за этап = `Math.Max(0, _lastKnownGold − _segmentStartGold)` (baseline берётся в начале сегмента); опыт = `_segmentXpAccum` (положительные внутрисегментные дельты с компенсацией level-up — зеркало логики метрик); сундуки = `_segmentChestAccum` (сумма положительных дельт мгновенных «точек» по `ChestTypeId`, ADR-011). Накопители сбрасываются в `StartNewSegment` на каждой границе. По завершённому сегменту золото/опыт публикуются в снапшот как `LastCompletedStageGold`/`LastCompletedStageXp` (показ в скобках, §7).
  - **Гейтинг распознавания этапа (корректность статистики):** забег записывается **только если этап был распознан из свежего `nextLocation` в течение сегмента** (`_segmentStageId.HasValue` — устанавливается, когда `sample.StageId` зарезолвился через `cfg.ResolveStageId`). Если `nextLocation` не распознан (первый проход этапа / этап с боссом акта, где `nextLocation` в MainZone не отображается) — забег **НЕ записывается** (лог Debug «Сегмент завершён без распознанного этапа»), а в виджете строка «Этап» показывает прочерк «—» (`_currentStageRecognized == false`). Антифликер-стабилизатор `nextLocation` (`NextLocationStabilizer`) сохранён для транзиентного OCR-шума; гейтинг работает на уровне сегмента (`_segmentStageId`/`_currentStageRecognized` сбрасываются только в `StartNewSegment`).
  - **`RunRecorder` — тонкий сервис персиста:** `PersistSegmentRunAsync(stageId, durationSeconds, goldGained, xpGained, chests, heroLevel, heroDamage, heroClassText, completedAtUtc, ct)` собирает `StageRun` + `HeroSnapshot` → `IRunRepository.AddRunAsync` → `PruneOldRunsAsync(stageId, 10)` (ретенция, §6) → `IStageAggregateRepository.RecomputeForStageAsync`. У него **нет состояния накопления** (`Reset()` — no-op, оставлен для совместимости вызовов из оркестратора при потере окна). `IStageCompletionDetector` из его пайплайна удалён.

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
- `IGameWindowController` — управление позицией окна игры: увод за экран и возврат (T068 Phase 1A).
  - Реализация: `GameWindowController` (Win32 `GetWindowRect`/`SetWindowPos` + user32.dll P/Invoke).
  - `HideOffScreen(window)`: сохраняет текущий `RECT` (left/top), перемещает окно на (-32000, -32000) через `SetWindowPos` с флагами `SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE`. Окно остаётся `Visible` — DWM компонует его поверхность, WGC продолжает захватывать кадры. **Не использует `SW_MINIMIZE`/`SW_HIDE`** — они убирают окно из DWM-композиции и останавливают захват.
  - `Restore(window)`: возвращает на сохранённый `left/top` тем же вызовом `SetWindowPos`; сбрасывает сохранённое состояние.
  - Оба метода идемпотентны и thread-safe (`lock`); возвращают `false` вместо исключения при недействительном HWND.
  - Регистрацию в DI выполняет слой `TBHStats.App` (Composition.cs, Phase 2).
- `IGameWindowTracker` — поиск/отслеживание окна, размер клиентской области (FR-001, FR-005b).
  - Реализация: `GameWindowTracker` (Win32 EnumWindows + user32.dll P/Invoke). Конфигурируется через `GameWindowTrackerOptions.WindowTitleHints` (case-insensitive Contains, дефолты: `"Task Bar Hero"`, `"TaskBarHero"`). **Короткую подсказку `"TBH"` НЕ использовать** — она даёт ложное совпадение с собственным окном (`TBHStats`) и окном редактора (`… - TBHStats - Visual Studio Code`), которые в Z-order часто выше игры (захват не того окна → пустые значения). Порядок static-полей в `GameWindowTrackerOptions` значим: `DefaultTitleHints` объявляется ДО `Default = new()`, иначе `Default.WindowTitleHints == null` (NRE в `FindGameWindow`).
  - Вспомогательные типы: `GameWindowHandle` (HWND + PID + заголовок), `SizePx` (ширина × высота клиентской области).
  - `GetVisibility`: `!IsWindow` → `Closed`; `IsIconic` → `Minimized`; иначе → `Visible`. Перекрытие НЕ влияет на статус.
  - `GetClientSize`: `GetClientRect` → `SizePx`; невалидный HWND → `SizePx.Empty` (0×0), без исключения.
- `ICaptureSession` / `CaptureSession` — кадры окна и `CaptureState` (FR-005). Возвращает `CapturedFrame?` — общий тип кадра (`SoftwareBitmap` + `SizePx` + timestamp), потребляемый OCR/детектором/экстрактором. Реализует `IAsyncDisposable`. Файлы: `Wgc/ICaptureSession.cs`, `Wgc/CaptureSession.cs`, `Wgc/Direct3D11Interop.cs`, `Wgc/GraphicsCaptureItemInterop.cs`.
- `IOcrReader` / `OcrReader` — распознавание значения из нормализованной ROI (FR-002/003). Кроп через `BitmapEncoder/Decoder + BitmapBounds`; confidence = геометрическое покрытие слов в ROI (см. §3).
- `ITabDetector` — распознавание активной вкладки (FR-002a).
- `IFieldExtractor` / `FieldExtractor` — кадр → набор доступных сырых значений → `RawObservation` (FR-002b, T023, ADR-019, ADR-022).
  - Сигнатура: `Task<RawObservation> ExtractAsync(CapturedFrame, IReadOnlyList<RoiCalibration>, TabRef?, GameMechanicsConfig, CancellationToken)`.
  - Ctor: `FieldExtractor(IOcrReader, IValueParser, IChestPanelAnalyzer)` (ADR-022: `IChestLayoutResolver` удалён из ctor).
  - Все калиброванные поля читаются каждый кадр (ADR-019); tab-gating снят. `activeTab`-поле не OCR-ится (берётся из параметра). Визуальные поля `stageProgress` / `bossPresent` пропускаются в v1 (детекция в T035).
  - FieldKey-маппинг: `gold/xp/xpToLevel/heroDamage` → `TryParseAbbreviatedNumber`; `xpPair` → `TryParseXpPair`; `heroLevel` → `int.TryParse` (≥1); `heroClass` → trimmed text; `stageId` → `StageText` (сырой); `stageTime` → `TryParseStageTimeSeconds`; `nextLocation` → `TryParseNextLocation`; `chest:<key>[@N]` → `IChestPanelAnalyzer.AnalyzeChestPanelAsync` → тип из цвета + счёт точек; мёрдж по typeId (наибольший `PanelMatch`). `IChestLayoutResolver` больше не вызывается.
  - Неизвестные FieldKey тихо пропускаются; ошибки парсинга не выбрасываются — поле остаётся null (FR-005).
- `IStageCompletionDetector` / `StageCompletionDetector` — детектор завершения этапа (FR-002, ADR-012, T035). Детерминированная машина состояний без WinRT-зависимостей; потребляет `RawObservation`, возвращает `StageCompletionEvent?`. **С T064 (ADR-025) запись забегов им НЕ управляется** — сегментация перенесена в `StatsOrchestrator` (граница сегмента по падению визуального прогресса после босса, §9). Класс остаётся в DI/контрактах, но `RunRecorder` его не вызывает.
  - Состояния: `InProgress` (бос ещё не виден) → `BossEngaged` (босс замечен) → обратно `InProgress` (событие выдано).
  - Переход в `BossEngaged`: `BossPresent == true` ИЛИ `StageProgress >= 0.99`.
  - Сигнал завершения (из `BossEngaged`): явный `BossPresent == false` ИЛИ ненулевой `StageTimeSeconds > 0`.
  - null-значения полей = «нет данных»; не меняют состояние и не триггерят завершение.
  - `StageCompletionEvent` (readonly record struct): `CompletedAtUtc`, `StageTimeSeconds?`, `StageProgressAtCompletion?`.
  - `Reset()` сбрасывает в `InProgress`; вызывается при смене этапа / переходе сессии в `Waiting`/`NotFound`.
  - Точные пороги уточняются эмпирически на фикстурах (T049).

**`TBHStats.Core`**
- `IValueParser` / `ValueParser` — сокращённые числа K/M/B/T **и полноразмерные числа с пробелом-разделителем разрядов** («54 678», «2 285 394» — реальный формат TBH, verified T049, нормализация `\p{Zs}` между цифрами; см. GAME-FACTS §12), время этапа («SS»/«MM:SS»/«H:MM:SS»), идентификатор этапа (R4). Реализован в `TBHStats.Core/Parsing/`; без статического состояния, `CultureInfo.InvariantCulture`, `decimal`-арифметика для точных множителей.
- `IMetricsCalculator` — темпы по надёжным интервалам (FR-006, FR-005a).
- `IStageAggregateCalculator` / `StageAggregateCalculator` — **чистая** доменная функция (T038): из забегов этапа считает `StageAggregate` (all-time + свежее окно последних N non-partial по `CompletedAtUtc` + power-context из `HeroSnapshot` + темпы сундуков). Не ставит `UpdatedAtUtc` (это делает репозиторий).
- `IOptimizationService` — **recency-aware** ранжирование и рекомендация этапа по `OptimizationMetric` + `AggregationScope` (дефолт `Recent` = при текущей силе; `AllTime` — справка); tie-break recent best (FR-008/009/017/019).
- `IGameMechanics` — доступ и перезагрузка `GameMechanicsConfig` (FR-021).

**`TBHStats.Capture`**
- `IStageCompletionDetector` / `StageCompletionDetector` — детерминированная машина состояний (InProgress/BossEngaged) над `RawObservation`; событие завершения этапа для сегментации забегов (T035, US2).

**`TBHStats.Data`**
- `IRunRepository` — забеги и сэмплы (FR-007).
- `IStageAggregateRepository` — агрегаты этапов; `RecomputeForStageAsync(stageId, recentWindowSize, ct)` пересчитывает all-time + свежее окно через `IStageAggregateCalculator` (FR-008, recency-aware).
- `ISettingsRepository` — настройки виджета, профиль оптимизации (вкл. `RecentWindowSize`/`Scope`), калибровки ROI (FR-016/003).

**`TBHStats.App/Services`** (T026)
- `IStatsOrchestrator` / `StatsOrchestrator` — фоновая петля US1 (FR-004). Singleton. `StartAsync`/`StopAsync` управляют `CancellationTokenSource` + `Task.Run`-петлёй. Не привязан к UI-диспетчеру — ViewModel маршалирует в UI.
- `LiveStatsSnapshot` (sealed record) — снимок для биндинга: состояние захвата + темпы + последние достоверные значения + IsStale.
- `ScopedSettingsRepositoryProxy` (internal) — адаптер для singleton-safe доступа к scoped `ISettingsRepository`: создаёт `AsyncServiceScope` на каждый вызов через `IServiceProvider`.
- `RunRecorder` (T036, US2; переработан в T064, ADR-025) — **тонкий stateless singleton персиста**: принимает уже сводные данные сегмента (`PersistSegmentRunAsync`: stageId/duration/gold/xp/chests/heroLevel/heroDamage/heroClassText/completedAt), собирает `StageRun` + `HeroSnapshot` (контекст силы) → `IRunRepository.AddRunAsync` → `PruneOldRunsAsync(stageId, 10)` (ретенция ≤10 забегов на этап) → `IStageAggregateRepository.RecomputeForStageAsync`. Scoped-репозитории получает через `IServiceScopeFactory`. **Накопление gold/xp/chest и триггер записи перенесены в `StatsOrchestrator`** (граница сегмента по падению прогресса после босса — см. §9); `IStageCompletionDetector` из пайплайна `RunRecorder` удалён. `Reset()` — no-op (состояния нет).
- `OptimizationProfileService` (T040, US2) — singleton: get/save и переключение `OptimizationProfile` (метрика/`RecentWindowSize`/`Scope`) через scoped `ISettingsRepository`.
- `CompareViewModel` / `CompareStageRow` (T042, US2) — recency-aware экран сравнения (см. §7).
- `Composition.AddTbhStatsServices` — полный composition root: Data (DbContext SQLite + 3 scoped репозитория), Core (7 singleton-сервисов, вкл. `IStageAggregateCalculator`/`IOptimizationService`), Capture (singleton-сервисы вкл. `ICaptureSession→CaptureSession`, `IStageCompletionDetector`), App (singleton `IStatsOrchestrator`+`RunRecorder`+`OptimizationProfileService` через фабрики; transient ViewModel'и).

---

## 12. Расширяемость

**Декларативный `GameMechanicsConfig`** (`TBHStats.Core/Mechanics`) описывает типы сундуков, классы героев, акты/сложности/этапы, вкладки и привязки полей к источникам. Встроенный default (`GameMechanicsConfig.CreateDefault()`) + переопределение из файла/БД, сидирует справочные таблицы.

Состав дефолтного сида:
- **ChestTypes** (3): brown «Базовый», blue «Редкий», red «Легендарный».
- **Tabs** (9): hero, stash, status, runes, cube, portal, settings, tradeship, mailbox (IsDataSource: hero/status/portal).
- **Acts** (3) × **Difficulties** (2: normal/nightmare) × **Stages** (10) = **60** этапов.
- **HeroClasses** (по умолчанию содержит один «неопределённый» класс `Id=1, Key="unknown"` — FK-якорь для `StageRun.Hero.HeroClassId`; конкретные классы открываются динамически и добавляются через `Reload`). Нераспознанный OCR-текст класса героя НЕ делает забег `IsPartial=true` — класс является метаданными и не влияет на корректность gold/xp/duration.
- **FieldSourceBindings** (24): gold→hero-tab, xp/xpToLevel/xpPair/heroLevel/heroDamage/heroClass→status-tab, stageId→portal-tab, остальные→MainZone (вкл. базовые `chest:brown/blue/red` и 9 калиброванных `chest:<тип>@1..@3`, генерируемых программно — см. §4).

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

### Режимы поставки (T048)

Конфигурация в `src/TBHStats.App/TBHStats.App.csproj` (ключевые свойства):

| Свойство | Packaged (MSIX) | Unpackaged (portable) |
|----------|----------------|----------------------|
| `WindowsPackageType` | `MSIX` | `None` (default) |
| `EnableMsixTooling` | `true` | `true` |
| `AppxPackageSigningEnabled` | `false` (sideload/dev) | — |
| Bootstrap Windows App SDK | автоматически (пакет) | не требуется при наличии WindowsAppSDK runtime на машине |

**`Package.appxmanifest`** (`src/TBHStats.App/Package.appxmanifest`) — MSIX identity manifest:
- `Identity.Name`: `AlgorithmicTrade.TBHStats`
- `MinVersion`: `10.0.19041.0` (синхронизировано с `TargetPlatformMinVersion` csproj)
- `Capabilities`: только `runFullTrust` (P/Invoke, WGC, D3D11, Windows.Media.Ocr)
- Сетевые capability (`internetClient` и пр.) **отсутствуют** (FR-012)

#### Команды сборки

```powershell
# Обычная сборка / разработка (unpackaged, WindowsPackageType=None — default):
dotnet restore TBHStats.sln
dotnet build TBHStats.sln -c Debug
dotnet run --project src/TBHStats.App

# Unpackaged publish (portable, self-contained, win-x64):
dotnet publish src/TBHStats.App/TBHStats.App.csproj `
  -c Release -r win-x64 `
  -p:WindowsPackageType=None `
  -p:SelfContained=true
# Артефакты: src/TBHStats.App/bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/

# Packaged MSIX (требует MSBuild / Visual Studio, НЕ dotnet-CLI из-за WinAppSdkValidateAppxManifestItems):
# MSBuild src/TBHStats.App/TBHStats.App.csproj `
#   /p:Configuration=Release /p:Platform=x64 `
#   /p:WindowsPackageType=MSIX /p:AppxPackageSigningEnabled=false `
#   /p:GenerateAppxPackageOnBuild=true
# Артефакты: src/TBHStats.App/bin/x64/Release/net8.0-windows10.0.22621.0/TBHStats.App_*.msix
```

**Ограничение CLI-упаковки**: `Microsoft.Windows.SDK.BuildTools.MSIX` задача `WinAppSdkValidateAppxManifestItems` при `WindowsPackageType=MSIX` падает в headless `dotnet` CLI окружении с `System.Security.Permissions` (MSBuild-хост не находит сборку .NET 8). Это известный баг BuildTools.MSIX 1.7.x. Для продуктового MSIX-пакета использовать Visual Studio 2022 ≥ 17.8 или MSBuild Desktop (не dotnet-CLI). Unpackaged-сборка и `dotnet build`/`dotnet run`/`dotnet test` работают без ограничений.

#### WinRT-доступ в обоих режимах

WinRT API (`Windows.Graphics.Capture`, `Windows.Media.Ocr`, `Windows.Graphics.Imaging`) доступны из `TBHStats.Capture` в обоих режимах при TFM `net8.0-windows10.0.22621.0` через CsWinRT-проекции (`Microsoft.Windows.SDK.NET.dll`, входит в `Microsoft.WindowsAppSDK`). Это подтверждено сборкой и тестами `TBHStats.Capture.Tests` (87 тестов pass) без packaged identity.

- **Packaged**: `runFullTrust` в манифесте снимает ограничения на WinRT-API, связанные с идентичностью.
- **Unpackaged**: CsWinRT проекции работают без identity, WGC/OCR доступны напрямую. Windows App SDK bootstrap (`WindowsAppSDK` runtime) уже установлен при наличии пакета `Microsoft.WindowsAppSDK` в проекте (Microsoft.Windows.SDK.BuildTools.WinApp обеспечивает `dotnet run` поддержку).

**Последовательность запуска (T028)**:

1. `App()` — `BuildHost()`: регистрация DI (Composition.AddTbhStatsServices).
2. `OnLaunched` — создаёт `WidgetWindow`, вызывает `Activate()` синхронно (WinUI требует).
3. Асинхронно (`InitializeAsync`): `DatabaseInitializer.InitializeAsync(db)` (миграции + сидинг) → `IStatsOrchestrator.StartAsync(CancellationToken.None)`.
4. При закрытии виджета — `IStatsOrchestrator.StopAsync()`.

**Целевые показатели**: кадр + OCR одной ROI < ~150 мс; живые темпы видны ≤10 c после старта (SC-001); возобновление после перекрытия ≤5 c (SC-008); низкая idle-нагрузка CPU (захват по требованию, не непрерывный видеопоток).

**Диагностика и логи (T047, ADR-016)**:
- Логи приложения: `%LOCALAPPDATA%\TBHStats\logs\tbhstats-YYYY-MM-DD.log` (ротация по дате UTC).
- Провайдер: лёгкий `FileLoggerProvider` (собственная реализация `ILoggerProvider`, `src/TBHStats.App/Services/Logging/`); без сетевых зависимостей (FR-012).
- Минимальный уровень в файл: `Information`; уровень `Debug` — только в `Debug`-синк (разработка).
- Путь к лог-директории не раскрывается на уровне Info/Warning во избежание PII (имя пользователя в пути).

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
