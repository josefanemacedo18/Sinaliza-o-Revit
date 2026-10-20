using System.Globalization;
using System.Text.RegularExpressions;

namespace SinalizacaoViaria.Core.Model;

/// <summary>
/// Nome dos tipos de piso que o plugin cria no Revit: "SV - {cor} {espessura} cm" (a espessura com a vírgula ou o ponto
/// da cultura do Windows). O nome promete a espessura: o plugin reaproveita o tipo pelo nome, então um tipo cuja estrutura
/// não bate com o nome (criado por versão antiga quando o Revit recusou a estrutura e ficou a do tipo base) gera pisos mais
/// grossos que o pedido – sobrepostos aos de baixo.
/// </summary>
public static partial class FloorTypeNames
{
    [GeneratedRegex(@"^SV - .+ (\d+(?:[.,]\d+)?) cm$")]
    private static partial Regex Pattern();

    /// <summary>Espessura (m) prometida pelo nome; nula se o tipo não é do plugin.</summary>
    public static double? Thickness(string? name)
    {
        if (name == null) return null;
        var m = Pattern().Match(name.Trim());
        if (!m.Success) return null;
        return double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cm) ? cm / 100 : null;
    }

    /// <summary>A estrutura (m) difere da espessura do nome em mais de meio milímetro.</summary>
    public static bool Mismatch(string? name, double structureWidthM) =>
        Thickness(name) is { } t && Math.Abs(structureWidthM - t) > 0.0005;
}
