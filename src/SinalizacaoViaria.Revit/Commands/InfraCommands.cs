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
            "Clique junto ao MEIO-FIO (de uma via, interseção, rotatória, orelha ou meio-fio avulso): o dispositivo encaixa na face da guia, " +
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
            "Desenhe o EIXO da obra de ponta a ponta (incluindo as rampas de acesso). O greide sobe pela rampa máxima até a altura do tabuleiro; " +
            "onde o aterro passaria de 6 m começa a estrutura (encontros, pilares e vãos). Tudo editável depois.",
            d, () =>
            {
                var c = (BridgeDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.FollowTerrain = false; c.GroundLine = null;
                var len = Math.Max(120, 2 * Math.Abs(c.Height) / Math.Max(0.02, c.MaxGrade) + Math.Max(60, 3 * c.SpanLength));
                var g = Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) }));
                return new FormPreview(g, null, null, Info(g, $"Exemplo com {len:0} m de eixo. Área de tabuleiro: {g.AreaByColor.GetValueOrDefault(MarkingColor.Concreto):0} m²."));
            }, false, edit ? "Aplicar" : "Inserir", 1180, 760);
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
         .Check("Faixas pintadas", () => d.LaneMarkings, v => d.LaneMarkings = v)
         .Check("Iluminação", () => d.Lighting, v => d.Lighting = v)
         .Number("Espaçamento dos postes (m)", () => d.LightSpacing, v => d.LightSpacing = v, 10, 80)
         .Section("Greide e acessos")
         .Number("Altura do tabuleiro sobre a base (m)", () => d.Height, v => d.Height = v, 2, 120, "0.00", "Gabarito vertical livre ≥ 5,50 m sobre rodovias (DNIT).")
         .Check("Rampa de acesso no início", () => d.ApproachStart, v => d.ApproachStart = v)
         .Check("Rampa de acesso no fim", () => d.ApproachEnd, v => d.ApproachEnd = v)
         .Percent("Rampa máxima (%)", () => d.MaxGrade, v => d.MaxGrade = v, 0.01, 0.12, "Veículos: 5–6 %; passarelas: ≤ 8,33 % (NBR 9050).")
         .Number("Curva vertical (m)", () => d.VerticalCurve, v => d.VerticalCurve = v, 0, 300)
         .Check("Acessos em terra armada (muros) em vez de taludes", () => d.ApproachWalls, v => d.ApproachWalls = v)
         .Number("Talude dos aterros (H : 1 V)", () => d.FillSlope, v => d.FillSlope = v, 0.5, 4, "0.0")
         .Section("Vãos e pilares")
         .Number("Vão típico (m)", () => d.SpanLength, v => d.SpanLength = v, 8, 200, "0")
         .Number("Vão principal – arcos e estaiadas (m, 0 = toda a obra)", () => d.MainSpan, v => d.MainSpan = v, 0, 1000, "0")
         .Choice("Pilares", new[] { ("Pórtico (dois pilares + travessa)", TipoPilar.Portico), ("Circular único", TipoPilar.Circular), ("Dois circulares + travessa", TipoPilar.DuplaCircular),
                 ("Parede", TipoPilar.Parede), ("Martelo (capitel)", TipoPilar.Martelo) }, () => d.PierType, v => d.PierType = v)
         .Number("Dimensão dos pilares (m)", () => d.PierSize, v => d.PierSize = v, 0.4, 6)
         .Section("Rio (pontes)")
         .Check("Lâmina d'água sob a obra", () => d.Water, v => d.Water = v)
         .Number("Cota da água sobre a base (m)", () => d.WaterLevel, v => d.WaterLevel = v, -100, 100)
         .Number("Largura do rio (m)", () => d.WaterWidth, v => d.WaterWidth = v, 2, 2000, "0")
         .Terrain(() => d.FollowTerrain, v => d.FollowTerrain = v, () => d.AdjustTerrain, v => d.AdjustTerrain = v);
        if (!edit) w.Modes(("Desenhar o eixo por pontos", PathMode.Desenhar), ("Selecionar linhas existentes", PathMode.Linhas), ("Dois cliques", PathMode.DoisPontos));
        return w;
    }

    // ------------------------------------------------------------------ túnel

    public static FormWindow Tunnel(TunnelDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar túnel" : "Túnel", "Túnel rodoviário",
            "Desenhe o EIXO do túnel entre os emboques. Revestimento, pavimento, passeios de serviço, iluminação, ventiladores e emboques; " +
            "com o terreno, as trincheiras de acesso diante dos emboques são terraplenadas e o Revit tenta escavar o Toposolid.",
            d, () =>
            {
                var c = (TunnelDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                var g = Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(40, 0) }));
                var (_, crown, half) = EarthworksGenerator.TunnelSection(c);
                return new FormPreview(g, null, null, Info(g, $"Largura livre {2 * half:0.00} m; altura até a coroa {crown:0.00} m."));
            }, false, edit ? "Aplicar" : "Inserir", 1080, 720);
        w.Choice("Seção", new[] { ("Ferradura (NATM)", SecaoTunel.Ferradura), ("Circular (TBM)", SecaoTunel.Circular), ("Retangular (vala coberta)", SecaoTunel.Retangular) },
                () => d.Section, v => d.Section = v)
         .Integer("Faixas", () => d.Lanes, v => d.Lanes = v, 1, 6)
         .Number("Largura da faixa (m)", () => d.LaneWidth, v => d.LaneWidth = v, 2.5, 4.5)
         .Number("Acostamento/faixa de segurança (m)", () => d.ShoulderWidth, v => d.ShoulderWidth = v, 0, 3)
         .Number("Passeio de serviço (m)", () => d.WalkwayWidth, v => d.WalkwayWidth = v, 0, 2)
         .Number("Gabarito vertical livre (m)", () => d.ClearHeight, v => d.ClearHeight = v, 3, 8, "0.00", "≥ 5,50 m em rodovias.")
         .Number("Espessura do revestimento (m)", () => d.LiningThickness, v => d.LiningThickness = v, 0.2, 1.5)
         .Number("Cota da pista no início (m)", () => d.StartZ, v => d.StartZ = v, -200, 200)
         .Number("Cota da pista no fim (m)", () => d.EndZ, v => d.EndZ = v, -200, 200)
         .Choice("Emboques", new[] { ("Parede de testa", TipoEmboque.Testa), ("Bisel (acompanha o talude)", TipoEmboque.Bisel), ("Pala em balanço", TipoEmboque.Pala) },
            () => d.Portal, v => d.Portal = v)
         .Number("Comprimento da pala/bisel (m)", () => d.PortalLength, v => d.PortalLength = v, 1, 40)
         .Number("Trincheira de acesso diante de cada emboque (m)", () => d.ApproachCut, v => d.ApproachCut = v, 0, 300)
         .Check("Iluminação", () => d.Lighting, v => d.Lighting = v)
         .Check("Ventiladores de jato", () => d.JetFans, v => d.JetFans = v)
         .Number("Espaçamento dos ventiladores (m)", () => d.FanSpacing, v => d.FanSpacing = v, 30, 500, "0")
         .Terrain(() => d.FollowTerrain, v => d.FollowTerrain = v, () => d.AdjustTerrain, v => d.AdjustTerrain = v);
        if (!edit) w.Modes(("Desenhar o eixo por pontos", PathMode.Desenhar), ("Selecionar linhas existentes", PathMode.Linhas), ("Dois cliques", PathMode.DoisPontos));
        return w;
    }

    // ------------------------------------------------------------------ trincheira

    public static FormWindow Trench(TrenchDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar trincheira" : "Trincheira", "Trincheira (via rebaixada)",
            "Desenhe o EIXO da trincheira de ponta a ponta: a pista desce pela rampa máxima até o rebaixo, entre muros de contenção com " +
            "guarda-corpo, e sobe no fim. Opcional: laje de travessia para a via transversal. O terreno entre os muros é escavado.",
            d, () =>
            {
                var c = (TrenchDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.FollowTerrain = false; c.GroundLine = null;
                var len = 2 * c.Depth / Math.Max(0.02, c.MaxGrade) + 80;
                var g = Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) }));
                return new FormPreview(g, null, null, Info(g, $"Exemplo com {len:0} m de eixo."));
            }, false, edit ? "Aplicar" : "Inserir", 1080, 720);
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
        if (!edit) w.Modes(("Desenhar o eixo por pontos", PathMode.Desenhar), ("Selecionar linhas existentes", PathMode.Linhas), ("Dois cliques", PathMode.DoisPontos));
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
         .Terrain(() => d.FollowTerrain, v => d.FollowTerrain = v, () => d.AdjustTerrain, v => d.AdjustTerrain = v);
        if (!edit) w.Modes(("Desenhar a linha por pontos", PathMode.Desenhar), ("Selecionar linhas existentes", PathMode.Linhas), ("Dois cliques", PathMode.DoisPontos));
        return w;
    }

    // ------------------------------------------------------------------ nó viário

    public static FormWindow Interchange(InterchangeDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar interseção em desnível" : "Interseção em desnível", "Interseção em desnível (nó viário)",
            "Clique o CENTRO do nó (sobre o cruzamento de duas vias, a direção vem da via principal) ou um ponto livre e a direção da via principal. " +
            "Viaduto, rampas e laços com greide, aterros, barreiras e iluminação são gerados; onde um ramo passa sobre outro vira ponte.",
            d, () =>
            {
                var c = (InterchangeDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.Position = Vec2.Zero; c.FollowTerrain = false;
                var g = Build(c, null);
                return new FormPreview(g, null, null, Info(g, $"Pavimento aproximado: {g.AreaByColor.GetValueOrDefault(MarkingColor.Asfalto):0} m²."));
            }, false, edit ? "Aplicar" : "Inserir", 1200, 780);
        w.Choice("Tipo", new[]
            {
                ("Diamante", TipoNoViario.Diamante), ("Diamante com rotatórias", TipoNoViario.DiamanteRotatorias), ("Trevo completo", TipoNoViario.TrevoCompleto),
                ("Trevo parcial (parclo)", TipoNoViario.TrevoParcial), ("Trombeta (entroncamento em T)", TipoNoViario.Trombeta),
                ("Rotatória em dois níveis", TipoNoViario.RotatoriaElevada),
            }, () => d.Type, v => d.Type = v)
         .Number("Ângulo entre as vias (°)", () => d.CrossAngleDeg, v => d.CrossAngleDeg = v, 45, 135, "0")
         .Check("Via principal por baixo (transversal em viaduto)", () => d.MainBelow, v => d.MainBelow = v)
         .Section("Via principal (pista dupla)")
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
         .Check("Defensas metálicas nos aterros altos", () => d.Guardrails, v => d.Guardrails = v)
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
internal static class TerrainActions
{
    public static void AfterCreate(UIDocument uidoc, MarkingDefinition def)
    {
        if (def is not ITerrainAware { AdjustTerrain: true }) return;
        var doc = uidoc.Document;
        if (!new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Any()) return;
        var report = Apply(uidoc, new[] { def }, new GradingOptions(), "SV - Terraplenagem");
        if (report != null && (report.Toposolids > 0 || report.Notes.Count > 0))
            TaskDialog.Show(CommandBase.AppTitle, $"Terreno ajustado a {def.KindName.ToLowerInvariant()}:\n\n" + report.Text());
    }

    public static TerrainReport? Apply(UIDocument uidoc, IEnumerable<MarkingDefinition> defs, GradingOptions opt, string name)
    {
        var doc = uidoc.Document;
        using var scope = MarkingService.RenderScope();
        using var t = new Transaction(doc, name);
        try
        {
            t.Start();
            var service = new MarkingService(doc, uidoc.ActiveView);
            var report = TerrainService.Apply(doc, service, defs, opt);
            t.Commit();
            return report;
        }
        catch (Exception ex)
        {
            Log.Error("Terraplenagem", ex);
            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            TaskDialog.Show(CommandBase.AppTitle, "Não foi possível ajustar o terreno: " + ex.Message);
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

[Transaction(TransactionMode.Manual)]
public sealed class CmdViaduto : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        InfraRunner.Run(uidoc, "infra:viaduto", () => { var b = new BridgeDefinition(); b.ApplyKindDefaults(); return b; }, d => InfraForms.Bridge(d, false), d => d.Kind = TipoObraDeArte.Viaduto);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdPonte : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        InfraRunner.Run(uidoc, "infra:ponte", () => { var b = new BridgeDefinition { Kind = TipoObraDeArte.Ponte }; b.ApplyKindDefaults(); return b; }, d => InfraForms.Bridge(d, false),
            d => d.Kind = TipoObraDeArte.Ponte);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdPassarela : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        InfraRunner.Run(uidoc, "infra:passarela", () => { var b = new BridgeDefinition { Kind = TipoObraDeArte.Passarela }; b.ApplyKindDefaults(); return b; }, d => InfraForms.Bridge(d, false),
            d => d.Kind = TipoObraDeArte.Passarela);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdTunel : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        InfraRunner.Run(uidoc, "infra:tunel", () => new TunnelDefinition(), d => InfraForms.Tunnel(d, false));
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdTrincheira : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        InfraRunner.Run(uidoc, "infra:trincheira", () => new TrenchDefinition(), d => InfraForms.Trench(d, false));
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
            var batch = new List<DrainageDefinition>();
            if (free)
            {
                var d = (DrainageDefinition)template.CloneWithNewId();
                d.Position = pt;
                d.Z = scanner.SurfaceZ(pt) ?? UnitConv.M(p.Z);
                // Alinhada à via mais próxima (quando houver).
                if (CurbFinder.Nearest(scanner.Faces(pt), pt, 30) is { } near) d.Along = near.Along;
                batch.Add(d);
            }
            else
            {
                var faces = scanner.Faces(pt);
                var hit = CurbFinder.Nearest(faces, pt);
                if (hit == null)
                {
                    TaskDialog.Show(CommandBase.AppTitle, "Nenhum meio-fio encontrado perto do clique. Clique junto à face de uma guia do plugin " +
                        "(via com calçada, interseção, rotatória, orelha ou meio-fio avulso) – ou use o tipo livre (grelha de piso / PV).");
                    continue;
                }
                var hits = new List<CurbHit> { hit };
                if (series)
                {
                    var q = Picking.PickPoint(uidoc, $"{template.DisplayCode}: clique o FIM da série no mesmo meio-fio – ESC cancela");
                    if (q == null) break;
                    hits = CurbFinder.Series(hit.Face, pt, UnitConv.ToVec2(q), template.SeriesSpacing);
                }
                foreach (var h in hits)
                {
                    var d = (DrainageDefinition)template.CloneWithNewId();
                    Fit(d, h);
                    batch.Add(d);
                }
            }
            var created = MarkingCreator.Commit(uidoc, batch, $"SV - {template.DisplayCode}");
            results.AddRange(created);
            foreach (var d in batch.Where(x => x.CutFloors))
            {
                try { FootprintCutter.ApplyDrainage(uidoc, d); }
                catch (Exception ex) { Log.Error("Recorte da drenagem", ex); }
            }
            scanner = new CurbScanner(doc, uidoc.ActiveView);           // pisos recortados: lê de novo
        }
        if (results.Count == 0) return Result.Cancelled;
        CommandBaseReport.Show(template.DisplayCode, results);
        return Result.Succeeded;
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
        if (warnings.Count > 0) TaskDialog.Show(CommandBase.AppTitle, $"{d0}:\n" + string.Join("\n", warnings.Take(15)));
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
[Transaction(TransactionMode.Manual)]
public sealed class CmdNoViario : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var template = UiHelpers.Remembered<InterchangeDefinition>("infra:no") ?? new InterchangeDefinition();
        template.Output = InfraRunner.Output3D();
        var w = InfraForms.Interchange(template, false);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        UiHelpers.Remember("infra:no", template);
        PluginContext.SaveSettings();
        var doc = uidoc.Document;
        var c = Picking.PickPoint(uidoc, "Interseção em desnível: clique o CENTRO (sobre o cruzamento de duas vias, ou um ponto livre) – ESC cancela");
        if (c == null) return Result.Cancelled;
        var d = (InterchangeDefinition)template.CloneWithNewId();
        var pt = UnitConv.ToVec2(c);
        d.Position = pt;
        d.Z = UnitConv.M(c.Z);
        // Sobre vias existentes: a mais importante (mais larga) é a principal; o ângulo vem dos eixos.
        var roads = MarkingStorage.Definitions(doc).OfType<RoadPavementDefinition>()
            .Select(r => (Road: r, Path: PathResolver.Resolve(doc, r.Path)))
            .Where(x => x.Path?.Main != null)
            .Select(x => (x.Road, Axis: x.Path!.Main!, Z: x.Path.Z, Hit: IntersectionGenerator.Project(x.Path.Main!, pt)))
            .Where(x => x.Hit.Distance < Math.Max(x.Road.TotalLeft, x.Road.TotalRight) + 10)
            .OrderByDescending(x => x.Road.TotalLeft + x.Road.TotalRight).ToList();
        if (roads.Count > 0)
        {
            var main = roads[0];
            d.Position = main.Hit.Point;
            d.Z = main.Z;
            var t = main.Axis.TangentAt(Math.Clamp(main.Hit.Station, 0, main.Axis.Length));
            d.AngleDeg = Math.Atan2(t.Y, t.X) * 180 / Math.PI;
            if (roads.Count > 1)
            {
                var o = roads[1];
                var t2 = o.Axis.TangentAt(Math.Clamp(o.Hit.Station, 0, o.Axis.Length));
                var ang = Math.Acos(Math.Clamp(Math.Abs(t.Dot(t2)), 0, 1)) * 180 / Math.PI;
                d.CrossAngleDeg = Math.Clamp(ang, 45, 90);
                if (t.Cross(t2) < 0) d.CrossAngleDeg = 180 - d.CrossAngleDeg;
            }
        }
        else
        {
            var q = Picking.PickPoint(uidoc, "Clique a direção da via principal – ESC = eixo X");
            if (q != null && UnitConv.ToVec2(q).DistanceTo(pt) > 0.5)
            {
                var dir = UnitConv.ToVec2(q) - pt;
                d.AngleDeg = Math.Atan2(dir.Y, dir.X) * 180 / Math.PI;
            }
        }
        var results = MarkingCreator.Commit(uidoc, new[] { d }, "SV - Interseção em desnível");
        TerrainActions.AfterCreate(uidoc, d);
        Report("Interseção em desnível", results);
        return Result.Succeeded;
    }
}

/// <summary>Terraplenagem: ajusta o Toposolid às vias, conexões e obras (corte, aterro, taludes e escavação).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdTerraplenagem : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        if (!new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Any())
        {
            TaskDialog.Show(AppTitle, "Nenhum Toposolid no projeto. Crie o terreno em Massa e terreno → Toposolid (ou converta a topografia antiga) e rode de novo.");
            return Result.Cancelled;
        }
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
        var form = new FormWindow("Terraplenagem", "Opções da terraplenagem",
                $"{defs.Count} elemento(s). Taludes das vias e conexões (as obras usam os próprios taludes).", null, null, false, "Aplicar", 640, 460)
            .Number("Talude de corte (H : 1 V)", () => opt.CutSlope, v => opt.CutSlope = v, 0.3, 5, "0.0#", "Solo: 1 : 1; rocha: 0,5 : 1 (DNIT).")
            .Number("Talude de aterro (H : 1 V)", () => opt.FillSlope, v => opt.FillSlope = v, 1, 5, "0.0#", "Usual 1,5 : 1.")
            .Number("Profundidade do subleito sob o pavimento (m)", () => opt.Subgrade, v => opt.Subgrade = v, 0, 2)
            .Number("Alcance máximo dos taludes (m)", () => opt.MaxDaylight, v => opt.MaxDaylight = v, 5, 500, "0")
            .Check("Escavar o terreno com túneis e caixas (quando o Revit permitir)", () => opt.Excavate, v => opt.Excavate = v);
        if (UiHelpers.ShowModal(form) != true) return Result.Cancelled;
        var report = TerrainActions.Apply(uidoc, defs, opt, "SV - Terraplenagem");
        if (report != null) TaskDialog.Show(AppTitle, report.Text());
        return report != null ? Result.Succeeded : Result.Failed;
    }
}
