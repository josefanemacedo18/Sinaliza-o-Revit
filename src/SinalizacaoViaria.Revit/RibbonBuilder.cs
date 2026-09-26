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

        // ---------------------------------------------------------------- Vias
        var via = app.CreateRibbonPanel(TabName, "Vias");
        Split(via, "SvVias", "Via",
            Data(typeof(CmdNovaVia), "Nova Via", "novavia",
                "Desenha uma via nova por pontos (curvas concordadas) com a seção completa. Clique sobre outra via para conectar – ou, depois, puxe a ponta de qualquer eixo até outra via: ela se conecta sozinha (ímã), em qualquer ângulo."),
            Data(typeof(CmdSinalizarVia), "Sinalizar Via", "via",
                "Seção transversal completa a partir de um eixo existente: faixas, ciclofaixas, estacionamento, canteiros, calçadas e toda a sinalização. Modelos prontos e personalizados."),
            Data(typeof(CmdPista), "Pista", "pista",
                "Só a pista dos veículos (asfalto, bloquete ou concreto), já conectada. Monte o resto passo a passo com Calçadas → 'Junto ao bordo de uma via'."),
            Data(typeof(CmdDesenharEixo), "Desenhar Eixo", "eixo",
                "Eixo com a ferramenta nativa Linha de modelo (reta, arco, spline, cadeia, snaps) ou por pontos com encaixe nas vias."));
        Large(via, typeof(CmdConexao), "Conexões", "conexao",
            "Interseções, rotatórias e cul-de-sacs: troque o tipo de uma conexão (clicando nela) ou ajuste todas as interseções do projeto (tipos I a IV, PARE / dê a preferência / semáforo).");
        Split(via, "SvCalcadas", "Calçadas",
            Data(typeof(CmdCalcadas), "Meio-fio, Calçada e Sarjeta", "calcada",
                "Meios-fios, sarjetas, sarjetões, calçadas, grama e faixas de caminhada ao longo de linhas – ou 'Junto ao bordo de uma via', empilhando a partir do bordo (fazem parte da via e das conexões)."),
            Data(typeof(CmdOrelha), "Orelha de Calçada", "orelha",
                "Avanço de calçada sobre o estacionamento, com cada ponta em curva, chanfro ou acompanhando a calçada; meio-fio, canteiro e árvores."),
            Data(typeof(CmdAreaCalcada), "Área de Calçada", "areacalcada",
                "Contorno livre (esquinas, ilhas, parklets, ciclovia no nível da calçada, faixa de serviço) com meio-fio e cantos arredondados."),
            Data(typeof(CmdCanteiro), "Canteiros", "canteiro",
                "Canteiros gramados, jardineiras e grelhas de árvore na faixa de serviço, com árvores."),
            Data(typeof(CmdCulDeSac), "Cul-de-sac", "culdesac",
                "Balão de retorno na ponta de uma via (ligado a ela) ou avulso: circular, excêntrico, gota, T, Y ou L."),
            Data(typeof(CmdRampa), "Rampas", "rampa",
                "Rebaixamentos de calçada (NBR 9050) com abas e piso tátil, e guias rebaixadas de veículos."));
        Split(via, "SvUrbanos", "Elementos Urbanos",
            Data(typeof(CmdMobiliario), "Elementos Urbanos", "mobiliario", "Bancos, lixeiras, postes, árvores, abrigos, paraciclos, hidrantes, floreiras, semáforos..."),
            Data(typeof(CmdElementoFamilia), "Famílias do Revit", "familia",
                "Use suas próprias famílias de componente (mobiliário, luminárias, plantio, modelo genérico...) como elementos urbanos – por cliques ou ao longo de linhas – " +
                "classificadas por subcategoria (iluminação, arborização, bancos, lixeiras...) no Quantitativo. Também classifica famílias já inseridas."));
        StackTwo(via,
            Data(typeof(CmdHierarquia), "Hierarquia e Esquinas", "hierarquia", "Hierarquia viária (CTB art. 60) e raio de concordância das esquinas de vias existentes – aplica às interseções delas."),
            Data(typeof(CmdEixos), "Mostrar/Ocultar Eixos", "eixos", "Mostra ou oculta na vista ativa as linhas de eixo usadas pelas marcas."));

        // ---------------------------------------------------------------- Sinalização horizontal
        var hor = app.CreateRibbonPanel(TabName, "Sinalização Horizontal");
        Split(hor, "SvLinhas", "Linhas",
            Data(typeof(CmdLinhaLongitudinal), "Linha Longitudinal", "linha", "LFO-1 a LFO-4, LMS, LBO, LCO, faixas exclusivas, LCA e LPP."),
            Data(typeof(CmdLinhaTransversal), "Linha Transversal", "transversal", "LRE, LDP, LRV, FTP e demais marcas transversais."),
            Data(typeof(CmdFaixaPedestres), "Faixa de Pedestres", "pedestre", "FTP-1 zebrada ou FTP-2 paralela com linhas de retenção."));
        Large(hor, typeof(CmdZebrado), "Zebrado", "zebrado",
            "Zebrados (ZPA, ZPA-A), chevrons, marcação de área de conflito (MAC, quadriculado) e lombadas em qualquer contorno, com linha de canalização.");
        Split(hor, "SvInscricoes", "Inscrições",
            Data(typeof(CmdSimbolos), "Setas e Símbolos", "seta", "Setas PEM, mudança de faixa, SIA, bicicleta, dê a preferência..."),
            Data(typeof(CmdLegendas), "Legendas", "legenda", "Legendas alongadas (PARE, ÔNIBUS, ESCOLA...) com qualquer fonte."));
        Large(hor, typeof(CmdVagas), "Vagas", "vaga",
            "Vagas paralelas ou em ângulo, PcD, idoso, carga e descarga, ônibus, táxi...");
        Split(hor, "SvComplementos", "Complementos",
            Data(typeof(CmdTachas), "Tachas", "tacha", "Tachas e tachões refletivos ao longo de linhas."),
            Data(typeof(CmdPisoTatil), "Piso Tátil", "tatil", "Piso tátil de alerta e direcional (NBR 16537)."),
            Data(typeof(CmdCiclovia), "Ciclovia / Faixa de Caminhada", "ciclo", "Ciclofaixa uni/bidirecional, ciclovia segregada ou faixa de caminhada completa: fundo, linhas, símbolos, setas e segregação."),
            Data(typeof(CmdCicloviaLinhas), "Marcas de Ciclovia Avulsas", "ciclo", "CIC-LD, CIC-FD, CIC-LC e cruzamento rodocicloviário (MCC) ao longo de linhas."),
            Data(typeof(CmdModeracao), "Quebra-mola e Lombadas", "quebramola", "Ondulações transversais, faixa elevada e lombada invertida."));

        // ---------------------------------------------------------------- Sinalização vertical e dispositivos
        var vert = app.CreateRibbonPanel(TabName, "Sinalização Vertical");
        Split(vert, "SvPlacas", "Placas",
            Data(typeof(CmdPlacas), "Placas", "placa", "Placas de regulamentação, advertência, indicação, educativas, turísticas e de obras, com suporte – em 3D."),
            Data(typeof(CmdDetalharPlacas), "Detalhar Placas", "detalheplaca", "Placa ampliada ao lado do suporte, com chamada e código/nome."),
            Data(typeof(CmdQuadroPlacas), "Quadro de Placas", "quadroplacas", "Símbolo, numeração, código, descrição, dimensões e quantidade de cada placa."),
            Data(typeof(CmdQuadroLegenda), "Quadro de Legenda", "quadrolegenda", "Legenda das placas do projeto, com desenho e descrição."));
        Split(vert, "SvSegregacao", "Segregação",
            Data(typeof(CmdDispositivos), "Bloqueios Físicos", "bloqueio", "Tartarugas, tachões, balizadores, cilindros, pilaretes, New Jersey, separadores..."),
            Data(typeof(CmdGuardRail), "Guard Rail", "guardrail", "Defensas metálicas simples, duplas e de cabos."));

        // ---------------------------------------------------------------- Detalhamento e quantitativos
        var det = app.CreateRibbonPanel(TabName, "Detalhamento");
        Split(det, "SvDetalhar", "Detalhar",
            Data(typeof(CmdAnotar), "Anotar", "anotar", "Chamada com texto automático para qualquer sinalização."),
            Data(typeof(CmdCotarSecao), "Cotar Seção", "cotasecao", "Cadeia de cotas automática atravessando a via."),
            Data(typeof(CmdDetalheTipico), "Detalhe Típico", "detalhetipico", "Detalhe ampliado e cotado de uma marca ou placa."),
            Data(typeof(CmdQuadroQuantitativos), "Quadro de Quantitativos", "quadroqtd", "Tabela de quantidades desenhada na prancha."),
            Data(typeof(CmdNotas), "Notas Gerais", "notas", "Bloco de notas numeradas do projeto."),
            Data(typeof(CmdNorte), "Norte", "norte", "Indicação de norte."));
        Large(det, typeof(CmdQuantitativos), "Quantitativos", "quantitativos",
            "Quantidades por categoria e hierarquia viária (m², m, un, consumo), exportação CSV e tabelas do Revit.");

        // ---------------------------------------------------------------- Editar e configurações
        var edit = app.CreateRibbonPanel(TabName, "Editar");
        Large(edit, typeof(CmdEditar), "Editar", "editar", "Edita os parâmetros de uma marca, via, interseção ou rotatória e a regenera.");
        Stack(edit,
            Data(typeof(CmdAtualizarTodas), "Atualizar Todas", "atualizar", "Regenera todas as marcas do projeto."),
            Data(typeof(CmdSelecionarConjunto), "Selecionar Conjunto", "selecionar", "Seleciona todos os elementos da mesma marca ou da mesma via."),
            Data(typeof(CmdAlternar2D3D), "Alternar 2D/3D", "alternar", "Converte as marcas selecionadas entre modelo 3D e detalhe 2D."));
        Stack(edit,
            Data(typeof(CmdConfiguracoes), "Configurações", "config", "Preferências do plugin (ímã de conexão, pisos do Revit, interseções automáticas...)."),
            Data(typeof(CmdCatalogo), "Catálogo", "catalogo", "Abre o catálogo normativo (JSON) para personalização."),
            Data(typeof(CmdSobre), "Normas / Sobre", "sobre", "Normas de referência e informações do plugin."));
    }

    private static void Split(RibbonPanel panel, string name, string text, params PushButtonData[] items)
    {
        if (panel.AddItem(new SplitButtonData(name, text)) is not SplitButton sb) return;
        sb.IsSynchronizedWithCurrentItem = true;
        foreach (var it in items) sb.AddPushButton(it);
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
