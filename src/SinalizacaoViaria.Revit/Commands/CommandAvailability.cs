using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Comando que funciona sem projeto aberto (Configurações, Catálogo, Normas / Sobre): o <see cref="CommandBase"/> não exige o
/// documento e o botão da faixa fica habilitado sempre (<see cref="AlwaysAvailable"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NoDocumentRequiredAttribute : Attribute
{
    public static bool On(Type cmd) => IsDefined(cmd, typeof(NoDocumentRequiredAttribute), false);
}

/// <summary>
/// Botão habilitado só com um projeto (.rvt) ativo – desabilitado sem documento e no editor de famílias, as mesmas condições
/// que o <see cref="CommandBase"/> exige (Prompt de Interface §7: estados coerentes). Revit chama a cada atualização da faixa:
/// a conferência é só a do documento ativo.
/// </summary>
public sealed class ProjectDocumentAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) =>
        applicationData.ActiveUIDocument?.Document is { IsFamilyDocument: false };
}

/// <summary>Botão sempre habilitado (comandos com <see cref="NoDocumentRequiredAttribute"/>).</summary>
public sealed class AlwaysAvailable : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
