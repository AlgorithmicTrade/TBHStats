# Specification Quality Checklist: TBHStats — помощник по статистике Task Bar Hero

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-05-31
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Все маркеры [NEEDS CLARIFICATION] закрыты ответами пользователя (2026-05-31):
  - **FR-003** — источник данных: визуальный захват с экрана (OCR), с калибровкой областей считывания.
  - **FR-009** — цель оптимизации: переключаемая пользователем (золото/час ↔ опыт/час), по умолчанию золото/час.
- Прочие неопределённости закрыты разумными значениями по умолчанию и зафиксированы в разделе Assumptions.
- Уточнения пользователя (2026-05-31, итерация 2) внесены:
  - **Устойчивое чтение** (FR-005/005a): перекрытие окна ≠ ошибка; ожидание видимости + пересчёт темпов только по доступным значениям.
  - **Сундуки** (FR-002/006/007/008): учёт количества по типам (коричневый/синий/красный) + темп сундуки/час.
  - **Расширяемость механик** (FR-021): декларативная конфигурация для новых сундуков/классов/этапов без переработки архитектуры.
  - **Независимость от окна/системы** (FR-005b): привязка к окну игры, работа при любом положении/масштабе/мониторе.

### Коррекция по Specification Analysis Report (2026-06-01, итерация 3)

Спецификация приведена в соответствие с реализацией и принятыми решениями по объёму:

- **C1 — E2E UI-тест-харнесс выведен из v1**: блок FR-022…FR-027 заменён пометкой об исключении; SC-012…SC-014 исключены; добавлены пункты в «Out of Scope»; раздел Assumptions «UI-тестирование (QA)» переписан. Причина: E2E-тесты признаны ненужными; продукт и тесты v1 — observe-only без инъекции ввода.
- **G1 — сундуки → deferred-P2**: FR-002 (считывание), FR-006 (сундуки/час), FR-007 (учёт в забеге), FR-008 (агрегат) помечены deferred — игровой счётчик графический, OCR не считывает; визуальный подсчёт — P2-backlog.
- **G2 — сегментация/время этапа**: FR-007 уточнён — надёжный сигнал завершения зависит от визуальной детекции прогрессбара/босса (deferred-P2); в v1 сегментация оппортунистическая.
- **G5 — `stageId`**: добавлен FR-002c + Assumption — текущий этап в v1 выводится производно (next-локация − 1); точная локализация флага Portal — deferred-P2.
- **S1 — статус**: `Draft` → `Implemented (v1, live-tuning)`.

> **Карв-аут конституции v2.2.0 (Security, QA-харнесс)** после вывода харнесса стал неприменимым к v1. Это **permissive-норма** (не нарушена), синхронизация конституции — отдельной Amendment Procedure (вне `/speckit.specify`), при желании пользователя.

- Открытых [NEEDS CLARIFICATION] нет. Спецификация согласована с `tasks.md` (Phase 7 удалена, P2-backlog T062…T068). Рассинхронизация остаётся в `plan.md` (упоминания `TBHStats.UiTests`) и `docs/` — закрывается отдельной правкой (см. рекомендации Analysis Report X1/D2).
