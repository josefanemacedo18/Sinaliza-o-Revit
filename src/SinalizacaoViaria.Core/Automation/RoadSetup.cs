using System.Globalization;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

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

    public ElementoSecao Clone() => (ElementoSecao)MemberwiseClone();

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
public sealed class RoadSetup
{
    public const double CurbWidth = 0.15;

    /// <summary>Mão dupla (eixo = divisão de sentidos) ou mão única (todas as faixas no sentido do eixo).</summary>
    public bool TwoWay { get; set; } = true;

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

    public List<string> Warnings { get; } = new();

    private double MedianHalf => TwoWay && Center == CenterTreatment.Canteiro ? MedianWidth / 2 : 0;

    public double SideWidth(IEnumerable<ElementoSecao> side) => side.Sum(e => e.Largura);

    /// <summary>Largura total entre os alinhamentos externos.</summary>
    public double TotalWidth => SideWidth(Right) + SideWidth(Left) + 2 * MedianHalf;

    /// <summary>Largura da pista (somente faixas de tráfego, ciclofaixas, estacionamento e acostamentos).</summary>
    public double CarriagewayWidth => Right.Concat(Left).Where(e => e.Tipo is not (TipoElementoSecao.Calcada or TipoElementoSecao.CanteiroFisico)).Sum(e => e.Largura);

    /// <summary>Gera todas as definições (todas associadas ao mesmo caminho).</summary>
    public List<MarkingDefinition> Build(PathReference path, OutputSettings output, Catalogo? catalog = null)
    {
        Warnings.Clear();
        var groupId = Guid.NewGuid().ToString("N");
        var res = new List<MarkingDefinition>();

        T Add<T>(T d) where T : MarkingDefinition
        {
            d.Output = output.Clone();
            d.GroupId = groupId;
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

        LinearMarkingDefinition Physical(string code, double offset, double width) => Add(new LinearMarkingDefinition
        {
            Code = code,
            Offset = offset,
            WidthOverride = width,
            StartSetback = StartSetback,
            EndSetback = EndSetback,
        });

        void Hatch(string code, double offset, double width) => Add(new HatchMarkingDefinition
        {
            Code = code,
            StripOffset = offset,
            StripWidth = width,
        });

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
                        if (PhysicalElements) PhysicalMedian(0, MedianWidth);
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
        return res;

        // ------------------------------------------------ lados
        void BuildSide(List<ElementoSecao> side, int sigma, bool reverseTraffic)
        {
            double a = half;
            for (int i = 0; i < side.Count; i++)
            {
                var e = side[i];
                var w = Math.Max(0.05, e.Largura);
                var b = a + w;
                var c = (a + b) / 2;

                // Linha na divisa interna (entre o elemento anterior e este)
                if (i > 0) BoundaryLine(side[i - 1], e, a, sigma);
                if (!string.IsNullOrWhiteSpace(e.Dispositivo))
                    Add(new DeviceMarkingDefinition { Code = e.Dispositivo!, Offset = sigma * a, StartSetback = StartSetback, EndSetback = EndSetback });

                switch (e.Tipo)
                {
                    case TipoElementoSecao.FaixaExclusiva:
                    case TipoElementoSecao.FaixaPreferencial:
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
                        if (Inscriptions && e.Espacamento > 0)
                        {
                            Add(new RepeatedMarkingDefinition
                            {
                                SymbolCode = "SIC", Length = Math.Min(1.8, w * 1.1), Offset = sigma * c, Spacing = e.Espacamento,
                                Reverse = reverseTraffic, StartOffset = Math.Max(5, StartSetback + 3), EndSetback = EndSetback + 3,
                            });
                            if (!e.Bidirecional)
                                Add(new RepeatedMarkingDefinition
                                {
                                    SymbolCode = "CIC-SETA", Length = 1.5, Offset = sigma * c, Spacing = e.Espacamento,
                                    Reverse = reverseTraffic, StartOffset = Math.Max(5, StartSetback + 3) + 3.0, EndSetback = EndSetback + 3,
                                });
                        }
                        break;

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
                        if (PhysicalElements) PhysicalMedian(sigma * c, w);
                        break;

                    case TipoElementoSecao.Calcada:
                        if (PhysicalElements) Sidewalk(e, a, b, sigma);
                        if (i < side.Count - 1) Warnings.Add("A calçada deve ser o último elemento do lado (alinhamento do lote).");
                        break;
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
                Line("CIC-LD", sigma * at, variant: "Contínua 0,20 m");
                return;
            }
            if (!EdgeLines) return;
            static bool EdgeLike(TipoElementoSecao t) => t is TipoElementoSecao.Acostamento or TipoElementoSecao.CanteiroFisico or TipoElementoSecao.Calcada;
            if (li && EdgeLike(to)) Line(EdgeCode, sigma * (at - EdgeInset));
            else if (lo && EdgeLike(ti)) Line(EdgeCode, sigma * (at + EdgeInset));
        }

        void PhysicalMedian(double centerOffset, double width)
        {
            if (width <= 2 * CurbWidth + 0.05)
            {
                Physical("MEIO-FIO", centerOffset, width);
                return;
            }
            Physical("MEIO-FIO", centerOffset + (width / 2 - CurbWidth / 2), CurbWidth);
            Physical("MEIO-FIO", centerOffset - (width / 2 - CurbWidth / 2), CurbWidth);
            Physical("GRAMADO", centerOffset, width - 2 * CurbWidth);
        }

        void Sidewalk(ElementoSecao e, double a, double b, int sigma)
        {
            var width = b - a;
            Physical("MEIO-FIO", sigma * (a + CurbWidth / 2), CurbWidth);
            var service = Math.Clamp(e.FaixaServico, CurbWidth, width) - CurbWidth;
            var access = Math.Clamp(e.FaixaAcesso, 0, Math.Max(0, width - CurbWidth - service));
            var free = width - CurbWidth - service - access;
            var s0 = a + CurbWidth;
            if (service > 0.02) Physical(e.ServicoGramado ? "GRAMADO" : "CALCADA", sigma * (s0 + service / 2), service);
            if (free > 0.02) Physical("CALCADA", sigma * (s0 + service + free / 2), free);
            if (access > 0.02) Physical("CALCADA", sigma * (b - access / 2), access);
            if (free < 1.20 - 1e-6)
                Warnings.Add($"Calçada com faixa livre de {free:0.00} m – a NBR 9050 exige no mínimo 1,20 m.");
        }
    }

    private static bool IsLane(List<ElementoSecao> side, int i) => side.Count > i && ElementoSecao.EhFaixaDeTrafego(side[i].Tipo);

    private static PathReference Clone(PathReference p) => new()
    {
        ElementIds = new List<string>(p.ElementIds),
        Points = new List<Vec2>(p.Points),
        Z = p.Z,
        Closed = false,
    };

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

    public static IReadOnlyList<Template> All { get; } = new List<Template>
    {
        new("Via local – 1 faixa por sentido, estacionamento e calçadas", () => new RoadSetup
        {
            Speed = 40, Center = CenterTreatment.LFO2,
            Right = { E(R, 3.00), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 2.50) },
            Left = { E(R, 3.00), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 2.50) },
        }),
        new("Via coletora – 2 faixas por sentido e calçadas", () => new RoadSetup
        {
            Speed = 50, Center = CenterTreatment.LFO1,
            Right = { E(R, 3.30), E(R, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
            Left = { E(R, 3.30), E(R, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
        }),
        new("Avenida – canteiro central, 2 + 2 faixas, estacionamento e calçadas", () => new RoadSetup
        {
            Speed = 60, Center = CenterTreatment.Canteiro, MedianWidth = 3.0, MedianType = TipoCanteiro.Fisico,
            Right = { E(R, 3.30), E(R, 3.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 3.50) },
            Left = { E(R, 3.30), E(R, 3.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 3.50) },
        }),
        new("Corredor de ônibus – faixa exclusiva junto ao canteiro central", () => new RoadSetup
        {
            Speed = 50, Center = CenterTreatment.Canteiro, MedianWidth = 2.0, MedianType = TipoCanteiro.Fisico,
            Right = { E(TipoElementoSecao.FaixaExclusiva, 3.50), E(R, 3.30), E(R, 3.30), E(TipoElementoSecao.Calcada, 3.50) },
            Left = { E(TipoElementoSecao.FaixaExclusiva, 3.50), E(R, 3.30), E(R, 3.30), E(TipoElementoSecao.Calcada, 3.50) },
        }),
        new("Faixa preferencial de ônibus junto à calçada", () => new RoadSetup
        {
            Speed = 50, Center = CenterTreatment.LFO3,
            Right = { E(R, 3.30), E(TipoElementoSecao.FaixaPreferencial, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
            Left = { E(R, 3.30), E(TipoElementoSecao.FaixaPreferencial, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
        }),
        new("Via com ciclofaixa segregada e faixa de segurança", () => new RoadSetup
        {
            Speed = 50, Center = CenterTreatment.LFO2,
            Right = { E(R, 3.30), E(TipoElementoSecao.FaixaSeguranca, 0.60), E(TipoElementoSecao.Ciclofaixa, 1.50, e => e.Dispositivo = "SEG-CIC"), E(TipoElementoSecao.Calcada, 2.50) },
            Left = { E(R, 3.30), E(TipoElementoSecao.Calcada, 2.50) },
        }),
        new("Via com canteiros laterais (via marginal / estacionamento)", () => new RoadSetup
        {
            Speed = 60, Center = CenterTreatment.LFO3,
            Right = { E(R, 3.50), E(R, 3.50), E(TipoElementoSecao.CanteiroFisico, 1.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 3.00) },
            Left = { E(R, 3.50), E(R, 3.50), E(TipoElementoSecao.CanteiroFisico, 1.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Calcada, 3.00) },
        }),
        new("Rodovia de pista simples com acostamentos", () => new RoadSetup
        {
            Speed = 80, Center = CenterTreatment.LFO2, CenterStudsCode = "TAC-A", CenterStudsVariant = "Espaçamento 16 m (tangente)",
            Right = { E(R, 3.50), E(TipoElementoSecao.Acostamento, 2.50) },
            Left = { E(R, 3.50), E(TipoElementoSecao.Acostamento, 2.50) },
        }),
        new("Rodovia de pista dupla – canteiro central com barreira New Jersey", () => new RoadSetup
        {
            Speed = 100, Center = CenterTreatment.Canteiro, MedianWidth = 1.20, MedianType = TipoCanteiro.Pintado, MedianDevice = "NJ",
            Right = { E(R, 3.60), E(R, 3.60), E(TipoElementoSecao.Acostamento, 3.00) },
            Left = { E(R, 3.60), E(R, 3.60), E(TipoElementoSecao.Acostamento, 3.00) },
        }),
        new("Via de mão única – 3 faixas e calçadas", () => new RoadSetup
        {
            TwoWay = false, Speed = 50, Center = CenterTreatment.Nenhum,
            Right = { E(R, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
            Left = { E(R, 3.50), E(R, 3.50), E(TipoElementoSecao.Calcada, 3.00) },
        }),
    };
}
