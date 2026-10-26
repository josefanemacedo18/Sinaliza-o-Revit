# SinalizaBIM 2.14.0 – Etapa 8

| Campo | Valor |
|---|---|
| Versão | 2.14.0 (InformationalVersion `2.14.0+072b6836977a9b0a204158ca89e907e184acb420`) |
| Etapa do plano unificado | 8 |
| Data (UTC) | 2026-10-26 |
| Commit do código | `072b6836977a9b0a204158ca89e907e184acb420` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `51aafd672b76f945c381f14b26de9143d71feff065d603526984e16d00820163` |
| Testes do núcleo | 3 022/3 022 em 13 min 14 s (Linux, SDK 10.0.401 pelo Nix) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
