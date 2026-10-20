using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Model;

/// <summary>Elemento que a camada Revit cria para uma marca: um Piso ou uma Forma direta.</summary>
public sealed record PlannedElement(bool IsFloor, MarkingColor Color, IReadOnlyList<Polygon2> Shapes)
{
    /// <summary>Curvas do esboço/perfil com o contorno ajustado (retas e arcos inteiros).</summary>
    public int FittedSegments(double tolerance = BoundaryFit.DefaultTolerance) =>
        Shapes.Sum(s => BoundaryFit.FitPolygon(ElementPlan.Prepare(s), tolerance).Sum(r => r.Count));

    /// <summary>Curvas do esboço/perfil como eram geradas antes: uma reta por vértice do contorno discretizado.</summary>
    public int RawSegments => Shapes.Sum(s => ElementPlan.Prepare(s) is var p ? p.Outer.Count + p.Holes.Sum(h => h.Count) : 0);
}

/// <summary>
/// Regras de quais peças viram Piso do Revit e de como as peças se agrupam em elementos – as mesmas usadas pela camada
/// Revit (o plano aqui reproduz o caso plano, sem superfície), para medir e testar o peso do modelo sem o Revit.
/// </summary>
public static class ElementPlan
{
    /// <summary>Pavimento, calçada, meio-fio, sarjeta e grama: viram Piso do Revit.</summary>
    public static bool FloorEligible(MarkingDefinition def, MarkingPiece p) =>
        p.Solid == null && p.Profile == null && p.Thickness >= 0.005 && p.Shape.Area > 0.01
        && p.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto or MarkingColor.PavimentoTerra or MarkingColor.Concreto or MarkingColor.Grama
        && def is RoadPavementDefinition or LinearMarkingDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition
            or SidewalkAreaDefinition or CurbExtensionDefinition or PlanterDefinition;

    /// <summary>Marca de sinalização horizontal (pintura) – vira piso quando a opção está ligada.</summary>
    public static bool PaintDefinition(MarkingDefinition def) =>
        def is LinearMarkingDefinition l && !RoadSectionInference.IsPhysical(l.Code)
        || def is HatchMarkingDefinition or SymbolMarkingDefinition or TextMarkingDefinition or ParkingMarkingDefinition or RepeatedMarkingDefinition
            or RecessMarkingDefinition or ChannelizationDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition;

    public static bool PaintFloorEligible(MarkingPiece p) =>
        p.Solid == null && p.Profile == null && MarkingColors.IsPaint(p.Color) && p.Shape.Area > 0.0004 && p.Thickness >= 0.0015;

    /// <summary>Códigos de linhas e símbolos que costumam ficar sobre uma pintura de fundo (ciclofaixa, faixa de caminhada).</summary>
    public static readonly IReadOnlyList<string> OverlayCodes = new[] { "CIC-LD", "CIC-LC", "FCA-BD", "MCC", "SIC", "CIC-SETA", "SPE", "LCA" };

    /// <summary>Marca pintada que fica sobre uma pintura de fundo: no Revit sobe a espessura da pintura (sem faces coincidentes).</summary>
    public static bool IsOverlayPaint(MarkingDefinition d) => d switch
    {
        LinearMarkingDefinition l => OverlayCodes.Contains(l.Code),
        RepeatedMarkingDefinition r => r.SymbolCode != null && OverlayCodes.Contains(r.SymbolCode),
        SymbolMarkingDefinition s => OverlayCodes.Contains(s.Code),
        TextMarkingDefinition => true,
        _ => false,
    };

    /// <summary>
    /// Peças com a altura que têm no Revit quando a sinalização vira piso (<c>MarkingService</c>): a pintura com a espessura
    /// <paramref name="paintThickness"/> (padrão 3 mm, a espessura mínima das Configurações) e, nas marcas sobre fundo, logo
    /// acima dele. Para conferir sobreposições no núcleo (<see cref="PieceOverlaps"/>).
    /// </summary>
    public static List<MarkingPiece> RevitPieces(MarkingDefinition def, MarkingGeometry geo, double paintThickness = 0.003)
    {
        if (!PaintDefinition(def)) return geo.Pieces.ToList();
        var lift = IsOverlayPaint(def) ? paintThickness : 0;
        var pt = Math.Max(0.002, paintThickness);
        return geo.Pieces.Select(p => p.Solid == null && p.Profile == null && MarkingColors.IsPaint(p.Color)
            ? p with { Elevation = p.Elevation + lift, Thickness = pt } : p).ToList();
    }

    /// <summary>
    /// Marcas que viram UM elemento só, com todos os materiais (placa: poste, chapa, orla, fundo e legenda) – seleciona-se e
    /// gira-se a placa inteira.
    /// </summary>
    public static bool SingleElement(MarkingDefinition def) => def is SignDefinition;

    /// <summary>Chave dos pisos: peças com a mesma chave e encostadas viram um piso só.</summary>
    public static (MarkingColor Color, double Elevation, double Thickness, string? Layer) FloorKey(MarkingPiece p) =>
        (p.Color, Math.Round(p.Elevation, 3), Math.Round(p.Thickness, 3), p.Layer);

    /// <summary>
    /// Contorno pronto para o Revit: união (remove auto-toques), arestas menores que 1 cm e vértices colineares removidos.
    /// Curvas discretizadas e recortes oblíquos geram arestas submilimétricas que o Revit recusa.
    /// </summary>
    public static Polygon2 Prepare(Polygon2 poly)
    {
        try
        {
            var u = PolygonOps.Union(new[] { poly }).OrderByDescending(p => p.Area).FirstOrDefault();
            var simplified = (u ?? poly).Simplified(0.01, 1e-4);
            return simplified ?? u ?? poly;
        }
        catch
        {
            return poly.Simplified(0.01, 1e-4) ?? poly;
        }
    }

    /// <summary>
    /// Elementos que a camada Revit cria para a marca (Piso e pintura como Piso ligados; sem superfície): pisos por chave e
    /// por parte contínua, pintura num piso por cor com todos os traços como contornos, demais peças numa forma direta por
    /// cor (ou uma só, nas placas).
    /// </summary>
    /// <summary>
    /// União das peças de um piso. Peças encostadas que não coincidem na grade das operações booleanas (0,1 mm) – depois de
    /// uma rotação, por exemplo – saem como um contorno que se toca ou em partes separadas por frestas; essas partes são
    /// fechadas com 0,5 mm (as frestas somem e o furo volta a ser furo). As demais ficam como a união deu.
    /// </summary>
    public static List<Polygon2> FloorUnion(IEnumerable<Polygon2> shapes)
    {
        var list = shapes.ToList();
        var union = PolygonOps.Union(list);
        if (!union.Any(SelfTouching)) return union;
        var healed = PolygonOps.Offset(PolygonOps.Offset(union, ClosingGap), -ClosingGap);
        return healed.Any(SelfTouching) ? union : healed;
    }

    /// <summary>Fresta fechada pela <see cref="FloorUnion"/> (m).</summary>
    public const double ClosingGap = 0.0005;

    private static bool SelfTouching(Polygon2 p) =>
        PolygonChecks.Problem(p.Outer, 1e-5) != null || p.Holes.Any(h => PolygonChecks.Problem(h, 1e-5) != null);

    public static List<PlannedElement> Plan(MarkingDefinition def, MarkingGeometry geo, bool physicalAsFloors = true, bool paintAsFloors = true,
        bool singleElement = true)
    {
        var res = new List<PlannedElement>();
        var paint = paintAsFloors && PaintDefinition(def);
        // Pintura como piso: a camada Revit dá à peça a espessura da tinta (≥ 2 mm) antes de escolher.
        var pieces = geo.Pieces.Select(p => paint && p.Solid == null && p.Profile == null && MarkingColors.IsPaint(p.Color) ? p with { Thickness = Math.Max(0.002, p.Thickness) } : p).ToList();
        var floorPieces = pieces.Where(p => physicalAsFloors && FloorEligible(def, p) || paint && PaintFloorEligible(p)).ToList();
        foreach (var grp in floorPieces.GroupBy(FloorKey))
        {
            var parts = FloorUnion(grp.Select(p => p.Shape)).Where(p => p.Area > 0.01).ToList();
            if (MarkingColors.IsPaint(grp.Key.Color) && parts.Count > 1) res.Add(new PlannedElement(true, grp.Key.Color, parts));
            else res.AddRange(parts.Select(p => new PlannedElement(true, grp.Key.Color, new[] { p })));
        }
        var solids = pieces.Where(p => !floorPieces.Contains(p)).ToList();
        if (solids.Count == 0) return res;
        if (singleElement && SingleElement(def)) res.Add(new PlannedElement(false, solids[0].Color, solids.Where(p => p.Solid == null && p.Profile == null).Select(p => p.Shape).ToList()));
        else
            foreach (var g in solids.GroupBy(p => p.Color))
                res.Add(new PlannedElement(false, g.Key, g.Where(p => p.Solid == null && p.Profile == null).Select(p => p.Shape).ToList()));
        return res;
    }
}
