using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Segurança de um cruzamento: pontos de conflito e acidentes previstos (funções de desempenho do HSM).</summary>
public sealed class NodeSafety
{
    public int Legs { get; set; }
    public int Crossing { get; set; }
    public int Merging { get; set; }
    public int Diverging { get; set; }
    /// <summary>Conflitos veículo × pedestre (conversões cruzando as faixas de pedestres).</summary>
    public int Pedestrian { get; set; }
    public int Total => Crossing + Merging + Diverging;
    public double AadtMajor { get; set; }
    public double AadtMinor { get; set; }
    public double CrashesPerYear { get; set; }
    /// <summary>Parcela com vítimas (feridos ou mortos).</summary>
    public double InjuryCrashesPerYear { get; set; }
    public string Model { get; set; } = "";
}

/// <summary>Custos anuais da operação (tempo, combustível, emissões e acidentes) – referência para comparar cenários.</summary>
public sealed class TrafficEconomics
{
    public double FuelLitersPerHour { get; set; }
    public double DelayCost { get; set; }
    public double FuelCost { get; set; }
    public double Co2Cost { get; set; }
    public double CrashCost { get; set; }
    public double Total => DelayCost + FuelCost + Co2Cost + CrashCost;
}

public static class TrafficSafety
{
    /// <summary>
    /// Pontos de conflito (FHWA): com n ramos de mão dupla, cruzamentos n²(n−1)(n−2)/6 e convergências = divergências =
    /// n(n−2); a rotatória elimina os cruzamentos (n convergências e n divergências; mais n entrelaçamentos no anel de
    /// duas faixas); o semáforo separa no tempo os fluxos que se cruzam, sobrando as conversões permitidas.
    /// </summary>
    public static NodeSafety Analyze(TrafficResult res, NodeResult nr)
    {
        var net = res.Network;
        var nd = nr.Node;
        var s = new NodeSafety();
        var legs = Arms(net, nd);
        s.Legs = legs.Count;
        var n = legs.Count;
        if (n < 3 || nd.IsZone || nd.Kind == TipoNo.Continuacao) return s;
        var twoWay = legs.Count(l => l.In && l.Out);
        if (nr.Control == ControleNo.Rotatoria)
        {
            s.Merging = legs.Count(l => l.In);
            s.Diverging = legs.Count(l => l.Out);
            s.Crossing = nd.RoundaboutLanes >= 2 ? n : 0;
        }
        else
        {
            // Ramos de mão única cortam os movimentos que não existem.
            var f = Math.Clamp((double)(twoWay + (n - twoWay) * 0.5) / n, 0.3, 1);
            s.Crossing = (int)Math.Round(n * n * (n - 1) * (n - 2) / 6.0 * f * f);
            s.Merging = (int)Math.Round(n * (n - 2) * f);
            s.Diverging = s.Merging;
            if (nr.Control == ControleNo.Semaforo)
            {
                // Sobram os cruzamentos das conversões à esquerda permitidas (sem fase protegida).
                s.Crossing = nr.Approaches.Count(a => a.LeftVolume >= 1 && !a.ProtectedLeft);
            }
        }
        if (nd.Crosswalks) s.Pedestrian = nr.Approaches.Count(a => a.Movements.GetValueOrDefault(Giro.Direita) >= 1) + nr.Approaches.Count(a => a.LeftVolume >= 1);

        // Volumes diários (VDM) por via a partir da hora analisada (fator K).
        var k = Math.Clamp(res.Options.KFactor, 0.05, 0.2);
        var byRoad = new Dictionary<string, double>();
        foreach (var a in nr.Approaches) byRoad[net.Links[a.Link].Road.Id] = byRoad.GetValueOrDefault(net.Links[a.Link].Road.Id) + a.Volume;
        foreach (var o in nd.Out)
        {
            var id = net.Links[o].Road.Id;
            byRoad[id] = byRoad.GetValueOrDefault(id) + res.Links[o].Volume;
        }
        // VDM médio dos ramos de cada via (a via que atravessa o nó tem dois ramos: entra + sai = 2× o volume do ramo).
        var aadt = byRoad.ToDictionary(x => x.Key, x => x.Value / Math.Max(1, legs.Count(l => l.RoadId == x.Key)) / k);
        if (aadt.Count == 0) return s;
        var ordered = aadt.OrderByDescending(x => x.Value).ToList();
        s.AadtMajor = Math.Max(1, ordered[0].Value);
        s.AadtMinor = Math.Max(1, ordered.Skip(1).Sum(x => x.Value));

        // SPF do HSM (cap. 12 – vias urbanas e suburbanas), colisões múltiplas + veículo isolado, sem calibração local.
        (double a, double b, double c) mv, sv;
        var signal = nr.Control == ControleNo.Semaforo;
        var three = n == 3;
        if (three && !signal) { mv = (-13.36, 1.11, 0.41); sv = (-6.81, 0.16, 0.51); s.Model = "HSM 3ST (3 ramos, PARE na secundária)"; }
        else if (three) { mv = (-12.13, 1.11, 0.26); sv = (-9.02, 0.42, 0.40); s.Model = "HSM 3SG (3 ramos, semáforo)"; }
        else if (!signal) { mv = (-8.90, 0.82, 0.25); sv = (-5.33, 0.33, 0.12); s.Model = "HSM 4ST (4 ramos, PARE na secundária)"; }
        else { mv = (-10.99, 1.07, 0.23); sv = (-10.21, 0.68, 0.27); s.Model = "HSM 4SG (4 ramos, semáforo)"; }
        double Spf((double a, double b, double c) p) => Math.Exp(p.a + p.b * Math.Log(s.AadtMajor) + p.c * Math.Log(s.AadtMinor));
        var crashes = Spf(mv) + Spf(sv);
        if (n > 4) { crashes *= Math.Pow(n / 4.0, 1.5); s.Model += $", ajustado para {n} ramos"; }
        switch (nr.Control)
        {
            case ControleNo.Rotatoria:
                // Conversão em rotatória (FHWA/HSM CMF 0,61 urbana de PARE; 0,52 de semáforo) sobre o modelo equivalente.
                crashes *= nd.RoundaboutLanes >= 2 ? 0.88 : 0.61;
                s.Model = "HSM (PARE equivalente) × CMF rotatória " + (nd.RoundaboutLanes >= 2 ? "0,88 (duas faixas)" : "0,61 (uma faixa)");
                break;
            case ControleNo.PreferenciaDireita:
            case ControleNo.Livre:
                crashes *= 1.25;
                s.Model += " × 1,25 (sem controle – preferência da direita)";
                break;
            case ControleNo.DePreferencia:
                crashes *= 1.05;
                break;
        }
        if (signal && nr.Approaches.Any(a => a.ProtectedLeft)) crashes *= 0.90;           // esquerda protegida
        if (nd.LeftPockets) crashes *= signal ? 0.90 : 0.80;                               // faixa de conversão à esquerda (HSM CMF)
        if (nd.RightTurnIslands) crashes *= 0.95;
        s.CrashesPerYear = crashes;
        s.InjuryCrashesPerYear = crashes * (signal ? 0.33 : nr.Control == ControleNo.Rotatoria ? 0.20 : 0.35);
        return s;
    }

    /// <summary>Ramos do nó: direções distintas das vias que chegam/saem (agrupadas por ângulo).</summary>
    public static List<(double Angle, bool In, bool Out, string RoadId)> Arms(TrafficNetwork net, TrafficNode nd)
    {
        var raw = new List<(double A, bool In, string Road)>();
        foreach (var i in nd.In)
        {
            var l = net.Links[i];
            var d = -l.Path.TangentAt(l.Length);
            raw.Add((Math.Atan2(d.Y, d.X), true, l.Road.Id));
        }
        foreach (var o in nd.Out)
        {
            var l = net.Links[o];
            var d = l.Path.TangentAt(0);
            raw.Add((Math.Atan2(d.Y, d.X), false, l.Road.Id));
        }
        var arms = new List<(double Angle, bool In, bool Out, string RoadId)>();
        foreach (var (a, isIn, road) in raw)
        {
            var k = arms.FindIndex(x => x.RoadId == road && Math.Abs(Math.IEEERemainder(x.Angle - a, 2 * Math.PI)) < 0.45);
            if (k < 0) arms.Add((a, isIn, !isIn, road));
            else arms[k] = (arms[k].Angle, arms[k].In || isIn, arms[k].Out || !isIn, road);
        }
        return arms;
    }

    /// <summary>Consumo de combustível (L/km) de um automóvel médio em função da velocidade média (km/h).</summary>
    public static double FuelPerKm(double kmh) => 0.045 + 1.3 / Math.Max(5, kmh) + 0.0000045 * kmh * kmh;

    public static TrafficEconomics Economics(TrafficResult res)
    {
        var o = res.Options;
        var e = new TrafficEconomics();
        var fuel = 0.0;
        var heavy = 1 + o.HeavyVehicles * 1.8 + o.Buses * 1.8;          // caminhões e ônibus consomem ~2,8× o automóvel
        foreach (var lr in res.Links.Values)
            fuel += lr.Link.Length / 1000 * lr.Volume * FuelPerKm(lr.Speed) * heavy;
        e.FuelLitersPerHour = fuel;
        var hours = Math.Max(0, o.AnnualHours);
        e.DelayCost = res.TotalDelayH * o.Occupancy * o.ValueOfTime * hours;
        e.FuelCost = fuel * o.FuelPrice * hours;
        e.Co2Cost = res.CO2kg / 1000 * o.Co2Price * hours;
        e.CrashCost = res.Safety.Values.Sum(s => s.CrashesPerYear) * o.CrashCost;
        return e;
    }
}
