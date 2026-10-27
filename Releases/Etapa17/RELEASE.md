# SinalizaBIM 2.22.0 – Etapa 17

| Campo | Valor |
|---|---|
| Versão | 2.22.0 (InformationalVersion `2.22.0+3dc933ae75173bf560efdcaff08eaee2578ac335`) |
| Etapa do plano unificado | 17 |
| Data (UTC) | 2026-10-27 |
| Commit do código | `3dc933ae75173bf560efdcaff08eaee2578ac335` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `b3df11d1b65e1ce568d84d187f0c3aabc6e83ef2015298a1b1c8a7887249886b` |
| Testes do núcleo | 3 183/3 183 em 14 min 3 s (Linux, SDK 10.0.401) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit): rampas como pisos e aparência dos materiais NÃO VALIDADAS NO REVIT (K31) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
