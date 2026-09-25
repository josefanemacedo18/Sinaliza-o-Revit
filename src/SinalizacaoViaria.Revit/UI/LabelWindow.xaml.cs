using System.Windows;
using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Opções das anotações com linha de chamada.</summary>
public partial class LabelWindow : Window
{
    private readonly LabelDefinition? _existing;

    public LabelDefinition? Result { get; private set; }

    public LabelWindow(LabelDefinition? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        var d = existing ?? UiHelpers.Remembered<LabelDefinition>("Anotacao") ?? new LabelDefinition();
        TbText.Text = UiHelpers.F(d.TextMm, "0.#");
        CkName.IsChecked = d.ShowName;
        CkDetails.IsChecked = d.ShowDetails;
        TbCustom.Text = existing?.CustomText ?? "";
        if (existing != null)
        {
            Title = "Editar anotação";
            BtnOk.Content = "Aplicar";
            TxtHint.Text = "Para mover a chamada, apague a anotação e crie outra (ou copie/mova os elementos – a cópia se torna uma anotação independente).";
        }
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var d = _existing != null ? (LabelDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new LabelDefinition();
            d.TextMm = UiHelpers.Parse(TbText, 2, "Altura do texto", 0.8, 20);
            d.ShowName = CkName.IsChecked == true;
            d.ShowDetails = CkDetails.IsChecked == true;
            d.CustomText = string.IsNullOrWhiteSpace(TbCustom.Text) ? null : TbCustom.Text.Replace("\r\n", "\n").Trim();
            if (_existing == null)
            {
                var memo = (LabelDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                memo.CustomText = null;
                UiHelpers.Remember("Anotacao", memo);
            }
            Result = d;
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
