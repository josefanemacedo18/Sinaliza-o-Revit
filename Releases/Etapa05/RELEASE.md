# SinalizaBIM 2.11.0 – Etapa 5

| Campo | Valor |
|---|---|
| Versão | 2.11.0 (InformationalVersion `2.11.0+5dc91f7fc3e8f0533194a24c9cabafff904b6e96`) |
| Etapa do plano unificado | 5 |
| Data (UTC) | 2026-10-26 |
| Commit do código | `5dc91f7fc3e8f0533194a24c9cabafff904b6e96` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `024b9887b7244a9f317a99589762771dff23d21c037c5cbd0d16a1d0f4787b65` |
| Testes do núcleo | 3 000/3 000 em 13 min 36 s (Linux, SDK 10.0.401 pelo Nix) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
