using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Uma linha do perfil longitudinal da rede: o dispositivo na distância acumulada do ramo, a tampa, os fundos de chegada e de
/// saída e o trecho que sai dele (nulo no último dispositivo do ramo).
/// </summary>
public sealed record LinhaPerfilRede(int Ramo, string Dispositivo, double Distancia, double Tampa, double? FundoChegada, double? FundoSaida,
    string? Trecho, double? Comprimento, double? Declividade, double? Diametro, double? Recobrimento);

/// <summary>Um dispositivo da rede: tipo, posição, tampa, fundo mais baixo, profundidade e quantos trechos chegam e saem.</summary>
public sealed record DispositivoRede(string Rotulo, string Tipo, Vec2 Posicao, double Tampa, double? Fundo, int Entradas, int Saidas, string Id)
{
    public double? Profundidade => Fundo is { } f ? Tampa - f : null;
}

/// <summary>
/// Perfil longitudinal da rede e lista dos dispositivos (Etapa 6; Prompt de Interface §16): ramos de montante para jusante –
/// o primeiro é o mais longo até o lançamento; cada ramo seguinte para no dispositivo em que encontra um ramo já listado –,
/// com tampas, fundos, declividades, diâmetros e recobrimentos medidos por <see cref="DrainageNetwork.Analyze"/>.
/// </summary>
public static class PerfilRede
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static List<LinhaPerfilRede> Linhas(IReadOnlyCollection<MarkingDefinition> defs, Func<Vec2, double?>? superficie = null)
    {
        var byId = defs.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var labels = DrainageNetwork.Labels(defs);
        var medidos = DrainageNetwork.Analyze(defs, superficie).ToDictionary(t => t.Id);
        var tubos = defs.OfType<DrainagePipeDefinition>().Where(p => medidos.ContainsKey(p.Id) && p.FromId != p.ToId).ToList();
        var saidas = tubos.GroupBy(p => p.FromId).ToDictionary(g => g.Key, g => g.ToList());
        var chegam = tubos.Select(p => p.ToId).ToHashSet();
        string L(string id) => labels.GetValueOrDefault(id, byId.GetValueOrDefault(id)?.DisplayCode ?? id);
        double Tampa(string id) => byId.GetValueOrDefault(id) is DrainageDefinition d ? d.Z : double.NaN;

        // Caminho até o lançamento pelo primeiro trecho de saída (ciclos interrompidos).
        List<DrainagePipeDefinition> Caminho(string inicio)
        {
            var c = new List<DrainagePipeDefinition>();
            var vistos = new HashSet<string> { inicio };
            var atual = inicio;
            while (saidas.TryGetValue(atual, out var s) && s.Count > 0)
            {
                var p = s[0];
                c.Add(p);
                if (!vistos.Add(p.ToId)) break;
                atual = p.ToId;
            }
            return c;
        }
        var cabeceiras = saidas.Keys.Where(id => !chegam.Contains(id))
            .Select(id => (Id: id, C: Caminho(id))).OrderByDescending(x => x.C.Sum(p => medidos[p.Id].Comprimento)).ThenBy(x => L(x.Id), StringComparer.Ordinal).ToList();
        // Ramos que saem por um segundo trecho de um dispositivo (divisão de vazão) entram como cabeceiras também.
        foreach (var (from, s) in saidas)
            foreach (var p in s.Skip(1)) cabeceiras.Add((from, new List<DrainagePipeDefinition> { p }.Concat(Caminho(p.ToId)).ToList()));

        var res = new List<LinhaPerfilRede>();
        var listados = new HashSet<string>();
        var usados = new HashSet<string>();
        var ramo = 0;
        foreach (var (inicio, caminho) in cabeceiras)
        {
            var trechos = new List<DrainagePipeDefinition>();
            foreach (var p in caminho)
            {
                if (!usados.Add(p.Id)) break;
                trechos.Add(p);
                if (listados.Contains(p.ToId)) break;
            }
            if (trechos.Count == 0) continue;
            ramo++;
            var dist = 0.0;
            double? chegada = null;
            foreach (var p in trechos)
            {
                var m = medidos[p.Id];
                res.Add(new LinhaPerfilRede(ramo, L(p.FromId), dist, Tampa(p.FromId), chegada, m.FundoMontante, m.Nome, m.Comprimento, m.Declividade,
                    m.Diametro, m.CobrimentoMinimo));
                listados.Add(p.FromId);
                dist += m.Comprimento;
                chegada = m.FundoJusante;
            }
            var fim = trechos[^1].ToId;
            res.Add(new LinhaPerfilRede(ramo, L(fim), dist, Tampa(fim), chegada, null, null, null, null, null, null));
            listados.Add(fim);
        }
        return res;
    }

    /// <summary>Dispositivos da rede (os ligados por trechos e as captações soltas), na ordem dos rótulos.</summary>
    public static List<DispositivoRede> Dispositivos(IReadOnlyCollection<MarkingDefinition> defs)
    {
        var labels = DrainageNetwork.Labels(defs);
        var tubos = defs.OfType<DrainagePipeDefinition>().ToList();
        var byId = defs.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var res = new List<DispositivoRede>();
        foreach (var d in defs.OfType<DrainageDefinition>())
        {
            var fundos = tubos.Select(p => p.FromId == d.Id ? DrainageNetwork.Ends(p, id => byId.GetValueOrDefault(id))?.InvertA
                    : p.ToId == d.Id ? DrainageNetwork.Ends(p, id => byId.GetValueOrDefault(id))?.InvertB : null)
                .Where(z => z != null).Select(z => z!.Value).ToList();
            res.Add(new DispositivoRede(labels.GetValueOrDefault(d.Id, d.DisplayCode), DrainageDefinition.NomeTipo(d.Type), d.Position, d.Z,
                fundos.Count > 0 ? fundos.Min() : null, tubos.Count(p => p.ToId == d.Id), tubos.Count(p => p.FromId == d.Id), d.Id));
        }
        return res;
    }

    /// <summary>Blocos do CSV: perfil longitudinal por ramo e lista de dispositivos.</summary>
    public static string Csv(IReadOnlyList<LinhaPerfilRede> perfil, IReadOnlyList<DispositivoRede> dispositivos)
    {
        string N(double? v, string f = "0.000") => v is { } x && double.IsFinite(x) ? x.ToString(f, Pt) : "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PERFIL LONGITUDINAL DA REDE (por ramo, de montante para jusante; o ramo seguinte para onde encontra um ramo já listado)");
        sb.AppendLine("Ramo;Dispositivo;Distância (m);Tampa (m);Fundo de chegada (m);Fundo de saída (m);Trecho;Extensão (m);Declividade (%);Diâmetro (m);Recobrimento mínimo (m)");
        foreach (var l in perfil)
            sb.AppendLine(string.Join(";", l.Ramo, l.Dispositivo, N(l.Distancia, "0.00"), N(l.Tampa), N(l.FundoChegada), N(l.FundoSaida), l.Trecho ?? "",
                N(l.Comprimento, "0.00"), N(l.Declividade, "0.00"), N(l.Diametro, "0.00"), N(l.Recobrimento, "0.00")));
        sb.AppendLine();
        sb.AppendLine("DISPOSITIVOS DA REDE");
        sb.AppendLine("Dispositivo;Tipo;X (m);Y (m);Tampa (m);Fundo mais baixo (m);Profundidade (m);Trechos que chegam;Trechos que saem");
        foreach (var d in dispositivos)
            sb.AppendLine(string.Join(";", d.Rotulo, d.Tipo, N(d.Posicao.X, "0.00"), N(d.Posicao.Y, "0.00"), N(d.Tampa), N(d.Fundo), N(d.Profundidade, "0.00"),
                d.Entradas, d.Saidas));
        return sb.ToString();
    }
}
