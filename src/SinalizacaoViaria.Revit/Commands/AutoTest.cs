using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Autoteste: roda as ferramentas do plugin sem janelas numa área de teste afastada do projeto – vias de todos os modelos,
/// conexões, o catálogo inteiro de sinalização, calçadas, topografia, obras, edição, detalhamento e quantitativos –, confere
/// o resultado no modelo (pintura sobre os pisos, continuidade dos pisos, terreno × via, obras × vias) e grava um relatório.
/// No fim tudo é desfeito (ou mantido para inspeção).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdAutoteste : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => Start(app, uidoc);

    internal static Result Start(UIApplication app, UIDocument uidoc)
    {
        var o = new AutoTestOptions();
        var w = new FormWindow("Autoteste", "Autoteste do SinalizaBIM (diagnóstico)",
                "Roda as ferramentas do plugin, sozinho, numa pequena ÁREA DE TESTE afastada do seu projeto e anota cada erro e aviso " +
                "num relatório. No fim tudo é desfeito (um único Desfazer).\n\n" +
                "RÁPIDO (padrão): uma amostra de cada ferramenta, com limite de 3 minutos – o arquivo não fica pesado. " +
                "Use o COMPLETO só quando for enviar um relatório de erros: ele cria milhares de elementos e pode levar 10 a 40 minutos.\n\n" +
                "Dica: rode num arquivo separado (cópia do projeto ou arquivo vazio) para não pesar o seu projeto.",
                null, null, false, "Iniciar o teste", 780, 760)
            .Section("Modo")
            .Check("Completo (todas as variantes e o catálogo inteiro – DEMORADO)", () => o.Full, v => o.Full = v)
            .Section("O que testar")
            .Check("Vias e conexões (interseções, rotatórias, cul-de-sac)", () => o.Roads, v => { o.Roads = v; o.Connections = v; })
            .Check("Sinalização horizontal", () => o.Horizontal, v => o.Horizontal = v)
            .Check("Sinalização vertical, dispositivos e mobiliário urbano", () => o.Vertical, v => o.Vertical = v)
            .Check("Calçadas: rampas, orelha, áreas, canteiros, moderação, piso tátil, ferrovia, drenagem", () => o.Sidewalks, v => o.Sidewalks = v)
            .Check("Topografia e Perfil da Via (pesado)", () => o.Terrain, v => o.Terrain = v)
            .Check("Obras: viaduto, ponte, túnel, trincheira, muros, taludes, nó viário (pesado)", () => o.Works, v => o.Works = v)
            .Check("Edição e detalhamento", () => o.Editing, v => { o.Editing = v; o.Detailing = v; })
            .Check("Simulador de Tráfego", () => o.Traffic, v => o.Traffic = v)
            .Check("Verificações do modelo (pintura × pisos, continuidade)", () => o.Checks, v => o.Checks = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        var runner = new AutoTestRunner(app, uidoc, o);
        var (summary, text) = runner.Run();
        UiHelpers.ShowModal(new AutoTestWindow(summary, text, runner.ReportPath));
        return Result.Succeeded;
    }
}

internal sealed class AutoTestOptions
{
    // Padrão leve: vias/conexões, horizontal, calçadas e simulador numa amostra; o resto só por escolha.
    public bool Roads { get; set; } = true;
    public bool Traffic { get; set; } = true;
    public bool Connections { get; set; } = true;
    public bool Horizontal { get; set; } = true;
    public bool Vertical { get; set; } = true;
    public bool Sidewalks { get; set; } = true;
    public bool Terrain { get; set; }
    public bool Works { get; set; }
    public bool Editing { get; set; }
    public bool Detailing { get; set; }
    public bool Checks { get; set; }
    /// <summary>Completo: todas as variantes; senão, no máximo 3 etapas por bloco e 3 minutos no total.</summary>
    public bool Full { get; set; }
    public int StepsPerGroup => Full ? int.MaxValue : 3;
    public double BudgetSeconds => Full ? double.MaxValue : 180;
}

/// <summary>Resultado de uma etapa do Autoteste.</summary>
internal sealed class StepReport(string group, string name)
{
    public string Group { get; } = group;
    public string Name { get; } = name;
    public long Ms { get; set; }
    public int Elements { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Info { get; } = new();

    public string Status => Errors.Count > 0 ? "ERRO" : Warnings.Count > 0 ? "AVISO" : "OK";

    public void Error(string m) { if (!Errors.Contains(m)) Errors.Add(m); }
    public void Warn(string m) { if (!Warnings.Contains(m) && !Errors.Contains(m)) Warnings.Add(m); }
    public void Note(string m) => Info.Add(m);

    /// <summary>Mensagem do plugin: erros explícitos viram ERRO, o resto aviso.</summary>
    public void Message(string m)
    {
        var l = m.ToLowerInvariant();
        if (l.Contains("falha") || l.Contains("erro") || l.Contains("não foi possível") || l.Contains("recusou") || l.Contains("exception"))
            Error(m);
        else Warn(m);
    }
}

/// <summary>Executa as etapas do Autoteste, recolhe erros do plugin e do Revit e monta o relatório.</summary>
internal sealed partial class AutoTestRunner
{
    private readonly UIApplication _app;
    private readonly UIDocument _uidoc;
    private readonly Document _doc;
    private readonly AutoTestOptions _opt;
    private readonly List<StepReport> _steps = new();
    private readonly List<string> _logs = new();
    private readonly List<string> _dialogs = new();
    private readonly HashSet<string> _before;
    private readonly Stopwatch _clock = new();
    private int _skipped;
    private StepReport? _cur;
    private Vec2 _origin;
    private double _z0;
    private Level? _level;
    private ViewPlan? _plan;

    public string ReportPath { get; }

    public AutoTestRunner(UIApplication app, UIDocument uidoc, AutoTestOptions opt)
    {
        _app = app;
        _uidoc = uidoc;
        _doc = uidoc.Document;
        _opt = opt;
        _before = MarkingStorage.Definitions(_doc).Select(d => d.Id).ToHashSet();
        Directory.CreateDirectory(PluginPaths.Root);
        ReportPath = Path.Combine(PluginPaths.Root, $"autoteste-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
    }

    public (string Summary, string Text) Run()
    {
        _clock.Start();
        Log.Capture = _logs;
        Notify.Sink = _dialogs;
        _app.Application.FailuresProcessing += OnFailures;
        var keep = false;
        using var tg = new TransactionGroup(_doc, "SV - Autoteste");
        try
        {
            tg.Start();
            Setup();
            if (_opt.Roads) Roads();
            if (_opt.Connections) Connections();
            if (_opt.Horizontal) Horizontal();
            if (_opt.Vertical) Vertical();
            if (_opt.Sidewalks) Sidewalks();
            if (_opt.Roads || _opt.Sidewalks) RoundT();
            if (_opt.Terrain || _opt.Works) Terrain();
            if (_opt.Works) Works();
            if (_opt.Editing) Editing();
            if (_opt.Detailing) Detailing();
            if (_opt.Traffic) Traffic();
            if (_opt.Checks) Checks();
        }
        catch (Exception ex)
        {
            var s = new StepReport("Autoteste", "Interrompido");
            s.Error($"O teste parou: {ex.GetType().Name}: {ex.Message}\n      {Log.Where(ex)}");
            _steps.Add(s);
        }
        finally
        {
            _app.Application.FailuresProcessing -= OnFailures;
            Log.Capture = null;
            Notify.Sink = null;
            Status("");
        }
        _clock.Stop();
        var text = Report();
        Save(text);
        var td = new TaskDialog(CommandBase.AppTitle)
        {
            MainInstruction = "Autoteste concluído – " + Summary(),
            MainContent = $"Relatório salvo em:\n{ReportPath}\n\nO que fazer com os elementos criados na área de teste?",
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Desfazer tudo (recomendado)", "O projeto volta exatamente ao que era antes do teste.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Manter os elementos para inspeção",
            "Ficam as vistas \"SV Autoteste\" (planta e 3D). Depois use Desfazer (Ctrl+Z) \"SV - Autoteste\" para remover tudo de uma vez.");
        keep = td.Show() == TaskDialogResult.CommandLink2;
        try
        {
            if (keep) tg.Assimilate();
            else tg.RollBack();
        }
        catch (Exception ex) { Log.Error("Autoteste – encerrar", ex); }
        return (Summary(), text);
    }

    // ------------------------------------------------------------------ etapas

    /// <summary>Simulador de Tráfego sobre tudo o que o teste criou (e o que já havia no projeto).</summary>
    private void Traffic()
    {
        Core.Traffic.TrafficNetwork? net = null;
        Core.Traffic.TrafficResult? res = null;
        Step("Tráfego", "Rede do projeto", s =>
        {
            net = CmdSimuladorTrafego.BuildNetwork(_doc);
            s.Note($"Sinalização interpretada: {net.Regulations.Count(r => r.Applied)} aplicada(s), {net.Regulations.Count(r => !r.Applied && r.Kind != Core.Traffic.TipoRegra.Informativa)} não associada(s); " +
                   $"{net.Links.Count(l => l.Closed)} trecho(s) fechado(s), {net.Links.Sum(l => l.ClosedLanes.Count)} faixa(s) fechada(s), {net.Nodes.Sum(n => n.ProhibitedTurns.Count)} conversão(ões) proibida(s).");
            s.Note($"{net.Roads.Count} via(s), {net.Nodes.Count} nó(s) ({net.Nodes.Count(n => n.IsZone)} entradas), {net.Links.Count} trecho(s), " +
                   $"{net.Signs.Count} placa(s), {net.Crosswalks.Count} travessia(s), {net.GradeSeparations.Count} cruzamento(s) em desnível.");
            foreach (var n in net.Notes) s.Note("Rede: " + n);
            if (net.Roads.Count == 0) s.Error("Nenhuma via lida do projeto.");
            foreach (var nd in net.Nodes.Where(n => !n.IsZone && (n.In.Count == 0 || n.Out.Count == 0)))
                s.Warn($"{nd.Label} ({nd.Kind}) sem tráfego de entrada ou de saída.");
        });
        if (net == null || net.Roads.Count == 0) return;
        Step("Tráfego", "Análise HCM e diagnóstico", s =>
        {
            res = Core.Traffic.TrafficAnalysis.Run(net, new Core.Traffic.TrafficOptions { Demand = Core.Traffic.NivelDemanda.Pico });
            s.Note($"Demanda {res.TotalDemand:0} veh/h, sem caminho {res.Unserved:0} veh/h, velocidade média {res.AvgSpeed:0.0} km/h, {res.Diagnostics.Count} diagnóstico(s).");
            foreach (var nr in res.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind != Core.Traffic.TipoNo.Continuacao))
                s.Note($"{nr.Node.Label} {nr.Node.Control}: nível {nr.LOS}, atraso {nr.Delay:0.0} s, {nr.Approaches.Count} aproximação(ões)" + (nr.Cycle > 0 ? $", ciclo {nr.Cycle:0} s" : ""));
            if (res.TotalDemand < 1) s.Warn("Nenhuma viagem gerada (faltam pontas livres de via?).");
            if (res.Unserved > 0.2 * Math.Max(1, res.TotalDemand + res.Unserved)) s.Warn("Mais de 20% das viagens sem caminho – confira as conexões lidas.");
            foreach (var d in res.Diagnostics.Where(d => d.Severity == Core.Traffic.Gravidade.Critico).Take(15)) s.Note("Crítico: " + d.Title);
            var txt = Core.Traffic.TrafficReport.Build(res, null, _doc.Title);
            if (txt.Length < 500) s.Error("Relatório do tráfego vazio.");
        });
        if (res == null) return;
        Step("Tráfego", "Microssimulação (5 min)", s =>
        {
            var r2 = Core.Traffic.TrafficAnalysis.Run(net, new Core.Traffic.TrafficOptions { Demand = Core.Traffic.NivelDemanda.Media, SimSeconds = 300, WarmupSeconds = 90 });
            var sim = Core.Traffic.TrafficSimulation.Run(r2);
            s.Note($"{sim.Spawned} veículos gerados, {sim.Completed} viagens concluídas, {sim.InNetwork} na rede, {sim.Backlog} sem entrar, tempo médio {sim.MeanTravelTime:0} s, {sim.Frames.Count} quadros.");
            if (sim.Spawned > 20 && sim.Completed < 0.3 * sim.Spawned) s.Warn("Poucas viagens concluídas: possível travamento na rede simulada.");
            var overlaps = Core.Traffic.TrafficSimulation.CountOverlaps(sim);
            if (overlaps > 2) s.Error($"{overlaps} contato(s) entre veículos na animação (deveria haver só fila e lentidão).");
            var blink = sim.Frames.Sum(f => Enumerable.Range(0, f.Count).Count(k => f.Data[k * Core.Traffic.SimFrame.Stride + 6] != 0));
            s.Note($"Contatos entre veículos: {overlaps}; setas acesas em {blink} posições de veículo.");
        });
        Step("Tráfego", "Mapa com a planta real do projeto", s =>
        {
            if (net.Backdrop.Count == 0) s.Error("O mapa não recebeu as peças do projeto (pavimento, calçadas, pintura).");
            s.Note(string.Join(", ", net.Backdrop.GroupBy(b => b.Layer).Select(g => $"{g.Key}: {g.Count()}")));
        });
        Step("Tráfego", "Semáforos: recomendação de tempos, onda verde e travessia no meio da quadra", s =>
        {
            var o = new Core.Traffic.TrafficOptions { Demand = Core.Traffic.NivelDemanda.Pico };
            var nodes = net.Nodes.Where(n => n.Kind == Core.Traffic.TipoNo.Intersecao && n.In.Count >= 3).Take(3).ToList();
            foreach (var n in nodes) o.Nodes.Add(new Core.Traffic.NodeOverride { Node = n.Key, Control = Core.Traffic.ControleNo.Semaforo });
            var adv = Core.Traffic.TrafficSignalAdvisor.Recommend(net, o, nodes.Select(n => n.Key).ToList());
            if (nodes.Count > 0 && adv.Count == 0) s.Error("Nenhuma recomendação de tempos gerada.");
            foreach (var a in adv) s.Note($"{a.Label}: ciclo {a.CycleBefore:0} → {a.Cycle:0} s, atraso {a.DelayBefore:0.0} → {a.DelayAfter:0.0} s.");
            var road = net.Roads.OrderByDescending(r => r.Axis.Length).First();
            var r0 = Core.Traffic.TrafficAnalysis.Run(net, o);
            var chain = Core.Traffic.TrafficSignalAdvisor.SignalsOnRoad(r0, road.Id);
            if (chain.Count >= 2)
            {
                var wave = Core.Traffic.TrafficSignalAdvisor.Recommend(net, o, chain.Select(n => n.Key).ToList(), road.Id);
                s.Note($"Onda verde na {road.Name}: {string.Join(", ", wave.Select(a => $"{a.Label} {a.Offset:0} s"))}.");
            }
            var lk = net.Links.OrderByDescending(l => l.Length).First();
            var mid = lk.Path.PointAt(lk.Length / 2);
            o.Crossings.Add(new Core.Traffic.CrossingSignal { X = mid.X, Y = mid.Y, Cycle = 60, PedGreen = 14 });
            var r1 = Core.Traffic.TrafficAnalysis.Run(net, o);
            if (!r1.CrossingPlans.ContainsKey(lk.Index)) s.Error("O semáforo de travessia do cenário não foi associado ao trecho.");
            else s.Note($"Travessia semaforizada em {lk.Name}: capacidade {lk.BaseCapacity:0} → {lk.Capacity:0} veh/h.");
            Core.Traffic.TrafficAnalysis.Run(net, new Core.Traffic.TrafficOptions { Demand = Core.Traffic.NivelDemanda.Pico });
        });
        Step("Tráfego", "Soluções testadas para o pior cruzamento", s =>
        {
            var worst = res.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind == Core.Traffic.TipoNo.Intersecao).OrderByDescending(n => n.Delay).FirstOrDefault();
            if (worst == null) { s.Note("Sem cruzamento no projeto de teste."); return; }
            var trials = Core.Traffic.TrafficSolutions.For(net, res.Options, res, worst.Node.Index, null, seeds: 1);
            if (trials.Count == 0) s.Warn($"{worst.Node.Label}: nenhuma alternativa gerada.");
            foreach (var t in trials.Take(5)) s.Note(t.Summary());
            if (trials.Any(t => t.Failed)) s.Error("Alguma alternativa falhou ao ser testada: " + trials.First(t => t.Failed).Error);
        });
        Step("Tráfego", "Cenários: PARE × rotatória × semáforo coordenado, contagem e comparação", s =>
        {
            var node = net.Nodes.FirstOrDefault(n => n.Kind == Core.Traffic.TipoNo.Intersecao && n.In.Count >= 3);
            var runs = new List<(string, Core.Traffic.TrafficResult)> { ("Projeto", res) };
            if (node != null)
            {
                foreach (var c in new[] { Core.Traffic.ControleNo.Rotatoria, Core.Traffic.ControleNo.Semaforo })
                {
                    var o = new Core.Traffic.TrafficOptions { Coordinate = true };
                    o.Nodes.Add(new Core.Traffic.NodeOverride { Node = node.Key, Control = c });
                    runs.Add(($"{node.Label} {c}", Core.Traffic.TrafficAnalysis.Run(net, o)));
                }
                var oc = new Core.Traffic.TrafficOptions();
                var lk = net.Links[node.In[0]];
                oc.Nodes.Add(new Core.Traffic.NodeOverride { Node = node.Key, Turns = { new Core.Traffic.TurnCount { Approach = lk.Key, Total = 500, Left = 20, Through = 60, Right = 20 } } });
                runs.Add(("contagem", Core.Traffic.TrafficAnalysis.Run(net, oc)));
            }
            var txt = Core.Traffic.TrafficComparison.Text(runs);
            if (txt.Length < 200) s.Error("Comparação de cenários vazia.");
            s.Note($"{runs.Count} cenário(s) comparados; acidentes previstos no projeto {res.Safety.Values.Sum(x => x.CrashesPerYear):0.0}/ano; custo anual R$ {res.Economics.Total / 1e6:0.00} mi.");
            // Cenários guardados no projeto (grava e lê de volta).
            var list = Core.Traffic.TrafficScenario.Presets();
            TrafficScenarioStore.Save(_doc, list);
            var back = TrafficScenarioStore.Load(_doc);
            if (back.Count != list.Count) s.Error($"Cenários gravados no projeto: {list.Count}, lidos de volta: {back.Count}.");
            res = Core.Traffic.TrafficAnalysis.Run(net, new Core.Traffic.TrafficOptions { Demand = Core.Traffic.NivelDemanda.Pico });
        });
        Step("Tráfego", "Gravar plano semafórico e resultados no modelo", s =>
        {
            var host = new TrafficHost(_uidoc);
            var sig = res.Nodes.Values.FirstOrDefault(n => n.Control == Core.Traffic.ControleNo.Semaforo && n.Phases.Count > 0 && n.Node.Kind == Core.Traffic.TipoNo.Intersecao);
            if (sig != null) s.Note(host.ApplyToIntersection(sig.Node.Key, null, Core.Traffic.TrafficAnalysis.PlanOf(res, sig, "Autoteste")));
            else s.Note("Nenhum semáforo no projeto de teste para gravar o plano.");
            s.Note(host.WriteResults(res, "Autoteste"));
        });
        if (_plan != null)
            Step("Tráfego", "Mapa de níveis de serviço na planta", s =>
            {
                var msg = TrafficDrawer.Draw(_doc, _plan, res);
                s.Note(msg);
            });
    }

    private void Step(string group, string name, Action<StepReport> body)
    {
        // Modo rápido: amostra de cada bloco e limite de tempo (o arquivo não fica pesado nem trava).
        if (_steps.Count(x => x.Group == group) >= _opt.StepsPerGroup) { _skipped++; return; }
        if (_clock.Elapsed.TotalSeconds > _opt.BudgetSeconds) { _skipped++; return; }
        var s = new StepReport(group, name);
        _steps.Add(s);
        _cur = s;
        Status($"SinalizaBIM – Autoteste ({_steps.Count}): {group} › {name}");
        var logs0 = _logs.Count;
        var dlg0 = _dialogs.Count;
        var sw = Stopwatch.StartNew();
        try { body(s); }
        catch (Exception ex) { s.Error($"Exceção {ex.GetType().Name}: {ex.Message}\n      {Log.Where(ex)}"); }
        finally
        {
            sw.Stop();
            s.Ms = sw.ElapsedMilliseconds;
            foreach (var l in _logs.Skip(logs0)) s.Error("Erro tratado pelo plugin (só ia para o log): " + l);
            foreach (var d in _dialogs.Skip(dlg0)) s.Message("Mensagem ao usuário: " + d.Replace("\n", " | "));
            if (s.Ms > 60000) s.Warn($"Lento: {s.Ms / 1000.0:0} s.");
            _cur = null;
            // Relatório parcial gravado a cada etapa: se o Revit fechar no meio, dá para saber onde parou.
            try { Save(Report()); } catch { /* opcional */ }
        }
    }

    /// <summary>Falhas do Revit durante o teste: avisos anotados e descartados; erros anotados e a transação desfeita.</summary>
    private void OnFailures(object? sender, FailuresProcessingEventArgs e)
    {
        try
        {
            var fa = e.GetFailuresAccessor();
            var error = false;
            foreach (var f in fa.GetFailureMessages())
            {
                var who = Who(fa.GetDocument(), f.GetFailingElementIds());
                var text = f.GetDescriptionText() + who;
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    _cur?.Warn("Aviso do Revit: " + text);
                    fa.DeleteWarning(f);
                }
                else
                {
                    _cur?.Error("ERRO do Revit (a transação foi desfeita): " + text);
                    error = true;
                }
            }
            if (error) e.SetProcessingResult(FailureProcessingResult.ProceedWithRollBack);
        }
        catch { /* nunca interrompe o Revit */ }
    }

    private static string Who(Document doc, ICollection<ElementId> ids)
    {
        if (ids.Count == 0) return "";
        var names = ids.Take(3).Select(doc.GetElement).Where(e => e != null)
            .Select(e => MarkingStorage.Read(e!) is { } r ? $"{r.Definition.DisplayCode} ({e!.Category?.Name})" : $"{e!.Category?.Name} {e.Id}").Distinct();
        return " – " + string.Join(", ", names) + (ids.Count > 3 ? $" e mais {ids.Count - 3}" : "");
    }

    // ------------------------------------------------------------------ ajudas

    /// <summary>Ponto da área de teste (coordenadas locais, m).</summary>
    private Vec2 P(double x, double y) => new(_origin.X + x, _origin.Y + y);

    private Vec2 Local(Vec2 w) => w - _origin;

    private string L(Vec2 w) { var l = Local(w); return $"({l.X:0.0}; {l.Y:0.0})"; }

    private PathReference Pts(params (double X, double Y)[] p) => PathReference.FromPoints(p.Select(q => P(q.X, q.Y)), _z0);

    private PathReference Closed(params (double X, double Y)[] p) => PathReference.FromPoints(p.Select(q => P(q.X, q.Y)), _z0, true);

    private PathReference Rect(double x, double y, double w, double h) => Closed((x, y), (x + w, y), (x + w, y + h), (x, y + h));

    /// <summary>Eixo como linhas de modelo (como o usuário desenha), com curvas de raio <paramref name="radius"/>.</summary>
    private PathReference Lines(double radius, params (double X, double Y)[] p)
    {
        var pts = p.Select(q => P(q.X, q.Y)).ToList();
        List<ElementId> ids;
        using (var t = new Transaction(_doc, "SV Autoteste - eixo"))
        {
            t.Start();
            ids = Picking.CreateAxis(_doc, _plan ?? _uidoc.ActiveView, RoadConnection.Fillet(pts, radius), UnitConv.Ft(_z0));
            t.Commit();
        }
        return PathReference.FromElements(ids.Select(id => _doc.GetElement(id).UniqueId));
    }

    private static OutputSettings Out() => PluginContext.Settings.NewOutput();

    private List<MarkingDefinition> TestDefs() => MarkingStorage.Definitions(_doc).Where(d => !_before.Contains(d.Id)).ToList();

    private HashSet<string> Present() => MarkingStorage.All(_doc).Select(r => r.MarkingId).ToHashSet();

    /// <summary>Anota os resultados (elementos, avisos) e confere que cada definição gerou elementos.</summary>
    private void Take(StepReport s, IEnumerable<RenderResult> rs, IEnumerable<MarkingDefinition>? defs = null)
    {
        foreach (var r in rs)
        {
            s.Elements += r.Elements.Count;
            foreach (var w in r.Warnings) s.Message(w);
        }
        if (defs == null) return;
        var present = Present();
        foreach (var d in defs.Where(d => !present.Contains(d.Id)))
            s.Error($"{d.DisplayCode}: nenhum elemento no modelo depois de gerar.");
    }

    private List<RenderResult> Commit(StepReport s, IEnumerable<MarkingDefinition> defs, string name)
    {
        var list = defs.ToList();
        var rs = MarkingCreator.Commit(_uidoc, list, "SV Autoteste - " + name);
        Take(s, rs, list);
        return rs;
    }

    private void Save(string text)
    {
        File.WriteAllText(ReportPath, text, Encoding.UTF8);
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "SinalizaBIM_autoteste.txt"), text, Encoding.UTF8); } catch { /* opcional */ }
    }

    private string Summary()
    {
        var err = _steps.Count(s => s.Status == "ERRO");
        var warn = _steps.Count(s => s.Status == "AVISO");
        return $"{_steps.Count} etapas: {_steps.Count - err - warn} OK, {warn} com aviso, {err} com erro ({_clock.Elapsed:hh\\:mm\\:ss}).";
    }

    private string Report()
    {
        var sb = new StringBuilder();
        var asm = typeof(AutoTestRunner).Assembly;
        DateTime built;
        try { built = File.GetLastWriteTime(asm.Location); } catch { built = DateTime.MinValue; }
        sb.AppendLine("SINALIZABIM – RELATÓRIO DO AUTOTESTE");
        sb.AppendLine($"Data: {DateTime.Now:dd/MM/yyyy HH:mm}   Duração: {_clock.Elapsed:hh\\:mm\\:ss}");
        sb.AppendLine(_opt.Full ? "Modo: COMPLETO" : $"Modo: RÁPIDO (amostra de até {_opt.StepsPerGroup} etapas por bloco, limite de {_opt.BudgetSeconds / 60:0} min; {_skipped} etapa(s) não executadas)");
        sb.AppendLine($"Revit: {_app.Application.VersionName} {_app.Application.SubVersionNumber} (build {_app.Application.VersionBuild})");
        sb.AppendLine($"Plugin: {asm.GetName().Version} – DLL de {built:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Projeto: {_doc.Title}   Marcas existentes antes do teste: {_before.Count}");
        sb.AppendLine($"Área de teste: origem X = {_origin.X:0} m, Y = {_origin.Y:0} m (coordenadas internas), nível {_level?.Name} (cota {_z0:0.00} m)");
        sb.AppendLine("Coordenadas nas mensagens: locais, em metros a partir da origem da área de teste.");
        sb.AppendLine();
        sb.AppendLine("RESUMO: " + Summary());
        var bad = _steps.Where(s => s.Errors.Count > 0).ToList();
        if (bad.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ETAPAS COM ERRO:");
            foreach (var s in bad) sb.AppendLine($"  ✖ {s.Group} › {s.Name}: {First(s.Errors[0])}{(s.Errors.Count > 1 ? $" (+{s.Errors.Count - 1})" : "")}");
        }
        sb.AppendLine();
        sb.AppendLine("DETALHES");
        string? group = null;
        foreach (var s in _steps)
        {
            if (s.Group != group) { group = s.Group; sb.AppendLine(); sb.AppendLine($"=== {group} ==="); }
            sb.AppendLine($"[{s.Status}] {s.Name}  ({s.Ms} ms, {s.Elements} elemento(s))");
            foreach (var e in s.Errors.Take(40)) sb.AppendLine("    ✖ " + e);
            if (s.Errors.Count > 40) sb.AppendLine($"    ✖ … e mais {s.Errors.Count - 40} erro(s)");
            foreach (var w in s.Warnings.Take(25)) sb.AppendLine("    ⚠ " + w);
            if (s.Warnings.Count > 25) sb.AppendLine($"    ⚠ … e mais {s.Warnings.Count - 25} aviso(s)");
            foreach (var i in s.Info) sb.AppendLine("    · " + i);
        }
        return sb.ToString();
    }

    private static string First(string m) { var i = m.IndexOf('\n'); var l = i < 0 ? m : m[..i]; return l.Length > 220 ? l[..220] + "…" : l; }

    // ------------------------------------------------------------------ barra de status do Revit

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hwnd, string text);

    private IntPtr _statusBar = IntPtr.Zero;

    private void Status(string text)
    {
        try
        {
            if (_statusBar == IntPtr.Zero) _statusBar = FindWindowEx(_app.MainWindowHandle, IntPtr.Zero, "msctls_statusbar32", null);
            if (_statusBar != IntPtr.Zero) SetWindowText(_statusBar, text);
        }
        catch { /* opcional */ }
    }
}
