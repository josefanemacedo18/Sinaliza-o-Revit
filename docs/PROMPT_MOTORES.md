# Prompt Complementar Mestre de Motores – SinalizaBIM para o Revit 2027

Transcrição do documento `Prompt_Complementar_Mestre_Motores_SinalizaBIM_Revit_2027.docx` enviado pelo usuário em 2026-10-26. Ele é executado junto com o Prompt Master
pelo plano único `docs/PLANO_DE_ACAO.md` (DECISIONS D-022). O texto está como foi enviado; só a formatação virou Markdown.
A presença de um requisito aqui **não** significa que ele esteja implementado: o estado real está na AUDIT §8–§9.

SINALIZABIM

PROMPT COMPLEMENTAR MESTRE

Motores Integrados de Engenharia, Infraestrutura e Urbanismo

Ambiente-alvo: Autodesk Revit 2027

## 1. Missão principal

Este prompt complementa o Prompt Master e o Prompt de Interface do SinalizaBIM. Oriente a evolução do add-in para uma plataforma integrada de engenharia, infraestrutura e urbanismo no Autodesk Revit 2027, com motores robustos e fluxos claros. A experiência deve buscar, em intenção e fluidez, a clareza de concepção do InfraWorks e a organização urbana de Cities: Skylines, sem copiar produtos, interfaces ou propriedade intelectual.

Este documento substitui os sete prompts especializados anteriormente planejados. Use-o junto ao Prompt Master e ao Prompt de Interface, sem apagar nem contradizer suas instruções. Audite lacunas reais, implemente ferramentas úteis ausentes e conecte os motores por dados e dependências consistentes.

## 2. Princípios obrigatórios

- Auditar repositório, solução, projetos, classes, serviços, testes e interface antes de alterar qualquer coisa.
- Não presumir que uma funcionalidade existe ou funciona apenas porque há um botão, classe ou nome sugestivo.
- Preservar e reaproveitar código funcional; evitar reescritas amplas, duplicação de serviços e regressões.
- Implementar funcionalidade real com validação, tratamento de erros, feedback e testes proporcionais ao risco.
- Registrar decisões técnicas importantes, limitações e dependências; trabalhar com autonomia nas decisões rotineiras.
- Distinguir build, testes unitários, testes geométricos, testes de integração e validação efetiva dentro do Revit.
- Não inventar resultados de testes, compatibilidade, dados normativos ou evidências.

## 3. Visão do produto

O SinalizaBIM deve permitir conceber, analisar, ajustar, materializar e documentar projetos de infraestrutura e urbanismo com continuidade entre disciplinas. O usuário precisa compreender o que cada ferramenta faz, quais dados exige, quais resultados produzirá e quais verificações continuam pendentes.

- Organizar ferramentas por tarefas e fluxos de trabalho, não apenas como uma lista de comandos.
- Oferecer pré-visualização antes de criar ou alterar elementos, quando tecnicamente viável.
- Usar parâmetros coerentes, unidades explícitas, padrões reutilizáveis e mensagens de erro acionáveis.
- Manter rastreabilidade entre geometria, elementos BIM, quantitativos e relatórios.
- Atualizar dependências sem duplicar elementos nem apagar edições manuais.

## 4. Arquitetura global dos motores

- Núcleo comum: unidades, coordenadas, tolerâncias, geometria, validação, diagnósticos, logging, configuração, persistência e identificação de objetos.
- Motores especializados: topografia/superfícies, infraestrutura viária, corredores, terraplenagem, pavimentação, drenagem, urbanismo/parcelamento e elementos urbanos.
- Adaptadores BIM para leitura, criação, atualização e associação com elementos do Revit, com transações e recuperação segura de falhas.
- Camada de interface para seleção contextual, parâmetros, pré-visualização, validação, execução, resumo e diagnósticos.
- Definir contratos explícitos entre motores: entradas, saídas, unidades, dependências, validade e comportamento diante de alterações.
- Antes de criar uma camada nova, verificar se já existe serviço equivalente que possa ser ampliado.

## 5. Inventário e expansão das ferramentas

Inventariar recursos prometidos, implementados, parciais, quebrados e ausentes. Comparar o inventário com tarefas reais de infraestrutura e urbanismo. Não adicionar ferramentas apenas para aumentar a lista: cada recurso deve resolver uma necessidade concreta e integrar-se ao sistema.

- P0: corrigir problemas que impedem uso confiável, causam perda de dados ou bloqueiam fluxos principais.
- P1: completar fluxos de trabalho e conexões fundamentais entre motores.
- P2: implementar ferramentas complementares, automações, relatórios e melhorias de produtividade.
- P3: desenvolver recursos avançados após estabilizar a base.
- Para cada lacuna, registrar justificativa, dependências, impacto, risco, testes e critérios de aceite.

## 6. Motor de topografia e superfícies

- Importar, interpretar e validar pontos, cotas, curvas de nível, linhas de quebra e dados topográficos disponíveis.
- Criar e editar superfícies existentes e projetadas, distinguindo terreno original de terreno proposto.
- Detectar dados inválidos ou duplicados, inconsistências de coordenadas, triângulos degenerados, descontinuidades e áreas sem suporte.
- Permitir selecionar, editar, recortar, comparar e visualizar superfícies.
- Usar Toposolid e recursos nativos do Revit quando apropriado, sem depender deles para todos os cálculos internos.
- Explicitar tolerâncias, unidades, origem e sistema de coordenadas.

## 7. Motor de infraestrutura viária

- Criar e editar eixos/alinhamentos horizontais, segmentos retos, curvas e transições quando suportadas.
- Gerenciar estaqueamento, estações e equações de estaqueamento.
- Criar perfis longitudinais do terreno e de projeto, rampas, curvas verticais e verificações configuráveis.
- Definir seções transversais parametrizadas: faixas, acostamentos, canteiros, passeios, taludes, sarjetas e outros componentes.
- Gerar corredores 3D a partir de alinhamento, perfil e seções, com regiões, transições e superfícies resultantes.
- Modelar interseções, entroncamentos e rotatórias com parâmetros explícitos e validação geométrica.
- Verificar continuidade, conflitos, inclinações, raios e critérios configurados; não alegar conformidade normativa sem regra e dados aplicáveis.
- Permitir revisar planta, perfil, seção e visualização tridimensional.

## 8. Motor de terraplenagem e plataformas

- Definir plataformas, limites de intervenção, cotas, declividades, taludes e transições.
- Comparar terreno existente e projetado e calcular volumes de corte e aterro com método documentado.
- Exibir mapas de corte/aterro, áreas de balanço, regiões críticas e resumo de volumes.
- Comparar alternativas e atualizar cálculos após mudanças nas superfícies ou vias.
- Explicitar tolerâncias, resolução, método de cálculo, hipóteses e limitações.
- Integrar superfícies resultantes a corredores viários e áreas urbanizadas sem criar cópias incoerentes.

## 9. Motor de pavimentação

- Definir estruturas de pavimento por camadas, espessuras, materiais e parâmetros configuráveis.
- Associar pavimentos a vias, estacionamentos e outras áreas pavimentadas.
- Gerar geometria ou elementos BIM conforme estratégia de materialização aprovada.
- Calcular áreas, volumes e quantitativos por camada, com unidades e rastreabilidade.
- Validar espessuras ausentes, regiões sobrepostas, limites inconsistentes e conflitos geométricos.
- Permitir reutilizar composições típicas e personalizar cada projeto.

## 10. Motor de drenagem pluvial

- Modelar redes com nós, trechos, caixas, bocas de lobo, poços de visita, descidas, saídas e dispositivos pertinentes.
- Verificar conectividade, direção do fluxo, cotas de entrada/saída, declividades, descontinuidades e trechos desconectados.
- Organizar áreas contribuintes e parâmetros de chuva, coeficientes e critérios adotados.
- Implementar métodos hidrológicos e hidráulicos adequados, como método racional quando aplicável, deixando explícitas as hipóteses.
- Não inventar dados de chuva, coeficientes, dimensões mínimas ou normas. Permitir configurar parâmetros e identificar dados pendentes.
- Detectar conflitos com terreno, vias, pavimentos e redes quando houver dados suficientes.
- Gerar perfis, diagramas, listas de dispositivos, quantitativos e relatórios.

## 11. Motor de urbanismo e parcelamento

- Criar e editar perímetros de glebas, áreas de intervenção e limites de parcelamento.
- Gerar quadras, lotes, áreas públicas, áreas verdes e sistema viário conforme regras configuráveis.
- Calcular áreas, testadas, perímetros, dimensões e indicadores urbanísticos relevantes.
- Permitir parâmetros para frente e área mínimas, recuos, percentuais e demais regras fornecidas para o projeto.
- Identificar lotes inválidos, sobreposições, lacunas, polígonos autointersectados e conflitos.
- Não tratar regras municipais ou zoneamentos específicos como universais.
- Integrar parcelamento com vias, passeios, drenagem, topografia e quantitativos.

## 12. Elementos urbanos e viários complementares

Auditar primeiro ferramentas existentes e recursos nativos do Revit. Implementar somente o que estiver ausente ou claramente insuficiente, evitando duplicações.

- Guias, meios-fios, sarjetas, passeios, rampas de acessibilidade e travessias.
- Ciclovias, ciclofaixas, baias, estacionamentos e pontos de parada.
- Sinalização horizontal e vertical, placas, marcações de solo e dispositivos de segurança viária.
- Postes, luminárias e pontos de iluminação pública quando houver escopo e dados definidos.
- Árvores, canteiros, áreas verdes, mobiliário urbano, bancos, lixeiras, abrigos e elementos de orientação.
- Bocas de lobo, canaletas, valetas, dissipadores, caixas e dispositivos de drenagem superficial.
- Famílias e bibliotecas reutilizáveis, com parâmetros para padronizar elementos.
- Posicionamento em lote, espaçamento, alinhamento, distribuição, orientação e substituição controlada.

## 13. UX e fluxos de trabalho

- Organizar ferramentas por tarefas: levantar dados, projetar, validar, materializar, quantificar e documentar.
- Guiar o usuário por etapas claras sem esconder parâmetros críticos nem exigir conhecimento da arquitetura interna.
- Usar valores padrão seguros, unidades explícitas, descrições curtas e validação imediata.
- Oferecer pré-visualização e resumo do que será criado, atualizado ou removido antes de operações relevantes.
- Exibir estados claros: pronto, processando, concluído, concluído com avisos, falhou ou requer dados.
- Permitir cancelar operações longas quando seguro e evitar congelamentos desnecessários.
- Manter consistência visual, navegação intuitiva, acessibilidade e comportamento coerente.

## 14. Integração associativa entre motores

- Terreno → alinhamento viário → perfil → corredor → superfície projetada → terraplenagem.
- Corredor e seção → estrutura de pavimento → quantitativos de camadas.
- Topografia e vias → áreas contribuintes e cotas → drenagem → verificações e quantitativos.
- Gleba → sistema viário → quadras → lotes → áreas públicas e indicadores.
- Alteração de entrada → identificar dependências afetadas → invalidar seletivamente → recalcular → atualizar BIM.
- Evitar recálculo integral quando atualização incremental for segura.
- Preservar edições manuais e comunicar conflitos quando não for possível conciliá-las automaticamente.

## 15. Materialização BIM e integridade do modelo

- Associar objetos gerados a identificadores persistentes e metadados de origem e dependências.
- Atualizar objetos existentes em vez de duplicá-los a cada execução.
- Definir comportamento para objetos editados manualmente, apagados ou alterados fora do motor.
- Agrupar operações em transações do Revit e executar rollback quando uma etapa crítica falhar.
- Validar pré-condições e resumir criações, atualizações, avisos e falhas.
- Respeitar regras de thread, API e ciclo de vida do Revit.
- Não apagar ou substituir elementos alheios ao plugin sem autorização explícita e confirmação adequada.

## 16. Validação técnica, diagnósticos e limites

- Validar geometria, unidades, coordenadas, conectividade, tolerâncias, limites e consistência dos parâmetros.
- Separar erro impeditivo, aviso, informação e condição que exige revisão profissional.
- Oferecer diagnóstico acionável com elemento, causa provável, impacto e possível correção.
- Criar testes para casos típicos, limites, dados inválidos e situações degeneradas.
- Não apresentar resultados como certificação, aprovação legal ou garantia de conformidade normativa.
- Registrar regras verificadas, parâmetros utilizados e verificações que não puderam ser executadas.

## 17. Quantitativos e relatórios rastreáveis

- Produzir quantitativos por categoria, região, etapa, material, camada, trecho, lote ou agrupamento relevante.
- Vincular cada número à geometria, parâmetros, método de cálculo e unidade de origem.
- Permitir conferir totais, subtotais, itens sem classificação e discrepâncias.
- Gerar relatórios de parâmetros, resultados, avisos, erros e pressupostos.
- Usar formatos de exportação compatíveis com o fluxo existente e a necessidade real.
- Evitar arredondamento interno prematuro; separar precisão de cálculo da precisão de apresentação.

## 18. Desempenho e escalabilidade

- Evitar recomputar toda a solução quando apenas uma parte mudou.
- Usar estruturas de dados e algoritmos adequados ao volume de pontos, vértices, trechos e elementos.
- Separar cálculos puros da API do Revit quando isso melhorar testabilidade e desempenho.
- Medir gargalos antes de otimizar e registrar tempos representativos.
- Fornecer feedback em operações demoradas e impedir atualizações repetidas desnecessárias.
- Invalidar corretamente caches e resultados derivados quando entradas mudarem.

## 19. Plano de execução contínua

Trabalhar em ciclos pequenos, verificáveis e integrados. Não encerrar o trabalho apenas porque a solução compila.

- Fase 1 — auditoria do repositório, mapa de arquitetura, inventário de ferramentas e riscos.
- Fase 2 — consolidar núcleo comum: unidades, coordenadas, geometria, validação, diagnósticos e IDs persistentes.
- Fase 3 — topografia e infraestrutura viária: alinhamentos, estaqueamento, perfis, seções e corredores.
- Fase 4 — superfícies projetadas, terraplenagem, plataformas e pavimentação.
- Fase 5 — drenagem pluvial, conectividade, verificações e quantitativos.
- Fase 6 — urbanismo, parcelamento e elementos urbanos/viários complementares.
- Fase 7 — integração entre motores, atualização associativa, regressão, desempenho e validação no Revit.
- Ajustar a ordem conforme dependências e riscos reais, registrando a justificativa.
- Implementar e testar integrações assim que os componentes necessários existirem.
- Manter registro persistente de progresso, decisões, problemas, testes e próximos passos.

## 20. Testes e critérios de aceitação

- Entradas válidas geram resultados esperados e reprodutíveis.
- Entradas inválidas produzem mensagens úteis sem corromper o modelo.
- Unidades, tolerâncias e coordenadas são tratadas consistentemente.
- Reexecutar uma operação não cria duplicatas nem degrada o modelo.
- Alterar uma entrada atualiza dependências e preserva dados não afetados.
- Falhas durante a materialização revertem alterações incompletas quando possível.
- Quantitativos são rastreáveis até os dados e a geometria de origem.
- Build, testes automatizados e testes manuais no Revit são registrados separadamente.
- Testes não executados ou dependentes de ambiente devem ser identificados, nunca marcados como aprovados.

## 21. Documentação e entrega

Manter um documento de estado técnico, preferencialmente PLANO_E_ESTADO_DOS_MOTORES_SINALIZABIM.md, ou equivalente já adotado no repositório.

- Registrar arquitetura e responsabilidades de cada motor.
- Manter inventário de recursos implementados, parciais, ausentes e descontinuados.
- Documentar dependências entre motores e contratos de dados.
- Registrar decisões, limitações, riscos e itens pendentes.
- Listar comandos executados, builds, testes e validações realizados.
- Guardar evidências concretas e instruções para reproduzir verificações.
- Priorizar próximas tarefas com critérios de aceite.
- Atualizar a documentação junto às alterações relevantes; não prometer recursos inexistentes.

## 22. Instrução final ao agente de desenvolvimento

Leia integralmente o Prompt Master, este Prompt Complementar Mestre de Motores e o Prompt de Interface. Trate-os como documentos complementares: o Master define princípios gerais e escopo central; este documento detalha motores, integrações e expansão funcional; o documento de Interface orienta experiência visual e interação.

Comece auditando o repositório real. Atualize o inventário técnico e implemente a próxima unidade prioritária com alterações focadas, testes e documentação. Trabalhe de forma autônoma e iterativa, sem exigir aprovação para cada decisão rotineira; sinalize bloqueios reais, riscos irreversíveis e decisões dependentes de requisitos do usuário.

O objetivo é um SinalizaBIM robusto, integrado, intuitivo e tecnicamente rastreável. Priorize qualidade geométrica, integridade BIM, clareza operacional e evidências verificáveis. Nunca confunda interface pronta com motor funcional, nem compilação bem-sucedida com validação completa.
