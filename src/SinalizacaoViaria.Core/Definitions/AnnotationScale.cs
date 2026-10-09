namespace SinalizacaoViaria.Core.Definitions;

/// <summary>
/// Detalhamento dimensionado em milímetros de papel × escala da vista: textos (notas nativas, que já seguem a escala),
/// símbolos, quadros, cotas e perfis. Quando a escala de uma vista muda, os detalhes dela são refeitos com a nova escala, em
/// volta do mesmo ponto de inserção.
/// </summary>
public static class AnnotationScale
{
    /// <summary>
    /// Detalhes desenhados na vista <paramref name="viewId"/> (UniqueId) – os que dependem da escala –, na ordem de geração
    /// (os que apontam para uma marca antes dos que leem o projeto inteiro).
    /// </summary>
    public static List<MarkingDefinition> ToRescale(IEnumerable<MarkingDefinition> defs, string viewId) =>
        defs.Where(d => d is IAnnotationDefinition && !string.IsNullOrEmpty(viewId) && d.Output.ViewId == viewId)
            .GroupBy(d => d.Id).Select(g => g.First())
            .OrderBy(d => d is IProjectWideAnnotation ? 1 : 0)
            .ThenBy(d => d is SectionProfileDefinition ? 1 : 0)
            .ToList();
}
