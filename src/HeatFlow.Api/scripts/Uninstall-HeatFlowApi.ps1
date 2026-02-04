<#
.SYNOPSIS
    Odinstalowuje usługę Windows HeatFlow.Api.

.DESCRIPTION
    Zatrzymuje usługę, odrejestrowuje ją. Opcjonalnie usuwa katalog z plikami aplikacji.
    Wymaga uruchomienia w PowerShell jako Administrator.

.PARAMETER RemoveFiles
    Po odrejestrowaniu usługi usuń katalog z aplikacją (domyślna ścieżka C:\HeatFlow.Api).

.PARAMETER InstallPath
    Katalog instalacji do usunięcia, gdy używane jest -RemoveFiles (domyślnie C:\HeatFlow.Api).
#>
[CmdletBinding()]
param(
    [Parameter()]
    [switch] $RemoveFiles,

    [Parameter()]
    [string] $InstallPath = "C:\HeatFlow.Api"
)

$ErrorActionPreference = "Stop"
$ServiceName = "HeatFlowApi"

# Sprawdzenie uprawnień administratora
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Uruchom skrypt jako Administrator (prawy przycisk na PowerShell -> Uruchom jako administrator)."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Usługa $ServiceName nie jest zainstalowana."
} else {
    Write-Host "Zatrzymywanie usługi $ServiceName..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Host "Usuwanie usługi..."
    & sc.exe delete $ServiceName
    Write-Host "Usługa HeatFlowApi została odinstalowana."
}

if ($RemoveFiles -and (Test-Path -LiteralPath $InstallPath)) {
    Write-Host "Usuwanie katalogu $InstallPath..."
    Remove-Item -Path $InstallPath -Recurse -Force
    Write-Host "Katalog usunięty."
} elseif ($RemoveFiles) {
    Write-Host "Katalog $InstallPath nie istnieje. Pomijam usuwanie."
}
