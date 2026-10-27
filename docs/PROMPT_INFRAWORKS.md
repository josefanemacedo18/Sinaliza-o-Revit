# Prompt "InfraWorks dentro do Revit" – SinalizaBIM para o Revit 2027

Pedido do usuário em 2026-10-27, primeiro como lista (abaixo, como foi escrita) e depois como prompt estruturado (seção 2),
com cinco imagens de referência. É executado pelo [PLANO_DE_ACAO_2](PLANO_DE_ACAO_2.md) (DECISIONS D-032). A presença de um
pedido aqui **não** significa que ele esteja implementado: o estado está no plano e na AUDIT.

As imagens não foram guardadas no repositório: são de terceiros (uma traz a marca e o telefone de uma empresa) e não há
licença para redistribuí-las. A descrição de cada uma está na seção 2 e no plano, que é o que o desenvolvimento usa.

## 1. Lista do usuário (como foi enviada)

> 1. Os materiais devem ser bem vistos e claros com a realidade no modo realista do Revit e não só no modo cores
>    consistentes
> 2. Rampas devem entrar para os pisos também
> 3. Prioridade máxima: Estude todas as ferramentas do infrawork e como o software funciona perfeitamente, pois quero que
>    traga tudo o que puder de forma adaptada para o plugin, pois o infrawork é a referência principal de funcionamento do
>    plugin
> 4. Permita adicionar, remover e modificar estacas assim como é feito no infrawork também, e até mudando sua altura no
>    terreno calculando corte e aterro ali na hora com vista de corte
> 5. Permita adicionar, remover, modificar faixas na via, inclusive determinado até que ponto ela deve ir se adicionado
>    uma faixa nova, assim como é feito no infrawork, o mesmo vale para qualquer outro elemento da via, até com extensões
>    de calçadas e etc
> 6. Na drenagem preciso saber que manilha está sendo usada, quantidade, suas conexões e tudo mais que um projeto de
>    drenagem urbana precisar ter, tudo muito bem calculado e integrado.
> 7. Adicione tudo o que um parcelamento do solo precisar ter e que está faltando no plugin, dito isso, digo o mesmo para
>    as outras matérias. Pois a intenção é ter um plugin completo e integrado.
> 9. Potencialize todos os motores, de forma a ampliar, refinar e deixar até mais rápido.
> 10. Otimize o plugin de forma que o deixe mais rápido para aplicação de suas ferramentas sem perder qualidade nenhuma.
> 11. Adicione ferramenta de gerar um rio, lago, lagoa, córrego e coisas do tipo no terreno, todo parametrizado.
> 12. Estude como funciona as extensões de calçadas de acordo com a imagem referência.
> 13. Adicione tudo quanto é ferramenta para se trabalhar em terreno, na topografia que considera que faltou para conseguir
>     melhor ajustar para projetos no plugin, pois a topografia é essencial em um projeto viário, drenagem, parcelamento do
>     solo e tudo mais que o plugin faz e deve fazer. Pois as vias continuam tendo problemas na topografia, principalmente
>     as rampas e nas interseções, pois o terreno fica comendo pedaços da via.
> 15. Amplie, adicione, reforce, melhore e refine ferramentas em geral de detalhamento projetual do plugin, de forma a ficar
>     bastante profissional
> 16. Em sinalização vertical de placas, na customização está muito limitado, não consigo parametrizar muita coisa quando
>     quero criar uma placa nova... Preciso poder ter mais liberdade de criação de placas personalizadas
> 17. Quero ter um grande poder de personalização de via para fazer o que eu quiser nela, ela estando modelada ou não, pois
>     sinto bastante limitação e preciso de maior liberdade de personalização, assim como é no infrawork
> 18. Uma situação crítica: em trechos curtos está sendo gerado várias rampas, extensões e faixas de pedestres
>     desnecessariamente, então preciso que seja respeitado uma certa distância inteligente de geração disso, quando digo
>     inteligente é porque tem que pensar no suficiente para o pedestre, identificando quadras e etc e quantidade, ou deixar
>     essas adições manuais mesmo
> 19. Chanfros devem ser feitos nas esquinas por padrão 1,50m, mas podendo ser personalizado.
> 20. Em uma imagem de referencia tem como realmente devem ser o comportamento do piso tatil em esquinas
> 21. para cortes e aterros e adição de estacas para isso, adicione graficos, e adicione graficos em outras materias que
>     precisar tambem.
>
> a ideia central é basicamente que o plugin seja um infrawork dentro do revit, com toda sua facilidade e personalização
> para se criar projetos viarios de todo jeito possivel como o projetista bem quiser, sem limitações rigidas. O atual plugin
> está avançando bastante, mas ainda está bastante limitado em comparação com o infrawork.

## 2. Prompt estruturado (enviado em seguida, com as imagens)

**Objetivo.** Transformar o SinalizaBIM (plugin Revit 2027, versão 2.19.0) num "InfraWorks dentro do Revit": projeto viário
editável sem limitações rígidas – via, estacas, greide, seções, faixas e qualquer componente da via editáveis por trecho,
com corte e aterro calculados na hora – e completar os módulos (terreno, drenagem, parcelamento, sinalização, detalhamento)
para um projeto urbano completo e integrado. Corrigir antes os defeitos críticos que o usuário vê hoje.

**Referências visuais.**
1. Piso tátil na esquina: faixa tátil direcional contínua entre a faixa de percurso e o acesso aos imóveis, paralela ao
   alinhamento predial (muro ou gradil), seguindo o chanfro da esquina; alerta nas mudanças de direção; ramais
   perpendiculares até as rampas e até a entrada de pedestres, com alerta no encontro; cotas em cm (150, 50, 70, 145, 30,
   40, total 300).
2. Esquina com chanfro no meio-fio: rampas a 10 % (1:10) alinhadas às travessias, faixa de pedestres com mínimo de 3,0 m e
   barras de 0,5 m / 0,5 m, ciclofaixa, canteiros de árvore > 1,5 × 1,5 m – referência da extensão de calçada e da esquina.
3. InfraWorks – Road Cross Section: seção na estaca 0+099.967 com navegação por estaca, declividades (5,69 %) e taludes
   1,5:1; painel Grading (Fixed Slope, limite 8,000 m, corte 1,000:1, aterro 1,500:1); corte e aterro; estacas rotuladas;
   ferramentas Terrain Themes, Suitability Maps, Point-to-Point Distance, 2D Distance & Slope, Path Distance, Range Finder,
   Terrain Statistics, Filter Select.
4. Chanfro no lote: mínimo de 1,50 m em cada lado, medido sobre os limites do terreno na esquina; o muro acompanha o chanfro.
5. InfraWorks: corte (vermelho) e aterro (verde) sobre o terreno ao longo da via, com curvas de nível rotuladas.

**Ordem pedida.** Etapa 14 – estudo do InfraWorks e auditoria (`docs/INFRAWORKS_REFERENCIA.md`, `docs/PLANO_DE_ACAO_2.md`,
D-032); Etapa 15 – defeitos críticos (item 18 e o terreno sobre a via do item 13), com regressão e o Autoteste conferindo
nós, rampas e calçadas; depois esquinas (19, 20, 12), rampas como pisos (2) e materiais (1), edição da via no estilo
InfraWorks (4, 5, 17, 21), terreno e corpos d'água (13, 11), drenagem (6), parcelamento e lacunas (7), placas (16),
detalhamento (15) e desempenho (9, 10) medido em toda etapa.

**Restrições pedidas.** Nada inventado (norma ou lei só com artigo e fonte conferidos; sem fonte, configurável e rotulado
"padrão do plugin" ou "informado pelo usuário"; nenhuma função do InfraWorks sem fonte); controles A/B/C; integração real
pelo grafo, preservando edições manuais e sem duplicar; nenhuma biblioteca externa de geometria ou gráficos sem perguntar;
texturas só da biblioteca do Revit; métodos da API 2027 conferidos; Toposolid não é Floor. Detalhes por item:
- materiais com ativo de aparência coerente e escala real, sem mudar a cor de Cores consistentes;
- rampas na categoria Pisos, com inclinação, abas e recortes, nos quantitativos e na conferência piso × terreno;
- seção navegável por estaca; estacas e PIVs acrescentados, movidos e removidos com corte e aterro na hora; componentes por
  trecho com transição; taludamento com limite; mapa vermelho e verde; gráficos do perfil, corte e aterro por estaca, áreas
  e diagrama de massas;
- drenagem com catálogo de tubos (material, DN, classe, comprimento útil, junta), peças, conexões e vala por trecho;
- corpos d'água por eixo ou contorno, com nível, profundidade, taludes, escavação registrada, água com material realista,
  faixa de proteção citada e ligação com drenagem, parcelamento e quantitativos;
- ferramentas de terreno: temas, curvas rotuladas, estatísticas, distâncias e declividades, linhas de quebra, plataformas,
  suavização, comparação de superfícies;
- placas com forma, dimensões, cores, textos, pictogramas e setas livres, prévia e biblioteca;
- geração automática com no máximo uma rampa ou travessia por aproximação de esquina de quadra ou interseção, espaçamento e
  trecho mínimos configuráveis, lista prévia e modo manual;
- chanfro no alinhamento predial medido sobre os limites, global e por esquina;
- desempenho medido antes e depois, resultado idêntico onde possível, reserva da varredura completa (K15).

**Aceite pedido.** Referência com fonte e plano cobrindo os 19 pedidos; regressão do item 18 (trecho curto: no máximo uma
travessia e um par de rampas por aproximação; manual: nenhuma); cenas do item 13 sem terreno acima da superfície acabada; cada
pedido com estado A/B/C e testes do que é calculado; toda etapa com 0 avisos, suíte verde, DLL rastreável e documentos, e
NÃO VALIDADO NO REVIT dito onde couber. Parar e perguntar antes de apagar arquivos ou dados, acrescentar pacote ou
biblioteca, mudar padrão sem volta ou fazer algo irreversível.
