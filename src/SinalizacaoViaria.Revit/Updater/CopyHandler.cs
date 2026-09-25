using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Updater;

/// <summary>
/// Trata cópias de elementos de sinalização (copiar/colar, copiar, matriz, espelhar não suportado):
/// a cópia recebe um novo identificador e seu caminho é convertido em pontos fixos deslocados,
/// tornando-se uma marca independente e editável.
/// </summary>
internal static class CopyHandler
{
    public static void Handle(Document doc, ICollection<ElementId> added)
    {
        var addedSet = new HashSet<ElementId>(added);
        var addedMarkings = added.Select(doc.GetElement).Where(e => e != null)
            .Select(MarkingStorage.Read).Where(r => r != null).Cast<StoredMarking>().ToList();
        if (addedMarkings.Count == 0) return;

        var others = MarkingStorage.All(doc).Where(r => !addedSet.Contains(r.Element.Id)).ToList();
        foreach (var group in addedMarkings.GroupBy(r => r.MarkingId))
        {
            var originals = others.Where(o => o.MarkingId == group.Key).ToList();
            if (originals.Count == 0) continue; // elemento realmente novo (criado pelo plugin)

            var copy = group.First();
            var original = originals.FirstOrDefault(o => o.Color == copy.Color && o.Element.GetType() == copy.Element.GetType()) ?? originals[0];
            var (delta, dz) = Offset(doc, original.Element, copy.Element);

            var def = MarkingDefinition.FromJson(copy.Definition.ToJson())!;
            def.Id = Guid.NewGuid().ToString("N");
            def.GroupId = null;
            if (copy.Element.OwnerViewId != ElementId.InvalidElementId && doc.GetElement(copy.Element.OwnerViewId) is View v)
                def.Output.ViewId = v.UniqueId;

            if (def.Path != null)
            {
                var resolved = PathResolver.Resolve(doc, def.Path);
                var chain = resolved?.Main;
                if (chain != null)
                {
                    var pts = chain.Points.Select(p => p + delta).ToList();
                    if (chain.Closed && pts.Count > 1 && pts[0].AlmostEquals(pts[^1], 1e-6)) pts.RemoveAt(pts.Count - 1);
                    def.SetPath(PathReference.FromPoints(pts, resolved!.Z + dz, def.Path.Closed || chain.Closed));
                }
            }
            def.Translate(delta, dz);

            foreach (var r in group)
            {
                MarkingStorage.Write(r.Element, def, r.Color);
                SharedParameters.Set(r.Element, SharedParameters.Id, def.Id);
            }
        }
    }

    private static (Vec2 Delta, double Dz) Offset(Document doc, Element original, Element copy)
    {
        var bo = Box(doc, original);
        var bc = Box(doc, copy);
        if (bo == null || bc == null) return (Vec2.Zero, 0);
        var co = (bo.Min + bo.Max) / 2;
        var cc = (bc.Min + bc.Max) / 2;
        var d = cc - co;
        return (new Vec2(UnitConv.M(d.X), UnitConv.M(d.Y)), UnitConv.M(d.Z));
    }

    private static BoundingBoxXYZ? Box(Document doc, Element e)
    {
        if (e.OwnerViewId != ElementId.InvalidElementId && doc.GetElement(e.OwnerViewId) is View v) return e.get_BoundingBox(v);
        return e.get_BoundingBox(null);
    }
}
