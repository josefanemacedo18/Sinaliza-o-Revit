# SinalizaBIM 2.12.0 – Etapa 6

| Campo | Valor |
|---|---|
| Versão | 2.12.0 (InformationalVersion `2.12.0+ab92d465816c5971c5465211b4345197922818a4`) |
| Etapa do plano unificado | 6 |
| Data (UTC) | 2026-10-26 |
| Commit do código | `ab92d465816c5971c5465211b4345197922818a4` |
| Configuração | `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | 0 Warning(s), 0 Error(s) |
| SHA-256 da DLL | `3605e634c87267f35c774c5c0aca0902e7c6a63ac1a6b1baec4c95b2b2f6ee9d` |
| Testes do núcleo | 3 011/3 011 em 14 min 20 s (Linux, SDK 10.0.401 pelo Nix) |
| Não verificado | não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit) |

Arquivos: `SinalizaBIM.dll` e `SinalizaBIM.addin` (instalação em `Instalar/LEIA-ME.txt`). O que mudou nesta etapa está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
