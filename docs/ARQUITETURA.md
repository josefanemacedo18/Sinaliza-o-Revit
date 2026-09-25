# Arquitetura

```
┌───────────────────────────── SinalizacaoViaria.Revit (net10.0-windows) ─────────────────────────────┐
│ App / RibbonBuilder ── Comandos (IExternalCommand) ── Janelas WPF (prévia ao vivo com o núcleo)     │
│        │                       │                                                                    │
│        │                       ▼                                                                    │
│        │              MarkingService ── PathResolver (linhas → polilinhas)                          │
│        │                 │    │     └── SurfaceSampler (ReferenceIntersector → Toposolid/pisos)     │
│        │                 │    └── StyleService (materiais, tipos de região, estilo de eixo)         │
│        │                 └── MarkingStorage (Extensible Storage: definição JSON por elemento)       │
│        │                      SharedParameters (SV_*)                                               │
│        └── MarkingUpdater (DMU): linhas alteradas → regenera; cópias → novas marcas                 │
└─────────────────────────────────────────────┬──────────────────────────────────────────────────────┘
                                              │ definições (JSON) / geometria 2D
┌─────────────────────────────── SinalizacaoViaria.Core (net10.0) ────────────────────────────────────┐
│ Catalog     catálogo normativo (JSON embutido + arquivo do usuário)                                 │
│ Definitions MarkingDefinition (Linear, Hatch, Symbol, Text, Parking) + MarkingBuilder               │
│ Generators  LinearPattern · Hatch · Symbol · Text · Parking                                         │
│ Automation  RoadSetup (seção transversal) · CrosswalkSetup (faixa + retenção)                       │
│ Geometry    Vec2, Polyline2 (estaqueamento/offset), Polygon2, PolygonOps (Clipper2), PathChainer    │
│ Quantities  QuantityCalculator (áreas por código/cor/material, consumo, CSV)                        │
└─────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

## Fluxo de uma marca

1. A janela monta uma **definição** (`LinearMarkingDefinition`, `HatchMarkingDefinition`...), que
   contém apenas parâmetros: código do catálogo, variante/velocidade, deslocamentos, cores, modo de
   representação e a **referência do caminho** (UniqueIds das linhas ou pontos fixos).
2. `MarkingService.Render` resolve o caminho, chama o `MarkingBuilder` do núcleo e obtém peças 2D
   (polígonos com furos, em metros) agrupadas por cor.
3. Para cada cor é criado/atualizado **um elemento**: `DirectShape` (extrusão fina com material, opcionalmente
   inclinada conforme a superfície) ou `FilledRegion` na vista.
4. A definição completa é gravada em cada elemento (Extensible Storage) e os parâmetros `SV_*` são
   preenchidos.
5. Quando uma linha de referência muda, o `MarkingUpdater` localiza as definições que a usam e repete
   o passo 2–4 dentro da mesma transação do usuário.

## Por que um núcleo separado?

* Todo o cálculo geométrico e normativo é testável fora do Revit (`dotnet test` em qualquer SO).
* As janelas usam o mesmo núcleo para a pré-visualização – o que se vê é o que será criado.
* Operações booleanas e deslocamentos robustos com **Clipper2** (zebrados em contornos côncavos,
  setas, símbolos, legendas com furos).

## Pontos de extensão

* **Novos tipos lineares/zebrados/vagas**: apenas catálogo JSON.
* **Nova forma de símbolo**: `FormaSimbolo` + método em `SymbolBuilder`.
* **Novo tipo de marca**: nova `MarkingDefinition` (registrar em `[JsonDerivedType]`), caso em
  `MarkingBuilder.Build/Describe`, janela e comando.
