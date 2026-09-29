using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Nível da calçada no alinhamento predial numa estaca (m acima do topo da pista).</summary>
public sealed class NivelAlinhamento
{
    public double Estaca { get; set; }
    public double Nivel { get; set; }
    public NivelAlinhamento Clone() => (NivelAlinhamento)MemberwiseClone();

    /// <summary>Lê "0:0,15; 40:0,40; 5+10:0,40" (estaca:nível).</summary>
    public static List<NivelAlinhamento> Parse(string? text)
    {
        var res = new List<NivelAlinhamento>();
        if (string.IsNullOrWhiteSpace(text)) return res;
        foreach (var tok in text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = tok.Split(new[] { ':', '=' }, 2);
            if (kv.Length != 2) continue;
            var st = PontoLargura.ParseEstaca(kv[0]);
            var lv = PontoLargura.ParseEstaca(kv[1]);
            if (st is { } s && lv is { } l && l > -3 && l < 5) res.Add(new NivelAlinhamento { Estaca = s, Nivel = l });
        }
        return res.OrderBy(x => x.Estaca).ToList();
    }

    public static string Format(IEnumerable<NivelAlinhamento> list) =>
        string.Join("; ", list.Select(x => $"{x.Estaca.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}:{x.Nivel.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}"));
}

/// <summary>
/// Níveis da calçada: inclinação transversal (NBR 9050: até 3 %) e nível no alinhamento predial variando ao longo da via
/// (edificações mais altas/antigas). A faixa de acesso junto ao lote absorve o desnível que a inclinação da faixa livre
/// não vence (rampa, até 8,33 %; acima disso, degraus).
/// </summary>
public static class SidewalkLevels
{
    /// <summary>Cota relativa (m) do topo da marca no ponto, somada ao nível próprio (Height). Nulo = marca sem níveis variáveis.</summary>
    public static Func<Vec2, double>? Function(LinearMarkingDefinition d, Polyline2 axis, Catalogo cat)
    {
        if (d.LevelProfile.Count == 0 || axis.Points.Count < 2) return null;
        var pts = d.LevelProfile.Select(p =>
        {
            var c = p.Clone();
            if (p.Anchor is { } a) c.Station = axis.Project(a).Station;
            return c;
        }).OrderBy(p => p.Station).ToList();
        var w0 = d.WidthOverride ?? LinearWidth(d, cat);
        var lat = d.PathRef.Lateral;
        var (shift, widen, _) = lat is { IsEmpty: false } ? lat.For(axis) : (_ => 0, _ => 0, new List<double>());
        (double I, double O) At(double s)
        {
            if (s <= pts[0].Station) return (pts[0].Inner, pts[0].Outer);
            if (s >= pts[^1].Station) return (pts[^1].Inner, pts[^1].Outer);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (s > pts[i + 1].Station) continue;
                var u = (s - pts[i].Station) / Math.Max(1e-9, pts[i + 1].Station - pts[i].Station);
                return (pts[i].Inner + (pts[i + 1].Inner - pts[i].Inner) * u, pts[i].Outer + (pts[i + 1].Outer - pts[i].Outer) * u);
            }
            return (pts[^1].Inner, pts[^1].Outer);
        }
        return p =>
        {
            var (s, t) = axis.Project(p);
            var c = d.Offset + shift(s);
            var w = Math.Max(0.05, w0 + widen(s));
            var u = d.Offset >= 0 ? (t - (c - w / 2)) / w : ((c + w / 2) - t) / w;
            u = Math.Clamp(u, 0, 1);
            var (i, o) = At(s);
            return i + (o - i) * u;
        };
    }

    private static double LinearWidth(LinearMarkingDefinition d, Catalogo cat)
    {
        var t = cat.Linear(d.Code);
        var v = t == null ? null : MarkingBuilder.ResolveVariant(t, d.Variant, d.Speed);
        return v?.Faixas.Max(f => f.Largura) ?? 1.0;
    }

    /// <summary>Maior subida do perfil (m) – a laje fica mais espessa para não abrir vão sob o topo elevado.</summary>
    public static double MaxRise(LinearMarkingDefinition d) =>
        d.LevelProfile.Count == 0 ? 0 : Math.Max(0, d.LevelProfile.Max(p => Math.Max(p.Inner, p.Outer)));
}

public sealed partial class RoadSetup
{
    /// <summary>
    /// Perfis de nível das faixas da calçada (serviço, livre, acesso) a partir da inclinação transversal e dos níveis no
    /// alinhamento. Distâncias <paramref name="a"/> (face do meio-fio) e <paramref name="b"/> (alinhamento).
    /// </summary>
    private void SidewalkLevelProfiles(ElementoSecao e, double a, double b, double top, double cw, double service, double access,
        LinearMarkingDefinition? serviceDef, LinearMarkingDefinition? freeDef, LinearMarkingDefinition? accessDef, bool left)
    {
        var slope = Math.Clamp(e.InclinacaoTransversal, -8, 8) / 100.0;
        if (Math.Abs(slope) < 1e-6 && e.NiveisAlinhamento.Count == 0) return;
        var width = b - a;
        var free = width - cw - service - access;
        var list = e.NiveisAlinhamento.Count > 0 ? e.NiveisAlinhamento.OrderBy(x => x.Estaca).ToList() : new List<NivelAlinhamento> { new() { Estaca = 0, Nivel = top + slope * width } };
        var side = left ? "esquerda" : "direita";
        foreach (var n in list)
        {
            // Nível (acima da pista) em cada divisa: meio-fio → serviço → livre → acesso → alinhamento.
            var d1 = top + slope * (cw + service);
            var d2 = d1 + slope * free;
            var lot = n.Nivel;
            double accIn, accOut;
            if (access > 0.05) { accIn = d2; accOut = lot; }
            else
            {
                // Sem faixa de acesso: a calçada inteira se inclina até o nível do alinhamento.
                var k = (lot - top) / Math.Max(0.05, width);
                d1 = top + k * (cw + service);
                d2 = lot;
                accIn = accOut = lot;
                if (Math.Abs(k) > 0.03 + 1e-6)
                    Warnings.Add($"Calçada ({side}), estaca {PontoLargura.FormatEstaca(n.Estaca)}: inclinação transversal de {k * 100:0.0}% – a NBR 9050 admite até 3 %. Use faixa de acesso para vencer o desnível do lote.");
            }
            var ramp = access > 0.05 ? (accOut - accIn) / access : 0;
            if (Math.Abs(ramp) > 0.0833 + 1e-6)
                Warnings.Add($"Calçada ({side}), estaca {PontoLargura.FormatEstaca(n.Estaca)}: a faixa de acesso vence {Math.Abs(accOut - accIn):0.00} m em {access:0.00} m ({Math.Abs(ramp) * 100:0}%) – acima de 8,33 %: prever degraus na faixa de acesso (NBR 9050) ou alargá-la.");
            serviceDef?.LevelProfile.Add(new SidewalkLevelPoint { Station = n.Estaca, Inner = top + slope * cw - (serviceDef.Height ?? top), Outer = d1 - (serviceDef.Height ?? top) });
            freeDef?.LevelProfile.Add(new SidewalkLevelPoint { Station = n.Estaca, Inner = d1 - top, Outer = d2 - top });
            accessDef?.LevelProfile.Add(new SidewalkLevelPoint { Station = n.Estaca, Inner = accIn - top, Outer = accOut - top });
        }
        // Rampa longitudinal do alinhamento entre pontos (rota acessível: até 8,33 %).
        for (int i = 0; i + 1 < list.Count; i++)
        {
            var g = Math.Abs(list[i + 1].Nivel - list[i].Nivel) / Math.Max(0.01, list[i + 1].Estaca - list[i].Estaca);
            if (g > 0.0833 + 1e-6)
                Warnings.Add($"Calçada ({side}): entre as estacas {PontoLargura.FormatEstaca(list[i].Estaca)} e {PontoLargura.FormatEstaca(list[i + 1].Estaca)} o nível no alinhamento varia {g * 100:0}% – acima de 8,33 %: afaste os pontos ou use degraus/patamar.");
        }
    }
}
