# Estudo dos manuais de sinalização – o que foi aplicado no SinalizaBIM

Este documento resume o estudo dos manuais fornecidos e registra **como cada regra virou parâmetro ou automatismo**
no plugin. A intenção é que o projetista saiba de onde vem cada valor padrão – e que possa mudá-lo, pois **tudo
continua editável** nas janelas e no catálogo (`catalogo-padrao.json`).

Manuais estudados:

| Documento | Uso principal no plugin |
|---|---|
| **DER/SP – Manual de Sinalização Rodoviária, Vol. I – Projeto (2023)** | projetos-tipo de rotatória (15, 16, 16-A) e de cruzamento rodoferroviário (13); quadros de canalização (B-9 e B-10), LFO-5, LDP 0,40 m (0,50 × 0,50), tamanho de zebrados, R-24a na ilha, marcadores de alinhamento |
| **MBST Vol. I – Sinalização de Regulamentação** | R-1, R-2, R-19, R-24a e R-33: uso e posição nas rotatórias, no cruzamento ferroviário e nas interseções |
| **MBST Vol. II – Sinalização de Advertência** | distância de colocação da placa pela **desaceleração de 2 m/s²** (item 3.13.2), mensagem "A … m", A-12, A-39/A-40/A-41 |
| **MBST Vol. III – Sinalização de Indicação** | conferência das placas de indicação já existentes no catálogo (não houve automatismo novo nesta rodada) |
| **MBST Vol. IV – Sinalização Horizontal** | LRV (método de cálculo), MIR (rotatória), MCF, MAE, MTL/MAO/MAP, SIP, altura de legendas, largura das linhas |
| **MBST Vol. VI – Dispositivos Auxiliares** | espaçamento de tachas (Tabela 4.6), tachões (Tabela 4.7), balizadores (Tabela 4.1) e marcadores de alinhamento (Tabela 5.1) |
| **MBST Vol. IX – Cruzamentos Rodoferroviários** | conjunto completo da passagem em nível |
| **CET-SP Vol. 13 – Espaço Cicloviário** | padrões I e II de ciclofaixa (fundo total × linha vermelha de contraste), ciclovia com LBO + vermelha |
| **CET-SP – Manual de Medidas Moderadoras do Tráfego** | almofada (*speed cushion*), platôs e faixas elevadas |
| **CET-SP – Dispositivos Delimitadores; Vol. 10 (parte 9) e Vol. 11** | conferência dos tachões/segregadores/balizadores já existentes em *Bloqueios Físicos* |

## 1. Regras que viraram cálculo automático (`DesignRules`)

Todas estão em `src/SinalizacaoViaria.Core/Automation/DesignRules.cs` e são usadas pelas ferramentas como **valor
inicial**; o campo correspondente na janela pode ser alterado.

| Regra | Fonte | Como é usada |
|---|---|---|
| Distância de desaceleração `d = (V0² − V1²) / (2·a)`, a = 2,00 m/s² | MBST Vol. II, 3.13.2 | posição da A-12 (rotatória) e da A-39/A-40 (ferrovia); texto "A … m" (múltiplo de 10 m, mínimo 20 m) |
| LRV: `Ei = i·(V0·t − 0,5·a·t²·i)`, a = 1,47 m/s², t = 1 s; largura 0,20 / 0,30 / 0,40 m | MBST Vol. IV, 5.2 | *Linha Transversal → LRV* com velocidades inicial e final; LRV do cruzamento ferroviário |
| Transição de canalização `l = 0,5·V·d`; mínimos 30 m (urbana) / 60 m (rodovia) junto a obstáculos | MBST Vol. IV 6.2; DER/SP B.3 | ferramenta *Canalização* (MTL/MAO/MAP) |
| Transição de acostamento `ta` = 30 / 40 / 50 m | DER/SP Quadro B-9 | MAP |
| Barras do zebrado de canalização a 1,50 m (V < 80) ou 2,50 m, largura 0,50 m, 45°, LCA 0,20 m | DER/SP Quadro B-10; MBST Vol. IV | canalização, ilhas separadoras pintadas das rotatórias |
| Tachas: 8 / 12 / 16 m (normal) e 6 / 9 / 12 m (especial), 2 / 4 m no trecho anterior | MBST Vol. VI, Tabela 4.6 | variantes TAC-A / TAC-B (pela velocidade) |
| Tachões: 4 m ao lado do fluxo; 1 m em fluxos divergentes; 0,25–0,50 m em minirrotatória | MBST Vol. VI, Tabela 4.7 | variantes TACHÃO; tachões da ilha pintada |
| Balizadores por raio da curva; marcadores de alinhamento por raio externo | MBST Vol. VI, Tabelas 4.1 e 5.1 | `DelineatorSpacing` / `AlignmentMarkerSpacing` (marcadores MA-ALIN na ilha da rotatória) |
| SIP (dê a preferência): 3,60 m (≤ 60 km/h) ou 6,00 m | MBST Vol. IV, 8.2.1 | SDP das rotatórias e interseções |
| Altura das legendas: 1,60 / 2,40 m (urbana), 2,40 / 4,00 m (rural) | MBST Vol. IV, 8.3 | PARE das rotatórias e do cruzamento ferroviário |
| Largura das linhas longitudinais: 0,10 m (< 80 km/h) ou 0,15 m | MBST Vol. IV | `LongitudinalLineWidth` |

## 2. Rotatórias (MBST Vol. IV – MIR; DER/SP projetos-tipo 15 e 16)

O que os manuais mostram e o que o plugin faz:

* **Minirrotatória com ilha pintada** (MBST): LCA contínua de 0,20 m em volta da ilha + tachões a cada 0,25–0,50 m;
  zebrado interno opcional. → preset *Mini* com `IslandType = Pintada`, `PaintedLineWidth`, `StudSpacing`,
  `PaintedIslandFill`.
* **Marcação do anel**: LBO externa entre as aberturas dos ramos, LMS entre faixas do anel, **setas curvas (IMC)** no
  anel após cada entrada. → `OuterEdgeLine`, `InnerEdgeLine`, `RingLaneLine`, `RingArrows` (símbolo IMC novo no
  catálogo, comprimento 4,5 / 6,0 m).
* **Aproximações**: LDP de 0,40 m com SDP; LFO-3 na aproximação de mão dupla; ilhas separadoras (gotas) físicas ou
  **pintadas** em amarelo (fluxos opostos) ou branco (mão única). → `ApproachDoubleLine`, `SplitterStyle` geral e por
  ramo, `ApproachSpeed`.
* **Placas** (DER/SP): R-2 + R-33 nas entradas quando o raio da ilha é pequeno (< 12 m), R-24a na ilha nas demais;
  A-12 com "A … m"; marcadores de alinhamento na ilha. → `DirectionSigns`, `AdvanceWarning`, `AlignmentMarkers`.
* **Integração mínima com as vias** (pedido do usuário, apoiado nos projetos-tipo, que mostram só o anel sobre o
  cruzamento): modos *Completa / Somente o anel / Somente a ilha* e chave *Recortar as vias ligadas*; recorte
  **físico** (zona da rotatória) separado do recorte de **pintura** (anel).

## 3. Cruzamento rodoferroviário (MBST Vol. IX / Vol. IV 5.8; DER/SP projeto-tipo 13)

* MCF: **duas linhas de retenção** de 0,30–0,60 m paralelas ao trilho, ≥ 3,0 m do trilho externo (DER/SP: 5,0 m sem
  cancela; 2,0 m antes da cancela) – variantes no catálogo (`MCF`) e ferramenta completa.
* **Retângulo de advertência** de 15 m entre 15 e 150 m da retenção com a **cruz de Santo André (SIF/CSA)** de 6,00 m
  por faixa; **LFO-3** na aproximação; **PARE** ≥ 1,60 m antes da retenção; LRV opcional.
* Placas **A-41 + R-1** no mesmo suporte a 3,60 m do eixo da ferrovia; **A-39** (sem barreira) / **A-40** (com
  barreira) antecipadas pela distância de desaceleração; R-19 em vias acima de 40 km/h.

## 4. Canalização em transição (MBST Vol. IV 6.2; DER/SP B.3)

* **MTL** – alteração de largura: triângulo de comprimento `l = 0,5·V·d`.
* **MAO** – obstáculo/ilha na pista: transição de entrada + obstáculo + transição de saída, afastamento lateral *a* de
  0,30–0,60 m; amarelo com área dos dois lados quando o obstáculo fica no eixo de via de mão dupla.
* **MAP** – acostamento: transição `ta` + trecho tangente.
* Todos com barras de 0,50 m a 45° e LCA de 0,20 m; cores conforme os fluxos separados.

## 5. Ciclovias (CET-SP Vol. 13)

* **Padrão I**: pintura de fundo vermelha em toda a faixa (já existia). **Padrão II**: linha branca de delimitação
  (0,25 m) acompanhada de **linha vermelha de contraste de 0,15 m** pelo lado interno – opção *Sem fundo: linha
  vermelha de contraste*. Na ciclovia segregada, LBO + linha vermelha.

## 6. Moderação de tráfego (CET-SP – Medidas Moderadoras)

* **Almofada** (*speed cushion*): 3,0–3,5 m de extensão, 6–7,5 cm de altura, **1,60–1,90 m de largura**, uma por
  faixa, para que ônibus e veículos de emergência passem com as rodas ao lado – novo tipo em *Quebra-mola e Lombadas*
  com largura e quantidade editáveis; pintura em barras nas rampas como nas ondulações.

## 7. Catálogo – códigos novos ou revisados

| Código | Origem | Observação |
|---|---|---|
| `LFO-5` | DER/SP B.2.1.5 | 3 segmentos longos antes da linha contínua (10×6 / 15×9 / 20×12 m; especial 8×4 / 12×6 / 16×8). Desenhe do início da transição até a linha contínua (alinhamento *Fim*). |
| `MCF` | MBST Vol. IV 5.8 | linha de retenção dupla do cruzamento ferroviário |
| `MAE` / `MAE-A` | MBST Vol. IV 5.7 | quadriculado ≥ 1,00 m para travessia de faixa exclusiva (branco no fluxo, amarelo contrafluxo) |
| `IMC` / `IMC-D` | MBST Vol. IV (MIR) | seta curva de circulação (anti-horária / horária) |
| `TAC-A`, `TAC-B` | MBST Vol. VI, Tab. 4.6 | 8 variantes (normal, especial e trecho anterior) com seleção pela velocidade |
| `TACHAO` | MBST Vol. VI, Tab. 4.7 | 4 m, 2 m, 1 m, 0,50 m e contíguos (minirrotatória) |
| `LDP` | DER/SP | variante 0,40 m (0,50 × 0,50 m) |
| `LRV` | MBST Vol. IV 5.2 | descrição do método; espaçamento calculado na ferramenta |
| `ZPA` / `ZPA-A` | MBST Vol. IV; DER/SP | descrição com as dimensões de referência dos dois manuais |

## 8. O que foi lido e ainda não virou ferramenta (próximas rodadas)

* MBST Vol. III: famílias de placas de indicação com diagramação (setas, distâncias, pictogramas) – o catálogo tem as
  placas, mas não o diagramador de mensagens.
* CET Medidas Moderadoras: chicanes, estreitamentos físicos e ilhas de refúgio como ferramenta própria (hoje se faz
  com *Canteiros* e *Área de Calçada*).
* MBST Vol. VI: balizadores em curva com espaçamento automático pelo raio (a regra já está em `DesignRules`;
  falta ligar à ferramenta *Bloqueios Físicos*).
* DER/SP: projetos-tipo de interseções em nível com terceira faixa e faixas de aceleração/desaceleração.
