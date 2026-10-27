# SinalizaBIM 2.20.0 – Etapa 15

| Campo | Valor |
|---|---|
| Versão | 2.20.0 (InformationalVersion `2.20.0+a62975ce239f91138be2f0ad975ff785f433b635`) |
| Etapa do plano unificado | 15 |
| Data (UTC) | 2026-10-27 |
| Commit do código | `a62975ce239f91138be2f0ad975ff785f433b635` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `1f6e14abf3c865ca51a218db7aee33bbdad0b3c984ddc55248601ba14620bdf1` |
| Testes do núcleo | 3 145/3 145 em 15 min 35 s (Linux, SDK 10.0.401) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit): regra do trecho mínimo e plataforma sob as rampas NÃO VALIDADAS NO REVIT (K28) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
