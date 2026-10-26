# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-26 (plano unificado dos três prompts; Etapa 0 concluída, Etapa 1 em andamento)

- **Três prompts, um plano** (D-022): Prompt Master, Prompt Complementar Mestre de Motores e Prompt de Interface
  executados por `docs/PLANO_DE_ACAO.md` (etapas 0–13, cada pedido repetido numa etapa só). Entregas em
  `Releases/EtapaNN` (`tools/release.sh E<n>`), a partir da 2.8.0 na Etapa 1. Ao fim de cada etapa o usuário é avisado.
- **Etapa 1 (atual):** classificação dos pisos sobrepostos no núcleo feita (`OverlapClassifier`, `9701ebb`); falta levá-la
  ao Autoteste, corrigir o resíduo INTERSECAO MEIO-FIO × CALCADA (62 cm³) que ela achou, conferir pisos × terreno e ligar
  as "Verificações do modelo".
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

Seguir o plano (`docs/PLANO_DE_ACAO.md`): terminar a Etapa 1, entregar `Releases/Etapa01` (2.8.0), avisar o usuário e
passar à Etapa 2. No Revit (quando houver): 5º Autoteste completo com a DLL mais recente, numa cópia, terminando com
**Desfazer tudo**; enviar o relatório `autoteste-….txt` **e** o `autoteste-…-dados.jsonl` (`%AppData%\SinalizaBIM`).

## Como retomar

O host de download do .NET (`builds.dotnet.microsoft.com`) é recusado pela política de rede da sessão. Se o contêiner vier
sem SDK, instale-o pelo Nix (o cache do Nix e o NuGet funcionam):

```bash
nix-env -f https://channels.nixos.org/nixos-unstable/nixexprs.tar.xz -iA dotnet-sdk_10   # SDK 10.0.401
export PATH=/root/.nix-profile/bin:$PATH
git checkout claude/dreamy-gates-a3nrta && git pull origin claude/dreamy-gates-a3nrta
dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true   # gera Instalar/Revit2027/SinalizaBIM.dll
dotnet test tests/SinalizacaoViaria.Core.Tests -c Release                            # ~15–26 min
```

O contêiner pode ser reciclado sem aviso (em 2026-10-26 perdeu-se trabalho não commitado): commit e push a cada passo verde.

Leia nesta ordem: docs/PLANO_DE_ACAO.md (plano e situação) → docs/PROMPT_MASTER.md (escopo e regras) → AUDIT.md (estado e evidências; §8 pelo Prompt Master) → KNOWN_ISSUES.md → ROADMAP.md → DECISIONS.md.

## Regras de trabalho em vigor

- Commits em nome de Josefane Macedo, com push para o ramo acima. Não abrir PR sem pedido.
- Antes de mudar o formato de uma definição: `CurrentVersion` + passo em `DefinitionVersioning.Passos` + teste.
- Nada de norma inventada: valor normativo só com a fonte citada no código ou no catálogo.
- Nunca alterar modelos reais do usuário. O Autoteste roda numa área de teste e desfaz tudo.
