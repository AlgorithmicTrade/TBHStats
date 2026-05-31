#requires -Version 7.0
<#
.SYNOPSIS
    Release-автоматизация для .NET-проекта TBHStats (PowerShell-аналог release.sh из kit).

.DESCRIPTION
    - Источник версии: <Version> в Directory.Build.props (единая для всех проектов решения).
    - Авто-определение типа бампа из conventional-commits (feat -> minor, fix/security -> patch, type! / BREAKING CHANGE -> major).
    - Генерация CHANGELOG.md (Keep a Changelog) и RELEASE_NOTES.md (user-facing) из коммитов с прошлого тега.
    - Создаёт release-commit `chore(release): vX.Y.Z`, аннотированный тег vX.Y.Z и пушит (--follow-tags).
    - Безопасный rollback при ошибке (бэкапы файлов, удаление тега, reset --soft).
    - Первый релиз (нет тегов) по умолчанию = 0.1.0 (см. -FirstVersion).

.PARAMETER BumpType
    Тип бампа: patch | minor | major. Если не задан — авто-детект из коммитов.

.PARAMETER Version
    Явная версия X.Y.Z (перекрывает BumpType и авто-детект).

.PARAMETER FirstVersion
    Версия для самого первого релиза, когда тегов ещё нет. По умолчанию 0.1.0.

.PARAMETER Yes
    Не спрашивать подтверждение (для автоматизации / slash-команды).

.PARAMETER DryRun
    Только показать превью; ничего не менять, не коммитить, не пушить.

.PARAMETER NoPush
    Сделать commit и tag локально, но НЕ пушить в origin.

.EXAMPLE
    pwsh -File scripts/release.ps1            # авто-детект бампа
    pwsh -File scripts/release.ps1 minor -Yes # принудительно minor, без подтверждения
    pwsh -File scripts/release.ps1 -Version 0.1.0 -Yes
    pwsh -File scripts/release.ps1 -DryRun     # превью без изменений
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('patch', 'minor', 'major')]
    [string]$BumpType,

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$FirstVersion = '0.1.0',

    [switch]$Yes,
    [switch]$DryRun,
    [switch]$NoPush
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# === Кодировка ===
# Гарантируем UTF-8 при выводе и при чтении stdout нативных команд (git).
# Без этого кириллица из commit-сообщений ломается: PowerShell декодирует
# UTF-8-байты git в OEM-кодировке консоли (cp866/cp1251). Setter [Console]
# может бросать при перенаправленном выводе (нет реальной консоли) — оборачиваем.
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    [Console]::InputEncoding  = [System.Text.UTF8Encoding]::new($false)
} catch { }   # headless/redirected — не критично, чтение git идёт через Invoke-GitUtf8

# === Пути и константы ===
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$PropsFile = Join-Path $ProjectRoot 'Directory.Build.props'
$ChangelogFile = Join-Path $ProjectRoot 'CHANGELOG.md'
$ReleaseNotesFile = Join-Path $ProjectRoot 'RELEASE_NOTES.md'
$Date = Get-Date -Format 'yyyy-MM-dd'

# Состояние для rollback
$script:CreatedCommit = $false
$script:CreatedTag = $null
$script:Backups = @{}   # original path -> backup path
$script:CreatedFiles = [System.Collections.Generic.List[string]]::new()  # файлы, созданные в этот запуск (для отката)

# === Логирование ===
function Write-Info    { param([string]$m) Write-Host "[i]  $m" -ForegroundColor Blue }
function Write-Ok      { param([string]$m) Write-Host "[ok] $m" -ForegroundColor Green }
function Write-Warn    { param([string]$m) Write-Host "[!]  $m" -ForegroundColor Yellow }
function Write-Err     { param([string]$m) Write-Host "[x]  $m" -ForegroundColor Red }

# UTF-8 без BOM
function Set-FileContentUtf8 {
    param([string]$Path, [string]$Content)
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
}

# Запуск git с детерминированным чтением stdout как UTF-8 (не зависит от
# кодировки консоли и работает при перенаправленном выводе). Возвращает строки.
function Invoke-GitUtf8 {
    param([string[]]$GitArgs)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'git'
    foreach ($a in $GitArgs) { [void]$psi.ArgumentList.Add($a) }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $psi.UseShellExecute        = $false
    $psi.WorkingDirectory       = $ProjectRoot
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd()
    [void]$p.StandardError.ReadToEnd()
    $p.WaitForExit()
    return ($out -split "`r?`n")
}

function Backup-File {
    param([string]$Path)
    if (Test-Path $Path) {
        $bak = "$Path.relbak"
        Copy-Item -LiteralPath $Path -Destination $bak -Force
        $script:Backups[$Path] = $bak
    }
}

function Restore-Backups {
    foreach ($orig in $script:Backups.Keys) {
        $bak = $script:Backups[$orig]
        if (Test-Path $bak) { Move-Item -LiteralPath $bak -Destination $orig -Force }
    }
}

function Clear-Backups {
    foreach ($bak in $script:Backups.Values) {
        if (Test-Path $bak) { Remove-Item -LiteralPath $bak -Force }
    }
    $script:Backups = @{}
}

function Invoke-Rollback {
    param([string]$Reason)
    Write-Err "Ошибка релиза: $Reason"
    Write-Warn 'Откат изменений...'
    if ($script:CreatedTag) {
        git tag -d $script:CreatedTag 2>$null | Out-Null
        Write-Ok "Удалён тег $script:CreatedTag"
    }
    if ($script:CreatedCommit) {
        git reset --soft HEAD~1 2>$null | Out-Null
        Write-Ok 'Откат release-коммита (working tree сохранён)'
    }
    Restore-Backups
    foreach ($f in $script:CreatedFiles) {
        if ((Test-Path $f) -and -not $script:Backups.ContainsKey($f)) {
            Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue
        }
    }
    Write-Info 'Откат завершён, файлы восстановлены из бэкапов.'
}

# === Git helpers ===
function Get-LastTag {
    $tags = @(git tag --sort=-version:refname 2>$null | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' })
    if ($tags.Count -gt 0) { return $tags[0] }
    return $null
}

# === Чтение/запись версии в Directory.Build.props ===
function Get-CurrentVersion {
    if (-not (Test-Path $PropsFile)) { return $null }
    $raw = Get-Content -LiteralPath $PropsFile -Raw
    if ($raw -match '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>') { return $Matches[1] }
    return $null
}

function Set-PropsVersion {
    param([string]$NewVersion)
    Backup-File $PropsFile
    $raw = Get-Content -LiteralPath $PropsFile -Raw
    if ($raw -match '<Version>\s*[0-9]+\.[0-9]+\.[0-9]+\s*</Version>') {
        $raw = [regex]::Replace($raw, '<Version>\s*[0-9]+\.[0-9]+\.[0-9]+\s*</Version>', "<Version>$NewVersion</Version>")
    }
    elseif ($raw -match '(<PropertyGroup>\s*\r?\n)') {
        # вставить <Version> первым свойством в первый PropertyGroup
        $raw = [regex]::Replace($raw, '(<PropertyGroup>\s*\r?\n)', "`$1    <Version>$NewVersion</Version>`r`n", 1)
    }
    else {
        throw "Не удалось найти <PropertyGroup> в $PropsFile для вставки <Version>."
    }
    Set-FileContentUtf8 -Path $PropsFile -Content $raw
}

# === Парсинг коммитов ===
function Get-CommitsSince {
    param([string]$LastTag)
    $range = if ($LastTag) { "$LastTag..HEAD" } else { 'HEAD' }
    # %h<TAB>%s — читаем через Invoke-GitUtf8 (детерминированный UTF-8, иначе кириллица ломается)
    $lines = Invoke-GitUtf8 @('log', '--format=%h%x09%s', $range)
    return @($lines | Where-Object { $_ -and $_.Trim() })
}

$ConvRe = '^(?<type>[a-z]+)(\((?<scope>[^)]+)\))?(?<bang>!)?:\s*(?<desc>.+)$'

function Get-Categorized {
    param([string[]]$Commits)
    $cat = [ordered]@{
        breaking = [System.Collections.Generic.List[object]]::new()
        security = [System.Collections.Generic.List[object]]::new()
        feat     = [System.Collections.Generic.List[object]]::new()
        fix      = [System.Collections.Generic.List[object]]::new()
        refactor = [System.Collections.Generic.List[object]]::new()
        perf     = [System.Collections.Generic.List[object]]::new()
        other    = [System.Collections.Generic.List[object]]::new()
        all      = [System.Collections.Generic.List[object]]::new()
    }
    foreach ($line in $Commits) {
        $parts = $line -split "`t", 2
        if ($parts.Count -lt 2) { continue }
        $hash = $parts[0]; $subject = $parts[1]
        $type = $null; $scope = $null; $desc = $subject; $bang = $false
        if ($subject -match $ConvRe) {
            $type = $Matches['type']; $scope = $Matches['scope']; $desc = $Matches['desc']
            $bang = [bool]$Matches['bang']
        }
        $item = [pscustomobject]@{ Hash = $hash; Type = $type; Scope = $scope; Desc = $desc; Subject = $subject }
        $cat.all.Add($item)
        if ($bang -or $subject -match 'BREAKING CHANGE') { $cat.breaking.Add($item) }
        elseif ($type -eq 'security') { $cat.security.Add($item) }
        elseif ($type -eq 'feat')     { $cat.feat.Add($item) }
        elseif ($type -eq 'fix')      { $cat.fix.Add($item) }
        elseif ($type -eq 'refactor') { $cat.refactor.Add($item) }
        elseif ($type -eq 'perf')     { $cat.perf.Add($item) }
        else                          { $cat.other.Add($item) }
    }
    return $cat
}

# === Определение бампа и расчёт версии ===
function Get-BumpType {
    param($Cat)
    if ($BumpType) { return @{ Type = $BumpType; Reason = 'Указан вручную' } }
    if ($Cat.breaking.Count -gt 0) { return @{ Type = 'major'; Reason = "$($Cat.breaking.Count) breaking change(s)" } }
    if ($Cat.feat.Count -gt 0)     { return @{ Type = 'minor'; Reason = "$($Cat.feat.Count) feature(s)" } }
    if ($Cat.security.Count -gt 0) { return @{ Type = 'patch'; Reason = "$($Cat.security.Count) security fix(es)" } }
    if ($Cat.fix.Count -gt 0)      { return @{ Type = 'patch'; Reason = "$($Cat.fix.Count) bug fix(es)" } }
    return @{ Type = 'patch'; Reason = 'По умолчанию (нет conventional-commits)' }
}

function Step-Version {
    param([string]$Current, [string]$Type)
    $p = $Current -split '\.'
    $maj = [int]$p[0]; $min = [int]$p[1]; $pat = [int]$p[2]
    switch ($Type) {
        'major' { $maj++; $min = 0; $pat = 0 }
        'minor' { $min++; $pat = 0 }
        'patch' { $pat++ }
    }
    return "$maj.$min.$pat"
}

# === Форматирование строк changelog ===
function Format-ChangelogLine {
    param($Item, [string]$Prefix = '')
    if ($Item.Scope) { return "- $Prefix**$($Item.Scope)**: $($Item.Desc) ($($Item.Hash))" }
    return "- $Prefix$($Item.Desc) ($($Item.Hash))"
}

function Format-UserLine {
    param($Item)
    $msg = $Item.Desc
    if ($msg.Length -ge 1) { $msg = $msg.Substring(0,1).ToUpper() + $msg.Substring(1) }
    if ($Item.Scope) { return "- **$($Item.Scope)**: $msg" }
    return "- $msg"
}

function New-ChangelogEntry {
    param([string]$Ver, $Cat)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("## [$Ver] - $Date")
    [void]$sb.AppendLine('')
    function Add-Section { param([string]$Title, $Items, [string]$Prefix='')
        if ($Items.Count -gt 0) {
            [void]$sb.AppendLine("### $Title")
            foreach ($it in $Items) { [void]$sb.AppendLine((Format-ChangelogLine $it $Prefix)) }
            [void]$sb.AppendLine('')
        }
    }
    Add-Section 'Security' $Cat.security
    Add-Section 'Added' $Cat.feat
    # Changed = breaking + refactor + perf
    if ($Cat.breaking.Count -gt 0 -or $Cat.refactor.Count -gt 0 -or $Cat.perf.Count -gt 0) {
        [void]$sb.AppendLine('### Changed')
        foreach ($it in $Cat.breaking) { [void]$sb.AppendLine((Format-ChangelogLine $it 'BREAKING: ')) }
        foreach ($it in $Cat.refactor) { [void]$sb.AppendLine((Format-ChangelogLine $it)) }
        foreach ($it in $Cat.perf)     { [void]$sb.AppendLine((Format-ChangelogLine $it)) }
        [void]$sb.AppendLine('')
    }
    Add-Section 'Fixed' $Cat.fix
    Add-Section 'Other' $Cat.other
    return $sb.ToString()
}

function New-ReleaseNotesEntry {
    param([string]$Ver, $Cat)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("## v$Ver")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("_Релиз от ${Date}_")
    [void]$sb.AppendLine('')
    function Add-Section { param([string]$Title, $Items)
        if ($Items.Count -gt 0) {
            [void]$sb.AppendLine("### $Title")
            [void]$sb.AppendLine('')
            foreach ($it in $Items) { [void]$sb.AppendLine((Format-UserLine $it)) }
            [void]$sb.AppendLine('')
        }
    }
    Add-Section 'Новые возможности' $Cat.feat
    $improv = [System.Collections.Generic.List[object]]::new()
    $Cat.perf | ForEach-Object { $improv.Add($_) }
    $Cat.refactor | ForEach-Object { $improv.Add($_) }
    Add-Section 'Улучшения' $improv
    Add-Section 'Безопасность' $Cat.security
    Add-Section 'Исправления' $Cat.fix
    Add-Section 'Несовместимые изменения' $Cat.breaking
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("_Сгенерировано автоматически из $($Cat.all.Count) коммит(ов)._")
    return $sb.ToString()
}

# === Обновление файлов ===
function Update-Changelog {
    param([string]$Ver, $Cat)
    $entry = New-ChangelogEntry $Ver $Cat
    if (Test-Path $ChangelogFile) {
        Backup-File $ChangelogFile
        $existing = Get-Content -LiteralPath $ChangelogFile -Raw
        if ($existing -match '(?m)^## \[Unreleased\].*$') {
            $idx = $existing.IndexOf($Matches[0]) + $Matches[0].Length
            $new = $existing.Substring(0, $idx) + "`r`n`r`n" + $entry.TrimEnd() + $existing.Substring($idx)
            Set-FileContentUtf8 -Path $ChangelogFile -Content $new
        }
        else {
            $new = $existing.TrimEnd() + "`r`n`r`n" + $entry
            Set-FileContentUtf8 -Path $ChangelogFile -Content $new
        }
    }
    else {
        $header = @"
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

$entry
"@
        Set-FileContentUtf8 -Path $ChangelogFile -Content $header
        $script:CreatedFiles.Add($ChangelogFile)
    }
}

function Update-ReleaseNotes {
    param([string]$Ver, $Cat)
    $entry = New-ReleaseNotesEntry $Ver $Cat
    if (Test-Path $ReleaseNotesFile) {
        Backup-File $ReleaseNotesFile
        $existing = Get-Content -LiteralPath $ReleaseNotesFile -Raw
        # вставить новый блок сразу после заголовка, перед первым "## v"
        $m = [regex]::Match($existing, '(?m)^## v\d+\.\d+\.\d+')
        if ($m.Success) {
            $new = $existing.Substring(0, $m.Index) + $entry + "`r`n" + $existing.Substring($m.Index)
        }
        else {
            $new = $existing.TrimEnd() + "`r`n`r`n" + $entry
        }
        Set-FileContentUtf8 -Path $ReleaseNotesFile -Content $new
    }
    else {
        $header = @"
# Release Notes

User-facing release notes for all versions.

$entry
"@
        Set-FileContentUtf8 -Path $ReleaseNotesFile -Content $header
        $script:CreatedFiles.Add($ReleaseNotesFile)
    }
}

# ============================ MAIN ============================
try {
    Push-Location $ProjectRoot

    Write-Host ''
    Write-Host '=== TBHStats Release (.NET) ===' -ForegroundColor Cyan
    Write-Host ''

    # Pre-flight
    if (-not (Test-Path $PropsFile)) { throw "Не найден $PropsFile (нужен для версии)." }

    $branch = (git branch --show-current).Trim()
    if (-not $branch) { throw 'Detached HEAD — переключитесь на ветку.' }
    Write-Ok "Ветка: $branch"

    if (-not (git remote -v | Select-String -SimpleMatch 'origin')) { throw "Не настроен remote 'origin'." }

    $lastTag = Get-LastTag
    $current = Get-CurrentVersion
    if (-not $current) { $current = '0.0.0' }

    if ($lastTag) {
        Write-Ok "Последний тег: $lastTag"
        $tagVer = $lastTag.TrimStart('v')
        if ($current -ne $tagVer) {
            Write-Warn "Версия в Directory.Build.props ($current) != тег ($tagVer) — синхронизирую."
            $current = $tagVer
        }
    }
    else {
        Write-Warn 'Тегов нет — это первый релиз.'
    }
    Write-Ok "Текущая версия: $current"

    # Коммиты (обёртка @() обязательна: пустой массив из функции PowerShell разворачивается в $null,
    # и $commits.Count под StrictMode упал бы вместо понятного сообщения «релизить нечего»)
    $commits = @(Get-CommitsSince $lastTag)
    if ($commits.Count -eq 0) { throw "Нет коммитов с прошлого релиза ($lastTag). Релизить нечего." }
    Write-Ok "Коммитов с прошлого релиза: $($commits.Count)"
    $cat = Get-Categorized $commits

    # Новая версия
    if ($Version) {
        $newVersion = $Version
        $reason = 'Указана явно (-Version)'
    }
    elseif (-not $lastTag) {
        $newVersion = $FirstVersion
        $reason = "Первый релиз (-FirstVersion $FirstVersion)"
    }
    else {
        $bump = Get-BumpType $cat
        $newVersion = Step-Version $current $bump.Type
        $reason = "$($bump.Type): $($bump.Reason)"
    }

    git rev-parse --verify --quiet "refs/tags/v$newVersion" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "Тег v$newVersion уже существует." }

    # Превью
    Write-Host ''
    Write-Host '------------------- ПРЕВЬЮ РЕЛИЗА -------------------' -ForegroundColor Cyan
    Write-Host ("Версия:  {0} -> {1}" -f $current, $newVersion)
    Write-Host ("Причина: {0}" -f $reason)
    Write-Host ("Коммитов: {0}  (feat:{1} fix:{2} security:{3} breaking:{4} refactor:{5} perf:{6} other:{7})" -f `
        $cat.all.Count, $cat.feat.Count, $cat.fix.Count, $cat.security.Count, $cat.breaking.Count, $cat.refactor.Count, $cat.perf.Count, $cat.other.Count)
    Write-Host ''
    Write-Host 'CHANGELOG.md (новая секция):' -ForegroundColor Cyan
    Write-Host '----------------------------------------------------'
    Write-Host (New-ChangelogEntry $newVersion $cat).TrimEnd()
    Write-Host '----------------------------------------------------'
    Write-Host ("Release-commit: chore(release): v{0}" -f $newVersion)
    Write-Host ("Тег: v{0}    Ветка: {1}    Push: {2}" -f $newVersion, $branch, (-not $NoPush))
    Write-Host '----------------------------------------------------'
    Write-Host ''

    if ($DryRun) {
        Write-Info 'DryRun — изменения не применялись.'
        Pop-Location
        return
    }

    if (-not $Yes) {
        $confirm = Read-Host 'Выполнить релиз? [Y/n]'
        if ($confirm -and $confirm -notmatch '^[Yy]$') { Write-Warn 'Отменено пользователем.'; Pop-Location; return }
    }

    # Применение
    Write-Info 'Обновляю Directory.Build.props...'
    Set-PropsVersion $newVersion
    Write-Info 'Обновляю CHANGELOG.md...'
    Update-Changelog $newVersion $cat
    Write-Info 'Обновляю RELEASE_NOTES.md...'
    Update-ReleaseNotes $newVersion $cat

    # Бэкапы больше не нужны (успешно применили) — удалим перед коммитом
    Clear-Backups

    Write-Info 'git add / commit...'
    git add -A
    if ($LASTEXITCODE -ne 0) { throw 'git add завершился с ошибкой.' }

    $commitMsg = @"
chore(release): v$newVersion

Релиз версии ${newVersion}: feat=$($cat.feat.Count), fix=$($cat.fix.Count), security=$($cat.security.Count), breaking=$($cat.breaking.Count).
Коммиты с $([string]::IsNullOrEmpty($lastTag) ? 'начала истории' : $lastTag) до HEAD.
"@
    git commit -m $commitMsg
    if ($LASTEXITCODE -ne 0) { throw 'git commit завершился с ошибкой.' }
    $script:CreatedCommit = $true
    Write-Ok 'Release-коммит создан.'

    $tagMsg = "Release v$newVersion`r`n`r`n" + (New-ChangelogEntry $newVersion $cat).TrimEnd()
    git tag -a "v$newVersion" -m $tagMsg
    if ($LASTEXITCODE -ne 0) { throw 'git tag завершился с ошибкой.' }
    $script:CreatedTag = "v$newVersion"
    Write-Ok "Тег v$newVersion создан."

    if ($NoPush) {
        Write-Warn "NoPush — push пропущен. Запушить вручную: git push origin $branch --follow-tags"
    }
    else {
        Write-Info "Push origin/$branch --follow-tags..."
        git push origin $branch --follow-tags
        if ($LASTEXITCODE -ne 0) { throw 'git push завершился с ошибкой.' }
        Write-Ok "Запушено в origin/$branch (с тегом)."
    }

    Write-Host ''
    Write-Ok "РЕЛИЗ v$newVersion УСПЕШНО ЗАВЕРШЁН"
    Write-Host ''
    Write-Info 'Сгенерированные файлы: CHANGELOG.md, RELEASE_NOTES.md; версия в Directory.Build.props.'
    Pop-Location
}
catch {
    Invoke-Rollback -Reason $_.Exception.Message
    Pop-Location -ErrorAction SilentlyContinue
    exit 1
}
