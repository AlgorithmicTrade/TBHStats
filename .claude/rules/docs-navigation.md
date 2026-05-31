# Documentation Navigation & Maintenance

> Обязательное правило для всех агентов и сессий, работающих с кодом TBHStats.
> Цель: (a) экономия контекста — читать только релевантный кусок документации; (b) предотвращение дрейфа документации относительно кода и спецификации.

## When to apply

Применяется в двух ситуациях:

1. **ПЕРЕД** изменением, добавлением или удалением функционала в любой части проекта — agent ОБЯЗАН свериться с архитектурной документацией затрагиваемой области.
2. **ПОСЛЕ** того как изменение реализовано — agent ОБЯЗАН обновить документацию затронутой области.

Срабатывает по intent: «добавь функционал», «реализуй задачу из tasks.md», «измени логику захвата/OCR», «отрефактори Core», «исправь баг в подсчёте сундуков», «удали слой» и т.п. — не по literal-фразе.

Исключения, при которых правило НЕ применяется:
- Тривиальные правки одной строки (typo, formatting, переименование локальной переменной).
- Изменения в `.claude/`, `.specify/templates/`, `claude-code-orchestrator-kit/` (вложенный пакет, gitignored) без затрагивания production-кода.
- Работа исключительно с тестами без изменения тестируемого кода (`tests/**` при неизменном `src/**`).
- `CHANGELOG.md` / `RELEASE_NOTES.md` — генерируются `release.ps1` из conventional-commits, вручную не правятся.

**Важный нюанс проекта.** Первоисточник требований и решений — спецификация `specs/001-tbh-stats-helper/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`) и конституция `.specify/memory/constitution.md`. Документы в `docs/` — **производный** навигационный слой над ними (это прямо зафиксировано в шапке `docs/project/DECISIONS.md`). При расхождении приоритет за первоисточниками. Изменение самих требований/принципов идёт через `/speckit.*`-команды и Amendment Procedure конституции, а не ручной правкой `docs/`.

## Step 1 — Перед изменением: навигация через индекс

Корневой файл-индекс: [`docs/README.md`](../../docs/README.md). Это единственная авторизованная точка входа в документацию.

Алгоритм:

1. Открой `docs/README.md`.
2. В секции «Карта документации» / «Спецификация фичи (SpecKit)» найди документ, соответствующий твоей задаче (см. таблицу «Quick navigation» ниже).
3. Прочитай **только эти 1–3 файла**, а не всю папку `docs/` или `specs/`. Контекстный бюджет ограничен.
4. Если для области нет документа в индексе — продолжай работу по коду/спецификации (читай исходники напрямую), но в **Step 3** ОБЯЗАТЕЛЬНО создай/дополни документ в `docs/` и добавь строку-ссылку в `docs/README.md` (раздел + Карта документации).

### Quick navigation: by topic

| Область задачи | Что прочитать (документ + раздел) |
|---|---|
| Захват окна (WGC, HWND, fallback, `IsIconic`/Waiting) — `TBHStats.Capture` | `docs/project/ARCHITECTURE.md` §2 «Слой захвата» + ADR-004 |
| OCR (Windows.Media.Ocr, Tesseract fallback, формат 1.2K/3.4M) — `TBHStats.Capture` | `docs/project/ARCHITECTURE.md` §3 «OCR» + ADR-005 |
| ROI / калибровка областей считывания — `TBHStats.Capture/Roi` | `docs/project/ARCHITECTURE.md` §4 «ROI» + ADR-006 |
| Детекция активной вкладки/раздела — `TBHStats.Capture` | `docs/project/ARCHITECTURE.md` §5 «Детекция активной вкладки» |
| Хранилище, схема БД, миграции — `TBHStats.Data` | `docs/project/ARCHITECTURE.md` §6 «Хранилище» + `specs/001-tbh-stats-helper/data-model.md` + ADR-013 |
| Графики/тренды (LiveCharts2) — `TBHStats.App` | `docs/project/ARCHITECTURE.md` §7 «Графики» + ADR-014 |
| Структура решения / новый проект в `.sln` | `docs/project/ARCHITECTURE.md` §8 «Структура решения» + корневой `CLAUDE.md` |
| Поток данных, оркестрация, машина состояний захвата | `docs/project/ARCHITECTURE.md` §9–§10 |
| Внутренние контракты (`IScreenCapture`, `IOcrEngine`, `IGameDataReader`, `IRepository`…) | `docs/project/ARCHITECTURE.md` §11 + `specs/001-tbh-stats-helper/contracts/services.md` |
| Расширяемость, `GameMechanicsConfig`, справочники (ChestType/Class/Tab) — `TBHStats.Core/Mechanics` | `docs/project/ARCHITECTURE.md` §12 «Расширяемость» + ADR-009 |
| Цель оптимизации (золото/час ↔ опыт/час), ранжирование этапов — `TBHStats.Core` | `specs/001-tbh-stats-helper/spec.md` (P2) + ADR-007 |
| Подсчёт сундуков (точки MainZone, неубывающий итог забега) — `TBHStats.Core/Parsing` | `docs/project/GAME-FACTS.md` (механика сундуков) + ADR-011 |
| Сегментация забегов, детекция завершения этапа (прогрессбар/босс) | `docs/project/GAME-FACTS.md` + ADR-012 |
| Игровые факты: 60 этапов (3×2×10), 9 разделов, источники Hero/Status/Portal/MainZone | `docs/project/GAME-FACTS.md` + ADR-008 |
| Задел под Android (`TBHStats.Core` без UI, `TBHStats.Remote`, remote API) | `docs/project/ARCHITECTURE.md` §13 + `specs/001-tbh-stats-helper/contracts/remote-api.openapi.yaml` + ADR-010 |
| UI-тесты живой игры (FlaUI, визуальная локализация) — `TBHStats.UiTests` | `specs/001-tbh-stats-helper/ui-test-scenarios.md` + ADR-015 |
| Сборка / запуск / TFM / поставка | `docs/project/ARCHITECTURE.md` §14 «Поставка» + `specs/001-tbh-stats-helper/quickstart.md` |
| Объём v1, приоритеты P1/P2/P3, дорожная карта | `docs/project/OVERVIEW.md` |

**Запрещено:**
- Делать архитектурные предположения без чтения соответствующего файла из индекса.
- Читать `docs/` или `specs/` целиком «для понимания» — это перерасход контекста.
- Игнорировать ADR в `docs/project/DECISIONS.md`: если меняешь решение, зафиксированное в ADR, СНАЧАЛА прочитай этот ADR.

## Step 2 — Сверка с ADR и конституцией (architecture decisions)

В TBHStats ADR — это **единый журнал** [`docs/project/DECISIONS.md`](../../docs/project/DECISIONS.md) (записи ADR-001 … ADR-015, lightweight-формат), а не отдельные файлы. Если задача затрагивает один из аспектов — обязательно прочитай соответствующую запись ДО внесения изменений:

| Область задачи | Обязательный ADR |
|---|---|
| Технологический стек, замена UI-фреймворка, TFM | ADR-002 |
| Способ захвата окна (WGC ↔ PrintWindow ↔ BitBlt/DXGI) | ADR-004 |
| Движок OCR, выбор Windows.Media.Ocr vs Tesseract | ADR-005 |
| Координаты ROI (нормализованные доли vs пиксели) | ADR-006 |
| Метрика оптимизации, что является целью v1 | ADR-007 |
| Матрица этапов (60 = 3×2×10), идентичность номера этапа | ADR-008 |
| Способ расширения (config-driven vs enum'ы в коде), схема справочников | ADR-009 |
| Граница `TBHStats.Core` ↔ UI/WinRT, контракт `TBHStats.Remote` | ADR-002, ADR-010 |
| Логика подсчёта сундуков (транзиентные точки MainZone) | ADR-011 |
| Сигнал завершения этапа / сегментация забегов | ADR-012 |
| Выбор/замена хранилища, схема БД, миграции | ADR-013 |
| Выбор/замена чарт-библиотеки | ADR-014 |
| Архитектура UI-тест-харнесса живой игры | ADR-015 |
| Изменение принципа типобезопасности / любого принципа конституции | ADR-003 + `.specify/memory/constitution.md` |

Список ADR — в [`docs/project/DECISIONS.md` → Оглавление](../../docs/project/DECISIONS.md#оглавление).

Если задача попадает одновременно в несколько строк таблицы — читать ВСЕ перечисленные ADR. Они не альтернативны и фиксируют разные инварианты (например, вынос логики в Core под будущий Android требует и ADR-002, и ADR-010 — они дополняют друг друга).

**Конституция как инвариант.** `.specify/memory/constitution.md` (v2.2.0) — single source of truth для принципов (Context-First, Single Source of Truth, Library-First + Context7, Strict Type Safety C#/.NET, Atomic Task Execution, Quality Gates, observe-only Security + QA carve-out и т.д.). Изменение кода НЕ должно нарушать действующий принцип. Изменение самого принципа — только через Amendment Procedure (документированная причина + impact-анализ + version bump + Sync Impact Report), как это сделано в ADR-003.

## Step 3 — После изменения: обновление документации

После того как код изменён и проверен (`dotnet build` зелёный, analyzers чисты), агент ОБЯЗАН:

1. Перечитать те же документы из Step 1, которые он использовал для навигации.
2. Если поведение/контракт/структура изменились — отредактировать соответствующие документы:
   - Изменился слой захвата / OCR / ROI / детекция вкладок (`TBHStats.Capture`) → обнови соответствующий раздел `docs/project/ARCHITECTURE.md` (§2–§5).
   - Изменилась схема БД / сущности / репозитории (`TBHStats.Data`) → обнови `docs/project/ARCHITECTURE.md` §6 + `specs/001-tbh-stats-helper/data-model.md`.
   - Изменился внутренний контракт (интерфейс сервиса) → обнови `docs/project/ARCHITECTURE.md` §11 + `specs/001-tbh-stats-helper/contracts/services.md`.
   - Изменилась доменная логика темпов/ранжирования/подсчёта сундуков/сегментации забегов (`TBHStats.Core`) → обнови `docs/project/ARCHITECTURE.md` (§9 поток данных) и/или `docs/project/GAME-FACTS.md`, если затронута интерпретация игровых фактов.
   - Изменилась игровая механика / факты (этапы, разделы, источники, типы сундуков) → обнови `docs/project/GAME-FACTS.md`.
   - Изменилась схема каталогов / состав `.sln` / расположение кода → обнови `docs/project/ARCHITECTURE.md` §8 + корневой `CLAUDE.md` (раздел Active Technologies).
   - Изменился UI/виджет/графики (`TBHStats.App`) → обнови `docs/project/ARCHITECTURE.md` §7.
   - Изменилась процедура сборки/запуска/поставки → обнови `docs/project/ARCHITECTURE.md` §14 + `specs/001-tbh-stats-helper/quickstart.md`.
3. Если ввёл **новый документ** в `docs/` — добавь ссылку на него в `docs/README.md`:
   - В таблицу «Карта документации» (или «Спецификация фичи (SpecKit)» — если документ относится к спеке).
   - В таблицу «Quick navigation: by topic» этого правила, если документ описывает новую topical area.
4. Если **принимаешь новое архитектурное решение**, ломающее существующий ADR или вводящее новый инвариант — добавь новую запись `ADR-0NN` в `docs/project/DECISIONS.md`:

   **Worked example.** При добавлении ADR-016 (например, «Кэш кадров между OCR-проходами»):
   1. В `docs/project/DECISIONS.md` § «Оглавление» вставить строку после ADR-015, сохранив нумерацию:
      `- [ADR-016 — Кэш кадров между OCR-проходами](#adr-016--кэш-кадров-между-ocr-проходами)`
   2. Добавить саму запись в конец журнала по шаблону существующих ADR: **Дата · Статус · Контекст · Решение · Альтернативы/почему отклонены · Последствия**.
   3. Если запись вводит новую topical area — добавить строку в «Quick navigation: by topic» этого правила и при необходимости в `docs/README.md`.
   4. Обновить дату актуализации в шапках `docs/project/DECISIONS.md` и `docs/README.md`.
5. Обнови дату актуализации (`Дата актуализации:`) в шапке `docs/README.md` и затронутых документов `docs/project/*`.

## Anti-patterns (что НЕ делать)

- ❌ «Прочитал весь `docs/` и `specs/`, чтобы понять» — нарушение контекстного бюджета. Используй `docs/README.md` → нужный файл по «Quick navigation».
- ❌ Изменить код и забыть документацию — приведёт к дрейфу. Step 3 обязателен.
- ❌ Изменить документацию без чтения текущего состояния файла — заменяет реальный контракт на воображаемый.
- ❌ Создать новый документ в `docs/` без ссылки в `docs/README.md` — он станет «мёртвым» (не виден будущим агентам).
- ❌ Игнорировать ADR в `docs/project/DECISIONS.md` при изменении в его области — приведёт к нарушению зафиксированного решения.
- ❌ Ручная правка `spec.md`/`plan.md`/`constitution.md` в обход `/speckit.*` и Amendment Procedure — ломает spec-driven pipeline (см. ADR-003).
- ❌ Дописать поведение в `docs/` так, что оно расходится со `specs/` — `specs/` первичны; синхронизируй в их сторону.

## Verified vs unverified claims

При работе с документацией применяется общее правило `~/.claude/rules/quality.md`:

- ✅ «Захват окна делается через WGC по HWND — описано в `docs/project/ARCHITECTURE.md` §2 + ADR-004» — verified.
- ⚠️ «Кажется, OCR работает через Tesseract» — unverified до чтения `ARCHITECTURE.md` §3 / ADR-005 (там основной движок — Windows.Media.Ocr).

## Краткая шпаргалка (TL;DR)

1. **До работы:** `docs/README.md` → нужный файл по таблице «Quick navigation» + затронутый ADR в `docs/project/DECISIONS.md`. Помни: `specs/` и `constitution.md` — первоисточники, `docs/` производно.
2. **Во время работы:** ничего не предполагай — verify по тексту документа; не нарушай действующие принципы конституции.
3. **После работы:** обнови те же документы + (новый документ → ссылка в `docs/README.md`) + (новое решение → запись `ADR-0NN` в `DECISIONS.md`) + дата актуализации.
