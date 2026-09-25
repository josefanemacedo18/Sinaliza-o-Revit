# Manual de uso

Todos os comandos ficam na guia **Sinalização Viária** da faixa de opções. As janelas mostram uma
**pré-visualização ao vivo** (com área pintada e extensão) gerada pelo mesmo motor que cria os
elementos no Revit, e avisos quando uma dimensão sai do intervalo de referência do MBST.

Convenções usadas em todo o plugin:

* Unidades em **metros**; aceita vírgula ou ponto decimal.
* **Deslocamento lateral positivo = à esquerda** do sentido em que o caminho foi desenhado.
* **Circulação pela direita** (Brasil) para posicionar linhas de retenção.
* O **sentido do tráfego** de setas e legendas é indicado pelo 2º clique.

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

**Desenhar Eixo** cria apenas o eixo por pontos, para uso posterior.

---

## 2. Sinalizar Via – seção transversal completa

1. Escolha um **modelo pronto** (via local, coletora, avenida com canteiro, corredor de ônibus,
   faixa preferencial, ciclofaixa segregada, canteiros laterais, rodovias, mão única) e clique
   *Aplicar* – ou monte a seção do zero.
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

## 2.1 Bloqueios físicos

Comando **Bloqueios Físicos** (painel *Segregação Física*): escolha o dispositivo, ajuste
dimensões, espaçamento entre centros (0 = contínuo), deslocamento e recuos, e selecione/desenhe o
caminho. Os dispositivos são modelados em 3D com altura real (a barreira New Jersey em camadas que
reproduzem o perfil; a defensa com postes e lâmina) e aparecem nos quantitativos em unidades ou
metros.

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

* Linhas por **código × cor × material**: área pintada (m²), extensão (m), unidades, número de
  elementos, consumo estimado (L/m² ou kg/m²) e referência normativa.
* **Resumo por cor e material** com consumo e microesferas de vidro.
* **Exportar CSV**: separador `;` e vírgula decimal (abre direto no Excel em português).
* **Criar tabela no Revit**: tabela *SV - Quantitativos de sinalização* (Modelos genéricos) agrupada
  por grupo, código e cor, com totais. Marcas em 2D (regiões preenchidas) aparecem no quadro do
  plugin e no CSV.

---

## 11. Configurações e catálogo

* **Configurações**: representação e material padrão, velocidade padrão, fonte das legendas,
  espessura mínima, elevação adicional, projeção sobre superfícies, atualização automática, contorno
  das regiões 2D e caminho do catálogo do usuário.
* **Catálogo**: abre o JSON para edição, recarrega ou restaura o padrão. Formato em
  [CATALOGO.md](CATALOGO.md).

Arquivos do usuário ficam em `%AppData%\SinalizacaoViaria\` (configurações, catálogo, parâmetros
compartilhados e `log.txt`).
