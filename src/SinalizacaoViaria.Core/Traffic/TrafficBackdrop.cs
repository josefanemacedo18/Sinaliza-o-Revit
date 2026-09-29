using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Camada de desenho do mapa do simulador (ordem de pintura).</summary>
public enum CamadaMapa { Pavimento = 0, Calcada = 1, Canteiro = 2, Pintura = 3, Fisico = 4 }

/// <summary>Uma forma real do projeto no mapa do simulador: pavimento, calçada, canteiro, pintura ou elemento físico.</summary>
public sealed record MapShape(Polygon2 Shape, CamadaMapa Layer, Rgb Color, double Top, string SourceId);

/// <summary>
/// Planta real do projeto para o mapa do Simulador de Tráfego: as mesmas peças que o plugin gera no Revit (pavimento das
/// vias, interseções e rotatórias, calçadas, meios-fios, canteiros, ilhas e toda a sinalização horizontal), com as cores
/// reais – em vez do esquema de faixas desenhado a partir do eixo.
/// </summary>
public static class TrafficBackdrop
{
    /// <summary>Limite de peças (projetos muito grandes: o mapa volta ao esquema nas partes que não couberem).</summary>
    public const int MaxShapes = 250_000;

    public static void Read(TrafficNetwork net, IReadOnlyCollection<MarkingDefinition> defs, Func<MarkingDefinition, MarkingGeometry?> geometryOf)
    {
        foreach (var d in defs.GroupBy(x => x.Id).Select(g => g.First()))
        {
            if (net.Backdrop.Count >= MaxShapes) { net.Notes.Add("Mapa: projeto muito grande – parte das peças não foi desenhada."); break; }
            // Placas, semáforos e mobiliário ficam como ícones (a peça em planta de uma placa não diz nada no mapa).
            if (d is SignDefinition) continue;
            MarkingGeometry? g;
            try { g = geometryOf(d); }
            catch { continue; }
            if (g == null) continue;
            foreach (var pc in g.Pieces)
            {
                if (pc.Shape.Outer.Count < 3 || pc.Shape.Area < 1e-4) continue;
                if (pc.Elevation > 1.2) continue;                      // placas, copas altas, cabos: fora da planta
                var layer = LayerOf(pc);
                net.Backdrop.Add(new MapShape(pc.Shape, layer, ColorOf(pc), pc.Elevation + pc.Thickness, d.Id));
            }
        }
        // Ordem de pintura: pavimento, calçadas, canteiros, pintura por cima; dentro da camada, do mais baixo e maior para o
        // menor (ilhas e refúgios por cima do pavimento da interseção).
        net.Backdrop.Sort((a, b) =>
        {
            var c = a.Layer.CompareTo(b.Layer);
            if (c != 0) return c;
            c = a.Top.CompareTo(b.Top);
            return c != 0 ? c : b.Shape.Area.CompareTo(a.Shape.Area);
        });
    }

    public static CamadaMapa LayerOf(MarkingPiece pc) => pc.Color switch
    {
        MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto => CamadaMapa.Pavimento,
        MarkingColor.Concreto when pc.Elevation + pc.Thickness > 0.35 => CamadaMapa.Fisico,   // barreiras, muretas
        MarkingColor.Concreto => CamadaMapa.Calcada,
        MarkingColor.Grama or MarkingColor.Folhagem => CamadaMapa.Canteiro,
        MarkingColor.Metal or MarkingColor.Madeira or MarkingColor.Vidro => CamadaMapa.Fisico,
        _ => CamadaMapa.Pintura,
    };

    /// <summary>Cores de planta (tons de foto aérea para os pisos; cores da tinta para a sinalização).</summary>
    public static Rgb ColorOf(MarkingPiece pc) => pc.Color switch
    {
        MarkingColor.Asfalto => new Rgb(0x44, 0x48, 0x4E),
        MarkingColor.Bloquete => new Rgb(0x8A, 0x80, 0x76),
        MarkingColor.PavimentoConcreto => new Rgb(0xA6, 0xA6, 0xA2),
        MarkingColor.Concreto => pc.Layer is { } l && l.Contains("MEIO", StringComparison.OrdinalIgnoreCase) ? new Rgb(0xE4, 0xE1, 0xDA) : new Rgb(0xCF, 0xCB, 0xC2),
        MarkingColor.Grama => new Rgb(0x6F, 0xA0, 0x55),
        MarkingColor.Folhagem => new Rgb(0x4E, 0x80, 0x3E),
        MarkingColor.Branca => new Rgb(0xF4, 0xF4, 0xF0),
        MarkingColor.Amarela => new Rgb(0xF2, 0xB8, 0x00),
        _ => MarkingColors.Display(pc.Color),
    };
}
