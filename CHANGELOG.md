# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.2] - 2026-05-31

### Added
- **US1**: реализовать живую статистику текущего забега (Phase 3, T019–T031) (efd7c2a)

## [0.1.1] - 2026-05-31

### Added
- **Foundational**: завершить Phase 2 — доменные history-модели, EF Core/SQLite, слой захвата WGC/OCR (666a4c4)
- **Capture**: добавить RoiMapper нормализованных долей ↔ пикселей (T015) (0843d7a)
- **Core**: добавить IValueParser для idle-чисел, времени и id этапа (T008) (4d298d8)
- **Capture**: реализовать IGameWindowTracker поиска и видимости окна игры (T012) (e334bb9)
- **Core**: добавить GameMechanicsConfig с дефолтным сидом и IGameMechanics (T007) (f573642)
- **Core**: добавить справочники, config-модели и доменные енумы (T006) (d482477)
- **Core**: добавить доменные енумы и value-объект StageRef (T005) (7455e0b)

## [0.1.0] - 2026-05-31

### Added
- **release**: добавить .NET release-автоматизацию (release.ps1 + /release) (0315409)
- **Setup**: инициализировать .NET 8 решение TBHStats (Phase 1) (cb92934)

### Other
- **spec**: добавить спецификацию и документацию проекта TBHStats (feature 001) (2f1f016)
- @ chore(repo): инициализировать репозиторий TBHStats (fdb253b)

