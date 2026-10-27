using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Diagnóstico do Terreno (Etapa 9; painel Terreno) – somente leitura: resume a superfície projetada (o Toposolid como está),
/// o terreno original guardado pela terraplenagem e o limite de declividade, e roda só o V14, o mesmo do Validar Modelo.
/// Não altera nada; pode selecionar as vias com eixo fora da superfície.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class CmdDiagnosticoTerreno : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var hosts = TerrainModel.Hosts(doc);
        if (hosts.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhum Toposolid no projeto: não há terreno para diagnosticar.\n\n" +
                                      "Crie ou importe o Toposolid (Massa e terreno) e rode de novo. Nada foi alterado.");
            return Result.Cancelled;
        }
        var projetado = TerrainModel.CurrentMesh(doc);
        var guardados = hosts.Where(TerrainModel.HasOriginal).ToList();
        TerrainMesh? original = null;
        if (guardados.Count > 0)
        {
            original = new TerrainMesh();
            foreach (var m in guardados.Select(TerrainModel.Original))
                foreach (var (a, b, c) in m.Triangles) original.Add(m.Vertices[a], m.Vertices[b], m.Vertices[c]);
        }
        var criterios = PluginContext.Settings.CriteriosGeometricos;
        // Sem faces lidas, a malha vazia dá o mesmo achado do Validar Modelo ("não há superfície do terreno").
        var achados = ModelValidator.Topografia(MarkingStorage.Definitions(doc), projetado ?? new TerrainMesh(), criterios);
        var texto = TerrainDiagnostics.Relatorio(hosts.Count, projetado != null ? TerrainDiagnostics.Resumir(projetado) : null, guardados.Count,
            original != null ? TerrainDiagnostics.Resumir(original) : null, criterios.DeclividadeMaximaTerreno, criterios.FonteTexto, achados);

        var problemas = achados.Where(a => a.Severidade != Severidade.NaoVerificavel).ToList();
        int Count(Severidade s) => achados.Count(a => a.Severidade == s);
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = problemas.Count == 0
                ? $"Terreno: nenhum problema nas verificações que rodaram ({Count(Severidade.NaoVerificavel)} não verificada(s))."
                : $"Terreno: {Count(Severidade.Erro)} erro(s), {Count(Severidade.Aviso)} aviso(s), {Count(Severidade.Info)} informação(ões), " +
                  $"{Count(Severidade.NaoVerificavel)} não verificada(s).",
            MainContent = texto,
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        var marcas = problemas.SelectMany(a => a.Ids).Distinct().ToList();
        var ids = new List<ElementId>();
        if (marcas.Count > 0)
        {
            MarkingStorage.Copies(doc, out var elements);
            ids = marcas.SelectMany(id => elements.TryGetValue(id, out var l) ? l : new List<ElementId>())
                .Where(id => doc.GetElement(id) != null).Distinct().ToList();
        }
        if (ids.Count > 0) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Selecionar os {ids.Count} elemento(s) das vias com eixo fora da superfície");
        if (td.Show() == TaskDialogResult.CommandLink1) uidoc.Selection.SetElementIds(ids);
        return Result.Succeeded;
    }
}
