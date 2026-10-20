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
- **LIMITE DA EVIDÊNCIA:** um build do Revit; um projeto; obras de arte e pranchas não estão no roteiro do Autoteste. As
  correções de EZ (retas restantes, sinalização das rotatórias, correção dos tipos de piso) ainda não rodaram no Revit.
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
| VI | Seção transversal da via (faixas, ciclofaixas, calçadas, canteiros, larguras variáveis) | VALIDADA (Core) | `RoadSetup*`, centenas de testes |
| VI | Pavimento (asfalto, bloquete, concreto, terra) e camadas no perfil | VALIDADA (Core) | rodadas AD3/LX2 |
| VI | Corredor 3D (pisos no greide, obras hospedadas) | VALIDADA (Core) / Revit não validado | rodadas K/L/VT |
| VII | Terreno (TIN, Toposolid nativo) | VALIDADA (Core) / Revit não validado | `Tin`, `TerrainMesh`, `Grading` |
| VII | Terraplenagem: corte/aterro, taludes, bermas, volumes | VALIDADA (Core); desde EV6 também o método das seções (Core, sem interface no Revit), conferido com a malha | `Grading.Volumes`, `SectionVolumes` (§6.7) |
| VII | Interseções (T, cruz, oblíquas, tipos II–IV), rotatórias, nós em desnível | VALIDADA (Core) | rodadas S2, V2, AB2, AE3 |
| VIII | Dispositivos de drenagem superficial (bocas de lobo, grelhas, PV, sarjetão, canaletas) – geometria | VALIDADA (Core) | `DrainageGenerator`, `SarjetaoGenerator` |
| VIII | Rede de drenagem (tubos, poços ligados, declividades) | AUSENTE | não há modelo de rede |
| VIII | Dimensionamento hidráulico | AUSENTE (não afirmado) | nenhum cálculo de vazão/Manning |
| IX | Loteamento: gleba, quadras, lotes por testada e por linhas, chanfros, uniões, numeração, categorias | VALIDADA (Core) | rodadas LT/LU/LV/LW/LX |
| IX | Regras mínimas configuráveis (frente 5 m e área 125 m² por padrão, editáveis) | VALIDADA (Core) | `LoteamentoDefinition.Minimos`; **sem aprovação legal automática** |
| IX | Quadros de áreas, de quadras e de lotes; memorial `.docx` | VALIDADA (Core) | `LotReport`, `LotMemorial`, `DocxWriter` |
| X | Verificações no Revit (pintura × pisos, continuidade, terreno × via) | IMPLEMENTADA, NÃO VALIDADA aqui | `AutoTestChecks.cs` |
| X | Validação do modelo com severidade, confiança e revisão humana | VALIDADA (Core) desde EV4/EV5; o comando rodou no Revit 2027 (relatório do usuário, 2026-10-19) | `ModelValidator` V01–V11, `CmdValidarModelo`, `docs/evidencias` |
| XI | Análise de capacidade, atraso e nível de serviço; Webster | VALIDADA (Core), não calibrada | `TrafficAnalysis`, testes S3/T8/AA2 |
| XI | Microssimulação (IDM, brechas, semáforos, pedestres) | VALIDADA (Core: invariantes como sem colisão e fila coerente); **não calibrada com dados de campo** | testes V4/W5/AA2 |
| XI | Animação | IMPLEMENTADA (é reprodução dos quadros simulados, não substitui a simulação) | `SimFrame`, `TrafficMap` |
| XII | Cenários de tráfego, comparação e teste de soluções | VALIDADA (Core) | `TrafficScenario`, `TrafficSolutions` |
| XII | Cenários de projeto (geometria) e otimização geral | AUSENTE / PARCIAL (greide compensado, tempos semafóricos) | — |
| XIII | Janelas WPF com prévia ao vivo (Core) | IMPLEMENTADA, NÃO VALIDADA aqui | `UI/*.xaml` |
| XIV | Transações, rollback, `FailuresPreprocessor` | IMPLEMENTADA, NÃO VALIDADA aqui | §3 |
| XV | Autoteste dentro do Revit (relatório) | VALIDADA no Revit 2027: modo rápido (DLL 2.0.0 e 2.1.0) e **completo** duas vezes (2026-10-20, 178 etapas; o 2º sem erro), com o resumo por elemento, a conferência das sobreposições, os pares persistentes por tipo e a medida das retas fora do eixo funcionando. Desde EZ: cotas e espessura do tipo nos exemplos de sobreposição e aviso de tipos de piso com estrutura diferente do nome (ainda não rodado no Revit) | `AutoTest*.cs`, `WarningSummary`, `docs/evidencias` |
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
- **§6.8 Drenagem.** Só geometria e encaixe (rebaixo de sarjeta, caixa, grelha, tubo de ligação), com as referências citadas no código (DNIT 030/2004-ES, NBR 10160). **Nenhum dimensionamento hidráulico** é feito ou afirmado.
- **§6.11 Tráfego.**
  - A análise usa formulações do tipo HCM e Webster.
  - A microssimulação usa IDM (parâmetros a, b e T por veículo) com zonas de conflito.
  - Os testes verificam **coerência** (sem colisões, filas com muita demanda), não a **precisão** contra campo.
  - Os resultados devem ser lidos como estimativa de estudo.
