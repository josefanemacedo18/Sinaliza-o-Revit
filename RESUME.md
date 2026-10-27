# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-27 (plano unificado concluído – Etapas 0–13; plano 2 – Etapas 14–17 entregues)

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
  - Etapa 6 ✅: 2.12.0 em `Releases/Etapa06` (`ab92d46`, 3 011/3 011) – drenagem lida do modelo (Ler do Modelo, D-025),
    pontos baixos e talvegues no V13, recobrimento sob a superfície acabada, perfil da rede.
  - Etapa 7 ✅: 2.13.0 em `Releases/Etapa07` (`e3a27c4`, 3 016/3 016) – indicadores do parcelamento, fonte dos mínimos,
    V16 e Trocar Elementos (auditoria dos elementos urbanos em AUDIT §9.2a).
  - Etapa 8 ✅: 2.14.0 em `Releases/Etapa08` (`072b683`, 3 022/3 022) – estado dos resultados (D-026), leitura com
    origem nas captações, atualização seletiva, impacto da edição, V17 (K22).
  - Etapa 9 ✅: 2.15.0 em `Releases/Etapa09` (`d074465`, 3 036/3 036) – design system (tokens iguais no
    `Styles.xaml` e no `UiTokens`, componentes, foco visível, contraste conferido em teste, cores soltas das janelas nos
    tokens) e faixa de opções por tema (D-027): guia *SinalizaBIM* com os 11 painéis do Prompt de Interface §7 e guia
    *SinalizaBIM Sinalização*; os 98 botões, cada um uma vez, mais o **Diagnóstico do Terreno**; dicas curtas com
    descrição longa, ícone próprio por botão e botão habilitado só com projeto (`NoDocumentRequired`). Mapa antigo →
    novo no MANUAL. Faixa e janelas não vistas no Revit (K23).
  - Etapa 10 ✅: 2.16.0 em `Releases/Etapa10` (`1a7f2b5`, 3 048/3 048) – Central do Projeto (indicadores reais com a
    situação de cada valor, nunca zero para o desconhecido; situação do projeto sem "validado"; módulos; atalhos pelo
    `CommandBase.Invoke`) e painel contextual (`DockablePane` que acompanha a seleção, só leitura; propriedades por tipo,
    estado e operações; sem edição em lote) – D-028, K24.
  - Etapa 11 ✅: 2.17.0 em `Releases/Etapa11` (`70d43af`, 3 075/3 075) – telas dos módulos (D-029): janela da via em
    nove abas com perfil, pavimentação, terraplenagem, drenagem, validação e resumo; gerenciador de estruturas de
    pavimento (tabela, esquema, quantitativo com a área medida, modelos do usuário); rede de drenagem por parte
    (geometria, conectividade, parâmetros, cálculo hidráulico, dimensionamento, vias e terreno), com perfil e seleção no
    modelo; terreno (origem, natural × projetada, superfície de cada cálculo, módulos a atualizar); seções dos nós com
    verificação geométrica × operacional; resultados do parcelamento; estaca da via nos formulários (K20 resolvido).
    Correções: a via refeita perdia a estrutura e o estaqueamento; o nó fora do registro ficava sem estado na
    terraplenagem. Telas não vistas no Revit (K25).
  - Etapa 12 ✅: 2.18.0 em `Releases/Etapa12` (`9102a7b`, 3 123/3 123) – D-030: **Central de Qualidade** no Validar Modelo
    (achados com o estado diante da anterior, filtros, ordem, seleção no modelo, CSV, por categoria, resolvidos, estado dos
    resultados; o último Autoteste numa aba própria, lido do TXT e conferido com os relatórios reais); **quantitativos com a
    origem** e o método de cada linha, aba Origem dos valores (quando, versão, configuração, validade da terraplenagem), Por
    via e Quadras e lotes; **gerenciador de configurações** (usuário, projeto, elemento; validação antes de aplicar e o que
    muda; perfis exemplo / usuário / referência citada; perfis de limites); tamanho das telas grandes lembrado. Correções:
    consumo de tinta com a unidade da taxa e litros somados com quilos; configurações salvas pela metade com um campo
    inválido. Telas não vistas no Revit (K26).
  - Etapa 13 ✅: 2.19.0 em `Releases/Etapa13` (`fdb39c2`, 3 136/3 136) – D-031: medição do núcleo (`tools/Desempenho`,
    4/16/64 vias) e **terraplenagem linear** com o mesmo resultado (`RingIndex`: 64 vias 12–15 s → 3–4 s, mesmo SHA-256);
    K14 e K15 medidos e não mudados (mexem no que o Revit cria ou lê); **progresso e cancelamento** em Atualizar Todas e
    Quantitativos (cancelar só antes de gravar – a transação é desfeita); **relatório do tráfego reprodutível** (F11:
    seção 9 com sementes, passo, aquecimento e assinatura); **Fluxo integrado** no Autoteste (via → interseção →
    terraplenagem → drenagem → quantitativos → validação); **relatório técnico final** em `docs/RELATORIO_TECNICO.md`.
    Não executada no Revit (K27).
  - `Instalar/Revit2027` tem a DLL 2.22.0 (Etapa 17). As anteriores continuam em `Releases/`.
- **Plano unificado concluído** (14 etapas, 2.8.0 a 2.19.0). Pendências abertas: F12 (cenários de projeto comparados por
  quantitativos), K14/K15 no Revit e tudo o que depende de rodar no Revit (K3, K17, K19, K23–K27).
- **Revit:** nada desde o 4º Autoteste completo rodou no Revit (K3). Relatórios em `docs/evidencias/`.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros (compilar sempre também o projeto de testes antes do
  commit).

## Plano 2 – "InfraWorks dentro do Revit" (desde 2026-10-27)

- Pedido do usuário em `docs/PROMPT_INFRAWORKS.md` (19 itens); plano em `docs/PLANO_DE_ACAO_2.md` (Etapas 14–26, D-032);
  referência do InfraWorks, com a fonte de cada ferramenta, em `docs/INFRAWORKS_REFERENCIA.md`.
- Etapa 14 ✅ (só documentos, sem DLL): estudo do InfraWorks pela ajuda oficial, lacunas por matéria, plano.
- Etapa 15 ✅: 2.20.0 em `Releases/Etapa15` (`a62975c`, 3 145/3 145) – D-033: trecho mínimo para travessia nas duas pontas
  da quadra (item 18, `EspacamentoTravessias`: o nó mais importante fica com a travessia; 0 = em todos os ramos) e
  plataforma da terraplenagem 5 cm abaixo do fundo de cada rampa, da via e dos nós (item 13, `InfraRoads.RampPads`); o
  Autoteste confere rampas e nós contra o Toposolid. NÃO VALIDADO NO REVIT (K28).
- Etapa 16 ✅: 2.21.0 em `Releases/Etapa16` (`0ef734a`, 3 163/3 163) – D-034: chanfro do lote pelos catetos, 1,50 m em
  cada lado nas quadras novas (padrão do loteamento, `Reconhecimento.ComChanfroPadrao`; as gravadas abrem iguais) – item 19;
  faixa tátil da esquina paralela ao alinhamento predial, seguindo o chanfro, alerta nas quinas, corte da quina com 0,60 m
  lisos da pista (`IntersectionGenerator.AlignmentCorner`; opção de voltar à curva do meio-fio) e faixa junto ao acesso
  (opção da via) – item 20; esquina da extensão de calçada em chanfro (`ShiftCorner`) e rampas no eixo das travessias
  conferidas – item 12. NÃO VALIDADO NO REVIT (K30); achado K29 (rampa além da faixa direcional não liga à rede).
- Etapa 17 ✅: 2.22.0 em `Releases/Etapa17` (`3dc933a`, 3 183/3 183) – D-035: rampas como Pisos de camada variável (um
  por corpo de concreto, fundo plano 5 cm abaixo, topo pela edição de forma – `RampFloors`, `MarkingService.CreateRampFloor`)
  e recorte da rampa contendo as abas inteiras – item 2; materiais com o ativo de aparência da biblioteca do Revit escolhido
  pelo título, cor de Cores consistentes intacta (`MaterialAppearance`, `StyleService.Appearance`) – item 1. Duas opções
  novas nas Configurações. NÃO VALIDADO NO REVIT (K31).
- **Próxima: Etapa 18** – estacas, greide e seção (itens 4 e 21): visualizador de seção por estaca (setas, estaca digitada,
  exagero vertical, corte/aterro com áreas), PIVs acrescentados/movidos/removidos com o perfil à vista e corte e aterro
  recalculados na hora, trecho de estacas nos volumes, gráficos (perfil terreno × greide, corte e aterro por estaca, áreas e
  diagrama de massas) – PLANO_DE_ACAO_2 §3. No núcleo: volumes por média das áreas iguais aos atuais e diagrama de massas
  conferido à mão.

## Próxima tarefa (antes do plano 2)

1. No Revit (quando houver): 5º Autoteste completo com a DLL 2.19.0, numa cópia, com **Fluxo integrado**, terminando com
   **Desfazer tudo**; enviar o relatório `autoteste-….txt` **e** o `autoteste-…-dados.jsonl` (`%AppData%\SinalizaBIM`);
   rodar também o Validar Modelo duas vezes (a segunda mostra a comparação com a primeira) e cancelar um Atualizar Todas.
   Os defeitos do relatório são reproduzidos no núcleo pelo arquivo de dados.
2. Sem o Revit: as recomendações do `docs/RELATORIO_TECNICO.md` §15 que não dependem dele – progresso na Terraplenagem e
   no Validar Modelo (mesmo `IProgressoOperacao`), F12 (cenários comparados por quantitativos) e K18 quando a mudança tocar
   o código. K14 e K15 só com um Autoteste para medir antes e depois (D-031).

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

Leia nesta ordem: docs/RELATORIO_TECNICO.md (estado final do plano) → docs/PLANO_DE_ACAO.md (plano e situação) → docs/PROMPT_MASTER.md (escopo e regras) → AUDIT.md (estado e evidências; §8 pelo Prompt Master) → KNOWN_ISSUES.md → ROADMAP.md → DECISIONS.md.

## Regras de trabalho em vigor

- Commits em nome de Josefane Macedo, com push para o ramo acima. Não abrir PR sem pedido.
- Antes de mudar o formato de uma definição: `CurrentVersion` + passo em `DefinitionVersioning.Passos` + teste.
- Nada de norma inventada: valor normativo só com a fonte citada no código ou no catálogo.
- Nunca alterar modelos reais do usuário. O Autoteste roda numa área de teste e desfaz tudo.
