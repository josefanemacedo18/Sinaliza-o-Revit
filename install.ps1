<#
.SYNOPSIS
    Compila e instala o plugin "Sinalização Viária Horizontal" no Revit 2027.

.DESCRIPTION
    Requer o .NET 10 SDK (https://dotnet.microsoft.com/download).
    Copia as DLLs para %AppData%\Autodesk\Revit\Addins\2027\SinalizacaoViaria
    e o manifesto SinalizacaoViaria.addin para %AppData%\Autodesk\Revit\Addins\2027.

.PARAMETER Uninstall
    Remove o plugin.

.PARAMETER AllUsers
    Instala em %ProgramData% (todos os usuários – requer PowerShell como administrador).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
param(
    [switch]$Uninstall,
    [switch]$AllUsers,
    [string]$RevitVersion = "2027"
)

$ErrorActionPreference = "Stop"
$root = if ($AllUsers) { $env:ProgramData } else { $env:APPDATA }
$addinRoot = Join-Path $root "Autodesk\Revit\Addins\$RevitVersion"
$addinDir = Join-Path $addinRoot "SinalizacaoViaria"
$manifest = Join-Path $addinRoot "SinalizacaoViaria.addin"

if ($Uninstall) {
    if (Test-Path $addinDir) { Remove-Item $addinDir -Recurse -Force }
    if (Test-Path $manifest) { Remove-Item $manifest -Force }
    Write-Host "Plugin removido de $addinRoot" -ForegroundColor Green
    exit 0
}

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    Write-Warning "Feche o Revit antes de instalar (as DLLs ficam bloqueadas enquanto ele está aberto)."
    exit 1
}

$project = Join-Path $PSScriptRoot "src\SinalizacaoViaria.Revit\SinalizacaoViaria.Revit.csproj"
$out = Join-Path $PSScriptRoot "dist\SinalizacaoViaria"

Write-Host "Executando testes do núcleo..." -ForegroundColor Cyan
dotnet test (Join-Path $PSScriptRoot "tests\SinalizacaoViaria.Core.Tests\SinalizacaoViaria.Core.Tests.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "Testes falharam." }

Write-Host "Compilando (Release)..." -ForegroundColor Cyan
dotnet build $project -c Release -p:DeployToRevit=false -o $out
if ($LASTEXITCODE -ne 0) { throw "Falha na compilação." }

New-Item -ItemType Directory -Force -Path $addinDir | Out-Null
Get-ChildItem $out -Include *.dll, *.pdb, *.deps.json -Recurse | Copy-Item -Destination $addinDir -Force
Copy-Item (Join-Path $out "SinalizacaoViaria.addin") $manifest -Force

Write-Host ""
Write-Host "Instalado com sucesso!" -ForegroundColor Green
Write-Host "  Manifesto: $manifest"
Write-Host "  Arquivos:  $addinDir"
Write-Host "Abra o Revit 2027 e procure a guia 'Sinalização Viária'."
