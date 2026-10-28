# InfraWorks como referência do SinalizaBIM

Estudo pedido pelo usuário em 2026-10-27 (item 3 do [prompt](PROMPT_INFRAWORKS.md), prioridade máxima). O que o InfraWorks
faz, como o plugin trata o mesmo assunto hoje e para que etapa do [PLANO_DE_ACAO_2](PLANO_DE_ACAO_2.md) vai cada lacuna.

**Fonte.** Ajuda oficial da Autodesk (help.autodesk.com/view/INFMDR/ENU, InfraWorks) e artigos de suporte da Autodesk,
consultados pela ferramenta Autodesk Product Help em 2026-10-27. Cada linha cita o tópico. Nada aqui foi tirado de memória
nem de vídeo; o que a ajuda não diz não está na tabela. **Nenhuma das ferramentas foi executada:** o InfraWorks não roda
neste ambiente.

**Adaptação ao Revit.** O InfraWorks é um modelo de terreno e corredores com edição direta no canvas. No Revit, o plugin
trabalha com elementos do Revit (Toposolid, pisos, sólidos, linhas de modelo, vistas e folhas): o que no InfraWorks é um
"grip" no canvas vira aqui uma janela com a seção ou o perfil e campos editáveis, ou um comando de clique na vista. O que o
Revit já faz sozinho (sol e céu, imagens, nuvem de pontos, vistas) não é refeito.

Situação no plugin: **existe** (faz o mesmo, adaptado), **parcial** (faz parte; a lacuna está dita), **falta** ou **não se
aplica** (fica no Revit ou depende de terceiros).

## 1. Vias por componentes (component roads)

| Ferramenta do InfraWorks | O que faz (pela fonte) | Fonte | No SinalizaBIM hoje | Situação | Etapa |
|---|---|---|---|---|---|
| Via por componentes | via montada por partes – faixas, meios-fios, sarjetas, canteiros, acostamentos, calçadas –, cada uma acrescentada, dividida, apagada ou mudada; montagens salvas para reusar | *About Component Roads* | seção montada ao criar a via (`RoadSetup`, `ElementoSecao`: faixas, ciclofaixa, estacionamento, canteiros, calçada com faixas NBR 9050, acostamento); modelos de seção; desde a 2.24, componentes por trecho mudam a seção entre duas estacas (acrescentar, tirar, largura, dividir) | existe (2.24) | 19 |
| Inserir componente numa "costura" | escolhe a costura entre dois componentes (ou entre a via e o terreno); o componente entra com 30 m e é ajustado por alças e valores; "Align to" leva a ponta ao início ou ao fim da via; mínimo de 2 m | *To insert components into a component road assembly* | recuo acrescentado numa via já criada (baia, aceleração, desaceleração), largura variável entre duas estacas; desde a 2.24, Componentes por Trecho: qualquer elemento acrescentado, tirado ou com outra largura entre duas estacas | existe (2.24) | 19 |
| Transições e divisão de componente | *Transition In/Out* afunila o componente na estaca inicial ou final, com comprimento e afunilamento pelas alças; *Split Component* divide | *To add or modify component road transitions* | transições dos recuos, da largura variável e dos componentes por trecho (retas, em S ou degrau); dividir um trecho numa estaca (2.24) | existe (2.24) | 19 |
| Trocar a montagem inteira | *Replace Assembly* | *To insert components…* | editar a via troca a seção (janela da via, aba Seção) | existe | — |
| Desenho do eixo por PI ou por elementos | cliques no canvas; raio da curva editável na anotação; espirais quando a geometria permite | *To add a component road* | eixo por linhas de modelo do Revit, raio das curvas, eixo com espirais no relatório (TS/SC/CS/ST) | existe (adaptado) | — |
| Anotações no canvas | estacas a cada 50 m, pontos notáveis (BC/EC, TS/SC/CS/ST), comprimentos dos componentes e das transições | *About Annotations for Component Roads* | estaqueamento com equações nos relatórios e no Perfil da Via; Anotar e cotas no detalhamento | parcial: sem anotação automática dos componentes | 25 |
| Greide: manipuladores, PIV | arrastar, acrescentar e tirar PIV; curva parabólica simétrica, assimétrica ou circular; K e comprimento editáveis | *Edit Vertical Geometry*; *To move a PVI*; *To add a vertical curve*; *To review and modify a vertical curve in the Profile View* | Perfil da Via em modos (acompanhar, suavizado, rampa constante, manual com PIVs em texto, nivelado); greide na janela da via | parcial: PIV só por texto, sem acrescentar ou arrastar com o perfil à vista | 18 |
| Profile View | perfil com o terreno existente (linha verde), estruturas, drenagem e camadas; exagero vertical; rastreio da estaca; "Drape Profile"; ligado ao visualizador de seção | *About The Profile View Panel* | gráfico do Perfil da Via (terreno × greide) | parcial: sem edição no gráfico nem ligação com a seção | 18 |
| Cross Section Viewer | seção em qualquer estaca, setas e estaca digitada, vistas de superelevação, montagem e corte/aterro (áreas), exagero vertical | *About The Cross Section Viewer* | perfil transversal desenhado no detalhamento (Cotar seção) | falta: não há visualizador interativo | 18 |
| Superelevação | aplicar e inspecionar pelas estacas críticas | *To inspect superelevated roads…* | superelevação no greide da via (`RoadGrade`), usada pela drenagem e pelos pisos | parcial: sem inspeção por estaca | 18 |
| Taludamento lateral | *Fixed Slope* (até o limite; se não alcança o terreno, face quase vertical) ou *Fixed Width*; limite; talude de corte e de aterro; valores por lado | *To set the roadside grading method* | talude de corte e de aterro por via; desde a 2.24, Taludes por Trecho: talude fixo com limite ou largura fixa, por lado | existe (2.24) | 19 |
| Dividir o taludamento | trechos com estaca inicial e final, talude e material próprios, transição de 5 a 100 m | *To split roadside grading* | Taludes por Trecho: trechos com estaca inicial e final, lado, método e transições (2.24); material próprio por trecho não | parcial: sem material por trecho | 19 |
| Material do corte e do aterro | estilo do corte e do aterro por lado | *To modify the roadside grading cut and fill material style* | mapa de corte (vermelho) e aterro (verde) no Toposolid | existe | — |
| Volumes de terraplenagem | média das áreas entre seções num intervalo de estacas; trecho de estacas; inclui ou não interseções e pontes (comparação de superfícies no nó); CSV; áreas de corte e aterro por estaca | *To view earthwork quantities* | malha e seções a cada 20 m por via, por elemento, CSV e planilha; nós pela malha | parcial: sem trecho de estacas, sem gráfico por estaca nem diagrama de massas | 18 |
| Pontes e túneis por trecho | estaca inicial e final na via | *To add a bridge…*; *To add a tunnel…* | viaduto, ponte, passarela, túnel e trincheira num trecho do eixo | existe | — |
| Muro de arrimo junto à via | muro numa área ao lado da via | *How to measure cut and fill next to the retaining wall* | muros de todos os tipos com terraplenagem | existe | — |
| Marcas de faixa | ligar e desligar, cor e estilo | *To modify lane markings on a component road* | sinalização horizontal completa (MBST Vol. IV) | existe (mais completo) | — |
| Decorações ao longo da via | modelos em uma costura com espaçamento, deslocamento, altura, inclinação, rotação e escala | *To place road decorations*; *To add decorations to road and railway styles* | mobiliário, árvores e placas ao longo de linhas e na calçada | parcial: por linha, não pela costura da via | 19 |
| Regeneração adiada | as edições só refazem a via quando confirmadas | *About The Profile View Panel* | atualização automática que pode ser desligada | existe | — |

## 2. Interseções e rotatórias

| Ferramenta do InfraWorks | O que faz (pela fonte) | Fonte | No SinalizaBIM hoje | Situação | Etapa |
|---|---|---|---|---|---|
| Interseção automática | criada onde duas vias se cruzam | *About Intersections* | interseções automáticas em cruz, T, esconsas, com tipos I–IV | existe | — |
| Zonas de giro | tipo de concordância do meio-fio (curva, curva com afunilamento), raio, ângulo | *About Intersections* | raio das esquinas, curvas reversas, chanfro ou reta nas pontas | existe | — |
| Zonas de alargamento | início e fim pela alça, número de faixas, transição linear ou curva-tangente-curva | *About Intersections* | faixas de aceleração e desaceleração, baias | parcial | 19 |
| Veículo de projeto | muda o desenho da interseção | *About Intersections* | — (trajetória de giro não existe; D-029) | falta | fora (sem método citado) |
| Converter em rotatória e editar | diâmetro inscrito, ilha central, avental, braços, norma | *To convert an intersection to a roundabout*; *To modify roundabout geometry* | rotatórias de todos os tipos, by-pass, faixas, ilhas; conversão pela ferramenta Conexão | existe | — |
| Distância de visibilidade | zonas, envelopes, linhas e triângulos de visibilidade | *About Sight Distance Analysis* | — (K8: só com método e norma citados pelo usuário) | falta | fora até haver método citado |

## 3. Terreno

| Ferramenta do InfraWorks | O que faz (pela fonte) | Fonte | No SinalizaBIM hoje | Situação | Etapa |
|---|---|---|---|---|---|
| Temas do terreno | colore por elevação, orientação ou declividade, com faixas, distribuição e rampa de cores | *To theme terrain*; *About Creating Themes* | Temas do Terreno (2.25): elevação, declividade e orientação do terreno natural ou atual, com legenda, áreas e barras na vista; classes do plugin ou informadas | existe (2.25) | 20 |
| Estatísticas do terreno | ferramenta de medição com dados da superfície e volumes da terraplenagem proposta | *No cut or fill volumes for "Terrain Statistics"* (suporte) | tela do terreno: natural × projetado, área e maiores corte e aterro | parcial | 20 |
| Medir ponto a ponto | distância em linha reta, sem acompanhar o terreno | *To measure point to point* | Revit mede distâncias | não se aplica (Revit) | — |
| Distância 2D e declividade | distância horizontal, vertical e declividade entre dois pontos | *To measure the slope between two points* | Medir no Terreno (2.25): distância horizontal, desnível e declividade entre dois pontos | existe (2.25) | 20 |
| Distância pelo caminho | medida por polilinha que acompanha o terreno (a ajuda remete a ela para medidas sobre o terreno) | *To measure point to point* (nota) | Medir no Terreno (2.25): por caminho, sobre o terreno, com subida, descida, declividade máxima e perfil | existe (2.25) | 20 |
| Áreas de cobertura | cobrem o terreno, moldam-no, abrem buracos e marcam áreas a evitar | *To add or modify coverage areas or create a hole in terrain* | subdivisões do Toposolid nos lotes | parcial | 20 |
| Áreas de terraplenagem (plataformas) | aplainam o terreno na cota dada, com talude e material de corte e aterro | *Grading Areas* | Plataforma (2.25): contorno na cota informada, média ou de equilíbrio, com taludes, na Terraplenagem e no registro; sem material próprio | existe (2.25) | 20 |
| Camadas de superfície e subsolo | camadas com material e opacidade; subsolo para sondagens | *Subsurface Terrain Layers* | terreno natural guardado × atual; Comparar Superfícies (2.25) com volumes e distribuição; Linha de Quebra e Suavizar Terreno (2.25) editam o natural; sem subsolo | parcial: sem camadas de subsolo | 20 |
| Água: lagos, lagoas e rios | polígono (lago, lagoa) ou polilinha (rio) com estilo de água; nível d'água nas propriedades; margem com faixa | *To add or modify water areas and rivers*; *How to change water levels*; *How to control a river or water embankment* (suporte) | — | falta | 21 |
| Simulação de cheias | depende do RiverFlow2D, de terceiros | *New features in 2017 releases* | — | não se aplica (licença de terceiros) | — |

## 4. Drenagem

| Ferramenta do InfraWorks | O que faz (pela fonte) | Fonte | No SinalizaBIM hoje | Situação | Etapa |
|---|---|---|---|---|---|
| Rede automática ao longo da via | bocas de lobo nos pontos baixos e pelo espaçamento máximo, PVs pelos afastamentos, tubos com a declividade mínima; só com meio-fio | *About Analysis-Driven Design*; *To add a drainage network to a road* | série de grelhas ao longo do meio-fio; pontos baixos sem captação apontados (V13) | parcial: sem a rede gerada pela via | 22 |
| Rede manual | tubos, bocas de lobo, PVs e lançamentos um a um | *About Drainage Networks* | PVs, caixas, bocas de lobo e trechos | existe | — |
| Dimensionar a rede | diâmetros, declividades e caixas pela vazão a seção plena | *Drainage Network Workflow* | método racional, Manning a seção plena, diâmetros calculados (Manual SUDERHSA) | existe | — |
| Análise das bocas de lobo | captação, espalhamento e passagem entre duas bocas | *About Drainage Design* | capacidade da sarjeta e engolimento das bocas | existe | — |
| Desempenho hidráulico | linha piezométrica e de energia, tubos afogados e PVs extravasando | *About Drainage Design* | — | falta | 22 |
| Catálogo de peças | tubos e conexões de um catálogo (compartilhado com o Civil 3D) | *About Drainage Design* | diâmetro por trecho; sem catálogo de tubos (material, classe, peça, junta) | falta | 22 |
| Quantitativo da rede | bueiros, bocas de lobo, PVs, tubos e lançamentos | *About Drainage Networks* | dispositivos no quantitativo | parcial: sem peças de tubo, conexões e vala | 22 |
| Bueiros | celular ou circular, número de células, material, n de Manning, configuração da entrada (FHWA HDS-5), condição de jusante | *To modify a culvert solution*; *To add a culvert manually* | talvegue sob aterro sem travessia apontado (V13) | falta: sem bueiro | 22 |
| Bacias | identifica bacias e escoamentos | *About Drainage Design* | áreas de contribuição informadas ou lidas da via | parcial | 22 |
| Chuva do projeto | equações IDF por modelo | *About Drainage Networks* | Projeto de Drenagem com a equação de chuva do usuário | existe | — |

## 5. Áreas, lotes e entorno

| Ferramenta do InfraWorks | O que faz (pela fonte) | Fonte | No SinalizaBIM hoje | Situação | Etapa |
|---|---|---|---|---|---|
| Lotes, servidões e faixas de domínio | importados ou desenhados; área mostrada; a faixa de domínio aparece na vista de corte/aterro | *To import parcels, easements, and right of ways*; *New features in 2017 releases* | loteamento completo (gleba, quadras, lotes, categorias, mínimos, chanfro, quadros, memorial) | parcial: sem faixa de domínio, servidão nem faixa não edificável | 23 |
| Quantitativo de materiais | quantidades da via e da drenagem | *About Drainage Networks* | Quantitativos com a origem de cada número | existe | — |
| Simulação de tráfego | disponível nas vias por componentes | *About automatic upgrade…* | Simulador de Tráfego (HCM, microssimulação, semáforos) | existe | — |
| Otimização do perfil e do corredor | otimização local com custo estimado, que pode ser interrompida | *Embedded Corridor & Profile Optimization* | Perfil da Via em modos (sem otimização por custo) | falta | fora (sem modelo de custo do usuário) |

## 6. O que entra no plano

As lacunas acima marcadas com etapa estão no [PLANO_DE_ACAO_2](PLANO_DE_ACAO_2.md). Ficam fora, com o motivo:
- veículo de projeto, visibilidade e otimização por custo: o plugin não traz método nem custo sem fonte do usuário (K8);
- simulação de cheias: depende de software de terceiros;
- ponto a ponto, sol e céu, imagens: o Revit já faz.
