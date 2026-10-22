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
| 2026-10-20 | EZ | 2 825 | 2 825 aprovados, 0 falhas | 14 min 46 s (Linux) | +25 `EZRevitFullRun2Tests` |
| 2026-10-20 | FA | 2 843 | 2 843 aprovados, 0 falhas | 20 min 18 s (Linux) | +18 `FARevitFullRun3Tests` |
| 2026-10-20 | FB | 2 856 | 2 856 aprovados, 0 falhas | 20 min 59 s (Linux) | +13 `FBAutotestSpeedTests` |
| 2026-10-21 | FC | 2 869 | 2 869 aprovados, 0 falhas | 14 min 57 s (Linux) | +13 `FCRevitFullRun4Tests` (fixtures do 4º Autoteste em `Fixtures/`) |
| 2026-10-21 | `87cf484` (Fase III, 2.2.0) | 2 876 | 2 876 aprovados, 0 falhas | 12 min 53 s (Linux) | +7 `Fase03ArquiteturaTests`; entrega `Releases/Fase03` |
| 2026-10-22 | `43bf855` (Fase IV, 2.3.0) | 2 887 | 2 887 aprovados, 0 falhas | 13 min 29 s (Linux) | +11 `Fase04GeometriaTests`; entrega `Releases/Fase04` |
| 2026-10-22 | `dd8b1fc` (Fase V, 2.4.0) | 2 894 | 2 894 aprovados, 0 falhas | 13 min 8 s (Linux) | +7 `Fase05AlinhamentoTests`; entrega `Releases/Fase05` |

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
| `Fase06ComponentesTests` | Fase VI: quantitativo por componente da seção (`ComponentQuantities`): extensão do meio-fio na faixa reta, na curva e no anel; vias de todos os modelos, interseções do 4º Autoteste e rotatórias de todos os tipos com toda peça física num componente; área medida repartida na proporção; sinalização e mobiliário fora; componente sem efeito nos pisos do Revit; CSV e planilha |
| `Fase06RegeneracaoTests` | Fase VI: conexões refeitas quando uma via muda (`ModelGraph.ConnectionsToRefresh`), nó que acompanha até 15 m (`IntersectionGenerator.FollowNode`), eixo movido (mesma interseção deslocada, mesmos quantitativos) e seção alterada (perfil e calçada da interseção) |
| `Fase05AlinhamentoTests` | Fase V: limites geométricos informados pelo usuário (`GeometricCheck`): sem limites nada é conferido, raio e tangente do eixo, vértice sem curva, rampas, K e curvas verticais do greide, fonte no CSV como foi escrita, V12 só com limites, configurações antigas sem o campo |
| `Fase04GeometriaTests` | Fase IV: centros exatos das curvas da extensão de calçada e do recorte dela (MER-0 × ORELHA), zebrado por contorno com os recortes no caminho do Revit (FTP-1 × ZPA), ajuste topológico (`BoundaryFit.FitRings`: borda curva comum, junção em T, trecho unificado, trecho aberto com pontas presas, rotatórias da fixture do 4º Autoteste sem lascas) |
| `Fase03ArquiteturaTests` | ordem de regeneração pelo grafo (`ModelGraph.RegenerationSort`): igual à antiga sem dependências no grupo, nó antes das filhas, detalhe de detalhe, ciclo, ids repetidos, recorte fraco; placas personalizadas só na lista do projeto (K1) |
| `FCRevitFullRun4Tests` | reprodução do 4º Autoteste completo com as definições e os contornos gravados no arquivo de dados: rotatórias (8 tipos) sem pintura sobre a sarjeta nem pisos sobrepostos; formas diretas da rotatória oval aceitáveis pelo Revit; T de via reconhecida em pisos com pavimento "Nenhum" (interseção formada, sem pares entre marcas, < 1 cm² depois do ajuste de arcos); cruzamento rodoferroviário só nas faixas de rolamento; as 8 interseções com os pisos da mesma curva no mesmo arco (emulação do contorno que vai ao Revit); `AxisSnap` mantendo o raio do arco vizinho |
| `FBAutotestSpeedTests` | leitura das marcas pelo tipo no começo do JSON (`DefinitionKinds`) igual ao teste de tipo da leitura completa, para todas as definições (JSON compacto e indentado, classes e interfaces); texto inesperado nunca descartado; ids de conjunto, recorte, marca, elemento e via hospedeira no JSON sem escape (filtro de texto de `MarkingStorage.Mentioning`) |
| `EYRevitFullRunTests` | perfis das 168 placas aceitáveis pelo Revit (pictograma que se toca, peça menor que a tolerância, furo encostado), retas fora do eixo pelo critério do Revit nas cenas de cruzamento, T, esconso, rotatórias e cul-de-sac, alinhamento a 45° e fora da janela, nenhum piso sobreposto em todos os modelos de via, ciclofaixas e canteiros, faixa de pedestres recortando as vagas, notas do Autoteste |
| `EXRevitWarningsTests` | alinhamento de anéis de pontos (lote com resíduo em coordenadas grandes, lote inclinado de projeto intacto), lotes reais do cruzamento sem deformação, Marca dos lotes com um, dois e homônimos loteamentos (cenário do Autoteste repetido sem marca repetida), resumo dos avisos do Autoteste por texto e por elemento |
| `EWRevitWarningsTests` | retas quase alinhadas aos eixos (resíduo × inclinação de projeto, quina, arcos), reprodução no cruzamento local × local (antes > 0, depois 0, área igual), `BoundarySegment.ToString` numa reta |
| `EVModelIntegrityTests` | leitura versionada (atual, migração, mais nova, ilegível), ida e volta JSON de **todos** os tipos de definição, grafo de dependências (ordem, ciclos, quebradas, recorte fraco), validador do modelo (V01–V11) e contornos degenerados (gravata, espinho, toque em vértice, colineares, NaN, tolerância em metros) |

## O que os testes NÃO cobrem

- Nada que dependa da API do Revit: criação de elementos, transações, DMU, Toposolid real, desempenho em modelos grandes.
  Isso é coberto pelo **Autoteste** (`CmdAutoteste`), que roda dentro do Revit (janela Sobre/Relatórios) e grava um
  relatório. Os relatórios enviados pelo usuário estão em `docs/evidencias/` (tabela abaixo); **neste ambiente não há
  Revit**, então cada correção do lado do Revit só fica confirmada no relatório seguinte.
- **Por que o Revit não roda aqui (verificado em 2026-10-21):** o ambiente de desenvolvimento é um contêiner Linux x86-64;
  o Revit 2027 é um aplicativo Windows com licença e interface, sem modo de linha de comando. O CI do GitHub
  (`windows-latest`) compila e roda os testes do núcleo no Windows a cada push, mas também não tem Revit. A alternativa
  sem interface seria o Autodesk Platform Services – Design Automation for Revit, que exige conta e credenciais da
  Autodesk que este projeto não tem. Por isso o que depende da API fica assim:
  - o núcleo (geometria, regras, geradores) é testado aqui e no CI;
  - o que o Revit faria com a geometria é emulado nos testes (contorno ajustado em retas e arcos, critério de retas fora do
    eixo, sobreposição de pisos), a partir dos dados gravados pelo Autoteste quando existem (`FCRevitFullRun4Tests`);
  - cada entrega em `Releases/` diz "não executado no Revit 2027" até um Autoteste confirmá-la.
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
| 2026-10-20 | idem | 2.1.0.0 (EY, DLL de 07:59) | Autoteste **completo** (178 etapas, 1 h 41 min) | 106 OK, 72 avisos (Revit: 3 153 sobreposições, 622 pares persistentes, listados por tipo; 85 retas fora do eixo – corrigidas no núcleo em EZ), **0 erros** | `docs/evidencias/2026-10-20-autoteste-completo-2.txt` |
| 2026-10-20 | idem | 2.1.0.0 (EZ, DLL de 13:28) | Autoteste **completo** (178 etapas, 1 h 41 min) | 110 OK, 67 avisos (Revit: 3 135 sobreposições, 590 pares persistentes com cotas; 8 retas fora do eixo), **1 erro** (forma direta da rotatória oval; corrigido em FA) | `docs/evidencias/2026-10-20-autoteste-completo-3.txt`, `2026-10-20-log-autoteste-completo-3.txt` |
| 2026-10-21 | 2027.3 (build 27.3.0.28) | 2.1.0.0 (FB, DLL de 08:57) | Autoteste **completo** (180 etapas, 36 min) | 111 OK, 68 avisos (modelo final: 323 pares sobrepostos, 3,10 m³; 3 retas fora do eixo, 2 delas ruído de 0°), **1 erro** (rotatória oval; corrigido no FC) | `docs/evidencias/2026-10-21-autoteste-completo-4.txt` (arquivo de dados reproduzido em `FCRevitFullRun4Tests`) |

## Validação manual no Revit (roteiro mínimo)

1. Copiar `Instalar/Revit2027/*` para `%AppData%\Autodesk\Revit\Addins\2027` e abrir o Revit 2027.
2. Verificar que a guia **SinalizaBIM** aparece, sem erro ao iniciar. O log fica em `%AppData%\SinalizaBIM\log.txt`.
3. Num arquivo de teste (nunca no projeto real), rodar **Autoteste → Rápido** e guardar o relatório **e** o arquivo de
   dados `autoteste-…-dados.jsonl` da mesma pasta (todos os avisos, pares de pisos sobrepostos, retas fora do eixo,
   contornos dos pisos e definições – o bastante para reproduzir no núcleo sem repetir o teste).
4. Rodar **Editar → Validar Modelo** num projeto existente: não altera nada (comando somente leitura) e grava
   `validacao-*.txt`.
