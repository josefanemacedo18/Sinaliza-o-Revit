using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Quantities;

/// <summary>Tipo de miniatura de um item do quantitativo.</summary>
public enum TipoMiniatura
{
    /// <summary>Planta (pintura: linhas, zebrados, setas, legendas, vagas).</summary>
    Planta,
    /// <summary>Perspectiva (elementos com volume: dispositivos, mobiliário, rampas, quebra-molas, meio-fio).</summary>
    Perspectiva,
    /// <summary>Amostra de material (pavimento, calçada, grama) – quadrado da cor/material.</summary>
    Material,
    /// <summary>Face da placa (sinalização vertical) – desenhada à parte.</summary>
    Placa,
}

/// <summary>
/// Amostra de "catálogo" de cada item do quantitativo: a própria sinalização isolada (um trecho da linha, uma unidade
/// do dispositivo, o símbolo, a legenda, a face da placa), e não o trecho do projeto onde ela está.
/// </summary>
public static class QuantitySamples
{
    /// <summary>Último erro ao montar uma amostra (diagnóstico).</summary>
    public static string? LastError { get; private set; }

    public static (MarkingGeometry? Geo, TipoMiniatura Kind) Sample(MarkingDefinition def, MarkingColor color, BuildContext ctx)
    {
        try
        {
            switch (def)
            {
                case SignDefinition:
                    return (null, TipoMiniatura.Placa);
                case RailwayDefinition rw:
                {
                    var c = (RailwayDefinition)MarkingDefinition.FromJson(rw.ToJson())!;
                    c.Tracks = 1; c.Offset = 0; c.Ditches = false;
                    return (MarkingBuilder.Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(3.0, 0) }), ctx), TipoMiniatura.Perspectiva);
                }
                case RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition
                    or SidewalkAreaDefinition or CurbExtensionDefinition or PlanterDefinition:
                    return (Swatch(color), TipoMiniatura.Material);
                case LinearMarkingDefinition l:
                {
                    var t = ctx.Catalog.Linear(l.Code);
                    var physical = Automation.RoadSectionInference.IsPhysical(l.Code) || l.Code == "SARJETAO" || (t?.Unidades ?? false);
                    if (Automation.RoadSectionInference.IsPhysical(l.Code) && !l.Code.StartsWith("MEIO-FIO")) return (Swatch(color), TipoMiniatura.Material);
                    var c = new LinearMarkingDefinition
                    {
                        Code = l.Code, Variant = l.Variant, Speed = l.Speed, WidthOverride = l.WidthOverride, PatternOverride = l.PatternOverride,
                        ColorOverride = l.ColorOverride, InvertSides = l.InvertSides, LrvFromKmh = l.LrvFromKmh, LrvToKmh = l.LrvToKmh, Depth = l.Depth,
                    };
                    var variant = t == null ? null : MarkingBuilder.ResolveVariant(t, c.Variant, c.Speed);
                    var period = variant?.Faixas.Where(f => f.Padrao is { Length: > 0 }).Select(f => f.Padrao!.Sum()).DefaultIfEmpty(0).Max() ?? 0;
                    var transverse = t?.Transversal ?? false;
                    var len = transverse ? 3.5 : Math.Clamp(period > 0 ? period * 2.2 : 8, 3, 24);
                    if (physical) len = t?.Unidades == true ? Math.Clamp(period * 1.1, 0.6, 4) : 2.0;
                    var path = transverse ? new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, len) }) : new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) });
                    return (MarkingBuilder.Build(c, path, ctx), physical ? TipoMiniatura.Perspectiva : TipoMiniatura.Planta);
                }
                case HatchMarkingDefinition h:
                {
                    var c = (HatchMarkingDefinition)MarkingDefinition.FromJson(h.ToJson())!;
                    c.Exclusions.Clear();
                    c.StripWidth = 0;
                    c.ReferenceDirection = null;
                    c.AxisPoint = null;
                    var rect = Polygon2.Rectangle(new Vec2(0, 0), new Vec2(5, 2.5)).Outer;
                    c.Boundary = PathReference.FromPoints(rect, 0, true);
                    return (MarkingBuilder.Build(c, new Polyline2(rect, true), ctx), TipoMiniatura.Planta);
                }
                case SymbolMarkingDefinition or TextMarkingDefinition:
                {
                    var c = MarkingDefinition.FromJson(def.ToJson())!;
                    c.Exclusions.Clear();
                    if (c is SymbolMarkingDefinition sy) { sy.Position = Vec2.Zero; sy.Direction = Vec2.UnitY; }
                    if (c is TextMarkingDefinition tx) { tx.Position = Vec2.Zero; tx.Direction = Vec2.UnitY; }
                    return (MarkingBuilder.Build(c, null, ctx), TipoMiniatura.Planta);
                }
                case DeviceMarkingDefinition dv:
                {
                    var c = (DeviceMarkingDefinition)MarkingDefinition.FromJson(dv.ToJson())!;
                    c.Exclusions.Clear();
                    c.Offset = 0;
                    c.StartSetback = 0;
                    c.EndSetback = 0;
                    var dev = ctx.Catalog.Dispositivo(dv.Code);
                    var sp = c.Spacing ?? dev?.Espacamento ?? 0;
                    var len = sp > 0 ? Math.Clamp(sp * 1.2, 0.8, 6) : 3.0;
                    return (MarkingBuilder.Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) }), ctx), TipoMiniatura.Perspectiva);
                }
                case UrbanElementDefinition u:
                {
                    var c = (UrbanElementDefinition)MarkingDefinition.FromJson(u.ToJson())!;
                    c.Exclusions.Clear();
                    c.UsePath = false;
                    c.Position = Vec2.Zero;
                    c.Direction = Vec2.UnitY;
                    return (MarkingBuilder.Build(c, null, ctx), TipoMiniatura.Perspectiva);
                }
                case TrafficCalmingDefinition tc:
                {
                    var c = (TrafficCalmingDefinition)MarkingDefinition.FromJson(tc.ToJson())!;
                    c.Exclusions.Clear();
                    return (MarkingBuilder.Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 7) }), ctx), TipoMiniatura.Perspectiva);
                }
                case ParkingMarkingDefinition pk:
                {
                    var c = (ParkingMarkingDefinition)MarkingDefinition.FromJson(pk.ToJson())!;
                    c.Exclusions.Clear();
                    return (MarkingBuilder.Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(12, 0) }), ctx), TipoMiniatura.Planta);
                }
                case RepeatedMarkingDefinition rp:
                {
                    var c = (RepeatedMarkingDefinition)MarkingDefinition.FromJson(rp.ToJson())!;
                    c.Exclusions.Clear();
                    return (MarkingBuilder.Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(10, 0) }), ctx), TipoMiniatura.Planta);
                }
                default:
                    return (null, TipoMiniatura.Perspectiva);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            return (null, TipoMiniatura.Planta);
        }
    }

    /// <summary>Quadrado de material (pavimento, concreto, grama...).</summary>
    public static MarkingGeometry Swatch(MarkingColor color)
    {
        var g = new MarkingGeometry();
        g.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(1.6, 1.0)), color));
        return g;
    }
}
