# ARCHITECTURE – Arquitetura do SinalizaBIM

Este documento descreve o código como ele é hoje e substitui o antigo `docs/ARQUITETURA.md`. Os riscos e o estado de
cada módulo estão em [AUDIT.md](AUDIT.md); as decisões e seus motivos, em [DECISIONS.md](DECISIONS.md).

## 1. Camadas

```
┌──────────────────────── SinalizaBIM.dll (net10.0-windows, WPF) – carregada pelo Revit 2027 ────────────────────────┐
│  SinalizacaoViaria.Revit                                                                                           │
│   App / RibbonBuilder ─ Commands (IExternalCommand, 93 Cmd*) ─ UI (janelas WPF com prévia do Core)                 │
│   Infrastructure: MarkingService (render) · MarkingStorage (Extensible Storage) · PathResolver · SurfaceSampler    │
│                   StyleService · SharedParameters · TerrainService · LotService · IntersectionService · Log …      │
│   Updater: MarkingUpdater (DMU) · CopyHandler                                                                      │
│                                                                                                                    │
│  SinalizacaoViaria.Core (compilado dentro da mesma DLL; também é uma biblioteca net10.0 testável sem o Revit)      │
│   Definitions  MarkingDefinition (≈ 70 tipos JSON polimórficos) · MarkingBuilder                                   │
│   Generators   sinalização, vias, interseções, rotatórias, obras, drenagem, detalhes (DetailGenerators1–5)        │
│   Automation   RoadSetup (seção), Grading/Tin (terraplenagem), RoadProfileDesigner, AutoSignage, regras            │
│   Geometry     Vec2 · Polyline2 · Polygon2 · PolygonOps (Clipper2) · Tracado · RoadGrade · Solid3D · FloorTiling   │
│   Parcelamento LotDivider · DivisaoPorLinhas · LotDrawing · LotReport · LotMemorial · Masterplan                   │
│   Traffic      TrafficNetwork · TrafficAnalysis · TrafficSimulation · cenários · soluções · diagnóstico           │
│   Quantities / Reports (DocxWriter, Memorial) / Geo (TM local, SIRGAS) / Catalog (MBST, JSON embutido)            │
│   Model        ModelGraph (dependências) · ModelValidator (V01–V11, severidade e confiança)                       │
└────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Regra de dependência: **o Core não referencia o Revit.** Tudo o que pode ser calculado sem o Revit (geometria, regras,
desenho 2D, tráfego, relatórios) fica no Core e é testado com `dotnet test` em qualquer SO. A camada Revit só traduz a
geometria em elementos e a lê de volta.

## 2. Fluxo de uma marca (definição → elementos)

1. A janela (WPF) monta uma `MarkingDefinition`, que guarda só parâmetros: código do catálogo, dimensões, saída e
   `PathReference` (UniqueIds de linhas e/ou pontos fixos, com cache do traçado).
2. `MarkingService.Render`:
   1. resolve o caminho (`PathResolver`);
   2. lê superfícies quando há projeção (`SurfaceSampler`);
   3. chama o Core (`MarkingBuilder`/geradores), que devolve peças 2D/3D em metros;
   4. cria ou atualiza `DirectShape`, `Floor`, `FilledRegion`, textos e linhas de detalhe.
3. `MarkingStorage.Write` grava o JSON da definição em **cada** elemento (esquema `7C1E9A52-…`). Definições sem desenho
   vão para um `DataStorage`. Os parâmetros compartilhados `SV_*` são preenchidos para tabelas e filtros.
4. `MarkingUpdater` (DMU) recebe as mudanças das linhas de referência e dos elementos e regenera as marcas afetadas
   **dentro da transação do usuário** (não abre transações). `RenderDependents` refaz os detalhes que apontam para a
   marca (`IAnnotationDefinition.TargetId`) e os de projeto inteiro (`IProjectWideAnnotation`).

## 3. Modelo de dados e identidade

- **Identidade:** `MarkingDefinition.Id` (GUID "N"), compartilhado por todos os elementos de uma marca. `GroupId` agrupa
  conjuntos (por exemplo, a via inteira gerada por Sinalizar Via). `CloneWithNewId` cria cópias independentes.
- **Versão:** `MarkingDefinition.Version` no JSON e `SchemaVersion` na entidade. `CurrentVersion = 1`.
  `DefinitionVersioning` (Core) decide o que fazer com cada versão lida; ver DECISIONS D-003.
- **Referências entre definições** (arestas do grafo de dependências, `ModelGraph`):

| Propriedade | De → para | Significado |
|---|---|---|
| `IAnnotationDefinition.TargetId` | detalhe → marca | detalhe da placa, chamada, cota, perfil, detalhe dos lotes |
| `SectionProfileDefinition.SectionId` | perfil → cota de seção | linha de corte |
| `IntersectionDefinition.RoadIds`, `MainRoadId`, `LegSettings[].RoadId` | interseção → pavimentos | vias que se cruzam |
| `RoundaboutDefinition.Legs[].RoadId`, `CulDeSacDefinition.RoadId` | rotatória/balão → pavimento | ramo ligado |
| `IntersectionDefinition.ChildIds`, `RoundaboutDefinition.ChildIds` | nó → marcas geradas | sinalização filha (regenerada pelo nó) |
| `QuadraDefinition.LoteamentoId` e quadros/detalhes do loteamento | quadra/quadro → loteamento | parcelamento |
| `ExclusionZone.SourceId` | marca recortada → marca que recorta | limpeza dos recortes |
| `PathReference.ElementIds`, `Output.SurfaceIds`, `LimiteElementos`, `GlebaElementos` | definição → elemento do Revit | associatividade (com cache do traçado) |

## 4. Geometria e tolerâncias

- Unidades internas do Core: **metros**. A conversão para pés acontece só na camada Revit (`UnitConv`).
- Booleanas e offsets de polígonos: Clipper2 (inteiros com escala fixa, robusto para contornos côncavos e com furos).
- Tolerâncias usadas hoje (deliberadamente locais, cada uma justificada no ponto de uso):

| Constante | Valor | Uso |
|---|---|---|
| `Vec2.Eps` | 1e-9 | comparações numéricas puras |
| `PolygonChecks.DefaultTolerance` | 1e-6 m | autointerseção e vértices repetidos (validador) |
| `FloorTiling.Coplanar` / `SeamTol` | 0,5 mm / 1 mm | planos e costuras dos pisos |
| `BoundaryFit.DefaultTolerance` | 5 mm | ajuste de arcos nos contornos de piso |
| `TrimTools.Gap` | 3 cm | folga nos recortes manuais |
| `Tracado` | 1e-4 m | pontos coincidentes do eixo |

## 5. Revit: regras seguidas

- **Threading:** a API do Revit é chamada só na thread principal, dentro do comando ou do DMU. O tráfego calcula com
  `Task.Run` só código do Core, a partir de uma janela modal; os resultados voltam ao Dispatcher antes de tocar no
  documento.
- **Transações:** cada comando abre a própria transação ou `TransactionGroup`, com um único Desfazer, e faz rollback
  em caso de erro. O DMU nunca abre transação.
- **Falhas:** `TerrainFailures` reverte quando há erro e registra no log os avisos que suprime.
- **Elementos:** sempre relidos por `UniqueId`, com `doc.GetElement(...) is {}` antes de usar. Elementos ausentes caem no
  cache do traçado (`PathReference.Cache`).

## 6. Testes

Ver [TESTS.md](TESTS.md). Testes xUnit no Core (`tests/SinalizacaoViaria.Core.Tests`). O comportamento dentro do Revit é
coberto pelo **Autoteste** (`CmdAutoteste`), que roda só dentro do Revit e gera um relatório.

## 7. Distribuição

`dotnet build src/SinalizacaoViaria.Revit -c Release` produz `Instalar/Revit2027/SinalizaBIM.dll` +
`SinalizaBIM.addin`. São dois arquivos, sem dependências soltas; o runtime .NET 10 vem com o Revit 2027. O CI
(`.github/workflows/build.yml`, Windows) testa, compila e publica esse pacote como artefato `SinalizaBIM-Revit2027`.

### Procedimento de versão (release)

1. Atualizar `<Version>` em `Directory.Build.props` (cada entrega de fase sobe o número do meio: 2.2.0, 2.3.0…). A versão
   aparece em **Normas / Sobre** e no assembly; a versão informativa leva também o commit (`2.2.0+<sha>`).
2. `dotnet test tests/SinalizacaoViaria.Core.Tests -c Release`: tudo verde. Registrar o resultado em TESTS.md.
3. Entrada no CHANGELOG.md e commit do código (árvore limpa).
4. `tools/release.sh <fase> "<resultado da suíte>"`: compila (0 avisos), copia a DLL e o `.addin` para
   `Releases/FaseNN` sem sobrescrever entregas anteriores e grava o `RELEASE.md` (commit, SHA-256, testes, o que não foi
   verificado). Ver `Releases/README.md`.
5. Commit de `Releases/` e de `Instalar/Revit2027` e push. O CI tem de ficar verde e publicar o artefato.
6. **Validação no Revit 2027** (manual, fora do CI), numa **cópia** de projeto ou num arquivo vazio: carregar sem erro, rodar
   **Autoteste → Rápido** e **Validar Modelo**, e guardar os dois relatórios com a versão. Sem essa etapa a versão continua
   "implementada, mas não validada no Revit" (AUDIT §5).
