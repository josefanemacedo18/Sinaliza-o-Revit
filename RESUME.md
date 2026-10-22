# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-22 (Fase VII concluída; Fase VIII em andamento)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.6.0 (`Directory.Build.props`), entregue em `Releases/Fase07` (commit `b87e668`, SHA-256 no
  `RELEASE.md`). `Instalar/Revit2027` tem a mesma DLL. As anteriores continuam em `Releases/Fase03` a `Fase06`
  (2.2.0–2.5.0). Nenhuma foi executada no Revit 2027.
- **Fases da missão:** quadro no ROADMAP. ✅ I–VII, IX, XI; 🔄 VIII (atual); ⬜ X, XII, XIII, XIV, XV.
- **Entregas por fase (G0):** `tools/release.sh <fase> "<suíte>"` compila de um commit limpo, confere `Versão+commit` na
  DLL e grava `Releases/FaseNN` com o manifesto; índice em `Releases/README.md`. Cada fase sobe o número do meio da versão.
- **Fase VII:** K16 (by-pass da rotatória sem frestas nem ilhas fora do meio-fio) e F7b (volumes por estaca por via, só na
  superfície de projeto da via, comparados com a malha; aba no quantitativo, CSV e planilha).
- **Fase VIII (em andamento):** rede de drenagem. A parte geométrica (trecho entre caixas, medição, V13, Configurações,
  Trecho de Rede, Relatório da Rede) foi feita numa cópia de trabalho e ainda não está no ramo. O usuário enviou o Manual
  de Drenagem Urbana (SUDERHSA, 2002) e um memorial descritivo modelo (Miguelópolis/SP, 2019): a fase passa a incluir o
  dimensionamento pelo Método Racional (curva IDF configurável, Manning a seção plena, sarjetas, bocas de lobo) e o
  memorial de drenagem em .docx.
- **Revit:** dois Autotestes rápidos e quatro **completos**, mais um Validar Modelo. Relatórios em `docs/evidencias/`. As
  mudanças do FC e das fases III a VII ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 921/2 921 (Fase VII).

## Próxima tarefa

1. **Fase VIII:** rede geométrica + dimensionamento hidráulico + memorial de drenagem; documentos, versão 2.7.0, suíte
   completa, `tools/release.sh 8`.
2. **Fases seguintes, na ordem do ROADMAP:** X (F10), XII (F12), XIII, XIV, XV.
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
