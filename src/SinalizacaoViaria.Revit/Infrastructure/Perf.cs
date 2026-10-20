using System.Diagnostics;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Tempo por trecho do plugin. O Autoteste liga e anota em cada etapa onde foi o tempo (gerar marcas, geometria do núcleo, ler
/// as marcas do projeto, atualizador, recortes, interseções, gravar a transação) – desligado não custa nada. Um trecho chamado
/// dentro dele mesmo (geração que gera dependentes) conta o tempo uma vez só; a contagem é de todas as chamadas. Os trechos se
/// sobrepõem (gerar inclui ler e montar a geometria): servem para achar o que pesa, não para somar.
/// </summary>
public static class Perf
{
    public static bool On { get; set; }

    internal sealed class Acc
    {
        public long Ticks, Count, Start;
        public int Depth;
    }

    private static readonly Dictionary<string, Acc> Sections = new();

    /// <summary>Mede o trecho até o fim do <c>using</c>.</summary>
    public static Scope Measure(string name)
    {
        if (!On) return default;
        if (!Sections.TryGetValue(name, out var a)) Sections[name] = a = new Acc();
        a.Count++;
        if (a.Depth++ == 0) a.Start = Stopwatch.GetTimestamp();
        return new Scope(a);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly Acc? _a;
        internal Scope(Acc a) => _a = a;

        public void Dispose()
        {
            if (_a == null || _a.Depth == 0) return;
            if (--_a.Depth == 0) _a.Ticks += Stopwatch.GetTimestamp() - _a.Start;
        }
    }

    /// <summary>Tempo acumulado (ms) e chamadas de cada trecho desde que foi ligado.</summary>
    public static Dictionary<string, (double Ms, long Count)> Snapshot() =>
        Sections.ToDictionary(k => k.Key, k => (k.Value.Ticks * 1000.0 / Stopwatch.Frequency, k.Value.Count));

    public static void Reset() => Sections.Clear();
}
