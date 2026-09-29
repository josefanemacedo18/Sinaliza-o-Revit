# Manual de uso – SinalizaBIM

Os comandos ficam em duas guias da faixa de opções: **SinalizaBIM** (vias, sinalização, detalhamento, edição) e
**SinalizaBIM Infra** (drenagem, obras de arte, contenções, relevo e terreno, nós viários) – separadas para que todos os
botões tenham espaço para o nome. As janelas mostram uma
**pré-visualização ao vivo** (com área pintada e extensão) gerada pelo mesmo motor que cria os
elementos no Revit, e avisos quando uma dimensão sai do intervalo de referência do MBST.

Convenções usadas em todo o plugin:

* Unidades em **metros**; aceita vírgula ou ponto decimal.
* **Deslocamento lateral positivo = à esquerda** do sentido em que o caminho foi desenhado.
* **Circulação pela direita** (Brasil) para posicionar linhas de retenção.
* O **sentido do tráfego** de setas e legendas é indicado pelo 2º clique.

### Organização da faixa de opções

| Painel | Ferramentas |
|---|---|
| **Vias** | **Via** (Via · Pista · **Via Férrea** · Desenhar Eixo) · **Conexões** (**Rotatória** · Interseção · Trocar Conexão) · **Calçadas** (Meio-fio/Calçada/Sarjeta · Orelha · Área de Calçada · Canteiros · Cul-de-sac · Rampas) · Hierarquia Viária · **Elementos Urbanos** (do plugin · famílias do Revit) · Mostrar/Ocultar Eixos |
| **Sinalização Horizontal** | **Linhas** (longitudinal · transversal · faixa de pedestres) · **Zebrado** (inclui área de conflito MAC) · **Inscrições** (setas/símbolos · legendas) · Vagas · **Complementos** (piso tátil · ciclovia · quebra-mola · cruzamento rodoferroviário) |
| **Sinalização Vertical** | **Placas** (placas · detalhar placas · mover chamada de placa · quadro de placas · quadro de legenda) · **Bloqueios Físicos** (todos os dispositivos, inclusive **tachas e tachões** e guard rail) |
| **Detalhamento** | **Detalhar** (anotar · cotar seção com perfil transversal · detalhe típico · quadro de quantitativos · notas · norte) · Quantitativos |
| **Tráfego** | **Simulador de Tráfego** (capacidade, nível de serviço, microssimulação animada e diagnóstico do projeto – seção 26) |
| **Editar** | Editar · Apagar Trecho · Atualizar Todas · Selecionar Conjunto · Alternar 2D/3D · Configurações · Catálogo · Normas |

Guia **SinalizaBIM Infra**:

| Painel | Ferramentas |
|---|---|
| **Drenagem** | boca de lobo / PV · grelha |
| **Obras de Arte** | **Pontes e Viadutos** (viaduto · ponte · passarela) · **Túneis e Trincheiras** (túnel · trincheira) |
| **Contenções** | muro de arrimo · talude |
| **Relevo e Terreno** | Perfil da Via · Terraplenagem |
| **Nós Viários** | Nó Viário (interseção em desnível) |

Os botões com seta (▾) agrupam ferramentas afins; o botão mostra a última usada do grupo.

---

## 1. Caminhos (eixos) e associatividade

Qualquer marca linear, zebrado ou conjunto de vagas pode usar como caminho:

| Opção | Resultado |
|---|---|
| **Selecionar linhas existentes** | Linhas de modelo ou de detalhe (retas, arcos, splines, elipses). Várias linhas são encadeadas automaticamente, invertendo o sentido quando necessário. **Associativo**: editar as linhas regenera a marca. |
| **Selecionar bordas (arestas)** | Bordas de **pisos** (seus ou do plugin), calçadas, lajes, topografia (Toposolid), paredes e telhados – clique as arestas em planta e *Concluir*. **Associativo**: editar o contorno do piso regenera a marca. Se a borda deixar de existir (piso apagado ou regenerado), a marca usa o traçado guardado na criação. |
| **Desenhar por pontos** | O plugin cria linhas de modelo no estilo *SV - Eixo de sinalização* (tracejado magenta) e as associa à marca. |
| **Dois cliques** | Para marcas transversais: cada par de cliques cria uma marca (repete até ESC). Não associativo. |

Se as linhas selecionadas formarem trechos separados, cada trecho recebe o padrão de forma
independente (o plugin avisa).

**Posição em relação à linha** (linhas, meios-fios, calçadas, sarjetas, gramados, faixas de caminhada,
ciclovias, dispositivos e canteiros):

| Opção | Resultado |
|---|---|
| **Centralizado na linha** | O eixo do elemento fica sobre a linha (padrão das marcas pintadas). |
| **Borda na linha – indicar o lado com um clique** | Depois de escolher a linha/borda, clique do lado em que o elemento deve ficar: a face lateral do elemento fica exatamente sobre a linha e ele cresce para esse lado. Ideal para encostar meio-fio, sarjeta e calçada na borda de um piso. |
| **Borda na linha – elemento à esquerda / à direita** | O mesmo, com o lado fixo (esquerda/direita no sentido em que a linha foi desenhada). |

O *deslocamento lateral* continua valendo e é somado a partir da borda. A largura é medida na própria
geometria gerada, por isso funciona também em curvas, linhas duplas e dispositivos.

> Dica: coloque o estilo *SV - Eixo de sinalização* como invisível nas vistas de prancha
> (Visibilidade/Gráficos → Linhas) para imprimir apenas a sinalização.

**Desenhar Eixo** cria apenas o eixo, para uso posterior, de duas formas:

* **Ferramenta nativa do Revit** (*Linha de modelo*): reta, arco, spline, cadeia, deslocamento,
  retângulo e todos os snaps do Revit. Escolha o estilo *SV - Eixo de sinalização* (opcional) e depois
  selecione as linhas no Sinalizar Via, na Pista ou em qualquer ferramenta (elas passam a usar o estilo de eixo).
* **Por pontos**: com encaixe nas vias existentes e curvas concordadas com o raio informado.

Todas as ferramentas que pedem pontos usam os snaps do Revit (pontas, meios, interseções, perpendicular,
centros, mais próximo).

---

## 2. Sinalizar Via – seção transversal completa

1. Escolha um **modelo pronto** (via local, coletora, avenida com canteiro, corredor de ônibus,
   faixa preferencial, ciclofaixa segregada, canteiros laterais, rodovias, mão única) ou um **modelo
   personalizado ★** e clique *Aplicar* – ou monte a seção do zero. **Salvar como modelo…** guarda a seção
   atual com um nome (use o mesmo nome para substituir); **Excluir modelo** remove um personalizado. A
   janela reabre sempre com a **última seção usada**.
2. **Mão dupla** (o eixo divide os sentidos) ou **mão única** (todas as faixas no sentido do eixo).
3. **Eixo / canteiro central**: LFO-1/2/3/4, sem marca ou canteiro central **físico** (meios-fios +
   grama) ou **pintado** (zebrado amarelo), com dispositivo opcional sobre o eixo (ex.: New Jersey,
   balizadores).
4. Monte cada lado **do eixo para fora** (botões *Adicionar, Remover, ▲▼, Copiar para o outro lado*),
   escolhendo o elemento e a largura:

| Elemento | O que é gerado |
|---|---|
| Faixa de rolamento | Linhas LMS entre faixas; LBO junto a acostamento, canteiro ou calçada. |
| Faixa exclusiva (ônibus) | Linha MFE contínua na divisa com as faixas comuns + legenda repetida (ÔNIBUS). Opcional: **pintura colorida de fundo** em toda a faixa (vermelha, azul, verde, amarela, laranja ou marrom – como na ciclofaixa e na faixa de caminhada), entre as linhas de bordo (código ONI-FD). |
| Faixa preferencial (ônibus) | Linha MFE seccionada + legenda repetida; mesma opção de fundo colorido. |
| Ciclofaixa | Linha CIC-LD, pintura vermelha, bicicletas e setas repetidas; segregação física opcional. |
| Faixa de estacionamento | Vagas do tipo escolhido (paralelas, em ângulo, PcD, carga e descarga...) com linha de fundo no lado da pista. |
| Acostamento | LBO no bordo da faixa. |
| Faixa de segurança / transição | Zebrado com linhas de canalização (buffer entre fluxos, ciclofaixa etc.). |
| Canteiro lateral físico / pintado | Meios-fios + grama (0,15 m) ou zebrado. |
| Calçada | Meio-fio, faixa de serviço (gramada ou em concreto), faixa livre e faixa de acesso; aviso se a faixa livre for menor que 1,20 m (NBR 9050). |

**Larguras da via: sarjeta e meio-fio contam na largura.** Com *A sarjeta soma na largura da pista* (ligado nas vias
novas), a **sarjeta** fica entre a última faixa e o meio-fio e **soma** na largura da pista: uma coletora com faixas de
3,30 + 3,50 m e sarjeta de 0,30 m tem **7,10 m** por lado até a face do meio-fio – as faixas continuam com a largura
útil informada e a linha de bordo fica na faixa, 0,10 m antes da sarjeta. Desligado (e nas vias criadas por versões
anteriores), a sarjeta fica dentro da faixa junto ao meio-fio, como antes. A **largura do meio-fio** (guia) também é
personalizável na criação: selecione a calçada e informe *Largura do meio-fio (m)* (0,05 a 0,60; padrão 0,15). A janela
mostra, sempre atualizadas:

* **entre meios-fios** – faixas + sarjetas (a pista, de face a face das guias);
* **com meios-fios** – a pista mais a largura das duas guias;
* **total** – de fora a fora, com as calçadas e os canteiros;
* e a composição (faixas, sarjetas, meios-fios, calçadas, canteiros e canteiro central).

**Nível de cada elemento (coluna *Nível (m)* e campo *Nível do topo em relação à pista*).** Cada elemento da seção
tem o seu nível, medido do topo do pavimento: vazio/padrão = calçada e canteiro físico a **+0,15**, faixas a **0**.

* **Faixas acima de 0** (ciclofaixa, estacionamento, ônibus, faixa de caminhada, rolamento) viram **plataforma
  elevada** (código PLATAFORMA – laje de concreto; vermelha na ciclofaixa com pintura de fundo) com **meio-fio no
  degrau**, e a pintura, os símbolos e as legendas do elemento ficam assentados sobre ela. Ex.: ciclovia no nível da
  calçada – ciclofaixa a 0,15: um meio-fio entre a pista e a ciclovia e **nenhum** entre a ciclovia e a calçada;
  sem sarjeta atrás da plataforma.
* **Calçada**: qualquer nível (0 = calçada no nível da pista; 0,20 = guia alta; negativo = rebaixada). O meio-fio só é
  criado onde há degrau para o elemento interno, com o topo no nível mais alto.
* **Vegetação** (faixa de serviço gramada da calçada e canteiro físico, campo *Nível da vegetação*): mais alta
  (floreira), no nível ou **rebaixada** (jardim de chuva / biovaleta). No canteiro rebaixado as guias ficam a 0,10 m.
* **Canteiro central físico**: *nível guia / vegetação* no quadro do eixo.
* Níveis acima da pista: o sólido nasce na pista; no nível ou abaixo dela vira uma laje de 0,10 m com o topo no nível
  pedido. As conexões (interseções, rotatórias, cul-de-sac) copiam esses níveis, pois leem os elementos da via.

5. **Elemento selecionado**: opções do elemento (tipo de vaga, legenda e espaçamento, faixas da
   calçada, pintura da ciclofaixa, **segregação física** na divisa interna – tartarugas, balizadores,
   tachões, New Jersey...).
6. **Linhas e opções**: velocidade (define largura e tracejado), divisórias, bordos, recuos, tachas no
   eixo, inscrições repetidas e elementos físicos.

Todos os elementos são associados ao eixo e pertencem ao mesmo **grupo**: mover o eixo atualiza a via
inteira; **Selecionar Conjunto → Todo o grupo** seleciona tudo. Cada elemento pode ser ajustado
depois com **Editar**.

### 2.0.1 Largura variável ao longo do eixo (levantamento)

Ruas existentes quase nunca têm largura constante: num ponto A a pista tem 10,00 m de meio-fio a meio-fio, num
ponto B 10,50 m; um muro antigo avança sobre a calçada. No quadro **Largura variável ao longo do eixo (levantamento)**
(Nova Via, Sinalizar Via, Pista e Editar via) informe, em cada **estaca**, a distância do **eixo** ao **bordo da pista**
(face do meio-fio / sarjeta) e ao **alinhamento** (muro, divisa do lote) de cada lado. Vazio = medida da seção.

* **Estaca** em metros (`110`) ou em estacas de 20 m (`5+10` = 110 m). *Adicionar entre meios-fios* cria a linha já com a
  largura total pedida na estaca (ex.: 10,50 m), dividida pelos dois lados.
* Entre as estacas a largura varia **gradualmente** (linear ou em **curva S suave**); antes do primeiro e depois do
  último ponto fica constante.
* **Quem absorve a variação na pista**: a faixa junto ao meio-fio (as outras mantêm a largura), todas as faixas
  proporcionalmente, ou o acostamento/estacionamento. A linha de bordo, a sarjeta, o meio-fio e as calçadas
  acompanham o bordo; o alinhamento estreita (ou alarga) a **faixa livre** – aviso se ela ficar abaixo de 1,20 m (NBR 9050).
* **Ler do desenho**: marque *Depois de indicar o eixo, ler do desenho os meios-fios e muros existentes* e, depois do
  eixo, clique as linhas do levantamento (meio-fio direito, esquerdo, alinhamento direito, esquerdo – **ESC** pula o
  lado). O plugin mede a distância a cada 5 m e cria os pontos sozinho (remove os colineares).
* As **estacas são ancoradas** ao desenho: estender ou mover o eixo mantém cada medida no seu lugar.
* Interseções, rotatórias e balões usam **a largura que a via tem naquele ponto**.

### 2.0.2 Recuos do meio-fio: baias de ônibus e faixas de aceleração/desaceleração

No quadro **Recuos do meio-fio** adicione, por estaca e lado:

| Tipo | Geometria | Sinalização gerada |
|---|---|---|
| **Baia de ônibus** | O meio-fio recua (profundidade, taper de entrada e de saída, curva reversa opcional); a faixa de tráfego continua reta. | MVE amarela (retângulo da baia), legenda ÔNIBUS, **LCO** na boca da baia, placa de ponto e abrigo na calçada. |
| **Faixa de desaceleração** | Taper + faixa auxiliar paralela antes da saída/conversão. | LCO separando a faixa + setas de conversão (PEM). |
| **Faixa de aceleração** | Faixa auxiliar paralela + taper de convergência depois da entrada. | LCO + setas de mudança de faixa (SMF). |
| **Embarque / táxi / carga** | Recuo curto com legenda livre. | MVE + legenda. |

Os valores iniciais vêm da **velocidade da via** (DNIT – Manual de Projeto de Interseções; AASHTO; indicativos).
*Calçada recua* = o alinhamento fica e a calçada estreita (aviso NBR 9050); desmarcado, a calçada inteira se desloca.
O simulador de tráfego lê as baias (ônibus param fora da faixa) e as faixas auxiliares (§ 26).

**Recuo numa via já criada.** **Conexões → Recuo na Via (baia / faixa auxiliar)**: clique junto ao bordo da via, no
início do recuo e do lado dele – a janela abre com a estaca e o lado já preenchidos, os valores pela velocidade e a
prévia sobre a seção da própria via. A via é refeita com o recuo (meio-fio, calçada, sarjeta, linhas, MVE, LCO,
setas, placa e abrigo) mantendo eixo, greide, trechos apagados e conexões. Para **alterar ou remover** um recuo
depois, use **Editar** sobre a marca do recuo (MVE, LCO ou setas): a janela do recuo abre direto, com a opção
*REMOVER este recuo da via*. Também continua valendo **Editar → A via inteira → Recuos do meio-fio**.

**Sinalização limpa no recuo.** A marcação da via acompanha o meio-fio recuado sem se misturar: as vagas de
estacionamento são interrompidas na extensão da baia/faixa auxiliar (só vagas inteiras antes e depois, nunca dentro da
baia), a linha de bordo e as demais linhas seguem o novo bordo com o seu próprio afastamento, e as interrupções ficam
amarradas ao eixo – ao prolongar, aparar ou editar a via elas continuam no lugar certo.

### 2.0.3 Calçadas com níveis variáveis (topografia e edificações antigas)

Selecione a calçada e informe:

* **Inclinação transversal (%, sobe para o lote)** – o caimento para a sarjeta (NBR 9050: até 3 % na faixa livre).
* **Nível no alinhamento (estaca:nível; …)** – ex.: `0:0; 60:0,35; 120:0,10` – o nível do piso junto ao lote em cada
  estaca, em relação ao topo do meio-fio (edificações antigas mais altas, soleiras, rampas de garagem).

A **faixa livre** mantém a inclinação transversal; a **faixa de acesso** (junto ao lote) vira **rampa** para absorver o
desnível. Avisos: inclinação transversal acima de 3 % e rampa longitudinal acima de 8,33 % (NBR 9050 – prever
patamares/degraus no lote). Os pisos da calçada acompanham os níveis (e ficam mais espessos onde sobem).

### 2.0.4 Sinalização horizontal como piso (edição fácil)

Em **Configurações → Sinalização horizontal … como Piso do Revit** (ligado por padrão) as linhas, zebrados, setas,
legendas e vagas são criadas como **pisos finos** (uma cor = um piso com todos os contornos) assentados sobre o
pavimento – como as calçadas e a pista. Assim dá para editar o contorno com as ferramentas do Revit, filtrar por
material e quantificar pela área real. Desligado, volta o modo anterior (sólidos/regiões).

### 2.0.5 Via nova a partir da ponta de outra (emenda concordada)

Quando uma via nova começa (ou termina) exatamente na **ponta** de uma via existente – pelo ímã de conexão ou
desenhando a partir dela – e as duas mudam de direção, o encontro vira uma **emenda concordada**, não um cruzamento:
a pista, a sarjeta, o meio-fio, a faixa gramada, a calçada e o canteiro central seguem por uma **curva** entre os dois
eixos (raio no eixo = o maior entre o raio de esquina da conexão e duas vezes a meia largura da seção mais larga),
sem a cunha vazia do lado de fora nem a sobreposição
do lado de dentro. Se as seções forem diferentes (largura de pista, calçada ou canteiro), a emenda faz a **transição
suave de largura** (1 : 8 por lado). As **linhas pintadas continuam pela curva** (eixo, divisórias, bordos – cada
linha liga à linha correspondente da outra via; as sem par vão até o meio da emenda). A emenda não recebe faixas de
pedestres, retenções nem placas, e o simulador a trata como continuação livre. Deflexão acima de 60° gera um aviso
(considere interseção em T, rotatória ou curva de raio maior). Em vias já criadas, use **Atualizar Todas**.

### 2.0.6 Várias linhas de uma vez: uma malha inteira de vias

Em **Sinalizar Via** / **Pista** com *Selecionar linhas existentes*, selecione todas as linhas do traçado de uma vez
(por exemplo, uma malha desenhada com linhas de referência). O plugin separa as linhas em **vias**:

* linhas encadeadas **só entre si** pela ponta (curva, deflexão) formam uma via só;
* num nó com três ou mais linhas, as que **seguem alinhadas** (desvio até 25°) continuam a mesma via – a rua que
  atravessa o cruzamento – e as demais viram vias próprias;
* uma linha que termina no **meio** de outra é outra via (entroncamento em T).

Cada via recebe a seção escolhida e as **interseções** são criadas em todos os encontros (T, cruz, esconsos), como se
as vias fossem criadas uma a uma. A largura variável e os recuos, que valem para um eixo só, ficam para a edição de
cada via (a janela avisa).

### 2.0.7 Pistas feitas com Piso comum (projetos antigos e formas complexas)

Quando a pista foi modelada à mão com **Piso** (projeto antigo, forma difícil de montar pelo plugin), use em
**Sinalizar Via** / **Via** a opção de caminho **Reconhecer em pisos existentes (eixo e largura do contorno do piso)**:

1. Escolha o modelo de seção (a sinalização que a via deve receber) e, em **Pavimento**, *Nenhum (pista já modelada)*
   – a opção é sugerida sozinha, para o piso existente não ser duplicado.
2. Clique em Criar e selecione os **pisos** da pista (um ou vários; podem estar encostados ou sobrepostos) → Concluir.
3. O plugin mostra o que reconheceu (**vias, comprimentos, larguras e encontros**) e, ao confirmar, cria as **linhas de
   eixo** (editáveis como qualquer eixo) e as vias, cada uma com a seção **ajustada à largura medida** – as faixas são
   escaladas para caber de bordo a bordo; onde a pista alarga ou estreita entram **estacas de largura variável**.
4. As **interseções** nascem nos encontros (T, cruz), como numa malha desenhada.

Como funciona: o contorno dos pisos é unido, triangulado e o **eixo cordal** liga os meios das cordas de bordo a bordo;
ramos curtos das quinas são podados, a via que atravessa um encontro segue reta, o eixo é recentrado entre os bordos e as
pontas vão até o bordo do piso. Canteiros (furos no piso) viram pistas separadas. A partir daí a via vale para tudo:
sinalização, interseções, rotatórias, recuos, cotas, quantitativos e o **Simulador de Tráfego**.

> Confira o **sentido** do eixo (vias de mão única) e a hierarquia; pistas muito irregulares podem pedir um ajuste manual
> das linhas de eixo criadas – depois use Editar na via.

## 2.1 Hierarquia viária (CTB art. 60)

Toda via criada recebe a sua **hierarquia viária** – campo **obrigatório** no topo da janela (os modelos
prontos já trazem uma sugestão): **trânsito rápido, arterial, coletora, local** (urbanas), **rodovia** ou
**estrada** (rurais). Escolher a hierarquia ajusta a velocidade padrão (CTB art. 61: 80, 60, 40 e 30 km/h
nas urbanas), que pode ser alterada.

A hierarquia é gravada em **todos os elementos** da via e da sua sinalização (parâmetro compartilhado
**SV_Hierarquia**, útil em tabelas e filtros de vista) e é usada:

* nos **quantitativos** (coluna, filtro e resumo por hierarquia – seção 10);
* nas **interseções**: a via de maior hierarquia é a **preferencial** (a secundária recebe PARE ou dê a
  preferência) e o raio das esquinas criadas automaticamente segue a hierarquia (6 m local, 8 m coletora,
  10 m arterial, 15 m rodovia/trânsito rápido) quando o campo de raio fica vazio.

**Raio de concordância das esquinas (raio da guia).** No **Sinalizar Via** (campo *Raio de
concordância das esquinas / guia*) e na **Pista** (*Raio das esquinas*) informe o raio desejado – por
exemplo 3 m para uma rua local estreita ou 12 m para acesso de caminhões. Vazio (ou 0) usa o padrão da
hierarquia. O valor fica gravado no pavimento e, no encontro de vias com raios diferentes, prevalece o
da **via de menor hierarquia** (a que faz a conversão). Para mudar o raio de vias já desenhadas use
**Via → Hierarquia e Esquinas**: selecione as vias, informe o novo raio e as interseções são refeitas.

Para delimitar uma via por **qualquer linha ou piso**, use **Hierarquia e Esquinas** selecionando linhas de
eixo/borda ou **pisos desenhados à mão**: as marcas que usam essas linhas/bordas recebem a hierarquia, e os pisos
ganham o parâmetro SV_Hierarquia e entram nos quantitativos (código PAV-PISO).

Para vias já existentes (ou criadas por versões anteriores) use **Via → Hierarquia Viária**: selecione
elementos das vias e escolha a hierarquia (opcionalmente ajustando a velocidade das linhas); as
interseções, rotatórias e cul-de-sacs dessas vias são atualizados.

## 2.2 Via desenhada por pontos – conexão natural com o sistema viário

### Ímã de conexão (como no InfraWorks)

Não é preciso nenhuma ferramenta para ligar vias: **puxe a ponta do eixo** de uma via (alça de arraste do
Revit, *Mover* ou editando a linha) **até o meio de outra via** – basta soltar sobre a pista ou a calçada
dela. A ponta vai sozinha para o **eixo** da outra via, **mantendo a direção da via** (prolonga ou apara a
sobra que passou do eixo), e a interseção se forma em qualquer ângulo, com todo o desenho das duas vias
ajustado. Soltando perto da **ponta** de outra via, as duas se emendam (continuação); dentro de uma
**rotatória**, a via vira um novo ramo. Afastando a ponta, a interseção é desfeita.

Quando uma via ligada é **movida**, as vias que chegam nela **acompanham**: a ponta que estava no
cruzamento é levada ao novo eixo. O mesmo ímã vale ao criar a via (desenhada ou por linhas
selecionadas). Pode ser desligado em **Configurações → Ímã de conexão**.

**Via → Via** com *Desenhar a via por pontos* (o antigo botão *Nova Via* foi unificado com *Sinalizar Via* – é a
mesma ferramenta) desenha a via clicando os pontos do
eixo, como no InfraWorks, e o sistema viário se ajusta sozinho:

* **Encaixe** (opção *Encaixar nas pontas e nos eixos das vias existentes*): clique **sobre uma via** –
  na pista ou na calçada – para ligar a nova via ao **eixo** dela (entroncamento em T ou, no meio do
  traçado, cruzamento); clique **perto da ponta** de uma via para **continuá-la**; clique **dentro de uma
  rotatória** para criar um **novo ramo**. A barra de status mostra o encaixe de cada ponto.
* **Curvas horizontais**: os vértices clicados são concordados com arcos do **raio** informado (reduzido
  onde as tangentes não cabem); o eixo fica com retas e arcos de modelo, editáveis no Revit.
* **Onde encontrar outra via**: **Interseção** (com os tipos e o controle da ferramenta Interseção),
  **Rotatória** (com os parâmetros da ferramenta Rotatória) ou **não ajustar**.
* **Pontas livres da via**: **cul-de-sac** (tipo e medidas da ferramenta Cul-de-sac, ajustados à largura
  e à calçada da via) ou sem tratamento.
* **Continuações**: duas vias emendadas em linha reta ficam simplesmente contínuas; emendadas em ângulo,
  a curva do meio-fio é arredondada, sem faixas, retenções ou placas.

Depois de criada, a via continua ligada: mover ou editar o eixo refaz interseções, rotatórias
(que acompanham o nó e ganham/perdem ramos) e cul-de-sacs.

**Via → Conexão** muda o tratamento de uma conexão existente: clique num **encontro de vias** ou numa
**rotatória** para escolher *Interseção*, *Rotatória* ou *Sem tratamento*, ou na **ponta livre** de uma via
para *Cul-de-sac* ou *Sem tratamento*. A geometria e a sinalização das vias são refeitas.

## 2.3 Pista – montagem da via passo a passo

**Via ▾ → Pista** cria só a **parte dos veículos** (asfalto, bloquete ou concreto) com a hierarquia, as
larguras à direita e à esquerda do eixo, mão dupla/única e, opcionalmente, linha de eixo e linhas de
bordo – já **conectada** às vias existentes (interseção simples). Depois monte o restante elemento por
elemento com **Calçadas ▾ → Meio-fio, Calçada e Sarjeta** no caminho **Junto ao bordo de uma via**:
clique ao lado da via, no lado desejado, e o elemento é colocado encostado no bordo:

* **meio-fio, calçada, grama** são empilhados para fora (o 2º elemento vai depois do 1º, e assim por diante);
* **sarjeta** fica dentro da pista, junto ao bordo; **linhas pintadas** logo depois dela.

Esses elementos passam a fazer parte da via (mesmo grupo) e a seção registrada no pavimento é atualizada –
as interseções, rotatórias e cul-de-sacs refazem também as calçadas e meios-fios acrescentados.

## 2.3.1 Curvas do eixo

Cantos vivos do eixo (linhas selecionadas com quebra, ou pontos clicados) são **arredondados** com o raio das
curvas informado na janela (**nunca menor que a meia largura da via + 1,5 m**): a borda interna, as linhas de
bordo, os meios-fios e as calçadas passam a acompanhar a curva, sem dobras. Para vias já criadas, clique no
asfalto → **Editar** → *Raio das curvas do eixo* (0 = cantos como desenhados).

## 2.5 Sonorizador longitudinal (rumble strip)

**Bloqueios Físicos → Sonorizador Longitudinal.** Elementos em série ao longo de uma linha – no **acostamento**, sobre
o **bordo** (LBO) ou no **eixo** – que vibram e fazem ruído quando o pneu passa por cima, alertando o motorista que sai
da faixa por sono ou distração (saídas de pista e colisões frontais).

| Tipo | Valores iniciais (indicativos – FHWA/DNIT) |
|---|---|
| **Fresado** no pavimento | ranhuras 18 × 40 cm (30 cm no eixo) a cada 30 cm, 13 mm de profundidade, escuras |
| **Termoplástico com relevo** (linha perfilada) | linha-base contínua + relevos 5 × 15 cm a cada 50 cm, 6 mm – continua visível à noite e com chuva |
| **Barras em relevo** | barras 10 × 30 cm a cada 60 cm, 8 mm |
| **Tachas sonorizadoras** | 10 × 10 cm a cada 1 m, 15 mm, amarelas |

Tudo é personalizável: comprimento, largura, espaçamento, profundidade/altura, **ângulo** (chevron), cor, deslocamento
em relação à linha, **trechos com interrupções** (ex.: 12 m de sonorizador e 3,6 m livres para o ciclista passar) e
recuos no início/fim (deixe livres interseções, acessos e pontes). Quantitativo em unidades. Avisos: fresado com mais
de 16 mm; acostamento sem interrupções para bicicletas.

## 2.6 Área de escape de caminhões (caixa de retenção)

**Bloqueios Físicos → Área de Escape.** Para descidas longas e íngremes, onde o caminhão pode perder o freio
(superaquecimento). Desenhe o **eixo da caixa** começando no bordo da pista, no sentido em que o veículo entra
(saída tangente, à direita).

* **Comprimento** calculado por L = V² / 254 (R + G): velocidade de entrada (AASHTO: 130–140 km/h), **rampa** da caixa
  (aclive reduz o comprimento) e **material** do leito – seixo rolado uniforme (R = 0,25, recomendado), areia (0,15),
  cascalho solto (0,10) ou brita (0,05). Informe o comprimento para fixá-lo – o aviso mostra quando ele é menor que o
  necessário.
* **Largura** (8 a 12 m), **profundidade do leito** (0,90–1,10 m) com **transição** na entrada (de 8 cm à profundidade
  total nos primeiros 30–60 m), meios-fios de contenção, **faixa de serviço** pavimentada ao lado (retirada do
  veículo) com **âncoras** de reboque, **berma** de material no fim, **zebrado de entrada** e **delineadores**.
* O volume de material aparece na prévia. O Simulador de Tráfego acusa **descida longa sem área de escape** (§ 26).

## 2.7 Imagem de Satélite – referência na escala métrica real

Guia **SinalizaBIM → Vias → Imagem de Satélite** (com uma vista de **planta** aberta). Traz a imagem aérea do local para a
vista, atrás de tudo, na **escala métrica real** e na **posição geográfica do projeto** – desenhe o eixo por cima
(*Desenhar Eixo*, *Via*, *Pista*) com a certeza de que 1 m na imagem é 1 m no Revit.

**Passo a passo**

1. Abra a vista de planta onde a via será desenhada.
2. *Imagem de Satélite* → escolha a **fonte**: *Google Satélite* (foto aérea, precisa da sua chave) ou *OpenStreetMap*
   (mapa das ruas – **não é foto de satélite**, não precisa de chave).
3. **Centro (lat, lon)**: no Google Maps, clique com o botão direito no local e clique nas coordenadas para copiá-las;
   cole no campo (aceita `-23.550520, -46.633308`, vírgula decimal ou `23.5505° S 46.6333° W`).
4. **Largura × altura** (até 3 km × 3 km), **resolução** (0,30 m/px por padrão – o zoom da fonte é escolhido por ela)
   e **altitude média** do local.
5. Na **primeira imagem** escolha o ponto do projeto que recebe o centro: *centro da vista atual* ou *origem interna
   (0, 0)*. Essa passa a ser a **origem geográfica do projeto**, gravada no arquivo: as próximas imagens encaixam
   exatamente nela e nos eixos já desenhados (*Redefinir a origem* só se quiser recomeçar).
6. **Prévia** (opcional) e **Baixar e colocar na vista**. O download e a reprojeção rodam em segundo plano, com
   progresso e botão *Parar*; o Revit só recebe a imagem no fim, num único *Desfazer*.

O painel mostra, antes de baixar: zoom, **metros por pixel efetivos**, pixels e blocos, número de tiles e MB estimados
(os já baixados vêm do cache `%AppData%\SinalizaBIM\tiles`) e o **erro máximo estimado da reprojeção**.

**O que é colocado:** a imagem em blocos de até 4000 px (imagens do Revit incorporadas ao projeto, lado a lado, camada
*Fundo*, **fixadas**), uma **escala gráfica** de 0–10–50–100 m (ou menor) e o texto de **créditos** (© Google /
© OpenStreetMap contributors, fonte, zoom, m/px e data) – obrigatório pelos termos das fontes. *Substituir a imagem
anterior* apaga de uma vez a imagem, a escala e os créditos da vez anterior.

**Como a escala fica certa.** Os tiles vêm na projeção Web Mercator, cuja escala muda com 1/cos(latitude) (em Brasília,
mais de 8 %) e nem é igual nos sentidos norte–sul e leste–oeste do elipsoide (≈ 0,5 % de diferença no Brasil). Por isso a
imagem **nunca é esticada** por um fator único: cada pixel do Revit é recalculado (interpolação bilinear) a partir do
ponto exato do mosaico, pela cadeia

modelo → rotação do **Norte verdadeiro** do projeto (*Gerenciar → Local → Posição*) → plano local no terreno →
**fator de altitude** R/(R+h) → **Transversa de Mercator local** no elipsoide GRS80/SIRGAS 2000, com meridiano central e
origem no centro do projeto e **k0 = 1** → latitude/longitude → pixel do tile.

Na TM local a distorção é menor que 1 ppm a alguns km do centro (no UTM a escala varia de 0,9996 a 1,0010 – até
40 cm/km); o fator de altitude faz as medidas valerem no terreno (a 800 m de altitude são ~12,5 cm/km). Testes do
núcleo: ida e volta lat/lon → plano < 0,001 mm; plano × geodésica (Vincenty) < 0,2 ppm em pares de 1 e 3 km;
erro da grade de interpolação < 0,001 mm.

**Chave do Google (Map Tiles API).** No [Google Cloud Console](https://console.cloud.google.com/): crie (ou escolha) um
projeto → *APIs e serviços* → ative a **Map Tiles API** → configure a **cobrança** → *Credenciais* → *Criar chave de API*
(recomendado: restringir a chave à Map Tiles API). Cole em **SinalizaBIM → Configurações → Imagem de satélite** (ou no
botão *Chave do Google…* da janela). A chave fica **cifrada** no seu usuário do Windows (DPAPI) e **nunca** vai para o
projeto, para o log ou para o código. O plugin usa só a API oficial (sessão de tiles 2D, tipo *satellite*); o uso é
cobrado por tile na sua conta conforme a tabela do Google – o número de tiles aparece antes de baixar.

**Limites de uso.** Google: até 2500 tiles por imagem e zoom máximo que o Google tem na área (consultado antes de baixar).
OpenStreetMap: até 300 tiles por imagem, zoom até 19, no máximo 2 downloads simultâneos, cache respeitando a validade
informada pelo servidor e identificação do plugin – conforme a *Tile Usage Policy* da OSM Foundation (sem download em
massa). Acima dos limites a resolução é reduzida automaticamente (com aviso) ou reduza a área. Sem internet, sem chave ou
com erro do servidor aparece a mensagem e **nada é criado** no projeto.

**Precisão.** As medidas **dentro** da imagem ficam na escala correta; a **posição absoluta** de imagens de satélite tem
erro típico de alguns metros (e pode haver deslocamento entre Google e OSM). Para projeto executivo, **ortofoto oficial
ou levantamento topográfico prevalecem**.

**Escala conferida no próprio Revit.** O tamanho que o Revit dá a uma imagem depende da resolução (DPI) do arquivo, e o
parâmetro *Largura* da imagem é "na vista, depois de escalar" – não é garantido que um valor gravado nele vire a mesma
medida no modelo. Por isso cada bloco é **medido no modelo** pelos cantos da imagem depois de colocado e corrigido até a
diferença ficar **abaixo de 1 mm**; o relatório final mostra *esperado × obtido* de cada bloco e o passo a passo completo
fica em `%AppData%\SinalizaBIM\imagem-aerea-diagnostico.txt` (tamanho após criar, após definir a largura, após posicionar
e caixa envolvente).

### 2.7.1 Imagem nítida de qualquer origem – Importar Imagem Aérea

Guia **Vias → Imagem Aérea → Importar Imagem Aérea**. Aceita JPG, PNG, TIF ou BMP de qualquer origem – exportação do
Google Earth Pro, ortofoto da prefeitura, voo de drone. **A licença da imagem é de quem a obteve.**

* **Com world file** (`.jgw`, `.pgw`, `.tfw`, `.wld` ao lado do arquivo) ou **GeoTIFF**: a imagem é reprojetada para o
  plano do projeto e entra **na escala e na posição corretas**. O sistema vem do GeoTIFF (EPSG 4674/4326, SIRGAS 2000 UTM
  31978–31985) ou do `.prj`; se não houver, a janela pergunta (geográficas, UTM SIRGAS 2000 fuso/hemisfério ou metros do
  projeto). Uma ortofoto em UTM é corrigida do fator de escala do fuso (até 0,1 %) e da convergência meridiana (a grade UTM
  fica girada em relação ao Norte verdadeiro – ~0,27° a 265 km do meridiano central).
* **Sem georreferência** (ex.: imagem salva do Google Earth Pro): informe o tamanho aproximado do pixel, a imagem é colocada
  no centro da vista e a **calibração por 2 pontos** abre em seguida.

**Receita com o Google Earth Pro:** posicione a vista de cima (tecla *U*, Norte para cima – tecla *N*), marque dois
**marcadores** em esquinas bem visíveis e afastadas (anote a latitude/longitude de cada um em *Propriedades*), salve a
imagem (*Arquivo → Salvar → Salvar imagem*), importe no plugin e calibre com as coordenadas dos dois marcadores – escala,
posição e Norte verdadeiro ficam certos.

### 2.7.2 Calibrar Escala da Imagem

Guia **Vias → Imagem Aérea → Calibrar Escala da Imagem** (ou logo após importar). Selecione a imagem, clique **2 pontos
sobre ela** (quanto mais afastados, melhor) e informe:

* a **distância real** entre eles (medida no Google Earth com a régua, na planta do loteamento ou em campo) – a imagem é
  redimensionada em torno do 1º ponto (ex.: medido 93,70 m, real 100,00 m → fator 1,06724); ou
* a **latitude/longitude** dos dois pontos – além da escala, a imagem vai para a posição geográfica do projeto e é girada
  para o Norte verdadeiro (se o Revit não permitir girar a imagem, o relatório avisa).

O tamanho final é medido no modelo e mostrado (esperado × obtido).

### 2.7.3 Fonte própria (XYZ)

Em **Configurações → Imagem aérea – fonte própria**, informe o endereço de um serviço de tiles no formato
`https://…/{z}/{x}/{y}` (ou `{-y}` para TMS) que **você tem direito de usar** (conta própria em um provedor de imagens,
servidor de ortofotos da prefeitura ou da empresa), o nome, os créditos e o zoom máximo. Ela aparece como fonte na janela
*Imagem de Satélite*, com as mesmas regras de cache, limite de tiles, reprojeção e conferência de escala. O plugin não traz
endereços pré-preenchidos: a licença e os créditos são de quem configura. O endereço fica só neste computador.

**Conferir a escala no Revit.** Com *Anotar → Alinhada*, meça a escala gráfica (deve dar exatamente 10/50/100 m) e uma
distância conhecida na imagem (ex.: a mesma distância medida no Google Earth) (largura de uma quadra, uma faixa de pedestres, um campo de futebol oficial de 105 m).

## 2.4 Bloqueios físicos

Comando **Bloqueios Físicos** (painel *Sinalização Vertical*) – **uma única ferramenta** para todos os
dispositivos físicos. As **tachas e tachões refletivos** fazem parte dela: aparecem no topo da lista (grupo *Tachas e
tachões refletivos*); ao escolher um e clicar *Continuar ›* abre a janela das tachas já nesse tipo (variante mono/
bidirecional, cor, cadência pela velocidade, caminho). O botão *Tachas e Tachões* saiu de *Complementos* e ficou como
atalho no menu de **Bloqueios Físicos**. Também estão aqui inclusive as **defensas metálicas (guard rail)**, que antes tinham um botão próprio
(o que duplicava a ferramenta). A lista é agrupada por família: *segregadores e tachões*, *balizadores,
pilaretes e frades*, *barreiras de concreto*, *defensas metálicas (guard rail)*, *gradis e floreiras* e
*canalização provisória (obras)*. Na **Sinalizar Via**, um mesmo dispositivo nunca é repetido no mesmo
alinhamento. Escolha o dispositivo, ajuste
dimensões, espaçamento entre centros (0 = contínuo), deslocamento e recuos, e selecione/desenhe o
caminho. Os dispositivos são modelados em 3D com a forma real e aparecem nos quantitativos em
unidades ou metros:

* **Esfera de concreto**, **floreira de concreto com vegetação** e **gradil de proteção de pedestres** (bloqueios
  urbanos); **cone**, **cavalete listrado** e **tambor canalizador** (obras);
* **Tartaruga (segregador)**: cúpula elíptica amarela com refletivos brancos nas duas faces;
  **tachão**: tronco trapezoidal com refletivos;
* **Balizador flexível**: base preta, haste com duas faixas refletivas brancas e topo arredondado;
  **cilindro delimitador**: base preta e duas faixas refletivas; **pilarete/frade**: fuste de concreto
  com topo abaulado e faixa amarela; **prisma**: tronco de pirâmide chanfrado;
* **Barreira New Jersey**: perfil padronizado (0,60 × 0,81 m, pé de 7,5 cm, faces a 55° e 84°, topo de
  15 cm) extrudado ao longo do caminho – contínua ou em módulos; **barreira plástica** com topo
  arredondado e módulos alternando **vermelho e branco**; **separador contínuo** com topo chanfrado;
* **Defensa metálica**: postes, espaçadores e **lâmina em "W"** (31 cm, chapa fina) de um ou dos dois
  lados; **defensa de cabos** com três cabos.

## 3. Linha Longitudinal / Transversal / Complementos

A mesma janela atende marcas longitudinais, transversais, tachas, piso tátil e ciclovia; muda apenas
a lista de tipos.

| Campo | Descrição |
|---|---|
| Variante | Dimensões do catálogo. *Pela velocidade* escolhe a variante automaticamente. |
| Largura | Substitui a largura (linhas duplas mantêm o vão entre as linhas). Na FTP-1, é a largura da faixa no sentido do tráfego; na LRV, a extensão transversal. |
| Traço / espaço | Substitui o tracejado. |
| Alinhamento | *Início*, *Fim*, *Centro* (simétrico) ou *Ajustar* (número inteiro de traços, com traço nas duas pontas). |
| Fase | Desloca o tracejado ao longo do caminho. |
| Recuo início/fim | Interrompe a marca antes das extremidades. |
| Inverter sentido / lados | Inverte o caminho; espelha linhas duplas (LFO-4). |
| LRV: velocidade inicial → final | Só para a LRV: informe de quanto para quanto a velocidade deve cair (ex.: 60 → 30 km/h). O espaçamento **decrescente** das linhas é calculado pelo **método do MBST Vol. IV** (desaceleração de 1,47 m/s², 1 s entre linhas) e a largura da linha (0,20 / 0,30 / 0,40 m) pela velocidade; a última linha fica 2 m antes do ponto crítico. Vazio = padrão da variante. |

**Tachas/tachões** são modelados com a forma real – tacha baixa com faces chanfradas e face refletiva; tachão em tronco
trapezoidal com refletivos nas duas faces de aproximação – e contam unidades (coluna *Unid.* nos quantitativos). As variantes de **TAC-A/TAC-B** seguem a
Tabela 4.6 do MBST Vol. VI (8 / 12 / 16 m pela velocidade em situação normal; 6 / 9 / 12 m em situação especial –
neblina, curvas, declives – e 2 / 4 m no trecho que antecede) e as do **TACHÃO** a Tabela 4.7 (4 m ao lado do fluxo,
1 m em fluxos divergentes, 0,25–0,50 m em minirrotatórias). **LFO-5** (DER/SP) é a linha seccionada de transição com
três segmentos longos antes da linha contínua (10×6 / 15×9 / 20×12 m). **Piso tátil** (PTA/PTD)
segue a NBR 16537 (larguras de 0,25 a 0,60 m).

---

## 4. Faixa de Pedestres

1. Escolha FTP-1 (zebrada), FTP-2 (paralela) ou MCC (cruzamento rodocicloviário) e a variante.
2. Ajuste a largura da faixa (FTP-1) e o recuo dos bordos.
3. Linhas de retenção: largura (0,30 a 0,60 m), distância livre até a faixa (padrão 1,60 m), lados e
   extensão (**meia pista** em vias de mão dupla, **pista inteira** em mão única).
4. **Sobrepor** (padrão ligado): a faixa fica por cima – no trecho ocupado por ela as linhas longitudinais
   (eixo, divisão de faixas) somem, inclusive linhas criadas depois. A mesma opção existe nos zebrados e nas
   marcas transversais. **Atualizar Todas** refaz os recortes.
4. Clique o bordo A e o bordo B no alinhamento da travessia; repita para outras travessias.

As barras da FTP-1 são sempre inteiras e centralizadas entre os bordos.

---

## 5. Zebrado

* Tipos: ZPA (branco), ZPA-A (amarelo), zebrado rodoviário, chevron, área de conflito (quadriculado),
  lombada (MOT) e faixa adicional PcD.
* Parâmetros: largura e espaço das barras, ângulo, deslocamento, chevron, quadriculado, contorno LCA e
  cores.
* O ângulo é medido a partir do **maior lado do contorno** ou da **direção do tráfego** indicada com
  dois cliques (que também define o eixo do chevron).
* O contorno pode ter qualquer forma, inclusive côncava; as barras são recortadas corretamente e não
  se sobrepõem ao contorno.
* **Contorno com furo (anel)**: selecione mais de um contorno fechado – o interno vira furo. É o caso do zebrado
  em volta de uma rotatória ou de uma ilha: o zebrado fica **só na coroa**, sem cobrir o miolo.
* **Faixa ao longo do contorno**: informe uma *largura da faixa* (0 = preencher a área) e o lado – **interno**,
  **externo**, centro, esquerda ou direita. O zebrado acompanha a linha (aberta ou fechada) com essa largura,
  como uma "borda zebrada" de um anel, sem precisar desenhar o segundo contorno.
* **MAE / MAE-A** (MBST Vol. IV 5.7): quadriculado de quadrados ≥ 1,00 m para a travessia de faixa exclusiva,
  branco (no fluxo) ou amarelo (contrafluxo).

### 5.1 Canalização em transição (MTL / MAO / MAP)

**Zebrado ▾ → Canalização**: a área neutra de uma **transição de largura** (MTL – estreitamento/alargamento),
da **aproximação de obstáculo ou ilha** (MAO – transição de entrada, obstáculo e transição de saída, com
afastamento lateral *a* de 0,30–0,60 m) ou do **acostamento** (MAP – início/fim/estreitamento com trecho tangente).
Selecione ou desenhe a **linha de referência no sentido do tráfego** (o bordo da faixa antes da transição); a
variação de largura *d* positiva abre o zebrado para a esquerda e negativa para a direita. Os comprimentos vêm da
velocidade – **l = 0,5 · V · d** (MBST Vol. IV 6.2 / DER-SP B.3), mínimos de 30 m (urbana) e 60 m (rodovia) junto a
obstáculos, transição de acostamento *ta* de 30/40/50 m (DER-SP Quadro B-9) – e as barras de 0,50 m a 45° ficam a
cada 1,50 m (V < 80) ou 2,50 m (DER-SP Quadro B-10), com linha de canalização de 0,20 m. Tudo editável; branco entre
fluxos de mesmo sentido e amarelo entre fluxos opostos (com a opção *dois lados* para obstáculo no eixo).

---

## 6. Setas, Símbolos e Legendas

* 1º clique: base do símbolo (cauda da seta / centro da base da legenda); 2º clique: sentido do
  tráfego. Ou use um ângulo fixo (0° = norte do projeto).
* Setas: 5,00 m (vias urbanas) ou 7,50 m (vias rápidas), ou qualquer comprimento; *fator de largura*
  engrossa/afina a seta.
* SIA: símbolo branco com fundo azul (a área azul já desconta o símbolo).
* **Escala (%)**: amplia ou reduz o símbolo ou a legenda inteira mantendo as proporções (ex.: 75 % em
  ciclovias e calçadas, 150 % em vias rápidas). Vale também na edição.
* Legendas: qualquer fonte TrueType instalada; altura das letras, fator de largura (menor = mais
  alongada), espaçamentos e ordem de leitura. Em legendas de duas linhas (ex.: "SÓ / ÔNIBUS") a
  **primeira linha fica mais próxima do condutor**.

---

## 7. Vagas

* Tipos do catálogo: comum (0°, 30°, 45°, 60°, 90°), PcD (faixa adicional de 1,20 m zebrada + SIA),
  idoso (legenda IDOSO), motocicleta, carga e descarga, ônibus, táxi, ambulância.
* Quantidade **0 = preencher todo o meio-fio**.
* Lado (direita/esquerda do caminho), recuo inicial, afastamento do meio-fio, inclinação invertida,
  linha de fundo, contorno completo, símbolos e pintura de fundo (ex.: azul em vagas PcD, sem sobrepor
  o símbolo).

---

## 8. Representação, superfícies e materiais

Painel **Representação no Revit** (em todas as janelas):

* **Pisos do Revit**: pavimento da pista (asfalto, bloquete, concreto), **calçadas, meios-fios, sarjetas e
  grama** – das vias, interseções, rotatórias, cul-de-sacs, orelhas, áreas de calçada e canteiros – são
  criados como **Piso** (tipos *SV - {material} {espessura}*, com o material da sinalização), editáveis com
  as ferramentas nativas (**Editar contorno**, tipo, material, estrutura). Peças vizinhas do mesmo material
  viram um piso só. Se você trocar o tipo de um piso, a troca é mantida quando a via é regenerada.
  **Contorno editado à mão é preservado**: se você editar o contorno de um piso (ou movê-lo), o plugin não o
  refaz nas regenerações seguintes – para voltar ao contorno gerado, apague o piso e use **Atualizar**.
  Se o Revit recusar algum contorno, aquela parte é gerada como forma direta e o motivo aparece no aviso.
  Projetos feitos em versões anteriores: use **Atualizar Todas** para converter pavimentos, calçadas,
  meios-fios e sarjetas em pisos. Desligue em **Configurações** para voltar à forma direta. A sinalização
  pintada e os dispositivos continuam como modelo genérico.
* **Modelo 3D**: sólido fino (Modelos genéricos). A espessura padrão é a do material, com mínimo
  modelável de 3 mm (configurável) – películas de tinta reais são finas demais para sólidos do Revit.
  Os quantitativos usam sempre a **área**, independentemente da espessura.
* **Acompanhar a superfície**: os **pisos** (pavimento, calçada, meio-fio, sarjeta, grama) são criados na
  cota do terreno e deformados com a **edição de forma nativa do piso** (vértices do contorno a cada 4 m e malha
  interna), continuando editáveis. As marcas pintadas ficam **inteiras**, num plano ajustado ao terreno – só são
  divididas onde o greide muda (desvio acima de 1,5 cm), acabando com os "quadradinhos" de 2 m. Linhas sobre
  pinturas de fundo (ciclofaixa) ficam sempre acima delas. Opcionalmente selecione as superfícies.
* **Detalhe 2D**: regiões preenchidas na vista ativa (planta ou vista de desenho).
* **Material**: define espessura e consumo nos quantitativos (tinta acrílica, termoplástico, plástico
  a frio, laminado elastoplástico...).

**Alternar 2D/3D** converte as marcas selecionadas (a conversão para 2D usa a vista ativa).

---

## 9. Edição e atualização

### 9.1 Editar por categoria (elemento, via inteira ou pavimento)

Ao clicar em **Editar** sobre um elemento que pertence a uma via (calçada, meio-fio, linha, ciclofaixa, pavimento...), o
plugin pergunta o que você quer editar:

| Opção | O que abre |
|---|---|
| **Só este elemento** | a janela do próprio elemento (largura, variante, deslocamento, cor...). |
| **A via inteira (seção transversal)** | a janela da via com a **seção guardada** na criação: acrescente, remova ou altere faixas, calçadas, meios-fios, sarjetas, canteiros e ciclofaixas. A via é regenerada sobre o mesmo eixo, o pavimento mantém a identidade e as interseções, rotatórias e cul-de-sacs ligados são refeitos. |
| **Pavimento, hierarquia e raios** | material, espessura, hierarquia viária, raio das esquinas e raio das curvas do eixo. |

Vias criadas por versões anteriores não guardaram a seção completa: a janela abre com pista e calçadas reconstruídas a
partir do pavimento – confira antes de aplicar.

* **Editar**: selecione uma marca (qualquer elemento dela) → a janela abre com os valores atuais →
  *Aplicar* regenera a marca mantendo o mesmo identificador (tabelas e seleções continuam válidas).
  No **pavimento da via** (clique no asfalto) a edição altera material, espessura, hierarquia e **raio das
  esquinas** – as interseções da via são refeitas com o novo raio.
* **Atualização automática**: ao mover/editar linhas de referência, as marcas associadas são
  regeneradas na mesma operação (pode ser desativada em Configurações).
* **Atualizar Todas**: regenera todo o projeto (após alterar o catálogo, as superfícies ou as
  configurações).
* **Cópias**: copiar/colar uma marca cria uma marca independente, com o caminho convertido em pontos
  na nova posição.

### 9.2 Apagar Trecho (sinalização horizontal)
Apaga **partes da sinalização horizontal** – linhas, faixas de pedestres, zebrados, setas, legendas, marcas de vagas – sem
desfazer a marca (pavimento, calçadas, placas e dispositivos não entram).
1. Clique a marca (ou selecione-a antes de chamar a ferramenta). Funciona em planta e em 3D.
2. Abre uma janela com a **planta da marca e as peças reais dela** (cada traço, seta, faixa):
   * **clique numa peça** para apagá-la – ela fica **vermelha**; clique de novo na área vermelha para **devolver**;
   * **Shift + arrastar** apaga tudo da marca dentro da janela;
   * numa **linha contínua longa**, o clique apaga só o comprimento indicado (3 m por padrão) em volta do ponto;
   * arrastar move a planta, roda do mouse dá zoom, dois cliques enquadram;
   * **Desfazer último**, **Devolver tudo** e **Recortes ativos** (desmarque para mostrar a marca inteira sem perder os recortes).
   * se a geometria da marca não puder ser recalculada (linha de referência apagada, marca antiga), as peças são lidas
     direto dos elementos do modelo e o recorte é aplicado **nos próprios elementos** (sólidos recortados, regiões 2D
     refeitas) – funciona com o eixo criado pela Via e com qualquer sinalização horizontal avulsa. Nesse caso as peças não
     podem ser devolvidas depois (use Desfazer).
   * as marcas agora **guardam o traçado da linha de referência** a cada geração: apagar a linha depois não faz a marca
     perder o caminho – ela continua regenerável e editável normalmente.
3. **Aplicar** regera a marca. Os trechos ficam guardados nela: chamar a ferramenta de novo mostra o que está apagado (em
   vermelho) para devolver ou apagar mais. Os recortes acompanham Editar, Atualizar e a edição da seção da via, e os
   quantitativos consideram só o que ficou pintado.

> Mover ou girar diretamente os sólidos/regiões gerados não altera a definição: na próxima
> regeneração a marca volta para o caminho. Para reposicionar, edite as linhas de referência ou use
> **Editar**.

---

### 9.3 Autoteste (diagnóstico de todas as ferramentas)

O Autoteste **saiu da faixa de opções** (era fácil clicar sem querer e o Revit ficava muito tempo ocupado). Ele agora
fica em **SinalizaBIM → Configurações → Autoteste (diagnóstico)…** e abre com o **teste rápido**: só os blocos Vias,
Tráfego, Conexões, Horizontal, Vertical e Calçadas, **no máximo 3 etapas por bloco** e **3 minutos no total** – o que
passar disso é pulado e contado no relatório (linha *Modo*). Para o teste de tudo, marque **Completo** e os demais
blocos (Topografia, Obras, Edição, Detalhamento, Verificações) – só em um arquivo de testes, pois leva de 10 a 40 min.

Roda sozinho, sem janelas, as ferramentas do plugin numa **área de teste**
criada 400 m à direita de tudo o que existe no projeto:

| Bloco | O que é testado |
|---|---|
| Vias | os 10 modelos de seção, via em curva/S, Pista ligada a outra via |
| Conexões | interseção em cruz, T, esconsa, avenida × local; controles (PARE, preferência, semáforo), ilhas físicas/pintadas, ilhas de conversão, bolsões, raios pequeno/grande; conversão em rotatória; rotatórias de todos os tipos; cul-de-sac de todos os tipos e automático |
| Horizontal | todas as linhas do catálogo com todas as variantes, zebrados (área, faixa, com furo), setas, legendas, vagas, inscrições, MAC, canalização, ciclovias, faixas de pedestres sobre a via, cruzamento rodoferroviário |
| Vertical | dispositivos, placas (catálogo completo ou amostra), tipos de suporte, mobiliário (ponto e linha), famílias classificadas |
| Calçadas | rampas (todos os tipos, com recorte), orelha, áreas de calçada, canteiros, moderação, piso tátil, via férrea, drenagem (todos os tipos e em série) |
| Largura e recuos / Segurança | via com largura variável e estreitamento do lote, baia de ônibus + faixas de desaceleração e aceleração, calçada com inclinação e níveis do alinhamento, arterial × coletora com LCO, sonorizadores (todos os tipos) e área de escape; via nova emendada na ponta de outra em ângulo (emenda concordada); várias linhas selecionadas virando uma malha de vias com T e cruz; recuo (baia) acrescentado numa via já criada; **via reconhecida em pisos comuns** (T feito com dois pisos, pista já modelada) |
| Topografia | Toposolid de teste com encosta, dois morros e um vale; vias acompanhando o terreno e suavizadas; cruzamentos no relevo; Perfil da Via em todos os modos e aplicado com obras automáticas |
| Obras | viaduto sobre outra via, ponte no vale, passarela, túnel, trincheira, muros (todos), taludes, nós viários, terraplenagem (simulação, aplicação, mapa) |
| Edição | editar linha/interseção/rotatória, mover eixo (atualização automática), Apagar Trecho (na via, fora da via, zebrado, sem linha de referência, devolver peças), 2D/3D, excluir, Atualizar Todas |
| Detalhamento | detalhe de placas, anotação, legenda, cota de seção + perfil, detalhe típico, quadros, notas, norte, eixos, quantitativos e tabelas |
| Tráfego | Simulador de Tráfego sobre a área de teste: leitura da rede, análise HCM e diagnóstico, microssimulação de 5 min, cenários (rotatória × semáforo coordenado × contagem) com comparação, cenários gravados e lidos do projeto, plano semafórico gravado na interseção, sinalização interpretada (placas, marcas, dispositivos e semáforos associados a trechos e cruzamentos), resultados nos parâmetros SV_NivelServico/SV_Trafego e mapa de níveis de serviço na planta; **contatos entre veículos (deve ser zero) e setas**, **planta real no mapa**, **recomendação de tempos e onda verde**, **semáforo de travessia do cenário** e **soluções testadas** para o pior cruzamento |
| Verificações | pintura escondida sob o piso ou flutuando, buracos e degraus entre pisos da pista, terreno acima da pista ou sem encostar na seção, estruturas invadindo outras vias, formas sem geometria |

Cada erro do plugin (inclusive os que antes só iam para o log), cada erro/aviso do Revit e cada verificação que falha
entram no relatório, salvo em `%AppData%\SinalizaBIM\autoteste-AAAAMMDD-HHMMSS.txt` (gravado a cada etapa – se o
Revit fechar no meio, o arquivo mostra onde parou). Tudo acontece dentro de um único Desfazer: no fim escolha
**Desfazer tudo** (recomendado) ou **Manter** para olhar as vistas "SV Autoteste" (planta e 3D). O teste rápido leva
até ~3 minutos; o completo, de 10 a 40 minutos; o progresso aparece na barra de status do Revit.

## 10. Quantitativos

**Miniaturas.** Cada linha mostra a **imagem da própria sinalização**, não do trecho do projeto: a face da placa, um
trecho da linha com o seu tracejado, o símbolo, a legenda, o zebrado, uma unidade do dispositivo em perspectiva ou a
amostra do material (pavimento, concreto, grama). A coluna de cor foi retirada da janela e da planilha (continua no CSV).

**Tabela no Revit.** *Criar tabela no Revit* e *Tabelas por categoria* abrem a tabela criada e mostram um resumo com o
nome e o número de linhas de cada uma (Navegador de projeto → Tabelas/Quantidades); se algo impedir a criação, o motivo
aparece na mensagem.

**Exportação Excel (.xlsx).** Botão principal da janela: planilha formatada como a janela – título e dados do projeto,
um bloco por categoria com as subcategorias, **miniatura de cada item**, colunas fixas e alinhadas (item, código,
descrição, quantidade, unidade, área, extensão, unidades, elementos, cor, material, consumo, hierarquia, referência),
subtotais, resumos por categoria, hierarquia e pintura, larguras de coluna, bordas e formatos numéricos.

**Exportação CSV.** O arquivo segue o desenho da janela: cabeçalho do projeto, um bloco por categoria com as
subcategorias como títulos, colunas próprias de cada categoria (memorial: código, descrição, quantidade, elementos;
horizontal: cor, material, área, extensão, consumo), subtotais e os resumos por categoria, hierarquia e pintura.

**Memorial × quantidades.** As categorias **Sinalização vertical, Dispositivos, Mobiliário, Moderação de tráfego e
Acessibilidade** são listadas como **memorial**: **uma linha por modelo** (ex.: *R-1 – Parada obrigatória: 3 un*), sem
desdobrar por cor nem por material da chapa. Cores e materiais (consumo de tinta, microesferas) continuam só na
**Sinalização horizontal**, onde interessam ao orçamento.

* Somente o que **existe no modelo**: partes apagadas de uma marca saem do quantitativo, e pisos entram
  com a **área real** (inclusive os editados à mão).
* Itens separados por **categoria**: 1. Sinalização horizontal, 2. Sinalização vertical,
  3. Dispositivos auxiliares e segregação física, 4. Acessibilidade (rampas e piso tátil),
  5. Calçadas, meios-fios e urbanização, 6. Moderação de tráfego, 7. Mobiliário e elementos urbanos.
* Filtro por categoria, por **hierarquia viária** e **pesquisa** (código, descrição, cor, material); abas
  *Itens por categoria*, *Resumo por categoria*, **Resumo por hierarquia viária** (extensão de vias,
  pavimento, área e extensão pintadas, placas, elementos e consumo de tinta de cada hierarquia) e
  *Pintura por cor e material*.
* Cada item é separado também pela **hierarquia viária** da via a que pertence (coluna *Hierarquia*).
* Cada linha traz a **quantidade na unidade de medição** do item (m², m ou un), área (pintada para
  tintas, em planta para concreto/grama/metal), extensão, unidades, consumo estimado e referência.
* **Exportar CSV**: blocos por categoria com subtotais (com a coluna de hierarquia), resumo por categoria,
  **resumo por hierarquia viária** e resumo de pintura
  (separador `;` e vírgula decimal – abre direto no Excel em português). Exporta a categoria/pesquisa
  atual.
* **Criar tabela no Revit**: *SV - Quantitativos de sinalização* com o **mesmo layout da janela** – título
  *QUANTITATIVO DE SINALIZAÇÃO VIÁRIA E URBANIZAÇÃO* em faixa azul, cabeçalhos em azul claro, blocos por
  **categoria** e **subcategoria** (cabeçalho e subtotal), colunas **Imagem · Código · Descrição · Quantidade · Un. ·
  Área (m²) · Extensão (m) · Unid. · Hierarquia · Material** e **TOTAL GERAL**. O Revit aceita no máximo **4 níveis de
  classificação** (era a causa do erro *"sorting/grouping field count would be greater than 4"*): categoria →
  subcategoria → código → cor (oculta, só separa a pintura por cor). Itens de memorial (placas, dispositivos,
  mobiliário) ficam numa linha por modelo. A coluna *Quantidade/Un.* usa os novos parâmetros **SV_QtdMedicao** e
  **SV_Unidade**, preenchidos também nos elementos antigos ao criar a tabela. A **miniatura** de cada item vai para o
  parâmetro *Imagem* dos elementos e a tabela é colocada numa **folha SV-Q01** – o Revit só desenha imagens de
  tabela em folhas. **Tabelas por categoria** cria uma tabela para cada categoria. O parâmetro compartilhado **SV_Categoria** também pode ser usado em filtros de vista.
* Para a prancha, use **Detalhamento → Quadro de Quantitativos** (seção 16).

**Janela redesenhada**: cabeçalho com o projeto, **cartões de resumo** (elementos, área pintada, extensão
pintada, pavimento, placas, elementos urbanos e tinta estimada), filtros e **grupos recolhíveis** por
**categoria → subcategoria** com subtotais no cabeçalho de cada grupo (itens, m², m e un). A coluna *Cor*
mostra a amostra da cor; ao clicar numa linha aparecem os detalhes (elementos no modelo, tipo de área,
extensão de via, microesferas, **tipos de família** e referência normativa). A aba *Resumo por categoria e
subcategoria* mostra os subtotais em árvore. Os **elementos urbanos** são subdivididos em *bancos e
assentos*, *iluminação pública*, *arborização e paisagismo*, *lixeiras*, *abrigos de transporte*,
*paraciclos*, *proteção e segurança*, *comunicação*, *infraestrutura*, *lazer* e *acessibilidade* – tanto os do
plugin quanto as **famílias do Revit** classificadas (seção 22.1). O CSV e a tabela do Revit trazem a
subcategoria.

---

## 11. Configurações e catálogo

* **Configurações**: representação e material padrão, velocidade padrão, fonte das legendas,
  espessura mínima, elevação adicional, projeção sobre superfícies, atualização automática, contorno
  das regiões 2D e caminho do catálogo do usuário.
* **Catálogo**: abre o JSON para edição, recarrega ou restaura o padrão. Formato em
  [CATALOGO.md](CATALOGO.md).

Arquivos do usuário ficam em `%AppData%\SinalizaBIM\` (configurações, catálogo, parâmetros
compartilhados e `log.txt`).

## 12. Sinalização vertical (Placas)

**Suportes aéreos (rodovias e vias arteriais).** Além da coluna simples e dupla, o tipo de suporte pode ser **braço
projetado** (coluna no bordo e braço sobre a pista), **semipórtico** (coluna robusta e viga treliçada em balanço) ou
**pórtico** (duas colunas e viga atravessando a pista). Parâmetros: vão da viga/braço, diâmetro das colunas e altura da
viga; a altura livre passa a 5,50 m (mínimo do MBST/DER) e a placa é deslocada para o meio da pista. O 1º clique é a
coluna no bordo; o deslocamento da placa (positivo à direita do condutor) diz para que lado a viga avança.

**Prévia 3D, zoom e pan.** Em todas as janelas com pré-visualização há o seletor **Planta | 3D** (o modo ativo fica destacado); a roda do mouse dá zoom em torno do cursor, arrastar move e o duplo clique reenquadra. O botão **3D / Planta** no canto da prévia: a vista
isométrica mostra o volume real do elemento (tachão, balizador, placa com suporte, quebra-mola...), o que ajuda a escolher
o tipo de cara.

Escolha a categoria e a placa, ajuste o tamanho (lista com os tamanhos usuais ou valor livre),
a legenda (ex.: velocidade na R-19; "-" remove a legenda), o suporte (coluna simples, duas colunas
ou sem suporte) e a altura livre sob a placa (2,10 m em calçadas). 1º clique: posição do suporte;
2º clique: sentido do tráfego que lê a placa – a face fica voltada para quem se aproxima. A vista
frontal da janela mostra a placa como o condutor a vê.

O catálogo traz a série completa do MBST/CTB Anexo II – **regulamentação R-1 a R-40** (com as
variantes a/b/c), **advertência A-1a a A-48**, marcadores de alinhamento e de perigo, Cruz de Santo
André, **serviços auxiliares** (hospital, posto, restaurante, hotel, ônibus, táxi, PcD...),
**atrativos turísticos**, **educativas**, **indicação** (destino, distância, localidade, marco
quilométrico), **informações complementares** (horário, exceções, distância) e **sinalização
temporária de obras** (fundo laranja). Cada placa tem descrição do significado e **pictograma
desenhado** (setas, curvas, veículos, pedestres, animais, tarja de proibição...). Placas com valor
(R-14 a R-19, A-20, A-37, A-38, A-46 a A-48) usam a legenda editável (ex.: `3,0 m`, `10 t`, `40`).
A lista tem miniaturas e pesquisa por código, nome ou descrição.

Os pictogramas são esquemáticos e reconhecíveis; para o desenho oficial exato, confira a edição
vigente do manual. O catálogo pode ser alterado (seção `placas`, campo `pictograma` – ver
[CATALOGO.md](CATALOGO.md)).

## 13. Urbanismo e drenagem

* **Sarjetão**: calha de concreto que atravessa a via (clique de sarjeta a sarjeta). É modelado com o
  **perfil côncavo real** – placa de concreto de 15 cm cuja superfície desce em parábola até a
  **flecha** no centro (padrão 5 cm, editável; acima de 12 cm a janela avisa) – e **recorta o
  pavimento e as marcas** sob ele, como nos sarjetões executados em cruzamentos.
* **Meio-fio e Sarjeta**: meios-fios de vários tipos, sarjetas, sarjetões (clique os dois bordos),
  calçadas, gramados e faixas de caminhada azuis/verdes ao longo de qualquer linha. No meio-fio com
  sarjeta conjugada, desenhe na face do meio-fio com a calçada à esquerda.
* **Sinalizar Via**: a calçada tem a opção **Sarjeta** (padrão 0,30 m) – a linha de bordo é afastada
  automaticamente; a **Faixa de caminhada** é um elemento da seção (azul ou verde, bordas brancas e
  símbolo de pedestre repetido).
* **Rampas**: 1º clique no centro da rampa, na face do meio-fio; 2º clique para dentro da calçada.
  Tudo é dimensionável: largura, altura, inclinação **ou comprimento** da rampa, inclinação **ou comprimento**
  das abas, **desnível residual** no meio-fio, **patamar** plano no topo, **material** (concreto, bloquete...),
  piso tátil (largura, comprimento ao longo do meio-fio, afastamento, cor) e largura do direcional.
  A rampa é um sólido inclinado (0 na pista → altura do meio-fio no topo, com a inclinação
  escolhida), as abas são cunhas triangulares com a inclinação das abas e o piso tátil de alerta
  acompanha a rampa (afastamento do meio-fio configurável; direcional opcional no eixo). Tipos:
  com abas, sem abas (laterais protegidas), **rebaixamento total** (calçada estreita: plataforma
  rebaixada na largura da travessia com rampas laterais; informe a profundidade da calçada) e guia
  rebaixada de veículos. A calçada, o meio-fio e o gramado sob a rampa são recortados. Excluir a
  rampa e usar **Atualizar Todas** restaura a calçada. Em 2D, a rampa mostra a seta de subida e a
  inclinação; a janela avisa quando a inclinação ou a largura não atendem à NBR 9050.
* **Elementos urbanos**: por ponto (posição + direção) ou distribuídos ao longo de linhas com
  espaçamento, deslocamento e rotação (ex.: árvores a cada 8 m, postes a cada 30 m).

## 14. Moderação de tráfego

Quebra-molas tipo A (3,70 m × 0,08 m) e tipo B (1,50 m × 0,06 m), faixa elevada (platô + rampas de
1,50 m, 0,15 m de altura, zebrado no platô), lombada invertida e **almofada** (*speed cushion*, CET-SP – Medidas
Moderadoras do Tráfego: 3,0 m × 6–7,5 cm, largura de 1,60–1,90 m, **uma por faixa** – ônibus e veículos de emergência
passam com as rodas ao lado; quantidade e largura editáveis). Clique os dois bordos da pista.
A **lombada invertida** recorta o pavimento (piso) e as linhas sobre ela, ficando visível a depressão em concreto.
As dimensões padrão devem ser conferidas com as resoluções do CONTRAN vigentes; lembre-se da
sinalização vertical obrigatória (ex.: A-18).

## 15. Ciclofaixa com medidas personalizadas

**Complementos ▾ → Ciclovia / Faixa de caminhada** monta tudo de uma vez, com pré-visualização:
ciclofaixa unidirecional ou bidirecional, ciclovia ou faixa de caminhada; largura, pintura de fundo
(vermelha, ou azul/verde na caminhada) **entre as linhas, sem sobreposição**, linhas de bordo (direita,
esquerda, ambas ou nenhuma; contínuas ou seccionadas), linha central amarela, bicicletas/pedestres e
setas a cada N metros e segregadores. As linhas e símbolos ficam ligeiramente acima da pintura de fundo,
eliminando os "quadrados" que apareciam na vista. A ferramenta antiga (só linhas) continua em
**Ciclofaixa – linhas**.

**Padrão II da CET-SP (Vol. 13)**: desmarcando a pintura de fundo, a linha branca de delimitação é acompanhada por
uma **linha vermelha de contraste** de 0,15 m pelo lado interno (largura editável) – a alternativa econômica à
pintura total. Na ciclovia segregada a linha vermelha acompanha a LBO.

No **Sinalizar Via**, selecione a ciclofaixa para ajustar: largura, largura da linha de delimitação,
linha contínua ou seccionada (traço/espaço), tamanho do símbolo da bicicleta e da seta, distância
entre eles, espaçamento das inscrições, pintura vermelha, bidirecional com linha central amarela e
segregação física.

## 16. Detalhamento (pranchas de sinalização)

Todos os comandos trabalham na **vista ativa** (planta de piso/implantação ou vista de desenho). As
medidas são em **milímetros de papel**: o desenho acompanha a escala da vista (12 mm a 1:200 =
2,40 m no modelo). Os elementos (regiões, textos "SV - Texto x mm" e linhas "SV - Chamada") ficam
ligados à marca de origem: editar a placa/marca atualiza o detalhe, e apagá-la remove o detalhe.

* **Detalhar Placas**: escolha as placas (selecionadas, visíveis na vista ou todas), a largura do
  símbolo, a distância e a direção em relação ao suporte, e se haverá linha de chamada e
  código/nome. O símbolo é a face da placa (forma, orla, fundo e legenda) desenhada na planta; o
  ponto vermelho marca o suporte. Rodar de novo o comando atualiza os detalhes existentes na vista
  (não duplica). Para ajustar só um símbolo, use **Editar** sobre ele. A chamada pode ser **reta**,
  com **cotovelo horizontal** ou **livre**, com terminal em **ponto** ou **seta**, e o número pode ir
  num **balão** acima do símbolo.
* **Mover Chamada de Placa**: leve o símbolo **para onde quiser** e desenhe a linha de chamada
  clicando os vértices (ESC termina) – ou só redesenhe a linha, mude a **ponta** da chamada (para
  apontar para a face da placa, o poste etc.) ou volte à chamada automática. Tudo fica relativo ao
  suporte: movendo a placa, o detalhe acompanha.
* **Cotar Seção – perfil transversal**: **um clique sobre a via** gera o corte perpendicular ao eixo, de
  alinhamento a alinhamento (ou *Dois pontos livres*). A **seção em planta é criada primeiro**; só **depois** o plugin pede o
  clique onde colocar o **PERFIL TRANSVERSAL** (ESC = sem perfil). O perfil é um **detalhe separado e agrupado** (grupo de
  detalhes do Revit *SV - Perfil transversal A-A*): selecione e mova tudo junto – a nova posição é mantida nas próximas
  atualizações. Editar o perfil muda escala, exagero vertical, caimento, níveis, cotas, legenda e moldura; apagar a cota
  apaga o perfil. O perfil
  é o corte do próprio modelo: pavimento, sarjetas, meios-fios, calçadas, faixas gramadas, canteiros, plataformas,
  dispositivos e pintura, cada camada **preenchida com o seu material e contornada**, na escala escolhida (1:50 por
  padrão, com **exagero vertical** opcional), com **nome de cada trecho** e chamada, **níveis** (▽ +0,15 / ±0,00),
  **caimento da pista** (i = 2,0 % para fora do eixo, editável), **cotas verticais** dos desníveis (meio-fio,
  plataforma, canteiro), **cotas horizontais** e total sob o perfil, **eixo**, linha do terreno, **legenda dos
  materiais** com espessuras e o título **SEÇÃO TRANSVERSAL A–A** com a escala. A letra avança sozinha (A, B, C…).
  Tudo se atualiza quando a via muda; a cadeia de cotas em planta é opcional.
  **Caimento real** (*Desenhar o caimento real*, ligado por padrão): a pista é desenhada **em duas águas** a partir do
  eixo com o caimento do greide da via (ou o indicado, 2 %) – ou com a **superelevação** do greide na estaca do corte –
  e a **calçada sobe para o lote** com a inclinação da seção (ou a informada, 2 %). Os **níveis** mostram as cotas
  reais (ex.: −0,11 na sarjeta, +0,04 no topo da guia), as setas de caimento aparecem também nas calçadas com o
  valor de cada trecho e as cotas verticais medem o degrau real do meio-fio. O título traz a **via e a estaca do corte**
  (ex.: *V01 – coletora – estaca 5+10,00 (110,00 m)*), também na planta; em vias de largura variável o corte usa a
  largura daquela estaca.
* **Cotar Seção** (cadeia em planta): cadeia de cotas com **nome de cada trecho** (calçada, meio-fio, faixa de
  rolamento, ciclofaixa, faixa de ônibus, faixa de caminhada, canteiro, estacionamento, zebrado),
  **eixo da via** em traço-ponto com a inscrição EIXO (opcionalmente dividindo as cotas em
  meias-larguras), **cota total**, **marcas de corte** nas pontas (letra no círculo + seta do sentido de
  observação) e título **SEÇÃO A–A (cotas em metros)**. Calçada e meio-fio encostados têm cada um sua
  cota; as bordas de pinturas de fundo sob uma linha são absorvidas pelo eixo da linha. Terminal em
  **traço 45°**, **seta** ou **ponto**; 1, 2 ou 3 casas decimais. A janela tem pré-visualização numa via
  de exemplo.
* **Detalhe Típico**: terminal das cotas (traço/seta/ponto), título sublinhado e moldura opcional.
* **Quadro de Quantitativos**: coluna **ITEM** numerada por categoria (1.1, 1.2… 7.3), como em
  planilha orçamentária.
* **Notas Gerais**: **modelos de notas** prontos – geral, sinalização horizontal (tintas, microesferas
  NBR 16184, retrorrefletância), sinalização vertical (NBR 14644, suportes, altura livre), acessibilidade
  (NBR 9050/16537), obras (MBST Vol. VII) e urbanização – editáveis.
* **Norte**: estilos *clássico*, *rosa dos ventos* (N, S, L, O) e *seta*.
* **Anotar**: clique sobre qualquer sinalização (o ponto clicado recebe a chamada) e depois onde o
  texto deve ficar. O texto automático traz código e nome e, opcionalmente, detalhes (variante/
  largura da linha, barras e espaçamento do zebrado, dimensões da vaga, espaçamento dos
  dispositivos, inclinação da rampa...). Um texto livre pode substituir o automático.
* **Quadro de Legenda**: clique o canto superior esquerdo. Lista **somente as placas** (sinalização
  vertical) do projeto – uma linha por tipo de placa, com o desenho e a descrição, em ordem:
  regulamentação, advertência, indicação e demais. Atualiza-se quando placas são criadas ou removidas.

Legenda, quadros e cotas de seção acompanham o projeto: são regenerados quando as marcas são
criadas, editadas ou quando os eixos mudam (e com **Atualizar Todas**). Todos podem ser editados com
**Editar**.

Dica: para pranchas, combine a sinalização em 3D (vista de planta com os sólidos) com os detalhes
de placas e o quadro de legenda – ou converta a sinalização horizontal para 2D (**Alternar 2D/3D**)
em uma vista dedicada.

## 17. Calçadas

**Cul-de-sac.** Clique na **ponta** de uma via para fechá-la com o balão, ou **sobre** uma via (fora da ponta) e depois no
centro do balão para criar uma **rua sem saída** saindo dela: o ramal recebe uma cópia exata da seção da via (pista,
linhas, sarjeta, meio-fio, grama, calçada), a interseção em T é feita automaticamente e o balão usa o mesmo perfil de
calçada. Com ESC, o balão é posicionado livremente por dois cliques.

* **Orelha de Calçada**: desenhe (ou clique dois pontos) na **face do meio-fio** existente, no trecho
  do avanço – em linha reta (meio de quadra) ou **contornando a esquina** (selecione as linhas/arco da
  esquina ou desenhe os pontos em volta dela; o meio-fio da orelha acompanha a esquina com raio =
  raio da esquina + avanço, ou com o *raio mínimo na esquina* informado), com a calçada à esquerda do sentido do desenho (ou desmarque a opção). Parâmetros:
  avanço sobre a pista (largura do estacionamento, ex.: 2,20 m), e **cada ponta com sua transição**:
  **curvas reversas** (raio), **chanfro** ou **reta** – ponta perpendicular que acompanha a calçada / a
  travessia, como nas orelhas de esquina –, altura e largura do meio-fio, **canteiro** gramado com margens e árvores.
  As vagas e linhas da pista sob a orelha são recortadas automaticamente (e restauradas se a orelha
  for apagada + **Atualizar Todas**). Se o trecho for curto para o raio, o raio é reduzido e um aviso
  é mostrado.
* **Área de Calçada**: contorno fechado livre (esquinas com avanço, ilhas, alargamentos). Tipos:
  calçada/avanço em concreto, canteiro gramado, **ciclovia no nível da calçada** (pintura vermelha),
  **parklet/deck**, faixa de serviço ajardinada e pavimento. Meio-fio opcional no contorno e
  **arredondamento automático dos cantos**. Pode recortar calçadas, gramados e marcas existentes.
* **Canteiros**: canteiros gramados, **jardineiras elevadas** com mureta e **grelhas de árvore**
  distribuídos ao longo da faixa de serviço (comprimento, largura, espaçamento entre centros ou
  faixa contínua, deslocamento lateral) com árvores; recortam a calçada sob eles.
* **Cul-de-sac**: clique perto da **ponta de uma via** – o balão se liga a ela: fica centrado na ponta do
  eixo, com a largura, a calçada e a hierarquia da via, recorta a via no trecho do balão e acompanha o eixo
  quando ele é movido (é removido se a ponta passar a encontrar outra via). Com *ESC*, posicione um balão
  avulso: 1º clique no início do balão sobre o eixo; 2º clique no centro do balão (ou fim da via). Tipos **circular, excêntrico (esquerda/direita), gota, em "T" (martelo), em "Y" e em
  "L"**. Gera pavimento, meio-fio, calçada, **ilha central** ajardinada e **linha de bordo**, com
  raio de concordância. Avisa quando o raio de giro fica abaixo de 9 m (confira a legislação
  municipal de parcelamento para o raio mínimo exigido).

Todas as ferramentas de calçada são paramétricas: **Editar** reabre a janela com pré-visualização e
o elemento é regenerado (com os recortes atualizados).

## 18. Pavimento da via

O **Sinalizar Via** gera o **pavimento da pista** (grupo *Pavimento e interseções* da janela):
**asfalto (CBUQ)**, **bloquete / pavimento intertravado** ou **concreto**, com espessura padrão de
0,05 / 0,08 / 0,15 m (indicativa – confira o dimensionamento do pavimento). O topo do pavimento fica no
nível do eixo; a sinalização fica por cima e as calçadas/meios-fios 0,15 m acima. Canteiros físicos e
sarjetas ficam sem pavimento. O pavimento também **registra a seção da via** (larguras da pista e das
calçadas), usada pelas interseções e rotatórias – por isso "Nenhum" desativa o ajuste automático.

## 19. Interseções

> **Conexões automáticas são sempre simples**: esquinas com o raio da hierarquia, **PARE** (linha de
> retenção, legenda e placa R-1) na via secundária e as **linhas da via principal contínuas** – só o bordo é
> interrompido na boca da outra via. Faixas de pedestres automáticas são opcionais (Configurações ou janela
> da via). Ilhas, bolsões, alargamentos e zebrados (tipos II a IV) só são aplicados quando você escolhe a
> interseção em **Conexões** – nunca nas conexões automáticas.

As interseções são criadas **automaticamente, direto nas linhas (eixos)**: ao criar uma via que **cruza**
outra (ou que **termina** junto a outra – entroncamento em T) e também ao **desenhar, mover ou editar um
eixo** já sinalizado, o plugin cria ou ajusta o cruzamento. Quando as vias deixam de se cruzar, a
interseção é removida e a sinalização volta a ser contínua. O comportamento pode ser desligado em
**Configurações → Criar/atualizar interseções automaticamente**.

A ferramenta **Conexões** (opção *Todas as interseções do projeto*, ou clicando numa conexão → *Interseção*) abre os parâmetros (raio, faixa, retenção, rampas) e a opção
*Aplicar em*:

* **Todos os cruzamentos e entroncamentos do projeto** – resolve de uma vez todas as vias que se
  sobrepõem (recomendado ao abrir projetos antigos);
* **Somente o cruzamento que eu clicar** – aplica os parâmetros apenas ao cruzamento mais próximo do
  ponto clicado.

Vias criadas por versões anteriores (ou com pavimento *Nenhum*) funcionam também: a seção transversal
é reconstruída a partir dos meios-fios, calçadas e linhas do grupo, e o pavimento é criado
automaticamente. A interseção:

* torna o pavimento contínuo no miolo e arredonda as **esquinas** com o **raio** informado (na face do
  meio-fio), com **meio-fio curvo** – qualquer raio a partir de 0,5 m gera a curva inteira (a área refeita
  acompanha cada ramo até o fim da curva, sem cortar o asfalto da esquina);
* refaz as **calçadas** junto à esquina – **contornando a curva** com a largura da calçada – e as **pontas
  dos canteiros centrais** (nariz com meio-fio,
  terminando 1 m antes da pista transversal; a travessia corta o canteiro formando **refúgio** no nível da
  pista);
* interrompe a sinalização horizontal das vias no cruzamento e na aproximação (até depois da linha de
  retenção) e remove as **vagas a 5 m da esquina**; os demais trechos das vias ficam intactos, qualquer
  que seja o ângulo;
* cria em cada ramo **faixa de pedestres** e **rebaixamentos de calçada** nas duas pontas da travessia.

Funciona para **qualquer geometria**: cruzamentos ortogonais ou oblíquos, entroncamentos em **T** e em
**Y**, vias curvas e nós com vários ramos. Em ângulos agudos as esquinas continuam arredondadas.

**Meio-fio e sarjeta nas esquinas.** O meio-fio das esquinas é um piso próprio (não se funde ao passeio) e a sarjeta da
seção das vias continua pela curva, junto ao meio-fio.

**Esquinas com a composição das vias.** Com *Esquinas com a mesma composição das calçadas das vias* (ligado por
padrão), a faixa de serviço gramada definida na seção das vias continua pela curva da esquina, entre o meio-fio e o
passeio, alinhando o cruzamento com as vias. Desligue para esquinas só em concreto.

**Cruzando vias totalmente diferentes.** Cada via guarda o seu perfil de calçada (meio-fio, sarjeta, faixa de serviço,
passeio, faixa de acesso) **de cada lado**. Na esquina entre duas vias diferentes a calçada é refeita como uma faixa
contínua ao longo da curva do meio-fio: começa com o perfil do lado da via A, termina com o perfil do lado da via B e faz a
**transição suave** entre eles (as faixas de mesmo tipo se casam; uma faixa que só existe numa das vias afina até zero;
o meio-fio mantém a largura). Assim a calçada não se distorce nem abre buracos quando uma via tem calçada larga com
grama e a outra calçada estreita, ou quando os lados de uma mesma via são diferentes. **Canteiros laterais** (entre a pista
e o estacionamento, por exemplo) não são lidos como calçada: terminam antes da faixa de pedestres e a boca do cruzamento
é repavimentada.

### 19.1 Via principal e controle

A **via principal** (preferencial) é escolhida automaticamente – a que atravessa o nó (dois ramos), depois
a mais larga e a mais longa – e pode ser trocada em **Editar**. O **controle** define a sinalização das
aproximações:

| Controle | Via secundária | Via principal |
|---|---|---|
| **PARE** (padrão) | LRE, legenda **PARE** e placa **R-1** | só travessia |
| **Dê a preferência** | LDP, símbolo **SDP** e placa **R-2** | só travessia |
| **Semáforo** | LRE | LRE |
| **Sem controle** | só travessia | só travessia |

Na mão dupla a retenção ocupa a meia pista de chegada (até o eixo, o canteiro ou a ilha); na mão única, a
pista inteira, só no ramo por onde o tráfego chega.

**Linha de continuidade (LCO) na boca das secundárias.** Quando a via principal tem hierarquia maior (arterial,
trânsito rápido ou rodovia × coletora/local), o bordo da principal continua **tracejado** atravessando a boca da
secundária: o motorista da principal vê a continuidade da pista e quem entra ou sai cruza a linha (MBST Vol. IV).
Em **Linha de continuidade**: *Pela hierarquia* (padrão), *Sempre* ou *Nunca*.

**Plano semafórico gravado.** O Simulador de Tráfego pode gravar na interseção o plano (ciclo, fases, verdes,
amarelo, vermelho geral, defasagem e verde de pedestres) – ele passa a valer na análise e fica com o modelo (§ 26).

### 19.1.1 Linhas no cruzamento e regras de conversão (MBST Vol. IV / CTB art. 207)

Na janela da interseção, bloco **Linhas no cruzamento e regras de conversão**:

* **Conversões à esquerda permitidas** (padrão): o eixo da via principal vira **linha de continuidade (LCO)** tracejada
  na boca das transversais – quem vem da principal pode converter; desmarcado, o eixo segue **contínuo** pela boca (linha
  contínua amarela proíbe a conversão, CTB art. 207) e cada aproximação recebe **R-4a** – só conversões à direita. O
  Simulador de Tráfego passa a proibir essas conversões.
* **LCO no miolo da via principal**: eixo amarelo (duplo quando a linha é dupla) e divisórias brancas tracejadas
  **1 × 1 m** (**2 × 2 m** acima de 60 km/h) atravessando o cruzamento; desmarcado, interrompidas.
* **Linhas contínuas nas aproximações**: o eixo seccionado vira **LFO-3** (LFO-1 em pista estreita) nos **15 m** antes do
  cruzamento (**30 m** acima de 60 km/h) e, no semáforo, as divisórias ficam **LMS-1** nos 20 m antes da retenção –
  sempre limitadas à metade da quadra quando há outro cruzamento perto.
* As linhas da via são **cortadas e substituídas** por essas marcas (nada fica sobreposto).

### 19.2 Tipos de interseção (MBST / DNIT)

Os tipos podem ser combinados na mesma interseção:

* **Tipo I – sem refúgio**: só as esquinas com raio (padrão).
* **Tipo II – ilha separadora (gota) na via secundária**: ilha **física** (meio-fio, núcleo em concreto
  ou grama) ou **pintada** (zebrado amarelo ZPA-A), com cabeça arredondada junto à via principal. A pista
  é **alargada** em volta da ilha (com teiper de volta à largura normal) e as linhas da via são refeitas
  deslocadas; a travessia passa por um refúgio no nível da pista; LFO-1 ao lado da ilha e zebrado à
  frente da cauda. Não é aplicada em ramos quase paralelos a outro (< 25°).
* **Tipo III – faixa de conversão livre à direita**: a esquina recebe curva de **raio maior** (a tangente
  não passa do raio pedido, então esquinas agudas ficam com raio proporcional) e uma **ilha triangular**
  física ou pintada (ZPA branco) separa a conversão do cruzamento. A calçada acompanha a curva. Escolha
  **todas as esquinas**, **só as agudas** (< 75°) ou **só as obtusas** (> 105°). Com todas as esquinas
  de um cruzamento ortogonal obtém-se a interseção "com ilhas".
* **Tipo IV – bolsão de conversão à esquerda na via principal**: com **canteiro central ≥ 3 m** o bolsão
  é recortado do canteiro (armazenamento + teiper; LMS-1/LMS-2 junto às faixas de passagem, LFO-1 do lado
  do fluxo oposto); **sem canteiro**, a pista é alargada dos dois lados e o bolsão fica entre linhas LFO-1 e
  LMS-1, com **zebrado amarelo** no teiper, como no desenho do Tipo IV. Seta **PEM-E** no bolsão.

A janela mostra uma **pré-visualização** ao vivo: na criação, um exemplo (cruzamento, T, oblíquo 60° ou
Y 45°, com via local, coletora ou avenida); na edição, as vias reais da interseção.

### 19.3 Atualização

Tudo é regenerado quando um eixo é movido, ao editar a interseção (**Editar**) e com **Atualizar Todas**.
Travessias, retenções, placas, zebrados e linhas deslocadas da interseção são recriados nessas ocasiões
(ajustes manuais neles são perdidos). A ferramenta **Interseção** com *Todos os cruzamentos* aplica os
tipos e o controle escolhidos a todas as interseções do projeto.

## 20. Rotatórias

**Continuidade das vias existentes (várias sinalizações ao mesmo tempo).** A rotatória recorta das vias ligadas só o
que ela realmente ocupa, por uma regra única para cada tipo de elemento: pavimento e elementos físicos só dentro da
área da rotatória (anel + concordâncias e, no by-pass, a faixa do by-pass – antes o recorte ia até 100 m e apagava
trechos inteiros); pintura das vias só até o início da marcação da rotatória; **vagas** até 5 m antes da entrada
(CTB art. 181, XIX); recortes antigos de uma geração anterior são removidos ao gerar de novo. Em **avenidas com
canteiro central**, o canteiro continua como **ilha separadora** do ramo (no modo *só o anel* / *só a ilha* o próprio
canteiro é usado, sem ilha duplicada), a travessia ganha **refúgio** no canteiro e a LFO-3 não é pintada onde já há
canteiro. Em **mini-rotatórias** as LDP/LRE não invadem o anel e aparece aviso quando o diâmetro inscrito é menor
que a via mais larga + 4 m.

**Seguir exatamente os elementos das vias.** Interseções, rotatórias e cul-de-sacs leem os **elementos reais** do grupo de
cada via ligada – sarjeta, meio-fio, faixa gramada, passeio, com as larguras, alturas e materiais de cada um, inclusive
em vias montadas com *Pista* + elementos junto ao bordo – e aplicam o mesmo perfil em volta da própria pista. O meio-fio
vira piso próprio, a sarjeta sai do pavimento (sem pisos sobrepostos) e, no lado oposto de um T, a sarjeta da via
continua. Rotatórias e cul-de-sacs sem via ligada usam os campos de faixa de serviço, grama e sarjeta. Antes: rotatórias e cul-de-sacs ligados a vias leem a seção das vias (faixa de serviço
gramada e sarjeta) e a continuam pelas próprias calçadas – opção *Seguir a composição das calçadas das vias ligadas*,
ligada por padrão; desligada, a faixa de serviço, a grama e a sarjeta são informadas nos campos ao lado. A **ilha
ajardinada** ganhou faixa pavimentada entre o meio-fio e a grama e grama elevada (canteiro).

**Concordâncias da rotatória.** A curva de entrada/saída de cada ramo é tangente ao bordo do ramo e ao círculo
externo do anel (traçado usual), com o raio daquele lado – não há mais o "dente" que aparecia entre os raios de entrada e
de saída na calçada.

**Rotatória elevada (platô)** e **ilha em calota**: em *Ilha central e anel*, marque *Rotatória elevada* para a pista
giratória subir (altura e comprimento das rampas editáveis; rampas geradas em cada ramo) – solução de moderação de
tráfego em vias locais, que exige A-18/A-32b e atenção à drenagem. A ilha **Calota rampada** é um tronco de cone baixo de
concreto (altura no centro, tamanho do platô superior e **platô gramado** opcionais), galgável pelas rodas traseiras de veículos longos, usada em minirrotatórias e
rotatórias compactas.

**Tipos** (escolha em *Tipo de rotatória* – os valores de referência são aplicados e **tudo continua
editável**):

| Tipo | Uso | Referência de dimensões |
|---|---|---|
| **Mini-rotatória** | vias locais, baixa velocidade | ilha **galgável** (pintada/elevada 7 cm), Ø inscrito ≈ 14–25 m |
| **Compacta** | vias locais/coletoras | 1 faixa, ilha pequena com faixa galgável |
| **1 faixa** | coletoras | Ø inscrito ≈ 30–40 m |
| **2 faixas** | arteriais | 2 faixas no anel, LMS entre elas |
| **Turbo-rotatória** | arteriais com fluxo alto | 2 faixas com **divisores físicos** (sem troca de faixa no anel) |
| **Oval (alongada)** | nós alongados/assimétricos | ilha elíptica (alongamento e ângulo livres) |
| **Com by-pass** | conversão à direita livre | faixa de by-pass (largura e raio) com ilhas separadoras |
| **Personalizada** | qualquer | todos os parâmetros livres |

Parâmetros adicionais: tipo da ilha (**ajardinada**, **pavimentada**, **galgável** ou **pintada**), altura da faixa
galgável, **raios de entrada e de saída** separados, largura dos divisores, largura/raio do by-pass,
distância e largura das **travessias**, número de **árvores**. A janela mostra o **diâmetro inscrito**.

**Elementos no lugar certo.** A **linha de bordo** acompanha o meio-fio real (inclusive nas curvas de entrada e saída),
a **faixa de pedestres** vai de calçada a calçada atravessando também as faixas de desvio (by-pass), com as rampas nas
duas pontas, os **refúgios** são recortados no nível da pista dentro das ilhas separadoras, o zebrado do "nariz" da ilha
foi retirado (a ilha já é física) e a LFO-3 da aproximação começa junto à ilha.

### 20.1 Integração com as vias – só a rotatória, sem refazer o cruzamento

| Modo | O que a rotatória gera / recorta |
|---|---|
| **Completa** | como antes: pista dos ramos, esquinas, calçadas, meio-fio; as vias são recortadas na zona toda. |
| **Somente o anel** | a pista giratória e a ilha; as vias ligadas são recortadas **apenas dentro do anel** (a pista delas continua até o anel); a sinalização dos ramos (LDP, SDP, ilhas separadoras, placas) é gerada sobre as vias existentes. |
| **Somente a ilha** | apenas a ilha central e as marcas do anel/ramos; as vias **não são recortadas** (só a ilha é subtraída) – ideal para inserir uma rotatória num cruzamento já modelado sem mexer nele. |

**Recortar as vias ligadas** pode ser desligado em qualquer modo (a rotatória fica só sobreposta). O recorte separa
o que é **físico** (pavimento, meio-fio, calçada – zona da rotatória) do que é **pintura** (linhas e marcas – só o
anel), então as linhas de bordo e de eixo das vias param no anel sem apagar a pista.

### 20.2 Minirrotatória pintada e marcação do anel (MBST Vol. IV – MIR)

A ilha **pintada** (preset *Mini*) é uma **LCA** contínua de 0,20 m (largura editável) com **tachões** a cada
0,25–0,50 m em volta, zebrado interno opcional; ilhas separadoras **pintadas** em amarelo (fluxos opostos) ou
branco (mão única, por ramo), **setas curvas IMC** no anel (uma por faixa, logo após cada entrada), LBO externa
entre as aberturas dos ramos, **LMS-1/LMS-2** entre as faixas do anel, LDP de 0,40 m com SDP de 3,60 / 6,00 m pela
velocidade de aproximação e **LFO-3** nas aproximações de mão dupla. Placas: **R-2 + R-33** nas entradas (quando o
raio da ilha é pequeno) ou **R-24a na ilha** (DER-SP projetos-tipo 15/16 – escolha em *Placas de sentido*),
**A-12 com "A … m"** (distância pela desaceleração de 2 m/s² do MBST Vol. II) e **marcadores de alinhamento** na ilha.

### 20.3 Personalizar cada ramo

Na seção *Personalizar cada ramo* escolha o ramo (pelo ângulo) e ajuste: largura, **mão dupla/única**, ilha
separadora (**física, pintada ou nenhuma**) e seu comprimento, **travessia** (sim/não), **raios de entrada e de
saída**, **controle** (Dê a preferência ou **PARE** – LRE de 0,40 m + legenda) e placas. As escolhas ficam
guardadas por via: ao redetectar os ramos (mover eixos, ligar outra via) elas são preservadas.

**Vias → Conexões → Rotatória** (seta do botão *Conexões*): clique o centro (o ponto é encaixado no cruzamento de vias mais próximo). Os ramos
são detectados das vias que passam pelo centro (ou informados por ângulo, para uma rotatória isolada).
Parâmetros: raio da **ilha central** (ajardinada, com árvores opcionais), **faixa galgável** em bloquete
para ônibus e caminhões, número e largura das **faixas da pista giratória** (linha divisória quando há 2
ou mais), **raio de entrada/saída**, calçada em volta e pavimento. Em cada ramo: **ilha separadora** em
gota com **zebrado** de aproximação, **linha de dê a preferência** e **símbolo "Dê a preferência"** na
entrada (circulação anti-horária), **travessia de pedestres** passando pela ilha, **rebaixamentos** e
placas **R-2** e **R-33**. As vias ligadas são recortadas e a interseção existente no mesmo nó é
substituída (com os recortes que ela fazia). A rotatória ligada às vias **acompanha o nó** quando os eixos
são movidos e ganha um **ramo novo** quando uma via é desenhada até ela (**Via** desenhada por pontos, clicando dentro da
rotatória); também pode ser criada direto na conexão (**Via → Rotatória** ou **Conexões → Trocar Conexão**). Confira as dimensões com o manual do DNIT / órgão local e com o veículo de projeto.

## 21. Piso tátil (NBR 16537)

**Complementos → Piso Tátil** desenha a **rota tátil** com placas moduladas de 0,25, 0,30 ou 0,40 m e
**relevo** (visível em 3D e em planta):

* **direcional**: barras paralelas ao sentido do deslocamento;
* **alerta**: domos em malha ortogonal, colocados automaticamente nas **mudanças de direção**, nas
  **extremidades** e nas **junções em T** entre trechos (selecione ou desenhe vários trechos de uma vez);
* largura em fileiras de placas, cor contrastante, lado do quadrado de alerta e profundidade do alerta
  nos extremos; opção *Somente faixa de alerta* (junto a rebaixamentos, desníveis, obstáculos);
* as placas são assentadas no topo da calçada (0,15 m, ajustável).

Os rebaixamentos (rampas) também usam placas com relevo acompanhando a inclinação. Confira dimensões e
distribuição com a ABNT NBR 16537 vigente.

## 22.1 Famílias do Revit como elementos urbanos

**Via → Elementos Urbanos → Famílias do Revit**: use **suas próprias famílias de componente** (mobiliário,
luminárias, plantio, modelo genérico, equipamentos…) com o design que quiser:

* escolha a família carregada (ou **carregue um .rfa**), a **subcategoria urbana** (sugerida pelo nome:
  iluminação, arborização, bancos, lixeiras, abrigos, paraciclos, segurança, comunicação, infraestrutura,
  lazer, acessibilidade), o **código no quantitativo** (ex.: POSTE-LED), a descrição e a hierarquia viária;
* **insira por cliques** (ponto + sentido para onde fica voltado, rotação adicional) ou **distribua ao longo
  de linhas/bordas** (espaçamento, afastamento lateral, recuos, nos dois lados e em quincôncio);
* os elementos são **assentados sobre a topografia/pisos** (opcional) com elevação adicional;
* **Classificar famílias já inseridas**: selecione instâncias existentes e elas passam a contar no
  quantitativo.

As instâncias recebem os parâmetros SV_* (SV_Categoria = 7. Mobiliário e elementos urbanos, SV_Grupo =
subcategoria, SV_Codigo, SV_Descricao, SV_Hierarquia), que podem ser editados na paleta Propriedades.
No **Quantitativo** elas aparecem na categoria 7, na subcategoria escolhida, contadas por código com a
lista dos tipos usados. Famílias baseadas em nível ou em plano de trabalho podem ser inseridas por ponto;
as hospedadas (em face/parede) podem ser inseridas pelo Revit e depois classificadas.

## 22. Mobiliário, árvores e placas sobre a calçada

Os elementos urbanos foram redesenhados: **árvore** com tronco, galhos, copa em lóbulos (contorno
orgânico em planta) e canteiro com guia; **palmeira**; **arbusto**; **banco** com ripas de madeira e
apoios de aço com braços; **lixeira** cônica com tampa; **poste** cônico com braço curvo e luminária LED;
**poste de pedestres**; **abrigo de ônibus** com vidro, painel e banco; **paraciclo** tubular; **hidrante**;
**floreira** com arbustos; **placa de rua** e **semáforo** com anteparo e pestanas. Elementos urbanos e
placas são assentados no **topo da calçada** (nível da base 0,15 m, ajustável na janela; 0 = nível da pista).

---

## 23. Cruzamento rodoferroviário (MCF)

**Complementos ▾ → Cruzamento Rodoferroviário**: clique o ponto onde a ferrovia cruza uma via do SinalizaBIM
(sem via, clique dois pontos do eixo) e, opcionalmente, dois pontos sobre o trilho para a **esconsidade**. A ferramenta
monta o conjunto do **MBST Vol. IX / Vol. IV 5.8** e do **DER-SP (projeto-tipo 13)**, em cada aproximação:

* **Linha de retenção dupla** (MCF, 2 × 0,40 m) **paralela ao trilho**, a 3,0 m do trilho externo (MBST; DER-SP
  usa 5,0 m sem cancela e 2,0 m antes da cancela – editável);
* **retângulo de advertência** (duas linhas transversais a 15 m) a 15–150 m da retenção, com a **cruz de Santo
  André (CSA) de 6,00 m por faixa**;
* **LFO-3** (linha dupla contínua) na aproximação de mão dupla, **PARE** por faixa (1,60 / 2,40 m pela velocidade),
  **LRV** opcional dimensionada pelo método do MBST e **MAC** sobre a passagem;
* placas **A-41** (cruz de Santo André, com "1 LINHA" / "n LINHAS") e **R-1** no mesmo suporte a 3,60 m do eixo da
  ferrovia, **A-39** (sem barreira) ou **A-40** (com barreira) a uma distância calculada pela desaceleração
  (2 m/s² – MBST Vol. II) e **R-19** antecipada em vias acima de 40 km/h.

Largura da pista, faixas por sentido, número de linhas férreas, bitola, distâncias e larguras são editáveis; o
conjunto é um grupo (Selecionar Conjunto pega tudo).

## 23.1 Via férrea

**Vias ▾ → Via Férrea**: desenhe ou selecione o **eixo** (entre as linhas, se houver mais de uma). A ferramenta modela
a via permanente completa em 3D, tudo editável (Editar):

| Parâmetro | Opções |
|---|---|
| Superestrutura | **em lastro** (convencional), **em laje** (fixação direta) ou **embutida no pavimento** (VLT/bonde) |
| Bitola | larga 1,600 · métrica 1,000 · padrão 1,435 · **mista** 1,000 + 1,600 (3 trilhos) · personalizada |
| Trilho | TR-45, TR-57, TR-68, UIC-60 e **Ri-60 de canaleta** – patim, alma e boleto com as dimensões reais |
| Linhas | 1 a 8 linhas paralelas, **entrevia** eixo a eixo e deslocamento do conjunto |
| Dormentes | concreto monobloco, **bibloco** (dois blocos + barra), madeira ou aço; espaçamento (0,60 m), comprimento pela bitola, largura e altura editáveis; **placas de apoio e fixações** |
| Lastro | altura sob o dormente (0,30 m), ombro (0,40 m), **talude** (1,5 : 1) – sólido trapezoidal contínuo em brita |
| Sublastro | espessura (0,20 m) e largura além do pé do lastro, em solo compactado |
| Drenagem | **valetas de concreto** (fundo e paredes) dos dois lados, largura e profundidade |
| Laje / embutida | espessura e largura da laje; revestimento da via embutida (concreto, asfalto, **grama – via verde**, bloquete) com as canaletas livres; topo do trilho rente ao piso |

Nível zero: topo da plataforma (via embutida: topo do pavimento). A janela mostra a prévia em planta/3D e o memorial
**por km** (trilhos em m e t, dormentes, fixações, lastro, sublastro e concreto em m³, revestimento, valetas). No
quantitativo a via entra em *Pavimentação e geometria viária*, em metros de via (unidades = dormentes). Materiais
novos: **Brita (lastro)** e **Sublastro**. Referências: ABNT NBR 7641, NBR 7590, NBR 11709 e NBR 5564. Para a
sinalização da passagem em nível use o **Cruzamento Rodoferroviário** (seção 23).

## 24. Infraestrutura – drenagem, obras de arte, contenções e nós viários

Painel **Infraestrutura**. Todas as obras são paramétricas, editáveis (Editar), regeneradas quando o eixo muda e entram no
quantitativo (categorias *9. Drenagem* e *10. Obras de arte, contenções e terraplenagem*). As peças são sólidos 3D
(formas diretas) com materiais próprios: concreto, aço, asfalto, brita, solo compactado, grama e água.

### 24.1 Drenagem
* **Boca de Lobo / PV**: boca de lobo **simples** (guia chapéu), **dupla** (duas bocas e pilarete), **com grelha**,
  **combinada** e **poço de visita**. **Grelha de Drenagem**: grelha de sarjeta, **grelha de piso** (calçadas, praças) e
  **canaleta com grelha contínua**.
* **Encaixe real na via**: clique junto ao **meio-fio** de qualquer elemento do plugin – via com calçada, interseção,
  rotatória, orelha ou meio-fio avulso. O dispositivo encaixa na **face da guia**, adota a **altura e a largura do meio-fio**
  clicado e **substitui** o trecho de pavimento, sarjeta, meio-fio, calçada e pinturas que ocupa (os pisos do Revit ganham o
  recorte): **rebaixo da sarjeta** com transições, **meio-fio refeito** descendo até o rebaixo, **guia chapéu** com a boca
  aberta e soleira, **caixa de captação oca** (paredes, fundo, lâmina d'água), **laje e tampa rentes à calçada** (tampa de
  concreto com alças ou tampão de ferro fundido com relevo), **tubo de ligação** com bolsas (sob a pista ou ao longo da guia).
* **Em série**: escolha *Em série ao longo do meio-fio*, clique o início e o fim na mesma guia e os dispositivos saem a cada
  espaçamento (30–60 m usuais).
* **Grelhas**: desenho em **barras** (com nervura central), **malha** ou **chapa com fendas** (pedestres); barras transversais
  (ciclistas), longitudinais ou diagonais; ferro fundido (NBR 10160), aço ou concreto; aro em cantoneira.
* **PV**: câmara cilíndrica, **cone excêntrico**, chaminé, **degraus de ferro**, berço, tubos de entrada e saída e **tampão
  Ø 0,60 m** em aro circular rente ao pavimento (o asfalto recebe o furo redondo). **Grelha de piso**: adota o nível do piso
  clicado (calçada ou pista).
* Apagar um dispositivo devolve a continuidade dos pisos recortados. Referências: DNIT 030/2004-ES, NBR 10160, PMSP.

### 24.2 Vias e obras: um só sistema
As obras de infraestrutura **não são mais objetos separados**: a pista, as calçadas, os meios-fios, as faixas e a sinalização
são sempre **a via do plugin** (pisos do Revit, conexões, interseções, rotatórias e cul-de-sac como em qualquer via), e a
obra é um **trecho dessa via**.
* **Greide da via** (perfil longitudinal): cada via pode ter PIVs com **curvas verticais**, **abaulamento** e
  **superelevação**. Todas as marcas da via acompanham o greide – os **pisos são deformados** (edição de forma nativa) e as
  pinturas, barreiras e dispositivos sobem e descem com ele.
* **Obra hospedada**: ponte, viaduto, passarela, túnel e trincheira guardam a via e o trecho (estacas) onde estão e geram
  **só a estrutura** – na largura e no greide da via. Mudou a seção da via (Editar → a via inteira)? A obra acompanha.
* **Interseções entre vias em níveis diferentes não são criadas** (e as existentes saem quando uma via passa a cruzar a outra
  por cima). Interseções e rotatórias ficam **na cota do greide** das vias que chegam nelas.

### 24.3 Viaduto, ponte e passarela
Três formas de inserir:
1. **Num trecho de uma via existente** – clique a via e as duas pontas do trecho. O greide da via é ajustado no trecho
   (rampas de acesso fora dele) e a estrutura é hospedada.
2. **Via nova: desenhar o eixo** – por pontos, com encaixe nas vias existentes e curvas concordadas; a via é criada com a
   seção escolhida (pelas faixas do formulário ou por um **modelo de via**) e a obra entra no trecho necessário.
3. **Via nova: selecionar linhas**.

**Perfil da obra**: *rampas de acesso* (o trecho em estrutura começa onde o aterro passaria de 6 m), *horizontal*, *entre
margens* (reta de cabeceira a cabeceira – pontes sobre vales e rios), *convexo* (ponto alto no meio), *côncavo* (ponto baixo
no meio – "barriga" sobre o vale), *inclinado* (rampa constante da altura do início à do fim), *personalizado* (PIVs
digitados a partir do início da obra: estaca; cota; curva) ou *manter o greide da via*. **Traçado reto**
(só as pontas do eixo), **esconsidade dos apoios** (pilares e encontros paralelos ao rio ou à via cruzada) e **pilares nas
estacas** que você indicar. Nem toda ponte é curva ou inclinada: a **Ponte** já vem reta, em nível e com pilares-parede.
* **Estrutura**: laje com balanços, abas, pingadeiras e **cornija** clara; **vigas "I"** por vão com transversinas e aparelhos
  de apoio, **caixão de altura variável**, laje maciça, **arcos**, **estaiada** (mastro H, A ou central; leque ou harpa) e
  **treliça**; pilares circular, duplo, pórtico, martelo, parede, Y e oblongo (com a esconsidade), **tubos de descida
  d'água** nos pilares, encontros com cortina, **alas** e laje de transição.
* **Guarda-corpo**: via com calçada sobre a obra → **mureta de concreto + guarda-corpo** (tubular, balaústres ou vidro);
  via sem calçada → **New Jersey**. Juntas, buzinotes e iluminação sobre a mureta/barreira.
* A prévia mostra a via inteira sobre um vale, com o terreno já terraplenado.

**Pilares fora das vias que passam por baixo**: o viaduto enxerga as outras vias do projeto sob ele – os pilares automáticos
deslizam ao longo do eixo até sair da pista e das calçadas delas (aviso quando não há ponto livre), e as fundações ficam
enterradas abaixo do pavimento da via de baixo (antes o bloco de fundação era posto no terreno natural e podia ficar acima de
uma via em corte).

**Juntas e encontros sem saliência**: as juntas de dilatação e a cortina dos encontros acompanham o abaulamento e a
superelevação do tabuleiro ponto a ponto – antes eram caixas retas na cota do centro e ficavam alguns centímetros acima do
asfalto nas bordas.

### 24.4 Túnel e trincheira
* **Túnel** num trecho de via (existente ou nova): revestimento em **ferradura**, **circular** ou **retangular**, emboques em
  testa, bisel ou pala, LED contínuo, eletrocalhas, nichos SOS e ventiladores; a pista e os passeios são os da via. Numa via
  nova o trecho em túnel é onde o terreno cobre a abóbada com folga.
* **Trincheira**: no trecho, a via desce pela rampa máxima até o rebaixo entre **muros** (flexão, cortina atirantada ou terra
  armada) com guarda-corpo, canaletas e laje de travessia; o terreno entre os muros é escavado no Toposolid.

### 24.5 Perfil da Via – a altura da via ao longo do terreno
**Para que serve**: definir o **greide** – a cota do eixo da via ao longo do comprimento – sobre a topografia, e decidir onde a
via vira obra. É o "perfil longitudinal" do projeto rodoviário.

**Como ler o gráfico da janela** (um corte ao longo do eixo, com a escala vertical exagerada):
* **marrom** = terreno natural; **linha escura** = a via (greide);
* **vermelho** = terreno que será **cortado**; **verde** = **aterro**;
* **faixas coloridas no alto** = trechos em obra (azul viaduto/ponte, preto túnel, laranja trincheira), com a extensão;
* embaixo as **estacas** (km+m ao longo da via), à esquerda as **cotas**; os quadrados brancos são os **PIVs** (onde a rampa
  muda, com a cota) e "i = %" é a **rampa** de cada trecho.

**Como usar**:
1. Clique a via.
2. Escolha como o greide é traçado: **acompanhar o terreno suavizado** (menor terraplenagem; ajuste a suavização e o
   alteamento), **rampa constante** entre as pontas, **compensar corte e aterro** (o greide sobe ou desce até o corte
   cobrir o aterro × fator de homogeneização – menos bota-fora e empréstimo), **nivelado** numa cota ou **PIVs digitados**
   (estaca; cota; curva) – que já abrem com os **PIVs atuais da via** para você só ajustar.
3. Limite a **rampa máxima** e as **curvas verticais**: comprimento fixo e/ou **parâmetro K** (L = K × variação de rampa
   em %; urbano ~40 km/h K 4–7, rodovia 80 km/h K 24–30 – DNIT); vale o maior.
4. Diga a partir de que **altura de aterro** a via vira **viaduto/ponte**, de que **profundidade de corte** vira **túnel** e,
   se quiser, **trincheira** (muros) nos cortes médios. O gráfico se atualiza a cada mudança.
5. **Aplicar**: o greide passa a valer para a via inteira (pista, calçadas, linhas, dispositivos), as obras são criadas nos
   trechos indicados (com os últimos parâmetros de Viaduto/Ponte, Túnel e Trincheira) e o **Toposolid é cortado/aterrado**
   com taludes até o terreno natural.

### 24.6 Muro de arrimo e talude
* **Muro de arrimo**: **flexão**, **gravidade**, **contrafortes**, **gabião**, **terra armada** e **cortina atirantada**, com
  altura variável, ficha, coroamento, barbacãs e guarda-corpo; o **reaterro** é do Toposolid.
* **Talude** de **corte** ou **aterro** com **bermas**, **canaletas meia-cana** (crista, pé e bermas), **descidas d'água em
  degraus** e revestimento. Com terreno nativo, a face do talude **é o próprio Toposolid** e a **grama vira subdivisão** do
  Toposolid; revestimentos rígidos ficam 3 cm acima da face.
* As **pontas do talude fecham em rampa até o terreno** (fechamento lateral em talude, banqueta por banqueta) – antes o
  Toposolid ligava a face ao terreno por triângulos soltos, com dentes e zigue-zague nas pontas. No **corte**, a **canaleta de
  crista** fica 1,5 m além da crista **sobre o terreno** (ou sobre a face do talude, se a encosta continuar subindo) – antes
  ela seguia a cota da crista e ficava solta no ar.

### 24.7 Nó viário (interseção em desnível) sobre as vias
**Diamante**, **diamante com rotatórias**, **trevo completo**, **trevo parcial**, **trombeta** e **rotatória em dois níveis**.
Clique o **cruzamento de duas vias do plugin** (a de maior hierarquia/largura é a principal) – ou um ponto livre e a direção:
as duas vias são criadas antes, com a ferramenta de vias.
* **A via de cima** ganha greide (gabarito + altura estrutural, rampa máxima, platô nos terminais) e um **viaduto hospedado,
  esconso** conforme o ângulo, com pilar no canteiro quando o vão pede. A interseção em nível que existia sai.
* **Cada ramo e laço é uma via do plugin**: eixo em spline (linha de modelo), faixa de 4 m, **greide que concorda com as duas
  vias** (desaceleração/aceleração no greide da via, rampa suavizada no meio) e **superelevação** nos laços. Edite como
  qualquer via.
* **Seção urbana completa** (padrão): ramos com **sarjeta, meio-fio e calçada** (externa na largura escolhida, interna
  estreita) e as vias criadas pelo nó com calçadas dos dois lados – os elementos laterais dos ramos são recortados onde
  encostam nas vias principal e transversal, e as calçadas destas são recortadas sob as faixas paralelas. Desmarcando, a seção
  é rodoviária (acostamento com **defensa**).
* **Ligações**: **faixas paralelas** de mudança de velocidade com **taper** (a pista do ramo afina até zero junto à via), com
  as calçadas, meios-fios, bordos e defensas da via **recortados** sob elas; **interseções** (PARE) nos terminais do diamante;
  **rotatórias** nos terminais do diamante com rotatórias e o **anel elevado** da rotatória em dois níveis (só com as vias no
  nível dela), com os **tabuleiros do anel** sobre a principal.
* **Trombeta**: a haste cruza a principal no viaduto; ramos diretos do lado da haste e **dois laços do outro lado** – todos os
  movimentos sem cruzamentos em nível.
* **Acabamentos do nó**: **narizes pavimentados com zebrado**, **linhas de continuidade** nas faixas paralelas, **pórticos de
  sinalização** antes das saídas, postes ao longo dos ramos, **torres de iluminação de 30 m** nos laços e rotatórias e **grama
  nos laços** (subdivisão do Toposolid). Onde um ramo passa sobre outra via, **ponte hospedada no ramo**.
* **Editar** o nó refaz ramos, viaduto, terminais e acabamentos com os novos parâmetros (as vias principal e transversal
  ficam).

### 24.8 Terraplenagem e topografia (Massa e terreno – terreno nativo)
**Como o corte e o aterro acontecem no Toposolid.** O Toposolid é uma superfície triangulada pelos seus pontos: o plugin
troca os pontos da área de projeto pelos pontos da plataforma, das cristas e pés de talude e dos degraus dos muros – e o
Revit triangula de novo. Por isso cada obra entrega os pontos certos:
* **Via em corte/aterro**: plataforma (bordas e eixo a cada 3 m) e **taludes** até o terreno natural, com pontos no meio da
  face dos taludes altos.
* **Muros** (trincheira, rampas em terra armada): **degrau** de 10 cm atrás da face do muro, no terreno natural – o terreno
  fica natural atrás do muro, sem a "rampa de terra" até a borda.
* **Emboque de túnel**: a via em corte termina numa **testa vertical** até a altura do emboque e, acima dela, a encosta é
  **recortada em talude**; o furo é aberto pela **Massa de escavação** do túnel (Massa fica oculta nas vistas por padrão).
  Se o Revit recusar a Massa, uma cópia como Modelo genérico (oculta) é tentada; se ainda assim recusar, o relatório explica
  como usar *Escavar* à mão.
* **Encontros de pontes**: com alas abertas, **saia de aterro** à frente do encontro até o terreno; com alas paralelas ou
  terra armada, face vertical contida.
* **Talude de corte**: a linha desenhada é o **pé**; a **crista fica no terreno natural** e o terreno à frente do pé é
  rebaixado (plataforma configurável). Talude de aterro: pé no terreno, crista acima.
* **Conferência**: depois de inserir os pontos o plugin **lê a geometria real do Toposolid** e compara com o projeto. Se o
  Revit tiver lido as cotas com um deslocamento constante (nível/deslocamento do Toposolid), os pontos são refeitos com a
  correção; o relatório mostra o **desvio máximo conferido**. A "superfície suavizada" dos Toposolids é desligada (ela
  arredonda taludes e degraus).

**Relevo ao criar a via (Via/Nova via)**: com Toposolid sob o eixo, escolha
* **Acompanhar o terreno** – a via sobe e desce com a topografia (greide colado ao terreno, suavização curta) e o terreno é
  cortado/aterrado só na largura da plataforma, deixando a seção em nível transversal;
* **Greide suavizado** – rampa máxima e curvas verticais, com cortes e aterros maiores em talude;
* **Plana** – sem alterar o terreno.
Taludes de corte/aterro e a rampa máxima ficam na mesma janela. Depois, **Perfil da Via** ajusta o greide e cria as obras.
**Pisos planos, sem vincos**: sobre o greide, o nó ou o terreno, cada piso é dividido em **partes planas** (o topo de cada
parte fica a no máximo 2 cm da superfície teórica; 3 cm sobre o Toposolid) e cada parte é um **piso plano inclinado** –
uma única face, sem as linhas de triangulação da edição de forma. As divisões seguem a lógica da obra: primeiro na
**crista do abaulamento** (cada caimento é um plano), depois **perpendiculares ao eixo** (juntas retas, como as juntas de
pavimentação). Numa rampa constante a pista inteira são só **dois pisos** (um por caimento); nas curvas verticais e
horizontais as partes ficam mais curtas. Ao longo das vias e nos nós a divisão continua sempre em faixas
transversais até ficar plana (nunca triângulos); só em superfícies sem eixo de referência (terreno) uma parte torcida vira
piso com edição de forma em malha regular. Partes vizinhas que juntas ainda cabem num plano são **fundidas de volta** (pisos grandes, poucas juntas) e o plano de
cada parte passa **exatamente pela superfície nas juntas** – sem degraus entre partes. A **pintura segue os próprios pisos**: é recortada nas mesmas juntas e
assenta no plano de cada piso, 4 mm acima dele – sem afundar nem flutuar. O relevo **acompanhar o terreno** usa suavização e curvas de
40 m (tangentes mais longas, pisos maiores). As curvas verticais do greide nunca se sobrepõem e têm comprimento mínimo K = 10 m por % de
variação de rampa.

**Correção importante**: a localização de pontos em relação ao eixo (estaca e afastamento) errava para pontos a mais de
~10 m do eixo em trechos curvos – calçadas e canteiros largos recebiam a cota de outra estaca (degraus e calçadas
"afundadas"). Corrigido: a busca agora amplia o raio até garantir o segmento mais próximo.

**Interseções, rotatórias e cul-de-sacs no relevo** – como se projeta na prática: a **via principal** (maior hierarquia;
empate = a mais larga) **atravessa o nó com o próprio greide e abaulamento**, e além da pista dela a superfície segue **em
nível na transversal** (a cota da borda). O nó inteiro – pavimento, esquinas, calçadas, rampas, ilhas – fica em **poucos
planos grandes**, sem os triângulos e "leques" que apareciam. As **vias secundárias fazem a concordância fora do nó**, nos
primeiros 20–40 m depois dele: partem exatamente da superfície do nó e passam suavemente (curva em S) para o próprio greide
e abaulamento – os pisos dessa transição ficam em faixas planas transversais (juntas retas), a plataforma do Toposolid
acompanha a mesma superfície. **Rampas de acessibilidade, meios-fios, ilhas e dispositivos do nó** acompanham a superfície
**vértice a vértice** (antes subiam só pela cota do centro e ficavam tortos ou soltos). Defina a **hierarquia** das vias para
escolher qual é a principal. Além da ponta de um eixo o greide segue na rampa final e o abaulamento é medido na normal da ponta
(antes formava um cone em volta da ponta). Pisos,
pinturas, faixas de pedestres, retenções e rampas do nó ficam nessa superfície (sem degrau na emenda com o piso da via), e o
nó entra na terraplenagem sempre que **qualquer via ligada** a ele molda o terreno: plataforma inclinada sob o pavimento e as
calçadas, taludes das vias ligadas. Ao criar uma via com relevo que cruza (ou encosta em) vias existentes, o greide dela é
**obrigado a passar na cota da outra via** no cruzamento, com uma plataforma de 12 m em nível – as duas chegam juntas à
interseção.

**Espessura do Toposolid**: o fundo do Toposolid precisa ficar abaixo do corte mais fundo – senão o Revit recusa ("o sólido
topográfico é muito fino para seu tipo"). O plugin confere antes e, se preciso, cria um tipo "… SV +N m" com a camada mais
grossa aumentada e aplica ao Toposolid. Se mesmo assim o Revit recusar, o ajuste é desfeito com uma mensagem explicando o
motivo (sem a janela que não pode ser ignorada). Os tipos de piso do plugin não usam camada variável (a laje sobe e desce
inteira, sem afinar).

* O terreno é **sempre o Toposolid nativo** (Massa e terreno → Sólido topográfico): aterros, cortes, taludes, saias e
  reaterros nunca viram sólidos. **Sem Toposolid, o plugin cria um** plano sob as obras automaticamente.
* O terreno é **lido direto da geometria do Toposolid** (sem depender de vista 3D) e, na primeira terraplenagem, o
  **terreno original é guardado no próprio Toposolid**. Cada ajuste refaz o terreno **a partir do original** com **todas** as
  vias com greide e obras do projeto: regerar não acumula aterros, e apagar uma obra devolve o terreno natural.
* Vias com greide moldam o terreno: plataforma sob o pavimento e **taludes de corte e aterro** até o terreno natural –
  **exceto sob pontes e viadutos** (o terreno fica natural) e nos túneis. Taludes vizinhos viram uma superfície só.
* **Terraplenagem** (botão): refaz tudo; vias escolhidas sem greide passam a moldar o terreno. Opção **novo levantamento**:
  o Toposolid atual vira o terreno natural (depois de editá-lo à mão ou importar outro levantamento).
* **Só calcular (simulação)**: calcula volumes e balanço **sem alterar o Toposolid**.
* **Tipo de terreno**: presets de taludes – solo comum (corte 1:1, aterro 1,5:1), arenoso (1,5:1 / 2:1), rocha (0,5:1 /
  1,5:1), argiloso rijo (0,75:1 / 1,5:1) ou personalizado.
* **Mapa de corte e aterro**: subdivisões coloridas no Toposolid – **vermelho** onde o terreno é cortado, **verde** onde é
  aterrado (mais de 10 cm) – para conferir e apresentar; rode de novo sem a opção para tirar.
* **Relatório**: área terraplenada, corte, aterro, **balanço de massas** com o **fator de homogeneização** (corte in situ por
  m³ de aterro compactado, 1,20–1,40 – DNIT): indica **bota-fora** (sobra de corte) ou **empréstimo** (falta), e os
  **volumes de cada elemento** (via, interseção, obra).
* Cotas conferidas nos vértices inseridos (correção automática de deslocamento constante); **grama** de taludes, ilhas e laços
  como **subdivisões** do Toposolid; volumes de corte e aterro no relatório. Use **Desfazer** para voltar.

## 25. O que foi aplicado dos manuais

O plugin incorporou regras dos manuais estudados (DER-SP Vol. I – Projeto, 2023; MBST Vol. I, II, III, IV, VI, IX;
CET-SP Vol. 10/11/13, Medidas Moderadoras e Dispositivos Delimitadores). O resumo do estudo, com o que cada
regra virou no plugin, está em `docs/ESTUDO-MANUAIS.md`. Em resumo, ficam **calculados pela velocidade** (e sempre
editáveis): distância das placas de advertência e da mensagem "A … m", espaçamento das LRV, comprimento das
transições de canalização, espaçamento das barras do zebrado de canalização, espaçamento de tachas e tachões,
tamanho do SDP, altura das legendas e largura das linhas longitudinais.

## 26. Simulador de Tráfego

**Tráfego → Simulador de Tráfego** lê o projeto inteiro e responde: *a rede funciona? onde trava? por quê? o que fazer?*

**O que é lido do projeto.** Vias (eixo, sentidos, número de faixas por sentido, faixas de ônibus, estacionamento,
ciclofaixa, canteiro central, largura entre meios-fios, velocidade e **hierarquia** – CTB art. 60/61 – e o **greide**),
**interseções** e o seu controle (PARE, Dê a preferência, semáforo, sem sinalização), **rotatórias** (raio, faixas),
**balões** (cul-de-sac), cruzamentos de eixos sem interseção (preferência de quem vem pela direita – CTB art. 29, III –
ou **em desnível** quando uma via passa sobre a outra), **placas** (R-1, R-2, R-19, A-14...), **faixas de pedestres**,
**rampas**, **vagas** e **moderação** (lombadas, platôs). As pontas livres das vias e os balões são as **entradas e
saídas** do tráfego.

**Cenário (painel da esquerda).**

* **Demanda**: baixa (entrepico), média, hora de pico ou saturada, com **crescimento** para o horizonte de projeto. Os
  volumes de pico por faixa vêm da hierarquia de cada entrada; **informe as contagens** nas entradas (campos *Volumes
  nas entradas*) para resultados de projeto – o campo mostra o volume estimado.
* **Frota**: % de caminhões e ônibus (equivalentes em carros de passeio), **fator de hora de pico**.
* **Pedestres** por travessia (verde mínimo de pedestres no semáforo e bloqueios nas travessias do meio da quadra).
* **Semáforos**: ciclo **otimizado** (Webster) ou fixo.
* **Microssimulação**: duração, aquecimento e semente aleatória.

**Análise (HCM).** A demanda é distribuída entre as entradas (modelo gravitacional) e alocada na rede por **equilíbrio**
(o motorista evita o cruzamento congestionado). Para cada cruzamento: capacidade, **v/c**, **atraso** e **nível de
serviço** A–F de cada aproximação, **fila (95%)** e, no semáforo, o **plano**: fases, verdes, ciclo e – quando o produto
conversão à esquerda × fluxo oposto passa do critério – **fase protegida** (verde antecipado ou fase de esquerdas).
PARE/Dê a preferência e preferência à direita por aceitação de brechas; rotatórias pelo modelo do HCM 6; trechos pela
velocidade (urbano) ou densidade (rodovias).

**Microssimulação.** Cada veículo (carro, caminhão, ônibus) segue o da frente (modelo IDM), escolhe a faixa (à direita
para converter à direita, à esquerda para converter à esquerda, a mais vazia para seguir), para no PARE, cede a
preferência, espera o verde, converte à esquerda nas brechas do fluxo oposto (e no entreverdes), entra na rotatória
quando o anel está livre, não entra no cruzamento sem espaço para sair (CTB art. 45), freia nas lombadas e para para os
pedestres. A simulação mostra o que a análise nó a nó não vê: **filas que alcançam o cruzamento anterior** e travamentos
em cadeia.

**Mapa (centro).** Trechos coloridos por **nível de serviço**, v/c, velocidade, volume ou velocidade simulada; nós com o
nível e o atraso; semáforos com a fase de cada instante (verde, amarelo, vermelho; ciano = esquerda protegida) e os
**veículos animados** (cor pela velocidade – vermelho parado; caminhões cinza, ônibus azuis). Roda do mouse = zoom,
arrastar = mover, **botão direito = enquadrar**, clique num nó ou trecho = detalhes. Controles de reprodução: ▶/❚❚,
velocidade (1× a 20×) e linha do tempo.

**Abas (direita).**

* **Diagnóstico** – cada item com gravidade (crítico, atenção, informação), o problema, **por quê** e **o que fazer**:
  capacidade (trechos saturados, aproximações E/F, fila que alcança o cruzamento anterior, ciclo longo, semáforo com
  pouco tráfego, **critério de semáforo** do MBST Vol. V, fase protegida e faixa exclusiva de conversão), rede (vias
  desconectadas, nós sem saída, viagens sem caminho, cruzamentos sem interseção, desníveis), sinalização (preferência
  sem placa, via principal de hierarquia menor, falta de R-1/R-2, A-14 antes do semáforo, R-19), segurança (ângulo
  agudo, via local ligada a rodovia, cruzamentos próximos demais), legislação (velocidade acima da máxima da hierarquia –
  CTB art. 61; vagas a menos de 5 m da esquina – CTB art. 181), geometria (raio de curva e rampa para a velocidade),
  moderação (lombada em arterial – Res. CONTRAN 600/2016), pedestres e acessibilidade (travessia no meio da quadra acima de
  60 km/h, travessia longa sem refúgio, faixa sem rebaixamento – NBR 9050). Filtre por gravidade ou tema; **Ver no mapa**
  (ou duplo clique) aproxima o local; **Selecionar no modelo** fecha a janela com os elementos envolvidos selecionados.
* **Indicadores** – demanda, velocidade média, atraso total, cruzamento mais crítico, distribuição dos níveis, emissões
  de CO₂ e os resultados da simulação (viagens, tempo médio, atraso, paradas).
* **Cruzamentos** e **Trechos** – tabelas completas (movimentos por conversão, fases e verdes).
* **Relatório** – o texto completo (rede, demanda, cruzamentos, trechos, diagnóstico, método e limitações).

**Botões.** **Desenhar na vista ativa** cria na planta o **mapa de níveis de serviço** (regiões coloridas sobre as faixas
de cada sentido, o nível e o atraso de cada cruzamento, título e legenda; desenhar de novo substitui o anterior nessa
vista). **Exportar relatório** grava o texto (.txt) e a tabela das aproximações (.csv, abre no Excel) em
`%AppData%\SinalizaBIM`. **Copiar relatório** leva o texto para a área de transferência.

> A demanda padrão é **estimada**. Para decidir a implantação de semáforos, faixas adicionais ou rotatórias, use contagens
> classificadas na hora de pico e confirme os critérios com contagens de 8 horas (MBST Vol. V).

### 26.1 Cenários salvos no projeto

No topo do painel da esquerda: escolha um **cenário**, dê **nome** e **notas** e use **Salvar** / **Salvar como novo** /
**Excluir**. O cenário guarda **todas** as opções (demanda, crescimento, frota, FHP, pedestres, semáforos, coordenação,
ônibus, custos, microssimulação), os **volumes contados nas entradas** e os **ajustes de cada cruzamento** – e fica
gravado **no próprio arquivo do Revit** (Informações do projeto; salve o arquivo). A aba **Cenários** tem *Criar cenários
de exemplo* (pico atual, horizonte de 10 anos, entrepico, teste de estresse).

### 26.2 Ajustes por cruzamento e contagens classificadas

Na aba **Cruzamentos**, selecione o cruzamento e, em **Ajustes deste cruzamento no cenário**:

* **Controle** do cenário – teste PARE, Dê a preferência, semáforo, **rotatória** ou sem controle **sem mexer no projeto**.
* **Ciclo** e **verdes** fixos (ex.: `42; 30; 12`, na ordem das fases).
* **Contagem classificada** de cada aproximação: total (veh/h) e % à esquerda, em frente, à direita e retorno. Vazio =
  a alocação da rede.

**Aplicar ao cenário e recalcular** refaz a análise. **Gravar plano semafórico no projeto** grava o plano calculado na
interseção (e ele passa a valer: *Usar os planos gravados nas interseções*). **Aplicar este controle no projeto** troca o
controle da interseção de verdade (placas, retenções e faixas refeitas). Rotatória: use a ferramenta Rotatória.

O **diagrama de tempos** mostra cada fase ao longo do ciclo (verde, amarelo, vermelho), a defasagem e a origem do plano
(otimizado, gravado, fixo do cenário, coordenado).

### 26.3 O que mais é lido do projeto

* **Largura variável**: a faixa mais estreita do trecho entra no fator de largura (HCM); estreitamento abaixo de 2,70 m é
  acusado.
* **Baias de ônibus** (recuos) e **pontos sem baia** (placa de ponto de ônibus ou abrigo junto ao meio-fio): o ônibus
  para fora da faixa ou **na faixa** (bloqueio – fator fbb do HCM), com *Ônibus/h que param em cada ponto* e *Embarque
  por parada*.
* **Faixas de desaceleração** e **ilhas de conversão à direita**: quem converte à direita sai da fila dos demais; fila
  maior que a faixa é acusada. **Faixas de aceleração** aparecem no mapa.
* **Bolsões de conversão à esquerda**: quem espera para converter não bloqueia a faixa direta.
* **Greide**: equivalente de veículos pesados maior nos aclives; **descidas longas** (≥ 5 % em mais de 1 km, ou mais de
  60 m de desnível) **sem área de escape** são críticas.
* **Sonorizadores**: rodovias e vias rápidas longas sem eles recebem recomendação.
* **Pedestres** nas faixas do cruzamento seguram a conversão à direita no começo do verde.

### 26.4 Coordenação semafórica (onda verde)

*Coordenar os semáforos*: ciclo **comum** (o maior da rede, ou o que os verdes de pedestres exigirem), **defasagens**
pela progressão ao longo dos trechos entre semáforos (sentido de maior volume; velocidade da via ou a informada) e
**fator de progressão** do HCM nas aproximações coordenadas. A microssimulação usa as defasagens.

### 26.5 Microssimulação ampliada

Além do descrito acima: **troca de faixa** (modelo MOBIL – obrigatória para chegar à faixa da conversão ou do ponto de
ônibus; para ultrapassar, com a regra de manter a direita, CTB art. 29), **ônibus parando** nos pontos (na baia, voltam
à faixa quando há brecha) e **pedestres** nas conversões. Indicadores de trocas de faixa e paradas atendidas.

### 26.6 Segurança viária e custos

* **Pontos de conflito** de cada cruzamento (FHWA): cruzamentos, convergências, divergências e conflitos com pedestres –
  a rotatória elimina os cruzamentos; o semáforo deixa só as conversões permitidas.
* **Acidentes previstos por ano** pelas funções de desempenho do **HSM** (cap. 12, vias urbanas), com VDM = volume da
  hora ÷ **fator K**, e fatores para rotatória, bolsões, esquerda protegida e ilhas. **São referências internacionais
  sem calibração local**: use para **comparar alternativas**, não como previsão.
* **Custos anuais**: tempo perdido (valor do tempo × ocupação), combustível (consumo pela velocidade), CO₂ e acidentes
  (custo médio por acidente) × horas por ano na situação. Todos os valores são editáveis no painel.

### 26.7 Comparar cenários e gravar no modelo

A aba **Cenários → Comparar os cenários salvos** roda a análise de cada cenário (e do atual) na mesma rede e mostra lado a
lado: demanda, velocidade, atraso total, pior cruzamento, cruzamentos E/F, trechos saturados, pontos de conflito,
acidentes previstos, CO₂, custo anual e diagnósticos críticos (★ = melhor) – e o atraso/nível de cada cruzamento em cada
cenário. **Gravar resultados no modelo** preenche os parâmetros **SV_NivelServico** e **SV_Trafego** (cenário, volume,
atraso, controle, ciclo, acidentes de referência) nos elementos das interseções, rotatórias e vias – use em tabelas e
filtros de vista. O mapa mostra os pontos de ônibus (quadrado vazado = baia), as faixas auxiliares e as áreas de escape.

### 26.8 Sinalização interpretada – o simulador lê o que foi projetado

Toda vez que o simulador abre (ou que se clica em **Reler o projeto**), ele percorre **toda a sinalização do projeto** e
traduz cada elemento no que ele significa para o motorista: em que trecho e em que sentido vale, em que cruzamento, em
que faixa. A associação é feita pela **posição** e, nas placas e setas, pela **direção** para onde o elemento está
voltado (a placa vale para o sentido de quem a vê de frente). Nada precisa ser informado à mão.

**Placas (sinalização vertical de regulamentação)**

| Placa | Efeito na análise e na microssimulação |
|---|---|
| R-1 PARE / R-2 Dê a preferência | a aproximação (cruzamento a até 70 m à frente) passa a parar / ceder; um cruzamento sem controle vira PARE ou Dê a preferência nas aproximações sinalizadas |
| R-3 Sentido proibido | o sentido do trecho é fechado – a via passa a ser de mão única e as rotas mudam |
| R-4a/R-4b, R-5a/R-5b | proíbe a conversão à esquerda / à direita / o retorno naquela aproximação (a demanda é redistribuída) |
| R-25a–d, R-26 | movimentos obrigatórios (siga em frente, só à esquerda/direita…) – os demais giros ficam proibidos |
| R-19 Velocidade máxima | o limite da legenda vale a partir da placa, no sentido dela, até a próxima R-19 ou o fim da via (velocidade livre, capacidade e simulação) |
| R-7 | ultrapassagem proibida no trecho |
| R-8a/R-8b | troca de faixa proibida no trecho (a microssimulação não muda de faixa) |
| R-6a/R-6c e R-6b | sem estacionamento / estacionamento regulamentado (manobras de vaga reduzem a capacidade – HCM) |
| R-9, R-14 a R-18 | caminhões fora do fluxo do trecho (o volume equivalente cai) |
| R-10, R-32, R-39 | trecho fechado ao tráfego geral (automotores proibidos, exclusivo de ônibus ou de caminhões) |
| Demais (advertência, indicação…) | listadas como informativas, sem efeito na circulação |

**Marcas no pavimento**

| Marca | Efeito |
|---|---|
| LFO-1, LFO-3 (e LFO-4 em um sentido) sobre o eixo | ultrapassagem proibida na parcela do trecho coberta |
| LMS-1 entre faixas do mesmo sentido | troca de faixa proibida |
| LRE / LDP atravessando uma aproximação | linha de parada do semáforo/PARE; num cruzamento sem controle, cria PARE / Dê a preferência |
| Legenda PARE / símbolo SDP | reforça o controle ou cria a parada / a preferência na aproximação |
| LRV, MCF | redução de velocidade no ponto (MCF: 20 km/h na passagem em nível) |
| Legendas DEVAGAR, ESCOLA (30 km/h), LENTO, CUIDADO, PEDESTRE, HOSPITAL | os motoristas reduzem junto à legenda |
| Legenda ÔNIBUS / EXCLUSIVA numa faixa | aquela faixa fica só para ônibus |
| Setas PEM-F/D/E/FD/FE/DE/RE/RD (até 90 m do cruzamento) | define os movimentos de **cada faixa** – a demanda escolhe a faixa pela seta (faixa exclusiva de conversão = fila própria) |
| Zebrados e canalizações sobre faixas | as faixas cobertas ficam fechadas no trecho (gargalo, convergência antes) |
| Vagas pintadas junto ao bordo | estacionamento com manobras no trecho |

**Dispositivos, barreiras e semáforos**

| Elemento | Efeito |
|---|---|
| Barreira, separador, balizadores ou tachões **sobre o eixo** de via de mão dupla | sentidos separados: sem ultrapassagem e, nos cruzamentos que a separação atravessa, só conversão à direita (sem cruzar nem virar à esquerda) |
| Balizadores/tachões **entre faixas** do mesmo sentido | troca de faixa impedida fisicamente |
| Cones, cavaletes, tambores, barreiras **dentro de uma faixa** | faixa interditada no trecho (obra) |
| Bloqueio **atravessando** a pista (≥ 60 % de um sentido) | sentido bloqueado – o tráfego procura outro caminho |
| Semáforo (elemento urbano) junto a um cruzamento | o cruzamento passa a ser semaforizado (se ainda não era) |
| Semáforo junto a uma faixa de pedestres no meio da quadra | travessia semaforizada: vermelho para os veículos 22 s a cada 75 s (18 s de pedestres) |
| Proteções laterais, dispositivos na calçada ou na ciclovia | informativos (não afetam as faixas de tráfego) |

A aba **Sinalização (n)** da janela lista tudo o que foi lido, com o efeito de cada item e o motivo de um item **não**
ter sido associado (ex.: "sem cruzamento a menos de 70 m à frente, no sentido da placa", "não está sobre o eixo de uma
via de mão dupla"). Filtre por *Aplicadas*, *Não associadas*, *Placas*, *Marcas* ou *Dispositivos e semáforos*; duplo
clique mostra o item no mapa. O relatório traz o bloco **Sinalização interpretada** e o diagnóstico acusa as
**incoerências**: PARE num cruzamento semaforizado, PARE em todas as aproximações, PARE na via de hierarquia maior,
seta pintada contradizendo uma placa, R-19 acima da velocidade da hierarquia, faixas e trechos fechados, separação
central cortando conversões e sinalização que não ficou associada a nada.

**Mudou a sinalização?** Com a janela aberta, altere o projeto e clique em **Reler o projeto** (aba Sinalização): a
janela reabre no **mesmo cenário** com a rede atualizada. Os comandos que alteram o projeto a partir da janela
(trocar o controle de um cruzamento, gravar plano) já oferecem a releitura.

### 26.9 Visualização do tráfego

O mapa foi redesenhado para parecer uma planta de verdade: **calçadas** com o meio-fio contornado, **asfalto** com
canteiro central, miolo dos cruzamentos contínuo, e – com zoom – o **eixo amarelo** (contínuo onde a ultrapassagem é
proibida), divisórias, bordos, **faixas de pedestres**, **linhas de retenção**, **setas por faixa** lidas do projeto e
faixas fechadas zebradas. Trechos fechados aparecem hachurados em vermelho. Parado, cada sentido é tingido pelo nível de
serviço; na animação o nível vira uma fita junto ao bordo, para os veículos ficarem em evidência.

* **Veículos**: com zoom, carros com carroceria chanfrada, para-brisa e vidro traseiro nas **cores reais da frota**,
  ônibus com faixa de janelas e ar-condicionado, caminhões com cavalo e carreta, **lanternas de freio acesas** quando
  freiam ou estão parados, e sombra; sem zoom, cada veículo vira um ponto. **Veículos pela velocidade** troca as cores
  reais pela rampa vermelho (parado) → verde (livre).
* **Semáforos** com a cor da fase de cada instante (com brilho), ícones da **sinalização lida** (PARE, dê a preferência,
  velocidade máxima, bloqueios) – liga/desliga em **Sinalização** –, pontos de ônibus (quadrado vazado = baia), faixas
  auxiliares e áreas de escape.
* **Painel da animação**: tempo simulado, veículos na rede, quantos estão parados, velocidade média instantânea e barra
  de progresso.
* **Veículos**, **Rótulos** e **Sinalização** ligam/desligam cada camada; roda do mouse = zoom no cursor, arrastar =
  mover, clique = selecionar cruzamento/trecho.

**Planta real.** Com **Planta real** marcado (padrão), o mapa desenha o projeto como ele é: as mesmas peças que o
plugin gera no Revit – pavimento das vias, interseções e rotatórias, calçadas, meios-fios, canteiros, ilhas e **toda a
sinalização horizontal** (eixos, divisórias, bordos, faixas de pedestres, retenções, setas, zebrados, legendas). O
desenho fica em cache (a animação continua leve). Desmarcado, volta ao esquema das faixas pelo eixo.

**Setas dos veículos.** Cada veículo **pisca a seta** do lado certo ao trocar de faixa, nos 45 m antes de uma conversão
e durante ela, e à direita para sair da rotatória (CTB art. 196).

### 26.10 Microssimulação coesa – fila e lentidão, nunca choque

Com muito tráfego os veículos **formam fila e andam devagar**, sem se atravessar:

* dentro do cruzamento cada veículo segue a trajetória da sua faixa de origem para a faixa de destino; trechos
  compartilhados (fusões, anel da rotatória) são percorridos em fila, e as **zonas de conflito** são ocupadas por um de
  cada vez – quem já passou da linha termina a travessia, quem chega **não entra no cruzamento para ficar parado dentro
  dele** (CTB art. 45) e espera na linha;
* a linha de retenção fica antes do cruzamento (não no centro do nó) e o veículo só cruza a linha com espaço na saída;
* troca de faixa só com espaço (inclusive ao lado de quem ainda está entrando no cruzamento ou trocando de faixa), com
  cooperação de quem vem atrás quando a troca é obrigatória; quem ficou na faixa errada espera, refaz a rota ou converte
  com cuidado;
* rotatória: ocupação máxima do anel (sem travamento circular), as duas faixas de uma entrada larga entram
  **alternadas** num anel de uma faixa e só com espaço para o veículo inteiro;
* calibração urbana com **motoristas diferentes entre si**: carros de 2,2 a 3,0 m/s² e intervalo desejado de 0,75 a
  1,25 s; caminhões e ônibus de 1,0 a 1,4 m/s² e 1,2 a 1,6 s (sorteio próprio – a demanda e as rotas não mudam);
* passo de cálculo de **0,25 s** (antes 0,5 s): arrancadas e frenagens mais suaves, fila descarregando com intervalo de
  ~2,7 s por veículo no verde (vazão de saturação próxima de 1 300–1 400 veíc/h/faixa, típica de via urbana brasileira);
* **pedestres visíveis** no mapa atravessando nas faixas do meio da quadra (grupo saindo dos dois lados quando o
  semáforo de pedestres abre, a 1,2 m/s).

O Autoteste confere que não há sobreposição de veículos na animação.

### 26.11 Semáforos no simulador: escolher, temporizar e recomendar

**Onde fica:** botão **🚦 Semáforos** na faixa de opções (painel Tráfego) – abre o simulador direto na aba
**🚦 Semáforos**. Também: aba *🚦 Semáforos* dentro do simulador, ou **clique com o botão direito** em qualquer
cruzamento ou trecho do mapa → *Tornar … semaforizado*, *Editar tempos de …*, *Recomendar tempos deste semáforo*,
*Remover o semáforo (cenário)*, *Criar semáforo de travessia aqui*, *Testar soluções para este cruzamento*.

A aba **🚦 Semáforos** reúne tudo numa tabela só (um semáforo por linha: ativo, local, tipo, ciclo, verdes,
entreverdes, defasagem e o resultado – atraso e nível de serviço):

1. **➕ Adicionar no mapa** – clique no cruzamento (semáforo veicular) ou no trecho (travessia no meio da quadra);
2. **Escolher objeto no projeto…** – clique num elemento do Revit (grupo focal, faixa, interseção);
3. edite os tempos direto na tabela e clique **Aplicar e recalcular**;
4. **Recomendar o selecionado** / **Recomendar todos** (Webster/HCM) ou, para uma via, **onda verde**;
5. **Gravar no projeto** leva os planos para as interseções.

Na primeira abertura aparece uma dica explicando esses passos.

* **🚦 Semáforo no mapa** (barra do mapa): clique num **cruzamento** para torná-lo semaforizado no cenário (e editar os
  tempos) ou num **trecho / faixa de pedestres** para criar um **semáforo de travessia no meio da quadra** – com ciclo,
  verde de pedestres, entreverdes, defasagem, *Ativo* (desmarque para testar sem o semáforo) e **Sugerir tempos**
  (verde de pedestres pela largura a 1,2 m/s + 4 s, ciclo e defasagem do semáforo vizinho da via).
* **Escolher objeto no projeto…** (aba Cruzamentos): a janela fecha, você clica no elemento do Revit que será o
  semáforo (grupo focal, placa, faixa de pedestres, interseção) e ela reabre no mesmo cenário com o semáforo criado.
* **Tempos de cada semáforo** (aba Cruzamentos): ciclo, verdes de cada fase, **entreverdes** (amarelo + vermelho geral)
  e **defasagem**; o diagrama de tempos mostra o resultado. **Gravar plano semafórico no projeto** leva tudo para a
  interseção.
* **Recomendação de tempos**: escolha **Este semáforo**, **Todos os semáforos do cenário** ou **Todos os cruzamentos de
  uma via** (com **onda verde** – ciclo comum e defasagens pela progressão no sentido de maior volume) e clique em
  **Recomendar tempos**: ciclo e verdes pelo método de Webster/HCM (verdes mínimos de veículos e de pedestres), com o
  **antes × depois** de cada cruzamento. **Aplicar no cenário** fixa os tempos; **Aplicar e gravar no projeto** grava o
  plano em cada interseção. Os demais semáforos da rede não mudam.

### 26.12 O que fazer para resolver – soluções testadas

No **Diagnóstico** (ou na aba Cruzamentos, *Testar soluções para este cruzamento*) o botão **Testar soluções** roda a
análise da rede inteira com cada alternativa cabível e mostra o **efeito medido**, da melhor para a pior:

| Alternativa | Quando aparece |
|---|---|
| Semaforizar o cruzamento | cruzamento sem semáforo |
| PARE / Dê a preferência na secundária | cruzamento sem controle com via principal definida |
| Rotatória | cruzamento comum |
| Retemporizar o semáforo (Webster) | semáforo |
| Onda verde na via principal | semáforo com outros semáforos na mesma via |
| Proibir as conversões à esquerda | aproximações com conversões à esquerda |
| Travessia: ciclo curto / sem semáforo | trecho com semáforo de travessia |
| Bolsões de conversão à esquerda | aproximações com esquerda sem bolsão |

Cada item traz o atraso e o nível do local antes → depois, o atraso total e a velocidade média da rede, os itens
críticos, **o que fazer no projeto** para implantar e o botão **Aplicar no cenário e simular**. Só é marcada como
solução (✔) a alternativa que melhora o local em pelo menos 10 % sem piorar a rede; quando nenhuma resolve sozinha, a
recomendação do diagnóstico indica as medidas de projeto (faixa adicional, bolsão, binário etc.).

**Critério normativo (MBST Vol. V).** A alternativa só é marcada como solução quando é justificável pelas normas: o
semáforo exige volumes mínimos (via principal ≥ 500 veíc/h e aproximação secundária ≥ 150 veíc/h); rotatória com mais
de 3 600 veíc/h e PARE/preferência com principal > 1 500 e secundária > 250 veíc/h recebem aviso (⚠) de que não
atendem. Uma alternativa que melhora o atraso mas não passa no critério aparece com o aviso e não é recomendada.

**Pacote completo de projeto.** Cada alternativa traz (expansor *O que entra no projeto*) a lista de **tudo o
que precisa ser implantado**, com a norma de cada item:

* **Semaforizar**: grupo focal veicular em cada aproximação (repetidor quando há 3+ faixas ou canteiro), grupos focais
  de pedestres, faixas de pedestres com rampas (NBR 9050) e piso tátil (NBR 16537), LRE a 1,6 m da faixa,
  A-14 "Semáforo à frente" nas vias ≥ 60 km/h (distância pela velocidade), plano semafórico gravado; remove PARE/R-2;
* **PARE / Dê a preferência**: R-1 ou R-2 + LRE/LDP + legenda, e aviso de verificar a visibilidade (triângulo);
* **Rotatória**: tipo pelo volume (1 ou 2 faixas), ramos com ilha separadora, R-33, A-12, LDP e travessias;
* **Retemporizar / onda verde**: planos com ciclo, verdes, entreverdes e defasagens de cada cruzamento;
* **Proibir esquerdas**: R-4a em cada aproximação e eixo contínuo (bloqueio da conversão);
* **Bolsão de esquerda**: bolsões nas aproximações (tipo IV) com setas e LFO;
* **Travessia**: grupos focais + LRE nos dois lados, ou retirada do semáforo com A-32b.

**✔ Aplicar no PROJETO** grava tudo no Revit num único Desfazer: muda o controle da interseção (ou converte em
rotatória), liga travessias, rampas, linhas de retenção e bolsões, grava o plano semafórico (e os planos da onda verde
nos outros cruzamentos), cria os semáforos e placas como elementos urbanos/placas nas posições indicadas e as LRE, e
remove os semáforos e placas que a solução substitui. Depois o simulador relê o projeto.
