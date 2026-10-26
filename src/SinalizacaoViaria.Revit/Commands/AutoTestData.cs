using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Arquivo de dados do Autoteste (autoteste-…-dados.jsonl, uma linha JSON por registro, gravado a cada etapa): tudo o que o
/// relatório resume – todos os avisos e erros sem limite, cada par de pisos sobrepostos (códigos, marcas, cotas, tipo, volume
/// e centro), cada reta fora do eixo com as pontas, o tempo por trecho do plugin – e, no fim, o contorno de todos os pisos do
/// teste e o JSON de todas as definições. Com ele os defeitos são reproduzidos no núcleo, fora do Revit, sem rodar o Autoteste
/// de novo. Só entra o que o teste criou (as marcas do projeto ficam de fora). Gravar nunca interrompe o teste.
/// </summary>
internal sealed partial class AutoTestRunner
{
    private static readonly JsonSerializerOptions DataJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly HashSet<string> _dumped = new();
    private readonly long _maxIdBefore;

    public string DataPath => Path.ChangeExtension(ReportPath, null) + "-dados.jsonl";

    private void Data(object record)
    {
        try { File.AppendAllText(DataPath, JsonSerializer.Serialize(record, DataJson) + "\n", Encoding.UTF8); }
        catch { /* opcional */ }
    }

    /// <summary>JSON da definição da marca (uma vez por marca) – para reproduzir o defeito no núcleo.</summary>
    private void DataDefinition(string? markingId)
    {
        if (string.IsNullOrEmpty(markingId) || _before.Contains(markingId) || !_dumped.Add(markingId)) return;
        try
        {
            if (MarkingStorage.Definition(_doc, markingId) is { } d)
                Data(new { tipo = "definicao", id = d.Id, codigo = d.DisplayCode, classe = d.GetType().Name, grupo = d.GroupId, json = d.ToJson() });
        }
        catch { /* opcional */ }
    }

    /// <summary>Maior id de elemento antes do teste: o que vier depois foi criado por ele.</summary>
    private static long MaxId(Document doc)
    {
        try { return new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds().Select(id => id.Value).DefaultIfEmpty(0).Max(); }
        catch { return long.MaxValue; }
    }

    private void DataStart()
    {
        try { File.WriteAllText(DataPath, "", Encoding.UTF8); } catch { /* opcional */ }
        Data(new
        {
            tipo = "inicio", data = DateTime.Now.ToString("s"), modo = _opt.Full ? "completo" : "rapido",
            revit = $"{_app.Application.VersionName} {_app.Application.SubVersionNumber} ({_app.Application.VersionBuild})",
            plugin = typeof(AutoTestRunner).Assembly.GetName().Version?.ToString(),
            origem = new[] { _origin.X, _origin.Y }, z0 = _z0, marcasAntes = _before.Count,
            unidades = "m; coordenadas locais (a partir da origem da área de teste)",
        });
    }

    private void DataStep(StepReport s)
    {
        Data(new
        {
            tipo = "etapa", n = _steps.IndexOf(s) + 1, grupo = s.Group, nome = s.Name, status = s.Status, ms = s.Ms, conferenciaMs = s.CheckMs,
            elementos = s.Elements, erros = s.Errors, avisos = s.Warnings, notas = s.Info,
            tempo = s.Perf.Where(p => p.Value.Ms >= 1).OrderByDescending(p => p.Value.Ms)
                .ToDictionary(p => p.Key, p => new { ms = Math.Round(p.Value.Ms), n = p.Value.Count }),
            leitura = new { varreduras = s.Reads.Scans, elementos = s.Reads.Elements, json = s.Reads.Parses },
        });
        foreach (var r in s.Records) Data(r);
        foreach (var id in s.Marks) DataDefinition(id);
    }

    private void DataEnd()
    {
        Data(new
        {
            tipo = "fim", duracao = _clock.Elapsed.ToString(@"hh\:mm\:ss"), resumo = Summary(),
            tempo = Perf.Snapshot().OrderByDescending(p => p.Value.Ms).ToDictionary(p => p.Key, p => new { ms = Math.Round(p.Value.Ms), n = p.Value.Count }),
        });
    }

    /// <summary>Ponto local (m, 4 casas) a partir de coordenadas internas do Revit.</summary>
    private double[] Loc(XYZ p) => new[] { Math.Round(UnitConv.M(p.X) - _origin.X, 4), Math.Round(UnitConv.M(p.Y) - _origin.Y, 4), Math.Round(UnitConv.M(p.Z), 4) };

    /// <summary>Elemento citado num registro: marca, código, classe da definição, categoria, tipo, cotas.</summary>
    private object ElementInfo(Element e, StepReport? s = null)
    {
        var r = MarkingStorage.Read(e);
        if (r != null) s?.Marks.Add(r.MarkingId);
        var bb = e.get_BoundingBox(null);
        string? type = null;
        double? cm = null;
        if (e is Floor f && _doc.GetElement(f.GetTypeId()) is FloorType ft)
        {
            type = ft.Name;
            try { if (ft.GetCompoundStructure() is { } cs) cm = Math.Round(UnitConv.M(cs.GetWidth()) * 100, 2); } catch { /* sem estrutura */ }
        }
        return new
        {
            id = e.Id.Value, marca = r?.MarkingId, codigo = r?.Definition.DisplayCode, classe = r?.Definition.GetType().Name, cor = r?.Color.ToString(),
            categoria = e.Category?.Name, tipo = type, espessuraCm = cm,
            z = bb == null ? null : new[] { Math.Round(UnitConv.M(bb.Min.Z), 4), Math.Round(UnitConv.M(bb.Max.Z), 4) },
        };
    }

    // ------------------------------------------------------------------ tempo por trecho

    private static Dictionary<string, (double Ms, long Count)> Delta(Dictionary<string, (double Ms, long Count)> a, Dictionary<string, (double Ms, long Count)> b) =>
        b.Select(kv => (kv.Key, Ms: kv.Value.Ms - (a.TryGetValue(kv.Key, out var x) ? x.Ms : 0), Count: kv.Value.Count - (a.TryGetValue(kv.Key, out var y) ? y.Count : 0)))
            .Where(t => t.Count > 0 || t.Ms > 0.5).ToDictionary(t => t.Key, t => (t.Ms, t.Count));

    private static readonly System.Globalization.CultureInfo Br = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Onde foi o tempo da etapa (trechos acima de 0,1 s; se sobrepõem – gerar inclui geometria e leitura).</summary>
    private static string? TimeNote(StepReport s)
    {
        var parts = s.Perf.Where(p => p.Value.Ms >= 100).OrderByDescending(p => p.Value.Ms).Take(8)
            .Select(p => string.Format(Br, "{0} {1:0.0} s ({2}×)", p.Key, p.Value.Ms / 1000, p.Value.Count)).ToList();
        if (s.Reads.Scans > 0)
            parts.Add(string.Format(Br, "marcas lidas: {0} varredura(s), {1:N0} elemento(s), {2:N0} JSON", s.Reads.Scans, s.Reads.Elements, s.Reads.Parses));
        if (s.CheckMs >= 100) parts.Add(string.Format(Br, "conferência do Autoteste {0:0.0} s", s.CheckMs / 1000.0));
        return parts.Count == 0 ? null : "Tempo: " + string.Join("; ", parts) + ".";
    }

    /// <summary>Resumo do tempo no fim do relatório: trechos do plugin e etapas mais lentas.</summary>
    private void TimeSummary(StringBuilder sb)
    {
        var total = Perf.Snapshot();
        if (total.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("TEMPO POR TRECHO DO PLUGIN (todas as etapas; os trechos se sobrepõem – gerar inclui a geometria e a leitura)");
            foreach (var (k, v) in total.OrderByDescending(p => p.Value.Ms))
                sb.AppendLine(string.Format(Br, "  {0,9:0.0} s  {1,8:N0}×  {2}", v.Ms / 1000, v.Count, k));
        }
        sb.AppendLine();
        sb.AppendLine("ETAPAS MAIS LENTAS");
        foreach (var s in _steps.OrderByDescending(x => x.Ms).Take(15))
            sb.AppendLine(string.Format(Br, "  {0,7:0.0} s  {1} › {2} ({3} elemento(s))", s.Ms / 1000.0, s.Group, s.Name, s.Elements));
        var groups = _steps.GroupBy(x => x.Group).Select(g => (g.Key, Ms: g.Sum(x => x.Ms), N: g.Count())).OrderByDescending(g => g.Ms);
        sb.AppendLine();
        sb.AppendLine("TEMPO POR BLOCO");
        foreach (var g in groups) sb.AppendLine(string.Format(Br, "  {0,7:0.0} min  {1} ({2} etapa(s))", g.Ms / 60000.0, g.Key, g.N));
    }

    // ------------------------------------------------------------------ modelo final

    /// <summary>
    /// Conferência do modelo como ficou no fim (independe dos avisos, que o teste descarta a cada transação): todos os pisos que
    /// o teste criou – pares com volume em comum e retas do esboço que o Revit avisaria como fora do eixo – e o contorno de cada
    /// piso no arquivo de dados. Depois, o JSON de todas as definições do teste.
    /// </summary>
    private void FinalModel()
    {
        Step("Modelo final", "Pisos do teste: sobreposições e retas fora do eixo no modelo como ficou", FinalFloors, always: true);
        Step("Modelo final", "Definições do teste no arquivo de dados (JSON para reproduzir no núcleo)", s =>
        {
            var defs = TestDefs();
            var els = MarkingStorage.Elements(_doc);
            foreach (var d in defs)
            {
                if (!_dumped.Add(d.Id)) continue;
                Data(new { tipo = "definicao", id = d.Id, codigo = d.DisplayCode, classe = d.GetType().Name, grupo = d.GroupId,
                    elementos = els.GetValueOrDefault(d.Id)?.Count ?? 0, json = d.ToJson() });
            }
            s.Note($"{defs.Count} definição(ões) do teste gravadas em {Path.GetFileName(DataPath)}.");
            foreach (var g in defs.GroupBy(d => d.GetType().Name).OrderByDescending(g => g.Count()))
                s.Note($"{g.Key}: {g.Count()}");
        }, always: true);
    }

    /// <summary>
    /// Classe do par de pisos (RT-1.3, <see cref="OverlapClassifier"/>) pelo que o Revit mede: código da marca (ou o tipo do piso
    /// comum), marca, grupo, volume em comum e de cada piso, pintura e se as vias dos dois têm nó. Nada é apagado.
    /// </summary>
    private Classificacao Classify(Element ea, Element eb, Solid sa, Solid sb, double volumeFt3, Func<string, string, bool?> nos)
    {
        (string Code, StoredMarking? R) Who(Element e)
        {
            var r = MarkingStorage.Read(e);
            return (r?.Definition.DisplayCode ?? (e is Floor f && _doc.GetElement(f.GetTypeId()) is { } t ? t.Name : e.Category?.Name ?? "?"), r);
        }
        var (ca, ra) = Who(ea);
        var (cb, rb) = Who(eb);
        var (ga, gb) = (ra?.Definition.GroupId, rb?.Definition.GroupId);
        return OverlapClassifier.Classificar(new FatosSobreposicao(ca, cb, ra?.MarkingId, rb?.MarkingId, ga, gb, volumeFt3 * 0.0283168,
            sa.Volume * 0.0283168, sb.Volume * 0.0283168, ra != null && MarkingColors.IsPaint(ra.Color), rb != null && MarkingColors.IsPaint(rb.Color),
            ViasComNo: ga != null && gb != null ? nos(ga, gb) : null));
    }

    /// <summary>Vias com nó em comum, pelas definições de pavimento, interseção e rotatória do projeto (lidas uma vez, quando preciso).</summary>
    private Func<string, string, bool?> NodesBetweenRoads() =>
        OverlapClassifier.NosEntreVias(MarkingStorage.Definitions(_doc, typeof(RoadPavementDefinition), typeof(IntersectionDefinition), typeof(RoundaboutDefinition)));

    private void FinalFloors(StepReport s)
    {
        var floors = new FilteredElementCollector(_doc).OfClass(typeof(Floor)).Cast<Floor>().Where(f => f.Id.Value > _maxIdBefore).ToList();
        var offAxis = new List<(string Code, double OffDeg, double Length)>();
        var info = new Dictionary<long, (string Code, string? Mark)>();
        foreach (var f in floors)
        {
            var r = MarkingStorage.Read(f);
            var code = r?.Definition.DisplayCode ?? "(piso comum)";
            info[f.Id.Value] = (code, r?.MarkingId);
            var rings = new List<List<double[]>>();
            var arcs = 0;
            try
            {
                if (_doc.GetElement(f.SketchId) is Sketch sk)
                    foreach (CurveArray arr in sk.Profile)
                    {
                        var ring = new List<double[]>();
                        foreach (Curve c in arr)
                        {
                            var a = c.GetEndPoint(0);
                            ring.Add(Loc(a).Take(2).ToArray());
                            if (c is Arc) { arcs++; ring.Add(Loc(c.Evaluate(0.5, true)).Take(2).Append(1.0).ToArray()); }
                            else if (c is Line l)
                            {
                                var d = l.Direction;
                                var seg = new Core.Geometry.BoundarySegment(Vec2.Zero, new Vec2(d.X, d.Y));
                                // Ruído de ponto flutuante não conta: retas de 2 cm a 1e-9° (desvio lateral de 1e-13 m) não têm aviso do
                                // Revit e apareciam aqui (4º Autoteste completo); conta a partir de 0,1 µm de desvio na ponta.
                                var len = UnitConv.M(l.Length);
                                if (AxisSnap.OffAxisDeg(seg) is { } off && off > 1e-9 && off <= AxisSnap.MaxAngleDeg && len * Math.Sin(off * Math.PI / 180) > 1e-7)
                                {
                                    offAxis.Add((code, off, len));
                                    s.Records.Add(new { tipo = "fora-do-eixo-final", piso = f.Id.Value, marca = r?.MarkingId, codigo = code, desvioGraus = off, comprimento = Math.Round(len, 4),
                                        de = Loc(a).Take(2).ToArray(), ate = Loc(c.GetEndPoint(1)).Take(2).ToArray() });
                                    if (r != null) s.Marks.Add(r.MarkingId);
                                }
                            }
                        }
                        rings.Add(ring);
                    }
            }
            catch { /* esboço ilegível */ }
            var bb = f.get_BoundingBox(null);
            string? type = null;
            double? cm = null;
            if (_doc.GetElement(f.GetTypeId()) is FloorType ft)
            {
                type = ft.Name;
                try { if (ft.GetCompoundStructure() is { } cs) cm = Math.Round(UnitConv.M(cs.GetWidth()) * 100, 2); } catch { /* sem estrutura */ }
            }
            Data(new { tipo = "piso", id = f.Id.Value, marca = r?.MarkingId, codigo = code, cor = r?.Color.ToString(), tipoPiso = type, espessuraCm = cm,
                z = bb == null ? null : new[] { Math.Round(UnitConv.M(bb.Min.Z), 4), Math.Round(UnitConv.M(bb.Max.Z), 4) }, arcos = arcs,
                aneis = rings, nota = "anéis do esboço; ponto com 3º valor 1 = meio de arco" });
        }
        s.Note($"{floors.Count} piso(s) criados pelo teste; contornos gravados em {Path.GetFileName(DataPath)}.");
        if (WarningSummary.OffAxisNote(offAxis) is { } note) { s.Note(note); s.Warn($"{offAxis.Count} reta(s) de esboço de piso fora do eixo no modelo final (o Revit avisaria)."); }
        else s.Note("Nenhuma reta de esboço de piso fora do eixo (0,000000001° a 0,2°) no modelo final.");

        // Pares de pisos com volume em comum: candidatos pela caixa (menos 0,5 mm, para não pegar pisos que só se encostam).
        var sw = Stopwatch.StartNew();
        const double shrink = 0.0005 / 0.3048;
        var items = floors.Select(f => (F: f, B: f.get_BoundingBox(null))).Where(x => x.B != null).OrderBy(x => x.B!.Min.X).ToList();
        var cand = new List<(Floor A, Floor B)>();
        for (int i = 0; i < items.Count && cand.Count < 60000; i++)
            for (int j = i + 1; j < items.Count && items[j].B!.Min.X + shrink < items[i].B!.Max.X - shrink; j++)
            {
                var (a, b) = (items[i].B!, items[j].B!);
                if (a.Min.Y + shrink < b.Max.Y - shrink && b.Min.Y + shrink < a.Max.Y - shrink && a.Min.Z + shrink < b.Max.Z - shrink && b.Min.Z + shrink < a.Max.Z - shrink)
                    cand.Add((items[i].F, items[j].F));
            }
        var solids = new Dictionary<long, Solid?>();
        var kinds = new List<(string A, string B, bool Same, double VolumeM3)>();
        var classes = new List<(Classificacao C, double VolumeM3)>();
        Func<string, string, bool?>? nos = null;
        int unknown = 0;
        foreach (var (fa, fb) in cand)
        {
            if (SolidOf(fa, solids) is not { } sa || SolidOf(fb, solids) is not { } sb) { unknown++; continue; }
            try
            {
                var inter = BooleanOperationsUtils.ExecuteBooleanOperation(sa, sb, BooleanOperationsType.Intersect);
                var v = inter?.Volume ?? 0;
                if (v <= 1e-4) continue;
                var (ia, ib) = (info[fa.Id.Value], info[fb.Id.Value]);
                var same = ia.Mark != null && ia.Mark == ib.Mark;
                kinds.Add((ia.Code, ib.Code, same, v * 0.0283168));
                var c = Classify(fa, fb, sa, sb, v, nos ??= NodesBetweenRoads());
                classes.Add((c, v * 0.0283168));
                double[]? center = null;
                try { center = Loc(inter!.ComputeCentroid()); } catch { /* sem centro */ }
                s.Records.Add(new { tipo = "sobreposicao-final", volumeCm3 = Math.Round(v * 28316.8, 1), mesmaMarca = same, centro = center,
                    classe = OverlapClassifier.Nome(c.Classe), motivo = c.Motivo, acao = c.Acao, a = ElementInfo(fa, s), b = ElementInfo(fb, s) });
            }
            catch { unknown++; }
        }
        sw.Stop();
        s.Note($"Pisos sobrepostos no modelo final: {cand.Count} par(es) candidatos pela caixa, {kinds.Count} com volume em comum, {unknown} sem conferência ({sw.Elapsed.TotalSeconds:0} s).");
        if (WarningSummary.PersistentPairsNote(kinds) is { } byKind)
        {
            s.Note(byKind);
            if (OverlapClassifier.Resumo(classes) is { } byClass) s.Note(byClass + " Motivo e ação de cada par no arquivo de dados; nada foi apagado.");
            var real = classes.Count(c => c.C.Classe is ClasseSobreposicao.Conflito or ClasseSobreposicao.Duplicacao);
            s.WarnFirst($"{kinds.Count} par(es) de pisos sobrepostos no modelo final ({kinds.Sum(k => k.VolumeM3):0.###} m³), {real} deles conflito real ou duplicação – detalhes no arquivo de dados.");
        }
    }
}
