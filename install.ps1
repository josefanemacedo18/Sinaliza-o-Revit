<#
.SYNOPSIS
    Instala o plugin SinalizaBIM no Revit 2027 (opcional).

.DESCRIPTION
    O script apenas copia os arquivos prontos de .\Instalar\Revit2027
    (SinalizaBIM.dll + SinalizaBIM.addin) para a pasta de Add-ins.
    Isso também pode ser feito manualmente – veja Instalar\LEIA-ME.txt.
    Não requer .NET SDK (exceto com -Build, para quem altera o código-fonte).

.PARAMETER Uninstall
    Remove o plugin.

.PARAMETER AllUsers
    Instala em %ProgramData% (todos os usuários – requer PowerShell como administrador).

.PARAMETER Build
    Recompila a partir do código-fonte antes de instalar (requer .NET 10 SDK).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
param(
    [switch]$Uninstall,
    [switch]$AllUsers,
    [switch]$Build,
    [string]$RevitVersion = "2027"
)

$ErrorActionPreference = "Stop"
$root = if ($AllUsers) { $env:ProgramData } else { $env:APPDATA }
$addinRoot = Join-Path $root "Autodesk\Revit\Addins\$RevitVersion"
$package = Join-Path $PSScriptRoot "Instalar\Revit$RevitVersion"
$files = @("SinalizaBIM.dll", "SinalizaBIM.addin")
# Arquivos de versões anteriores (antes da renomeação para SinalizaBIM).
$legacyFiles = @("SinalizacaoViaria.dll", "SinalizacaoViaria.addin")

# Estrutura antiga (subpasta) de versões anteriores do plugin.
$legacyDir = Join-Path $addinRoot "SinalizacaoViaria"

if ($Uninstall) {
    foreach ($f in $files + $legacyFiles) { Remove-Item (Join-Path $addinRoot $f) -Force -ErrorAction SilentlyContinue }
    if (Test-Path $legacyDir) { Remove-Item $legacyDir -Recurse -Force }
    Write-Host "Plugin removido de $addinRoot" -ForegroundColor Green
    exit 0
}

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    Write-Warning "Feche o Revit antes de instalar (a DLL fica bloqueada enquanto ele está aberto)."
    exit 1
}

if ($Build) {
    $project = Join-Path $PSScriptRoot "src\SinalizacaoViaria.Revit\SinalizacaoViaria.Revit.csproj"
    dotnet build $project -c Release -p:DeployToRevit=false
    if ($LASTEXITCODE -ne 0) { throw "Falha na compilação." }
}

New-Item -ItemType Directory -Force -Path $addinRoot | Out-Null
if (Test-Path $legacyDir) { Remove-Item $legacyDir -Recurse -Force }
foreach ($f in $legacyFiles) { Remove-Item (Join-Path $addinRoot $f) -Force -ErrorAction SilentlyContinue }
foreach ($f in $files) {
    $src = Join-Path $package $f
    if (-not (Test-Path $src)) { throw "Arquivo não encontrado: $src" }
    Copy-Item $src $addinRoot -Force
    Unblock-File (Join-Path $addinRoot $f) -ErrorAction SilentlyContinue
}

Write-Host "Instalado em $addinRoot" -ForegroundColor Green
Write-Host "Abra o Revit 2027 e procure a guia 'SinalizaBIM'."
