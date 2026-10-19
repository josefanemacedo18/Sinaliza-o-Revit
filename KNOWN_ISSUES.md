# KNOWN_ISSUES – Problemas conhecidos e limitações

Itens abertos, com a evidência e o motivo de ainda não estarem corrigidos. Os já corrigidos saem daqui e vão para o
[CHANGELOG.md](CHANGELOG.md). Os códigos R* são os da [AUDIT.md](AUDIT.md).

| # | Item | Impacto | Evidência | Por que está aberto / próximo passo |
|---|---|---|---|---|
| K1 (R11) | Placas personalizadas de um projeto entram no catálogo **global**: com dois projetos abertos na mesma sessão do Revit, as placas de um aparecem no outro | Médio: só mistura listas; não grava em outro projeto | `CustomSignStore.Apply` → `CustomSigns.Apply(PluginContext.Catalog, …)` | A correção pede um catálogo por documento, o que muda `PluginContext`. Fica para a fase de arquitetura (ROADMAP F3) |
| K2 (R2) | Uma marca com vários elementos usa a **primeira** cópia da definição; não há regra para cópias divergentes | Baixo (hipótese não reproduzida) | `MarkingStorage.Definitions` | O **Validar Modelo** (V03) aponta o caso. "Atualizar" regrava todas as cópias iguais |
| K3 | O funcionamento dentro do Revit 2027 não é verificado no CI (não há Revit no CI) | Alto para a garantia, nulo para o código | AUDIT §2 | É preciso um relatório do Autoteste rodado pelo usuário |
| K4 (R12) | `CmdAreaConflito` e `CmdNovaVia` não estão na faixa de opções (código morto) | Nenhum | `RibbonBuilder.cs` | A função existe por Zebrado e por Via. Não apagar sem decisão do autor |
| K5 | Drenagem só geométrica: não há rede (tubos, PVs ligados, declividades) nem dimensionamento hidráulico | Funcional | AUDIT §6.8 | ROADMAP F8. Nenhum dimensionamento é afirmado |
| K6 | Microssimulação de tráfego não calibrada com dados de campo | Os resultados são estimativa de estudo | AUDIT §6.11 | Calibração exige contagens reais do usuário |
| K7 | O quantitativo de terraplenagem no Revit usa só a malha; o método das seções (`SectionVolumes`, EV6) existe e é testado no Core, mas ainda não tem interface | Funcional | `Grading.Volumes`, `SectionVolumes` | ROADMAP F7b: tabela por estaca no quantitativo, com o terreno e a superfície de projeto do Toposolid |
| K8 | Não há verificação de distância de visibilidade nem de raio/K mínimos normativos. O relatório de elementos existe para o traçado por PIs e para o greide (EV5), mas não para o eixo desenhado das vias comuns | Funcional | `AlignmentElements` | ROADMAP F5b. Critérios só com norma citada |
| K9 | Tolerâncias geométricas definidas em cada módulo, sem política central | Baixo | ARCHITECTURE §4 | Documentado. Unificar sem caso de falha seria refatoração por vaidade |
| K10 | Pacote do Revit referenciado pela primeira versão 2027.0.0: recursos da API de atualizações 2027.x não podem ser usados | Por projeto | `.csproj` | Decisão D-002 (carrega em todas as 2027.x) |
