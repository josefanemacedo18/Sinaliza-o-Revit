using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Traffic;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Simulador de Tráfego: lê as vias, interseções, rotatórias, balões, placas, faixas de pedestres, vagas e moderação do
/// projeto, monta a rede, estima a demanda, calcula capacidade e nível de serviço (HCM), roda a microssimulação e
/// diagnostica problemas de capacidade, segurança, sinalização, legislação e acessibilidade.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdSimuladorTrafego : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var net = BuildNetwork(doc);
        if (net.Roads.Count == 0)
            throw new UserMessageException("Nenhuma via do SinalizaBIM foi encontrada no projeto.\n\nO Simulador de Tráfego lê as vias criadas com Nova Via / Pista " +
                                           "(pavimento com a seção), as interseções, rotatórias e balões. Crie as vias e rode o simulador de novo.");
        var w = new TrafficWindow(net, doc.Title, (res, sim) => TrafficDrawer.Draw(doc, uidoc.ActiveView, res));
        if (UiHelpers.ShowModal(w) == true && w.SelectIds.Count > 0)
        {
            var all = MarkingStorage.All(doc);
            var ids = all.Where(s => w.SelectIds.Contains(s.MarkingId)).Select(s => s.Element.Id).Distinct().ToList();
            if (ids.Count > 0)
            {
                uidoc.Selection.SetElementIds(ids);
                try { uidoc.ShowElements(ids); } catch { /* vista sem os elementos */ }
            }
            else TaskDialog.Show(AppTitle, "Os elementos deste item não foram encontrados na vista/projeto.");
        }
        return Result.Succeeded;
    }

    /// <summary>Rede viária a partir das definições guardadas no projeto.</summary>
    internal static TrafficNetwork BuildNetwork(Document doc)
    {
        var defs = MarkingStorage.Definitions(doc);
        var cache = new Dictionary<string, (Polyline2, double)?>();
        (Polyline2 Axis, double Z)? AxisOf(MarkingDefinition d)
        {
            if (cache.TryGetValue(d.Id, out var c)) return c;
            (Polyline2, double)? r = null;
            try
            {
                var rp = PathResolver.Resolve(doc, d.Path);
                if (rp?.Main is { } m && m.Length > 0.5) r = (m, rp.Z);
            }
            catch (Exception ex)
            {
                Log.Error("Simulador de Tráfego – caminho", ex);
            }
            cache[d.Id] = r;
            return r;
        }
        return TrafficNetworkBuilder.Build(defs, AxisOf);
    }
}

/// <summary>Desenha o mapa de níveis de serviço na vista em planta (regiões preenchidas coloridas e textos).</summary>
internal static class TrafficDrawer
{
    private const string Prefix = "SV - Tráfego ";

    public static string Draw(Document doc, View view, TrafficResult res)
    {
        if (view is not ViewPlan && view.ViewType != ViewType.DraftingView)
            throw new UserMessageException("Abra uma vista em planta (planta de piso ou de implantação) para desenhar o mapa de níveis de serviço.");
        var net = res.Network;
        var z = view is ViewPlan vp && vp.GenLevel != null ? vp.GenLevel.ProjectElevation : view.SketchPlane?.GetPlane().Origin.Z ?? 0;
        var scale = Math.Max(1, view.Scale);
        var count = 0;
        using var t = new Transaction(doc, "Simulador de Tráfego – mapa de níveis de serviço");
        t.Start();
        // Remove o mapa anterior desta vista.
        var old = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType()
            .Where(e => (e is FilledRegion || e is TextNote) && doc.GetElement(e.GetTypeId())?.Name.StartsWith(Prefix) == true)
            .Select(e => e.Id).ToList();
        if (old.Count > 0) doc.Delete(old);

        var styles = new StyleService(doc);
        var text = TextType(doc, styles, 2.2);
        var textBig = TextType(doc, styles, 3.5);
        ElementId? invisible = styles.InvisibleLineStyle();

        // Trechos: faixa colorida sobre as faixas de cada sentido, pelo nível do trecho/aproximação.
        foreach (var lr in res.Links.Values)
        {
            var l = lr.Link;
            if (l.Length < 2) continue;
            var nr = res.Nodes.GetValueOrDefault(l.To);
            var ap = nr?.Approaches.FirstOrDefault(a => a.Link == l.Index);
            var los = lr.Volume < 1 ? "-" : ap != null && ap.Volume >= 1 && string.CompareOrdinal(ap.LOS, lr.LOS) > 0 ? ap.LOS : lr.LOS;
            var inner = l.Road.TwoWay ? (l.Road.Median ? 1.0 : 0.0) + 0.3 : -l.Lanes * l.Road.LaneWidth / 2 + 0.3;
            var outer = l.Road.TwoWay ? inner + l.Lanes * l.Road.LaneWidth - 0.6 : l.Lanes * l.Road.LaneWidth / 2 - 0.3;
            var poly = Band(l.Path, inner, outer, 1.0);
            if (poly == null) continue;
            if (Region(doc, view, RegionType(doc, styles, los), poly, z, invisible)) count++;
        }
        // Nós: círculo com a cor do nível e texto com o atraso.
        foreach (var nr in res.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind != TipoNo.Continuacao))
        {
            var nd = nr.Node;
            var r = Math.Max(2.5, scale * 0.0035);
            var ring = Enumerable.Range(0, 32).Select(i => nd.Pos + new Vec2(Math.Cos(i * Math.PI / 16), Math.Sin(i * Math.PI / 16)) * r).ToList();
            Region(doc, view, RegionType(doc, styles, nr.LOS), ring, z, invisible);
            var label = $"{nd.Label} – nível {nr.LOS}" + (nr.Volume >= 1 ? $"\r{nr.Delay:0} s/veh · {nr.Volume:0} veh/h" : "") + (nr.Cycle > 0 ? $"\rciclo {nr.Cycle:0} s" : "");
            Note(doc, view, text, nd.Pos + new Vec2(r * 1.4, r * 1.2), z, label, HorizontalTextAlignment.Left);
        }
        // Título e legenda no canto inferior esquerdo da rede.
        var (min, max) = net.Bounds();
        var u = scale * 0.001;                    // 1 mm de papel em metros
        var at = new Vec2(min.X, min.Y - 14 * u);
        Note(doc, view, textBig, at, z, $"SIMULADOR DE TRÁFEGO – níveis de serviço (HCM) – demanda {res.Options.DemandLabel} – {DateTime.Now:dd/MM/yyyy}", HorizontalTextAlignment.Left);
        var legend = new[] { "A", "B", "C", "D", "E", "F" };
        for (int i = 0; i < legend.Length; i++)
        {
            var p = at + new Vec2(i * 34 * u, -9 * u);
            var sq = new List<Vec2> { p, p + new Vec2(6 * u, 0), p + new Vec2(6 * u, -4 * u), p + new Vec2(0, -4 * u) };
            Region(doc, view, RegionType(doc, styles, legend[i]), sq, z, invisible);
            Note(doc, view, text, p + new Vec2(7.5 * u, 0.5 * u), z, $"{legend[i]} – {TrafficReport.LosMeaning(legend[i])}", HorizontalTextAlignment.Left);
        }
        t.Commit();
        return $"Mapa desenhado na vista \"{view.Name}\": {count} trecho(s) coloridos, {res.Nodes.Values.Count(n => !n.Node.IsZone && n.Node.Kind != TipoNo.Continuacao)} nó(s) com o nível de serviço. " +
               "Um novo desenho substitui o anterior nesta vista.";
    }

    /// <summary>Faixa entre dois afastamentos (positivos à direita do sentido), recuada nas pontas.</summary>
    private static List<Vec2>? Band(Polyline2 path, double off0, double off1, double trim)
    {
        var L = path.Length;
        if (L < 2 * trim + 1) return null;
        var n = Math.Max(2, (int)((L - 2 * trim) / 3) + 1);
        var a = new List<Vec2>();
        var b = new List<Vec2>();
        for (int i = 0; i <= n; i++)
        {
            var s = trim + (L - 2 * trim) * i / n;
            var p = path.PointAt(s);
            var d = path.TangentAt(s);
            var right = new Vec2(d.Y, -d.X);
            a.Add(p + right * off0);
            b.Add(p + right * off1);
        }
        b.Reverse();
        a.AddRange(b);
        return a;
    }

    private static bool Region(Document doc, View view, ElementId type, List<Vec2> ring, double z, ElementId? lineStyle)
    {
        try
        {
            var tol = doc.Application.ShortCurveTolerance * 1.5;
            var pts = new List<XYZ>();
            foreach (var v in ring)
            {
                var p = new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), z);
                if (pts.Count == 0 || pts[^1].DistanceTo(p) > tol) pts.Add(p);
            }
            while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= tol) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) return false;
            var loop = new CurveLoop();
            for (int i = 0; i < pts.Count; i++) loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
            var fr = FilledRegion.Create(doc, type, view.Id, new List<CurveLoop> { loop });
            if (lineStyle != null) try { fr.SetLineStyleId(lineStyle); } catch { /* opcional */ }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Simulador de Tráfego – região", ex);
            return false;
        }
    }

    private static void Note(Document doc, View view, ElementId type, Vec2 at, double z, string s, HorizontalTextAlignment align)
    {
        try
        {
            var opt = new TextNoteOptions(type) { HorizontalAlignment = align, VerticalAlignment = VerticalTextAlignment.Top };
            TextNote.Create(doc, view.Id, new XYZ(UnitConv.Ft(at.X), UnitConv.Ft(at.Y), z), s, opt);
        }
        catch (Exception ex)
        {
            Log.Error("Simulador de Tráfego – texto", ex);
        }
    }

    private static readonly Dictionary<string, (byte R, byte G, byte B)> Colors = new()
    {
        ["A"] = (0x2E, 0x7D, 0x32), ["B"] = (0x7C, 0xB3, 0x42), ["C"] = (0xF2, 0xC2, 0x1B),
        ["D"] = (0xFB, 0x8C, 0x00), ["E"] = (0xE5, 0x39, 0x35), ["F"] = (0x8E, 0x10, 0x10), ["-"] = (0x9E, 0xA7, 0xB3),
    };

    private static ElementId RegionType(Document doc, StyleService styles, string los)
    {
        if (!Colors.ContainsKey(los)) los = "-";
        var name = $"{Prefix}nível {(los == "-" ? "sem tráfego" : los)}";
        var types = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        var frt = types.FirstOrDefault(x => x.Name == name);
        if (frt != null) return frt.Id;
        var baseType = types.FirstOrDefault() ?? throw new UserMessageException("O projeto não possui nenhum tipo de região preenchida para duplicar.");
        frt = (FilledRegionType)baseType.Duplicate(name);
        var solid = styles.SolidFillPattern();
        var (r, g, b) = Colors[los];
        if (solid != ElementId.InvalidElementId)
        {
            frt.ForegroundPatternId = solid;
            frt.ForegroundPatternColor = new Color(r, g, b);
            try { frt.BackgroundPatternId = ElementId.InvalidElementId; } catch { /* algumas versões exigem padrão válido */ }
        }
        frt.IsMasking = false;
        return frt.Id;
    }

    private static ElementId TextType(Document doc, StyleService styles, double mm)
    {
        var name = $"{Prefix}texto {mm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} mm";
        var tt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault(x => x.Name == name);
        if (tt != null) return tt.Id;
        var src = (TextNoteType)doc.GetElement(styles.TextType(mm));
        tt = (TextNoteType)src.Duplicate(name);
        try { tt.get_Parameter(BuiltInParameter.TEXT_BACKGROUND)?.Set(0); } catch { /* 0 = opaco */ }
        return tt.Id;
    }
}
