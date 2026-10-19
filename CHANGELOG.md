# CHANGELOG

Formato: data, etapa, mudanças, testes. O histórico anterior a 2026-10-19 está no `git log`: 118 commits, rodadas A a LX,
cada uma com seus testes.

## 2026-10-19 – EV1 a EV4: auditoria, documentos de controle, estabilização e validação do modelo

### Segurança de dados
- **Definições de versão mais nova ficam intactas.**
  - `DefinitionVersioning.Load` lê o JSON com estado: Atual, Migrada, MaisNova ou Inválida.
  - `MarkingService.Render`/`RescaleInPlace` não regeram uma definição gravada por versão mais nova do plugin: mantêm os elementos e avisam.
  - `MarkingStorage.Write` recusa regravá-la, para não perder campos desconhecidos.
  - Antes, ela era lida e regravada como v1.
- Base de migrações versionadas: passos N → N+1 aplicados em ordem na leitura (DECISIONS D-003).
- O log gira para `log.1.txt` ao passar de 2 MB, em vez de ser apagado.

### Erros que antes passavam em silêncio
- Contorno degenerado deixado de fora de um piso de pintura: agora é contado, registrado no log e avisado ao usuário.
- Norte do projeto não lido na calibração ou importação de imagem: o fallback para 0° é registrado no log.
- Avisos do Revit suprimidos na atualização do terreno (`TerrainFailures`): agora vão para o log.

### Modelo integrado e validação
- `ModelGraph` (Core): grafo de dependências derivado das definições.
  - Tipos de referência: alvo de detalhe, vias de interseção/rotatória/balão, loteamento, filhas, recorte (fraca).
  - Faz: referências quebradas, ciclos (Tarjan), ordem de regeneração (Kahn) e dependentes transitivos.
- `ModelValidator` (Core): verificações V01–V08, cada uma com severidade, confiança e indicação de revisão humana, e relatório em texto.
  - V01 dados ilegíveis
  - V02 versão mais nova
  - V03 cópias divergentes
  - V04 referência quebrada
  - V05 ciclo
  - V06 elemento do Revit apagado
  - V07 contorno inválido
  - V08 corte sem perfil
- `PolygonChecks` (Core): poucos vértices, colineares, área nula, NaN, lados que se cruzam ou se tocam e espinhos, com tolerância em metros.
- Revit: comando **Editar → Validar Modelo**.
  - `TransactionMode.ReadOnly`: não altera o projeto.
  - Grava `validacao-*.txt` e oferece selecionar os elementos com erro ou aviso.

### Testes e compilação
- Suíte: **2 730/2 730 aprovados** (13 min 7 s); Core, plugin e testes com 0 avisos.
- +26 testes em `EVModelIntegrityTests`, incluindo ida e volta JSON de **todos** os tipos de definição registrados.
- Avisos de compilação dos testes zerados (xUnit2029 em `AB2IntersectionTests`, CS0028 em `AD6AutoSignageTests`).
- Pacotes de teste com versão fixa (antes `17.*`/`2.*`): `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3 e `xunit.runner.visualstudio` 2.8.2, as versões que já eram restauradas.

### Documentação
- Novos: `AUDIT.md`, `ARCHITECTURE.md`, `ROADMAP.md`, `TESTS.md`, `KNOWN_ISSUES.md`, `DECISIONS.md`, `RESUME.md` e este arquivo.
- `docs/ARQUITETURA.md` passa a apontar para `ARCHITECTURE.md`; o manual ganha o Validar Modelo.
