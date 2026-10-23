# AUDIT – Auditoria técnica do SinalizaBIM

Auditoria iniciada em 2026-10-19 sobre o commit `38c8c10` (ramo `claude/dreamy-gates-a3nrta`). Cada afirmação traz a
evidência e é marcada como **FATO** (verificado por comando, teste ou leitura do código), **INFERÊNCIA** (conclusão
provável a partir de fatos) ou **HIPÓTESE** (risco plausível ainda não demonstrado).

> Ambiente da auditoria: Linux, .NET SDK 10, sem Revit. **Nada aqui foi executado dentro do Revit.** Tudo o que depende
> do Revit (API, transações, DMU, desempenho em modelos reais) está, no máximo, "implementado, mas não validado neste
> ambiente".

## 1. Inventário

| Item | Evidência | Tipo |
|---|---|---|
| Solução `SinalizacaoViaria.slnx` com 3 projetos: Core (`net10.0`), Revit (`net10.0-windows`, WPF) e testes xUnit (`net10.0`) | arquivos `.csproj` | FATO |
| Core: 133 arquivos C#, ~52 400 linhas (sem o Clipper2); Revit: 101 arquivos, ~31 700 linhas; testes: 85 arquivos, ~19 500 linhas | `find … \| wc -l` | FATO |
| O Core é compilado **dentro** da DLL do plugin (`<Compile Include="..\SinalizacaoViaria.Core\**\*.cs">`), e o resultado é uma DLL única, `SinalizaBIM.dll` | `SinalizacaoViaria.Revit.csproj` | FATO |
| A API do Revit vem de `Nice3point.Revit.Api.RevitAPI/RevitAPIUI` **2027.0.0**, só como referência de compilação (`ExcludeAssets="runtime"`) | `.csproj` | FATO |
| Biblioteca de terceiros: Clipper2 1.5.4 (Boost Software License 1.0), em código-fonte, sem modificações | `ThirdParty/Clipper2/README.md`, `LICENSE.txt` | FATO |
| Pacote de instalação: `Instalar/Revit2027/SinalizaBIM.dll` + `SinalizaBIM.addin` (versionados no git) | `ls Instalar/Revit2027` | FATO |
| Manifesto `.addin`: `Type="Application"`, `FullClassName=SinalizacaoViaria.Revit.App` | `SinalizaBIM.addin` | FATO |
| 93 classes `Cmd*`. A faixa de opções registra 90 delas (12 painéis); `CmdAutoteste` abre pela janela Sobre/Relatórios (`ReportCommands.cs:194`); `CmdAreaConflito` e `CmdNovaVia` não são alcançáveis | `RibbonBuilder.cs`, `grep` | FATO |
| CI: GitHub Actions `build.yml` em `windows-latest` roda os testes do Core e compila o plugin a cada push. As últimas 5 execuções no ramo foram bem-sucedidas (run 99 = `38c8c10`) | API do GitHub Actions | FATO |
| Documentação existente: `README.md`, `docs/MANUAL.md` (~2 950 linhas), `docs/CATALOGO.md`, `docs/ESTUDO-MANUAIS.md` e `docs/ARQUITETURA.md` | `ls docs` | FATO |

## 2. Compilação e testes (linha de base)

| Verificação | Resultado | Tipo |
|---|---|---|
| `dotnet build src/SinalizacaoViaria.Core -c Release` | 0 erros, 0 avisos | FATO |
| `dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true` (no Linux) | 0 erros, 0 avisos (`CA1416` silenciado no `.csproj`) | FATO |
| Compilação dos testes | 0 erros e 2 avisos distintos: `AB2IntersectionTests.cs:452` (xUnit2029) e `AD6AutoSignageTests.cs:19` (CS0028) | FATO |
| Suíte do Core | Ver [TESTS.md](TESTS.md). A rodada anterior (`38c8c10`) teve 2 704 testes e 100 % aprovados, com a mesma suíte executada no CI Windows | FATO |
| Pacotes de teste com versão flutuante (`17.*`, `2.*`): a restauração pode mudar sem nenhuma mudança no repositório | `SinalizacaoViaria.Core.Tests.csproj` | FATO, com risco de reprodutibilidade |

### Compatibilidade com o Revit 2027

- **FATO:** o plugin compila contra as referências 2027.0.0 do NuGet Nice3point, com o framework-alvo `net10.0-windows`.
- **INFERÊNCIA:** o Revit 2027 carrega add-ins .NET 10, porque o pacote de referência 2027 exige `net10.0` e a compilação passa. Isso não prova o carregamento em execução.
- **FATO (2026-10-19, relatório do usuário em `docs/evidencias/2026-10-19-autoteste-rapido.txt`):** o Revit 2027.0 (build
  27.0.4.412) carregou a DLL 2.0.0.0 de 2026-10-19 (com EV1–EV4) e rodou o **Autoteste rápido**: 8 etapas, 4 OK, 4 com
  aviso, **0 erros**, em 3 min 12 s. As etapas cobrem preparação, loteamento (gleba, quadras e lotes; lote personalizado; unir
  lotes), vias (local, coletora, avenida) e interseção em cruz.
- **FATO:** **Validar Modelo** rodou no mesmo Revit, somente leitura, num projeto com 4 marcas e 48 elementos
  (`docs/evidencias/2026-10-19-validacao-modelo.txt`). Teve 1 achado V06, que levou ao ajuste da severidade (ver CHANGELOG
  EW1).
- **FATO (2026-10-19 23:19, `docs/evidencias/2026-10-19-autoteste-rapido-2.1.0.txt`):** a DLL **2.1.0** (com EW1–EW3)
  rodou o Autoteste rápido no mesmo Revit: 7 etapas, 4 OK, 3 com aviso, **0 erros**, em 3 min 6 s (a interseção em cruz
  ficou fora pelo limite de tempo do modo rápido). O projeto já tinha 216 marcas, entre elas o loteamento do Autoteste
  anterior, mantido para inspeção.
- **FATO (2026-10-20, `docs/evidencias/2026-10-20-autoteste-completo.txt` e o trecho do log no mesmo diretório):** a DLL
  2.1.0 com EX rodou o Autoteste **completo**: 178 etapas (loteamento, vias, conexões, rotatórias, cul-de-sacs,
  sinalização horizontal e vertical, calçadas, drenagem, largura e recuos, segurança viária, tráfego), 97 OK, 80 com
  aviso e **1 com erro** (perfil de 2 peças das placas recusado pelo Revit; corrigido em EY), em 1 h 42 min.
- **FATO (2026-10-20 09:44, `docs/evidencias/2026-10-20-autoteste-completo-2.txt`):** a DLL do EY (07:59) rodou o 2º
  Autoteste **completo**: 178 etapas, 106 OK, 72 com aviso e **0 erros**, em 1 h 41 min. A correção das placas (EY1) foi
  confirmada no Revit; as retas fora do eixo caíram de 1 385 para 85 (as 85 corrigidas no núcleo em EZ); 622 pares de pisos
  continuam sobrepostos, agora listados por tipo (K11).
- **FATO (2026-10-20 15:12, `docs/evidencias/2026-10-20-autoteste-completo-3.txt`):** a DLL do EZ rodou o 3º Autoteste
  **completo**: 178 etapas, 110 OK, 67 com aviso e **1 erro** (rotatória oval: forma direta recusada; corrigido em FA), em
  1 h 41 min. Retas fora do eixo: 8 (rotatória com by-pass; corrigidas no núcleo em FA); 590 pares persistentes, com as cotas
  de base e topo, que mostraram a causa dos maiores (tipo do primeiro piso de cada cor aplicado a todas as peças na nova
  geração – D-012).
- **FATO (2026-10-21 09:35, `docs/evidencias/2026-10-21-autoteste-completo-4.txt`):** a DLL do FB rodou o 4º Autoteste
  **completo** no Revit 2027.3 (build 27.3.0.28): 180 etapas, 111 OK, 68 com aviso e **1 erro** (rotatória oval:
  `CreateExtrusionGeometry` recusou um contorno; corrigido no FC), em **36 min**. Etapa "Modelo final": 323 pares de pisos
  sobrepostos (3,10 m³) e 1 reta fora do eixo (mais 2 de 0°, ruído). O arquivo de dados permitiu reproduzir no núcleo cada
  par tratado no FC sem outro Autoteste (`FCRevitFullRun4Tests`).
- **LIMITE DA EVIDÊNCIA:** dois builds do Revit (27.0 e 27.3); um projeto; obras de arte e pranchas não estão no roteiro do
  Autoteste. As correções do FC (rotatória oval, pinturas sobre a sarjeta, T de via reconhecida, cruzamento rodoferroviário,
  arcos das esquinas, lombada invertida) ainda não rodaram no Revit.
- **Desempenho (FB, medido nos relatórios):** no 3º completo, a mesma operação ficava mais lenta à medida que o projeto
  crescia: uma via de 12 elementos levava 5 s no começo e um cul-de-sac de 30 elementos, 46 s no meio do teste. A causa era a
  leitura de todas as definições a cada consulta; foi corrigida no código sem mudar o resultado (`FBAutotestSpeedTests`). O
  4º completo, com a DLL do FB, levou 36 min contra 1 h 41 min do 3º (180 etapas contra 178).
- **Avisos do Revit nos relatórios** (todos nas etapas com interseção ou loteamento; as vias isoladas não tiveram nenhum):
  - "A linha no croqui está ligeiramente fora da linha central": reproduzido no núcleo e corrigido nos pisos das marcas
    (EW2: no 2º relatório, de centenas para 62). Os 62 restantes não diziam em que pisos; EX levou o alinhamento aos
    caminhos que ainda não o tinham (lotes, áreas remanescentes, acabamentos do terreno, contorno só de retas) – a
    confirmar (KNOWN_ISSUES K12);
  - "Os elementos têm valores de 'Marca' duplicados" (65 + 12 + 11 no 2º relatório): defeito real com dois loteamentos no
    projeto, corrigido em EX (DECISIONS D-009). No completo não houve o aviso, mas o projeto tinha só 6 marcas antes do
    teste (sem um segundo loteamento): a correção não foi exercitada;
  - no completo: 1 385 retas fora do eixo (causa achada em EY: critério de 0,2° com 45° e pontas deslocadas pelo ajuste
    do contorno; KNOWN_ISSUES K12) e 3 091 avisos de pisos sobrepostos, dos quais 614 pares persistem (K11, K13);
  - "Pisos realçados se sobrepõem" (239 no 2º relatório, 23 entre elementos da mesma marca): padrão de sobreposição de
    passagem entre vias que se cruzam, não reproduzido no núcleo; o Autoteste agora mede no fim da etapa se os pares ainda
    se sobrepõem (KNOWN_ISSUES K11).

## 3. Arquitetura observada

Resumo; o detalhe está em [ARCHITECTURE.md](ARCHITECTURE.md).

- **FATO:** o Core é puro .NET, sem dependência do Revit. Contém a geometria, os geradores, as definições, o tráfego, o parcelamento, os quantitativos e os relatórios. É testado em qualquer sistema operacional.
- **FATO:** o Revit é a camada de integração: comandos (`IExternalCommand`), `MarkingService` (renderização em `DirectShape`/`Floor`/`FilledRegion`/textos), `MarkingStorage` (Extensible Storage), `MarkingUpdater` (DMU) e janelas WPF.
- **FATO:** a persistência guarda o JSON da `MarkingDefinition` em **cada** elemento gerado (esquema `7C1E9A52-…`, campos `MarkingId`, `Definition`, `Color` e `SchemaVersion`). Definições sem desenho (loteamento, cortes só de dados) ficam em `DataStorage`. As configurações do projeto ficam em entidades nas `ProjectInformation`: placas personalizadas, cenários de tráfego, estilos de seção e georreferência.
- **FATO:** a identidade persistente é `MarkingDefinition.Id` (GUID "N"). Os elementos de uma mesma marca compartilham o Id; `CloneWithNewId` gera cópias independentes, e `CopyHandler` trata as cópias feitas pelo usuário.
- **FATO:** as dependências entre definições estão implícitas em propriedades (`TargetId`, `RoadIds`, `MainRoadId`, `RoadId`, `ChildIds`, `LoteamentoId`, `SectionId`, `ExclusionZone.SourceId`, `PathReference.ElementIds`). **Não há grafo explícito, detecção de ciclos nem verificação de referências quebradas.**
- **FATO:** há versionamento de dados (`MarkingDefinition.Version = CurrentVersion = 1`, e `SchemaVersion` na entidade), mas **nenhum pipeline de migração**. Uma definição com `Version` maior (gravada por versão futura) é lida e regravada sem aviso.
- **FATO:** a concorrência está correta no que foi visto. A única computação em segundo plano é `Task.Run` com código só do Core em `TrafficWindow` (janela modal aberta dentro do comando); a API do Revit é chamada só na thread principal. Não é preciso `ExternalEvent` no desenho atual.
- **FATO:** há 71 `new Transaction(`, 16 `TransactionGroup` e 17 rollbacks explícitos. O DMU não abre transações (regra da API respeitada). Existe um `IFailuresPreprocessor` (`TerrainFailures`, `InfraCommands.cs:511`) que reverte em caso de erro, mas **apaga os avisos sem registrá-los**.

## 4. Riscos e defeitos encontrados (priorizados)

| # | Prioridade | Achado | Evidência | Tipo | Encaminhamento |
|---|---|---|---|---|---|
| R1 | 1 – segurança de dados | Definição gravada por versão **mais nova** do plugin (`Version > CurrentVersion`) é lida, regenerada e **regravada como versão 1**, perdendo campos desconhecidos | `MarkingStorage.Read` não verifica `Version`; `System.Text.Json` ignora membros desconhecidos | FATO (código); efeito = INFERÊNCIA | EV3: proteção de versão + base de migrações |
| R2 | 1 | Marca com várias cópias (uma por cor ou elemento): `Definitions()` usa a **primeira** cópia. Se as cópias divergirem (gravação parcial, edição externa), a escolha é arbitrária | `MarkingStorage.cs:91` | HIPÓTESE (não reproduzida) | EV4: o validador aponta "cópias divergentes" |
| R3 | 1 | Ao passar de 2 MB, o log é **apagado** e o histórico de erros se perde | `Log.cs:35` | FATO | EV3: rotação (`log.1.txt`) |
| R4 | 2 – defeitos | Contorno degenerado é descartado em silêncio ao criar piso (pode faltar parte do piso sem aviso) | `MarkingService.cs:1589` | FATO | EV3: contar e avisar |
| R5 | 2 | Calibração e importação de imagem: falha ao ler o Norte do projeto vira 0° sem aviso | `AerialImageCommands.cs:202`, `:360` | FATO | EV3: registrar no log |
| R6 | 2 | `TerrainFailures` apaga todos os avisos do Revit sem registrá-los | `InfraCommands.cs:520` | FATO | EV3: registrar no log |
| R7 | 4 – integridade geométrica | Não há verificação de autointerseção de polígonos (quadras, glebas, contornos); `Polygon2.IsValid` só testa ≥ 3 vértices e área > 1e-8 | `Polygon2.cs:35` | FATO | EV4/EV5: `PolygonChecks` + validador |
| R8 | 4 | Tolerâncias espalhadas (`Vec2.Eps = 1e-9`, `BoundaryFit 0.005`, `FloorTiling 0.0005/0.001`, `TrimTools 0.03`…), sem política central documentada | `grep const double` | FATO | documentado em ARCHITECTURE.md; sem refatoração |
| R9 | 5 – testes | Testes organizados por rodada de funcionalidade; não há testes focados nas primitivas geométricas em casos degenerados (PIs coincidentes, deflexão ~180°, polilinha de 1 ponto) | lista de arquivos de teste | FATO | EV5 |
| R10 | 5 | 2 avisos de compilação nos testes; pacotes de teste com versão flutuante | log de compilação | FATO | EV3 |
| R11 | 6 – arquitetura | Catálogo global (`PluginContext.Catalog`) recebe as placas personalizadas de **cada** documento aberto: com dois projetos abertos, as placas de um aparecem no outro | `CustomSignStore.Apply` → `CustomSigns.Apply(PluginContext.Catalog, …)` | INFERÊNCIA (código) | KNOWN_ISSUES; correção exige catálogo por documento |
| R12 | 6 | Comandos inalcançáveis (`CmdAreaConflito`, `CmdNovaVia`): código morto, mantido (a função existe por Zebrado e Via) | `RibbonBuilder.cs` | FATO | KNOWN_ISSUES (não apagar sem pedido) |
| R13 | 10 – documentação | `docs/ARQUITETURA.md` descreve só a 1ª versão (5 geradores); o README do Clipper2 cita nomes antigos de arquivo | leitura | FATO | EV2: ARCHITECTURE.md substitui |
| R14 | 11 | Microssimulação de tráfego: modelo IDM com aceitação de brechas, **não calibrado** com dados de campo; o mapa anima os quadros já simulados | `TrafficSimulation.cs` | FATO | AUDIT §6.11; sem afirmar precisão |

Fora do escopo do achado: as ~93 capturas `catch { }` no Revit são, na maioria, leituras de propriedades opcionais (`Parameter?.Set`, metadados), sem efeito sobre dados. Foram revisadas uma a uma e só as acima afetam resultados.

## 5. Matriz de funcionalidades

Legenda de estado:

- **VALIDADA (Core)**: lógica coberta por testes automatizados que passam (local e CI). A integração com o Revit não foi validada neste ambiente.
- **IMPLEMENTADA, NÃO VALIDADA**: código presente, sem teste automatizado que cubra o comportamento principal.
- **PARCIAL**: parte do escopo pedido existe.
- **PROTÓTIPO**: funciona em casos simples, sem robustez.
- **AUSENTE**.
- **BLOQUEADA**: depende de algo indisponível.

| Fase | Funcionalidade | Estado | Evidência |
|---|---|---|---|
| II | Compilação do Core e do plugin, empacotamento em DLL única | VALIDADA (Core); a DLL é gerada | builds sem avisos, CI verde, `Instalar/Revit2027` |
| II | Inicialização no Revit (faixa de opções, DMU) | VALIDADA no Revit 2027.0 (build 27.0.4.412) pelo Autoteste rápido do usuário | `docs/evidencias/2026-10-19-autoteste-rapido.txt` |
| III | Definições paramétricas persistentes (JSON polimórfico, Extensible Storage) | VALIDADA (Core: serialização); Revit não validado | testes de ida e volta JSON em várias rodadas |
| III | Identidade persistente (GUID) e cópias independentes | VALIDADA (Core) / IMPLEMENTADA (Revit `CopyHandler`) | `CloneWithNewId`, testes |
| III | Grafo de dependências sem ciclos | VALIDADA (Core) desde EV4; usado na validação, ainda não na regeneração (D-004) | `ModelGraph`, `EVModelIntegrityTests` |
| III | Migrações versionadas | VALIDADA (Core) desde EV3, como base; nenhum passo registrado ainda (só existe a v1) | `DefinitionVersioning`, testes de migração simulada |
| III | Regeneração associativa (DMU por caminho e por dependentes) | IMPLEMENTADA, NÃO VALIDADA aqui | `MarkingUpdater`, `RenderDependents` |
| IV | Primitivas 2D (Vec2, Polyline2 com estaqueamento/offset, Polygon2, booleanas Clipper2) | VALIDADA (Core), indiretamente | usadas por milhares de testes; poucos testes diretos de casos degenerados |
| IV | Validação de polígono (autointerseção, área nula) | VALIDADA (Core) desde EV4 | `PolygonChecks`, testes de casos degenerados |
| IV | Sólidos 3D (varredura com greide, tesselação) | VALIDADA (Core) | `Solid3D`, testes das rodadas H/I |
| V | Alinhamento horizontal por PIs (curva circular + clotoides) e eixo das vias comuns | VALIDADA (Core). Relatório de elementos do traçado (TS/SC/CS/ST) desde EV5 e do eixo das vias comuns (tangentes, curvas, cantos) desde EW3, em CSV no Perfil da Via | `Tracado`, `AlignmentElements`, `EVAlignmentElementsTests`, `EWAxisElementsTests` |
| V | Perfil vertical (PIVs, curvas parabólicas, rampas, superelevação, caimentos) | VALIDADA (Core); elementos (PCV/PTV, K, alto/baixo) e CSV desde EV5; sem verificação de distância de visibilidade (não inventar critério) | `RoadGrade`, `AlignmentElements` |
| V | Projeto de greide automático (acompanhar terreno, compensado) | VALIDADA (Core) | `RoadProfileDesigner`, testes rodadas J/K/AL3 |
| V | Limites geométricos informados pelo usuário (raio, tangente, deflexão sem curva, rampa, K, curva vertical) com a fonte | VALIDADA (Core) desde a Fase V; CSV de verificação no Perfil da Via e V12 no Validar Modelo **não executados no Revit**; sem valores normativos no plugin | `GeometricCheck`, `Fase05AlinhamentoTests` |
| VI | Seção transversal da via (faixas, ciclofaixas, calçadas, canteiros, larguras variáveis) | VALIDADA (Core) | `RoadSetup*`, centenas de testes |
| VI | Pavimento (asfalto, bloquete, concreto, terra) e camadas no perfil | VALIDADA (Core) | rodadas AD3/LX2 |
| VI | Corredor 3D (pisos no greide, obras hospedadas) | VALIDADA (Core) / Revit não validado | rodadas K/L/VT |
| VI | Quantitativo por componente da seção (pavimento, meio-fio com extensão, sarjeta, calçada, gramado, ilhas) | VALIDADA (Core) desde a Fase VI; aba, CSV e planilha **não executados no Revit** | `ComponentQuantities`, `Fase06ComponentesTests` |
| VI | Regeneração depois de mudar eixo, greide ou seção | Regras do núcleo VALIDADAS (Fase VI); updater no Revit não validado (K17) | `ModelGraph.ConnectionsToRefresh`, `IntersectionGenerator.FollowNode`, `Fase06RegeneracaoTests` |
| VII | Terreno (TIN, Toposolid nativo) | VALIDADA (Core) / Revit não validado | `Tin`, `TerrainMesh`, `Grading` |
| VII | Terraplenagem: corte/aterro, taludes, bermas, volumes | VALIDADA (Core); desde EV6 também o método das seções, conferido com a malha; desde a Fase VII as seções de cada via vão ao quantitativo do Revit (aba, CSV e planilha – **não executados no Revit**) | `Grading.Volumes`, `SectionVolumes` (§6.7), `Fase07InterseccoesTests` |
| VII | Interseções (T, cruz, oblíquas, tipos II–IV), rotatórias, nós em desnível | VALIDADA (Core) | rodadas S2, V2, AB2, AE3 |
| VIII | Dispositivos de drenagem superficial (bocas de lobo, grelhas, PV, sarjetão, canaletas) – geometria | VALIDADA (Core) | `DrainageGenerator`, `SarjetaoGenerator` |
| VIII | Rede de drenagem (trechos entre caixas, sentido, ligações, ciclos, recobrimento, diâmetros, PVs) | VALIDADA (Core) desde a Fase VIII; comandos Trecho de Rede e Relatório da Rede **não executados no Revit** | `DrainageNetwork`, `Fase08DrenagemTests` |
| VIII | Dimensionamento pelo método racional (IDF, C, tc acumulado, Manning a seção plena, sarjetas, bocas de lobo) e Memorial de Drenagem | VALIDADA (Core) contra os exemplos 6.1–6.3 e a tabela B.3 do Manual SUDERHSA e a planilha do memorial modelo (fontes do usuário, D-019); Revit não validado; limites em K5 | `DrainageHydraulics`, `DrainageMemorial`, `Fase08HidraulicaTests` |
| IX | Loteamento: gleba, quadras, lotes por testada e por linhas, chanfros, uniões, numeração, categorias | VALIDADA (Core) | rodadas LT/LU/LV/LW/LX |
| IX | Regras mínimas configuráveis (frente 5 m e área 125 m² por padrão, editáveis) | VALIDADA (Core) | `LoteamentoDefinition.Minimos`; **sem aprovação legal automática** |
| IX | Quadros de áreas, de quadras e de lotes; memorial `.docx` | VALIDADA (Core) | `LotReport`, `LotMemorial`, `DocxWriter` |
| X | Verificações no Revit (pintura × pisos, continuidade, terreno × via) | IMPLEMENTADA, NÃO VALIDADA aqui | `AutoTestChecks.cs` |
| X | Validação do modelo com severidade, confiança e revisão humana | VALIDADA (Core) desde EV4/EV5; o comando rodou no Revit 2027 (relatório do usuário, 2026-10-19) | `ModelValidator` V01–V13, `CmdValidarModelo`, `docs/evidencias` |
| XI | Análise de capacidade, atraso e nível de serviço; Webster | VALIDADA (Core), não calibrada | `TrafficAnalysis`, testes S3/T8/AA2 |
| XI | Microssimulação (IDM, brechas, semáforos, pedestres) | VALIDADA (Core: invariantes como sem colisão e fila coerente); **não calibrada com dados de campo** | testes V4/W5/AA2 |
| XI | Animação | IMPLEMENTADA (é reprodução dos quadros simulados, não substitui a simulação) | `SimFrame`, `TrafficMap` |
| XII | Cenários de tráfego, comparação e teste de soluções | VALIDADA (Core) | `TrafficScenario`, `TrafficSolutions` |
| XII | Cenários de projeto (geometria) e otimização geral | AUSENTE / PARCIAL (greide compensado, tempos semafóricos) | — |
| XIII | Janelas WPF com prévia ao vivo (Core) | IMPLEMENTADA, NÃO VALIDADA aqui | `UI/*.xaml` |
| XIV | Transações, rollback, `FailuresPreprocessor` | IMPLEMENTADA, NÃO VALIDADA aqui | §3 |
| XV | Autoteste dentro do Revit (relatório) | VALIDADA no Revit 2027: modo rápido (DLL 2.0.0 e 2.1.0) e **completo** quatro vezes (2026-10-20, 178 etapas; 2026-10-21, 180 etapas com o arquivo de dados e a conferência final), com o resumo por elemento, a conferência das sobreposições, os pares persistentes por tipo, a medida das retas fora do eixo e (desde EZ) as cotas e a espessura do tipo dos exemplos funcionando | `AutoTest*.cs`, `WarningSummary`, `docs/evidencias` |
| — | Sinalização horizontal e vertical (MBST), dispositivos, mobiliário, acessibilidade (NBR 9050/16537) | VALIDADA (Core) | maior parte da suíte |
| — | Detalhamento, pranchas, quantitativos, memorial | VALIDADA (Core) / Revit não validado | rodadas AF/AH/AI |
| — | Georreferência e calibração de imagem | VALIDADA (Core) | `Geo/*`, testes X1 |

## 5.1 Situação dos riscos após EV3/EV4

| Risco | Situação |
|---|---|
| R1 | **Corrigido:** definição mais nova é somente leitura (D-003) |
| R2 | **Mitigado:** detectado pelo validador (V03); a escolha da cópia continua igual |
| R3 | **Corrigido:** rotação do log |
| R4, R5, R6 | **Corrigidos:** registrados no log; R4 também avisa o usuário |
| R7 | **Corrigido:** `PolygonChecks` + V07 |
| R10 | **Corrigido:** 0 avisos; pacotes fixados |
| R13 | **Corrigido:** ARCHITECTURE.md |
| R9 | **Corrigido** (EV5): testes de casos degenerados das primitivas (`EVGeometryEdgeTests`) |
| R8, R11, R12, R14 | Abertos: ver KNOWN_ISSUES e ROADMAP |

## 6. Notas por domínio

- **§6.5 Alinhamento.** Quando o raio pedido não cabe entre os PIs vizinhos, ele é **reduzido com aviso** (`Tracado.Build`). As espirais são simétricas e o desenvolvimento é limitado a 95 % do arco. A integração é numérica (passo ≤ 1,5° de deflexão). Ver `Tracado.cs`.
- **§6.7 Volumes de terraplenagem.** O método é a **amostragem estratificada em malha**: um ponto fixo, sorteado de forma determinística, por célula; o volume é Σ(Δz·área da célula); a malha é limitada a ~250 mil amostras. Está em `Grading.Volumes`, `Grading.cs:1262`. O próprio código explica a escolha: com o centro da célula, o erro chegava a 4 % em vias alinhadas à malha. Desde EV6 existe no Core o **método das seções transversais** (`SectionVolumes`: seções perpendiculares ao eixo, média das áreas, sem correção de curvatura). Ele é conferido com a malha num teste (diferença < 2 %), mas ainda não aparece no quantitativo do Revit.
- **§6.8 Drenagem.** Dispositivos com encaixe na via (rebaixo de sarjeta, caixa, grelha, tubo de ligação; DNIT 030/2004-ES, NBR 10160) e, desde a Fase VIII, a **rede** (trechos entre as caixas) com **dimensionamento pelo método racional** segundo as fontes enviadas pelo usuário (Manual SUDERHSA 2002 e memorial modelo de Miguelópolis/SP 2019; D-019): planilha de galerias, captações, V13 e Memorial de Drenagem. A planilha do memorial modelo e os exemplos do manual são testes. Limites em K5; nada executado no Revit.
- **§6.11 Tráfego.**
  - A análise usa formulações do tipo HCM e Webster.
  - A microssimulação usa IDM (parâmetros a, b e T por veículo) com zonas de conflito.
  - Os testes verificam **coerência** (sem colisões, filas com muita demanda), não a **precisão** contra campo.
  - Os resultados devem ser lidos como estimativa de estudo.

## 7. RT-1 – Integração Toposolid × vias × terraplenagem × drenagem (auditoria RT-1.1, 2026-10-22)

Levantamento pelo código (nada executado no Revit neste ciclo). Os métodos da API citados são os que o plugin já chama e
que compilam contra o pacote do Revit 2027 referenciado (K10); nenhum foi presumido.

### 7.1 Terreno (Toposolid)
- **Criação:** `Toposolid.Create(doc, loops, pontos, tipo, nível)` só quando o projeto não tem terreno e o usuário pede
  "criar terreno" (`TerrainService.cs:709`); o Autoteste cria o dele (`AutoTestSteps2.cs:37`).
- **Leitura:** malha atual (`TerrainModel.Live`) e **terreno original**, gravado no próprio Toposolid em armazenamento
  extensível na primeira terraplenagem (`TerrainModel.Save/Original`, `TerrainModel.cs:89–130`); `TerrainModel.Ground` dá a
  cota natural sob um ponto (volumes, seções por estaca, Relatório da Rede).
- **Alteração:** editor de forma (`GetSlabShapeEditor`, `AddPoints`/`AddPoint`; `TerrainService.cs:414` e `639–650`): os
  pontos dentro da área de projeto e da área da terraplenagem anterior (com 1 m de folga) são trocados pela superfície de
  projeto; fora dela voltam os originais (`TerrainRebuild.Points`). A superfície suavizada é desligada
  (`Toposolid.SetSmoothedSurface`, `TerrainService.cs:345–347`) e o tipo do Toposolid tem a espessura ajustada (`611–622`).
- **Escavação:** `CanBeExcavatedBy`/`ExcavateBy` com túneis e caixas de drenagem (`TerrainService.cs:444–448`).
- **Subdivisões:** `CreateSubDivision` para grama de taludes e ilhas, mapa de corte e aterro e lotes
  (`TerrainService.cs:748`, `LotService.cs:104/174`).
- **Separação lógica:** terreno original = dado gravado no Toposolid; terreno projetado = o próprio Toposolid editado;
  pavimentação = pisos. Não há um Toposolid projetado separado: a reversibilidade hoje é pelo dado (a região é refeita a
  partir do original a cada terraplenagem) e pelo Desfazer do Revit; `TerrainModel.Reset` esquece o original (novo
  levantamento).

### 7.2 Pisos das vias
- Um `Floor` por peça, com o tipo pela espessura (D-012). O topo segue o greide da via (perfil, abaulamento e
  superelevação do `RoadGrade`) por edição de forma em lote (`MarkingService.FlushShapeEdits`, `MarkingService.cs:2181–2270`):
  os vértices do contorno ficam no plano de cada faceta do greide (pista e pintura nos mesmos planos, rodada VT) ou nos
  pontos da malha do terreno quando a marca acompanha a superfície (`AddDrapePoints`). A cota vem do projeto – o piso não
  é movido até "encostar".

### 7.3 Terraplenagem
- Núcleo `Grading`: plataformas e taludes de corte e aterro até o terreno natural, com os taludes de cada via (ou os
  padrões 1,0 e 1,5 H:V, configuráveis em `GradingOptions`); plataforma por camadas – o terreno fica no fundo da estrutura
  sob a pista (`Subgrade` = 0,40 m sob o topo) e sobe ao topo do acabamento nas bordas externas, onde começa o talude
  (`Grading.cs:650–690`, `Layered`; rodada VT). Volumes pela malha e pelas seções (EV6, F7b).

### 7.4 Drenagem
- Os dispositivos encaixam na face do meio-fio e adotam a cota da base da guia (`DrainageCommand.Fit`); a rede (Fase VIII)
  usa as cotas de tampa e de fundo das caixas. A declividade da sarjeta e a área de contribuição são informadas em cada
  dispositivo (K5), não lidas da via nem do terreno.

### 7.5 Verificações que já existem
- Autoteste, `AutoTestChecks.CheckRoad`: pintura × pisos (escondida ou flutuando), buracos e degraus no pavimento e terreno
  × via (Toposolid acima da pista; desnível de mais de 30 cm entre o terreno e a borda da seção). **É a opção "Verificações
  do modelo", desligada por padrão (`AutoTestOptions.Checks`), e não rodou em nenhum dos quatro Autotestes completos.**
- Núcleo: `PieceOverlaps` (pares de pisos sobrepostos) e a conferência final dos pisos do Autoteste (pares por tipo).

### 7.6 Lacunas (destino no ROADMAP)
1. Terreno × via no núcleo, sem o Revit: topo dos pisos × superfície de projeto e terreno nas bordas (RT-1.2, Fase X).
2. Classificação dos pares sobrepostos em camadas intencionais, interseções legítimas, duplicações, conflitos reais e
   inconclusivos (RT-1.3, Fase X).
3. "Verificações do modelo" ligadas no Autoteste completo (RT-1.2/RT-1.6).
4. Drenagem lendo a declividade da sarjeta do greide e a área do modelo; pontos baixos sem captação (RT-1.5).
5. Toposolid projetado separado do original: avaliar contra a edição atual com o original gravado (RT-1.4, decisão a
   registrar).


## 8. Auditoria pelo Prompt Master (2026-10-23)

Em 2026-10-23 o usuário enviou o **Prompt Master** (`docs/PROMPT_MASTER.md`). Ele amplia o escopo para uma plataforma de
infraestrutura urbana no Revit 2027 e troca a sequência rígida das 15 fases pela ordem de riscos e dependências
(DECISIONS D-021). Esta seção compara o código com cada parte do Prompt Master, sem refazer as auditorias anteriores.
Ela usa as cinco situações pedidas no item 28:

- **IT – implementada e testada:** testes automatizados do núcleo passam (local e CI). O Revit não foi validado, salvo
  quando indicado.
- **INV – implementada, não validada:** o código existe, mas sem teste automatizado do comportamento principal (em geral,
  código que só roda dentro do Revit).
- **P – parcial.**
- **A – ausente.**
- **B – bloqueada.**

Base: commit `6d2b72f`, compilação limpa, suíte 2 949/2 949 (Fase VIII). Nada desta seção foi executado no Revit.

### 8.1 Mapa dos módulos e dependências

A coluna "Usa" mostra a dependência **principal** de cada módulo. As pastas do Core são espaços de nomes de um único
assembly, não módulos isolados. A contagem das referências entre pastas (`SinalizacaoViaria.Core.X` citado em outra pasta)
mostra **referências mútuas**:
- `Geometry` usa as peças de `Model` (`MarkingPiece`, `MarkingColor`, em 5 arquivos), e `Model` usa `Geometry`;
- `Definitions`, `Generators` e `Automation` se citam entre si (`MarkingBuilder` chama os geradores, que leem as
  definições).

Isso compila (é um assembly só), mas contraria o §4 do Prompt Master. Está registrado como R15 (§8.3) e K18.

| Camada (Prompt Master §4) | Módulo | Onde | Usa |
|---|---|---|---|
| Domínio | Definições persistentes (≈ 70 tipos), versões e migrações, identidade (GUID) | `Core/Definitions`, `Core/Serialization`, `Core/Model/ModelGraph` | Geometria |
| Motor geométrico | Vec2, Polyline2, Polygon2, booleanas (Clipper2), traçado, greide, sólidos, contornos, pares sobrepostos | `Core/Geometry`, `Core/Model/PieceOverlaps`, `ThirdParty/Clipper2` | — |
| Motor topográfico | TIN (Delaunay), malha do terreno, terreno de projeto (plataformas e taludes), volumes (malha e seções) | `Core/Automation/Tin`, `TerrainMesh`, `Grading`, `SectionVolumes` | Geometria |
| Serviços – vias | seção (RoadSetup), greide automático, geradores das vias, interseções, rotatórias, nós, acessos | `Core/Automation/Road*`, `Core/Generators/*Road*`, `Intersection*`, `Roundabout*`, `Interchange*` | Geometria, Domínio, Topografia |
| Serviços – drenagem | dispositivos, rede, hidráulica (método racional), memorial | `Core/Generators/DrainageGenerator`, `Core/Automation/Drainage*`, `Core/Reports/DrainageMemorial` | Geometria, Domínio |
| Serviços – parcelamento | gleba, quadras, lotes, quadros, memorial | `Core/Parcelamento` | Geometria, Domínio, vias (área de circulação) |
| Serviços – sinalização e tráfego | sinalização (MBST), catálogo, simulação e análise | `Core/Generators`, `Core/Catalog`, `Core/Traffic` | Geometria, Domínio, vias |
| Serviços – quantitativos e relatórios | por marca, por componente, volumes, `.docx` | `Core/Quantities`, `Core/Reports` | todos os serviços |
| Validação (Core) | grafo de dependências, validador V01–V13, classificação de sobreposições (nova, Fase X) | `Core/Model` | Domínio, Geometria, drenagem, parcelamento |
| Adaptadores Revit | render e leitura (`MarkingService`, `PathResolver`, `SurfaceSampler`), persistência (`MarkingStorage`, `*Store`), terreno (`TerrainService`, `TerrainModel`), lotes, interseções | `Revit/Infrastructure` | Core |
| Atualização associativa | DMU (`MarkingUpdater`), cópias (`CopyHandler`) | `Revit/Updater` | Adaptadores, `ModelGraph` |
| Interface | 93 comandos, faixa de opções, janelas WPF com prévia, formulários | `Revit/Commands`, `Revit/UI` | Adaptadores, Core |
| Validação (Revit) | Validar Modelo, Autoteste (rápido e completo), conferência final dos pisos | `Revit/Commands/ValidationCommands`, `AutoTest*` | tudo |

### 8.2 Matriz pelo escopo do Prompt Master

| § | Requisito | Situação | Evidência e o que falta |
|---|---|---|---|
| 3 | Plugin nativo do Revit 2027, sem motor externo | IT | DLL única carregada no Revit 2027 (Autotestes); Core sem referência ao Revit (ARCHITECTURE §1) |
| 4 | Identificadores estáveis e rastreio projeto ↔ elemento | IT (Core) / INV (Revit) | `MarkingDefinition.Id` em cada elemento (Extensible Storage); `ModelGraph` |
| 5 | Núcleo geométrico com tolerâncias explícitas | P | primitivas e casos degenerados testados (EV5); tolerâncias documentadas, mas definidas em cada módulo (K9) |
| 5 | Detecção de geometria inválida, lacunas e sobreposições | P | `PolygonChecks` (V07), `PieceOverlaps`; **a classificação das sobreposições falta** (RT-1.3, Fase X) |
| 6 | Terreno existente × projetado separados | IT (Core) / INV (Revit) | original gravado no Toposolid (AUDIT §7.1); `Grading` |
| 6 | Diagnóstico da superfície (dados insuficientes, triângulos inválidos, descontinuidades, declividade fora do limite, extrapolação) | **A** | nenhum diagnóstico do TIN no núcleo; o Revit recebe os pontos sem conferência |
| 7 | Eixos, alinhamentos, greides, estacas, seções, prévia | IT (Core) / INV (prévia no Revit) | Fases V e VI; `RoadPreviewServer` (DirectContext3D) |
| 8 | Interseções e rotatórias calculadas, regeneração ao mudar eixos | IT (Core) / INV (updater, K17) | Fases VI e VII |
| 8 | Configuração impossível explicada, com os parâmetros a rever | P | avisos dos geradores (`IntersectionGenerator`, `RoundaboutGenerator`), sem lista padronizada de parâmetros |
| 9 | Corte e aterro com método documentado e limites | IT (Core) | `Grading.Volumes` (malha) e `SectionVolumes` (seções), AUDIT §6.7 |
| 9 | Cálculo de terraplenagem desatualizado depois de mudar a via | **A** | o Toposolid fica como estava até nova Terraplenagem; nada aponta que ficou velho |
| 10 | Estrutura de pavimento por via ou trecho (ordem, espessura, material) usada pelo cálculo | P | camadas só no desenho do corte (`SectionLayer`); a terraplenagem usa uma profundidade única de subleito (0,40 m, `TerrainService.Subgrade`); o piso tem só o revestimento |
| 10 | Sobreposições classificadas antes de qualquer correção | P → em andamento | `OverlapClassifier` (Fase X) |
| 11 | Drenagem: conectividade, sentido, cotas, ligações | IT (Core) / INV (Revit) | `DrainageNetwork`, V13, Fase VIII |
| 11 | Hidráulica separada da geometria, com entradas, hipóteses e limites documentados | IT (Core) | `DrainageNetwork.Check` (geometria) × `DrainageHydraulics` (método racional); memorial com as fontes; K5 |
| 11 | Drenagem integrada à topografia e aos greides | P | a área e a declividade da sarjeta são informadas por dispositivo (K5, RT-1.5) |
| 12 | Quadras e lotes por regras configuráveis, quadros e memorial | IT (Core) / validado no Revit | Fase IX; sem aprovação legal automática |
| 13 | Parâmetros pela interface, perfis salvos | P | formulários e Configurações; modelos de via salvos; Projeto de Drenagem por documento; sem salvar ou carregar perfis de terraplenagem e de interseção |
| 14 | Relações explícitas e regeneração só do necessário | P | `ModelGraph` + DMU; terraplenagem e drenagem fora do grafo de regeneração |
| 14 | Mostrar o que foi atualizado e o que ficou desatualizado | **A** | o DMU regenera em silêncio; nenhum estado "desatualizado" |
| 15 | Transações, rollback, falhas | INV | ARCHITECTURE §5; Autotestes no Revit sem documento inconsistente |
| 16 | Causa das retas fora do eixo e das sobreposições | P | K11 e K12 reproduzidos e corrigidos no núcleo até o 4º Autoteste; a confirmação depende de um novo Autoteste (K3) |
| 17 | Validação por módulo e transversal | P | V01–V13 com severidade e confiança |
| 17 | Erro / alerta / informação / **não verificável**; módulo, objeto, evidência e ação recomendada; resumo, métricas, versão; histórico antes × depois | P | `Achado` tem código, severidade, confiança, mensagem e Ids; **faltam** o módulo, a evidência, a ação recomendada, a categoria "não verificável" e a comparação com a validação anterior |
| 18 | Testes unitários e de integração, casos-limite e inválidos | IT | 2 949 testes; fixtures dos Autotestes |
| 19 | Autoteste com versão, data, ambiente, executados e ignorados | IT no Revit | 4 Autotestes completos (`docs/evidencias`) |
| 19 | Problemas conhecidos × novos × inconclusivos | P | pares persistentes por tipo; sem comparação com a rodada anterior nem marcação de "conhecido" |
| 20 | Fluxos por tarefa, unidades, prévia, mensagens acionáveis, cancelar | P | painéis por tarefa; cancelamento só em janelas de cálculo (`TrafficWindow` e outras) |
| 21 | Atualização incremental, cache com invalidação, desempenho | P | D-013 (leitura rápida); K14 e K15 abertos |
| 23 | DLL só de build limpo, versão rastreável, entregas preservadas | IT | `tools/release.sh`, `Releases/FaseNN` com SHA-256 |
| 24 | Documentação e progresso | IT | documentos de controle; esta seção |
| 1, 25 | Simulação urbana ou tráfego em escala de cidade | A (fora do escopo atual) | a microssimulação é local, não calibrada (K6); nada de equivalência com InfraWorks ou Cities: Skylines |

Correção desta auditoria na matriz da §5: a linha "VIII | Dimensionamento hidráulico | AUSENTE" ficou desatualizada com
a Fase VIII (o dimensionamento existe e é testado; ver a linha acima dela). Foi retirada.

### 8.3 Riscos e prioridades (item 28.5)

| Ordem | Risco | Situação | Encaminhamento |
|---|---|---|---|
| 1 | Perda de dados | R1–R3 corrigidos (§5.1); K2 apontado pelo V03; nenhum risco novo encontrado | — |
| 2 | Falha de build | compilação limpa; CI verde até `6d2b72f` (execução 125) | conferir o CI de cada push |
| 3 | Inconsistência geométrica | pisos sobrepostos sem classe (K11, RT-1.3); retas fora do eixo (K12, 1 restante, corrigida no FC); pisos × terreno sem conferência no núcleo (RT-1.2); terreno sem diagnóstico (§6 do Prompt Master) | **agora:** classificador, conferência piso × terreno, diagnóstico do terreno |
| 4 | Regressão não vista | "Verificações do modelo" desligadas no Autoteste (§7.5); validação sem comparação com a anterior | ligar as verificações no Autoteste completo; histórico da validação |
| 5 | Resultado velho apresentado como atual | terraplenagem desatualizada depois de mudar a via, sem aviso | assinatura das entradas gravada com a terraplenagem e conferida no Validar Modelo |
| 6 | Cálculo com dado incoerente | estrutura do pavimento diferente entre o corte (camadas) e a terraplenagem (0,40 m) | estrutura do pavimento na via, usada pelo corte, pela terraplenagem e pelos quantitativos |
| 7 | Acoplamento entre pastas do Core (R15) | referências mútuas (§8.1) | sem reestruturação em bloco (Prompt Master §2); código novo sem nova referência mútua; um ciclo é desfeito quando a mudança já toca aquele código |
| 8 | Demais | F12, F11, K14/K15, revisão da interface (XIII) | depois dos itens acima |
