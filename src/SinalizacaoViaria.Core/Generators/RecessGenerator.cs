using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Marcas dos recuos do meio-fio (MBST Vol. IV): baia de ônibus com MVE amarela e legenda ÔNIBUS e LCO na boca da baia;
/// faixas de desaceleração/aceleração separadas da faixa de tráfego por LCO, com seta de conversão (saída) ou de mudança
/// obrigatória de faixa (entrada); recuo de embarque com LCO e legenda.
/// </summary>
public static class RecessGenerator
{
    public static MarkingGeometry Build(RecessMarkingDefinition d, Polyline2 axis, BuildContext ctx)
    {
        var geo = new MarkingGeometry { PathLength = axis.Length };
        double St(Vec2? a, double s) => a is { } p ? axis.Project(p).Station : s;
        var s0 = St(d.A0, d.S0);
        var s1 = St(d.A1, d.S1);
        var s2 = St(d.A2, d.S2);
        var s3 = St(d.A3, d.S3);
        var L = axis.Length;
        if (s3 <= 0.5 || s0 >= L - 0.5) { geo.Warnings.Add("O recuo está fora do eixo da via."); return geo; }
        var sg = d.Left ? 1.0 : -1.0;

        void Line(string code, double dist, double a, double b, string? variant = null, MarkingColor? color = null, double? width = null)
        {
            a = Math.Clamp(a, 0, L);
            b = Math.Clamp(b, 0, L);
            if (b - a < 0.5) return;
            var lm = new LinearMarkingDefinition
            {
                Code = code, Offset = sg * dist, StartSetback = a, EndSetback = L - b, Speed = variant == null ? d.Speed : null, Variant = variant,
                ColorOverride = color, WidthOverride = width,
            };
            geo.Merge(MarkingBuilder.BuildLinear(lm, axis, ctx));
        }
        void Cross(double s, double d0, double d1, MarkingColor color)
        {
            s = Math.Clamp(s, 0, L);
            var p = axis.PointAt(s);
            var n = axis.TangentAt(s).PerpLeft * sg;
            geo.AddRange(PolygonOps.Strip(new[] { p + n * d0, p + n * d1 }, 0.10), color);
        }
        Vec2 Travel(double s) => axis.TangentAt(Math.Clamp(s, 0, L)) * (d.Reverse ? -1 : 1);
        Vec2 At(double s, double dist) => axis.PointAt(Math.Clamp(s, 0, L)) + axis.TangentAt(Math.Clamp(s, 0, L)).PerpLeft * (sg * dist);
        void Text(string text, double s, double dist, MarkingColor color)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            geo.Merge(MarkingBuilder.BuildText(new TextMarkingDefinition { Text = text, Position = At(s, dist), Direction = Travel(s), Height = 1.60, Color = color }, ctx));
        }
        void Arrow(string code, double s, double dist)
        {
            if (ctx.Catalog.Simbolo(code) == null) return;
            geo.Merge(MarkingBuilder.BuildSymbol(new SymbolMarkingDefinition { Code = code, Position = At(s, dist), Direction = Travel(s), Length = 5.0 }, ctx));
        }

        var outer = d.Curb + d.Depth - d.Gutter - 0.10;   // limite externo útil do recuo (antes da sarjeta)
        switch (d.Type)
        {
            case TipoRecuo.BaiaOnibus:
            case TipoRecuo.RecuoEmbarque:
            {
                // Boca da baia: a linha de bordo vira linha de continuidade (quem entra e sai cruza a linha).
                Line("LCO", d.EdgeLine, s0, s3);
                if (d.Type == TipoRecuo.BaiaOnibus && ctx.Catalog.Linear("MVE") != null && s2 - s1 > 2 && outer - d.LaneEdge > 1)
                {
                    var inner = d.LaneEdge + 0.15;
                    Line("MVE", inner, s1, s2);
                    Line("MVE", outer, s1, s2);
                    Cross(s1, inner, outer, MarkingColor.Amarela);
                    Cross(s2, inner, outer, MarkingColor.Amarela);
                }
                var mid = d.Reverse ? (s1 + s2) / 2 + 1 : (s1 + s2) / 2 - 1;
                if (s2 - s1 > 6) Text(d.Legend, mid, (d.LaneEdge + outer) / 2, d.Type == TipoRecuo.BaiaOnibus ? MarkingColor.Amarela : MarkingColor.Branca);
                break;
            }
            case TipoRecuo.FaixaDesaceleracao:
            {
                // Separação da faixa auxiliar a partir do fim da transição; setas de saída no fim.
                Line("LCO", d.LaneEdge, s1, s3 > s2 + 0.5 ? s3 : s2);
                var aux = d.LaneEdge + d.Depth / 2;
                var end = d.Reverse ? s1 + 12 : s2 - 12;
                if (s2 - s1 > 20) Arrow(d.Left ? "PEM-E" : "PEM-D", end, aux);
                if (s2 - s1 > 60) Arrow(d.Left ? "PEM-E" : "PEM-D", d.Reverse ? s1 + 42 : s2 - 42, aux);
                break;
            }
            case TipoRecuo.FaixaAceleracao:
            {
                Line("LCO", d.LaneEdge, s0, s2);
                var aux = d.LaneEdge + d.Depth / 2;
                if (s2 - s0 > 25) Arrow(d.Left ? "SMF-D" : "SMF-E", d.Reverse ? s0 + 15 : s2 - 15, aux);
                break;
            }
        }
        return geo;
    }
}
