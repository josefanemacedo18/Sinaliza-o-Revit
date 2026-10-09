using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Formulários das obras de infraestrutura (criação e edição), com prévia em planta/3D.</summary>
internal static class InfraForms
{
    private static MarkingGeometry Build(MarkingDefinition d, Polyline2? path) =>
        MarkingBuilder.Build(d, path, new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs });

    private static string Info(MarkingGeometry g, string? extra = null)
    {
        var w = g.Warnings.Distinct().ToList();
        return (extra ?? "") + (w.Count > 0 ? (extra != null ? "\n" : "") + "⚠ " + string.Join("\n⚠ ", w) : "");
    }

    private static FormWindow Percent(this FormWindow w, string label, Func<double> get, Action<double> set, double min, double max, string? tip = null) =>
        w.Number(label, () => get() * 100, v => set(v / 100), min * 100, max * 100, "0.0#", tip);

    private static FormWindow Terrain(this FormWindow w, Func<bool> follow, Action<bool> setFollow, Func<bool> adjust, Action<bool> setAdjust) =>
        w.Section("Topografia (Massa e terreno)", "Com um Toposolid no projeto a obra se apoia no terreno natural e o terreno é ajustado a ela.")
         .Check("Acompanhar o terreno natural (Toposolid)", follow, setFollow)
         .Check("Ajustar o Toposolid ao criar (corte, aterro, reaterro)", adjust, setAdjust);

    /// <summary>Seção da via nova criada com a obra: −1 = pelas faixas do formulário; senão, índice do modelo de via.</summary>
    public static int NewRoadTemplate { get; set; } = -1;
    public static double NewRoadRadius { get; set; } = 150;

    /// <summary>
    /// Onde a obra entra: num trecho de uma via existente (a via vira a obra no trecho) ou numa via nova criada com a
    /// ferramenta de vias (pisos, faixas, conexões) – nos dois casos a obra é hospedada na via.
    /// </summary>
    private static FormWindow HostModes(this FormWindow w, string what)
    {
        var templates = new List<(string, int)> { ("Pelas faixas, acostamentos e passeios acima", -1) };
        templates.AddRange(RoadTemplates.All.Select((t, i) => (t.Name, i)));
        return w.Section("Via da obra",
                $"A {what} é um TRECHO de uma via do plugin: a pista, as calçadas e a sinalização são pisos da via (acompanham o greide) e se conectam " +
                "às outras vias, rotatórias e interseções; a obra gera a estrutura. Numa via existente o greide dela é ajustado no trecho.")
            .Choice("Seção da via nova", templates, () => NewRoadTemplate, v => NewRoadTemplate = v)
            .Number("Raio das curvas ao desenhar o eixo (m)", () => NewRoadRadius, v => NewRoadRadius = v, 0, 5000, "0")
            .Modes(($"Num trecho de uma VIA EXISTENTE (clique a via e as pontas do trecho)", PathMode.ViaExistente),
                   ("Via nova: desenhar o eixo por pontos (encaixa nas vias existentes)", PathMode.Desenhar),
                   ("Via nova: selecionar linhas existentes como eixo", PathMode.ViaNovaLinhas));
    }

    /// <summary>Seção da via da prévia: o modelo escolhido ou a das faixas do formulário.</summary>
    private static RoadSetup PreviewSetup(Func<RoadSetup> own) =>
        NewRoadTemplate >= 0 && NewRoadTemplate < RoadTemplates.All.Count ? RoadTemplates.All[NewRoadTemplate].Create() : own();

    private static List<double>? ParseStations(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var list = text.Split(new[] { ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.TryParse(x.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
            .Where(v => !double.IsNaN(v) && v > 0).OrderBy(v => v).ToList();
        return list.Count == 0 ? null : list;
    }

    private static string StationsText(List<double>? l) =>
        l == null ? "" : string.Join("; ", l.Select(v => v.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))));

    // ------------------------------------------------------------------ drenagem

    public static FormWindow Drainage(DrainageDefinition d, bool edit, bool inlets)
    {
        var types = inlets
            ? new[] { ("Boca de lobo simples (guia chapéu)", TipoDrenagem.BocaDeLoboSimples), ("Boca de lobo dupla (duas bocas e pilarete)", TipoDrenagem.BocaDeLoboDupla),
                      ("Boca de lobo com grelha na sarjeta", TipoDrenagem.BocaDeLoboGrelha), ("Boca de lobo combinada (guia chapéu + grelha)", TipoDrenagem.BocaDeLoboCombinada),
                      ("Poço de visita (PV) com tampão", TipoDrenagem.PocoDeVisita) }
            : new[] { ("Grelha de sarjeta (ralo) com caixa", TipoDrenagem.GrelhaSarjeta), ("Grelha de piso com caixa (calçadas, praças, pátios)", TipoDrenagem.GrelhaQuadrada),
                      ("Canaleta com grelha contínua (ao longo de linha)", TipoDrenagem.GrelhaContinua) };
        if (!types.Any(t => t.Item2 == d.Type)) { d.Type = types[0].Item2; d.ApplyDefaults(); }
        var w = new FormWindow(edit ? "Editar drenagem" : inlets ? "Boca de lobo / PV" : "Grelha de drenagem",
            inlets ? "Bocas de lobo e poços de visita" : "Grelhas e canaletas de drenagem",
            "Clique junto ao MEIO-FIO (de uma via, interseção, rotatória, extensão de calçada ou meio-fio avulso): o dispositivo encaixa na face da guia, " +
            "adota a altura dela e SUBSTITUI o trecho de pavimento, sarjeta, meio-fio e calçada que ocupa (rebaixo, guia chapéu, laje e tampa). " +
            "Em série: clique o início e o fim no mesmo meio-fio e os dispositivos saem a cada espaçamento. Grelhas com barras transversais ao fluxo são seguras para ciclistas.",
            d, () =>
            {
                var g = DrainageGenerator.Demo(d);
                return new FormPreview(g, null, null, Info(g, "Prévia numa rua de exemplo: pista, sarjeta, meio-fio e calçada recortados. Use 3D para ver a caixa."));
            }, false, edit ? "Aplicar" : "Inserir", 1080, 760);
        w.Choice("Tipo", types, () => d.Type, v => d.Type = v, preset: v => { d.Type = v; d.ApplyDefaults(); });
        bool Pv() => d.Type == TipoDrenagem.PocoDeVisita;
        bool Opening() => d.Type is TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla or TipoDrenagem.BocaDeLoboCombinada;
        bool HasGrate() => !Pv() && d.Type is not (TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla);
        bool AtCurb() => !Pv() && d.Type is not (TipoDrenagem.GrelhaQuadrada or TipoDrenagem.GrelhaContinua);
        w.If(() => !Pv() && !d.IsLinear, x => x.Integer("Módulos lado a lado", () => d.Modules, v => d.Modules = v, 1, 4, "Bocas de lobo triplas, grelhas em série."));
        w.If(HasGrate, x => x.Section("Grelha")
             .Number("Comprimento (m)", () => d.GrateLength, v => d.GrateLength = v, 0.2, 3)
             .Number("Largura (m)", () => d.GrateWidth, v => d.GrateWidth = v, 0.1, 2)
             .Choice("Desenho do tampo", new[] { ("Barras com nervura central", EstiloGrelha.Barras), ("Malha quadriculada", EstiloGrelha.Malha), ("Chapa com fendas (pedestres)", EstiloGrelha.Fendas) },
                () => d.GrateStyle, v => d.GrateStyle = v)
             .If(() => d.GrateStyle == EstiloGrelha.Barras, y => y.Choice("Barras", new[] { ("Transversais ao fluxo (ciclistas)", OrientacaoBarras.Transversal), ("Longitudinais", OrientacaoBarras.Longitudinal), ("Diagonais", OrientacaoBarras.Diagonal) },
                () => d.Bars, v => d.Bars = v))
             .Number("Largura das barras (m)", () => d.BarWidth, v => d.BarWidth = v, 0.01, 0.1, "0.000")
             .Number("Vão entre barras / fendas (m)", () => d.BarGap, v => d.BarGap = v, 0.008, 0.1, "0.000", "Até 25 mm em vias com bicicletas; 10–15 mm em calçadas.")
             .Number("Largura do aro (m)", () => d.FrameWidth, v => d.FrameWidth = v, 0.02, 0.15)
             .If(AtCurb, y => y.Number("Afastamento da guia (m)", () => d.GrateOffset, v => d.GrateOffset = v, 0.02, 1.0, "0.00", "Distância da face do meio-fio à grelha."))
             .Choice("Material", new[] { ("Ferro fundido dúctil (NBR 10160)", MaterialGrelha.FerroFundido), ("Aço galvanizado", MaterialGrelha.AcoGalvanizado), ("Concreto", MaterialGrelha.Concreto) },
                () => d.Material, v => d.Material = v));
        w.If(Opening, x => x.Section("Boca na guia (guia chapéu)")
             .Number("Comprimento da boca (m)", () => d.OpeningLength, v => d.OpeningLength = v, 0.5, 6)
             .Number("Altura livre da boca (m)", () => d.OpeningHeight, v => d.OpeningHeight = v, 0.08, 0.3));
        w.If(AtCurb, x => x.Section("Sarjeta e meio-fio")
             .Number("Rebaixo da sarjeta (m)", () => d.Depression, v => d.Depression = v, 0, 0.2, "0.00", "Depressão junto à boca: aumenta a captação (PMSP: 5–10 cm).")
             .Number("Transição do rebaixo (m)", () => d.DepressionLength, v => d.DepressionLength = v, 0.2, 5)
             .Number("Largura do rebaixo (m)", () => d.GutterWidth, v => d.GutterWidth = v, 0.3, 1.5)
             .Number("Altura do meio-fio (m)", () => d.CurbHeight, v => d.CurbHeight = v, 0.05, 0.3)
             .Number("Largura do meio-fio (m)", () => d.CurbWidth, v => d.CurbWidth = v, 0.08, 0.3)
             .Check("Adotar a altura e a largura do meio-fio clicado", () => d.AutoFit, v => d.AutoFit = v));
        w.If(() => !d.IsLinear, x => x.Section("Caixa / câmara")
             .Number("Comprimento interno / diâmetro do PV (m)", () => d.BoxLength, v => d.BoxLength = v, 0.3, 6)
             .If(() => !Pv(), y => y.Number("Largura interna (m)", () => d.BoxWidth, v => d.BoxWidth = v, 0.3, 3)));
        w.If(() => d.IsLinear, x => x.Section("Canaleta"));
        w.Number("Profundidade (m)", () => d.BoxDepth, v => d.BoxDepth = v, 0.15, 8)
         .Number("Espessura das paredes (m)", () => d.WallThickness, v => d.WallThickness = v, 0.06, 0.4);
        w.If(Opening, x => x.Number("Laje de cobertura (m)", () => d.SlabThickness, v => d.SlabThickness = v, 0.06, 0.3)
             .Check("Tampa de inspeção na calçada", () => d.Lid, v => d.Lid = v)
             .Choice("Tampa", new[] { ("Concreto com alças (rente à calçada)", TipoTampa.Concreto), ("Tampão de ferro fundido com relevo", TipoTampa.FerroFundido) },
                () => d.LidType == TipoTampa.Nenhuma ? TipoTampa.Concreto : d.LidType, v => d.LidType = v)
             .Number("Lado da tampa (m)", () => d.LidDiameter, v => d.LidDiameter = v, 0.4, 1.2));
        w.If(Pv, x => x.Number("Diâmetro do tampão (m)", () => d.LidDiameter, v => d.LidDiameter = v, 0.5, 1.0, "0.00", "NBR 10160: Ø 0,60 m livre.")
             .Check("Degraus de ferro", () => d.Steps, v => d.Steps = v));
        w.If(() => !d.IsLinear, x => x.Check("Tubo(s) de ligação", () => d.OutletPipe, v => d.OutletPipe = v)
             .Number("Diâmetro do tubo (m)", () => d.PipeDiameter, v => d.PipeDiameter = v, 0.2, 1.5, "0.00", "Usual ≥ 0,40 m em redes urbanas.")
             .Number("Comprimento do tubo (m)", () => d.PipeLength, v => d.PipeLength = v, 0.5, 30)
             .If(AtCurb, y => y.Choice("Saída do tubo", new[] { ("Sob a pista (ramal até a galeria)", SaidaTubo.SobAPista), ("Ao longo do meio-fio", SaidaTubo.AoLongo) },
                () => d.PipeDirection, v => d.PipeDirection = v)));
        w.Section("Encaixe na via")
         .Check("Recortar pavimento, sarjeta, meio-fio, calçada e pinturas sob o dispositivo", () => d.CutFloors, v => d.CutFloors = v);
        if (!edit)
        {
            w.If(AtCurb, x => x.Number("Espaçamento na série (m)", () => d.SeriesSpacing, v => d.SeriesSpacing = v, 5, 200, "0", "Usual 30–60 m, conforme a vazão da sarjeta.")
                .Modes(("Um a um: clicar junto ao meio-fio", PathMode.DoisPontos), ("Em série ao longo do meio-fio (início e fim)", PathMode.Serie)));
            w.If(() => d.IsLinear, x => x.Modes(("Canaleta: desenhar a linha", PathMode.Desenhar), ("Canaleta: selecionar linhas", PathMode.Linhas)));
            w.If(() => d.Type is TipoDrenagem.PocoDeVisita or TipoDrenagem.GrelhaQuadrada, x => x.Modes(("Clicar o ponto (repete até ESC)", PathMode.DoisPontos)));
        }
        return w;
    }

    // ------------------------------------------------------------------ viaduto / ponte / passarela

    public static FormWindow Bridge(BridgeDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? $"Editar {d.KindName.ToLowerInvariant()}" : d.KindName, "Viaduto, ponte ou passarela",
            "A obra é um trecho de uma VIA do plugin: escolha uma via existente (clique a via e as pontas do trecho) ou desenhe o eixo de uma via nova. " +
            "A pista, as calçadas e a sinalização são pisos da via, no greide; a obra gera tabuleiro, vigas, pilares, encontros e guarda-corpos. " +
            "Aterros e taludes são feitos no terreno nativo (Massa e terreno → Sólido topográfico).",
            d, () =>
            {
                var c = (BridgeDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.FollowTerrain = true; c.GroundLine = null;
                var run = Math.Abs(c.Height) / Math.Max(0.02, c.MaxGrade);
                var len = Math.Max(160, 2 * run + Math.Max(60, 3 * c.SpanLength));
                var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) });
                // Terreno de exemplo: um vale no meio do eixo (rio ou via mais baixa).
                double Ground(Vec2 p) => -Math.Max(4, c.Height * 0.8) * Math.Exp(-Math.Pow((p.X - len / 2) / Math.Max(25, len * 0.18), 2));
                var (grade, s0, s1, warn) = InfraRoads.BridgeGrade(c, axis, Ground);
                c.HostStart = s0; c.HostEnd = s1;
                if (c.Water) c.WaterLevel = Ground(new Vec2(len / 2, 0)) + 0.8;
                var g = InfraDemo.Preview(PreviewSetup(() => InfraRoads.Setup(c)), axis, grade, new MarkingDefinition[] { c }, PluginContext.Catalog, Ground);
                g.Warnings.AddRange(warn);
                return new FormPreview(g, null, null, Info(g, $"Exemplo: via de {len:0} m sobre um vale; obra de {s1 - s0:0} m (estacas {s0:0} a {s1:0})."));
            }, false, edit ? "Aplicar" : "Inserir", 1180, 780);
        w.Choice("Obra", new[] { ("Viaduto", TipoObraDeArte.Viaduto), ("Ponte", TipoObraDeArte.Ponte), ("Passarela de pedestres", TipoObraDeArte.Passarela) },
                () => d.Kind, v => d.Kind = v, preset: v => { d.Kind = v; d.ApplyKindDefaults(); })
         .Choice("Sistema estrutural", new[]
            {
                ("Vigas pré-moldadas (longarinas I)", SistemaEstrutural.VigasPreMoldadas), ("Viga caixão", SistemaEstrutural.CaixaoCelular),
                ("Laje maciça (vãos curtos)", SistemaEstrutural.LajeMacica), ("Arco inferior", SistemaEstrutural.ArcoInferior),
                ("Arco superior atirantado", SistemaEstrutural.ArcoSuperior), ("Estaiada (mastro em H)", SistemaEstrutural.Estaiada),
                ("Treliça metálica", SistemaEstrutural.Trelica),
            }, () => d.System, v => d.System = v)
         .Section("Tabuleiro")
         .Integer("Faixas de rolamento", () => d.Lanes, v => d.Lanes = v, 0, 8, "0 = passarela.")
         .Number("Largura da faixa (m)", () => d.LaneWidth, v => d.LaneWidth = v, 1.2, 4.5, "0.00", "Passarela: largura livre.")
         .Number("Faixa de segurança / acostamento (m)", () => d.ShoulderWidth, v => d.ShoulderWidth = v, 0, 3.5)
         .Number("Passeio de cada lado (m)", () => d.SidewalkWidth, v => d.SidewalkWidth = v, 0, 5)
         .Choice("Proteção lateral", new[] { ("Barreira New Jersey", TipoGuarda.NewJersey), ("Guarda-corpo metálico", TipoGuarda.GuardaCorpoMetalico),
                 ("New Jersey + guarda-corpo", TipoGuarda.NewJerseyComGuardaCorpo) }, () => d.Barrier, v => d.Barrier = v)
         .Number("Espessura da laje (m)", () => d.DeckThickness, v => d.DeckThickness = v, 0.15, 0.6)
         .Number("Altura das vigas (m, 0 = vão/16)", () => d.GirderDepth, v => d.GirderDepth = v, 0, 6)
         .Number("Espaçamento das vigas (m)", () => d.GirderSpacing, v => d.GirderSpacing = v, 1.5, 5)
         .Percent("Caimento transversal (%)", () => d.CrossSlope, v => d.CrossSlope = v, 0, 0.08, "Abaulamento de duas águas a partir do eixo (2 % usual).")
         .Choice("Guarda-corpo (passeios e passarela)", new[] { ("Tubular", TipoGuardaCorpo.Tubular), ("Balaústres (vão ≤ 11 cm)", TipoGuardaCorpo.Balaustres), ("Vidro laminado", TipoGuardaCorpo.Vidro) },
            () => d.RailingStyle, v => d.RailingStyle = v)
         .Check("Buzinotes (drenos do tabuleiro)", () => d.Drains, v => d.Drains = v)
         .Check("Faixas pintadas", () => d.LaneMarkings, v => d.LaneMarkings = v)
         .Check("Iluminação", () => d.Lighting, v => d.Lighting = v)
         .Number("Espaçamento dos postes (m)", () => d.LightSpacing, v => d.LightSpacing = v, 10, 80)
         .Section("Greide e acessos")
         .Choice("Perfil da obra", new[]
            {
                ("Rampas de acesso até a altura, trecho em nível (viaduto)", PerfilObra.RampasDeAcesso), ("Horizontal na altura indicada (sem rampas)", PerfilObra.Horizontal),
                ("Reta de uma margem à outra (ponte sobre vale/rio)", PerfilObra.EntreMargens), ("Convexo – curva vertical única com o alto no meio", PerfilObra.Convexo),
                ("Inclinado – rampa constante da altura do início à do fim", PerfilObra.Inclinado),
                ("Côncavo – ponto baixo no meio (vale entre cristas)", PerfilObra.Concavo),
                ("Personalizado – PIVs digitados", PerfilObra.Personalizado),
                ("Manter o greide da via existente", PerfilObra.GreideDaVia),
            }, () => d.ProfileKind, v => d.ProfileKind = v)
         .Check("Traçado reto (só as pontas do eixo desenhado – obra em tangente)", () => d.StraightAxis, v => d.StraightAxis = v)
         .If(() => d.ProfileKind is PerfilObra.RampasDeAcesso or PerfilObra.Horizontal,
             x => x.Number("Altura do tabuleiro sobre a base (m)", () => d.Height, v => d.Height = v, 2, 120, "0.00", "Gabarito vertical livre ≥ 5,50 m sobre rodovias (DNIT)."))
         .If(() => d.ProfileKind == PerfilObra.Inclinado,
             x => x.Number("Altura do tabuleiro no INÍCIO (m sobre a base)", () => d.Height, v => d.Height = v, -50, 120, "0.00"))
         .If(() => d.ProfileKind == PerfilObra.Convexo,
             x => x.Number("Cota do ponto ALTO no meio (m sobre a base)", () => d.Height, v => d.Height = v, -50, 120, "0.00"))
         .If(() => d.ProfileKind == PerfilObra.Concavo,
             x => x.Number("Cota do ponto BAIXO no meio (m sobre a base)", () => d.Height, v => d.Height = v, -50, 120, "0.00", "Abaixo das cabeceiras – ponte em \"barriga\" sobre o vale."))
         .If(() => d.ProfileKind == PerfilObra.Inclinado,
             x => x.Number("Altura do tabuleiro no FIM (m sobre a base)", () => d.EndHeight, v => d.EndHeight = v, -50, 120, "0.00"))
         .If(() => d.ProfileKind == PerfilObra.Personalizado,
             x => x.Text("PIVs – uma linha por PIV: estaca (a partir do início da obra); cota; curva", () => d.ProfilePvis, v => d.ProfilePvis = v ?? "", true,
                 "Ex.: 0; 6\n40; 9; 60\n120; 7"))
         .If(() => d.ProfileKind is PerfilObra.RampasDeAcesso or PerfilObra.Inclinado, x => x
             .Check("Rampa de acesso no início", () => d.ApproachStart, v => d.ApproachStart = v)
             .Check("Rampa de acesso no fim", () => d.ApproachEnd, v => d.ApproachEnd = v))
         .Percent("Rampa máxima (%)", () => d.MaxGrade, v => d.MaxGrade = v, 0.01, 0.12, "Veículos: 5–6 %; passarelas: ≤ 8,33 % (NBR 9050).")
         .Number("Curva vertical (m)", () => d.VerticalCurve, v => d.VerticalCurve = v, 0, 300)
         .Check("Acessos em terra armada (muros) em vez de taludes", () => d.ApproachWalls, v => d.ApproachWalls = v)
         .Number("Talude dos aterros (H : 1 V)", () => d.FillSlope, v => d.FillSlope = v, 0.5, 4, "0.0")
         .Section("Vãos e pilares")
         .Number("Vão típico (m)", () => d.SpanLength, v => d.SpanLength = v, 8, 200, "0")
         .Number("Vão principal – arcos e estaiadas (m, 0 = toda a obra)", () => d.MainSpan, v => d.MainSpan = v, 0, 1000, "0")
         .Choice("Pilares", new[] { ("Pórtico (dois pilares + travessa)", TipoPilar.Portico), ("Circular único", TipoPilar.Circular), ("Dois circulares + travessa", TipoPilar.DuplaCircular),
                 ("Parede (pontas arredondadas)", TipoPilar.Parede), ("Martelo (capitel)", TipoPilar.Martelo), ("Em Y", TipoPilar.Y), ("Oblongo", TipoPilar.Oblongo) },
                 () => d.PierType, v => d.PierType = v)
         .Number("Dimensão dos pilares (m)", () => d.PierSize, v => d.PierSize = v, 0.4, 6)
         .Number("Esconsidade dos apoios (°)", () => d.Skew, v => d.Skew = v, -60, 60, "0",
            "Pilares e encontros paralelos ao rio ou à via cruzada (0 = perpendiculares ao eixo).")
         .Text("Pilares nas estações (m a partir do início, separadas por ;)", () => StationsText(d.PierStations), v => d.PierStations = ParseStations(v),
            tooltip: "Vazio = distribuídos pelo vão típico. Use para desviar de vias, rios e redes sob a obra.")
         .Check("Tabuleiro contínuo (juntas só nos encontros)", () => d.Continuous, v => d.Continuous = v)
         .If(() => d.System == SistemaEstrutural.CaixaoCelular, x => x.Check("Caixão com altura variável (mísulas)", () => d.VariableDepth, v => d.VariableDepth = v))
         .If(() => d.System == SistemaEstrutural.Estaiada, x => x
             .Choice("Mastro", new[] { ("Em H (dois fustes e travessas)", FormaMastro.H), ("Em A (fustes unidos no topo)", FormaMastro.A), ("Central (canteiro)", FormaMastro.Central) },
                () => d.Pylon, v => d.Pylon = v)
             .Choice("Estais", new[] { ("Em leque", ArranjoEstais.Leque), ("Em harpa (paralelos)", ArranjoEstais.Harpa) }, () => d.Stays, v => d.Stays = v))
         .If(() => d.System is SistemaEstrutural.ArcoInferior or SistemaEstrutural.ArcoSuperior,
             x => x.Percent("Flecha do arco / vão (%)", () => d.ArchRise, v => d.ArchRise = v, 0.08, 0.40))
         .Choice("Alas dos encontros", new[] { ("Abertas (acompanham o talude)", TipoAla.Abertas), ("Paralelas ao eixo", TipoAla.Paralelas) }, () => d.WingWalls, v => d.WingWalls = v)
         .Section("Rio (pontes)")
         .Check("Lâmina d'água sob a obra", () => d.Water, v => d.Water = v)
         .Number("Cota da água sobre a base (m)", () => d.WaterLevel, v => d.WaterLevel = v, -100, 100)
         .Number("Largura do rio (m)", () => d.WaterWidth, v => d.WaterWidth = v, 2, 2000, "0")
         .Terrain(() => d.FollowTerrain, v => d.FollowTerrain = v, () => d.AdjustTerrain, v => d.AdjustTerrain = v);
        if (!edit) w.HostModes(d.KindName.ToLowerInvariant());
        return w;
    }

    // ------------------------------------------------------------------ túnel

    public static FormWindow Tunnel(TunnelDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar túnel" : "Túnel", "Túnel rodoviário",
            "O túnel é um trecho de uma VIA do plugin (existente ou nova): a pista e os passeios são pisos da via; o túnel gera revestimento, " +
            "emboques, iluminação, ventiladores, eletrocalhas e nichos SOS. Numa via nova o trecho em túnel sai onde o terreno cobre a abóbada.",
            d, () =>
            {
                var c = (TunnelDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.GroundLine = null;
                var len = 360.0;
                var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) });
                double Ground(Vec2 p) => 24 * Math.Exp(-Math.Pow((p.X - len / 2) / 85, 2));
                var grade = new RoadGrade { Points = { new(0, 0), new(len, 0) } };
                var (_, crown, half) = EarthworksGenerator.TunnelSection(c);
                var (s0, s1) = InfraRoads.TunnelRange(axis, grade, p => Ground(p), crown) ?? (100, 260);
                c.HostStart = s0; c.HostEnd = s1;
                var g = InfraDemo.Preview(PreviewSetup(() => InfraRoads.Setup(c)), axis, grade, new MarkingDefinition[] { c }, PluginContext.Catalog, Ground);
                return new FormPreview(g, null, null, Info(g, $"Largura livre {2 * half:0.00} m; altura até a coroa {crown:0.00} m. Exemplo: túnel de {s1 - s0:0} m sob um morro."));
            }, false, edit ? "Aplicar" : "Inserir", 1080, 740);
        w.Choice("Seção", new[] { ("Ferradura (NATM)", SecaoTunel.Ferradura), ("Circular (TBM)", SecaoTunel.Circular), ("Retangular (vala coberta)", SecaoTunel.Retangular) },
                () => d.Section, v => d.Section = v)
         .Integer("Faixas", () => d.Lanes, v => d.Lanes = v, 1, 6)
         .Number("Largura da faixa (m)", () => d.LaneWidth, v => d.LaneWidth = v, 2.5, 4.5)
         .Number("Acostamento/faixa de segurança (m)", () => d.ShoulderWidth, v => d.ShoulderWidth = v, 0, 3)
         .Number("Passeio de serviço (m)", () => d.WalkwayWidth, v => d.WalkwayWidth = v, 0, 2)
         .Number("Gabarito vertical livre (m)", () => d.ClearHeight, v => d.ClearHeight = v, 3, 8, "0.00", "≥ 5,50 m em rodovias.")
         .Number("Espessura do revestimento (m)", () => d.LiningThickness, v => d.LiningThickness = v, 0.2, 1.5)
         .Check("Rampa constante entre os emboques (senão, segue o greide da via)", () => d.StraightAxis, v => d.StraightAxis = v)
         .Number("Via nova: cota da pista no início (m)", () => d.StartZ, v => d.StartZ = v, -200, 200)
         .Number("Via nova: cota da pista no fim (m)", () => d.EndZ, v => d.EndZ = v, -200, 200)
         .Choice("Emboques", new[] { ("Parede de testa", TipoEmboque.Testa), ("Bisel (acompanha o talude)", TipoEmboque.Bisel), ("Pala em balanço", TipoEmboque.Pala) },
            () => d.Portal, v => d.Portal = v)
         .Number("Comprimento da pala/bisel (m)", () => d.PortalLength, v => d.PortalLength = v, 1, 40)
         .Number("Trincheira de acesso diante de cada emboque (m)", () => d.ApproachCut, v => d.ApproachCut = v, 0, 300)
         .Check("Iluminação", () => d.Lighting, v => d.Lighting = v)
         .Check("Ventiladores de jato", () => d.JetFans, v => d.JetFans = v)
         .Number("Espaçamento dos ventiladores (m)", () => d.FanSpacing, v => d.FanSpacing = v, 30, 500, "0")
         .Terrain(() => d.FollowTerrain, v => d.FollowTerrain = v, () => d.AdjustTerrain, v => d.AdjustTerrain = v);
        if (!edit) w.HostModes("túnel");
        return w;
    }

    // ------------------------------------------------------------------ trincheira

    public static FormWindow Trench(TrenchDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar trincheira" : "Trincheira", "Trincheira (via rebaixada)",
            "A trincheira é um trecho de uma VIA do plugin: no trecho escolhido a pista desce pela rampa máxima até o rebaixo, entre muros de contenção " +
            "com guarda-corpo, e sobe no fim. Opcional: laje de travessia para a via transversal. O terreno entre os muros é escavado no Toposolid.",
            d, () =>
            {
                var c = (TrenchDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.FollowTerrain = false; c.GroundLine = null;
                var len = 2 * c.Depth / Math.Max(0.02, c.MaxGrade) + 80;
                var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) });
                var grade = InfraRoads.TrenchOnRoad(RoadGrade.Flat(len), 0, len, c.Depth, c.MaxGrade, c.VerticalCurve, _ => 0);
                c.HostStart = 0; c.HostEnd = len;
                var g = InfraDemo.Preview(PreviewSetup(() => InfraRoads.Setup(c)), axis, grade, new MarkingDefinition[] { c }, PluginContext.Catalog, _ => 0);
                return new FormPreview(g, null, null, Info(g, $"Exemplo com {len:0} m de eixo."));
            }, false, edit ? "Aplicar" : "Inserir", 1080, 740);
        w.Integer("Faixas", () => d.Lanes, v => d.Lanes = v, 1, 6)
         .Number("Largura da faixa (m)", () => d.LaneWidth, v => d.LaneWidth = v, 2.5, 4.5)
         .Number("Faixa de segurança (m)", () => d.ShoulderWidth, v => d.ShoulderWidth = v, 0, 3)
         .Number("Rebaixo da pista (m)", () => d.Depth, v => d.Depth = v, 1, 20, "0.00", "Gabarito 5,50 m + laje de travessia ≈ 6,5 m.")
         .Percent("Rampa máxima (%)", () => d.MaxGrade, v => d.MaxGrade = v, 0.02, 0.10)
         .Number("Curva vertical (m)", () => d.VerticalCurve, v => d.VerticalCurve = v, 0, 200)
         .Choice("Contenção", new[] { ("Muros de flexão (concreto armado)", TipoContencaoTrincheira.Flexao), ("Cortina atirantada", TipoContencaoTrincheira.CortinaAtirantada),
                 ("Terra armada (placas)", TipoContencaoTrincheira.TerraArmada) }, () => d.Wall, v => d.Wall = v)
         .Number("Espessura dos muros (m)", () => d.WallThickness, v => d.WallThickness = v, 0.2, 1.5)
         .Number("Guarda-corpo sobre os muros (m)", () => d.ParapetHeight, v => d.ParapetHeight = v, 0.9, 2)
         .Number("Laje de travessia – comprimento (m, 0 = sem)", () => d.CoverLength, v => d.CoverLength = v, 0, 200)
         .Number("Laje de travessia – posição (0 a 1)", () => d.CoverAt, v => d.CoverAt = v, 0.05, 0.95)
         .Check("Canaletas ao pé dos muros", () => d.Drainage, v => d.Drainage = v)
         .Check("Iluminação", () => d.Lighting, v => d.Lighting = v)
         .Terrain(() => d.FollowTerrain, v => d.FollowTerrain = v, () => d.AdjustTerrain, v => d.AdjustTerrain = v);
        if (!edit) w.HostModes("trincheira");
        return w;
    }

    // ------------------------------------------------------------------ muro de arrimo

    public static FormWindow Wall(RetainingWallDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar muro de arrimo" : "Muro de arrimo", "Muro de arrimo",
            "Desenhe a linha da FACE do muro (pé, do lado do terreno baixo). O solo contido fica do lado escolhido; a base acompanha o terreno " +
            "e o reaterro atrás do muro é levado até o topo no Toposolid.",
            d, () =>
            {
                var c = (RetainingWallDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.FollowTerrain = false; c.GroundLine = null;
                var g = Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(12, 0) }));
                return new FormPreview(g, null, null, Info(g, $"Área de face no exemplo (12 m): {g.AreaByColor.Values.Sum():0.0} m²."));
            }, false, edit ? "Aplicar" : "Inserir", 1080, 720);
        w.Choice("Tipo", new[]
            {
                ("Flexão (concreto armado, sapata)", TipoMuro.Flexao), ("Gravidade (concreto ciclópico)", TipoMuro.Gravidade), ("Com contrafortes", TipoMuro.Contrafortes),
                ("Gabião (degraus)", TipoMuro.Gabiao), ("Terra armada (placas)", TipoMuro.TerraArmada), ("Cortina atirantada", TipoMuro.CortinaAtirantada),
            }, () => d.Type, v => d.Type = v)
         .Number("Altura no início (m)", () => d.HeightStart, v => d.HeightStart = v, 0.5, 30)
         .Number("Altura no fim (m)", () => d.HeightEnd, v => d.HeightEnd = v, 0.5, 30)
         .Number("Largura no topo (m)", () => d.TopWidth, v => d.TopWidth = v, 0.15, 2)
         .Number("Largura da base/sapata (m, 0 = automática)", () => d.BaseWidth, v => d.BaseWidth = v, 0, 20)
         .Number("Espessura da sapata (m)", () => d.FootingThickness, v => d.FootingThickness = v, 0.25, 2)
         .Number("Ponta da sapata (m)", () => d.ToeLength, v => d.ToeLength = v, 0, 5)
         .Number("Ficha – embutimento (m)", () => d.Embedment, v => d.Embedment = v, 0, 5)
         .Number("Espaçamento dos contrafortes (m)", () => d.CounterfortSpacing, v => d.CounterfortSpacing = v, 1.5, 10)
         .Number("Módulo do gabião (m)", () => d.GabionSize, v => d.GabionSize = v, 0.5, 1.5)
         .Number("Placa da terra armada (m)", () => d.PanelSize, v => d.PanelSize = v, 0.75, 2.5)
         .Number("Espaçamento dos tirantes (m)", () => d.AnchorSpacing, v => d.AnchorSpacing = v, 1.5, 6)
         .Check("Solo contido à ESQUERDA da linha (senão, à direita)", () => d.RetainedLeft, v => d.RetainedLeft = v)
         .Check("Coroamento", () => d.Coping, v => d.Coping = v)
         .Check("Barbacãs (drenos a cada 2 m)", () => d.WeepHoles, v => d.WeepHoles = v)
         .Check("Guarda-corpo no topo", () => d.Guardrail, v => d.Guardrail = v)
         .Number("Largura do reaterro no topo (m)", () => d.BackfillWidth, v => d.BackfillWidth = v, 1, 50)
         .Terrain(() => d.FollowTerrain, v => d.FollowTerrain = v, () => d.AdjustTerrain, v => d.AdjustTerrain = v);
        if (!edit) w.Modes(("Desenhar a linha por pontos", PathMode.Desenhar), ("Selecionar linhas existentes", PathMode.Linhas), ("Dois cliques", PathMode.DoisPontos));
        return w;
    }

    // ------------------------------------------------------------------ talude

    public static FormWindow Slope(SlopeDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar talude" : "Talude", "Talude de corte ou aterro",
            "Desenhe a linha do PÉ do talude; ele sobe para o lado escolhido com a inclinação, as bermas e o revestimento. " +
            "No Toposolid, a face do talude (com as bermas) é aplicada e concordada com o terreno natural no pé e na crista.",
            d, () =>
            {
                var c = (SlopeDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.FollowTerrain = false; c.GroundLine = null;
                var g = Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(30, 0) }));
                var prof = EarthworksGenerator.SlopeProfile(c);
                return new FormPreview(g, null, null, Info(g, $"Largura em planta: {prof[^1].Y:0.0} m para {prof[^1].Z:0.0} m de altura."));
            }, false, edit ? "Aplicar" : "Inserir", 1080, 720);
        w.Choice("Tipo", new[] { ("Aterro", TipoTalude.Aterro), ("Corte", TipoTalude.Corte) }, () => d.Type, v => d.Type = v,
                preset: v => { d.Type = v; d.Ratio = v == TipoTalude.Corte ? 1.0 : 1.5; })
         .Number("Altura (m)", () => d.Height, v => d.Height = v, 0.5, 100)
         .Number("Inclinação (H : 1 V)", () => d.Ratio, v => d.Ratio = v, 0.2, 5, "0.0#", "Aterro 1,5 : 1; corte em solo 1 : 1 (DNIT).")
         .Number("Berma a cada (m de altura, 0 = sem)", () => d.BermEvery, v => d.BermEvery = v, 0, 30)
         .Number("Largura da berma (m)", () => d.BermWidth, v => d.BermWidth = v, 0, 10)
         .Choice("Revestimento", new[] { ("Grama / hidrossemeadura", RevestimentoTalude.Grama), ("Concreto projetado", RevestimentoTalude.ConcretoProjetado),
                 ("Enrocamento (rip-rap)", RevestimentoTalude.Enrocamento), ("Solo exposto", RevestimentoTalude.SoloExposto) }, () => d.Lining, v => d.Lining = v)
         .Check("Canaleta de crista", () => d.CrestChannel, v => d.CrestChannel = v)
         .Check("Canaleta de pé", () => d.ToeChannel, v => d.ToeChannel = v)
         .Check("Canaletas nas bermas", () => d.BermChannels, v => d.BermChannels = v)
         .Number("Descidas d'água a cada (m, 0 = sem)", () => d.DowndrainSpacing, v => d.DowndrainSpacing = v, 0, 200)
         .Check("O talude sobe para a ESQUERDA da linha (senão, para a direita)", () => d.UphillLeft, v => d.UphillLeft = v)
         .If(() => d.Type == TipoTalude.Corte, x => x.Number("Corte: plataforma rebaixada à frente do pé (m)", () => d.ToePlatform, v => d.ToePlatform = v, 0, 50, "0.0",
             "No corte a linha desenhada é o PÉ do talude; a crista fica no terreno natural e o terreno à frente do pé é rebaixado até a cota do pé."))
         .Terrain(() => d.FollowTerrain, v => d.FollowTerrain = v, () => d.AdjustTerrain, v => d.AdjustTerrain = v);
        if (!edit) w.Modes(("Desenhar a linha por pontos", PathMode.Desenhar), ("Selecionar linhas existentes", PathMode.Linhas), ("Dois cliques", PathMode.DoisPontos));
        return w;
    }

    // ------------------------------------------------------------------ nó viário

    public static FormWindow Interchange(InterchangeDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar nó viário" : "Nó viário", "Interseção em desnível (nó viário) sobre as vias",
            "Clique o CRUZAMENTO de duas vias do plugin: a via de cima ganha greide e viaduto (esconso conforme o ângulo) e cada ramo ou laço " +
            "vira uma VIA do plugin – pista, faixas, bordos, defensas, greide – ligada às duas por faixas de desaceleração/aceleração com taper, " +
            "interseções ou rotatórias. Num ponto livre as duas vias são criadas antes. Tudo continua editável como via.",
            d, () =>
            {
                var c = (InterchangeDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.Position = Vec2.Zero; c.FollowTerrain = false; c.AngleDeg = 0;
                c.MainRoad = null; c.CrossRoad = null; c.MainLength = 1100; c.CrossLength = 1000;
                var g = InterchangeDemo.Build(c, PluginContext.Catalog);
                return new FormPreview(g, null, null, Info(g, "Exemplo sobre uma rodovia de pista dupla e uma via transversal (as suas vias entram no lugar delas)."));
            }, false, edit ? "Aplicar" : "Inserir", 1240, 800);
        w.Choice("Tipo", new[]
            {
                ("Diamante", TipoNoViario.Diamante), ("Diamante com rotatórias", TipoNoViario.DiamanteRotatorias), ("Trevo completo", TipoNoViario.TrevoCompleto),
                ("Trevo parcial (parclo)", TipoNoViario.TrevoParcial), ("Trombeta (entroncamento em T)", TipoNoViario.Trombeta),
                ("Rotatória em dois níveis", TipoNoViario.RotatoriaElevada),
            }, () => d.Type, v => d.Type = v)
         .Number("Ângulo entre as vias (°)", () => d.CrossAngleDeg, v => d.CrossAngleDeg = v, 45, 135, "0")
         .Check("Via principal por baixo (transversal em viaduto)", () => d.MainBelow, v => d.MainBelow = v)
         .Section("Vias criadas num ponto livre", "Usadas só quando não há vias no ponto clicado; com vias existentes valem as seções delas.")
         .Integer("Faixas por sentido", () => d.MainLanes, v => d.MainLanes = v, 1, 5)
         .Number("Largura da faixa (m)", () => d.MainLaneWidth, v => d.MainLaneWidth = v, 3, 4)
         .Number("Canteiro central (m)", () => d.MainMedian, v => d.MainMedian = v, 0.6, 20)
         .Number("Acostamento (m)", () => d.MainShoulder, v => d.MainShoulder = v, 0, 3.5)
         .Number("Extensão modelada (m)", () => d.MainLength, v => d.MainLength = v, 200, 3000, "0")
         .Section("Via transversal")
         .Integer("Faixas por sentido", () => d.CrossLanes, v => d.CrossLanes = v, 1, 4)
         .Number("Largura da faixa (m)", () => d.CrossLaneWidth, v => d.CrossLaneWidth = v, 3, 4)
         .Number("Acostamento (m)", () => d.CrossShoulder, v => d.CrossShoulder = v, 0, 3.5)
         .Number("Extensão modelada (m)", () => d.CrossLength, v => d.CrossLength = v, 160, 3000, "0")
         .Section("Ramos")
         .Number("Largura das rampas (m)", () => d.RampWidth, v => d.RampWidth = v, 4.5, 12)
         .If(() => d.Type is TipoNoViario.TrevoCompleto or TipoNoViario.TrevoParcial or TipoNoViario.Trombeta,
             x => x.Number("Raio dos laços (m)", () => d.LoopRadius, v => d.LoopRadius = v, 30, 150, "0", "≈ 50 m para 40 km/h (DNIT).")
                   .Percent("Superelevação dos laços (%)", () => d.Superelevation, v => d.Superelevation = v, 0, 0.10, "DNIT: até 8 %."))
         .Number("Raio das curvas dos ramos (m)", () => d.RampRadius, v => d.RampRadius = v, 60, 600, "0")
         .Number("Espirais de transição (m)", () => d.SpiralLength, v => d.SpiralLength = v, 0, 150, "0", "Clotoides entre a tangente e a curva circular.")
         .Number("Faixa de mudança de velocidade (m)", () => d.SpeedChangeLength, v => d.SpeedChangeLength = v, 30, 300, "0", "Desaceleração/aceleração paralela à principal.")
         .Percent("Rampa máxima dos ramos (%)", () => d.RampGrade, v => d.RampGrade = v, 0.02, 0.08)
         .If(() => d.Type is TipoNoViario.Diamante or TipoNoViario.DiamanteRotatorias or TipoNoViario.TrevoParcial,
             x => x.Number("Distância dos terminais ao centro (m)", () => d.TerminalDistance, v => d.TerminalDistance = v, 40, 600, "0"))
         .If(() => d.Type is TipoNoViario.DiamanteRotatorias or TipoNoViario.RotatoriaElevada,
             x => x.Number("Raio das rotatórias (m)", () => d.RoundaboutRadius, v => d.RoundaboutRadius = v, 12, 80))
         .Number("Gabarito vertical (m)", () => d.Clearance, v => d.Clearance = v, 4.5, 8, "0.00", "DNIT: 5,50 m.")
         .Number("Altura estrutural do tabuleiro (m)", () => d.DeckDepth, v => d.DeckDepth = v, 0.8, 4)
         .Number("Talude dos aterros (H : 1 V)", () => d.FillSlope, v => d.FillSlope = v, 1, 4, "0.0")
         .Section("Acabamento")
         .Check("Barreiras", () => d.Barriers, v => d.Barriers = v)
         .Check("Seção urbana completa (sarjeta, meio-fio e calçada) nos ramos e nas vias criadas", () => d.UrbanSection, v => d.UrbanSection = v)
         .If(() => d.UrbanSection, x => x.Number("Largura da calçada (m)", () => d.SidewalkWidth, v => d.SidewalkWidth = v, 1.2, 6, "0.00"))
         .Check("Defensas metálicas nos aterros altos (seção rodoviária)", () => d.Guardrails, v => d.Guardrails = v)
         .Check("Faixas pintadas", () => d.Markings, v => d.Markings = v)
         .Check("Zebrados nos narizes e setas nos ramos", () => d.GoreMarkings, v => d.GoreMarkings = v)
         .Check("Pórticos de sinalização antes das saídas", () => d.Gantries, v => d.Gantries = v)
         .Check("Iluminação (postes)", () => d.Lighting, v => d.Lighting = v)
         .Check("Torres de iluminação nos laços e rotatórias", () => d.HighMasts, v => d.HighMasts = v)
         .Terrain(() => d.FollowTerrain, v => d.FollowTerrain = v, () => d.AdjustTerrain, v => d.AdjustTerrain = v);
        return w;
    }

    /// <summary>Janela de edição das obras de infraestrutura (null se o tipo não for daqui).</summary>
    public static (FormWindow Window, MarkingDefinition Working)? ForEdit(MarkingDefinition def)
    {
        var working = MarkingDefinition.FromJson(def.ToJson())!;
        FormWindow? w = working switch
        {
            DrainageDefinition dr => Drainage(dr, true, dr.Type is TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla or TipoDrenagem.BocaDeLoboGrelha
                or TipoDrenagem.BocaDeLoboCombinada or TipoDrenagem.PocoDeVisita),
            BridgeDefinition br => Bridge(br, true),
            TunnelDefinition tn => Tunnel(tn, true),
            TrenchDefinition tr => Trench(tr, true),
            RetainingWallDefinition rw => Wall(rw, true),
            SlopeDefinition sl => Slope(sl, true),
            InterchangeDefinition ic => Interchange(ic, true),
            _ => null,
        };
        return w == null ? null : (w, working);
    }
}

/// <summary>Terraplenagem logo após criar/editar uma obra que conversa com o terreno.</summary>
/// <summary>Recolhe os erros do Revit na terraplenagem (avisos são descartados) e desfaz a transação em vez de travar a tela.</summary>
internal sealed class TerrainFailures : IFailuresPreprocessor
{
    public List<string> Errors { get; } = new();

    public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
    {
        var error = false;
        foreach (var f in a.GetFailureMessages())
        {
            if (f.GetSeverity() == FailureSeverity.Warning) { a.DeleteWarning(f); continue; }
            Errors.Add(f.GetDescriptionText());
            error = true;
        }
        return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}

internal static class TerrainActions
{
    /// <summary>
    /// Depois de criar/editar uma obra ou uma via com greide: refaz o terreno nativo (Toposolid) com ela. Sem Toposolid no
    /// projeto, cria um terreno plano sob as obras – aterros, cortes e taludes são sempre do terreno do Revit.
    /// </summary>
    public static void AfterCreate(UIDocument uidoc, MarkingDefinition def) => AfterCreate(uidoc, new[] { def });

    public static void AfterCreate(UIDocument uidoc, IReadOnlyCollection<MarkingDefinition> defs, bool quiet = false)
    {
        var shapes = defs.Where(d => d is ITerrainAware { AdjustTerrain: true } || d.Output.Grade is { AdjustTerrain: true }).ToList();
        if (shapes.Count == 0) return;
        var opt = new GradingOptions { CreateIfMissing = true };
        var report = Apply(uidoc, shapes, opt, "SV - Terraplenagem");
        if (report?.Created == true)
            MarkingCreator.Commit(uidoc, shapes.Where(d => d is ITerrainAware).ToList(), "SV - Obra sobre o terreno nativo");
        if (report == null || quiet) return;
        var important = report.Created || report.Notes.Any(n => n.StartsWith("Aviso") || n.Contains("Nenhum") || n.Contains("não"));
        if (important)
            Notify.Show("Terreno nativo (Massa e terreno → Sólido topográfico) ajustado:\n\n" + report.Text());
    }

    public static TerrainReport? Apply(UIDocument uidoc, IEnumerable<MarkingDefinition> defs, GradingOptions opt, string name)
    {
        var doc = uidoc.Document;
        using var scope = MarkingService.RenderScope();
        using var t = new Transaction(doc, name);
        try
        {
            // Erros do Revit na edição do Toposolid viram texto no relatório (sem a janela "não pode ser ignorado").
            var failures = new TerrainFailures();
            var fho = t.GetFailureHandlingOptions();
            fho.SetFailuresPreprocessor(failures);
            fho.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(fho);
            t.Start();
            var service = new MarkingService(doc, uidoc.ActiveView);
            var report = TerrainService.Apply(doc, service, defs, opt);
            var status = t.Commit();
            if (status != TransactionStatus.Committed || failures.Errors.Count > 0)
            {
                Notify.Show("O Revit recusou o ajuste do terreno e ele foi desfeito:\n\n" + string.Join("\n", failures.Errors.Distinct()) +
                    "\n\nSe a mensagem falar em sólido \"muito fino\", aumente a espessura do tipo do Toposolid (Editar tipo → Estrutura) e rode Terraplenagem.");
                return null;
            }
            return report;
        }
        catch (Exception ex)
        {
            Log.Error("Terraplenagem", ex);
            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            Notify.Show("Não foi possível ajustar o terreno: " + ex.Message);
            return null;
        }
    }
}

/// <summary>Obras ao longo de um eixo: formulário, caminho e terraplenagem.</summary>
internal static class InfraRunner
{
    public static Result Run<T>(UIDocument uidoc, string key, Func<T> create, Func<T, FormWindow> form, Action<T>? prepare = null) where T : MarkingDefinition
    {
        var template = UiHelpers.Remembered<T>(key) ?? create();
        template.Output = Output3D();
        prepare?.Invoke(template);
        if (template is ITerrainAware ta) ta.GroundLine = null;
        var w = form(template);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        UiHelpers.Remember(key, template);
        PluginContext.SaveSettings();
        return MarkingCreator.CreateAlongPath(uidoc, template, w.PathMode, template.DisplayCode, false, d => TerrainActions.AfterCreate(uidoc, d));
    }

    /// <summary>Obras de infraestrutura: sempre sólidos 3D, sem projetar na superfície (o terreno é tratado pela própria obra).</summary>
    public static OutputSettings Output3D()
    {
        var o = PluginContext.Settings.NewOutput();
        o.Mode = OutputMode.Modelo3D;
        o.Drape = false;
        o.SurfaceIds.Clear();
        o.ElevationOffset = 0;
        return o;
    }
}

/// <summary>
/// Pontes, viadutos, passarelas, túneis e trincheiras como trechos de vias do plugin: num trecho de uma via existente (o
/// greide dela é ajustado e a estrutura hospedada) ou numa via nova criada com a ferramenta de vias.
/// </summary>
internal static class HostedRunner
{
    public static Result Run<T>(UIDocument uidoc, string key, Func<T> create, Func<T, FormWindow> form, Action<T>? prepare = null)
        where T : MarkingDefinition, IHostedStructure, ITerrainAware
    {
        var doc = uidoc.Document;
        var template = UiHelpers.Remembered<T>(key) ?? create();
        template.Output = InfraRunner.Output3D();
        prepare?.Invoke(template);
        template.GroundLine = null;
        template.HostRoad = null;
        var w = form(template);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        UiHelpers.Remember(key, template);
        PluginContext.SaveSettings();
        var def = (T)template.CloneWithNewId();

        if (w.PathMode == PathMode.ViaExistente)
        {
            var road = RoadWorks.PickRoad(uidoc, $"{def.KindName}: clique a VIA (pista, calçada ou linha) onde fica a obra");
            if (road == null) return Result.Cancelled;
            var stretch = RoadWorks.PickStretch(uidoc, road, def.KindName);
            if (stretch == null) return Result.Cancelled;
            var (s0, s1) = stretch.Value;
            if (s1 - s0 < 5) { TaskDialog.Show(CommandBase.AppTitle, "Trecho muito curto (mínimo 5 m)."); return Result.Cancelled; }
            CommandBase.ReportResults(def.KindName, OnExistingRoad(uidoc, def, road, s0, s1).Where(r => r.Warnings.Count > 0).ToList());
            return Result.Succeeded;
        }

        // Via nova com a obra: o eixo é desenhado (ou escolhido) como o de qualquer via do plugin.
        var straight = def is BridgeDefinition { StraightAxis: true };
        var snapped = new List<string>();
        var path = RoadWorks.AxisFor(uidoc, w.PathMode != PathMode.ViaNovaLinhas, InfraForms.NewRoadRadius, straight, snapped);
        if (path == null) return Result.Cancelled;
        var resolved = PathResolver.Resolve(doc, path);
        if (resolved?.Main is not { Length: >= 10 }) { TaskDialog.Show(CommandBase.AppTitle, "Eixo muito curto para a obra (mínimo 10 m)."); return Result.Cancelled; }
        var results = OnNewRoad(uidoc, def, path, InfraForms.NewRoadTemplate);
        if (results == null) return Result.Failed;
        if (snapped.Count > 0 && results.Count > 0) results[0].Warnings.Insert(0, "Conexões: " + string.Join("; ", snapped.Distinct()) + ".");
        CommandBase.ReportResults(def.KindName, results.Where(x => x.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }

    /// <summary>Obra num trecho [s0, s1] de uma via existente: o greide da via é recomposto com a obra e o terreno ajustado.</summary>
    public static List<RenderResult> OnExistingRoad<T>(UIDocument uidoc, T def, PickedRoad road, double s0, double s1)
        where T : MarkingDefinition, IHostedStructure, ITerrainAware
    {
        var doc = uidoc.Document;
        var results = new List<RenderResult>();
        RoadWorks.Host(def, road.Pavement, s0, s1);
        var grade = RoadWorks.GradeWith(doc, road, def);
        road.Pavement.Output.Grade = grade.Clone();
        def.Output.Grade = grade.Clone();
        results.AddRange(RoadWorks.SetGrade(uidoc, road.GroupId, grade, new MarkingDefinition[] { def }));
        TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { road.Pavement, def });
        return results;
    }

    /// <summary>
    /// Obra numa via nova no eixo <paramref name="path"/> (seção do modelo <paramref name="template"/>, −1 = pela obra).
    /// Nulo se a via não pôde ser criada.
    /// </summary>
    public static List<RenderResult>? OnNewRoad<T>(UIDocument uidoc, T def, PathReference path, int template)
        where T : MarkingDefinition, IHostedStructure, ITerrainAware
    {
        var doc = uidoc.Document;
        var results = new List<RenderResult>();
        var resolved = PathResolver.Resolve(doc, path);
        if (resolved?.Main is not { } axis || axis.Length < 10)
        {
            results.Add(new RenderResult());
            results[0].Warnings.Add("Eixo muito curto para a obra (mínimo 10 m).");
            return results;
        }
        var g0 = RoadWorks.Ground(doc, resolved.Z);
        double G(Vec2 p) => def.FollowTerrain ? g0(p) ?? 0 : 0;
        RoadGrade roadGrade;
        double a, b2;
        var warn = new List<string>();
        switch (def)
        {
            case BridgeDefinition br:
            {
                (roadGrade, a, b2, warn) = InfraRoads.BridgeGrade(br, axis, G);
                break;
            }
            case TunnelDefinition tn:
            {
                roadGrade = new RoadGrade { Points = { new(0, tn.StartZ), new(axis.Length, tn.EndZ) } };
                var (_, crown, _) = EarthworksGenerator.TunnelSection(tn);
                var range = InfraRoads.TunnelRange(axis, roadGrade, p => def.FollowTerrain ? g0(p) : null, crown);
                (a, b2) = range ?? (Math.Min(axis.Length * 0.3, tn.ApproachCut), Math.Max(axis.Length * 0.7, axis.Length - tn.ApproachCut));
                if (range == null && def.FollowTerrain) warn.Add("Sem cobertura de terreno suficiente sobre o eixo: o túnel foi posto no meio do eixo – confira o trecho.");
                break;
            }
            case TrenchDefinition tr:
            {
                var flat = RoadGrade.Flat(axis.Length);
                roadGrade = InfraRoads.TrenchOnRoad(flat, 0, axis.Length, tr.Depth, tr.MaxGrade, tr.VerticalCurve, s => G(axis.PointAt(s)));
                (a, b2) = (0, axis.Length);
                break;
            }
            default:
                roadGrade = RoadGrade.Flat(axis.Length); (a, b2) = (0, axis.Length); break;
        }
        roadGrade.AdjustTerrain = def.AdjustTerrain;
        // Greide "sem obras" da via nova: reta entre as cotas das pontas (a obra é reaplicada sobre ele ao ser editada).
        roadGrade.WithoutWorks = def is TunnelDefinition
            ? new RoadGrade { Points = { new(0, roadGrade.Z(0)), new(axis.Length, roadGrade.Z(axis.Length)) } }
            : new RoadGrade { Points = { new(0, G(axis.PointAt(0))), new(axis.Length, G(axis.PointAt(axis.Length))) } };
        var setup = template >= 0 && template < RoadTemplates.All.Count
            ? RoadTemplates.All[template].Create()
            : def switch
            {
                BridgeDefinition br => InfraRoads.Setup(br),
                TunnelDefinition tn => InfraRoads.Setup(tn),
                TrenchDefinition tr => InfraRoads.Setup(tr),
                _ => InfraRoads.Setup(2, 3.5, 1, 0),
            };
        var (pav, created) = RoadWorks.CreateRoad(uidoc, setup, path, roadGrade, true, def.KindName);
        results.AddRange(created);
        if (pav == null) { CommandBase.ReportResults(def.KindName, results); return null; }
        RoadWorks.Host(def, pav, a, b2);
        var r = MarkingCreator.Commit(uidoc, new MarkingDefinition[] { def }, $"SV - {def.KindName}");
        if (warn.Count > 0 && r.Count > 0) r[0].Warnings.InsertRange(0, warn);
        results.AddRange(r);
        TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { pav, def });
        return results;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdViaduto : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        HostedRunner.Run(uidoc, "infra:viaduto", () => { var b = new BridgeDefinition(); b.ApplyKindDefaults(); return b; }, d => InfraForms.Bridge(d, false), d => d.Kind = TipoObraDeArte.Viaduto);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdPonte : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        HostedRunner.Run(uidoc, "infra:ponte", () => { var b = new BridgeDefinition { Kind = TipoObraDeArte.Ponte }; b.ApplyKindDefaults(); return b; }, d => InfraForms.Bridge(d, false),
            d => d.Kind = TipoObraDeArte.Ponte);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdPassarela : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        HostedRunner.Run(uidoc, "infra:passarela", () => { var b = new BridgeDefinition { Kind = TipoObraDeArte.Passarela }; b.ApplyKindDefaults(); return b; }, d => InfraForms.Bridge(d, false),
            d => d.Kind = TipoObraDeArte.Passarela);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdTunel : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        HostedRunner.Run(uidoc, "infra:tunel", () => new TunnelDefinition(), d => InfraForms.Tunnel(d, false));
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdTrincheira : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        HostedRunner.Run(uidoc, "infra:trincheira", () => new TrenchDefinition(), d => InfraForms.Trench(d, false));
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdMuroArrimo : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        InfraRunner.Run(uidoc, "infra:muro", () => new RetainingWallDefinition(), d => InfraForms.Wall(d, false));
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdTalude : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        InfraRunner.Run(uidoc, "infra:talude", () => new SlopeDefinition(), d => InfraForms.Slope(d, false));
}

/// <summary>
/// Drenagem: bocas de lobo e PVs, ou grelhas e canaletas – encaixadas na face do meio-fio (um a um ou em série) e recortando
/// o pavimento, a sarjeta, o meio-fio e a calçada que substituem.
/// </summary>
internal static class DrainageCommand
{
    public static Result Run(UIDocument uidoc, bool inlets)
    {
        var key = inlets ? "infra:bocadelobo" : "infra:grelha";
        var template = UiHelpers.Remembered<DrainageDefinition>(key) ?? new DrainageDefinition { Type = inlets ? TipoDrenagem.BocaDeLoboSimples : TipoDrenagem.GrelhaSarjeta };
        template.Output = InfraRunner.Output3D();
        var w = InfraForms.Drainage(template, false, inlets);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        UiHelpers.Remember(key, template);
        PluginContext.SaveSettings();
        if (template.IsLinear)
            return MarkingCreator.CreateAlongPath(uidoc, template, w.PathMode is PathMode.DoisPontos or PathMode.Serie ? PathMode.Desenhar : w.PathMode, template.DisplayCode,
                false, d => FootprintCutter.ApplyFor(uidoc, d));
        var doc = uidoc.Document;
        var scanner = new CurbScanner(doc, uidoc.ActiveView);
        var results = new List<RenderResult>();
        var free = template.Type is TipoDrenagem.PocoDeVisita or TipoDrenagem.GrelhaQuadrada;
        var series = !free && w.PathMode == PathMode.Serie;
        while (true)
        {
            var p = Picking.PickPoint(uidoc, free
                ? $"{template.DisplayCode}: clique o centro (sobre a pista, a calçada ou um ponto livre) – ESC encerra"
                : series ? $"{template.DisplayCode}: clique o INÍCIO da série junto ao meio-fio – ESC encerra"
                : $"{template.DisplayCode}: clique junto ao MEIO-FIO, do lado da pista – ESC encerra");
            if (p == null) break;
            var pt = UnitConv.ToVec2(p);
            Vec2? end = null;
            if (series)
            {
                if (Fitted(template, pt, UnitConv.M(p.Z), scanner, null) == null)
                {
                    TaskDialog.Show(CommandBase.AppTitle, NoCurb);
                    continue;
                }
                var q = Picking.PickPoint(uidoc, $"{template.DisplayCode}: clique o FIM da série no mesmo meio-fio – ESC cancela");
                if (q == null) break;
                end = UnitConv.ToVec2(q);
            }
            var batch = Fitted(template, pt, UnitConv.M(p.Z), scanner, end);
            if (batch == null)
            {
                TaskDialog.Show(CommandBase.AppTitle, NoCurb);
                continue;
            }
            results.AddRange(Place(uidoc, batch));
            scanner = new CurbScanner(doc, uidoc.ActiveView);           // pisos recortados: lê de novo
        }
        if (results.Count == 0) return Result.Cancelled;
        CommandBaseReport.Show(template.DisplayCode, results);
        return Result.Succeeded;
    }

    private const string NoCurb = "Nenhum meio-fio encontrado perto do clique. Clique junto à face de uma guia do plugin " +
        "(via com calçada, interseção, rotatória, extensão de calçada ou meio-fio avulso) – ou use o tipo livre (grelha de piso / PV).";

    /// <summary>
    /// Dispositivos do clique: livres (PV, grelha de piso) no ponto, alinhados à via mais próxima; os demais encaixados no
    /// meio-fio (um, ou uma série até <paramref name="seriesEnd"/>). Nulo = nenhum meio-fio perto.
    /// </summary>
    internal static List<DrainageDefinition>? Fitted(DrainageDefinition template, Vec2 pt, double clickZ, CurbScanner scanner, Vec2? seriesEnd)
    {
        var batch = new List<DrainageDefinition>();
        if (template.Type is TipoDrenagem.PocoDeVisita or TipoDrenagem.GrelhaQuadrada)
        {
            var d = (DrainageDefinition)template.CloneWithNewId();
            d.Position = pt;
            d.Z = scanner.SurfaceZ(pt) ?? clickZ;
            // Alinhada à via mais próxima (quando houver).
            if (CurbFinder.Nearest(scanner.Faces(pt), pt, 30) is { } near) d.Along = near.Along;
            batch.Add(d);
            return batch;
        }
        var hit = CurbFinder.Nearest(scanner.Faces(pt), pt);
        if (hit == null) return null;
        var hits = seriesEnd is { } q ? CurbFinder.Series(hit.Face, pt, q, template.SeriesSpacing) : new List<CurbHit> { hit };
        foreach (var h in hits)
        {
            var d = (DrainageDefinition)template.CloneWithNewId();
            Fit(d, h);
            batch.Add(d);
        }
        return batch;
    }

    /// <summary>Gera os dispositivos e recorta os pisos que eles substituem.</summary>
    internal static List<RenderResult> Place(UIDocument uidoc, List<DrainageDefinition> batch)
    {
        var created = MarkingCreator.Commit(uidoc, batch, $"SV - {batch.FirstOrDefault()?.DisplayCode}");
        foreach (var d in batch.Where(x => x.CutFloors))
        {
            try { FootprintCutter.ApplyDrainage(uidoc, d); }
            catch (Exception ex) { Log.Error("Recorte da drenagem", ex); }
        }
        return created;
    }

    /// <summary>Encaixa o dispositivo na face do meio-fio (posição, direção, cota e medidas da guia).</summary>
    private static void Fit(DrainageDefinition d, CurbHit h)
    {
        d.Position = h.Position;
        d.Along = h.Along;
        d.SidewalkLeft = true;
        d.Z = h.Face.BaseZ;
        if (!d.AutoFit) return;
        if (h.Face.CurbHeight is > 0.04 and < 0.5) d.CurbHeight = Math.Round(h.Face.CurbHeight, 3);
        if (h.Face.CurbWidth is > 0.06 and < 0.5) d.CurbWidth = h.Face.CurbWidth;
        if (h.Face.GutterWidth > 0.05) d.GutterWidth = Math.Max(d.GutterWidth, h.Face.GutterWidth + 0.10);
    }
}

/// <summary>Resumo padrão após criar elementos (mesma mensagem dos demais comandos).</summary>
internal static class CommandBaseReport
{
    public static void Show(string d0, List<RenderResult> results)
    {
        var warnings = results.SelectMany(r => r.Warnings).Distinct().ToList();
        if (warnings.Count > 0) Notify.Show($"{d0}:\n" + string.Join("\n", warnings.Take(15)));
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdBocaDeLobo : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => DrainageCommand.Run(uidoc, true);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdGrelha : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => DrainageCommand.Run(uidoc, false);
}

/// <summary>Interseção em desnível: centro (encaixa no cruzamento de vias) e direção da via principal.</summary>
/// <summary>Terraplenagem: ajusta o Toposolid às vias, conexões e obras (corte, aterro, taludes e escavação).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdTerraplenagem : CommandBase
{
    private static int SoilPreset(GradingOptions o) => (o.CutSlope, o.FillSlope) switch
    {
        (1.0, 1.5) => 0, (1.5, 2.0) => 1, (0.5, 1.5) => 2, (0.75, 1.5) => 3, _ => 4,
    };

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var noTerrain = !new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Any();
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = "Terraplenagem no Toposolid",
            MainContent = "As vias, conexões e obras ficam no seu greide (planas, como sempre) e o terreno é ajustado a elas: plataformas sob o pavimento, " +
                          "taludes de corte e aterro até encontrar o terreno natural, reaterro de muros, faces de taludes e emboques. " +
                          "Use Desfazer (Ctrl+Z) para voltar ao terreno anterior.",
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Selecionar vias, conexões e obras", "Escolha os elementos (Concluir na barra de opções).");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Todas as vias, conexões e obras do projeto");
        td.CommonButtons = TaskDialogCommonButtons.Cancel;
        var choice = td.Show();
        List<MarkingDefinition> defs;
        if (choice == TaskDialogResult.CommandLink1)
        {
            var picked = MarkingPicker.PickMany(uidoc, "Selecione vias, interseções, rotatórias e obras a terraplenar e clique em Concluir");
            defs = picked.Select(r => r.Definition).GroupBy(d => d.Id).Select(g => g.First()).ToList();
        }
        else if (choice == TaskDialogResult.CommandLink2) defs = MarkingStorage.Definitions(doc).ToList();
        else return Result.Cancelled;
        defs = defs.Where(TerrainService.Grades).ToList();
        if (defs.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhuma via, conexão ou obra entre os elementos escolhidos.");
            return Result.Cancelled;
        }
        var opt = new GradingOptions();
        var resetOriginal = false;
        var form = new FormWindow("Terraplenagem", "Opções da terraplenagem",
                $"{defs.Count} elemento(s). O terreno é refeito a partir do terreno ORIGINAL com todas as vias e obras que o moldam – " +
                "regerar não acumula aterros e apagar uma obra devolve o terreno natural.\n" +
                "O relatório traz corte, aterro, o balanço de massas (bota-fora ou empréstimo) e os volumes de cada elemento. " +
                "Use \"Só calcular\" para ver os volumes sem mexer no terreno.", null, null, false, "Aplicar", 700, 620)
            .Check("Só calcular (simulação: volumes e balanço, sem alterar o Toposolid)", () => opt.DryRun, v => opt.DryRun = v)
            .Choice("Tipo de terreno (taludes sugeridos)", new[]
            {
                ("Solo comum – corte 1 : 1, aterro 1,5 : 1", 0), ("Solo arenoso / pouco coesivo – corte 1,5 : 1, aterro 2 : 1", 1),
                ("Rocha – corte 0,5 : 1, aterro 1,5 : 1", 2), ("Solo argiloso rijo – corte 0,75 : 1, aterro 1,5 : 1", 3), ("Personalizado (valores abaixo)", 4),
            }, () => SoilPreset(opt), v =>
            {
                (opt.CutSlope, opt.FillSlope) = v switch { 0 => (1.0, 1.5), 1 => (1.5, 2.0), 2 => (0.5, 1.5), 3 => (0.75, 1.5), _ => (opt.CutSlope, opt.FillSlope) };
            })
            .Number("Talude de corte (H : 1 V)", () => opt.CutSlope, v => opt.CutSlope = v, 0.3, 5, "0.0#", "Solo: 1 : 1; rocha: 0,5 : 1 (DNIT).")
            .Number("Talude de aterro (H : 1 V)", () => opt.FillSlope, v => opt.FillSlope = v, 1, 5, "0.0#", "Usual 1,5 : 1.")
            .Number("Profundidade do subleito sob o pavimento (m)", () => opt.Subgrade, v => opt.Subgrade = v, 0, 2)
            .Number("Alcance máximo dos taludes (m)", () => opt.MaxDaylight, v => opt.MaxDaylight = v, 5, 500, "0")
            .Number("Fator de homogeneização (corte ÷ aterro compactado)", () => opt.Homogenization, v => opt.Homogenization = v, 1, 2, "0.00",
                "Volume de corte (in situ) necessário por m³ de aterro compactado: 1,20–1,40 em solos (DNIT). Entra no balanço de massas.")
            .Check("Escavar o terreno com túneis e caixas (quando o Revit permitir)", () => opt.Excavate, v => opt.Excavate = v)
            .Check("Grama dos taludes e ilhas como subdivisões do Toposolid", () => opt.Finishes, v => opt.Finishes = v)
            .Check("Mapa de corte (vermelho) e aterro (verde) no Toposolid", () => opt.CutFillMap, v => opt.CutFillMap = v,
                "Subdivisões coloridas onde o terreno é cortado ou aterrado mais de 10 cm – para conferir e apresentar. Rode de novo sem a opção para tirar.")
            .Check("Novo levantamento: o terreno atual passa a ser o terreno natural", () => resetOriginal, v => resetOriginal = v,
                "Marque se você editou o Toposolid à mão (ou importou um levantamento novo) e quer que ele seja a nova referência.");
        if (noTerrain)
        {
            opt.CreateIfMissing = true;
            form.Section("Terreno nativo", "O projeto não tem Toposolid: um terreno plano será criado sob os elementos (Massa e terreno) e moldado a eles.")
                .Number("Margem do terreno criado (m)", () => opt.NewTerrainMargin, v => opt.NewTerrainMargin = v, 10, 1000, "0");
        }
        if (UiHelpers.ShowModal(form) != true) return Result.Cancelled;
        var report = Apply(uidoc, defs, opt, resetOriginal);
        if (report != null) TaskDialog.Show(AppTitle, report.Text());
        return report != null ? Result.Succeeded : Result.Failed;
    }

    /// <summary>
    /// Terraplenagem dos elementos escolhidos: as vias passam a moldar o terreno e as obras a ajustá-lo; o Toposolid é refeito
    /// (ou, na simulação, só os volumes são calculados). Nulo se o Revit recusou.
    /// </summary>
    internal static TerrainReport? Apply(UIDocument uidoc, List<MarkingDefinition> defs, GradingOptions opt, bool resetOriginal)
    {
        var doc = uidoc.Document;
        if (opt.DryRun) opt.CreateIfMissing = false;
        // Vias escolhidas passam a moldar o terreno (greide plano quando ainda não têm um) e as obras, a ajustá-lo.
        var toCommit = new List<MarkingDefinition>();
        var roadGroups = defs.OfType<RoadPavementDefinition>().Where(r => r.GroupId != null && r.Output.Grade is not { AdjustTerrain: true })
            .Select(r => r.GroupId!).ToHashSet();
        foreach (var d in MarkingStorage.Definitions(doc).Where(d => d.GroupId != null && roadGroups.Contains(d.GroupId!)))
        {
            d.Output.Grade ??= new RoadGrade();
            d.Output.Grade.AdjustTerrain = true;
            d.Output.Grade.CutSlope = opt.CutSlope;
            d.Output.Grade.FillSlope = opt.FillSlope;
            toCommit.Add(d);
        }
        foreach (var d in defs.OfType<ITerrainAware>().Where(t => !t.AdjustTerrain).Cast<MarkingDefinition>())
        {
            switch (d)
            {
                case BridgeDefinition b: b.AdjustTerrain = true; break;
                case TunnelDefinition t: t.AdjustTerrain = true; break;
                case TrenchDefinition t: t.AdjustTerrain = true; break;
                case RetainingWallDefinition w: w.AdjustTerrain = true; break;
                case SlopeDefinition sl: sl.AdjustTerrain = true; break;
                case InterchangeDefinition i: i.AdjustTerrain = true; break;
            }
            toCommit.Add(d);
        }
        // Simulação: nada é gravado – as marcações acima valem só para o cálculo.
        if (toCommit.Count > 0 && !opt.DryRun) MarkingCreator.Commit(uidoc, toCommit, "SV - Elementos que moldam o terreno");
        if (resetOriginal && !opt.DryRun)
        {
            using var t = new Transaction(doc, "SV - Novo terreno natural");
            t.Start();
            TerrainService.ResetOriginal(doc);
            t.Commit();
        }
        var report = TerrainActions.Apply(uidoc, opt.DryRun ? defs.Concat(toCommit).GroupBy(d => d.Id).Select(g => g.Last()).ToList() : toCommit, opt,
            opt.DryRun ? "SV - Terraplenagem (simulação)" : "SV - Terraplenagem");
        if (report?.Created == true)
            MarkingCreator.Commit(uidoc, MarkingStorage.Definitions(doc).Where(x => x is ITerrainAware).ToList(), "SV - Obras sobre o terreno nativo");
        return report;
    }
}
