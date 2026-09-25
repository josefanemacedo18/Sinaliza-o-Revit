using System.Text.Json;
using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>Forma de representação no Revit.</summary>
public enum OutputMode
{
    /// <summary>Sólido 3D fino (DirectShape, categoria Modelos genéricos) – aparece em todas as vistas e em tabelas.</summary>
    Modelo3D,
    /// <summary>Região preenchida 2D na vista (componente de detalhe) – ideal para pranchas.</summary>
    Detalhe2D,
}

/// <summary>Como a marca obtém a elevação.</summary>
public sealed class OutputSettings
{
    public OutputMode Mode { get; set; } = OutputMode.Modelo3D;

    /// <summary>Espessura do sólido (m). 0 = espessura do material.</summary>
    public double Thickness { get; set; }

    /// <summary>Deslocamento vertical em relação ao caminho/superfície (m).</summary>
    public double ElevationOffset { get; set; } = 0.001;

    /// <summary>Projeta a marca sobre as superfícies selecionadas (Toposolid, Piso, Topografia...).</summary>
    public bool Drape { get; set; }

    /// <summary>UniqueIds das superfícies de projeção. Vazio = qualquer superfície visível na vista 3D.</summary>
    public List<string> SurfaceIds { get; set; } = new();

    /// <summary>UniqueId da vista (modo 2D).</summary>
    public string? ViewId { get; set; }

    /// <summary>Material de demarcação (nome do catálogo) – usado em quantitativos e espessura.</summary>
    public string? Material { get; set; }

    public OutputSettings Clone() => new()
    {
        Mode = Mode, Thickness = Thickness, ElevationOffset = ElevationOffset, Drape = Drape,
        SurfaceIds = new List<string>(SurfaceIds), ViewId = ViewId, Material = Material,
    };
}

/// <summary>
/// Caminho de referência de uma marca: elementos do Revit (linhas de modelo/detalhe) que a tornam
/// associativa, ou pontos fixos (quando criada por cliques).
/// </summary>
public sealed class PathReference
{
    /// <summary>UniqueIds das curvas (associativo).</summary>
    public List<string> ElementIds { get; set; } = new();

    /// <summary>Pontos fixos em metros (coordenadas internas do projeto).</summary>
    public List<Vec2> Points { get; set; } = new();

    /// <summary>Elevação (m) usada quando o caminho é por pontos.</summary>
    public double Z { get; set; }

    public bool Closed { get; set; }

    [JsonIgnore]
    public bool IsAssociative => ElementIds.Count > 0;

    public static PathReference FromPoints(IEnumerable<Vec2> pts, double z, bool closed = false) =>
        new() { Points = pts.ToList(), Z = z, Closed = closed };

    public static PathReference FromElements(IEnumerable<string> ids) => new() { ElementIds = ids.ToList() };
}

/// <summary>Definição paramétrica de uma marca: gravada no elemento (Extensible Storage) e usada para regenerá-lo.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo")]
[JsonDerivedType(typeof(LinearMarkingDefinition), "linear")]
[JsonDerivedType(typeof(HatchMarkingDefinition), "area")]
[JsonDerivedType(typeof(SymbolMarkingDefinition), "simbolo")]
[JsonDerivedType(typeof(TextMarkingDefinition), "legenda")]
[JsonDerivedType(typeof(ParkingMarkingDefinition), "vagas")]
[JsonDerivedType(typeof(RepeatedMarkingDefinition), "repetida")]
[JsonDerivedType(typeof(DeviceMarkingDefinition), "dispositivo")]
public abstract class MarkingDefinition
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Identificador do conjunto (todos os elementos Revit da mesma marca compartilham este id).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Agrupamento opcional (ex.: todas as linhas geradas por "Sinalizar via").</summary>
    public string? GroupId { get; set; }

    public OutputSettings Output { get; set; } = new();

    /// <summary>Observações do projetista.</summary>
    public string? Notes { get; set; }

    [JsonIgnore]
    public abstract string KindName { get; }

    /// <summary>Código exibido em tabelas (ex.: LFO-2, ZPA, PEM-F).</summary>
    public abstract string DisplayCode { get; }

    public virtual PathReference? Path => null;

    /// <summary>Substitui o caminho de referência (marcas baseadas em caminho).</summary>
    public virtual void SetPath(PathReference path) => throw new NotSupportedException($"{KindName} não usa caminho.");

    /// <summary>Desloca os dados posicionais que não dependem do caminho (pontos de inserção, eixos).</summary>
    public virtual void Translate(Vec2 delta, double dz) { }

    public string ToJson() => JsonSerializer.Serialize(this, JsonConfig.Compact);

    public static MarkingDefinition? FromJson(string json) =>
        JsonSerializer.Deserialize<MarkingDefinition>(json, JsonConfig.Compact);

    public MarkingDefinition CloneWithNewId()
    {
        var c = FromJson(ToJson())!;
        c.Id = Guid.NewGuid().ToString("N");
        return c;
    }
}

public sealed class LinearMarkingDefinition : MarkingDefinition
{
    public string Code { get; set; } = "LFO-2";
    public string? Variant { get; set; }
    /// <summary>Se preenchido, a variante é escolhida automaticamente pela velocidade (km/h).</summary>
    public double? Speed { get; set; }
    public PathReference PathRef { get; set; } = new();

    public double Offset { get; set; }
    public AlinhamentoPadrao? Alignment { get; set; }
    public double Phase { get; set; }
    public double StartSetback { get; set; }
    public double EndSetback { get; set; }
    public bool Reverse { get; set; }
    public bool InvertSides { get; set; }
    public double? WidthOverride { get; set; }
    public double[]? PatternOverride { get; set; }
    public MarkingColor? ColorOverride { get; set; }

    public override string KindName => "Linear";
    public override string DisplayCode => Code;
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

public sealed class HatchMarkingDefinition : MarkingDefinition
{
    public string Code { get; set; } = "ZPA";
    public PathReference Boundary { get; set; } = new() { Closed = true };
    public double? BarWidth { get; set; }
    public double? Gap { get; set; }
    public double? AngleDeg { get; set; }
    public double? BorderWidth { get; set; }
    public bool? Chevron { get; set; }
    public bool? Crossed { get; set; }
    public MarkingColor? BarColor { get; set; }
    public MarkingColor? BorderColor { get; set; }
    /// <summary>Direção de referência (sentido do tráfego) – dois cliques; nulo = maior lado.</summary>
    public Vec2? ReferenceDirection { get; set; }
    public Vec2? AxisPoint { get; set; }
    public double Phase { get; set; }

    /// <summary>
    /// Modo "faixa": em vez de um contorno fechado, a área é uma faixa de largura <see cref="StripWidth"/>
    /// ao longo do caminho (deslocada de <see cref="StripOffset"/>) – usada em canteiros pintados e faixas de segurança.
    /// </summary>
    public double? StripWidth { get; set; }
    public double StripOffset { get; set; }

    [JsonIgnore]
    public bool IsStrip => StripWidth is > 0;

    public override string KindName => "Área";
    public override string DisplayCode => Code;
    public override PathReference? Path => Boundary;
    public override void SetPath(PathReference path) { if (!IsStrip) path.Closed = true; Boundary = path; }
    public override void Translate(Vec2 delta, double dz) { if (AxisPoint is { } a) AxisPoint = a + delta; }
}

public sealed class SymbolMarkingDefinition : MarkingDefinition
{
    public string Code { get; set; } = "PEM-F";
    public double Length { get; set; } = 5.0;
    public double WidthFactor { get; set; } = 1.0;
    public Vec2 Position { get; set; }
    /// <summary>Sentido do tráfego (vetor unitário) – o símbolo "aponta" para esta direção.</summary>
    public Vec2 Direction { get; set; } = Vec2.UnitY;
    public double Z { get; set; }
    public bool Mirror { get; set; }
    public MarkingColor? ColorOverride { get; set; }

    public override string KindName => "Símbolo";
    public override string DisplayCode => Code;
    public override void Translate(Vec2 delta, double dz) { Position += delta; Z += dz; }
}

public sealed class TextMarkingDefinition : MarkingDefinition
{
    public string Text { get; set; } = "PARE";
    public double Height { get; set; } = 1.60;
    public double WidthFactor { get; set; } = 0.40;
    public double LetterSpacing { get; set; } = 0.08;
    public double LineSpacing { get; set; } = 1.00;
    public string FontFamily { get; set; } = "Arial";
    public bool Bold { get; set; } = true;
    public bool BottomToTop { get; set; } = true;
    public Vec2 Position { get; set; }
    public Vec2 Direction { get; set; } = Vec2.UnitY;
    public double Z { get; set; }
    public MarkingColor Color { get; set; } = MarkingColor.Branca;

    public override string KindName => "Legenda";
    public override string DisplayCode => "LEG";
    public override void Translate(Vec2 delta, double dz) { Position += delta; Z += dz; }
}

public sealed class ParkingMarkingDefinition : MarkingDefinition
{
    public string Code { get; set; } = "MER-90";
    public PathReference PathRef { get; set; } = new();
    public double? Angle { get; set; }
    public double? StallWidth { get; set; }
    public double? StallLength { get; set; }
    public double? LineWidth { get; set; }
    public MarkingColor? Color { get; set; }
    public int Count { get; set; }
    public bool RightSide { get; set; } = true;
    public double StartOffset { get; set; }
    public double CurbOffset { get; set; }
    public bool FlipAngle { get; set; }
    public bool BackLine { get; set; } = true;
    public bool FullOutline { get; set; }
    public bool IncludeSymbols { get; set; } = true;
    public MarkingColor? FillColor { get; set; }
    public double SymbolSize { get; set; } = 1.20;
    public double LegendHeight { get; set; } = 0.50;

    public override string KindName => "Estacionamento";
    public override string DisplayCode => Code;
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

/// <summary>
/// Símbolo ou legenda repetido ao longo de um caminho (ex.: "ÔNIBUS" a cada 50 m numa faixa exclusiva,
/// bicicletas numa ciclofaixa). Associativo: acompanha o eixo.
/// </summary>
public sealed class RepeatedMarkingDefinition : MarkingDefinition
{
    public PathReference PathRef { get; set; } = new();

    /// <summary>Código do símbolo do catálogo. Se vazio, usa <see cref="Text"/>.</summary>
    public string? SymbolCode { get; set; }
    public string? Text { get; set; }

    /// <summary>Comprimento do símbolo (m).</summary>
    public double Length { get; set; } = 1.5;

    public double Offset { get; set; }
    public double Spacing { get; set; } = 50;
    public double StartOffset { get; set; } = 10;
    public double EndSetback { get; set; } = 5;

    /// <summary>Tráfego no sentido contrário ao caminho (faixas do lado esquerdo em vias de mão dupla).</summary>
    public bool Reverse { get; set; }

    public double TextHeight { get; set; } = 1.60;
    public double WidthFactor { get; set; } = 0.30;
    public double LetterSpacing { get; set; } = 0.05;
    public double LineSpacing { get; set; } = 1.00;
    public string FontFamily { get; set; } = "Arial";
    public bool Bold { get; set; } = true;
    public MarkingColor? Color { get; set; }

    public override string KindName => "Inscrições repetidas";
    public override string DisplayCode => string.IsNullOrWhiteSpace(SymbolCode) ? "LEG" : SymbolCode!;
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

/// <summary>Dispositivos físicos (segregadores, balizadores, pilaretes, barreiras, defensas) ao longo de um caminho.</summary>
public sealed class DeviceMarkingDefinition : MarkingDefinition
{
    public string Code { get; set; } = "SEG-CIC";
    public PathReference PathRef { get; set; } = new();
    public double Offset { get; set; }
    /// <summary>Distância entre centros (m). Nulo = do catálogo; 0 = contínuo.</summary>
    public double? Spacing { get; set; }
    public double StartSetback { get; set; }
    public double EndSetback { get; set; }
    public double? LengthOverride { get; set; }
    public double? WidthOverride { get; set; }
    public double? HeightOverride { get; set; }
    public MarkingColor? Color { get; set; }
    public bool Reverse { get; set; }

    public override string KindName => "Dispositivo físico";
    public override string DisplayCode => Code;
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}
