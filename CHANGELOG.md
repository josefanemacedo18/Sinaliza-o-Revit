# CHANGELOG

Formato: data, etapa, mudanças, testes. O histórico anterior a 2026-10-19 está no `git log`: 118 commits, rodadas A a LX,
cada uma com seus testes.

## 2026-10-28 – Etapa 19: componentes da via por trecho e taludes por trecho (versão 2.24.0)

Itens 5 e 17 do [plano 2](docs/PLANO_DE_ACAO_2.md) (D-037). Entrega: `Releases/Etapa19` (não executada no Revit 2027).

### Componentes por trecho (item 5)
- **Componentes por Trecho** (Sistema Viário › Ajustar Via ▾, e o botão na janela da via): acrescentar, tirar ou mudar a
  largura de **qualquer elemento** de um lado da seção (faixa, ciclofaixa, estacionamento, acostamento, faixa de segurança,
  canteiro físico ou pintado, faixa de caminhada, calçada) entre as estacas **A** e **B**, com **transição** antes de A e
  depois de B (retas, em S ou degrau). Em via já modelada (a via é refeita pela seção gravada) ou nova (item 17).
- A seção se deforma a partir do eixo: os elementos de fora se deslocam; a sarjeta, o meio-fio e a linha de bordo vão com o
  lado de fora. As marcas que só a seção do trecho tem (linha da ciclofaixa, meios-fios e grama do canteiro, vagas, bordo
  novo) saem **só entre A e B**; as que ela não tem são **interrompidas** (ao tirar, desde o início da transição – o elemento
  afunila até sumir); símbolos, segregadores e zebrados por recorte sem dono, refeito com a via.
- Pavimento: o canteiro acrescentado recorta a pista **só no trecho**; o tirado a fecha no trecho; as interseções e
  rotatórias leem a seção da estaca onde ficam (`RoadPavementDefinition.Local`).
- Trechos gravados podem ser **divididos** numa estaca ou **removidos**; trechos sobrepostos do mesmo lado e trechos cujo
  elemento mudou de tipo na seção são ignorados com aviso.

### Taludes por lado e por trecho (item 5)
- **Taludes por Trecho** (Terraplenagem › Contenções ▾): por lado e por trecho, **talude fixo** (corte e aterro, com
  **largura máxima** opcional – onde não alcança o terreno, o terreno fica natural além do limite, face quase vertical) ou
  **largura fixa**, com transições; a seção numa estaca mostra o resultado antes de aplicar.
- A Terraplenagem usa inclinação e limite por vértice da borda; as seções e volumes do Perfil da Via usam os mesmos taludes;
  o Perfil da Via mantém os trechos ao regravar o greide.

### Correções achadas no caminho
- Depois de gravar o greide (Perfil da Via, Relevo da via nova), o terreno era refeito com o pavimento lido **antes** da
  gravação – com o greide anterior; agora recebe o pavimento gravado (achado pela leitura do código, NÃO VALIDADO NO REVIT).
- A cópia da seção (`RoadSetup.Clone`) dividia a lista de avisos com a original: gerar uma prévia apagava os avisos.
- O vão do pavimento numa via de largura variável acompanha as duas bordas (antes só o centro se deslocava).

### Testes
- `Etapa19ComponentesTests` (13): ciclofaixa acrescentada (bordos, transições, linha nova só no trecho, bordo interrompido,
  sarjeta e calçada deslocadas, pavimento), faixa tirada (afunila, divisória interrompida, bordo contínuo), largura de uma
  faixa, canteiro acrescentado e tirado (vãos do pavimento, seção na estaca), ciclofaixa tirada com símbolos e segregadores,
  transições retas e em S, trechos inválidos, dividir, via refeita pela seção gravada (e num eixo em curva), edição à mão
  preservada, sem componentes nada muda, ligação na faixa e na janela.
- `Etapa19TaludesTests` (6): talude por lado e por trecho com transições (pé, superfície e volume), limite com face, largura
  fixa em aterro e corte, seção do Perfil da Via com os mesmos taludes, greide com os trechos (cópia e JSON), ligação.
- NÃO EXECUTADO NO REVIT (K33).

## 2026-10-28 – Etapa 18: seção por estaca, PIVs editáveis e diagrama de massas (versão 2.23.0)

Itens 4 e 21 do [plano 2](docs/PLANO_DE_ACAO_2.md) (D-036). Entrega: `Releases/Etapa18` (a janela não foi aberta no Revit 2027).

### Perfil da via
- **Gráfico escolhido:** perfil longitudinal, **seção transversal na estaca** (estaca digitada em n+resto ou metros, setas
  anterior/próxima, exagero vertical; plataforma no greide com o abaulamento e taludes de corte e aterro até o terreno; corte
  e aterro com as áreas), **áreas de corte e aterro por estaca** e **diagrama de massas** (Brückner, com o fator de
  homogeneização, os pontos de passagem e o saldo: bota-fora ou empréstimo).
- **Trecho de estacas:** os volumes de corte e aterro de um trecho, com as áreas das pontas interpoladas.
- **PIVs com o perfil à vista:** ler, acrescentar, mover (estaca e cota, ou a cota do terreno) e remover um PIV por botões; o
  texto, o gráfico, as áreas e os volumes se refazem na hora.
- Os volumes da janela são pela média das áreas das seções da **seção-tipo** (antes de aplicar) – estimativa para comparar
  greides; o exato continua vindo da Terraplenagem.

### Núcleo
- `EstacasTerraplenagem` (seção na estaca, volumes, Brückner, pontos de passagem, edição de PIV) e `EarthworkCharts`.
- A média das áreas é uma conta só (`SectionVolumes.FromSections`), usada também pelas seções da Terraplenagem (mesmo
  resultado); `SectionVolumes.Trecho`.

### Testes
- `Etapa18EstacasTests` (10): área do trapézio em aterro e corte sobre terreno plano, terreno inclinado com corte e aterro na
  mesma seção, volumes por média das áreas (à mão e iguais ao método da Terraplenagem), trecho de estacas, Brückner à mão,
  edição de PIV com o volume refeito, gráficos, a ligação na janela.
- NÃO EXECUTADO NO REVIT (K32).

## 2026-10-27 – Etapa 17: rampas como Pisos e materiais realistas (versão 2.22.0)

Itens 2 e 1 do [plano 2](docs/PLANO_DE_ACAO_2.md) (D-035). Entrega: `Releases/Etapa17` (não executada no Revit 2027).

### Rampas como Pisos (item 2)
- Cada parte de concreto da rampa e da guia rebaixada – rampa, abas, platô e rampas laterais do rebaixamento total, patamar,
  cantos nivelados – vira um **Piso do Revit** de **fundo plano e topo inclinado**: tipo *SV - {material} rampa
  {espessura}* com a camada de espessura variável (`CompoundStructure.VariableLayerIndex`, conferido com
  `CanLayerBeVariable`), criado plano e levado à cota de cada vértice pela edição de forma. O fundo fica 5 cm abaixo da base
  da rampa, sobre a plataforma do terreno da Etapa 15. As rampas entram nas tabelas de pisos e na conferência piso × terreno;
  as placas táteis continuam sólidos. Parte recusada pelo Revit fica forma direta, com aviso.
- **Configurações → Rampas e guias rebaixadas também como Piso** (ligada; desligada = forma direta, como até a 2.21).
- **Recorte da rampa:** a ponta das abas, junto ao meio-fio, passava ~4 cm do recorte e sobrava meio-fio sob ela (0,036 m²
  por aba – com pisos, seriam pisos sobrepostos). O lado inclinado do recorte segue agora a hipotenusa da aba.

### Aparência realista dos materiais (item 1)
- Asfalto, concreto, bloquete, terra, brita, grama, folhagem, metal, vidro, madeira e água recebem o **ativo de aparência
  da biblioteca instalada com o Revit** (`Application.GetAssets`, `AppearanceAssetElement.Create`), escolhido pelo título em
  inglês ou português, na escala do próprio ativo. A **cor do material não muda** e o Sombreado/Cores consistentes
  continua com ela (`UseRenderAppearanceForShading` = falso). Material que já tem aparência não é tocado; pinturas ficam na
  cor lisa; sem ativo que sirva, cor lisa (log).
- **Configurações → Materiais com a aparência realista da biblioteca do Revit** (ligada).

### Testes
- `Etapa17PisosMateriaisTests` (20): partes da rampa com e sem cantos e com desnível (um piso por corpo, topo plano, fundo
  5 cm abaixo, espessura do tipo, inclinação, sem sobreposição e dentro do recorte), rebaixamento total, rampa no relevo;
  escolha do ativo para cada material, pinturas sem ativo, telhas e blocos de fora, título em português; as duas
  configurações registradas; a ligação no plugin.
- NÃO EXECUTADO NO REVIT (K31).

## 2026-10-27 – Etapa 16: esquinas – chanfro de 1,50 m, piso tátil e extensão em chanfro (versão 2.21.0)

Itens 19, 20 e 12 do [plano 2](docs/PLANO_DE_ACAO_2.md) (D-034). Entrega: `Releases/Etapa16` (não executada no Revit 2027).

### Chanfro de 1,50 m por padrão, medido sobre os limites (item 19, imagem 4)
- **Medida pelos catetos:** o chanfro da quina pode ser medido sobre cada limite do lote, a partir do encontro dos
  alinhamentos ("mínimo de 1,50 m em cada lado, medido sobre os limites do terreno na esquina"), além da face (hipotenusa –
  a medida de antes, que continua como opção). Medida própria por esquina, na forma da quadra; a janela mostra os catetos e
  a face de cada quina.
- **Quadras novas** (Loteamento novo e Reconhecer quadras) nascem com o chanfro ligado, **1,50 m em cada lado** – padrão do
  loteamento (*Chanfro das quadras novas*; 0 = sem chanfro), **informado pelo usuário** [a confirmar na lei municipal] – e as
  interseções em volta são refeitas para a calçada ocupar os chanfros. Lotes por Linhas segue o limite desenhado.
- **Projetos salvos:** as quadras gravadas antes abrem iguais (medida pela face, chanfro como estava); os loteamentos
  antigos passam a dar 1,50 m às quadras **novas** deles.

### Piso tátil da esquina paralelo ao alinhamento predial (item 20, imagem 1)
- **Causa:** a faixa direcional da esquina seguia a curva do meio-fio, deslocada para dentro da calçada: sem mudança de
  direção marcada (nenhum alerta) e, nas esquinas de raio grande com calçada estreita, a faixa passava da quina do lote.
- **Agora:** paralela ao alinhamento predial, na mesma distância do lote que nos trechos retos, até a quina viva ou
  **paralela ao chanfro** do lote de esquina, com **alerta em cada mudança de direção** (NBR 16537, 7.4.3). Canto a menos de
  0,60 m lisos da pista (7.8.2 a): a faixa corta a quina com um trecho reto; quina estreita: folgas equilibradas, sem tocar o
  meio-fio nem o lote. A interseção tem a opção de voltar ao traçado pela curva do meio-fio.
- **Faixa junto à faixa de acesso** (opção da via, imagem 1: meio-fio 15, mobiliário 70, percurso 145, faixa tátil 30,
  acesso 40 cm – informado pelo usuário), quando sobram 1,20 m de faixa livre; desmarcada = posição da 7.8.2.
- **Ramais até as entradas de pedestres dos lotes:** NÃO FEITO – o plugin não conhece as entradas dos lotes (Etapa 23).
- Projetos salvos: o traçado novo vale na próxima atualização de cada interseção.

### Esquina da extensão de calçada em chanfro e rampas alinhadas (item 12, imagem 2)
- **Esquina da extensão em chanfro reto** (recuo em cada face a partir do encontro das faces deslocadas), geral e por
  esquina (0 = curva com o raio); recuo que faria o chanfro entrar na calçada é reduzido, com aviso. Sem centro de arco no
  chanfro (o Revit faz o trecho reto).
- As **rampas no eixo das travessias** foram conferidas com e sem extensão, com curva e com chanfro.
- O resto da imagem 2 já existia e fica como estava: rampas a 8,33 % e abas a 10 % (NBR 9050), faixa de pedestres de 4,0 m
  (largura configurável) com as barras do catálogo (MBST), canteiros com árvores nas extensões.

### Testes
- `Etapa16EsquinasTests` (18): catetos de 1,50 m em esquinas de 60°, 90° e 120° (área e face), medida por esquina e pela
  face, quadras e loteamentos antigos abrem iguais, a calçada da interseção ocupa os chanfros de 1,50 m; faixa tátil na quina
  viva (vértice a 90° com alerta), paralela ao chanfro (dois de 45°), corte da quina com a folga e na quina estreita, opção
  pela curva, faixa junto ao acesso e a posição da norma quando não cabe; rampas alinhadas às travessias, extensão em
  chanfro em esquinas de 70° e 90°, ajuste por esquina e recuo reduzido; a ligação nas janelas e nos comandos.
- NÃO EXECUTADO NO REVIT (K29, K30).

## 2026-10-27 – Etapa 15: defeitos críticos – travessias em trechos curtos e terreno sobre a via (versão 2.20.0)

Primeira entrega do [plano 2](docs/PLANO_DE_ACAO_2.md) ("InfraWorks dentro do Revit", D-032). A Etapa 14 (estudo do
InfraWorks pela ajuda oficial, lacunas por matéria e o plano) foi só de documentos, sem DLL. Entrega: `Releases/Etapa15`
(não executada no Revit 2027).

### Travessias espaçadas em trechos curtos (item 18)
- **Causa:** cada interseção fazia travessia, rampas e extensões do lado contínuo em todos os ramos, sem olhar o nó vizinho:
  um trecho de 30 m entre duas esquinas ganhava duas faixas de pedestres e quatro rampas.
- **Agora:** quando o trecho até a interseção ou rotatória vizinha na mesma via é menor que o **trecho mínimo** da
  interseção (50 m, padrão do plugin, sem norma citada), só um dos dois nós faz a travessia sobre essa via – o de mais vias,
  depois o de controle mais forte (rotatória sempre fica com a dela); no empate, o de Id menor, a mesma escolha vista dos
  dois lados.
- O ajuste do ramo ("Faixa de pedestres neste ramo": Sim/Não) vale mais que a regra; **trecho mínimo 0** volta ao
  comportamento antigo; travessias desligadas na interseção são o **modo manual**. A janela da interseção tem o campo do
  trecho mínimo e diz que ramo ficou sem travessia e por quê.
- Projetos salvos: a regra vale na próxima atualização de cada interseção.

### Terreno abaixo das rampas e conferência nos nós (item 13)
- **Causa:** sob a calçada o terreno fica no fundo da laje, que é também o fundo da rampa; o pé da rampa tem milímetros (o
  rebaixamento total, uma placa de 5 mm) e a triangulação do Toposolid pode passar até 2 cm acima do projeto – o terreno
  aparecia por cima. As rampas não entravam na terraplenagem: nem as da via nem as das interseções, cuja geometria tira o
  corpo delas da calçada.
- **Agora:** cada rampa tem plataforma própria na terraplenagem, sem talude, **5 cm abaixo do fundo** (padrão do plugin),
  fora da pista. Rampas das interseções e rotatórias entram com o nó; as da via, as do grupo dela e as soltas sobre a calçada
  dela. Vale na próxima Terraplenagem.
- **Autoteste:** a verificação de cada via passa a conferir as rampas (o terreno não pode passar do topo) e os pisos das
  interseções e rotatórias que ela toca, cada nó uma vez – antes os nós eram pulados.

### Testes
- `Etapa15GeracaoTests` (5): trecho curto com uma travessia e um par de rampas, trecho longo com as duas, escolha pelo peso
  vista dos dois lados, modo manual, rotatória e a ligação no plugin.
- `Etapa15TerrenoTests` (4): o pé da rampa sem folga cabia no erro da triangulação e com folga não; a plataforma da rampa
  vence a da via e sai da pista; sem rampas nada muda; a ligação na terraplenagem e no Autoteste.
- NÃO EXECUTADO NO REVIT (K28).

## 2026-10-27 – Etapa 13: desempenho, validação e entrega (versão 2.19.0)

Etapa 13, a última do plano unificado (Master §21, §27; Motores §18, Fase 7; Interface §21, §32). Entrega:
`Releases/Etapa13` (não executada no Revit 2027). Relatório técnico final em [docs/RELATORIO_TECNICO.md](docs/RELATORIO_TECNICO.md)
(D-031).

### Medir antes de otimizar (K14, K15)
- **`tools/Desempenho`** (programa de console, fora do plugin): mede o núcleo, sem o Revit, em projetos sintéticos de 4, 16 e
  64 vias e no piso tátil longo. Saída em `docs/evidencias/2026-10-27-desempenho-nucleo.txt`, com a análise dos tempos por
  trecho do 4º Autoteste completo.
- **Terraplenagem linear no tamanho do projeto:** os contornos dos taludes procuravam o ponto mais próximo percorrendo todos
  os lados de todas as plataformas – quadrático (10,8 s dos 13 s com 64 vias). Um índice em grade (`RingIndex`) dá **o mesmo
  ponto**: mesmo SHA-256 de todos os pontos de projeto, dos pés dos taludes, dos volumes e da área nos três tamanhos. 64 vias:
  12,1–14,6 s → 2,9–4,0 s.
- **K14** (piso tátil de alerta de 30 m, 171 s no 3º Autoteste): o núcleo gera os 3 120 relevos em 2,3 ms; o tempo é do Revit,
  que cria um sólido por relevo. **K15**: a leitura das marcas no documento é ~28 % do 4º Autoteste (34,5 milhões de
  elementos visitados em 11 350 varreduras), o custo está em visitar os elementos, não no JSON. Nenhum dos dois foi mudado:
  as duas correções mexem no que o Revit cria ou lê e precisam ser vistas no Revit (KNOWN_ISSUES, com o plano e a medida).

### Progresso e cancelamento (Interface §21)
- **Atualizar Todas** e **Quantitativos** abrem uma janela de progresso: a etapa, o que está sendo feito (a marca), quantos de
  quantos e **Cancelar**. A barra só aparece com o total conhecido; sem total, o indicador de atividade, sem percentual
  inventado. Atualizações limitadas a uma a cada 100 ms, mais as trocas de etapa.
- **Cancelar só em ponto seguro:** em Atualizar Todas, antes de gravar – a transação é desfeita e o modelo fica como estava
  ("cancelado pelo usuário – nada foi alterado"); depois da gravação, os recortes de sobreposição terminam e o botão diz que
  não dá mais para cancelar. Nos Quantitativos, que só leem, cancelar fecha sem resultado. Fechar a janela é pedir para
  cancelar.
- A operação continua no fio do Revit (a API não aceita outro); a janela principal do Revit fica desabilitada enquanto ela
  roda e volta ao fechar, mesmo com erro. O Autoteste chama as mesmas rotinas sem janela.

### Relatório do tráfego reprodutível (F11)
- Seção **9. Análise, simulação e animação – parâmetros para reproduzir** no relatório do Simulador de Tráfego: a análise
  (HCM) é determinística; a microssimulação é estocástica, com as **sementes** (chegadas e perfis de motorista), o passo, o
  aquecimento e o tempo medido; a animação é a reprodução dos quadros da mesma rodada e nenhum número vem dela; as
  alternativas usam três sementes. Todos os parâmetros do cenário e uma **assinatura** (SHA-256) da rede e dos parâmetros.
  A linha de resumo diz a semente.

### Fluxo integrado no Autoteste
- Opção **Fluxo integrado** (sempre no Completo): duas vias que se cruzam sobre o terreno de teste → interseção →
  terraplenagem → grelha na sarjeta → quantitativos → validação. Cada etapa confere o que a anterior fez: o registro da
  terraplenagem tem as vias e o nó; a grelha é lida na sarjeta da via certa e a terraplenagem acusa a caixa nova; o resumo
  por via traz a terraplenagem da própria via; editar o talude da via faz a V15 e o impacto da edição apontarem a via, e a
  terraplenagem refeita limpa tudo. As cinco etapas vão juntas (o modo rápido não corta o fluxo).
- O Toposolid de teste passou a um método comum (`TerrenoDeTeste`), usado pela Topografia e pelo fluxo.

### Testes
- `Etapa13DesempenhoTests` (3): o índice dá o mesmo ponto que a busca em todos (4 000 pontos, perto e longe), a ligação na
  terraplenagem e os volumes por via somando o total.
- `Etapa13TrafegoTests` (3): a mesma semente dá os mesmos números e o mesmo relatório; a seção 9; a assinatura muda com a
  rede e os parâmetros.
- `Etapa13ProgressoTests` (5): fração só com total, contagem, limitador (relógio injetado), cancelamento só no ponto seguro e
  a ligação nos comandos e na janela.
- `Etapa13IntegradoTests` (2): a sequência do fluxo no núcleo e a ligação da etapa no Autoteste.
- Janela de progresso e fluxo integrado NÃO EXECUTADOS NO REVIT (K27).

## 2026-10-27 – Etapa 12: qualidade, quantitativos e configurações (versão 2.18.0)

Etapa 12 do plano unificado (Prompt de Interface §17–§19, §26; Master §13, §17; Motores §17). Entrega: `Releases/Etapa12`
(não executada no Revit 2027). O conteúdo das telas vem do núcleo e é testado; as janelas só mostram (D-029, D-030).

### Central de Qualidade (§17)
- **Validar Modelo** abre a Central de Qualidade no lugar da caixa de diálogo (`CentralQualidade`, no núcleo; os mesmos
  verificadores V01–V17, nenhum trocado por conferência superficial). Tabela com gravidade, categoria, código, elementos (o
  nome das marcas), descrição, **estado diante da validação anterior** (novo, persistente, primeira validação, não
  verificado) e ação recomendada; filtros por categoria, gravidade, estado e texto, ordem escolhida, detalhes do achado,
  **Selecionar no modelo** (com a vista aproximada) e **Exportar CSV** com a data e a versão.
- **Por categoria** com todas as categorias que têm verificador (zero = conferida sem achado); **Resolvidos** desde a
  anterior; **Estado dos resultados**.
- **Autoteste (testes do plugin)**, separado do diagnóstico do modelo: o último relatório lido do TXT (`RelatorioAutoteste`)
  – executadas, aprovadas, com aviso, com erro, não executadas, duração, versões, projeto e as mensagens; conferido com os
  seis relatórios reais em `docs/evidencias`.
- O relatório TXT e o histórico continuam como antes; o comando continua somente leitura. Não há botão "validar de novo": a
  janela é modal e o modelo não muda com ela aberta – roda-se o comando outra vez.

### Quantitativos com a origem de cada número (§18)
- **Origem e método em cada linha**: as marcas de origem (nome e Id), os pisos desenhados à mão e as famílias do usuário, e
  o método de cálculo com a configuração usada (material, taxa do catálogo, fator). Coluna *Origem*, detalhes da linha,
  pesquisa pelo elemento e **Selecionar no modelo**.
- **Aba Origem dos valores** (`OrigemQuantitativos`): calculado agora (data e versão), quantas marcas e elementos do
  usuário, material padrão, de onde vem o fator de homogeneização e se o terreno do modelo confere com os volumes (estado da
  terraplenagem); o que pede atenção também no alto.
- **Por via** (`QuantitativosPorVia`): eixo, pavimento, calçada e meio-fio do grupo da via, camadas e corte e aterro da parte
  da via. **Quadras e lotes** (`QuantitativosParcelamento`): o mesmo resumo dos Quadros do Loteamento.
- CSV e planilha com as colunas *Origem* e *Método*, a origem no cabeçalho e os blocos novos. As vias têm os mesmos nomes em
  todas as telas (`NomesDasMarcas`: *Via 1*, *Via 2*… – inclusive na Central de Qualidade).

### Gerenciador de configurações e perfis (§19, §26)
- **Configurações em abas** (`RegistroConfiguracoes`): do usuário (este computador), do projeto e de cada elemento (lidas do
  modelo, com o comando que as edita), perfis reutilizáveis e onde fica cada configuração – grupo, lugar, valor e o que
  afeta. Um teste garante que toda configuração nova do usuário aparece no gerenciador.
- **Validação antes de aplicar**: faixas, valores não numéricos, rampa mínima maior que a máxima, material fora do catálogo,
  catálogo do usuário inexistente, limites sem fonte. **O que muda ao salvar**, de quanto para quanto e para quem; nada é
  alterado nos elementos existentes sem um comando.
- **Perfis reutilizáveis** rotulados como *exemplo do plugin* (as seções-tipo, que não representam norma), *do usuário* ou
  *referência citada pelo usuário* (não conferida pelo plugin); **perfis de limites geométricos** com nome e fonte. O plugin
  não traz perfis de limites nem de estrutura.
- **Tamanho das janelas** das telas grandes lembrado entre sessões (limitado à tela; sem tamanho guardado, o padrão), com
  *Restaurar o tamanho das janelas*. Preferência do usuário, fora do modelo.

### Correções
- **Consumo de tinta na unidade do total:** o consumo saía com a unidade da taxa do catálogo (*6,60 L/m²* em vez de
  *6,60 L*), e os subtotais, os resumos, o cartão *Tinta estimada* e as exportações somavam litros com quilos quando havia
  materiais diferentes. O total vai em L, kg ou m² (`QuantityRow.UnidadeDoTotal`) e as somas são por unidade
  (`QuantityCalculator.Consumo`), com testes.
- **Configurações salvas pela metade:** salvar com um campo inválido deixava os campos lidos antes dele aplicados na sessão
  (sem gravar). A janela edita uma cópia (`PluginSettings.Clone`) e só aplica depois de validar tudo (`CopiarDe`).

### Testes
- `Etapa12QualidadeTests` (19): estados, chaves repetidas, ordem, filtros, contagens, CSV, leitura dos relatórios reais do
  Autoteste e as fontes da janela e do comando.
- `Etapa12QuantitativosTests` (18): unidade do consumo e somas por unidade, origem e método (marcas, pisos do usuário,
  famílias, terraplenagem), por via, quadras e lotes, origem dos valores, exportações e as fontes da janela e do comando.
- `Etapa12ConfiguracoesTests` (11): registro, cobertura de toda configuração, impacto, validação, projeto e elementos,
  perfis, persistência (sem arquivo, versão anterior, corrompido), cópia e tamanho das janelas.
- Telas NÃO VISTAS NO REVIT (K26).

## 2026-10-27 – Etapa 11: telas dos módulos (versão 2.17.0)

Etapa 11 do plano unificado (Prompt de Interface §10–§16; Master §7, §13). Entrega: `Releases/Etapa11` (não executada no
Revit 2027). O conteúdo de cada tela é montado no núcleo e testado; as janelas só mostram (D-029).

### Janela da via em nove seções (§10, §11)
- **Abas:** Identificação, Geometria, Perfil, Seção, Pavimento, Terraplenagem, Drenagem, Validação e Resumo; cada uma rola nas
  duas direções. Botões **Validar** e **Resumo** antes de Aplicar.
- **Conteúdo** (`FormularioVia`, no núcleo): na via existente, o greide, o estaqueamento, a estrutura do pavimento, o estado na
  terraplenagem, as captações na sarjeta e os trechos ligados; na via nova, o que acontece ao aplicar. A Validação confere o
  traçado e o greide contra os limites informados (V12, `ModelValidator.Tracado`); o Resumo diz o que Aplicar refaz e o que
  fica a refazer (terraplenagem, captações lidas).

### Gerenciador de estruturas de pavimento (§15)
- **Estrutura do Pavimento** abre uma tabela de camadas (nome, material, espessura, função, estado e se o material existe no
  Revit), com adicionar, remover e reordenar, o corte esquemático e o quantitativo previsto só com a área medida da via
  (`GerenciadorPavimento`).
- **Modelos do usuário:** guardar, usar e excluir estruturas; ficam nas configurações do plugin.

### Rede de drenagem (§16)
- **Relatório da Rede** abre uma janela com as partes separadas (`RedeDrenagemTela`): consistência geométrica, conectividade,
  verificação de parâmetros, cálculo hidráulico, dimensionamento e drenagem × vias e terreno; mais o perfil de cada ramo
  (desenhado) e os dispositivos.
- **Sem cálculo inventado:** sem equação de chuva, o cálculo hidráulico e o dimensionamento aparecem como não feitos;
  critério não informado aparece como não conferido; tubos a seção plena (lâmina e remanso não calculados).
- **Dimensionamento:** diâmetro do modelo × calculado (aumenta, reduz, sem diâmetro, não dimensionável); aplicar pede
  confirmação com quantos aumentam e quantos reduzem.
- Cada problema tem o elemento, o texto e o que fazer; **Selecionar no modelo** aproxima a vista. Um teste garante que todo
  critério emitido pelo núcleo tem a sua parte e a sua correção.

### Terreno e topografia (§14)
- **Diagnóstico do Terreno** abre a janela do terreno (`TerrenoTela`), ainda somente leitura: origem dos dados (cada
  Toposolid), superfície existente e projetada, a diferença entre elas por amostragem, limites e parâmetros, o V14, **qual
  superfície cada cálculo lê** (conferido contra o código num teste) e o que precisa de atualização quando o terreno muda.
- O comando escolhido roda depois que a janela fecha, pelo evento externo do painel contextual
  (`ContextPanelService.RunLater`). Cada Toposolid é triangulado uma vez só.

### Interseções e rotatórias (§12)
- **Seções novas** nos formulários (`FormularioNo`): identificação e vias conectadas (com a principal, os ramos, o menor
  ângulo diante do limite do plugin e as vias que não existem mais), pavimentação, terraplenagem, drenagem (captações e
  pontos baixos na área do nó), verificação e resumo – refeitas a cada mudança dos campos (`FormWindow.Status`).
- **Verificação geométrica × operacional:** os limites do gerador com a origem; capacidade e nível de serviço são do
  Simulador de Tráfego; visibilidade e trajetória de giro não são calculadas.

### Quadras e lotes (§13)
- **Loteamento › Resultados do parcelamento** (`ResultadosParcelamento`): área de intervenção e limites, quadras, lotes,
  sistema viário associado, áreas públicas, parâmetros dimensionais, áreas residuais e verificação, com as tabelas, sem
  afirmar conformidade legal; seleciona a quadra ou o lote no modelo.

### Estaca da via nos formulários (K20)
- As larguras medidas, os recuos, os níveis da calçada, a largura por trecho, os retornos e as extensões de calçada mostram e
  leem a estaca do estaqueamento da via; os avisos da seção também a citam (`EstacasDaVia`; `RoadSetup.Estaqueamento`, que
  não é gravado com a seção). No estaqueamento padrão nada muda. **K20 resolvido.**

### Correções
- **Via refeita sem perder dados:** editar a seção da via, pôr um recuo ou editar um retorno refazia o pavimento sem a
  estrutura das camadas (Etapa 5) nem o estaqueamento (Etapa 4). A herança do pavimento anterior foi para o núcleo
  (`RoadSetup.HerdarDoAnterior`), com teste.
- **Estado da terraplenagem de um nó:** interseção, rotatória ou balão fora do registro ficava sem estado no painel; agora o
  nó molda o terreno pelas vias ligadas (`PainelContextual.NaTerraplenagem` com a busca das vias), com teste.

### Testes
- `Etapa11TelasTests`: 27 testes – conteúdo das seções da via, gerenciador de pavimento, rede de drenagem (classificação,
  sem chuva, com chuva e dimensionamento), terreno (diferença, partes, impactos, superfície de cada cálculo), nós (vias,
  ângulos, verificação, terraplenagem do nó), parcelamento, estacas da via e herança do pavimento; e as fontes das janelas e
  dos comandos.
- Telas NÃO VISTAS NO REVIT (K25).

## 2026-10-27 – Etapa 10: Central do Projeto e painel contextual (versão 2.16.0)

Etapa 10 do plano unificado (Prompt de Interface §8, §9, §20). Entrega: `Releases/Etapa10` (não executada no Revit 2027).

### Central do Projeto (D-028)
- **Botão Central do Projeto** (painel Projeto). Janela somente leitura com:
  - cabeçalho: projeto, arquivo, versão e a situação geral;
  - situação do projeto: a última verificação e o estado dos resultados derivados, com atalhos;
  - cartões dos indicadores;
  - os módulos, com a operação principal.
- **Indicadores** (`CentralDoProjeto`, no núcleo):
  - vias, extensão das vias, interseções e rotatórias;
  - área das quadras, lotes e área parcelada (as quadras divididas como no quadro do loteamento);
  - volumes de corte e de aterro (do registro da terraplenagem);
  - pavimento, medido nas peças só quando você clica Calcular;
  - rede de drenagem;
  - inconsistências da última validação gravada, com a data.
- **A situação de cada valor** é dita: calculado, não calculado, indisponível, desatualizado ou inválido. Valor
  desconhecido aparece como "—", nunca como zero.
- **Sem estado inventado:** a situação geral é o pior nível entre a última verificação e os resultados a refazer, e nunca
  diz "validado". As operações recentes não aparecem, porque o plugin não guarda esse histórico.
- **Atalhos:** abrem o comando depois que a Central fecha, pelo mesmo caminho do botão (`CommandBase.Invoke`: as mesmas
  conferências e mensagens). **Copiar resumo** copia o texto.

### Painel contextual
- **Botão Painel Contextual** (painel Projeto): mostra ou oculta um painel acoplável do Revit. Na primeira vez, ele abre
  como aba junto da paleta Propriedades.
- **Propriedades por tipo** (`PainelContextual`, no núcleo):
  - via: seção, eixo, estaqueamento, greide e estrutura do pavimento, camada por camada, com a fonte;
  - nós: interseção, rotatória, balão e nó viário;
  - drenagem: dispositivo e trecho de rede;
  - urbanismo: quadra (com os lotes) e loteamento;
  - obras e demais marcas.
- **Estado e operações:** o estado vem com o nível (em dia ou não com a terraplenagem aplicada; área lida da via ou
  informada à mão), e as operações compatíveis abrem os comandos existentes.
- **Vários elementos:** só as propriedades iguais em todos, sem edição em lote. Tipos diferentes mostram a contagem por
  tipo.
- **Funcionamento no Revit:** o evento de seleção só lê, como a API exige. As operações rodam por `ExternalEvent`. Numa
  seleção grande, o painel considera as 50 primeiras marcas e avisa.

### Testes
- `Etapa10CentralTests` (12):
  - Central: nada de zero inventado, situação dos valores, volumes em dia e desatualizados, lotes e área parcelada,
    drenagem e inconsistências, situação geral e todo tipo de marca com módulo;
  - painel: vazio, via, seleção múltipla sem lote, drenagem e quadra;
  - nas fontes: os comandos citados existem na faixa, o painel só lê na seleção e a Central roda o comando pelo
    `Invoke`.

## 2026-10-27 – Etapa 9: design system e faixa de opções por tema (versão 2.15.0)

Etapa 9 do plano unificado (Prompt de Interface §5, §7, §22–§24; Master §20). Entrega: `Releases/Etapa09` (não
executada no Revit 2027).

### Faixa de opções por tema (D-027)
- **Guia SinalizaBIM** com os 11 painéis do fluxo pedidos no Prompt de Interface (§7): Projeto, Terreno, Sistema Viário,
  Interseções, Terraplenagem, Pavimentação, Drenagem, Urbanismo, Análise, Documentação e Sistema.
- **Guia SinalizaBIM Sinalização** com a sinalização horizontal, a vertical e a segurança viária. A lista do §7 não tem
  painel para a sinalização, que soma 23 botões.
- A guia *SinalizaBIM Infra* deixou de existir: os painéis dela foram para os temas.
- Os 98 botões continuam, cada um uma vez e com o mesmo nome. O MANUAL tem o mapa antigo → novo.
- **Diagnóstico do Terreno** (novo, painel Terreno), somente leitura:
  - resumo da superfície projetada e do terreno original guardado;
  - declividade máxima informada, com a fonte;
  - o V14, o mesmo do Validar Modelo (`ModelValidator.Topografia`);
  - pode selecionar as vias com eixo fora da superfície.
- **Dicas curtas** (até 120 caracteres), com o texto completo na descrição longa, que o Revit mostra ao manter o cursor. A
  dica do Validar Modelo, desatualizada, foi reescrita (V01–V17 e o estado dos resultados).
- **Disponibilidade coerente:** o botão só fica habilitado com um projeto (.rvt) ativo, as mesmas condições que o comando
  exige. Exceções: Configurações, Catálogo e Normas / Sobre. O atributo `NoDocumentRequired` é a única fonte da regra, para
  o comando e para o botão.
- **Ícones:** 10 desenhos novos, e cada botão tem o seu. Antes, 8 desenhos se repetiam em botões diferentes e Normas / Sobre
  caía no ícone padrão. A Via Férrea ganhou trilhos e dormentes; a cruz de Santo André ficou para o Cruzamento
  Rodoferroviário.

### Design system (Interface §5, §22, §23)
- **Tokens** num só lugar, com o mesmo nome e valor no XAML (`Styles.xaml`) e no código (`UiTokens`):
  - superfícies, texto, primária, seleção e bordas;
  - estados – sucesso, aviso e erro, cada um com o seu fundo – e desabilitado;
  - tipografia (Segoe UI, cinco tamanhos) e espaçamento (escala de 4).

  As chaves antigas continuam.
- **Componentes:**
  - texto: título de seção, subtítulo, legenda, valor (algarismos alinhados), valor grande, texto de validação, status
    (ok, aviso, erro, informação, não verificado);
  - contêineres: cartão e faixas de mensagem;
  - controles: botões secundário e de perigo, tabelas (cabeçalho e linhas alternadas), barra de progresso, expansor e dicas
    que quebram linha.
- **Acessibilidade:**
  - foco de teclado visível nos controles;
  - contraste do texto de pelo menos 4,5 : 1, conferido em teste;
  - o aviso passou a âmbar escuro #8F5D00 (antes #A8561F);
  - a cor nunca é o único sinal: o texto diz o estado.
- **Cores soltas** passaram aos tokens:
  - Quantitativos: 29 linhas do XAML e as cores dos indicadores;
  - Simulador de Tráfego: as cores da interface;
  - resumo da janela da via e Autoteste.

  Os desenhos (mapa, prévia, miniaturas, ícones) mantêm as cores de dados.

### Mensagens
- As mensagens que apontavam a guia *SinalizaBIM Infra* ou o painel *Loteamento* apontam os painéis novos. Isso inclui a
  frase do memorial do loteamento sobre vias sem nome.

### Testes
- `Etapa09InterfaceTests` (14):
  - faixa: cada comando uma vez (menos os três fora de propósito), painéis na ordem, ícone próprio e desenhado, dicas curtas
    e disponibilidade;
  - design system: tokens iguais no XAML e no código, contraste, componentes, foco e contêineres sem cor de texto própria;
  - janelas: as chaves de estilo usadas estão definidas (antes, uma chave ausente só falharia ao abrir a janela no Revit) e
    não há cores soltas;
  - Diagnóstico do Terreno: o mesmo V14 do Validar Modelo, o resumo e o relatório;
  - o manual cita todos os painéis e rótulos.

## 2026-10-26 – Etapa 8: integração associativa – estado dos resultados (versão 2.14.0)

Etapa 8 do plano unificado (Fase X; Prompt Master §14; Motores §14–§15). Entrega: `Releases/Etapa08` (não executada no
Revit 2027).

### Auditoria
- **Grafo de dependências:** o `ModelGraph` cobria as referências gravadas entre marcas (detalhe, nó, quadra, rede,
  recortes) e a atualização automática refazia as conexões. Nada dizia o estado dos resultados derivados do modelo, além
  da V15 da terraplenagem, nem distinguia um valor lido do modelo de um editado à mão.

### Origem dos valores lidos e atualização seletiva
- **Registro da leitura** (`ContribuicaoLida`): o Ler do Modelo guarda em cada captação o que leu (via, quando, área,
  declividade, ponto baixo). Campo igual ao lido vem do modelo; diferente foi editado à mão e fica preservado.
- **Estado da leitura** (`DrenagemDoModelo.Estado`):
  - nunca lida;
  - em dia;
  - desatualizada: o campo ainda é o lido, mas o modelo agora dá outro valor (a via mudou, outra captação entrou acima,
    o dispositivo saiu da sarjeta);
  - editada à mão.
- **Atualizar só as desatualizadas** (Ler do Modelo): relê só essas e troca só os campos que ainda eram os lidos.

### Estado dos resultados e impacto da edição (`EstadoDosResultados`)
- **Estado do projeto:** terraplenagem (registro), captações lidas, o que a atualização automática refaz e o que é
  calculado na hora. Sai numa seção própria do relatório do Validar Modelo.
- **Impacto de uma edição:**
  - conexões ligadas à via;
  - marcas dependentes pelo grafo;
  - terraplenagem não reaplicada;
  - captações lidas que ficaram desatualizadas.

  O relatório de Editar, do Perfil da Via e da Estrutura do Pavimento diz o que ficou por refazer.
- **V17 – Integração:** leitura do modelo que ficou desatualizada (as editadas à mão não entram).

### Correção
- **Assinatura das caixas de drenagem:** passou a ignorar os dados hidráulicos (área, coeficientes, tempos, vazão
  externa, sarjeta, ponto baixo, leitura do modelo), porque as caixas só entram na terraplenagem pela escavação. Antes,
  preencher a área acusava a terraplenagem desatualizada (V15). Um registro gravado pela 2.11–2.13 com caixas que tinham
  esses dados aponta essas caixas uma vez; rodar a Terraplenagem regrava o registro.

### Testes
- `Etapa08IntegracaoTests` (6). `Etapa02DiagnosticoTests` conta 17 regras.

## 2026-10-26 – Etapa 7: urbanismo e elementos complementares (versão 2.13.0)

Etapa 7 do plano unificado (Fase IX e Motores §11–§12). Entrega: `Releases/Etapa07` (não executada no Revit 2027).

### Auditoria antes de implementar (Motores §12)
- **Elementos urbanos e viários complementares:** quase tudo já existia (AUDIT §9.2a). Faltava a substituição controlada,
  que esta etapa faz.
- **Parcelamento:** os percentuais por categoria, as áreas públicas, o sistema viário e os mínimos editáveis já existiam.
  Faltavam:
  - a fonte dos mínimos;
  - indicadores além dos percentuais;
  - a conferência no Validar Modelo (os avisos só apareciam no quadro e no memorial).

### Parcelamento
- **Indicadores** (`IndicadoresUrbanisticos`): lotes, densidade bruta (lotes por hectare da gleba), área e frente dos lotes
  (média, menor, maior), lotes abaixo do lote mínimo e percentuais do sistema viário, das áreas públicas e das áreas verdes.
  Sem gleba ou sem lotes, o indicador diz isso em vez de dar zero. Saem no memorial, abaixo do quadro de áreas.
- **Fonte dos mínimos** no loteamento (Mínimos gerais): na tela, no memorial e nas mensagens da validação.
- **V16 – parcelamento** (Quadras e lotes):
  - percentual da gleba abaixo do mínimo informado, com a fonte;
  - lotes abaixo do lote mínimo, por quadra;
  - sem mínimos informados, "não verificado".
- `LotReport.MinimosNaoAtendidos` faz a conta usada pelos avisos do quadro e pela V16.

### Elementos urbanos
- **Trocar Elementos** (Elementos Urbanos ▾, `SubstituicaoUrbana`): troca o tipo de vários elementos de uma vez, mantendo
  posição, direção, caminho, afastamento, recuos, rotação e nível. Medidas, cor e espaçamento próprios ficam ou passam a
  ser os do novo tipo, por escolha.

### Testes
- `Etapa07UrbanismoTests` (5). `Etapa02DiagnosticoTests` conta 16 regras.

## 2026-10-26 – Etapa 6: drenagem integrada às vias e ao terreno (versão 2.12.0)

Etapa 6 do plano unificado (Fase VIII e RT-1.5). Entrega: `Releases/Etapa06` (não executada no Revit 2027).

### Achados da auditoria que esta etapa trata
- A área de contribuição e a declividade da sarjeta de cada captação só existiam como valores digitados (K5).
- O recobrimento dos tubos era medido sob a reta entre as tampas, mesmo com uma via ou uma baixada do terreno entre elas.
  O `DrainageNetwork.Analyze` já aceitava uma superfície, mas nenhum chamador a passava.
- Nada apontava um ponto baixo da sarjeta sem captação, nem um aterro de via sobre um talvegue sem travessia.

### Drenagem lida do modelo (`DrenagemDoModelo`)
- **Sarjeta de cada boca de lobo e grelha:** a via de meio-fio mais próximo, a até 1,5 m da face. Lê a declividade pelo
  greide (com abaulamento ou superelevação), o sentido do escoamento e se a captação está num ponto baixo (a sarjeta sobe
  ao menos 1 cm dos dois lados, a até 5 m).
- **Área que escoa até a captação:** a pista do eixo ao meio-fio e a calçada (com superelevação, a pista inteira para o
  lado baixo). Vai do divisor, da ponta da via ou da captação de cima até ela; com a sarjeta em nível entre duas
  captações, metade para cada. Lotes e áreas externas não entram.
- **Comando Ler do Modelo** (Drenagem): mostra o que leu e preenche só os campos vazios ou substitui todos, por escolha do
  usuário. Nada é gravado sem a escolha.
- **V13 – drenagem × vias e terreno:**
  - ponto baixo da sarjeta sem captação (aviso), só quando o projeto tem drenagem modelada;
  - captação cuja declividade informada difere do greide em mais de 0,5 ponto percentual, ou que está num ponto baixo
    sem a marca (informação);
  - talvegue do terreno natural sob o aterro de uma via, sem trecho de rede atravessando a via a até 15 m (aviso). Trechos
    em obra ficam de fora. Sem o terreno natural, "não verificado". O Validar Modelo passa o terreno natural do Toposolid.
- **Recobrimento sob a superfície acabada:** pista e calçada das vias do plugin e, fora delas, o terreno projetado; sem
  nenhum dos dois, a reta entre as tampas. Vale no V13, no Relatório da Rede e no memorial.
- **Perfil longitudinal da rede e lista de dispositivos** (`PerfilRede`): ramos de montante para jusante, com tampas,
  fundos, declividades, diâmetros e recobrimentos; tipo, posição, profundidade e ligações de cada dispositivo. Entram no
  CSV do Relatório da Rede, com as captações × modelo; o perfil também é conferido no Autoteste.
- **Tolerâncias da conferência** no registro único (`Tolerancias`): são limites do plugin, não de norma.
- **Nome dos tipos de dispositivo** passou para `DrainageDefinition.NomeTipo` (memorial e relatório usam o mesmo).
- **Traçado guardado** (`PathReference.StoredPolyline`): pontos ou, numa via sobre linhas do Revit, o cache das linhas –
  o mesmo que o validador já usava. Corrige, antes da entrega, a primeira versão desta etapa (`c959e00`), que lia só os
  pontos e ignorava as vias por linhas.

### Testes
- `Etapa06DrenagemTests` (11, com a regressão da via sobre linhas).

## 2026-10-26 – Etapa 5: estrutura do pavimento e terraplenagem desatualizada (versão 2.11.0)

Etapa 5 do plano unificado (Fases VI, VII e X). Entrega: `Releases/Etapa05` (não executada no Revit 2027).

### Achados da auditoria que esta etapa corrige
- **Três profundidades para a mesma via:** o corte transversal desenhava camadas próprias (padrão 15 + 15 cm), a
  terraplenagem usava um subleito único (0,40 m) e o quantitativo de terraplenagem usava sempre 0,40 m, mesmo quando a
  terraplenagem tinha sido aplicada com outro subleito (o MANUAL dizia isso).
- **Terraplenagem velha apresentada como atual:** depois de mudar uma via ou obra, o Toposolid ficava como estava e nada
  apontava que ele não acompanhava mais o projeto.

### Estrutura do pavimento (`EstruturaPavimento`)
- **Na via:** camadas sob o revestimento, de cima para baixo, com nome, espessura (cm), material, função (base, sub-base,
  reforço do subleito, regularização, outra) e a fonte. O revestimento é o próprio piso da pista. Campo opcional: projetos
  antigos abrem iguais.
- **Comando Estrutura do Pavimento** (Relevo e Terreno): espessura do revestimento, camadas em texto
  ("nome; espessura; material; função") e fonte. Linha ilegível, função desconhecida, camada sem nome, espessura nula ou
  acima de 2 m: nada é gravado. Oferece refazer a terraplenagem quando a via molda um terreno existente.
- **Terraplenagem:** o fundo da caixa sob a pista é a estrutura da via (revestimento + camadas). Interseções, rotatórias,
  balões e as marcas deles usam a via ligada mais funda. Vias sem estrutura usam o subleito das opções. O relatório diz
  quantas vias usaram a estrutura.
- **Corte transversal:** sem camadas escolhidas no perfil, desenha as da estrutura da via cortada (antes, as típicas).
  Vias de estruturas diferentes no mesmo corte: as da primeira, com aviso. A janela do perfil ganhou "Usar a estrutura
  do pavimento da via cortada".
- **Quantitativo por camada** (`PavementLayerQuantities`): aba *Camadas do pavimento*, CSV e planilha. Revestimento e cada
  camada de cada via e nó, com área do pavimento em planta, volume e fonte; totais por camada e material; vias sem
  estrutura listadas. Vias numeradas pela ordem no projeto (o código PAV-ASF é o mesmo de todas). Limite: sem
  sobrelargura das camadas sob a sarjeta e o meio-fio (K21).

### Registro da terraplenagem e V15
- **Registro** (`RegistroTerraplenagem`, nas Informações do projeto, na mesma transação da terraplenagem): quando, versão,
  subleito, taludes, alcance, volumes, homogeneização e a assinatura (SHA-256 da definição) de cada marca que moldou o
  terreno. A linha do terreno natural guardada pelas obras não entra na assinatura (é leitura do terreno). Com falha num
  Toposolid, o registro antigo fica.
- **Quem molda o terreno** passou para o núcleo (`EarthworkRecord.Participantes`), a mesma escolha da Terraplenagem.
- **V15 – terraplenagem desatualizada** (módulo Terraplenagem): marca que molda o terreno e mudou, foi apagada, deixou de
  moldá-lo ou entrou depois da última terraplenagem. Um achado por situação. Sem registro: "não verificado".
- **Comparação de alternativas:** "Só calcular" compara a simulação com a terraplenagem aplicada (corte, aterro, balanço,
  diferença e quantas marcas diferem).
- **Quantitativo de terraplenagem:** usa as opções da última terraplenagem aplicada (subleito, taludes, alcance,
  homogeneização). Sem registro, as padrão.
- **Autoteste (K19):** sob a pista, confere também o fundo da estrutura, com a profundidade da última terraplenagem,
  quando o registro tem a via com a definição atual. Senão diz por que não conferiu.

### Testes
- `Etapa05PavimentoTests` (14). `Etapa02DiagnosticoTests` conta 15 regras. A mensagem do commit `ebfacc8` diz 14 testes
  quando eram 13; o 14º entrou em `0be14f2`.

## 2026-10-26 – Etapa 4: estaqueamento e limites dos avisos (versão 2.10.0)

Etapa 4 do plano unificado (Fases V e VII). Entrega: `Releases/Etapa04` (não executada no Revit 2027).

### Limites citados nos avisos (regra: nenhuma norma inventada)
- **Achado da auditoria:** avisos de rotatória, interseção, canalização e obras citavam valores sem fonte no código ou no
  estudo dos manuais:
  - minirrotatória com "referência: 13 a 25 m";
  - "T entre 75° e 105°" e "< 30°";
  - transição de 30/60 m junto a obstáculo;
  - pista giratória de 4 m e ilha central de 4 m;
  - "rotatórias compactas/mini devem ter uma faixa".
- **`LimitesDeConferencia`:** cada limite tem valor, unidade, origem e fonte. A origem é o espaço que o gerador precisa
  para desenhar a peça ou um valor de referência. Sem fonte, o aviso diz "limite de conferência do plugin, sem norma
  citada – confira com a norma adotada". Os valores e as condições não mudaram.
- **Textos retirados:** "13 a 25 m" e "T entre 75° e 105°", que não tinham origem.
- **Ilha separadora:** o aviso dizia "mínimo ~7 m". O gerador precisa de 6 m de faixas mais 0,80 m de ilha (6,8 m), e o
  aviso agora diz isso.

### Estaqueamento (D-023)
- **`Estaqueamento` da via:**
  - estaca do início do eixo e intervalo entre estacas;
  - equações de estaca na forma do levantamento ("estaca atrás = estaca à frente");
  - posição no eixo de uma estaca. Estaca numa lacuna não tem posição; estaca repetida (equação que volta) fica no
    primeiro trecho, e isso é dito.
- **Gravação:** no pavimento da via, opcional. Dentro das definições, as estacas continuam sendo distâncias ao longo do
  eixo, então não há migração e projetos antigos abrem iguais.
- **Onde aparece:**
  - CSVs do eixo, do greide e da verificação do traçado;
  - V12 do Validar Modelo;
  - tabela de volumes por estaca.

  Com estaqueamento próprio, as colunas em metros dizem "distância no eixo".
- **Perfil da Via:** campos de estaca inicial, intervalo e equações. Valores ilegíveis ou uma equação fora do eixo não são
  aplicados, e nada muda no modelo.
- **Limite (K20):** os formulários de largura por trecho, recuo, extensão e níveis da calçada ainda pedem a distância desde
  o início do eixo. Isso muda na Etapa 11.

### Testes
- `Etapa04ViasTests` (7).

## 2026-10-26 – Etapas 2 e 3: diagnóstico padronizado e topografia (versão 2.9.0)

Etapas 2 e 3 do plano unificado, na Fase X. Entrega conjunta em `Releases/Etapa03` (não executada no Revit 2027).

**Por que juntas.** A Etapa 3 já tinha posto nas Configurações o campo "Declividade máxima do terreno". Uma DLL só da Etapa
2 mostraria esse campo sem efeito, e o Prompt de Interface proíbe controle que parece funcionar e não funciona. Por isso
as duas etapas saem numa entrega só, e não há `Releases/Etapa02`.

### Etapa 2 – diagnóstico padronizado e tolerâncias
- **Módulo e ação recomendada em cada achado do Validar Modelo.** As regras V01–V14 estão em `ModelValidator.Regras`, cada
  uma com módulo, o que verifica e a ação padrão. Um achado pode ter ação própria: o V04 diz o que fazer em cada tipo de
  dependência quebrada.
- **"Não verificado"** (`Severidade.NaoVerificavel`): a verificação que não pôde rodar é dita, nunca contada como "sem
  problema". Casos:
  - limites geométricos não informados (V12);
  - rede sem Projeto de Drenagem ou sem equação de chuva (V13);
  - elementos do Revit sem o documento (V06);
  - sem Toposolid ou sem a declividade máxima do terreno (V14).
- **Histórico da validação** (`ValidationHistory`): a validação anterior do projeto, gravada fora do modelo (o comando
  continua somente leitura), entra na comparação, que separa os achados **novos**, **persistentes** e **resolvidos**. O
  achado é identificado pelo código e pelas marcas, não pela mensagem.
- **Relatório:** resumo com a versão do plugin, contagens por severidade e por módulo, comparação, ação de cada achado e a
  seção "Não verificado".
- **Tolerâncias** do núcleo reunidas em `Tolerancias.Todas`, com unidade, onde fica e uso. Nenhum valor mudou (K9). O teste
  confere as relações entre elas: grade das booleanas < mesmo vértice < ajuste do contorno.

### Etapa 3 – topografia
- **Diagnóstico da superfície** (`TerrainDiagnostics`):
  - dados insuficientes (menos de três pontos ou todos numa reta);
  - coordenada não numérica;
  - o mesmo ponto com cotas diferentes;
  - triângulos sem área;
  - declividade acima do limite **informado** (sem limite, não conferida);
  - consultas fora da superfície (extrapolação).
- **V14 no Validar Modelo**, com o Toposolid **projetado** (`TerrainModel.CurrentMesh`). As consultas são os eixos das
  vias que moldam o terreno.
- **Configurações:** declividade máxima do terreno (%) no grupo dos limites geométricos. Ela não liga o V12.
- **Ainda não feito, passa à tela do terreno (Etapa 11):**
  - importação de pontos;
  - curvas de nível – o Toposolid tem recursos nativos que não devem ser replicados sem antes conferir o que a API 2027
    oferece.

### Outros
- Testes novos: `Etapa02DiagnosticoTests` (5) e `Etapa03TerrenoTests` (8).
- Correção do commit `01ed60c`: a mensagem dizia "compila sem avisos", mas o projeto de testes não compilava (só o núcleo e
  o plugin tinham sido compilados). O CI apontou e `762c20f` corrigiu.

## 2026-10-26 – Etapa 1: integridade geométrica (versão 2.8.0)

Etapa 1 do plano unificado (`docs/PLANO_DE_ACAO.md`), na Fase X. Entrega: `Releases/Etapa01` (não executada no Revit 2027).

### Pisos sobrepostos classificados (RT-1.3)
- `OverlapClassifier` classifica cada par pelo que foi medido, com motivo e ação recomendada. Nada é apagado.
- **As cinco classes:**
  - camada intencional (só apoiados);
  - interseção legítima (peças de duas vias que se cruzam sem nó);
  - duplicação acidental;
  - conflito real;
  - inconclusivo (piso que não é do plugin, ou um piso inteiro dentro de outro de outro tipo).
- **Autoteste:** a conferência do fim de cada etapa e a do modelo final dão o resumo por classe. O arquivo de dados traz a
  classe, o motivo e a ação de cada par, e o aviso do modelo final diz quantos pares são conflito real ou duplicação.
- **Resíduo investigado sem mudar o gerador:**
  - nas peças cruas de um cruzamento simples, o classificador achou 2 pares meio-fio × calçada (62 cm³ cada) – um vértice
    a 5 mm da borda comum, simplificado num piso e não no outro;
  - com o contorno ajustado como no Revit (ajuste conjunto, D-016) não sobra nenhum par;
  - o teste exige zero pares nesse caso.

### Pisos × terreno projetado (RT-1.2)
- `FloorTerrainCheck` (núcleo) acusa quatro problemas:
  - piso enterrado (terreno acima do topo);
  - terreno dentro do piso ou da estrutura;
  - piso suspenso (vão sob a estrutura);
  - degrau entre o terreno e a borda da seção, onde o talude devia começar.
- Ele diz quantos pontos foram de fato conferidos: zero pontos é "não verificado", não "sem problema".
- **Defeito corrigido no Autoteste:** a conferência terreno × via comparava a pista com o terreno **natural**
  (`TerrainModel.Ground`) e acusaria "Toposolid acima da pista" em todo trecho de corte. Agora compara com o Toposolid
  projetado (`TerrainModel.Current`), pelo verificador do núcleo. Ela nunca tinha rodado: as "Verificações do modelo" eram
  opcionais e desligadas. Passam a rodar sempre no Completo.
- Limite: no Revit, o fundo da estrutura sob a pista não é conferido, porque a profundidade do subleito não é gravada com a
  terraplenagem (K19, Etapa 5).

### Outros
- `Fase08HidraulicaTests`: aviso xUnit2029 corrigido (a compilação dos testes voltou a 0 avisos).
- Testes novos: `Fase10CompatibilizacaoTests` (9) e `Fase10PisoTerrenoTests` (8).

## 2026-10-26 – Etapa 0: plano unificado dos três prompts

Sem DLL nova (a Etapa 0 não muda o plugin).
- Prompts de Motores e de Interface lidos com o Prompt Master. O plano único `docs/PLANO_DE_ACAO.md` tem 14 etapas e diz
  em que etapa fica cada pedido repetido (D-022).
- AUDIT §9:
  - interface atual: WPF, `Styles.xaml`, `FormWindow`, `IconFactory`, 2 guias e 12 painéis por tipo de elemento;
  - o que o Prompt de Interface pede e não existe;
  - lacunas dos motores classificadas de P0 a P3.
- `tools/release.sh` aceita etapas (`E<n>` → `Releases/EtapaNN`).
- Etapa 1 começou:
  - `OverlapClassifier` no núcleo (commit `9701ebb`). Ele classifica os pisos sobrepostos em camada intencional,
    interseção legítima, duplicação acidental, conflito real e inconclusivo, com motivo e ação;
  - testes `Fase10CompatibilizacaoTests` (9);
  - aviso xUnit2029 corrigido em `Fase08HidraulicaTests`.

## 2026-10-23 – Prompt Master adotado: auditoria pelo novo escopo

Sem mudança de código nem DLL nova.
- `docs/PROMPT_MASTER.md`: transcrição do documento enviado pelo usuário. Ele rege o desenvolvimento a partir de agora
  (DECISIONS D-021): a ordem vem dos riscos, e as 15 fases ficam como mapa de rastreio.
- AUDIT §8:
  - mapa dos módulos e dependências, com a contagem das referências entre pastas do Core, que mostrou referências
    mútuas (R15, K18);
  - matriz pelo escopo do Prompt Master nas cinco situações pedidas;
  - riscos e prioridades.
- Lacunas novas no ROADMAP (X3–X5, P6–P8):
  - diagnóstico do terreno;
  - validação com módulo, ação recomendada, "não verificável" e comparação com a anterior;
  - terraplenagem desatualizada sem aviso;
  - estrutura do pavimento só no desenho do corte;
  - atualização associativa sem mostrar o que ficou desatualizado.
- Correção na matriz da AUDIT §5: saiu a linha que ainda dava o dimensionamento hidráulico como ausente.

## 2026-10-22 – Fase VIII concluída: rede de drenagem dimensionada pelo método racional e memorial de drenagem (versão 2.7.0)

Fase da missão: VIII (drenagem e redes). Entrega: `Releases/Fase08` (não executada no Revit 2027). No meio da fase, o usuário
pediu o requisito transversal RT-1 (Toposolid × vias × terraplenagem × drenagem): registrado no ROADMAP, a fase seguiu.

### Fontes enviadas pelo usuário
- *Manual de Drenagem Urbana – Região Metropolitana de Curitiba* (SUDERHSA, versão 1.0, dez. 2002) e o memorial descritivo de
  drenagem pluvial de um loteamento em Miguelópolis/SP (set. 2019), como modelo. O cálculo usa só o que está nesses
  documentos, com a fonte citada no Projeto de Drenagem, no relatório e no memorial (D-019).
- A planilha de galerias do memorial modelo foi reproduzida com a equação de chuva dele: intensidade a menos de 0,005 mm/h e
  vazão a menos de 0,1 % nos 12 trechos. O C da planilha é o da fórmula de Horner com 80 % de área impermeável e a vazão
  máxima é a de Manning a seção plena com n ≈ 0,0151 (o memorial não escreve esses dois valores; a predefinição usa 80 % e
  0,015). Com as cotas exatas, o trecho 6 passa da capacidade (+3,0 %) – na própria planilha ele já passava (+2,6 %).

### Rede de drenagem
- **Trecho de Rede**: tubo de concreto entre dois dispositivos com caixa (PV, bocas de lobo, grelhas), de parede a parede, no
  fundo de cada caixa ou nas cotas informadas, com bolsas; o de jusante vira o montante do trecho seguinte. Editar o trecho:
  diâmetro e cotas de fundo. Mover ou editar um dispositivo redesenha os trechos ligados a ele (`MarkingService.RenderNetwork`).
- Trechos nomeados de montante para jusante (T1, T2…); ligações das bocas de lobo separadas das galerias.
- Conferência (sempre): contrafluxo, trecho ligado ao próprio dispositivo, dispositivo apagado e ciclos. Com o Projeto de
  Drenagem: declividades, recobrimento sobre a geratriz superior externa, diâmetros mínimos (galeria e ligação), espaçamento
  entre PVs (tabela 6.1 do manual), PV de queda e alinhamento pela geratriz superior na mudança de diâmetro.

### Vazões e dimensionamento
- **Projeto de Drenagem** (gravado nas Informações do projeto, D-020): equação de chuva nas formas potência e logarítmica
  (Fendrich e Parigot de Souza do manual; Barretos/DAEE-SP do memorial), tempo de retorno, tempo de concentração inicial,
  duração mínima, C ponderado ou de Horner, rugosidade, diâmetros comerciais, limites, sarjetas, bocas de lobo, valas e a
  identificação do memorial. Predefinições "Manual SUDERHSA" e "Memorial modelo"; sem equação de chuva, só a geometria.
- Contribuição em cada dispositivo: área local, C, área impermeável, tempo de concentração ou talvegue (Kirpich), vazão
  externa, declividade da sarjeta e ponto baixo.
- Método racional com o maior tempo de concentração que chega ao trecho mais o percurso a seção plena; Manning a seção
  plena (capacidade e velocidades) e o menor diâmetro comercial que escoa a vazão. Captações: sarjeta por Manning na seção
  triangular com o fator da tabela 6.3, lâmina, velocidade e engolimento pelas equações 6.5 a 6.7 com a tabela 6.4.
- **Relatório da Rede**: planilha de galerias (as colunas do memorial modelo e as do cálculo) e captações em CSV; **aplica os
  diâmetros calculados** e redesenha os tubos. **Validar Modelo**: V13 com a geometria, os critérios e, com a equação de chuva,
  capacidade, velocidade, sarjeta e engolimento.

### Memorial de Drenagem (.docx)
- Na estrutura do modelo: capa com a identificação; 1 Do projeto; 2 Parâmetros de projeto (vazões, chuva, recorrência, tempo
  de concentração, coeficiente de escoamento, capacidade das vias, critério das captações, bocas de lobo, tubos,
  recobrimento, velocidades, ligações e PVs); 3 Planilha das galerias em página de paisagem, com as captações e as
  observações da verificação; 4 Descritivo dos serviços; 5 Quantitativo (tubos por diâmetro, PVs, bocas de lobo por tipo e
  estimativas de escavação, escoramento, reaterro e material excedente); 6 Disposições finais com as assinaturas.
- O texto dos serviços é do plugin (o do modelo não é copiado), com os parâmetros do projeto (largura da vala, escoramento).
- `DocxWriter`: seções em paisagem, quebra de página, parágrafos centralizados e em negrito, cabeçalho com o autor e rodapé
  "Página X de Y".

### Não implementado (motivo em K5)
- Tempo de concentração de George Ribeiro (o memorial cita sem a expressão); divisão da vazão em dispositivo com mais de uma
  saída; excesso de uma boca de lobo passado à seguinte pela sarjeta; terreno entre as caixas no recobrimento; macrodrenagem.

### Testes
- `Fase08DrenagemTests`: caixas da rede, tubo entre as paredes, avisos, medição, conferência sem e com critérios, ciclo,
  persistência, V13 e o Projeto de Drenagem em JSON (incluindo limite apagado que continua apagado ao reabrir).
- `Fase08HidraulicaTests`: tabela B.3 (Fendrich e Parigot), exemplos 6.1 (sarjeta), 6.2 (bocas de lobo) e 6.3 (galeria) do
  manual, planilha do memorial modelo, junções (tempo de concentração e áreas), dimensionamento aplicado, captações, PV de
  queda, espaçamento e geratriz superior, V13 com a vazão e o memorial em .docx (seções, paisagem, cabeçalho e rodapé).
- Autoteste: etapa "Drenagem – rede" (PVs, trechos, vazões, diâmetros calculados, PV movido, V13 e memorial).

## 2026-10-22 – Fase VII concluída: rotatória com by-pass sem pisos sobrepostos e volumes por estaca no quantitativo (versão 2.6.0)

Fase da missão: VII (terreno, terraplenagem e interseções) – concluída no que é verificável aqui. Entrega: `Releases/Fase07`
(não executada no Revit 2027).

### Rotatória com by-pass (K16)
- No 4º Autoteste completo, a rotatória com by-pass ficou com 43 pares de pisos sobrepostos no modelo final; na emulação do
  contorno do Revit, 30 pares e 111 L entre as ilhas do by-pass e o meio-fio externo.
- Causa 1: os lóbulos do by-pass eram recortados a 2 cm da pista (para separá-los dela) e depois unidos de volta. O vão
  ficava fora da pista e virava uma **tira de meio-fio de 2 cm × ~14 m colada na ilha** do by-pass. Agora o vão volta a ser
  pista, depois da zona efetiva da rotatória: unido antes, ligava os lóbulos às faixas dos ramos e a zona ia até o fim delas.
- Causa 2: a abertura da ilha (−0,75 m / +0,75 m, para tirar fatias estreitas) refazia as curvas com outros vértices e passava
  até 0,4 mm por cima dessa tira. Agora o resultado fica dentro da ilha de antes.
- Resultado na fixture do 4º Autoteste: 0 pares crus (eram 4, de 2 a 5 cm²) e 0 na emulação do contorno do Revit; todas as
  peças do meio-fio externo com 15 cm de largura média (havia peça de 8,3 cm por causa da tira).

### Volumes por estaca no quantitativo de terraplenagem (F7b, K7)
- O quantitativo de terraplenagem no Revit usava só a malha; o método das seções existia só no núcleo (EV6).
- Agora o quantitativo calcula, para cada via que molda o terreno, as **seções a cada 20 m** sobre a mesma superfície de
  projeto e o terreno original do Toposolid, **só na parte que é da via** (`SectionVolumes.ForOwner`: interseções,
  rotatórias e outras vias ficam fora, sem contar duas vezes). Ao lado, a parte da via na malha, para conferir os dois
  métodos (`SecoesDaVia.Comparacao`).
- Janela do quantitativo: aba **Terraplenagem por estaca** (comparação malha × seções por via e a tabela: estaca, áreas de
  corte e aterro, volumes acumulados e massa). O mesmo bloco no CSV e na planilha. O Autoteste anota os dois volumes de
  cada via na etapa do quantitativo de terraplenagem.
- O volume do quantitativo continua o da malha; as seções são para conferência e para a tabela por estaca. Sem correção
  de curvatura (as seções perpendiculares se sobrepõem no lado interno das curvas fechadas).

### Testes
- `Fase07InterseccoesTests` (7): by-pass do 4º Autoteste sem pisos sobrepostos (cru e contorno do Revit), meio-fio externo
  sem tira estreita, ilhas fora do meio-fio externo (fixture e by-pass montado à mão); seções da via conferem com a parte
  dela na malha (−1,4 %), nó que cruza a via fica fora, tabela, CSV e planilha.
- `Fase04GeometriaTests`: o teste das rotatórias do 4º Autoteste passa a incluir a com by-pass.

## 2026-10-22 – Fase VI concluída: quantitativo por componente da seção e regras da regeneração no núcleo (versão 2.5.0)

Fase da missão: VI (corredores, seções e pavimentação) – concluída no que é verificável aqui. Entrega: `Releases/Fase06`
(não executada no Revit 2027).

### Quantitativo por componente da seção
- No quadro por marca, a interseção, a rotatória e o cul-de-sac saem como uma unidade, e o concreto de meio-fio, sarjeta,
  calçada, ilhas e canteiros vai junto numa linha "Concreto". Não dava para saber quantos metros de meio-fio ou metros
  quadrados de calçada o projeto tem.
- `ComponentQuantities` soma, em todas as marcas da seção (vias e seus elementos de bordo, interseções, rotatórias,
  extensões de calçada, cul-de-sac e áreas de calçada), cada componente: pavimento, faixa galgável, sarjeta, sarjetão,
  meio-fio (área e extensão), calçada, gramado, ilhas e canteiros pavimentados; o que é físico e não é da seção (árvores
  da ilha) fica em "outros". Uma linha por componente, material e hierarquia viária, com a origem (marcas e quantas).
- Medido nas peças geradas, depois dos recortes. A extensão do meio-fio é a do eixo da peça: L = (P + √(P² − 16A)) / 4,
  exata na faixa reta e no setor de coroa; num anel fechado, P / 2. Pisos editados no modelo entram com a área medida,
  repartida entre os componentes da mesma cor na proporção do que foi gerado.
- Os geradores marcam o componente das peças que a camada não identifica (`MarkingPiece.Component`): meio-fio de
  canteiro, ilha e ilha separadora; miolos gramados ou pavimentados; faixa galgável; sarjeta, calçada e faixa de serviço
  sem perfil da via. A marcação **não separa pisos no Revit** (a chave dos pisos continua cor, cota, espessura e camada).
- Janela do quantitativo: aba **Resumo por componente da seção** (com o filtro de hierarquia); o mesmo bloco no CSV e na
  planilha.

### Regeneração depois de mudar eixo, greide ou seção
- A escolha das conexões a refazer (interseções pelas vias, rotatórias pela via ou pelo grupo do ramo, balões pela via)
  passou da camada Revit para o núcleo (`ModelGraph.ConnectionsToRefresh`); a regra do nó que acompanha o eixo movido até
  15 m ou faz a interseção sair, idem (`IntersectionGenerator.FollowNode`). O Revit chama as duas; o comportamento é o
  mesmo.
- Conferido no núcleo: eixos movidos 7 m e 4 m → a mesma interseção (id e ajustes) refeita fica deslocada do mesmo vetor
  e com os mesmos quantitativos por componente (diferença ≤ 0,001 m²); calçada da via 1 m mais larga → o perfil lido pela
  interseção aumenta 1 m de cada lado e a calçada dela, de 63,9 para 77,7 m².
- A atualização automática no Revit (updater) depois dessas edições continua não executada aqui (K17).

### Testes
- `Fase06ComponentesTests` (16): extensão do meio-fio exata na faixa reta, na curva e no anel; vias de todos os modelos,
  interseções do 4º Autoteste e rotatórias de todos os tipos com toda peça física num componente (soma igual ao físico
  gerado); área medida no modelo repartida na proporção; sinalização e mobiliário fora; componente sem efeito nos pisos
  do Revit; CSV e planilha com o bloco novo.
- `Fase06RegeneracaoTests` (4): conexões refeitas por via alterada; nó que acompanha até 15 m; eixo movido; seção
  alterada.

## 2026-10-22 – Fase V concluída: limites geométricos do traçado e do greide informados pelo usuário (versão 2.4.0)

Fase da missão: V (alinhamentos e perfis) – concluída. Entrega: `Releases/Fase05` (não executada no Revit 2027).

### Limites geométricos configuráveis (K8)
- O plugin já listava os elementos do traçado (curvas nos PIs e no eixo das vias comuns) e do greide (rampas e curvas
  verticais), mas não os conferia contra nada. Agora o usuário informa os limites em **Configurações → Limites geométricos
  do traçado e do greide**: raio mínimo, tangente mínima entre curvas, deflexão máxima de vértice sem curva, rampa máxima e
  mínima, K mínimo das curvas convexas e côncavas, comprimento mínimo de curva vertical e a **fonte** (norma, edição,
  tabela, classe da via).
- O plugin **não traz valores normativos**. Campo vazio não é conferido; a fonte sai nos relatórios como foi escrita, ou
  como "fonte não informada pelo usuário".
- `GeometricCriteria` (guardado em `PluginSettings.CriteriosGeometricos`) e `GeometricCheck` (Core):
  - `Axis`: curvas do eixo abaixo do raio mínimo, tangente curta entre duas curvas e vértice sem curva (acima da deflexão
    máxima informada; sem ela, com raio mínimo informado, o vértice conta como raio zero);
  - `Horizontal`: o mesmo para as curvas nos PIs do traçado;
  - `Vertical`: rampas fora do intervalo, K abaixo do mínimo (convexa: a rampa diminui; sem curva, K = 0) e curva
    vertical curta;
  - `Csv` (";" e vírgula decimal, fonte na primeira linha) e `Summary` (resumo para a mensagem do comando).
- **Perfil da Via** grava `verificacao-<via>.csv` ao lado dos outros relatórios e mostra os primeiros itens fora dos
  limites; sem limites informados, avisa que traçado e greide não foram conferidos.
- **Validar Modelo**: novo código **V12** (aviso, confiança média, revisão humana), um por via, com até quatro itens e a
  fonte. Só aparece com limites informados.
- Distância de visibilidade continua sem cálculo (K8).

### Testes
- `Fase05AlinhamentoTests` (7): sem limites nada é conferido; raio e tangente abaixo do informado; vértice sem curva;
  rampas, K e comprimento das curvas verticais; fonte no CSV como foi escrita; V12 só com limites; configurações antigas
  sem o campo carregam com limites vazios.

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
