# Plano de ação unificado – Prompt Master, Motores e Interface

Plano único para os três documentos enviados pelo usuário. Cada pedido aparece uma vez só, mesmo quando vários documentos o
repetem.

- [Prompt Master](PROMPT_MASTER.md) (2026-10-23): princípios, escopo central e ordem por risco.
- [Prompt Complementar Mestre de Motores](PROMPT_MOTORES.md) (2026-10-26): motores, contratos entre motores e expansão funcional. Substitui
  os sete prompts especializados que estavam previstos.
- [Prompt de Interface](PROMPT_INTERFACE.md) (2026-10-26): design system, faixa de opções, Central do Projeto, formulários, diagnósticos e
  configurações.

Regras de uso deste plano:
- Substitui a lista "Próximas etapas" do ROADMAP, que agora aponta para cá. O quadro das 15 fases continua no ROADMAP como
  mapa de rastreio (D-021).
- Estado de cada item: AUDIT §8 (pelo escopo do Master) e §9 (motores e interface). Decisões: DECISIONS D-021 e D-022.
- O documento de estado pedido pelo Prompt de Motores (§21, "ou equivalente já adotado") é este plano junto com a AUDIT e
  os documentos de controle que já existem. Não foi criado um documento a mais.

## 1. Como os três documentos se combinam

| Documento | Papel | Quando entra |
|---|---|---|
| Prompt Master | princípios, escopo, critérios gerais, prioridade por risco | o tempo todo |
| Motores | o que cada motor calcula, contratos, integração associativa, P0–P3 | Etapas 1 a 8 e 13 |
| Interface | como o usuário vê e usa o que os motores fazem | Etapas 9 a 12 |

O Prompt de Interface (§1) pede que o Master esteja implementado antes dele. Por isso as telas vêm depois dos motores de que
dependem. Uma tela só entra quando o motor que ela mostra existe. Senão, o controle fica no estado C (indisponível, dito
com clareza) – nunca simulado.

## 2. Regras comuns (as mesmas nos três documentos)

1. **Antes de mexer:** auditar o código real e preservar o que funciona. Nada de reescrita ampla, serviço duplicado ou
   arquitetura paralela.
2. **Nada inventado:** nenhum resultado, norma, coeficiente, dado de chuva, teste ou compatibilidade inventados. Um
   parâmetro sem fonte fica configurável e apontado como pendente.
3. **Quatro níveis de prova, registrados separadamente:** build, testes automatizados, testes geométricos ou de integração
   do núcleo e validação dentro do Revit 2027. Neste ambiente não há Revit nem Windows: o que depende deles fica **NÃO
   VALIDADO NO REVIT**.
4. **Controles da interface em três estados:** A – funcional; B – parcial, com o limite dito; C – indisponível, dito com
   clareza. Nenhuma mensagem de sucesso sem operação concluída.
5. **Diagnóstico acionável:** elemento, causa provável, impacto, ação recomendada. Erro, aviso, informação e "não
   verificável" ficam separados.
6. **Modelo íntegro:** identificadores persistentes; reexecutar não duplica; transações com rollback; nada alheio ao plugin
   é apagado; edições manuais preservadas ou o conflito é comunicado.
7. **Autonomia:** decisões técnicas reversíveis sem pedir autorização. O usuário só é chamado em bloqueio real, risco
   irreversível ou requisito que só ele define.

## 3. Pedidos repetidos e onde ficam

| Pedido | Master | Motores | Interface | Fica em |
|---|---|---|---|---|
| Auditoria, inventário, mapa de módulos | §2, §28 | §1, §5, Fase 1 | Etapa Zero, Etapa 1 | **Etapa 0** |
| Pisos sobrepostos classificados, retas fora do eixo, piso × terreno | §5, §10, §16 | §9, §16 | — | **Etapa 1** |
| Diagnóstico padronizado (gravidade, elemento, causa, ação, "não verificável", histórico) | §17, §19 | §16 | §17.2, §27 | **Etapa 2** (núcleo) → **Etapa 12** (tela) |
| Unidades, tolerâncias, coordenadas explícitas | §5 | §4, §6 | — | **Etapa 2** |
| Diagnóstico do terreno | §6 | §6 | §7.2, §14 | **Etapa 3** (motor) → **Etapa 11** (tela) |
| Eixos, estacas, perfis, seções, corredores | §7 | §7 | §10 | **Etapa 4** (motor) → **Etapa 11** (formulário de vias) |
| Interseções e rotatórias explicando o que não resolvem | §8 | §7 | §12 | **Etapa 4** → **Etapa 11** |
| Estrutura de pavimento por camadas usada no cálculo | §10 | §9 | §15 | **Etapa 5** (motor) → **Etapa 11** (gerenciador) |
| Terraplenagem desatualizada, comparação de alternativas | §9 | §8 | §7.5, §10.7 | **Etapa 5** |
| Drenagem integrada a greides e terreno, perfis e listas | §11 | §10 | §16 | **Etapa 6** → **Etapa 11** |
| Indicadores urbanísticos, elementos urbanos em lote | §12 | §11, §12 | §13 | **Etapa 7** → **Etapa 11** |
| Atualização associativa e estados (atualizado, pendente, desatualizado) | §14 | §14, §15 | §8.4, §20 | **Etapa 8** (motor) → **Etapa 10** (Central do Projeto) |
| Fluxos por tarefa e faixa de opções por tema | §20 | §13 | §2.3, §7 | **Etapa 9** |
| Pré-visualização e resumo antes de aplicar | §7 | §3, §13 | §10.9, §11 | **Etapa 11** |
| Quantitativos e relatórios rastreáveis | §17 | §17 | §18 | **Etapa 12** |
| Configurações do usuário × do projeto, perfis reutilizáveis | §13 | §13 | §19, §26 | **Etapa 12** |
| Progresso, cancelamento, desempenho | §20, §21 | §13, §18 | §21 | **Etapa 13** (medição) e em cada tela longa |
| Relatório técnico final, imagens reais da interface | §27 | §21 | §32 | **Etapa 13** |

## 4. Etapas

**Situação:** ✅ concluída · 🔄 em andamento · ⬜ não iniciada.

**Entrega:** cada etapa que muda código termina com:
- build limpo e suíte completa verde;
- uma DLL em `Releases/EtapaNN`, com o número do meio da versão subindo (D-022), preservando as anteriores;
- documentos atualizados;
- um aviso ao usuário com o que foi feito, os testes e o caminho da DLL.

| Etapa | Situação | Conteúdo | Origem | Critério de aceite |
|---|---|---|---|---|
| **0 – Auditoria unificada e plano** | ✅ | AUDIT §8 (escopo do Master) e §9 (motores e interface); este plano; D-022; transcrição dos três prompts em `docs/` | Master §2/§28; Motores Fase 1; Interface Etapas 0–1 | inventário com evidência; cada pedido com uma etapa |
| **1 – Integridade geométrica (P0)** | ✅ 2.8.0, `Releases/Etapa01` | pisos sobrepostos classificados no núcleo e no Autoteste; resíduo INTERSECAO MEIO-FIO × CALCADA investigado (some com o ajuste de contorno do Revit: não é conflito); pisos × terreno no núcleo (`FloorTerrainCheck`); conferência do Autoteste contra o Toposolid projetado (antes, contra o natural) e sempre no Completo; retas fora do eixo (K12) só no próximo Autoteste | Master §5, §10, §16, RT-1.2/1.3; Motores §9, §16 | 2 966/2 966; Revit NÃO VALIDADO (K3, K11, K12, K19) |
| **2 – Núcleo de diagnósticos e tolerâncias** | ✅ 2.9.0, `Releases/Etapa03` (com a 3) | regras V01–V14 com módulo e ação; "não verificado" (`Severidade.NaoVerificavel`); comparação com a validação anterior (novo, persistente, resolvido), gravada fora do modelo; relatório com versão e módulos; tolerâncias no registro `Tolerancias` sem mudar valores | Master §5, §17, §19; Motores §4, §16; Interface §17.2 | 2 979/2 979; Validar Modelo NÃO VALIDADO NO REVIT |
| **3 – Topografia e superfícies** | ✅ 2.9.0, `Releases/Etapa03` (com a 2) | diagnóstico da superfície (`TerrainDiagnostics`) e V14 com o Toposolid projetado; declividade máxima do terreno nas Configurações; terreno original × projetado explícito (`TerrainModel.Ground` × `Current`); importação de pontos e curvas de nível passam à tela do terreno (Etapa 11), conferindo antes o que o Toposolid nativo e a API 2027 já oferecem | Master §6; Motores §6; Interface §14 | 2 979/2 979; V14 NÃO VALIDADO NO REVIT |
| **4 – Vias, corredores e interseções** | ✅ 2.10.0, `Releases/Etapa04` | limites citados nos avisos de rotatória, interseção, canalização e obras num registro (`LimitesDeConferencia`), com a fonte ou "sem norma citada" (saíram "13 a 25 m" e "T entre 75° e 105°", sem origem); estaqueamento da via (estaca inicial, intervalo, equações) nos relatórios e no Perfil da Via (D-023); os avisos dos nós já diziam a causa e o parâmetro | Master §7, §8; Motores §7 | 2 986/2 986; formulários ainda pela distância no eixo (K20); Revit NÃO VALIDADO |
| **5 – Terraplenagem e pavimentação** | ✅ 2.11.0, `Releases/Etapa05` | estrutura do pavimento na via (camadas sob o revestimento, com espessura, material, função e fonte; comando Estrutura do Pavimento), usada pela terraplenagem (fundo da caixa; nó pela via mais funda), pelo corte transversal e pelo quantitativo por camada; registro da terraplenagem aplicada com a assinatura de cada marca e V15 (alterada, apagada, retirada, nova); "Só calcular" compara a alternativa com a aplicada; quantitativo de terraplenagem com as opções da aplicada (D-024) | Master §9, §10; Motores §8, §9; Interface §15 | 3 000/3 000; projetos antigos abrem iguais; cenários completos comparados (F12) ficam para a Etapa 13; sobrelargura das camadas e transição da caixa nos nós (K21); Revit NÃO VALIDADO (K19) |
| **6 – Drenagem integrada** | ✅ 2.12.0, `Releases/Etapa06` | sarjeta de cada captação pelo greide e área que escoa até ela pela geometria da via (`DrenagemDoModelo`, comando Ler do Modelo, D-025); V13 com pontos baixos sem captação, captação que diverge do greide e talvegue do terreno natural sob aterro sem travessia; recobrimento sob a superfície acabada (vias e terreno projetado) no V13, no relatório e no memorial; perfil longitudinal da rede e lista de dispositivos (`PerfilRede`) | Master §11; Motores §10; Interface §16 | 3 011/3 011; K5 reduzido (lotes e áreas externas fora da área lida); tela do perfil: Etapa 11; Revit NÃO VALIDADO |
| **7 – Urbanismo e elementos complementares** | ✅ 2.13.0, `Releases/Etapa07` | auditoria primeiro (AUDIT §9.2a): quase tudo existia; feito o que faltava – indicadores do parcelamento no memorial (densidade, área e frente dos lotes, percentuais viário, público e verde), fonte dos mínimos, V16 (mínimos não atendidos e lotes abaixo do lote mínimo) e Trocar Elementos (substituição controlada) | Master §12; Motores §11, §12; Interface §13 | 3 016/3 016; valetas e dissipadores sem dado de projeto (fora); Revit NÃO VALIDADO |
| **8 – Integração associativa** | ✅ 2.14.0, `Releases/Etapa08` | estado dos resultados derivados pela comparação do que foi lido com o que o modelo dá agora (D-026): terraplenagem (registro) e captações lidas (`ContribuicaoLida`: em dia, desatualizada, editada à mão – preservada); atualização seletiva (só as desatualizadas, só os campos lidos); impacto de uma edição (conexões, dependentes do grafo, terraplenagem e captações) no relatório das edições; V17 e a seção "Estado dos resultados" no Validar Modelo; correção da assinatura das caixas na V15 | Master §14; Motores §14, §15 | 3 022/3 022; o estado cobre terraplenagem e captações (K22); updater no Revit NÃO VALIDADO (K17) |
| **9 – Design system e faixa de opções** | ✅ 2.15.0, `Releases/Etapa09` | tokens centrais iguais no `Styles.xaml` e no `UiTokens` (cores, estados com fundo, tipografia, espaçamento) e componentes (cartão, faixas, status, valores, botões, tabelas, foco visível); cores soltas das janelas nos tokens; faixa por tema (D-027): guia SinalizaBIM com Projeto, Terreno, Sistema Viário, Interseções, Terraplenagem, Pavimentação, Drenagem, Urbanismo, Análise, Documentação e Sistema, e guia SinalizaBIM Sinalização; os 98 botões, cada um uma vez, mais o Diagnóstico do Terreno (V14 só da topografia); dicas curtas com descrição longa, ícone próprio e botão habilitado só com projeto; mapa antigo → novo no MANUAL | Interface §5, §6, §7, §22–§24; Master §20 | 3 036/3 036 (`Etapa09InterfaceTests`: faixa e design system); nenhum comando perdido; faixa e janelas NÃO VISTAS NO REVIT (K23) |
| **10 – Central do Projeto e painel contextual** | ✅ 2.16.0, `Releases/Etapa10` | Central do Projeto (`CentralDoProjeto` no núcleo): indicadores reais – vias, extensão, interseções, rotatórias, quadras, lotes, área parcelada, corte e aterro do registro, pavimento medido só ao pedir, drenagem, inconsistências da última validação com a data – cada um com a situação do valor (calculado, não calculado, indisponível, desatualizado, inválido) e nunca zero para o desconhecido; situação do projeto e geral sem "validado"; módulos com a operação; atalhos pelo mesmo caminho do botão (`CommandBase.Invoke`). Painel contextual (`PainelContextual` no núcleo, `DockablePane`): propriedades por tipo, estado com nível e operações; seleção múltipla só com o que é igual, sem edição em lote; o evento de seleção só lê (D-028) | Interface §8, §9, §20 | 3 048/3 048 (`Etapa10CentralTests`: resumo e painel no núcleo); telas NÃO VALIDADAS NO REVIT (K24) |
| **11 – Telas dos módulos** | ✅ 2.17.0, `Releases/Etapa11` | janela da via em nove abas (identificação, geometria, perfil, seção, pavimento, terraplenagem, drenagem, validação, resumo; Validar e Resumo antes de Aplicar); gerenciador de estruturas de pavimento (tabela, esquema, quantitativo com a área medida, modelos do usuário); rede de drenagem separando geometria, conectividade, parâmetros, cálculo hidráulico e dimensionamento, com perfil e seleção no modelo; terreno (origem, natural × projetada, superfície de cada cálculo, módulos a atualizar); seções de interseções e rotatórias com verificação geométrica × operacional; resultados do parcelamento; estaca da via nos formulários (K20); correção da via refeita sem a estrutura e o estaqueamento (D-029) | Interface §10–§16; Master §7, §13 | modelos de apresentação testados no núcleo (`Etapa11TelasTests`); telas NÃO VALIDADAS NO REVIT (K25) |
| **12 – Qualidade, quantitativos e configurações** | ⬜ | Central de Qualidade (validador e Autoteste separados, filtros, localizar elemento); central de quantitativos com a origem de cada número; gerenciador de configurações (usuário × projeto) e perfis reutilizáveis (exemplo, usuário, referência citada) | Interface §17–§19, §26; Master §13, §17; Motores §17 | testes de persistência e validação dos valores |
| **13 – Desempenho, validação e entrega** | ⬜ | medir antes de otimizar (K14, K15); progresso e cancelamento nas operações longas; relatório de simulação × animação (F11); etapa integrada no Autoteste; relatório técnico final (Interface §32), com imagens reais só se o Revit rodar | Master §21, §27; Motores §18, Fase 7; Interface §21, Etapa 9 | Autoteste completo no Revit pelo usuário (fora deste ambiente) |

A ordem segue a prioridade do Master (§28.5) e as dependências: as telas (9–12) usam o que os motores (1–8) entregam.
Se uma etapa de motor travar num ponto que só o Revit confirma, a parte do núcleo é entregue e o restante fica registrado
como NÃO VALIDADO.

## 5. Limites deste ambiente (valem para todas as etapas)

- **Revit 2027 e Windows:** não existem aqui. Tudo o que é API do Revit, janela WPF ou aparência fica NÃO VALIDADO NO
  REVIT até o usuário rodar o Autoteste ou abrir a tela. Não há capturas reais da interface: o Prompt de Interface (§32)
  aceita isso desde que fique dito.
- **SDK do .NET:** o host de download da Microsoft é recusado pela política de rede desta sessão. O SDK 10.0.401 foi
  instalado pelo cache do Nix. O NuGet funciona.
- **Persistência:** o contêiner pode ser reciclado. Por isso, cada passo verde vai para o ramo (commit e push) antes do
  próximo.
