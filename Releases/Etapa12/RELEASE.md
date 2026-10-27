# SinalizaBIM 2.18.0 – Etapa 12

| Campo | Valor |
|---|---|
| Versão | 2.18.0 (InformationalVersion `2.18.0+9102a7b731b25efb00605691c3cabbaea80a89ce`) |
| Etapa do plano unificado | 12 |
| Data (UTC) | 2026-10-27 |
| Commit do código | `9102a7b731b25efb00605691c3cabbaea80a89ce` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `95b43f686bcce9e3ffffdcc8c272e47ee7eb3d40ccc8e3a0fae7f874f30b32d1` |
| Testes do núcleo | 3 123/3 123 em 14 min 50 s (Linux, SDK 10.0.401 pelo Nix) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
