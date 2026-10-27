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
/// Resumo de uma superfície (Diagnóstico do Terreno): triângulos, pontos distintos em planta (5 cm – os do levantamento), área
/// em planta (soma das faces), cotas mínima e máxima e o retângulo em planta. Só entram os vértices com coordenadas numéricas.
/// </summary>
public sealed record ResumoSuperficie(int Triangulos, int Pontos, double AreaPlanta, double? CotaMinima, double? CotaMaxima, Vec2? Min, Vec2? Max)
{
    public double? Desnivel => CotaMaxima - CotaMinima;
}

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

    /// <summary>Resumo da malha: o que o Diagnóstico do Terreno mostra de cada superfície (projetada e original).</summary>
    public static ResumoSuperficie Resumir(TerrainMesh m)
    {
        var v = m.Vertices.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)).ToList();
        var area = 0.0;
        foreach (var (ia, ib, ic) in m.Triangles)
        {
            var (a, b, c) = (m.Vertices[ia], m.Vertices[ib], m.Vertices[ic]);
            var d = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (double.IsFinite(d)) area += Math.Abs(d) / 2;
        }
        if (v.Count == 0) return new(m.Count, 0, area, null, null, null, null);
        var pontos = v.Select(p => ((long)Math.Round(p.X * 20), (long)Math.Round(p.Y * 20))).Distinct().Count();
        return new(m.Count, pontos, area, v.Min(p => p.Z), v.Max(p => p.Z), new Vec2(v.Min(p => p.X), v.Min(p => p.Y)), new Vec2(v.Max(p => p.X), v.Max(p => p.Y)));
    }

    /// <summary>
    /// Texto do Diagnóstico do Terreno (somente leitura): a superfície projetada (o Toposolid como está), o terreno original
    /// guardado pela terraplenagem, o limite de declividade com a fonte e os achados do V14 – os mesmos do Validar Modelo.
    /// </summary>
    /// <param name="toposolids">Toposolids principais do projeto.</param>
    /// <param name="projetado">Resumo da superfície atual (todos os Toposolids juntos); nulo se não há faces.</param>
    /// <param name="comOriginal">Quantos Toposolids têm o terreno original guardado.</param>
    /// <param name="original">Resumo dos terrenos originais guardados; nulo se nenhum.</param>
    /// <param name="declividadePct">Declividade máxima das faces (%), informada pelo usuário; nula = não conferida.</param>
    /// <param name="fonte">Fonte dos limites geométricos (a do usuário ou o aviso de que não foi informada).</param>
    /// <param name="achados">Achados do V14 (<see cref="Model.ModelValidator.Topografia"/>).</param>
    public static string Relatorio(int toposolids, ResumoSuperficie? projetado, int comOriginal, ResumoSuperficie? original,
        double? declividadePct, string fonte, IReadOnlyList<Model.Achado> achados)
    {
        var sb = new System.Text.StringBuilder();
        string F(double x, string f = "N2") => x.ToString(f, Pt);
        void Superficie(ResumoSuperficie r)
        {
            sb.AppendLine($"• {F(r.Triangulos, "N0")} triângulo(s), {F(r.Pontos, "N0")} ponto(s) distinto(s) em planta, {F(r.AreaPlanta, "N1")} m² em planta (soma das faces).");
            if (r is { CotaMinima: { } zmin, CotaMaxima: { } zmax, Min: { } a, Max: { } b })
                sb.AppendLine($"• Cotas de {F(zmin)} a {F(zmax)} m (desnível de {F(zmax - zmin)} m); retângulo em planta de {F(b.X - a.X, "N1")} × {F(b.Y - a.Y, "N1")} m.");
        }
        sb.AppendLine("SUPERFÍCIE PROJETADA (o Toposolid como está agora)");
        sb.AppendLine($"• {toposolids} Toposolid(s) principal(is) no projeto.");
        if (projetado != null) Superficie(projetado);
        else sb.AppendLine("• Nenhuma face de topo lida dos Toposolids.");
        sb.AppendLine();
        sb.AppendLine("TERRENO ORIGINAL (o natural, guardado na primeira terraplenagem)");
        if (comOriginal == 0 || original == null)
            sb.AppendLine("• Nenhum Toposolid tem o terreno original guardado: a Terraplenagem ainda não foi aplicada, e o terreno atual é tratado como o natural.");
        else
        {
            sb.AppendLine($"• Guardado em {comOriginal} de {toposolids} Toposolid(s).");
            Superficie(original);
        }
        sb.AppendLine();
        sb.AppendLine("DECLIVIDADE MÁXIMA DAS FACES");
        sb.AppendLine(declividadePct is { } d
            ? $"• {F(d, "0.#")} % – {fonte}."
            : "• Não informada: a declividade das faces não foi conferida (Configurações › Limites geométricos).");
        sb.AppendLine();
        sb.AppendLine("DIAGNÓSTICO (V14 – o mesmo do Validar Modelo)");
        var problemas = achados.Where(a => a.Severidade != Model.Severidade.NaoVerificavel).ToList();
        if (problemas.Count == 0) sb.AppendLine("• Nenhum problema nas verificações que rodaram.");
        foreach (var a in problemas) sb.AppendLine($"• [{Model.ModelValidator.Nome(a.Severidade)}] {a.Mensagem} → {a.AcaoRecomendada}");
        foreach (var a in achados.Where(a => a.Severidade == Model.Severidade.NaoVerificavel))
            sb.AppendLine($"• [{Model.ModelValidator.Nome(a.Severidade)}] {a.Mensagem} → {a.AcaoRecomendada}");
        sb.AppendLine();
        sb.Append("Nada foi alterado no projeto.");
        return sb.ToString();
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
