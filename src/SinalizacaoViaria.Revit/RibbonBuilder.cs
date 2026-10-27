using System.Reflection;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Revit.Commands;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit;

/// <summary>
/// Monta a faixa de opções por tema (Etapa 9; Prompt de Interface §7; D-027): a guia "SinalizaBIM" com os 11 painéis do fluxo
/// de projeto – Projeto, Terreno, Sistema Viário, Interseções, Terraplenagem, Pavimentação, Drenagem, Urbanismo, Análise,
/// Documentação e Sistema – e a guia "SinalizaBIM Sinalização" com a sinalização horizontal, a vertical e a segurança viária.
/// Cada comando aparece uma vez, com ícone próprio; a dica é curta e o detalhe vai na descrição longa (aparece ao manter o
/// cursor); o botão só fica habilitado com um projeto aberto, salvo os que rodam sem ele (<see cref="NoDocumentRequiredAttribute"/>).
/// </summary>
public static class RibbonBuilder
{
    public const string TabName = "SinalizaBIM";
    public const string SignTab = "SinalizaBIM Sinalização";
    private static readonly string AssemblyPath = Assembly.GetExecutingAssembly().Location;

    public static void Build(UIControlledApplication app)
    {
        Tab(app, TabName);

        // ---------------------------------------------------------------- Projeto
        var projeto = Panel(app, TabName, "Projeto");
        Large(projeto, Data(typeof(CmdCentralProjeto), "Central do Projeto", "central",
            "Resumo do projeto: indicadores, situação, módulos e atalhos – sem alterar nada.",
            "Indicadores com dados reais – vias, extensão, interseções, rotatórias, quadras, lotes, área parcelada, corte e aterro, pavimento, " +
            "drenagem e inconsistências –, cada um com a situação (calculado, não calculado, indisponível, desatualizado ou inválido) e nunca zero " +
            "para o que não se sabe; a situação do projeto (última verificação e resultados a refazer) e os módulos com a operação principal. " +
            "O pavimento é medido só quando pedido."));
        Large(projeto, Data(typeof(CmdAtualizarTodas), "Atualizar Todas", "atualizar", "Regenera todas as marcas do projeto."));
        Large(projeto, Data(typeof(CmdEditar), "Editar", "editar", "Edita os parâmetros de uma marca, via, interseção ou rotatória e a regenera."));
        Stack(projeto,
            Data(typeof(CmdSelecionarConjunto), "Selecionar Conjunto", "selecionar", "Seleciona todos os elementos da mesma marca ou da mesma via."),
            Data(typeof(CmdAlternar2D3D), "Alternar 2D/3D", "alternar", "Converte as marcas selecionadas entre modelo 3D e detalhe 2D."),
            Data(typeof(CmdPainelContextual), "Painel Contextual", "painel",
                "Mostra ou oculta o painel com as propriedades do elemento do SinalizaBIM selecionado.",
                "Painel acoplável – na primeira vez, como aba junto da paleta Propriedades do Revit – que acompanha a seleção: identifica a via, " +
                "interseção, rotatória, drenagem, quadra ou marca e mostra só as propriedades dela, o estado e as operações compatíveis. " +
                "Vários elementos: só o que é igual em todos, sem alteração em lote."));

        // ---------------------------------------------------------------- Terreno
        var terreno = Panel(app, TabName, "Terreno");
        Large(terreno, Data(typeof(CmdDiagnosticoTerreno), "Diagnóstico do Terreno", "terreno",
            "Confere a superfície do Toposolid (V14) e mostra o terreno original guardado – sem alterar nada.",
            "Pontos, triângulos, área e cotas da superfície projetada e do terreno original guardado pela terraplenagem; declividade máxima " +
            "informada, com a fonte; dados insuficientes ou inválidos, cotas diferentes no mesmo ponto, triângulos sem área, faces acima do " +
            "limite e eixos de vias fora da superfície – o mesmo V14 do Validar Modelo. Pode selecionar as vias com eixo fora da superfície."));
        Split(terreno, "SvImagem", "Imagem Aérea",
            Data(typeof(CmdImportarImagem), "Importar Imagem e Calibrar", "importarimagem",
                "Importa uma imagem aérea na escala e posição certas (world file ou GeoTIFF) ou a calibra por 2 pontos.",
                "Qualquer imagem aérea (captura, ortofoto, drone): com world file ou GeoTIFF entra na escala e posição certas; " +
                "sem georreferência, é colocada e a calibração por 2 pontos abre em seguida."),
            Data(typeof(CmdCalibrarImagem), "Calibrar Escala da Imagem", "calibrarimagem",
                "Põe na escala exata uma imagem já no projeto, por 2 pontos e a distância real (ou a lat/lon).",
                "Selecione uma imagem já no projeto (ex.: vista aérea colada ou importada), clique 2 pontos sobre ela e informe a distância real " +
                "(ou a lat/lon dos dois): a imagem é redimensionada – e posicionada/girada para o Norte verdadeiro com lat/lon – na escala exata, medida no modelo."));

        // ---------------------------------------------------------------- Sistema viário
        var viario = Panel(app, TabName, "Sistema Viário");
        Split(viario, "SvVias", "Via",
            Data(typeof(CmdSinalizarVia), "Via", "via",
                "Via completa a partir do eixo: pista, faixas, calçadas, canteiros e toda a sinalização, já conectada.",
                "Via completa a partir do eixo – desenhado por pontos (curvas concordadas, encaixe nas vias existentes) ou selecionado: faixas, ciclofaixas, " +
                "estacionamento, canteiros, calçadas e toda a sinalização, com modelos prontos e personalizados. Cria a pista e as conexões como a ferramenta Pista e " +
                "acrescenta os demais elementos junto ao bordo."),
            Data(typeof(CmdFerrovia), "Via Férrea", "ferrovia",
                "Via férrea completa: lastro, dormentes, fixações, trilhos e bitola – ou via em laje e via embutida (VLT).",
                "Via férrea completa e personalizável: lastro com taludes, sublastro, dormentes (concreto, bibloco, madeira, aço), fixações e trilhos " +
                "TR-45/57/68, UIC-60 ou Ri-60 – ou via em laje e via embutida no pavimento (VLT) –, bitola larga, métrica, padrão ou mista, várias linhas e valetas."));
        Split(viario, "SvEixos", "Eixos",
            Data(typeof(CmdDesenharEixo), "Desenhar Eixo", "eixo",
                "Eixo com a ferramenta nativa Linha de modelo (reta, arco, spline, cadeia, snaps) ou por pontos com encaixe nas vias."),
            Data(typeof(CmdEixos), "Mostrar/Ocultar Eixos", "eixos", "Mostra ou oculta na vista ativa as linhas de eixo usadas pelas marcas."));
        Large(viario, Data(typeof(CmdPerfilVia), "Perfil da Via", "perfil",
            "Greide da via sobre a topografia, com curvas verticais; onde o terreno pede, a via vira obra de arte.",
            "Greide (perfil longitudinal) de uma via sobre a topografia: acompanha o terreno com rampa máxima e curvas verticais, rampa constante, nivelado ou por PIVs. " +
            "Onde o terreno pede, a via vira obra automaticamente – viaduto/ponte nos aterros altos, túnel nos cortes profundos, trincheira nos cortes médios – e o resto é aterro e corte no Toposolid."));
        Split(viario, "SvSecoes", "Seções Transversais",
            Data(typeof(CmdCorteTransversal), "Corte Transversal", "cortetransversal",
                "Só o perfil transversal em corte: um clique sobre a via e outro onde colocar o perfil.",
                "Só o perfil transversal em corte: um clique sobre a via e outro onde colocar o perfil – sem gerar antes a cota de seção (na planta ficam a linha de corte e as marcas A–A)."),
            Data(typeof(CmdCotarSecao), "Cotar Seção", "cotasecao",
                "Perfil transversal em corte com camadas, níveis e cotas, mais as cotas e as marcas do corte na planta.",
                "Um clique sobre a via: perfil transversal em corte (camadas, níveis, caimento, cotas horizontais e verticais, materiais) + cotas e marcas do corte na planta."));
        Split(viario, "SvAjustarVia", "Ajustar Via",
            Data(typeof(CmdLarguraTrecho), "Alterar Largura por Trecho", "largura",
                "Muda a largura da calçada, pista, estacionamento ou canteiro entre dois pontos do eixo, com transição.",
                "Clique o início (A) e o fim (B) no eixo – ou digite as estacas – e mude a largura da calçada, da pista, do estacionamento ou do canteiro, com transição em cada ponta. Setas e cotas na planta antes de confirmar."),
            Data(typeof(CmdRecuoVia), "Recuo na Via (baia / faixa auxiliar)", "recuo",
                "Baia de ônibus, faixa de desaceleração/aceleração ou recuo de embarque numa via já criada.",
                "Numa via já criada: clique junto ao bordo e crie baia de ônibus, faixa de desaceleração/aceleração ou recuo de embarque – a via é refeita com meio-fio, calçada e sinalização do recuo. Editar no recuo altera ou remove."),
            Data(typeof(CmdDistanciaVias), "Distância entre Vias", "distancia",
                "Mostra a distância entre duas vias e move a segunda para a distância nova, refazendo as interseções.",
                "Clique a via A (fica) e a via B (move): mostra a distância entre eixos, meios-fios e bordos e aceita a nova – a via B se move inteira " +
                "e as interseções são refeitas, numa única operação desfazível."),
            Data(typeof(CmdMoverVia), "Mover Via", "movervia",
                "Desloca uma via inteira com tudo o que está sobre ela, refazendo as interseções.",
                "Desloca uma via inteira (eixo, sinalização, calçadas, extensões, piso tátil e elementos sobre ela) por ΔX/ΔY digitados ou dois cliques, refazendo as interseções."),
            Data(typeof(CmdHierarquia), "Hierarquia e Esquinas", "hierarquia",
                "Hierarquia viária (CTB art. 60) e raio de concordância das esquinas de vias existentes – aplica às interseções delas."));
        Split(viario, "SvCalcadas", "Calçadas",
            Data(typeof(CmdCalcadas), "Meio-fio, Calçada e Sarjeta", "calcada",
                "Meios-fios, sarjetas, calçadas, grama e faixas de caminhada ao longo de linhas ou junto ao bordo da via.",
                "Meios-fios, sarjetas, sarjetões, calçadas, grama e faixas de caminhada ao longo de linhas – ou 'Junto ao bordo de uma via', empilhando a partir do bordo (fazem parte da via e das conexões)."),
            Data(typeof(CmdOrelha), "Extensão de Calçada", "orelha",
                "Avanço de calçada sobre o estacionamento, com pontas em curva ou chanfro, meio-fio e árvores.",
                "Avanço de calçada sobre o estacionamento, com cada ponta em curva, chanfro ou acompanhando a calçada; meio-fio, canteiro e árvores."),
            Data(typeof(CmdAreaCalcada), "Área de Calçada", "areacalcada",
                "Calçada em contorno livre (esquinas, ilhas, parklets) com meio-fio e cantos arredondados.",
                "Contorno livre (esquinas, ilhas, parklets, ciclovia no nível da calçada, faixa de serviço) com meio-fio e cantos arredondados."),
            Data(typeof(CmdRampa), "Rampas", "rampa", "Rebaixamentos de calçada (NBR 9050) com abas e piso tátil, e guias rebaixadas de veículos."));
        Split(viario, "SvObrasArte", "Obras de Arte",
            Data(typeof(CmdViaduto), "Viaduto", "viaduto",
                "Viaduto num trecho de via existente ou nova, com rampas, encontros, pilares, vigas e guarda-corpos.",
                "Viaduto num trecho de uma via existente ou numa via nova: a via (pisos, faixas, calçadas) passa sobre a estrutura – rampas de acesso, encontros, pilares, vigas/caixão/laje, guarda-corpos, juntas e iluminação."),
            Data(typeof(CmdPonte), "Ponte", "ponte",
                "Ponte num trecho de via: reta ou em curva, em vigas, caixão, arcos, estaiada ou treliça.",
                "Ponte num trecho de via (existente ou nova): reta ou em curva, em nível, entre margens ou convexa; vigas, caixão, arcos, estaiada ou treliça; apoios esconsos e pilares onde você quiser."),
            Data(typeof(CmdPassarela), "Passarela", "passarela", "Passarela de pedestres em treliça com cobertura, rampas ≤ 8,33 % (NBR 9050) e guarda-corpos."),
            Data(typeof(CmdTunel), "Túnel", "tunel",
                "Túnel num trecho de via: ferradura (NATM), circular (TBM) ou retangular, com revestimento e emboques.",
                "Túnel num trecho de via (existente ou nova): ferradura (NATM), circular (TBM) ou retangular, com revestimento, iluminação, ventiladores e emboques – a pista e os passeios são da via."),
            Data(typeof(CmdTrincheira), "Trincheira", "trincheira",
                "Trecho de via rebaixado entre muros, com rampas, guarda-corpos, canaletas e laje de travessia.",
                "Trecho de via rebaixado entre muros (flexão, cortina atirantada ou terra armada), com rampas, guarda-corpos, canaletas e laje de travessia."));

        // ---------------------------------------------------------------- Interseções
        var intersecoes = Panel(app, TabName, "Interseções");
        Large(intersecoes, Data(typeof(CmdIntersecao), "Interseção", "intersecao",
            "Cria ou atualiza a interseção do cruzamento clicado ou todas as do projeto.",
            "Cria/atualiza a interseção do cruzamento clicado ou todas as do projeto (tipos I a IV, PARE / dê a preferência / semáforo)."));
        Large(intersecoes, Data(typeof(CmdRotatoria), "Rotatória", "rotatoria",
            "Rotatória no cruzamento clicado (os ramos vêm das vias) ou isolada, de qualquer tipo, tudo editável.",
            "Clique o centro (encaixa no cruzamento mais próximo; os ramos vêm das vias) ou um ponto livre para uma rotatória isolada. " +
            "Tipos: mini, compacta, 1 faixa, 2 faixas, turbo, oval, com by-pass ou personalizada – tudo editável."));
        Large(intersecoes, Data(typeof(CmdNoViario), "Nó Viário", "noviario",
            "Interseção em desnível: diamante, trevo completo ou parcial, trombeta e rotatória em dois níveis.",
            "Interseção em desnível: diamante, diamante com rotatórias, trevo completo, trevo parcial, trombeta e rotatória em dois níveis – viadutos, rampas e laços com greide."));
        StackTwo(intersecoes,
            Data(typeof(CmdConexao), "Trocar Conexão", "conexao",
                "Troca o tratamento de um encontro de vias: interseção, rotatória, cul-de-sac ou nenhum.",
                "Clique num encontro de vias, rotatória ou ponta livre e troque o tratamento: interseção, rotatória, cul-de-sac ou nenhum."),
            Data(typeof(CmdCulDeSac), "Cul-de-sac", "culdesac", "Balão de retorno na ponta de uma via (ligado a ela) ou avulso: circular, excêntrico, gota, T, Y ou L."));

        // ---------------------------------------------------------------- Terraplenagem
        var terraplenagem = Panel(app, TabName, "Terraplenagem");
        Large(terraplenagem, Data(typeof(CmdTerraplenagem), "Terraplenagem", "terraplenagem",
            "Ajusta o Toposolid às vias, conexões e obras: plataformas e taludes, com volumes de corte e aterro.",
            "Ajusta o Toposolid (Massa e terreno) às vias, conexões e obras: plataformas, taludes de corte e aterro até o terreno natural, reaterro de muros e escavação – com volumes de corte e aterro."));
        Split(terraplenagem, "SvContencoes", "Contenções",
            Data(typeof(CmdTalude), "Talude", "talude",
                "Taludes de corte e aterro com bermas, canaletas, descidas d'água e revestimento, no Toposolid.",
                "Taludes de corte e aterro com bermas, canaletas de crista, pé e bermas, descidas d'água e revestimento – aplicados ao Toposolid."),
            Data(typeof(CmdMuroArrimo), "Muro de Arrimo", "muro",
                "Muros de flexão, gravidade, contrafortes, gabião, terra armada e cortina atirantada, com reaterro.",
                "Muros de flexão, gravidade, contrafortes, gabião, terra armada e cortina atirantada – base no terreno, barbacãs, coroamento e reaterro no Toposolid."));

        // ---------------------------------------------------------------- Pavimentação
        var pavimentacao = Panel(app, TabName, "Pavimentação");
        Large(pavimentacao, Data(typeof(CmdEstruturaPavimento), "Estrutura do Pavimento", "estruturapav",
            "Camadas do pavimento de uma via, com material, espessura, função e fonte – o plugin não dimensiona.",
            "Camadas do pavimento de uma via (revestimento, base, sub-base, reforço), com material, espessura, função e fonte: o fundo da caixa na terraplenagem, " +
            "as camadas no corte transversal e o quantitativo por camada. O plugin não dimensiona – as espessuras são do projeto de pavimentação."));
        Large(pavimentacao, Data(typeof(CmdPista), "Pista", "pista",
            "Só a pista dos veículos (asfalto, bloquete ou concreto), já conectada às outras vias.",
            "Só a pista dos veículos (asfalto, bloquete ou concreto), já conectada. Monte o resto passo a passo com Calçadas → 'Junto ao bordo de uma via'."));

        // ---------------------------------------------------------------- Drenagem
        var drenagem = Panel(app, TabName, "Drenagem");
        Split(drenagem, "SvDrenagem", "Dispositivos",
            Data(typeof(CmdBocaDeLobo), "Boca de Lobo / PV", "bocadelobo",
                "Bocas de lobo (simples, dupla, com grelha, combinada) e poços de visita, alinhados ao meio-fio.",
                "Bocas de lobo simples, dupla, com grelha e combinada (guia chapéu, caixa, tampa, rebaixo da sarjeta) e poços de visita – alinhadas ao meio-fio com um clique."),
            Data(typeof(CmdGrelha), "Grelha de Drenagem", "grelha",
                "Grelhas de sarjeta e de piso com caixa, e canaleta com grelha contínua ao longo de linhas.",
                "Grelhas de sarjeta e de piso com caixa, e canaleta com grelha contínua ao longo de linhas (barras transversais seguras para ciclistas)."));
        Stack(drenagem,
            Data(typeof(CmdTrechoDrenagem), "Trecho de Rede", "tubodren",
                "Liga dois dispositivos com caixa por um tubo: clique no de montante e no de jusante.",
                "Liga dois dispositivos com caixa (PV, boca de lobo, grelha) por um tubo de concreto: clique no de montante e no de jusante; o de jusante vira o montante do próximo trecho (Esc termina)."),
            Data(typeof(CmdProjetoDrenagem), "Projeto de Drenagem", "chuva",
                "Equação de chuva, tempo de retorno, coeficientes, diâmetros e limites da rede, gravados no projeto.",
                "Equação de chuva, tempo de retorno, coeficiente de escoamento, rugosidade, diâmetros, limites e identificação do memorial, gravados no projeto – com as predefinições do Manual SUDERHSA (2002) e do memorial modelo."),
            Data(typeof(CmdCaptacoesDoModelo), "Ler do Modelo", "dremodelo",
                "Lê da via a declividade da sarjeta, o ponto baixo e a área de contribuição de cada captação.",
                "Lê da via cada boca de lobo e grelha: declividade da sarjeta pelo greide, ponto baixo e a área que escoa até ela (pista do eixo ao meio-fio e calçada, " +
                "entre o divisor e a captação). Mostra o que leu e preenche só os campos vazios – ou substitui todos, se você escolher."));
        Large(drenagem, Data(typeof(CmdRedeDrenagem), "Relatório da Rede", "rededren",
            "Dimensionamento das galerias pelo método racional, captações e conferência da rede (CSV).",
            "Planilha de dimensionamento das galerias pelo método racional, captações (sarjeta e engolimento) e conferência da rede (sentido, ligações, ciclos, recobrimento, PVs); aplica os diâmetros calculados (CSV)."));

        // ---------------------------------------------------------------- Urbanismo
        var urbanismo = Panel(app, TabName, "Urbanismo");
        Large(urbanismo, Data(typeof(CmdLoteamento), "Loteamento", "loteamento",
            "Gleba → quadras (a gleba menos as vias) → lotes pela testada, numerados em cada quadra.",
            "Gleba (linha de propriedade, linhas, Toposolid ou cliques) → quadras reconhecidas (a gleba menos as vias) → lotes pela testada com medida padrão (ex.: 12 × 30), " +
            "numerados de 1 a N em cada quadra. Cada lote é uma subdivisão do Toposolid: acompanha o relevo em planta e no 3D (sem Toposolid, piso plano)."));
        Split(urbanismo, "SvLoteEdicao", "Quadra e Lote",
            Data(typeof(CmdQuadraLote), "Quadra", "quadra",
                "Medidas padrão da quadra, testadas, lotes de esquina, sentido da numeração e lote 1.",
                "Clique um lote: medidas padrão da quadra, sobra, miolo, testadas (apontar com cliques, em ordem de prioridade), lote de esquina em cada esquina " +
                "(por uma testada, pela outra ou com medida própria), sentido da numeração e lote 1."),
            Data(typeof(CmdLotesPorLinhas), "Lotes por Linhas", "lotelinhas",
                "Divide a quadra em lotes pelas linhas que você desenhar: cada região fechada vira um lote.",
                "Selecione o limite da quadra (linhas fechadas ou um lote de uma quadra) e depois as linhas que dividem os lotes: cada região fechada vira um lote. Mova as linhas e use Atualizar para refazer."),
            Data(typeof(CmdCategoriaLote), "Categoria do Lote", "categorialote",
                "Pincel de categoria: escolha a categoria e clique os lotes um a um (ESC termina).",
                "Pincel: escolha a categoria (Lote, Institucional, Área verde...) e clique os lotes um a um – cada clique já aplica; ESC termina."),
            Data(typeof(CmdLote), "Lote", "lote",
                "Frente, profundidade, divisão em partes e categoria de um lote; os vizinhos e a numeração se ajustam.",
                "Clique um lote: frente, profundidade, divisão em partes e categoria (Lote, Institucional, Área verde ou outra) – os vizinhos e a numeração se ajustam."),
            Data(typeof(CmdUnirLotes), "Unir Lotes", "unirlotes",
                "Une lotes vizinhos da mesma quadra num lote só e refaz a numeração.",
                "Selecione lotes vizinhos da mesma quadra: viram um lote só (sobra maior, lote maior que o padrão) e a numeração se refaz. Desfazer: Lote → Desfazer a união."));
        StackTwo(urbanismo,
            Data(typeof(CmdNomearVias), "Nomear Vias", "vianome",
                "Dá nome à via: entra nas confrontações do memorial, no detalhamento e no masterplan.",
                "Selecione a via (eixo, piso ou elemento dela) e escreva o nome – entra nas confrontações do memorial, no detalhamento e no masterplan."),
            Data(typeof(CmdCategoriasLotes), "Categorias e Filtros", "filtrolote",
                "Categorias de lote (cor, hachura, prefixo, área pública, mínimo %) e filtros de vista por categoria.",
                "Categorias de lote (cor e hachura em planta e no 3D, numeração com prefixo, área pública, mínimo %), nova categoria e filtros de vista para mostrar/ocultar cada uma."));
        Split(urbanismo, "SvLoteDoc", "Quadros e Áreas",
            Data(typeof(CmdQuadrosLoteamento), "Quadros do Loteamento", "quadrolote",
                "Quadro de áreas (m² e % da gleba), de quadras e de lotes numa Legenda própria, e a tabela dos lotes.",
                "Quadro de áreas (m² e % da gleba), de quadras e de lotes (lote a lote: via, área e perímetro – todas as quadras, uma ou uma por quadra) " +
                "numa Legenda própria (vai para qualquer folha) – e a tabela nativa dos lotes."),
            Data(typeof(CmdQuadroQuadra), "Quadro da Quadra", "quadroquadra",
                "Quadro de lotes de uma só quadra, numa Legenda própria ou na vista ativa.",
                "Clique um lote: o quadro de lotes só daquela quadra (lote, categoria, via, área, perímetro), numa Legenda própria para qualquer folha ou na vista ativa."),
            Data(typeof(CmdDetalharLotes), "Detalhar Lotes", "detalhelotes",
                "Número, área, medidas, categoria, letra da quadra e nome das vias na vista ativa.",
                "Na vista ativa: número, área, medidas dos lados, categoria, letra da quadra num círculo e nome das vias (cada item opcional)."));
        Split(urbanismo, "SvUrbanos", "Elementos Urbanos",
            Data(typeof(CmdMobiliario), "Elementos Urbanos", "mobiliario", "Bancos, lixeiras, postes, árvores, abrigos, paraciclos, hidrantes, floreiras, semáforos..."),
            Data(typeof(CmdTrocarElementosUrbanos), "Trocar Elementos", "trocar",
                "Troca o tipo de vários elementos urbanos de uma vez, mantendo posição, direção e espaçamento.",
                "Selecione vários elementos urbanos e troque o tipo de uma vez (ex.: lixeiras por paraciclos): posição, direção, caminho e espaçamento ficam; " +
                "medidas e cor próprias ficam ou voltam às do novo tipo."),
            Data(typeof(CmdElementoFamilia), "Famílias do Revit", "familia",
                "Usa suas famílias de componente como elementos urbanos, classificadas no Quantitativo.",
                "Use suas próprias famílias de componente (mobiliário, luminárias, plantio, modelo genérico...) como elementos urbanos – por cliques ou ao longo de linhas – " +
                "classificadas por subcategoria (iluminação, arborização, bancos, lixeiras...) no Quantitativo. Também classifica famílias já inseridas."),
            Data(typeof(CmdCanteiro), "Canteiros", "canteiro", "Canteiros gramados, jardineiras e grelhas de árvore na faixa de serviço, com árvores."));
        Large(urbanismo, Data(typeof(CmdMasterplan), "Masterplan", "masterplan",
            "Cópia da planta só com o layout: lotes nas cores das categorias, vias, calçadas e vegetação.",
            "Cópia da planta ativa só com o layout: lotes nas cores das categorias, vias, calçadas, árvores e mobiliário, nomes das quadras e vias e números dos lotes – " +
            "sem sinalização, cotas nem textos técnicos. Cria o modelo de vista \"SV - Masterplan\"."));

        // ---------------------------------------------------------------- Análise
        var analise = Panel(app, TabName, "Análise");
        Large(analise, Data(typeof(CmdValidarModelo), "Validar Modelo", "validar",
            "Confere o projeto sem alterar nada (V01 a V17) e mostra o estado dos resultados derivados.",
            "Dados ilegíveis ou de versão mais nova, cópias divergentes, referências quebradas, dependências circulares, elementos do Revit apagados, " +
            "contornos inválidos, quadras e lotes, interseções, traçado e greide (limites informados), drenagem, topografia, terraplenagem desatualizada, " +
            "parcelamento e resultados lidos do modelo que ficaram desatualizados. Cada achado traz módulo, severidade, confiança, ação recomendada e se pede " +
            "revisão humana; o que não pôde ser conferido aparece como não verificado. O relatório é gravado em TXT e comparado com a validação anterior."));
        Split(analise, "SvTrafego", "Tráfego",
            Data(typeof(CmdSimuladorTrafego), "Simulador de Tráfego", "trafego",
                "Capacidade e nível de serviço (HCM) de cruzamentos e trechos, semáforos, microssimulação e diagnóstico.",
                "Lê todo o projeto (vias, faixas, sentidos, hierarquia, velocidades, interseções e seus controles, rotatórias, balões, placas, " +
                "faixas de pedestres, vagas, lombadas e greide), estima a demanda, calcula capacidade e nível de serviço (HCM) de cada cruzamento " +
                "e trecho, otimiza os semáforos, anima a microssimulação dos veículos e gera o diagnóstico com recomendações (capacidade, segurança, " +
                "sinalização, CTB, acessibilidade) – com mapa de níveis de serviço na planta e relatório."),
            Data(typeof(CmdSemaforos), "Semáforos", "semaforos",
                "Lista, cria e ajusta os tempos dos semáforos, com recomendação e onda verde.",
                "Abre o Simulador direto na aba Semáforos: lista de todos os semáforos, criar (clique no mapa ou escolha um elemento do projeto), " +
                "tempos de cada um (ciclo, verdes, entreverdes, defasagem), recomendação para um, todos ou todos os cruzamentos de uma via com onda verde, e gravar no projeto."));

        // ---------------------------------------------------------------- Documentação
        var documentacao = Panel(app, TabName, "Documentação");
        Large(documentacao, Data(typeof(CmdQuantitativos), "Quantitativos", "quantitativos",
            "Quantidades por categoria e hierarquia viária (m², m, un, consumo), exportação CSV e tabelas do Revit."));
        Large(documentacao, Data(typeof(CmdPranchas), "Pranchas", "pranchas",
            "Divide o eixo da via em trechos que cabem na folha e monta as pranchas com a legenda e os quadros.",
            "Com a folha (carimbo carregado no projeto) e a escala escolhidas, divide o eixo da via em trechos que cabem na folha – com sobreposição e " +
            "linhas de corte \"continua na prancha X\" – ou usa trechos por estacas ou cortes clicados; cria uma vista girada por trecho e monta as " +
            "folhas com a legenda de placas e o quadro de quantidades. Editar um trecho atualiza a prancha."));
        Split(documentacao, "SvMemoriais", "Memoriais",
            Data(typeof(CmdMemorial), "Memorial Descritivo", "memorial",
                "Memorial descritivo (.docx) do modelo: normas, vias, interseções, sinalização e quantidades.",
                "Gera o memorial descritivo (.docx) do modelo: objetivo e normas, vias, interseções, sinalização horizontal e vertical por código com as " +
                "quantidades do quantitativo, acessibilidade, drenagem e obras – com nome do projeto, responsável técnico e ART em branco para preencher."),
            Data(typeof(CmdMemorialDrenagem), "Memorial de Drenagem", "memdren",
                "Memorial de drenagem pluvial (.docx): parâmetros, planilha das galerias, serviços e quantitativo.",
                "Memorial descritivo de drenagem pluvial (.docx): parâmetros de projeto, planilha das galerias, descritivo dos serviços, quantitativo e disposições finais."),
            Data(typeof(CmdMemorialLoteamento), "Memorial do Loteamento", "memlote",
                "Memorial do loteamento (.docx): gleba, quadro de áreas, sistema viário, quadras e lotes.",
                "Memorial descritivo (.docx): gleba, quadro de áreas, sistema viário, quadras e descrição de cada lote com as confrontações."));
        Split(documentacao, "SvDetalhar", "Detalhar",
            Data(typeof(CmdAnotar), "Anotar", "anotar", "Chamada com texto automático para qualquer sinalização."),
            Data(typeof(CmdDetalheTipico), "Detalhe Típico", "detalhetipico", "Detalhe ampliado e cotado de uma marca ou placa."),
            Data(typeof(CmdQuadroQuantitativos), "Quadro de Quantitativos", "quadroqtd",
                "Quadro de quantidades do projeto – numa Legenda própria (colocada direto na folha escolhida) ou na vista ativa."),
            Data(typeof(CmdNotas), "Notas Gerais", "notas", "Bloco de notas numeradas do projeto."),
            Data(typeof(CmdNorte), "Norte", "norte", "Indicação de norte."));

        // ---------------------------------------------------------------- Sistema (funcionam sem projeto aberto)
        var sistema = Panel(app, TabName, "Sistema");
        Stack(sistema,
            Data(typeof(CmdConfiguracoes), "Configurações", "config",
                "Preferências do plugin: ímã de conexão, pisos do Revit, interseções automáticas, limites geométricos e Autoteste."),
            Data(typeof(CmdCatalogo), "Catálogo", "catalogo", "Abre, recarrega ou restaura o catálogo normativo (JSON) para personalização."),
            Data(typeof(CmdSobre), "Normas / Sobre", "sobre", "Normas de referência e informações do plugin."));
        // O Autoteste (diagnóstico) fica em Configurações: é ferramenta de suporte, não de projeto.

        // ================================================================ Guia "SinalizaBIM Sinalização"
        // A sinalização numa guia própria: a guia principal fica com os 11 painéis do fluxo e espaço para os nomes.
        Tab(app, SignTab);
        var horizontal = Panel(app, SignTab, "Sinalização Horizontal");
        Split(horizontal, "SvLinhas", "Linhas",
            Data(typeof(CmdLinhaLongitudinal), "Linha Longitudinal", "linha", "LFO-1 a LFO-4, LMS, LBO, LCO, faixas exclusivas, LCA e LPP."),
            Data(typeof(CmdLinhaTransversal), "Linha Transversal", "transversal", "LRE, LDP, LRV, FTP e demais marcas transversais."),
            Data(typeof(CmdFaixaPedestres), "Faixa de Pedestres", "pedestre", "FTP-1 zebrada ou FTP-2 paralela com linhas de retenção."));
        Split(horizontal, "SvZebrado", "Zebrado",
            Data(typeof(CmdZebrado), "Zebrado", "zebrado",
                "Zebrados, chevrons, área de conflito (MAC) e quadriculado (MAE) em qualquer contorno, com furos.",
                "Zebrados (ZPA, ZPA-A), chevrons, marcação de área de conflito (MAC), quadriculado (MAE) em qualquer contorno – com furos – ou como faixa ao longo de um anel."),
            Data(typeof(CmdCanalizacao), "Canalização (MTL / MAO / MAP)", "canalizacao",
                "Área neutra de transição de largura, de aproximação de obstáculo ou de acostamento, dimensionada pela velocidade.",
                "Área neutra de transição de largura, aproximação de obstáculo ou acostamento, dimensionada pela velocidade (l = 0,5·V·d) ao longo de uma linha de referência."));
        Split(horizontal, "SvInscricoes", "Inscrições",
            Data(typeof(CmdSimbolos), "Setas e Símbolos", "seta", "Setas PEM, mudança de faixa, SIA, bicicleta, dê a preferência..."),
            Data(typeof(CmdLegendas), "Legendas", "legenda", "Legendas alongadas (PARE, ÔNIBUS, ESCOLA...) com qualquer fonte."));
        Large(horizontal, Data(typeof(CmdVagas), "Vagas", "vaga", "Vagas paralelas ou em ângulo, PcD, idoso, carga e descarga, ônibus, táxi..."));
        Split(horizontal, "SvCiclo", "Ciclovia e Piso Tátil",
            Data(typeof(CmdCiclovia), "Ciclovia / Faixa de Caminhada", "ciclo",
                "Ciclofaixa, ciclovia segregada ou faixa de caminhada completa: fundo, linhas, símbolos e segregação.",
                "Ciclofaixa uni/bidirecional, ciclovia segregada ou faixa de caminhada completa: fundo, linhas, símbolos, setas e segregação."),
            Data(typeof(CmdCicloviaLinhas), "Marcas de Ciclovia Avulsas", "ciclomarcas", "CIC-LD, CIC-FD, CIC-LC e cruzamento rodocicloviário (MCC) ao longo de linhas."),
            Data(typeof(CmdPisoTatil), "Piso Tátil", "tatil", "Piso tátil de alerta e direcional (NBR 16537)."));
        Large(horizontal, Data(typeof(CmdApagarTrecho), "Apagar Trecho", "apagartrecho",
            "Apaga partes da sinalização horizontal clicando cada peça; as peças apagadas podem ser devolvidas depois.",
            "Apaga partes da sinalização HORIZONTAL: escolha a marca e, na planta dela, clique em cada traço, seta ou faixa a apagar " +
            "(Shift + arrastar apaga uma janela). A marca continua a mesma e as peças apagadas podem ser devolvidas depois."));

        var vertical = Panel(app, SignTab, "Sinalização Vertical");
        Split(vertical, "SvPlacas", "Placas",
            Data(typeof(CmdPlacas), "Placas", "placa", "Placas de regulamentação, advertência, indicação, educativas, turísticas e de obras, com suporte – em 3D."),
            Data(typeof(CmdDetalharPlacas), "Detalhar Placas", "detalheplaca", "Placa ampliada ao lado do suporte, com chamada (reta, cotovelo ou livre), código/nome e número em balão."),
            Data(typeof(CmdMoverChamadaPlaca), "Mover Chamada de Placa", "chamadaplaca",
                "Leva o símbolo detalhado para outro lugar e redesenha a linha de chamada pelos vértices.",
                "Leve o símbolo detalhado para onde quiser e desenhe a linha de chamada clicando os vértices – ou mude a ponta da chamada."),
            Data(typeof(CmdQuadroPlacas), "Quadro de Placas", "quadroplacas",
                "Símbolo, código, descrição, dimensões e quantidade de cada placa, numa Legenda própria ou na vista ativa.",
                "Símbolo, numeração, código, descrição, dimensões e quantidade de cada placa – numa Legenda própria (qualquer vista ativa, inclusive folha) ou na vista ativa."),
            Data(typeof(CmdQuadroLegenda), "Quadro de Legenda", "quadrolegenda",
                "Legenda das placas do projeto, com desenho e descrição, que vai para várias folhas e se atualiza sozinha.",
                "Legenda das placas do projeto, com desenho e descrição – numa Legenda própria que vai para várias folhas e se atualiza sozinha."));

        var seguranca = Panel(app, SignTab, "Segurança Viária");
        Split(seguranca, "SvBloqueios", "Dispositivos de Segurança",
            Data(typeof(CmdDispositivos), "Bloqueios Físicos", "bloqueio",
                "Tachas, segregadores, balizadores, pilaretes, New Jersey, defensas, gradis e canalização de obras.",
                "Todos os dispositivos físicos numa só ferramenta: tachas e tachões refletivos, segregadores, balizadores, pilaretes e frades, New Jersey, " +
                "defensas metálicas (guard rail simples, dupla e de cabos), gradis, floreiras e canalização provisória de obras."),
            Data(typeof(CmdTachas), "Tachas e Tachões", "tacha",
                "Tachas e tachões refletivos ao longo de linhas, com cadência pela velocidade.",
                "Tachas e tachões refletivos ao longo de linhas (mono/bidirecionais, cadência pela velocidade) – atalho de Bloqueios Físicos."),
            Data(typeof(CmdSonorizador), "Sonorizador Longitudinal", "sonorizador",
                "Sonorizador (rumble strip) no bordo, acostamento ou eixo: fresado, termoplástico, barras ou tachas.",
                "Sonorizador (rumble strip) no bordo, acostamento ou eixo: fresado, termoplástico com relevo, barras ou tachas – dimensões, espaçamento, ângulo, cor e interrupções para ciclistas."),
            Data(typeof(CmdAreaEscape), "Área de Escape", "escape",
                "Caixa de retenção para caminhões sem freio em descidas, pela velocidade, rampa e material.",
                "Caixa de retenção para caminhões sem freio em descidas: comprimento pela velocidade, rampa e material (AASHTO), profundidade com transição, faixa de serviço com âncoras, berma, zebrado e delineadores."));
        Large(seguranca, Data(typeof(CmdModeracao), "Quebra-mola e Lombadas", "quebramola", "Ondulações transversais, faixa elevada, lombada invertida e almofadas (speed cushion)."));
        Large(seguranca, Data(typeof(CmdCruzamentoFerroviario), "Cruzamento Rodoferroviário", "passagemnivel",
            "Passagem em nível completa: retenção, cruz de Santo André, PARE e as placas A-41, R-1 e A-39.",
            "Passagem em nível completa: linha de retenção dupla, retângulo de advertência com cruz de Santo André, linha dupla, PARE, LRV e placas A-41/R-1/A-39."));
    }

    private static void Tab(UIControlledApplication app, string name)
    {
        try { app.CreateRibbonTab(name); } catch { /* já existe */ }
    }

    private static RibbonPanel Panel(UIControlledApplication app, string tab, string name) => app.CreateRibbonPanel(tab, name);

    private static void Split(RibbonPanel panel, string name, string text, params PushButtonData[] items)
    {
        if (panel.AddItem(new SplitButtonData(name, text)) is not SplitButton sb) return;
        sb.IsSynchronizedWithCurrentItem = true;
        foreach (var it in items) sb.AddPushButton(it);
    }

    /// <summary>
    /// Botão de um comando: rótulo, ícone (<see cref="IconFactory"/>), dica curta e, quando há, o detalhe (descrição longa,
    /// mostrada ao manter o cursor sobre o botão). Habilitado só com um projeto ativo, salvo os comandos que rodam sem ele.
    /// </summary>
    private static readonly Dictionary<string, string> Labels = new();

    /// <summary>Rótulo do botão de um comando (classe Cmd*), para os atalhos da Central; nulo se o comando não está na faixa.</summary>
    public static string? LabelOf(string command) => Labels.TryGetValue(command, out var l) ? l : null;

    private static PushButtonData Data(Type cmd, string text, string icon, string tooltip, string? details = null)
    {
        Labels[cmd.Name] = text;
        var d = new PushButtonData(cmd.Name, text, AssemblyPath, cmd.FullName)
        {
            ToolTip = tooltip,
            LargeImage = IconFactory.Large(icon),
            Image = IconFactory.Small(icon),
            AvailabilityClassName = (NoDocumentRequiredAttribute.On(cmd) ? typeof(AlwaysAvailable) : typeof(ProjectDocumentAvailability)).FullName,
        };
        if (details != null) d.LongDescription = details;
        return d;
    }

    private static void Large(RibbonPanel panel, PushButtonData data) => panel.AddItem(data);

    private static void Stack(RibbonPanel panel, PushButtonData a, PushButtonData b, PushButtonData c) =>
        panel.AddStackedItems(a, b, c);

    private static void StackTwo(RibbonPanel panel, PushButtonData a, PushButtonData b) =>
        panel.AddStackedItems(a, b);
}
