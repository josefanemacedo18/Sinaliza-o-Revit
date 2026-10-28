using System.Text.Json;
using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>O que um componente por trecho faz com a seção entre as estacas A e B.</summary>
public enum OperacaoComponente
{
    /// <summary>Acrescenta um elemento (faixa, ciclofaixa, estacionamento, canteiro, calçada...) numa posição do lado.</summary>
    Inserir,
    /// <summary>Tira um elemento do lado.</summary>
    Remover,
    /// <summary>Muda a largura de um elemento do lado.</summary>
    Largura,
}

/// <summary>
/// Componente da via por trecho: entre o início (A) e o fim (B) um elemento de um lado da seção é acrescentado, tirado ou
/// muda de largura; antes de A e depois de B, transições do comprimento pedido levam de volta à seção. Os elementos de fora
/// se deslocam (a via se alarga ou estreita) e tudo é refeito a partir do eixo, como a largura por trecho.
/// </summary>
public sealed class ComponenteTrecho
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public OperacaoComponente Operacao { get; set; } = OperacaoComponente.Inserir;
    public bool LadoEsquerdo { get; set; }
    /// <summary>
    /// Posição no lado, a partir do eixo (0 = junto ao eixo ou ao canteiro central). Inserir: o elemento entra antes do elemento
    /// desta posição (= número de elementos do lado: depois do último); Remover e Largura: o elemento desta posição.
    /// </summary>
    public int Posicao { get; set; }
    /// <summary>
    /// Tipo do elemento na posição quando o trecho foi feito (Remover e Largura): se a seção mudar e a posição tiver outro tipo,
    /// o trecho é ignorado com aviso, em vez de mexer no elemento errado.
    /// </summary>
    public TipoElementoSecao? TipoNaPosicao { get; set; }
    /// <summary>Elemento acrescentado (Inserir).</summary>
    public ElementoSecao? Elemento { get; set; }
    /// <summary>Largura do elemento entre A e B (Largura).</summary>
    public double Largura { get; set; }
    /// <summary>Estaca do início (A) do trecho com a seção alterada (m).</summary>
    public double EstacaA { get; set; }
    /// <summary>Estaca do fim (B) do trecho (m).</summary>
    public double EstacaB { get; set; }
    /// <summary>Comprimento da transição antes de A (m).</summary>
    public double TransicaoA { get; set; } = 10;
    /// <summary>Comprimento da transição depois de B (m).</summary>
    public double TransicaoB { get; set; } = 10;
    /// <summary>Transições em curva S (suave); falso = retas.</summary>
    public bool CurvaS { get; set; }

    public ComponenteTrecho Clone()
    {
        var c = (ComponenteTrecho)MemberwiseClone();
        c.Elemento = Elemento?.Clone();
        return c;
    }

    [JsonIgnore] public double A => Math.Min(EstacaA, EstacaB);
    [JsonIgnore] public double B => Math.Max(EstacaA, EstacaB);
    [JsonIgnore] public double Inicio => A - Math.Max(0, TransicaoA);
    [JsonIgnore] public double Fim => B + Math.Max(0, TransicaoB);

    /// <summary>Fração da mudança na estaca: 0 fora do trecho e das transições, 1 entre A e B.</summary>
    public double Fator(double s) => TrechoLargura.FatorTransicao(s, A, B, TransicaoA, TransicaoB, CurvaS);

    /// <summary>Divide o trecho na estaca <paramref name="s"/> (entre A e B): duas partes com a mesma mudança, sem transição na emenda.</summary>
    public (ComponenteTrecho Antes, ComponenteTrecho Depois)? Dividir(double s)
    {
        if (s <= A + 0.5 || s >= B - 0.5) return null;
        var a = Clone();
        var b = Clone();
        b.Id = Guid.NewGuid().ToString("N")[..8];
        (a.EstacaA, a.EstacaB, a.TransicaoB) = (A, s, 0);
        (b.EstacaA, b.EstacaB, b.TransicaoA) = (s, B, 0);
        return (a, b);
    }

    public static string Rotulo(OperacaoComponente o) => o switch
    {
        OperacaoComponente.Inserir => "Acrescentar",
        OperacaoComponente.Remover => "Tirar",
        _ => "Mudar a largura",
    };

    /// <summary>Descrição curta ("Acrescentar Ciclofaixa 1,50 m à direita, posição 2, estacas …").</summary>
    public string Descricao(Func<double, string> estaca)
    {
        var lado = LadoEsquerdo ? "à esquerda" : "à direita";
        var o = Operacao switch
        {
            OperacaoComponente.Inserir => $"Acrescentar {ElementoSecao.Rotulo(Elemento?.Tipo ?? TipoElementoSecao.FaixaRolamento)} de {Elemento?.Largura ?? 0:0.00} m {lado} (antes do elemento {Posicao + 1})",
            OperacaoComponente.Remover => $"Tirar o elemento {Posicao + 1} {lado} ({ElementoSecao.Rotulo(TipoNaPosicao ?? TipoElementoSecao.FaixaRolamento)})",
            _ => $"Largura do elemento {Posicao + 1} {lado} ({ElementoSecao.Rotulo(TipoNaPosicao ?? TipoElementoSecao.FaixaRolamento)}) em {Largura:0.00} m",
        };
        return $"{o}: de {estaca(A)} a {estaca(B)}, transições de {TransicaoA:0.#} m e {TransicaoB:0.#} m";
    }
}

public sealed partial class RoadSetup
{
    /// <summary>Componentes da via por trecho (acrescentados, tirados ou com outra largura entre duas estacas). Vazio = nenhum.</summary>
    public List<ComponenteTrecho> ComponentesTrecho { get; set; } = new();

    /// <summary>
    /// Componente por trecho validado: zona da seção (distâncias ao eixo, do lado) que se deforma, quanto os elementos de fora
    /// dela se deslocam com o trecho pleno e, para Inserir/Remover, a seção do trecho (para as marcas próprias).
    /// </summary>
    private sealed record ComponenteAtivo(ComponenteTrecho T, bool Left, Zone Zona, double Delta, double Seam, RoadSetup? Secao);

    /// <summary>Marca própria de um componente por trecho (acrescentada só entre A e B): posição na seção do trecho.</summary>
    private sealed record MarcaDoTrecho(ComponenteAtivo C, int Sigma, double D0, double D1);

    /// <summary>Faixa sem pavimento acrescentada por um componente (canteiro físico, sarjeta da calçada nova).</summary>
    private sealed record VaoDoTrecho(ComponenteAtivo C, PavementGap Gap);

    /// <summary>Componentes por trecho válidos (avisos dos ignorados em <paramref name="warnings"/>).</summary>
    private List<ComponenteAtivo> ComponentesAtivos(List<string>? warnings = null)
    {
        var res = new List<ComponenteAtivo>();
        foreach (var t in ComponentesTrecho)
        {
            var tag = $"Componente por trecho ({ComponenteTrecho.Rotulo(t.Operacao).ToLowerInvariant()}, estacas {Estaca(t.A)} a {Estaca(t.B)})";
            if (t.B - t.A < 0.5) { warnings?.Add($"{tag}: A e B coincidem – trecho ignorado."); continue; }
            var left = t.LadoEsquerdo;
            // Trechos do mesmo lado não podem se sobrepor (transições incluídas): as marcas de um não sabem do outro.
            if (res.Any(r => r.Left == left && r.T.Fim > t.Inicio + 1e-6 && r.T.Inicio < t.Fim - 1e-6))
            {
                warnings?.Add($"{tag}: sobrepõe outro componente por trecho do mesmo lado (transições incluídas) – trecho ignorado.");
                continue;
            }
            var side = left ? Left : Right;
            var iv = Intervalos(left);
            double Start(int i) => i == 0 ? MedianHalf : iv[i - 1].B;
            switch (t.Operacao)
            {
                case OperacaoComponente.Inserir:
                {
                    if (t.Elemento is not { } e || e.Largura < 0.05) { warnings?.Add($"{tag}: elemento sem largura – trecho ignorado."); continue; }
                    var p = Math.Clamp(t.Posicao, 0, side.Count);
                    if (p == side.Count && side.Count > 0 && side[^1].Tipo == TipoElementoSecao.Calcada)
                    {
                        warnings?.Add($"{tag}: a calçada é o último elemento do lado (alinhamento do lote) – nada entra depois dela; trecho ignorado.");
                        continue;
                    }
                    if (e.Tipo == TipoElementoSecao.Calcada && p != side.Count)
                    {
                        warnings?.Add($"{tag}: a calçada só pode entrar como último elemento do lado – trecho ignorado.");
                        continue;
                    }
                    var m = SecaoPura();
                    (left ? m.Left : m.Right).Insert(p, e.Clone());
                    var ivm = m.Intervalos(left);
                    // Costura: no fim do elemento anterior – antes da sarjeta, que é da calçada e vai com ela.
                    var seam = Start(p) - SarjetaDentro(side, p);
                    var outerBase = p < iv.Count ? iv[p].A : (iv.Count == 0 ? MedianHalf : iv[^1].B);
                    var outerNew = p + 1 < ivm.Count ? ivm[p + 1].A : ivm[^1].B;
                    var delta = outerNew - outerBase;
                    res.Add(new ComponenteAtivo(t, left, new Zone(seam, seam, seam, seam), delta, seam, m));
                    break;
                }
                case OperacaoComponente.Remover:
                {
                    var p = t.Posicao;
                    if (p < 0 || p >= side.Count || t.TipoNaPosicao is { } tp && side[p].Tipo != tp)
                    {
                        warnings?.Add($"{tag}: a seção não tem mais esse elemento nessa posição – trecho ignorado.");
                        continue;
                    }
                    var m = SecaoPura();
                    (left ? m.Left : m.Right).RemoveAt(p);
                    var ivm = m.Intervalos(left);
                    // A sarjeta da calçada seguinte (no fim do elemento tirado) vai com a calçada: a zona que some fica antes dela.
                    // A linha de bordo junto ao meio-fio (no fim da faixa tirada) também: passa a ser a da faixa de dentro.
                    var g = SarjetaDentro(side, p + 1);
                    var z0 = Math.Max(Start(p) - (p > 0 ? g : 0), MedianHalf);
                    var z1 = Math.Max(z0 + 0.05, iv[p].B - g - BordoNoFim(side, p));
                    var outerNew = p < ivm.Count ? ivm[p].A : (ivm.Count == 0 ? MedianHalf : ivm[^1].B);
                    var outerBase = p + 1 < iv.Count ? iv[p + 1].A : iv[p].B;
                    res.Add(new ComponenteAtivo(t, left, new Zone(z0, z1, z1, z1), outerNew - outerBase, z0, m));
                    break;
                }
                default:
                {
                    var p = t.Posicao;
                    if (p < 0 || p >= side.Count || t.TipoNaPosicao is { } tp && side[p].Tipo != tp)
                    {
                        warnings?.Add($"{tag}: a seção não tem mais esse elemento nessa posição – trecho ignorado.");
                        continue;
                    }
                    if (t.Largura < 0.05) { warnings?.Add($"{tag}: largura nula – use Tirar; trecho ignorado."); continue; }
                    var delta = t.Largura - Math.Max(0.05, side[p].Largura);
                    if (Math.Abs(delta) < 1e-6) continue;
                    // Calçada: o meio-fio e a faixa de serviço ficam; a faixa livre muda (como a largura por trecho).
                    var z = Zones(side);
                    // Faixa: a sarjeta da calçada seguinte e a linha de bordo (junto dela) acompanham o lado de fora.
                    var g = SarjetaDentro(side, p + 1);
                    var z1 = Math.Max(iv[p].A + 0.05, iv[p].B - g - BordoNoFim(side, p));
                    var zone = side[p].Tipo == TipoElementoSecao.Calcada && z.Walk
                        ? new Zone(z.General.F0, Math.Max(z.General.F0 + 0.05, z.General.F1), Math.Max(z.General.F0 + 0.05, z.General.F1), Math.Max(z.General.F0 + 0.05, z.General.F1))
                        : new Zone(iv[p].A, z1, z1, z1);
                    if (side[p].Tipo == TipoElementoSecao.Calcada && z.Walk && z.General.F1 - z.General.F0 + delta < 1.20 - 1e-6)
                        warnings?.Add($"{tag}: faixa livre da calçada com {Math.Max(0, z.General.F1 - z.General.F0 + delta):0.00} m – a NBR 9050 exige 1,20 m.");
                    res.Add(new ComponenteAtivo(t, left, zone, delta, iv[p].A, null));
                    break;
                }
            }
        }
        return res;
    }

    /// <summary>
    /// Folga (m) antes do fim do elemento <paramref name="p"/> para a linha de bordo que fica nele junto ao elemento seguinte
    /// (faixa de tráfego antes de acostamento, canteiro ou calçada; ou a última faixa do lado): a linha acompanha o lado de fora.
    /// </summary>
    private double BordoNoFim(List<ElementoSecao> side, int p) =>
        EdgeLines && ElementoSecao.EhFaixaDeTrafego(side[p].Tipo)
        && (p + 1 == side.Count || side[p + 1].Tipo is TipoElementoSecao.Acostamento or TipoElementoSecao.CanteiroFisico or TipoElementoSecao.Calcada)
            ? EdgeInset + 0.02 : 0;

    /// <summary>
    /// Sarjeta da calçada da posição <paramref name="p"/> que fica dentro do elemento anterior (sarjeta não somada, elemento
    /// anterior no nível da pista): 0 sem ela.
    /// </summary>
    private double SarjetaDentro(List<ElementoSecao> side, int p) =>
        p > 0 && p < side.Count && side[p].Tipo == TipoElementoSecao.Calcada && PhysicalElements && !GutterAdds && !side[p - 1].Elevado
            ? Math.Max(0, side[p].Sarjeta) : 0;

    /// <summary>Seção sem nada por trecho nem derivados (só os elementos): base das seções dos componentes por trecho.</summary>
    private RoadSetup SecaoPura()
    {
        var c = Clone();
        c.LargurasVariaveis.Clear();
        c.Recuos.Clear();
        c.TrechosLargura.Clear();
        c.ComponentesTrecho.Clear();
        c.Retornos.Clear();
        c.TravessiasCanteiro.Clear();
        c.ExtensoesCalcada.Clear();
        c.PisoTatil = false;
        c.Sinalizacao = null;
        return c;
    }

    /// <summary>
    /// Elementos de um lado na estaca <paramref name="s"/> com os componentes por trecho (acrescentados com a largura da
    /// transição, tirados ou com a largura nova), do eixo para fora: tipo e largura.
    /// </summary>
    public List<(TipoElementoSecao Tipo, double Largura)> SecaoNaEstaca(bool left, double s)
    {
        var side = left ? Left : Right;
        var items = side.Select((e, i) => (Key: (double)i, e.Tipo, W: Math.Max(0.05, e.Largura))).ToList();
        foreach (var c in ComponentesAtivos().Where(c => c.Left == left))
        {
            var f = c.T.Fator(s);
            if (f < 1e-9) continue;
            var t = c.T;
            switch (t.Operacao)
            {
                case OperacaoComponente.Inserir:
                    items.Add((t.Posicao - 0.5, t.Elemento!.Tipo, t.Elemento.Largura * f));
                    break;
                case OperacaoComponente.Remover:
                {
                    var k = items.FindIndex(x => Math.Abs(x.Key - t.Posicao) < 1e-9);
                    if (k >= 0) items[k] = items[k] with { W = items[k].W * (1 - f) };
                    break;
                }
                default:
                {
                    var k = items.FindIndex(x => Math.Abs(x.Key - t.Posicao) < 1e-9);
                    if (k >= 0) items[k] = items[k] with { W = items[k].W + (t.Largura - items[k].W) * f };
                    break;
                }
            }
        }
        return items.Where(x => x.W > 1e-3).OrderBy(x => x.Key).Select(x => (x.Tipo, x.W)).ToList();
    }

    /// <summary>
    /// Marcas próprias dos componentes Inserir/Remover: a seção do trecho é gerada inteira e comparada com a da via (posições
    /// levadas à seção do trecho). O que só existe nela é acrescentado entre A e B (caminho do eixo recortado); o que só existe
    /// na via é interrompido no trecho (Remover: das transições inclusive; Inserir: entre A e B). O pavimento ganha os vãos dos
    /// canteiros acrescentados e perde, no trecho, os dos tirados.
    /// </summary>
    private (Dictionary<MarkingDefinition, MarcaDoTrecho> Own, List<VaoDoTrecho> Gaps) ApplyComponentes(List<MarkingDefinition> res, RoadPavementDefinition pav,
        PathReference path, OutputSettings output, Catalogo? catalog, string groupId, Polyline2? axis)
    {
        var own = new Dictionary<MarkingDefinition, MarcaDoTrecho>();
        var gapsOut = new List<VaoDoTrecho>();
        var active = ComponentesAtivos(Warnings).Where(c => c.Secao != null).ToList();
        if (active.Count == 0) return (own, gapsOut);
        if (axis == null || axis.Points.Count < 2)
        {
            Warnings.Add("Componentes por trecho: a via não tem o eixo à mão nesta geração – as marcas próprias dos trechos não foram feitas.");
            return (own, gapsOut);
        }
        var L = axis.Length;
        var baseDefs = res.Where(d => d is not RoadPavementDefinition).ToList();
        var baseKeys = baseDefs.ToDictionary(d => d, Chave);
        foreach (var c in active)
        {
            var t = c.T;
            var a = Math.Clamp(t.A, 0, L);
            var b = Math.Clamp(t.B, 0, L);
            if (b - a < 0.5) { Warnings.Add($"Componente por trecho de {Estaca(t.A)} a {Estaca(t.B)}: fora do eixo – ignorado."); continue; }
            var m = c.Secao!;
            // Recuos das pontas da via só onde o trecho chega a elas.
            m.StartSetback = Math.Max(0, StartSetback - a);
            m.EndSetback = Math.Max(0, EndSetback - (L - b));
            m.Estaqueamento = Estaqueamento;
            var mDefs = m.Build(path, output, catalog, groupId, null, null).Where(d => d is not RoadPavementDefinition).ToList();
            var sigmaT = c.Left ? 1 : -1;
            var used = new HashSet<MarkingDefinition>();
            var fresh = new List<MarkingDefinition>();
            foreach (var md in mDefs)
            {
                var key = Chave(md);
                var pm = Posicao(md);
                var match = baseDefs.FirstOrDefault(bd => !used.Contains(bd) && baseKeys[bd] == key && Mesma(c, sigmaT, Posicao(bd), pm));
                if (match != null) used.Add(match);
                else fresh.Add(md);
            }
            // O que some no trecho: Remover desde o início da transição (o elemento afunila até sumir); Inserir só entre A e B.
            var (r0, r1) = t.Operacao == OperacaoComponente.Remover ? (Math.Max(0, t.Inicio), Math.Min(L, t.Fim)) : (a, b);
            var range = new StationRange { Start = r0, End = r1, AnchorStart = axis.PointAt(r0), AnchorEnd = axis.PointAt(r1) };
            foreach (var bd in baseDefs.Where(d => !used.Contains(d) && Lado(Posicao(d)) == sigmaT))
            {
                switch (bd)
                {
                    case LinearMarkingDefinition l: l.Breaks.Add(range); break;
                    case ParkingMarkingDefinition pk: pk.Breaks.Add(range); break;
                    default:
                        // Recorte sem dono: é da própria via (refeito com ela) – a limpeza dos recortes de marcas apagadas não o toca.
                        if (Posicao(bd) is { } bp)
                            bd.Exclusions.Add(new ExclusionZone { Points = Faixa(axis, r0, r1, sigmaT, bp.D0, bp.D1, c.Delta) });
                        break;
                }
            }
            // Marcas novas: só entre A e B, no eixo recortado (as estacas do perfil lateral são as desse caminho).
            var sub = axis.SubPoints(a, b);
            foreach (var md in fresh)
            {
                if (Posicao(md) is not { } pm || pm.Sigma != sigmaT) continue;
                md.SetPath(PathReference.FromPoints(sub, path.Z));
                own[md] = new MarcaDoTrecho(c, pm.Sigma, pm.D0, pm.D1);
                res.Add(md);
            }
            // Pavimento: vãos (canteiros, sarjetas) que só a seção do trecho tem, e os da via que o trecho tira.
            var mPav = m.PavementDefinition();
            var usedGaps = new HashSet<PavementGap>();
            foreach (var g in mPav.Gaps.Where(g => Math.Sign(g.Offset) == sigmaT))
            {
                var gm = (Sigma: sigmaT, D0: Math.Abs(g.Offset) - g.Width / 2, D1: Math.Abs(g.Offset) + g.Width / 2);
                var bg = pav.Gaps.FirstOrDefault(x => !usedGaps.Contains(x) && x.Median == g.Median && Math.Sign(x.Offset) == sigmaT
                                                      && Mesma(c, sigmaT, (sigmaT, Math.Abs(x.Offset) - x.Width / 2, Math.Abs(x.Offset) + x.Width / 2), gm));
                if (bg != null) usedGaps.Add(bg);
                else gapsOut.Add(new VaoDoTrecho(c, g with { From = a, To = b }));
            }
            pav.Gaps = pav.Gaps.Select(g => usedGaps.Contains(g) || Math.Sign(g.Offset) != sigmaT ? g
                : g with { Except = (g.Except ?? new List<StationRange>()).Append(range).ToList() }).ToList();
        }
        return (own, gapsOut);

        static int Lado((int Sigma, double D0, double D1)? p) => p?.Sigma ?? 0;
    }

    /// <summary>Lado (+1 esquerda) e faixa transversal (distâncias ao eixo) de uma marca da seção; nulo sem posição.</summary>
    private static (int Sigma, double D0, double D1)? Posicao(MarkingDefinition d)
    {
        (int, double, double)? Band(double offset, double width)
        {
            var sg = Math.Sign(offset);
            if (sg == 0) return (0, 0, 0);
            var x = Math.Abs(offset);
            return width > 1e-6 ? (sg, x - width / 2, x + width / 2) : (sg, x, x);
        }
        return d switch
        {
            LinearMarkingDefinition l => Band(l.Offset, l.WidthOverride ?? 0),
            HatchMarkingDefinition h when h.IsStrip => Band(h.StripOffset, h.StripWidth ?? 0),
            RepeatedMarkingDefinition r => Band(r.Offset, 0),
            DeviceMarkingDefinition dv => Band(dv.Offset, 0),
            ParkingMarkingDefinition p => (p.RightSide ? 1 : -1, -p.CurbOffset, -p.CurbOffset),
            _ => null,
        };
    }

    /// <summary>
    /// A marca da via (posição <paramref name="pb"/>) é a mesma da seção do trecho (<paramref name="pm"/>): posições levadas à
    /// seção do trecho iguais a 1 cm. Do outro lado e no eixo a posição não muda.
    /// </summary>
    private static bool Mesma(ComponenteAtivo c, int sigmaT, (int Sigma, double D0, double D1)? pb, (int Sigma, double D0, double D1)? pm)
    {
        if (pb is not { } b || pm is not { } m) return pb == null && pm == null;
        if (b.Sigma != m.Sigma) return false;
        const double tol = 0.01;
        if (b.Sigma != sigmaT) return Math.Abs(b.D0 - m.D0) < tol && Math.Abs(b.D1 - m.D1) < tol;
        var point = b.D1 - b.D0 < 1e-6;
        foreach (var x0 in Map(c, b.D0, point ? 0 : 1))
            foreach (var x1 in point ? new[] { x0 } : Map(c, b.D1, -1))
                if (Math.Abs(x0 - m.D0) < tol && Math.Abs(x1 - m.D1) < tol) return true;
        return false;
    }

    /// <summary>
    /// Distância na seção do trecho de um ponto da seção da via (componente Inserir/Remover pleno). <paramref name="interior"/>:
    /// de que lado do ponto fica a marca (+1 fora, −1 dentro, 0 linha) – decide a costura. Vazio = o ponto cai no elemento tirado.
    /// </summary>
    private static IEnumerable<double> Map(ComponenteAtivo c, double d, int interior)
    {
        const double e = 0.005;
        if (c.T.Operacao == OperacaoComponente.Inserir)
        {
            var a = c.Seam;
            if (d < a - e) return new[] { d };
            if (d > a + e) return new[] { d + c.Delta };
            return interior > 0 ? new[] { a + c.Delta } : interior < 0 ? new[] { a } : new[] { a, a + c.Delta };
        }
        var (z0, z1) = (c.Zona.A0, c.Zona.A1);
        if (d < z0 + e && (interior <= 0 || d < z0 - e)) return new[] { d };
        if (d > z1 - e && (interior >= 0 || d > z1 + e)) return new[] { d + c.Delta };
        return Array.Empty<double>();
    }

    /// <summary>Distância na seção da via equivalente a uma distância na seção do trecho (para o deslocamento dos outros trechos).</summary>
    private static double NaVia(ComponenteAtivo c, double dm)
    {
        if (c.T.Operacao == OperacaoComponente.Inserir)
            return dm <= c.Seam ? dm : dm >= c.Seam + c.Delta ? dm - c.Delta : c.Seam;
        return dm <= c.Zona.A0 ? dm : dm - c.Delta;
    }

    /// <summary>Identidade de uma marca sem posição nem caminho (tipo, código, larguras, cores...).</summary>
    private static string Chave(MarkingDefinition d)
    {
        var c = d.ShallowCopy();
        c.Id = "";
        c.GroupId = null;
        c.Exclusions = new List<ExclusionZone>();
        switch (c)
        {
            case LinearMarkingDefinition l: l.Offset = 0; l.StartSetback = 0; l.EndSetback = 0; l.Breaks = new(); l.PathRef = new PathReference(); break;
            case HatchMarkingDefinition h: h.StripOffset = 0; h.Boundary = new PathReference(); break;
            case RepeatedMarkingDefinition r: r.Offset = 0; r.StartOffset = 0; r.EndSetback = 0; r.PathRef = new PathReference(); break;
            case DeviceMarkingDefinition dv: dv.Offset = 0; dv.StartSetback = 0; dv.EndSetback = 0; dv.PathRef = new PathReference(); break;
            case ParkingMarkingDefinition p: p.CurbOffset = 0; p.StartOffset = 0; p.Breaks = new(); p.PathRef = new PathReference(); break;
        }
        return JsonSerializer.Serialize(c, JsonConfig.Compact);
    }

    /// <summary>
    /// Contorno (planta) que cobre a faixa transversal [d0, d1] de um lado entre as estacas s0 e s1, com folga para o
    /// deslocamento do trecho – recorte das marcas que somem no trecho.
    /// </summary>
    private static List<Vec2> Faixa(Polyline2 axis, double s0, double s1, int sigma, double d0, double d1, double delta)
    {
        var lo = Math.Max(0.0, Math.Min(d0, d0 + delta) - 0.5);
        var hi = Math.Max(d1, d1 + delta) + 0.5;
        var n = Math.Max(2, (int)Math.Ceiling((s1 - s0) / 1.0) + 1);
        var outer = new List<Vec2>();
        var inner = new List<Vec2>();
        for (int i = 0; i < n; i++)
        {
            var s = s0 + (s1 - s0) * i / (n - 1);
            var p = axis.PointAt(s);
            var nr = axis.TangentAt(s).PerpLeft * sigma;
            inner.Add(p + nr * lo);
            outer.Add(p + nr * hi);
        }
        inner.Reverse();
        return outer.Concat(inner).ToList();
    }
}
