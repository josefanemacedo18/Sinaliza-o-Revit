using System.Text.Json;
using System.Text.Json.Serialization;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Contagem classificada de conversões de uma aproximação (percentuais e, opcionalmente, o total contado).</summary>
public sealed class TurnCount
{
    /// <summary>Aproximação: id da via + sentido ("id|+" no sentido do eixo, "id|-" contrário).</summary>
    public string Approach { get; set; } = "";
    /// <summary>Volume total contado na aproximação (veh/h). Nulo = o da alocação.</summary>
    public double? Total { get; set; }
    public double Left { get; set; }
    public double Through { get; set; } = 100;
    public double Right { get; set; }
    public double UTurn { get; set; }

    public double Share(Giro g)
    {
        var sum = Math.Max(1e-9, Left + Through + Right + UTurn);
        return g switch { Giro.Esquerda => Left, Giro.Direita => Right, Giro.Retorno => UTurn, _ => Through } / sum;
    }
}

/// <summary>Ajustes do cenário para um cruzamento: controle, ciclo e verdes fixos, contagens de conversão.</summary>
public sealed class NodeOverride
{
    /// <summary>Chave do nó (<see cref="TrafficNode.Key"/>).</summary>
    public string Node { get; set; } = "";
    /// <summary>Controle do cenário (nulo = o do projeto).</summary>
    public ControleNo? Control { get; set; }
    /// <summary>Ciclo fixo (s) – nulo = otimizado (ou o plano gravado).</summary>
    public double? Cycle { get; set; }
    /// <summary>Verdes fixos de cada fase (s), na ordem das fases.</summary>
    public List<double>? Greens { get; set; }
    public List<TurnCount> Turns { get; set; } = new();
    /// <summary>Entreverdes por fase (amarelo + vermelho geral), s – nulo = 4 s (ou o do plano gravado).</summary>
    public double? Intergreen { get; set; }
    /// <summary>Defasagem fixa do início do ciclo (s) – nula = 0 ou a da coordenação.</summary>
    public double? Offset { get; set; }
    /// <summary>Proíbe as conversões à esquerda e retornos no cruzamento (teste de solução; R-4a no projeto).</summary>
    public bool? NoLeft { get; set; }
    /// <summary>Acrescenta bolsões de conversão à esquerda (teste de solução).</summary>
    public bool? LeftPockets { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Control == null && Cycle == null && (Greens == null || Greens.Count == 0) && Turns.Count == 0 && Intergreen == null && Offset == null && NoLeft != true && LeftPockets != true;
}

/// <summary>
/// Semáforo de travessia de pedestres no meio da quadra (cenário): posição (m, coordenadas do projeto), ativo ou não
/// (desligar um semáforo existente), ciclo, verde de pedestres, entreverdes e defasagem.
/// </summary>
public sealed class CrossingSignal
{
    public double X { get; set; }
    public double Y { get; set; }
    public bool Enabled { get; set; } = true;
    public double Cycle { get; set; } = 75;
    public double PedGreen { get; set; } = 18;
    public double Clearance { get; set; } = 4;
    public double Offset { get; set; }
    [JsonIgnore]
    public double VehicleRed => Math.Min(Cycle - 5, PedGreen + Clearance);
}

/// <summary>Cenário de simulação salvo no projeto: todas as opções e ajustes por cruzamento, com nome e notas.</summary>
public sealed class TrafficScenario
{
    public string Name { get; set; } = "Cenário";
    public string Notes { get; set; } = "";
    public DateTime Saved { get; set; } = DateTime.Now;
    public TrafficOptions Options { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string ToJson(IEnumerable<TrafficScenario> list) => JsonSerializer.Serialize(list.ToList(), Json);

    public static List<TrafficScenario> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<TrafficScenario>>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public TrafficScenario Clone() => FromJson(ToJson(new[] { this })).FirstOrDefault() ?? new TrafficScenario();

    /// <summary>Cenários prontos para começar: hoje no pico, horizonte de 10 anos, entrepico e contrafluxo saturado.</summary>
    public static List<TrafficScenario> Presets() => new()
    {
        new TrafficScenario { Name = "Pico atual", Notes = "Hora de pico com a demanda estimada ou contada.", Options = new TrafficOptions { Demand = NivelDemanda.Pico } },
        new TrafficScenario { Name = "Pico – horizonte 10 anos", Notes = "Crescimento de 3 % ao ano (≈ +34 %).", Options = new TrafficOptions { Demand = NivelDemanda.Pico, Growth = 0.34 } },
        new TrafficScenario { Name = "Entrepico", Notes = "Fora do pico: semáforos com ciclo curto.", Options = new TrafficOptions { Demand = NivelDemanda.Media } },
        new TrafficScenario { Name = "Teste de estresse", Notes = "Demanda 40 % acima do pico – onde a rede quebra primeiro.", Options = new TrafficOptions { Demand = NivelDemanda.Saturada } },
    };
}

/// <summary>Comparação de cenários: indicadores lado a lado e diferenças por cruzamento.</summary>
public static class TrafficComparison
{
    private static readonly System.Globalization.CultureInfo Pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public sealed record Row(string Indicator, string Unit, List<double> Values, bool LowerIsBetter);

    public static List<Row> Rows(IReadOnlyList<TrafficResult> results)
    {
        List<double> V(Func<TrafficResult, double> f) => results.Select(f).ToList();
        static IEnumerable<NodeResult> Nodes(TrafficResult r) => r.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind != TipoNo.Continuacao && n.Volume > 0);
        return new List<Row>
        {
            new("Demanda", "veh/h", V(r => r.TotalDemand), false),
            new("Velocidade média na rede", "km/h", V(r => r.AvgSpeed), false),
            new("Atraso total", "veh·h/h", V(r => r.TotalDelayH), true),
            new("Pior atraso em cruzamento", "s/veh", V(r => Nodes(r).Select(n => n.Delay).DefaultIfEmpty(0).Max()), true),
            new("Cruzamentos em nível E ou F", "un", V(r => Nodes(r).Count(n => n.LOS is "E" or "F")), true),
            new("Trechos saturados (v/c > 1)", "un", V(r => r.Links.Values.Count(l => l.X > 1)), true),
            new("Pontos de conflito", "un", V(r => r.Safety.Sum(s => s.Value.Total)), true),
            new("Acidentes previstos", "acid./ano", V(r => r.Safety.Sum(s => s.Value.CrashesPerYear)), true),
            new("CO₂", "kg/h", V(r => r.CO2kg), true),
            new("Custo anual (tempo + combustível + CO₂ + acidentes)", "R$ mil/ano", V(r => r.Economics.Total / 1000), true),
            new("Diagnósticos críticos", "un", V(r => r.Diagnostics.Count(d => d.Severity == Gravidade.Critico)), true),
        };
    }

    public static string Text(IReadOnlyList<(string Name, TrafficResult Result)> runs)
    {
        if (runs.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("COMPARAÇÃO DE CENÁRIOS");
        sb.AppendLine(new string('=', 30 + 18 * runs.Count));
        sb.Append("Indicador".PadRight(52));
        foreach (var (n, _) in runs) sb.Append((n.Length > 16 ? n[..15] + "…" : n).PadLeft(18));
        sb.AppendLine();
        var rows = Rows(runs.Select(r => r.Result).ToList());
        foreach (var row in rows)
        {
            var label = $"{row.Indicator} ({row.Unit})";
            sb.Append((label.Length > 51 ? label[..50] + "…" : label).PadRight(52));
            var best = row.LowerIsBetter ? row.Values.Min() : row.Values.Max();
            foreach (var v in row.Values)
                sb.Append(((Math.Abs(v - best) < 1e-9 && runs.Count > 1 ? "★ " : "") + v.ToString(Math.Abs(v) >= 100 ? "N0" : "N1", Pt)).PadLeft(18));
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("Cruzamentos – atraso (s/veh) e nível de serviço");
        var keys = runs[0].Result.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind != TipoNo.Continuacao).Select(n => n.Node).ToList();
        foreach (var nd in keys.OrderBy(n => n.Label))
        {
            sb.Append($"{nd.Label} – {TrafficReport.KindLabel(nd.Kind)}".PadRight(52));
            foreach (var (_, r) in runs)
            {
                var nr = r.Nodes.Values.FirstOrDefault(x => x.Node.Key == nd.Key);
                sb.Append((nr == null ? "–" : $"{nr.Delay.ToString("0.0", Pt)} {nr.LOS} ({TrafficReport.ControlShort(nr.Control)})").PadLeft(18));
            }
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("★ = melhor valor entre os cenários. Custos e acidentes são estimativas de referência – veja as premissas no relatório de cada cenário.");
        return sb.ToString();
    }
}
