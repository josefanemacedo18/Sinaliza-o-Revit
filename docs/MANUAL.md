# Manual de uso – SinalizaBIM

Todos os comandos ficam na guia **SinalizaBIM** da faixa de opções. As janelas mostram uma
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
| **Infraestrutura** | **Drenagem** (boca de lobo / PV · grelha) · **Obras de Arte** (viaduto · ponte · passarela · túnel · trincheira) · **Contenções** (muro de arrimo · talude) · **Nó Viário** · **Terraplenagem** |
| **Detalhamento** | **Detalhar** (anotar · cotar seção com perfil transversal · detalhe típico · quadro de quantitativos · notas · norte) · Quantitativos |
| **Editar** | Editar · Atualizar Todas · Selecionar Conjunto · Alternar 2D/3D · Configurações · Catálogo · Normas |

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

> Mover ou girar diretamente os sólidos/regiões gerados não altera a definição: na próxima
> regeneração a marca volta para o caminho. Para reposicionar, edite as linhas de referência ou use
> **Editar**.

---

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
* **Boca de Lobo / PV**: boca de lobo **simples** (guia chapéu), **dupla**, **com grelha**, **combinada** e **poço de
  visita**. Clique junto ao **meio-fio** de uma via: o dispositivo se alinha à guia, com a **caixa de captação** (paredes,
  fundo, laje e tampão) sob a calçada ou sob a sarjeta, o **rebaixo da sarjeta** com transições e o **tubo de ligação**.
  Longe de vias, clique o ponto e a direção. Módulos lado a lado (bocas triplas).
* **Grelha de Drenagem**: grelha de sarjeta e grelha quadrada de piso com caixa, e **canaleta com grelha contínua** ao
  longo de linhas. Barras **transversais** (seguras para ciclistas), longitudinais ou diagonais; ferro fundido
  (NBR 10160), aço ou concreto; caixilho, vão entre barras e todas as medidas editáveis.
* Referências: DNIT 030/2004-ES, ABNT NBR 10160, diretrizes de drenagem da PMSP.

### 24.2 Viaduto, ponte e passarela
Desenhe o **eixo de ponta a ponta**. O greide sobe pela **rampa máxima** (com curvas verticais) até a altura do tabuleiro;
onde o aterro passaria de 6 m começa a estrutura. Gera: **rampas de acesso em aterro** (taludes ou **terra armada**),
**encontros** com alas, **pilares** (pórtico, circular, duplo, parede, martelo) sobre blocos, **aparelhos de apoio**,
superestrutura em **vigas pré-moldadas**, **viga caixão**, **laje maciça**, **arco inferior**, **arco superior atirantado**
(pendurais e contraventamento), **estaiada** (mastro em H e estais em leque) ou **treliça metálica**, tabuleiro, pavimento,
**passeios**, **barreiras New Jersey** e/ou **guarda-corpos**, **juntas de dilatação**, **iluminação** e **faixas pintadas**.
Ponte: **lâmina d'água** opcional. Passarela: treliça coberta, rampas ≤ 8,33 % (NBR 9050). Pilares descem até o terreno.
Quantitativo em m² de tabuleiro. Referências: NBR 7188/7187, DNIT (gabarito 5,50 m).

### 24.3 Túnel e trincheira
* **Túnel** em **ferradura** (NATM), **circular** (TBM) ou **retangular** (vala coberta): revestimento, arco invertido/laje,
  pavimento, **passeios de serviço**, **iluminação** contínua, **ventiladores de jato** e **emboques** em parede de testa,
  **bisel** ou **pala**. Gabarito vertical garantido. As trincheiras de acesso diante dos emboques são terraplenadas; o plugin
  tenta **escavar o Toposolid** com o túnel (Revit 2025+: *Toposolid → Escavar*), quando o Revit aceita o elemento.
* **Trincheira**: via rebaixada que desce pela rampa máxima até o rebaixo e sobe no fim, entre **muros de flexão**,
  **cortina atirantada** (cabeças de tirantes) ou **terra armada** (placas), com **guarda-corpo** no topo, canaletas ao pé,
  iluminação e **laje de travessia** para a via transversal. O terreno entre os muros é escavado.

### 24.4 Muro de arrimo e talude
* **Muro de arrimo**: **flexão** (parede + sapata com ponta e talão), **gravidade**, **contrafortes**, **gabião** em
  degraus, **terra armada** (placas desencontradas + maciço reforçado) e **cortina atirantada**. Altura variável (início/fim),
  ficha, coroamento, **barbacãs** a cada 2 m, guarda-corpo. A base acompanha o terreno e o **reaterro** atrás do muro é
  levado até o topo no Toposolid. Medido em m² de face.
* **Talude** de **corte** ou **aterro**: inclinação H:V, **bermas** a cada *n* metros de altura, **canaletas** de crista, de pé
  e das bermas, **descidas d'água em degraus** e revestimento (grama, concreto projetado, enrocamento ou solo). A face do
  talude é aplicada ao Toposolid e concordada com o terreno no pé e na crista.

### 24.5 Nó viário (interseção em desnível)
**Diamante**, **diamante com rotatórias**, **trevo completo**, **trevo parcial (parclo)**, **trombeta** e **rotatória em dois
níveis**. Clique o **centro** – sobre o cruzamento de duas vias do plugin, a direção e o ângulo vêm dos eixos (a mais larga é a
principal) – ou um ponto livre e a direção. Gera a via principal em pista dupla (barreira central, iluminação), a transversal
em viaduto, rampas e **laços** com greide (rampa máxima, curvas verticais), rotatórias com ilhas gramadas, aterros com
taludes, barreiras e faixas. **Onde um ramo passa sobre outro vira ponte automaticamente** (tabuleiro, vigas, pilares,
encontros). Gabarito vertical, largura das rampas, raio dos laços (≈ 50 m – 40 km/h), distância dos terminais e extensões
são editáveis; as extensões crescem sozinhas quando as rampas não cabem. Referência: DNIT – Manual de Projeto de
Interseções.

### 24.6 Terraplenagem e topografia (Massa e terreno)
* **Terraplenagem** ajusta o **Toposolid** nativo às **vias, interseções, rotatórias, cul-de-sacs e obras**: as vias e
  conexões continuam **planas no seu nível** (estáveis como sempre) e o terreno se ajusta a elas – plataforma sob a
  estrutura do pavimento, **taludes de corte e aterro** (H:V configuráveis) até encontrar o terreno natural, reaterro de muros,
  faces de taludes, escavação de trincheiras e emboques. Os pontos do Toposolid dentro da área de projeto são substituídos
  pela superfície de projeto (*Modificar subelementos* do Toposolid). A mensagem final traz a **área terraplenada** e os
  **volumes de corte e aterro**. Use **Desfazer** para voltar ao terreno anterior.
* As obras com **"Ajustar o Toposolid ao criar"** marcado fazem isso sozinhas ao criar/editar; com **"Acompanhar o terreno
  natural"**, pilares descem até o chão, muros e taludes apoiam-se no terreno e as rampas de acesso começam na cota do
  terreno. O **terreno natural de referência fica guardado** na obra na primeira geração: depois da terraplenagem o projeto não
  "afunda" ao ser regenerado.
* Precisa de um **Toposolid** (Massa e terreno → Toposolid; uma topografia antiga pode ser convertida). Sem Toposolid, as
  obras são geradas sobre o plano da base.

## 25. O que foi aplicado dos manuais

O plugin incorporou regras dos manuais estudados (DER-SP Vol. I – Projeto, 2023; MBST Vol. I, II, III, IV, VI, IX;
CET-SP Vol. 10/11/13, Medidas Moderadoras e Dispositivos Delimitadores). O resumo do estudo, com o que cada
regra virou no plugin, está em `docs/ESTUDO-MANUAIS.md`. Em resumo, ficam **calculados pela velocidade** (e sempre
editáveis): distância das placas de advertência e da mensagem "A … m", espaçamento das LRV, comprimento das
transições de canalização, espaçamento das barras do zebrado de canalização, espaçamento de tachas e tachões,
tamanho do SDP, altura das legendas e largura das linhas longitudinais.

