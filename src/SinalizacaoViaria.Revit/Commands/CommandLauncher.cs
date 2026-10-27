using Autodesk.Revit.UI;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Comandos do plugin pelo nome da classe – os que a Central do Projeto e o painel contextual oferecem (o núcleo os nomeia,
/// sem conhecer a camada Revit). Executa pelo <see cref="CommandBase.Invoke"/>: as mesmas validações e mensagens do botão.
/// </summary>
public static class CommandCatalog
{
    public static Type? Find(string name) =>
        typeof(CommandBase).Assembly.GetType($"{typeof(CommandBase).Namespace}.{name}") is { IsAbstract: false } t && typeof(CommandBase).IsAssignableFrom(t) ? t : null;

    /// <summary>Executa o comando (só num contexto válido da API: dentro de um comando ou de um ExternalEvent).</summary>
    public static Result Run(string name, UIApplication app)
    {
        if (Find(name) is not { } t)
        {
            Log.Error("Comando pelo nome", new InvalidOperationException($"comando {name} não existe"));
            TaskDialog.Show(CommandBase.AppTitle, $"O comando {name} não foi encontrado nesta versão do plugin.");
            return Result.Failed;
        }
        return ((CommandBase)Activator.CreateInstance(t)!).Invoke(app);
    }
}

/// <summary>
/// Executa, fora do clique do painel contextual (janela não modal), o comando escolhido nele: o clique pede, o Revit chama
/// <see cref="Execute"/> quando a API está disponível. Um pedido por vez; um novo clique antes troca o pedido.
/// </summary>
public sealed class PaneCommandHandler : IExternalEventHandler
{
    private string? _pending;

    public void Request(string command) => _pending = command;

    public void Execute(UIApplication app)
    {
        var c = _pending;
        _pending = null;
        if (c == null) return;
        try { CommandCatalog.Run(c, app); }
        catch (Exception ex) { Log.Error($"Painel contextual – {c}", ex); }
    }

    public string GetName() => "SinalizaBIM – comando do painel contextual";
}
