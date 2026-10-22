# CHANGELOG

Formato: data, etapa, mudanças, testes. O histórico anterior a 2026-10-19 está no `git log`: 118 commits, rodadas A a LX,
cada uma com seus testes.

## 2026-10-22 – Fase IV concluída: ajuste topológico dos contornos e pares restantes do 4º Autoteste (versão 2.3.0)

Fase da missão: IV (motor geométrico e topologia) – concluída. Entrega: `Releases/Fase04` (não executada no Revit 2027).

### Ajuste topológico do contorno dos pisos (K13, D-016)
- Cada piso ajustava sozinho o contorno em retas e arcos (tolerância de 5 mm). Dois pisos vizinhos escolhiam quebras, raios e
  junções diferentes para a mesma borda e ficavam sobrepostos numa lasca de milímetros ao longo dela. O Revit avisa "Pisos
  realçados se sobrepõem" para cada par.
- `BoundaryFit.FitRings` ajusta juntos os pisos físicos de uma marca, em três passos:
  1. Prepara as bordas comuns. O vértice de um piso que cai na aresta do vizinho entra nela (junção em T). Trechos com
     vértices próprios em cada piso, a até 5 mm um do outro, passam a usar os mesmos vértices.
  2. Ajusta cada borda comum uma vez só, como trecho aberto com as pontas fixas (`FitChain`), e os dois pisos usam o mesmo
     resultado. Uma ponta que cai dentro de um arco exato (corte sobre a corda) vai para a circunferência antes do ajuste.
  3. Nos pisos que ficaram fora (bordas só geometricamente iguais), retas e circunferências da mesma borda passam a ser
     uma só e os vértices são refeitos nos encontros.
- Piso cujo contorno montado não passa nas conferências sai da topologia junto com os pares dele, e tudo é refeito. Essas
  conferências são: sem autointerseção, trechos acima da aresta mínima e perto do contorno original nos dois sentidos.
- `AxisSnap.SnapChain` faz o alinhamento aos eixos de um trecho aberto sem mover as pontas.
- No Revit, `MarkingService.PrepareSharedFit` roda o ajuste conjunto antes de criar os pisos de cada marca, e `FittedLoop`
  usa o resultado. A pintura continua ajustada peça a peça.
- Medido nas fixtures do 4º Autoteste (emulação do contorno do Revit no núcleo):
  - interseções: 0 cm³ em todas, inclusive a emenda em curva `fd4594` (era 2,8 L) e as junções com as rampas (eram até
    31 cm³);
  - rotatórias: de 9,6–67 L para 0–39 cm³, exceto a com by-pass (K16).

### Pares restantes do 4º Autoteste (K11)
- **MER-0 × ORELHA (13,95 cm² no contorno do Revit):** a extensão de calçada reta não informava os centros das curvas de
  transição, e cada piso ajustava os arcos sozinho. Agora:
  - `SidewalkGenerator.EarArcCenters` dá os centros das curvas de transição;
  - o recorte que ela faz nas marcas vizinhas leva esses centros (`ExclusionZone.ArcCenters`, `FootprintCutter`);
  - `ApplyExclusions` não perde mais os centros da própria marca.
- **FTP-1 × ZPA na minirrotatória:** no Revit, o zebrado por contorno ia por `MarkingBuilder.BuildHatch`, que ignorava os
  recortes da marca. O zebrado da gota passava sob a faixa de pedestres, embora o núcleo recortasse. `BuildHatchArea`
  aplica os recortes.

### Testes
- `Fase04GeometriaTests` (11) com fixtures do 4º Autoteste:
  - centros das curvas da extensão de calçada;
  - recorte que mantém e acrescenta centros;
  - MER-0 × ORELHA e FTP-1 × ZPA;
  - borda curva comum com o mesmo arco nos dois pisos;
  - junção em T;
  - trecho com vértices próprios unificado;
  - trecho aberto com pontas presas;
  - rotatórias sem lascas.
- `FCRevitFullRun4Tests`: a emulação do contorno do Revit usa o ajuste conjunto. O limite das interseções caiu para
  5 cm³ em todas (a emenda podia ter 3 L).

## 2026-10-21 – Fase III concluída: ordem de regeneração pelo grafo e placas personalizadas por projeto (versão 2.2.0)

Fase da missão: III (arquitetura integrada) – concluída. Entrega: `Releases/Fase03` (não executada no Revit 2027).

### Ordem de regeneração pelo grafo de dependências (D-004)
- `ModelGraph.RegenerationSort`: grupo (marcas, detalhes, anotações do projeto), depois a profundidade de dependência
  dentro do conjunto (referências fortes), depois a ordem de entrada. `MarkingService.DependencyOrder` usa essa ordem na
  criação de marcas, na atualização de um conjunto, nas interseções e nos detalhes dependentes.
- Sem dependências dentro de um grupo, o resultado é o mesmo de antes. Muda só onde a ordem antiga podia gerar algo antes
  do que ele lê: filha de um nó antes do nó; detalhe de um detalhe antes do detalhe. Ciclos não travam (a ordem de entrada
  decide) e os recortes não ordenam.

### Placas personalizadas só no projeto que as tem (K1)
- Com dois projetos abertos na mesma sessão, as placas personalizadas de um apareciam na lista de placas do outro e podiam
  ser usadas lá sem estar guardadas no projeto. O catálogo da sessão continua acumulando as placas, para que a placa de
  qualquer marca seja encontrada pelo código. Mas a janela de placas agora lista só as do catálogo normativo e as do
  projeto aberto (`CustomSigns.ForProject`).
- A placa da marca em edição continua na lista mesmo que tenha vindo de outro projeto.

### Testes
- `Fase03ArquiteturaTests` (7): equivalência com a ordem antiga sem dependências, nó antes das filhas, detalhe de detalhe,
  ciclo, ids repetidos e vazios, recorte que não ordena, filtro das placas por projeto.

## 2026-10-21 – FC: 4º Autoteste completo – rotatória oval, pinturas sobre a sarjeta, T de via reconhecida e arcos das esquinas

Fase da missão: IV/XIV (geometria e regras da API do Revit) – atual; XV (testes e evidência) junto.

### O que o 4º Autoteste completo mostrou (DLL do FB, 2026-10-21 09:35)
- 180 etapas em **36 min** (o 3º levou 1 h 41 min): 111 OK, 68 com aviso, 1 erro. Evidência:
  `docs/evidencias/2026-10-21-autoteste-completo-4.txt`.
- Erro: rotatória oval – `CreateExtrusionGeometry` recusou um contorno.
- Etapa "Modelo final": 323 pares de pisos sobrepostos (3,10 m³) e 3 retas fora do eixo (duas de 0°, ruído da conferência).
  Maiores: FTP-1 × ROTATORIA 108, LBO × ROTATORIA 44 (0,39 m³), ROTATORIA × ROTATORIA 36, INTERSECAO × INTERSECAO 13;
  CALCADA × CALCADA 0,97 m³, CALCADA × GRAMADO 0,59 m³ e (piso comum) × (piso comum) 0,69 m³ no T da via reconhecida em
  pisos; LRE/MCF/CSA × MER-0/SARJETA no cruzamento rodoferroviário.
- Tudo reproduzido no núcleo com as definições e os contornos do arquivo de dados (`FCRevitFullRun4Tests`, fixtures
  `fc-autoteste4-rotatorias.json` e `fc-autoteste4-intersecoes.json`).

### Rotatórias (FC2, FC3)
- Oval: a diferença entre o pavimento e as peças deixava lascas de milímetros junto às bordas curvas, que viravam formas
  diretas com arestas abaixo da tolerância (extrusão recusada). As lascas são limpas (`Heal`) antes.
- Linhas de bordo (LBO) e faixas de pedestres (FTP-1) ficavam sobre a sarjeta de 0,30 m que o perfil das vias leva para a
  rotatória. Agora a LBO fica além da sarjeta, como nas vias; a faixa de pedestres para antes dela e as ilhas sob a faixa
  são recortadas com a sarjeta em volta.
- Pisos da via recortados 1 mm além da zona da rotatória (PAV-ASF × ROTATORIA, diferenças de 0,1–0,5 mm nas bordas).

### Via reconhecida em pisos com pavimento "Nenhum" (FC3)
- A via não registrava a definição do pavimento e não se ligava à outra: sem interseção, calçadas, meios-fios e linhas das
  duas vias se cruzavam (61 pares). A definição agora sempre existe; com "Nenhum" ela fica só como dado (sem piso), e o T é
  formado (DECISIONS D-014). O atualizador apaga esse dado quando a via some.
- O Autoteste desenhava o piso do ramo do T sobre o da via principal (pisos do usuário sobrepostos); o ramo começa no bordo.

### Cruzamento rodoferroviário (FC3)
- LRE, MCF e CSA iam do eixo ao bordo da pista inteira, passando sobre vagas, sarjeta e linhas centrais. Agora cobrem só as
  faixas de rolamento contíguas ao eixo (`RailCrossingSetup.FitTo` lê a seção da via), a 0,20 m do eixo e da faixa seguinte.

### Interseções: pisos da mesma curva no mesmo arco (FC3)
- Causa dos 13 pares INTERSECAO × INTERSECAO (0,6–4,6 L cada): o canto da pista sai do Clipper com flecha de 0,2 % do
  raio (12 mm em R = 6 m, 20 mm em R = 10 m). Os recortes (rampas, limite da zona) caem sobre essas cordas. Com 5 mm de
  tolerância, o ajuste de arcos recusava o centro exato da esquina e cada piso caía num círculo próprio – ~2 mm sobrepostos
  ao longo da curva inteira.
- `BoundaryFit`, com o centro exato do traçado:
  - aceita pontos para dentro da circunferência até a flecha das cordas (0,25 % do raio, máx. 3 cm);
  - toma o raio pelo envelope externo dos vértices, no milímetro quando está a até 0,45 mm dele;
  - leva a junção reta → arco ao encontro da reta com a circunferência (a reta continua reta); com a reta quase tangente
    (encontro longe), à projeção na circunferência;
  - um degrau de poucos mm entre dois arcos da mesma circunferência some;
  - a busca do fim do arco continua depois da bissecção (um corte na corda faz o arco "não caber" e caber de novo);
  - folga de meia corda na conferência do giro;
  - arcos seguidos em volta do mesmo centro ficam com um raio só.
- `AxisSnap`: a ponta de um arco desliza pela reta alinhada até a circunferência dele, em vez de ser empurrada para o lado;
  longe dela, o arco fica com o mesmo raio. Antes o arco era refeito pelo ponto do meio antigo e o centro andava centímetros.
- Emenda em curva: o centro da curva do eixo entra nos centros exatos e a curva tem flecha de 1 mm.
- Emulação no núcleo do que vai ao Revit (contorno ajustado amostrado de volta): de 2,7–11,5 L por interseção para
  0–31 cm³ nos cruzamentos e T; 2,8 L na emenda em curva (KNOWN_ISSUES K13).

### Reta fora do eixo e conferência final (FC4)
- A lombada invertida recortava a sarjeta com 1 cm de folga e deixava uma reta de 218 m a 1 cm fora do eixo. Ela não recorta
  mais os elementos físicos da via.
- A conferência final do Autoteste não conta mais desalinhamentos abaixo do ruído numérico (deslocamento da ponta < 0,1 µm).

### Testes
- `FCRevitFullRun4Tests`: rotatórias (8 tipos) sem pintura sobre a sarjeta nem pisos sobrepostos; formas diretas da oval que o
  Revit aceita; T da via reconhecida (sem pares entre marcas e < 1 cm² depois do ajuste); cruzamento rodoferroviário; as 8
  interseções do 4º Autoteste com os pisos da mesma curva no mesmo arco; `AxisSnap` mantendo o raio.
- `V4Tests` (pavimento "Nenhum" registrado sem peças) e `EWRevitWarningsTests` atualizados: arco com o mesmo raio depois
  do alinhamento; o cruzamento local × local já sai do ajuste sem retas quase alinhadas (elas vinham da junção projetada na
  direção do centro, que girava a reta).
- Suíte completa: 2 869/2 869 em 14 min 57 s (Linux). Core, plugin e testes com 0 avisos.

## 2026-10-20 – FB: Autoteste completo mais rápido e com todos os dados numa rodada só

Fase da missão: XV (desempenho e testes) – atual; IV e XIV seguem em andamento, com o Autoteste como evidência.

### Por que o Autoteste completo demorava (3º completo: 1 h 41 min, 178 etapas)
- O tempo de uma mesma operação crescia com o projeto: uma via local levava 5 s no começo e um cul-de-sac de 30 elementos,
  46 s no meio; uma linha de 3 elementos, 5 s. Causa: ler as marcas do projeto interpretava o JSON de **todos** os elementos
  do plugin a cada consulta. Cada geração consultava a própria marca (`ById`). Toda marca 3D perguntava se era filha de uma
  interseção, interpretando o projeto inteiro. Interseções, atualizador e recortes consultavam dezenas de vezes por
  operação. Medido no núcleo: ~60 µs por JSON (147 definições de vias, 2 940 leituras em 183 ms).
- Os recortes sob contornos (faixa de pedestres, MAC, extensão de calçada, drenagem) montavam a geometria de todas as marcas
  do projeto para achar as que passam sob o contorno.
- O piso tátil de alerta de 30 m (cada relevo é um sólido) levou 171 s numa etapa só.

### Leitura das marcas (FB1)
- `MarkingStorage` lê o id gravado em cada elemento e só interpreta o JSON quando precisa:
  - `ById` e `Definition`: só a marca pedida;
  - `Definitions`: só o primeiro elemento de cada marca;
  - `Ids` e `Elements`: nenhum JSON.
- O tipo da definição é lido do começo do JSON (`DefinitionKinds`: o discriminador "tipo", que o System.Text.Json grava
  primeiro). `Definitions<T>` e `Definitions(doc, tipos…)` interpretam só as marcas dos tipos pedidos: interseções,
  rotatórias, vias, anotações. São 50 chamadas trocadas.
- `InGroup` e `Mentioning` interpretam só o JSON que cita o id do conjunto, da marca ou do elemento alterado: membros da via,
  recortes de uma interseção, marcas que o atualizador precisa refazer.
- Na dúvida (texto num formato inesperado), a marca é interpretada como antes. O resultado é o mesmo da leitura completa
  (DECISIONS D-013).
- `MarkingService`:
  - a pergunta "é filha de um nó?" lê só as interseções e as rotatórias;
  - a consulta por id dos geradores lê só a marca pedida (depois de 8 consultas, o cache geral);
  - os detalhes dependentes leem só as anotações.
- Cópias (`CopyHandler`) e a limpeza do atualizador ao apagar marcas (`RemoveOrphanDetails`) não interpretam mais o projeto
  inteiro.
- Recortes sob contornos (`FootprintCutter`): a marca cujo caminho fica a mais de 60 m do contorno não tem a geometria
  montada.

### Autoteste (FB2)
- Nova ordem: vias, conexões e "Largura e recuos" (as etapas mais pesadas) logo depois do loteamento, com o projeto ainda
  pequeno; calçadas e catálogos de sinalização horizontal e vertical depois.
- Piso tátil (PTA/PTD) do catálogo com 6 m em vez de 30 m – o mesmo desenho.
- Tempo por trecho do plugin em cada etapa (`Perf`): gerar marcas, geometria do núcleo, leitura das marcas (varreduras,
  elementos, JSON), gravação da transação, atualizador, interseções, recortes e avisos do Revit. No fim do relatório: o tempo
  por trecho, as 15 etapas mais lentas e o tempo por bloco.

### Tudo numa rodada só (FB3)
- Arquivo de dados `autoteste-…-dados.jsonl` ao lado do relatório, gravado a cada etapa:
  - todos os erros, avisos e notas, sem o limite de 25 do relatório;
  - cada par de pisos sobrepostos: códigos, marcas, classe, cor, tipo, espessura, cotas, volume e centro;
  - cada reta fora do eixo, com as pontas;
  - o JSON de cada marca citada.
- Nova etapa final, "Modelo final", que roda também no modo rápido:
  - confere **todos** os pisos do teste no modelo como ficou – pares com volume em comum e retas do esboço fora do eixo –,
    sem depender dos avisos, que o teste descarta a cada transação;
  - grava o contorno de cada piso e o JSON de todas as definições do teste.
- Com esse arquivo, os defeitos que restarem podem ser reproduzidos no núcleo, fora do Revit, sem rodar o Autoteste de novo.

### Testes
- `FBAutotestSpeedTests` (13):
  - o tipo de todas as definições é lido do começo do JSON, compacto e indentado;
  - `MayBe` bate com o teste de tipo da leitura completa, em classes e interfaces;
  - texto inesperado nunca é descartado;
  - ids de conjunto, recorte, marca, elemento e via hospedeira saem no JSON sem escape.
- Suíte completa: **2 856 aprovados, 0 falhas** (20 min 59 s). Plugin: 0 avisos, 0 erros.

## 2026-10-20 – FA: 3º Autoteste completo – forma direta na rotatória oval, tipos de piso na nova geração e recuos

Fases da missão: IV (motor geométrico) e XIV (regras da API do Revit), as duas em andamento; VI (corredores e seções)
avançou com os recuos.

### Evidência
- 3º Autoteste **completo** com a DLL do EZ, em `docs/evidencias/2026-10-20-autoteste-completo-3.txt` (log do período em
  `2026-10-20-log-autoteste-completo-3.txt`):
  - 178 etapas: 110 OK, 67 com aviso e **1 com erro** (rotatória oval: o Revit recusou a forma direta), em 1 h 41 min;
  - retas fora do eixo: de 85 para **8**, todas na rotatória com by-pass; LDP × SDP: de 34 pares para 0;
  - 3 135 avisos de pisos sobrepostos, 590 pares que continuam;
  - a Preparação não achou tipo de piso com estrutura diferente do nome.
- As cotas dos exemplos (novas no EZ) mostraram a causa dos 150 pares ROTATORIA × ROTATORIA: os dois pisos têm tipo de 15 cm
  igual ao nome, mas um vai de 0,121 a 0,271 m – a hipótese do tipo antigo (D-011) não se confirmou.

### Tipo de piso na nova geração (FA2)
- Ao gerar de novo uma marca, o tipo do primeiro piso de cada cor valia para todas as peças dela, como se o usuário o
  tivesse escolhido. Na ilha da rotatória, a gola das árvores (concreto de 12 cm) recebia o tipo de 15 cm do meio-fio e
  afundava 3 cm na ilha (150 pares, 5,2 m³); a sarjeta (0,5 cm) recebia 15 cm e invadia pavimento, faixa de pedestres e
  bordo (FTP-1 × ROTATORIA, LBO × ROTATORIA, INTERSECAO × INTERSECAO).
- Agora só conta como escolha do usuário o tipo que difere do gravado na chave do piso ao criá-lo, e só para as peças da
  mesma cor e da mesma espessura do piso trocado. DECISIONS D-012.

### Rotatória oval: forma direta recusada (FA1)
- As golas das árvores da ilha eram quatro vigas encostadas; giradas, não coincidiam na grade das booleanas e a união saía
  como um contorno que se toca (ou em duas peças). A gola agora é uma peça só (moldura com furo) e a união dos pisos fecha
  frestas de até 1 mm quando a parte se toca (`ElementPlan.FloorUnion`).
- O plugin usava o primeiro ajuste em retas e arcos do contorno sem conferir: um anel da calçada do by-pass se cruzava.
  `BoundaryFit.FitRing` confere e tenta tolerâncias menores antes de voltar às retas.
- Forma direta: cada objeto é conferido (`DirectShape.IsValidShape`) e o recusado sai com aviso e posição, em vez de o
  Revit recusar a cor inteira da marca.

### Retas fora do eixo restantes (FA4)
- As 8 retas da rotatória com by-pass (22,6 m a 0,11°) vinham da limpeza de "espinhos": a ponta de uma lasca de 1,7 m da
  calçada era tirada e os vizinhos eram ligados por uma reta inclinada. A ponta agora é cortada no pé da perpendicular sobre
  o lado mais longo, que mantém a direção (`ProfileCleanup.SpikeFoot`, também no preparo do contorno do plugin).

### Recuos: baias e faixas de aceleração/desaceleração (FA3)
- Recuo maior que a faixa livre da calçada (faixa de desaceleração de 3,5 m, calçada com 2,8 m livres): a calçada ficava com
  largura negativa e a grama passava por cima dela e 0,7 m além do lote. A faixa livre agora absorve até sumir e o resto
  empurra o alinhamento, com aviso ("a calçada some no trecho e o alinhamento avança … m sobre o lote").
- Sarjeta, meio-fio e grama, que só se deslocam, passam a ter as bordas na normal do eixo (como a calçada, que também
  estreita): antes, deslocadas e engrossadas na normal da linha inclinada, faziam no fim sem transição da faixa de
  desaceleração uma perna transversal sobre as vizinhas, e lascas nas transições.
- A faixa que some só é gerada onde tem 1 cm ou mais; as bordas de uma faixa variável são amostradas onde o deslocamento ou
  a largura variam.
- No núcleo, as duas etapas de recuos do Autoteste ficam sem nenhum piso sobreposto (antes 1,6 m³ e 0,3 m³, os mesmos
  volumes do Revit).

### Ainda não corrigido (próximo ciclo)
- Pinturas cortadas por outras: MAC desenhada sobre a extensão de calçada, linha de retenção × vagas (LRE × MER-0), faixa de
  pedestres × zebrado da ilha pintada nas interseções (FTP-1 × ZPA-A) e × linha de continuidade (FTP-1 × LCO), vaga ×
  sarjeta (MER-0 × SARJETA, 4 cm³ cada). KNOWN_ISSUES K11.

### Testes
- `FARevitFullRun3Tests` (18): pisos das 8 rotatórias na posição do Autoteste sem contorno que se toca nem reta fora do eixo,
  gola em peça única com furo, união das peças giradas, espinho, as duas etapas de recuos sem sobreposição, espessura do tipo.
- Suíte completa: **2 843 aprovados, 0 falhas** (20 min 18 s). Plugin: 0 avisos, 0 erros.

## 2026-10-20 – EZ: 2º Autoteste completo – retas fora do eixo restantes, sinalização das rotatórias e tipos de piso

Fases da missão: IV (motor geométrico) e XIV (regras da API do Revit), as duas em andamento. Quadro das 15 fases no ROADMAP.

### Evidência
- 2º Autoteste **completo** do usuário com a DLL do EY (20/10 07:59), em `docs/evidencias/2026-10-20-autoteste-completo-2.txt`:
  - 178 etapas: 106 OK, 72 com aviso e **0 com erro**, em 1 h 41 min – o erro das placas do EY não voltou;
  - retas fora do eixo: de 1 385 avisos para **85**, em 17 etapas;
  - 3 153 avisos de pisos sobrepostos: 2 053 de passagem (piso apagado ou refeito no fim da etapa), 39 sem volume em
    comum, 439 sem conferência e **622 que continuam**, agora listados por tipo.
- Pares persistentes por tipo (todas as etapas): ROTATORIA × ROTATORIA da mesma marca 149, FTP-1 × ROTATORIA 107,
  MER-0 × SARJETA 57, LBO × ROTATORIA 44, LDP × SDP 34, INTERSECAO × INTERSECAO 33, FTP-1 × LCO 12, LRE × MER-0 12 e
  outros com menos de 10.

### Retas fora do eixo (EZ1)
Cada grupo das 85 retas reproduzido no núcleo e corrigido:
- **Zebrados a 45° das canalizações** (MTL 14, MAP 13, MAC, ZPA, MER-45; deslocamento de 0,0 mm): alinhar a reta de 45°
  movia o vértice da quina só na direção dela e entortava a horizontal vizinha, que já era exata. As retas exatas entram
  agora no grupo e o vértice vai ao encontro das duas (`AxisSnap`).
- **Pisos que acompanham o terreno** (vértices mantidos um a um): passam também pelo alinhamento, com no máximo 0,5 mm por
  vértice – a edição de forma lê as cotas na posição final.
- **Rotatórias** (2 a 9 retas de 17 a 28 m por etapa, a 0,03°–0,12°, ponta a 10–44 mm):
  - `BoundaryFit.Significant` conferia o primeiro ponto do anel com o último ainda não decidido e o último com o primeiro
    já removido; os dois saíam juntos e a reta do bordo do ramo ia até o começo da curva de entrada. A volta começa agora
    no canto mais vivo, que sempre fica;
  - `Polygon2.Simplified`: de dois vértices a menos que a aresta mínima, ficava o primeiro – na quina do bordo do ramo com o
    círculo da zona (9 mm) ficava o ponto do círculo e a reta de 18 m saía inclinada. Fica agora o da ponta do lado mais
    comprido.
- **Via reconhecida em pisos comuns** (18 retas a 0,014° nos pisos da via e o aviso na própria linha do eixo): as pontas
  livres do eixo cordal escorregam uns centímetros para a quina e o prolongamento seguia daí. As pontas seguem agora reto a
  partir dos pontos já centrados, e o eixo lido passa pelo alinhamento de linhas abertas (`AxisSnap.SnapPath`).

### Sinalização das entradas das rotatórias (EZ2)
- O triângulo "dê a preferência" (SDP) saía da posição no sentido do anel e entrava nos dentes da linha (LDP × SDP em
  todas as entradas). Agora começa 1 m depois da linha e, com travessia, termina 0,5 m antes da faixa de pedestres; sem
  espaço, é omitido com aviso na etapa.
- Na minirrotatória, com a travessia a 5 m, a faixa de pedestres passava sobre o zebrado da gota pintada (FTP-1 × ZPA): a
  faixa recorta o zebrado.
- Rotatórias (mini, 1 faixa, compacta, 2 faixas) sem pintura sobre pintura nas entradas, no núcleo.

### Tipos de piso do plugin
- Hipótese para os 149 pares ROTATORIA × ROTATORIA da mesma marca (pares iguais de 10 750 cm³): tipo "SV - … cm" criado por
  versão antiga com a estrutura do tipo base, mais grossa que o nome, invadindo os pisos de baixo.
- Um tipo existente do plugin cuja estrutura difere da espessura do nome (mais de 0,5 mm) passa a ser corrigido quando é
  usado (fica a camada estrutural, com a espessura do nome; registro no log). DECISIONS D-011.
- Autoteste: a Preparação avisa os tipos com estrutura diferente do nome; os exemplos de pisos sobrepostos trazem as cotas
  de base e topo e a espessura do tipo.
- A confirmar no próximo Autoteste.

### Testes
- `EZRevitFullRun2Tests` (25): zebrados e canalizações sem retas fora do eixo, contorno da pista de todas as rotatórias
  sem retas fora do eixo, fim de reta na volta do anel, vértices colados na simplificação, eixo exato da via reconhecida,
  alinhamento de linhas abertas, sinalização das entradas das rotatórias sem pintura sobre pintura, leitura da espessura no
  nome do tipo de piso.
- Suíte completa: **2 825 aprovados, 0 falhas** (14 min 46 s). Plugin: 0 avisos, 0 erros.

## 2026-10-20 – EY: Autoteste completo no Revit 2027 – placas, retas fora do eixo e pisos sobrepostos

### Evidência
- Autoteste **completo** do usuário com a DLL 2.1.0 do EX, guardado em `docs/evidencias/2026-10-20-autoteste-completo.txt`,
  com o trecho do log do período em `2026-10-20-log-autoteste-completo.txt`:
  - 178 etapas: 97 OK, 80 com aviso e **1 com erro**, em 1 h 42 min;
  - 3 091 avisos de pisos sobrepostos em 52 etapas e 1 385 de retas fora do eixo em 57 etapas.
- A conferência do EX funcionou. Dos 3 091 pares sobrepostos:
  - **2 023** tinham um dos pisos já apagado ou refeito no fim da etapa (aviso de passagem);
  - 29 já não tinham volume em comum;
  - 425 ficaram sem conferência;
  - **614 continuam sobrepostos**.
- Os 62 avisos de retas fora do eixo da etapa do loteamento eram de SARJETA, CALCADA, ORELHA, INTERSECAO e LEG, não dos lotes:
  a hipótese do EX não se confirmou.

### Placas – o único erro (EY1)
- O Revit recusou o perfil de peças do catálogo de placas ("Curve loops do not satisfy the input requirements" e "Perfil
  inválido").
- Reproduzido no núcleo com o mesmo preparo do contorno: 19 perfis (de 1 501) se tocam.
  - Exemplos: o pictograma do R-13 e do A-31 passa duas vezes pelo mesmo vértice.
  - Outros voltam sobre si com lascas de 0,1 mm, a grade das booleanas.
- `ProfileCleanup` (Core):
  - aponta o problema;
  - devolve partes limpas (pescoços e lascas menores que 1 mm saem);
  - descarta peças menores que a tolerância do Revit.
- O gerador de placas usa as partes limpas, e o `MarkingService` tenta as partes quando o Revit recusa um perfil.
  - Peça menor que a tolerância vira aviso ("ficaram de fora"), não erro no log.

### Retas fora do eixo (EY3)
- Pela ajuda do Revit 2027, o aviso vale para retas a até 0,2° da horizontal, da vertical **ou de 45°**.
- O ajuste do contorno em retas e arcos desloca as pontas das retas em 1 a 4 mm, por exemplo retas de 15 m a 0,005° nas
  rotatórias.
- `AxisSnap` passa a conferir 0°, 45°, 90° e 135°, só dentro dos 0,2°.
  - No `MarkingService` usa o desvio do ajuste do contorno (padrão 5 mm).
  - Não aplica um alinhamento que faria um contorno válido se tocar.
  - DECISIONS D-010.
- Nas cenas de teste (cruzamento, T, esconso, três rotatórias, cul-de-sac): de 87 retas que o Revit avisaria para 2.

### Pisos sobrepostos que persistem (EY2)
- `PieceOverlaps` (Core) mede a sobreposição como o Autoteste no Revit: área em planta × altura em comum.
  - `ElementPlan.RevitPieces` dá às pinturas a mesma altura que o Revit dá, inclusive as marcas sobre pintura de fundo.
- Reproduzidos, com os mesmos volumes do relatório, e corrigidos:
  - **Via com canteiros laterais**: a linha de fundo das vagas ficava metade dentro do meio-fio do canteiro (MER-0 ×
    MEIO-FIO). Agora não há linha de fundo quando a divisa é meio-fio de canteiro ou calçada.
  - **Ciclofaixa bidirecional e ciclovia**: o símbolo da bicicleta encostava na linha de bordo (CIC-LD × SIC). Agora cada
    símbolo fica no meio do seu lado, entre a linha central e a de bordo.
  - **Canteiros com árvore** (gramado e jardineira): a grama e a proteção do tronco passavam sobre a borda, mais alta
    (3 328 e 8 320 cm³, iguais aos do Revit). Agora ficam só sobre a terra.
  - **Faixa de pedestres sobre vagas** (MER-0 × FTP-1): o recorte das marcas "por cima" inclui as vagas.
- Todos os 10 modelos de via, as 4 ciclofaixas e os 3 canteiros ficam sem nenhum par sobreposto no núcleo.
- Sem correção ainda:
  - lascas entre pisos vizinhos da mesma interseção ou rotatória, criadas pelo ajuste do contorno piso a piso (K13);
  - os tipos persistentes que o relatório não detalhou.

### Autoteste
- Todos os pares que continuam sobrepostos são listados por tipo, com o volume, numa nota sempre visível.
  - O aviso com exemplos vai para o topo da etapa; antes ficava escondido no "… e mais N avisos" em 26 de 39 etapas.
- As retas dos avisos "fora da linha central" são medidas: desalinhamento, comprimento e quanto a ponta teria de andar.

### Testes
- +15 `EYRevitFullRunTests`.
- Suíte completa: **2 800/2 800** (14 min 34 s), sem regressão nos testes antigos de placas, ciclofaixas, vagas e canteiros.

## 2026-10-20 – EX: segundo Autoteste (2.1.0), Marca única dos lotes, alinhamento nos lotes e sobreposições conferidas

### Evidência
- Autoteste rápido com a DLL 2.1.0 guardado em `docs/evidencias/2026-10-19-autoteste-rapido-2.1.0.txt`: 7 etapas, 4 OK,
  3 com aviso, **0 erros**. A interseção em cruz ficou fora pelo limite de tempo do modo rápido.
- Avisos do Revit:
  - etapa do loteamento: 239 × "Pisos realçadas se sobrepõem" (23 entre elementos da mesma marca), 65 × "Marca"
    duplicada, 62 × "ligeiramente fora da linha central" (eram centenas antes do EW2);
  - lote personalizado e união de lotes: 12 e 11 × "Marca" duplicada;
  - vias isoladas: nenhum.

### "Marca" duplicada dos lotes (defeito real)
- Causa: a Marca de cada lote era "{quadra}-{número}". O projeto tinha dois loteamentos com as mesmas quadras (o do
  Autoteste anterior, mantido para inspeção, e o novo), então "B-3" se repetia.
- `LotMarks` (Core): com um loteamento só, continua "B-3"; com dois ou mais, o nome do loteamento vai na frente ("Jardim
  B-3") e, entre homônimos, também o início do id ("Autoteste-23ef B-3"). Vale para a área remanescente. SV_Codigo, quadros e
  memorial não mudam. DECISIONS D-009.

### "Ligeiramente fora da linha central" fora dos pisos das marcas
- Três caminhos ainda montavam contornos sem o alinhamento aos eixos do EW2:
  - lotes e áreas remanescentes (`LotService.Loops`);
  - acabamentos do terreno (`TerrainService.ApplyFinishes`);
  - o contorno só de retas do `MarkingService`, usado quando os arcos de um furo tocariam o contorno.
- Agora todos passam pelo `AxisSnap`: `AxisSnap.SnapRing` (Core) e o helper `PlanLoops` (Revit). Os pisos que acompanham o
  terreno (pontos de apoio da edição de forma) continuam com os vértices como estão.
- Defeito achado de passagem: nos acabamentos do terreno, uma reta menor que 0,01 pé era pulada e o contorno ficava aberto;
  o Revit recusava a subdivisão (registrado no log). Agora os pontos próximos saem antes de montar o contorno.
- **Não confirmado** que os 62 avisos vinham desses caminhos: a cena do núcleo (cruzamento na origem) gera lotes já exatos.
  O próximo relatório diz em que pisos ficaram (KNOWN_ISSUES K12).

### Autoteste
- O resumo de cada tipo de aviso diz em que elementos, contando todos e não só os 25 detalhados: "Σ 62 × "…" – em QUADRA-B
  40, LEG 22" (`WarningSummary`, Core, com testes).
- No fim de cada etapa com avisos "Pisos realçadas se sobrepõem", cada par é conferido no modelo como ficou, pelo volume em
  comum dos sólidos. O relatório separa os pares que **ainda se sobrepõem** (viram aviso da etapa, com exemplos) dos que
  **não se sobrepõem mais** (aviso de passagem) e dos pisos apagados ou refeitos. É a medida que decide o K11.

### Testes
- +10 `EXRevitWarningsTests`.
- Suíte completa: **2 785/2 785** (14 min 49 s).

## 2026-10-19 – EW: primeira evidência no Revit 2027, versão 2.1.0 e avisos do Revit

### Evidência
- Relatórios do usuário guardados em `docs/evidencias/`.
  - Autoteste rápido no Revit 2027.0 (build 27.0.4.412): 8 etapas, 4 OK, 4 com aviso, 0 erros.
  - Validar Modelo: 1 achado.
- AUDIT §2 e a matriz foram atualizados: inicialização, Autoteste e Validar Modelo **rodaram no Revit**. O modo completo ainda não foi rodado.

### Versão
- `Directory.Build.props`: **2.1.0** (antes 2.0.0), aprovado pelo usuário.

### Validar Modelo
- V06 recalibrado com o relatório real. As linhas usadas para **criar a gleba** não são relidas depois, então apagá-las não tem efeito: agora é **informação**, sem revisão humana. Linhas do limite e dos lotes de quadras "por linhas" continuam como **aviso**, porque são relidas.

### Avisos do Revit "A linha no croqui está ligeiramente fora da linha central"
- Reproduzido no núcleo: no cruzamento local × local, o pavimento da interseção tinha 24 retas inclinadas de 0,0008° a 0,067° (desvio de até 0,17 mm), resíduo das operações de recorte.
- `AxisSnap` (Core) deixa exatas as retas quase alinhadas aos eixos, movendo vértices no máximo 0,5 mm e só em retas com menos de 1° de inclinação. Retas inclinadas de projeto não são mexidas.
- Aplicado no contorno dos pisos no Revit (`MarkingService.FittedLoop` e o caminho só com retas).
- No cruzamento de teste: 24 → 0 retas quase alinhadas; contornos válidos; área igual (< 1 cm²).
- Defeito achado de passagem: `BoundarySegment.Circle` lia `Mid!.Value` numa reta e o `ToString` do record lançava `InvalidOperationException`. Corrigido, com teste.
- +6 testes `EWRevitWarningsTests`.

### Elementos do eixo das vias comuns (F5b)
- `AlignmentElements.FromAxis` reconhece na polilinha gerada do eixo de qualquer via (cantos arredondados ou arcos do Revit):
  - tangentes;
  - curvas circulares: raio pela corda e pelo giro total, deflexão, sentido e T;
  - cantos vivos.
- Exporta CSV pt-BR com estacas.
- **Perfil da Via** grava também `eixo-<via>.csv`.
- +4 testes `EWAxisElementsTests` (canto arredondado com R = 30 conferido à mão, canto vivo, curva reversa, reta).

### Avisos do Revit "Pisos realçados se sobrepõem"
- **Não reproduzidos no núcleo** (KNOWN_ISSUES K11). O Autoteste agora identifica cada elemento do aviso (id do elemento e da marca), marca "[mesma marca]" quando são pisos de uma mesma marca e soma os avisos por tipo em cada etapa.
- O próximo relatório decide se são pisos duplicados na regeneração ou sobreposição real.

### Testes
- Suíte completa: **2 771/2 771** (21 min 50 s), com EW1 e EW2.
- Depois dela, +4 testes do eixo (classes EW/EV de alinhamento 19/19): total 2 775.

## 2026-10-19 – EV6: volumes por seções transversais (Core)

### Terraplenagem (fase VII)
- `SectionVolumes` (Core) calcula volumes pelo **método das seções transversais**:
  - áreas de corte e aterro em seções perpendiculares ao eixo a cada N m, com a última sempre no fim;
  - volume pela média das áreas × distância;
  - acumulados (curva de massas) e CSV pt-BR.
- É um cálculo independente da malha de `Grading.Volumes`. Num caso de 2 000 m³ os dois métodos concordam com diferença abaixo de 2 %.
- Limitações documentadas, não corrigidas:
  - sem correção de curvatura (Pappus);
  - a média das áreas superestima volumes de variação não linear (teste com o erro esperado h²/12·∫A'').
- Defeito encontrado e corrigido antes de entrar: com a seção exatamente na ponta da plataforma, o teste dentro/fora era ambíguo e o último trecho perdia metade do volume (1 700 em vez de 2 000 m³). As pontas agora são amostradas 1 mm para dentro.
- **Ainda sem interface no Revit.** O quantitativo de terraplenagem continua pela malha.
- +9 testes `EVSectionVolumesTests`. Suíte completa: **2 765/2 765** (14 min 28 s).

## 2026-10-19 – EV5: geometria em casos degenerados e elementos do eixo

### Integridade geométrica (fase IV)
- `EVGeometryEdgeTests` (14 testes) cobrem:
  - polilinha vazia, de um ponto, com pontos repetidos e fechada com o ponto final repetido;
  - estacas fora do trecho;
  - offset em volta de 180° e em ângulo agudo (limite de esquadria);
  - traçado com PIs coincidentes, colineares, deflexão de 179°, 179,99° e 180°, raio que não cabe e espiral maior que o arco.
- Resultado: nenhum NaN/∞, e as pontas foram preservadas.
- `Polyline2.Project` sem segmentos devolve distância `double.MaxValue`. Agora isso é **contrato documentado e testado**, não acaso: um eixo degenerado nunca é "o mais próximo". Não foi mudado porque tem 141 chamadas.

### Alinhamentos (fase V)
- `AlignmentElements` (Core) gera os relatórios a partir das **mesmas rotinas** da geometria:
  - curvas horizontais do traçado por PIs: deflexão, R e Ls efetivos e pedidos, T, estacas TS/SC/CS/ST, extensão;
  - PIVs do greide: i1, i2, A, L efetivo e pedido, K, PCV/PTV, ponto alto/baixo.
  - Exporta CSV pt-BR e estacas "n+resto".
- `Tracado.Build` foi separado em `Solve` (curvas) + desenho, e `RoadGrade.CurveLength` foi extraído de `Z(s)`. A geometria não mudou: a suíte inteira passa.
- Revit: **Perfil da Via** grava `greide-<via>.csv` com os elementos do greide aplicado.
- Nenhum critério normativo novo: o piso K = 10 do greide já existia e passa a estar documentado como suavização, não como norma.
- +10 testes `EVAlignmentElementsTests`, com valores calculados à mão: curva circular Δ = 90°, convexa ±2 %, côncava com piso K, vão curto, consistência com `Z(s)` e `Tracado.Build`.

### Compatibilização (fase X)
- Validar Modelo ganha três verificações (+2 testes):
  - V09: quadras do mesmo loteamento sobrepostas (erro);
  - V10: quadra fora da gleba (aviso, confiança média);
  - V11: interseção com menos de duas vias existentes (aviso).
- Tolerância de 0,5 m² para não apontar contornos que só se encostam.

### Testes
- Suíte completa: **2 754/2 754** (13 min 38 s) com os refatoramentos de `Tracado`/`RoadGrade`. Depois disso, +2 testes do validador (V09–V11), rodados na classe `EVModelIntegrityTests`: 28/28.

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
