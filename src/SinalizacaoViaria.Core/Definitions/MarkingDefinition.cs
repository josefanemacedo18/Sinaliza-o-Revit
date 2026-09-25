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
[JsonDerivedType(typeof(SignPlanDetailDefinition), "detalhe-placa")]
[JsonDerivedType(typeof(LabelDefinition), "anotacao")]
[JsonDerivedType(typeof(LegendDefinition), "quadro-legenda")]
[JsonDerivedType(typeof(CurbExtensionDefinition), "orelha")]
[JsonDerivedType(typeof(SidewalkAreaDefinition), "area-calcada")]
[JsonDerivedType(typeof(PlanterDefinition), "canteiro")]
[JsonDerivedType(typeof(CulDeSacDefinition), "cul-de-sac")]
[JsonDerivedType(typeof(SectionDimensionDefinition), "cota-secao")]
[JsonDerivedType(typeof(TypicalDetailDefinition), "detalhe-tipico")]
[JsonDerivedType(typeof(QuantityTableDefinition), "quadro-quantitativo")]
[JsonDerivedType(typeof(NotesDefinition), "notas")]
[JsonDerivedType(typeof(NorthArrowDefinition), "norte")]
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
    /// <summary>Rebaixamento total da calçada (calçadas estreitas): platô no nível da pista e rampas laterais ao longo do meio-fio.</summary>
    RebaixamentoTotal,
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
    /// <summary>Afastamento do piso tátil em relação à face do meio-fio (m).</summary>
    public double TactileSetback { get; set; }
    /// <summary>Profundidade da calçada rebaixada (m) – rebaixamento total.</summary>
    public double SidewalkDepth { get; set; } = 1.80;
    /// <summary>Piso tátil direcional no eixo da rampa, até o fim da subida.</summary>
    public bool DirectionalTactile { get; set; }
    /// <summary>Recortar automaticamente calçadas/meios-fios sob a rampa.</summary>
    public bool CutSidewalk { get; set; } = true;

    public override string KindName => "Rampa";
    public override string DisplayCode => Type switch
    {
        TipoRampa.AcessoVeiculos => "RAMPA-VEIC",
        TipoRampa.RebaixamentoSemAbas => "RAMPA-PED",
        TipoRampa.RebaixamentoTotal => "RAMPA-TOTAL",
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

public enum TipoTransicao
{
    /// <summary>Curvas reversas tangentes (raio configurável).</summary>
    Curva,
    /// <summary>Chanfro reto (comprimento de transição configurável).</summary>
    Chanfro,
}

/// <summary>
/// Orelha (avanço) de calçada sobre a faixa de estacionamento: desenhada ao longo da face do meio-fio,
/// com transições curvas ou em chanfro, meio-fio novo e canteiro/árvores opcionais.
/// </summary>
public sealed class CurbExtensionDefinition : MarkingDefinition
{
    public PathReference PathRef { get; set; } = new();
    /// <summary>Avanço sobre a pista (m) – normalmente a largura da faixa de estacionamento.</summary>
    public double Depth { get; set; } = 2.20;
    public TipoTransicao Transition { get; set; } = TipoTransicao.Curva;
    /// <summary>Raio das curvas de transição (ou comprimento do chanfro).</summary>
    public double Radius { get; set; } = 1.50;
    /// <summary>Verdadeiro quando a calçada existente fica à esquerda do sentido de desenho.</summary>
    public bool SidewalkOnLeft { get; set; } = true;
    public double Height { get; set; } = 0.15;
    public double CurbWidth { get; set; } = 0.15;
    public bool Planter { get; set; }
    public double PlanterWidth { get; set; } = 1.00;
    public double PlanterMargin { get; set; } = 0.50;
    public int Trees { get; set; }
    /// <summary>Recorta as marcas da pista (vagas, linhas) sob a orelha.</summary>
    public bool CutRoadMarkings { get; set; } = true;

    public override string KindName => "Orelha de calçada";
    public override string DisplayCode => "ORELHA";
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

public enum TipoAreaCalcada
{
    /// <summary>Calçada/avanço elevado em concreto.</summary>
    Calcada,
    /// <summary>Canteiro gramado elevado.</summary>
    Canteiro,
    /// <summary>Ciclovia no nível da calçada (pintura vermelha).</summary>
    Ciclovia,
    /// <summary>Área de estar / parklet em deck.</summary>
    Deck,
    /// <summary>Pavimento (asfalto) – ilhas, alargamentos.</summary>
    Pavimento,
    /// <summary>Faixa de serviço/ajardinada no nível da calçada.</summary>
    FaixaServico,
}

/// <summary>Área livre de calçada/canteiro definida por um contorno fechado (esquinas, avanços, ilhas).</summary>
public sealed class SidewalkAreaDefinition : MarkingDefinition
{
    public PathReference PathRef { get; set; } = new() { Closed = true };
    public TipoAreaCalcada Type { get; set; } = TipoAreaCalcada.Calcada;
    public double Height { get; set; } = 0.15;
    public bool Curb { get; set; } = true;
    public double CurbWidth { get; set; } = 0.15;
    /// <summary>Arredonda todos os cantos do contorno (m). 0 = cantos vivos.</summary>
    public double FilletRadius { get; set; }
    /// <summary>Recorta calçadas/gramados existentes sob a área.</summary>
    public bool CutExisting { get; set; }

    public override string KindName => "Área de calçada";
    public override string DisplayCode => Type switch
    {
        TipoAreaCalcada.Canteiro => "CANT-AREA",
        TipoAreaCalcada.Ciclovia => "CICLO-CALC",
        TipoAreaCalcada.Deck => "PARKLET",
        TipoAreaCalcada.Pavimento => "PAVIMENTO",
        TipoAreaCalcada.FaixaServico => "FX-SERVICO",
        _ => "AVANCO",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) { PathRef = path; PathRef.Closed = true; }
}

public enum TipoCanteiroCalcada
{
    /// <summary>Canteiro gramado no nível da calçada, com guia de contorno.</summary>
    Gramado,
    /// <summary>Jardineira elevada com mureta.</summary>
    Jardineira,
    /// <summary>Grelha de proteção de árvore (piso metálico).</summary>
    GrelhaArvore,
}

/// <summary>Canteiros, jardineiras e grelhas de árvore distribuídos ao longo de uma linha na calçada.</summary>
public sealed class PlanterDefinition : MarkingDefinition
{
    public PathReference PathRef { get; set; } = new();
    public TipoCanteiroCalcada Type { get; set; } = TipoCanteiroCalcada.Gramado;
    /// <summary>Comprimento de cada canteiro (m).</summary>
    public double Length { get; set; } = 2.00;
    public double Width { get; set; } = 1.00;
    /// <summary>Distância entre centros (m). 0 = faixa contínua.</summary>
    public double Spacing { get; set; } = 6.00;
    /// <summary>Deslocamento lateral do eixo dos canteiros (+ à esquerda).</summary>
    public double Offset { get; set; }
    /// <summary>Altura da superfície da calçada (topo do gramado/grelha).</summary>
    public double SurfaceHeight { get; set; } = 0.15;
    public double BorderWidth { get; set; } = 0.08;
    /// <summary>Altura da mureta da jardineira acima da calçada.</summary>
    public double BorderHeight { get; set; } = 0.40;
    public bool Trees { get; set; } = true;
    public bool CutSidewalk { get; set; } = true;

    public override string KindName => "Canteiro na calçada";
    public override string DisplayCode => Type switch
    {
        TipoCanteiroCalcada.Jardineira => "JARDINEIRA",
        TipoCanteiroCalcada.GrelhaArvore => "GRELHA-ARV",
        _ => "CANT-GRAMA",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

public enum TipoCulDeSac
{
    Circular,
    ExcentricoEsquerda,
    ExcentricoDireita,
    /// <summary>Em "T" (martelo) – manobra em três movimentos.</summary>
    Martelo,
    /// <summary>Em "Y".</summary>
    EmY,
    EmLEsquerda,
    EmLDireita,
    /// <summary>Gota (balão alongado).</summary>
    Gota,
}

/// <summary>Balão de retorno (cul-de-sac) no fim de uma via: pavimento, meio-fio, calçada, ilha e linha de bordo.</summary>
public sealed class CulDeSacDefinition : MarkingDefinition
{
    /// <summary>1º ponto: início do balão no eixo; 2º ponto: centro do balão / fim da via.</summary>
    public PathReference PathRef { get; set; } = new();
    public TipoCulDeSac Type { get; set; } = TipoCulDeSac.Circular;
    public double RoadWidth { get; set; } = 7.00;
    /// <summary>Raio do balão até a face do meio-fio (m).</summary>
    public double BulbRadius { get; set; } = 10.00;
    /// <summary>Raio de concordância entre o bordo da via e o balão.</summary>
    public double TransitionRadius { get; set; } = 6.00;
    /// <summary>Martelo: comprimento total da cabeça do "T" (m).</summary>
    public double HeadLength { get; set; } = 20.00;
    /// <summary>"Y" e "L": comprimento dos ramos (m).</summary>
    public double BranchLength { get; set; } = 10.00;
    public double BranchAngle { get; set; } = 45;
    public bool Island { get; set; }
    public double IslandRadius { get; set; } = 3.00;
    public double SidewalkWidth { get; set; } = 2.50;
    public double CurbWidth { get; set; } = 0.15;
    public double Height { get; set; } = 0.15;
    public bool Pavement { get; set; } = true;
    public double PavementThickness { get; set; } = 0.05;
    public bool EdgeLine { get; set; } = true;

    public override string KindName => "Cul-de-sac";
    public override string DisplayCode => Type switch
    {
        TipoCulDeSac.Martelo => "CDS-T",
        TipoCulDeSac.EmY => "CDS-Y",
        TipoCulDeSac.EmLEsquerda or TipoCulDeSac.EmLDireita => "CDS-L",
        TipoCulDeSac.ExcentricoEsquerda or TipoCulDeSac.ExcentricoDireita => "CDS-EXC",
        TipoCulDeSac.Gota => "CDS-GOTA",
        _ => "CDS-CIRC",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

/// <summary>Elementos de detalhamento 2D (existem apenas numa vista; não entram em quantitativos).</summary>
public interface IAnnotationDefinition
{
    /// <summary>Marca à qual o detalhe se refere (nulo = independente).</summary>
    string? TargetId { get; }
}

/// <summary>Detalhe que depende de todo o projeto (legenda, quadros, cotas) – regenerado quando as marcas mudam.</summary>
public interface IProjectWideAnnotation : IAnnotationDefinition { }

/// <summary>
/// Detalhe de placa em planta: a face da placa desenhada em escala de papel, afastada do suporte,
/// com linha de chamada até o poste e identificação (código e nome).
/// </summary>
public sealed class SignPlanDetailDefinition : MarkingDefinition, IAnnotationDefinition
{
    public string SignId { get; set; } = "";
    /// <summary>Deslocamento do centro do símbolo em relação ao suporte (mm de papel, X = leste, Y = norte).</summary>
    public Vec2 OffsetMm { get; set; } = new(0, 20);
    /// <summary>Largura do símbolo da placa no papel (mm).</summary>
    public double SymbolMm { get; set; } = 12;
    public double TextMm { get; set; } = 2.0;
    public bool Label { get; set; } = true;
    public bool ShowName { get; set; } = true;
    public bool Leader { get; set; } = true;
    /// <summary>Número da placa no projeto (ex.: P01) – aparece no símbolo e no quadro de placas.</summary>
    public string? Number { get; set; }

    public string? TargetId => SignId;
    public override string KindName => "Detalhe de placa";
    public override string DisplayCode => "DET-PLACA";
}

/// <summary>Anotação com linha de chamada para qualquer sinalização.</summary>
public sealed class LabelDefinition : MarkingDefinition, IAnnotationDefinition
{
    public string MarkingTargetId { get; set; } = "";
    /// <summary>Ponto sobre a marca (m).</summary>
    public Vec2 Anchor { get; set; }
    /// <summary>Posição do texto (m).</summary>
    public Vec2 LabelPosition { get; set; }
    public double TextMm { get; set; } = 2.0;
    /// <summary>Texto livre (substitui o automático).</summary>
    public string? CustomText { get; set; }
    public bool ShowName { get; set; } = true;
    public bool ShowDetails { get; set; } = true;

    public string? TargetId => MarkingTargetId;
    public override void Translate(Vec2 delta, double dz) { Anchor += delta; LabelPosition += delta; }
    public override string KindName => "Anotação";
    public override string DisplayCode => "ANOT";
}

/// <summary>Quadro de legenda com amostra e descrição de cada tipo de sinalização usada no projeto.</summary>
public sealed class LegendDefinition : MarkingDefinition, IProjectWideAnnotation
{
    /// <summary>Canto superior esquerdo (m).</summary>
    public Vec2 Position { get; set; }
    public string Title { get; set; } = "LEGENDA – SINALIZAÇÃO VIÁRIA";
    public double RowMm { get; set; } = 10;
    public double SampleMm { get; set; } = 24;
    public double TextColumnMm { get; set; } = 110;
    public double TextMm { get; set; } = 2.0;
    public bool Horizontal { get; set; } = true;
    public bool Vertical { get; set; } = true;
    public bool Physical { get; set; } = true;

    public string? TargetId => null;
    public override void Translate(Vec2 delta, double dz) => Position += delta;
    public override string KindName => "Quadro de legenda";
    public override string DisplayCode => "LEGENDA";
}

/// <summary>Cotagem automática da seção transversal: mede faixas, linhas, canteiros e calçadas cortados pela linha.</summary>
public sealed class SectionDimensionDefinition : MarkingDefinition, IProjectWideAnnotation
{
    public Vec2 Start { get; set; }
    public Vec2 End { get; set; }
    /// <summary>Distância da linha de cota ao alinhamento clicado (mm de papel, + à esquerda de início→fim).</summary>
    public double OffsetMm { get; set; } = 10;
    public double TextMm { get; set; } = 2.0;
    /// <summary>Linhas pintadas estreitas são cotadas pelo eixo (padrão de projeto: largura de faixa eixo a eixo).</summary>
    public bool LineAxes { get; set; } = true;
    /// <summary>Largura máxima (m) considerada "linha" para cotar pelo eixo.</summary>
    public double AxisMaxWidth { get; set; } = 0.35;
    public bool IncludeEnds { get; set; } = true;
    public bool Total { get; set; } = true;
    public bool Horizontal { get; set; } = true;
    public bool Physical { get; set; } = true;

    public string? TargetId => null;
    public override void Translate(Vec2 delta, double dz) { Start += delta; End += delta; }
    public override string KindName => "Cota de seção";
    public override string DisplayCode => "COTA-SEC";
}

/// <summary>Detalhe típico cotado (em escala ampliada) de uma marca do projeto: linha, zebrado, vaga ou placa em elevação.</summary>
public sealed class TypicalDetailDefinition : MarkingDefinition, IAnnotationDefinition
{
    public string MarkingTargetId { get; set; } = "";
    /// <summary>Canto superior esquerdo (m).</summary>
    public Vec2 Position { get; set; }
    /// <summary>Escala do detalhe (ex.: 20 para 1:20).</summary>
    public double DetailScale { get; set; } = 20;
    public double TextMm { get; set; } = 2.0;
    public string? Title { get; set; }

    public string? TargetId => MarkingTargetId;
    public override void Translate(Vec2 delta, double dz) => Position += delta;
    public override string KindName => "Detalhe típico";
    public override string DisplayCode => "DET-TIP";
}

/// <summary>Quadro de quantitativos (ou de placas) desenhado na prancha, separado por categoria.</summary>
public sealed class QuantityTableDefinition : MarkingDefinition, IProjectWideAnnotation
{
    public Vec2 Position { get; set; }
    public string Title { get; set; } = "QUADRO DE QUANTITATIVOS – SINALIZAÇÃO VIÁRIA";
    /// <summary>Categoria (nome do enum CategoriaQuantitativo) ou nulo para todas.</summary>
    public string? Category { get; set; }
    /// <summary>Quadro de placas: símbolo, numeração, código, descrição, dimensões e quantidade.</summary>
    public bool SignsOnly { get; set; }
    public double RowMm { get; set; } = 6;
    public double TextMm { get; set; } = 2.0;

    public string? TargetId => null;
    public override void Translate(Vec2 delta, double dz) => Position += delta;
    public override string KindName => SignsOnly ? "Quadro de placas" : "Quadro de quantitativos";
    public override string DisplayCode => SignsOnly ? "QUADRO-PLACAS" : "QUADRO-QTD";
}

/// <summary>Bloco de notas gerais do projeto de sinalização.</summary>
public sealed class NotesDefinition : MarkingDefinition, IAnnotationDefinition
{
    public const string DefaultText =
        "Cotas em metros, salvo indicação em contrário.\n" +
        "A sinalização horizontal e vertical segue o Manual Brasileiro de Sinalização de Trânsito (CONTRAN) e as resoluções vigentes.\n" +
        "Pintura: tinta/termoplástico conforme especificação do projeto, com microesferas de vidro (tipos I-B e II-A) conforme ABNT NBR 16184.\n" +
        "Placas: chapa, película retrorrefletiva e suportes conforme ABNT NBR 11904, NBR 14644 e especificação do órgão com circunscrição sobre a via.\n" +
        "Altura livre mínima sob as placas de 2,10 m em calçadas; afastamento lateral mínimo de 0,30 m do meio-fio.\n" +
        "Rebaixamentos de calçada e piso tátil conforme ABNT NBR 9050 e NBR 16537.\n" +
        "Conferir as interferências (redes, acessos, arborização) em campo antes da execução.";

    public Vec2 Position { get; set; }
    public string Title { get; set; } = "NOTAS GERAIS";
    public string Text { get; set; } = DefaultText;
    public double WidthMm { get; set; } = 130;
    public double TextMm { get; set; } = 2.0;
    public bool Numbered { get; set; } = true;

    public string? TargetId => null;
    public override void Translate(Vec2 delta, double dz) => Position += delta;
    public override string KindName => "Notas gerais";
    public override string DisplayCode => "NOTAS";
}

/// <summary>Indicação de norte.</summary>
public sealed class NorthArrowDefinition : MarkingDefinition, IAnnotationDefinition
{
    public Vec2 Position { get; set; }
    public double SizeMm { get; set; } = 16;
    /// <summary>Ângulo do norte em relação ao eixo Y do projeto (graus, anti-horário).</summary>
    public double AngleDeg { get; set; }

    public string? TargetId => null;
    public override void Translate(Vec2 delta, double dz) => Position += delta;
    public override string KindName => "Norte";
    public override string DisplayCode => "NORTE";
}
