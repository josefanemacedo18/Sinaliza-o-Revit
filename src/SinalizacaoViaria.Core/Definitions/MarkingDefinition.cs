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
[JsonDerivedType(typeof(SignDefinition), "placa")]
[JsonDerivedType(typeof(UrbanElementDefinition), "mobiliario")]
[JsonDerivedType(typeof(RampDefinition), "rampa")]
[JsonDerivedType(typeof(TrafficCalmingDefinition), "moderacao")]
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

    /// <summary>Elevação (m) das marcas posicionadas por ponto (sem caminho).</summary>
    public virtual double? PointZ => null;

    /// <summary>Áreas recortadas da marca (ex.: rebaixamentos de calçada). Coordenadas em metros.</summary>
    public List<ExclusionZone> Exclusions { get; set; } = new();

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
    public override double? PointZ => Z;
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
    public override double? PointZ => Z;
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

/// <summary>Área recortada de uma marca, com a marca que a originou (para limpeza automática).</summary>
public sealed class ExclusionZone
{
    public string? SourceId { get; set; }
    public List<Vec2> Points { get; set; } = new();
}

public enum TipoSuporte
{
    /// <summary>Coluna simples.</summary>
    Simples,
    /// <summary>Duas colunas (placas largas).</summary>
    Duplo,
    /// <summary>Sem suporte (fixada em poste/parede existente).</summary>
    Nenhum,
}

/// <summary>Placa de sinalização vertical com suporte.</summary>
public sealed class SignDefinition : MarkingDefinition
{
    public string Code { get; set; } = "R-1";
    public Vec2 Position { get; set; }
    /// <summary>Sentido do tráfego que lê a placa (a face fica voltada para o condutor que se aproxima).</summary>
    public Vec2 Direction { get; set; } = Vec2.UnitY;
    public double Z { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    /// <summary>Altura livre sob a placa (m). Vias urbanas: 2,10 m.</summary>
    public double MountHeight { get; set; } = 2.10;
    public TipoSuporte Support { get; set; } = TipoSuporte.Simples;
    public double PostDiameter { get; set; } = 0.063;
    /// <summary>Legenda substituta (vazio = a do catálogo; "-" = sem legenda).</summary>
    public string? Legend { get; set; }
    /// <summary>Deslocamento lateral da placa em relação ao suporte (m, + à direita do condutor).</summary>
    public double LateralOffset { get; set; }

    public override string KindName => "Placa";
    public override string DisplayCode => Code;
    public override double? PointZ => Z;
    public override void Translate(Vec2 delta, double dz) { Position += delta; Z += dz; }
}

/// <summary>Elemento urbanístico viário (banco, poste, árvore, abrigo...), por ponto ou distribuído ao longo de um caminho.</summary>
public sealed class UrbanElementDefinition : MarkingDefinition
{
    public string Code { get; set; } = "BANCO";
    public bool UsePath { get; set; }
    public PathReference PathRef { get; set; } = new();
    public Vec2 Position { get; set; }
    /// <summary>Direção para a qual o elemento está voltado.</summary>
    public Vec2 Direction { get; set; } = Vec2.UnitY;
    public double Z { get; set; }
    public double? Spacing { get; set; }
    public double Offset { get; set; }
    public double StartOffset { get; set; } = 2;
    public double EndSetback { get; set; } = 1;
    /// <summary>Rotação em relação ao caminho (graus): 90 = voltado para a esquerda do caminho.</summary>
    public double RotationDeg { get; set; } = 90;
    public double? Length { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public MarkingColor? Color { get; set; }

    public override string KindName => "Elemento urbano";
    public override string DisplayCode => Code;
    public override PathReference? Path => UsePath ? PathRef : null;
    public override double? PointZ => UsePath ? null : Z;
    public override void SetPath(PathReference path) { PathRef = path; UsePath = true; }
    public override void Translate(Vec2 delta, double dz) { Position += delta; Z += dz; }
}

public enum TipoRampa
{
    /// <summary>Rebaixamento de calçada com abas laterais (NBR 9050).</summary>
    RebaixamentoComAbas,
    /// <summary>Rebaixamento sem abas (laterais protegidas por canteiro/mobiliário).</summary>
    RebaixamentoSemAbas,
    /// <summary>Guia rebaixada / rampa de acesso de veículos.</summary>
    AcessoVeiculos,
}

/// <summary>
/// Rampa em calçada. O caminho tem 2 pontos: o 1º no meio-fio (face voltada para a pista) e o 2º
/// para dentro da calçada, indicando o sentido da subida.
/// </summary>
public sealed class RampDefinition : MarkingDefinition
{
    public TipoRampa Type { get; set; } = TipoRampa.RebaixamentoComAbas;
    public PathReference PathRef { get; set; } = new();
    public double Width { get; set; } = 1.50;
    public double Height { get; set; } = 0.15;
    /// <summary>Inclinação da rampa (8,33 % = 0,0833).</summary>
    public double Slope { get; set; } = 0.0833;
    /// <summary>Inclinação das abas laterais (10 %).</summary>
    public double FlareSlope { get; set; } = 0.10;
    public bool Tactile { get; set; } = true;
    public double TactileWidth { get; set; } = 0.40;
    public MarkingColor TactileColor { get; set; } = MarkingColor.Amarela;
    /// <summary>Recortar automaticamente calçadas/meios-fios sob a rampa.</summary>
    public bool CutSidewalk { get; set; } = true;

    public override string KindName => "Rampa";
    public override string DisplayCode => Type switch
    {
        TipoRampa.AcessoVeiculos => "RAMPA-VEIC",
        TipoRampa.RebaixamentoSemAbas => "RAMPA-PED",
        _ => "RAMPA-ABAS",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

public enum TipoModeracao
{
    /// <summary>Ondulação transversal tipo A (quebra-mola).</summary>
    OndulacaoA,
    /// <summary>Ondulação transversal tipo B.</summary>
    OndulacaoB,
    /// <summary>Faixa elevada para travessia de pedestres.</summary>
    FaixaElevada,
    /// <summary>Lombada invertida (valeta/depressão transversal).</summary>
    LombadaInvertida,
}

/// <summary>Dispositivo de moderação de tráfego atravessando a pista (caminho = bordo A → bordo B).</summary>
public sealed class TrafficCalmingDefinition : MarkingDefinition
{
    public TipoModeracao Type { get; set; } = TipoModeracao.OndulacaoA;
    public PathReference PathRef { get; set; } = new();
    /// <summary>Extensão no sentido do tráfego (m). Nulo = padrão do tipo.</summary>
    public double? Length { get; set; }
    /// <summary>Altura (ou profundidade da lombada invertida) em m.</summary>
    public double? Height { get; set; }
    /// <summary>Faixa elevada: comprimento de cada rampa (m).</summary>
    public double? RampLength { get; set; }
    public bool Marking { get; set; } = true;
    public MarkingColor? MarkingColor { get; set; }
    /// <summary>Faixa elevada: pintar faixa de pedestres zebrada no platô.</summary>
    public bool Crosswalk { get; set; } = true;

    public override string KindName => "Moderação de tráfego";
    public override string DisplayCode => Type switch
    {
        TipoModeracao.OndulacaoA => "OND-A",
        TipoModeracao.OndulacaoB => "OND-B",
        TipoModeracao.FaixaElevada => "FX-ELEV",
        _ => "LOMB-INV",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}
