<#
.SYNOPSIS
    Instaluje HeatFlow.Api jako usługę Windows.

.DESCRIPTION
    Publikuje aplikację (jeśli potrzeba), rejestruje usługę HeatFlowApi i uruchamia ją.
    Wymaga uruchomienia w PowerShell jako Administrator.

.PARAMETER InstallPath
    Katalog instalacji (domyślnie C:\HeatFlow.Api).

.PARAMETER DoNotStart
    Zarejestruj usługę, ale jej nie uruchamiaj.

.PARAMETER Force
    Jeśli usługa HeatFlowApi już istnieje, zatrzymaj i odrejestruj ją przed instalacją.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string] $InstallPath = "C:\HeatFlow.Api",

    [Parameter()]
    [switch] $DoNotStart,

    [Parameter()]
    [switch] $Force
)

$ErrorActionPreference = "Stop"
$ServiceName = "HeatFlowApi"

# Sprawdzenie uprawnień administratora
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Uruchom skrypt jako Administrator (prawy przycisk na PowerShell -> Uruchom jako administrator)."
}

$exePath = Join-Path $InstallPath "HeatFlow.Api.exe"
$projectPath = Join-Path $PSScriptRoot "..\HeatFlow.Api.csproj"

# Opcjonalnie: usuń istniejącą usługę
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($Force) {
        Write-Host "Usługa $ServiceName istnieje. Zatrzymuję i usuwam..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName
        Start-Sleep -Seconds 2
    } else {
        Write-Error "Usługa $ServiceName jest już zarejestrowana. Użyj -Force, aby ją zastąpić."
    }
}

# Jeśli brak exe – publikuj
if (-not (Test-Path -LiteralPath $exePath)) {
    if (-not (Test-Path -LiteralPath $projectPath)) {
        Write-Error "Nie znaleziono projektu: $projectPath. Uruchom skrypt z repozytorium lub podaj katalog z opublikowaną aplikacją w $InstallPath."
    }
    Write-Host "Publikowanie do $InstallPath..."
    $null = New-Item -ItemType Directory -Path $InstallPath -Force
    dotnet publish (Resolve-Path $projectPath) -c Release -o $InstallPath
    if (-not (Test-Path -LiteralPath $exePath)) {
        Write-Error "Publikowanie nie utworzyło pliku $exePath."
    }
} else {
    Write-Host "Aplikacja istnieje w $InstallPath. Pomijam publikację."
}

# Przypomnienie o appsettings
$appsettingsPath = Join-Path $InstallPath "appsettings.json"
if (-not (Test-Path -LiteralPath $appsettingsPath)) {
    Write-Warning "Brak appsettings.json w $InstallPath. Skopiuj go z repozytorium i uzupełnij ConnectionStrings:DefaultConnection, HeatFlow:ApiKey itd."
}

# Rejestracja usługi
Write-Host "Rejestracja usługi $ServiceName..."
New-Service -Name $ServiceName `
    -BinaryPathName $exePath `
    -DisplayName "HeatFlow API" `
    -StartupType Automatic

if (-not $DoNotStart) {
    Write-Host "Uruchamianie usługi..."
    Start-Service -Name $ServiceName
    Write-Host "Usługa HeatFlowApi jest uruchomiona."
} else {
    Write-Host "Usługa zarejestrowana. Uruchom ręcznie: Start-Service $ServiceName"
}
