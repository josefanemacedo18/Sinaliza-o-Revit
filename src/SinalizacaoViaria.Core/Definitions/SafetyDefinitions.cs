using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>Tipo de sonorizador longitudinal.</summary>
public enum TipoSonorizador
{
    /// <summary>Ranhuras fresadas no pavimento (acostamento ou bordo) – "rumble strip" fresado.</summary>
    Fresado,
    /// <summary>Linha de bordo/eixo em termoplástico com relevo (perfilada) – vibra e continua visível à noite e com chuva.</summary>
    TermoplasticoRelevo,
    /// <summary>Barras transversais em relevo (plástico a frio/termoplástico) em série.</summary>
    BarrasRelevo,
    /// <summary>Tachas sonorizadoras (elementos pré-fabricados) em série.</summary>
    Tachas,
}

/// <summary>Posição do sonorizador em relação à via.</summary>
public enum PosicaoSonorizador { Bordo, Acostamento, Eixo, Livre }

/// <summary>
/// Sonorizador longitudinal ao longo de um caminho (bordo, acostamento ou eixo): elementos repetidos com comprimento,
/// largura, espaçamento e altura/profundidade próprios, em trechos com interrupções para ciclistas.
/// </summary>
public sealed class RumbleStripDefinition : MarkingDefinition
{
    public PathReference PathRef { get; set; } = new();
    public TipoSonorizador Type { get; set; } = TipoSonorizador.Fresado;
    public PosicaoSonorizador Position { get; set; } = PosicaoSonorizador.Acostamento;
    /// <summary>Deslocamento lateral do centro dos elementos (m, + à esquerda do caminho).</summary>
    public double Offset { get; set; }
    /// <summary>Dimensão do elemento ao longo da via (m).</summary>
    public double ElementLength { get; set; } = 0.18;
    /// <summary>Dimensão transversal (m).</summary>
    public double ElementWidth { get; set; } = 0.40;
    /// <summary>Espaçamento entre elementos, de centro a centro (m).</summary>
    public double Spacing { get; set; } = 0.30;
    /// <summary>Profundidade (fresado) ou altura do relevo (m).</summary>
    public double Depth { get; set; } = 0.013;
    /// <summary>Trecho contínuo e interrupção (m) – interrupções para a travessia de ciclistas. 0 = contínuo.</summary>
    public double SegmentLength { get; set; }
    public double GapLength { get; set; }
    public double StartSetback { get; set; }
    public double EndSetback { get; set; }
    public MarkingColor? Color { get; set; }
    /// <summary>Elementos em ângulo (chevron) em relação à transversal (graus). 0 = perpendiculares.</summary>
    public double Angle { get; set; }

    public override string KindName => "Sonorizador longitudinal";
    public override string DisplayCode => Type switch
    {
        TipoSonorizador.Fresado => "SON-FRE",
        TipoSonorizador.TermoplasticoRelevo => "SON-TER",
        TipoSonorizador.BarrasRelevo => "SON-BAR",
        _ => "SON-TAC",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;

    /// <summary>Valores de referência (FHWA/DNIT, indicativos) de cada tipo.</summary>
    public void ApplyPreset(TipoSonorizador t)
    {
        Type = t;
        switch (t)
        {
            case TipoSonorizador.Fresado:
                ElementLength = 0.18; ElementWidth = Position == PosicaoSonorizador.Eixo ? 0.30 : 0.40; Spacing = 0.30; Depth = 0.013; Color = null; break;
            case TipoSonorizador.TermoplasticoRelevo:
                ElementLength = 0.05; ElementWidth = 0.15; Spacing = 0.50; Depth = 0.006; Color = MarkingColor.Branca; break;
            case TipoSonorizador.BarrasRelevo:
                ElementLength = 0.10; ElementWidth = 0.30; Spacing = 0.60; Depth = 0.008; Color = MarkingColor.Branca; break;
            default:
                ElementLength = 0.10; ElementWidth = 0.10; Spacing = 1.00; Depth = 0.015; Color = MarkingColor.Amarela; break;
        }
    }
}

/// <summary>Material do leito da caixa de retenção (resistência ao rolamento – AASHTO).</summary>
public enum MaterialRetencao
{
    /// <summary>Seixo rolado / cascalho arredondado de granulometria uniforme (R = 0,25) – o recomendado.</summary>
    Seixo,
    /// <summary>Cascalho solto (R = 0,10).</summary>
    Cascalho,
    /// <summary>Areia (R = 0,15).</summary>
    Areia,
    /// <summary>Brita solta (R = 0,05).</summary>
    Brita,
}

/// <summary>
/// Área de escape de caminhões (rampa de fuga): caixa de retenção com material granular solto, entrada em teiper a partir
/// do bordo, faixa de serviço pavimentada ao lado (retirada dos veículos, com âncoras), barreira/berma no fim, sinalização
/// e delineadores. O caminho é o eixo da caixa (no sentido do tráfego que entra).
/// </summary>
public sealed class EscapeRampDefinition : MarkingDefinition
{
    public PathReference PathRef { get; set; } = new();
    /// <summary>Velocidade de entrada de projeto (km/h) – caminhão sem freio (AASHTO: 130 a 140 km/h).</summary>
    public double EntrySpeed { get; set; } = 130;
    /// <summary>Rampa da caixa (%, positiva = aclive).</summary>
    public double Grade { get; set; } = 5;
    public MaterialRetencao Material { get; set; } = MaterialRetencao.Seixo;
    /// <summary>Comprimento da caixa (m). Nulo = calculado (L = V² / 254 (R + G)).</summary>
    public double? Length { get; set; }
    public double Width { get; set; } = 8.0;
    /// <summary>Profundidade do leito (m) – AASHTO 0,90 a 1,10 m.</summary>
    public double BedDepth { get; set; } = 1.0;
    /// <summary>Transição da profundidade na entrada (de 0,08 m à profundidade total), m.</summary>
    public double DepthTaper { get; set; } = 30;
    /// <summary>Faixa de serviço ao lado da caixa (m). 0 = sem.</summary>
    public double ServiceWidth { get; set; } = 3.5;
    /// <summary>Faixa de serviço à esquerda da caixa (no sentido de entrada).</summary>
    public bool ServiceLeft { get; set; } = true;
    /// <summary>Espaçamento das âncoras de reboque na faixa de serviço (m).</summary>
    public double AnchorSpacing { get; set; } = 60;
    /// <summary>Barreira (berma de material) no fim da caixa.</summary>
    public bool EndBerm { get; set; } = true;
    /// <summary>Zebrado de entrada, delineadores e placas.</summary>
    public bool Signage { get; set; } = true;

    [JsonIgnore]
    public double Resistance => Material switch
    {
        MaterialRetencao.Seixo => 0.25,
        MaterialRetencao.Areia => 0.15,
        MaterialRetencao.Cascalho => 0.10,
        _ => 0.05,
    };

    /// <summary>Comprimento necessário para parar (m): L = V² / (254 (R ± G)).</summary>
    [JsonIgnore]
    public double RequiredLength => EntrySpeed * EntrySpeed / (254 * Math.Max(0.02, Resistance + Grade / 100.0));

    [JsonIgnore]
    public double ActualLength => Length is > 5 ? Length.Value : Math.Ceiling(RequiredLength / 5) * 5;

    public override string KindName => "Área de escape (caixa de retenção)";
    public override string DisplayCode => "ESCAPE";
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}
