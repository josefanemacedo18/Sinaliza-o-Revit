# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-19

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Último ciclo concluído:** EV1–EV4.
  - Auditoria.
  - Documentos de controle.
  - Proteção de dados de versão mais nova e base de migrações.
  - Log com rotação; capturas silenciosas que afetavam resultado passam a ser registradas.
  - `ModelGraph`, `ModelValidator`, `PolygonChecks`.
  - Comando **Validar Modelo**.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros (Linux, `-p:EnableWindowsTargeting=true`).
- **Testes:** linha de base 2 704/2 704; depois de EV3/EV4, **2 730/2 730** (+26 de `EVModelIntegrityTests`).
- **Não verificado:** execução dentro do Revit 2027 (sem Revit no ambiente). O comando novo usa só APIs já usadas no
  projeto (`TaskDialog`, `FilteredElementCollector`, `ExtensibleStorageFilter`, `Selection.SetElementIds`) e
  `TransactionMode.ReadOnly`.

## Próxima tarefa

ROADMAP **F4** (fase IV): testes de borda das primitivas geométricas.
- `Polyline2`: pontos coincidentes, polilinha de 1 e 2 pontos, estaqueamento além das pontas, offset em volta de 180°.
- `Tracado.Build`: PIs coincidentes, PIs colineares, deflexão próxima de 180°, raio maior que o que cabe.
- Critério: nenhum NaN/∞ na saída e mensagens claras quando a entrada é inválida. Corrigir só o que falhar, com teste
  de regressão.

Depois, **F5**: relatório de elementos do traçado (TS/SC/CS/ST) e do greide (PIV/PCV/PTV/K).

## Como retomar

```bash
git checkout claude/dreamy-gates-a3nrta && git pull origin claude/dreamy-gates-a3nrta
dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true   # gera Instalar/Revit2027/SinalizaBIM.dll
dotnet test tests/SinalizacaoViaria.Core.Tests -c Release                            # ~15 min
```

Leia nesta ordem: AUDIT.md (estado e evidências) → KNOWN_ISSUES.md → ROADMAP.md → DECISIONS.md.

## Regras de trabalho em vigor

- Commits em nome de Josefane Macedo, com push para o ramo acima. Não abrir PR sem pedido.
- Antes de mudar o formato de uma definição: `CurrentVersion` + passo em `DefinitionVersioning.Passos` + teste.
- Nada de norma inventada: valor normativo só com a fonte citada no código ou no catálogo.
- Nunca alterar modelos reais do usuário. O Autoteste roda numa área de teste e desfaz tudo.
