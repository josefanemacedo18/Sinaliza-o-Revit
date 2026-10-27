using Autodesk.Revit.UI;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.Updater;

namespace SinalizacaoViaria.Revit;

/// <summary>Ponto de entrada do plugin: cria a faixa de opções, registra o atualizador automático e o painel contextual.</summary>
public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            RibbonBuilder.Build(application);
            MarkingUpdater.Register(application.ActiveAddInId);
            // Painel contextual: se falhar, o plugin continua (o botão avisa e o log tem o motivo).
            try { ContextPanelService.Register(application); }
            catch (Exception ex) { Log.Error("Painel contextual – registro", ex); }
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Log.Error("OnStartup", ex);
            TaskDialog.Show("SinalizaBIM", "Falha ao iniciar o plugin: " + ex.Message);
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            MarkingUpdater.Unregister(application.ActiveAddInId);
            ContextPanelService.Unregister(application);
            PluginContext.SaveSettings();
        }
        catch (Exception ex)
        {
            Log.Error("OnShutdown", ex);
        }
        return Result.Succeeded;
    }
}
