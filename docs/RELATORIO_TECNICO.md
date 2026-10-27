# Relatório técnico final – plano unificado (Etapas 0–13)

Relatório de entrega pedido pelo Prompt de Interface (§32), com o resumo do Prompt Master (§27) e o estado técnico do Prompt
de Motores (§21). Data: 2026-10-27. Versão: **2.19.0** (`Releases/Etapa13`). Base do plano: commit `9701ebb` (AUDIT §9).

Este relatório resume e remete aos documentos de controle, que têm o detalhe: [PLANO_DE_ACAO](PLANO_DE_ACAO.md) (o que cada
etapa fez), [CHANGELOG](../CHANGELOG.md) (cada mudança), [AUDIT](../AUDIT.md) (estado com evidência), [KNOWN_ISSUES](../KNOWN_ISSUES.md)
(limites), [DECISIONS](../DECISIONS.md) (D-021 a D-031), [TESTS](../TESTS.md) (linha de base dos testes) e
[MANUAL](MANUAL.md) (uso).

> **Compilação × validação visual.** Tudo neste relatório foi compilado e testado no núcleo, em Linux, sem Revit. **Nenhuma
> tela, janela, faixa de opções ou operação no modelo das versões 2.8.0 a 2.19.0 foi executada no Revit 2027.** Por isso
> não há capturas de tela: não existem capturas reais, e o relatório não usa imagens ilustrativas no lugar delas. Os itens
> que dependem do Revit estão em KNOWN_ISSUES (K3, K17, K19, K23–K27), cada um com o que conferir.

## Níveis de prova

| Nível | Situação | Evidência |
|---|---|---|
| Compilação | plugin (`net10.0-windows`), núcleo, testes e `tools/Desempenho`: 0 avisos, 0 erros | `tools/release.sh` grava o resultado em `Releases/Etapa13/RELEASE.md`; CI Windows a cada push |
| Testes automatizados do núcleo | suíte completa verde na versão 2.19.0 (contagem e duração em TESTS.md) | TESTS.md, linha da Etapa 13 |
| Testes geométricos e de integração do núcleo | pisos sobrepostos, pisos × terreno, terraplenagem, drenagem, estado dos resultados, fluxo integrado – no núcleo, com fixtures dos Autotestes reais | `Fase10*`, `Etapa01`–`Etapa13*Tests`, `FCRevitFullRun4Tests` |
| Validação no Revit 2027 | **não feita** para 2.8.0–2.19.0; o último Autoteste completo no Revit foi o 4º (2026-10-21, DLL 2.1.0) | `docs/evidencias/2026-10-21-autoteste-completo-4.txt`; K3 |

## 1. Resumo da interface anterior

Pela leitura do código no início do plano (AUDIT §9.1, commit `9701ebb`):

- WPF, janelas modais abertas pelos comandos; 51 arquivos em `Revit/UI`, dos quais 20 XAML, e a maioria dos comandos num
  formulário genérico (`FormWindow`) com prévia.
- Um `Styles.xaml` de 74 linhas, com cores repetidas soltas nas janelas.
- Faixa de opções com 2 guias e 12 painéis por tipo de elemento (Vias, Sinalização Horizontal e Vertical, Detalhamento,
  Tráfego, Editar, Drenagem, Obras de Arte, Contenções, Relevo e Terreno, Nós Viários, Loteamento).
- Quantitativos numa janela com abas e exportação; configurações só do usuário; Validar Modelo num `TaskDialog` com o
  relatório em texto; progresso só no Simulador de Tráfego.
- Sem visão do projeto inteiro, sem painel que acompanhe a seleção, sem estado de resultado desatualizado na tela.

## 2. Mudanças implementadas

Motores (Etapas 1–8), com a interface que os mostra (Etapas 9–12) e a entrega (Etapa 13). Cada linha remete ao CHANGELOG.

| Etapa | Versão | O que mudou |
|---|---|---|
| 1 | 2.8.0 | pisos sobrepostos classificados (cinco classes, nada apagado); pisos × terreno no núcleo; Autoteste contra o Toposolid projetado |
| 2–3 | 2.9.0 | validação com módulo, ação, "não verificado" e histórico; tolerâncias num registro; diagnóstico do terreno (V14) |
| 4 | 2.10.0 | estaqueamento com estaca inicial e equações; limites dos avisos com a fonte ou "sem norma citada" |
| 5 | 2.11.0 | estrutura do pavimento em camadas usada pela terraplenagem, pelo corte e pelo quantitativo; registro da terraplenagem e V15 |
| 6 | 2.12.0 | drenagem lida do modelo (sarjeta e área pela via), pontos baixos, talvegues, recobrimento sob a superfície acabada, perfil da rede |
| 7 | 2.13.0 | indicadores do parcelamento, fonte dos mínimos, V16, Trocar Elementos |
| 8 | 2.14.0 | estado dos resultados (atualizado, desatualizado, editado à mão), atualização seletiva, impacto da edição, V17 |
| 9 | 2.15.0 | design system (tokens e componentes) e faixa de opções por tema |
| 10 | 2.16.0 | Central do Projeto e painel contextual |
| 11 | 2.17.0 | telas dos módulos (via em nove abas, pavimento, drenagem, terreno, nós, parcelamento) |
| 12 | 2.18.0 | Central de Qualidade, quantitativos com a origem de cada número, gerenciador de configurações e perfis |
| 13 | 2.19.0 | medição do núcleo e terraplenagem linear; progresso e cancelamento; relatório do tráfego reprodutível; fluxo integrado no Autoteste |

## 3. Componentes visuais criados

- **Tokens** iguais no `Styles.xaml` e no `UiTokens`: cores (primária, superfícies, bordas, texto), estados com fundo (ok,
  informação, aviso, erro, não verificado, desabilitado), tipografia e espaçamento; contraste conferido em teste (Etapa 9).
- **Componentes:** cartão, faixa de estado, status com nível, valor com unidade, botões primário e secundário, tabelas
  (`UiTables.Grid`), linhas de propriedade (`ContextPanelPage.Linha`), estado do formulário (`FormWindow.Status`) e foco
  visível. Nenhuma cor solta nas janelas (teste: nenhum hexadecimal nos `.cs` da interface).
- **Ícones** próprios por botão, desenhados por código (`IconFactory`), a 16 e 32 px.
- **Janela de progresso** (`ProgressWindow`, Etapa 13): etapa, item, contagem, barra só com total conhecido e Cancelar.

## 4. Janelas implementadas

| Janela | Etapa | Conteúdo (montado no núcleo) |
|---|---|---|
| Central do Projeto (`ProjectCentralWindow`) | 10 | indicadores reais com a situação de cada valor, situação do projeto, módulos e atalhos (`CentralDoProjeto`) |
| Painel contextual (`DockablePane`, `ContextPanelPage`) | 10 | propriedades por tipo, estado e operações da seleção, só leitura (`PainelContextual`) |
| Via em nove abas (`RoadWindow`) | 11 | identificação, geometria, perfil, seção, pavimento, terraplenagem, drenagem, validação e resumo (`FormularioVia`) |
| Gerenciador de pavimentos (`PavementManagerWindow`) | 11 | camadas, esquema em corte, quantitativo previsto, modelos do usuário (`GerenciadorPavimento`) |
| Rede de drenagem (`DrainageNetworkWindow`) | 11 | geometria, conectividade, parâmetros, cálculo, dimensionamento e perfil (`RedeDrenagemTela`) |
| Terreno (`TerrainWindow`) | 11 | origem, natural × projetado, superfície de cada cálculo, módulos a atualizar (`TerrenoTela`) |
| Seções dos formulários de interseção e rotatória | 11 | vias conectadas, verificação geométrica × operacional, resumo (`FormularioNo`) |
| Resultados do parcelamento (`LotResultsWindow`) | 11 | quadras, lotes, indicadores, sem conformidade legal afirmada (`ResultadosParcelamento`) |
| Central de Qualidade (`QualityCenterWindow`) | 12 | achados do Validar Modelo com estado, filtros, seleção, CSV; o último Autoteste numa aba (`CentralQualidade`) |
| Quantitativos (abas novas) | 12 | origem e método de cada linha, origem dos valores, por via, quadras e lotes (`OrigemQuantitativos`) |
| Configurações em abas (`SettingsWindow`) | 12 | do usuário, do projeto, de cada elemento, perfis, onde fica cada uma (`RegistroConfiguracoes`) |
| Progresso (`ProgressWindow`) | 13 | Atualizar Todas e Quantitativos (`EstadoProgresso`) |

## 5. Comandos reorganizados

- Faixa por tema (Etapa 9, D-027): guia **SinalizaBIM** com Projeto, Terreno, Sistema Viário, Interseções, Terraplenagem,
  Pavimentação, Drenagem, Urbanismo, Análise, Documentação e Sistema; guia **SinalizaBIM Sinalização**.
- Os 98 botões que existiam, cada um uma vez, mais o **Diagnóstico do Terreno**; nenhum comando perdido (teste). Dicas curtas
  com descrição longa; botão habilitado só com projeto aberto. Mapa antigo → novo no MANUAL.
- Comandos novos no plano: Estrutura do Pavimento, Ler do Modelo (drenagem), Trocar Elementos, Diagnóstico do Terreno,
  Central do Projeto, painel contextual.

## 6. Funcionalidades integradas

- **Atalhos pelo mesmo caminho do botão:** a Central e as telas abrem os comandos por `CommandBase.Invoke` (a mesma
  disponibilidade, transação e tratamento de erro); o painel contextual, por `ExternalEvent`.
- **Um estado só para cada resultado:** a terraplenagem (registro e V15) e as captações lidas (V17) aparecem iguais no Validar
  Modelo, na Central do Projeto, no painel, na janela da via e na tela do terreno (D-026).
- **Selecionar no modelo** a partir da Central de Qualidade, dos Quantitativos e da rede de drenagem.
- **Mesmos nomes de via** em todas as telas (`NomesDasMarcas`).
- **Fluxo integrado no Autoteste** (Etapa 13): via → interseção → terraplenagem → drenagem → quantitativos → validação, cada
  módulo conferido pelo que o anterior gravou, com os mesmos comandos do usuário.

## 7. Funcionalidades ainda indisponíveis

Ditas na tela como indisponíveis ou ausentes (estado C), nunca simuladas:

- **Cenários de projeto comparados por quantitativos (F12).** A Terraplenagem compara a simulação com a aplicada; comparar
  alternativas de projeto inteiras continua manual.
- **Progresso** só em Atualizar Todas e Quantitativos (e a barra que o Simulador já tinha); Terraplenagem, Validar Modelo e
  Autoteste sem janela de progresso (K27).
- **Desempenho no Revit:** piso tátil longo (K14) e varreduras do documento (K15) medidos, não mudados (D-031).
- Estados "recalculando" e "falha na atualização" não são guardados; operações recentes não aparecem na Central (não há
  histórico confiável); sem edição em lote no painel contextual.
- Distância de visibilidade (K8); valetas e dissipadores; macrodrenagem, reservatórios e tempo de concentração de George
  Ribeiro (K5); calibração do tráfego com dados de campo (K6); filtros e colunas lembrados entre sessões.

## 8. Arquivos criados

67 arquivos em `src`, `tests` e `tools` desde `9701ebb` (`git diff --name-status 9701ebb HEAD`).

- **Núcleo – motores e modelos de tela:** `Automation/` ContextPanel, DrainageModel, DrainageNetworkScreen, DrainageProfile,
  EarthworkRecord, FloorTerrainCheck, NodeForm, PavementManager, Progress, ProjectCentral, ResultState, RoadForm,
  TerrainDiagnostics, TerrainScreen, UrbanSwap; `Definitions/PavementStructure`; `Generators/LimitesDeConferencia`;
  `Geometry/Estaqueamento`; `Model/` AutotestReport, MarkingNames, QualityCenter, Tolerancias, ValidationHistory;
  `Parcelamento/` LotIndicators, LotResults; `Quantities/` PavementLayerQuantities, QuantityOrigin; `Settings/SettingsRegistry`.
- **Plugin:** `Commands/` AutoTestSteps5, CentralCommands, CommandAvailability, CommandLauncher, PavementStructureCommands,
  TerrainDiagnosticCommands; `Infrastructure/` ContextPanelService, EarthworkRecordStore, EditImpact; `UI/` ContextPanelPage,
  DrainageNetworkWindow, LotResultsWindow, PavementManagerWindow, ProgressWindow, ProjectCentralWindow, QualityCenterWindow,
  TerrainWindow, UiTables, UiTokens.
- **Testes:** `Fase10PisoTerrenoTests` e `Etapa02`–`Etapa13*Tests` (18 classes).
- **Ferramenta:** `tools/Desempenho` (medição do núcleo, fora do plugin).
- **Documentos:** `docs/PLANO_DE_ACAO.md`, os três prompts em `docs/`, este relatório e as evidências em `docs/evidencias`.

## 9. Arquivos modificados

70 arquivos em `src`, `tests` e `tools`; ao todo, 137 arquivos com 15 369 linhas acrescentadas e 890 removidas. Os principais:

- **Núcleo:** `Automation/Grading` (índice da terraplenagem), `RoadSetup*`, `SectionVolumes`; `Model/ModelValidator` (V12–V17,
  módulo, ação, não verificado); `Quantities/QuantityCalculator` e `QuantityWorkbook` (origem, método, unidade do consumo);
  `Settings/PluginSettings`; `Traffic/TrafficReport` e `TrafficSimulation` (F11); geradores de interseção, rotatória,
  canalização e ponte.
- **Plugin:** `RibbonBuilder` e `IconFactory` (faixa por tema); `App` (painel contextual); comandos de edição, relatórios,
  validação, drenagem, terreno, interseções e loteamento; `TerrainService`, `TerrainModel`, `MarkingStorage`; o Autoteste
  (`AutoTest*.cs`); `Styles.xaml`, `FormWindow`, `UiHelpers` e as janelas de via, quantitativos, configurações, tráfego e
  Autoteste.
- **Ferramentas:** `tools/release.sh` (entregas por etapa).

## 10. Alterações arquiteturais

- **Conteúdo das telas no núcleo** (D-029): cada tela tem um modelo de apresentação testável (`FormularioVia`, `CentralDoProjeto`,
  `CentralQualidade`…); as janelas só mostram e chamam comandos existentes.
- **Estado dos resultados por comparação** (D-026): o que foi lido ou aplicado guarda a origem (registro da terraplenagem,
  leitura das captações) e o estado vem de comparar com o modelo atual – sem flags que podem ficar velhas.
- **Um registro para cada coisa configurável ou citada:** tolerâncias (`Tolerancias`), limites dos avisos
  (`LimitesDeConferencia`), configurações (`RegistroConfiguracoes`).
- **Progresso e cancelamento** (D-031): interface `IProgressoOperacao` no núcleo, janela no plugin; cancelamento só em ponto
  seguro antes de gravar.
- Sem arquitetura paralela nem reescrita: os comandos, o armazenamento e os geradores existentes foram estendidos. O
  acoplamento entre pastas do Core (K18) continua; código novo sem referência mútua nova (D-021).

## 11. Testes executados

- **Suíte completa do núcleo** a cada etapa (TESTS.md): 2 966 na Etapa 1, 3 123 na Etapa 12 e a contagem da 2.19.0 na
  linha da Etapa 13.
- **Testes por etapa** (+170 desde a Etapa 1): classificação dos pisos, pisos × terreno, validação e histórico, terreno,
  estaqueamento, pavimento e terraplenagem, drenagem lida do modelo, urbanismo, estado dos resultados, faixa de opções e
  design system (inclusive contraste e nenhuma cor solta), modelos de cada tela, qualidade, quantitativos, configurações,
  desempenho, tráfego, progresso e fluxo integrado.
- **Testes de fonte** para o que não roda sem o Revit: a ligação dos comandos, das janelas e do Autoteste (o que é chamado e
  em que ordem; por exemplo, que nenhum ponto de cancelamento fica depois da gravação).
- **Medição** do núcleo (`tools/Desempenho`) e comparação bit a bit da terraplenagem antes e depois da correção.
- **Compilação** do plugin a cada commit (0 avisos) e CI Windows a cada push.

## 12. Resultados dos testes

- Suíte completa: todos aprovados em todas as entregas, 0 falhas (TESTS.md; o resultado da 2.19.0 está no manifesto
  `Releases/Etapa13/RELEASE.md`).
- Terraplenagem de 64 vias: 12,1–14,6 s → 2,9–4,0 s, mesmo SHA-256 dos resultados nos três tamanhos.
- Nenhuma operação do núcleo cresce ao quadrado com o projeto (de 4 para 64 vias, no máximo ~10× para 16× as marcas).
- Tráfego: a mesma semente dá os mesmos números e o mesmo relatório; outra semente, outra amostra.
- **Não executado:** nada no Revit 2027 desde a DLL 2.1.0. As telas, a faixa, o painel, as janelas e o fluxo integrado
  compilam e seus modelos passam nos testes, mas **não foram vistos nem usados no Revit**.

## 13. Limitações encontradas

- **Ambiente sem Revit nem Windows:** a API do Revit, as janelas WPF e a aparência não podem ser executadas aqui; o CI
  Windows também não tem Revit, e a Design Automation da Autodesk exigiria credenciais que o projeto não tem (TESTS.md).
- **Sem capturas reais** da interface pelo mesmo motivo.
- **Rede da sessão:** o download do SDK da Microsoft é recusado; o SDK 10.0.401 veio do cache do Nix (RESUME).
- **Mudanças no que o Revit cria ou lê** (K14, K15) não foram feitas sem poder conferir no Revit (D-031).

## 14. Problemas conhecidos

KNOWN_ISSUES tem a lista com evidência e próximo passo. Os principais:

- K3: as versões 2.8.0–2.19.0 não rodaram no Revit.
- K11, K12, K16: pisos sobrepostos, retas fora do eixo e a rotatória com by-pass – corrigidos no núcleo, a conferir no 5º
  Autoteste completo.
- K14, K15: piso tátil longo e leitura das marcas no documento – medidos, não mudados.
- K17, K19: atualização automática e fundo da estrutura sob a pista no Revit.
- K23–K27: faixa, Central, painel, telas, Central de Qualidade, quantitativos, configurações, progresso e fluxo integrado não
  vistos no Revit.
- K5, K6, K8: drenagem com área só da via, tráfego não calibrado, sem distância de visibilidade.

## 15. Recomendações futuras

Em ordem de prioridade (risco primeiro, como no Prompt Master §28.5):

1. **5º Autoteste completo no Revit** com a DLL 2.19.0, numa cópia, com **Fluxo integrado**, terminando com Desfazer tudo;
   enviar o relatório e o `-dados.jsonl`. Ele confere K3, K11, K12, K16, K17, K19 e K27 de uma vez, e os defeitos são
   reproduzidos no núcleo pelo arquivo de dados.
2. **Abrir as telas no Revit** (K23–K27): as duas guias, a Central, o painel, a janela da via, a Central de Qualidade, os
   Quantitativos, as Configurações e a janela de progresso, cancelando um Atualizar Todas.
3. **K15** com o plano registrado: uma leitura do documento por geração de marca, medida pelos contadores do Autoteste e
   conferida pelo "Modelo final".
4. **K14:** relevos do piso tátil agrupados por placa, conferindo a área e a contagem das unidades.
5. **Progresso** na Terraplenagem e no Validar Modelo, pelo mesmo `IProgressoOperacao`.
6. **F12:** cenários de projeto comparados pelos quantitativos.
7. **K18:** desfazer um ciclo entre pastas do Core sempre que a mudança já tocar aquele código.

## Artefatos

- DLL 2.19.0 em `Releases/Etapa13/SinalizaBIM.dll` (com o `.addin` e o `RELEASE.md`: commit, SHA-256, compilação e testes) e
  a mesma em `Instalar/Revit2027`. As anteriores continuam em `Releases/` (índice em `Releases/README.md`).
- Medição do núcleo: `docs/evidencias/2026-10-27-desempenho-nucleo.txt` (reproduzir: `dotnet run -c Release --project
  tools/Desempenho`).
