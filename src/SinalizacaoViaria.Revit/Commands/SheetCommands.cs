using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Janela de um trecho de prancha (criação do conjunto e edição de um trecho).</summary>
internal static class SheetForms
{
    public static readonly (string, double)[] Scales =
        { ("1:100", 100), ("1:200", 200), ("1:250", 250), ("1:500", 500), ("1:750", 750), ("1:1000", 1000), ("1:2000", 2000) };

    /// <summary>Edição de um trecho: estacas, escala, giro e conteúdo próprios.</summary>
    public static FormWindow Edit(SheetSegmentDefinition d)
    {
        var start = SheetPlanner.Station(d.Start);
        var end = SheetPlanner.Station(d.End);
        var along = d.RotationDeg == null;
        var angle = d.RotationDeg ?? 0;
        var w = new FormWindow($"Editar prancha {d.SheetNumber}", $"Prancha {d.SheetNumber} – trecho {d.Index}",
                "Estacas no formato 10+5,00 (estacas de 20 m) ou em metros. A vista, a folha, as linhas de corte das pranchas vizinhas, a legenda " +
                "e o quadro são refeitos.", null, null, false, "Aplicar", 640, 560)
            .Section("Trecho")
            .Text("Início (estaca)", () => start, v => start = v ?? start)
            .Text("Fim (estaca)", () => end, v => end = v ?? end)
            .Number("Escala 1:", () => d.Scale, v => d.Scale = v, 20, 10000, "0")
            .Number("Faixa lateral de cada lado do eixo (m)", () => d.HalfWidth, v => d.HalfWidth = v, 1, 500, "0.#")
            .Section("Giro da vista")
            .Check("Ao longo do trecho (texto legível)", () => along, v => along = v)
            .Number("Ângulo fixo (°, quando não for ao longo do trecho)", () => angle, v => angle = v, -180, 180, "0.#")
            .Section("Conteúdo")
            .Check("Linhas de corte \"continua na prancha X\"", () => d.MatchLines, v => d.MatchLines = v)
            .Check("Legenda de placas", () => d.Legend, v => d.Legend = v)
            .Check("Quadro de quantidades", () => d.Quantities, v => d.Quantities = v)
            .Check("Legenda e quadro só com o que está no trecho (desmarcado = projeto inteiro)", () => d.ContentOfSegment, v => d.ContentOfSegment = v)
            .Number("Altura do texto (mm)", () => d.TextMm, v => d.TextMm = v, 1, 10, "0.#");
        w.Closing += (_, _) =>
        {
            if (w.DialogResult != true) return;
            try
            {
                var a = SheetPlanner.ParseStation(start);
                var b = SheetPlanner.ParseStation(end);
                if (b <= a) throw new FormatException("o fim deve vir depois do início");
                (d.Start, d.End) = (a, b);
            }
            catch (Exception ex)
            {
                UiHelpers.Error("Estacas inválidas (" + ex.Message + ") – o trecho ficou como estava.");
            }
            d.RotationDeg = along ? null : angle;
        };
        return w;
    }
}

/// <summary>
/// Pranchas: com a folha (carimbo) e a escala escolhidas, divide o eixo da via em trechos que cabem na folha – com sobreposição e
/// linhas de corte – ou usa os trechos dados por estacas ou por cortes clicados; cria uma vista por trecho com a região de corte
/// girada ao longo do eixo e monta cada folha com a legenda de placas e o quadro de quantidades.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdPranchas : CommandBase
{
    private enum Modo { Automatico, Estacas, Cliques }

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var tbs = SheetBuilder.TitleBlocks(doc);
        if (tbs.Count == 0)
            throw new UserMessageException("Nenhuma família de folha (carimbo) carregada neste projeto.\n\nCarregue o carimbo do escritório " +
                                           "(Inserir → Carregar família → pasta Anotações / Folhas de título) e repita o comando.");
        if (uidoc.ActiveView is not ViewPlan source || source.IsTemplate)
            throw new UserMessageException("Abra a vista em planta que servirá de base para as pranchas (as vistas dos trechos são cópias dela).");
        var road = RoadMover.Pick(uidoc, "Pranchas: clique sobre a via a detalhar – ESC cancela", out _);
        if (road == null) return Result.Cancelled;
        var axis = road.Axis;
        var name = !string.IsNullOrWhiteSpace(road.Def.Notes) ? road.Def.Notes!.Trim() : Hierarquia.Label(road.Def.Hierarchy);

        var st = PluginContext.Settings;
        var tbIndex = Math.Clamp(int.TryParse(st.Get("pranchas:carimbo"), out var ti) ? ti : 0, 0, tbs.Count - 1);
        double scale = double.TryParse(st.Get("pranchas:escala"), out var sc) ? sc : 500;
        double overlap = 20, margin = 10, rightCol = 190;
        double half = Math.Ceiling(Math.Max(road.Def.TotalLeft, road.Def.TotalRight) + 10);
        var modo = Modo.Automatico;
        var stations = $"0-{Math.Floor(axis.Length / 2)}; {Math.Floor(axis.Length / 2) - 20}-{Math.Floor(axis.Length)}";
        var prefix = st.Get("pranchas:prefixo") ?? "P";
        var first = 1;
        bool legend = true, quantities = true, ofSegment = true, matchLines = true;
        var w = new FormWindow("Pranchas", "Pranchas da via",
                $"Via: {name} – eixo com {UiHelpers.F(axis.Length)} m. Cada trecho vira uma vista (cópia da planta ativa) com a região de corte girada " +
                "ao longo do eixo, numa folha com o carimbo escolhido, a legenda de placas e o quadro de quantidades na coluna da direita.",
                null, null, false, "Criar pranchas", 760, 720)
            .Section("Folha e escala")
            .Choice("Carimbo (família de folha)", tbs.Select((t, i) => ($"{t.FamilyName}: {t.Name}", i)), () => tbIndex, v => tbIndex = v)
            .Choice("Escala", SheetForms.Scales, () => scale, v => scale = v)
            .Number("Margem da folha (mm)", () => margin, v => margin = v, 0, 50, "0")
            .Number("Coluna da direita – legenda, quadro e carimbo (mm)", () => rightCol, v => rightCol = v, 0, 600, "0")
            .Number("Faixa lateral de cada lado do eixo (m)", () => half, v => half = v, 1, 500, "0.#")
            .Section("Trechos")
            .Choice("Divisão", new[] { ("Automática – o maior trecho que cabe em cada folha", Modo.Automatico), ("Por estacas (digitadas abaixo)", Modo.Estacas),
                    ("Por cortes clicados no eixo", Modo.Cliques) }, () => modo, v => modo = v)
            .Number("Sobreposição entre folhas (m)", () => overlap, v => overlap = v, 0, 500, "0.#")
            .Text("Estacas dos trechos (ex.: 0-200; 9+0,00 a 20+0,00)", () => stations, v => stations = v ?? "", multiline: true)
            .Text("Prefixo do número da folha", () => prefix, v => prefix = string.IsNullOrWhiteSpace(v) ? "P" : v.Trim())
            .Integer("Primeiro número", () => first, v => first = v, 0, 999)
            .Section("Conteúdo de cada folha")
            .Check("Linhas de corte \"continua na prancha X\"", () => matchLines, v => matchLines = v)
            .Check("Legenda de placas", () => legend, v => legend = v)
            .Check("Quadro de quantidades", () => quantities, v => quantities = v)
            .Check("Legenda e quadro só com o que está no trecho (desmarcado = projeto inteiro)", () => ofSegment, v => ofSegment = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        st.Set("pranchas:carimbo", tbIndex.ToString());
        st.Set("pranchas:escala", scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
        st.Set("pranchas:prefixo", prefix);
        PluginContext.SaveSettings();
        var tb = tbs[tbIndex];

        // Cortes clicados: antes de abrir as transações.
        var cuts = new List<double>();
        if (modo == Modo.Cliques)
        {
            while (Picking.PickPoint(uidoc, $"Pranchas: clique sobre o eixo onde cortar ({cuts.Count} corte(s)) – ESC conclui") is { } p)
                cuts.Add(axis.Project(UnitConv.ToVec2(p)).Station);
            if (cuts.Count == 0) throw new UserMessageException("Nenhum corte clicado – use a divisão automática ou por estacas.");
        }

        var results = new List<RenderResult>();
        var defs = new List<SheetSegmentDefinition>();
        using var tg = new TransactionGroup(doc, "SV - Pranchas");
        tg.Start();
        ViewSheet firstSheet;
        SheetFrame frame;
        using (var t = new Transaction(doc, "SV - Pranchas: folha"))
        {
            t.Start();
            if (!tb.IsActive) tb.Activate();
            firstSheet = ViewSheet.Create(doc, tb.Id);
            doc.Regenerate();
            var box = SheetBuilder.TitleBlockBox(doc, firstSheet) ?? throw new UserMessageException("O carimbo escolhido não tem contorno na folha.");
            frame = SheetFrame.PlanArea((box.Max.X - box.Min.X) * 304.8, (box.Max.Y - box.Min.Y) * 304.8, margin, rightCol);
            t.Commit();
        }
        List<SheetSegment> segs;
        try
        {
            segs = modo switch
            {
                Modo.Estacas => SheetPlanner.FromRanges(axis, SheetPlanner.ParseRanges(stations), frame, scale, half),
                Modo.Cliques => SheetPlanner.FromRanges(axis, SheetPlanner.RangesFromCuts(cuts, axis.Length, overlap), frame, scale, half),
                _ => SheetPlanner.Auto(axis, frame, scale, overlap, half),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            tg.RollBack();
            throw new UserMessageException(ex.Message);
        }
        if (segs.Count == 0) { tg.RollBack(); return Result.Cancelled; }

        var setId = Guid.NewGuid().ToString("N");
        using (var t = new Transaction(doc, "SV - Pranchas: vistas e folhas"))
        {
            t.Start();
            foreach (var s in segs)
            {
                var sheet = s.Index == 1 ? firstSheet : ViewSheet.Create(doc, tb.Id);
                var number = SheetBuilder.UniqueSheetNumber(doc, $"{prefix}{first + s.Index - 1:00}");
                sheet.SheetNumber = number;
                sheet.Name = $"{name} – trecho {s.Index} (est. {SheetPlanner.Station(s.Start)} a {SheetPlanner.Station(s.End)})";
                var view = (View)doc.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
                view.Name = SheetBuilder.UniqueViewName(doc, $"SV - {number} – {name}");
                view.Scale = (int)Math.Round(scale);
                var d = new SheetSegmentDefinition
                {
                    RoadId = road.Def.Id, SetId = setId, Index = s.Index, SheetNumber = number, Start = s.Start, End = s.End, Scale = scale,
                    HalfWidth = half, FrameWidthMm = frame.WidthMm, FrameHeightMm = frame.HeightMm, MarginMm = margin, MatchLines = matchLines,
                    Legend = legend, Quantities = quantities, ContentOfSegment = ofSegment, SheetId = sheet.UniqueId, TitleBlockTypeId = tb.UniqueId,
                };
                DetailHelpers.PrepareOutput(d, view);
                defs.Add(d);
                if (!s.Fits) results.Add(new RenderResult { Warnings = { $"Prancha {number}: o trecho não cabe na folha a 1:{scale:0}." } });
            }
            t.Commit();
        }
        SheetSegmentDefinition.Link(defs);
        results.AddRange(MarkingCreator.Commit(uidoc, defs, "SV - Pranchas: trechos"));
        foreach (var d in defs) results.AddRange(SheetBuilder.EnsureContent(uidoc, d));
        MarkingCreator.Commit(uidoc, defs, "SV - Pranchas: vínculos");
        tg.Assimilate();
        try { uidoc.ActiveView = firstSheet; } catch (Exception ex) { Log.Error("Abrir prancha", ex); }
        results.Insert(0, new RenderResult { Warnings = { $"{defs.Count} prancha(s) criada(s): {string.Join(", ", defs.Select(x => x.SheetNumber))}. Para mudar um trecho, use Editar sobre o título ou a linha de corte dele." } });
        Report("Pranchas", results, alwaysShow: true);
        return Result.Succeeded;
    }
}
