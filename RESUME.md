# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-19

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Último ciclo concluído:** EV6, volumes pelo método das seções (Core, sem interface no Revit). Antes: EV5 e EV1–EV4.
  - EV5: casos degenerados das primitivas; elementos do traçado e do greide (CSV no Perfil da Via); V09–V11 no validador;
    procedimento de versão.
  - EV1–EV4:
  - Auditoria.
  - Documentos de controle.
  - Proteção de dados de versão mais nova e base de migrações.
  - Log com rotação; capturas silenciosas que afetavam resultado passam a ser registradas.
  - `ModelGraph`, `ModelValidator`, `PolygonChecks`.
  - Comando **Validar Modelo**.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros (Linux, `-p:EnableWindowsTargeting=true`).
- **Testes:**
  - linha de base: 2 704/2 704;
  - depois de EV3/EV4: 2 730/2 730;
  - depois de EV5: 2 754/2 754;
  - depois de EV6: **2 765/2 765** (suíte completa, 14 min 28 s).
- **CI (Windows):** verde no commit de EV1–EV4 (run 100). Os seguintes foram enviados; confira o resultado em Actions.
- **Não verificado:** execução dentro do Revit 2027 (sem Revit no ambiente). O comando novo usa só APIs já usadas no
  projeto (`TaskDialog`, `FilteredElementCollector`, `ExtensibleStorageFilter`, `Selection.SetElementIds`) e
  `TransactionMode.ReadOnly`.

## Próxima tarefa

ROADMAP **F5b**: elementos do eixo das vias comuns.
- O eixo vem de linhas do Revit (retas e arcos, ou cantos arredondados por `PathReference`).
- Gerar a mesma tabela (tangentes, arcos com R, Δ, estacas) a partir do caminho resolvido, com testes de eixo conhecido.
- Depois: **F7b** (tabela por estaca no quantitativo de terraplenagem do Revit) e **F3** (catálogo por documento, que resolve K1).

Decisão pendente do autor: subir a versão do plugin (`Directory.Build.props`, hoje 2.0.0) para marcar esta etapa
(sugestão: 2.1.0). Não foi feito sem pedido.

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
