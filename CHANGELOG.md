# CHANGELOG

Formato: data, etapa, mudanças, testes. O histórico anterior a 2026-10-19 está no `git log`: 118 commits, rodadas A a LX,
cada uma com seus testes.

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
