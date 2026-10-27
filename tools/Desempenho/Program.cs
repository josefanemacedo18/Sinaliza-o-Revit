using System.Diagnostics;
using System.Globalization;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

// Medição de desempenho do núcleo (Etapa 13; Prompt de Motores §18, Prompt de Interface §21): o que o núcleo gasta, sem o
// Revit, em projetos sintéticos de três tamanhos (vias paralelas de 200 m com faixas, calçadas, meios-fios e sarjetas,
// como o Sinalizar Via as cria). Cada tempo é a mediana de 3 rodadas depois de uma de aquecimento. O crescimento entre
// tamanhos (×4 marcas) diz se a operação é linear (≈4×) ou pior (≈16× = quadrática). A criação dos elementos no Revit,
// a gravação da transação e a varredura das marcas no documento NÃO estão aqui: são medidas pelo Autoteste.

var pt = CultureInfo.GetCultureInfo("pt-BR");
var cat = CatalogService.LoadDefault();
var ctx = new BuildContext { Catalog = cat };

static double Mediana(Action a)
{
    a();
    var t = new List<double>();
    for (var i = 0; i < 3; i++)
    {
        var sw = Stopwatch.StartNew();
        a();
        t.Add(sw.Elapsed.TotalMilliseconds);
    }
    t.Sort();
    return t[1];
}

string Ms(double ms) => ms >= 1000 ? (ms / 1000).ToString("0.00", pt) + " s" : ms.ToString("0.0", pt) + " ms";

Console.WriteLine("SINALIZABIM – DESEMPENHO DO NÚCLEO (sem o Revit)");
Console.WriteLine($"Data: {DateTime.Now:dd/MM/yyyy HH:mm}   .NET {Environment.Version}   {Environment.ProcessorCount} processador(es)   {Environment.OSVersion}");
Console.WriteLine("Mediana de 3 rodadas depois de uma de aquecimento. O Revit (criação de elementos, transação, leitura das marcas no");
Console.WriteLine("documento) não entra: é medido pelo Autoteste (tempo por trecho).");
Console.WriteLine();

// ---------------------------------------------------------------- K14: piso tátil de alerta longo
Console.WriteLine("K14 – PISO TÁTIL DE ALERTA (PTA) AO LONGO DE UMA LINHA");
foreach (var len in new[] { 6.0, 30.0 })
{
    var pta = new LinearMarkingDefinition { Code = "PTA" };
    var linha = new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) });
    MarkingGeometry? g = null;
    var ms = Mediana(() => g = MarkingBuilder.Build(pta, linha, ctx));
    Console.WriteLine($"  {len.ToString("0", pt)} m: geometria do núcleo {Ms(ms)}; {g!.Pieces.Count.ToString("N0", pt)} peça(s), " +
                      $"{g.Pieces.Count(p => p.Solid != null).ToString("N0", pt)} sólido(s), {g.UnitCount.ToString("N0", pt)} unidade(s)");
}
Console.WriteLine("  No 3º Autoteste completo, a etapa com PTA de 30 m levou 171 s no Revit; a diferença para o tempo acima é a criação");
Console.WriteLine("  de um sólido por relevo no Revit.");
Console.WriteLine();

// ---------------------------------------------------------------- projetos de três tamanhos
static List<MarkingDefinition> Projeto(int vias, Catalogo cat)
{
    var defs = new List<MarkingDefinition>();
    for (var i = 0; i < vias; i++)
    {
        var s = new RoadSetup { Speed = 40, Hierarchy = HierarquiaViaria.Local };
        foreach (var lado in new[] { s.Left, s.Right })
        {
            lado.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 });
            lado.Add(new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = 3.0 });
        }
        var y = i * 40.0;
        var eixo = new Polyline2(new[] { new Vec2(0, y), new Vec2(200, y) });
        var marcas = s.Build(PathReference.FromPoints(eixo.Points, 100), new OutputSettings(), cat, axis: eixo);
        foreach (var r in marcas.OfType<RoadPavementDefinition>())
        {
            r.Output.Grade = new RoadGrade { DefaultCurve = 60, AdjustTerrain = true };
            r.Output.Grade.Points.Add(new GradePoint(0, 2));
            r.Output.Grade.Points.Add(new GradePoint(200, 4));
        }
        defs.AddRange(marcas);
    }
    return defs;
}

static Polyline2 EixoDe(MarkingDefinition d) =>
    d.Path?.Points is { Count: > 1 } p ? new Polyline2(p) : new Polyline2(new[] { Vec2.Zero, new Vec2(200, 0) });

var linhas = new List<(string Operacao, int Vias, int Marcas, double Ms)>();
foreach (var n in new[] { 4, 16, 64 })
{
    var defs = Projeto(n, cat);
    var items = new List<(MarkingDefinition, MarkingGeometry)>();
    void Med(string op, Action a) => linhas.Add((op, n, defs.Count, Mediana(a)));
    Med("Geometria de todas as marcas (BuildGeometry)", () =>
    {
        items.Clear();
        foreach (var d in defs) items.Add((d, MarkingBuilder.Build(d, EixoDe(d), ctx)));
    });
    var json = new List<string>();
    Med("Gravar as definições em JSON", () => { json.Clear(); json.AddRange(defs.Select(d => d.ToJson())); });
    Med("Ler as definições do JSON", () => { foreach (var j in json) MarkingDefinition.FromJson(j); });
    var nomes = NomesDasMarcas.Montar(defs);
    Med("Nomes das marcas", () => NomesDasMarcas.Montar(defs));
    List<QuantityRow> rows = new();
    Med("Quantitativo por item (com a origem)", () => rows = QuantityCalculator.Compute(items, cat, null, nomes));
    Med("Resumo por componente da seção", () => ComponentQuantities.Compute(items));
    var camadas = new List<CamadaQuantidade>();
    Med("Camadas do pavimento", () => camadas = PavementLayerQuantities.Compute(items, defs));
    Med("Resumo por via", () => QuantitativosPorVia.Linhas(items, nomes, null, camadas));
    Med("CSV do quantitativo", () => QuantityCalculator.ToCsv(rows, QuantityCalculator.Summary(rows), "P"));
    var copias = defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e")).ToList();
    List<Achado> achados = new();
    Med("Validar Modelo (V01–V17, sem terreno)", () => achados = ModelValidator.Validate(copias, _ => true, incluirNaoVerificados: true));
    Med("Central de Qualidade (linhas e contagens)", () => CentralQualidade.Montar(achados, null, id => nomes.GetValueOrDefault(id), "P", DateTime.Now, "x", n, n));
    var registro = EarthworkRecord.Gravar(EarthworkRecord.Participantes(defs), 0.4, 1, 1.5, DateTime.Now, "x");
    Med("Estado dos resultados (terraplenagem e captações)", () => EstadoDosResultados.Calcular(defs, registro));
    Med("Central do Projeto (indicadores)", () => CentralDoProjeto.Resumir(defs, registro, null, false));
    var corredores = defs.OfType<RoadPavementDefinition>().Select(r =>
    {
        var c = new GradeCorridor { CutSlope = 1.0, FillSlope = 1.5, Owner = r.Id };
        var y = EixoDe(r).Points[0].Y;
        for (var x = 0.0; x <= 200; x += 5) { c.Left.Add(new Vec3(x, y + 6.5, 2 + x / 100)); c.Right.Add(new Vec3(x, y - 6.5, 2 + x / 100)); }
        return c;
    }).ToList();
    // Terreno que sobe 2 % ao longo das vias e ondula de lado (sem tendência): cada via tem o mesmo trabalho em qualquer tamanho.
    Med("Terraplenagem (malha de projeto e volumes)", () => Grading.Design(corredores, Array.Empty<GradePad>(), p => 3 + 0.02 * p.X + 0.5 * Math.Sin(p.Y / 30), 0, 30, 0.5, 1.0, 1.0));
}

Console.WriteLine("OPERAÇÕES DO NÚCLEO POR TAMANHO DO PROJETO (vias de 200 m, a 40 m uma da outra)");
var ops = linhas.Select(l => l.Operacao).Distinct().ToList();
var tamanhos = linhas.Select(l => (l.Vias, l.Marcas)).Distinct().ToList();
Console.WriteLine($"  {"Operação",-52}" + string.Concat(tamanhos.Select(t => $"{$"{t.Vias} vias / {t.Marcas} marcas",22}")) + "   crescimento (×4 marcas)");
foreach (var op in ops)
{
    var t = linhas.Where(l => l.Operacao == op).Select(l => l.Ms).ToList();
    var cresc = string.Join(" e ", t.Zip(t.Skip(1), (a, b) => a > 0.05 ? (b / a).ToString("0.0", pt) + "×" : "—"));
    Console.WriteLine($"  {op,-52}" + string.Concat(t.Select(x => $"{Ms(x),22}")) + $"   {cresc}");
}
Console.WriteLine();
Console.WriteLine("Leitura: ≈4× a cada 4× marcas é linear; ≈16× seria quadrático. Tempos abaixo de 1 ms variam com o ruído da máquina.");
