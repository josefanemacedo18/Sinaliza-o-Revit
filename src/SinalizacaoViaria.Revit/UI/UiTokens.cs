using System.Windows;
using System.Windows.Media;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Tokens do design system (Etapa 9; Prompt de Interface §5, §22): as cores, os tamanhos de texto e o espaçamento das janelas
/// do plugin. O XAML usa as mesmas chaves em <c>Styles.xaml</c> (o teste <c>Etapa09InterfaceTests</c> confere que cada cor
/// daqui tem a mesma chave e o mesmo valor lá); o código usa esta classe. Cor nunca é o único sinal de um estado: o texto
/// diz o estado (aviso, erro, desatualizado).
/// </summary>
public static class UiTokens
{
    private static SolidColorBrush Make(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // ---- superfícies e texto
    /// <summary>Fundo das janelas e painéis (cinza muito claro).</summary>
    public static readonly SolidColorBrush Panel = Make("#F4F6F9");
    /// <summary>Superfícies, cartões e campos (branco).</summary>
    public static readonly SolidColorBrush Surface = Make("#FFFFFF");
    /// <summary>Linha alternada das tabelas.</summary>
    public static readonly SolidColorBrush AltRow = Make("#FAFBFD");
    /// <summary>Texto principal (cinza muito escuro).</summary>
    public static readonly SolidColorBrush Text = Make("#1B2330");
    /// <summary>Texto secundário (cinza médio; contraste ≥ 4,5 : 1 sobre branco).</summary>
    public static readonly SolidColorBrush TextSecondary = Make("#5F6B7A");
    /// <summary>Nome antigo do texto secundário (mesmo valor).</summary>
    public static readonly SolidColorBrush Muted = Make("#5F6B7A");
    /// <summary>Texto sobre a cor primária.</summary>
    public static readonly SolidColorBrush TextOnAccent = Make("#FFFFFF");
    /// <summary>Texto auxiliar sobre a cor primária.</summary>
    public static readonly SolidColorBrush TextOnAccentMuted = Make("#CFE0F5");
    /// <summary>Elementos desabilitados (cinza legível).</summary>
    public static readonly SolidColorBrush Disabled = Make("#8893A2");

    // ---- primária e seleção
    /// <summary>Cor primária (azul técnico): ações principais, foco, informação.</summary>
    public static readonly SolidColorBrush Accent = Make("#1F5FA8");
    /// <summary>Primária escura: títulos e cabeçalhos.</summary>
    public static readonly SolidColorBrush AccentDark = Make("#174A83");
    /// <summary>Seleção ativa (borda).</summary>
    public static readonly SolidColorBrush Selection = Make("#CFE0F5");
    /// <summary>Seleção ativa e cabeçalhos de grupo (fundo).</summary>
    public static readonly SolidColorBrush SelectionBackground = Make("#E8EFF8");

    // ---- bordas
    public static readonly SolidColorBrush Border = Make("#D5DBE3");
    /// <summary>Linhas de grade e divisões leves.</summary>
    public static readonly SolidColorBrush BorderSubtle = Make("#E6EAF0");
    /// <summary>Borda de amostras e controles pequenos.</summary>
    public static readonly SolidColorBrush BorderStrong = Make("#8893A2");

    // ---- estados (sempre com o texto do estado ao lado)
    /// <summary>Ação positiva / atualizado (verde discreto).</summary>
    public static readonly SolidColorBrush Success = Make("#2E7D32");
    public static readonly SolidColorBrush SuccessBackground = Make("#EAF4EA");
    /// <summary>Aviso / desatualizado (âmbar escuro, legível sobre branco).</summary>
    public static readonly SolidColorBrush Warning = Make("#8F5D00");
    public static readonly SolidColorBrush WarningBackground = Make("#FFF4DD");
    /// <summary>Nome antigo do aviso (mesmo valor).</summary>
    public static readonly SolidColorBrush Warn = Make("#8F5D00");
    /// <summary>Erro (vermelho controlado).</summary>
    public static readonly SolidColorBrush Error = Make("#B3261E");
    public static readonly SolidColorBrush ErrorBackground = Make("#FCEBEA");

    // ---- desenho
    /// <summary>Fundo das miniaturas (asfalto), onde a sinalização aparece como na via.</summary>
    public static readonly SolidColorBrush Asphalt = Make("#3E4248");

    /// <summary>
    /// Cores de destaque dos indicadores (cartões do Quantitativo): identificam a categoria (pintura amarela, placas
    /// vermelhas...), sempre com o nome do indicador ao lado.
    /// </summary>
    public static class Indicadores
    {
        public static readonly SolidColorBrush Elementos = Accent;
        public static readonly SolidColorBrush Pintura = Make("#E0A800");
        public static readonly SolidColorBrush ExtensaoPintada = Make("#F2F2F2");
        public static readonly SolidColorBrush Pavimento = Asphalt;
        public static readonly SolidColorBrush Placas = Make("#C62828");
        public static readonly SolidColorBrush ElementosUrbanos = Make("#4E8F3A");
        public static readonly SolidColorBrush Tinta = Make("#7A5AA6");
        public static readonly SolidColorBrush Corte = Make("#B5651D");
        public static readonly SolidColorBrush Aterro = Make("#6D8B3A");
    }

    // ---- tipografia (Segoe UI, a fonte do Windows e do Revit)
    public const string FontFamily = "Segoe UI";
    /// <summary>Título da janela.</summary>
    public const double FontTitle = 15;
    /// <summary>Título de seção.</summary>
    public const double FontSection = 13;
    /// <summary>Texto de formulário, rótulos e valores.</summary>
    public const double FontBody = 12;
    /// <summary>Informação auxiliar, legendas e status.</summary>
    public const double FontCaption = 11;
    /// <summary>Valor destacado de um indicador.</summary>
    public const double FontValueLarge = 19;

    // ---- espaçamento (escala de 4)
    public const double SpaceXs = 2, SpaceS = 4, SpaceM = 8, SpaceL = 12, SpaceXl = 16, SpaceXxl = 24;
    public static readonly Thickness CardPadding = new(SpaceL);
    public static readonly Thickness SectionMargin = new(0, SpaceL, 0, SpaceS);
}
