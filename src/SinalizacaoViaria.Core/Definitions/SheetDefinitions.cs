using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>
/// Trecho de prancha: parte do eixo de uma via desenhada numa vista própria (região de corte girada ao longo do trecho) e
/// colocada numa folha com a legenda de placas e o quadro de quantidades. Desenha na vista do trecho as linhas de corte
/// ("continua na prancha X") e a identificação. Escala, giro e conteúdo são de cada trecho; editar o trecho refaz a vista e a
/// folha. Como os trechos são estacas do eixo, a vista acompanha a via quando ela muda.
/// </summary>
public sealed class SheetSegmentDefinition : MarkingDefinition, IAnnotationDefinition
{
    /// <summary>Pavimento da via (eixo de referência).</summary>
    public string RoadId { get; set; } = "";
    /// <summary>Conjunto de pranchas (trechos da mesma divisão).</summary>
    public string SetId { get; set; } = "";
    public int Index { get; set; } = 1;
    public string SheetNumber { get; set; } = "";
    /// <summary>Estacas do trecho (m ao longo do eixo).</summary>
    public double Start { get; set; }
    public double End { get; set; }
    /// <summary>Escala da vista (1:n).</summary>
    public double Scale { get; set; } = 500;
    /// <summary>Giro da vista (°). Nulo = ao longo do trecho.</summary>
    public double? RotationDeg { get; set; }
    /// <summary>Faixa lateral a mostrar de cada lado do eixo (m).</summary>
    public double HalfWidth { get; set; } = 25;
    /// <summary>Área da planta na folha (mm) e margem da folha (mm).</summary>
    public double FrameWidthMm { get; set; } = 600;
    public double FrameHeightMm { get; set; } = 400;
    public double MarginMm { get; set; } = 10;
    public bool MatchLines { get; set; } = true;
    public bool Legend { get; set; } = true;
    public bool Quantities { get; set; } = true;
    /// <summary>Legenda e quadro com o que está dentro do trecho (verdadeiro) ou com o projeto inteiro.</summary>
    public bool ContentOfSegment { get; set; } = true;
    public double TextMm { get; set; } = 2.5;
    /// <summary>Linhas de corte com a folha anterior / seguinte (estaca) e o número delas.</summary>
    public double? PrevMatch { get; set; }
    public double? NextMatch { get; set; }
    public string? PrevSheet { get; set; }
    public string? NextSheet { get; set; }
    /// <summary>Folha, tipo de carimbo e vistas de desenho da legenda e do quadro (UniqueIds) e as marcas deles.</summary>
    public string? SheetId { get; set; }
    public string? TitleBlockTypeId { get; set; }
    public string? LegendViewId { get; set; }
    public string? TableViewId { get; set; }
    public string? LegendId { get; set; }
    public string? TableId { get; set; }
    /// <summary>Giro já aplicado à região de corte da vista (°).</summary>
    public double AppliedAngleDeg { get; set; }

    public string? TargetId => RoadId;
    public override string KindName => "Trecho de prancha";
    public override string DisplayCode => string.IsNullOrWhiteSpace(SheetNumber) ? "PRANCHA" : $"PRANCHA {SheetNumber}";

    public SheetFrame Frame => new(FrameWidthMm, FrameHeightMm);

    /// <summary>Trecho no eixo atual (região de corte, ângulo, linhas de corte).</summary>
    public SheetSegment Segment(Polyline2 axis) =>
        SheetPlanner.Segment(axis, Index, Start, End, Frame, Scale, HalfWidth, RotationDeg) with { PrevMatch = PrevMatch, NextMatch = NextMatch };

    /// <summary>Liga os trechos de um conjunto (ordem, linhas de corte e números das folhas vizinhas).</summary>
    public static void Link(IList<SheetSegmentDefinition> set)
    {
        var ordered = set.OrderBy(s => s.Start).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            s.Index = i + 1;
            s.PrevMatch = i > 0 ? (ordered[i - 1].End + s.Start) / 2 : null;
            s.NextMatch = i + 1 < ordered.Count ? (s.End + ordered[i + 1].Start) / 2 : null;
            s.PrevSheet = i > 0 ? ordered[i - 1].SheetNumber : null;
            s.NextSheet = i + 1 < ordered.Count ? ordered[i + 1].SheetNumber : null;
        }
    }
}
