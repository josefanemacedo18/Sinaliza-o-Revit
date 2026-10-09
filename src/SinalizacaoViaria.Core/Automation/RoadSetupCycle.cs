namespace SinalizacaoViaria.Core.Automation;

/// <summary>Ciclofaixa presa à via: entra na seção transversal (lado + afastamento do meio-fio) e é refeita com a via.</summary>
public sealed partial class RoadSetup
{
    /// <summary>Largura mínima da faixa de rolamento que cede espaço à ciclofaixa (m) [a confirmar].</summary>
    public const double FaixaMinimaComCiclofaixa = 2.70;

    /// <summary>
    /// Insere a ciclofaixa <paramref name="ciclo"/> num lado da seção. <paramref name="afastamento"/> é a distância da face do
    /// meio-fio à borda externa da ciclofaixa: os elementos junto ao meio-fio que cabem nela (estacionamento, faixa de
    /// segurança...) ficam por fora e a diferença vira faixa de segurança zebrada entre eles e a ciclofaixa. Com
    /// <paramref name="ocuparPista"/> a faixa de rolamento vizinha cede a largura (o meio-fio fica no lugar) enquanto ficar com
    /// pelo menos <see cref="FaixaMinimaComCiclofaixa"/>; senão (ou sem a opção) a pista alarga. Devolve os avisos.
    /// </summary>
    public List<string> InserirCiclofaixa(bool esquerda, ElementoSecao ciclo, double afastamento = 0, bool ocuparPista = true)
    {
        var msgs = new List<string>();
        var side = esquerda ? Left : Right;
        if (ciclo.Tipo != TipoElementoSecao.FaixaCaminhada) ciclo.Tipo = TipoElementoSecao.Ciclofaixa;
        ciclo.Largura = Math.Max(0.8, ciclo.Largura);
        afastamento = Math.Max(0, afastamento);
        var curb = side.FindIndex(e => e.Tipo == TipoElementoSecao.Calcada);
        if (curb < 0) curb = side.Count;
        // Do meio-fio para dentro: os elementos que cabem no afastamento ficam por fora da ciclofaixa.
        var i = curb;
        double acc = 0;
        while (i > 0 && !ElementoSecao.EhFaixaDeTrafego(side[i - 1].Tipo) && side[i - 1].Tipo != TipoElementoSecao.CanteiroFisico
               && acc + side[i - 1].Largura <= afastamento + 0.01)
        {
            acc += side[i - 1].Largura;
            i--;
        }
        var gap = Math.Round(Math.Max(0, afastamento - acc), 2);
        side.Insert(i, ciclo);
        var added = ciclo.Largura;
        if (gap >= 0.05)
        {
            side.Insert(i + 1, new ElementoSecao { Tipo = TipoElementoSecao.FaixaSeguranca, Largura = gap });
            added += gap;
            msgs.Add($"Faixa de segurança zebrada de {gap:0.00} m entre a ciclofaixa e o que fica junto ao meio-fio (afastamento pedido).");
        }
        if (ocuparPista)
        {
            var lane = side.Take(i).LastOrDefault(e => ElementoSecao.EhFaixaDeTrafego(e.Tipo));
            if (lane != null && lane.Largura - added >= FaixaMinimaComCiclofaixa - 1e-6)
            {
                lane.Largura = Math.Round(lane.Largura - added, 2);
                msgs.Add($"A faixa de rolamento vizinha passou a {lane.Largura:0.00} m; o meio-fio ficou no lugar.");
            }
            else
                msgs.Add($"A faixa de rolamento ficaria com menos de {FaixaMinimaComCiclofaixa:0.00} m [a confirmar] – a pista foi alargada em {added:0.00} m.");
        }
        else msgs.Add($"A pista foi alargada em {added:0.00} m (o meio-fio se afasta do eixo).");
        return msgs;
    }
}

public sealed partial class BikeLaneSetup
{
    /// <summary>Elemento da seção equivalente a estas opções (ciclofaixa ou faixa de caminhada na seção da via).</summary>
    public ElementoSecao ToElemento() => new()
    {
        Tipo = Type == TipoCiclo.FaixaCaminhada ? TipoElementoSecao.FaixaCaminhada : TipoElementoSecao.Ciclofaixa,
        Largura = Math.Max(0.8, Width),
        PinturaFundo = Background,
        Bidirecional = Type == TipoCiclo.CiclofaixaBidirecional,
        LinhaCentral = Type == TipoCiclo.CiclofaixaBidirecional,
        LinhaSeccionada = DashedLines,
        LarguraLinha = LineWidth,
        Dispositivo = Segregation,
        Espacamento = SymbolSpacing,
        TamanhoSimbolo = SymbolLength,
        TamanhoSeta = Arrows ? ArrowLength : 0,
        CorCaminhada = WalkColor,
        CruzamentoColorido = CrossingColor,
        CorCruzamento = CrossingColorValue == Model.MarkingColor.Vermelha ? null : CrossingColorValue,
        ZonaConflito = Math.Abs(ConflictZone - 20) < 1e-9 ? null : ConflictZone,
    };
}
