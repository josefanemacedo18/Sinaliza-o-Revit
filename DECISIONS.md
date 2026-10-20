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
- **Consequências:** não há o que migrar nem sincronizar. O grafo serve à validação e à ordem de regeneração. O fluxo de
  regeneração existente (`MarkingService.DependencyOrder`, `RenderDependents`) **não foi trocado** nesta etapa, porque
  funciona e é coberto pelo Autoteste. Trocar exige validação no Revit.

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
