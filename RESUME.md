# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-22 (Fase V concluída; Fase VI em andamento)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.4.0 (`Directory.Build.props`), entregue em `Releases/Fase05` (commit `dd8b1fc`, SHA-256 no
  `RELEASE.md`). `Instalar/Revit2027` tem a mesma DLL. As anteriores continuam em `Releases/Fase03` (2.2.0) e
  `Releases/Fase04` (2.3.0). Nenhuma foi executada no Revit 2027.
- **Fases da missão:** quadro no ROADMAP. ✅ I, II, III, IV, V, IX, XI; 🔄 VI (atual), VII, X, XII, XIV, XV; ⬜ VIII, XIII.
- **Entregas por fase (G0):** `tools/release.sh <fase> "<suíte>"` compila de um commit limpo, confere `Versão+commit` na
  DLL e grava `Releases/FaseNN` com o manifesto; índice em `Releases/README.md`. Cada fase sobe o número do meio da versão.
- **Fase V:** limites geométricos do traçado e do greide informados pelo usuário (K8): `GeometricCriteria` e
  `GeometricCheck` (Core), V12 no Validar Modelo, CSV `verificacao-*.csv` no Perfil da Via e campos em Configurações.
- **Fase VI (em andamento):** resumo do quantitativo por componente da seção (`ComponentQuantities`, `SectionComponent`,
  `MarkingPiece.Component` – não muda os pisos do Revit) e regras da regeneração no núcleo (`ModelGraph.ConnectionsToRefresh`,
  `IntersectionGenerator.FollowNode`), com `Fase06ComponentesTests` e `Fase06RegeneracaoTests`.
- **Revit:** dois Autotestes rápidos e quatro **completos**, mais um Validar Modelo. Relatórios em `docs/evidencias/`. As
  mudanças do FC e das fases III a V ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 894/2 894 (Fase V).

## Próxima tarefa

1. **Fase VI:** concluir (documentos, versão 2.5.0, suíte completa, `tools/release.sh 6`). A atualização no Revit depois de
   mudar eixo/greide/seção continua a conferir num Autoteste (o updater não roda aqui).
2. **Fases seguintes, na ordem do ROADMAP:** VII (F7b, K16), VIII (F8), X (F10), XII (F12), XIII, XIV, XV.
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
