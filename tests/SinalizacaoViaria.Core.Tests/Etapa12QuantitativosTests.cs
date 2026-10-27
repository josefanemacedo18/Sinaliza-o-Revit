using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 12 do plano – central de quantitativos (Prompt de Interface §18): a unidade do consumo (o total, não a taxa) e os
/// resumos sem somar litros com quilos; a origem e o método de cada linha; o resumo por via e o de quadras e lotes; a
/// origem dos números (quando, versão, configuração, validade da terraplenagem) e as exportações com tudo isso.
/// </summary>
public class Etapa12QuantitativosTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static readonly DateTime T0 = new(2026, 10, 27, 9, 0, 0);
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private const string Tinta = "Tinta acrílica base solvente";
    private const string Termo = "Termoplástico extrudado";

    private static Polyline2 Reta(double y, double len = 100) => new(new[] { new Vec2(0, y), new Vec2(len, y) });

    /// <summary>Via de 100 m com pistas de 3,5 m e calçadas de 3 m, no eixo y.</summary>
    private static RoadPavementDefinition Via(double y) => new()
    {
        PathRef = PathReference.FromPoints(new[] { new Vec2(0, y), new Vec2(100, y) }, 100),
        LeftWidth = 3.5, RightWidth = 3.5, LeftSidewalk = 3, RightSidewalk = 3,
    };

    private static LinearMarkingDefinition Linha(string material)
    {
        var l = new LinearMarkingDefinition { Code = "LFO-1" };
        l.Output.Material = material;
        return l;
    }

    private static (MarkingDefinition, MarkingGeometry) Item(MarkingDefinition d, Polyline2? eixo = null) => (d, MarkingBuilder.Build(d, eixo, Ctx));

    // ------------------------------------------------------------------ unidade do consumo

    [Theory]
    [InlineData("L/m²", "L")]
    [InlineData("kg/m²", "kg")]
    [InlineData("m²/m²", "m²")]
    [InlineData("L/m2", "L")]
    [InlineData("L", "L")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void Consumo_UnidadeDoTotal(string? taxa, string total) => Assert.Equal(total, QuantityRow.UnidadeDoTotal(taxa));

    [Fact]
    public void Consumo_NaUnidadeDoTotal_ATaxaFicaNoMetodo()
    {
        // Antes: "6,60 L/m²" – o total (área × taxa) saía com a unidade da taxa.
        var tinta = Cat.Material(Tinta)!;
        Assert.Equal("L/m²", tinta.UnidadeConsumo);
        var r = QuantityCalculator.Compute(new[] { Item(Linha(Tinta), Reta(0)) }, Cat).Single(x => x.Code == "LFO-1");
        Assert.Equal("L", r.ConsumptionUnit);
        Assert.Equal(r.Area * tinta.Consumo, r.MaterialConsumption, 9);
        Assert.Equal($"{r.MaterialConsumption.ToString("N2", Pt)} L", r.ConsumptionText);
        Assert.Contains("consumo = área × 0,6 L/m² e microesferas = área × 0,25 kg/m² (Tinta acrílica base solvente, catálogo de materiais – estimativa)", r.Metodo);
        Assert.Equal("L", QuantityCalculator.Summary(new[] { r }).Single().ConsumptionUnit);
    }

    [Fact]
    public void Consumo_ResumosNaoSomamLitrosComQuilos()
    {
        var rows = QuantityCalculator.Compute(new[] { Item(Linha(Tinta), Reta(0)), Item(Linha(Termo), Reta(10, 50)) }, Cat);
        var l = rows.Single(r => r.Material == Tinta);
        var k = rows.Single(r => r.Material == Termo);
        Assert.Equal(("L", "kg"), (l.ConsumptionUnit, k.ConsumptionUnit));
        var esperado = $"{l.MaterialConsumption.ToString("N2", Pt)} L + {k.MaterialConsumption.ToString("N2", Pt)} kg";
        Assert.Equal(esperado, QuantityCalculator.Consumo(rows));
        var cat = QuantityCalculator.CategorySummary(rows).Single();
        Assert.Equal(esperado, cat.ConsumptionText);
        Assert.Equal((0.0, ""), (cat.MaterialConsumption, cat.ConsumptionUnit));   // nenhum total que misture unidades
        Assert.Equal(esperado, QuantityCalculator.SubcategorySummary(rows).Single().ConsumptionText);
        var h = QuantityCalculator.HierarchySummary(rows).Single();
        Assert.Equal((esperado, 0.0), (h.ConsumoTexto, h.PaintConsumption));
        // Um material só: o total e a unidade.
        var so = QuantityCalculator.CategorySummary(rows.Where(r => r.Material == Tinta)).Single();
        Assert.Equal((l.MaterialConsumption, "L", $"{l.MaterialConsumption.ToString("N2", Pt)} L"), (so.MaterialConsumption, so.ConsumptionUnit, so.ConsumptionText));
        // O CSV: o subtotal e o resumo por categoria por unidade; o consumo de cada item na unidade do total.
        var csv = QuantityCalculator.ToCsv(rows, QuantityCalculator.Summary(rows), "P").Replace("\r", "").Split('\n');
        Assert.Contains(csv, x => x.StartsWith(";SUBTOTAL;", StringComparison.Ordinal) && x.Contains(esperado));
        Assert.Contains(csv, x => x.StartsWith("1. Sinalização horizontal;", StringComparison.Ordinal) && x.EndsWith(";" + esperado));
        Assert.Equal($"{k.MaterialConsumption.ToString("N2", Pt)} kg", Campos(csv.First(x => Regex.IsMatch(x, @"^\d+;LFO-1;") && x.Contains(Termo)))[11]);
    }

    // ------------------------------------------------------------------ origem e método

    [Fact]
    public void Origem_CadaLinhaDizAsMarcasEOMetodo()
    {
        var v1 = Via(0);
        var v2 = Via(50);
        var placa = new SignDefinition { Code = "R-1", Position = new Vec2(5, 5), Direction = new Vec2(1, 0) };
        var defs = new MarkingDefinition[] { v1, v2, placa };
        var nomes = NomesDasMarcas.Montar(defs);
        Assert.Equal(("Via 1", "Via 2", "Placa R-1"), (nomes[v1.Id], nomes[v2.Id], nomes[placa.Id]));
        // Piso desenhado à mão com hierarquia (a camada Revit o lê como pavimento): elemento do usuário, não marca.
        var piso = new RoadPavementDefinition();
        var gp = new MarkingGeometry();
        gp.AreaOverrides[MarkingColor.Asfalto] = 80;
        var doUsuario = new HashSet<MarkingDefinition>(ReferenceEqualityComparer.Instance) { piso };
        var rows = QuantityCalculator.Compute(new[] { Item(v1, Reta(0)), Item(v2, Reta(50)), Item(placa), (piso, gp) }, Cat, null, nomes, doUsuario);
        var pav = rows.Single(r => r.Category == CategoriaQuantitativo.PavimentacaoGeometria && r.Color == MarkingColor.Asfalto);
        Assert.Equal(new[] { (v1.Id, "Via 1"), (v2.Id, "Via 2") }, pav.Origens.Select(o => (o.Id, o.Nome)));
        Assert.Equal(1, pav.ElementosDoUsuario);
        Assert.Equal("2 marcas do plugin: Via 1, Via 2; 1 piso(s) desenhado(s) à mão no modelo", pav.OrigemTexto);
        Assert.Equal("área em planta dos pisos no modelo (o piso editado à mão entra com a área que tem); extensão de via pelo eixo; " +
                     "pisos desenhados à mão com a hierarquia informada: área do piso medida no modelo", pav.Metodo);
        Assert.Contains("\nOrigem: 2 marcas do plugin: Via 1, Via 2", pav.DetailText);
        Assert.Contains("\nMétodo: área em planta dos pisos no modelo", pav.DetailText);
        var r1 = rows.Single(r => r.Code == "R-1");
        Assert.Equal(("1 marca do plugin: Placa R-1", 0), (r1.OrigemTexto, r1.ElementosDoUsuario));
        Assert.StartsWith("contagem de placas", r1.Metodo);
        // Sem os nomes: montados com as marcas dos itens, sem contar o piso do usuário como via.
        var sem = QuantityCalculator.Compute(new[] { (piso, gp), Item(v1, Reta(0)) }, Cat, null, null, doUsuario);
        Assert.Equal("Via 1", sem.Single(r => r.Category == CategoriaQuantitativo.PavimentacaoGeometria && r.Color == MarkingColor.Asfalto).Origens.Single().Nome);
        // Só o piso do usuário: a linha diz isso, sem método de marca.
        var soPiso = QuantityCalculator.Compute(new[] { ((MarkingDefinition)piso, gp) }, Cat, null, null, doUsuario).Single();
        Assert.Equal(("1 piso(s) desenhado(s) à mão no modelo", "pisos desenhados à mão com a hierarquia informada: área do piso medida no modelo"),
            (soPiso.OrigemTexto, soPiso.Metodo));
    }

    [Fact]
    public void Origem_MaisDeTresMarcas_EMaisN()
    {
        var linhas = Enumerable.Range(0, 5).Select(i => Item(Linha(Tinta), Reta(i * 5, 20))).ToList();
        var r = QuantityCalculator.Compute(linhas, Cat).Single();
        Assert.Equal(5, r.Origens.Count);
        Assert.Equal("5 marcas do plugin: Linear LFO-1, Linear LFO-1, Linear LFO-1 e mais 2", r.OrigemTexto);
        Assert.Equal(5, r.Origens.Select(o => o.Id).Distinct().Count());
    }

    [Fact]
    public void Origem_Terraplenagem_MarcasEFator()
    {
        var terra = new[] { new EarthworkItem("Via 1", null, 120, 40, 900, "id-1"), new EarthworkItem("Interseção 1", null, 30, 0, 200, "id-2"), new EarthworkItem("Obra", null, 0, 10, 50) };
        var rows = QuantityCalculator.AddEarthworks(Array.Empty<QuantityRow>(), terra, 1.25);
        var corte = rows.Single(r => r.Code == "TER-CORTE");
        var aterro = rows.Single(r => r.Code == "TER-ATERRO");
        Assert.Equal(new[] { "id-1", "id-2" }, corte.Origens.Select(o => o.Id));
        Assert.Equal(new[] { "id-1" }, aterro.Origens.Select(o => o.Id));   // a obra sem Id não entra na origem
        Assert.StartsWith("volume de corte entre a superfície de projeto", corte.Metodo);
        Assert.EndsWith("calculado agora, com as marcas atuais", aterro.Metodo);
        var bal = rows.Single(r => r.Code is "TER-BOTAFORA" or "TER-EMPRESTIMO");
        Assert.Equal(("TER-BOTAFORA", 87.5), (bal.Code, bal.Volume));   // 150 − 50 × 1,25
        Assert.Equal("—", bal.OrigemTexto);
        Assert.StartsWith("balanço: corte − aterro × 1,25", bal.Metodo);
    }

    [Fact]
    public void Origem_FamiliasDoUsuario()
    {
        var familias = new[]
        {
            new FamilyItem("POSTE", "Poste de luz", "Tipo A", CategoriaUrbana.Iluminacao, null),
            new FamilyItem("POSTE", "Poste de luz", "Tipo B", CategoriaUrbana.Iluminacao, null),
        };
        var r = QuantityCalculator.AddFamilies(Array.Empty<QuantityRow>(), familias).Single();
        Assert.Equal((2, "2 instância(s) de família do Revit"), (r.ElementosDoUsuario, r.OrigemTexto));
        Assert.Empty(r.Origens);
        Assert.Equal("contagem das instâncias de família do Revit classificadas em Elementos Urbanos › Famílias do Revit", r.Metodo);
    }

    // ------------------------------------------------------------------ por via, quadras e lotes

    /// <summary>A via montada como no plugin (Configurar Via): a via e as calçadas, meios-fios e sarjetas no mesmo grupo.</summary>
    private static List<(MarkingDefinition, MarkingGeometry)> ViaMontada(double y)
    {
        var s = new RoadSetup { Speed = 40, Hierarchy = HierarquiaViaria.Local };
        foreach (var lado in new[] { s.Left, s.Right })
        {
            lado.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 });
            lado.Add(new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = 3.0 });
        }
        var axis = Reta(y);
        return s.Build(PathReference.FromPoints(axis.Points, 100), new OutputSettings(), Cat, axis: axis).Select(d => Item(d, axis)).ToList();
    }

    [Fact]
    public void PorVia_ComponentesDoGrupoCamadasETerraplenagemDaVia()
    {
        var g1 = ViaMontada(0);
        var g2 = ViaMontada(60);
        var items = g1.Concat(g2).ToList();
        var defs = items.Select(i => i.Item1).ToList();
        var v1 = (RoadPavementDefinition)g1.Single(i => i.Item1 is RoadPavementDefinition).Item1;
        var sem = new List<string>();
        var camadas = PavementLayerQuantities.Compute(items, defs, sem);
        var linhas = QuantitativosPorVia.Linhas(items, NomesDasMarcas.Montar(defs), new[] { new EarthworkItem("Via 1", null, 120, 40, 900, v1.Id) }, camadas, sem);
        Assert.Equal(new[] { "Via 1", "Via 2" }, linhas.Select(l => l.Via));
        var a = linhas[0];
        // Os componentes do grupo da via – a calçada e o meio-fio são marcas próprias, criadas com ela.
        var comp = ComponentQuantities.Compute(g1);
        Assert.Equal(comp.Where(c => c.Code == SectionComponent.Pavimento).Sum(c => c.Area), a.Pavimento, 9);
        Assert.Equal(comp.Where(c => c.Code == SectionComponent.Calcada).Sum(c => c.Area), a.Calcada, 9);
        Assert.Equal(comp.Where(c => c.Code == SectionComponent.MeioFio).Sum(c => c.Length), a.MeioFio, 9);
        Assert.True(a.Pavimento > 0 && a.Calcada > 0 && a.MeioFio > 0);
        Assert.Equal(100, a.Extensao, 6);
        Assert.Equal(camadas.Where(c => c.Origem == "Via 1").Sum(c => c.Volume), a.Camadas, 9);
        Assert.True(a.Camadas > 0);
        Assert.True(a.SemEstrutura);   // sem estrutura informada: só o revestimento
        Assert.Equal((120.0, 40.0), (a.Corte!.Value, a.Aterro!.Value));
        Assert.Null(linhas[1].Corte);   // a via 2 não tem terraplenagem calculada
        // Piso do usuário não é via do plugin.
        var piso = new RoadPavementDefinition();
        Assert.DoesNotContain(QuantitativosPorVia.Linhas(items.Append((piso, new MarkingGeometry())), NomesDasMarcas.Montar(defs),
            doUsuario: new HashSet<MarkingDefinition>(ReferenceEqualityComparer.Instance) { piso }), l => l.Id == piso.Id);
        // Via sem grupo: só ela (o pavimento).
        var solta = Via(200);
        var s1 = QuantitativosPorVia.Linhas(new[] { Item(solta, Reta(200)) }, NomesDasMarcas.Montar(new[] { solta })).Single();
        Assert.Equal((700.0, 0.0, 0.0), (Math.Round(s1.Pavimento, 6), s1.Calcada, s1.MeioFio));
    }

    /// <summary>A cena da Etapa 7: gleba de 122 × 80 m, uma quadra de 102 × 60 m (testada sul), 9 lotes e o miolo como área verde.</summary>
    private static (LoteamentoDefinition Lot, QuadraDefinition Q) Loteamento()
    {
        var lot = new LoteamentoDefinition { Nome = "Jardim Teste", Gleba = new() { new(-10, -10), new(112, -10), new(112, 70), new(-10, 70) } };
        var q = LTScenes.Q("S", new List<Vec2> { new(0, 0), new(102, 0), new(102, 60), new(0, 60) }, new Vec2(51, 0));
        q.Miolo = ModoMiolo.AreaPropria;
        q.LoteamentoId = lot.Id;
        return (lot, q);
    }

    [Fact]
    public void Parcelamento_OMesmoResumoDosQuadros()
    {
        var (lot, q) = Loteamento();
        var linhas = QuantitativosParcelamento.Linhas(new MarkingDefinition[] { lot, q, Via(200) });
        var r = LotReport.Resumo(lot, new[] { q });
        Assert.All(linhas, l => Assert.Equal((lot.Id, "Jardim Teste"), (l.LoteamentoId, l.Loteamento)));
        Assert.Equal(new[] { "Gleba", "Quadras (1)" }.Concat(r.Categorias.Select(c => c.Nome)).Append(ResumoLoteamento.SistemaViario), linhas.Select(l => l.Categoria));
        Assert.Equal((9760.0, 100.0, (int?)null, ""), (Math.Round(linhas[0].Area, 2), linhas[0].Percentual, linhas[0].Quantidade, linhas[0].Tipo));
        Assert.Equal(6120.0, linhas[1].Area, 2);
        var viario = linhas.Single(l => l.Categoria == ResumoLoteamento.SistemaViario);
        Assert.Equal(3640.0, viario.Area, 2);
        Assert.Equal(37.30, viario.Percentual, 2);
        var lotes = linhas.Single(l => l.Categoria == CategoriaLote.Lote);
        Assert.Equal((r.TotalLotes, "privada"), (lotes.Quantidade!.Value, lotes.Tipo));
        Assert.Equal(9, r.TotalLotes);
        Assert.Empty(QuantitativosParcelamento.Linhas(new MarkingDefinition[] { Via(0) }));
    }

    // ------------------------------------------------------------------ origem dos números

    [Fact]
    public void OrigemDosValores_QuandoConfiguracaoEValidade()
    {
        var v1 = Via(0);
        var rows = QuantityCalculator.Compute(new[] { Item(v1, Reta(0)), Item(Linha(Tinta), Reta(20)) }, Cat);
        rows = QuantityCalculator.AddEarthworks(rows, new[] { new EarthworkItem("Via 1", null, 120, 40, 900, v1.Id) }, 1.3);
        string Texto(IReadOnlyList<EstadoResultado>? estados, DateTime? aplicada = null) => string.Join("\n",
            OrigemQuantitativos.Linhas(rows, T0, "2.18.0", "Tinta acrílica base água", 1.3, aplicada, estados).Select(l => $"[{l.Nivel}] {l.Texto}"));
        var t = Texto(null);
        Assert.Contains("[Informacao] Calculado agora, em 27/10/2026 09:00, a partir do modelo aberto (SinalizaBIM 2.18.0): nenhum número vem de valor guardado.", t);
        Assert.Contains("[Informacao] Origem: 2 marca(s) do plugin. Cada linha diz as suas marcas e o método", t);
        Assert.Contains("[Informacao] Pintura: material padrão Tinta acrílica base água (a marca com material próprio usa o dela)", t);
        Assert.Contains("balanço com fator de homogeneização 1,30 padrão do plugin (nenhuma terraplenagem aplicada).", t);
        Assert.Contains("[NaoVerificado] O estado da terraplenagem aplicada não pôde ser lido", t);
        Assert.Contains("[Informacao] Unidades: m (extensão), m² (área), m³ (volume) e un (contagem)", t);
        static EstadoResultado E(SituacaoResultado s, string d) => new("Terraplenagem", "Terreno (Toposolid) moldado pelas marcas", s, d, null, Array.Empty<string>());
        var ok = Texto(new[] { E(SituacaoResultado.Atualizado, "aplicada em 26/10/2026 09:00 com as marcas atuais.") }, T0.AddDays(-1));
        Assert.Contains("fator de homogeneização 1,30 da terraplenagem aplicada em 26/10/2026 09:00.", ok);
        Assert.Contains("[Ok] O terreno do modelo confere com estes volumes: terraplenagem aplicada em 26/10/2026 09:00 com as marcas atuais.", ok);
        Assert.Contains("[Aviso] O terreno do modelo não confere com estes volumes: 1 marca(s) diferem da terraplenagem de 26/10/2026 09:00 (V15). Rode a Terraplenagem",
            Texto(new[] { E(SituacaoResultado.Desatualizado, "1 marca(s) diferem da terraplenagem de 26/10/2026 09:00 (V15).") }, T0.AddDays(-1)));
        // Sem pintura nem terraplenagem: sem essas linhas; sem versão: "—".
        var placa = new SignDefinition { Code = "R-1", Position = Vec2.Zero, Direction = new Vec2(1, 0) };
        var so = OrigemQuantitativos.Linhas(QuantityCalculator.Compute(new[] { Item(placa) }, Cat), T0, null, null, 1.25, null, null);
        Assert.DoesNotContain(so, l => l.Texto.StartsWith("Pintura", StringComparison.Ordinal) || l.Texto.StartsWith("Terraplenagem", StringComparison.Ordinal));
        Assert.Contains("(SinalizaBIM —)", so[0].Texto);
    }

    // ------------------------------------------------------------------ exportações

    /// <summary>Campos de uma linha do CSV (";", com aspas no campo que tem ";").</summary>
    private static List<string> Campos(string linha)
    {
        var res = new List<string>();
        var sb = new System.Text.StringBuilder();
        var aspas = false;
        for (var i = 0; i < linha.Length; i++)
        {
            var c = linha[i];
            if (c == '"' && aspas && i + 1 < linha.Length && linha[i + 1] == '"') { sb.Append('"'); i++; }
            else if (c == '"') aspas = !aspas;
            else if (c == ';' && !aspas) { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        res.Add(sb.ToString());
        return res;
    }

    [Fact]
    public void Exportacoes_LevamOrigemMetodoViasELotes()
    {
        var v1 = Via(0);
        var rows = QuantityCalculator.Compute(new[] { Item(v1, Reta(0)), Item(Linha(Tinta), Reta(20)) }, Cat);
        var origem = new[] { "Calculado agora, em 27/10/2026 09:00, a partir do modelo aberto (SinalizaBIM 2.18.0)." };
        var vias = new[] { new LinhaPorVia(v1.Id, "Via 1", null, 100, 700, 600, 200, 70, true, 120, null) };
        var (lot, q) = Loteamento();
        var parc = QuantitativosParcelamento.Linhas(new MarkingDefinition[] { lot, q });
        var csv = QuantityCalculator.ToCsv(rows, null, "P", origem: origem, vias: vias, parcelamento: parc);
        var linhas = csv.Replace("\r", "").Split('\n');
        Assert.Contains(origem[0], linhas);
        Assert.All(linhas.Where(l => l.StartsWith("Item;Código;", StringComparison.Ordinal)), l => Assert.EndsWith(";Referência;Origem;Método", l));
        var itens = linhas.Where(l => Regex.IsMatch(l, @"^\d+;")).ToList();
        Assert.Equal(rows.Count, itens.Count);
        Assert.All(itens, l => Assert.Equal(16, Campos(l).Count));
        Assert.All(linhas.Where(l => l.StartsWith(";SUBTOTAL;", StringComparison.Ordinal)), l => Assert.Equal(16, Campos(l).Count));
        var pav = Campos(itens.First(l => l.Contains(";PAV-", StringComparison.Ordinal)));
        Assert.Equal("1 marca do plugin: Via 1", pav[14]);
        Assert.StartsWith("área em planta dos pisos no modelo", pav[15]);
        Assert.Contains($"Via 1;{Hierarquia.Label(null)};100,00;700,00;600,00;200,00;70,00 (só revestimento);120,00;não calculado", linhas);
        Assert.Contains("Jardim Teste;Gleba;;9760,00;100,00;", linhas);
        Assert.Contains(linhas, l => l.StartsWith("QUADRAS E LOTES (", StringComparison.Ordinal));
        // A planilha: as colunas de origem e método, o cabeçalho da origem e os blocos por via e de quadras e lotes.
        var bytes = QuantityWorkbook.Build(rows, QuantityCalculator.Summary(rows), "P", null, origem: origem, vias: vias, parcelamento: parc);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var sr = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = sr.ReadToEnd();
        foreach (var t in new[] { ">Origem<", ">Método<", ">1 marca do plugin: Via 1<", "RESUMO POR VIA", "QUADRAS E LOTES", "Calculado agora, em 27/10/2026", ">não calculado<" })
            Assert.Contains(t, sheet);
        new System.Xml.XmlDocument().LoadXml(sheet);
    }

    // ------------------------------------------------------------------ janela e comando

    private static string Raiz([CallerFilePath] string aqui = "")
    {
        foreach (var inicio in new[] { Path.GetDirectoryName(aqui), AppContext.BaseDirectory })
        {
            var dir = string.IsNullOrEmpty(inicio) ? null : new DirectoryInfo(inicio);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "SinalizacaoViaria.Revit"))) dir = dir.Parent;
            if (dir != null) return dir.FullName;
        }
        throw new DirectoryNotFoundException("raiz do repositório");
    }

    private static string Ler(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz(), "src", "SinalizacaoViaria.Revit" }.Concat(partes).ToArray()));

    [Fact]
    public void JanelaDosQuantitativos_OrigemPorViaLotesESelecao()
    {
        var xaml = Ler("UI", "QuantitiesWindow.xaml");
        foreach (var aba in new[] { "Header=\"Por via\"", "Header=\"Quadras e lotes\"", "Header=\"Origem dos valores\"" }) Assert.Contains(aba, xaml);
        Assert.Contains("Binding=\"{Binding OrigemTexto}\"", xaml);
        Assert.Contains("Content=\"Selecionar no modelo\" Click=\"SelectClick\"", xaml);
        // Os resumos mostram o consumo por unidade (texto), não um número que some litros com quilos.
        Assert.Contains("Header=\"Consumo de tinta\" Binding=\"{Binding ConsumptionText}\"", xaml);
        Assert.Contains("Header=\"Consumo de tinta\" Binding=\"{Binding ConsumoTexto}\"", xaml);
        Assert.DoesNotContain("Binding=\"{Binding PaintConsumption", xaml);
        Assert.DoesNotContain("Header=\"Consumo de tinta\" Binding=\"{Binding MaterialConsumption", xaml);
        var cs = Ler("UI", "QuantitiesWindow.xaml.cs");
        Assert.Contains("r.Origens.Any(o => o.Nome.Contains(q, StringComparison.OrdinalIgnoreCase))", cs);   // filtrar por elemento
        Assert.Equal(2, Regex.Matches(cs, @"OrigemTextos, VisibleVias, _parcelamento").Count);         // CSV e planilha
        Assert.Contains("QuantityCalculator.Consumo(com)", cs);
        var cmd = Ler("Commands", "ReportCommands.cs");
        Assert.Contains("NomesDasMarcas.Montar(defs), doUsuario)", cmd);
        Assert.Contains("doUsuario.Add(pv);", cmd);
        Assert.Contains("OrigemQuantitativos.Linhas(rows, DateTime.Now", cmd);
        Assert.Contains("uidoc.ShowElements(elems)", cmd);
        Assert.Contains("new EarthworkItem($\"{kind}{label} {count[kind]}\", d.Hierarchy, v.Cut, v.Fill, v.Area, d.Id)", Ler("Infrastructure", "TerrainService.cs"));
        // A Central de Qualidade dá às vias os mesmos nomes.
        Assert.Contains("var nomes = NomesDasMarcas.Montar(defs);", Ler("Commands", "ValidationCommands.cs"));
    }
}
