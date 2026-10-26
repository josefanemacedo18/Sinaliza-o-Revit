# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-26 (Etapa 1 entregue; Etapas 2 e 3 em andamento)

- **Três prompts, um plano** (D-022): Prompt Master, Prompt Complementar Mestre de Motores e Prompt de Interface
  executados por `docs/PLANO_DE_ACAO.md` (etapas 0–13, cada pedido repetido numa etapa só). Entregas em
  `Releases/EtapaNN` (`tools/release.sh E<n>`). Ao fim de cada etapa o usuário é avisado.
- **Etapa 0** ✅ (plano e AUDIT §8–§9). **Etapa 1** ✅: 2.8.0 em `Releases/Etapa01` (commit `43ab058`, SHA-256 no
  `RELEASE.md`, suíte 2 966/2 966). Ela trouxe:
  - pisos sobrepostos classificados (`OverlapClassifier`) no núcleo e no Autoteste;
  - pisos × terreno (`FloorTerrainCheck`);
  - Autoteste comparando com o Toposolid projetado (antes, com o terreno natural), com as "Verificações do modelo"
    sempre no Completo.
  `Instalar/Revit2027` tem a mesma DLL. As anteriores continuam em `Releases/Fase03` a `Fase08`.
- **Etapa 2 (atual, `a51520c`, testes ainda não executados):**
  - `ModelValidator.Regras` (módulo e ação);
  - `Severidade.NaoVerificavel`;
  - `ValidationHistory` (novo, persistente, resolvido) e o relatório novo;
  - `Tolerancias`;
  - Validar Modelo com histórico.
- **Etapa 3 (iniciada, `01ed60c`):** `TerrainDiagnostics` e o limite de declividade do terreno nas Configurações. Falta o
  V14 no Validar Modelo, com o Toposolid.
- **Revit:** nada desde o 4º Autoteste completo rodou no Revit (K3). Relatórios em `docs/evidencias/`.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.

## Próxima tarefa

1. Rodar `Etapa02DiagnosticoTests` e a suíte, entregar `Releases/Etapa02` (2.9.0) e avisar o usuário.
2. Etapa 3: V14 no Validar Modelo (Toposolid projetado; eixos das vias como consultas), testes, entrega 2.10.0.
3. Seguir o plano (Etapas 4–13).
4. No Revit (quando houver): 5º Autoteste completo com a DLL mais recente, numa cópia, terminando com **Desfazer tudo**;
   enviar o relatório `autoteste-….txt` **e** o `autoteste-…-dados.jsonl` (`%AppData%\SinalizaBIM`).

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
