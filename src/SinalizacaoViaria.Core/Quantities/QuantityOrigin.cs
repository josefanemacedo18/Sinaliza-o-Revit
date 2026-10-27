using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Core.Quantities;

/// <summary>Uma marca do plugin na origem de uma linha do quantitativo: o Id (para selecionar no modelo) e o nome.</summary>
public sealed record OrigemQuantidade(string Id, string Nome);

/// <summary>
/// De onde vêm os números do quantitativo (Etapa 12; Prompt de Interface §18.1): quando foram calculados e com que versão, de
/// quantas marcas e elementos do usuário, a configuração usada (material de pintura, taxas, fator de homogeneização), o estado
/// de validade da terraplenagem e as unidades. Os quantitativos são calculados na hora a partir do modelo aberto – não há
/// valor guardado que possa ficar velho –, mas o terreno do modelo pode não acompanhar o projeto (terraplenagem não aplicada).
/// </summary>
public static class OrigemQuantitativos
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="materialPadrao">Material de pintura padrão das Configurações (nulo = o primeiro do catálogo).</param>
    /// <param name="homogeneizacao">Fator usado no balanço de massas.</param>
    /// <param name="terraplenagemAplicada">Data da última terraplenagem aplicada (de onde vem o fator); nula = nenhuma, fator padrão.</param>
    /// <param name="estados">Estado dos resultados (<see cref="EstadoDosResultados"/>); nulo = não calculado.</param>
    public static List<EstadoContexto> Linhas(IReadOnlyList<QuantityRow> rows, DateTime quando, string? versao, string? materialPadrao,
        double homogeneizacao, DateTime? terraplenagemAplicada, IReadOnlyList<EstadoResultado>? estados)
    {
        var res = new List<EstadoContexto>
        {
            new(NivelSituacao.Informacao, $"Calculado agora, em {quando.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}, a partir do modelo aberto " +
                                          $"(SinalizaBIM {versao ?? "—"}): nenhum número vem de valor guardado. Depois de mudar o projeto, abra os Quantitativos de novo."),
        };
        var marcas = rows.SelectMany(r => r.Origens.Select(o => o.Id)).Distinct().Count();
        var pisos = rows.Where(r => !r.IsFamily).Sum(r => r.ElementosDoUsuario);
        var familias = rows.Where(r => r.IsFamily).Sum(r => r.ElementosDoUsuario);
        res.Add(new(NivelSituacao.Informacao, $"Origem: {marcas} marca(s) do plugin" +
                                              (pisos > 0 ? $", {pisos} piso(s) desenhado(s) à mão com hierarquia" : "") +
                                              (familias > 0 ? $", {familias} instância(s) de família do Revit classificadas" : "") +
                                              ". Cada linha diz as suas marcas e o método (escolha a linha); \"Selecionar no modelo\" mostra os elementos."));
        if (rows.Any(r => r.MaterialConsumption > 1e-6))
            res.Add(new(NivelSituacao.Informacao, $"Pintura: material padrão {materialPadrao ?? "o primeiro do catálogo"} (a marca com material próprio usa o dela); " +
                                                  "consumo e microesferas = área pintada × taxas do catálogo de materiais – estimativa, a conferir com a especificação do fabricante."));
        if (rows.Any(r => r.Category == CategoriaQuantitativo.ObrasDeArteTerraplenagem && r.Unit == "m³"))
        {
            res.Add(new(NivelSituacao.Informacao, "Terraplenagem: volumes do projeto atual contra o terreno original guardado no Toposolid, pela malha; " +
                                                  $"balanço com fator de homogeneização {homogeneizacao.ToString("0.00", Pt)} " +
                                                  (terraplenagemAplicada is { } t ? $"da terraplenagem aplicada em {t.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}."
                                                      : "padrão do plugin (nenhuma terraplenagem aplicada).")));
            var e = estados?.FirstOrDefault(x => x.Modulo == "Terraplenagem");
            res.Add(e == null
                ? new(NivelSituacao.NaoVerificado, "O estado da terraplenagem aplicada não pôde ser lido: não se sabe se o terreno do modelo confere com estes volumes.")
                : e.Situacao switch
                {
                    SituacaoResultado.Atualizado => new(NivelSituacao.Ok, $"O terreno do modelo confere com estes volumes: terraplenagem {e.Detalhe}"),
                    SituacaoResultado.Desatualizado => new(NivelSituacao.Aviso, $"O terreno do modelo não confere com estes volumes: {e.Detalhe} " +
                                                                               "Rode a Terraplenagem para o Toposolid acompanhar o projeto."),
                    _ => new(NivelSituacao.NaoVerificado, $"Terreno do modelo × estes volumes: {e.Detalhe}"),
                });
        }
        res.Add(new(NivelSituacao.Informacao, "Unidades: m (extensão), m² (área), m³ (volume) e un (contagem); o consumo vai na unidade do material " +
                                              "(L, kg ou m²) e as microesferas em kg. Os valores não são arredondados no cálculo: a tela e o CSV mostram 2 casas, a planilha 4."));
        return res;
    }
}

/// <summary>Uma via no resumo por via: extensão do eixo, áreas dos componentes da seção, camadas do pavimento, corte e aterro.</summary>
/// <param name="Camadas">Volume do revestimento e das camadas da estrutura (m³); <paramref name="SemEstrutura"/> = só o revestimento.</param>
/// <param name="Corte">Corte (m³) da parte da via na terraplenagem; nulo = não calculado (sem Toposolid ou a via não molda o terreno).</param>
public sealed record LinhaPorVia(string Id, string Via, HierarquiaViaria? Hierarquia, double Extensao, double Pavimento, double Calcada, double MeioFio,
    double Camadas, bool SemEstrutura, double? Corte, double? Aterro)
{
    public string HierarquiaNome => Definitions.Hierarquia.Label(Hierarquia);
}

/// <summary>
/// Resumo por via (Prompt de Interface §18: resultados organizados por vias): cada via com a extensão do eixo, o pavimento, a
/// calçada e o meio-fio – os mesmos números do resumo por componente, só do grupo da via (a via e as calçadas, meios-fios e
/// sarjetas criados com ela, que são marcas próprias do mesmo grupo) –, o volume das camadas do pavimento e o corte e o
/// aterro da parte dela na terraplenagem. Interseções e rotatórias ficam no resumo por componente e nas camadas.
/// </summary>
public static class QuantitativosPorVia
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="nomes">Nome das marcas (<see cref="NomesDasMarcas"/>).</param>
    /// <param name="terra">Terraplenagem por elemento (com o Id da marca); nula ou vazia = não calculada.</param>
    /// <param name="camadas">Camadas do pavimento (<see cref="PavementLayerQuantities"/>), pelo rótulo da via.</param>
    /// <param name="semEstrutura">Rótulos das marcas sem estrutura do pavimento informada.</param>
    public static List<LinhaPorVia> Linhas(IEnumerable<(MarkingDefinition Def, MarkingGeometry Geo)> items, IReadOnlyDictionary<string, string> nomes,
        IEnumerable<EarthworkItem>? terra = null, IEnumerable<CamadaQuantidade>? camadas = null, IEnumerable<string>? semEstrutura = null,
        IReadOnlySet<MarkingDefinition>? doUsuario = null)
    {
        var porId = (terra ?? Enumerable.Empty<EarthworkItem>()).Where(i => i.Id != null).GroupBy(i => i.Id!).ToDictionary(g => g.Key, g => (g.Sum(i => i.Cut), g.Sum(i => i.Fill)));
        var cam = (camadas ?? Enumerable.Empty<CamadaQuantidade>()).GroupBy(c => c.Origem).ToDictionary(g => g.Key, g => g.Sum(c => c.Volume));
        var sem = (semEstrutura ?? Enumerable.Empty<string>()).ToHashSet();
        var lista = items.Where(i => doUsuario?.Contains(i.Def) != true).ToList();
        var grupos = lista.Where(i => !string.IsNullOrEmpty(i.Def.GroupId)).ToLookup(i => i.Def.GroupId!);
        var res = new List<LinhaPorVia>();
        foreach (var (def, geo) in lista)
        {
            if (def is not RoadPavementDefinition) continue;
            // O grupo da via (calçadas, meios-fios e sarjetas criados com ela); grupo com mais de uma via = só a própria via.
            var grupo = string.IsNullOrEmpty(def.GroupId) ? null : grupos[def.GroupId!].ToList();
            if (grupo == null || grupo.Count(i => i.Def is RoadPavementDefinition) != 1) grupo = new() { (def, geo) };
            var comp = ComponentQuantities.Compute(grupo);
            double A(string c) => comp.Where(r => r.Code == c).Sum(r => r.Area);
            var nome = nomes.TryGetValue(def.Id, out var n) ? n : $"{def.KindName} {def.DisplayCode}";
            var t = porId.TryGetValue(def.Id, out var v) ? v : ((double, double)?)null;
            res.Add(new LinhaPorVia(def.Id, nome, def.Hierarchy, geo.PathLength, A(SectionComponent.Pavimento), A(SectionComponent.Calcada),
                comp.Where(r => r.Code == SectionComponent.MeioFio).Sum(r => r.Length), cam.GetValueOrDefault(nome), sem.Contains(nome), t?.Item1, t?.Item2));
        }
        return res;
    }

    /// <summary>Bloco do CSV (";" e vírgula decimal).</summary>
    public static string Csv(IEnumerable<LinhaPorVia> linhas)
    {
        string N(double v) => v.ToString("0.00", Pt);
        var sb = new StringBuilder();
        sb.AppendLine("RESUMO POR VIA (eixo, componentes da seção medidos nas peças, camadas do pavimento e terraplenagem da parte da via)");
        sb.AppendLine("Via;Hierarquia viária;Extensão do eixo (m);Pavimento (m²);Calçada (m²);Meio-fio (m);Camadas do pavimento (m³);Corte (m³);Aterro (m³)");
        foreach (var l in linhas)
            sb.AppendLine(string.Join(";", l.Via, l.HierarquiaNome, N(l.Extensao), N(l.Pavimento), N(l.Calcada), N(l.MeioFio),
                N(l.Camadas) + (l.SemEstrutura ? " (só revestimento)" : ""), l.Corte is { } c ? N(c) : "não calculado", l.Aterro is { } a ? N(a) : "não calculado"));
        return sb.ToString();
    }
}

/// <summary>Uma linha das quadras e lotes de um loteamento: categoria, quantidade, área e percentual da gleba.</summary>
/// <param name="Quantidade">Parcelas da categoria; nulo nas linhas de área (gleba, quadras, sistema viário).</param>
public sealed record LinhaParcelamento(string LoteamentoId, string Loteamento, string Categoria, int? Quantidade, double Area, double Percentual, bool Publica)
{
    /// <summary>"pública" ou "privada" nas categorias de parcela; vazio nas linhas de área.</summary>
    public string Tipo => Quantidade == null ? "" : Publica ? "pública" : "privada";
}

/// <summary>
/// Quadras e lotes no quantitativo (Prompt de Interface §18: organizados por quadras e lotes): o mesmo resumo dos Quadros e do
/// memorial do Loteamento (<see cref="LotReport.Resumo"/>), por loteamento – gleba, quadras, cada categoria de parcela e o
/// sistema viário (gleba − quadras − sobra em volta das vias). Os lotes não entram no quadro por marca: têm este bloco próprio.
/// </summary>
public static class QuantitativosParcelamento
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public const string Metodo = "divisão das quadras de cada loteamento – o mesmo resumo dos Quadros e do memorial do Loteamento; " +
                                 "sistema viário = gleba − quadras − sobra em volta das vias";

    public static List<LinhaParcelamento> Linhas(IEnumerable<MarkingDefinition> defs)
    {
        var lista = defs.ToList();
        var quadras = lista.OfType<QuadraDefinition>().ToList();
        var res = new List<LinhaParcelamento>();
        foreach (var lot in lista.OfType<LoteamentoDefinition>().OrderBy(l => l.Nome, StringComparer.Create(Pt, true)))
        {
            var r = LotReport.Resumo(lot, quadras);
            res.Add(new(lot.Id, lot.Nome, "Gleba", null, r.AreaGleba, r.AreaGleba > 1e-9 ? 100 : 0, false));
            res.Add(new(lot.Id, lot.Nome, $"Quadras ({r.Quadras.Count})", null, r.AreaQuadras, r.Pct(r.AreaQuadras), false));
            foreach (var c in r.Categorias) res.Add(new(lot.Id, lot.Nome, c.Nome, c.Quantidade, c.Area, c.Percentual, c.Publica));
            if (lot.GlebaPolygon != null) res.Add(new(lot.Id, lot.Nome, ResumoLoteamento.SistemaViario, null, r.AreaViaria, r.Pct(r.AreaViaria), true));
        }
        return res;
    }

    /// <summary>Bloco do CSV (";" e vírgula decimal).</summary>
    public static string Csv(IEnumerable<LinhaParcelamento> linhas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("QUADRAS E LOTES (" + Metodo + ")");
        sb.AppendLine("Loteamento;Categoria;Quantidade (un);Área (m²);% da gleba;Tipo");
        foreach (var l in linhas)
            sb.AppendLine(string.Join(";", l.Loteamento.Replace(';', ','), l.Categoria.Replace(';', ','), l.Quantidade?.ToString(Pt) ?? "",
                l.Area.ToString("0.00", Pt), l.Percentual.ToString("0.00", Pt), l.Tipo));
        return sb.ToString();
    }
}
