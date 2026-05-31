# Quickstart: TBHStats

**Feature**: `001-tbh-stats-helper` | **Date**: 2026-05-31 | Stack: .NET 8 / C# / WinUI 3

> Документ-ориентир для разработчика. Код проекта появляется на этапе `/speckit.implement`.
> Сейчас репозиторий содержит только спеку/план — раздел «Сборка» описывает целевой процесс.

## Предварительные требования

- **Windows 11** (10.0.22621+); WGC и Windows.Media.Ocr — встроены.
- **.NET 8 SDK** (LTS).
- **Windows App SDK** (WinUI 3) workload: `dotnet workload install` нужных компонентов / Visual Studio 2022 c «.NET Desktop» + «Windows App SDK».
- Visual Studio 2022 17.8+ или Rider (опционально).
- Установленная игра **Task Bar Hero** для реальной калибровки/проверки захвата (без моков — quality-rules).

## Целевая структура решения

```
TBHStats.sln
src/TBHStats.Core      # домен, оптимизация, парсинг, game-mechanics config (без UI/WinRT)
src/TBHStats.Capture   # WGC по HWND, Windows.Media.Ocr, ROI-маппинг, состояние Waiting
src/TBHStats.Data      # EF Core + SQLite, репозитории, миграции
src/TBHStats.App       # WinUI 3 виджет (MVVM, графики, калибровка)
src/TBHStats.Remote    # (future) локальный API для Android — в v1 пустой задел
tests/TBHStats.*.Tests # xUnit
```

## Сборка и запуск (целевой процесс)

```powershell
# из корня репозитория
dotnet restore TBHStats.sln
dotnet build TBHStats.sln -c Debug        # должен проходить чисто (Quality Gate VII)
dotnet test  TBHStats.sln                 # Core/Data/Capture тесты (реальный SQLite, фикстуры-картинки)
dotnet run --project src/TBHStats.App     # запуск виджета (unpackaged по умолчанию)
```

Хранилище создаётся автоматически: `%LOCALAPPDATA%\TBHStats\tbhstats.db` (EF Core миграции применяются при старте).

### Режимы поставки (T048)

TBHStats поддерживает два режима сборки через MSBuild-свойство `WindowsPackageType`:

#### Unpackaged (portable, default)

Используется для `dotnet build`, `dotnet run`, `dotnet test` и portable-дистрибутива.
`WindowsPackageType=None` — значение по умолчанию в csproj.

```powershell
# Debug/dev build:
dotnet build TBHStats.sln -c Debug

# Portable publish (self-contained, win-x64):
dotnet publish src/TBHStats.App/TBHStats.App.csproj `
  -c Release -r win-x64 `
  -p:WindowsPackageType=None `
  -p:SelfContained=true
# Артефакты: src/TBHStats.App/bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/TBHStats.App.exe
```

WinRT API (WGC, Windows.Media.Ocr) доступны без package identity через CsWinRT-проекции при TFM `net8.0-windows10.0.22621.0`.

#### Packaged (MSIX)

Предпочтительный режим для финальной поставки — надёжная WinRT-идентичность, sideload/Store.
Требует **Visual Studio 2022 ≥ 17.8** или **MSBuild Desktop** (не `dotnet` CLI).

```powershell
# Через VS: правой кнопкой → Package and Publish → Create App Packages
# Через MSBuild Desktop (например из Developer PowerShell for VS 2022):
MSBuild src/TBHStats.App/TBHStats.App.csproj `
  /p:Configuration=Release /p:Platform=x64 `
  /p:WindowsPackageType=MSIX `
  /p:AppxPackageSigningEnabled=false `
  /p:GenerateAppxPackageOnBuild=true
# Артефакты: src/TBHStats.App/bin/x64/Release/net8.0-windows10.0.22621.0/AppPackages/
```

Манифест пакета: `src/TBHStats.App/Package.appxmanifest`
- Identity: `AlgorithmicTrade.TBHStats`, Publisher `CN=AppPublisher` (для sideload; заменить реальным сертификатом для Store).
- Capabilities: только `runFullTrust` — без сетевых (FR-012).

**Ограничение**: `dotnet build /p:WindowsPackageType=MSIX` завершается ошибкой MSB4018 в headless-CLI окружении из-за бага `Microsoft.Windows.SDK.BuildTools.MSIX` 1.7.x (не находит `System.Security.Permissions` в MSBuild-хосте). Для `dotnet build`/тестов всегда использовать `WindowsPackageType=None`.

## Запуск UI-тестов живой игры (QA-харнесс)

E2E-аудит интерфейса (FR-022…FR-027) — отдельный проект `tests/TBHStats.UiTests` (**FlaUI + визуальная локализация (OCR) + SendInput**, НЕ Playwright). Прогоняется против **реально запущенной** игры; не входит в поставку (продукт observe-only).

```powershell
# Требуется запущенная игра Task Bar Hero. Харнесс кликает в игре (human-like) для навигации по разделам.
# Safety-Guard запрещает необратимые действия (Runes/Cube/Stash/Trade). Не запускать во время важной игровой сессии.
dotnet test tests/TBHStats.UiTests        # сценарии TS-00…TS-10 (ui-test-scenarios.md)
```

- Пререквизит: NuGet `FlaUI.Core`, `FlaUI.UIA3` (подключаются в проекте).
- Конфиг: `ClickDelayRangeMs` (150–600), `WaitTimeoutMs` (5000), `RepeatRuns` (5), `ForbiddenElements` (запрещённые к клику).
- Детерминизм: повтор серии прогонов даёт одинаковый вердикт (SC-012).

## Первый запуск (пользовательский сценарий)

1. Запустить **Task Bar Hero**.
2. Запустить **TBHStats** — виджет находит окно игры (FR-001). Если не найдено → статус «игра не найдена» (US1 сц.4).
3. **Калибровка (один раз)**: на захваченном кадре окна разметить прямоугольники над значениями (золото, опыт, время этапа, класс/уровень/урон, текущий этап, счётчики сундуков) и над **областью названия активной вкладки**. Для каждого значения указать источник — основная зона или конкретная вкладка (FR-002b). Всё сохраняется как доли клиентской области (FR-003, R3) → переживает перемещение/масштаб (FR-005b).
4. Виджет показывает живые **золото/час, опыт/час, сундуки/час** (P1, ≤10с — SC-001).
5. Поиграть на разных этапах → накопить историю → открыть **Сравнение**: ранжирование + рекомендация (P2). Переключатель цели **золото/час ↔ опыт/час** (FR-019).
6. (P3) Открыть **Графики** для трендов по этапу.

## Проверка ключевых требований (acceptance smoke)

| Проверка | Ожидание | FR/SC |
|----------|----------|-------|
| Перекрыть окно игры другим окном | Захват продолжается (WGC ловит фон); ошибки нет | FR-005/R1 |
| Свернуть окно игры | Статус «ожидание», последние достоверные значения, без записи мусора; возобновление ≤5с после разворота | FR-005, SC-007/008 |
| Переместить/растянуть окно, сменить монитор | Считывание продолжается без перекалибровки | FR-005b, SC-009 |
| Навести курсор на число / всплывашка игры | Точка с низкой уверенностью отброшена, не записана | FR-005a/010, SC-007 |
| Открыть разные вкладки игры | Помощник определяет активную вкладку по названию; значения вкладки обновляются только когда она открыта, без ошибок | FR-002a/002b, SC-011 |
| Завершить этап | Записан `StageRun` с временем/золотом/опытом/сундуками | FR-007 |
| Перезапустить TBHStats / ПК | История на месте | FR-011, SC-004 |
| Добавить 4-й тип сундука в конфиг | Учитывается без правки кода/схемы, старая история валидна | FR-021, SC-010 |

## Заметки по реализации

- **WGC**: `GraphicsCaptureItem` из HWND через `IGraphicsCaptureItemInterop`; кадр → `SoftwareBitmap` → ROI-кроп → OCR. Свёрнутое окно (`IsIconic`) = `Waiting`.
- **OCR**: основной `Windows.Media.Ocr`; per-ROI fallback на Tesseract (whitelist цифр) при низкой точности.
- **Поставка**: предпочтительно **MSIX (packaged)** для надёжного WinRT-доступа; unpackaged/self-contained — опционально (R6).
- **MVVM**: `CommunityToolkit.Mvvm`. **Графики**: `LiveCharts2`.
- **Тесты**: парсер чисел и оптимизация — чистый unit; Data — на реальном временном SQLite; OCR — на сохранённых скриншотах игры (фикстуры), без моков распознавания.
