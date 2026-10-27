# SinalizaBIM 2.17.0 – Etapa 11

| Campo | Valor |
|---|---|
| Versão | 2.17.0 (InformationalVersion `2.17.0+70d43afebe3e1bcf3e155241124f5067903ebaec`) |
| Etapa do plano unificado | 11 |
| Data (UTC) | 2026-10-27 |
| Commit do código | `70d43afebe3e1bcf3e155241124f5067903ebaec` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `4272dec83d0034343937a7a19bb6556b6d7397396cca972263504bb9d4ba0200` |
| Testes do núcleo | 3 075/3 075 em 15 min 5 s (Linux, SDK 10.0.401 pelo Nix) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
