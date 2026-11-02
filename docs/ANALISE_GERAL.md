# Análise geral do SinalizaBIM – rigidez, riscos e bugs (2026-11-02)

Documento para debate. Pausa da Etapa 22 (drenagem completa). Nada aqui foi visto no Revit: o próprio
KNOWN_ISSUES registra que desde o 4º Autoteste completo nada rodou no Revit (K3), e as telas e o updater das
etapas 11 a 21 seguem como NÃO VALIDADOS (K17, K24–K35).

## 1. Estado do repositório

- Commits da Etapa 21 (2.26.0) e 22a/b (262f6e1) entregues ou no histórico; suíte 3 232/3 232 na 21.
- **WIP não commitado da Etapa 22c (bueiros):** `Culverts.cs`, `CulvertDefinitions.cs`, `CulvertGenerator.cs` e os
  ajustes em `MarkingBuilder`, `ModelValidator`, `DrainageModel`, `QuantityCalculator`, `DrainageMemorial`. O núcleo
  compila com 0 avisos; **os testes da 22c ainda não foram escritos nem rodados**. Não está commitado de propósito.

## 2. Rigidez: valores fixos no código que o usuário não altera

Constantes de geração de interseções (`IntersectionGenerator.cs`, 3 255 linhas, um único arquivo):

| Constante | Valor | Onde aparece | Exposta nas Configurações? |
|---|---|---|---|
| `MinRampWidth` | 1,50 m | largura mínima da rampa | não |
| `MaxRampSlope` | 8,33 % | inclinação máxima da rampa | não |
| `MaxFlareSlope` | 10 % | inclinação do flare | não |
| `TotalSideSlope` | 5 % | caimento lateral total | não |
| `MaxRampShift` | 2,0 m | deslocamento máximo da rampa | não |
| `RampCurveGap` | 0,10 m | folga da curva da rampa | não |
| `MaxNoseRadius` | 1,50 m | raio máximo do nariz da ilha | não |
| `NodeMergeDistance` | 3,0 m | fusão de nós próximos (5 usos) | não |

Essas constantes são **limites duros**: o usuário não troca nenhuma delas, mesmo quando o projeto ou a
prefeitura pede outro valor. A janela da interseção tem ~145 controles, mas nenhum deles vai até essas constantes.
A regra "padrão do plugin" vale aqui, mas sem a opção de voltar ao valor do usuário.

Outras rigidezes que a análise encontra no modelo:

- `IntersectionDefinition` tem cantos, faixas, travessias, ilhas e controle, mas **a geometria de cada ramo** (a
  curva de cada esquina, a ilha, o ângulo mínimo) não é um dado da marca: sai do gerador. Para mudar uma esquina,
  é preciso mudar o algoritmo, não o projeto.
- Rotatória: `RoundaboutDefinition` tem tipo e raio, mas o layout (by-pass, lóbulos) não é editável por ramo.
- Chanfro de esquina (Etapa 16): 1,50 m é o padrão das quadras novas; a regra dos catetos não tem alternativa
  "medida pela face" no caso de quadras já gravadas (há opção global, mas não por esquina em todos os caminhos).
- Pisos de rampa (Etapa 17): `RampFloors` aceita a forma, mas o fundo é sempre plano a 5 cm (fixo).
- Limites de geometria (K8): raio, tangente, rampa e K só são conferidos contra o que o usuário informa; sem informar,
  nada é conferido. Não há valor padrão de norma – correto pela regra do projeto, mas é uma fonte de "tudo parece ok".

## 3. Pontos de risco de bugs (com evidência no próprio repositório)

1. **Geometria de interseção concentrada num arquivo de 3 255 linhas** (`IntersectionGenerator.cs`). Cada correção
   (K11, K12, K16, Etapa 15, 16) mexeu nele. Risco de regressão lateral: uma mudança numa esquina altera a outra.
2. **Tolerâncias por "1 cm" e por "2 cm"** (K12, K16): o plugin conta como erro qualquer ponto a milímetros do alvo.
   O efeito real no Revit (Toposolid, junção de sólidos) não foi medido – a fonte do problema está na precisão
   do Revit, não no núcleo.
3. **Sobreposição de pisos** (K11): 323 pares no 4º Autoteste; tratados no núcleo, mas o Revit só confirma num novo
   Autoteste.
4. **Atualização depois de editar** (K17): mudar eixo, greide ou seção reconstrói conexões, nós e quantitativos. As
   regras foram testadas no núcleo; o `MarkingUpdater` e os comandos de edição **não rodaram no Revit**. É o ponto
   onde um bug aparece para o usuário sem aviso.
5. **Terraplenagem como caixa-preta de resultado**: volumes vêm de malha (K7); o quantitativo mostra o número sem a
   incerteza. Em projeto real, a diferença entre malha e seção é grande em terreno irregular.
6. **Excesso de fallback silencioso**: vários cálculos devolvem `null`/zero quando faltam dados (ex.: `Ler do Modelo`,
   V13 sem equação de chuva). O relatório diz "não verificado", mas a tela da via pode parecer completa.
7. **Terreno natural guardado** (Etapa 20 e 21): o terreno de referência é "congelado" na definição; editar a
   topografia depois não atualiza as obras sem comando explícito. Uma terraplenagem refeita pode usar o terreno
   antigo se o usuário não repetir a ação.

## 4. O que não consigo afirmar daqui

- Quais bugs você viu na prática: **preciso da lista** (projeto, interseção, passo para reproduzir, o que esperava).
  Sem ela, qualquer correção é chute.
- Se os bugs de interseção são de geração (núcleo, reproduzível aqui) ou de sincronização com o Revit (só no Revit).
  Essa divisão decide se a correção é minha ou se precisa de um Autoteste seu.

## 5. Proposta para debate

Três caminhos, que não se excluem:

- **A. Estabilizar antes de expandir:** congelar novas etapas; reproduzir cada bug informado como teste de regressão
  no núcleo; só depois seguir o plano (22 em diante).
- **B. Liberar a rigidez:** tornar as constantes da tabela da seção 2 configuráveis por projeto, com o valor do
  plugin como padrão e a fonte escrita pelo usuário (sem inventar norma). Mudança de comportamento padrão: nenhuma.
- **C. Dividir `IntersectionGenerator.cs`** por responsabilidade (esquinas, rampas, ilhas, rotatória, travessias).
  Refatoração por vaidade não entra sem um bug que a justifique – então só se o caminho A revelar o problema.

Decisões que dependem de você:

1. Qual é o prioritário: estabilidade (A), flexibilidade (B) ou as duas?
2. Você tem a lista de bugs? Se sim, de quais projetos (um arquivo do Revit ou um relatório do Autoteste)?
3. A Etapa 22 (drenagem completa) continua na ordem depois disso, ou sobe a prioridade de alguma das duas?
