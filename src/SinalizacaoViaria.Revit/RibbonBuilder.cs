using System.Reflection;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Revit.Commands;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit;

/// <summary>Monta a guia "SinalizaBIM" na faixa de opções.</summary>
public static class RibbonBuilder
{
    public const string TabName = "SinalizaBIM";
    private static readonly string AssemblyPath = Assembly.GetExecutingAssembly().Location;

    public static void Build(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); } catch { /* já existe */ }

        var via = app.CreateRibbonPanel(TabName, "Via");
        Large(via, typeof(CmdNovaVia), "Nova\nVia", "novavia",
            "Desenha uma via nova por pontos – ou puxe a ponta de qualquer eixo até outra via: ela se conecta sozinha (ímã), em qualquer ângulo. (com curvas concordadas) que se conecta às vias existentes: clique sobre uma via para criar um entroncamento/cruzamento, perto da ponta para continuá-la ou numa rotatória para um novo ramo. O sistema viário se ajusta sozinho (interseção ou rotatória) e as pontas livres podem receber cul-de-sac.");
        Large(via, typeof(CmdSinalizarVia), "Sinalizar\nVia", "via",
            "Monta a seção transversal completa a partir do eixo: faixas de rolamento, exclusivas e preferenciais, ciclofaixas, estacionamento, acostamentos, faixas de segurança, canteiros centrais e laterais, calçadas e dispositivos de segregação – com toda a sinalização.");
        Large(via, typeof(CmdConexao), "Conexão", "conexao",
            "Troca o tratamento de uma conexão: num encontro de vias, interseção, rotatória ou nenhum; numa ponta livre, cul-de-sac ou nenhum.");
        Large(via, typeof(CmdHierarquia), "Hierarquia\nViária", "hierarquia",
            "Define a hierarquia viária (CTB art. 60) de vias existentes – usada nos quantitativos e na via preferencial das interseções.");
        Large(via, typeof(CmdIntersecao), "Interseção", "intersecao",
            "Ajusta o cruzamento/entroncamento de vias: pavimento contínuo, esquinas com raio, calçadas e canteiros refeitos, sinalização interrompida, faixas de pedestres, retenções e rampas. Também é feito automaticamente ao criar uma via que cruza outra.");
        Large(via, typeof(CmdRotatoria), "Rotatória", "rotatoria",
            "Rotatória com ilha central ajardinada, faixa galgável, pista giratória (1 a 4 faixas), ramos com ilhas separadoras, dê a preferência, travessias e placas R-2/R-33 – liga-se às vias existentes.");
        Large(via, typeof(CmdLinhaLongitudinal), "Linha\nLongitudinal", "linha",
            "LFO-1 a LFO-4, LMS-1/2, LBO, LCO, faixas exclusivas, LCA e LPP ao longo de linhas do modelo.");
        Large(via, typeof(CmdCalcadas), "Meio-fio e\nSarjeta", "calcada",
            "Meios-fios (vários tipos), sarjetas, sarjetões, calçadas, canteiros gramados e faixas de caminhada azuis/verdes ao longo de qualquer linha.");
        Large(via, typeof(CmdDesenharEixo), "Desenhar\nEixo", "eixo",
            "Desenha um eixo/caminho por pontos com o estilo de linha 'SV - Eixo de sinalização'.");

        var trans = app.CreateRibbonPanel(TabName, "Transversais");
        Large(trans, typeof(CmdFaixaPedestres), "Faixa de\nPedestres", "pedestre",
            "Faixa de travessia (FTP-1 zebrada ou FTP-2 paralela) com linhas de retenção automáticas.");
        Large(trans, typeof(CmdLinhaTransversal), "Linha\nTransversal", "transversal",
            "LRE, LDP, LRV, FTP e demais marcas transversais por dois cliques.");

        var areas = app.CreateRibbonPanel(TabName, "Canalização");
        Large(areas, typeof(CmdZebrado), "Zebrado", "zebrado",
            "Zebrados (ZPA), chevrons, áreas de conflito e lombadas em qualquer contorno fechado, com linha de canalização.");
        Large(areas, typeof(CmdAreaConflito), "Área de\nConflito", "conflito",
            "Marcação de área de conflito (quadriculado amarelo) em cruzamentos que não devem ser bloqueados.");

        var vert = app.CreateRibbonPanel(TabName, "Sinalização Vertical");
        Large(vert, typeof(CmdPlacas), "Placas", "placa",
            "Placas de regulamentação, advertência, indicação, educativas, turísticas e de obras, com suporte, altura livre e legenda – em 3D.");

        var mod = app.CreateRibbonPanel(TabName, "Moderação de Tráfego");
        Large(mod, typeof(CmdModeracao), "Quebra-mola\ne Lombadas", "quebramola",
            "Ondulações transversais tipo A e B, faixa elevada para travessia e lombada invertida, com volume 3D e pintura.");

        var calc = app.CreateRibbonPanel(TabName, "Calçadas");
        Large(calc, typeof(CmdOrelha), "Orelha de\nCalçada", "orelha",
            "Avanço de calçada sobre a faixa de estacionamento, com transições curvas ou em chanfro, meio-fio, canteiro e árvores – recorta vagas e linhas da pista.");
        Large(calc, typeof(CmdAreaCalcada), "Área de\nCalçada", "areacalcada",
            "Contorno livre (esquinas, avanços, ilhas, parklets, ciclovia no nível da calçada, faixa de serviço) com meio-fio e cantos arredondados.");
        Large(calc, typeof(CmdCanteiro), "Canteiros", "canteiro",
            "Canteiros gramados, jardineiras elevadas e grelhas de árvore distribuídos na faixa de serviço, com árvores – recortam a calçada.");
        Large(calc, typeof(CmdCulDeSac), "Cul-de-sac", "culdesac",
            "Balão de retorno circular, excêntrico, em gota, em T, Y ou L, com pavimento, meio-fio, calçada, ilha central e linha de bordo.");

        var urb = app.CreateRibbonPanel(TabName, "Urbanismo");
        Large(urb, typeof(CmdRampa), "Rampas", "rampa",
            "Rebaixamentos de calçada (NBR 9050) com abas e piso tátil, e guias rebaixadas de veículos – recortam a calçada automaticamente.");
        Large(urb, typeof(CmdMobiliario), "Elementos\nUrbanos", "mobiliario",
            "Bancos, lixeiras, postes de iluminação, árvores, abrigos de ônibus, paraciclos, hidrantes, floreiras, placas de rua e semáforos.");

        var insc = app.CreateRibbonPanel(TabName, "Inscrições");
        Large(insc, typeof(CmdSimbolos), "Setas e\nSímbolos", "seta",
            "Setas PEM, mudança de faixa, SIA, bicicleta, 'Dê a preferência', cruz de Santo André...");
        Large(insc, typeof(CmdLegendas), "Legendas", "legenda",
            "Legendas alongadas (PARE, ÔNIBUS, ESCOLA...) com qualquer fonte instalada.");

        var est = app.CreateRibbonPanel(TabName, "Estacionamento");
        Large(est, typeof(CmdVagas), "Vagas", "vaga",
            "Vagas paralelas ou em ângulo, PcD (com faixa adicional e SIA), idoso, carga e descarga, ônibus, táxi...");

        var disp = app.CreateRibbonPanel(TabName, "Complementos");
        Stack(disp,
            Data(typeof(CmdTachas), "Tachas", "tacha", "Tachas e tachões refletivos ao longo de linhas, com contagem automática."),
            Data(typeof(CmdPisoTatil), "Piso Tátil", "tatil", "Piso tátil de alerta e direcional (ABNT NBR 16537)."),
            Data(typeof(CmdCiclovia), "Ciclovia", "ciclo", "Linhas de ciclofaixa, pintura vermelha e cruzamento rodocicloviário (MCC)."));

        var seg = app.CreateRibbonPanel(TabName, "Segregação Física");
        Large(seg, typeof(CmdGuardRail), "Guard\nRail", "guardrail",
            "Defensas metálicas simples, duplas e de cabos ao longo de bordos, canteiros e obras de arte.");
        Large(seg, typeof(CmdDispositivos), "Bloqueios\nFísicos", "bloqueio",
            "Segregadores (tartarugas), tachões, balizadores flexíveis, cilindros, pilaretes, prismas, barreiras New Jersey e modulares, separadores e defensas metálicas.");

        var det = app.CreateRibbonPanel(TabName, "Detalhamento");
        Large(det, typeof(CmdDetalharPlacas), "Detalhar\nPlacas", "detalheplaca",
            "Na planta, desenha cada placa ampliada ao lado do suporte com linha de chamada e código/nome (ex.: R-1 Parada obrigatória). Tamanhos em mm de papel; acompanha a edição da placa.");
        Large(det, typeof(CmdAnotar), "Anotar", "anotar",
            "Chamada com texto automático para qualquer sinalização (código, nome, largura, padrão, espaçamento, dimensões) – atualizada quando a marca é editada.");
        Large(det, typeof(CmdQuadroLegenda), "Quadro de\nLegenda", "quadrolegenda",
            "Quadro de legenda com amostra desenhada e descrição de cada tipo de sinalização usado no projeto.");
        Large(det, typeof(CmdCotarSecao), "Cotar\nSeção", "cotasecao",
            "Cadeia de cotas automática atravessando a via: larguras de faixas (eixo a eixo das linhas), canteiros, meios-fios e calçadas, com cota total – atualizada quando a sinalização muda.");
        Large(det, typeof(CmdDetalheTipico), "Detalhe\nTípico", "detalhetipico",
            "Detalhe ampliado e cotado de uma marca: traço/espaço e larguras das linhas, zebrados, vagas e placa em elevação (altura livre, dimensões, suporte).");
        Stack(det,
            Data(typeof(CmdQuadroQuantitativos), "Quadro de Quantitativos", "quadroqtd", "Tabela de quantidades por categoria desenhada na prancha (m², m, un), atualizada automaticamente."),
            Data(typeof(CmdQuadroPlacas), "Quadro de Placas", "quadroplacas", "Símbolo, numeração, código, descrição, dimensões e quantidade de cada placa do projeto."),
            Data(typeof(CmdNotas), "Notas Gerais", "notas", "Bloco de notas numeradas do projeto (texto padrão editável)."));
        Stack(det,
            Data(typeof(CmdNorte), "Norte", "norte", "Indicação de norte."),
            Data(typeof(CmdEixos), "Mostrar/Ocultar Eixos", "eixos", "Mostra ou oculta na vista ativa as linhas 'SV - Eixo de sinalização' usadas como caminho das marcas."),
            Data(typeof(CmdAlternar2D3D), "Alternar 2D/3D", "alternar", "Converte as marcas selecionadas entre modelo 3D e detalhe 2D (vista ativa)."));

        var edit = app.CreateRibbonPanel(TabName, "Editar");
        Large(edit, typeof(CmdEditar), "Editar", "editar", "Edita os parâmetros de uma marca existente e a regenera.");
        StackTwo(edit,
            Data(typeof(CmdAtualizarTodas), "Atualizar Todas", "atualizar", "Regenera todas as marcas do projeto (após mudar o catálogo ou as superfícies)."),
            Data(typeof(CmdSelecionarConjunto), "Selecionar Conjunto", "selecionar", "Seleciona todos os elementos da mesma marca ou do mesmo grupo (ex.: toda a via)."));

        var rep = app.CreateRibbonPanel(TabName, "Relatórios");
        Large(rep, typeof(CmdQuantitativos), "Quantitativos", "quantitativos",
            "Quadro de quantidades por código, cor e material (m², m, unidades, consumo), exportação CSV e tabela do Revit.");

        var cfg = app.CreateRibbonPanel(TabName, "Configurações");
        Stack(cfg,
            Data(typeof(CmdConfiguracoes), "Configurações", "config", "Preferências padrão do plugin."),
            Data(typeof(CmdCatalogo), "Catálogo", "catalogo", "Abre o catálogo normativo (JSON) para personalização."),
            Data(typeof(CmdSobre), "Normas / Sobre", "sobre", "Normas de referência e informações do plugin."));
    }

    private static PushButtonData Data(Type cmd, string text, string icon, string tooltip)
    {
        var d = new PushButtonData(cmd.Name, text, AssemblyPath, cmd.FullName)
        {
            ToolTip = tooltip,
            LargeImage = IconFactory.Large(icon),
            Image = IconFactory.Small(icon),
        };
        return d;
    }

    private static void Large(RibbonPanel panel, Type cmd, string text, string icon, string tooltip) =>
        panel.AddItem(Data(cmd, text, icon, tooltip));

    private static void Stack(RibbonPanel panel, PushButtonData a, PushButtonData b, PushButtonData c) =>
        panel.AddStackedItems(a, b, c);

    private static void StackTwo(RibbonPanel panel, PushButtonData a, PushButtonData b) =>
        panel.AddStackedItems(a, b);
}
