# Plano de ação 2 – "InfraWorks dentro do Revit"

Plano do [prompt de 2026-10-27](PROMPT_INFRAWORKS.md) (DECISIONS D-032). Continua o [plano unificado](PLANO_DE_ACAO.md),
cujas Etapas 0–13 estão concluídas (2.8.0 a 2.19.0). As regras daquele plano valem aqui:
- nada inventado;
- quatro níveis de prova registrados separadamente, e o que depende do Revit fica NÃO VALIDADO NO REVIT;
- controles nos estados A/B/C;
- uma DLL por etapa que muda código, em `Releases/EtapaNN`, com o número do meio da versão subindo (D-022);
- documentos atualizados.

Referência do InfraWorks, com a fonte de cada ferramenta: [INFRAWORKS_REFERENCIA](INFRAWORKS_REFERENCIA.md).

## 1. Os pedidos e onde ficam

Situação: ✅ feito · 🔶 parcial (o limite fica dito) · ⬜ não iniciado. A coluna muda a cada entrega.

| Item | Pedido | Etapa | Situação |
|---|---|---|---|
| 3 | estudar o InfraWorks e trazer o que der | 14 (estudo) → 18–22 | ✅ estudo; o resto nas etapas |
| 18 | rampas, extensões e travessias demais em trechos curtos | 15 | ✅ 2.20.0 (regra do trecho mínimo; NÃO VALIDADO NO REVIT, K28) |
| 13 | terreno "comendo" a via nas rampas e interseções | 15 | ✅ 2.20.0 (plataforma sob as rampas; Autoteste confere rampas e nós; NÃO VALIDADO NO REVIT, K28) |
| 19 | chanfro de 1,50 m por padrão, personalizável | 16 | ✅ 2.21.0 (catetos sobre os limites, padrão do loteamento nas quadras novas; NÃO VALIDADO NO REVIT, K30) |
| 20 | piso tátil na esquina como a imagem 1 | 16 | ✅ 2.21.0 (paralela ao alinhamento, seguindo o chanfro, alerta nas quinas; ramais às entradas dos lotes: Etapa 23; NÃO VALIDADO NO REVIT, K30) |
| 12 | extensão de calçada e esquina como a imagem 2 | 16 | ✅ 2.21.0 (esquina da extensão em chanfro; rampas no eixo das travessias conferidas; NÃO VALIDADO NO REVIT, K30) |
| 2 | rampas como pisos | 17 | ✅ 2.22.0 (piso de camada variável por corpo, fundo plano; NÃO VALIDADO NO REVIT, K31) |
| 1 | materiais realistas no modo Realista | 17 | ✅ 2.22.0 (ativo da biblioteca do Revit pelo título, cor de Cores consistentes intacta; NÃO VALIDADO NO REVIT, K31) |
| 4 | estacas e PIVs editáveis, cota no terreno, corte e aterro na hora, vista da seção | 18 | ⬜ |
| 21 | gráficos de corte e aterro por estaca (e nas outras matérias) | 18 (terraplenagem), 20–22 (demais) | ⬜ |
| 5 | faixas e demais componentes por trecho, inclusive extensões | 19 | ⬜ |
| 17 | personalização da via, modelada ou não | 19 | ⬜ |
| 13 | ferramentas de terreno e topografia | 20 | ⬜ |
| 11 | rio, córrego, lago e lagoa parametrizados | 21 | ⬜ |
| 6 | drenagem completa: tubo, peças, conexões, vala | 22 | ⬜ |
| 7 | o que falta ao parcelamento e às outras matérias | 23 (e as lacunas da §2 nas etapas indicadas) | ⬜ |
| 16 | placas personalizadas sem limite | 24 | ⬜ |
| 15 | detalhamento profissional | 25 | ⬜ |
| 9, 10 | motores mais amplos e mais rápidos, sem perder qualidade | medido em toda etapa; 26 | ⬜ |

## 2. Lacunas por matéria (item 7)

Levantadas pela leitura do código e do MANUAL, e comparadas com o InfraWorks. O que já existe não é refeito.

| Matéria | Já existe | Lacuna | Etapa |
|---|---|---|---|
| Vias | seção montada (faixas, ciclofaixa, estacionamento, canteiros, calçada NBR 9050), largura variável, recuos, greide, estaqueamento com equações, janela da via em nove abas | componentes por trecho (início, fim, transição) para qualquer elemento; dividir e apagar um componente num trecho; taludamento por lado e por trecho; anotações automáticas dos componentes | 19, 25 |
| Interseções e calçadas | interseções I–IV, rotatórias, extensões de calçada, rampas, travessias, piso tátil ligado às rampas | geração automática sem regra de espaçamento nem noção de quadra (item 18); esquina como as imagens 1 e 2 | 15, 16 |
| Terraplenagem | malha e seções por estaca, por elemento, registro e V15, mapa de corte e aterro | visualizador de seção por estaca, edição de estaca e PIV com o resultado na hora, trecho de estacas, gráficos por estaca e diagrama de massas | 18 |
| Topografia | Toposolid moldado, terreno natural guardado, diagnóstico (V14), tela do terreno | temas (elevação, declividade, orientação), distância e declividade entre pontos e pelo caminho, plataformas, linhas de quebra, suavização, comparação de superfícies; terreno sobre os pisos nas rampas e nós | 15, 20 |
| Corpos d'água | — | rio, córrego, lago, lagoa | 21 |
| Drenagem | rede manual, método racional, Manning, sarjetas e bocas de lobo (Manual SUDERHSA), dimensionamento, V13, perfil, memorial, Ler do Modelo | catálogo de tubos (material, DN, classe, comprimento útil, junta), número de peças, conexões, vala (escavação, berço, reaterro, bota-fora), linha piezométrica, rede gerada pela via, bueiros | 22 |
| Pavimentação | estrutura em camadas, gerenciador, quantitativo por camada | rampas como pisos; aparência realista dos materiais | 17 |
| Parcelamento | gleba, quadras reconhecidas, lotes, categorias, mínimos com fonte, chanfro (pela face), quadros, memorial com confrontações, masterplan, V09, V10 e V16 | chanfro pelos catetos (1,50 m) como padrão; coordenadas e rumos ou azimutes dos vértices no memorial; faixas não edificáveis (ao longo de águas e vias) e faixa de domínio, com a lei citada e o valor informado; conferência da declividade do terreno na gleba com o limite da lei conferido | 16, 23 |
| Sinalização vertical | 168 placas do catálogo, suportes, placa personalizada | forma, dimensões, cores, textos, pictogramas e setas livres, com prévia e biblioteca | 24 |
| Detalhamento | anotação, cota de seção e perfil transversal, detalhe típico, quadros, legenda, notas, norte, folhas | seções transversais em série nas folhas, perfil longitudinal com grade de estacas e cotas, planta de estaqueamento, rótulos de terreno e greide, carimbo do projeto | 25 |
| Desempenho | terraplenagem linear (Etapa 13), progresso e cancelamento | K14 (piso tátil longo), K15 (varreduras do documento) | 26 |
| Redes de água, esgoto e iluminação | — | fora deste plano: dependem das normas da concessionária e do município, que o usuário não enviou; registradas para quando houver fonte | — |

## 3. Etapas

Cada etapa que muda código termina com:
- build limpo e suíte completa verde;
- uma DLL em `Releases/EtapaNN`;
- os documentos atualizados;
- um aviso ao usuário com o que foi feito, os testes, o caminho da DLL e o que conferir no Revit.

| Etapa | Itens | Conteúdo | Critério de aceite |
|---|---|---|---|
| **14 – Estudo do InfraWorks e auditoria** | 3, 7 | [INFRAWORKS_REFERENCIA](INFRAWORKS_REFERENCIA.md) com fonte em cada linha; lacunas por matéria (§2); este plano; D-032 | ✅ 2026-10-27 – só documentos (sem DLL) |
| **15 – Defeitos críticos** ✅ 2.20.0 | 18, 13 | **18:** rampas, travessias e extensões geradas com regra: no máximo uma por aproximação de esquina de quadra ou de interseção, duplicatas próximas fundidas, espaçamento e trecho mínimos configuráveis ("padrão do plugin"), lista prévia para desmarcar e modo manual. **13:** a causa do terreno acima da via nas rampas, calçadas, interseções e rotatórias; correção; a conferência do Autoteste passa a cobrir nós, rampas e calçadas (hoje `AutoTestChecks.CheckRoad` pula os nós e as obras) | regressão: trecho curto entre duas esquinas gera no máximo uma travessia e um par de rampas por aproximação; manual não gera nenhuma; cenas com rampa, cruz, T e rotatória sobre terreno inclinado sem terreno acima da superfície acabada além da tolerância |
| **16 – Esquinas** ✅ 2.21.0 | 19, 20, 12 | chanfro pelos catetos sobre os limites (imagem 4), 1,50 m por padrão nas quadras novas, global e por esquina, mantendo a medida pela face como opção; piso tátil da esquina pela imagem 1 (direcional paralelo ao alinhamento predial seguindo o chanfro, alerta nas mudanças de direção, ramais às rampas e entradas); esquina e extensão de calçada pela imagem 2 (rampas alinhadas às travessias) | testes do chanfro (áreas, catetos, esquinas não retas), do traçado tátil (alertas nos vértices, ramais) e das rampas alinhadas; projetos antigos abrem iguais |
| **17 – Pisos e materiais** ✅ 2.22.0 | 2, 1 | rampas na categoria Pisos (inclinação, abas, recortes), nos quantitativos por componente e na conferência piso × terreno; materiais do plugin com ativo de aparência da biblioteca do Revit e escala real, sem mudar a cor de Cores consistentes | testes da geometria do piso da rampa e da escolha dos ativos; métodos da API 2027 conferidos na documentação; aparência NÃO VALIDADA NO REVIT |
| **18 – Estacas, greide e seção** | 4, 21 | visualizador de seção por estaca (setas, estaca digitada, exagero vertical, montagem e corte/aterro com áreas); PIVs acrescentados, movidos e removidos com o perfil à vista, cota editável e corte e aterro recalculados na hora; trecho de estacas nos volumes; gráficos: perfil terreno × greide, corte e aterro por estaca, áreas das seções e diagrama de massas (Brückner) | núcleo: volumes por média das áreas iguais aos atuais, diagrama de massas conferido à mão, edição de PIV com o greide e os volumes refeitos; tela NÃO VALIDADA NO REVIT |
| **19 – Componentes da via por trecho** | 5, 17 | inserir, dividir, mudar e tirar qualquer componente (faixa, ciclofaixa, estacionamento, canteiro, calçada, baia, extensão) entre duas estacas, com transição de entrada e de saída; em via modelada ou nova; taludamento por lado e por trecho (talude fixo com limite ou largura fixa), com transição | testes de seção por estaca com componentes por trecho, das transições, da regeneração pelo grafo e das edições preservadas |
| **20 – Terreno e topografia** | 13, 21 | temas de elevação, declividade e orientação (regiões coloridas e legenda numa vista); distância e declividade entre dois pontos e pelo caminho; plataformas (área nivelada com taludes, na terraplenagem e no registro); linhas de quebra; suavização de região; comparação de superfícies com gráfico | testes dos temas, das medidas e das plataformas no núcleo |
| **21 – Corpos d'água** | 11 | rio e córrego por eixo, lago e lagoa por contorno: nível d'água, profundidade, taludes das margens, fundo por estaca; escavação no Toposolid com registro; superfície de água; faixa de proteção configurável, com a lei citada; ligação com lançamentos, parcelamento e quantitativos | testes da seção do leito, da escavação e da faixa |
| **22 – Drenagem completa** | 6, 21 | catálogo editável de tubos; por trecho: tubo, peças, conexões e vala; quantitativo e memorial com eles; linha piezométrica; rede gerada ao longo da via; bueiros; gráficos do perfil da rede | testes das peças e da vala, da linha piezométrica conferida à mão e da rede gerada |
| **23 – Parcelamento e lacunas** | 7 | coordenadas e rumos ou azimutes no memorial dos lotes; faixas não edificáveis e de domínio com a fonte; declividade da gleba com o limite conferido na lei; demais lacunas da §2 | testes do memorial (coordenadas, azimutes, fechamento), das faixas e da conferência da declividade |
| **24 – Placas personalizadas** | 16 | editor com forma livre, dimensões, cores, textos com fonte, altura e alinhamento, pictogramas e setas posicionáveis, camadas, prévia e biblioteca do usuário | testes do modelo da placa (ida e volta JSON, geometria das camadas) |
| **25 – Detalhamento** | 15 | seções em série nas folhas, perfil longitudinal com grade de estacas, planta de estaqueamento, rótulos, anotações automáticas dos componentes, carimbo | testes dos modelos de desenho no núcleo; folhas NÃO VALIDADAS NO REVIT |
| **26 – Desempenho** | 9, 10 | K15 (uma leitura do documento por geração de marca, com reserva da varredura completa), K14 (relevos do piso tátil agrupados), o que as medições das etapas 15–25 mostrarem | medido antes e depois (`tools/Desempenho` e contadores do Autoteste), resultado idêntico onde possível; NÃO VALIDADO NO REVIT até um Autoteste |

A ordem segue o pedido do usuário: primeiro os defeitos que estragam o projeto hoje, depois as esquinas (que dependem da
correção da geração), e então os pedidos maiores. Se uma etapa depender de outra, a mudança de ordem é registrada aqui.

## 4. Limites deste ambiente

Os mesmos do plano anterior (§5): sem Revit nem Windows; telas, aparência e operações no modelo NÃO VALIDADAS NO REVIT até
o usuário rodar o Autoteste ou abrir a tela; commit e push a cada passo verde.
