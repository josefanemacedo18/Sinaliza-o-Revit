# SinalizaBIM 2.24.0 – Etapa 19

| Campo | Valor |
|---|---|
| Versão | 2.24.0 (InformationalVersion `2.24.0+5660834bad490aeb14d8540f958480550f6cd7dd`) |
| Etapa do plano unificado | 19 |
| Data (UTC) | 2026-10-28 |
| Commit do código | `5660834bad490aeb14d8540f958480550f6cd7dd` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `579f77ec9f1c2c4e4f2c4a30e21d1f8962b31ec8c89bf51d5d0dc38738c225e4` |
| Testes do núcleo | 3 212/3 212 em 15 min 25 s (Linux, SDK 10.0.401) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit): Componentes por Trecho, Taludes por Trecho e a terraplenagem com o pavimento gravado NÃO VALIDADOS NO REVIT (K33) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
