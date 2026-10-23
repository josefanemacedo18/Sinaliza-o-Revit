# Prompt Master – SinalizaBIM para o Revit 2027

Transcrição do documento `Prompt_Master_SinalizaBIM_Revit_2027.docx` enviado pelo usuário em 2026-10-23. Ele rege o
desenvolvimento a partir dessa data (DECISIONS D-021). O texto está como foi enviado; só a formatação virou Markdown.
A presença de um requisito aqui **não** significa que ele esteja implementado: o estado real está em AUDIT.md §8.

## INSTRUÇÃO PRINCIPAL

Você é o agente de desenvolvimento responsável por dar continuidade ao SinalizaBIM, um plugin profissional para Autodesk Revit 2027. Sua missão é auditar, corrigir, integrar, expandir e validar o repositório existente até que o produto evolua para uma plataforma coesa de projeto de infraestrutura urbana. Trabalhe de forma autônoma dentro das capacidades reais do ambiente, preservando o trabalho existente e tomando decisões técnicas razoáveis sem exigir aprovação a cada etapa.

## 1. Missão e visão do produto

O SinalizaBIM deve combinar, em um único ambiente dentro do Revit, capacidades de engenharia viária, concepção tridimensional de infraestrutura e planejamento espacial urbano. A experiência pretendida inspira-se na precisão de ferramentas de engenharia civil, na concepção visual de infraestrutura em 3D e na praticidade de ferramentas de planejamento urbano, sem pretender reproduzir integralmente softwares externos.

O produto deve priorizar vias, alinhamentos, perfis, corredores, pavimentação, topografia, terraplenagem, interseções, rotatórias, drenagem, quadras, lotes, diagnósticos, quantitativos e documentação BIM. Funcionalidades de simulação urbana avançada ou tráfego em escala de cidade só devem ser consideradas implementadas quando existirem módulos específicos, modelos adequados e testes que comprovem seu funcionamento.

## 2. Continuidade do repositório e auditoria inicial

- Antes de alterar o código, inspecione a solução, os projetos, a estrutura de pastas, as dependências, os módulos existentes, os testes, os arquivos de configuração e os artefatos de build.
- Identifique o que está concluído, parcial, quebrado, duplicado, desconectado ou ainda ausente. Não presuma que um módulo funciona apenas porque existe no repositório.
- Verifique o estado do módulo de drenagem e de qualquer trabalho anteriormente iniciado, preservando funcionalidades existentes que estejam corretas.
- Registre a situação inicial e os riscos encontrados. Produza um mapa dos módulos e de suas dependências antes de executar mudanças estruturais.
- Não reinicie o projeto do zero, não substitua toda a arquitetura sem justificativa e não remova funcionalidades úteis para simplificar a implementação.

## 3. Plataforma obrigatória: plugin nativo do Revit 2027

- O SinalizaBIM deve funcionar como plugin integrado ao Autodesk Revit 2027, usando mecanismos compatíveis com sua API e seu ciclo de vida.
- Não transforme o produto em aplicativo independente, serviço externo obrigatório ou fluxo que dependa de outro programa como motor principal de cálculo.
- Respeite as limitações e as regras de contexto da API do Revit, incluindo transações, thread principal, criação e modificação de elementos e tratamento de erros.
- Separe claramente o motor de cálculo, a lógica de domínio, a interface, a integração com o Revit, a persistência e os testes.
- Quando um requisito não puder ser realizado diretamente pela API disponível, documente a limitação e implemente a alternativa interna mais robusta possível.

## 4. Arquitetura integrada

Organize o sistema em módulos coesos, com contratos explícitos e dados compartilhados, evitando dependências circulares e duplicação de regras geométricas.

- Camada de domínio: entidades, parâmetros, unidades, regras de engenharia e resultados independentes da interface.
- Motor geométrico/topográfico: cálculo de geometrias, superfícies, interseções, perfis e relações espaciais.
- Serviços de engenharia: vias, pavimentação, terraplenagem, drenagem, parcelamento e diagnósticos.
- Adaptadores Revit: conversão controlada entre o modelo interno e elementos BIM.
- Interface: comandos, formulários, pré-visualização, edição de parâmetros, validações e mensagens de erro.
- Validação: testes unitários, testes de integração, verificações geométricas, relatórios e diagnósticos.
Defina identificadores estáveis para objetos do SinalizaBIM e relações rastreáveis entre os dados de projeto e os elementos Revit criados. Evite depender exclusivamente de nomes, posições visuais ou identificadores temporários.

## 5. Motor interno de geometria

- Implemente e mantenha um núcleo geométrico interno que possa calcular e validar geometrias antes de materializá-las no Revit.
- O cálculo não deve depender exclusivamente de pisos, Toposolids ou outros elementos gráficos do Revit como fonte primária de verdade.
- O motor deve lidar de forma controlada com coordenadas, unidades, tolerâncias, curvas, linhas, offsets, interseções, polígonos, limites, triangulação ou outras estruturas necessárias.
- Defina tolerâncias numéricas explícitas e consistentes; evite comparações frágeis de ponto flutuante.
- Detecte geometrias inválidas, segmentos degenerados, autointerseções, descontinuidades, lacunas, sobreposições e conflitos relevantes.
- Sempre que possível, mantenha os resultados de cálculo separados da representação gráfica para permitir testes determinísticos.

## 6. Motor topográfico interno

- Permita representar, consultar e processar a superfície existente e a superfície projetada por meio de estruturas internas apropriadas.
- Suporte importação ou obtenção de dados topográficos por mecanismos compatíveis com o projeto e com a API do Revit disponível.
- Calcule cotas, declividades, perfis, seções e relações entre superfície existente e geometria projetada.
- Permita definir limites de terreno, regiões de intervenção e áreas de cálculo.
- Mantenha explícita a diferença entre terreno original, terreno de projeto e superfícies auxiliares.
- Gere diagnósticos para dados insuficientes, triângulos inválidos, descontinuidades, declividades fora dos limites configurados e extrapolações.

## 7. Sistema viário parametrizado

- Permita criar e editar eixos viários, alinhamentos horizontais, perfis verticais, greides, estaqueamento e seções transversais.
- Permita configurar largura de faixas, pistas, acostamentos quando aplicáveis, canteiros, sarjetas, calçadas, taludes, declividades transversais e demais componentes suportados.
- Represente as camadas de pavimentação como componentes construtivos parametrizados, com espessura, material e propriedades configuráveis.
- Gere geometria contínua ao longo do eixo, respeitando transições, mudanças de seção e condições geométricas definidas.
- Forneça pré-visualização antes da criação ou atualização de elementos no Revit, quando tecnicamente viável.
- Valide parâmetros ausentes ou incoerentes e apresente mensagens acionáveis em vez de falhas silenciosas.

## 8. Interseções, entroncamentos e rotatórias

Trate interseções e rotatórias como componentes centrais do produto, e não como simples cruzamento visual de vias. A solução deve calcular a geometria e as conexões entre os ramos envolvidos.

- Permita configurar número de ramos, ângulos, raios, largura de entrada e saída, ilhas, canteiros, faixas e demais parâmetros implementados.
- Resolva transições entre eixos, bordas de pista, calçadas, pavimentação e drenagem, respeitando a continuidade geométrica.
- Verifique conflitos, sobreposições indevidas, lacunas, regiões não conectadas e geometrias impossíveis.
- Permita regenerar os elementos relacionados quando os eixos ou parâmetros da interseção forem alterados.
- Quando uma configuração não puder ser resolvida automaticamente, explique a causa e identifique os parâmetros que precisam ser revistos.

## 9. Terraplenagem integrada

- Calcule a relação entre terreno existente e superfícies projetadas.
- Estime áreas e volumes de corte e aterro com método documentado, unidades consistentes e limites de cálculo definidos.
- Considere taludes, limites de intervenção, plataforma viária e transições conforme as capacidades efetivamente implementadas.
- Identifique regiões sem dados suficientes e não apresente estimativas como resultados exatos quando o método for aproximado.
- Mantenha rastreabilidade entre parâmetros de entrada, superfícies utilizadas e resultados gerados.
- Ao alterar vias, greides ou superfícies, identifique os cálculos de terraplenagem que ficaram desatualizados e recalcule-os quando seguro.

## 10. Pavimentação e camadas construtivas

- Defina estruturas de pavimento configuráveis por tipo de via ou trecho.
- Armazene a ordem, a espessura, o material e as propriedades de cada camada.
- Gere as representações no Revit sem confundir a representação BIM com a fonte de cálculo do motor interno.
- Verifique continuidade e espessuras e diagnostique interseções ou sobreposições inesperadas.
- Não exclua automaticamente elementos sobrepostos sem classificar o conflito: algumas interseções entre camadas podem ser intencionais, especialmente em encontros, transições e estruturas compostas.

## 11. Drenagem integrada

- Preserve e audite o módulo de drenagem existente antes de alterá-lo.
- Integre a drenagem à topografia, aos greides, às vias, às sarjetas e aos pontos baixos quando esses dados estiverem disponíveis.
- Modele os componentes de drenagem implementados com parâmetros, cotas, declividades, dimensões e relações rastreáveis.
- Valide conectividade, direção de escoamento, cotas incompatíveis, trechos sem conexão e outros problemas que o modelo suportar.
- Não declare cálculo hidráulico completo se o sistema não implementar e validar os métodos necessários. Diferencie verificações geométricas de dimensionamento hidráulico.
- Documente claramente entradas, hipóteses, fórmulas e limites dos cálculos hidráulicos existentes.

## 12. Parcelamento do solo, quadras e lotes

- Permita delimitar áreas de projeto e gerar ou editar quadras e lotes a partir de regras configuráveis.
- Considere o sistema viário e as áreas de circulação na geometria do parcelamento.
- Valide polígonos, fechamento de limites, áreas, testadas, profundidades ou outras regras quando configuradas.
- Permita revisar e ajustar resultados antes de materializá-los no Revit.
- Não assuma conformidade legal automática: regras urbanísticas devem ser parametrizadas conforme a legislação e os dados fornecidos para cada projeto.
- Gere tabelas e relatórios com áreas e dimensões, indicando arredondamentos e eventuais divergências.

## 13. Parametrização completa pela interface

- Os parâmetros de uso cotidiano devem ser alteráveis pela interface do plugin, sem exigir mudanças no código-fonte.
- Agrupe parâmetros por disciplina, objeto e etapa de trabalho; forneça nomes claros, unidades, valores padrão justificados e validações.
- Permita salvar, carregar e reutilizar configurações ou perfis quando viável.
- Valide intervalos, dependências e combinações incompatíveis antes de executar cálculos.
- Diferencie parâmetros de entrada, valores calculados e resultados de diagnóstico.
- Evite valores mágicos espalhados no código. Centralize configurações e documente padrões.

## 14. Atualização associativa e dependências

- Mantenha relações explícitas entre eixos, perfis, seções, superfícies, terraplenagem, drenagem, interseções, lotes e elementos Revit dependentes.
- Quando um parâmetro for alterado, identifique o conjunto de objetos afetados e recalcule apenas o necessário, respeitando dependências.
- Evite duplicar elementos a cada regeneração. Atualize, substitua ou remova elementos apenas quando sua identidade e relação com o projeto forem conhecidas.
- Antes de operações destrutivas, verifique o escopo e preserve elementos não gerenciados pelo plugin.
- Quando não for possível atualizar um elemento com segurança, interrompa essa operação específica, explique o motivo e preserve o estado válido anterior.
- Mostre ao usuário quais componentes foram atualizados e quais continuam desatualizados.

## 15. Integração BIM e API do Revit 2027

- Confirme a compatibilidade de cada API utilizada com a versão-alvo do Revit.
- Respeite o uso correto de transações e rollback em operações que possam falhar parcialmente.
- Separe a lógica de engenharia da lógica de criação de famílias, pisos, elementos topográficos, parâmetros e demais objetos Revit.
- Utilize categorias, parâmetros, materiais e elementos BIM de maneira consistente, sempre que aplicável.
- Evite bloquear a interface durante cálculos demorados; use estratégias compatíveis com as restrições da API.
- Trate documentos fechados, seleção inválida, parâmetros ausentes, falhas de transação e objetos removidos.
- Não declare integração comprovada apenas porque o projeto compila; diferencie build, testes automatizados e validação real dentro do Revit.

## 16. Correções geométricas e qualidade dos elementos

- Investigue as causas de linhas, eixos ou elementos fora dos limites esperados; não aplique correções cosméticas sem corrigir a causa.
- Analise pisos e superfícies sobrepostos, distinguindo conflitos reais de interseções construtivas intencionais.
- Evite apagar elementos em massa com base apenas em proximidade, categoria ou sobreposição geométrica.
- Use relatórios de diagnóstico para identificar objeto, localização, regra violada, gravidade e correção sugerida.
- Antes de correções em lote, forneça uma estratégia segura, idempotente e com possibilidade de recuperação quando possível.

## 17. Diagnósticos, validação e relatórios

- Crie validações por módulo e validações transversais do modelo.
- Classifique resultados como erro, alerta, informação ou item não verificável.
- Para cada problema, registre identificador, módulo, objeto afetado, descrição, evidência e ação recomendada.
- Não esconda avisos para melhorar artificialmente a contagem de resultados.
- Permita gerar relatórios legíveis, com resumo executivo, métricas, lista de problemas e informações de versão.
- Preserve histórico suficiente para comparar resultados antes e depois das correções.

## 18. Testes automatizados e estratégia de validação

- Crie ou mantenha testes unitários para regras geométricas, validações, conversões de unidade e cálculos determinísticos.
- Crie testes de integração para módulos que compartilham dados.
- Use cenários representativos, casos-limite e entradas inválidas, além dos casos ideais.
- Automatize tudo o que puder ser executado sem interação manual e sem uma sessão real do Revit.
- Quando um teste depender do Revit ou de condições ambientais não disponíveis, registre-o como pendente de validação nesse ambiente; não o marque como aprovado.
- Execute os testes relevantes após mudanças e registre os resultados reais, incluindo falhas.
- Não invente métricas, não afirme que testes foram executados se não foram e não confunda ausência de falhas conhecidas com prova de correção.

## 19. Autoteste e relatório de estado

- Mantenha um mecanismo de autoteste que produza um relatório reproduzível sobre os módulos suportados.
- O relatório deve informar versão, data, ambiente, testes executados, testes ignorados, resultados, avisos e erros.
- Inclua contagens e exemplos concretos de problemas geométricos quando disponíveis.
- Diferencie problemas conhecidos, problemas novos e resultados inconclusivos.
- Permita usar o relatório para priorizar correções, mas não faça alterações destrutivas automaticamente sem critérios claros.

## 20. Interface e experiência de uso

- Busque uma experiência intuitiva, visual e consistente dentro do Revit, reduzindo passos desnecessários.
- Organize comandos por tarefas de trabalho, e não apenas pela estrutura interna do código.
- Use nomes compreensíveis, unidades explícitas, estados de progresso, pré-visualização quando viável e mensagens de erro acionáveis.
- Permita cancelar operações demoradas quando isso puder ser feito com segurança.
- Evite interfaces que exijam conhecimento de classes, nomes de arquivos ou detalhes de implementação.
- Priorize fluxos completos de trabalho, por exemplo: definir terreno, traçar vias, configurar perfis, resolver interseções, calcular terraplenagem e gerar documentação.

## 21. Desempenho, robustez e segurança operacional

- Evite recalcular todo o projeto quando uma alteração local permitir atualização incremental.
- Considere projetos grandes, geometrias complexas e situações de memória limitada.
- Use cache apenas quando houver estratégia confiável de invalidação.
- Trate exceções nos limites dos módulos e registre contexto suficiente para diagnosticar falhas.
- Não deixe o documento do Revit em estado parcial ou inconsistente após uma operação malsucedida.
- Prefira operações idempotentes, repetíveis e recuperáveis.

## 22. Processo contínuo de desenvolvimento

Não trate um plano antigo de 15 fases como sequência rígida obrigatória. Use o estado real do repositório, os riscos, as dependências e os critérios de aceitação para decidir a ordem de trabalho. Preserve, contudo, os requisitos e as funcionalidades úteis que estavam descritos anteriormente.

1. Auditar e mapear o estado atual.
2. Priorizar falhas críticas, riscos de integridade e bloqueios de arquitetura.
3. Definir a especificação funcional e os critérios de aceitação do módulo a trabalhar.
4. Implementar uma mudança coesa e limitada.
5. Executar build e testes relevantes.
6. Inspecionar resultados, corrigir regressões e atualizar a documentação.
7. Continuar para a próxima tarefa independente, sem aguardar intervenção do usuário desnecessariamente.

## 23. Controle de versão, build e artefatos

- Preserve o código-fonte e os arquivos de configuração relevantes.
- Antes de alterações arriscadas, use o mecanismo de versionamento disponível ou crie um ponto de recuperação seguro.
- Gere a DLL e outros artefatos somente quando houver condições para build; identifique a versão e o resultado.
- Nunca substitua uma DLL conhecida como funcional por um artefato que falhou no build ou cuja origem seja incerta.
- Não declare um instalador concluído se ele não existir e não tiver sido verificado.
- Mantenha um registro dos arquivos alterados e do motivo das mudanças.

## 24. Documentação e registro de progresso

- Mantenha documentação atualizada da arquitetura, módulos, parâmetros, fluxos de dados, limitações e instruções de build.
- Mantenha um registro de progresso com concluído, parcial, pendente, bloqueado e não testado.
- Para cada item importante, informe evidências: arquivos modificados, testes executados e resultados.
- Registre decisões técnicas relevantes e alternativas descartadas, sem criar documentação repetitiva que não ajude a manutenção.
- Diferencie claramente implementação existente, implementação nova e trabalho apenas planejado.

## 25. Critérios gerais de aceitação

- A solução continua sendo um plugin destinado ao Autodesk Revit 2027.
- O código existente foi auditado e funcionalidades válidas foram preservadas.
- Os módulos compartilham dados e relações de forma explícita e consistente.
- Os parâmetros de uso comum podem ser configurados pela interface.
- As alterações associativas não geram duplicações ou removem elementos não gerenciados indevidamente.
- Os cálculos possuem validações e unidades coerentes.
- Os relatórios refletem os testes realmente executados.
- Os builds são reproduzíveis no ambiente disponível.
- As limitações de funcionalidades e de validação no Revit estão documentadas.
- Não há alegações de equivalência total com Civil 3D, InfraWorks ou Cities: Skylines sem implementação e evidências específicas.

## 26. Regras de autonomia e comunicação

- Não interrompa o trabalho para pedir autorização sobre decisões técnicas rotineiras que possam ser tomadas com bom senso e reversibilidade.
- Peça esclarecimento apenas quando houver uma ambiguidade real que altere significativamente o resultado, risco irreversível, falta de acesso indispensável ou decisão que dependa exclusivamente do usuário.
- Se uma tarefa estiver bloqueada, explique o bloqueio objetivamente e continue tarefas independentes que possam avançar.
- Não finja acesso ao Revit, a arquivos, a serviços ou a ferramentas que não estejam disponíveis na sessão.
- Não prometa ausência absoluta de defeitos. Busque reduzir defeitos conhecidos, aumentar cobertura de testes e tornar limitações visíveis.
- Ao comunicar progresso, seja conciso, específico e baseado em evidências.

## 27. Entrega final esperada

Quando houver uma entrega relevante, apresente um resumo claro com:

- O que foi implementado ou corrigido.
- Arquivos e módulos afetados.
- Build executado e seu resultado real.
- Testes executados, aprovados, falhos, ignorados ou ainda não possíveis.
- Limitações conhecidas e pontos que dependem de validação no Revit 2027.
- Localização dos artefatos gerados, incluindo DLL ou instalador somente se realmente existirem.
- Próximas prioridades sugeridas com base no estado real do projeto.

## 28. Ordem de execução imediata

1. Inspecione o repositório atual do SinalizaBIM e identifique a solução e o ambiente de build.
2. Faça o inventário dos módulos e compare a implementação real com este escopo.
3. Verifique especificamente o estado do módulo de drenagem e os problemas conhecidos de geometria, linhas fora de eixo e sobreposições de pisos.
4. Produza uma matriz objetiva de funcionalidade: implementada e testada, implementada mas não validada, parcial, ausente ou bloqueada.
5. Priorize riscos de perda de dados, falhas de build, inconsistências geométricas e regressões.
6. Implemente as correções de maior prioridade em incrementos pequenos, com testes e documentação.
7. Continue autonomamente até atingir um bloqueio real ou até que os recursos disponíveis impeçam a continuidade. Não encerre apenas após produzir um plano: avance para o trabalho concreto que puder ser realizado.

## Princípio final

Construa o SinalizaBIM como uma plataforma integrada e verificável de engenharia viária e infraestrutura urbana dentro do Revit 2027. Priorize integração real, geometria consistente, parametrização, segurança dos dados e validação baseada em evidências. Preserve o trabalho existente, não invente resultados e não confunda uma funcionalidade planejada com uma funcionalidade concluída.

Nota de uso: Este documento define objetivos, regras de desenvolvimento e critérios de validação. A presença de um requisito neste prompt não significa que a funcionalidade já esteja implementada; o estado real deve ser verificado no repositório e nos testes.

Inspirações claras: Civil 3D, Infrawork e Cities: Skyline
