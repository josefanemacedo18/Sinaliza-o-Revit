# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-23 (Prompt Master adotado; Fase X em andamento)

- **Prompt Master** (`docs/PROMPT_MASTER.md`, enviado em 2026-10-23): rege o desenvolvimento (D-021). As 15 fases ficam
  como mapa de rastreio; a ordem vem dos riscos (AUDIT §8.3). Auditoria pelo novo escopo: AUDIT §8 (mapa de módulos,
  matriz em cinco situações, riscos). Lacunas que ele revelou: diagnóstico do terreno, validação sem módulo, ação e
  comparação, terraplenagem desatualizada sem aviso, estrutura do pavimento só no desenho do corte, referências mútuas
  entre pastas do Core (K18).
- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.7.0 (`Directory.Build.props`), entregue em `Releases/Fase08` (commit `9ed7e12`, SHA-256 no
  `RELEASE.md`). `Instalar/Revit2027` tem a mesma DLL. As anteriores continuam em `Releases/Fase03` a `Fase07`
  (2.2.0–2.6.0). Nenhuma foi executada no Revit 2027.
- **Fases da missão:** quadro no ROADMAP. ✅ I–IX, XI; 🔄 X (atual), XII, XIV, XV; ⬜ XIII.
- **Entregas por fase (G0):** `tools/release.sh <fase> "<suíte>"` compila de um commit limpo, confere `Versão+commit` na
  DLL e grava `Releases/FaseNN` com o manifesto; índice em `Releases/README.md`. Cada fase sobe o número do meio da versão.
- **Fase VIII:** rede de drenagem (trechos entre caixas) e dimensionamento pelo método racional com as fontes enviadas pelo
  usuário (Manual SUDERHSA 2002 e memorial modelo de Miguelópolis/SP 2019 – D-019; Projeto de Drenagem por documento –
  D-020); Relatório da Rede com a planilha de galerias e os diâmetros calculados; V13; Memorial de Drenagem (.docx). A
  planilha do memorial modelo e os exemplos 6.1–6.3 do manual são testes. Limites em K5.
- **Requisito transversal RT-1** (pedido do usuário em 2026-10-22, durante a Fase VIII): integração Toposolid × pisos das
  vias × terraplenagem × drenagem; plano e partes no ROADMAP. Já levantado (para a auditoria RT-1.1): o plugin guarda o
  terreno original, refaz o Toposolid pelo editor de forma (`TerrainService.Apply`, `ApplyPoints`) com as plataformas e
  taludes do núcleo (`Grading`, plataforma por camadas e borda no topo do acabamento), deixa o terreno 0,40 m sob o topo do
  pavimento, escava elementos enterrados (`ExcavateBy`) e os pisos seguem o greide por edição de forma em planos
  (`MarkingService.FlushShapeEdits`). As "Verificações do modelo" do Autoteste (pintura × pisos, pavimento contínuo,
  terreno × via – `AutoTestChecks.CheckRoad`) ficam desligadas por padrão e **nunca rodaram** nos Autotestes do usuário.
- **Revit:** dois Autotestes rápidos e quatro **completos**, mais um Validar Modelo. Relatórios em `docs/evidencias/`. As
  mudanças do FC e das fases III a VIII ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 949/2 949 (Fase VIII).

## Próxima tarefa

1. **Fase X (ROADMAP, Próximas etapas X1–X5):**
   1. `OverlapClassifier` (arquivo novo em `Core/Model`, ainda sem testes no início desta retomada) com testes e no
      Autoteste;
   2. pisos × terreno no núcleo e "Verificações do modelo" ligadas no Autoteste completo;
   3. diagnóstico do terreno;
   4. validação com módulo, evidência, ação, "não verificável" e comparação com a anterior;
   5. terraplenagem desatualizada.
2. **Depois:** P6 (estrutura do pavimento), F10, P7, F12, F11, P8, K14/K15 – na ordem do ROADMAP.
3. **No Revit (quando houver):** 5º Autoteste completo com a DLL mais recente, numa cópia, terminando com **Desfazer
   tudo**; enviar o relatório `autoteste-….txt` **e** o `autoteste-…-dados.jsonl` (`%AppData%\SinalizaBIM`).

## Como retomar

```bash
git checkout claude/dreamy-gates-a3nrta && git pull origin claude/dreamy-gates-a3nrta
dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true   # gera Instalar/Revit2027/SinalizaBIM.dll
dotnet test tests/SinalizacaoViaria.Core.Tests -c Release                            # ~15–22 min
```

Leia nesta ordem: docs/PROMPT_MASTER.md (escopo e regras) → AUDIT.md (estado e evidências; §8 pelo Prompt Master) → KNOWN_ISSUES.md → ROADMAP.md → DECISIONS.md.

## Regras de trabalho em vigor

- Commits em nome de Josefane Macedo, com push para o ramo acima. Não abrir PR sem pedido.
- Antes de mudar o formato de uma definição: `CurrentVersion` + passo em `DefinitionVersioning.Passos` + teste.
- Nada de norma inventada: valor normativo só com a fonte citada no código ou no catálogo.
- Nunca alterar modelos reais do usuário. O Autoteste roda numa área de teste e desfaz tudo.
