using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>
/// Legenda de placas, quadro de placas e quadros de quantidades numa vista própria (Legenda do Revit ou, sem nenhuma legenda
/// no projeto, vista de desenho): o conteúdo lê o projeto inteiro e não depende da vista em que o comando foi chamado – nem
/// da escala, nem da região de corte –, e a vista própria pode ir para qualquer folha.
/// </summary>
public static class ProjectTableViews
{
    /// <summary>Escala da vista própria. O tamanho no papel não depende dela (tudo é dimensionado em mm de papel).</summary>
    public const int Scale = 100;

    /// <summary>Quadros que leem o projeto inteiro e podem ter vista própria.</summary>
    public static bool IsProjectTable(MarkingDefinition d) => d is LegendDefinition or QuantityTableDefinition;

    /// <summary>Nome da vista própria do quadro.</summary>
    public static string ViewName(MarkingDefinition d) => d switch
    {
        LegendDefinition => "SV - Legenda de placas",
        QuantityTableDefinition { SignsOnly: true } => "SV - Quadro de placas",
        QuantityTableDefinition q when !string.IsNullOrEmpty(q.Category) && Enum.TryParse<CategoriaQuantitativo>(q.Category, out var c) =>
            $"SV - Quadro de quantitativos – {QuantityRow.CategoryLabel(c)}",
        QuantityTableDefinition => "SV - Quadro de quantitativos",
        _ => "SV - " + d.KindName,
    };

    /// <summary>
    /// Cópia do quadro pronta para a vista própria: canto superior esquerdo na origem, sem a vista de origem e sem o
    /// trecho de prancha (o projeto inteiro).
    /// </summary>
    public static T ForOwnView<T>(T d) where T : MarkingDefinition
    {
        var c = (T)d.CloneWithNewId();
        c.Output.Mode = OutputMode.Detalhe2D;
        c.Output.ViewId = null;
        switch (c)
        {
            case LegendDefinition lg:
                lg.Position = Vec2.Zero;
                lg.DrawnAnchor = null;
                lg.SheetSegmentId = null;
                break;
            case QuantityTableDefinition qt:
                qt.Position = Vec2.Zero;
                qt.DrawnAnchor = null;
                qt.SheetSegmentId = null;
                break;
        }
        return c;
    }

    /// <summary>
    /// Canto inferior esquerdo (m de papel) para um quadro de tamanho <paramref name="size"/> numa folha: dentro da área útil,
    /// no alto e o mais à direita possível, sem cobrir as vistas/tabelas já colocadas (<paramref name="occupied"/>) nem o
    /// carimbo. Nulo quando não cabe em lugar nenhum.
    /// </summary>
    public static Vec2? SheetSpot(Vec2 areaMin, Vec2 areaMax, IReadOnlyList<(Vec2 Min, Vec2 Max)> occupied, Vec2 size, double gap = 0.005)
    {
        if (size.X > areaMax.X - areaMin.X + 1e-9 || size.Y > areaMax.Y - areaMin.Y + 1e-9) return null;
        var xs = new List<double> { areaMax.X - size.X, areaMin.X };
        var ys = new List<double> { areaMax.Y - size.Y, areaMin.Y };
        foreach (var (mn, mx) in occupied)
        {
            xs.Add(mn.X - gap - size.X);
            xs.Add(mx.X + gap);
            ys.Add(mn.Y - gap - size.Y);
            ys.Add(mx.Y + gap);
        }
        var tol = 1e-9;
        bool Inside(double x, double y) =>
            x >= areaMin.X - tol && y >= areaMin.Y - tol && x + size.X <= areaMax.X + tol && y + size.Y <= areaMax.Y + tol;
        bool Free(double x, double y) => occupied.All(o =>
            x + size.X <= o.Min.X - gap + tol || x >= o.Max.X + gap - tol || y + size.Y <= o.Min.Y - gap + tol || y >= o.Max.Y + gap - tol);
        foreach (var y in ys.Distinct().OrderByDescending(v => v))
            foreach (var x in xs.Distinct().OrderByDescending(v => v))
                if (Inside(x, y) && Free(x, y)) return new Vec2(x, y);
        return null;
    }
}
