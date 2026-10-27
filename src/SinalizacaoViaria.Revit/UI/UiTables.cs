using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Tabela só de leitura das telas dos módulos (Etapa 11): colunas de texto com quebra de linha e larguras proporcionais. Sem
/// ordenar pelo cabeçalho: os números estão em texto (pt-BR) e a ordem alfabética os embaralharia ("10,0" antes de "9,0").
/// </summary>
internal static class UiTables
{
    /// <param name="colunas">Cabeçalho, propriedade da linha e peso da largura.</param>
    public static DataGrid Grid<T>(IReadOnlyList<T> linhas, double altura, bool multipla, params (string Header, string Path, double Peso)[] colunas)
    {
        var g = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = multipla ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single, MaxHeight = altura, Margin = new Thickness(0, 2, 0, 4),
            ItemsSource = linhas,
        };
        foreach (var (header, path, peso) in colunas)
        {
            var estilo = new Style(typeof(TextBlock));
            estilo.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            g.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = new DataGridLength(peso, DataGridLengthUnitType.Star), ElementStyle = estilo });
        }
        return g;
    }
}
