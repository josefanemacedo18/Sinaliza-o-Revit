using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Corpo d'água: o leito vai para a Terraplenagem (faixa no fundo do rio com os taludes das margens; plataforma no fundo do
/// lago) e a superfície de água fica no modelo, na cota do nível d'água.
/// </summary>
public static class WaterGenerator
{
    public static MarkingGeometry Build(CorpoDaguaDefinition d, Polyline2? path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        if (path == null || path.Points.Count < 2 || !d.EhRio && path.Points.Count < 3)
        {
            geo.Warnings.Add($"{d.KindName}: {(d.EhRio ? "eixo" : "contorno")} não encontrado.");
            return geo;
        }
        var m = Math.Max(0.1, d.TaludeMargem);
        if (d.EhRio)
        {
            var L = path.Length;
            var terreno = d.Fundo.Count == 0 ? Infra.GroundProfile(d, path, ctx, step: 2) : null;
            if (d.Fundo.Count == 0 && ctx.Ground == null && d.GroundLine is not { Count: >= 2 })
                geo.Warnings.Add($"{d.KindName}: sem terreno para o fundo padrão (terreno − {d.Profundidade:0.00} m) – informe o fundo por estaca.");
            var fundo = CorposDagua.Fundo(d, L, terreno);
            var b = Math.Max(0.1, d.LarguraFundo);
            // Leito: faixa no fundo, com as margens em talude até o terreno; pontas em talude (cone).
            var c = Infra.Corridor(path, fundo, -b / 2, b / 2, 0, L, 0, m, m, step: 1, label: d.KindName);
            c.SpillStart = c.SpillEnd = true;
            geo.Corridors.Add(c);
            var wa = CorposDagua.LarguraAgua(d);
            SolidSweep.Along(geo, path, s =>
            {
                var z = fundo.Z(s) + d.Lamina;
                return SolidSweep.Rect(-wa / 2, wa / 2, z - 0.03, z);
            }, MarkingColor.Agua, 0, L, 2, "AGUA");
            if (d.Lamina <= 0.01) geo.Warnings.Add($"{d.KindName}: lâmina d'água nula – só o leito.");
            return geo;
        }
        var margem = CorposDagua.Margem(path);
        if (d.NivelAgua <= d.CotaFundo) geo.Warnings.Add($"{d.KindName}: o nível d'água ({d.NivelAgua:0.00}) não está acima do fundo ({d.CotaFundo:0.00}).");
        var fundoLago = CorposDagua.FundoDoLago(margem, d.CotaFundo, d.NivelAgua, m);
        if (fundoLago == null)
        {
            geo.Warnings.Add($"{d.KindName}: com o talude {m:0.0#} : 1 e a profundidade {d.NivelAgua - d.CotaFundo:0.00} m o fundo não cabe no contorno – diminua o talude ou a profundidade.");
            return geo;
        }
        geo.Pads.Add(new GradePad(fundoLago, d.CotaFundo) { CutSlope = m, FillSlope = m, Daylight = true, Label = d.KindName });
        geo.Pieces.Add(new MarkingPiece(margem, MarkingColor.Agua) { Elevation = d.NivelAgua - 0.03, Thickness = 0.03, Layer = "AGUA" });
        return geo;
    }
}
