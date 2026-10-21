# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-21 (Fase III concluída; Fase IV em andamento)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.2.0 (`Directory.Build.props`), entregue em `Releases/Fase03` (commit `87cf484`, SHA-256 no
  `RELEASE.md`). `Instalar/Revit2027` tem a mesma DLL. Não executada no Revit 2027.
- **Fases da missão:** quadro no ROADMAP. ✅ I, II, III, IX, XI; 🔄 IV (atual), V, VI, VII, X, XII, XIV, XV; ⬜ VIII, XIII.
- **Entregas por fase (G0):** `tools/release.sh <fase> "<suíte>"` compila de um commit limpo, confere `Versão+commit` na
  DLL e grava `Releases/FaseNN` com o manifesto; índice em `Releases/README.md`. Cada fase sobe o número do meio da versão.
- **Fase III:** ordem de regeneração pelo grafo (`ModelGraph.RegenerationSort`, D-004) e placas personalizadas só na lista
  do projeto que as tem (K1, `CustomSigns.ForProject`). `Fase03ArquiteturaTests` (7).
- **Fase IV em andamento (não commitada no momento deste registro):** centros exatos dos arcos da extensão de calçada
  (`SidewalkGenerator.EarArcCenters`) e do recorte que ela deixa nas marcas vizinhas (`ExclusionZone.ArcCenters`,
  `FootprintCutter`) – MER-0 × ORELHA; zebrado por contorno no Revit com os recortes da marca
  (`MarkingBuilder.BuildHatchArea`) – FTP-1 × ZPA. Testes em `Fase04GeometriaTests`.
- **Revit:** dois Autotestes rápidos e quatro **completos**, mais um Validar Modelo. Relatórios em `docs/evidencias/`. As
  mudanças do FC e das fases III/IV ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 876/2 876 (Fase III).

## Próxima tarefa

1. **Fase IV:** concluir K11 (MER-0 × ORELHA, FTP-1 × ZPA) e K13 (emenda em curva e rotatórias), suíte completa,
   versão 2.3.0 e `tools/release.sh 4`.
2. **Fases seguintes, na ordem do ROADMAP:** V (K8), VI, VII (F7b), VIII (F8), X (F10), XII (F12), XIII, XIV, XV.
3. **No Revit (quando houver):** 5º Autoteste completo com a DLL mais recente, numa cópia, terminando com **Desfazer
   tudo**; enviar o relatório `autoteste-….txt` **e** o `autoteste-…-dados.jsonl` (`%AppData%\SinalizaBIM`).

## Como retomar

```bash
git checkout claude/dreamy-gates-a3nrta && git pull origin claude/dreamy-gates-a3nrta
dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true   # gera Instalar/Revit2027/SinalizaBIM.dll
dotnet test tests/SinalizacaoViaria.Core.Tests -c Release                            # ~15–22 min
```

Leia nesta ordem: AUDIT.md (estado e evidências) → KNOWN_ISSUES.md → ROADMAP.md → DECISIONS.md.

## Regras de trabalho em vigor

- Commits em nome de Josefane Macedo, com push para o ramo acima. Não abrir PR sem pedido.
- Antes de mudar o formato de uma definição: `CurrentVersion` + passo em `DefinitionVersioning.Passos` + teste.
- Nada de norma inventada: valor normativo só com a fonte citada no código ou no catálogo.
- Nunca alterar modelos reais do usuário. O Autoteste roda numa área de teste e desfaz tudo.
