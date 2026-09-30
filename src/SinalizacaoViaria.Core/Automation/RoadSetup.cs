using System.Globalization;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Tratamento do eixo entre os sentidos de uma via de mão dupla.</summary>
public enum CenterTreatment
{
    Nenhum,
    /// <summary>Linha simples contínua (LFO-1).</summary>
    LFO1,
    /// <summary>Linha simples seccionada (LFO-2).</summary>
    LFO2,
    /// <summary>Linha dupla contínua (LFO-3).</summary>
    LFO3,
    /// <summary>Linha contínua/seccionada (LFO-4).</summary>
    LFO4,
    /// <summary>Canteiro central (físico ou pintado).</summary>
    Canteiro,
}

/// <summary>Tipo de canteiro.</summary>
public enum TipoCanteiro
{
    /// <summary>Meios-fios + área gramada, elevado 0,15 m.</summary>
    Fisico,
    /// <summary>Zebrado com linha de canalização, no nível da pista.</summary>
    Pintado,
}

/// <summary>Elementos que compõem cada lado da seção transversal, do eixo para fora.</summary>
public enum TipoElementoSecao
{
    FaixaRolamento,
    FaixaExclusiva,
    FaixaPreferencial,
    Ciclofaixa,
    Estacionamento,
    Acostamento,
    /// <summary>Faixa de segurança / transição / amortecimento zebrada (buffer).</summary>
    FaixaSeguranca,
    CanteiroFisico,
    CanteiroPintado,
    Calcada,
    /// <summary>Faixa de caminhada de pedestres pintada na pista (azul ou verde).</summary>
    FaixaCaminhada,
}

/// <summary>Um elemento da seção transversal (faixa, canteiro, calçada...).</summary>
public sealed class ElementoSecao
{
    public TipoElementoSecao Tipo { get; set; } = TipoElementoSecao.FaixaRolamento;
    public double Largura { get; set; } = 3.50;

    /// <summary>Estacionamento: código da vaga do catálogo.</summary>
    public string Vaga { get; set; } = "MER-0";

    /// <summary>Faixas exclusivas/preferenciais: legenda repetida (vazio = sem legenda).</summary>
    public string Legenda { get; set; } = "ÔNIBUS";

    /// <summary>Espaçamento das legendas/símbolos repetidos (m). 0 = não repetir.</summary>
    public double Espacamento { get; set; } = 50;

    /// <summary>Calçada: largura da faixa de serviço (junto ao meio-fio), incluindo o meio-fio.</summary>
    public double FaixaServico { get; set; } = 0.70;

    /// <summary>Calçada: largura da faixa de acesso (junto ao lote). 0 = não possui.</summary>
    public double FaixaAcesso { get; set; }

    /// <summary>Calçada: faixa de serviço gramada (senão, em concreto).</summary>
    public bool ServicoGramado { get; set; } = true;

    /// <summary>Ciclofaixa: pintura vermelha de fundo.</summary>
    public bool PinturaFundo { get; set; } = true;

    /// <summary>Ciclofaixa bidirecional (sem setas de sentido).</summary>
    public bool Bidirecional { get; set; }

    /// <summary>Dispositivo físico do catálogo implantado na divisa interna do elemento (segregação). Vazio = nenhum.</summary>
    public string? Dispositivo { get; set; }

    // ---- Ciclofaixa: medidas personalizáveis
    /// <summary>Largura da linha de delimitação da ciclofaixa (m).</summary>
    public double LarguraLinha { get; set; } = 0.20;
    /// <summary>Linha de delimitação seccionada.</summary>
    public bool LinhaSeccionada { get; set; }
    public double TracoLinha { get; set; } = 1.0;
    public double EspacoLinha { get; set; } = 1.0;
    /// <summary>Comprimento do símbolo da bicicleta (m).</summary>
    public double TamanhoSimbolo { get; set; } = 1.50;
    /// <summary>Comprimento da seta de sentido (m).</summary>
    public double TamanhoSeta { get; set; } = 1.50;
    /// <summary>Distância entre o símbolo e a seta (m).</summary>
    public double DistanciaSeta { get; set; } = 3.0;
    /// <summary>Linha central amarela em ciclofaixas bidirecionais.</summary>
    public bool LinhaCentral { get; set; } = true;

    // ---- Calçada
    /// <summary>Largura da sarjeta junto ao meio-fio, dentro da pista (m). 0 = sem sarjeta.</summary>
    public double Sarjeta { get; set; } = 0.30;

    /// <summary>Calçada: largura do meio-fio (guia) no topo (m).</summary>
    public double LarguraMeioFio { get; set; } = RoadSetup.CurbWidth;

    /// <summary>Largura efetiva do meio-fio (limitada a 5–60 cm).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double MeioFioEfetivo => Math.Clamp(LarguraMeioFio, 0.05, 0.60);

    // ---- Faixa de caminhada
    /// <summary>Cor da faixa de caminhada (Azul ou Verde).</summary>
    public MarkingColor CorCaminhada { get; set; } = MarkingColor.Azul;

    // ---- Faixa de ônibus
    /// <summary>Faixa exclusiva/preferencial: pintura de fundo colorida em toda a largura.</summary>
    public bool FundoOnibus { get; set; }
    /// <summary>Cor da pintura de fundo da faixa de ônibus.</summary>
    public MarkingColor CorOnibus { get; set; } = MarkingColor.Vermelha;

    // ---- Níveis
    /// <summary>
    /// Nível do topo do elemento em relação ao topo da pista (m). Nulo = padrão do tipo (calçada e canteiro físico 0,15;
    /// demais 0). Acima de 0 as faixas pintadas (ciclofaixa, estacionamento, ônibus...) viram plataforma elevada com
    /// meio-fio no degrau; calçada e canteiro aceitam também valores negativos (rebaixados).
    /// </summary>
    public double? Altura { get; set; }

    /// <summary>Calçada (faixa de serviço gramada) e canteiro físico: nível do topo da vegetação (m). Nulo = igual ao elemento.</summary>
    public double? AlturaVegetacao { get; set; }

    /// <summary>Calçada: inclinação transversal (%), subindo em direção ao lote (NBR 9050: até 3 %). 0 = plana.</summary>
    public double InclinacaoTransversal { get; set; }

    /// <summary>Calçada: nível no alinhamento predial ao longo da via (edificações mais altas, topografia). Vazio = pela inclinação.</summary>
    public List<NivelAlinhamento> NiveisAlinhamento { get; set; } = new();

    public static double AlturaPadrao(TipoElementoSecao t) =>
        t is TipoElementoSecao.Calcada or TipoElementoSecao.CanteiroFisico ? RoadSetup.CurbHeight : 0;

    /// <summary>Nível efetivo do topo (m).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double AlturaEfetiva => Altura ?? AlturaPadrao(Tipo);

    /// <summary>Nível efetivo da vegetação (m).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double AlturaVegetacaoEfetiva => AlturaVegetacao ?? AlturaEfetiva;

    /// <summary>Elemento de pista (pintado) elevado acima do pavimento: vira plataforma com laje própria.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Elevado => Tipo is not (TipoElementoSecao.Calcada or TipoElementoSecao.CanteiroFisico) && AlturaEfetiva >= 0.01;

    public ElementoSecao Clone()
    {
        var c = (ElementoSecao)MemberwiseClone();
        c.NiveisAlinhamento = NiveisAlinhamento.Select(n => n.Clone()).ToList();
        return c;
    }

    public static string Rotulo(TipoElementoSecao t) => t switch
    {
        TipoElementoSecao.FaixaRolamento => "Faixa de rolamento",
        TipoElementoSecao.FaixaExclusiva => "Faixa exclusiva (ônibus)",
        TipoElementoSecao.FaixaPreferencial => "Faixa preferencial (ônibus)",
        TipoElementoSecao.Ciclofaixa => "Ciclofaixa",
        TipoElementoSecao.Estacionamento => "Faixa de estacionamento",
        TipoElementoSecao.Acostamento => "Acostamento",
        TipoElementoSecao.FaixaSeguranca => "Faixa de segurança / transição (zebrada)",
        TipoElementoSecao.CanteiroFisico => "Canteiro lateral físico",
        TipoElementoSecao.CanteiroPintado => "Canteiro lateral pintado",
        TipoElementoSecao.Calcada => "Calçada",
        TipoElementoSecao.FaixaCaminhada => "Faixa de caminhada (pedestres)",
        _ => t.ToString(),
    };

    /// <summary>Largura padrão ao inserir um elemento.</summary>
    public static double LarguraPadrao(TipoElementoSecao t) => t switch
    {
        TipoElementoSecao.FaixaRolamento => 3.50,
        TipoElementoSecao.FaixaExclusiva => 3.50,
        TipoElementoSecao.FaixaPreferencial => 3.50,
        TipoElementoSecao.Ciclofaixa => 1.50,
        TipoElementoSecao.Estacionamento => 2.20,
        TipoElementoSecao.Acostamento => 2.50,
        TipoElementoSecao.FaixaSeguranca => 0.60,
        TipoElementoSecao.CanteiroFisico => 1.50,
        TipoElementoSecao.CanteiroPintado => 1.00,
        TipoElementoSecao.Calcada => 3.00,
        TipoElementoSecao.FaixaCaminhada => 1.50,
        _ => 3.0,
    };

    public static bool EhFaixaDeTrafego(TipoElementoSecao t) =>
        t is TipoElementoSecao.FaixaRolamento or TipoElementoSecao.FaixaExclusiva or TipoElementoSecao.FaixaPreferencial;
}

/// <summary>
/// Seção transversal completa de uma via, para geração automática de toda a sinalização e dos
/// elementos físicos: faixas de rolamento, exclusivas e preferenciais, ciclofaixas, estacionamento,
/// acostamentos, faixas de segurança, canteiros centrais e laterais e calçadas.
/// </summary>
public sealed partial class RoadSetup
{
    public const double CurbWidth = 0.15;
    /// <summary>Altura usual do meio-fio / nível da calçada acima da pista (m).</summary>
    public const double CurbHeight = 0.15;

    /// <summary>Canteiro central físico: nível do topo dos meios-fios (m).</summary>
    public double MedianHeight { get; set; } = CurbHeight;
    /// <summary>Canteiro central físico: nível do topo da vegetação (m). Nulo = igual aos meios-fios.</summary>
    public double? MedianVegetationHeight { get; set; }

    /// <summary>Mão dupla (eixo = divisão de sentidos) ou mão única (todas as faixas no sentido do eixo).</summary>
    public bool TwoWay { get; set; } = true;

    /// <summary>Raio das esquinas nas conexões desta via (nulo = pela hierarquia).</summary>
    public double? CornerRadius { get; set; }

    /// <summary>Hierarquia viária (CTB art. 60) – gravada em todas as marcas da via.</summary>
    public HierarquiaViaria Hierarchy { get; set; } = HierarquiaViaria.NaoDefinida;

    /// <summary>Elementos do lado direito do eixo, do eixo para fora.</summary>
    public List<ElementoSecao> Right { get; set; } = new();

    /// <summary>Elementos do lado esquerdo do eixo, do eixo para fora.</summary>
    public List<ElementoSecao> Left { get; set; } = new();

    public CenterTreatment Center { get; set; } = CenterTreatment.LFO2;
    public double MedianWidth { get; set; } = 2.0;
    public TipoCanteiro MedianType { get; set; } = TipoCanteiro.Fisico;

    /// <summary>Dispositivo físico sobre o eixo/canteiro central (ex.: barreira New Jersey). Vazio = nenhum.</summary>
    public string? MedianDevice { get; set; }

    /// <summary>LFO-4 com a linha contínua do lado direito.</summary>
    public bool InvertCenter { get; set; }

    /// <summary>Código das linhas entre faixas de mesmo sentido.</summary>
    public string LaneDividerCode { get; set; } = "LMS-2";

    public bool EdgeLines { get; set; } = true;
    public string EdgeCode { get; set; } = "LBO";

    /// <summary>Afastamento do eixo da linha de bordo para dentro da faixa (m).</summary>
    public double EdgeInset { get; set; } = 0.10;

    /// <summary>Velocidade regulamentada (km/h) – escolhe a variante de cada linha.</summary>
    public double Speed { get; set; } = 60;

    public double StartSetback { get; set; }
    public double EndSetback { get; set; }

    /// <summary>Tachas sobre o eixo central.</summary>
    public string? CenterStudsCode { get; set; }
    public string? CenterStudsVariant { get; set; }

    /// <summary>Gera legendas e símbolos repetidos (ÔNIBUS, bicicletas...).</summary>
    public bool Inscriptions { get; set; } = true;

    /// <summary>Gera calçadas, meios-fios e canteiros físicos (senão, apenas a sinalização).</summary>
    public bool PhysicalElements { get; set; } = true;

    /// <summary>Pavimento da pista (gerado sob a sinalização).</summary>
    public TipoPavimento Pavement { get; set; } = TipoPavimento.Asfalto;
    public double? PavementThickness { get; set; }

    /// <summary>
    /// Sarjeta SOMADA à largura: fica entre a última faixa e o meio-fio e a faixa mantém a largura útil (a pista entre
    /// meios-fios = faixas + sarjetas). Nulo/falso = vias antigas (a sarjeta ocupa a borda da faixa).
    /// </summary>
    public bool? SarjetaSomada { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    private bool GutterAdds => SarjetaSomada == true;

    /// <summary>Sarjeta acrescentada antes do elemento <paramref name="i"/> (calçada sem faixa elevada antes dela).</summary>
    private double AddedGutter(List<ElementoSecao> side, int i) =>
        GutterAdds && PhysicalElements && side[i].Tipo == TipoElementoSecao.Calcada && !(i > 0 && side[i - 1].Elevado)
            ? Math.Max(0, side[i].Sarjeta) : 0;

    public List<string> Warnings { get; } = new();

    /// <summary>Largura da pista, calçada e faixas sem pavimento de um lado (do eixo para fora).</summary>
    private (double Carriage, double Sidewalk, List<PavementGap> Gaps) SideInfo(List<ElementoSecao> side, int sigma)
    {
        var gaps = new List<PavementGap>();
        double a = MedianHalf;
        ElementoSecao? prev = null;
        for (int i = 0; i < side.Count; i++)
        {
            var e = side[i];
            var w = Math.Max(0.05, e.Largura);
            if (e.Tipo == TipoElementoSecao.Calcada)
            {
                a += AddedGutter(side, i);
                var gutter = PhysicalElements && prev is not { Elevado: true } ? Math.Max(0, e.Sarjeta) : 0;
                if (gutter > 0.01) gaps.Add(new PavementGap(sigma * (a - gutter / 2), gutter, Median: false));
                return (a, w, gaps);
            }
            if (e.Tipo == TipoElementoSecao.CanteiroFisico && PhysicalElements) gaps.Add(new PavementGap(sigma * (a + w / 2), w));
            a += w;
            prev = e;
        }
        return (a, 0, gaps);
    }

    /// <summary>Pavimento + registro da seção (usado por interseções e rotatórias).</summary>
    public RoadPavementDefinition PavementDefinition()
    {
        var (rc, rs, rg) = SideInfo(Right, -1);
        var (lc, ls, lg) = SideInfo(Left, +1);
        var d = new RoadPavementDefinition
        {
            Material = Pavement,
            Thickness = PavementThickness,
            RightWidth = rc,
            LeftWidth = lc,
            RightSidewalk = rs,
            LeftSidewalk = ls,
            CurbWidth = Right.Concat(Left).FirstOrDefault(x => x.Tipo == TipoElementoSecao.Calcada)?.MeioFioEfetivo ?? CurbWidth,
            TwoWay = TwoWay,
            StartSetback = StartSetback,
            EndSetback = EndSetback,
            CornerRadius = CornerRadius,
        };
        d.Gaps.AddRange(rg);
        d.Gaps.AddRange(lg);
        if (TwoWay && Center == CenterTreatment.Canteiro && MedianType == TipoCanteiro.Fisico && PhysicalElements)
            d.Gaps.Add(new PavementGap(0, MedianWidth));
        return d;
    }

    private double MedianHalf => TwoWay && Center == CenterTreatment.Canteiro ? MedianWidth / 2 : 0;

    /// <summary>Largura de um lado, do eixo (sem o canteiro central) ao alinhamento: elementos + sarjetas somadas.</summary>
    public double SideWidth(IEnumerable<ElementoSecao> side)
    {
        var l = side.ToList();
        return l.Sum(e => e.Largura) + Enumerable.Range(0, l.Count).Sum(i => AddedGutter(l, i));
    }

    /// <summary>Larguras da seção para conferência (m).</summary>
    public sealed record Larguras(double Total, double EntreMeiosFios, double ComMeiosFios, double Faixas, double Sarjetas, double MeiosFios,
        double Calcadas, double Canteiros, double Central);

    /// <summary>
    /// Larguras da seção: total (alinhamento a alinhamento), pista entre as faces dos meios-fios, pista com os meios-fios,
    /// faixas de tráfego, sarjetas, meios-fios, calçadas (sem o meio-fio) e canteiros.
    /// </summary>
    public Larguras Widths()
    {
        double faixas = 0, sarj = 0, guias = 0, calc = 0, cant = 0, entre = 2 * MedianHalf;
        foreach (var side in new[] { Right, Left })
        {
            var edge = 0.0;
            var reached = false;
            for (int i = 0; i < side.Count; i++)
            {
                var e = side[i];
                var w = Math.Max(0.05, e.Largura);
                if (ElementoSecao.EhFaixaDeTrafego(e.Tipo)) faixas += w;
                if (e.Tipo is TipoElementoSecao.CanteiroFisico or TipoElementoSecao.CanteiroPintado) cant += w;
                if (e.Tipo == TipoElementoSecao.Calcada)
                {
                    sarj += PhysicalElements && !(i > 0 && side[i - 1].Elevado) ? Math.Max(0, e.Sarjeta) : 0;
                    if (!reached) edge += AddedGutter(side, i);
                    var cw = PhysicalElements && Math.Abs(e.AlturaEfetiva - (i > 0 ? side[i - 1].AlturaEfetiva : 0)) > 0.02 ? Math.Min(e.MeioFioEfetivo, w) : 0;
                    guias += cw;
                    calc += w - cw;
                    reached = true;
                    continue;
                }
                if (!reached) edge += w;
            }
            entre += edge;
        }
        return new Larguras(TotalWidth, entre, entre + guias, faixas, sarj, guias, calc, cant, TwoWay && Center == CenterTreatment.Canteiro ? MedianWidth : 0);
    }

    /// <summary>Largura total entre os alinhamentos externos.</summary>
    public double TotalWidth => SideWidth(Right) + SideWidth(Left) + 2 * MedianHalf;

    /// <summary>Maior distância do eixo a uma das bordas externas (calçadas incluídas).</summary>
    public double MaxHalfWidth => Math.Max(SideWidth(Right), SideWidth(Left)) + MedianHalf;

    /// <summary>
    /// Raio mínimo das curvas do eixo para que a borda interna da via (e as linhas/calçadas) também façam curva:
    /// meia largura + 1,5 m.
    /// </summary>
    public static double MinAxisRadius(double maxHalfWidth) => maxHalfWidth + 1.5;

    /// <summary>
    /// Cantos vivos do eixo arredondados em todas as marcas da via (pavimento, linhas, calçadas): raio pedido, nunca
    /// menor que <see cref="MinAxisRadius"/>.
    /// </summary>
    public static double ApplyAxisRadius(IEnumerable<MarkingDefinition> defs, double requested)
    {
        var list = defs.ToList();
        var pav = list.OfType<RoadPavementDefinition>().FirstOrDefault();
        var half = pav == null ? 0 : Math.Max(pav.TotalLeft, pav.TotalRight);
        var r = Math.Max(requested, MinAxisRadius(half));
        foreach (var d in list)
            if (d.Path != null && d is not IntersectionDefinition) d.Path.SmoothRadius = r;
        return r;
    }

    /// <summary>Largura da pista (somente faixas de tráfego, ciclofaixas, estacionamento e acostamentos).</summary>
    public double CarriagewayWidth => Right.Concat(Left).Where(e => e.Tipo is not (TipoElementoSecao.Calcada or TipoElementoSecao.CanteiroFisico)).Sum(e => e.Largura);

    /// <summary>Gera todas as definições (todas associadas ao mesmo caminho).</summary>
    public List<MarkingDefinition> Build(PathReference path, OutputSettings output, Catalogo? catalog = null, string? existingGroupId = null, string? existingPavementId = null,
        Polyline2? axis = null)
    {
        Warnings.Clear();
        var groupId = existingGroupId ?? Guid.NewGuid().ToString("N");
        var res = new List<MarkingDefinition>();

        T Add<T>(T d) where T : MarkingDefinition
        {
            d.Output = output.Clone();
            d.GroupId = groupId;
            d.Hierarchy = Hierarchy == HierarquiaViaria.NaoDefinida ? null : Hierarchy;
            d.SetPath(Clone(path));
            res.Add(d);
            return d;
        }

        LinearMarkingDefinition Line(string code, double offset, string? variant = null, bool invert = false, double? width = null) => Add(new LinearMarkingDefinition
        {
            Code = code,
            Speed = variant == null ? Speed : null,
            Variant = variant,
            Offset = offset,
            InvertSides = invert,
            WidthOverride = width,
            StartSetback = StartSetback,
            EndSetback = EndSetback,
        });

        LinearMarkingDefinition Physical(string code, double offset, double width, double? top = null, MarkingColor? color = null) => Add(new LinearMarkingDefinition
        {
            Code = code,
            Offset = offset,
            WidthOverride = width,
            StartSetback = StartSetback,
            EndSetback = EndSetback,
            // Nível só gravado quando difere do catálogo (0,15 m apoiado na pista) – vias antigas continuam iguais.
            Height = top is { } t && Math.Abs(t - CurbHeight) > 1e-6 ? t : null,
            ColorOverride = color,
        });

        void Hatch(string code, double offset, double width) => Add(new HatchMarkingDefinition
        {
            Code = code,
            StripOffset = offset,
            StripWidth = width,
        });

        // ------------------------------------------------ pavimento (sempre registrado: guarda a seção para interseções)
        RoadPavementDefinition? pavement = null;
        if (Pavement != TipoPavimento.Nenhum)
        {
            var pav = Add(PavementDefinition());
            pav.SetupJson = RoadTemplates.ToJson(this);
            if (existingPavementId != null) pav.Id = existingPavementId;
            pavement = pav;
        }
        // Retornos: alargamentos e bolsões laterais entram como recuos derivados (só nesta geração – a seção gravada guarda o retorno).
        var retornos = RetornoPlans();
        var derived = retornos.SelectMany(p => p.Recesses).ToList();
        Recuos.AddRange(derived);

        // ------------------------------------------------ eixo central
        var half = MedianHalf;
        if (TwoWay)
        {
            switch (Center)
            {
                case CenterTreatment.LFO1: Line("LFO-1", 0); break;
                case CenterTreatment.LFO2: Line("LFO-2", 0); break;
                case CenterTreatment.LFO3: Line("LFO-3", 0); break;
                case CenterTreatment.LFO4: Line("LFO-4", 0, invert: InvertCenter); break;
                case CenterTreatment.Canteiro:
                    if (MedianType == TipoCanteiro.Fisico)
                    {
                        if (PhysicalElements) PhysicalMedian(0, MedianWidth, MedianHeight, MedianVegetationHeight ?? MedianHeight);
                        if (EdgeLines)
                        {
                            if (IsLane(Left, 0)) Line(EdgeCode, half + EdgeInset);
                            if (IsLane(Right, 0)) Line(EdgeCode, -(half + EdgeInset));
                        }
                    }
                    else
                    {
                        Hatch("ZPA-A", 0, MedianWidth);
                    }
                    break;
            }
            if (!string.IsNullOrWhiteSpace(MedianDevice))
                Add(new DeviceMarkingDefinition { Code = MedianDevice!, Offset = 0, StartSetback = StartSetback, EndSetback = EndSetback });
            if (!string.IsNullOrWhiteSpace(CenterStudsCode) && Center is not (CenterTreatment.Canteiro or CenterTreatment.Nenhum))
                Line(CenterStudsCode!, 0, variant: string.IsNullOrWhiteSpace(CenterStudsVariant) ? null : CenterStudsVariant);
        }
        else if (IsLane(Left, 0) && IsLane(Right, 0))
        {
            // Mão única: divisão entre as faixas adjacentes ao eixo.
            BoundaryLine(Left[0], Right[0], 0, 0);
        }

        BuildSide(Right, -1, reverseTraffic: false);
        BuildSide(Left, +1, reverseTraffic: TwoWay);
        // Largura variável (levantamento) e recuos (baias, faixas de aceleração/desaceleração).
        MarkingDefinition AddVar(MarkingDefinition d)
        {
            d.Output = output.Clone();
            d.GroupId = groupId;
            d.Hierarchy = Hierarchy == HierarquiaViaria.NaoDefinida ? null : Hierarchy;
            switch (d)
            {
                case SignDefinition sg: sg.Z = path.Z; break;
                case UrbanElementDefinition ue: ue.Z = path.Z; break;
                case RecessMarkingDefinition rc: rc.Reverse = rc.Left && TwoWay; d.SetPath(Clone(path)); break;
                case SymbolMarkingDefinition sm: sm.Z = path.Z; break;
                case LinearMarkingDefinition { PathRef.Points.Count: >= 2 }: break;
                default: d.SetPath(Clone(path)); break;
            }
            res.Add(d);
            return d;
        }
        ApplyVariation(res, axis, new Dictionary<MarkingDefinition, (int, double, double)>(), AddVar);
        // Retornos em U: abertura do canteiro, vagas e linhas interrompidas e a sinalização.
        ApplyRetornos(res, pavement, retornos, axis ?? (path.Points.Count >= 2 ? new Polyline2(path.Points) : null), AddVar);
        Recuos.RemoveAll(derived.Contains);
        return res;

        // ------------------------------------------------ lados
        void BuildSide(List<ElementoSecao> side, int sigma, bool reverseTraffic)
        {
            double a = half;
            // Níveis (topo) de cada elemento; faixas pintadas não aceitam nível negativo (ficariam sob o pavimento).
            var hs = side.Select(x => x.Tipo is TipoElementoSecao.Calcada or TipoElementoSecao.CanteiroFisico ? x.AlturaEfetiva : Math.Max(0, x.AlturaEfetiva)).ToList();
            foreach (var x in side.Where(x => x.Tipo is not (TipoElementoSecao.Calcada or TipoElementoSecao.CanteiroFisico) && x.AlturaEfetiva < -1e-6))
                Warnings.Add($"{ElementoSecao.Rotulo(x.Tipo)}: nível negativo não se aplica a faixas da pista – usado 0.");
            double H(int k) => k < 0 || k >= side.Count ? 0 : hs[k];
            // Degrau entre dois elementos vizinhos de pista (canteiros e calçadas têm os próprios meios-fios).
            bool Step(int k) => k > 0 && k < side.Count && Math.Abs(H(k) - H(k - 1)) > 0.02
                                && side[k].Tipo is not (TipoElementoSecao.Calcada or TipoElementoSecao.CanteiroFisico)
                                && side[k - 1].Tipo != TipoElementoSecao.CanteiroFisico;
            for (int i = 0; i < side.Count; i++)
            {
                var e = side[i];
                // Sarjeta somada: a pista cresce até a face do meio-fio e a faixa mantém a largura útil.
                a += AddedGutter(side, i);
                var w = Math.Max(0.05, e.Largura);
                var b = a + w;
                var c = (a + b) / 2;
                var h = hs[i];

                // Linha na divisa interna (entre o elemento anterior e este) – ou meio-fio, quando há degrau.
                if (i == 0 && e.Elevado && PhysicalElements)
                    Physical("MEIO-FIO", sigma * (a + CurbWidth / 2), CurbWidth, h);
                if (i > 0 && Step(i) && PhysicalElements)
                {
                    // Meio-fio do lado mais alto, com o topo no nível dele.
                    var up = H(i) > H(i - 1);
                    Physical("MEIO-FIO", sigma * (up ? a + CurbWidth / 2 : a - CurbWidth / 2), CurbWidth, Math.Max(H(i), H(i - 1)));
                }
                else if (i > 0) BoundaryLine(side[i - 1], e, a, sigma);
                var first = res.Count;
                // Um único dispositivo por alinhamento (evita bloqueios/defensas sobrepostos na mesma divisa).
                if (!string.IsNullOrWhiteSpace(e.Dispositivo)
                    && !res.OfType<DeviceMarkingDefinition>().Any(x => string.Equals(x.Code, e.Dispositivo, StringComparison.OrdinalIgnoreCase) && Math.Abs(x.Offset - sigma * a) < 0.05))
                    Add(new DeviceMarkingDefinition { Code = e.Dispositivo!, Offset = sigma * a, StartSetback = StartSetback, EndSetback = EndSetback });

                switch (e.Tipo)
                {
                    case TipoElementoSecao.FaixaExclusiva:
                    case TipoElementoSecao.FaixaPreferencial:
                        if (e.FundoOnibus)
                        {
                            // Fundo entre as linhas de bordo (0,20 m) para não encobri-las.
                            Add(new LinearMarkingDefinition { Code = "ONI-FD", Offset = sigma * c, WidthOverride = Math.Max(0.5, w - 0.40),
                                ColorOverride = e.CorOnibus, StartSetback = StartSetback, EndSetback = EndSetback });
                        }
                        if (Inscriptions && e.Espacamento > 0 && !string.IsNullOrWhiteSpace(e.Legenda))
                            Add(new RepeatedMarkingDefinition
                            {
                                Text = e.Legenda, Offset = sigma * c, Spacing = e.Espacamento, Reverse = reverseTraffic,
                                StartOffset = Math.Max(10, StartSetback + 5), EndSetback = EndSetback + 5,
                                TextHeight = Speed > 60 ? 2.40 : 1.60, WidthFactor = 0.30, LetterSpacing = 0.05,
                            });
                        break;

                    case TipoElementoSecao.Ciclofaixa:
                        if (e.PinturaFundo) Line("CIC-FD", sigma * c, width: w);
                        if (e.Bidirecional && e.LinhaCentral)
                            Line("CIC-LC", sigma * c, variant: "Seccionada 0,10 m (1 × 1 m)");
                        if (Inscriptions && e.Espacamento > 0)
                        {
                            var start = Math.Max(5, StartSetback + 3);
                            Add(new RepeatedMarkingDefinition
                            {
                                SymbolCode = "SIC", Length = Math.Max(0.3, e.TamanhoSimbolo), Offset = sigma * (e.Bidirecional ? a + w / 4 : c),
                                Spacing = e.Espacamento, Reverse = reverseTraffic, StartOffset = start, EndSetback = EndSetback + 3,
                            });
                            if (!e.Bidirecional && e.TamanhoSeta > 0)
                                Add(new RepeatedMarkingDefinition
                                {
                                    SymbolCode = "CIC-SETA", Length = e.TamanhoSeta, Offset = sigma * c, Spacing = e.Espacamento,
                                    Reverse = reverseTraffic, StartOffset = start + e.TamanhoSimbolo + Math.Max(0, e.DistanciaSeta), EndSetback = EndSetback + 3,
                                });
                        }
                        break;

                    case TipoElementoSecao.FaixaCaminhada:
                    {
                        var edge = 0.10;
                        Add(new LinearMarkingDefinition { Code = "FCA", Offset = sigma * c, WidthOverride = Math.Max(0.1, w - 2 * edge),
                            ColorOverride = e.CorCaminhada, StartSetback = StartSetback, EndSetback = EndSetback });
                        Add(new LinearMarkingDefinition { Code = "FCA-BD", Offset = sigma * (a + edge / 2), StartSetback = StartSetback, EndSetback = EndSetback });
                        Add(new LinearMarkingDefinition { Code = "FCA-BD", Offset = sigma * (b - edge / 2), StartSetback = StartSetback, EndSetback = EndSetback });
                        if (Inscriptions && e.Espacamento > 0)
                            Add(new RepeatedMarkingDefinition
                            {
                                SymbolCode = "SPE", Length = Math.Min(1.5, w * 0.9), Offset = sigma * c, Spacing = e.Espacamento,
                                Reverse = reverseTraffic, StartOffset = Math.Max(5, StartSetback + 3), EndSetback = EndSetback + 3,
                            });
                        break;
                    }

                    case TipoElementoSecao.Estacionamento:
                    {
                        var preset = catalog?.Vaga(e.Vaga);
                        var parallel = preset == null || preset.Angulo < 1;
                        var depth = preset == null ? w : parallel ? preset.Largura : preset.Comprimento * Math.Sin(preset.Angulo * Math.PI / 180) + preset.Largura * Math.Cos(preset.Angulo * Math.PI / 180);
                        if (!parallel && depth > w + 0.05)
                            Warnings.Add($"Estacionamento {e.Vaga}: as vagas ocupam {depth:0.00} m, mais que a largura da faixa ({w:0.00} m).");
                        Add(new ParkingMarkingDefinition
                        {
                            Code = e.Vaga,
                            // Vagas partem do alinhamento externo (meio-fio) em direção ao eixo.
                            CurbOffset = -b,
                            RightSide = sigma > 0,
                            StallWidth = parallel ? w : null,
                            BackLine = true,
                            StartOffset = StartSetback,
                            FlipAngle = reverseTraffic,
                        });
                        break;
                    }

                    case TipoElementoSecao.FaixaSeguranca:
                        Hatch("ZPA", sigma * c, w);
                        break;

                    case TipoElementoSecao.CanteiroPintado:
                        Hatch("ZPA", sigma * c, w);
                        break;

                    case TipoElementoSecao.CanteiroFisico:
                        if (PhysicalElements) PhysicalMedian(sigma * c, w, h, e.AlturaVegetacaoEfetiva);
                        break;

                    case TipoElementoSecao.Calcada:
                        if (PhysicalElements) Sidewalk(e, a, b, sigma, h, H(i - 1));
                        if (i < side.Count - 1) Warnings.Add("A calçada deve ser o último elemento do lado (alinhamento do lote).");
                        break;
                }

                // Faixa elevada (ciclovia no nível da calçada, estacionamento elevado...): laje própria entre os meios-fios
                // e a pintura do elemento (fundo, símbolos, legendas) assentada sobre ela.
                if (e.Elevado && PhysicalElements)
                {
                    for (int k = first; k < res.Count; k++)
                        if (!(res[k] is LinearMarkingDefinition pl && RoadSectionInference.IsPhysical(pl.Code)))
                            res[k].Output.ElevationOffset = h + 0.001;
                    var curbIn = i == 0 || Step(i) && H(i) > H(i - 1) ? CurbWidth : 0;
                    var curbOut = i + 1 < side.Count && Step(i + 1) && H(i) > H(i + 1)
                                  || i + 1 == side.Count ? CurbWidth : 0;
                    if (i + 1 == side.Count) Physical("MEIO-FIO", sigma * (b - CurbWidth / 2), CurbWidth, h);
                    var pw = w - curbIn - curbOut;
                    if (pw > 0.05)
                        Physical("PLATAFORMA", sigma * (a + curbIn + pw / 2), pw, h,
                            e.Tipo == TipoElementoSecao.Ciclofaixa && e.PinturaFundo ? MarkingColor.Vermelha
                            : ElementoSecao.EhFaixaDeTrafego(e.Tipo) ? MarkingColor.PavimentoConcreto : null);
                }

                a = b;
            }

            // Bordo externo quando o último elemento é uma faixa de tráfego.
            if (side.Count > 0 && ElementoSecao.EhFaixaDeTrafego(side[^1].Tipo) && EdgeLines)
                Line(EdgeCode, sigma * (a - EdgeInset));
        }

        // Linha entre dois elementos adjacentes (interno → externo) na distância 'at' do eixo.
        void BoundaryLine(ElementoSecao inner, ElementoSecao outer, double at, int sigma)
        {
            var ti = inner.Tipo;
            var to = outer.Tipo;
            bool li = ElementoSecao.EhFaixaDeTrafego(ti), lo = ElementoSecao.EhFaixaDeTrafego(to);
            if (li && lo)
            {
                if (ti == TipoElementoSecao.FaixaExclusiva ^ to == TipoElementoSecao.FaixaExclusiva)
                    Line("MFE", sigma * at, variant: "Contínua 0,20 m");
                else if (ti == TipoElementoSecao.FaixaPreferencial ^ to == TipoElementoSecao.FaixaPreferencial)
                    Line("MFE", sigma * at, variant: "Seccionada 0,20 m (1 × 1 m)");
                else
                    Line(LaneDividerCode, sigma * at);
                return;
            }
            if ((li && to == TipoElementoSecao.Ciclofaixa) || (lo && ti == TipoElementoSecao.Ciclofaixa))
            {
                var bike = to == TipoElementoSecao.Ciclofaixa ? outer : inner;
                var line = Line("CIC-LD", sigma * at, variant: bike.LinhaSeccionada ? "Seccionada 0,20 m (1 × 1 m)" : "Contínua 0,20 m",
                    width: Math.Abs(bike.LarguraLinha - 0.20) > 1e-6 ? bike.LarguraLinha : null);
                if (bike.LinhaSeccionada) line.PatternOverride = new[] { Math.Max(0.1, bike.TracoLinha), Math.Max(0, bike.EspacoLinha) };
                return;
            }
            // Sarjeta da calçada: ocupa a borda da pista; a linha de bordo fica além dela.
            // (sem sarjeta atrás de uma faixa elevada: a água corre no nível da pista, não sobre a plataforma)
            var gutter = to == TipoElementoSecao.Calcada && PhysicalElements && !inner.Elevado ? Math.Max(0, outer.Sarjeta) : 0;
            if (gutter > 0.01) Physical("SARJETA", sigma * (at - gutter / 2), gutter);
            if (!EdgeLines) return;
            static bool EdgeLike(TipoElementoSecao t) => t is TipoElementoSecao.Acostamento or TipoElementoSecao.CanteiroFisico or TipoElementoSecao.Calcada;
            if (li && EdgeLike(to)) Line(EdgeCode, sigma * (at - gutter - EdgeInset));
            else if (lo && EdgeLike(ti)) Line(EdgeCode, sigma * (at + EdgeInset));
        }

        void PhysicalMedian(double centerOffset, double width, double top, double vegetation)
        {
            // Meios-fios com o topo no nível do canteiro; num canteiro rebaixado (jardim de chuva) a guia fica 0,10 m acima da pista.
            var curbTop = top > 0.01 ? top : 0.10;
            if (width <= 2 * CurbWidth + 0.05)
            {
                Physical("MEIO-FIO", centerOffset, width, curbTop);
                return;
            }
            Physical("MEIO-FIO", centerOffset + (width / 2 - CurbWidth / 2), CurbWidth, curbTop);
            Physical("MEIO-FIO", centerOffset - (width / 2 - CurbWidth / 2), CurbWidth, curbTop);
            Physical("GRAMADO", centerOffset, width - 2 * CurbWidth, vegetation);
        }

        void Sidewalk(ElementoSecao e, double a, double b, int sigma, double top, double inner)
        {
            var width = b - a;
            var cw = Math.Min(e.MeioFioEfetivo, width - 0.05);
            // Meio-fio só onde há degrau para o elemento interno (calçada no nível de uma ciclovia elevada não tem guia entre elas).
            var curb = Math.Abs(top - inner) > 0.02 ? cw : 0;
            if (curb > 0) Physical("MEIO-FIO", sigma * (a + cw / 2), cw, Math.Max(top, inner));
            var service = Math.Clamp(e.FaixaServico, cw, width) - cw;
            var access = Math.Clamp(e.FaixaAcesso, 0, Math.Max(0, width - cw - service));
            var free = width - cw - service - access;
            var s0 = a + cw;
            if (curb == 0) { service += cw; s0 = a; }
            LinearMarkingDefinition? sd = null, fd = null, ad = null;
            if (service > 0.02)
            {
                if (e.ServicoGramado) sd = Physical("GRAMADO", sigma * (s0 + service / 2), service, e.AlturaVegetacaoEfetiva);
                else sd = Physical("CALCADA", sigma * (s0 + service / 2), service, top);
            }
            if (free > 0.02) fd = Physical("CALCADA", sigma * (s0 + service + free / 2), free, top);
            if (access > 0.02) ad = Physical("CALCADA", sigma * (b - access / 2), access, top);
            SidewalkLevelProfiles(e, a, b, top, curb, service, access, sd, fd, ad, sigma > 0);
            if (free < 1.20 - 1e-6)
                Warnings.Add($"Calçada com faixa livre de {free:0.00} m – a NBR 9050 exige no mínimo 1,20 m.");
        }
    }

    private static bool IsLane(List<ElementoSecao> side, int i) => side.Count > i && ElementoSecao.EhFaixaDeTrafego(side[i].Tipo);

    private static PathReference Clone(PathReference p)
    {
        var c = p.Clone();
        c.Closed = false;
        return c;
    }

    /// <summary>Lê uma lista de larguras no formato "3,50; 3,30" ou "3.5 3.3".</summary>
    public static List<double> ParseWidths(string text)
    {
        var res = new List<double>();
        foreach (var tok in text.Split(new[] { ';', ' ', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = tok.Trim().Replace(',', '.');
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0)
                res.Add(v);
        }
        return res;
    }

    public RoadSetup Clone()
    {
        var c = (RoadSetup)MemberwiseClone();
        c.Right = Right.Select(e => e.Clone()).ToList();
        c.Left = Left.Select(e => e.Clone()).ToList();
        c.LargurasVariaveis = LargurasVariaveis.Select(p => p.Clone()).ToList();
        c.Recuos = Recuos.Select(r => r.Clone()).ToList();
        c.Retornos = Retornos.Select(r => r.Clone()).ToList();
        return c;
    }
}

/// <summary>Modelos prontos de seção transversal.</summary>
public static class RoadTemplates
{
    public sealed record Template(string Name, Func<RoadSetup> Create)
    {
        public override string ToString() => Name;
    }

    private static ElementoSecao E(TipoElementoSecao t, double w, Action<ElementoSecao>? cfg = null)
    {
        var e = new ElementoSecao { Tipo = t, Largura = w };
        cfg?.Invoke(e);
        return e;
    }

    private const TipoElementoSecao R = TipoElementoSecao.FaixaRolamento;

    /// <summary>Seção em JSON (modelos personalizados).</summary>
    public static string ToJson(RoadSetup s) => System.Text.Json.JsonSerializer.Serialize(s, JsonConfig.Options);

    public static RoadSetup? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<RoadSetup>(json, JsonConfig.Options); }
        catch { return null; }
    }

    private static readonly List<Template> Base = new()
    {
        new("Via local – 1 faixa por sentido, estacionamento e calçadas", () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Local, Speed = 40, Center = CenterTreatment.LFO2,
            Right = { E(R, 3.00), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 2.50) },
            Left = { E(R, 3.00), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 2.50) },
        }),
        new("Via coletora – 2 faixas por sentido e calçadas", () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Coletora, Speed = 50, Center = CenterTreatment.LFO1,
            Right = { E(R, 3.30), E(R, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
            Left = { E(R, 3.30), E(R, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
        }),
        new("Avenida – canteiro central, 2 + 2 faixas, estacionamento e calçadas", () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Arterial, Speed = 60, Center = CenterTreatment.Canteiro, MedianWidth = 3.0, MedianType = TipoCanteiro.Fisico,
            Right = { E(R, 3.30), E(R, 3.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 3.50) },
            Left = { E(R, 3.30), E(R, 3.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 3.50) },
        }),
        new("Corredor de ônibus – faixa exclusiva junto ao canteiro central", () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Arterial, Speed = 50, Center = CenterTreatment.Canteiro, MedianWidth = 2.0, MedianType = TipoCanteiro.Fisico,
            Right = { E(TipoElementoSecao.FaixaExclusiva, 3.50), E(R, 3.30), E(R, 3.30), E(TipoElementoSecao.Calcada, 3.50) },
            Left = { E(TipoElementoSecao.FaixaExclusiva, 3.50), E(R, 3.30), E(R, 3.30), E(TipoElementoSecao.Calcada, 3.50) },
        }),
        new("Faixa preferencial de ônibus junto à calçada", () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Arterial, Speed = 50, Center = CenterTreatment.LFO3,
            Right = { E(R, 3.30), E(TipoElementoSecao.FaixaPreferencial, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
            Left = { E(R, 3.30), E(TipoElementoSecao.FaixaPreferencial, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
        }),
        new("Via com ciclofaixa segregada e faixa de segurança", () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Coletora, Speed = 50, Center = CenterTreatment.LFO2,
            Right = { E(R, 3.30), E(TipoElementoSecao.FaixaSeguranca, 0.60), E(TipoElementoSecao.Ciclofaixa, 1.50, e => e.Dispositivo = "SEG-CIC"), E(TipoElementoSecao.Calcada, 2.50) },
            Left = { E(R, 3.30), E(TipoElementoSecao.Calcada, 2.50) },
        }),
        new("Via com canteiros laterais (via marginal / estacionamento)", () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Arterial, Speed = 60, Center = CenterTreatment.LFO3,
            Right = { E(R, 3.50), E(R, 3.50), E(TipoElementoSecao.CanteiroFisico, 1.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 3.00) },
            Left = { E(R, 3.50), E(R, 3.50), E(TipoElementoSecao.CanteiroFisico, 1.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 3.00) },
        }),
        new("Rodovia de pista simples com acostamentos", () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Rodovia, Speed = 80, Center = CenterTreatment.LFO2, CenterStudsCode = "TAC-A", CenterStudsVariant = "Espaçamento 16 m (tangente)",
            Right = { E(R, 3.50), E(TipoElementoSecao.Acostamento, 2.50) },
            Left = { E(R, 3.50), E(TipoElementoSecao.Acostamento, 2.50) },
        }),
        new("Rodovia de pista dupla – canteiro central com barreira New Jersey", () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Rodovia, Speed = 100, Center = CenterTreatment.Canteiro, MedianWidth = 1.20, MedianType = TipoCanteiro.Pintado, MedianDevice = "NJ",
            Right = { E(R, 3.60), E(R, 3.60), E(TipoElementoSecao.Acostamento, 3.00) },
            Left = { E(R, 3.60), E(R, 3.60), E(TipoElementoSecao.Acostamento, 3.00) },
        }),
        new("Via de mão única – 3 faixas e calçadas", () => new RoadSetup
        {
            TwoWay = false, Hierarchy = HierarquiaViaria.Coletora, Speed = 50, Center = CenterTreatment.Nenhum,
            Right = { E(R, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
            Left = { E(R, 3.50), E(R, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
        }),
    };

    /// <summary>Modelos prontos: a sarjeta é somada à largura da pista (as faixas mantêm a largura útil).</summary>
    public static IReadOnlyList<Template> All { get; } = Base.Select(t => new Template(t.Name, () =>
    {
        var s = t.Create();
        s.SarjetaSomada ??= true;
        return s;
    })).ToList();
}
