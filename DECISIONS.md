# DECISIONS – Registro de decisões técnicas

Formato: contexto, decisão, consequências. Decisões novas vão no fim. Uma decisão revogada não é apagada: é marcada
como substituída.

## D-001 – Núcleo independente do Revit, compilado dentro da DLL do plugin
- **Contexto:** a lógica geométrica e normativa precisa ser testável sem o Revit, e a instalação deve ser "copiar 2 arquivos".
- **Decisão:** `SinalizacaoViaria.Core` (net10.0) não referencia a API do Revit. O projeto do plugin compila os fontes do
  Core por `<Compile Include>`, e o resultado é uma única `SinalizaBIM.dll`. O Clipper2 é incorporado como código-fonte.
- **Consequências:** `dotnet test` roda em qualquer SO. A DLL não tem dependências soltas. Toda regra nova entra no Core
  com teste, e a camada Revit fica fina.

## D-002 – Compilar contra a API 2027.0.0
- **Contexto:** o assembly `RevitAPI` muda de versão a cada atualização 2027.x, e o .NET exige versão referenciada menor
  ou igual à carregada.
- **Decisão:** referenciar `Nice3point.Revit.Api.*` **2027.0.0**, só para compilação (`ExcludeAssets="runtime"`).
- **Consequências:** a DLL carrega em qualquer 2027.x (inferência; ver AUDIT §2). APIs novas das atualizações não
  podem ser usadas.

## D-003 – Versão de dados e migrações das definições
- **Contexto:** as definições (JSON) ficam gravadas nos modelos dos usuários por anos. Antes, um JSON de versão mais
  nova era lido e regravado como v1, e os campos desconhecidos se perdiam. Também não havia como migrar formatos antigos.
- **Decisão:**
  - `DefinitionVersioning.Load` lê com estado: Atual, Migrada, MaisNova ou Inválida. Ela aplica passos registrados
    (N → N+1) até `CurrentVersion` e nunca lança exceção.
  - Definição **mais nova** é somente leitura: `MarkingService.Render`/`RescaleInPlace` mantêm os elementos como estão,
    com aviso, e `MarkingStorage.Write` recusa gravá-la.
  - JSON sem `version` conta como v1.
- **Consequências:** mudar o formato exige três coisas: incrementar `CurrentVersion`, registrar o passo em
  `DefinitionVersioning.Passos` e criar um teste de migração. O teste de ida e volta cobre todos os tipos registrados.
  O esquema do Extensible Storage (GUID `7C1E9A52-…`) **não muda**: só o JSON interno é versionado, porque esquemas do
  Revit são imutáveis.

## D-004 – Grafo de dependências derivado, não persistido
- **Contexto:** as dependências já estão gravadas nas definições (`TargetId`, `RoadIds`, `ChildIds`, `LoteamentoId`,
  `SourceId`…). Um segundo registro persistido poderia divergir delas.
- **Decisão:** `ModelGraph` é **calculado** a partir das definições lidas. Os recortes (`ExclusionZone.SourceId`) são
  referência fraca: não entram em ciclos nem na ordem, porque a via guarda o recorte da interseção que a cita. As
  filhas (`ChildIds`) dependem do nó que as lista.
- **Consequências:** não há o que migrar nem sincronizar. O grafo serve à validação e à ordem de regeneração.
- **Atualização (Fase III, 2026-10-21):** `MarkingService.DependencyOrder` passou a usar `ModelGraph.RegenerationSort`. A
  chave é (grupo, profundidade de dependência dentro do conjunto, ordem de entrada), em que o grupo é: marcas, depois
  detalhes, depois anotações do projeto todo.
  - Sem dependências dentro de um grupo, o resultado é idêntico ao de antes (teste de equivalência).
  - A ordem só muda onde antes podia sair errada: filha antes do nó; detalhe de um detalhe antes do detalhe.
  - Os recortes continuam fora da ordem.
  - Não foi substituído por `RegenerationOrder` (Kahn sobre o projeto todo), que reordenaria grupos e conjuntos inteiros
    sem necessidade.
  - Validação no Revit pendente (Releases/Fase03).

## D-005 – Validação do modelo somente leitura, com severidade e confiança
- **Contexto:** a missão pede verificações com severidade, confiança e indicação de revisão humana, sem decisões
  automáticas irreversíveis.
- **Decisão:** `ModelValidator.Validate` (Core) devolve achados V01–V11. `CmdValidarModelo` usa
  `TransactionMode.ReadOnly`: não altera o modelo, grava um TXT e só oferece selecionar os elementos.
- **Consequências:** nenhuma correção automática. Corrigir é decisão do usuário, com as ferramentas existentes
  (Editar, Atualizar, apagar).

## D-006 – Erros tratados registrados, nunca engolidos quando afetam o resultado
- **Contexto:** a auditoria encontrou capturas silenciosas que escondiam perda de geometria ou valor padrão assumido.
- **Decisão:** quando a captura muda o resultado (contorno degenerado fora do piso, Norte assumido 0°, avisos do Revit
  suprimidos), ela passa a registrar no log e, se houver perda visível, a avisar o usuário. Leituras de propriedades
  opcionais continuam silenciosas.
- **Consequências:** `log.txt` gira para `log.1.txt` ao passar de 2 MB, em vez de ser apagado.

## D-007 – Sem bibliotecas geométricas externas novas
- **Contexto:** a missão restringe novas dependências.
- **Decisão:** as verificações novas (`PolygonChecks`) são código próprio. O Clipper2 continua a única biblioteca de
  terceiros.
- **Consequências:** nada muda na distribuição nem nas licenças.

## D-008 – Retas quase alinhadas aos eixos ficam exatas no contorno dos pisos
- **Contexto:** o Autoteste no Revit 2027 (2026-10-19) mostrou centenas de avisos "A linha no croqui está ligeiramente
  fora da linha central" nos pisos das interseções. No núcleo, as retas estavam inclinadas por resíduo numérico dos
  recortes (até 0,17 mm).
- **Decisão:** `AxisSnap.Snap` no contorno ajustado antes de criar as curvas do Revit:
  - vértices ligados por retas com menos de 1° de inclinação recebem a mesma coordenada (a da reta mais longa do grupo);
  - só quando nenhum vértice anda mais de 0,5 mm.
- **Consequências:** o aviso some nos casos de resíduo, e a geometria muda menos de 0,5 mm. Retas realmente inclinadas
  pelo projeto (por exemplo, uma via a 0,05° num trecho longo) continuam inclinadas e podem continuar com o aviso, que
  nesse caso é verdadeiro. Os limites são do plugin, não de norma.

## D-009 – Marca dos lotes única no projeto, com o nome do loteamento quando há mais de um
- **Contexto:** o 2º Autoteste no Revit 2027 (2026-10-19) teve 65 + 12 + 11 avisos "Os elementos têm valores de 'Marca'
  duplicados": a Marca de cada lote era "{quadra}-{número}" e o projeto tinha dois loteamentos (o do Autoteste anterior,
  mantido para inspeção, e o novo), com as mesmas quadras.
- **Decisão:** `LotMarks` (Core). Com um loteamento só, a Marca continua "B-3". Com dois ou mais, ganha o nome do
  loteamento na frente ("Jardim B-3"); entre loteamentos de mesmo nome (sem diferenciar maiúsculas), também o início do id,
  crescendo de 4 em 4 caracteres até diferir ("Jardim-23ef B-3"). A área remanescente segue a mesma regra. O SV_Codigo, os
  quadros e o memorial continuam com "B-3".
- **Consequências:** a Marca deixa de se repetir sem mudar nada em projetos com um loteamento só. As marcas de um loteamento
  gerado quando era o único só ganham o prefixo quando ele for regenerado; até lá continuam únicas, porque o novo loteamento
  já nasce com prefixo. Etiquetas que mostram a Marca passam a mostrar o prefixo quando houver mais de um loteamento.

## D-010 – Alinhamento das retas pelo critério documentado do Revit e com o desvio do ajuste do contorno
- **Contexto:** o Autoteste completo (2026-10-20) ainda teve 1 385 avisos "ligeiramente fora da linha central". A ajuda do
  Revit 2027 ("Element Is Slightly Off Axis") diz que o aviso vale para retas desalinhadas de 1×10⁻⁹ a 0,2° da horizontal,
  da vertical ou de 45°. No núcleo, as retas avisadas saíam do ajuste do contorno em retas e arcos, que desloca as pontas
  em 1 a 4 mm (retas de 15 m nas rotatórias a 0,005°).
- **Decisão:** `AxisSnap` passa a conferir 0°, 45°, 90° e 135°, só dentro dos 0,2° do Revit (antes 1°, só X e Y). No
  `MarkingService`, cada vértice pode andar até o desvio máximo do ajuste do contorno (Configurações, padrão 5 mm); nos
  lotes, áreas remanescentes e acabamentos do terreno, sem ajuste de arcos, continua 0,5 mm. Um alinhamento que faria um
  contorno válido se tocar não é aplicado.
- **Consequências:** o desvio total de um vértice em relação à geometria gerada pode chegar a duas vezes a tolerância do
  ajuste (5 mm do ajuste + 5 mm do alinhamento) – invisível na escala do projeto. Retas entre 0,2° e 1°, que antes eram
  alinhadas, ficam como estão (o Revit não avisa e a inclinação é de projeto). Os limites são do plugin e da ajuda do
  Revit, não de norma.


## D-011 – Tipos de piso do plugin com estrutura diferente do nome são corrigidos ao serem usados
- **Contexto:** no 2º Autoteste completo (2026-10-20 09:44) os pisos de uma mesma rotatória continuaram sobrepostos em 149
  pares, quase todos com o mesmo volume (10 750 cm³), o que não se reproduz no núcleo com a espessura do nome do tipo. O
  plugin reaproveita os tipos pelo nome ("SV - Concreto 15 cm"); um tipo criado por versão antiga, quando o Revit recusou a
  estrutura nova, ficou com a estrutura do tipo base – mais grosso que o nome – e invade os pisos de baixo.
- **Decisão:** ao usar um tipo existente cujo nome segue o padrão do plugin ("SV - … N cm") e cuja estrutura difere da
  espessura do nome em mais de 0,5 mm, o plugin deixa só a camada estrutural com a espessura do nome e registra a correção no
  log. Tipos com outros nomes nunca são mexidos. A Preparação do Autoteste lista os tipos nessa situação antes de começar.
- **Consequências:** os pisos que já usam o tipo mudam de espessura junto (passam à espessura que o nome declara), também nas
  marcas que não estão sendo refeitas; materiais das camadas removidas saem do tipo. É uma alteração de tipos do próprio
  plugin no modelo, feita só quando o plugin cria ou refaz pisos com aquele tipo, e reversível pelo histórico do Revit
  (Desfazer). Hipótese a confirmar no próximo Autoteste.
- **Atualização (FA, 3º Autoteste completo):** a hipótese não se confirmou – a Preparação não achou nenhum tipo nessa
  situação e as cotas dos exemplos mostraram tipos de 15 cm iguais ao nome. A causa dos 150 pares era outra (D-012). A
  correção fica como proteção para modelos com tipos de versões antigas.

## D-012 – Tipo de piso do usuário só onde ele trocou, e por espessura
- **Contexto:** na nova geração de uma marca, o plugin guardava, por cor, o tipo do primeiro piso existente e o aplicava a
  todas as peças daquela cor – para manter o tipo que o usuário tivesse trocado. Mas o primeiro piso quase sempre tinha o
  tipo do próprio plugin, e cores com espessuras diferentes (concreto do meio-fio 15 cm, da gola da árvore 12 cm, da sarjeta
  0,5 cm) passavam todas a 15 cm: pisos mais grossos que o pedido, invadindo os de baixo (3º Autoteste completo: 150 pares
  nas rotatórias e os de faixa de pedestres, bordo e pavimento sobre a sarjeta).
- **Decisão:** a chave de reaproveitamento já grava o tipo que o plugin deu ao piso ao criá-lo. Conta como escolha do
  usuário só o piso cujo tipo atual difere desse; a escolha vale para as peças da mesma cor e da espessura do tipo original
  (pisos sem chave, de versões antigas: só se o tipo não for um tipo do plugin, e então para a cor inteira).
- **Consequências:** a troca de tipo feita pelo usuário continua mantida nas novas gerações, agora sem estender a peças de
  outra espessura; os pisos que tinham herdado um tipo errado voltam ao tipo da espessura certa na próxima geração.

## D-013 – Ler as marcas sem interpretar o que não precisa, sem cache entre consultas
- **Contexto:** cada consulta às marcas do projeto (por id, por tipo, por conjunto) interpretava o JSON de todos os
  elementos do plugin (~60 µs cada). Como gerar, conectar, recortar e atualizar consultam dezenas de vezes por operação, o
  tempo de uma operação crescia com o projeto. No 3º Autoteste completo: 1 h 41 min, e um cul-de-sac de 30 elementos levava
  46 s no meio do teste contra 5 s de uma via no começo.
- **Alternativas descartadas:**
  - Cache das definições interpretadas por documento. As definições são objetos mutáveis que os comandos alteram antes de
    gerar. Um cache compartilhado devolveria um objeto alterado e não gravado (geração com falha, transação desfeita), e o
    Revit não avisa quando uma transação é desfeita.
  - Cópia profunda a partir do cache: custa o mesmo que interpretar.
- **Decisão:** cada consulta continua lendo o documento, mas só interpreta o JSON necessário. Antes, três filtros baratos:
  - o id da marca (campo próprio do esquema);
  - o tipo (começo do JSON, `DefinitionKinds`);
  - um filtro de texto pelo id procurado (`Mentioning`).
  Na dúvida, interpreta. O resultado é idêntico ao da leitura completa (`FBAutotestSpeedTests`).
- **Consequências:**
  - O ganho é proporcional à fração de marcas descartadas; o tempo por trecho do Autoteste mede o que sobrou.
  - Uma migração que renomear o discriminador deixa o nome antigo desconhecido, e ele cai na leitura completa.
  - Um discriminador nunca pode ser reaproveitado para outro tipo.

## D-014 – Via com pavimento "Nenhum" guarda a definição do pavimento como dado
- **Contexto:** a via reconhecida em pisos comuns (pista já modelada pelo usuário) usa pavimento "Nenhum". O plugin não
  registrava a definição do pavimento – e é ela que liga a via às interseções e rotatórias e guarda a seção para editar a
  via depois. No 4º Autoteste completo, o T de duas vias assim não virou interseção: calçadas, meios-fios e linhas se
  cruzaram (61 pares de pisos sobrepostos).
- **Alternativas descartadas:**
  - Piso de espessura mínima: um piso a mais sob o do usuário, sobreposto a ele.
  - Inferir a via pelas outras marcas do conjunto: já existe para vias antigas (`IntersectionService.Roads`), mas perde a
    seção e é a leitura mais cara do projeto (K15).
- **Decisão:** a definição do pavimento sempre é criada. Com "Nenhum", o `MarkingService` a grava só como dado
  (`SaveAsData`, sem elemento de modelo). O atualizador apaga o dado quando o conjunto não tem mais nenhuma outra marca.
- **Consequências:** interseções, rotatórias e edição da via funcionam igual para a pista do plugin e a do usuário. Uma via
  "Nenhum" de versão anterior só ganha a definição ao ser gerada de novo.

## D-015 – Ajuste de arcos com o centro exato aceita os cortes sobre as cordas da curva discretizada
- **Contexto:** as operações booleanas (Clipper) discretizam as curvas com flecha de 0,2 % do raio: 12 mm em R = 6 m, 20 mm em
  R = 10 m. Os recortes (rampas, limite da zona) caem sobre essas cordas, para dentro da curva. Com os 5 mm de tolerância, o
  ajuste em retas e arcos que vai ao Revit recusava o centro exato da esquina, e cada piso caía num círculo próprio: os
  vizinhos se sobrepunham ~2 mm ao longo da curva (13 pares INTERSECAO no 4º Autoteste completo).
- **Alternativas descartadas:**
  - Discretizar as curvas com 2 mm no `Close` das interseções: mudou outras formas que os testes protegem (grama da esquina,
    ângulos das extensões de calçada, rampas sobre a pista, orelhas, plataformas no terreno).
  - Não densificar as cordas no meio-fio da esquina: a sarjeta e o meio-fio chegavam ao limite da zona 11 cm fora das faixas
    da via (T da via reconhecida).
- **Decisão:** só quando o centro é um dos centros exatos do traçado (esquinas, emenda em curva):
  - os pontos podem ficar para dentro da circunferência até a flecha das cordas (0,25 % do raio, entre 5 mm e 3 cm);
  - o raio é o envelope externo dos vértices, no milímetro de projeto quando está a até 0,45 mm dele;
  - a junção com uma reta vai ao encontro dela com a circunferência (reta quase tangente, com o encontro longe: projeção
    na circunferência).
  Para os demais arcos, a tolerância continua 5 mm nos dois sentidos.
- **Consequências:**
  - Pisos vizinhos da mesma curva recebem o mesmo arco (0–31 cm³ sobrepostos por interseção, eram 2,7–11,5 L).
  - Um vértice de corte pode ficar até 3 cm de onde o Clipper o pôs, sempre sobre a curva de projeto.
  - A emenda em curva ainda tem as transições das pontas (K13).

## D-016 – Ajuste topológico: a borda comum a dois pisos da mesma marca é ajustada uma vez só
- **Contexto:** o contorno de cada piso vai ao Revit em retas e arcos inteiros (`BoundaryFit`, 5 mm), ajustado piso a piso.
  Numa borda curva comum a dois pisos, cada um escolhia quebras, raios e junções próprios. Sem centro exato do traçado
  (rotatórias, curvas que vêm de deslocamentos com cantos arredondados), os dois ficavam até 5 mm um do outro e se
  sobrepunham ao longo da curva. No 4º Autoteste completo: 36 pares ROTATORIA × ROTATORIA da mesma marca e a emenda em
  curva entre duas vias (2,8 L na emulação).
- **Alternativas descartadas:**
  - Só unificar retas e circunferências já ajustadas (consenso): resolve curvas com centro exato, mas não as compostas
    (cada piso parte a curva em arcos diferentes, que não se reconhecem como a mesma borda). As rotatórias caíam só 30 a
    70 %.
  - Informar centros exatos para todas as curvas das rotatórias: as curvas vêm de deslocamentos com cantos arredondados
    sobre polígonos já arredondados – não há um centro só.
- **Decisão:** `BoundaryFit.FitRings` (núcleo), usado pelo plugin para os pisos físicos de cada marca:
  - a borda comum (os mesmos vértices nos dois pisos, depois das junções em T e da unificação de trechos a até 5 mm) é
    ajustada uma vez, como trecho aberto com pontas fixas, e os dois pisos usam o mesmo resultado;
  - só entram pares cujos vértices comuns cobrem ≥ 95 % do contato entre eles;
  - piso que não passa nas conferências sai da topologia junto com os pares dele;
  - nos demais pisos, retas e circunferências da mesma borda viram uma só (consenso), sem mexer nos pisos já ajustados
    em conjunto.
- **Consequências:**
  - Pisos vizinhos da mesma marca não se sobrepõem por causa do ajuste (0 cm³ nas interseções, até 39 cm³ nas rotatórias
    da fixture).
  - Um vértice pode andar até 5 mm (trecho unificado) ou até a flecha da corda (ponta movida para o arco exato) –
    sempre dentro da tolerância do ajuste.
  - O ajuste de uma marca custa mais: cada piso ainda é ajustado sozinho, e os trechos comuns uma vez mais.
  - Pisos de marcas diferentes continuam ajustados separados; entre eles valem os centros exatos (D-015).

## D-017 – Componente da seção marcado na peça, separado da camada que divide os pisos
- **Contexto:** o quantitativo por componente (Fase VI) precisa saber o que cada peça é – meio-fio, sarjeta, calçada,
  gramado, ilha. A camada (`MarkingPiece.Layer`) já diz isso nas peças que vêm do perfil das vias, mas nas interseções,
  rotatórias e cul-de-sacs o meio-fio do canteiro e das ilhas, os miolos e a faixa galgável não têm camada. E a camada
  entra na chave dos pisos do Revit (`ElementPlan.FloorKey`): dar camada a essas peças mudaria quais pisos são criados e
  unidos – mudança no modelo que só um Autoteste confirmaria.
- **Decisão:** um campo à parte, `MarkingPiece.Component`, posto pelos geradores onde a camada não identifica a peça e
  lido só pelo quantitativo. A classificação é: marcação do gerador → camada → código da marca linear → pavimento pela
  cor → "outros" (o físico que não é da seção). A pintura não é componente.
- **Consequências:**
  - Os pisos do Revit não mudam (teste `MarcacaoDoComponente_NaoMudaOsPisosDoRevit`).
  - Gerador novo que crie peça física da seção sem camada nem marcação cai em "outros"; os testes de soma (todo físico
    gerado = soma dos componentes, sem "outros" nas vias, interseções e rotatórias sem paisagismo) acusam.
  - A extensão do meio-fio é medida na peça (fórmula da faixa), não no eixo da linha: inclui os recortes de rampas e
    travessias.

## D-018 – Volumes por estaca medidos só na superfície da própria via
- **Contexto:** a tabela por estaca (F7b) usa o método das seções sobre a superfície de projeto do projeto conjunto. Uma
  seção perpendicular à via atravessa também o nó de uma interseção, a rotatória ou outra via: somando as vias, o volume do
  nó entraria em mais de uma, e nenhuma bateria com a parte dela na malha.
- **Decisão:** a seção só conta os pontos cuja superfície de projeto é da via (`GradingResult.DesignAt` informa o dono da
  plataforma ou do talude – `SectionVolumes.ForOwner`). A malha continua a medida do quantitativo; as seções ficam ao lado,
  com a diferença em %, para conferir.
- **Consequências:** a tabela de cada via confere com a parte dela na malha (−1,4 % no teste em corte; com um nó cruzando,
  a seção no nó sai vazia). O volume dos nós não aparece na tabela por estaca (eles não têm estacas); está no quantitativo.

## D-019 – Dimensionamento da drenagem só com as fontes enviadas pelo usuário
- **Contexto:** até a Fase VIII o plugin não dimensionava a drenagem: não havia método nem valores com fonte, e a regra da
  missão é não inventar norma. Em 2026-10-22 o usuário enviou o *Manual de Drenagem Urbana – RMC* (SUDERHSA, 2002) e um
  memorial descritivo de drenagem (Miguelópolis/SP, 2019) e pediu o cálculo da vazão no memorial.
- **Decisão:** o cálculo usa só o que está nesses documentos: método racional, curvas IDF (Fendrich, Parigot de Souza;
  Barretos como escrita no memorial), Kirpich, Horner (o memorial cita; a expressão aplicada é a que reproduz a planilha
  dele), Manning a seção plena, sarjeta pelo exemplo 6.1, bocas de lobo pelas equações 6.5–6.7 e tabelas 6.3, 6.4 e 6.1.
  Cada predefinição e cada texto do memorial levam a fonte. O que as fontes deixam ao projetista é dito no código e no
  relatório (lâmina entre 1 e 2 vezes a abertura: vertedor; grelha entre 12 e 42 cm: o menor dos dois; tabela 6.3
  interpolada). O que nenhuma fonte dá (George Ribeiro, macrodrenagem) não é implementado.
- **Consequências:** a planilha do memorial modelo e os exemplos do manual são testes de regressão. Os valores do manual são
  de Curitiba e os do memorial, de Miguelópolis: a equação de chuva começa vazia (só geometria) até o usuário escolher uma
  predefinição ou digitar a do local, com a fonte. O projetista continua responsável pelo projeto.

## D-020 – Projeto de Drenagem gravado no projeto do Revit, não nas Configurações
- **Contexto:** os limites da rede (Fase VIII, primeira versão) estavam nas Configurações do usuário. Chuva, tempo de
  retorno e critérios dependem do local e do órgão de cada obra; nas Configurações, valeriam para todos os projetos do usuário.
- **Decisão:** `ProjetoDrenagem` (critérios, equação de chuva e identificação do memorial) fica nas Informações do projeto
  (armazenamento extensível, `DrainageDesignStore`), como os cenários de tráfego. Os limites deixam as Configurações (a
  versão anterior não chegou a ser entregue). Limite nulo = não conferir, e o nulo é gravado (sobrevive à reabertura).
- **Consequências:** cada projeto tem os seus critérios; um projeto novo começa sem equação de chuva. O Validar Modelo, o
  Relatório da Rede e o Memorial leem o mesmo registro.


## D-021 – O Prompt Master rege o desenvolvimento; as 15 fases ficam como referência
- **Contexto:** em 2026-10-23 o usuário enviou o *Prompt Master* (`docs/PROMPT_MASTER.md`). Ele amplia o escopo para uma
  plataforma de infraestrutura urbana no Revit 2027 (topografia, corredores, terraplenagem, pavimentação, drenagem,
  parcelamento, diagnósticos). O item 22 diz para não tratar o plano antigo de 15 fases como sequência rígida e para
  ordenar o trabalho pelo estado real, pelos riscos e pelas dependências, preservando os requisitos anteriores.
- **Decisão:**
  - A ordem do trabalho segue a prioridade do item 28.5: perda de dados, build, inconsistência geométrica, regressão
    (AUDIT §8.3).
  - O quadro das 15 fases continua no ROADMAP como mapa de rastreio: cada entrega diz a que fase pertence. Ele deixa de
    ser a fila.
  - As regras anteriores continuam valendo: nada inventado, nada declarado validado no Revit sem ter rodado no Revit, e uma
    DLL em `Releases/FaseNN` para cada entrega com mudança de código, preservando as anteriores. A numeração segue a fase
    a que a entrega pertence.
  - Não há reestruturação em bloco da arquitetura (item 2). As referências mútuas entre pastas do Core (R15) são
    desfeitas só quando a mudança já toca aquele código, e código novo não cria referência mútua nova.
- **Alternativa descartada:** reorganizar o Core em projetos separados por camada antes de continuar. Exigiria mover
  centenas de arquivos sem defeito a corrigir, e o item 2 pede justificativa para trocar a arquitetura.
- **Consequências:** a próxima entrega (Fase X) junta o que a auditoria pôs no topo: sobreposições classificadas, piso ×
  terreno, diagnóstico do terreno e validação com módulo, ação recomendada, "não verificável" e comparação com a anterior.

## D-022 – Plano único para os três prompts; entregas por etapa
- **Contexto:** em 2026-10-26 o usuário enviou o *Prompt Complementar Mestre de Motores* e o *Prompt de Interface*, para
  serem executados junto com o Prompt Master sem trabalho repetido. Os três pedem várias coisas iguais: auditoria,
  diagnóstico padronizado, pavimento por camadas, estados de atualização, configurações, desempenho e relatório final.
- **Decisão:**
  - **Plano:** um só, em `docs/PLANO_DE_ACAO.md`, com 14 etapas (0–13). Cada pedido repetido fica numa etapa só: o motor
    numa etapa, a tela dele numa etapa posterior.
  - **Ordem:** motores (1–8) antes das telas (9–12), como o Prompt de Interface pede (§1); desempenho e entrega (13) no
    fim.
  - **Entregas:**
    - cada etapa que muda código gera uma DLL em `Releases/EtapaNN` (`tools/release.sh E<n>`) e sobe o número do meio
      da versão: Etapa 1 = 2.8.0;
    - as entregas `FaseNN` anteriores ficam como estão;
    - o quadro das 15 fases continua como rastreio (D-021).
  - **Documento de estado:** o Prompt de Motores (§21) pede um, "ou equivalente já adotado". O equivalente é o plano mais
    os documentos de controle (AUDIT §8–§9, ROADMAP, KNOWN_ISSUES, TESTS, CHANGELOG). Não foi criado um documento novo.
- **Alternativa descartada:** seguir as três sequências (as 7 fases do Prompt de Motores, as 9 etapas do Prompt de
  Interface e as 15 fases antigas) em paralelo. Os mesmos itens seriam auditados e implementados duas ou três vezes.
- **Consequências:** o usuário recebe um aviso ao fim de cada etapa. O ROADMAP aponta para o plano em vez de manter outra
  lista de próximas etapas.

## D-023 – Estacas guardadas como distância no eixo; o estaqueamento só muda o que é mostrado
- **Contexto:** o Prompt de Motores (§7) pede estaqueamento e equações de estaca. O plugin sempre contou as estacas a partir
  de 0 no início do eixo, de 20 em 20 m. Essas estacas estão guardadas em dezenas de campos das definições (larguras por
  trecho, recuos, extensões, obras, PIVs) e são digitadas em vários formulários.
- **Decisão:** dentro das definições, toda estaca continua sendo a **distância ao longo do eixo a partir do início**. O
  `Estaqueamento` da via (estaca inicial, intervalo, equações "estaca atrás = estaca à frente") é guardado no pavimento,
  é opcional e só converte essa distância na estaca mostrada. Os relatórios desta via o usam desde a Etapa 4. Nulo = o
  padrão de sempre.
- **Alternativa descartada:** guardar o valor da estaca nos campos. Exigiria migrar todas as definições existentes e
  regravar cada vez que uma equação muda – risco de perda de dados (R1) sem ganho para o cálculo.
- **Consequências:** projetos antigos abrem iguais (campo ausente = padrão). Os formulários que pedem estacas continuam
  pedindo a distância desde o início do eixo até a tela da via ser refeita (Etapa 11; K20).

## D-024 – Estrutura do pavimento na via; registro da terraplenagem no projeto
- **Contexto:** o Prompt Master (§9, §10) e o de Motores (§8, §9) pedem estrutura de pavimento por camadas usada no cálculo
  e o estado "desatualizado" dos resultados. O corte, a terraplenagem e o quantitativo usavam profundidades diferentes, e
  nada dizia que o Toposolid não acompanhava mais o projeto.
- **Decisão:**
  - **Estrutura:** guardada no pavimento da via (`RoadPavementDefinition.Estrutura`), só as camadas **sob** o
    revestimento. O revestimento continua sendo a espessura do piso da pista, para não haver dois valores para a mesma
    coisa. A profundidade (revestimento + camadas) é o fundo da caixa; nó = a via ligada mais funda.
  - **Registro:** nas Informações do projeto (armazenamento estendido, esquema próprio), gravado dentro da transação da
    terraplenagem. A assinatura é o SHA-256 do JSON da definição, sem a linha do terreno natural das obras.
  - **V15:** compara o registro com as marcas atuais. Sem registro, "não verificado", nunca "em dia".
- **Alternativas descartadas:**
  - estrutura por tipo de via ou num catálogo do projeto: a Etapa 11 (gerenciador de pavimentos) pode acrescentar
    modelos; por ora cada via tem a sua;
  - registro num arquivo fora do modelo, como o histórico da validação: o estado do terreno é do modelo e precisa
    acompanhar o Desfazer;
  - assinatura de todas as entradas (Toposolid, opções): o terreno original e as opções mudam só pela própria
    Terraplenagem, que regrava o registro.
- **Consequências:** projetos antigos abrem iguais; a primeira Terraplenagem na 2.11 grava o registro. Até lá a V15 diz
  "não verificado".

## D-025 – Área de contribuição lida da via por escolha do usuário, nunca em silêncio
- **Contexto:** RT-1.5 e o Prompt Master (§11) pedem a drenagem integrada às vias e ao terreno. O cálculo hidráulico usava
  só a área e a declividade digitadas em cada captação (K5).
- **Decisão:** o núcleo lê da via a sarjeta (greide) e a área que escoa até a captação (geometria da via), mas o cálculo
  continua usando o que está gravado no dispositivo. O comando Ler do Modelo mostra o que leu e grava só por escolha:
  preencher os vazios ou substituir tudo. O V13 aponta a divergência entre o informado e o greide como informação.
- **Alternativas descartadas:**
  - usar a área da via sempre que o campo estivesse vazio: o resultado mudaria sem o usuário ver, e a área da via não
    tem os lotes;
  - medir a área sobre o Toposolid (bacias pelo terreno): exigiria a triangulação inteira por captação e não separa a
    água da via da que vem dos lotes – fica para quando houver regra do usuário (K5).
- **Consequências:** projetos antigos calculam igual. A área lida é da via; o projetista soma lotes e áreas externas.

## D-026 – Estado dos resultados pela comparação do que foi lido com o que o modelo diz agora
- **Contexto:** o Prompt Master (§14) pede mostrar o que foi atualizado e o que ficou desatualizado, preservando as
  edições manuais. A atualização automática (DMU) refaz geometria em silêncio; os resultados derivados guardados no modelo
  (terreno moldado, valores lidos da via) não tinham estado.
- **Decisão:**
  - cada resultado derivado guarda o que o gerou: o registro da terraplenagem (D-024) e, nas captações, a leitura feita
    (`ContribuicaoLida`);
  - o estado é calculado na hora, comparando o guardado com o que o modelo dá agora – sem marcar nada como "sujo" a cada
    evento;
  - campo igual ao lido = do modelo; diferente = editado à mão, que a atualização não toca.
- **Alternativa descartada:** marcar dependentes como desatualizados no updater a cada mudança. O updater não roda neste
  ambiente (K17), uma marcação perdida deixaria o estado errado para sempre, e a comparação dá o mesmo resultado sem
  guardar estado extra.
- **Consequências:** o estado vale também para mudanças que não passam pelo plugin (por exemplo, outra captação colocada
  acima). Cobre a terraplenagem e as captações; outros resultados derivados entram quando guardarem o que os gerou.

## D-027 – Faixa de opções por tema em duas guias; o comando e o botão com a mesma regra de disponibilidade
- **Contexto:** o Prompt de Interface (§7) pede a faixa organizada pelo fluxo de trabalho, nos painéis Projeto, Terreno,
  Sistema Viário, Interseções, Terraplenagem, Pavimentação, Drenagem, Urbanismo, Análise, Documentação e Sistema, sem
  perder nem duplicar comandos, com dicas curtas e estados coerentes. A faixa da 2.14 era por tipo de elemento, em duas
  guias (SinalizaBIM e SinalizaBIM Infra). O usuário pediu a reorganização completa (2026-10-26: "mude assim como foi
  pedido").
- **Decisão:**
  - a guia *SinalizaBIM* tem exatamente os 11 painéis do §7, nessa ordem;
  - a sinalização – horizontal, vertical e segurança viária, 23 botões – fica numa guia própria, *SinalizaBIM
    Sinalização*: o §7 não tem painel para ela, e dentro de um painel temático ela esconderia os demais;
  - cada comando aparece uma vez e com o nome de antes; ferramentas afins num botão com seta; ícone próprio por botão;
  - dica curta (até 120 caracteres) e o texto completo na descrição longa;
  - o atributo `NoDocumentRequired` diz se o comando roda sem projeto, e o mesmo atributo escolhe a disponibilidade do
    botão: habilitado só com um projeto .rvt ativo, salvo Configurações, Catálogo e Normas / Sobre.
- **Alternativas descartadas:**
  - manter a faixa por tipo de elemento e mudar só o visual: o usuário recusou;
  - pôr a sinalização dentro de Sistema Viário: sete grupos a mais num painel que já tem sete;
  - disponibilidade escrita à parte para cada botão: duas fontes para a mesma regra divergiriam.
- **Consequências:** quem usava a 2.14 encontra cada ferramenta pelo mapa antigo → novo do MANUAL. A guia principal tem
  36 colunas: em tela estreita o Revit recolhe os painéis em botões com seta (K23). A cobertura, a ordem dos painéis, os
  ícones e as dicas são conferidos em teste (`Etapa09InterfaceTests`).

## D-028 – Central do Projeto e painel contextual: só dados reais, só leitura, comandos pelo mesmo caminho do botão
- **Contexto:** o Prompt de Interface (§8, §9, §20) pede uma Central com indicadores e a situação do projeto e um painel com as
  propriedades do elemento selecionado, sem inventar dado, data ou estado, sem zero para o desconhecido, sem "validado" por
  uma operação concluída e sem alteração em lote sem validação.
- **Decisão:**
  - o núcleo calcula o resumo e as propriedades (testáveis sem o Revit); a camada Revit lê o modelo e mostra;
  - cada indicador tem a situação do valor – calculado, não calculado, indisponível, desatualizado ou inválido – e o valor
    desconhecido é "—"; o que é caro (medir os pisos do pavimento) só é calculado quando o usuário pede;
  - a situação vem da última validação gravada (com a data) e do estado dos resultados (D-026); a situação geral é o pior
    nível, dito pelo que se sabe;
  - o painel só mostra: o evento de seleção só lê, como a API exige; várias marcas mostram só o que é igual em todas, e não há
    edição em lote;
  - a Central e o painel abrem os comandos existentes por `CommandBase.Invoke`, com as conferências e mensagens do botão; a
    Central depois de fechar, dentro do comando dela; o painel por `ExternalEvent`;
  - sem "operações recentes": o plugin não guarda histórico confiável delas.
- **Alternativas descartadas:**
  - `PostCommand` com o identificador do botão: o formato do identificador de um botão dentro de um botão com seta não é
    documentado, e um engano falharia em silêncio;
  - rodar a validação inteira ao abrir a Central: lenta em projeto grande; a última validação gravada, com a data, já diz o
    estado conhecido e o atalho refaz;
  - editar propriedades no painel: duplicaria as janelas de edição e abriria espaço para alteração em lote sem validação.
- **Consequências:** a Central abre rápido e nunca mostra um valor que não saiba de onde veio; o painel não altera nada. O que
  não foi visto no Revit está em K24.

