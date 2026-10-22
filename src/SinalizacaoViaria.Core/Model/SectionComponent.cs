namespace SinalizacaoViaria.Core.Model;

/// <summary>
/// Componentes da seção da via no quantitativo por componente (Fase VI): pavimento, meio-fio, sarjeta, calçada, gramado,
/// ilhas. A peça diz o seu pela camada (<see cref="MarkingPiece.Layer"/>) ou, quando a camada não diz, pela marcação
/// <see cref="MarkingPiece.Component"/> que o gerador põe (meio-fio da ilha, miolo do canteiro).
/// </summary>
public static class SectionComponent
{
    public const string Pavimento = "PAVIMENTO";
    public const string MeioFio = "MEIO-FIO";
    public const string Sarjeta = "SARJETA";
    public const string Sarjetao = "SARJETAO";
    public const string Calcada = "CALCADA";
    public const string Gramado = "GRAMADO";
    public const string Ilha = "ILHA";
    public const string Galgavel = "GALGAVEL";
    public const string Outros = "OUTROS";

    /// <summary>Ordem no quadro (da pista para fora).</summary>
    public static readonly string[] All = { Pavimento, Galgavel, Sarjeta, Sarjetao, MeioFio, Calcada, Gramado, Ilha, Outros };

    public static string Label(string code) => code switch
    {
        Pavimento => "Pavimento (pista e áreas pavimentadas)",
        MeioFio => "Meio-fio (guia)",
        Sarjeta => "Sarjeta",
        Sarjetao => "Sarjetão",
        Calcada => "Calçada (passeio)",
        Gramado => "Faixa gramada e canteiros gramados",
        Ilha => "Ilhas, canteiros e divisores pavimentados",
        Galgavel => "Faixa galgável da rotatória",
        _ => "Outros elementos físicos da seção",
    };

    /// <summary>Componente pelo código de uma camada ou de uma marca linear (MEIO-FIO-12, SARJETAO...); nulo se não é um.</summary>
    public static string? FromCode(string? code) => code switch
    {
        null or "" => null,
        "SARJETAO" => Sarjetao,
        "SARJETA" => Sarjeta,
        "CALCADA" => Calcada,
        "GRAMADO" => Gramado,
        _ when code.StartsWith("MEIO-FIO", StringComparison.Ordinal) => MeioFio,
        _ => null,
    };
}
