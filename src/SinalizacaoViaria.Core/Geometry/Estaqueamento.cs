using System.Globalization;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>Equação de estaca: na distância <see cref="Distancia"/> ao longo do eixo, a estaca à frente passa a valer <see cref="Frente"/> (m).</summary>
public sealed class EquacaoEstaca
{
    public double Distancia { get; set; }
    public double Frente { get; set; }
}

/// <summary>
/// Estaqueamento de uma via (Motores §7): estaca do início do eixo, intervalo entre estacas inteiras e equações de estaca
/// ("estaca atrás = estaca à frente"). Dentro das definições toda estaca continua sendo a <b>distância ao longo do eixo</b> a
/// partir do início (D-023): o estaqueamento só converte essa distância na estaca mostrada e lida pelo usuário. O padrão (0,
/// 20 m, sem equações) é o que o plugin sempre usou.
/// </summary>
public sealed class Estaqueamento
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Valor (m) da estaca no início do eixo: 2000 = estaca 100+0,00 com intervalo de 20 m.</summary>
    public double Inicio { get; set; }

    /// <summary>Distância entre estacas inteiras (m).</summary>
    public double Intervalo { get; set; } = 20;

    /// <summary>Equações de estaca, guardadas pela distância ao longo do eixo.</summary>
    public List<EquacaoEstaca> Equacoes { get; set; } = new();

    public bool EhPadrao => Math.Abs(Inicio) < 1e-9 && Math.Abs(Intervalo - 20) < 1e-9 && Equacoes.Count == 0;

    public Estaqueamento Clone() => new()
    {
        Inicio = Inicio, Intervalo = Intervalo,
        Equacoes = Equacoes.Select(e => new EquacaoEstaca { Distancia = e.Distancia, Frente = e.Frente }).ToList(),
    };

    private List<EquacaoEstaca> Ordenadas() => Equacoes.Where(e => double.IsFinite(e.Distancia) && double.IsFinite(e.Frente))
        .OrderBy(e => e.Distancia).ToList();

    /// <summary>Valor da estaca (m) na distância ao longo do eixo; na própria equação vale a estaca à frente.</summary>
    public double Valor(double distancia)
    {
        var v = Inicio + distancia;
        foreach (var e in Ordenadas())
        {
            if (e.Distancia > distancia + 1e-9) break;
            v = e.Frente + (distancia - e.Distancia);
        }
        return v;
    }

    /// <summary>Estaca atrás da equação: o valor que vinha do trecho anterior, na distância da equação.</summary>
    public double ValorAtras(EquacaoEstaca eq)
    {
        var v = Inicio + eq.Distancia;
        foreach (var e in Ordenadas())
        {
            if (e.Distancia >= eq.Distancia - 1e-9) break;
            v = e.Frente + (eq.Distancia - e.Distancia);
        }
        return v;
    }

    /// <summary>Estaca no formato "n+resto" (vírgula decimal).</summary>
    public string Formatar(double valor)
    {
        var passo = Intervalo > 0 ? Intervalo : 20;
        var n = Math.Floor(valor / passo + 1e-9);
        return $"{n:0}+{(valor - n * passo).ToString("0.00", Pt)}";
    }

    /// <summary>Estaca mostrada na distância ao longo do eixo.</summary>
    public string Rotulo(double distancia) => Formatar(Valor(distancia));

    /// <summary>Lê "n+resto" (intervalo da via) ou um número em metros como VALOR da estaca; nulo se não for número.</summary>
    public double? Ler(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim().Replace(" ", "");
        static double? Num(string x) =>
            double.TryParse(x.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
        var k = t.IndexOf('+');
        if (k > 0 && Num(t[..k]) is { } n && Num(t[(k + 1)..]) is { } resto) return n * (Intervalo > 0 ? Intervalo : 20) + resto;
        return Num(t);
    }

    /// <summary>
    /// Distância ao longo do eixo (0 a <paramref name="comprimento"/>) onde a estaca vale <paramref name="valor"/>; nula se o
    /// valor cair numa lacuna das equações ou fora do eixo. Valor repetido (equação que volta) = o primeiro trecho.
    /// </summary>
    public double? Distancia(double valor, double comprimento)
    {
        var eqs = Ordenadas().Where(e => e.Distancia > 1e-9 && e.Distancia < comprimento - 1e-9).ToList();
        var inicios = new List<(double D, double V)> { (0, Inicio) };
        inicios.AddRange(eqs.Select(e => (e.Distancia, e.Frente)));
        for (int i = 0; i < inicios.Count; i++)
        {
            var (d0, v0) = inicios[i];
            var d1 = i + 1 < inicios.Count ? inicios[i + 1].D : comprimento;
            if (valor >= v0 - 1e-9 && valor <= v0 + (d1 - d0) + 1e-9) return Math.Clamp(d0 + (valor - v0), d0, d1);
        }
        return null;
    }

    /// <summary>Equações no texto "estaca atrás = estaca à frente; …" (a forma do levantamento).</summary>
    public string EquacoesTexto() => string.Join("; ", Ordenadas().Select(e => $"{Formatar(ValorAtras(e))} = {Formatar(e.Frente)}"));

    /// <summary>
    /// Lê equações no texto "estaca atrás = estaca à frente; …", em ordem ao longo do eixo; a estaca atrás é localizada com o
    /// estaqueamento das equações anteriores. Devolve os problemas de leitura (lista vazia = tudo lido).
    /// </summary>
    public List<string> LerEquacoes(string? texto, double comprimento)
    {
        var problemas = new List<string>();
        Equacoes = new List<EquacaoEstaca>();
        if (string.IsNullOrWhiteSpace(texto)) return problemas;
        foreach (var parte in texto.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var lados = parte.Split('=', StringSplitOptions.TrimEntries);
            if (lados.Length != 2 || Ler(lados[0]) is not { } atras || Ler(lados[1]) is not { } frente)
            {
                problemas.Add($"\"{parte}\": use \"estaca atrás = estaca à frente\" (ex.: 15+12,40 = 16+0,00)");
                continue;
            }
            if (Distancia(atras, comprimento) is not { } d || (Equacoes.Count > 0 && d <= Equacoes[^1].Distancia + 1e-6))
            {
                problemas.Add($"\"{parte}\": a estaca {Formatar(atras)} não está no eixo depois da equação anterior");
                continue;
            }
            Equacoes.Add(new EquacaoEstaca { Distancia = d, Frente = frente });
        }
        return problemas;
    }

    /// <summary>O que impede usar o estaqueamento ou merece atenção (equação que repete estacas).</summary>
    public List<string> Problemas(double comprimento)
    {
        var res = new List<string>();
        if (!(Intervalo > 0) || !double.IsFinite(Intervalo)) res.Add("o intervalo entre estacas deve ser maior que zero");
        if (!double.IsFinite(Inicio)) res.Add("a estaca inicial não é um número");
        var eqs = Ordenadas();
        if (eqs.Count != Equacoes.Count) res.Add("equação com valor não numérico");
        foreach (var e in eqs.Where(e => e.Distancia <= 1e-9 || e.Distancia >= comprimento - 1e-9))
            res.Add($"equação na distância {e.Distancia.ToString("0.00", Pt)} m fora do eixo ({comprimento.ToString("0.00", Pt)} m)");
        for (int i = 1; i < eqs.Count; i++)
            if (Math.Abs(eqs[i].Distancia - eqs[i - 1].Distancia) < 1e-6) res.Add($"duas equações na mesma distância ({eqs[i].Distancia.ToString("0.00", Pt)} m)");
        foreach (var e in eqs.Where(e => e.Frente < ValorAtras(e) - 1e-9))
            res.Add($"a equação {Formatar(ValorAtras(e))} = {Formatar(e.Frente)} repete estacas: ler uma estaca repetida usa o primeiro trecho");
        return res;
    }
}

/// <summary>
/// A estaca que o usuário vê e digita numa via (K20; D-023): as definições guardam a distância ao longo do eixo; os
/// formulários e os avisos mostram e leem a estaca do estaqueamento da via (início, intervalo, equações). No padrão (0, 20 m,
/// sem equações) é o "n+resto" de sempre e um número digitado é a própria distância.
/// </summary>
public sealed class EstacasDaVia
{
    private static readonly Estaqueamento Base = new();

    /// <summary>O padrão (estaca 0 no início do eixo, a cada 20 m).</summary>
    public static EstacasDaVia Padrao { get; } = new(null);

    /// <param name="estaqueamento">O da via; nulo ou padrão = o padrão.</param>
    /// <param name="comprimento">Comprimento do eixo (m), para achar a distância de uma estaca depois de equações; infinito = desconhecido.</param>
    public EstacasDaVia(Estaqueamento? estaqueamento, double comprimento = double.PositiveInfinity)
    {
        Estaqueamento = estaqueamento is { EhPadrao: false } e ? e.Clone() : null;
        Comprimento = double.IsFinite(comprimento) && comprimento > 0 ? comprimento : double.PositiveInfinity;
    }

    /// <summary>O estaqueamento próprio da via; nulo = o padrão.</summary>
    public Estaqueamento? Estaqueamento { get; }
    public double Comprimento { get; }
    public bool EhPadrao => Estaqueamento == null;

    /// <summary>A estaca mostrada para a distância ao longo do eixo.</summary>
    public string Mostrar(double distancia) => (Estaqueamento ?? Base).Rotulo(distancia);

    /// <summary>
    /// A distância ao longo do eixo da estaca digitada ("n+resto" ou o valor em metros). No padrão, o valor é a distância; com o
    /// estaqueamento da via, nulo se a estaca não estiver no eixo (antes do início ou na lacuna de uma equação).
    /// </summary>
    public double? Ler(string? texto)
    {
        if ((Estaqueamento ?? Base).Ler(texto) is not { } v) return null;
        return Estaqueamento == null ? v : Estaqueamento.Distancia(v, Comprimento);
    }

    /// <summary>Como digitar, para o rótulo do campo.</summary>
    public string Formato => Estaqueamento == null ? "m ou 5+10" : $"estaca da via, ex.: {Mostrar(0)}";
}
