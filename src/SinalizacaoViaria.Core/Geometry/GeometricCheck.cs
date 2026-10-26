using System.Globalization;
using System.Text;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Limites geométricos do traçado e do greide informados pelo usuário (K8). O plugin não traz valores normativos: limite vazio
/// não é conferido, e a fonte (norma, edição, tabela, classe da via) é a que o usuário escreveu – sai no relatório como foi
/// dada. Unidades: m, graus, % e m/% (K = L / |A|, como no relatório do greide).
/// </summary>
public sealed class GeometricCriteria
{
    /// <summary>Fonte dos limites (ex.: "norma X, tabela Y, classe Z"), escrita pelo usuário.</summary>
    public string? Fonte { get; set; }
    /// <summary>Raio mínimo das curvas horizontais (m).</summary>
    public double? RaioMinimo { get; set; }
    /// <summary>Tangente mínima entre duas curvas horizontais seguidas (m).</summary>
    public double? TangenteMinima { get; set; }
    /// <summary>Deflexão máxima de um vértice sem curva (°); acima dela o vértice é apontado.</summary>
    public double? DeflexaoMaximaSemCurva { get; set; }
    /// <summary>Rampa máxima do greide (%, em módulo).</summary>
    public double? RampaMaxima { get; set; }
    /// <summary>Rampa mínima do greide (%, em módulo) – escoamento longitudinal.</summary>
    public double? RampaMinima { get; set; }
    /// <summary>K mínimo das curvas verticais convexas (m/%).</summary>
    public double? KMinimoConvexa { get; set; }
    /// <summary>K mínimo das curvas verticais côncavas (m/%).</summary>
    public double? KMinimoConcava { get; set; }
    /// <summary>Comprimento mínimo de curva vertical (m).</summary>
    public double? CurvaVerticalMinima { get; set; }
    /// <summary>
    /// Declividade máxima das faces do terreno (%) acima da qual o Validar Modelo aponta a face (V14). É do terreno, não do
    /// traçado: não entra em <see cref="IsEmpty"/>.
    /// </summary>
    public double? DeclividadeMaximaTerreno { get; set; }

    /// <summary>Nenhum limite informado: nada a conferir.</summary>
    public bool IsEmpty => RaioMinimo == null && TangenteMinima == null && DeflexaoMaximaSemCurva == null && RampaMaxima == null
                           && RampaMinima == null && KMinimoConvexa == null && KMinimoConcava == null && CurvaVerticalMinima == null;

    /// <summary>Fonte para o relatório: a do usuário ou o aviso de que ele não a informou.</summary>
    public string FonteTexto => string.IsNullOrWhiteSpace(Fonte) ? "fonte não informada pelo usuário" : Fonte!.Trim();

    public GeometricCriteria Clone() => (GeometricCriteria)MemberwiseClone();
}

/// <summary>Elemento do traçado ou do greide fora de um limite informado.</summary>
/// <param name="Elemento">Ex.: "Curva 3", "PIV 2", "Rampa PIV 1–2".</param>
/// <param name="Inicio">Estaca (m) onde o elemento começa (ou o PI/PIV).</param>
/// <param name="Fim">Estaca (m) onde termina; igual ao início para pontos.</param>
public sealed record Violacao(string Elemento, double Inicio, double Fim, string Criterio, double Valor, double Limite, string Unidade)
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public string Texto =>
        $"{Elemento} ({(Fim > Inicio + 1e-6 ? $"estacas {AlignmentElements.Estaca(Inicio)} a {AlignmentElements.Estaca(Fim)}" : $"estaca {AlignmentElements.Estaca(Inicio)}")}): " +
        $"{Criterio} {Valor.ToString("0.##", Pt)} {Unidade} – limite {Limite.ToString("0.##", Pt)} {Unidade}";
}

/// <summary>
/// Confere os elementos do traçado (curvas do eixo ou dos PIs) e do greide (rampas e curvas verticais) contra os limites que o
/// usuário informou (<see cref="GeometricCriteria"/>). Só descreve o que está fora: não corrige nem sugere valores.
/// </summary>
public static class GeometricCheck
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Eixo de uma via como foi gerado (<see cref="AlignmentElements.FromAxis"/>).</summary>
    public static List<Violacao> Axis(IReadOnlyList<ElementoEixo> els, GeometricCriteria c)
    {
        var res = new List<Violacao>();
        var curva = 0;
        var vertice = 0;
        for (int i = 0; i < els.Count; i++)
        {
            var e = els[i];
            if (e.Tipo == TipoElementoEixo.Curva)
            {
                curva++;
                if (c.RaioMinimo is { } rmin && e.Raio < rmin - 1e-6)
                    res.Add(new($"Curva {curva}", e.Inicio, e.Fim, "raio", e.Raio, rmin, "m"));
            }
            else if (e.Tipo == TipoElementoEixo.Vertice)
            {
                vertice++;
                if (SharpVertex(Math.Abs(e.DeflexaoGraus), c) is { } lim)
                    res.Add(new($"Vértice {vertice} (sem curva)", e.Inicio, e.Inicio, "deflexão", Math.Abs(e.DeflexaoGraus), lim, "°"));
            }
            else if (c.TangenteMinima is { } tmin && i > 0 && i + 1 < els.Count && els[i - 1].Tipo != TipoElementoEixo.Tangente
                     && els[i + 1].Tipo != TipoElementoEixo.Tangente && e.Comprimento < tmin - 1e-6)
                res.Add(new("Tangente entre curvas", e.Inicio, e.Fim, "comprimento", e.Comprimento, tmin, "m"));
        }
        return res;
    }

    /// <summary>Curvas nos PIs de um traçado (<see cref="AlignmentElements.Horizontal(IReadOnlyList{Pi}, List{string}?)"/>).</summary>
    public static List<Violacao> Horizontal(IReadOnlyList<ElementoHorizontal> els, GeometricCriteria c)
    {
        var res = new List<Violacao>();
        ElementoHorizontal? prev = null;
        foreach (var e in els)
        {
            if (e.TemCurva)
            {
                var s0 = e.EstacaTs ?? 0;
                var s1 = e.EstacaSt ?? s0;
                if (c.RaioMinimo is { } rmin && e.Raio < rmin - 1e-6)
                    res.Add(new($"PI {e.Pi}", s0, s1, "raio", e.Raio, rmin, "m"));
                if (c.TangenteMinima is { } tmin && prev?.EstacaSt is { } st && e.EstacaTs is { } ts && ts - st < tmin - 1e-6)
                    res.Add(new($"Tangente PI {prev.Pi}–{e.Pi}", st, ts, "comprimento", Math.Max(0, ts - st), tmin, "m"));
                prev = e;
            }
            else if (SharpVertex(Math.Abs(e.DeflexaoGraus), c) is { } lim && Math.Abs(e.DeflexaoGraus) > 1e-6)
                res.Add(new($"PI {e.Pi} (sem curva)", 0, 0, "deflexão", Math.Abs(e.DeflexaoGraus), lim, "°"));
        }
        return res;
    }

    /// <summary>
    /// Vértice sem curva: apontado acima da deflexão máxima informada; sem ela, qualquer deflexão conta como raio zero quando
    /// há raio mínimo. Devolve o limite usado (nulo = não apontar).
    /// </summary>
    private static double? SharpVertex(double deflexao, GeometricCriteria c) =>
        c.DeflexaoMaximaSemCurva is { } dmax ? (deflexao > dmax + 1e-9 ? dmax : null) : c.RaioMinimo != null ? 0 : null;

    /// <summary>PIVs do greide (<see cref="AlignmentElements.Vertical"/>): rampas entre PIVs e curvas verticais.</summary>
    public static List<Violacao> Vertical(IReadOnlyList<ElementoVertical> els, GeometricCriteria c)
    {
        var res = new List<Violacao>();
        for (int i = 0; i < els.Count; i++)
        {
            var e = els[i];
            if (e.RampaSaidaPct is { } g && i + 1 < els.Count)
            {
                var (s0, s1) = (e.Estaca, els[i + 1].Estaca);
                var name = $"Rampa PIV {e.Piv}–{els[i + 1].Piv}";
                if (c.RampaMaxima is { } gmax && Math.Abs(g) > gmax + 1e-9)
                    res.Add(new(name, s0, s1, "rampa", Math.Abs(g), gmax, "%"));
                if (c.RampaMinima is { } gmin && Math.Abs(g) < gmin - 1e-9)
                    res.Add(new(name, s0, s1, "rampa", Math.Abs(g), gmin, "%"));
            }
            if (e.VariacaoPct is not { } a || Math.Abs(a) < 1e-9) continue;
            // Convexa: a rampa diminui (A < 0). Sem curva (L = 0) o K é zero.
            var convexa = a < 0;
            var kmin = convexa ? c.KMinimoConvexa : c.KMinimoConcava;
            var k = e.TemCurva ? e.K ?? 0 : 0;
            var (c0, c1) = e.TemCurva ? (e.EstacaPcv ?? e.Estaca, e.EstacaPtv ?? e.Estaca) : (e.Estaca, e.Estaca);
            var nome = $"PIV {e.Piv} ({(convexa ? "convexa" : "côncava")}{(e.TemCurva ? "" : ", sem curva")})";
            if (kmin is { } km && k < km - 1e-9)
                res.Add(new(nome, c0, c1, "K", k, km, "m/%"));
            if (c.CurvaVerticalMinima is { } lmin && e.Comprimento < lmin - 1e-6)
                res.Add(new(nome, c0, c1, "comprimento da curva", e.Comprimento, lmin, "m"));
        }
        return res;
    }

    /// <summary>Relatório CSV (";" e vírgula decimal): a fonte dos limites na primeira linha e uma linha por elemento fora.</summary>
    public static string Csv(IEnumerable<Violacao> vs, GeometricCriteria c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Limites informados pelo usuário – fonte: {c.FonteTexto.Replace(';', ',')}");
        sb.AppendLine("Elemento;Estaca inicial;Estaca final;Critério;Valor;Limite;Unidade");
        foreach (var v in vs)
            sb.AppendLine(string.Join(";", v.Elemento, AlignmentElements.Estaca(v.Inicio), AlignmentElements.Estaca(v.Fim), v.Criterio,
                v.Valor.ToString("0.###", Pt), v.Limite.ToString("0.###", Pt), v.Unidade));
        return sb.ToString();
    }

    /// <summary>Resumo para a mensagem do comando: quantos elementos fora e os primeiros deles.</summary>
    public static string Summary(IReadOnlyList<Violacao> vs, GeometricCriteria c, int max = 6)
    {
        if (vs.Count == 0) return $"Traçado e greide dentro dos limites informados ({c.FonteTexto}).";
        var sb = new StringBuilder($"{vs.Count} elemento(s) fora dos limites informados ({c.FonteTexto}):");
        foreach (var v in vs.Take(max)) sb.Append("\n  • ").Append(v.Texto);
        if (vs.Count > max) sb.Append($"\n  … e mais {vs.Count - max} (ver o CSV).");
        return sb.ToString();
    }
}
