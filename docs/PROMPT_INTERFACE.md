# Prompt de Interface – SinalizaBIM para o Revit 2027

Transcrição do documento `Prompt_Interface_SinalizaBIM_Revit_2027.docx` enviado pelo usuário em 2026-10-26. Ele é executado junto com o Prompt Master
pelo plano único `docs/PLANO_DE_ACAO.md` (DECISIONS D-022). O texto está como foi enviado; só a formatação virou Markdown.
A presença de um requisito aqui **não** significa que ele esteja implementado: o estado real está na AUDIT §8–§9.

**PROMPT DE IMPLEMENTAÇÃO DA INTERFACE PROFISSIONAL DO SINALIZABIM**

Revit 2027 — Design System, UX/UI, Fluxos de Trabalho e Integração Funcional

## 1. CONTEXTO E MOMENTO DE EXECUÇÃO

O SinalizaBIM é um plugin de infraestrutura e urbanismo desenvolvido para funcionar dentro do Autodesk Revit 2027.

A implementação do Prompt Master do projeto deverá estar concluída antes do início desta etapa. Portanto, considere que você está trabalhando sobre um projeto existente, com arquitetura de software, módulos, funcionalidades e integrações já desenvolvidos ou em diferentes estágios de maturidade.

Sua missão agora é transformar a interface do SinalizaBIM em uma experiência de software profissional, moderna, visualmente refinada, intuitiva e consistente.

Esta etapa é dedicada à interface, à experiência do usuário e à integração visual e funcional dos recursos existentes.

Não interprete esta solicitação como autorização para reescrever o plugin inteiro, substituir sua arquitetura ou descartar funcionalidades existentes.

Antes de modificar qualquer arquivo, faça uma auditoria da implementação atual.

A interface deve ser desenvolvida para o ambiente real do Revit 2027, respeitando as limitações, os padrões de integração e os recursos de interface efetivamente disponíveis nessa versão.

Não crie apenas um protótipo visual. Implemente a interface real no projeto, conectando seus controles às funcionalidades existentes sempre que estas estiverem disponíveis.

Quando alguma funcionalidade ainda não estiver implementada, a interface deverá comunicar essa condição com clareza, sem simular resultados ou apresentar controles que aparentem funcionar quando não funcionam.

## 2. VISÃO DO PRODUTO E REFERÊNCIAS DE EXPERIÊNCIA

O SinalizaBIM deve transmitir a sensação de um software profissional de engenharia de infraestrutura e planejamento urbano.

A experiência deve combinar quatro princípios.

2.1. Familiaridade do Revit

- Utilizar a faixa de opções do Revit como principal ponto de acesso aos comandos.
- Preservar a área de trabalho nativa do Revit.
- Utilizar janelas, painéis e diálogos compatíveis com o ambiente hospedeiro.
- Evitar criar uma segunda aplicação independente dentro do Revit.
- Não replicar desnecessariamente ferramentas que já existem no programa hospedeiro.
- Manter coerência com os padrões de navegação, seleção e edição do Revit.

2.2. Clareza visual inspirada no InfraWorks

O usuário precisa compreender facilmente:

- Qual objeto está editando.
- Quais parâmetros estão sendo alterados.
- Como a alteração afeta o projeto.
- Quais elementos dependem daquele objeto.
- Se a geometria está atualizada.
- Se existem problemas de modelagem ou incompatibilidades.
- Quais resultados foram calculados.

2.3. Facilidade de planejamento urbano inspirada no Cities: Skylines

O usuário deve conseguir passar naturalmente de uma etapa para outra:

1. Preparação do terreno.
2. Definição do traçado viário.
3. Criação de interseções.
4. Definição de quadras.
5. Parcelamento de lotes.
6. Configuração de pavimentação.
7. Dimensionamento e organização da drenagem.
8. Verificação do projeto.
9. Extração de quantitativos e documentação.

Essa sequência representa uma possibilidade de organização dos fluxos de trabalho, não uma restrição que impeça outras ordens de execução.

2.4. Qualidade de um software comercial

A interface deve apresentar hierarquia visual consistente, ícones de qualidade, tipografia legível, espaçamento equilibrado, alinhamento rigoroso, estados visuais claros, feedback imediato, mensagens de erro úteis, formulários organizados, navegação previsível e aparência profissional em todas as janelas.

O resultado deve parecer parte de um produto comercial cuidadosamente desenvolvido, e não uma coleção de formulários técnicos montados sem uma linguagem visual comum.

## 3. PRINCÍPIO FUNDAMENTAL: A INTERFACE DEVE SER FUNCIONAL

Não desenvolva uma interface composta apenas por botões bonitos. Cada elemento deve ter uma finalidade real e um comportamento coerente.

Todo botão, campo, seletor, aba, menu, indicador, comando ou opção de configuração deve estar em uma das seguintes situações:

Estado A — Funcional: o controle está conectado à implementação real e executa sua tarefa.

Estado B — Parcialmente funcional: o controle executa somente as operações já implementadas e informa claramente suas limitações.

Estado C — Indisponível: o controle informa que a funcionalidade ainda não está disponível, sem simular sua execução.

Não utilize mensagens fictícias de sucesso, cálculos inventados ou resultados demonstrativos como se fossem dados reais do projeto. Não deixe botões com aparência funcional sem uma ação correspondente. Não crie comandos redundantes apenas para preencher espaço.

A prioridade é oferecer uma interface completa para os recursos efetivamente disponíveis, mantendo a arquitetura preparada para a expansão dos demais módulos.

## 4. ETAPA ZERO — AUDITORIA OBRIGATÓRIA

Antes de começar a implementar a interface, analise o projeto existente.

Investigue:

- Estrutura da solução e dos projetos.
- Linguagem e versão do framework utilizados.
- Tecnologia de interface atualmente adotada.
- Ponto de entrada do plugin.
- Arquivos de configuração e inicialização.
- Registro das ferramentas na interface do Revit.
- Implementação da faixa de opções.
- Janelas e painéis já existentes.
- Serviços e módulos de infraestrutura.
- Modelos de dados.
- Sistemas de cálculo.
- Serviços de geração de geometria.
- Mecanismos de diagnóstico.
- Sistema de relatórios.
- Testes automatizados existentes.
- Estratégia de atualização dos elementos do Revit.
- Dependências externas.
- Restrições específicas do Revit 2027.

Identifique quais partes da interface podem ser reaproveitadas e quais precisam ser melhoradas. Não altere desnecessariamente os módulos de cálculo para resolver problemas exclusivamente visuais. Não substitua serviços de domínio por lógica implementada diretamente nos formulários. Não introduza uma nova tecnologia de interface sem verificar sua compatibilidade com o projeto e com o Revit 2027.

Ao concluir a auditoria, estabeleça um plano de implementação compatível com a arquitetura existente.

Não interrompa o trabalho para solicitar autorização para decisões técnicas rotineiras. Tome decisões fundamentadas, preserve a compatibilidade e registre as escolhas importantes. Solicite minha intervenção somente diante de um bloqueio real que não possa ser resolvido com segurança a partir do código e dos requisitos existentes.

## 5. DIREÇÃO VISUAL E IDENTIDADE DO PRODUTO

5.1. Conceito visual

Adote uma linguagem visual contemporânea, técnica, minimalista e sofisticada. A interface deve transmitir precisão, confiabilidade, organização, engenharia, tecnologia, controle e eficiência.

Evite uma aparência excessivamente futurista, caricata, colorida ou semelhante à de um jogo. A inspiração em softwares visuais não deve comprometer a seriedade técnica do produto.

5.2. Paleta de cores

Implemente um sistema de cores centralizado, consistente e configurável.

Elemento   •   Direção visual

Fundo das janelas   •   Cinza muito claro ou neutro

Superfícies e cartões   •   Branco ou cinza quase branco

Texto principal   •   Cinza muito escuro

Texto secundário   •   Cinza médio

Cor primária   •   Azul profundo ou azul técnico

Seleção ativa   •   Azul com contraste suficiente

Ações positivas   •   Verde discreto

Avisos   •   Âmbar

Erros   •   Vermelho controlado

Elementos desabilitados   •   Cinza com contraste legível

Bordas   •   Cinza neutro e discreto

Essas cores são referências iniciais, não valores obrigatórios. Ajuste a paleta após examinar o ambiente real do Revit e a identidade visual já existente no projeto.

Não utilize cores saturadas indiscriminadamente. Reserve as cores de destaque para ações, estados e informações que realmente precisem chamar atenção. Não dependa exclusivamente da cor para comunicar estados: utilize também textos, ícones e símbolos quando apropriado.

5.3. Tipografia

Utilize uma fonte legível e compatível com o ambiente Windows e com a tecnologia de interface adotada. Estabeleça uma hierarquia consistente para títulos de janelas, títulos de seções, subtítulos, texto de formulários, rótulos, valores numéricos, informações auxiliares, mensagens de validação e informações de status.

Evite fontes decorativas, textos excessivamente pequenos e títulos desnecessariamente grandes. Os valores de engenharia devem ser particularmente legíveis.

5.4. Espaçamento e alinhamento

Estabeleça uma escala consistente de espaçamento, margens e dimensões.

Evite campos desalinhados, botões de tamanhos aleatórios, textos cortados, espaços excessivos, elementos comprimidos, formulários que exigem rolagem desnecessária e informações importantes escondidas. A interface deve continuar utilizável em diferentes resoluções e escalas de DPI.

5.5. Ícones

Utilize ícones vetoriais ou outra solução apropriada à tecnologia adotada. Mantenha estilo, espessura de traço, legibilidade, tamanho, contraste e aparência consistentes.

Crie ou adote ícones específicos para terreno, eixos viários, ruas e avenidas, interseções, rotatórias, seções transversais, perfis longitudinais, terraplenagem, pavimentação, drenagem, quadras, lotes, quantitativos, diagnósticos, relatórios e configurações.

Não misture estilos incompatíveis nem utilize emojis como substitutos dos ícones profissionais da interface.

## 6. ARQUITETURA DA INTERFACE

Organize a interface em três níveis complementares:

1. Faixa de opções do SinalizaBIM dentro do Revit.
2. Janelas e painéis de trabalho especializados.
3. Painéis contextuais para propriedades, diagnósticos e resultados.

A faixa de opções deve permitir acesso rápido aos comandos principais. As janelas especializadas devem concentrar operações que exigem mais espaço, configuração ou visualização de informações. Os painéis contextuais devem apresentar somente as informações relevantes para a tarefa atual.

Evite concentrar todo o plugin em uma única janela gigantesca. Também evite criar uma janela independente para cada pequeno comando.

## 7. FAIXA DE OPÇÕES DO SINALIZABIM NO REVIT

Implemente uma organização profissional para a guia SinalizaBIM, com painéis temáticos claramente identificados.

7.1. Painel Projeto

- Central do Projeto.
- Configurações do Projeto.
- Gerenciador de Infraestrutura.
- Atualizar Projeto.
- Verificar Atualizações do Modelo.

7.2. Painel Terreno

- Analisar Terreno.
- Superfície Topográfica.
- Curvas de Nível.
- Regiões e Áreas.
- Diagnóstico Topográfico.

7.3. Painel Sistema Viário

- Criar Via.
- Editar Via.
- Gerenciar Eixos.
- Perfis Longitudinais.
- Seções Transversais.
- Gerenciar Seções-Tipo.

7.4. Painel Interseções

- Criar Interseção.
- Criar Rotatória.
- Editar Geometria.
- Verificar Conflitos.

7.5. Painel Terraplenagem

- Calcular Terraplenagem.
- Visualizar Corte e Aterro.
- Comparar Superfícies.
- Quantificar Movimentação de Terra.

7.6. Painel Pavimentação

- Configurar Pavimento.
- Gerenciar Camadas.
- Gerar Pavimentação.
- Calcular Quantitativos.
- Verificar Pavimentação.

7.7. Painel Drenagem

- Gerenciar Rede.
- Inserir Dispositivos.
- Configurar Tubulações.
- Analisar Rede.
- Verificar Drenagem.

7.8. Painel Urbanismo

- Criar Quadras.
- Parcelar Lotes.
- Editar Parcelamento.
- Verificar Lotes.
- Calcular Áreas.

7.9. Painel Análise

- Executar Diagnósticos.
- Verificar Geometria.
- Verificar Dependências.
- Analisar Inconsistências.

7.10. Painel Documentação

- Gerar Relatórios.
- Gerar Quantitativos.
- Gerenciar Documentação.
- Exportar Resultados.

7.11. Painel Sistema

- Preferências.
- Gerenciador de Módulos.
- Informações da Versão.
- Ajuda.

### Regras da faixa de opções

Utilize os padrões suportados pela API do Revit 2027. Preserve os comandos existentes e não crie comandos duplicados. Não crie botões que executem operações incompatíveis com o contexto atual. Utilize tooltips, textos curtos e claros, ícones apropriados e estados de habilitação coerentes.

Se um comando depender de seleção, projeto aberto ou dados específicos, valide essa condição e informe o que está faltando.

## 8. CENTRAL DO PROJETO

Crie uma janela principal chamada Central do Projeto SinalizaBIM, ponto de entrada para as operações de infraestrutura e urbanismo.

8.1. Cabeçalho

Apresente nome do projeto, identificação do documento Revit, situação geral do modelo, estado de atualização, última verificação quando disponível e acessos às configurações e à ajuda. Não invente datas, estados ou informações inexistentes.

8.2. Resumo do projeto

Apresente cartões informativos para indicadores relevantes, quando disponíveis:

- Extensão total das vias.
- Quantidade de vias.
- Quantidade de interseções.
- Quantidade de rotatórias.
- Área total de quadras.
- Quantidade de lotes.
- Área parcelada.
- Volume de corte.
- Volume de aterro.
- Quantitativos de pavimentação.
- Estado da rede de drenagem.
- Quantidade de inconsistências detectadas.

Cada indicador deve vir de dados reais ou de resultados armazenados validamente. Não exiba zero quando o dado for desconhecido. Diferencie valor calculado, não calculado, indisponível, desatualizado e inválido. Quando possível, permita abrir o módulo ou relatório relacionado ao clicar no indicador.

8.3. Módulos e operações recentes

Organize os módulos em cartões ou blocos com ícone, nome, descrição curta, estado de disponibilidade e acesso à operação principal. Mostre operações recentes apenas se o sistema registrar esse histórico de forma confiável.

8.4. Situação do projeto

Apresente estados como atualizado, alterações pendentes, necessita de recálculo, existem avisos, existem erros ou verificação não executada. Esses estados devem refletir informações reais. Não declare que o projeto está validado apenas porque uma operação foi concluída.

## 9. JANELA DE TRABALHO E PAINÉIS CONTEXTUAIS

Aproveite a visualização do modelo no Revit e evite cobrir desnecessariamente a área de trabalho. Sempre que tecnicamente viável, prefira janelas redimensionáveis, painéis reutilizáveis ou mecanismos compatíveis com o Revit 2027.

Avalie um painel contextual para propriedades do SinalizaBIM, desde que compatível com a arquitetura existente.

9.1. Organização do painel contextual

Ao selecionar um elemento do SinalizaBIM, identifique seu tipo e apresente propriedades relevantes: via, eixo, interseção, rotatória, seção-tipo, camada de pavimento, elemento de drenagem, quadra ou lote.

Não apresente todos os parâmetros de todos os módulos simultaneamente. Exiba somente os parâmetros pertinentes ao elemento selecionado.

9.2. Comportamento contextual

- Sem elemento compatível selecionado: apresente orientações e atalhos úteis.
- Com um elemento selecionado: identifique-o, mostre propriedades, estado de atualização e operações compatíveis.
- Com múltiplos elementos selecionados: apresente propriedades compartilhadas somente quando for seguro.
- Com elemento que não pertença ao SinalizaBIM: preserve o comportamento normal do Revit.

Não permita alterações em lote que possam gerar inconsistências sem validação adequada.

## 10. FORMULÁRIO DE CRIAÇÃO E EDIÇÃO DE VIAS

Esta é uma das interfaces mais importantes do produto. Crie um fluxo de trabalho profissional para criar e editar vias, permitindo compreender a geometria, configurar parâmetros e verificar consequências antes da geração.

10.1. Estrutura da janela

Organize o formulário em:

1. Identificação.
2. Geometria horizontal.
3. Perfil longitudinal.
4. Seção transversal.
5. Pavimentação.
6. Terraplenagem.
7. Drenagem.
8. Validação.
9. Resumo da operação.

Utilize abas ou seções expansíveis. Não imponha sequência rígida quando as configurações puderem ser realizadas independentemente.

10.2. Identificação

Campos possíveis: nome da via, código ou identificador, tipo de via, categoria funcional e observações.

10.3. Geometria horizontal

Disponibilize, conforme suportado pelo motor geométrico:

- Definição do eixo e método de criação.
- Parâmetros de alinhamento.
- Largura da pista.
- Número e largura das faixas.
- Largura dos acostamentos e calçadas.
- Largura de canteiros.
- Afastamentos laterais.
- Parâmetros de transição.
- Configuração de elementos curvos.

Campos dependentes devem atualizar-se de forma coerente. Por exemplo, a alteração do número de faixas deve refletir a largura calculada da pista quando esse modo de definição estiver ativo. Não imponha dependências quando o usuário tiver definido valores explicitamente.

10.4. Perfil longitudinal

Disponibilize, conforme suportado: elevação, inclinação longitudinal, estacas ou progressivas, pontos de controle, curvas verticais, transições de greide e relação com o terreno existente.

Valide os valores antes da geração. Use critérios técnicos implementados e configurações do projeto; não invente limites normativos. Quando uma regra depender de norma, identifique a referência e a configuração utilizada.

10.5. Seção transversal

Apresente um esquema visual da seção-tipo, quando viável, representando pista, faixas, acostamentos, calçadas, canteiros, bordas, taludes e camadas de pavimento.

A representação deve atualizar-se com os parâmetros e refletir os valores efetivamente usados no cálculo. Não precisa ser um sistema CAD independente, mas deve ser clara e suficientemente precisa para representar a configuração.

10.6. Pavimentação

Disponibilize seleção do tipo de pavimento, definição das camadas, espessuras, materiais, parâmetros de geração e quantitativos previstos quando calculáveis. Permita editar os parâmetros pela interface.

10.7. Terraplenagem

Apresente terreno existente e projetado, parâmetros de taludes quando aplicáveis, método de cálculo disponível, volumes calculados, estado do cálculo e avisos geométricos. Não apresente estimativas como resultados definitivos.

10.8. Drenagem

Apresente configurações disponíveis e, quando integradas, dispositivos associados, pontos de captação, tubulações, direções de escoamento, conexões e estado de verificação. Não afirme que a rede está hidraulicamente dimensionada se houver apenas verificação geométrica.

10.9. Rodapé de ações

Inclua ações claramente diferenciadas: Pré-visualizar, Validar, Aplicar, Aplicar e atualizar dependências e Cancelar. Utilize somente ações reais. Explique impactos antes de operações irreversíveis ou que alterem muitos elementos.

## 11. PRÉ-VISUALIZAÇÃO E CONFIRMAÇÃO DAS OPERAÇÕES

Sempre que tecnicamente possível, implemente pré-visualização antes de gerar ou alterar geometrias relevantes.

A pré-visualização poderá apresentar geometria prevista, dimensões, seção transversal, perfil longitudinal, quantitativos calculados, elementos dependentes afetados e avisos de validação.

Utilize dados reais do modelo e parâmetros efetivamente informados. Priorize operações frequentes e de maior impacto, começando pela criação e edição de vias.

Antes de confirmar uma alteração relevante, informe o que será criado, atualizado ou recalculado, quais elementos existentes poderão ser modificados e quais problemas foram detectados. Não execute alterações destrutivas sem confirmação e tratamento seguro de falhas.

## 12. INTERFACE DE INTERSEÇÕES E ROTATÓRIAS

Desenvolva formulários específicos para interseções e rotatórias, sem reutilizar indiscriminadamente o formulário de vias.

12.1. Interseções

Organize os parâmetros em identificação, vias conectadas, geometria, ângulos e alinhamentos, bordas e concordâncias, calçadas, pavimentação, terraplenagem, verificação e resumo.

Apresente conexões existentes e incompatibilidades detectadas. Ofereça configurações geométricas alternativas quando os algoritmos existentes as suportarem.

12.2. Rotatórias

Organize identificação, parâmetros dimensionais, ilha central, faixas circulares, entradas e saídas, vias conectadas, geometria, pavimentação, terraplenagem, drenagem e verificação.

Exiba uma representação esquemática atualizada. Não considere a geometria gerada como validação operacional de tráfego. Diferencie validação geométrica das análises operacionais não implementadas.

## 13. INTERFACE DE QUADRAS E PARCELAMENTO DE LOTES

Crie uma interface específica, acessível e tecnicamente precisa para quadras e lotes.

13.1. Organização

Divida o fluxo em área de intervenção, limites do parcelamento, quadras, lotes, sistema viário associado, áreas públicas, parâmetros dimensionais, verificação e quantitativos.

13.2. Configurações

Disponibilize, conforme os algoritmos existentes, dimensões dos lotes, testadas, profundidades, áreas mínimas, afastamentos, orientação, formas de divisão, critérios de geração, tratamento de áreas residuais, identificação de lotes e configuração de quadras.

Os parâmetros devem ser acessíveis pela interface, sem exigir alterações no código.

13.3. Visualização

Destaque, quando possível, limites das quadras, divisões dos lotes, identificadores, áreas, testadas, áreas residuais e problemas geométricos. Não dependa exclusivamente de cores para identificar problemas.

13.4. Resultados

Apresente número de quadras e lotes, área parcelada, áreas individuais, áreas públicas quando identificáveis, lotes com problemas e áreas residuais. Não afirme conformidade legal ou urbanística automática sem regras explicitamente configuradas e implementadas.

## 14. INTERFACE DE TERRENO E TOPOGRAFIA

Organize as operações em origem dos dados, superfície existente, superfície projetada, curvas de nível, pontos de controle, limites de análise, parâmetros, resultados e diagnósticos.

Permita selecionar fontes de dados compatíveis com a implementação. Identifique claramente qual superfície é usada em cada cálculo e não misture dados de superfícies diferentes silenciosamente.

Quando uma alteração do terreno afetar vias, pavimentação ou terraplenagem, informe quais módulos poderão necessitar de atualização.

## 15. INTERFACE DE PAVIMENTAÇÃO

Implemente um gerenciador visual de estruturas de pavimento. Apresente ordem da camada, nome, material, espessura, função e estado de configuração.

Quando suportado, permita adicionar, remover e reordenar camadas, editar espessuras, selecionar materiais e salvar configurações reutilizáveis.

Utilize uma representação esquemática em corte. Apresente quantitativos apenas quando houver dados suficientes. Diferencie materiais cadastrados no Revit, definidos no SinalizaBIM e ainda não associados. Evite duplicações desnecessárias.

## 16. INTERFACE DE DRENAGEM

Apresente, quando suportado, elementos da rede, nós, tubulações, dispositivos, conexões, sentidos de escoamento, parâmetros dimensionais, estados de verificação e problemas de conectividade.

Separe claramente:

1. Consistência geométrica.
2. Conectividade da rede.
3. Verificação de parâmetros.
4. Cálculo hidráulico, caso implementado.
5. Dimensionamento técnico, caso implementado.

Não apresente verificação geométrica como dimensionamento hidráulico completo. Quando possível, permita navegar até o elemento relacionado ao problema. Explique o problema e ofereça informações úteis para sua correção.

## 17. DIAGNÓSTICOS E CENTRAL DE QUALIDADE

Crie uma interface unificada para diagnósticos, reunindo os resultados dos verificadores existentes sem substituí-los por verificações superficiais.

17.1. Categorias

Organize problemas por geometria, topografia, vias, interseções, terraplenagem, pavimentação, drenagem, quadras e lotes, dependências e integração com o Revit. Inclua somente categorias compatíveis com verificadores existentes.

17.2. Classificação

Diferencie erro, aviso, informação e verificação não executada. Preserve classificações mais específicas já utilizadas pelo sistema.

17.3. Lista de problemas

Apresente tabela com categoria, elemento, descrição, gravidade, estado, ação recomendada e data ou identificação da verificação quando disponível. Permita filtrar e ordenar. Quando viável, permita selecionar o problema e localizar o elemento no modelo.

17.4. Ações

Disponibilize, conforme suportado, abrir elemento, selecionar elemento, exibir detalhes, reexecutar verificação, exportar resultados e consultar recomendações. Não implemente correções automáticas destrutivas sem análise explícita de impacto.

17.5. Relatório completo de autoteste

Se houver sistema de autoteste, integre seus resultados e apresente testes executados, aprovados, com avisos, com falhas e não executados, duração, versão, contexto e referências aos detalhes dos problemas.

Não confunda diagnóstico do modelo com testes automatizados do código. São informações diferentes e devem ser apresentadas separadamente.

## 18. CENTRAL DE QUANTITATIVOS E RELATÓRIOS

Crie uma interface para consultar e exportar resultados, organizados por vias, pavimentação, terraplenagem, drenagem, quadras, lotes e outros módulos disponíveis.

Quando possível, permita filtrar por elemento, categoria ou setor, agrupar resultados, ordenar valores, visualizar totais, exportar relatórios e consultar a origem dos valores.

Identifique claramente unidades como metros, metros quadrados, metros cúbicos, unidades e, quando aplicável e calculado, quilogramas ou toneladas.

Formate números conforme a configuração regional do usuário e a precisão necessária. Não arredonde os valores internos apenas para melhorar a aparência.

18.1. Rastreabilidade

Quando possível, identifique elementos de origem, método de cálculo, data da atualização, estado de validade e versão ou configuração utilizada. O usuário deve compreender a origem dos números.

## 19. GERENCIADOR DE CONFIGURAÇÕES

Implemente uma central de configurações para parâmetros acessíveis ao usuário, organizados em Geral, Unidades, Geometria, Topografia, Sistema Viário, Seções-Tipo, Pavimentação, Terraplenagem, Drenagem, Urbanismo, Diagnósticos, Documentação, Aparência e Desempenho, conforme suportado.

19.1. Requisitos

- Permitir editar parâmetros sem modificar o código.
- Identificar valores inválidos.
- Informar quando uma alteração afeta cálculos existentes.
- Distinguir configurações do usuário das configurações do projeto.
- Preservar configurações quando tecnicamente apropriado.
- Validar valores antes de aplicá-los.
- Evitar alterações silenciosas em elementos existentes.

19.2. Perfis reutilizáveis

Se a arquitetura permitir, ofereça perfis para seções-tipo, estruturas de pavimento, configurações de vias, parâmetros de urbanismo e critérios de diagnóstico.

Não crie predefinições fictícias que aparentem representar normas oficiais. Identifique perfis de exemplo, do usuário e associados a referências técnicas verificáveis.

## 20. ATUALIZAÇÕES, DEPENDÊNCIAS E SEGURANÇA

Quando um parâmetro for modificado, identifique, conforme a arquitetura existente, se a alteração é visual, requer recálculo, afeta geometria, quantitativos ou dependências, ou pode invalidar resultados anteriores.

20.1. Estados de atualização

Apresente estados como atualizado, alterações pendentes, recalculando, necessita de validação, falha na atualização e atualização parcial. Utilize-os somente quando respaldados pelo funcionamento real.

20.2. Confirmação de operações

Antes de operações que possam alterar muitos elementos, apresente resumo do impacto: quantidade de elementos afetados, módulos dependentes, tipo de alteração, possíveis consequências e erros de validação. Se a quantidade não puder ser conhecida, informe essa limitação.

20.3. Tratamento de falhas

Preserve o estado anterior sempre que tecnicamente possível, utilize transações adequadas do Revit, registre detalhes técnicos, apresente mensagens compreensíveis, evite elementos parcialmente atualizados e permita repetir a operação quando seguro.

Não esconda exceções relevantes nem apresente sucesso após uma operação incompleta.

## 21. FEEDBACK, CARREGAMENTO E DESEMPENHO

Durante operações demoradas, apresente feedback adequado: indicador de progresso, etapa atual, descrição, quantidade processada quando disponível, avisos e cancelamento quando seguro.

Não invente percentuais. Se não houver estimativa confiável, utilize indicador de atividade sem percentual.

Respeite as regras de execução e acesso ao documento da API do Revit. Não mova operações que dependem do contexto autorizado do Revit para threads incompatíveis. Separe cálculos independentes, preparação de dados e atualizações do modelo quando apropriado.

Avalie o desempenho em projetos de diferentes tamanhos. Evite recálculos completos quando uma atualização incremental for segura e suficiente.

## 22. PADRÕES DE FORMULÁRIOS E COMPONENTES REUTILIZÁVEIS

Crie uma biblioteca de componentes visuais reutilizáveis compatível com a arquitetura: cabeçalhos, cartões informativos, botões primários e secundários, campos numéricos e de texto, listas, tabelas, seletores, abas, seções expansíveis, indicadores de estado, mensagens de validação, barras de progresso, confirmações, painéis de diagnóstico, mensagens de erro e tooltips.

Padronize espaçamento, tipografia, bordas, cores, foco, estados desabilitados, erros e carregamento.

Não implemente repetidamente o mesmo componente em cada módulo. Centralize aparência e comportamento para facilitar manutenção. Não introduza dependência visual desnecessária apenas para obter um efeito estético.

## 23. ACESSIBILIDADE E USABILIDADE

Implemente, quando aplicável:

- Contraste adequado.
- Textos legíveis.
- Navegação por teclado.
- Ordem lógica de foco.
- Atalhos consistentes.
- Rótulos claros.
- Tooltips informativos.
- Identificação de campos obrigatórios.
- Mensagens de validação próximas aos campos.
- Confirmação de operações relevantes.
- Estados de foco perceptíveis.

Não utilize apenas ícones sem identificação em operações pouco conhecidas ou potencialmente destrutivas. Considere diferentes dimensões de tela, escalas de DPI e configurações regionais. Não dependa de posições fixas que quebrem os formulários em outras resoluções.

## 24. CONSISTÊNCIA COM O REVIT 2027

Verifique compatibilidade da interface e dos assemblies, inicialização e encerramento do plugin, registro dos comandos, integração com o documento ativo, seleção de elementos, atualização das vistas, transações, tratamento de exceções, múltiplos documentos conforme suportado, fechamento e reabertura de documentos, persistência das configurações e coexistência com comandos nativos.

Não implemente atalhos que interfiram indevidamente nos comandos nativos. Não substitua a interface padrão do Revit. Não crie dependências de um executável externo para exibir a interface principal nem torne serviços externos obrigatórios para recursos locais.

Preserve e amplie de forma organizada a arquitetura consolidada de comandos, serviços e janelas.

## 25. ARQUITETURA DE SOFTWARE E SEPARAÇÃO DE RESPONSABILIDADES

Separe apresentação, estado da interface, serviços de aplicação, regras de negócio, motor geométrico, motor topográfico, integração com Revit, persistência, diagnósticos e relatórios.

Escolha a tecnologia de acordo com a arquitetura existente e preserve padrões adequados já utilizados.

Evite formulários gigantescos com toda a lógica embutida, regras duplicadas, chamadas diretas desnecessárias à API do Revit em cada controle, dependências circulares, estado global desnecessário, acesso descontrolado ao documento ativo e acoplamento excessivo.

A interface deve chamar os serviços existentes e apresentar seus resultados. Se faltar um serviço, implemente a integração mínima necessária, sem criar arquitetura paralela ou duplicar lógica.

## 26. PERSONALIZAÇÃO E PERSISTÊNCIA DA EXPERIÊNCIA

Quando tecnicamente apropriado, permita personalizar dimensões das janelas, último módulo utilizado, filtros, organização de colunas, preferências de visualização, unidades de exibição, configurações visuais e perfis de trabalho.

Preserve preferências de forma compatível com a arquitetura. Não misture preferências pessoais com dados técnicos do modelo e não armazene informações sensíveis desnecessárias.

A interface deve abrir em estado previsível mesmo sem configurações anteriores.

## 27. ESTADOS VISUAIS E MENSAGENS

Padronize a comunicação. Mensagens devem ser compreensíveis e orientar uma ação quando necessário.

Evite mensagens genéricas como “Erro”, “Falha”, “Operação inválida” ou “Não foi possível”.

Sempre que houver informação suficiente, explique:

1. O que aconteceu.
2. Por que aconteceu.
3. Qual elemento foi afetado.
4. O que o usuário pode fazer.
5. Se os dados foram preservados.

Não exponha detalhes internos de exceções como única explicação. Registre detalhes técnicos para diagnóstico.

Padronize sucesso, aviso, erro, informação, carregamento, operação cancelada, resultado desatualizado e recurso indisponível. Só mostre sucesso quando a operação tiver sido concluída.

## 28. TESTES OBRIGATÓRIOS DA INTERFACE

Não considere o trabalho concluído apenas porque o projeto compila.

28.1. Testes de compilação

Compile a solução, verifique referências, identifique erros e avisos relevantes, confira compatibilidade com o ambiente de destino e confirme a presença dos componentes necessários.

28.2. Testes automatizados

Quando viável, teste validação de campos, conversão de unidades, formatação de valores, estados da interface, regras de habilitação, carregamento e persistência de configurações, integração com serviços, tratamento de falhas e modelos de apresentação. Não duplique testes existentes sem necessidade.

28.3. Testes de integração

Verifique registro da guia, inicialização de janelas, execução dos comandos, comunicação com serviços, atualização dos dados, seleção de elementos, comportamento após alterações no modelo e recuperação após falhas.

28.4. Testes visuais

Inspecione a interface real quando houver ambiente disponível. Verifique cortes de texto, alinhamento, botões fora da área visível, escala, contraste, legibilidade, rolagem, redimensionamento, foco, posição das janelas e diferentes escalas de DPI.

28.5. Testes no Revit

Quando o ambiente estiver disponível, teste no Revit 2027. Se não estiver, registre a limitação. Não declare teste no Revit se apenas compilou. Não declare aprovação visual sem inspecionar a interface e não invente resultados.

## 29. ESTRATÉGIA DE IMPLEMENTAÇÃO

Execute o trabalho em etapas, mantendo o projeto funcional.

ETAPA 1 — Auditoria e plano

- Inspecionar a interface existente.
- Identificar tecnologias e componentes.
- Mapear os comandos atuais.
- Identificar serviços disponíveis.
- Elaborar o plano.
- Registrar riscos e dependências.

ETAPA 2 — Design system

Implementar paleta, tipografia, espaçamento, botões, campos, cartões, tabelas, indicadores, mensagens, ícones e estados visuais.

ETAPA 3 — Faixa de opções

Organizar a guia SinalizaBIM no Revit, preservando comandos existentes e conectando a navegação aos módulos disponíveis.

ETAPA 4 — Central do Projeto

Implementar a janela principal e integrar dados reais do projeto.

ETAPA 5 — Interfaces de infraestrutura

Priorizar:

1. Criação e edição de vias.
2. Interseções e rotatórias.
3. Terraplenagem.
4. Pavimentação.
5. Drenagem.

ETAPA 6 — Interfaces de urbanismo

Implementar quadras, parcelamento de lotes, áreas, configurações urbanísticas e verificações relacionadas.

ETAPA 7 — Diagnósticos e documentação

Integrar central de qualidade, autotestes, quantitativos, relatórios e exportação.

ETAPA 8 — Configurações e refinamento

Implementar preferências, perfis reutilizáveis, persistência, acessibilidade e padronização final.

ETAPA 9 — Validação e acabamento

Compilar, executar testes disponíveis, corrigir problemas, inspecionar interfaces, verificar integração, documentar limitações e preparar a entrega.

Essa sequência é uma orientação, não uma obrigação de dividir o código em módulos artificiais. Respeite dependências técnicas e valide etapas menores quando isso for mais seguro.

## 30. AUTONOMIA DE EXECUÇÃO

Trabalhe de forma autônoma. Não interrompa o desenvolvimento para pedir autorização para escolher espaçamentos, ajustar cores, organizar campos, melhorar alinhamentos, reutilizar componentes, corrigir problemas visuais, refatorar apresentação, corrigir integração evidente, criar testes, melhorar mensagens, organizar comandos ou tomar decisões técnicas reversíveis e compatíveis.

Tome decisões fundamentadas e registre escolhas relevantes. Preserve módulos existentes. Não remova recursos para simplificar a interface. Não reescreva o motor geométrico ou topográfico apenas para acomodar escolhas visuais. Não altere contratos de serviços sem necessidade comprovada.

Se uma mudança arquitetural for indispensável, escolha a solução de menor impacto e documente a justificativa. Se uma funcionalidade estiver ausente, prepare a interface para integração futura, mas não a apresente como concluída.

Solicite minha intervenção somente quando houver um bloqueio real que não possa ser resolvido com segurança a partir do projeto e dos requisitos.

## 31. CRITÉRIOS DE ACEITAÇÃO

A etapa será considerada concluída quando os critérios abaixo forem atendidos, na medida em que o ambiente e as funcionalidades existentes permitirem.

### Aparência

- Identidade visual consistente.
- Componentes com padrões compartilhados.
- Ícones uniformes.
- Formulários organizados.
- Informações legíveis.
- Ausência de problemas visuais evidentes.
- Janelas adequadas a diferentes tamanhos.
- Aparência profissional de software de infraestrutura.

### Usabilidade

- Comandos organizados por finalidade.
- Operações fáceis de identificar.
- Formulários com agrupamentos lógicos.
- Mensagens úteis.
- Feedback para ações importantes.
- Validação de alterações relevantes.
- Resultados e limitações compreensíveis.

### Integração

- Integração com Revit 2027.
- Comandos conectados às funções correspondentes.
- Origem dos dados identificável.
- Respeito à arquitetura existente.
- Preservação das funções nativas do Revit.

### Confiabilidade

- Nenhum controle simulando funcionalidade inexistente.
- Mensagens de sucesso correspondentes a operações concluídas.
- Tratamento adequado de falhas.
- Estados de atualização confiáveis.
- Resultados não inventados.
- Testes documentados.
- Limitações explicitadas.

### Manutenção

- Componentes visuais reutilizáveis.
- Lógica separada da apresentação.
- Respeito à arquitetura existente.
- Alterações documentadas.
- Projeto compilável no ambiente de destino, conforme os recursos disponíveis.

Não declare a conclusão se critérios essenciais permanecerem sem verificação. Identifique o que foi concluído, testado e o que permanece pendente.

## 32. DOCUMENTAÇÃO E ENTREGA

Ao finalizar, produza relatório técnico contendo:

1. Resumo da interface anterior.
2. Mudanças implementadas.
3. Componentes visuais criados.
4. Janelas implementadas.
5. Comandos reorganizados.
6. Funcionalidades integradas.
7. Funcionalidades ainda indisponíveis.
8. Arquivos criados.
9. Arquivos modificados.
10. Alterações arquiteturais.
11. Testes executados.
12. Resultados dos testes.
13. Limitações encontradas.
14. Problemas conhecidos.
15. Recomendações futuras.

Inclua imagens da interface real se possível. Não substitua capturas reais por imagens conceituais sem identificá-las como tais.

Se a interface não puder ser executada no Revit durante esta etapa, registre a limitação e diferencie resultados de compilação de validação visual.

Atualize a documentação existente, evitando documentos redundantes sem necessidade.

## 33. INSTRUÇÃO FINAL

Sua missão é transformar a interface do SinalizaBIM em uma experiência profissional, sofisticada e intuitiva, adequada ao trabalho diário de engenharia de infraestrutura e planejamento urbano dentro do Revit 2027.

A estética não pode comprometer precisão, confiabilidade, desempenho ou manutenção. Priorize clareza visual, organização dos comandos, facilidade de uso e integração real.

Não se limite a descrever o que deveria ser feito. Inspecione o projeto, implemente as melhorias, execute as verificações disponíveis e documente os resultados.

Não substitua a arquitetura existente sem justificativa. Não simule funcionalidades. Não invente resultados. Não declare testes que não foram executados. Não peça autorização para decisões rotineiras.

Trabalhe até atingir o melhor resultado que o estado real do projeto permitir, mantendo o SinalizaBIM funcional durante o processo.

Resultado desejado: uma interface integrada ao Revit 2027 que combine a familiaridade do ambiente BIM, a clareza visual de ferramentas modernas de infraestrutura e a praticidade necessária para desenvolver projetos complexos de vias, terraplenagem, pavimentação, drenagem e urbanismo.
