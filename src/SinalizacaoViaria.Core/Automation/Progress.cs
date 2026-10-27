using System.Diagnostics;
using System.Globalization;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Estado de uma operação demorada (Etapa 13; Prompt de Interface §21): a etapa, o que está sendo feito, quantos de quantos
/// (só quando se sabe) e se cancelar ainda é seguro. Sem total conhecido não há percentual – a janela mostra só que está
/// trabalhando.
/// </summary>
public sealed record EstadoProgresso(string Etapa, string? Descricao = null, int? Feitos = null, int? Total = null, bool Cancelavel = true)
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Fração de 0 a 1 dos itens processados; nula sem total (indicador de atividade, sem percentual inventado).</summary>
    public double? Fracao => Total is > 0 && Feitos is { } f ? Math.Clamp((double)f / Total.Value, 0, 1) : null;

    /// <summary>"12 de 300 marcas" quando o total é conhecido, "12 marcas" quando não; vazio sem contagem.</summary>
    public string Contagem(string unidade) => Feitos is not { } f ? ""
        : Total is { } t ? $"{f.ToString("N0", Pt)} de {t.ToString("N0", Pt)} {unidade}" : $"{f.ToString("N0", Pt)} {unidade}";
}

/// <summary>Quem mostra o progresso de uma operação e diz se o usuário pediu para cancelar.</summary>
public interface IProgressoOperacao
{
    void Informar(EstadoProgresso estado);

    /// <summary>O usuário pediu para cancelar; a operação para no próximo ponto seguro.</summary>
    bool Cancelado { get; }
}

/// <summary>Cancelamento pedido pelo usuário num ponto seguro da operação (antes de gravar): nada foi alterado.</summary>
public sealed class OperacaoCanceladaException : Exception
{
    public OperacaoCanceladaException(string operacao) : base($"{operacao}: cancelado pelo usuário – nada foi alterado.") { }
}

/// <summary>
/// Limita as atualizações da janela de progresso (cada uma redesenha a janela): no máximo uma a cada <paramref name="intervaloMs"/>,
/// mais a primeira, a última e cada troca de etapa (uma etapa longa sem contagem não fica com o rótulo da anterior). O relógio é
/// injetável para teste.
/// </summary>
public sealed class LimitadorDeProgresso
{
    private readonly double _intervalo;
    private readonly Func<double> _agoraMs;
    private double? _ultima;
    private string? _etapa;

    public LimitadorDeProgresso(double intervaloMs = 100, Func<double>? agoraMs = null)
    {
        _intervalo = intervaloMs;
        if (agoraMs == null)
        {
            var sw = Stopwatch.StartNew();
            agoraMs = () => sw.Elapsed.TotalMilliseconds;
        }
        _agoraMs = agoraMs;
    }

    /// <summary>Se já é hora de atualizar: sempre na primeira chamada, numa etapa nova e com <paramref name="ultima"/>.</summary>
    public bool Deve(string etapa, bool ultima = false)
    {
        var agora = _agoraMs();
        if (!ultima && etapa == _etapa && _ultima is { } u && agora - u < _intervalo) return false;
        _ultima = agora;
        _etapa = etapa;
        return true;
    }
}

/// <summary>Atalhos para os laços das operações: informar com o limitador e parar no ponto seguro se o usuário cancelou.</summary>
public static class Progresso
{
    /// <summary>
    /// Ponto seguro: lança <see cref="OperacaoCanceladaException"/> se o usuário cancelou; senão informa o item
    /// <paramref name="feitos"/> de <paramref name="total"/> (nulos = etapa sem contagem), com o limitador.
    /// </summary>
    public static void Passo(IProgressoOperacao? p, LimitadorDeProgresso limite, string operacao, string etapa, int? feitos = null, int? total = null, string? descricao = null)
    {
        if (p == null) return;
        if (p.Cancelado) throw new OperacaoCanceladaException(operacao);
        if (limite.Deve(etapa, total is { } t && feitos >= t)) p.Informar(new EstadoProgresso(etapa, descricao, feitos, total));
    }

    /// <summary>
    /// Informa uma etapa em que não dá mais para cancelar (depois de gravar a transação): nunca lança, mesmo que o usuário tenha
    /// pedido para cancelar – o pedido vale só até o ponto seguro.
    /// </summary>
    public static void SemCancelar(IProgressoOperacao? p, LimitadorDeProgresso limite, string etapa, int? feitos = null, int? total = null, string? descricao = null)
    {
        if (p != null && limite.Deve(etapa, total is { } t && feitos >= t))
            p.Informar(new EstadoProgresso(etapa, descricao, feitos, total, Cancelavel: false));
    }
}
