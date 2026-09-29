namespace SinalizacaoViaria.Core.Definitions;

/// <summary>
/// Plano semafórico de uma interseção (gravado pelo Simulador de Tráfego ou informado pelo projetista): ciclo, defasagem
/// e fases em sequência com verde, amarelo e vermelho geral.
/// </summary>
public sealed class SignalPlanDef
{
    public double Cycle { get; set; } = 90;
    /// <summary>Defasagem (s) em relação ao início do ciclo da rede – coordenação ("onda verde").</summary>
    public double Offset { get; set; }
    public List<SignalPhaseDef> Phases { get; set; } = new();
    /// <summary>Origem do plano (ex.: "Simulador – cenário Pico 2030", "Contagem CET").</summary>
    public string Source { get; set; } = "";
    public DateTime? Saved { get; set; }

    public double LostTime => Phases.Sum(p => p.Yellow + p.AllRed);
    public SignalPlanDef Clone() => new()
    {
        Cycle = Cycle, Offset = Offset, Source = Source, Saved = Saved,
        Phases = Phases.Select(p => p.Clone()).ToList(),
    };
}

/// <summary>Fase do plano: aproximações atendidas (via + sentido) e tempos.</summary>
public sealed class SignalPhaseDef
{
    public string Name { get; set; } = "";
    public double Green { get; set; } = 30;
    public double Yellow { get; set; } = 3;
    public double AllRed { get; set; } = 1;
    /// <summary>Aproximações atendidas: "idDaVia|+" (sentido do eixo) ou "idDaVia|-" (contrário).</summary>
    public List<string> Approaches { get; set; } = new();
    /// <summary>Fase exclusiva das conversões à esquerda.</summary>
    public bool LeftOnly { get; set; }
    /// <summary>Verde de pedestres atravessando durante a fase (s, 0 = sem).</summary>
    public double PedestrianGreen { get; set; }

    public SignalPhaseDef Clone() => new()
    {
        Name = Name, Green = Green, Yellow = Yellow, AllRed = AllRed, LeftOnly = LeftOnly, PedestrianGreen = PedestrianGreen,
        Approaches = Approaches.ToList(),
    };
}
