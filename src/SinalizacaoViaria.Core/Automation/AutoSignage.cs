using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Grupos da sinalização automática de uma via (cada um pode ser desligado).</summary>
public sealed class SinalizacaoAutomatica
{
    /// <summary>R-19 com a velocidade da via (hierarquia – CTB art. 61) no início de cada sentido.</summary>
    public bool Velocidade { get; set; } = true;
    /// <summary>R-24a (sentido de circulação) no início das vias de mão única.</summary>
    public bool Sentido { get; set; } = true;
    /// <summary>A-32b (passagem sinalizada de pedestres) antecipada e junto às travessias de pedestres da via.</summary>
    public bool Travessias { get; set; } = true;

    public SinalizacaoAutomatica Clone() => (SinalizacaoAutomatica)MemberwiseClone();

    /// <summary>Nenhum grupo ligado.</summary>
    public bool Empty => !Velocidade && !Sentido && !Travessias;
}

/// <summary>
/// Sinalização vertical automática da via (refeita a partir do eixo junto com os outros elementos derivados) e preservação do que
/// o usuário editou à mão. As placas de controle das interseções (R-1/R-2, retenção e legenda) vêm da própria interseção.
/// </summary>
public static class AutoSignage
{
    /// <summary>Recuo da primeira placa em relação ao início do trecho (m) – [a confirmar] no MBST Vol. I.</summary>
    public const double StartOffset = 20;

    /// <summary>Afastamento da placa a partir da face do meio-fio, na calçada (m) – o mesmo das placas das interseções.</summary>
    public const double CurbClearance = 0.45;

    /// <summary>Placas da via com o papel (parte do id determinístico) de cada uma.</summary>
    public static List<(SignDefinition Sign, string Role)> RoadSigns(RoadSetup s, Polyline2 axis, double z, IReadOnlyList<double> crossings)
    {
        var res = new List<(SignDefinition, string)>();
        if (s.Sinalizacao is not { } sv || sv.Empty || axis.Length < 10) return res;
        var len = axis.Length;
        var right = s.Lado(false);
        var left = s.Lado(true);
        // Lado direito de quem trafega: no sentido do eixo, a calçada direita; no contrário (mão dupla), a esquerda.
        double Lateral(RoadSetup.LadoVia l) => l.HasSidewalk ? l.Face + l.CurbW + CurbClearance : l.Face + 1.0;
        Vec2 At(double st, double o) => RoadFeatures.PointAt(axis, Math.Clamp(st, 0, len), o);
        Vec2 Dir(double st, int sense) => axis.TangentAt(Math.Clamp(st, 0, len)) * sense;
        var senses = s.TwoWay ? new[] { 1, -1 } : new[] { 1 };
        foreach (var sense in senses)
        {
            var lado = sense > 0 ? right : left;
            var o = sense > 0 ? -Lateral(lado) : Lateral(lado);
            // Estaca do início do trecho para quem trafega neste sentido.
            double Ahead(double d) => sense > 0 ? Math.Min(len, s.StartSetback + d) : Math.Max(0, len - s.EndSetback - d);
            var tag = sense > 0 ? "D" : "E";
            if (sv.Velocidade)
            {
                var st = Ahead(StartOffset);
                res.Add((new SignDefinition { Code = "R-19", Legend = s.Speed.ToString("0"), Position = At(st, o), Direction = Dir(st, sense), Z = z }, $"sv:R-19:{tag}"));
            }
            if (sv.Sentido && !s.TwoWay)
            {
                var st = Ahead(StartOffset - 10);
                res.Add((new SignDefinition { Code = "R-24a", Position = At(st, o), Direction = Dir(st, sense), Z = z }, $"sv:R-24a:{tag}"));
                // Do outro lado da pista também (mão única: a calçada esquerda é vista por quem entra).
                var ol = Lateral(left);
                res.Add((new SignDefinition { Code = "R-24a", Position = At(st, ol), Direction = Dir(st, sense), Z = z }, $"sv:R-24a:{tag}2"));
            }
            if (sv.Travessias)
            {
                // A-32b antecipada (distância pela velocidade, como nas soluções do simulador) e junto à travessia.
                var dist = TrafficPackages.WarningDistance(Math.Min(60, s.Speed));
                var k = 0;
                foreach (var c in crossings.OrderBy(c => c))
                {
                    var before = c - sense * dist;
                    if (before > 5 && before < len - 5)
                        res.Add((new SignDefinition { Code = "A-32b", Position = At(before, o), Direction = Dir(before, sense), Z = z }, $"sv:A-32b:{tag}{k}a"));
                    var at = c - sense * 3;
                    res.Add((new SignDefinition { Code = "A-32b", Position = At(at, o), Direction = Dir(at, sense), Z = z }, $"sv:A-32b:{tag}{k}"));
                    k++;
                }
            }
        }
        return res;
    }

    /// <summary>Ponto de referência de uma marca (placas, símbolos e legendas: a posição; linhas: o meio do caminho).</summary>
    public static Vec2? AnchorOf(MarkingDefinition d) => d switch
    {
        SignDefinition sg => sg.Position,
        SymbolMarkingDefinition sm => sm.Position,
        TextMarkingDefinition tx => tx.Position,
        UrbanElementDefinition ue when ue.Path is not { Points.Count: >= 2 } => ue.Position,
        _ when d.Path is { Points.Count: >= 2 } pr => new Polyline2(pr.Points).PointAt(new Polyline2(pr.Points).Length / 2),
        _ => null,
    };

    /// <summary>
    /// Sinalização gerada menos o que já está no projeto editado à mão: mesmo id, ou mesmo tipo e código a menos de
    /// <paramref name="radius"/> m. O que foi editado fica como está; nada é duplicado.
    /// </summary>
    public static List<MarkingDefinition> Preserve(IEnumerable<MarkingDefinition> generated, IEnumerable<MarkingDefinition> existing, double radius = 3.0)
    {
        var manual = existing.Where(e => e.EditadoManualmente).ToList();
        if (manual.Count == 0) return generated.ToList();
        return generated.Where(g =>
        {
            if (manual.Any(m => m.Id == g.Id)) return false;
            var a = AnchorOf(g);
            return !manual.Any(m => m.GetType() == g.GetType() && m.DisplayCode == g.DisplayCode && a is { } ga && AnchorOf(m) is { } ma && ga.DistanceTo(ma) < radius);
        }).ToList();
    }
}
