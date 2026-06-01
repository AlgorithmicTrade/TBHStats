# Архитектура TBHStats

**Проект**: TBHStats — десктоп-помощник по статистике для игры Task Bar Hero
**Платформа**: Windows 11 (x64/arm64), один локальный пользователь
**Дата актуализации**: 2026-06-01 (Issue E — CalibrationViewModel: поддержка проверки chestZone-ROI через IChestZoneAnalyzer: показывает все найденные плашки + число точек; ранее: ADR-023 — зонный детектор плашек `IChestZoneAnalyzer`/`ChestZoneAnalyzer`: одна ROI `chestZone` охватывает всю группу плашек; горизонтальная сегментация по цвету колонок; счёт точек по отфильтрованным строкам + агрегированный профиль + run-алгоритм; приоритетный путь в `FieldExtractor`; per-ROI `IChestPanelAnalyzer`-путь сохранён как fallback; §4/§11 + Capture.Tests: 2 новых теста на chests.jpg/main.jpg; ранее: Issue C — проверка chest-ROI в калибровке: `TestSelectedRoiOcr` для FieldKey `chest:*` вызывает `IChestPanelAnalyzer` вместо OCR, показывает тип + число точек, §7; ADR-022 — тип сундука по цвету плашки `IChestPanelAnalyzer`, фикс фантомных значений, FieldExtractor убирает IChestLayoutResolver из пайплайна, §3/§4/§11; T062 — визуальный детектор точек сундуков `IChestDotCounter`/`ChestDotCounter` в `TBHStats.Capture/Chests/`: chest-ROI больше не вызывают OCR, счёт точек по яркости пикселей (run-ы тёмных колонок), §3/§4 + ADR-021; ранее: золото — расходуемый баланс, валидатор не гейтит sanity по убыванию золота, §9; ранее: короткое скользящее окно живых темпов `LiveRateWindowSeconds=90`: `ComputeLiveRates` теперь получает срез ≤90 с вместо всего буфера — быстрая сходимость и отзывчивость к смене этапа, §9; ранее: детект выброса ставки опыт/ч: `RateOutlierDetector` в Core (`raw > max(EMA×6, 5M)` → отброс кадра + сброс буфера без обнуления EMA) + проводка в `StatsOrchestrator` с guard `buffer.Count > 1`, §9; ранее: надёжный детектор смены героя: `HeroSwitchDetector` в Core (падение уровня + смена XpToLevel без level-up-сигнатуры) + диаг-лог спайков XpPerHour в `StatsOrchestrator`, §9; ранее: фикс OCR: паддинг мелких кропов перед апскейлом — `OcrPaddingPixels=6` при `min(w,h) < PaddingThreshold=40`, предотвращает misread запятой при агрессивном апскейле Fant, §3; ранее: сброс окна живых темпов при смене класса героя: `StatsOrchestrator.ResolveHeroClassKey` + belt-and-suspenders сброс `_reliableBuffer`/EMA при смене нормализованного ключа, §9; ранее: мульти-панельный UI — снятие tab-gating, чтение всех полей каждый кадр, апскейл мелких кропов OCR, порог уверенности 0.02, перезагрузка ROI каждую итерацию, кэш-кроп без перекодирования кадра, запятая=десятичная, «Этап»=nextLocation−1, сундуки-точки графические → счёт по изображению P2, отключён always-on-top, виджет-строка «До уровня», EMA-сглаживание темпов + guard level-up/невозможного xp + guard разрыва XP (смена героя / потолок уровня) — §3/§5/§9 + ADR-019; мульти-позиционные ROI сундуков по числу типов `N` и детекция раскладки `IChestLayoutResolver`, §4/§9 + ADR-018; объединённая зона опыта `xpPair` с парсингом по `/`, §3/§9; структурный guard по уровню героя: разрыв детектируется по `levelDelta != 0 && levelDelta != 1`, ловит смену героя в late-game независимо от магнитуды XP-дельты, §9; ранее: T008/T015/T014/T013/T009/T010/T022/T023/T024/T025/T026/T028/T035–T042 US2 recency-aware; T043–T046 US3 тренды/ретенция; T047 обработка ошибок и логирование; T048 конфигурация поставки MSIX/unpackaged; T053 accessibility-проход; фикс старта виджета и захвата: XAML-кисти WinUI 3, static-init `GameWindowTrackerOptions`, CsWinRT-маршалинг WGC/D3D11 interop, подсказки заголовка без `"TBH"`; калибровка ROI: живой кадр через общий ICaptureSession + ScrollViewer-зум/панорамирование + рисование рамки мышью по пикселям кадра)
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

**Реализация (T014):**
- `CapturedFrame` (sealed, IDisposable) — общий тип кадра слоя: `SoftwareBitmap Bitmap`, `SizePx ClientSize`, `DateTimeOffset TimestampUtc`. Возвращается `ICaptureSession`, потребляется OCR/детектором/экстрактором. `Dispose()` освобождает `Bitmap`.
- `OcrResult` (readonly record struct) — `(string RawText, double Confidence, bool Recognized)`.
- `IOcrReader` — `Task<OcrResult> ReadAsync(CapturedFrame, RoiCalibration, CancellationToken)`.
- `OcrReader` — реальная реализация:
  - Lazy-init `WinOcrEngine` (`TryCreateFromUserProfileLanguages` → fallback `TryCreateFromLanguage("en")`). Если оба null — возвращает `OcrResult("", 0, false)` без исключения.
  - Кроп `SoftwareBitmap` к ROI через `BitmapEncoder` (BMP, in-memory stream) + `BitmapDecoder` с `BitmapTransform.Bounds` — извлекает суб-регион без ручного попиксельного копирования. Конвертация к `Bgra8/Premultiplied` через `SoftwareBitmap.Convert` при необходимости.
  - **Предобработка мелких кропов (ADR-019):** Windows.Media.Ocr не распознаёт слишком маленькие изображения (мелкие поля gold/xp/heroLevel высотой 18–27px → пусто). Если меньшая сторона кропа < `MinOcrDimension=96` — апскейл целочисленным множителем (cap по `OcrEngine.MaxImageDimension`, интерполяция Fant) перед `RecognizeAsync`. Подтверждено на живой игре + Microsoft Q&A. Дополнительно: если `min(w,h) < PaddingThreshold=40` (очень тесный кроп, апскейл ×3+), перед апскейлом добавляется `OcrPaddingPixels=6` пикселей однотонного тёмного паддинга вокруг кропа — это предотвращает искажение граничных глифов (запятая, точка) при агрессивном апскейле Fant-интерполяцией (эмпирически подтверждено на overall_priest.jpg: без паддинга запятая «126,9» читается как «;»).
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
| `Migrations/20260531155141_AddRecencyAwareAggregation.cs` | Аддитивная миграция (US2, T038/S1b): recency-aware поля `StageAggregates` (Recent* + power-context `RecentHeroLevel/Damage Min/Max`), `StageAggregateChestRates.RecentRatePerHour`, `OptimizationProfiles.RecentWindowSize`/`Scope`. Только `AddColumn` — история не теряется (FR-013) |
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
string dbConnectionString = DatabaseInitializer.GetConnectionString(DatabaseInitializer.GetDbPath());
services.AddDbContext<TbhStatsDbContext>(
    options => options.UseSqlite(dbConnectionString),
    ServiceLifetime.Scoped);
// При старте:
await DatabaseInitializer.InitializeAsync(db, ct);
```

---

## 7. Графики и UI-виджет

### Виджет живой статистики (T028, US1)

Стартовое окно приложения — `WidgetWindow` (`TBHStats_App.Views.WidgetWindow`):

- Наследует `Window` (Windows App SDK), namespace `TBHStats_App` (как `MainWindow`).
- DataContext корневого Grid задаётся из DI: `App.Services.GetRequiredService<LiveStatsViewModel>()`.
- Стартовый размер 320×220 px (SC-006: ≤15% экрана); позиция/размер/AlwaysOnTop восстанавливаются из `WidgetSettings` через `AppWindow.MoveAndResize` + `OverlappedPresenter.IsAlwaysOnTop`.
- При изменении размера/позиции (AppWindow.Changed) — сохранение в `WidgetSettings` через отдельный scope (дедупликация, задержка 500 мс).
- При закрытии виджета — `IStatsOrchestrator.StopAsync()`.
- Кнопка «Калибровка» открывает `CalibrationHostWindow` — отдельное окно-хост с Frame.Navigate(`CalibrationView`).
  - `CalibrationViewModel` получает **общий singleton `ICaptureSession`** (тот же, что у оркестратора; доступ сериализован семафором сессии). Команда `CaptureFrame` делает снимок окна игры (`TryGetFrameAsync`), конвертирует `SoftwareBitmap`→`SoftwareBitmapSource` (BGRA8 Premultiplied) на UI-потоке и кладёт в `FrameImage` + `FrameWidthPx/FrameHeightPx`. Авто-захват при открытии страницы; кнопка «Захватить кадр» — повторный снимок. VM также получает **`IChestPanelAnalyzer`** (singleton, тот же экземпляр `ChestDotCounter`, что и в `FieldExtractor`).
  - Кадр показывается в `ScrollViewer` (`ZoomMode="Enabled"`, MinZoom 0.1 / MaxZoom 16) с панорамированием; контент `PreviewContent` имеет размер кадра в пикселях (`Width/Height ← FrameWidthPx/FrameHeightPx`), `Image` `Stretch="Fill"`. Масштаб: Ctrl+колесо / кнопки «−/Вписать/+»; при захвате кадр авто-вписывается (`FitToView` через `DispatcherQueue`).
  - ROI задаётся **рисованием рамки мышью** прямо по кадру (`CalibrationView.xaml.cs`: PointerPressed/Moved/Released на `PreviewContent`). Координаты ROI — это **доли от размера контента**: `SelectedItem.X = pixel / RoiOverlayCanvas.ActualWidth` и т.п. (letterbox не нужен — контент совпадает с кадром по пропорциям; зум/панорамирование учитываются автоматически, т.к. `GetCurrentPoint(RoiOverlayCanvas)` возвращает координаты в системе контента). Оверлей лежит внутри зумируемого контента → рамки масштабируются вместе с кадром. Числовые поля X/Y/W/H остаются для тонкой правки (живая перерисовка оверлея).
  - **Команда «Проверить» (TestSelectedRoiOcr)**: три ветки:
      - FieldKey `"chestZone"` → `IChestZoneAnalyzer.AnalyzeZoneAsync`: отображается «<Тип>: <N>, ...» для каждой найденной плашки и «Зона: распознано плашек — N»; при пустом результате — подсказка «ROI должна покрывать всю группу плашек (по горизонтали и с точками снизу)». **Рекомендуется** для калибровки основного пути ADR-023: нарисовать одну зону на всю горизонтальную группу плашек вместо отдельных `chest:*@N` ROI.
      - FieldKey с префиксом `chest:` (legacy per-плашечный путь) → `IChestPanelAnalyzer.AnalyzeChestPanelAsync`: отображается тип + число точек одной плашки.
      - Остальные поля — OCR-путь (`IOcrReader.ReadAsync`).
- Кнопка «Сравнение» открывает `CompareHostWindow` — окно-хост с Frame.Navigate(`CompareView`) (US2, T041).
- Кнопка «Графики» открывает `ChartsHostWindow` — окно-хост с Frame.Navigate(`ChartsView`) (US3, T045).
- **Keyboard accelerators (T053 A11y)**: Alt+G — графики, Alt+C — сравнение, Alt+K — калибровка. Все кнопки имеют `AutomationProperties.Name` и `AutomationProperties.AutomationId`.

Визуальные состояния (T031):
- `IsGameFound == false` → красная плашка «Игра не найдена».
- `IsWaiting == true` → жёлтая плашка «Ожидание».
- `IsStale == true` → метка времени последнего обновления приглушена; поле `LastUpdateText`.

### Экран сравнения этапов (T041/T042, US2, recency-aware)

`CompareView` (`TBHStats_App.Views`) + `CompareViewModel`:
- Таблица всех этапов с историей, ранжированных `IOptimizationService` по выбранной метрике (золото/час ↔ опыт/час, FR-009/019) и `AggregationScope` (свежее окно / вся история).
- Рекомендованный этап помечен «★»; «устаревшие» забеги (сила окна заметно ниже текущей силы отряда из `IStatsOrchestrator.Current`) — пометкой «⚠ устар.» (текст, не только цвет — A11y).
- Контекст силы окна (диапазон уровня/урона выбранного героя) показывается в строке (`PowerText`).
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
 завершение этапа (прогрессбар + босс, T035) ──────────────────► AddRunAsync → RecomputeForStageAsync (P2)
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

**Реализация (T026)**:
- `LiveStatsSnapshot` (sealed record) — `src/TBHStats.App/Services/LiveStatsSnapshot.cs`: поля `CaptureState State`, `LiveRates Rates`, `long? Gold`, `int? HeroLevel`, `string? HeroClass`, `long? HeroDamage`, `StageRef? Stage`, `DateTime? LastReliableUtc`, `bool IsStale`. Статик `Empty` — начальное значение.
- `IStatsOrchestrator` — `src/TBHStats.App/Services/IStatsOrchestrator.cs`: `LiveStatsSnapshot Current`, `event EventHandler<LiveStatsSnapshot>? SnapshotUpdated`, `Task StartAsync(CancellationToken)`, `Task StopAsync()`.
- `StatsOrchestrator` — `src/TBHStats.App/Services/StatsOrchestrator.cs`: singleton, зависимости через DI (вкл. `IOcrReader` для диагностики). `_current` volatile (запись через `_current = snapshot`; WinUI-приложение single-writer). Буфер `_reliableBuffer` ограничен `MaxReliableBufferSize=200`. `ConfidenceThreshold=0.02` (ADR-019). `StaleThresholdSeconds=30`. `LiveRateWindowSeconds=90` (см. «Короткое скользящее окно темпов» ниже). ROI перечитываются каждую итерацию (калибровка применяется без перезапуска).

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
- `IStageCompletionDetector` / `StageCompletionDetector` — детектор завершения этапа (FR-002, ADR-012, T035). Детерминированная машина состояний без WinRT-зависимостей; потребляет `RawObservation`, возвращает `StageCompletionEvent?`.
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
- `RunRecorder` (T036, US2) — stateful singleton: накапливает gold/xp (с компенсацией level-up)/chest-дельты между стартом и завершением забега (по `IStageCompletionDetector`), на завершении собирает `StageRun` + `HeroSnapshot` (контекст силы) → `IRunRepository.AddRunAsync` → `IStageAggregateRepository.RecomputeForStageAsync`. Scoped-репозитории получает через `IServiceScopeFactory`. Интегрирован в петлю `StatsOrchestrator` (опц. 9-й параметр). Ограничение v1: триггер завершения зависит от визуальных полей MainZone (null до калибровки T049).
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
- **HeroClasses** (пустой по умолчанию — классы открываются динамически и добавляются через `Reload`).
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
