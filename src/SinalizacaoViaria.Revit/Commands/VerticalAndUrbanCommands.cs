using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Sinalização vertical: placas com suporte.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdPlacas : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new SignWindow();
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        var template = w.Result;
        EnsureDetailView(uidoc, template.Output);
        var results = Placement.Loop(uidoc, $"Placa {template.Code}", w.FixedAngle, w.AngleDeg, (pos, dir, z) =>
        {
            var d = (SignDefinition)template.CloneWithNewId();
            d.Position = pos;
            d.Direction = dir;
            d.Z = z;
            return d;
        });
        if (results.Count == 0) return Result.Cancelled;
        Report("Sinalização vertical", results);
        return Result.Succeeded;
    }
}

/// <summary>Mobiliário e elementos urbanísticos viários.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdMobiliario : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new UrbanWindow();
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        var template = w.Result;
        EnsureDetailView(uidoc, template.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) template.Output.SurfaceIds = s;
        if (w.PathMode is { } mode) return MarkingCreator.CreateAlongPath(uidoc, template, mode, template.Code);

        var results = Placement.Loop(uidoc, template.Code, false, 0, (pos, dir, z) =>
        {
            var d = (UrbanElementDefinition)template.CloneWithNewId();
            d.UsePath = false;
            d.Position = pos;
            d.Direction = dir;
            d.Z = z;
            return d;
        });
        if (results.Count == 0) return Result.Cancelled;
        Report("Elementos urbanos", results);
        return Result.Succeeded;
    }
}

/// <summary>Rampas e rebaixamentos de calçada (NBR 9050) com recorte automático da calçada.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdRampa : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new RampWindow();
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        var template = w.Result;
        EnsureDetailView(uidoc, template.Output);
        var all = new List<RenderResult>();
        while (true)
        {
            var a = Picking.PickPoint(uidoc, "Rampa: clique o centro da rampa na face do meio-fio (ESC encerra)");
            if (a == null) break;
            var b = Picking.PickPoint(uidoc, "Rampa: clique um ponto para dentro da calçada (sentido da subida)");
            if (b == null) break;
            var (pts, z) = Picking.ToCore(new[] { a, b });
            var d = (RampDefinition)template.CloneWithNewId();
            d.SetPath(PathReference.FromPoints(pts, z));
            all.AddRange(MarkingCreator.Commit(uidoc, new[] { d }, "SV - Rampa"));
            var cut = RampCutter.Apply(uidoc, d);
            if (cut > 0 && all.Count > 0) all[^1].Warnings.Add($"Calçada/meio-fio recortados em {cut} elemento(s).");
        }
        if (all.Count == 0) return Result.Cancelled;
        Report("Rampas", all.Where(r => r.Warnings.Any(x => !x.StartsWith("Calçada/meio-fio"))).ToList());
        return Result.Succeeded;
    }
}

/// <summary>Recorta calçadas, meios-fios e gramados sob uma rampa (zonas de exclusão associadas à rampa).</summary>
internal static class RampCutter
{
    private static readonly string[] CutCodes = { "CALCADA", "GRAMADO", "MEIO-FIO", "MEIO-FIO-12", "MEIO-FIO-ALTO", "MEIO-FIO-SARJ" };

    public static int Apply(UIDocument uidoc, RampDefinition ramp)
    {
        var doc = uidoc.Document;
        var path = PathResolver.Resolve(doc, ramp.Path)?.Main;
        var candidates = MarkingStorage.Definitions(doc).OfType<LinearMarkingDefinition>().Where(d => CutCodes.Contains(d.Code)).ToList();
        var touched = new List<MarkingDefinition>();
        var service = new MarkingService(doc, uidoc.ActiveView);
        Polygon2? footprint = ramp.CutSidewalk && path != null && path.Points.Count >= 2 ? RampGenerator.Footprint(ramp, path) : null;

        foreach (var d in candidates)
        {
            var had = d.Exclusions.RemoveAll(z => z.SourceId == ramp.Id) > 0;
            var hits = false;
            if (footprint != null)
            {
                var copy = (LinearMarkingDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                copy.Exclusions.Clear();
                var geo = service.BuildGeometry(copy, out _);
                hits = PolygonOps.Intersect(geo.Pieces.Select(p => p.Shape), new[] { footprint }).Sum(p => p.Area) > 1e-4;
                if (hits) d.Exclusions.Add(new ExclusionZone { SourceId = ramp.Id, Points = footprint.Outer.ToList() });
            }
            if (had || hits) touched.Add(d);
        }
        if (touched.Count > 0) MarkingCreator.Commit(uidoc, touched, "SV - Recortar calçada");
        return touched.Count;
    }
}

/// <summary>Quebra-molas, faixas elevadas e lombadas invertidas.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdModeracao : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new CalmingWindow();
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        EnsureDetailView(uidoc, w.Result.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) w.Result.Output.SurfaceIds = s;
        return MarkingCreator.CreateAlongPath(uidoc, w.Result, w.PathMode, w.Result.DisplayCode, afterEach: d => FootprintCutter.ApplyFor(uidoc, d));
    }
}

/// <summary>Atalho: marcação de área de conflito.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdAreaConflito : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => CmdZebrado.RunWith(uidoc, "MAC");
}

/// <summary>Atalho: guard rail / defensas.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdGuardRail : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new DeviceWindow(null, "DEF");
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        EnsureDetailView(uidoc, w.Result.Output);
        return MarkingCreator.CreateAlongPath(uidoc, w.Result, w.PathMode, w.Result.Code);
    }
}
