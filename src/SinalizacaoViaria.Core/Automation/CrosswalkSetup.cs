using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

public enum StopLineSpan
{
    /// <summary>Somente as faixas que chegam à travessia (meia pista – vias de mão dupla).</summary>
    MeiaPista,
    /// <summary>Toda a largura da pista (vias de mão única).</summary>
    PistaInteira,
}

/// <summary>
/// Travessia de pedestres completa: faixa (FTP-1/FTP-2) + linhas de retenção antes da faixa,
/// a partir de dois pontos nos bordos da pista. Considera circulação pela direita (Brasil).
/// </summary>
public sealed class CrosswalkSetup
{
    public string CrosswalkCode { get; set; } = "FTP-1";
    public string? CrosswalkVariant { get; set; }

    /// <summary>Largura da faixa no sentido do tráfego (m). Nulo = da variante.</summary>
    public double? CrosswalkWidth { get; set; }

    public bool StopLines { get; set; } = true;
    public string StopLineVariant { get; set; } = "0,40 m";

    /// <summary>Distância livre entre a faixa de pedestres e a linha de retenção (m).</summary>
    public double StopLineDistance { get; set; } = 1.60;

    public bool StopLineLeftSide { get; set; } = true;
    public bool StopLineRightSide { get; set; } = true;
    public StopLineSpan Span { get; set; } = StopLineSpan.MeiaPista;

    /// <summary>Recuo das extremidades em relação aos bordos clicados (m).</summary>
    public double EdgeSetback { get; set; }

    /// <summary>
    /// Gera as definições. <paramref name="crosswalkWidth"/> é a largura total ocupada pela faixa
    /// (no sentido do tráfego) e <paramref name="stopLineWidth"/> a largura da LRE.
    /// </summary>
    public List<MarkingDefinition> Build(Vec2 a, Vec2 b, double z, OutputSettings output, double crosswalkWidth, double stopLineWidth)
    {
        var groupId = Guid.NewGuid().ToString("N");
        var res = new List<MarkingDefinition>();
        var u = (b - a).Normalized();
        var n = u.PerpLeft;
        var aa = a + u * EdgeSetback;
        var bb = b - u * EdgeSetback;

        res.Add(new LinearMarkingDefinition
        {
            Code = CrosswalkCode,
            Variant = CrosswalkVariant,
            WidthOverride = CrosswalkCode.StartsWith("FTP-1", StringComparison.OrdinalIgnoreCase) ? CrosswalkWidth : null,
            PathRef = PathReference.FromPoints(new[] { aa, bb }, z),
            Output = output.Clone(),
            GroupId = groupId,
        });

        if (!StopLines) return res;
        var d = crosswalkWidth / 2 + StopLineDistance + stopLineWidth / 2;
        var m = (aa + bb) / 2;

        // Lado +n: condutor desloca-se no sentido -n; sua direita é -u (lado de A).
        if (StopLineLeftSide)
        {
            var p0 = aa + n * d;
            var p1 = (Span == StopLineSpan.MeiaPista ? m : bb) + n * d;
            res.Add(StopLine(p0, p1));
        }
        // Lado -n: condutor desloca-se no sentido +n; sua direita é +u (lado de B).
        if (StopLineRightSide)
        {
            var p0 = (Span == StopLineSpan.MeiaPista ? m : aa) - n * d;
            var p1 = bb - n * d;
            res.Add(StopLine(p0, p1));
        }
        return res;

        LinearMarkingDefinition StopLine(Vec2 p0, Vec2 p1) => new()
        {
            Code = "LRE",
            Variant = StopLineVariant,
            PathRef = PathReference.FromPoints(new[] { p0, p1 }, z),
            Output = output.Clone(),
            GroupId = groupId,
        };
    }
}
