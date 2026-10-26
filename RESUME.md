# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-26 (Etapas 0–5 entregues; Etapa 6 em andamento)

- **Três prompts, um plano** (D-022): Prompt Master, Prompt Complementar Mestre de Motores e Prompt de Interface
  executados por `docs/PLANO_DE_ACAO.md` (etapas 0–13). Entregas em `Releases/EtapaNN` (`tools/release.sh E<n>`). Ao fim
  de cada etapa o usuário é avisado.
- **Entregas:**
  - Etapa 0 ✅: plano e AUDIT §8–§9.
  - Etapa 1 ✅: 2.8.0 em `Releases/Etapa01` (`43ab058`, 2 966/2 966) – pisos sobrepostos classificados, pisos × terreno,
    Autoteste contra o Toposolid projetado.
  - Etapas 2 e 3 ✅, entrega conjunta: 2.9.0 em `Releases/Etapa03` (`23c92c8`, 2 979/2 979) – validação com módulo,
    ação, "não verificado" e histórico; tolerâncias num registro; diagnóstico do terreno e V14.
  - Etapa 4 ✅: 2.10.0 em `Releases/Etapa04` (`ee75f3f`, 2 986/2 986) – limites dos avisos com a origem dita
    (`LimitesDeConferencia`); estaqueamento da via com estaca inicial e equações (D-023, K20).
  - Etapa 5 ✅: 2.11.0 em `Releases/Etapa05` (`5dc91f7`, 3 000/3 000) – estrutura do pavimento na via usada pela
    terraplenagem, pelo corte e pelo quantitativo por camada; registro da terraplenagem e V15; "Só calcular" compara com
    a aplicada (D-024, K19, K21).
  - `Instalar/Revit2027` tem a DLL 2.11.0. As anteriores continuam em `Releases/`.
- **Etapa 6 (atual):** drenagem integrada – sarjeta e área de contribuição lidas da via (`DrenagemDoModelo`, comando Ler
  do Modelo), pontos baixos sem captação e talvegues sob aterro no V13, recobrimento sob a superfície acabada, perfil
  longitudinal da rede e lista de dispositivos no Relatório da Rede.
- **Revit:** nada desde o 4º Autoteste completo rodou no Revit (K3). Relatórios em `docs/evidencias/`.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros (compilar sempre também o projeto de testes antes do
  commit).

## Próxima tarefa

1. Etapa 6 (PLANO). Depois, as Etapas 7–13 na ordem do plano.
2. No Revit (quando houver): 5º Autoteste completo com a DLL mais recente, numa cópia, terminando com **Desfazer tudo**;
   enviar o relatório `autoteste-….txt` **e** o `autoteste-…-dados.jsonl` (`%AppData%\SinalizaBIM`); rodar também o
   Validar Modelo duas vezes (a segunda mostra a comparação com a primeira).

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
