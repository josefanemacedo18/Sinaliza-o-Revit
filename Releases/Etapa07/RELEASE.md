# SinalizaBIM 2.13.0 – Etapa 7

| Campo | Valor |
|---|---|
| Versão | 2.13.0 (InformationalVersion `2.13.0+e3a27c45b660c6de696b030ad91a0bbddb10dc50`) |
| Etapa do plano unificado | 7 |
| Data (UTC) | 2026-10-26 |
| Commit do código | `e3a27c45b660c6de696b030ad91a0bbddb10dc50` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `61dcc44d737ad4e59cd7c0bf815f1f806c245b6865e48d275b9b1a2b087de18b` |
| Testes do núcleo | 3 016/3 016 em 15 min 5 s (Linux, SDK 10.0.401 pelo Nix) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
