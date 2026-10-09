using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Extensão de calçada (avanço sobre a faixa de estacionamento) num ponto qualquer da via, gravada na seção dela e
/// refeita a partir do eixo sempre que a via muda: lado, trecho (estaca inicial e comprimento ao longo do eixo), avanço,
/// pontas, travessia no meio da quadra e mobiliário.
/// </summary>
public sealed class ExtensaoCalcada
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Lado esquerdo do eixo (no sentido do desenho); falso = lado direito.</summary>
    public bool LadoEsquerdo { get; set; }
    /// <summary>Estaca do início do trecho (m ao longo do eixo).</summary>
    public double Estaca { get; set; }
    /// <summary>Comprimento ao longo do eixo (m), pontas incluídas.</summary>
    public double Comprimento { get; set; } = 10.0;
    /// <summary>Avanço sobre a pista (m). Nulo = a largura do estacionamento daquele lado (com a sarjeta).</summary>
    public double? Profundidade { get; set; }
    public TipoTransicao Ponta { get; set; } = TipoTransicao.Curva;
    /// <summary>Raio da curva (ou comprimento do chanfro) da ponta inicial (m).</summary>
    public double Raio { get; set; } = 1.50;
    /// <summary>Ponta final (nulo = igual à inicial).</summary>
    public TipoTransicao? PontaFinal { get; set; }
    public double? RaioFinal { get; set; }
    /// <summary>Travessia de pedestres no meio da quadra no centro da extensão (faixa, rampas e piso tátil).</summary>
    public bool Travessia { get; set; }
    public double LarguraFaixa { get; set; } = 4.0;
    /// <summary>Rampas nas pontas da travessia.</summary>
    public bool Rampa { get; set; } = true;
    /// <summary>Piso tátil de alerta nas rampas.</summary>
    public bool Tatil { get; set; } = true;
    /// <summary>Canteiro gramado na extensão (com as árvores dentro dele).</summary>
    public bool Canteiro { get; set; }
    public int Arvores { get; set; }
    public int Paraciclos { get; set; }
    public int Bancos { get; set; }

    public ExtensaoCalcada Clone() => (ExtensaoCalcada)MemberwiseClone();

    /// <summary>Estaca do eixo da travessia (centro do trecho).</summary>
    public double EstacaTravessia => Estaca + Comprimento / 2;
}

public sealed partial class RoadSetup
{
    /// <summary>Extensões de calçada em pontos quaisquer da via (meio da quadra), geradas com ela.</summary>
    public List<ExtensaoCalcada> ExtensoesCalcada { get; set; } = new();

    /// <summary>
    /// Piso tátil nas calçadas: faixa direcional no meio da faixa livre, ligada a cada rampa por um ramal perpendicular com
    /// alerta no topo da rampa e nas junções (NBR 16537 – posições [a confirmar]).
    /// </summary>
    public bool PisoTatil { get; set; }

    /// <summary>Um lado da via: face do meio-fio (afastamento do eixo), calçada, estacionamento junto ao meio-fio e sarjeta.</summary>
    public sealed record LadoVia(int Sigma, double Face, bool HasSidewalk, double SidewalkWidth, double CurbW, double Service, double Free,
        double Access, double Top, double? Parking, double Gutter, bool Grass = false)
    {
        /// <summary>Afastamento (a partir da face do meio-fio) do meio da faixa livre.</summary>
        public double FreeCenter => CurbW + Service + Free / 2;
    }

    /// <summary>Medidas de um lado da via, como a geração das calçadas as usa.</summary>
    public LadoVia Lado(bool left)
    {
        var side = left ? Left : Right;
        var sigma = left ? 1 : -1;
        double a = MedianHalf;
        for (int i = 0; i < side.Count; i++)
        {
            var e = side[i];
            if (e.Tipo == TipoElementoSecao.Calcada)
            {
                var added = AddedGutter(side, i);
                a += added;
                var width = Math.Max(0.05, e.Largura);
                var cw = Math.Min(e.MeioFioEfetivo, width - 0.05);
                var service = Math.Clamp(e.FaixaServico, cw, width) - cw;
                var access = Math.Clamp(e.FaixaAcesso, 0, Math.Max(0, width - cw - service));
                var free = width - cw - service - access;
                double? park = i > 0 && side[i - 1].Tipo == TipoElementoSecao.Estacionamento && !side[i - 1].Elevado ? side[i - 1].Largura + added : null;
                return new LadoVia(sigma, a, true, width, cw, service, free, access, e.AlturaEfetiva, park, park != null ? added : 0,
                    e.ServicoGramado && service > 0.02);
            }
            a += Math.Max(0.05, e.Largura);
        }
        return new LadoVia(sigma, a, false, 0, 0, 0, 0, 0, 0, null, 0);
    }
}

/// <summary>Recorte que um elemento derivado faz nas marcas da via.</summary>
public enum TipoRecorteVia
{
    /// <summary>Extensão: pavimento, sarjeta e meio-fio saem na zona dela (com o meio-fio antigo); a pintura e as vagas, sob ela.</summary>
    Extensao,
    /// <summary>Passagem pavimentada da rampa até a faixa livre: só a grama da faixa de serviço sai sob ela.</summary>
    Passagem,
    /// <summary>Rampa: calçada, grama e meio-fio saem sob ela.</summary>
    Rampa,
}

public sealed record RecorteVia(string SourceId, TipoRecorteVia Tipo, List<Polygon2> Zona, List<Polygon2> SobAPintura);

/// <summary>Elementos derivados da seção da via (refeitos a partir do eixo) e os recortes deles nas marcas da via.</summary>
public sealed class ElementosDerivados
{
    public List<MarkingDefinition> Children { get; } = new();
    public List<RecorteVia> Cuts { get; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>Prefixo dos ids dos filhos e das origens dos recortes (id da via + ":").</summary>
    public string Prefix { get; init; } = "";
}

/// <summary>
/// Elementos derivados da seção de uma via que dependem da geometria do eixo: extensões de calçada (com a sarjeta na frente,
/// travessia, rampas e mobiliário) e a rede de piso tátil das calçadas. Os ids são fixos (id da via + extensão + papel), de
/// modo que a via e o plugin os refazem no lugar quando o eixo muda.
/// </summary>
public static class RoadFeatures
{
    /// <summary>Folga lateral da travessia sem mobiliário, além da rampa com as abas (m).</summary>
    public const double CrossingClearance = 0.50;
    /// <summary>Profundidade do alerta no topo das rampas (2 placas de 0,25 m) – [a confirmar].</summary>
    public const double RampTopAlert = 0.50;

    public static bool HasAny(RoadSetup s) => s.ExtensoesCalcada.Count > 0 || s.PisoTatil || s.Sinalizacao is { Empty: false };

    /// <summary>Prefixo dos ids dos elementos derivados de uma via.</summary>
    public static string PrefixOf(RoadPavementDefinition pav) => pav.Id + ":";

    /// <summary>Rampa das travessias da via: largura da faixa (mín. 1,50 m), 8,33 %, abas de 10 % (NBR 9050).</summary>
    public static RampDefinition RampTemplate(double width, double height, bool tactile) => new()
    {
        Type = TipoRampa.RebaixamentoComAbas,
        Width = Math.Max(IntersectionGenerator.MinRampWidth, width),
        Height = Math.Clamp(height, 0.02, 0.40),
        Slope = IntersectionGenerator.MaxRampSlope,
        FlareSlope = IntersectionGenerator.MaxFlareSlope,
        Tactile = tactile,
        SquareCut = true,
        CutSidewalk = false,
    };

    /// <summary>Plano de uma extensão: trecho final (estacas no eixo), avanço da face, sarjeta e o modelo de extensão.</summary>
    private sealed record Plano(ExtensaoCalcada Spec, RoadSetup.LadoVia Lado, double S0, double S1, double Face, double Gutter,
        Polyline2 Path, CurbExtensionDefinition Template, Polygon2 Footprint, List<Polygon2> CutZone, Polyline2? GutterPath, string Id);

    public static ElementosDerivados Build(RoadSetup s, RoadPavementDefinition pav, Polyline2 axis, double z, OutputSettings output,
        string? groupId, Catalogo? cat)
    {
        var res = new ElementosDerivados { Prefix = PrefixOf(pav) };
        if (axis.Length < 2 || !HasAny(s)) return res;
        var hierarchy = s.Hierarchy == HierarquiaViaria.NaoDefinida ? (HierarquiaViaria?)null : s.Hierarchy;
        T Add<T>(T d, string role) where T : MarkingDefinition
        {
            d.Id = res.Prefix + role;
            d.Output = output.Clone();
            d.GroupId = groupId;
            d.Hierarchy = hierarchy;
            res.Children.Add(d);
            return d;
        }
        Vec2 At(double st, double o) => PointAt(axis, st, o);
        MarkingDefinition AddD(MarkingDefinition d, string role) => Add(d, role);
        var left = s.Lado(true);
        var right = s.Lado(false);
        RoadSetup.LadoVia LadoDe(bool l) => l ? left : right;

        // 1. Extensões pedidas (as que levam travessia são estendidas para a faixa e as rampas caberem).
        var planos = new List<Plano>();
        foreach (var e in s.ExtensoesCalcada)
        {
            var p = Planejar(s, e, LadoDe(e.LadoEsquerdo), axis, res.Prefix + e.Id, res.Warnings);
            if (p != null) planos.Add(p);
        }
        // 2. Travessias: extensão espelhada do outro lado (quando há estacionamento lá e nenhuma extensão cobre a faixa).
        foreach (var p in planos.ToList().Where(p => p.Spec.Travessia))
        {
            var sc = p.Spec.EstacaTravessia;
            var other = LadoDe(!p.Spec.LadoEsquerdo);
            if (planos.Any(q => q.Spec.LadoEsquerdo != p.Spec.LadoEsquerdo && q.S0 <= sc - p.Spec.LarguraFaixa / 2 && q.S1 >= sc + p.Spec.LarguraFaixa / 2)) continue;
            if (other.Parking == null || !other.HasSidewalk) continue;
            var mirror = p.Spec.Clone();
            mirror.LadoEsquerdo = !p.Spec.LadoEsquerdo;
            mirror.Profundidade = null;
            mirror.Estaca = p.S0;
            mirror.Comprimento = p.S1 - p.S0;
            mirror.Travessia = false;
            mirror.Canteiro = false;
            mirror.Arvores = mirror.Paraciclos = mirror.Bancos = 0;
            var q = Planejar(s, mirror, other, axis, res.Prefix + p.Spec.Id + "-oposta", res.Warnings);
            if (q != null) planos.Add(q);
        }

        // 3. Extensões: piso, sarjeta na frente e recortes.
        var rampsBySide = new Dictionary<int, List<(RampDefinition Ramp, string Id)>> { [1] = new(), [-1] = new() };
        var furniture = new List<Polygon2>();
        foreach (var p in planos)
        {
            var ext = (CurbExtensionDefinition)p.Template.CloneWithNewId();
            ext.PathRef = PathReference.FromPoints(p.Path.Points, z);
            Add(ext, p.Id[res.Prefix.Length..] + ":ext");
            if (p.GutterPath != null)
            {
                var g = new LinearMarkingDefinition { Code = "SARJETA", WidthOverride = p.Gutter, PathRef = PathReference.FromPoints(p.GutterPath.Points, z) };
                g.Exclusions.Add(new ExclusionZone { SourceId = p.Id, Points = p.Footprint.Outer.ToList() });
                Add(g, p.Id[res.Prefix.Length..] + ":sarjeta");
            }
            res.Cuts.Add(new RecorteVia(p.Id, TipoRecorteVia.Extensao, p.CutZone, new List<Polygon2> { p.Footprint }));
        }

        // 4. Travessias no meio da quadra: faixa de face a face, rampas alinhadas nas duas pontas.
        foreach (var p in planos.Where(p => p.Spec.Travessia))
        {
            var e = p.Spec;
            var sc = e.EstacaTravessia;
            var w = Math.Max(1.0, e.LarguraFaixa);
            var role = p.Id[res.Prefix.Length..];
            var other = LadoDe(!e.LadoEsquerdo);
            var otherPlan = planos.FirstOrDefault(q => q != p && q.Spec.LadoEsquerdo != e.LadoEsquerdo && q.S0 <= sc - w / 2 && q.S1 >= sc + w / 2);
            var thisFace = p.Lado.Sigma * (p.Lado.Face - p.Face);
            var otherFace = other.Sigma * (other.Face - (otherPlan?.Face ?? 0));
            var cws = new CrosswalkSetup { CrosswalkWidth = w, StopLines = false, EdgeSetback = 0.3 }
                .Build(At(sc, thisFace), At(sc, otherFace), z, new OutputSettings(), w, 0.40);
            var medRect = s.TwoWay && s.Center == CenterTreatment.Canteiro && s.MedianType == TipoCanteiro.Fisico && s.MedianWidth > 0.3
                ? new[] { At(sc - w / 2 - 0.5, -s.MedianWidth / 2), At(sc + w / 2 + 0.5, -s.MedianWidth / 2), At(sc + w / 2 + 0.5, s.MedianWidth / 2), At(sc - w / 2 - 0.5, s.MedianWidth / 2) }.ToList()
                : null;
            for (int k = 0; k < cws.Count; k++)
            {
                if (medRect != null) cws[k].Exclusions.Add(new ExclusionZone { SourceId = p.Id, Points = medRect });
                Add(cws[k], role + ":faixa" + (k == 0 ? "" : k.ToString()));
            }
            if (!e.Rampa) continue;
            // Rampas na face da extensão (deste lado) e na do outro lado (extensão ou meio-fio da via).
            foreach (var (lado, face, plan, tag) in new[] { (p.Lado, p.Face, p, "rampa"), (other, otherPlan?.Face ?? 0, otherPlan, "rampa-oposta") })
            {
                if (!lado.HasSidewalk) { res.Warnings.Add("Travessia: o outro lado da via não tem calçada – sem rampa nele."); continue; }
                var tpl = RampTemplate(w, lado.Top, e.Tatil);
                var (_, run, _) = RampGenerator.Dimensions(tpl);
                // Sem extensão e com calçada estreita (rampa + 1,20 m livres não cabem): rebaixamento total da calçada.
                if (plan == null && lado.SidewalkWidth - run < 1.20)
                {
                    tpl.Type = TipoRampa.RebaixamentoTotal;
                    tpl.SidewalkDepth = lado.SidewalkWidth;
                    tpl.Slope = IntersectionGenerator.TotalSideSlope;
                    res.Warnings.Add($"Travessia na estaca {PontoLargura.FormatEstaca(sc)}: calçada estreita para a rampa e a faixa livre de 1,20 m – feito o rebaixamento total.");
                }
                var curb = At(sc, lado.Sigma * (lado.Face - face));
                var up = (At(sc, lado.Sigma * (lado.Face - face + 1)) - curb).Normalized();
                var ramp = (RampDefinition)tpl.CloneWithNewId();
                ramp.PathRef = PathReference.FromPoints(new[] { curb, curb + up }, z);
                var rid = res.Prefix + role + ":" + tag;
                Add(ramp, role + ":" + tag);
                var fp = RampGenerator.Footprint(ramp, new Polyline2(ramp.PathRef.Points));
                var body = RampBody(ramp);
                res.Cuts.Add(new RecorteVia(rid, TipoRecorteVia.Rampa, new List<Polygon2> { fp }, body));
                // A sarjeta em frente à face (na curva a face se afasta da base reta da rampa) sai só sob o corpo da rampa.
                foreach (var g in res.Children.OfType<LinearMarkingDefinition>().Where(g => g.Code == "SARJETA"))
                    foreach (var b in body) g.Exclusions.Add(new ExclusionZone { SourceId = rid, Points = b.Outer.ToList() });
                // A extensão é recortada pela rampa que fica na borda dela.
                if (plan != null)
                    foreach (var c in res.Children.OfType<CurbExtensionDefinition>().Where(c => c.Id == plan.Id + ":ext"))
                        c.Exclusions.Add(new ExclusionZone { SourceId = rid, Points = fp.Outer.ToList() });
                rampsBySide[lado.Sigma].Add((ramp, rid));
                // Faixa de serviço gramada entre o topo da rampa e a faixa livre: passagem pavimentada na largura da rampa (a rota
                // acessível e o ramal tátil não passam sobre a grama).
                if (lado.Grass && Passagem(axis, lado, ramp) is { } pass)
                {
                    var pid = res.Prefix + role + ":passagem" + (tag == "rampa" ? "" : "-oposta");
                    Add(new SidewalkAreaDefinition
                    {
                        PathRef = PathReference.FromPoints(pass.Outer, z, closed: true), Type = TipoAreaCalcada.Calcada, Curb = false, Height = lado.Top,
                    }, role + ":passagem" + (tag == "rampa" ? "" : "-oposta"));
                    res.Cuts.Add(new RecorteVia(pid, TipoRecorteVia.Passagem, new List<Polygon2> { pass }, new List<Polygon2> { pass }));
                }
            }
        }

        // 5. Mobiliário e canteiro nas extensões, fora da travessia.
        foreach (var p in planos)
            furniture.AddRange(Mobiliario(p, axis, z, cat, res, AddD));

        // 6. Piso tátil: faixa direcional no meio da faixa livre de cada calçada, ligada a cada rampa da via.
        if (s.PisoTatil)
            foreach (var lado in new[] { left, right }.Where(l => l.HasSidewalk && l.Free >= 0.6))
                Tatil(s, lado, axis, z, rampsBySide[lado.Sigma], res, AddD);

        // 7. Sinalização vertical automática da via (R-19, R-24a, A-32b nas travessias) – refeita com o eixo, ids fixos.
        if (s.Sinalizacao is { Empty: false })
        {
            var crossings = planos.Where(p => p.Spec.Travessia).Select(p => p.Spec.EstacaTravessia)
                .Concat(s.TravessiasCanteiro.Where(t => t.Faixa).Select(t => t.Estaca)).Distinct().ToList();
            foreach (var (sign, role) in AutoSignage.RoadSigns(s, axis, z, crossings)) Add(sign, role);
        }
        return res;
    }

    /// <summary>Trecho, caminho na face do meio-fio e contorno de uma extensão; nulo (com aviso) quando não cabe.</summary>
    private static Plano? Planejar(RoadSetup s, ExtensaoCalcada e, RoadSetup.LadoVia lado, Polyline2 axis, string id, List<string> warnings)
    {
        var tag = $"Extensão de calçada na estaca {PontoLargura.FormatEstaca(e.Estaca)}";
        if (!lado.HasSidewalk) { warnings.Add($"{tag}: o lado da via não tem calçada."); return null; }
        var depth = e.Profundidade is > 0.1 ? e.Profundidade.Value : lado.Parking;
        if (depth == null) { warnings.Add($"{tag}: o lado não tem estacionamento junto ao meio-fio – informe a profundidade."); return null; }
        depth = Math.Clamp(depth.Value, 0.3, 10);
        // A sarjeta da via continua na frente da extensão (a face fica a avanço − sarjeta da original).
        var gutter = lado.Gutter;
        if (depth - gutter < 0.6) gutter = 0;
        var face = depth.Value - gutter;
        var kind0 = e.Ponta;
        var kind1 = e.PontaFinal ?? e.Ponta;
        var r0 = e.Raio;
        var r1 = e.RaioFinal ?? e.Raio;
        var lt0 = IntersectionGenerator.EarTransition(kind0, r0, face);
        var lt1 = IntersectionGenerator.EarTransition(kind1, r1, face);
        var s0 = e.Estaca;
        var s1 = e.Estaca + Math.Max(1.0, e.Comprimento);
        if (e.Travessia)
        {
            // A parte com o avanço inteiro cobre a faixa e as rampas com as abas (+ folga): senão a extensão é estendida.
            var tpl = RampTemplate(e.LarguraFaixa, lado.Top, e.Tatil);
            var half = Math.Max(e.LarguraFaixa / 2, e.Rampa ? IntersectionGenerator.RampHalfExtent(tpl) : 0) + 0.3;
            var sc = e.EstacaTravessia;
            var need0 = sc - half - lt0;
            var need1 = sc + half + lt1;
            if (need0 < s0 - 1e-6 || need1 > s1 + 1e-6)
            {
                s0 = Math.Min(s0, need0);
                s1 = Math.Max(s1, need1);
                warnings.Add($"{tag}: estendida para {s1 - s0:0.00} m para a travessia e as rampas ficarem sobre ela.");
            }
        }
        if (s0 < 0.5 || s1 > axis.Length - 0.5)
        {
            warnings.Add($"{tag}: fora do trecho da via.");
            s0 = Math.Max(0.5, s0);
            s1 = Math.Min(axis.Length - 0.5, s1);
            if (s1 - s0 < 1.0) return null;
        }
        // Face do meio-fio daquele lado no trecho (deslocamento do eixo, como o meio-fio da via).
        var face0 = new Polyline2(axis.SubPoints(s0, s1)).Offset(lado.Sigma * lado.Face);
        var tplExt = new CurbExtensionDefinition
        {
            Depth = face, Transition = kind0, Radius = r0, EndTransition = kind1, EndRadius = r1,
            SidewalkOnLeft = lado.Sigma > 0, Height = lado.Top, CurbWidth = lado.CurbW, CutRoadMarkings = true,
        };
        var fp = SidewalkGenerator.EarFootprint(tplExt, face0);
        if (fp == null) { warnings.Add($"{tag}: não foi possível montar o contorno."); return null; }
        Polyline2? gutterPath = null;
        if (gutter > 0.01)
        {
            var edge = new Polyline2(IntersectionGenerator.CleanFacePath(SidewalkGenerator.EarOuterEdge(tplExt, face0)));
            if (edge.Length > 1) gutterPath = edge.Offset(lado.Sigma > 0 ? -gutter / 2 : gutter / 2);
        }
        var cut = SidewalkGenerator.EarCutZone(tplExt, face0);
        if (gutterPath != null) cut = PolygonOps.Union(cut.Concat(PolygonOps.Strip(gutterPath.Points, gutter, roundJoins: true)));
        return new Plano(e, lado, s0, s1, face, gutter, face0, tplExt, fp, cut, gutterPath, id);
    }

    /// <summary>
    /// Bancos, paraciclos, árvores e canteiro na parte com o avanço inteiro, fora da travessia (rampa com as abas + folga).
    /// Devolve as áreas ocupadas (para o piso tátil não passar por elas).
    /// </summary>
    private static List<Polygon2> Mobiliario(Plano p, Polyline2 axis, double z, Catalogo? cat, ElementosDerivados res,
        Func<MarkingDefinition, string, MarkingDefinition> add)
    {
        var e = p.Spec;
        var used = new List<Polygon2>();
        if (e.Bancos + e.Paraciclos + e.Arvores <= 0 && !e.Canteiro) return used;
        var path = p.Path;
        var L = path.Length;
        var lt0 = IntersectionGenerator.EarTransition(p.Template.Transition, p.Template.Radius, p.Face);
        var lt1 = IntersectionGenerator.EarTransition(p.Template.EndTransition ?? p.Template.Transition, p.Template.EndRadius ?? p.Template.Radius, p.Face);
        var free = new List<(double A, double B)> { (lt0 + 0.3, L - lt1 - 0.3) };
        if (e.Travessia)
        {
            var (xc, _) = path.Project(axis.PointAt(Math.Clamp(e.EstacaTravessia, 0, axis.Length)));
            var tpl = RampTemplate(e.LarguraFaixa, p.Lado.Top, e.Tatil);
            var half = Math.Max(e.LarguraFaixa / 2, e.Rampa ? IntersectionGenerator.RampHalfExtent(tpl) : 0) + CrossingClearance;
            free = free.SelectMany(f => new[] { (f.A, Math.Min(f.B, xc - half)), (Math.Max(f.A, xc + half), f.B) }).Where(f => f.Item2 - f.Item1 > 0.1).ToList();
        }
        // Normal para a pista (a extensão avança para lá) e posição lateral no meio da plataforma.
        var toRoad = p.Lado.Sigma > 0 ? -1.0 : 1.0;
        Vec2 Pt(double x, double o) => path.PointAt(Math.Clamp(x, 0, L)) + path.TangentAt(Math.Clamp(x, 0, L)).PerpLeft * (toRoad * o);
        Vec2 Tan(double x) => path.TangentAt(Math.Clamp(x, 0, L));
        var mid = Math.Max(0.4, p.Face / 2);
        var k = 0;
        bool Take(double len, out double x)
        {
            for (int i = 0; i < free.Count; i++)
            {
                var (a, b) = free[i];
                if (b - a < len) continue;
                x = a + len / 2;
                free[i] = (a + len, b);
                return true;
            }
            x = 0;
            return false;
        }
        void Item(string code, double len, Vec2 dir, int count, string label)
        {
            var placed = 0;
            for (int i = 0; i < count; i++)
            {
                if (!Take(len, out var x)) break;
                var el = new UrbanElementDefinition { Code = code, Position = Pt(x, mid), Direction = dir.Length < 1e-9 ? Tan(x) : dir, Z = z, BaseElevation = p.Lado.Top };
                if (code == "BANCO") el.Direction = (Pt(x, mid - 1) - Pt(x, mid)).Normalized();   // voltado para a calçada
                if (code == "PARACICLO") el.Direction = Tan(x);
                add(el, p.Id[res.Prefix.Length..] + $":mob{k++}");
                var t = Tan(x);
                var n = t.PerpLeft;
                var hl = len / 2;
                var hw = 0.45;
                var c = Pt(x, mid);
                used.Add(new Polygon2(new[] { c - t * hl - n * hw, c + t * hl - n * hw, c + t * hl + n * hw, c - t * hl + n * hw }));
                placed++;
            }
            if (placed < count) res.Warnings.Add($"Extensão de calçada na estaca {PontoLargura.FormatEstaca(e.Estaca)}: sem espaço para {count - placed} {label}.");
        }
        Item("BANCO", 2.2, Vec2.Zero, e.Bancos, "banco(s)");
        Item("PARACICLO", 0.9, Vec2.Zero, e.Paraciclos, "paraciclo(s)");
        var ext = (CurbExtensionDefinition?)res.Children.FirstOrDefault(c => c.Id == p.Id + ":ext");
        if (e.Canteiro && ext != null)
        {
            // Canteiro nos trechos que sobraram (árvores dentro dele).
            ext.Planter = true;
            ext.PlanterRanges = free.Where(f => f.B - f.A > 1.0).Select(f => new StationRange { Start = f.A, End = f.B }).ToList();
            ext.Trees = ext.PlanterRanges.Count > 0 ? e.Arvores : 0;
            if (ext.PlanterRanges.Count == 0) res.Warnings.Add($"Extensão de calçada na estaca {PontoLargura.FormatEstaca(e.Estaca)}: sem espaço para o canteiro.");
            foreach (var r in ext.PlanterRanges)
                used.Add(PolygonOps.Strip(new Polyline2(path.SubPoints(r.Start, r.End)).Offset(toRoad * p.Face / 2).Points, p.Face, roundJoins: false).OrderByDescending(x => x.Area).First());
        }
        else Item("ARVORE", 1.5, Vec2.Zero, e.Arvores, "árvore(s)");
        return used;
    }

    /// <summary>
    /// Rede tátil de um lado: faixa direcional no meio da faixa livre (ao longo da via) e, para cada rampa daquele lado, um
    /// ramal perpendicular até a faixa de alerta no topo da rampa (junção com alerta na faixa principal).
    /// </summary>
    private static void Tatil(RoadSetup s, RoadSetup.LadoVia lado, Polyline2 axis, double z, List<(RampDefinition Ramp, string Id)> ramps,
        ElementosDerivados res, Func<MarkingDefinition, string, MarkingDefinition> add)
    {
        var tag = lado.Sigma > 0 ? "E" : "D";
        var off = lado.Sigma * (lado.Face + lado.FreeCenter);
        var main = axis.Offset(off);
        var branches = new List<List<Vec2>>();
        var cover = new List<Polygon2>();
        var k = 0;
        foreach (var (ramp, _) in ramps)
        {
            var top = RampTop(ramp);
            if (top == null) continue;
            var (a, b, dir) = top.Value;
            var mid = (a + b) / 2;
            // Alerta no topo da rampa, na largura dela (a faixa principal não passa por cima dele).
            var alertEnd = mid + dir * RampTopAlert;
            cover.Add(new Polygon2(new[] { a, b, b + dir * RampTopAlert, a + dir * RampTopAlert }));
            add(new TactileRouteDefinition
            {
                PathRef = PathReference.FromPoints(new[] { a + dir * (RampTopAlert / 2), b + dir * (RampTopAlert / 2) }, z),
                AlertOnly = true, Module = 0.25, Rows = (int)Math.Round(RampTopAlert / 0.25), Elevation = lado.Top,
            }, $"tatil-alerta-{tag}{k++}");
            // Ramal perpendicular do topo (alerta) até a faixa principal.
            var (st, _) = main.Project(alertEnd);
            var foot = main.PointAt(st);
            if (foot.DistanceTo(alertEnd) > 0.05 && (foot - alertEnd).Dot(dir) > 0)
                branches.Add(new List<Vec2> { alertEnd, foot });
        }
        var route = add(new TactileRouteDefinition
        {
            PathRef = PathReference.FromPoints(main.Points, z),
            Module = 0.25, Rows = 1, Elevation = lado.Top, AlertAtEnds = false, AlertAtJunctions = true, AlertAtTurns = true,
            Branches = branches.Count > 0 ? branches : null,
        }, $"tatil-{tag}");
        // Nada sobre as rampas (rebaixamento total de calçada estreita) e sob os alertas do topo.
        foreach (var (ramp, rid) in ramps)
            route.Exclusions.Add(new ExclusionZone { SourceId = rid, Points = RampGenerator.Footprint(ramp, new Polyline2(ramp.PathRef.Points)).Outer.ToList() });
        foreach (var c in cover) route.Exclusions.Add(new ExclusionZone { SourceId = res.Prefix + "tatil", Points = c.Outer.ToList() });
    }

    /// <summary>
    /// Ponto na estaca e no afastamento indicados. A normal vem da diferença central (média dos dois segmentos num vértice):
    /// as pontas de uma travessia nos dois lados de uma via curva ficam na mesma estaca.
    /// </summary>
    public static Vec2 PointAt(Polyline2 axis, double st, double o)
    {
        var ss = Math.Clamp(st, 0, axis.Length);
        var a = axis.PointAt(Math.Max(0, ss - 0.05));
        var b = axis.PointAt(Math.Min(axis.Length, ss + 0.05));
        var t = (b - a).Length > 1e-9 ? (b - a).Normalized() : axis.TangentAt(ss);
        return axis.PointAt(ss) + t.PerpLeft * o;
    }

    /// <summary>
    /// Trecho da faixa de serviço (do fim do meio-fio ou do topo da rampa até o início da faixa livre) na largura do topo da rampa,
    /// seguindo o eixo; nulo se a rampa já chega à faixa livre.
    /// </summary>
    public static Polygon2? Passagem(Polyline2 axis, RoadSetup.LadoVia lado, RampDefinition ramp)
    {
        if (RampTop(ramp) is not { } top) return null;
        var (a, b, _) = top;
        var (sa, oa) = axis.Project(a);
        var (sb, ob) = axis.Project(b);
        var start = Math.Max(lado.Face + lado.CurbW, Math.Min(Math.Abs(oa), Math.Abs(ob)));
        var end = lado.Face + lado.CurbW + lado.Service;
        if (end - start < 0.05) return null;
        var (s0, s1) = (Math.Min(sa, sb), Math.Max(sa, sb));
        var n = Math.Max(1, (int)Math.Ceiling((s1 - s0) / 0.25));
        var pts = new List<Vec2>();
        for (int i = 0; i <= n; i++) pts.Add(PointAt(axis, s0 + (s1 - s0) * i / n, lado.Sigma * start));
        for (int i = n; i >= 0; i--) pts.Add(PointAt(axis, s0 + (s1 - s0) * i / n, lado.Sigma * end));
        var poly = new Polygon2(pts);
        return poly.Area > 0.01 ? poly : null;
    }

    /// <summary>Corpo da rampa (a partir da face do meio-fio, sem a folga de 5 cm sobre a pista do recorte).</summary>
    public static List<Polygon2> RampBody(RampDefinition r)
    {
        var path = new Polyline2(r.PathRef.Points);
        var f = RampGenerator.FrameOf(path);
        var back = new Polygon2(new[] { f.P(-50, 0), f.P(50, 0), f.P(50, 50), f.P(-50, 50) });
        return PolygonOps.Intersect(new[] { RampGenerator.Footprint(r, path) }, new[] { back }).Where(p => p.Area > 1e-4).ToList();
    }

    /// <summary>Topo de uma rampa (borda do lado da calçada): extremos e direção de subida. Nulo para rebaixamento total.</summary>
    public static (Vec2 A, Vec2 B, Vec2 Up)? RampTop(RampDefinition r)
    {
        if (r.PathRef.Points.Count < 2 || r.Type == TipoRampa.RebaixamentoTotal) return null;
        var f = RampGenerator.FrameOf(new Polyline2(r.PathRef.Points));
        var (w, len, _) = RampGenerator.Dimensions(r);
        var top = len + (r.LandingDepth > 0.05 ? r.LandingDepth : 0);
        return (f.P(-w / 2, top), f.P(w / 2, top), f.Up);
    }

    /// <summary>
    /// Aplica os recortes nas marcas da via: extensões tiram pavimento, sarjeta e meio-fio na zona delas e a pintura e as vagas
    /// sob elas; rampas tiram calçada, grama, meio-fio e o piso tátil. Recortes antigos da mesma origem saem antes.
    /// </summary>
    public static void ApplyCuts(ElementosDerivados f, IEnumerable<MarkingDefinition> members)
    {
        foreach (var m in members)
        {
            if (f.Prefix.Length > 0 && m.Id.StartsWith(f.Prefix)) continue;
            if (f.Prefix.Length > 0) m.Exclusions.RemoveAll(z => z.SourceId != null && z.SourceId.StartsWith(f.Prefix));
            foreach (var c in f.Cuts)
            {
                List<Polygon2>? zone = c.Tipo switch
                {
                    TipoRecorteVia.Extensao when IntersectionGenerator.CutByEars(m) =>
                        m is RoadPavementDefinition || m is LinearMarkingDefinition l && (l.Code.StartsWith("SARJETA") || l.Code.StartsWith("MEIO-FIO")) ? c.Zona : c.SobAPintura,
                    TipoRecorteVia.Rampa when IntersectionGenerator.CutByRamps(m) || m is TactileRouteDefinition => c.Zona,
                    TipoRecorteVia.Rampa when m is LinearMarkingDefinition { Code: "SARJETA" } => c.SobAPintura,
                    TipoRecorteVia.Passagem when m is LinearMarkingDefinition { Code: "GRAMADO" } => c.Zona,
                    _ => null,
                };
                if (zone == null) continue;
                foreach (var z in zone) m.Exclusions.Add(new ExclusionZone { SourceId = c.SourceId, Points = z.Outer.ToList() });
            }
        }
    }
}
