# SinalizaBIM 2.15.0 – Etapa 9

| Campo | Valor |
|---|---|
| Versão | 2.15.0 (InformationalVersion `2.15.0+d0744652a75cdf7c55ccb1224e19123cf35bd1c2`) |
| Etapa do plano unificado | 9 |
| Data (UTC) | 2026-10-27 |
| Commit do código | `d0744652a75cdf7c55ccb1224e19123cf35bd1c2` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `e714ce1e81b60bd917698bca81bb977f9c7953ff4dbb866d54c76c84a5535031` |
| Testes do núcleo | 3 036/3 036 em 14 min 35 s (Linux, SDK 10.0.401 pelo Nix) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
