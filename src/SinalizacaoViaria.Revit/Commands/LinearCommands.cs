using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Base para os comandos que usam a janela de marcas lineares.</summary>
public abstract class LinearCommandBase : CommandBase
{
    protected abstract string WindowTitle { get; }
    protected abstract GrupoMarca[] Groups { get; }
    protected virtual PathMode DefaultMode => PathMode.Linhas;

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new LinearWindow(WindowTitle, Groups, null, DefaultMode);
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        var template = w.Result;
        EnsureDetailView(uidoc, template.Output);
        if (w.PickSurfaces)
        {
            var s = MarkingCreator.PickSurfaces(uidoc);
            if (s != null) template.Output.SurfaceIds = s;
        }
        return MarkingCreator.CreateAlongPath(uidoc, template, w.PathMode, template.Code);
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdLinhaLongitudinal : LinearCommandBase
{
    protected override string WindowTitle => "Marcas longitudinais";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Longitudinal, GrupoMarca.Canalizacao, GrupoMarca.Estacionamento };
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdLinhaTransversal : LinearCommandBase
{
    protected override string WindowTitle => "Marcas transversais";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Transversal };
    protected override PathMode DefaultMode => PathMode.DoisPontos;
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdTachas : LinearCommandBase
{
    protected override string WindowTitle => "Tachas e tachões";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Dispositivo };
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdPisoTatil : LinearCommandBase
{
    protected override string WindowTitle => "Sinalização tátil no piso (NBR 16537)";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Acessibilidade };
    protected override PathMode DefaultMode => PathMode.Desenhar;
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdCiclovia : LinearCommandBase
{
    protected override string WindowTitle => "Ciclovias e ciclofaixas";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Ciclovia };
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdCalcadas : LinearCommandBase
{
    protected override string WindowTitle => "Calçadas, meios-fios e canteiros";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Urbanizacao };
}
