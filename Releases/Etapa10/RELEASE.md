# SinalizaBIM 2.16.0 – Etapa 10

| Campo | Valor |
|---|---|
| Versão | 2.16.0 (InformationalVersion `2.16.0+1a7f2b526138568c9b901aa312b5d6565f94af18`) |
| Etapa do plano unificado | 10 |
| Data (UTC) | 2026-10-27 |
| Commit do código | `1a7f2b526138568c9b901aa312b5d6565f94af18` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `6ed57d2f609a1926b8d89069a325ff89551f5d6b7be319a5bd4badba7a280470` |
| Testes do núcleo | 3 048/3 048 em 14 min 47 s (Linux, SDK 10.0.401 pelo Nix) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
