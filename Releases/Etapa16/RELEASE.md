# SinalizaBIM 2.21.0 – Etapa 16

| Campo | Valor |
|---|---|
| Versão | 2.21.0 (InformationalVersion `2.21.0+0ef734a9a3f96ea4992c5ae006773f2a1b7238cf`) |
| Etapa do plano unificado | 16 |
| Data (UTC) | 2026-10-27 |
| Commit do código | `0ef734a9a3f96ea4992c5ae006773f2a1b7238cf` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `b44407346b7d5bd244841ec3ef339b03c41c45664c9c2261d70fb086dca30543` |
| Testes do núcleo | 3 163/3 163 em 15 min 12 s (Linux, SDK 10.0.401) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit): chanfro de 1,50 m nas quadras novas, faixa tátil da esquina e extensão em chanfro NÃO VALIDADOS NO REVIT (K30) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
