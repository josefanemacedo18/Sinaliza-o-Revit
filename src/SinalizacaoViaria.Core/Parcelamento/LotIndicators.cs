using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>
/// Indicadores do parcelamento (Etapa 7; Prompt Master §12; Motores §11): lotes, densidade bruta (lotes por hectare da
/// gleba), área e frente dos lotes (média, menor, maior), quantos ficam abaixo do lote mínimo e os percentuais do sistema
/// viário, das áreas públicas e das áreas verdes. São medidas do projeto; os limites são os informados no loteamento
/// (nenhuma regra municipal é universal).
/// </summary>
public sealed record IndicadoresLoteamento(int Lotes, double AreaGleba, double? LotesPorHectare, double? AreaMedia, double? AreaMenor, double? AreaMaior,
    double? FrenteMedia, double? FrenteMenor, double? FrenteMaior, int AbaixoDoMinimo, double PctViario, double PctPublico, double PctVerde);

public static class IndicadoresUrbanisticos
{
    public static IndicadoresLoteamento Calcular(ResumoLoteamento r)
    {
        var lotes = r.Quadras.SelectMany(q => q.Divisao.Parcelas)
            .Where(p => string.Equals(p.Categoria, CategoriaLote.Lote, StringComparison.OrdinalIgnoreCase)).ToList();
        var areas = lotes.Select(p => p.Area).ToList();
        var frentes = lotes.Where(p => p.Frente > 1e-6).Select(p => p.Frente).ToList();
        var verde = r.Categorias.Where(c => string.Equals(c.Nome, CategoriaLote.AreaVerde, StringComparison.OrdinalIgnoreCase)).Sum(c => c.Area);
        double? Med(List<double> l) => l.Count > 0 ? l.Average() : null;
        double? Min(List<double> l) => l.Count > 0 ? l.Min() : null;
        double? Max(List<double> l) => l.Count > 0 ? l.Max() : null;
        return new IndicadoresLoteamento(lotes.Count, r.AreaGleba, r.AreaGleba > 1e-6 ? lotes.Count / (r.AreaGleba / 10000) : null,
            Med(areas), Min(areas), Max(areas), Med(frentes), Min(frentes), Max(frentes), lotes.Count(p => p.AbaixoDoMinimo != null),
            r.Pct(r.AreaViaria), r.Pct(r.AreaPublica), r.Pct(verde));
    }

    /// <summary>Linhas "indicador; valor" (memorial e quadros), com o que não se mede dito como tal.</summary>
    public static List<(string Indicador, string Valor)> Linhas(IndicadoresLoteamento i)
    {
        var br = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        string M(double? v, string un) => v is { } x ? $"{x.ToString("#,##0.00", br)} {un}" : "sem lotes";
        return new()
        {
            ("Lotes", i.Lotes.ToString(br)),
            ("Densidade bruta (lotes por hectare da gleba)", i.LotesPorHectare is { } d ? d.ToString("0.0", br) : "gleba não definida"),
            ("Área média dos lotes", M(i.AreaMedia, "m²")),
            ("Menor e maior lote", i.AreaMenor is { } a0 && i.AreaMaior is { } a1 ? $"{a0.ToString("#,##0.00", br)} m² e {a1.ToString("#,##0.00", br)} m²" : "sem lotes"),
            ("Frente média dos lotes", M(i.FrenteMedia, "m")),
            ("Menor e maior frente", i.FrenteMenor is { } f0 && i.FrenteMaior is { } f1 ? $"{f0.ToString("0.00", br)} m e {f1.ToString("0.00", br)} m" : "sem lotes com frente"),
            ("Lotes abaixo do lote mínimo informado", i.AbaixoDoMinimo.ToString(br)),
            ("Sistema viário", LotReport.P(i.PctViario)),
            ("Áreas públicas", LotReport.P(i.PctPublico)),
            ("Áreas verdes", LotReport.P(i.PctVerde)),
        };
    }
}
