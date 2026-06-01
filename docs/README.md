# TBHStats — Документация

**TBHStats** — десктоп-виджет-помощник по статистике для idle RPG **«Task Bar Hero»** (Windows 11). Работает в режиме **observe-only**: визуально считывает данные из окна игры (захват экрана + OCR), не вмешиваясь в её процесс, и на их основе считает **золото/час**, **опыт/час**, **сундуки/час**, а также рекомендует оптимальный этап для фарма под выбранную цель оптимизации.

> Дата актуализации: **2026-06-01** (золото — расходуемый баланс: монотонность не гейтит надёжность сэмпла, ADR-020 + spec/research/data-model; живой темп по короткому скользящему окну 90 с `LiveRateWindowSeconds`, §9 ARCHITECTURE.md; ранее: мульти-панельный UI — снятие tab-gating + апскейл мелких кропов OCR + порог 0.02 + перезагрузка ROI каждый кадр, ADR-019; мульти-позиционные ROI сундуков по числу типов + детекция раскладки, ADR-018; объединённая ROI опыта `xpPair` с парсингом по `/`; ранее T048: конфигурация поставки MSIX/unpackaged; T047: обработка ошибок и логирование)

---

## Карта документации

Документы создаются параллельно и могут ещё дописываться — здесь приведены пути и назначение.

| Документ | Назначение |
| --- | --- |
| [project/OVERVIEW.md](project/OVERVIEW.md) | Что и зачем за продукт, объём, текущий статус и дорожная карта. |
| [project/GAME-FACTS.md](project/GAME-FACTS.md) | Авторитетный справочник по игре TBH: разделы, этапы, источники данных, механики. §12 — [справочные скриншоты интерфейса](project/GAME-FACTS.md#12-справочные-скриншоты) (`screenshots/`, визуальный референс + фикстуры OCR). |
| [project/ARCHITECTURE.md](project/ARCHITECTURE.md) | Технический стек, слои, захват экрана и OCR, структура решения, потоки данных. |
| [project/DECISIONS.md](project/DECISIONS.md) | Журнал ключевых архитектурных решений (ADR). |

---

## Спецификация фичи (SpecKit)

Артефакты спецификации первой фичи лежат в `specs/001-tbh-stats-helper/`.

| Артефакт | Назначение |
| --- | --- |
| [../specs/001-tbh-stats-helper/spec.md](../specs/001-tbh-stats-helper/spec.md) | Функциональные требования и пользовательские истории. |
| [../specs/001-tbh-stats-helper/plan.md](../specs/001-tbh-stats-helper/plan.md) | Технический план реализации. |
| [../specs/001-tbh-stats-helper/research.md](../specs/001-tbh-stats-helper/research.md) | Обоснование технических решений. |
| [../specs/001-tbh-stats-helper/data-model.md](../specs/001-tbh-stats-helper/data-model.md) | Модель данных и «Game UI Map». |
| [../specs/001-tbh-stats-helper/contracts/](../specs/001-tbh-stats-helper/contracts/) | Внутренние контракты ([services.md](../specs/001-tbh-stats-helper/contracts/services.md)) и черновик remote API ([remote-api.openapi.yaml](../specs/001-tbh-stats-helper/contracts/remote-api.openapi.yaml)). |
| [../specs/001-tbh-stats-helper/quickstart.md](../specs/001-tbh-stats-helper/quickstart.md) | Сборка и первый запуск. |

---

## Быстрые факты

- **Жанр игры:** Idle RPG (Task Bar Hero).
- **Платформа:** Windows 11.
- **Стек:** .NET 8 / C# / WinUI 3.
- **Этапы:** 60 (3 акта × 2 сложности × 10 этапов).
- **Разделы игры:** 9.
- **Источники данных:** панели **Hero / Status / Portal** + основная зона **MainZone**.
- **Цель оптимизации:** максимизация **золото/час** либо **опыт/час**.
- **Статус:** реализованы US1+US2+US3 + большая часть Polish (Phase 6); `TBHStats.sln` build 0/0, тесты Core 165 / Data 64 / Capture 90 GREEN. Остаётся live-acceptance (T051) и QA-харнесс (Phase 7).
- **Репозиторий:** GitHub `AlgorithmicTrade/TBHStats`.

---

## С чего начать новой сессии

1. Прочитать [project/OVERVIEW.md](project/OVERVIEW.md) — контекст продукта, объём и статус.
2. Прочитать [project/GAME-FACTS.md](project/GAME-FACTS.md) — устройство игры и источники данных.
3. Прочитать [project/ARCHITECTURE.md](project/ARCHITECTURE.md) — стек, слои и потоки.
4. Затем углубиться в спецификацию: [spec.md](../specs/001-tbh-stats-helper/spec.md) и [plan.md](../specs/001-tbh-stats-helper/plan.md).

**Следующий шаг разработки:** завершить **T051** (live-smoke на реальной игре, `quickstart.md`) и при необходимости начать **Phase 7** (QA-харнесс, T055–T061). Список задач и статусы — `specs/001-tbh-stats-helper/tasks.md`.
