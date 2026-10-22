using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Pontas de um trecho: centros das caixas, cotas de fundo, tampas (cota de implantação) e recuos até as paredes.</summary>
public sealed record PontasTrecho(Vec2 A, Vec2 B, double InvertA, double InvertB, double RimA, double RimB, double InsetA, double InsetB,
    string CodeA, string CodeB);

/// <summary>Um trecho da rede medido: extensão (centro a centro), cotas de fundo, declividade e menor cobrimento.</summary>
public sealed record TrechoRede(string Id, string Nome, string Montante, string Jusante, double Comprimento, double FundoMontante,
    double FundoJusante, double Diametro, double CobrimentoMinimo, bool Ligacao = false)
{
    /// <summary>Declividade (%) no sentido montante → jusante; negativa = contrafluxo.</summary>
    public double Declividade => Comprimento > 1e-9 ? (FundoMontante - FundoJusante) / Comprimento * 100 : 0;
}

/// <summary>Problema encontrado na rede: geométrico (sempre conferido) ou fora de um critério informado. <paramref name="Ids"/>: as marcas envolvidas.</summary>
public sealed record ProblemaRede(string Elemento, string Criterio, string Texto, bool PorLimite, IReadOnlyList<string>? Ids = null);

/// <summary>
/// Geometria da rede de drenagem (F8): os trechos (<see cref="DrainagePipeDefinition"/>) ligam os dispositivos (PV, bocas de
/// lobo, grelhas com caixa). Mede extensão, cotas de fundo, declividade e cobrimento; confere o sentido do escoamento, ligações
/// quebradas e ciclos (sempre) e os critérios geométricos do <see cref="ProjetoDrenagem"/> (declividade, recobrimento,
/// diâmetros, espaçamento e queda nos PVs, alinhamento pela geratriz superior). A vazão é de <see cref="DrainageHydraulics"/>.
/// </summary>
public static class DrainageNetwork
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Espessura da parede do tubo de concreto adotada no desenho (m) – a mesma do tubo de saída das caixas.</summary>
    public static double Wall(double diameter) => Math.Max(0.04, Math.Clamp(diameter, 0.1, 3.0) / 2 * 0.16);

    /// <summary>Pontas do trecho pelas caixas dos dispositivos; nulo se faltar um deles (ou for uma canaleta).</summary>
    public static PontasTrecho? Ends(DrainagePipeDefinition p, Func<string, MarkingDefinition?>? lookup)
    {
        if (lookup?.Invoke(p.FromId) is not DrainageDefinition a || lookup(p.ToId) is not DrainageDefinition b) return null;
        if (DrainageGenerator.NetworkBox(a) is not { } ba || DrainageGenerator.NetworkBox(b) is not { } bb) return null;
        return new PontasTrecho(ba.Center, bb.Center, p.InvertFrom ?? a.Z - ba.InvertDepth, p.InvertTo ?? b.Z - bb.InvertDepth, a.Z, b.Z,
            ba.Inset, bb.Inset, a.DisplayCode, b.DisplayCode);
    }

    /// <summary>Rótulos dos dispositivos da rede ("PV-1", "BL-S-2"...), na ordem em que aparecem.</summary>
    public static Dictionary<string, string> Labels(IEnumerable<MarkingDefinition> defs)
    {
        var res = new Dictionary<string, string>();
        var n = 0;
        foreach (var d in defs.OfType<DrainageDefinition>().Where(d => !d.IsLinear)) res[d.Id] = $"{d.DisplayCode}-{++n}";
        return res;
    }

    /// <summary>
    /// Trechos de montante para jusante: um trecho vem depois de todos os que chegam ao seu dispositivo de montante (ordem
    /// topológica; empates na ordem dos dispositivos e dos trechos). Trechos em ciclo ou de um dispositivo para ele mesmo vão
    /// no fim, na ordem do projeto.
    /// </summary>
    public static List<DrainagePipeDefinition> PipeOrder(IEnumerable<MarkingDefinition> defs)
    {
        var list = defs.ToList();
        var pipes = list.OfType<DrainagePipeDefinition>().ToList();
        var valid = pipes.Where(p => p.FromId != p.ToId).ToList();
        var indeg = new Dictionary<string, int>();
        foreach (var p in valid) indeg[p.ToId] = indeg.GetValueOrDefault(p.ToId) + 1;
        var outs = valid.GroupBy(p => p.FromId).ToDictionary(g => g.Key, g => g.ToList());
        var nodes = list.OfType<DrainageDefinition>().Select(d => d.Id).Concat(valid.SelectMany(p => new[] { p.FromId, p.ToId })).Distinct().ToList();
        var queue = new Queue<string>(nodes.Where(n => indeg.GetValueOrDefault(n) == 0));
        var res = new List<DrainagePipeDefinition>();
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            foreach (var p in outs.GetValueOrDefault(u) ?? new List<DrainagePipeDefinition>())
            {
                res.Add(p);
                indeg[p.ToId]--;
                if (indeg[p.ToId] == 0) queue.Enqueue(p.ToId);
            }
        }
        var seen = res.Select(p => p.Id).ToHashSet();
        res.AddRange(pipes.Where(p => !seen.Contains(p.Id)));
        return res;
    }

    /// <summary>Nomes dos trechos ("T1", "T2"...) na ordem de montante para jusante.</summary>
    public static Dictionary<string, string> PipeNames(IEnumerable<MarkingDefinition> defs) =>
        PipeOrder(defs).Select((p, k) => (p.Id, Nome: $"T{k + 1}")).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Nome);

    /// <summary>
    /// Trechos medidos, de montante para jusante. O cobrimento é a superfície menos a geratriz superior externa, amostrado a
    /// cada metro; sem <paramref name="surface"/>, a superfície é interpolada entre as tampas dos dois dispositivos.
    /// </summary>
    public static List<TrechoRede> Analyze(IReadOnlyCollection<MarkingDefinition> defs, Func<Vec2, double?>? surface = null)
    {
        var byId = defs.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var labels = Labels(defs);
        var names = PipeNames(defs);
        var res = new List<TrechoRede>();
        foreach (var p in PipeOrder(defs))
        {
            if (Ends(p, id => byId.GetValueOrDefault(id)) is not { } e) continue;
            var len = e.A.DistanceTo(e.B);
            var top = Math.Clamp(p.Diameter, 0.1, 3.0) + Wall(p.Diameter);
            var cover = double.MaxValue;
            var n = Math.Max(1, (int)Math.Ceiling(len));
            for (int i = 0; i <= n; i++)
            {
                var t = (double)i / n;
                var q = e.A + (e.B - e.A) * t;
                var z = surface?.Invoke(q) ?? e.RimA + (e.RimB - e.RimA) * t;
                cover = Math.Min(cover, z - (e.InvertA + (e.InvertB - e.InvertA) * t + top));
            }
            var (ma, ju) = (labels.GetValueOrDefault(p.FromId, e.CodeA), labels.GetValueOrDefault(p.ToId, e.CodeB));
            var ligacao = byId.GetValueOrDefault(p.FromId) is DrainageDefinition { IsInlet: true };
            res.Add(new TrechoRede(p.Id, names[p.Id], ma, ju, len, e.InvertA, e.InvertB, p.Diameter, cover, ligacao));
        }
        return res;
    }

    /// <summary>
    /// Problemas da geometria da rede. Sempre: trecho com dispositivo inexistente, trecho de um dispositivo para ele mesmo,
    /// contrafluxo e ciclo. Com o <see cref="ProjetoDrenagem"/>: declividade, recobrimento e diâmetros mínimos, espaçamento entre
    /// PVs (tabela 6.1), PV de queda e alinhamento pela geratriz superior na mudança de diâmetro (Manual SUDERHSA, fig. 6.2).
    /// </summary>
    public static List<ProblemaRede> Check(IReadOnlyCollection<MarkingDefinition> defs, ProjetoDrenagem? c, Func<Vec2, double?>? surface = null)
    {
        var res = new List<ProblemaRede>();
        var byId = defs.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var names = PipeNames(defs);
        foreach (var p in PipeOrder(defs))
        {
            var nome = names[p.Id];
            if (p.FromId == p.ToId) res.Add(new(nome, "ligação", $"{nome}: o trecho começa e termina no mesmo dispositivo.", false, new[] { p.Id }));
            else if (Ends(p, id => byId.GetValueOrDefault(id)) == null)
                res.Add(new(nome, "dispositivo", $"{nome}: um dispositivo da ponta não existe mais ou não é caixa (canaleta).", false, new[] { p.Id }));
        }
        var trechos = Analyze(defs, surface);
        foreach (var t in trechos)
        {
            var id = $"{t.Nome} ({t.Montante} → {t.Jusante})";
            var ids = new[] { t.Id };
            if (t.FundoJusante > t.FundoMontante + 1e-6)
                res.Add(new(id, "sentido", $"{id}: fundo de jusante {N(t.FundoJusante)} m acima do de montante {N(t.FundoMontante)} m (contrafluxo).", false, ids));
            if (c == null) continue;
            var dec = t.Declividade;
            var subindo = t.FundoJusante > t.FundoMontante + 1e-6;
            var dmin = t.Ligacao ? c.DeclividadeMinimaLigacao : c.DeclividadeMinima;
            if (dmin is { } d0 && dec < d0 - 1e-9 && !subindo)
                res.Add(new(id, "declividade", $"{id}: declividade {N(dec)} % – mínimo de {N(d0)} %{(t.Ligacao ? " nas ligações" : "")}.", true, ids));
            if (!t.Ligacao && c.DeclividadeMaxima is { } dmax && dec > dmax + 1e-9)
                res.Add(new(id, "declividade", $"{id}: declividade {N(dec)} % – máximo de {N(dmax)} %.", true, ids));
            if (c.RecobrimentoMinimo is { } cmin && t.CobrimentoMinimo < cmin - 1e-9)
                res.Add(new(id, "cobrimento", $"{id}: recobrimento {N(t.CobrimentoMinimo)} m – mínimo de {N(cmin)} m sobre a geratriz superior externa.", true, ids));
            var diamMin = t.Ligacao ? c.DiametroLigacao : c.DiametroMinimo;
            if (diamMin is { } dm && t.Diametro < dm - 1e-9)
                res.Add(new(id, "diâmetro", $"{id}: diâmetro {N(t.Diametro * 1000, "0")} mm – mínimo de {N(dm * 1000, "0")} mm{(t.Ligacao ? " nas ligações" : " nas galerias")}.", true, ids));
            if (c.EspacamentoPv && !t.Ligacao && t.Comprimento > Hidraulica.EspacamentoPv(t.Diametro) + 1e-6)
                res.Add(new(id, "espaçamento", $"{id}: {N(t.Comprimento, "0.0")} m entre dispositivos – máximo de {N(Hidraulica.EspacamentoPv(t.Diametro), "0")} m para Ø {N(t.Diametro * 1000, "0")} mm (tabela 6.1).", true, ids));
        }
        if (c != null) Nodes(defs, trechos, c, res);

        // Ciclos (a água voltaria ao ponto de partida): busca em profundidade no grafo montante → jusante.
        var pipes = defs.OfType<DrainagePipeDefinition>().ToList();
        var next = pipes.Where(p => p.FromId != p.ToId).GroupBy(p => p.FromId).ToDictionary(g => g.Key, g => g.Select(p => p.ToId).ToList());
        var labels = Labels(defs);
        var state = new Dictionary<string, int>();
        var stack = new List<string>();
        void Dfs(string u)
        {
            state[u] = 1;
            stack.Add(u);
            foreach (var v in next.GetValueOrDefault(u) ?? new List<string>())
            {
                if (state.GetValueOrDefault(v) == 1)
                {
                    var ring = stack.Skip(stack.IndexOf(v)).ToList();
                    var cyc = ring.Append(v).Select(x => labels.GetValueOrDefault(x, x)).ToList();
                    res.Add(new("rede", "ciclo", $"Ciclo na rede: {string.Join(" → ", cyc)} – a água voltaria ao ponto de partida.", false, ring));
                }
                else if (state.GetValueOrDefault(v) == 0) Dfs(v);
            }
            stack.RemoveAt(stack.Count - 1);
            state[u] = 2;
        }
        // Na ordem dos dispositivos (relatório estável: o ciclo começa no primeiro deles).
        foreach (var u in defs.OfType<DrainageDefinition>().Select(d => d.Id).Concat(next.Keys).Distinct())
            if (next.ContainsKey(u) && state.GetValueOrDefault(u) == 0) Dfs(u);
        return res;
    }

    /// <summary>Nos dispositivos: PV de queda e geratriz superior do tubo que sai acima da do que chega (diâmetro maior).</summary>
    private static void Nodes(IReadOnlyCollection<MarkingDefinition> defs, List<TrechoRede> trechos, ProjetoDrenagem c, List<ProblemaRede> res)
    {
        var porId = trechos.ToDictionary(t => t.Id);
        var pipes = defs.OfType<DrainagePipeDefinition>().Where(p => porId.ContainsKey(p.Id)).ToList();
        var labels = Labels(defs);
        foreach (var g in pipes.GroupBy(p => p.ToId))
        {
            var saem = pipes.Where(p => p.FromId == g.Key).Select(p => porId[p.Id]).ToList();
            if (saem.Count == 0) continue;
            var nome = labels.GetValueOrDefault(g.Key, "dispositivo");
            foreach (var chega in g.Select(p => porId[p.Id]))
                foreach (var sai in saem)
                {
                    var queda = chega.FundoJusante - sai.FundoMontante;
                    if (c.QuedaPv is { } qmax && queda > qmax + 1e-6)
                        res.Add(new(nome, "queda", $"{nome}: {N(queda)} m entre o fundo de {chega.Nome} e o de {sai.Nome} – PV de queda (acima de {N(qmax)} m).", true,
                            new[] { g.Key }));
                    var topoChega = chega.FundoJusante + chega.Diametro;
                    var topoSai = sai.FundoMontante + sai.Diametro;
                    if (!chega.Ligacao && sai.Diametro > chega.Diametro + 1e-6 && topoSai > topoChega + 0.01)
                        res.Add(new(nome, "alinhamento", $"{nome}: o tubo que sai ({sai.Nome}, Ø {N(sai.Diametro * 1000, "0")} mm) tem a geratriz superior " +
                            $"{N(topoSai - topoChega)} m acima da do que chega ({chega.Nome}, Ø {N(chega.Diametro * 1000, "0")} mm) – alinhe pela geratriz superior.", true,
                            new[] { g.Key, sai.Id }));
                }
        }
    }

    private static string N(double v, string fmt = "0.00") => v.ToString(fmt, Pt);

    /// <summary>Relatório CSV (";" e vírgula decimal): a fonte dos critérios, os trechos medidos e os problemas.</summary>
    public static string Csv(IReadOnlyList<TrechoRede> trechos, IReadOnlyList<ProblemaRede> problemas, ProjetoDrenagem? c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("REDE DE DRENAGEM – geometria");
        sb.AppendLine(c == null
            ? "Critérios: não informados (só sentido, ligações e ciclos foram conferidos)"
            : $"Critérios: {c.FonteTexto.Replace(';', ',')}");
        sb.AppendLine("Trecho;Montante;Jusante;Tipo;Extensão (m);Fundo montante (m);Fundo jusante (m);Declividade (%);Diâmetro (mm);Menor recobrimento (m)");
        foreach (var t in trechos)
            sb.AppendLine(string.Join(";", t.Nome, t.Montante, t.Jusante, t.Ligacao ? "ligação" : "galeria", N(t.Comprimento), N(t.FundoMontante, "0.000"),
                N(t.FundoJusante, "0.000"), N(t.Declividade, "0.000"), N(t.Diametro * 1000, "0"), N(t.CobrimentoMinimo)));
        sb.AppendLine();
        sb.AppendLine("Problemas;Critério;Descrição");
        if (problemas.Count == 0) sb.AppendLine("nenhum;;");
        foreach (var p in problemas) sb.AppendLine(string.Join(";", p.Elemento.Replace(';', ','), p.Criterio, p.Texto.Replace(';', ',')));
        sb.AppendLine("Recobrimento: superfície menos a geratriz superior externa do tubo, a cada metro; sem superfície, interpolada entre as tampas.");
        return sb.ToString();
    }
}
