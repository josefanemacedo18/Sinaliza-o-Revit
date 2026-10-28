using System.Text.Json.Serialization;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>Como o talude lateral da via chega ao terreno num trecho.</summary>
public enum MetodoTalude
{
    /// <summary>Inclinação fixa (H : 1 V) até o terreno; com limite, onde não o alcança dentro dele, face quase vertical.</summary>
    TaludeFixo,
    /// <summary>Largura horizontal fixa: o talude vai da borda ao terreno nessa largura (a inclinação resulta do terreno).</summary>
    LarguraFixa,
}

public enum LadoTalude { Ambos, Esquerdo, Direito }

/// <summary>
/// Taludamento lateral por lado e por trecho: entre as estacas A e B (B nulo = até o fim da via) os taludes do lado indicado
/// seguem o método e os valores deste trecho; antes de A e depois de B, transições do comprimento pedido voltam aos taludes
/// da via (<see cref="RoadGrade.CutSlope"/>, <see cref="RoadGrade.FillSlope"/>). Um trecho de 0 até o fim vale como o talude
/// do lado na via inteira. Valores informados pelo usuário (padrão do plugin: os da via).
/// </summary>
public sealed class TrechoTalude
{
    public LadoTalude Lado { get; set; } = LadoTalude.Ambos;
    public double EstacaA { get; set; }
    /// <summary>Fim do trecho (m); nulo = até o fim da via.</summary>
    public double? EstacaB { get; set; }
    public double TransicaoA { get; set; } = 10;
    public double TransicaoB { get; set; } = 10;
    public MetodoTalude Metodo { get; set; } = MetodoTalude.TaludeFixo;
    /// <summary>Talude de corte (H : 1 V).</summary>
    public double Corte { get; set; } = 1.0;
    /// <summary>Talude de aterro (H : 1 V).</summary>
    public double Aterro { get; set; } = 1.5;
    /// <summary>
    /// Talude fixo: largura horizontal máxima (m) a partir da borda; nula = o alcance da terraplenagem. Onde o talude não chega ao
    /// terreno dentro dela, o terreno fica natural além do limite (face quase vertical – muro, contenção).
    /// </summary>
    public double? Limite { get; set; }
    /// <summary>Largura fixa: largura horizontal do talude (m).</summary>
    public double Largura { get; set; } = 3.0;

    public TrechoTalude Clone() => (TrechoTalude)MemberwiseClone();

    [JsonIgnore] public double A => EstacaB is { } b ? Math.Min(EstacaA, b) : EstacaA;

    /// <summary>Fim do trecho numa via de comprimento <paramref name="length"/>.</summary>
    public double B(double length) => EstacaB is { } b ? Math.Max(EstacaA, b) : Math.Max(EstacaA, length);

    /// <summary>Fração do trecho na estaca (0 fora, 1 entre A e B; transições retas). Sem transição na ponta que é o início/fim da via.</summary>
    public double Fator(double s, double length)
    {
        var b = B(length);
        var ta = A <= 1e-6 ? 0 : TransicaoA;
        var tb = b >= length - 1e-6 ? 0 : TransicaoB;
        ta = Math.Max(0, ta);
        tb = Math.Max(0, tb);
        if (s < A - ta - 1e-9 || s > b + tb + 1e-9) return 0;
        if (s < A) return ta < 1e-6 ? 1 : (s - (A - ta)) / ta;
        if (s <= b) return 1;
        return tb < 1e-6 ? 1 : 1 - (s - b) / tb;
    }

    [JsonIgnore]
    public bool Valido => Corte >= 0.1 && Aterro >= 0.1 && (Metodo != MetodoTalude.LarguraFixa || Largura >= 0.1) && (Limite is null or >= 0.1);
}

/// <summary>
/// Talude numa estaca de um lado, já com as transições: inclinações de corte e de aterro, largura máxima (m) e o peso da
/// largura fixa (0 = só talude fixo, 1 = só largura fixa <see cref="Largura"/>).
/// </summary>
public readonly record struct TaludeNoPonto(double Corte, double Aterro, double Limite, double Largura, double PesoLargura)
{
    /// <summary>
    /// Inclinações e limite no vértice da borda (ponto <paramref name="p"/>, cota <paramref name="z"/>, normal para fora
    /// <paramref name="n"/>): a largura fixa vira a inclinação que leva a borda ao terreno a <see cref="Largura"/> m.
    /// </summary>
    public (double Corte, double Aterro, double Limite) NoVertice(Vec2 p, double z, Vec2 n, Func<Vec2, double?> ground)
    {
        if (PesoLargura < 1e-9) return (Corte, Aterro, Limite);
        var w = Math.Max(0.1, Largura);
        var gw = ground(p + n * w);
        var cut = Corte;
        var fill = Aterro;
        if (gw is { } g)
        {
            // Inclinação que encontra o terreno na largura fixa (limitada a 0,1 : 1 e 100 : 1 – terreno quase no nível da borda).
            var r = Math.Clamp(w / Math.Max(1e-3, Math.Abs(g - z)), 0.1, 100);
            if (g > z) cut += (r - cut) * PesoLargura; else fill += (r - fill) * PesoLargura;
        }
        return (cut, fill, Limite + (w - Limite) * PesoLargura);
    }
}

/// <summary>Taludes da via por lado e por trecho (<see cref="RoadGrade.Taludes"/>).</summary>
public static class TaludesDaVia
{
    /// <summary>
    /// Talude do lado (<paramref name="left"/>) em função da estaca: os da via (<paramref name="cut"/>, <paramref name="fill"/>,
    /// alcance <paramref name="reach"/>) e, por cima, cada trecho do lado na ordem da lista, com as transições.
    /// </summary>
    public static Func<double, TaludeNoPonto> Resolver(double cut, double fill, IEnumerable<TrechoTalude>? trechos, bool left, double length, double reach = 60)
    {
        var mine = (trechos ?? Enumerable.Empty<TrechoTalude>())
            .Where(t => t.Valido && (t.Lado == LadoTalude.Ambos || (t.Lado == LadoTalude.Esquerdo) == left)).ToList();
        return s =>
        {
            double c = cut, f = fill, lim = reach, w = 0, pw = 0;
            foreach (var t in mine)
            {
                var k = t.Fator(s, length);
                if (k < 1e-9) continue;
                if (t.Metodo == MetodoTalude.TaludeFixo)
                {
                    c += (t.Corte - c) * k;
                    f += (t.Aterro - f) * k;
                    lim += (Math.Min(reach, t.Limite ?? reach) - lim) * k;
                    pw *= 1 - k;
                }
                else
                {
                    // Na transição a inclinação e o limite vão do que valia antes à largura fixa (TaludeNoPonto.NoVertice).
                    w = pw < 1e-9 ? t.Largura : w + (t.Largura - w) * k;
                    pw += (1 - pw) * k;
                }
            }
            return new TaludeNoPonto(c, f, lim, w, pw);
        };
    }

    /// <summary>Há algum trecho que muda o talude da via.</summary>
    public static bool Algum(RoadGrade? g) => g?.Taludes is { Count: > 0 } l && l.Any(t => t.Valido);
}
