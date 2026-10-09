using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Classe de uma peça da pré-visualização da via.</summary>
public enum ClassePrevia { Pista, MeioFio, Calcada, Canteiro, Linha }

/// <summary>Peça da pré-visualização: contorno (m) e cor da peça gerada.</summary>
public sealed record PecaPrevia(ClassePrevia Classe, Polygon2 Contorno, MarkingColor Cor, string Codigo);

/// <summary>Pré-visualização de uma via: o eixo concordado (como será criado) e as peças principais da via.</summary>
public sealed record PreviaVia(IReadOnlyList<Vec2> Eixo, IReadOnlyList<PecaPrevia> Pecas)
{
    public IEnumerable<PecaPrevia> Da(ClassePrevia c) => Pecas.Where(p => p.Classe == c);
}

/// <summary>
/// Pré-visualização ao desenhar o eixo: o eixo clicado é concordado com o mesmo raio da criação, a via é gerada pela mesma
/// rotina e as peças são separadas em pista, meio-fio (com a sarjeta), calçada, canteiro e linhas principais. Não há
/// geometria paralela: a prévia é a via gerada, só que sem gravar nada.
/// </summary>
public static class RoadPreview
{
    /// <summary>Eixo concordado (retas e arcos) a partir dos pontos clicados – o mesmo da criação, discretizado.</summary>
    public static List<Vec2> Axis(IReadOnlyList<Vec2> clicked, double curveRadius, double step = 1.0)
    {
        if (clicked.Count < 2) return clicked.ToList();
        return RoadConnection.Densify(RoadConnection.Fillet(clicked, curveRadius), step);
    }

    /// <summary>
    /// Pré-visualização com a geração <paramref name="generate"/> (a mesma do comando: seção da janela, pista...). Nula com
    /// menos de dois pontos.
    /// </summary>
    public static PreviaVia? Build(IReadOnlyList<Vec2> clicked, double curveRadius,
        Func<PathReference, Polyline2, IEnumerable<MarkingDefinition>> generate, BuildContext ctx)
    {
        var pts = Axis(clicked, curveRadius);
        if (pts.Count < 2 || new Polyline2(pts).Length < 0.05) return null;
        var axis = new Polyline2(pts);
        var defs = generate(PathReference.FromPoints(pts, 0), axis).ToList();
        return From(defs, axis, ctx);
    }

    /// <summary>Pré-visualização de uma seção (<see cref="RoadSetup"/>) desenhada pelos pontos clicados.</summary>
    public static PreviaVia? Build(RoadSetup setup, IReadOnlyList<Vec2> clicked, double curveRadius, Catalogo cat) =>
        Build(clicked, curveRadius, (pr, ax) => setup.Clone().Build(pr, new OutputSettings(), cat, axis: ax), new BuildContext { Catalog = cat });

    /// <summary>Peças principais das marcas geradas sobre o eixo.</summary>
    public static PreviaVia From(IEnumerable<MarkingDefinition> defs, Polyline2 axis, BuildContext ctx)
    {
        var res = new List<PecaPrevia>();
        foreach (var d in defs)
        {
            if (d is not (RoadPavementDefinition or LinearMarkingDefinition)) continue;
            MarkingGeometry g;
            try { g = MarkingBuilder.Build(d, axis, ctx); }
            catch { continue; }
            foreach (var p in g.Pieces)
                if (Classify(d, p) is { } c) res.Add(new PecaPrevia(c, p.Shape, p.Color, d.DisplayCode));
        }
        return new PreviaVia(axis.Points, res);
    }

    /// <summary>Classe de uma peça (nula = fora da pré-visualização: zebrados, símbolos, vagas, placas...).</summary>
    public static ClassePrevia? Classify(MarkingDefinition d, MarkingPiece p)
    {
        if (d is RoadPavementDefinition) return ClassePrevia.Pista;
        if (d is not LinearMarkingDefinition l) return null;
        var code = l.Code;
        if (code.StartsWith("MEIO-FIO", StringComparison.Ordinal) || code.StartsWith("SARJETA", StringComparison.Ordinal)
            || p.Layer?.StartsWith("MEIO-FIO", StringComparison.Ordinal) == true) return ClassePrevia.MeioFio;
        if (code is "CALCADA" or "PASSEIO" or "PLATAFORMA") return ClassePrevia.Calcada;
        if (code == "GRAMADO") return ClassePrevia.Canteiro;
        if (RoadSectionInference.IsPhysical(code)) return null;
        return p.Color is MarkingColor.Branca or MarkingColor.Amarela ? ClassePrevia.Linha : null;
    }
}
