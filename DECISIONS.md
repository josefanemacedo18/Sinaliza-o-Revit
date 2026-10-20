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
