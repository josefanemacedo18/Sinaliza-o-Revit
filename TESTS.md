# TESTS – Testes, linha de base e como validar

## Como rodar

```bash
# Núcleo (qualquer SO, ~15 min na máquina de CI; os testes de terreno são os mais lentos)
dotnet test tests/SinalizacaoViaria.Core.Tests -c Release

# Só uma rodada/classe
dotnet test tests/SinalizacaoViaria.Core.Tests -c Release --filter "FullyQualifiedName~EVModelIntegrityTests"

# Plugin (DLL única para o Revit 2027; no Linux precisa do EnableWindowsTargeting)
dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true
```

Não compile enquanto a suíte roda com `--no-build`: a compilação troca as DLLs em uso.

## Linha de base

| Data | Commit | Testes | Resultado | Duração | Observação |
|---|---|---|---|---|---|
| 2026-10-16 | `38c8c10` | 2 704 | 2 704 aprovados | — | CI Windows (run 99) verde |
| 2026-10-19 | `38c8c10` | 2 704 | 2 704 aprovados, 0 falhas | 14 min 47 s (Linux) | linha de base da auditoria (`AUDIT.md`) |
| 2026-10-19 | EV3/EV4 | 2 730 | 2 730 aprovados, 0 falhas | 13 min 7 s (Linux) | +26 testes `EVModelIntegrityTests`; 0 avisos em toda a compilação |
| 2026-10-19 | EV5 | 2 754 | 2 754 aprovados, 0 falhas | 13 min 38 s (Linux) | +14 `EVGeometryEdgeTests`, +10 `EVAlignmentElementsTests` |
| 2026-10-19 | EV5 (V09–V11) | 2 756 | classe `EVModelIntegrityTests` 28/28 | — | só o validador mudou depois da suíte completa |
| 2026-10-19 | EV6 | 2 765 | 2 765 aprovados, 0 falhas | 14 min 28 s (Linux) | +9 `EVSectionVolumesTests`, +2 do validador (V09–V11) |
| 2026-10-19 | EW1/EW2 | 2 771 | 2 771 aprovados, 0 falhas | 21 min 50 s (Linux) | +1 V06 da gleba, +5 `EWRevitWarningsTests` |
| 2026-10-19 | EW3 | 2 775 | classes de alinhamento 19/19 | — | +4 `EWAxisElementsTests` (código novo e isolado depois da suíte completa) |
| 2026-10-20 | EX | 2 785 | 2 785 aprovados, 0 falhas | 14 min 49 s (Linux) | +10 `EXRevitWarningsTests` |
| 2026-10-20 | EY | 2 800 | 2 800 aprovados, 0 falhas | 14 min 34 s (Linux) | +15 `EYRevitFullRunTests` |

Compilação na linha de base:
- Core e plugin: 0 avisos.
- Testes: 2 avisos (xUnit2029 e CS0028), corrigidos na EV3.

### Mais lentos (linha de base)

| Classe | Tempo somado |
|---|---|
| `AL3TopographyTests` | ~674 s |
| `VTTerraplenagemTests` | ~158 s |
| `VTRelevoTests` | ~114 s |
| `AA1SolutionReproTests` | ~104 s |
| `AB2IntersectionTests` | ~102 s |

O teste isolado mais lento é `AA1SolutionReproTests.Recomendadas_SaoAsImplantadasENaoPioramARede`, com 66 s.

## Organização

Os arquivos de teste têm o prefixo da rodada em que a funcionalidade entrou (`AB2…`, `LX…`, `VT…`). Cada rodada traz
testes de reprodução do defeito relatado e testes de regressão. Classes transversais:

| Classe | Cobre |
|---|---|
| `EVGeometryEdgeTests` | polilinha vazia/1 ponto/pontos repetidos/fechada, estacas fora do trecho, offset em 180° e ângulo agudo, projeção sem segmentos; traçado com PIs coincidentes, colineares, deflexão 179–180°, raio que não cabe, espiral maior que o arco |
| `EVAlignmentElementsTests` | elementos do traçado (TS/SC/CS/ST, curva circular calculada à mão, espirais, reduzida, sem curva) e do greide (convexa ±2 %, côncava com piso K, vão curto, extremo fora da curva, consistência com `Z(s)`), CSV e estacas |
| `EVSectionVolumesTests` | volumes por seções: corte/aterro constantes, terreno linear (exato) e quadrático (erro h²/12 esperado), última seção no fim, eixo em curva, conferência com a malha de `Grading.Volumes`, CSV |
| `EWAxisElementsTests` | elementos do eixo das vias comuns: canto arredondado (R, Δ, T, estacas conferidos à mão), canto vivo, curva reversa, reta, CSV |
| `EYRevitFullRunTests` | perfis das 168 placas aceitáveis pelo Revit (pictograma que se toca, peça menor que a tolerância, furo encostado), retas fora do eixo pelo critério do Revit nas cenas de cruzamento, T, esconso, rotatórias e cul-de-sac, alinhamento a 45° e fora da janela, nenhum piso sobreposto em todos os modelos de via, ciclofaixas e canteiros, faixa de pedestres recortando as vagas, notas do Autoteste |
| `EXRevitWarningsTests` | alinhamento de anéis de pontos (lote com resíduo em coordenadas grandes, lote inclinado de projeto intacto), lotes reais do cruzamento sem deformação, Marca dos lotes com um, dois e homônimos loteamentos (cenário do Autoteste repetido sem marca repetida), resumo dos avisos do Autoteste por texto e por elemento |
| `EWRevitWarningsTests` | retas quase alinhadas aos eixos (resíduo × inclinação de projeto, quina, arcos), reprodução no cruzamento local × local (antes > 0, depois 0, área igual), `BoundarySegment.ToString` numa reta |
| `EVModelIntegrityTests` | leitura versionada (atual, migração, mais nova, ilegível), ida e volta JSON de **todos** os tipos de definição, grafo de dependências (ordem, ciclos, quebradas, recorte fraco), validador do modelo (V01–V11) e contornos degenerados (gravata, espinho, toque em vértice, colineares, NaN, tolerância em metros) |

## O que os testes NÃO cobrem

- Nada que dependa da API do Revit: criação de elementos, transações, DMU, Toposolid real, desempenho em modelos grandes.
  Isso é coberto pelo **Autoteste** (`CmdAutoteste`), que roda dentro do Revit (janela Sobre/Relatórios) e grava um
  relatório. Os relatórios enviados pelo usuário estão em `docs/evidencias/` (tabela abaixo); **neste ambiente não há
  Revit**, então cada correção do lado do Revit só fica confirmada no relatório seguinte.
- Precisão do tráfego contra dados de campo: os testes verificam coerência (sem colisões, filas, conservação), não a
  calibração.
- Normas: os valores normativos vêm do catálogo e das referências citadas no código. Os testes verificam que os
  valores são aplicados, não que a norma esteja correta.

## Execuções no Revit (evidência)

| Data | Revit | DLL | Execução | Resultado | Arquivo |
|---|---|---|---|---|---|
| 2026-10-19 | 2027.0 (build 27.0.4.412) | 2.0.0.0 (EV1–EV4) | Autoteste **rápido** (8 etapas; 162 não executadas) | 4 OK, 4 avisos (Revit: sobreposição de pisos e retas fora do eixo; loteamento: sobras de testada), 0 erros | `docs/evidencias/2026-10-19-autoteste-rapido.txt` |
| 2026-10-19 | idem | idem | Validar Modelo (4 marcas, 48 elementos) | 1 aviso V06 (linhas da gleba apagadas; recalibrado para informação) | `docs/evidencias/2026-10-19-validacao-modelo.txt` |
| 2026-10-19 | idem | 2.1.0.0 (EW1–EW3) | Autoteste **rápido** (7 etapas; 171 não executadas) | 4 OK, 3 avisos (Revit: 239 sobreposições, 88 Marcas duplicadas, 62 retas fora do eixo; loteamento: sobras de testada), 0 erros | `docs/evidencias/2026-10-19-autoteste-rapido-2.1.0.txt` |
| 2026-10-20 | idem | 2.1.0.0 (EX) | Autoteste **completo** (178 etapas, 1 h 42 min) | 97 OK, 80 avisos (Revit: 3 091 sobreposições, 614 pares persistentes; 1 385 retas fora do eixo), **1 erro** (perfis de placas; corrigido em EY) | `docs/evidencias/2026-10-20-autoteste-completo.txt`, `2026-10-20-log-autoteste-completo.txt` |

## Validação manual no Revit (roteiro mínimo)

1. Copiar `Instalar/Revit2027/*` para `%AppData%\Autodesk\Revit\Addins\2027` e abrir o Revit 2027.
2. Verificar que a guia **SinalizaBIM** aparece, sem erro ao iniciar. O log fica em `%AppData%\SinalizaBIM\log.txt`.
3. Num arquivo de teste (nunca no projeto real), rodar **Autoteste → Rápido** e guardar o relatório.
4. Rodar **Editar → Validar Modelo** num projeto existente: não altera nada (comando somente leitura) e grava
   `validacao-*.txt`.
