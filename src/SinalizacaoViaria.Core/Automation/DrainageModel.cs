using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Via vista pela drenagem (Etapa 6): eixo guardado, cota da base, greide e larguras de cada lado – do eixo à face do meio-fio
/// (<see cref="RoadPavementDefinition.LeftWidth"/>) e a calçada. Lado +1 = esquerda do eixo, −1 = direita.
/// </summary>
public sealed record ViaDrenada(string Id, string Nome, Polyline2 Eixo, double CotaBase, RoadGrade? Greide,
    double PistaEsquerda, double PistaDireita, double CalcadaEsquerda, double CalcadaDireita, Estaqueamento? Estacas, double AlturaMeioFio = 0.15)
{
    public double Pista(int lado) => lado > 0 ? PistaEsquerda : PistaDireita;
    public double Calcada(int lado) => lado > 0 ? CalcadaEsquerda : CalcadaDireita;

    /// <summary>Há meio-fio (e sarjeta) do lado: a calçada da via inclui o meio-fio.</summary>
    public bool MeioFio(int lado) => Calcada(lado) > 0.01;

    /// <summary>Cota da sarjeta (junto à face do meio-fio) na estação, com o abaulamento ou a superelevação do greide.</summary>
    public double ZSarjeta(double s, int lado) => CotaBase + (Greide?.Z(s, lado * Pista(lado)) ?? 0);

    /// <summary>Ponto na face do meio-fio.</summary>
    public Vec2 Sarjeta(double s, int lado) => Eixo.PointAt(s) + Eixo.TangentAt(s).PerpLeft * (lado * Pista(lado));

    public string Estaca(double s) => (Estacas ?? new Estaqueamento()).Rotulo(s);

    /// <summary>
    /// Largura que escoa para a sarjeta do lado na estação: a calçada do lado e a pista do eixo ao meio-fio (abaulamento no
    /// eixo); com superelevação, a pista inteira vai para o lado baixo e nada da pista para o alto.
    /// </summary>
    public double LarguraContribuinte(double s, int lado)
    {
        var e = Greide?.E(s) ?? 0;
        if (Math.Abs(e) < 1e-9) return Pista(lado) + Calcada(lado);
        var baixo = e > 0 ? -1 : 1;     // e > 0: a esquerda sobe, a água vai para a direita
        return Calcada(lado) + (lado == baixo ? PistaEsquerda + PistaDireita : 0);
    }
}

/// <summary>Sarjeta junto a um dispositivo, lida da via: estaca, declividade longitudinal, sentido do escoamento e ponto baixo.</summary>
/// <param name="Sentido">+1: a água corre no sentido do eixo (estacas crescentes) ao passar pelo dispositivo; −1: contra; 0: sarjeta em nível.</param>
public sealed record SarjetaLida(string ViaId, string Via, int Lado, double Estaca, string EstacaTexto, double? Declividade, int Sentido,
    bool PontoBaixo, double Afastamento);

/// <summary>Área que escoa pela sarjeta até o dispositivo, estimada pela geometria da via: o trecho de/até e a largura média.</summary>
public sealed record AreaLida(double Area, double De, double Ate, double Largura);

/// <summary>Estado do que foi lido do modelo numa captação (Etapa 8).</summary>
public enum EstadoLeitura { NuncaLida, EmDia, Desatualizada, Editada }

/// <summary>Captação com o que o modelo diz dela (nulo = não está na sarjeta de uma via do plugin).</summary>
public sealed record CaptacaoLida(DrainageDefinition Dispositivo, string Rotulo, SarjetaLida? Sarjeta, AreaLida? Area);

/// <summary>Ponto baixo da sarjeta de uma via: onde a água para (a sarjeta sobe dos dois lados).</summary>
public sealed record PontoBaixoSarjeta(string ViaId, string Via, int Lado, double Estaca, string EstacaTexto, Vec2 Ponto, double Profundidade);

/// <summary>Talvegue do terreno natural sob o aterro de uma via, sem trecho de rede que atravesse a via perto dele.</summary>
public sealed record TalvegueSobAterro(string ViaId, string Via, double Estaca, string EstacaTexto, Vec2 Ponto, double Aterro, double Profundidade);

/// <summary>
/// Drenagem lida do modelo (Etapa 6; Prompt Master §11; Motores §10; RT-1.5): a sarjeta de cada captação pelo greide da via
/// (declividade, sentido, ponto baixo), a área que escoa até ela pela geometria da via, os pontos baixos da sarjeta sem
/// captação e os talvegues sob aterro sem travessia. É uma leitura geométrica: lotes, quadras e áreas fora da via não entram
/// na área, a água que passa por uma captação conta como captada nela, e as tolerâncias abaixo são da conferência do plugin,
/// não valores normativos.
/// </summary>
public static class DrenagemDoModelo
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Distância máxima (m) entre o dispositivo e a face do meio-fio para ele ser da sarjeta daquela via.</summary>
    public const double AlcanceMeioFio = 1.5;

    /// <summary>Distância máxima (m) entre a captação e o ponto baixo para ela ser a captação dele.</summary>
    public const double ToleranciaPontoBaixo = 5.0;

    /// <summary>Profundidade mínima (m) de um ponto baixo: a sarjeta sobe pelo menos isto dos dois lados.</summary>
    public const double ProfundidadeMinima = 0.01;

    /// <summary>Diferença (pontos percentuais) entre a declividade informada da sarjeta e a do greide para ser apontada.</summary>
    public const double DiferencaDeclividade = 0.5;

    /// <summary>Aterro mínimo (m) sobre um talvegue para conferir a travessia, e profundidade mínima (m) do talvegue.</summary>
    public const double AterroMinimo = 1.0, TalvegueMinimo = 0.5;

    /// <summary>Distância máxima (m), ao longo do eixo, entre o talvegue e um trecho de rede que atravesse a via.</summary>
    public const double AlcanceTravessia = 15.0;

    private const double Passo = 0.5;

    /// <summary>Vias do plugin com eixo guardado.</summary>
    public static List<ViaDrenada> Vias(IEnumerable<MarkingDefinition> defs)
    {
        var res = new List<ViaDrenada>();
        var n = 0;
        foreach (var r in defs.OfType<RoadPavementDefinition>())
        {
            n++;
            // Pontos ou, numa via sobre linhas do Revit, o traçado guardado delas (o StoredPath só lê os pontos).
            if (r.PathRef.StoredPolyline() is not { Length: > 1 } eixo) continue;
            res.Add(new ViaDrenada(r.Id, $"Via {n}", eixo, r.PathRef.Z, r.Output.Grade, r.LeftWidth, r.RightWidth,
                r.LeftSidewalk, r.RightSidewalk, r.Estaqueamento, r.CurbHeight));
        }
        return res;
    }

    private static (double S, int Lado, double Offset) Projetar(ViaDrenada v, Vec2 p)
    {
        var s = Math.Clamp(v.Eixo.Project(p).Station, 0, v.Eixo.Length);
        var o = (p - v.Eixo.PointAt(s)).Dot(v.Eixo.TangentAt(s).PerpLeft);
        return (s, o >= 0 ? 1 : -1, Math.Abs(o));
    }

    private static List<(double S, double Z)> Perfil(ViaDrenada v, int lado)
    {
        var L = v.Eixo.Length;
        var n = Math.Max(2, (int)Math.Ceiling(L / Passo));
        return Enumerable.Range(0, n + 1).Select(i => { var s = L * i / n; return (s, v.ZSarjeta(s, lado)); }).ToList();
    }

    /// <summary>Pontos baixos interiores da sarjeta do lado: a sarjeta sobe ao menos <see cref="ProfundidadeMinima"/> dos dois lados.</summary>
    public static List<PontoBaixoSarjeta> PontosBaixos(ViaDrenada v, int lado)
    {
        var res = new List<PontoBaixoSarjeta>();
        if (v.Greide == null || !v.MeioFio(lado)) return res;
        var p = Perfil(v, lado);
        // Trechos de mesma cota (patamar) contam como um ponto só, no meio do patamar.
        var runs = new List<(int I0, int I1, double Z)>();
        foreach (var (pt, i) in p.Select((x, i) => (x, i)))
            if (runs.Count > 0 && Math.Abs(runs[^1].Z - pt.Z) < 1e-6) runs[^1] = (runs[^1].I0, i, runs[^1].Z);
            else runs.Add((i, i, pt.Z));
        var maxEsq = new double[runs.Count];
        var maxDir = new double[runs.Count];
        for (int k = 0; k < runs.Count; k++) maxEsq[k] = Math.Max(k > 0 ? maxEsq[k - 1] : double.MinValue, runs[k].Z);
        for (int k = runs.Count - 1; k >= 0; k--) maxDir[k] = Math.Max(k + 1 < runs.Count ? maxDir[k + 1] : double.MinValue, runs[k].Z);
        for (int k = 1; k + 1 < runs.Count; k++)
        {
            if (!(runs[k].Z < runs[k - 1].Z && runs[k].Z < runs[k + 1].Z)) continue;
            var prof = Math.Min(maxEsq[k - 1], maxDir[k + 1]) - runs[k].Z;
            if (prof < ProfundidadeMinima) continue;
            var s = (p[runs[k].I0].S + p[runs[k].I1].S) / 2;
            res.Add(new PontoBaixoSarjeta(v.Id, v.Nome, lado, s, v.Estaca(s), v.Sarjeta(s, lado), prof));
        }
        return res;
    }

    /// <summary>A sarjeta junto ao dispositivo, pela via de meio-fio mais próximo (até <see cref="AlcanceMeioFio"/>); nulo fora das vias.</summary>
    public static SarjetaLida? Sarjeta(DrainageDefinition d, IReadOnlyList<ViaDrenada> vias)
    {
        if (d.IsLinear) return null;
        SarjetaLida? best = null;
        foreach (var v in vias)
        {
            var (s, lado, o) = Projetar(v, d.Position);
            var af = Math.Abs(o - v.Pista(lado));
            if (af > AlcanceMeioFio || (best != null && af >= best.Afastamento)) continue;
            if (s <= 1e-6 && (d.Position - v.Eixo.PointAt(0)).Dot(v.Eixo.TangentAt(0)) < -AlcanceMeioFio) continue;
            if (s >= v.Eixo.Length - 1e-6 && (d.Position - v.Eixo.PointAt(v.Eixo.Length)).Dot(v.Eixo.TangentAt(v.Eixo.Length)) > AlcanceMeioFio) continue;
            double? decl = null;
            var sentido = 0;
            var baixo = false;
            if (v.Greide != null)
            {
                var a = Math.Max(0, s - 0.5);
                var b = Math.Min(v.Eixo.Length, s + 0.5);
                var rampa = b - a > 1e-6 ? (v.ZSarjeta(b, lado) - v.ZSarjeta(a, lado)) / (b - a) : 0;
                decl = Math.Abs(rampa) * 100;
                sentido = Math.Abs(rampa) < 1e-6 ? 0 : rampa > 0 ? -1 : 1;
                baixo = PontosBaixos(v, lado).Any(pb => Math.Abs(pb.Estaca - s) <= ToleranciaPontoBaixo);
            }
            best = new SarjetaLida(v.Id, v.Nome, lado, s, v.Estaca(s), decl, sentido, baixo, af);
        }
        return best;
    }

    /// <summary>
    /// Área que escoa pela sarjeta até a captação: do dispositivo, sarjeta acima (para os dois lados, enquanto ela sobe), até
    /// o divisor (ponto alto), a ponta da via ou a captação seguinte do mesmo lado – que capta o que vem de cima dela. A largura
    /// é <see cref="ViaDrenada.LarguraContribuinte"/>. Nulo sem greide (sem sentido de escoamento).
    /// </summary>
    public static AreaLida? Area(SarjetaLida sarjeta, ViaDrenada v, IEnumerable<double> outrasCaptacoes)
    {
        if (v.Greide == null) return null;
        var outras = outrasCaptacoes.Where(x => Math.Abs(x - sarjeta.Estaca) > 0.25).ToList();
        var lado = sarjeta.Lado;
        double Andar(int dir)
        {
            var s0 = sarjeta.Estaca;
            var s = s0;
            var vizinha = dir > 0 ? outras.Where(x => x > s).Cast<double?>().DefaultIfEmpty(null).Min() : outras.Where(x => x < s).Cast<double?>().DefaultIfEmpty(null).Max();
            var limite = vizinha ?? (dir > 0 ? v.Eixo.Length : 0);
            var subiu = false;
            while (true)
            {
                var t = Math.Clamp(s + dir * Passo, 0, v.Eixo.Length);
                if (Math.Abs(t - s) < 1e-9) return s;
                if (dir > 0 ? t >= limite : t <= limite)
                {
                    // Chegou à captação vizinha: o que fica entre as duas é desta. Sarjeta em nível entre elas: metade para cada.
                    if (vizinha == null || subiu) return limite;
                    return (s0 + limite) / 2;
                }
                var dz = v.ZSarjeta(t, lado) - v.ZSarjeta(s, lado);
                if (dz < -1e-6) return s;     // passou do divisor: a água dali vai para o outro lado
                if (dz > 1e-6) subiu = true;
                s = t;
            }
        }
        var de = Andar(-1);
        var ate = Andar(1);
        if (ate - de < 1e-6) return new AreaLida(0, de, ate, 0);
        var n = Math.Max(1, (int)Math.Ceiling((ate - de) / Passo));
        double area = 0;
        for (int i = 0; i < n; i++)
        {
            var s0 = de + (ate - de) * i / n;
            var s1 = de + (ate - de) * (i + 1) / n;
            area += v.LarguraContribuinte((s0 + s1) / 2, lado) * (s1 - s0);
        }
        return new AreaLida(area, de, ate, area / (ate - de));
    }

    /// <summary>Todas as captações (bocas de lobo e grelhas, não PV nem canaleta) com a sarjeta e a área lidas do modelo.</summary>
    public static List<CaptacaoLida> Captacoes(IReadOnlyCollection<MarkingDefinition> defs)
    {
        var vias = Vias(defs);
        var rotulos = DrainageNetwork.Labels(defs);
        var inlets = defs.OfType<DrainageDefinition>().Where(d => d.IsInlet && !d.IsLinear).ToList();
        var lidas = inlets.Select(d => (D: d, S: Sarjeta(d, vias))).ToList();
        var res = new List<CaptacaoLida>();
        foreach (var (d, s) in lidas)
        {
            AreaLida? area = null;
            if (s != null && vias.FirstOrDefault(v => v.Id == s.ViaId) is { } via)
                area = Area(s, via, lidas.Where(x => x.S != null && x.D != d && x.S.ViaId == s.ViaId && x.S.Lado == s.Lado).Select(x => x.S!.Estaca));
            res.Add(new CaptacaoLida(d, rotulos.GetValueOrDefault(d.Id, d.DisplayCode), s, area));
        }
        return res;
    }

    /// <summary>
    /// Grava nas captações o que o modelo diz: a área (quando 0 ou com <paramref name="sobrescrever"/>), a declividade da
    /// sarjeta (quando vazia ou com <paramref name="sobrescrever"/>) e o ponto baixo (só liga), e registra a leitura
    /// (<see cref="DrainageDefinition.Lida"/>, Etapa 8) – o valor informado que ficou é reconhecido depois como editado à mão.
    /// Devolve os dispositivos que mudaram e o que mudou. As definições são alteradas no lugar: quem chama grava.
    /// </summary>
    public static List<(DrainageDefinition Dispositivo, string Texto)> Aplicar(IEnumerable<CaptacaoLida> captacoes, bool sobrescrever = false,
        DateTime? quando = null)
    {
        var mudou = new List<(DrainageDefinition, string)>();
        foreach (var c in captacoes)
        {
            var d = c.Dispositivo;
            var partes = new List<string>();
            if (c.Area is { Area: > 1e-6 } a && (sobrescrever || d.AreaContribuicao <= 1e-9))
            {
                d.AreaContribuicao = Math.Round(a.Area, 1);
                partes.Add($"área {d.AreaContribuicao.ToString("N1", Pt)} m²");
            }
            if (c.Sarjeta is { Declividade: { } decl } && (sobrescrever || d.DeclividadeSarjeta == null))
            {
                d.DeclividadeSarjeta = Math.Round(decl, 2);
                partes.Add($"sarjeta {d.DeclividadeSarjeta.Value.ToString("0.00", Pt)} %");
            }
            if (c.Sarjeta is { PontoBaixo: true } && !d.PontoBaixo)
            {
                d.PontoBaixo = true;
                partes.Add("ponto baixo");
            }
            if (c.Sarjeta != null)
            {
                var lida = Leitura(c, quando ?? DateTime.Now);
                if (!MesmaLeitura(d.Lida, lida))
                {
                    d.Lida = lida;
                    if (partes.Count == 0) partes.Add("leitura registrada (os valores informados ficam)");
                }
            }
            if (partes.Count > 0) mudou.Add((d, $"{c.Rotulo}: {string.Join(", ", partes)}"));
        }
        return mudou;
    }

    private static ContribuicaoLida Leitura(CaptacaoLida c, DateTime quando) => new()
    {
        ViaId = c.Sarjeta?.ViaId ?? "", Quando = quando,
        Area = c.Area is { Area: > 1e-6 } a ? Math.Round(a.Area, 1) : null,
        Declividade = c.Sarjeta?.Declividade is { } x ? Math.Round(x, 2) : null,
        PontoBaixo = c.Sarjeta?.PontoBaixo ?? false,
    };

    private static bool Igual(double? x, double? y, double tol) => x == null ? y == null : y != null && Math.Abs(x.Value - y.Value) <= tol;

    private static bool MesmaLeitura(ContribuicaoLida? a, ContribuicaoLida b) =>
        a != null && a.ViaId == b.ViaId && Igual(a.Area, b.Area, 0.05) && Igual(a.Declividade, b.Declividade, 0.005) && a.PontoBaixo == b.PontoBaixo;

    private static bool AreaDoModelo(DrainageDefinition d) => d.Lida?.Area is { } a && Math.Abs(d.AreaContribuicao - a) <= 0.05;
    private static bool DeclividadeDoModelo(DrainageDefinition d) => d.Lida?.Declividade is { } a && d.DeclividadeSarjeta is { } x && Math.Abs(x - a) <= 0.005;

    /// <summary>
    /// Estado do que foi lido do modelo numa captação (Etapa 8): nunca lido; em dia; desatualizado (o campo ainda é o lido,
    /// mas o modelo agora diz outra coisa – a via mudou, outra captação entrou acima, o dispositivo saiu da sarjeta); ou
    /// editado à mão (nenhum campo é mais o lido: fica como está).
    /// </summary>
    public static EstadoLeitura Estado(CaptacaoLida c)
    {
        var d = c.Dispositivo;
        if (d.Lida is not { } l) return EstadoLeitura.NuncaLida;
        var area = AreaDoModelo(d);
        var decl = DeclividadeDoModelo(d);
        if (!area && !decl) return EstadoLeitura.Editada;
        if (c.Sarjeta == null) return EstadoLeitura.Desatualizada;
        var agora = Leitura(c, l.Quando);
        if (agora.ViaId != l.ViaId || (area && !Igual(agora.Area, l.Area, 0.05)) || (decl && !Igual(agora.Declividade, l.Declividade, 0.005)))
            return EstadoLeitura.Desatualizada;
        return EstadoLeitura.EmDia;
    }

    /// <summary>
    /// Atualização seletiva (Etapa 8): relê só as captações desatualizadas e troca só os campos que ainda eram os lidos – os
    /// editados à mão ficam. Captação fora da sarjeta de uma via não é relida (fica para conferir à mão).
    /// </summary>
    public static List<(DrainageDefinition Dispositivo, string Texto)> AtualizarDesatualizadas(IEnumerable<CaptacaoLida> captacoes, DateTime? quando = null)
    {
        var mudou = new List<(DrainageDefinition, string)>();
        foreach (var c in captacoes.Where(c => c.Sarjeta != null && Estado(c) == EstadoLeitura.Desatualizada))
        {
            var d = c.Dispositivo;
            var agora = Leitura(c, quando ?? DateTime.Now);
            var partes = new List<string>();
            if (AreaDoModelo(d) && agora.Area is { } a && Math.Abs(a - d.AreaContribuicao) > 0.05)
            {
                partes.Add($"área {d.AreaContribuicao.ToString("N1", Pt)} → {a.ToString("N1", Pt)} m²");
                d.AreaContribuicao = a;
            }
            if (DeclividadeDoModelo(d) && agora.Declividade is { } x && Math.Abs(x - d.DeclividadeSarjeta!.Value) > 0.005)
            {
                partes.Add($"sarjeta {d.DeclividadeSarjeta.Value.ToString("0.00", Pt)} → {x.ToString("0.00", Pt)} %");
                d.DeclividadeSarjeta = x;
            }
            if (agora.PontoBaixo && !d.PontoBaixo)
            {
                d.PontoBaixo = true;
                partes.Add("ponto baixo");
            }
            d.Lida = agora;
            mudou.Add((d, $"{c.Rotulo}: {(partes.Count > 0 ? string.Join(", ", partes) : "leitura renovada")}"));
        }
        return mudou;
    }

    /// <summary>
    /// O que foi informado na captação e diverge do modelo: declividade da sarjeta (mais de <see cref="DiferencaDeclividade"/>
    /// ponto percentual do greide) e captação num ponto baixo sem "ponto baixo" marcado (o cálculo do engolimento muda).
    /// </summary>
    public static List<ProblemaRede> Divergencias(IEnumerable<CaptacaoLida> captacoes)
    {
        var res = new List<ProblemaRede>();
        foreach (var c in captacoes)
        {
            if (c.Sarjeta is not { } s) continue;
            var d = c.Dispositivo;
            if (d.DeclividadeSarjeta is { } inf && s.Declividade is { } lida && Math.Abs(inf - lida) > DiferencaDeclividade)
                res.Add(new ProblemaRede(c.Rotulo, "sarjeta-modelo",
                    $"{c.Rotulo}: declividade da sarjeta informada {inf.ToString("0.00", Pt)} %, a do greide da {s.Via} na estaca {s.EstacaTexto} é {lida.ToString("0.00", Pt)} %.",
                    false, new[] { d.Id }));
            if (s.PontoBaixo && !d.PontoBaixo)
                res.Add(new ProblemaRede(c.Rotulo, "ponto-baixo-modelo",
                    $"{c.Rotulo}: está no ponto baixo da sarjeta da {s.Via} (estaca {s.EstacaTexto}), mas não está marcada como captação em ponto baixo.",
                    false, new[] { d.Id }));
        }
        return res;
    }

    /// <summary>
    /// Pontos baixos da sarjeta sem captação a até <see cref="ToleranciaPontoBaixo"/> m: a água para ali. Conta qualquer boca de
    /// lobo ou grelha (inclusive de nó ou de outra via) e as canaletas pelo traçado.
    /// </summary>
    public static List<PontoBaixoSarjeta> PontosBaixosSemCaptacao(IReadOnlyCollection<MarkingDefinition> defs)
    {
        var inlets = defs.OfType<DrainageDefinition>().Where(d => d.IsInlet).ToList();
        bool Captado(Vec2 p) => inlets.Any(d => d.IsLinear
            ? d.PathRef.StoredPolyline() is { } path && Math.Abs(path.SignedDistance(p)) <= ToleranciaPontoBaixo
            : d.Position.DistanceTo(p) <= ToleranciaPontoBaixo);
        return Vias(defs).SelectMany(v => new[] { 1, -1 }.SelectMany(l => PontosBaixos(v, l))).Where(pb => !Captado(pb.Ponto)).ToList();
    }

    /// <summary>
    /// Talvegues do terreno <b>natural</b> sob o aterro das vias: ponto mais baixo do terreno ao longo do eixo, com ao menos
    /// <see cref="TalvegueMinimo"/> m de profundidade dos dois lados e <see cref="AterroMinimo"/> m de aterro sobre ele, sem
    /// trecho de rede atravessando o eixo a até <see cref="AlcanceTravessia"/> m. A via represaria a água ali. Trechos em
    /// obra (ponte, viaduto) ficam de fora por <paramref name="pular"/>.
    /// </summary>
    public static List<TalvegueSobAterro> TalveguesSemTravessia(IReadOnlyCollection<MarkingDefinition> defs, Func<Vec2, double?> terrenoNatural,
        Func<ViaDrenada, double, bool>? pular = null)
    {
        var res = new List<TalvegueSobAterro>();
        // Trechos em obra (ponte, viaduto, túnel, trincheira): sem aterro da via sobre o terreno.
        var grupos = defs.OfType<RoadPavementDefinition>().ToDictionary(r => r.Id, r => r.GroupId);
        var obras = defs.OfType<IHostedStructure>().Where(h => h.HostRoad != null)
            .Select(h => (h.HostRoad, A: Math.Min(h.HostStart, h.HostEnd), B: Math.Max(h.HostStart, h.HostEnd))).ToList();
        bool EmObra(ViaDrenada v, double s) =>
            grupos.TryGetValue(v.Id, out var g) && g != null && obras.Any(o => o.HostRoad == g && s >= o.A - AlcanceTravessia && s <= o.B + AlcanceTravessia);
        var tubos = defs.OfType<DrainagePipeDefinition>().Select(p => DrainageNetwork.Ends(p, id => defs.FirstOrDefault(x => x.Id == id)))
            .Where(e => e != null).Select(e => (e!.A, e.B)).ToList();
        foreach (var v in Vias(defs).Where(v => v.Greide is { AdjustTerrain: true }))
        {
            var L = v.Eixo.Length;
            var n = Math.Max(2, (int)Math.Ceiling(L / 2.0));
            var pts = Enumerable.Range(0, n + 1).Select(i => L * i / n).Select(s => (S: s, T: terrenoNatural(v.Eixo.PointAt(s)), Z: v.CotaBase + v.Greide!.Z(s))).ToList();
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                if (pts[i].T is not double t || pts[i - 1].T is not double ta || pts[i + 1].T is not double tb) continue;
                if (!(t < ta && t <= tb)) continue;
                var esq = pts.Take(i).Select(x => x.T).Where(x => x != null).DefaultIfEmpty(t).Max()!.Value;
                var dir = pts.Skip(i + 1).Select(x => x.T).Where(x => x != null).DefaultIfEmpty(t).Max()!.Value;
                var prof = Math.Min(esq, dir) - t;
                var aterro = pts[i].Z - t;
                var s = pts[i].S;
                if (prof < TalvegueMinimo || aterro < AterroMinimo || EmObra(v, s) || (pular?.Invoke(v, s) ?? false)) continue;
                // Fundo do mesmo vale com o terreno irregular: um achado só.
                if (res.Any(x => x.ViaId == v.Id && Math.Abs(x.Estaca - s) <= AlcanceTravessia)) continue;
                var atravessa = tubos.Any(tb2 =>
                {
                    var (a, b) = tb2;
                    var (sa, la, _) = Projetar(v, a);
                    var (sb, lb, _) = Projetar(v, b);
                    return la != lb && Math.Min(sa, sb) <= s + AlcanceTravessia && Math.Max(sa, sb) >= s - AlcanceTravessia;
                });
                if (!atravessa) res.Add(new TalvegueSobAterro(v.Id, v.Nome, s, v.Estaca(s), v.Eixo.PointAt(s), aterro, prof));
            }
        }
        return res;
    }

    /// <summary>
    /// Superfície acabada para o recobrimento da rede: sobre a pista de uma via do plugin, a cota dela (greide, abaulamento ou
    /// superelevação); sobre a calçada, a cota da sarjeta mais a altura do meio-fio (sem a inclinação transversal da calçada);
    /// fora das vias, o <paramref name="terreno"/> (o projetado). Nulo onde não há nenhum dos dois: quem usa interpola entre as
    /// tampas. Antes o recobrimento era medido sempre sob a reta entre as tampas (K5).
    /// </summary>
    public static Func<Vec2, double?> SuperficieAcabada(IReadOnlyCollection<MarkingDefinition> defs, Func<Vec2, double?>? terreno)
    {
        var vias = Vias(defs);
        return p =>
        {
            foreach (var v in vias)
            {
                var (s, lado, o) = Projetar(v, p);
                var u = (p - v.Eixo.PointAt(s)).Dot(v.Eixo.TangentAt(s));
                if (Math.Abs(u) > 0.05) continue;                       // além das pontas do eixo
                if (o <= v.Pista(lado)) return v.CotaBase + (v.Greide?.Z(s, lado * o) ?? 0);
                if (o <= v.Pista(lado) + v.Calcada(lado)) return v.ZSarjeta(s, lado) + v.AlturaMeioFio;
            }
            return terreno?.Invoke(p);
        };
    }

    /// <summary>Lado da via escrito ("esquerdo"/"direito", olhando no sentido do eixo).</summary>
    public static string Lado(int lado) => lado > 0 ? "esquerdo" : "direito";

    /// <summary>Bloco do CSV: cada captação com o que foi informado e o que o modelo diz.</summary>
    public static string Csv(IReadOnlyList<CaptacaoLida> captacoes)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("CAPTAÇÕES × MODELO (área: pista do eixo ao meio-fio e calçada, entre o divisor e a captação; lotes e áreas externas não entram)");
        sb.AppendLine("Dispositivo;Via;Lado;Estaca;Sentido do escoamento;Declividade informada (%);Declividade do greide (%);Ponto baixo informado;Ponto baixo do greide;Área informada (m²);Área da via (m²);Trecho da via (m)");
        foreach (var c in captacoes)
        {
            var d = c.Dispositivo;
            var s = c.Sarjeta;
            sb.AppendLine(string.Join(";", c.Rotulo, s?.Via ?? "fora das vias", s == null ? "" : Lado(s.Lado), s?.EstacaTexto ?? "",
                s == null ? "" : s.Sentido switch { > 0 => "no sentido do eixo", < 0 => "contra o eixo", _ => "em nível" },
                d.DeclividadeSarjeta?.ToString("0.00", Pt) ?? "", s?.Declividade?.ToString("0.00", Pt) ?? "",
                d.PontoBaixo ? "sim" : "não", s == null ? "" : s.PontoBaixo ? "sim" : "não",
                d.AreaContribuicao.ToString("0.0", Pt), c.Area?.Area.ToString("0.0", Pt) ?? "",
                c.Area is { } a ? $"{a.De.ToString("0.0", Pt)} a {a.Ate.ToString("0.0", Pt)}" : ""));
        }
        return sb.ToString();
    }
}
