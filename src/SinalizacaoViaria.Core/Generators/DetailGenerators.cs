using System.Globalization;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Ferramentas de detalhamento 2D: símbolos de placas em planta com chamada, anotações de
/// sinalização e quadro de legenda. Dimensões em mm de papel, convertidas pela escala da vista.
/// </summary>
public static partial class DetailGenerator
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string F(double v, string fmt = "0.00") => v.ToString(fmt, Pt);

    public const string MissingTarget = "Elemento de referência não encontrado";

    private static MarkingGeometry Missing(string msg)
    {
        var g = new MarkingGeometry();
        g.Warnings.Add(msg);
        return g;
    }

    // ------------------------------------------------------------------ placas em planta

    /// <summary>Face da placa sem sobreposições (orla, fundo e legenda disjuntos) – para regiões 2D.</summary>
    public static List<(Polygon2 Shape, MarkingColor Color)> FlatFace(PlacaDef p, double w, double h, string? legend, IGlyphOutlineProvider glyphs)
    {
        var layers = SignGenerator.Face(p, w, h, 0, legend, glyphs);
        var res = new List<(Polygon2, MarkingColor)>();
        var maxLayer = layers.Count == 0 ? 0 : layers.Max(l => l.Layer);
        for (int layer = 0; layer <= maxLayer; layer++)
        {
            var mine = layers.Where(l => l.Layer == layer).ToList();
            var above = layers.Where(l => l.Layer > layer).Select(l => l.Shape).ToList();
            foreach (var group in mine.GroupBy(m => m.Color))
            {
                var shapes = PolygonOps.Union(group.Select(g => g.Shape));
                var cut = above.Count > 0 ? PolygonOps.Difference(shapes, above) : shapes;
                res.AddRange(cut.Select(c => (c, group.Key)));
            }
        }
        return res;
    }

    public static MarkingGeometry SignDetail(SignPlanDetailDefinition d, BuildContext ctx)
    {
        if (ctx.Lookup?.Invoke(d.SignId) is not SignDefinition sign) return Missing(MissingTarget + " (placa excluída).");
        var placa = ctx.Catalog.Placa(sign.Code);
        if (placa == null) return Missing($"Placa {sign.Code} não existe no catálogo.");

        var geo = new MarkingGeometry();
        var w = sign.Width ?? placa.Largura;
        var h = sign.Height ?? placa.Altura;
        var face = FlatFace(placa, w, h, sign.Legend, ctx.Glyphs);
        var (mn, mx) = Bounds(face.Select(f => f.Shape));
        var faceCenter = (mn + mx) / 2;
        var k = ctx.Mm(d.SymbolMm) / Math.Max(0.01, mx.X - mn.X);
        var center = sign.Position + d.OffsetMm * ctx.Mm(1);
        Vec2 T(Vec2 v) => center + (v - faceCenter) * k;
        foreach (var (shape, color) in face) geo.Pieces.Add(new MarkingPiece(shape.Transform(T), color));

        var hx = (mx.X - mn.X) * k / 2;
        var hy = (mx.Y - mn.Y) * k / 2;
        var post = sign.Position;
        if (d.Leader) AddLeader(geo, ctx, post + d.AnchorMm * ctx.Mm(1), center, hx, hy, d.LeaderStyle,
                d.LeaderStyle == EstiloChamada.Livre ? d.ElbowsMm.Select(e => post + e * ctx.Mm(1)).ToList() : new List<Vec2>(), d.Terminal);
        if (d.NumberBubble && !string.IsNullOrWhiteSpace(d.Number))
        {
            // Balão com o número acima do símbolo (padrão de pranchas de sinalização).
            var r = Math.Max(ctx.Mm(Math.Max(2.2, d.TextMm * 1.4)), DetailGenerator.TextWidth(d.Number!, d.TextMm, ctx) / 2 + ctx.Mm(1.0));
            var bc = center + new Vec2(0, hy + r + ctx.Mm(1));
            geo.Annotations.Add(new AnnotationLine(CurveTools.Circle(bc, r, r * 0.02).Append(CurveTools.Circle(bc, r, r * 0.02)[0]).ToList(), MarkingColor.Preta));
            geo.Annotations.Add(new AnnotationText(bc + new Vec2(0, ctx.Mm(d.TextMm) / 2), d.Number!, d.TextMm, TextAlign.Center));
        }
        if (d.Label)
        {
            var text = (string.IsNullOrWhiteSpace(d.Number) ? "" : d.Number + " – ") + placa.Codigo + (d.ShowName ? "\n" + placa.Nome : "");
            if (!string.IsNullOrWhiteSpace(sign.Legend) && sign.Legend != "-" && placa.Codigo.StartsWith("R-19")) text += $" – {sign.Legend} km/h";
            // Texto ao lado do símbolo, do lado oposto ao suporte, para não cruzar a chamada.
            var side = d.OffsetMm.X < -1e-6 ? -1.0 : 1.0;
            var lines = text.Split('\n').Length;
            var top = center.Y + ctx.Mm(lines * d.TextMm * 1.6) / 2;
            geo.Annotations.Add(new AnnotationText(new Vec2(center.X + side * (hx + ctx.Mm(1.5)), top), text, d.TextMm,
                side > 0 ? TextAlign.Left : TextAlign.Right));
        }
        return geo;
    }

    /// <summary>
    /// Linha de chamada do suporte (<paramref name="tip"/>) até a borda do símbolo, reta, com cotovelo horizontal
    /// ou passando pelos vértices dados, com terminal (ponto ou seta) na ponta.
    /// </summary>
    public static void AddLeader(MarkingGeometry geo, BuildContext ctx, Vec2 tip, Vec2 center, double hx, double hy, EstiloChamada style,
        IReadOnlyList<Vec2> elbows, TerminalChamada terminal)
    {
        if (tip.DistanceTo(center) <= Math.Max(hx, hy) * 1.05 && elbows.Count == 0) return;
        Vec2 Edge(Vec2 from)
        {
            var v = from - center;
            if (v.Length < 1e-9) return center;
            var t = Math.Min(Math.Abs(v.X) < 1e-9 ? double.MaxValue : hx / Math.Abs(v.X), Math.Abs(v.Y) < 1e-9 ? double.MaxValue : hy / Math.Abs(v.Y));
            return center + v * Math.Min(1, t);
        }
        var pts = new List<Vec2> { tip };
        switch (style)
        {
            case EstiloChamada.Cotovelo:
            {
                var side = tip.X >= center.X ? 1.0 : -1.0;
                var landing = center + new Vec2(side * hx, 0);
                var elbow = landing + new Vec2(side * ctx.Mm(4), 0);
                // Suporte muito próximo em X: cotovelo vertical (sobe/desce e entra pela lateral).
                if (Math.Abs(tip.X - center.X) < hx + ctx.Mm(4)) elbow = new Vec2(elbow.X, tip.Y);
                pts.Add(elbow);
                pts.Add(landing);
                break;
            }
            case EstiloChamada.Livre when elbows.Count > 0:
                pts.AddRange(elbows);
                pts.Add(Edge(elbows[^1]));
                break;
            default:
                pts.Add(Edge(tip));
                break;
        }
        var dotR = ctx.Mm(0.6);
        var dir = (pts[1] - pts[0]).Normalized();
        switch (terminal)
        {
            case TerminalChamada.Ponto:
                geo.Pieces.Add(new MarkingPiece(new Polygon2(CurveTools.Circle(tip, dotR, dotR * 0.05)), MarkingColor.Vermelha));
                pts[0] = tip + dir * dotR;
                break;
            case TerminalChamada.Seta:
            {
                var len = ctx.Mm(2.2);
                var half = ctx.Mm(0.7);
                var n = dir.PerpLeft;
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { tip, tip + dir * len + n * half, tip + dir * len - n * half }), MarkingColor.Vermelha));
                pts[0] = tip + dir * len * 0.9;
                break;
            }
        }
        geo.Annotations.Add(new AnnotationLine(pts, MarkingColor.Vermelha));
    }

    // ------------------------------------------------------------------ anotações

    /// <summary>Texto automático de identificação de uma marca.</summary>
    public static string AutoText(MarkingDefinition target, Catalogo cat, bool showName = true, bool showDetails = true)
    {
        var info = MarkingBuilder.Describe(target, cat);
        var head = showName ? $"{info.Code} – {info.Name}" : info.Code;
        if (!showDetails) return head;
        string? detail = target switch
        {
            LinearMarkingDefinition l when cat.Linear(l.Code) is { } t && MarkingBuilder.ResolveVariant(t, l.Variant, l.Speed) is { } v =>
                l.WidthOverride is > 0 ? $"{v.Nome} – largura {F(l.WidthOverride.Value)} m" : v.Nome,
            HatchMarkingDefinition h when cat.Hachura(h.Code) is { } t =>
                $"barras {F(h.BarWidth ?? t.LarguraBarra)} m a cada {F((h.BarWidth ?? t.LarguraBarra) + (h.Gap ?? t.Espacamento))} m – {F(h.AngleDeg ?? t.Angulo, "0")}°",
            SymbolMarkingDefinition s => $"L = {F(s.Length)} m",
            TextMarkingDefinition tx => $"letras de {F(tx.Height)} m",
            ParkingMarkingDefinition p when cat.Vaga(p.Code) is { } t =>
                $"{F(p.StallWidth ?? t.Largura)} × {F(p.StallLength ?? t.Comprimento)} m – {F(p.Angle ?? t.Angulo, "0")}°",
            DeviceMarkingDefinition dv when cat.Dispositivo(dv.Code) is { } t =>
                (dv.Spacing ?? t.Espacamento) <= 0 ? "contínuo" : $"a cada {F(dv.Spacing ?? t.Espacamento)} m",
            SignDefinition sg when cat.Placa(sg.Code) is { } t =>
                $"{F(sg.Width ?? t.Largura)} m – altura livre {F(sg.MountHeight)} m",
            RampDefinition r => $"largura {F(r.Width)} m – i = {F(r.Slope * 100, "0.##")} %",
            TrafficCalmingDefinition tc =>
                $"{F(tc.Length ?? TrafficCalmingGenerator.Defaults(tc.Type).Length)} × {F(tc.Height ?? TrafficCalmingGenerator.Defaults(tc.Type).Height)} m",
            _ => null,
        };
        return detail == null ? head : head + "\n" + detail;
    }

    public static MarkingGeometry Label(LabelDefinition d, BuildContext ctx)
    {
        if (ctx.Lookup?.Invoke(d.MarkingTargetId) is not { } target) return Missing(MissingTarget + " (marca excluída).");
        var geo = new MarkingGeometry();
        var side = d.LabelPosition.X >= d.Anchor.X ? 1.0 : -1.0;
        var shoulder = d.LabelPosition - new Vec2(side * ctx.Mm(3), 0);
        var dotR = ctx.Mm(0.5);
        geo.Pieces.Add(new MarkingPiece(new Polygon2(CurveTools.Circle(d.Anchor, dotR, dotR * 0.05)), MarkingColor.Vermelha));
        geo.Annotations.Add(new AnnotationLine(new[] { d.Anchor, shoulder, d.LabelPosition }, MarkingColor.Vermelha));
        var text = string.IsNullOrWhiteSpace(d.CustomText) ? AutoText(target, ctx.Catalog, d.ShowName, d.ShowDetails) : d.CustomText!;
        geo.Annotations.Add(new AnnotationText(d.LabelPosition + new Vec2(side * ctx.Mm(1), ctx.Mm(d.TextMm * 0.7)), text, d.TextMm,
            side > 0 ? TextAlign.Left : TextAlign.Right));
        return geo;
    }

    // ------------------------------------------------------------------ quadro de legenda

    private static string RowKey(MarkingDefinition d) => d switch
    {
        RepeatedMarkingDefinition r => string.IsNullOrWhiteSpace(r.SymbolCode) ? "LEG:" + r.Text : r.SymbolCode!,
        TextMarkingDefinition t => "LEG:" + t.Text,
        _ => d.DisplayCode,
    };

    /// <summary>O quadro de legenda lista apenas a sinalização vertical (placas) do projeto.</summary>
    private static bool Include(MarkingDefinition d, LegendDefinition lg) => d is SignDefinition;

    /// <summary>Regulamentação, advertência, indicação e demais placas, nessa ordem.</summary>
    private static int SignOrder(string code) => code.Length == 0 ? 9 : char.ToUpperInvariant(code[0]) switch
    {
        'R' => 0, 'A' => 1, 'I' => 2, 'S' => 3, 'E' => 4, 'T' => 5, 'O' => 6, _ => 7,
    };

    public static MarkingGeometry Legend(LegendDefinition lg, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var all = ctx.AllDefinitions?.Invoke() ?? Array.Empty<MarkingDefinition>();
        var rows = all.Where(d => Include(d, lg))
            .GroupBy(RowKey).Select(g => g.First())
            .Select(d => (Def: d, Info: MarkingBuilder.Describe(d, ctx.Catalog)))
            .OrderBy(r => SignOrder(r.Info.Code)).ThenBy(r => r.Info.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var x0 = lg.Position.X;
        var y0 = lg.Position.Y;
        var rowH = ctx.Mm(lg.RowMm);
        var sampleW = ctx.Mm(lg.SampleMm);
        var textW = ctx.Mm(lg.TextColumnMm);
        var totalW = sampleW + textW;
        var titleH = ctx.Mm(Math.Max(lg.RowMm * 0.8, lg.TextMm * 3));
        var totalH = titleH + Math.Max(1, rows.Count) * rowH;

        Line(x0, y0, x0 + totalW, y0);
        Line(x0 + totalW, y0, x0 + totalW, y0 - totalH);
        Line(x0 + totalW, y0 - totalH, x0, y0 - totalH);
        Line(x0, y0 - totalH, x0, y0);
        Line(x0, y0 - titleH, x0 + totalW, y0 - titleH);
        if (rows.Count > 0) Line(x0 + sampleW, y0 - titleH, x0 + sampleW, y0 - totalH);
        geo.Annotations.Add(new AnnotationText(new Vec2(x0 + totalW / 2, y0 - (titleH - ctx.Mm(lg.TextMm * 1.2)) / 2), lg.Title, lg.TextMm * 1.2));
        if (rows.Count == 0)
            geo.Annotations.Add(new AnnotationText(new Vec2(x0 + totalW / 2, y0 - titleH - rowH * 0.3), "Nenhuma placa no projeto.", lg.TextMm));

        var pad = ctx.Mm(1.5);
        for (int i = 0; i < rows.Count; i++)
        {
            var top = y0 - titleH - i * rowH;
            if (i > 0) Line(x0, top, x0 + totalW, top);
            var boxMin = new Vec2(x0 + pad, top - rowH + pad);
            var boxMax = new Vec2(x0 + sampleW - pad, top - pad);
            try
            {
                var sample = Sample(rows[i].Def, ctx, (boxMax.X - boxMin.X), ctx.Mm(0.5));
                Fit(sample, boxMin, boxMax, geo, paintBackground: IsPaintSample(rows[i].Def));
            }
            catch
            {
                // Amostra opcional: a descrição continua no quadro.
            }
            var label = rows[i].Def switch
            {
                RepeatedMarkingDefinition { SymbolCode: null or "" } r => $"LEG – Legenda \"{r.Text}\"",
                _ => $"{rows[i].Info.Code} – {rows[i].Info.Name}",
            };
            geo.Annotations.Add(new AnnotationText(new Vec2(x0 + sampleW + ctx.Mm(2), top - (rowH - ctx.Mm(lg.TextMm)) / 2), label, lg.TextMm, TextAlign.Left));
        }
        geo.UnitCount = rows.Count;
        return geo;

        void Line(double ax, double ay, double bx, double by) =>
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(ax, ay), new Vec2(bx, by) }, MarkingColor.Preta));
    }

    private static bool IsPaintSample(MarkingDefinition d) =>
        d is LinearMarkingDefinition or HatchMarkingDefinition or SymbolMarkingDefinition or TextMarkingDefinition
            or ParkingMarkingDefinition or RepeatedMarkingDefinition;

    /// <summary>Geometria representativa de uma marca (coordenadas locais) para o quadro de legenda.</summary>
    public static MarkingGeometry Sample(MarkingDefinition def, BuildContext ctx, double boxWidthModel, double minStrokeModel)
    {
        var d = MarkingDefinition.FromJson(def.ToJson())!;
        d.Exclusions.Clear();
        var local = new BuildContext { Catalog = ctx.Catalog, Glyphs = ctx.Glyphs, ViewScale = ctx.ViewScale };
        Polyline2 Straight(double len) => new(new[] { Vec2.Zero, new Vec2(len, 0) });

        switch (d)
        {
            case SignDefinition sg when ctx.Catalog.Placa(sg.Code) is { } p:
            {
                var g = new MarkingGeometry();
                foreach (var (s, c) in FlatFace(p, sg.Width ?? p.Largura, sg.Height ?? p.Altura, sg.Legend, ctx.Glyphs)) g.Pieces.Add(new MarkingPiece(s, c));
                return g;
            }
            case LinearMarkingDefinition l when ctx.Catalog.Linear(l.Code) is { } t:
            {
                var v = MarkingBuilder.ResolveVariant(t, l.Variant, l.Speed);
                var period = v?.Faixas.Select(f => f.Padrao.Sum()).DefaultIfEmpty(0).Max() ?? 0;
                var len = t.Transversal ? 4 : Math.Clamp(period * 1.6, 5, 20);
                var width = l.WidthOverride ?? v?.Faixas.Max(f => f.Largura) ?? 0.1;
                var scale = boxWidthModel / len;
                if (width * scale < minStrokeModel && !t.Transversal) l.WidthOverride = minStrokeModel / scale;
                l.Offset = 0; l.StartSetback = 0; l.EndSetback = 0; l.Phase = 0;
                return MarkingBuilder.Build(l, Straight(len), local);
            }
            case HatchMarkingDefinition h:
                h.StripWidth = null;
                h.ReferenceDirection = Vec2.UnitX;
                h.AxisPoint = null;
                return MarkingBuilder.Build(h, new Polyline2(new[] { Vec2.Zero, new Vec2(8, 0), new Vec2(8, 3), new Vec2(0, 3) }, true), local);
            case SymbolMarkingDefinition s:
                s.Position = Vec2.Zero; s.Direction = Vec2.UnitX;
                return MarkingBuilder.Build(s, null, local);
            case TextMarkingDefinition tx:
                tx.Position = Vec2.Zero; tx.Direction = Vec2.UnitY;
                return MarkingBuilder.Build(tx, null, local);
            case RepeatedMarkingDefinition r when !string.IsNullOrWhiteSpace(r.SymbolCode):
                return MarkingBuilder.Build(new SymbolMarkingDefinition { Code = r.SymbolCode!, Length = r.Length, Direction = Vec2.UnitX, ColorOverride = r.Color }, null, local);
            case RepeatedMarkingDefinition r:
                return MarkingBuilder.Build(new TextMarkingDefinition { Text = r.Text ?? "", Height = r.TextHeight, WidthFactor = r.WidthFactor, Direction = Vec2.UnitY }, null, local);
            case ParkingMarkingDefinition pk:
                pk.Count = 2; pk.StartOffset = 0; pk.CurbOffset = 0; pk.RightSide = true;
                return MarkingBuilder.Build(pk, Straight(14), local);
            case DeviceMarkingDefinition dv:
                dv.Offset = 0; dv.StartSetback = 0; dv.EndSetback = 0;
                return MarkingBuilder.Build(dv, Straight(6), local);
            case UrbanElementDefinition ue:
                ue.UsePath = false; ue.Position = Vec2.Zero; ue.Direction = Vec2.UnitY;
                return MarkingBuilder.Build(ue, null, local);
            case RampDefinition rp:
                return MarkingBuilder.Build(rp, new Polyline2(new[] { Vec2.Zero, new Vec2(0, 1) }), local);
            case TrafficCalmingDefinition tc:
                return MarkingBuilder.Build(tc, new Polyline2(new[] { Vec2.Zero, new Vec2(0, 4) }), local);
            case CurbExtensionDefinition ce:
                return MarkingBuilder.Build(ce, Straight(12), local);
            case SidewalkAreaDefinition sa:
                return MarkingBuilder.Build(sa, new Polyline2(new[] { Vec2.Zero, new Vec2(8, 0), new Vec2(8, 5), new Vec2(0, 5) }, true), local);
            case PlanterDefinition pl:
                pl.Offset = 0;
                return MarkingBuilder.Build(pl, Straight(12), local);
            case CulDeSacDefinition cd:
                return MarkingBuilder.Build(cd, new Polyline2(new[] { Vec2.Zero, new Vec2(0, 18) }), local);
            case RoadPavementDefinition pv:
            {
                var g = new MarkingGeometry();
                g.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(Vec2.Zero, new Vec2(8, 3)), pv.Color));
                return g;
            }
            case TactileRouteDefinition tr:
                return TactileGenerator.Route(tr, new[] { new Polyline2(new[] { new Vec2(0, 0.2), new Vec2(2.2, 0.2), new Vec2(2.2, 1.4) }) }, 0);
            case RoundaboutDefinition rb:
            {
                var c = (RoundaboutDefinition)MarkingDefinition.FromJson(rb.ToJson())!;
                c.Center = Vec2.Zero;
                c.Landscaping = false;
                return RoundaboutGenerator.Build(c, local);
            }
            default:
                return new MarkingGeometry();
        }
    }

    /// <summary>Encaixa a amostra na caixa (escala uniforme, centralizada), com fundo de pavimento opcional.</summary>
    private static void Fit(MarkingGeometry sample, Vec2 boxMin, Vec2 boxMax, MarkingGeometry target, bool paintBackground)
    {
        if (sample.Pieces.Count == 0) return;
        var (mn, mx) = Bounds(sample.Pieces.Select(p => p.Shape));
        var bw = boxMax.X - boxMin.X;
        var bh = boxMax.Y - boxMin.Y;
        var s = Math.Min(bw / Math.Max(1e-6, mx.X - mn.X), bh / Math.Max(1e-6, mx.Y - mn.Y));
        var c = (mn + mx) / 2;
        var bc = (boxMin + boxMax) / 2;
        var placed = sample.Pieces.Select(p => new MarkingPiece(p.Shape.Transform(v => bc + (v - c) * s), p.Color)).ToList();
        if (paintBackground)
        {
            var box = Polygon2.Rectangle(boxMin, boxMax);
            foreach (var bg in PolygonOps.Difference(new[] { box }, placed.Select(p => p.Shape)))
                target.Pieces.Add(new MarkingPiece(bg, MarkingColor.Asfalto));
        }
        // Peças da mesma cor unidas (regiões 2D não podem se sobrepor).
        foreach (var group in placed.GroupBy(p => p.Color))
            foreach (var u in PolygonOps.Union(group.Select(g => g.Shape)))
                target.Pieces.Add(new MarkingPiece(u, group.Key));
    }

    internal static (Vec2 Min, Vec2 Max) Bounds(IEnumerable<Polygon2> polys)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in polys)
        {
            var (a, b) = p.Bounds;
            minX = Math.Min(minX, a.X); minY = Math.Min(minY, a.Y);
            maxX = Math.Max(maxX, b.X); maxY = Math.Max(maxY, b.Y);
        }
        return (new Vec2(minX, minY), new Vec2(maxX, maxY));
    }
}
