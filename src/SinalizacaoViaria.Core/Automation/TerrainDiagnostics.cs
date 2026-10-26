using System.Globalization;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

public enum ProblemaTerreno
{
    /// <summary>Sem superfície, ou com menos de três pontos distintos fora de uma reta: nada pode ser calculado sobre ela.</summary>
    DadosInsuficientes,
    /// <summary>Coordenada não numérica (NaN ou infinita).</summary>
    CoordenadaInvalida,
    /// <summary>O mesmo ponto em planta com cotas diferentes: degrau vertical ou dado duplicado inconsistente.</summary>
    CotasDiferentesNoMesmoPonto,
    /// <summary>Triângulo sem área em planta (pontos alinhados ou repetidos): não define cota.</summary>
    TrianguloDegenerado,
    /// <summary>Faces mais íngremes que o limite informado pelo usuário.</summary>
    DeclividadeAcimaDoLimite,
    /// <summary>Pontos consultados fora da superfície: o cálculo ali seria extrapolação.</summary>
    ForaDaSuperficie,
}

/// <summary>Um problema da superfície: tipo, texto, quantos elementos (pontos ou triângulos) e um lugar de exemplo.</summary>
public sealed record AchadoTerreno(ProblemaTerreno Tipo, string Texto, int Quantidade, Vec2? Onde);

/// <summary>
/// Diagnóstico da superfície do terreno (Prompt Master §6, Motores §6): dados insuficientes, coordenadas inválidas, cotas
/// diferentes no mesmo ponto, triângulos degenerados, declividade acima do limite **informado** (sem limite, não conferida) e
/// consultas fora da superfície. Lê a malha como ela é (original ou projetada – quem chama diz qual) e não corrige nada.
/// </summary>
public static class TerrainDiagnostics
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Distância em planta (m) abaixo da qual dois vértices são o mesmo ponto: 1 mm.</summary>
    public const double MesmoPonto = 0.001;

    /// <summary>Diferença de cota (m) acima da qual o mesmo ponto tem cotas diferentes: 1 cm.</summary>
    public const double CotaDiferente = 0.01;

    /// <summary>Área em planta (m²) abaixo da qual o triângulo não tem área: 1 cm².</summary>
    public const double AreaMinima = 1e-4;

    /// <param name="terreno">Malha da superfície.</param>
    /// <param name="declividadeMaxima">Declividade máxima das faces (m/m), informada pelo usuário; nula = não conferida.</param>
    /// <param name="consultas">Pontos em que algum cálculo vai ler a superfície (eixos das vias, por exemplo).</param>
    public static List<AchadoTerreno> Diagnosticar(TerrainMesh? terreno, double? declividadeMaxima = null, IEnumerable<Vec2>? consultas = null)
    {
        var res = new List<AchadoTerreno>();
        if (terreno == null || terreno.Count == 0)
        {
            res.Add(new(ProblemaTerreno.DadosInsuficientes, "não há superfície do terreno", 0, null));
            return res;
        }
        var v = terreno.Vertices;
        var invalidos = v.Where(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)).ToList();
        if (invalidos.Count > 0)
            res.Add(new(ProblemaTerreno.CoordenadaInvalida, $"{invalidos.Count} vértice(s) com coordenada não numérica", invalidos.Count, null));
        var validos = v.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)).ToList();

        // Pontos distintos em planta e as cotas de cada um.
        var porPonto = new Dictionary<(long, long), (Vec2 P, double Min, double Max)>();
        foreach (var p in validos)
        {
            var k = ((long)Math.Round(p.X / MesmoPonto), (long)Math.Round(p.Y / MesmoPonto));
            porPonto[k] = porPonto.TryGetValue(k, out var c) ? (c.P, Math.Min(c.Min, p.Z), Math.Max(c.Max, p.Z)) : (p.XY, p.Z, p.Z);
        }
        var distintos = porPonto.Values.Select(x => x.P).ToList();
        if (distintos.Count < 3 || Colineares(distintos))
            res.Add(new(ProblemaTerreno.DadosInsuficientes, $"só {distintos.Count} ponto(s) distinto(s){(distintos.Count >= 3 ? ", todos numa reta" : "")} – a superfície não define cotas",
                distintos.Count, distintos.FirstOrDefault()));
        var degraus = porPonto.Values.Where(x => x.Max - x.Min > CotaDiferente).OrderByDescending(x => x.Max - x.Min).ToList();
        if (degraus.Count > 0)
            res.Add(new(ProblemaTerreno.CotasDiferentesNoMesmoPonto, string.Format(Pt,
                "{0} ponto(s) com cotas diferentes no mesmo lugar em planta (até {1:0.00} m) – degrau vertical ou dado duplicado", degraus.Count, degraus[0].Max - degraus[0].Min),
                degraus.Count, degraus[0].P));

        int degenerados = 0, ingremes = 0;
        Vec2? ondeDeg = null, ondeIng = null;
        double pior = 0;
        foreach (var (ia, ib, ic) in terreno.Triangles)
        {
            var (a, b, c) = (v[ia], v[ib], v[ic]);
            if (!double.IsFinite(a.X + a.Y + a.Z + b.X + b.Y + b.Z + c.X + c.Y + c.Z)) continue;
            var d = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (Math.Abs(d) / 2 < AreaMinima) { degenerados++; ondeDeg ??= (a.XY + b.XY + c.XY) / 3; continue; }
            if (declividadeMaxima is not double lim) continue;
            // Gradiente do plano do triângulo: z = a.Z + gx·(x − a.X) + gy·(y − a.Y).
            var gx = ((b.Z - a.Z) * (c.Y - a.Y) - (c.Z - a.Z) * (b.Y - a.Y)) / d;
            var gy = ((c.Z - a.Z) * (b.X - a.X) - (b.Z - a.Z) * (c.X - a.X)) / d;
            var g = Math.Sqrt(gx * gx + gy * gy);
            if (g <= lim) continue;
            ingremes++;
            if (g > pior) { pior = g; ondeIng = (a.XY + b.XY + c.XY) / 3; }
        }
        if (degenerados > 0)
            res.Add(new(ProblemaTerreno.TrianguloDegenerado, $"{degenerados} triângulo(s) sem área em planta (pontos alinhados ou repetidos)", degenerados, ondeDeg));
        if (ingremes > 0)
            res.Add(new(ProblemaTerreno.DeclividadeAcimaDoLimite, string.Format(Pt, "{0} face(s) mais íngremes que {1:0.#} % (até {2:0.#} %)", ingremes,
                declividadeMaxima!.Value * 100, pior * 100), ingremes, ondeIng));

        if (consultas != null)
        {
            var fora = consultas.Where(p => terreno.Z(p) == null).ToList();
            if (fora.Count > 0)
                res.Add(new(ProblemaTerreno.ForaDaSuperficie, $"{fora.Count} ponto(s) consultado(s) fora da superfície – ali o cálculo seria extrapolação", fora.Count, fora[0]));
        }
        return res;
    }

    private static bool Colineares(List<Vec2> pts)
    {
        var a = pts[0];
        var b = pts.OrderByDescending(p => p.DistanceTo(a)).First();
        var u = b - a;
        if (u.Length < MesmoPonto) return true;
        var n = new Vec2(-u.Y, u.X) / u.Length;
        return pts.All(p => Math.Abs((p - a).Dot(n)) < MesmoPonto);
    }
}
