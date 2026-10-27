using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Revit.Commands;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Painel contextual do SinalizaBIM (Etapa 10; Prompt de Interface §9): painel acoplável do Revit (registrado ao iniciar,
/// fechado até o usuário abrir) que acompanha a seleção. O evento de seleção só lê – não altera o documento, não muda a
/// seleção, não abre transação, como a API exige; elemento que não é do plugin fica com o Revit. As operações do painel
/// rodam por um ExternalEvent, quando a API está disponível, com as validações do botão da faixa.
/// </summary>
public static class ContextPanelService
{
    public static readonly DockablePaneId PaneId = new(new Guid("5B3D8F2A-6C41-4E7B-9A1D-2F8C7E4B6A90"));
    public const string PaneTitle = "SinalizaBIM – Painel Contextual";

    /// <summary>Marcas interpretadas por seleção (as demais só são contadas): a seleção de uma área grande não trava o Revit.</summary>
    public const int MaxMarcas = 50;

    private static ContextPanelPage? _page;
    private static ExternalEvent? _event;
    private static readonly PaneCommandHandler Handler = new();

    public static void Register(UIControlledApplication app)
    {
        _page = new ContextPanelPage(RequestCommand);
        app.RegisterDockablePane(PaneId, PaneTitle, new Provider(_page));
        _event = ExternalEvent.Create(Handler);
        app.SelectionChanged += OnSelectionChanged;
        app.ControlledApplication.DocumentClosing += OnDocumentClosing;
    }

    public static void Unregister(UIControlledApplication app)
    {
        app.SelectionChanged -= OnSelectionChanged;
        app.ControlledApplication.DocumentClosing -= OnDocumentClosing;
        _event?.Dispose();
        _event = null;
    }

    /// <summary>O painel registrado (nulo se o registro falhou ao iniciar).</summary>
    public static DockablePane? Pane(UIApplication app)
    {
        try { return app.GetDockablePane(PaneId); }
        catch (Exception ex) { Log.Error("Painel contextual – localizar", ex); return null; }
    }

    /// <summary>Atualiza o painel com a seleção atual (ao abri-lo pelo botão; chamado dentro de um comando).</summary>
    public static void Refresh(UIDocument uidoc)
    {
        if (_page == null) return;
        try { _page.Show(Context(uidoc.Document, uidoc.Selection.GetElementIds())); }
        catch (Exception ex) { Log.Error("Painel contextual – atualizar", ex); }
    }

    private static void RequestCommand(string command) => RunLater(command);

    /// <summary>
    /// Pede o comando para rodar quando a API estiver livre – depois do comando atual, pelo mesmo ExternalEvent do painel
    /// (Etapa 11: telas somente leitura que abrem outro comando). Falso se o evento não existe (o registro falhou ao iniciar).
    /// </summary>
    public static bool RunLater(string command)
    {
        if (_event == null) return false;
        Handler.Request(command);
        return _event.Raise() is ExternalEventRequest.Accepted or ExternalEventRequest.Pending;
    }

    private static void OnDocumentClosing(object? sender, DocumentClosingEventArgs e) => _page?.Show(PainelContextual.Vazio());

    private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_page == null) return;
        try { _page.Show(Context(e.GetDocument(), e.GetSelectedElements())); }
        catch (Exception ex) { Log.Error("Painel contextual – seleção", ex); }
    }

    /// <summary>
    /// O que mostrar para os elementos: o Id de cada um (sem interpretar), uma definição por marca (até <see cref="MaxMarcas"/>),
    /// o registro da terraplenagem e, só para uma quadra, a busca do loteamento dela.
    /// </summary>
    internal static ContextoSelecao Context(Document? doc, ICollection<ElementId> ids)
    {
        if (doc == null || doc.IsFamilyDocument) return PainelContextual.Vazio();
        var porMarca = new Dictionary<string, Element>();
        var fora = 0;
        foreach (var id in ids)
        {
            if (doc.GetElement(id) is not { } e) continue;
            if (MarkingStorage.IdOf(e) is { } marca) porMarca.TryAdd(marca, e);
            else fora++;
        }
        var defs = new List<MarkingDefinition>();
        foreach (var e in porMarca.Values.Take(MaxMarcas))
            if (MarkingStorage.Read(e) is { } r) defs.Add(r.Definition);
        if (defs.Count == 0) return PainelContextual.Vazio(fora);
        var ctx = PainelContextual.De(defs, fora, EarthworkRecordStore.Load(doc), id => MarkingStorage.Definition(doc, id));
        return porMarca.Count > MaxMarcas
            ? ctx with { Notas = ctx.Notas.Append($"{porMarca.Count} marcas na seleção: o resumo considera as {MaxMarcas} primeiras.").ToList() }
            : ctx;
    }

    private sealed class Provider : IDockablePaneProvider
    {
        private readonly FrameworkElement _content;
        public Provider(FrameworkElement content) => _content = content;

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _content;
            // Na primeira vez, como aba junto da paleta Propriedades do Revit; depois o Revit guarda onde o usuário o deixou.
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Tabbed, TabBehind = DockablePanes.BuiltInDockablePanes.PropertiesPalette };
            data.VisibleByDefault = false;
        }
    }
}
