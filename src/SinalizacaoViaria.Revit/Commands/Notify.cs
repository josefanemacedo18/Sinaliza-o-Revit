using Autodesk.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Mensagens ao usuário vindas das rotinas compartilhadas pelas ferramentas: janela do Revit normalmente; durante o
/// Autoteste são recolhidas no relatório (o teste não para esperando um clique).
/// </summary>
internal static class Notify
{
    public static List<string>? Sink { get; set; }

    public static bool Quiet => Sink != null;

    public static void Show(string text)
    {
        if (Sink != null) { Sink.Add(text); return; }
        TaskDialog.Show(CommandBase.AppTitle, text);
    }
}
