using System.Globalization;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>De onde vem um limite usado nos avisos dos geradores.</summary>
public enum OrigemLimite
{
    /// <summary>O próprio gerador precisa desse espaço para desenhar a peça (não é norma).</summary>
    Geometrico,
    /// <summary>Valor de referência para conferência. Sem <see cref="LimiteDeConferencia.Fonte"/>, não há norma citada.</summary>
    Referencia,
}

/// <summary>
/// Limite de um aviso: nome, valor, unidade, de onde vem e a fonte (nula = o plugin não cita norma para ele). O aviso diz
/// isso ao usuário – um limite sem fonte nunca é apresentado como norma.
/// </summary>
public sealed record LimiteDeConferencia(string Nome, double Valor, string Unidade, OrigemLimite Origem, string? Fonte, string Onde)
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public string ValorTexto => Valor.ToString("0.##", Pt) + (Unidade == "°" ? "°" : " " + Unidade);

    /// <summary>Como o aviso cita o limite: a fonte, que é o que o gerador precisa, ou que não há norma citada.</summary>
    public string Rotulo => Origem == OrigemLimite.Geometrico
        ? $"{ValorTexto}: espaço que o plugin precisa para desenhar a peça"
        : Fonte != null ? $"{ValorTexto} ({Fonte})" : $"{ValorTexto}: limite de conferência do plugin, sem norma citada – confira com a norma adotada";
}

/// <summary>
/// Registro dos limites citados nos avisos de interseções, rotatórias, canalização e obras (Prompt Master §13: sem valores
/// mágicos espalhados; regra do projeto: nenhuma norma inventada). Os valores são os que os geradores já usavam; a auditoria
/// da Etapa 4 não achou fonte para os de referência e os rotula assim.
/// </summary>
public static class LimitesDeConferencia
{
    public static readonly LimiteDeConferencia PistaGiratoriaMinima =
        new("Largura mínima da pista giratória", 4.0, "m", OrigemLimite.Referencia, null, "RoundaboutGenerator");
    public static readonly LimiteDeConferencia MinirrotatoriaDiametroMaximo =
        new("Diâmetro inscrito máximo da minirrotatória", 28, "m", OrigemLimite.Referencia, null, "RoundaboutGenerator");
    public static readonly LimiteDeConferencia IlhaCentralMinima =
        new("Raio mínimo da ilha central elevada", 4.0, "m", OrigemLimite.Referencia, null, "RoundaboutGenerator");
    public static readonly LimiteDeConferencia PistaParaIlhaSeparadora =
        new("Pista do ramo para a ilha separadora (6 m de faixas + 0,80 m de ilha)", 6.8, "m", OrigemLimite.Geometrico, null, "RoundaboutGenerator");
    public static readonly LimiteDeConferencia CanteiroParaBolsao =
        new("Canteiro para o bolsão de conversão (2,70 m + separador de 0,30 m)", 3.0, "m", OrigemLimite.Geometrico, null, "IntersectionGenerator");
    public static readonly LimiteDeConferencia AnguloMinimoEntreVias =
        new("Ângulo mínimo entre as vias sem canalização", 30, "°", OrigemLimite.Referencia, null, "IntersectionGenerator");
    public static readonly LimiteDeConferencia TransicaoObstaculoUrbana =
        new("Transição mínima junto a obstáculo (via urbana)", 30, "m", OrigemLimite.Referencia, null, "ChannelizationGenerator");
    public static readonly LimiteDeConferencia TransicaoObstaculoRodovia =
        new("Transição mínima junto a obstáculo (rodovia)", 60, "m", OrigemLimite.Referencia, null, "ChannelizationGenerator");
    public static readonly LimiteDeConferencia EixoMinimoDaObra =
        new("Eixo mínimo da obra de arte", 10, "m", OrigemLimite.Geometrico, null, "BridgeGenerator");
    public static readonly LimiteDeConferencia TrechoMinimoDaObra =
        new("Trecho mínimo da obra na via", 5, "m", OrigemLimite.Geometrico, null, "BridgeGenerator");

    public static IReadOnlyList<LimiteDeConferencia> Todos { get; } = new[]
    {
        PistaGiratoriaMinima, MinirrotatoriaDiametroMaximo, IlhaCentralMinima, PistaParaIlhaSeparadora, CanteiroParaBolsao,
        AnguloMinimoEntreVias, TransicaoObstaculoUrbana, TransicaoObstaculoRodovia, EixoMinimoDaObra, TrechoMinimoDaObra,
    };
}
