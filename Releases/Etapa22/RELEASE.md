# SinalizaBIM 2.27.0 – Etapa 22

| Campo | Valor |
|---|---|
| Versão | 2.27.0 (InformationalVersion `2.27.0+1d2b78e07243015cd83df261c8d1d8c6117bc1d7`) |
| Etapa do plano unificado | 22 |
| Data (UTC) | 2026-11-03 |
| Commit do código | `1d2b78e07243015cd83df261c8d1d8c6117bc1d7` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `1bcefa38ae0ba3be5e4da46ebca10f3157e44c0a320ee59a73739ba5a44f82de` |
| Testes do núcleo | 3 249/3 249 aprovados (suíte completa em 14 min 25 s, Linux, SDK 10.0.401) |
| Não verificado | o canteiro aberto pela curva de giro, a travessia atrás do nariz, a ciclofaixa até a faixa, a calçada de cada ramo da rotatória e os campos novos das janelas no Revit 2027 (K36) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
