using System.Globalization;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Quantities;

/// <summary>
/// Uma camada do pavimento de uma marca: de onde (via ou nó), a camada, o material, a função, a espessura, a área em planta e o
/// volume. <see cref="Camada"/> "Revestimento" é o próprio piso; as demais vêm da estrutura do pavimento da via.
/// </summary>
public sealed record CamadaQuantidade(string Origem, HierarquiaViaria? Hierarquia, int Ordem, string Camada, string? Material, string Funcao,
    double EspessuraCm, double Area, string? Fonte)
{
    public double Volume => Area * EspessuraCm / 100;
    public string HierarquiaNome => Definitions.Hierarquia.Label(Hierarquia);
}

/// <summary>
/// Quantitativo por camada do pavimento (Etapa 5; Prompt Master §10; Motores §9): para cada via e cada nó (interseção,
/// rotatória, balão), a área do pavimento medida nas peças geradas (a do modelo, quando o piso foi editado) vezes a
/// espessura do revestimento e de cada camada da estrutura. Nó usa a estrutura da via ligada mais funda. Sem estrutura
/// informada só o revestimento sai, e a marca fica na lista das que não têm estrutura. A área das camadas é a do pavimento em
/// planta (sem a sobrelargura das camadas sob a sarjeta e o meio-fio).
/// </summary>
public static class PavementLayerQuantities
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static string Revestimento(TipoPavimento t) => t switch
    {
        TipoPavimento.Bloquete => "bloquete (intertravado)",
        TipoPavimento.Concreto => "concreto",
        TipoPavimento.Terra => "leito natural / revestimento primário",
        _ => "CBUQ (asfáltico)",
    };

    /// <summary>Via que dá a estrutura ao nó: a ligada mais funda (com estrutura); sem nenhuma com estrutura, a primeira ligada.</summary>
    public static RoadPavementDefinition? ViaDoNo(MarkingDefinition no, IReadOnlyCollection<MarkingDefinition> all)
    {
        var ids = EarthworkRecord.NodeRoads(no).ToList();
        var vias = all.OfType<RoadPavementDefinition>().Where(r => ids.Contains(r.Id)).ToList();
        return vias.Where(v => v.ProfundidadeEstrutura != null).OrderByDescending(v => v.ProfundidadeEstrutura).FirstOrDefault() ?? vias.FirstOrDefault();
    }

    /// <summary>
    /// Rótulo de cada via e nó, pela ordem no projeto ("Via 2", "Interseção 1"): o código das vias (PAV-ASF…) é o mesmo para
    /// todas e não diz qual é.
    /// </summary>
    public static Dictionary<string, string> Rotulos(IEnumerable<MarkingDefinition> all)
    {
        var res = new Dictionary<string, string>();
        var n = new Dictionary<string, int>();
        foreach (var d in all.Where(d => d is RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition))
        {
            var k = d is RoadPavementDefinition ? "Via" : d.KindName;
            n[k] = n.GetValueOrDefault(k) + 1;
            res.TryAdd(d.Id, $"{k} {n[k]}");
        }
        return res;
    }

    /// <param name="items">Marcas com a geometria gerada.</param>
    /// <param name="all">Todas as definições do projeto (para achar as vias ligadas a cada nó).</param>
    /// <param name="semEstrutura">Recebe o código das vias e nós com pavimento e sem estrutura informada.</param>
    public static List<CamadaQuantidade> Compute(IEnumerable<(MarkingDefinition Def, MarkingGeometry Geo)> items, IReadOnlyCollection<MarkingDefinition> all,
        List<string>? semEstrutura = null)
    {
        var res = new List<CamadaQuantidade>();
        var rot = Rotulos(all);
        string R(MarkingDefinition d) => rot.GetValueOrDefault(d.Id, d.DisplayCode);
        foreach (var (def, geo) in items)
        {
            var via = def switch
            {
                RoadPavementDefinition r => r,
                IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition => ViaDoNo(def, all),
                _ => null,
            };
            if (via == null) continue;
            var area = ComponentQuantities.Compute(new[] { (def, geo) }).Where(r => r.Code == SectionComponent.Pavimento).Sum(r => r.Area);
            if (area < 1e-6) continue;
            var origem = def is RoadPavementDefinition ? R(def) : $"{R(def)} (pela {R(via)})";
            var e = via.Estrutura is { Camadas.Count: > 0 } x ? x : null;
            res.Add(new CamadaQuantidade(origem, def.Hierarchy, 0, "Revestimento", Revestimento(via.Material), "revestimento",
                via.ActualThickness * 100, area, e?.Fonte));
            if (e == null) { semEstrutura?.Add(origem); continue; }
            var i = 0;
            foreach (var c in e.Camadas.Where(c => double.IsFinite(c.EspessuraCm) && c.EspessuraCm > 0))
                res.Add(new CamadaQuantidade(origem, def.Hierarchy, ++i, c.Nome, c.Material, EstruturaPavimento.Nome(c.Funcao), c.EspessuraCm, area, e.Fonte));
        }
        return res;
    }

    /// <summary>Totais por camada e material (todas as vias e nós), na ordem de cima para baixo.</summary>
    public static List<(string Camada, string? Material, double Area, double Volume)> Totais(IEnumerable<CamadaQuantidade> linhas) =>
        linhas.GroupBy(l => (l.Camada, l.Material))
            .Select(g => (g.Key.Camada, g.Key.Material, Area: g.Sum(l => l.Area), Volume: g.Sum(l => l.Volume), Ordem: g.Min(l => l.Ordem)))
            .OrderBy(t => t.Ordem).ThenBy(t => t.Camada, StringComparer.OrdinalIgnoreCase)
            .Select(t => (t.Camada, t.Material, t.Area, t.Volume)).ToList();

    /// <summary>Bloco do CSV (";" e vírgula decimal).</summary>
    public static string Csv(IReadOnlyList<CamadaQuantidade> linhas, IReadOnlyList<string>? semEstrutura = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("CAMADAS DO PAVIMENTO (área do pavimento em planta × espessura; sem a sobrelargura das camadas sob sarjeta e meio-fio)");
        sb.AppendLine("Origem;Hierarquia viária;Camada;Material;Função;Espessura (cm);Área (m²);Volume (m³);Fonte da estrutura");
        foreach (var l in linhas)
            sb.AppendLine(string.Join(";", l.Origem, Hierarquia.Label(l.Hierarquia), l.Camada, l.Material ?? "", l.Funcao,
                l.EspessuraCm.ToString("0.#", Pt), l.Area.ToString("0.00", Pt), l.Volume.ToString("0.000", Pt), l.Fonte ?? ""));
        foreach (var t in Totais(linhas))
            sb.AppendLine(string.Join(";", "TOTAL", "", t.Camada, t.Material ?? "", "", "", t.Area.ToString("0.00", Pt), t.Volume.ToString("0.000", Pt), ""));
        if (semEstrutura is { Count: > 0 })
            sb.AppendLine($"Sem estrutura do pavimento informada (só o revestimento): {string.Join(", ", semEstrutura)}");
        return sb.ToString();
    }
}
