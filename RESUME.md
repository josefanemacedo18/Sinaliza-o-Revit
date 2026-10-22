# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-22 (Fase VI concluída; Fase VII em andamento)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.5.0 (`Directory.Build.props`), entregue em `Releases/Fase06` (commit `ec398bc`, SHA-256 no
  `RELEASE.md`). `Instalar/Revit2027` tem a mesma DLL. As anteriores continuam em `Releases/Fase03` a `Fase05`
  (2.2.0–2.4.0). Nenhuma foi executada no Revit 2027.
- **Fases da missão:** quadro no ROADMAP. ✅ I–VI, IX, XI; 🔄 VII (atual), X, XII, XIV, XV; ⬜ VIII, XIII.
- **Entregas por fase (G0):** `tools/release.sh <fase> "<suíte>"` compila de um commit limpo, confere `Versão+commit` na
  DLL e grava `Releases/FaseNN` com o manifesto; índice em `Releases/README.md`. Cada fase sobe o número do meio da versão.
- **Fase VI:** quantitativo por componente da seção (`ComponentQuantities`, `SectionComponent`, `MarkingPiece.Component`,
  D-017) e regras da regeneração no núcleo (`ModelGraph.ConnectionsToRefresh`, `IntersectionGenerator.FollowNode`). O
  updater no Revit depois de mudar eixo/greide/seção continua não executado (K17).
- **Fase VII (em andamento):**
  - K16 (rotatória com by-pass): corrigido no núcleo na cópia de trabalho – a ilha do by-pass fica dentro do lóbulo depois
    da abertura e o vão de 2 cm entre lóbulo e pista volta a ser pista depois da zona efetiva (`RoundaboutGenerator`);
    `Fase07InterseccoesTests` e o teste da Fase IV com o by-pass incluído.
  - F7b (tabela de volumes por estaca no quantitativo de terraplenagem do Revit): `SectionVolumes.Compute` com
    `GradingResult.DesignZ` e o terreno original do Toposolid, por via, ao lado da malha.
- **Revit:** dois Autotestes rápidos e quatro **completos**, mais um Validar Modelo. Relatórios em `docs/evidencias/`. As
  mudanças do FC e das fases III a VI ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 914/2 914 (Fase VI).

## Próxima tarefa

1. **Fase VII:** K16 e F7b (acima), documentos, versão 2.6.0, suíte completa, `tools/release.sh 7`.
2. **Fases seguintes, na ordem do ROADMAP:** VIII (F8), X (F10), XII (F12), XIII, XIV, XV.
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
