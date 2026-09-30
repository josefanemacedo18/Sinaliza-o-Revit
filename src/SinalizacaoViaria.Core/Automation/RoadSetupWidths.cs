using System.Globalization;
using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Medida do levantamento numa estaca do eixo: distâncias do eixo ao bordo da pista (face do meio-fio / sarjeta) e ao
/// alinhamento (muro, divisa do lote) de cada lado. Vazio = a medida da seção.
/// </summary>
public sealed class PontoLargura
{
    public double Estaca { get; set; }
    public double? BordoEsquerdo { get; set; }
    public double? BordoDireito { get; set; }
    public double? AlinhamentoEsquerdo { get; set; }
    public double? AlinhamentoDireito { get; set; }
    public PontoLargura Clone() => (PontoLargura)MemberwiseClone();

    /// <summary>Lê estacas no formato "110", "110,5" ou "5+10,00" (estacas de 20 m).</summary>
    public static double? ParseEstaca(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().Replace(" ", "");
        double? Num(string x)
        {
            x = x.Replace(',', '.');
            return double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        }
        var k = t.IndexOf('+');
        if (k > 0 && Num(t[..k]) is { } est && Num(t[(k + 1)..]) is { } frac) return est * 20 + frac;
        return Num(t);
    }

    /// <summary>Estaca no formato "E + frac" (estacas de 20 m).</summary>
    public static string FormatEstaca(double s)
    {
        var e = Math.Floor(s / 20 + 1e-9);
        return $"{e:0}+{(s - e * 20).ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"))}";
    }
}

/// <summary>Elemento da pista que absorve a variação da largura entre meios-fios.</summary>
public enum AbsorcaoLargura
{
    /// <summary>O elemento junto ao bordo (faixa, estacionamento ou acostamento encostado no meio-fio).</summary>
    ElementoJuntoAoBordo,
    /// <summary>A faixa de tráfego mais próxima do meio-fio.</summary>
    UltimaFaixaDeTrafego,
    /// <summary>Todas as faixas de tráfego, proporcionalmente à largura.</summary>
    TodasAsFaixas,
    /// <summary>A faixa de estacionamento (senão, o elemento junto ao bordo).</summary>
    Estacionamento,
}

/// <summary>Tipo de recuo do meio-fio num trecho da via.</summary>
public enum TipoRecuo
{
    /// <summary>Baia de ônibus: o meio-fio recua e a faixa de tráfego continua (MVE + ÔNIBUS, LCO na boca da baia).</summary>
    BaiaOnibus,
    /// <summary>Faixa de desaceleração (saída): taper + faixa auxiliar paralela, separada por LCO.</summary>
    FaixaDesaceleracao,
    /// <summary>Faixa de aceleração (entrada): faixa auxiliar paralela + taper de convergência.</summary>
    FaixaAceleracao,
    /// <summary>Recuo para embarque/desembarque, táxi ou carga e descarga.</summary>
    RecuoEmbarque,
    /// <summary>Alargamento ou bolsão lateral de um retorno em U (derivado do retorno, sem marcas próprias).</summary>
    Retorno,
}

/// <summary>Recuo do meio-fio (baia de ônibus, faixa de aceleração/desaceleração, recuo de embarque).</summary>
public sealed class RecuoVia
{
    public TipoRecuo Tipo { get; set; } = TipoRecuo.BaiaOnibus;
    public bool LadoEsquerdo { get; set; }
    /// <summary>Estaca do início da transição de entrada (m).</summary>
    public double Estaca { get; set; }
    /// <summary>Extensão com a profundidade total (m).</summary>
    public double Comprimento { get; set; } = 15;
    /// <summary>Recuo do meio-fio (m).</summary>
    public double Profundidade { get; set; } = 3.0;
    public double TaperEntrada { get; set; } = 15;
    public double TaperSaida { get; set; } = 10;
    /// <summary>O alinhamento predial fica onde está (a calçada estreita). Falso = a calçada inteira se desloca.</summary>
    public bool CalcadaRecua { get; set; } = true;
    /// <summary>Transições em curva reversa (S); falso = retas (taper).</summary>
    public bool CurvaReversa { get; set; }
    /// <summary>Marcas do recuo (MVE, legenda, LCO, setas).</summary>
    public bool Sinalizacao { get; set; } = true;
    /// <summary>Placa (ponto de ônibus) e abrigo na calçada.</summary>
    public bool PlacaEAbrigo { get; set; } = true;
    /// <summary>Legenda do recuo de embarque (ex.: TÁXI, CARGA E DESCARGA). Vazio = sem legenda.</summary>
    public string Legenda { get; set; } = "";

    public RecuoVia Clone() => (RecuoVia)MemberwiseClone();

    [JsonIgnore] public double S1 => Estaca + Math.Max(0, TaperEntrada);
    [JsonIgnore] public double S2 => S1 + Math.Max(0, Comprimento);
    [JsonIgnore] public double S3 => S2 + Math.Max(0, TaperSaida);

    public static string Rotulo(TipoRecuo t) => t switch
    {
        TipoRecuo.BaiaOnibus => "Baia de ônibus",
        TipoRecuo.FaixaDesaceleracao => "Faixa de desaceleração (saída)",
        TipoRecuo.FaixaAceleracao => "Faixa de aceleração (entrada)",
        TipoRecuo.Retorno => "Alargamento / bolsão de retorno",
        _ => "Recuo de embarque / táxi / carga e descarga",
    };

    /// <summary>Valores de referência pela velocidade da via (DNIT/AASHTO, indicativos) e pelo tipo.</summary>
    public static RecuoVia Padrao(TipoRecuo t, double speedKmh, double laneWidth = 3.5)
    {
        var v = Math.Max(30, speedKmh);
        // Relação da transição (lateral 1 : n): 1:5 urbano lento até 1:20 em rodovias.
        var ratio = v <= 40 ? 5 : v <= 60 ? 8 : v <= 80 ? 15 : 20;
        return t switch
        {
            TipoRecuo.BaiaOnibus => new RecuoVia { Tipo = t, Profundidade = 3.0, Comprimento = 15, TaperEntrada = Math.Round(3.0 * Math.Max(5, ratio)), TaperSaida = Math.Round(3.0 * Math.Max(3, ratio * 0.6)) },
            TipoRecuo.FaixaDesaceleracao => new RecuoVia
            {
                Tipo = t, Profundidade = laneWidth, TaperEntrada = Math.Round(laneWidth * ratio), TaperSaida = 0, PlacaEAbrigo = false,
                Comprimento = v <= 50 ? 50 : v <= 60 ? 70 : v <= 70 ? 90 : v <= 80 ? 110 : v <= 100 ? 150 : 180,
            },
            TipoRecuo.FaixaAceleracao => new RecuoVia
            {
                Tipo = t, Profundidade = laneWidth, TaperEntrada = 0, TaperSaida = Math.Round(laneWidth * ratio), PlacaEAbrigo = false,
                Comprimento = v <= 50 ? 60 : v <= 60 ? 90 : v <= 70 ? 125 : v <= 80 ? 160 : v <= 100 ? 230 : 280,
            },
            _ => new RecuoVia { Tipo = t, Profundidade = 2.5, Comprimento = 12, TaperEntrada = 10, TaperSaida = 8, PlacaEAbrigo = false, Legenda = "EMBARQUE" },
        };
    }
}

public sealed partial class RoadSetup
{
    /// <summary>Medidas do levantamento ao longo do eixo (largura variável). Vazio = seção constante.</summary>
    public List<PontoLargura> LargurasVariaveis { get; set; } = new();

    /// <summary>Elemento da pista que absorve a variação da largura entre meios-fios.</summary>
    public AbsorcaoLargura Absorcao { get; set; } = AbsorcaoLargura.ElementoJuntoAoBordo;

    /// <summary>Transição em curva S entre os pontos medidos (falso = linear).</summary>
    public bool TransicaoSuave { get; set; }

    /// <summary>Recuos do meio-fio (baias de ônibus, faixas de aceleração/desaceleração, embarque).</summary>
    public List<RecuoVia> Recuos { get; set; } = new();

    [JsonIgnore]
    public bool HasVariableWidth => LargurasVariaveis.Count > 0 || Recuos.Count > 0;

    /// <summary>Distâncias do eixo ao bordo (face do meio-fio) e ao alinhamento, conforme a seção.</summary>
    public (double Bordo, double Alinhamento, bool TemCalcada) Nominal(bool left)
    {
        var side = left ? Left : Right;
        var (c, sw, _) = SideInfo(side, left ? 1 : -1);
        return (c, c + sw, sw > 0);
    }

    /// <summary>Zonas de um lado: onde a variação é absorvida (A0–A1) e a faixa livre da calçada (F0–F1).</summary>
    private sealed record Zone(double A0, double A1, double F0, double F1);

    private (Zone General, Zone Bay, Zone Lane, double Curb, double Lot, double LaneEdge, double Gutter, bool Walk) Zones(List<ElementoSecao> side)
    {
        var iv = new List<(double A, double B, ElementoSecao E)>();
        double a = MedianHalf;
        var sw = -1;
        for (int i = 0; i < side.Count; i++)
        {
            a += AddedGutter(side, i);
            var w = Math.Max(0.05, side[i].Largura);
            iv.Add((a, a + w, side[i]));
            if (side[i].Tipo == TipoElementoSecao.Calcada && sw < 0) { sw = i; }
            a += w;
            if (sw >= 0) break;
        }
        var nc = sw < 0 ? iv.Count : sw;
        var curb = sw >= 0 ? iv[sw].A : a;
        var lot = sw >= 0 ? iv[sw].B : curb;
        int LastLane() { for (int j = nc - 1; j >= 0; j--) if (ElementoSecao.EhFaixaDeTrafego(iv[j].E.Tipo)) return j; return -1; }
        (double, double) Of(int j) => j < 0 ? (curb, curb) : (iv[j].A, iv[j].B);
        (double A0, double A1) abs;
        switch (Absorcao)
        {
            case AbsorcaoLargura.UltimaFaixaDeTrafego: abs = Of(LastLane() >= 0 ? LastLane() : nc - 1); break;
            case AbsorcaoLargura.TodasAsFaixas:
            {
                var lanes = Enumerable.Range(0, nc).Where(j => ElementoSecao.EhFaixaDeTrafego(iv[j].E.Tipo)).ToList();
                abs = lanes.Count == 0 ? Of(nc - 1) : (iv[lanes[0]].A, iv[lanes[^1]].B);
                break;
            }
            case AbsorcaoLargura.Estacionamento:
            {
                var pk = Enumerable.Range(0, nc).LastOrDefault(j => iv[j].E.Tipo == TipoElementoSecao.Estacionamento, -1);
                abs = Of(pk >= 0 ? pk : nc - 1);
                break;
            }
            default: abs = Of(nc - 1); break;
        }
        double f0 = curb, f1 = curb;
        double gutter = 0;
        if (sw >= 0)
        {
            var e = side[sw];
            var width = iv[sw].B - iv[sw].A;
            var cw = Math.Min(e.MeioFioEfetivo, width - 0.05);
            var service = Math.Clamp(e.FaixaServico, cw, width) - cw;
            var access = Math.Clamp(e.FaixaAcesso, 0, Math.Max(0, width - cw - service));
            f0 = curb + cw + service;
            f1 = Math.Max(f0, iv[sw].B - access);
            gutter = PhysicalElements && !(sw > 0 && side[sw - 1].Elevado) ? Math.Max(0, e.Sarjeta) : 0;
        }
        var ll = LastLane();
        var laneEdge = ll >= 0 ? iv[ll].B : curb;
        var bayB = Math.Max(0, curb - gutter - 0.01);
        var laneB = Math.Max(0, laneEdge - EdgeInset - 0.08);
        // A linha de bordo (a EdgeInset do bordo da faixa) acompanha o meio-fio inteira: a absorção termina antes dela.
        if (abs.A1 - abs.A0 > 0.3) abs = (abs.A0, Math.Max(abs.A0 + 0.1, abs.A1 - EdgeInset - 0.06));
        return (new Zone(abs.A0, abs.A1, f0, f1), new Zone(bayB, bayB, f0, f1), new Zone(laneB, laneB, f0, f1), curb, lot, laneEdge, gutter, sw >= 0);
    }

    /// <summary>Deslocamento (m, para fora) na distância <paramref name="d"/> do eixo para variações dc (bordo) e dl (alinhamento).</summary>
    private static double Warp(Zone z, double dc, double dl, double d)
    {
        if (Math.Abs(dc) < 1e-9 && Math.Abs(dl) < 1e-9) return 0;
        if (d <= z.A0 + 1e-9) return 0;
        if (d < z.A1) return dc * (d - z.A0) / (z.A1 - z.A0);
        if (d <= z.F0 + 1e-9) return dc;
        if (d < z.F1) return dc + (dl - dc) * (d - z.F0) / (z.F1 - z.F0);
        return dl;
    }

    /// <summary>Variação (bordo, alinhamento) pedida pelo levantamento num lado, na estaca.</summary>
    private (double Dc, double Dl) Surveyed(bool left, double s, List<PontoLargura> pts)
    {
        if (pts.Count == 0) return (0, 0);
        var (nb, na, walk) = Nominal(left);
        (double, double) At(PontoLargura p)
        {
            var b = left ? p.BordoEsquerdo : p.BordoDireito;
            var al = left ? p.AlinhamentoEsquerdo : p.AlinhamentoDireito;
            if (!walk && b == null && al != null) b = al;
            var dc = b is { } bb ? bb - nb : 0;
            var dl = walk && al is { } aa ? aa - na : dc;
            return (dc, dl);
        }
        if (s <= pts[0].Estaca) return At(pts[0]);
        if (s >= pts[^1].Estaca) return At(pts[^1]);
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            if (s > pts[i + 1].Estaca) continue;
            var len = pts[i + 1].Estaca - pts[i].Estaca;
            var u = len < 1e-9 ? 1 : (s - pts[i].Estaca) / len;
            if (TransicaoSuave) u = u * u * (3 - 2 * u);
            var (a0, a1) = At(pts[i]);
            var (b0, b1) = At(pts[i + 1]);
            return (a0 + (b0 - a0) * u, a1 + (b1 - a1) * u);
        }
        return At(pts[^1]);
    }

    /// <summary>Recuo do meio-fio (m) do recuo <paramref name="r"/> na estaca.</summary>
    private static double RecessDepth(RecuoVia r, double s)
    {
        double U(double u) => r.CurvaReversa ? u * u * (3 - 2 * u) : u;
        // Transições nulas (faixa de aceleração começa, a de desaceleração termina em degrau): profundidade plena até a
        // própria estaca do degrau – o recuo volta a zero só 5 cm depois (estacas extras em VariationStations).
        if (s < r.Estaca - 1e-9 || s > r.S3 + 1e-9) return 0;
        if (s < r.S1) return r.TaperEntrada < 1e-6 ? r.Profundidade : r.Profundidade * U((s - r.Estaca) / r.TaperEntrada);
        if (s <= r.S2) return r.Profundidade;
        return r.TaperSaida < 1e-6 ? r.Profundidade : r.Profundidade * U(1 - (s - r.S2) / r.TaperSaida);
    }

    /// <summary>Estacas onde a variação é avaliada (quebras + a cada 1 m nas transições suaves).</summary>
    private List<double> VariationStations(List<PontoLargura> pts)
    {
        var st = new List<double>();
        for (int i = 0; i < pts.Count; i++)
        {
            st.Add(pts[i].Estaca);
            if (TransicaoSuave && i + 1 < pts.Count)
                for (var s = pts[i].Estaca + 1; s < pts[i + 1].Estaca - 0.5; s += 1) st.Add(s);
        }
        foreach (var r in Recuos)
        {
            st.AddRange(new[] { r.Estaca, r.S1, r.S2, r.S3 });
            // Degraus sem transição: dois pontos quase coincidentes.
            if (r.TaperEntrada < 0.05) st.Add(r.Estaca - 0.05);
            if (r.TaperSaida < 0.05) st.Add(r.S3 + 0.05);
            if (r.CurvaReversa)
            {
                for (var s = r.Estaca + 1; s < r.S1 - 0.5; s += 1) st.Add(s);
                for (var s = r.S2 + 1; s < r.S3 - 0.5; s += 1) st.Add(s);
            }
        }
        return st.Where(s => s >= 0).Select(s => Math.Round(s, 3)).Distinct().OrderBy(s => s).ToList();
    }

    /// <summary>Deslocamento total (para fora) de um lado, na estaca e na distância d do eixo.</summary>
    private Func<double, double, double> SideWarp(bool left, List<PontoLargura> pts)
    {
        var side = left ? Left : Right;
        var z = Zones(side);
        var recs = Recuos.Where(r => r.LadoEsquerdo == left && r.Profundidade > 0.01).ToList();
        return (s, d) =>
        {
            var (dc, dl) = Surveyed(left, s, pts);
            var total = Warp(z.General, dc, dl, d);
            foreach (var r in recs)
            {
                var dep = RecessDepth(r, s);
                if (dep < 1e-6) continue;
                var zone = r.Tipo is TipoRecuo.FaixaAceleracao or TipoRecuo.FaixaDesaceleracao ? z.Lane : z.Bay;
                total += Warp(zone, dep, z.Walk && r.CalcadaRecua ? 0 : dep, d);
            }
            return total;
        };
    }

    /// <summary>
    /// Aplica a largura variável e os recuos às marcas da via: perfis laterais em cada marca (deslocamento/alargamento),
    /// bordas variáveis no pavimento, interrupção da linha de bordo nas bocas das baias e as marcas dos recuos.
    /// </summary>
    private void ApplyVariation(List<MarkingDefinition> res, Polyline2? axis, Dictionary<MarkingDefinition, (int Sigma, double D0, double D1)> bands,
        Func<MarkingDefinition, MarkingDefinition> add)
    {
        if (!HasVariableWidth) return;
        var pts = LargurasVariaveis.OrderBy(p => p.Estaca).Select(p => p.Clone()).ToList();
        var stations = VariationStations(pts);
        if (stations.Count == 0) return;
        if (axis != null) stations = stations.Where(s => s <= axis.Length + 1e-6).ToList();
        Vec2? Anchor(double s) => axis == null ? null : axis.PointAt(Math.Clamp(s, 0, axis.Length));
        var wl = SideWarp(true, pts);
        var wr = SideWarp(false, pts);
        double W(int sigma, double s, double d) => sigma > 0 ? wl(s, d) : wr(s, d);

        foreach (var def in res.ToList())
        {
            if (def is RoadPavementDefinition pav)
            {
                var (bl, al, _) = Nominal(true);
                var (br, ar, _) = Nominal(false);
                pav.EdgeVariation = stations.Select(s => new EdgeVariationPoint
                {
                    Station = s, Anchor = Anchor(s),
                    CurbLeft = wl(s, bl), CurbRight = wr(s, br), LotLeft = wl(s, al), LotRight = wr(s, ar),
                }).ToList();
                if (!pav.HasEdgeVariation) pav.EdgeVariation.Clear();
                pav.Gaps = pav.Gaps.Select(g =>
                {
                    var sg = Math.Sign(g.Offset);
                    if (sg == 0) return g;
                    var lp = new LateralProfile { Points = stations.Select(s => new LateralPoint(s, sg * W(sg, s, Math.Abs(g.Offset)), 0, Anchor(s))).ToList() };
                    return lp.IsEmpty ? g : g with { Lateral = lp };
                }).ToList();
                continue;
            }
            if (def.Path == null) continue;
            (int Sigma, double D0, double D1)? band = bands.TryGetValue(def, out var b) ? b : def switch
            {
                LinearMarkingDefinition l => Band(l.Offset, l.WidthOverride is > 0.25 ? l.WidthOverride.Value : 0),
                HatchMarkingDefinition h when h.IsStrip => Band(h.StripOffset, h.StripWidth ?? 0),
                RepeatedMarkingDefinition r => Band(r.Offset, 0),
                DeviceMarkingDefinition dv => Band(dv.Offset, 0),
                ParkingMarkingDefinition p => (p.RightSide ? 1 : -1, -p.CurbOffset, -p.CurbOffset),
                _ => null,
            };
            if (band is not { } bd || bd.Sigma == 0) continue;
            var prof = new LateralProfile();
            foreach (var s in stations)
            {
                var d0 = W(bd.Sigma, s, bd.D0);
                var d1 = W(bd.Sigma, s, bd.D1);
                prof.Points.Add(new LateralPoint(s, bd.Sigma * (d0 + d1) / 2, d1 - d0, Anchor(s)));
            }
            if (!prof.IsEmpty) def.Path.Lateral = prof;
        }

        foreach (var sideLeft in new[] { false, true })
        {
            var (nb, na, walk) = Nominal(sideLeft);
            foreach (var p in pts)
            {
                var (dc, dl) = Surveyed(sideLeft, p.Estaca, pts);
                var z = Zones(sideLeft ? Left : Right);
                var absW = z.General.A1 - z.General.A0;
                if (Math.Abs(dc) > 1e-3 && absW > 0.05 && absW + dc < 1.0)
                    Warnings.Add($"Estaca {PontoLargura.FormatEstaca(p.Estaca)} ({(sideLeft ? "esquerda" : "direita")}): a variação de {dc:0.00} m deixa o elemento que a absorve com {absW + dc:0.00} m.");
                if (walk)
                {
                    var free = z.General.F1 - z.General.F0 + (dl - dc);
                    if (free < 1.20 - 1e-6)
                        Warnings.Add($"Estaca {PontoLargura.FormatEstaca(p.Estaca)} ({(sideLeft ? "esquerda" : "direita")}): faixa livre da calçada com {Math.Max(0, free):0.00} m – a NBR 9050 exige 1,20 m.");
                }
            }
        }

        foreach (var r in Recuos.Where(r => r.Profundidade > 0.01 && r.CalcadaRecua))
        {
            var z = Zones(r.LadoEsquerdo ? Left : Right);
            if (!z.Walk) continue;
            var free = z.General.F1 - z.General.F0 - r.Profundidade;
            if (free < 1.20 - 1e-6)
                Warnings.Add($"{RecuoVia.Rotulo(r.Tipo)} na estaca {PontoLargura.FormatEstaca(r.Estaca)}: o recuo de {r.Profundidade:0.00} m deixa a faixa livre da calçada com {Math.Max(0, free):0.00} m (NBR 9050: 1,20 m) – desloque a calçada ou reduza a profundidade.");
        }

        // Recuos: marcas próprias, bordo interrompido na boca das baias e placa/abrigo.
        foreach (var r in Recuos.Where(r => r.Profundidade > 0.01))
        {
            var side = r.LadoEsquerdo ? Left : Right;
            var sigma = r.LadoEsquerdo ? 1 : -1;
            var z = Zones(side);
            var bay = r.Tipo is TipoRecuo.BaiaOnibus or TipoRecuo.RecuoEmbarque or TipoRecuo.Retorno;
            if (bay)
                foreach (var lbo in res.OfType<LinearMarkingDefinition>().Where(l => l.Code == EdgeCode && Math.Sign(l.Offset) == sigma && Math.Abs(Math.Abs(l.Offset) - (z.LaneEdge - EdgeInset)) < 0.6))
                    lbo.Breaks.Add(new StationRange { Start = r.Estaca, End = r.S3, AnchorStart = Anchor(r.Estaca), AnchorEnd = Anchor(r.S3) });
            // Sem vagas ao longo do recuo (ponto de ônibus, faixa auxiliar – CTB art. 181): a faixa de estacionamento é
            // interrompida inteira, do início da transição de entrada ao fim da de saída.
            foreach (var pk in res.OfType<ParkingMarkingDefinition>().Where(p => ParkingSide(p) == sigma))
                pk.Breaks.Add(new StationRange { Start = r.Estaca, End = r.S3, AnchorStart = Anchor(r.Estaca), AnchorEnd = Anchor(r.S3) });
            if (r.Sinalizacao)
                add(new RecessMarkingDefinition
                {
                    Type = r.Tipo, Left = r.LadoEsquerdo, S0 = r.Estaca, S1 = r.S1, S2 = r.S2, S3 = r.S3,
                    A0 = Anchor(r.Estaca), A1 = Anchor(r.S1), A2 = Anchor(r.S2), A3 = Anchor(r.S3),
                    LaneEdge = z.LaneEdge, EdgeLine = z.LaneEdge - EdgeInset, Curb = z.Curb, Gutter = z.Gutter, Depth = r.Profundidade,
                    Speed = Speed, Legend = r.Tipo == TipoRecuo.BaiaOnibus ? "ÔNIBUS" : r.Legenda, Smooth = r.CurvaReversa,
                });
            if (r.PlacaEAbrigo && r.Tipo == TipoRecuo.BaiaOnibus && axis != null && z.Walk)
            {
                double Sd(double s) => Math.Clamp(s, 0, axis.Length);
                var travel = axis.TangentAt(Sd(r.S1)) * (r.LadoEsquerdo && TwoWay ? -1 : 1);
                var nrm = axis.TangentAt(Sd(r.S1)).PerpLeft * sigma;
                var dCurb = z.Curb + (r.CalcadaRecua ? r.Profundidade : r.Profundidade);
                var signAt = axis.PointAt(Sd(r.S1 + 1)) + nrm * (dCurb + 0.6);
                add(new SignDefinition { Code = "SAU-ONIBUS", Position = signAt, Direction = travel, Width = 0.50 });
                var mid = (r.S1 + r.S2) / 2;
                var shelterAt = axis.PointAt(Sd(mid)) + axis.TangentAt(Sd(mid)).PerpLeft * sigma * (dCurb + 1.6);
                add(new UrbanElementDefinition { Code = "ABRIGO", Position = shelterAt, Direction = -nrm });
            }
        }

        // Lado das vagas em relação ao eixo (+1 esquerda): as vagas ficam a sideSign·CurbOffset do caminho.
        static int ParkingSide(ParkingMarkingDefinition p)
        {
            var sideSign = p.RightSide ? -1.0 : 1.0;
            return Math.Abs(p.CurbOffset) < 1e-9 ? (int)sideSign : Math.Sign(sideSign * p.CurbOffset);
        }

        (int, double, double)? Band(double offset, double width)
        {
            var sg = Math.Sign(offset);
            if (sg == 0) return null;
            var d = Math.Abs(offset);
            return width > 0.25 ? (sg, d - width / 2, d + width / 2) : (sg, d, d);
        }
    }

    /// <summary>Menor e maior largura entre meios-fios e total ao longo da via (com a variação).</summary>
    public (double MinEntre, double MaxEntre, double MinTotal, double MaxTotal) WidthRange()
    {
        var w = Widths();
        if (!HasVariableWidth) return (w.EntreMeiosFios, w.EntreMeiosFios, w.Total, w.Total);
        var pts = LargurasVariaveis.OrderBy(p => p.Estaca).ToList();
        var wl = SideWarp(true, pts);
        var wr = SideWarp(false, pts);
        var (bl, al, _) = Nominal(true);
        var (br, ar, _) = Nominal(false);
        double mn = double.MaxValue, mx = double.MinValue, tn = double.MaxValue, tx = double.MinValue;
        foreach (var s in VariationStations(pts).DefaultIfEmpty(0))
        {
            var e = w.EntreMeiosFios + wl(s, bl) + wr(s, br);
            var t = w.Total + wl(s, al) + wr(s, ar);
            mn = Math.Min(mn, e); mx = Math.Max(mx, e); tn = Math.Min(tn, t); tx = Math.Max(tx, t);
        }
        return (mn, mx, tn, tx);
    }
}
