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

```bash
# Desempenho do núcleo (Etapa 13): projetos de 4, 16 e 64 vias e o piso tátil longo, sem o Revit
dotnet run -c Release --project tools/Desempenho > docs/evidencias/AAAA-MM-DD-desempenho-nucleo.txt
```

A saída de 2026-10-27 está em `docs/evidencias/2026-10-27-desempenho-nucleo.txt`. O programa não entra no CI nem no plugin.

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
| 2026-10-22 | `ec398bc` (Fase VI, 2.5.0) | 2 914 | 2 914 aprovados, 0 falhas | 13 min 5 s (Linux) | +16 `Fase06ComponentesTests`, +4 `Fase06RegeneracaoTests`; entrega `Releases/Fase06` |
| 2026-10-22 | `b87e668` (Fase VII, 2.6.0) | 2 921 | 2 921 aprovados, 0 falhas | 24 min 24 s (Linux, com compilações em paralelo) | +7 `Fase07InterseccoesTests`; entrega `Releases/Fase07` |
| 2026-10-22 | `9ed7e12` (Fase VIII, 2.7.0) | 2 949 | 2 949 aprovados, 0 falhas | 26 min 7 s (Linux) | +14 `Fase08DrenagemTests`, +14 `Fase08HidraulicaTests`; entrega `Releases/Fase08` |
| 2026-10-26 | `43ab058` (Etapa 1, 2.8.0) | 2 966 | 2 966 aprovados, 0 falhas | 15 min 17 s (Linux, SDK 10.0.401 pelo Nix) | +9 `Fase10CompatibilizacaoTests`, +8 `Fase10PisoTerrenoTests`; entrega `Releases/Etapa01` |
| 2026-10-26 | `23c92c8` (Etapas 2 e 3, 2.9.0) | 2 979 | 2 979 aprovados, 0 falhas | 14 min 38 s (Linux, SDK 10.0.401 pelo Nix) | +5 `Etapa02DiagnosticoTests`, +8 `Etapa03TerrenoTests`; entrega `Releases/Etapa03` |
| 2026-10-26 | `ee75f3f` (Etapa 4, 2.10.0) | 2 986 | 2 986 aprovados, 0 falhas | 15 min 54 s (Linux, SDK 10.0.401 pelo Nix) | +7 `Etapa04ViasTests`; entrega `Releases/Etapa04` |
| 2026-10-26 | `5dc91f7` (Etapa 5, 2.11.0) | 3 000 | 3 000 aprovados, 0 falhas | 13 min 36 s (Linux, SDK 10.0.401 pelo Nix) | +14 `Etapa05PavimentoTests`; entrega `Releases/Etapa05` |
| 2026-10-26 | `ab92d46` (Etapa 6, 2.12.0) | 3 011 | 3 011 aprovados, 0 falhas | 14 min 20 s (Linux, SDK 10.0.401 pelo Nix) | +11 `Etapa06DrenagemTests`; entrega `Releases/Etapa06` |
| 2026-10-26 | `e3a27c4` (Etapa 7, 2.13.0) | 3 016 | 3 016 aprovados, 0 falhas | 15 min 5 s (Linux, SDK 10.0.401 pelo Nix) | +5 `Etapa07UrbanismoTests`; entrega `Releases/Etapa07` |
| 2026-10-26 | `072b683` (Etapa 8, 2.14.0) | 3 022 | 3 022 aprovados, 0 falhas | 13 min 14 s (Linux, SDK 10.0.401 pelo Nix) | +6 `Etapa08IntegracaoTests`; entrega `Releases/Etapa08` |
| 2026-10-27 | `d074465` (Etapa 9, 2.15.0) | 3 036 | 3 036 aprovados, 0 falhas | 14 min 35 s (Linux, SDK 10.0.401 pelo Nix) | +14 `Etapa09InterfaceTests` (faixa, ícones, dicas, disponibilidade, tokens, contraste, chaves de estilo, Diagnóstico do Terreno, manual); entrega `Releases/Etapa09` |
| 2026-10-27 | `1a7f2b5` (Etapa 10, 2.16.0) | 3 048 | 3 048 aprovados, 0 falhas | 14 min 47 s (Linux, SDK 10.0.401 pelo Nix) | +12 `Etapa10CentralTests` (indicadores e situação da Central, painel contextual, seleção múltipla, fontes do painel e da Central); entrega `Releases/Etapa10` |
| 2026-10-27 | `70d43af` (Etapa 11, 2.17.0) | 3 075 | 3 075 aprovados, 0 falhas | 15 min 5 s (Linux, SDK 10.0.401 pelo Nix) | +27 `Etapa11TelasTests` (seções da via, gerenciador de pavimento, rede de drenagem por parte, terreno, nós, parcelamento, estaca da via, herança do pavimento refeito, fontes das janelas e dos comandos); entrega `Releases/Etapa11` |
| 2026-10-27 | `9102a7b` (Etapa 12, 2.18.0) | 3 123 | 3 123 aprovados, 0 falhas | 14 min 50 s (Linux, SDK 10.0.401 pelo Nix) | +48: 19 `Etapa12QualidadeTests` (estados diante da anterior, chaves repetidas, ordem, filtros, contagens, CSV, os seis relatórios reais do Autoteste, fontes), 18 `Etapa12QuantitativosTests` (unidade do consumo e somas por unidade, origem e método, por via, quadras e lotes, origem dos valores, exportações, fontes), 11 `Etapa12ConfiguracoesTests` (registro e cobertura de toda configuração, impacto, validação, projeto e elementos, perfis, persistência, cópia, tamanho das janelas); entrega `Releases/Etapa12` |
| 2026-10-27 | `fdb39c2` (Etapa 13, 2.19.0) | 3 136 | 3 136 aprovados, 0 falhas | 15 min 45 s (Linux, SDK 10.0.401 pelo Nix) | +13: 3 `Etapa13DesempenhoTests` (índice da terraplenagem igual à busca em todos, ligação, volumes por via), 3 `Etapa13TrafegoTests` (mesma semente, mesmos números e relatório; seção 9; assinatura), 5 `Etapa13ProgressoTests` (fração, contagem, limitador, cancelamento só no ponto seguro, ligação nos comandos e na janela), 2 `Etapa13IntegradoTests` (fluxo no núcleo e ligação no Autoteste); entrega `Releases/Etapa13` |
| 2026-10-27 | `a62975c` (Etapa 15, 2.20.0) | 3 145 | 3 145 aprovados, 0 falhas | 15 min 35 s (Linux, SDK 10.0.401 pelo Nix) | +9: 5 `Etapa15GeracaoTests` (trecho curto com uma travessia e um par de rampas por aproximação, trecho longo com as duas, semáforo vence, modo manual, rotatória, ligação), 4 `Etapa15TerrenoTests` (pé da rampa contra o erro da triangulação, plataforma da rampa vence a da via e sai da pista, sem rampas nada muda, ligação na terraplenagem e no Autoteste); entrega `Releases/Etapa15` |
| 2026-10-27 | `0ef734a` (Etapa 16, 2.21.0) | 3 163 | 3 163 aprovados, 0 falhas | 15 min 12 s (Linux, SDK 10.0.401 pelo Nix) | +18: `Etapa16EsquinasTests` (chanfro pelos catetos em 60°, 90° e 120°, por esquina e pela face, projetos antigos, calçada nos chanfros; faixa tátil na quina viva, paralela ao chanfro, corte da quina com a folga e na quina estreita, opção pela curva, junto ao acesso; rampas no eixo das travessias, extensão em chanfro, ajuste por esquina e recuo reduzido; ligação no plugin); entrega `Releases/Etapa16` |
| 2026-10-27 | `3dc933a` (Etapa 17, 2.22.0) | 3 183 | 3 183 aprovados, 0 falhas | 14 min 3 s (Linux, SDK 10.0.401 pelo Nix) | +20: `Etapa17PisosMateriaisTests` (partes da rampa como pisos com e sem cantos e com desnível, rebaixamento total, rampa no relevo, recorte contendo as abas; ativo de aparência por material, pinturas sem ativo, exclusões, título em português; configurações; ligação no plugin); entrega `Releases/Etapa17` |

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
| `Etapa15GeracaoTests` | Etapa 15 (item 18): trecho curto entre duas esquinas com uma travessia e um par de rampas (antes duas e quatro), trecho longo com as duas, escolha pelo peso vista dos dois lados, modo manual, rotatória vizinha e a ligação no plugin |
| `Etapa15TerrenoTests` | Etapa 15 (item 13): o pé da rampa sem folga cabia no erro da triangulação do Toposolid e com folga não; a plataforma da rampa vence a da via na terraplenagem e sai da pista; sem rampas nada muda; a ligação na terraplenagem e no Autoteste |
| `Etapa16EsquinasTests` | Etapa 16 (itens 19, 20 e 12): chanfro de 1,50 m em cada lado sobre os limites em esquinas de 60°, 90° e 120°, medida por esquina e pela face, quadras e loteamentos antigos abrem iguais, calçada da interseção nos chanfros; faixa tátil da esquina na quina viva, paralela ao chanfro, cortando a quina com a folga e na quina estreita, a opção pela curva do meio-fio, junto à faixa de acesso; rampas no eixo das travessias, extensão em chanfro (70° e 90°), ajuste por esquina e recuo reduzido; ligação nas janelas e comandos |
| `Etapa17PisosMateriaisTests` | Etapa 17 (itens 2 e 1): partes da rampa como pisos (um por corpo, topo plano, fundo 5 cm abaixo, espessura do tipo, inclinação, sem sobreposição e dentro do recorte), rebaixamento total, rampa no relevo; escolha do ativo de aparência por material, pinturas sem ativo, exclusões, título em português; configurações registradas; ligação no plugin |
| `Etapa13DesempenhoTests` | Etapa 13: o índice da terraplenagem (`RingIndex`) dá exatamente o ponto da busca em todas as plataformas (4 000 pontos, perto e longe, com furos), a ligação nos contornos dos taludes e os volumes por via somando o total |
| `Etapa13TrafegoTests` | Etapa 13 (F11): a mesma semente dá os mesmos números e o mesmo relatório (menos a data); a seção 9 com sementes, passo, aquecimento e medição; a assinatura muda com os parâmetros e com a rede |
| `Etapa13ProgressoTests` | Etapa 13: fração só com total conhecido, contagem, limitador com relógio injetado, cancelamento só no ponto seguro, nenhum ponto de cancelamento depois da gravação em Atualizar Todas, Quantitativos e a janela (sem `Task.Run`, Revit desabilitado e devolvido) |
| `Etapa13IntegradoTests` | Etapa 13: a sequência do fluxo integrado no núcleo (registro com vias e nó, grelha lida na via certa e acusada como nova, leitura sem mudar a assinatura, edição do talude → V15 e impacto, terraplenagem refeita limpa) e a ligação da etapa no Autoteste (opção, ordem, etapas juntas, motores reais) |
| `Etapa04ViasTests` | Etapa 4: limites dos avisos num registro com a origem dita (sem fonte = "sem norma citada"; espaço que o gerador precisa); avisos de minirrotatória, pista giratória, compacta com duas faixas e canalização; estaqueamento (padrão igual ao de sempre, estaca inicial, intervalo, equação com lacuna e equação que repete estacas, leitura e escrita das equações, problemas antes de aplicar, CSV do eixo, texto do V12 e ida e volta JSON) |
| `Etapa03TerrenoTests` | Etapa 3: diagnóstico da superfície (`TerrainDiagnostics`) – superfície boa sem achados, sem superfície ou pontos numa reta, o mesmo ponto com cotas diferentes (5 mm não conta), coordenada não numérica, triângulo sem área, declividade só com limite informado, consultas fora da superfície; V14 no validador (via que molda o terreno fora dele, declividade com limite, "não verificado" sem terreno ou sem limite) |
| `Etapa02DiagnosticoTests` | Etapa 2: regras V01–V14 com módulo e ação (V04 por tipo de dependência), "não verificado" só quando pedido (V12 sem limites, V13 sem Projeto ou sem chuva, V06 sem o documento), histórico da validação (chaves, JSON, novo × persistente × resolvido pela chave e não pela mensagem), relatório com versão, módulos, ação, comparação e "Não verificado", registro de tolerâncias coerente |
| `Fase10CompatibilizacaoTests` | Etapa 1 (Fase X): pisos sobrepostos classificados (`OverlapClassifier`) em camada intencional, interseção legítima, duplicação acidental, conflito real e inconclusivo, com motivo e ação; duas vias cruzadas sem nó (geradores reais) e as mesmas com o nó, sem nenhum par com o contorno ajustado como no Revit; nós entre vias lidos das definições |
| `Fase10PisoTerrenoTests` | Etapa 1 (Fase X, RT-1.2): pisos × terreno projetado (`FloorTerrainCheck`) – enterrado, terreno dentro do piso, suspenso e degrau na borda; cinco cenas de topografia difícil do AL3 sem achados, com pontos de fato conferidos; contra o terreno original, as mesmas cenas acusam corte e aterro |
| `Fase07InterseccoesTests` | Fase VII: rotatória com by-pass do 4º Autoteste sem pisos sobrepostos (cru e contorno do Revit), meio-fio externo sem tira estreita, ilhas fora do meio-fio (K16); seções de cada via só na superfície dela, conferidas com a malha, nó fora, tabela por estaca, CSV e planilha (F7b) |
| `Fase08DrenagemTests` | Fase VIII: caixas da rede no fundo do tubo de saída desenhado, tubo entre as paredes nas cotas de fundo, avisos (dispositivo apagado, caixas encostadas, contrafluxo), medição (extensão, declividade, recobrimento), conferência só geométrica e com critérios, ciclo com os dispositivos, persistência e grafo, V13 com as marcas envolvidas, Projeto de Drenagem em JSON com as predefinições |
| `Fase08HidraulicaTests` | Fase VIII: curvas IDF contra a tabela B.3 do Manual SUDERHSA, exemplos 6.1 (sarjeta), 6.2 (bocas de lobo) e 6.3 (galeria), planilha do memorial modelo reproduzida (i, Q, vazão máxima, velocidade, profundidades, C de Horner, trecho acima da capacidade), junções (tempo de concentração e áreas), vazão externa, dimensionamento aplicado, captações, PV de queda, espaçamento e geratriz superior, V13 com a vazão e o Memorial de Drenagem em .docx |
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
2. Verificar que as guias **SinalizaBIM** (11 painéis) e **SinalizaBIM Sinalização** (3 painéis) aparecem, sem erro ao
   iniciar, e que sem projeto aberto só o painel **Sistema** fica habilitado. O log fica em `%AppData%\SinalizaBIM\log.txt`.
3. Num arquivo de teste (nunca no projeto real), rodar **Autoteste → Rápido** e guardar o relatório **e** o arquivo de
   dados `autoteste-…-dados.jsonl` da mesma pasta (todos os avisos, pares de pisos sobrepostos, retas fora do eixo,
   contornos dos pisos e definições – o bastante para reproduzir no núcleo sem repetir o teste).
4. Rodar **Análise → Validar Modelo** num projeto existente: não altera nada (comando somente leitura) e grava
   `validacao-*.txt`.
5. Desde a 2.19: **Projeto → Atualizar Todas** numa cópia com muitas marcas – cancelar no meio (o modelo fica igual, nada
   novo no Desfazer) e depois deixar terminar; abrir **Quantitativos** e cancelar. No Autoteste, marcar **Fluxo integrado**
   (ou o Completo) e conferir as cinco etapas do bloco sem erro.
6. Desde a 2.20: num projeto com quadras curtas, atualizar as interseções e conferir uma travessia por trecho; rodar a
   Terraplenagem com rampas e interseções em relevo e olhar as rampas no 3D (o terreno não pode aparecer por cima); no
   Autoteste Completo, conferir "Terreno × rampas" e "Terreno × nós" nas verificações de cada via.
7. Desde a 2.21: criar um loteamento numa cópia e conferir os chanfros de 1,50 m em cada lado nas quinas e a calçada da
   interseção dentro deles; ligar o piso tátil numa via com interseção e conferir a faixa da esquina paralela ao lote, com
   alerta em cada quina; numa interseção com extensões, informar o recuo do chanfro e conferir o meio-fio reto no 3D.
8. Desde a 2.22: atualizar uma interseção com rampas e conferir, no 3D e num corte, cada rampa como Piso inclinado de fundo
   plano, sem degrau com a calçada; abrir uma tabela de pisos e achar as rampas; trocar a vista para **Realista** e conferir
   asfalto, concreto, grama e bloquete com textura (os títulos escolhidos ficam no `log.txt`).
