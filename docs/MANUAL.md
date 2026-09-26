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
| **Vias** | **Via** (Nova Via · Sinalizar Via · Pista · Desenhar Eixo) · **Conexões** (interseções, rotatórias, cul-de-sac – uma ou todas) · **Calçadas** (Meio-fio/Calçada/Sarjeta · Orelha · Área de Calçada · Canteiros · Cul-de-sac · Rampas) · Hierarquia Viária · Elementos Urbanos · Mostrar/Ocultar Eixos |
| **Sinalização Horizontal** | **Linhas** (longitudinal · transversal · faixa de pedestres) · **Zebrado** (inclui área de conflito MAC) · **Inscrições** (setas/símbolos · legendas) · Vagas · **Complementos** (tachas · piso tátil · ciclovia · quebra-mola) |
| **Sinalização Vertical** | **Placas** (placas · detalhar placas · quadro de placas · quadro de legenda) · **Segregação** (bloqueios físicos · guard rail) |
| **Detalhamento** | **Detalhar** (anotar · cotar seção · detalhe típico · quadro de quantitativos · notas · norte) · Quantitativos |
| **Editar** | Editar · Atualizar Todas · Selecionar Conjunto · Alternar 2D/3D · Configurações · Catálogo · Normas |

Os botões com seta (▾) agrupam ferramentas afins; o botão mostra a última usada do grupo.

---

## 1. Caminhos (eixos) e associatividade

Qualquer marca linear, zebrado ou conjunto de vagas pode usar como caminho:

| Opção | Resultado |
|---|---|
| **Selecionar linhas existentes** | Linhas de modelo ou de detalhe (retas, arcos, splines, elipses). Várias linhas são encadeadas automaticamente, invertendo o sentido quando necessário. **Associativo**: editar as linhas regenera a marca. |
| **Desenhar por pontos** | O plugin cria linhas de modelo no estilo *SV - Eixo de sinalização* (tracejado magenta) e as associa à marca. |
| **Dois cliques** | Para marcas transversais: cada par de cliques cria uma marca (repete até ESC). Não associativo. |

Se as linhas selecionadas formarem trechos separados, cada trecho recebe o padrão de forma
independente (o plugin avisa).

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
| Faixa exclusiva (ônibus) | Linha MFE contínua na divisa com as faixas comuns + legenda repetida (ÔNIBUS). |
| Faixa preferencial (ônibus) | Linha MFE seccionada + legenda repetida. |
| Ciclofaixa | Linha CIC-LD, pintura vermelha, bicicletas e setas repetidas; segregação física opcional. |
| Faixa de estacionamento | Vagas do tipo escolhido (paralelas, em ângulo, PcD, carga e descarga...) com linha de fundo no lado da pista. |
| Acostamento | LBO no bordo da faixa. |
| Faixa de segurança / transição | Zebrado com linhas de canalização (buffer entre fluxos, ciclofaixa etc.). |
| Canteiro lateral físico / pintado | Meios-fios + grama (0,15 m) ou zebrado. |
| Calçada | Meio-fio, faixa de serviço (gramada ou em concreto), faixa livre e faixa de acesso; aviso se a faixa livre for menor que 1,20 m (NBR 9050). |

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

Para vias já existentes (ou criadas por versões anteriores) use **Via → Hierarquia Viária**: selecione
elementos das vias e escolha a hierarquia (opcionalmente ajustando a velocidade das linhas); as
interseções, rotatórias e cul-de-sacs dessas vias são atualizados.

## 2.2 Nova via – conexão natural com o sistema viário

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

**Via → Nova Via** (ou *Sinalizar Via* com *Desenhar a via por pontos*) desenha a via clicando os pontos do
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

## 2.4 Bloqueios físicos

Comando **Bloqueios Físicos** (painel *Segregação Física*): escolha o dispositivo, ajuste
dimensões, espaçamento entre centros (0 = contínuo), deslocamento e recuos, e selecione/desenhe o
caminho. Os dispositivos são modelados em 3D com a forma real e aparecem nos quantitativos em
unidades ou metros:

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

**Tachas/tachões** contam unidades (coluna *Unid.* nos quantitativos). **Piso tátil** (PTA/PTD)
segue a NBR 16537 (larguras de 0,25 a 0,60 m).

---

## 4. Faixa de Pedestres

1. Escolha FTP-1 (zebrada), FTP-2 (paralela) ou MCC (cruzamento rodocicloviário) e a variante.
2. Ajuste a largura da faixa (FTP-1) e o recuo dos bordos.
3. Linhas de retenção: largura (0,30 a 0,60 m), distância livre até a faixa (padrão 1,60 m), lados e
   extensão (**meia pista** em vias de mão dupla, **pista inteira** em mão única).
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
  as ferramentas nativas (tipo, material, estrutura). Se você trocar o tipo de um piso, a troca é mantida
  quando a via é regenerada. Desligue em **Configurações** para voltar à forma direta. A sinalização
  pintada e os dispositivos continuam como modelo genérico.
* **Modelo 3D**: sólido fino (Modelos genéricos). A espessura padrão é a do material, com mínimo
  modelável de 3 mm (configurável) – películas de tinta reais são finas demais para sólidos do Revit.
  Os quantitativos usam sempre a **área**, independentemente da espessura.
* **Acompanhar a superfície**: cada peça é posicionada e inclinada conforme o Toposolid/piso/topografia
  sob ela (traços longos são divididos em peças de até 2 m). Opcionalmente selecione as superfícies.
* **Detalhe 2D**: regiões preenchidas na vista ativa (planta ou vista de desenho).
* **Material**: define espessura e consumo nos quantitativos (tinta acrílica, termoplástico, plástico
  a frio, laminado elastoplástico...).

**Alternar 2D/3D** converte as marcas selecionadas (a conversão para 2D usa a vista ativa).

---

## 9. Edição e atualização

* **Editar**: selecione uma marca (qualquer elemento dela) → a janela abre com os valores atuais →
  *Aplicar* regenera a marca mantendo o mesmo identificador (tabelas e seleções continuam válidas).
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
* **Criar tabela no Revit**: *SV - Quantitativos de sinalização* agrupada por **categoria** (com
  cabeçalho e subtotal), hierarquia viária, grupo, código e cor. **Tabelas por categoria** cria uma tabela para cada
  categoria. O parâmetro compartilhado **SV_Categoria** também pode ser usado em filtros de vista.
* Para a prancha, use **Detalhamento → Quadro de Quantitativos** (seção 16).

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
1,50 m, 0,15 m de altura, zebrado no platô) e lombada invertida. Clique os dois bordos da pista.
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
  (não duplica). Para ajustar só um símbolo, use **Editar** sobre ele.
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
  meio-fio), com **meio-fio curvo**;
* refaz as **calçadas** junto à esquina e as **pontas dos canteiros centrais** (nariz com meio-fio,
  terminando 1 m antes da pista transversal; a travessia corta o canteiro formando **refúgio** no nível da
  pista);
* interrompe a sinalização horizontal das vias no cruzamento e na aproximação (até depois da linha de
  retenção) e remove as **vagas a 5 m da esquina**; os demais trechos das vias ficam intactos, qualquer
  que seja o ângulo;
* cria em cada ramo **faixa de pedestres** e **rebaixamentos de calçada** nas duas pontas da travessia.

Funciona para **qualquer geometria**: cruzamentos ortogonais ou oblíquos, entroncamentos em **T** e em
**Y**, vias curvas e nós com vários ramos. Em ângulos agudos as esquinas continuam arredondadas.

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

**Via → Rotatória**: clique o centro (o ponto é encaixado no cruzamento de vias mais próximo). Os ramos
são detectados das vias que passam pelo centro (ou informados por ângulo, para uma rotatória isolada).
Parâmetros: raio da **ilha central** (ajardinada, com árvores opcionais), **faixa galgável** em bloquete
para ônibus e caminhões, número e largura das **faixas da pista giratória** (linha divisória quando há 2
ou mais), **raio de entrada/saída**, calçada em volta e pavimento. Em cada ramo: **ilha separadora** em
gota com **zebrado** de aproximação, **linha de dê a preferência** e **símbolo "Dê a preferência"** na
entrada (circulação anti-horária), **travessia de pedestres** passando pela ilha, **rebaixamentos** e
placas **R-2** e **R-33**. As vias ligadas são recortadas e a interseção existente no mesmo nó é
substituída (com os recortes que ela fazia). A rotatória ligada às vias **acompanha o nó** quando os eixos
são movidos e ganha um **ramo novo** quando uma via é desenhada até ela (**Nova Via**, clicando dentro da
rotatória); também pode ser criada direto na conexão (**Nova Via → Rotatória** ou **Conexão**). Confira as dimensões com o manual do DNIT / órgão local e com o veículo de projeto.

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

## 22. Mobiliário, árvores e placas sobre a calçada

Os elementos urbanos foram redesenhados: **árvore** com tronco, galhos, copa em lóbulos (contorno
orgânico em planta) e canteiro com guia; **palmeira**; **arbusto**; **banco** com ripas de madeira e
apoios de aço com braços; **lixeira** cônica com tampa; **poste** cônico com braço curvo e luminária LED;
**poste de pedestres**; **abrigo de ônibus** com vidro, painel e banco; **paraciclo** tubular; **hidrante**;
**floreira** com arbustos; **placa de rua** e **semáforo** com anteparo e pestanas. Elementos urbanos e
placas são assentados no **topo da calçada** (nível da base 0,15 m, ajustável na janela; 0 = nível da pista).
