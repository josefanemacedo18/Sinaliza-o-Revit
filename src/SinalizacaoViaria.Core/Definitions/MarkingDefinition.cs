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

    /// <summary>
    /// Greide da via (perfil longitudinal) ao longo do caminho: pisos, pinturas e dispositivos acompanham as cotas. Todas as
    /// marcas de uma via compartilham o mesmo greide.
    /// </summary>
    public RoadGrade? Grade { get; set; }

    public OutputSettings Clone() => new()
    {
        Mode = Mode, Thickness = Thickness, ElevationOffset = ElevationOffset, Drape = Drape,
        SurfaceIds = new List<string>(SurfaceIds), ViewId = ViewId, Material = Material, Grade = Grade?.Clone(),
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

    /// <summary>
    /// Traçado (m) de cada referência de <see cref="ElementIds"/> no momento da criação – usado se a referência deixar de
    /// existir (ex.: aresta de um piso que foi regenerado), para a marca não sumir.
    /// </summary>
    public List<List<Vec2>>? Cache { get; set; }

    /// <summary>
    /// Raio (m) com que os cantos vivos do eixo são arredondados antes de gerar a marca (vias: bordas, linhas e calçadas
    /// acompanham a curva). Nulo/0 = cantos como desenhados.
    /// </summary>
    public double? SmoothRadius { get; set; }

    /// <summary>Pontos do caminho já com os cantos arredondados (<see cref="SmoothRadius"/>).</summary>
    public static IReadOnlyList<Vec2> Smooth(IReadOnlyList<Vec2> pts, double? radius, bool closed) =>
        radius is > 0.01 ? CurveTools.FilletCorners(pts, radius.Value, closed) : pts;

    [JsonIgnore]
    public bool IsAssociative => ElementIds.Count > 0;

    /// <summary>Prefixo das referências a arestas de elementos (bordas de pisos, lajes, topografia...).</summary>
    public const string EdgePrefix = "edge|";

    public static bool IsEdge(string id) => id.StartsWith(EdgePrefix, StringComparison.Ordinal);

    /// <summary>UniqueId do elemento dono da referência (linha ou aresta).</summary>
    public static string OwnerOf(string id)
    {
        if (!IsEdge(id)) return id;
        var stable = id.Substring(EdgePrefix.Length);
        var k = stable.IndexOf(':');
        return k < 0 ? stable : stable.Substring(0, k);
    }

    public PathReference Clone() => new()
    {
        ElementIds = new List<string>(ElementIds),
        Points = new List<Vec2>(Points),
        Z = Z,
        Closed = Closed,
        Cache = Cache?.Select(c => new List<Vec2>(c)).ToList(),
        SmoothRadius = SmoothRadius,
    };

    public static PathReference FromPoints(IEnumerable<Vec2> pts, double z, bool closed = false) =>
        new() { Points = pts.ToList(), Z = z, Closed = closed };

    public static PathReference FromElements(IEnumerable<string> ids) => new() { ElementIds = ids.ToList() };
}

/// <summary>Posição do elemento em relação à linha de referência.</summary>
public enum Justificacao
{
    /// <summary>Centralizado na linha.</summary>
    Centro,
    /// <summary>Borda direita sobre a linha: o elemento fica à esquerda (sentido da linha).</summary>
    Esquerda,
    /// <summary>Borda esquerda sobre a linha: o elemento fica à direita (sentido da linha).</summary>
    Direita,
    /// <summary>Lado indicado com um clique na criação (convertido em Esquerda/Direita).</summary>
    Clique,
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
[JsonDerivedType(typeof(RoadPavementDefinition), "pavimento-via")]
[JsonDerivedType(typeof(IntersectionDefinition), "intersecao")]
[JsonDerivedType(typeof(RoundaboutDefinition), "rotatoria")]
[JsonDerivedType(typeof(TactileRouteDefinition), "rota-tatil")]
[JsonDerivedType(typeof(ChannelizationDefinition), "canalizacao")]
[JsonDerivedType(typeof(RailwayDefinition), "ferrovia")]
[JsonDerivedType(typeof(SectionProfileDefinition), "perfil-secao")]
[JsonDerivedType(typeof(DrainageDefinition), "drenagem")]
[JsonDerivedType(typeof(BridgeDefinition), "obra-de-arte")]
[JsonDerivedType(typeof(TunnelDefinition), "tunel")]
[JsonDerivedType(typeof(TrenchDefinition), "trincheira")]
[JsonDerivedType(typeof(RetainingWallDefinition), "muro-arrimo")]
[JsonDerivedType(typeof(SlopeDefinition), "talude")]
[JsonDerivedType(typeof(InterchangeDefinition), "no-viario")]
public abstract class MarkingDefinition
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Identificador do conjunto (todos os elementos Revit da mesma marca compartilham este id).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Agrupamento opcional (ex.: todas as linhas geradas por "Sinalizar via").</summary>
    public string? GroupId { get; set; }

    public OutputSettings Output { get; set; } = new();

    /// <summary>Hierarquia viária (CTB art. 60) da via a que a marca pertence – entra nos quantitativos.</summary>
    public HierarquiaViaria? Hierarchy { get; set; }

    /// <summary>Observações do projetista.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Posição do elemento em relação à linha de referência: centralizado ou com a borda sobre a linha (elemento à
    /// esquerda ou à direita, no sentido da linha). Vale para marcas ao longo de caminho (linhas, meios-fios,
    /// calçadas, sarjetas, dispositivos, canteiros).
    /// </summary>
    public Justificacao Justify { get; set; }

    /// <summary>
    /// Sobrepor: a marca fica "por cima" – as linhas longitudinais pintadas sob ela (eixo, bordo, divisão de faixas) são
    /// interrompidas no trecho ocupado. Padrão nas faixas de pedestres e zebrados.
    /// </summary>
    public bool Overlay { get; set; }

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
    /// <summary>LRV: velocidade inicial (km/h) – as linhas são espaçadas pelo método do MBST Vol. IV (a = 1,47 m/s², 1 s). 0/nulo = padrão da variante.</summary>
    public double? LrvFromKmh { get; set; }
    /// <summary>LRV: velocidade final desejada (km/h).</summary>
    public double? LrvToKmh { get; set; }
    /// <summary>Sarjetão: flecha (profundidade da depressão no centro, m). Nulo = 0,05 m.</summary>
    public double? Depth { get; set; }
    /// <summary>
    /// Nível do TOPO do elemento em relação ao topo do pavimento da pista (m): 0,15 = calçada usual, 0 = no nível da pista,
    /// negativo = rebaixado (jardim de chuva, canteiro rebaixado). Nulo = espessura do catálogo apoiada na pista.
    /// </summary>
    public double? Height { get; set; }

    public override string KindName => "Linear";
    public override string DisplayCode => Code;
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

/// <summary>Lado de uma faixa (zebrado em faixa) em relação ao caminho de referência.</summary>
public enum LadoFaixa
{
    Centro,
    /// <summary>À esquerda do sentido do caminho.</summary>
    Esquerda,
    /// <summary>À direita do sentido do caminho.</summary>
    Direita,
    /// <summary>Contorno fechado: para dentro (anel interno à borda).</summary>
    Interno,
    /// <summary>Contorno fechado: para fora (anel externo à borda).</summary>
    Externo,
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
    /// <summary>Posição da faixa em relação ao caminho: centrada, para um dos lados ou (contorno fechado) para dentro/para fora.</summary>
    public LadoFaixa StripSide { get; set; } = LadoFaixa.Centro;

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
    /// <summary>Escala do símbolo (1 = tamanho informado).</summary>
    public double Scale { get; set; } = 1.0;

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
    /// <summary>Escala da legenda (altura, espaçamentos) – 1 = tamanho informado.</summary>
    public double Scale { get; set; } = 1.0;

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
    /// <summary>Escala dos símbolos/legendas repetidos.</summary>
    public double Scale { get; set; } = 1.0;

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
    /// <summary>Trecho apagado à mão (ferramenta Apagar Trecho) – não é limpo automaticamente.</summary>
    public bool Manual { get; set; }
    /// <summary>Desativado: o recorte fica guardado, mas a marca volta a aparecer inteira (liga/desliga).</summary>
    public bool Enabled { get; set; } = true;
}

public enum TipoSuporte
{
    /// <summary>Coluna simples.</summary>
    Simples,
    /// <summary>Duas colunas (placas largas).</summary>
    Duplo,
    /// <summary>Sem suporte (fixada em poste/parede existente).</summary>
    Nenhum,
    /// <summary>Braço projetado: coluna na lateral e braço horizontal sobre a pista (placa suspensa).</summary>
    BracoProjetado,
    /// <summary>Semipórtico: coluna robusta na lateral e viga treliçada em balanço sobre a pista.</summary>
    SemiPortico,
    /// <summary>Pórtico: duas colunas e viga atravessando toda a pista.</summary>
    Portico,
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
    /// <summary>Pórtico/semipórtico/braço: vão da viga ou comprimento do braço (m), a partir da coluna na direção da placa.</summary>
    public double StructureSpan { get; set; } = 12.0;
    /// <summary>Pórtico/semipórtico/braço: diâmetro (ou lado) das colunas (m).</summary>
    public double StructureColumn { get; set; } = 0.30;
    /// <summary>Pórtico/semipórtico/braço: altura da viga (m).</summary>
    public double StructureBeam { get; set; } = 0.60;
    /// <summary>Suporte aéreo (pórtico, semipórtico ou braço projetado).</summary>
    public bool Overhead => Support is TipoSuporte.BracoProjetado or TipoSuporte.SemiPortico or TipoSuporte.Portico;
    /// <summary>Legenda substituta (vazio = a do catálogo; "-" = sem legenda).</summary>
    public string? Legend { get; set; }
    /// <summary>Deslocamento lateral da placa em relação ao suporte (m, + à direita do condutor).</summary>
    public double LateralOffset { get; set; }
    /// <summary>Nível do terreno onde o suporte é fincado, acima do ponto clicado (0,15 m = topo da calçada).</summary>
    public double BaseElevation { get; set; } = 0.15;

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
    /// <summary>Nível de assentamento acima do ponto/caminho (0,15 m = topo da calçada).</summary>
    public double BaseElevation { get; set; } = 0.15;

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
    /// <summary>Comprimento da rampa (m) – se informado, define a inclinação (desnível ÷ comprimento).</summary>
    public double? Length { get; set; }
    /// <summary>Comprimento das abas ao longo do meio-fio (m) – se informado, define a inclinação das abas.</summary>
    public double? FlareLength { get; set; }
    /// <summary>Desnível residual entre a pista e o início da rampa (m; NBR 9050: até 5 mm sem tratamento).</summary>
    public double LipHeight { get; set; }
    /// <summary>Comprimento do piso tátil de alerta ao longo do meio-fio (m). Nulo = largura toda da rampa.</summary>
    public double? TactileLength { get; set; }
    /// <summary>Largura da faixa de piso tátil direcional (m).</summary>
    public double DirectionalWidth { get; set; } = 0.25;
    /// <summary>Patamar plano no topo da rampa (m) – faixa nivelada antes da calçada.</summary>
    public double LandingDepth { get; set; }
    /// <summary>Material da rampa (concreto, bloquete...).</summary>
    public MarkingColor RampColor { get; set; } = MarkingColor.Concreto;

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
    /// <summary>Almofada (speed cushion): elevação estreita por faixa, transponível por ônibus e veículos de emergência.</summary>
    Almofada,
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
    /// <summary>Almofada: largura de cada almofada (m) – 1,60 a 1,90 m (CET – Medidas Moderadoras).</summary>
    public double CushionWidth { get; set; } = 1.70;
    /// <summary>Almofada: quantidade na largura da pista (0 = uma por faixa de ~3,5 m).</summary>
    public int CushionCount { get; set; }

    public override string KindName => "Moderação de tráfego";
    public override string DisplayCode => Type switch
    {
        TipoModeracao.OndulacaoA => "OND-A",
        TipoModeracao.OndulacaoB => "OND-B",
        TipoModeracao.FaixaElevada => "FX-ELEV",
        TipoModeracao.Almofada => "ALMOFADA",
        _ => "LOMB-INV",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

/// <summary>Marcas de canalização em transição (MBST Vol. IV 6.2; DER/SP B.3).</summary>
public enum TipoCanalizacao
{
    /// <summary>MTL – alteração de largura de pista (estreitamento/alargamento): triângulo de transição.</summary>
    TransicaoLargura,
    /// <summary>MAO – aproximação de ilha ou obstáculo fixo na pista: transição de entrada, obstáculo e transição de saída.</summary>
    Obstaculo,
    /// <summary>MAP – início/fim/estreitamento de acostamento pavimentado: transição + trecho tangente.</summary>
    Acostamento,
}

/// <summary>
/// Área neutra de canalização (zebrado com linha de canalização) ao longo de uma linha de referência – o bordo da faixa
/// antes da transição, no sentido do tráfego. Comprimentos pela velocidade (l = 0,5·V·d), tudo editável.
/// </summary>
public sealed class ChannelizationDefinition : MarkingDefinition
{
    public TipoCanalizacao Type { get; set; } = TipoCanalizacao.TransicaoLargura;
    public PathReference PathRef { get; set; } = new();
    public double Speed { get; set; } = 60;
    /// <summary>Variação de largura d (m): quanto o bordo se desloca; positivo = para a esquerda do sentido do caminho.</summary>
    public double WidthChange { get; set; } = 3.5;
    /// <summary>Comprimento da transição (m). 0 = calculado: l = 0,5·V·d (mínimos: 30 m urbana / 60 m rodovia junto a obstáculos).</summary>
    public double Length { get; set; }
    /// <summary>Estação (m) do início da transição ao longo do caminho.</summary>
    public double StartStation { get; set; }
    /// <summary>Obstáculo: extensão ao longo da via (m).</summary>
    public double ObstacleLength { get; set; } = 6;
    /// <summary>Obstáculo: afastamento lateral a, da linha de canalização ao obstáculo (0,30 a 0,60 m).</summary>
    public double Clearance { get; set; } = 0.45;
    /// <summary>Obstáculo: transição de saída (m). 0 = igual à de entrada.</summary>
    public double ExitLength { get; set; }
    /// <summary>Obstáculo no eixo de via de mão dupla: área neutra dos dois lados da linha (amarela).</summary>
    public bool BothSides { get; set; }
    /// <summary>Acostamento: trecho tangente L (m). 0 = transição do acostamento ta pela velocidade (30/40/50 m).</summary>
    public double TangentLength { get; set; }
    public bool Rural { get; set; }
    public double BarWidth { get; set; } = 0.50;
    /// <summary>Espaçamento das barras (m). 0 = pela velocidade: 1,50 m (V &lt; 80) ou 2,50 m.</summary>
    public double Gap { get; set; }
    public double LineWidth { get; set; } = 0.20;
    /// <summary>Branca entre fluxos de mesmo sentido; amarela entre fluxos opostos.</summary>
    public MarkingColor Color { get; set; } = MarkingColor.Branca;

    public override string KindName => "Canalização";
    public override string DisplayCode => Type switch
    {
        TipoCanalizacao.Obstaculo => "MAO",
        TipoCanalizacao.Acostamento => "MAP",
        _ => "MTL",
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
    /// <summary>Ponta reta, perpendicular ao meio-fio (acompanha a calçada / a travessia).</summary>
    Reta,
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
    /// <summary>Raio das curvas de transição (ou comprimento do chanfro) – ponta inicial.</summary>
    public double Radius { get; set; } = 1.50;
    /// <summary>Transição da ponta final (nulo = igual à inicial).</summary>
    public TipoTransicao? EndTransition { get; set; }
    public double? EndRadius { get; set; }
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
    /// <summary>Raio mínimo do meio-fio da orelha nas esquinas (0 = acompanha a esquina com raio igual ao avanço).</summary>
    public double CornerRadius { get; set; }

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
    /// <summary>Perfil de borda (meio-fio, sarjeta, grama, calçada) copiado das vias ligadas – aplicado em volta da pista.</summary>
    public List<Automation.EdgeBand> EdgeProfile { get; set; } = new();

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
    /// <summary>Calçada com a composição da via (faixa de serviço e sarjeta lidas da via ligada).</summary>
    public bool MatchRoadSection { get; set; } = true;
    public double ServiceStripWidth { get; set; }
    public bool ServiceStripGrass { get; set; } = true;
    public double GutterWidth { get; set; }
    public double CurbWidth { get; set; } = 0.15;
    public double Height { get; set; } = 0.15;
    public bool Pavement { get; set; } = true;
    public double PavementThickness { get; set; } = 0.05;
    public bool EdgeLine { get; set; } = true;
    /// <summary>Pavimento da via a cuja ponta o balão está ligado (segue o eixo e recorta a via). Nulo = avulso.</summary>
    public string? RoadId { get; set; }
    /// <summary>Ligado ao fim (true) ou ao início (false) do eixo da via.</summary>
    public bool AtRoadEnd { get; set; } = true;

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
/// Detalhe posicionado livremente na vista: se o usuário mover os elementos (ou o grupo), a nova posição é lida na
/// próxima regeneração – o detalhe não "volta" para o lugar antigo.
/// </summary>
public interface IPlacedAnnotation
{
    /// <summary>Canto inferior esquerdo das linhas desenhadas na última geração (m).</summary>
    Vec2? DrawnAnchor { get; set; }
}

/// <summary>Detalhe cujos elementos formam um grupo de detalhes do Revit (seleciona e move tudo junto).</summary>
public interface IGroupedAnnotation
{
    string GroupName { get; }
}

/// <summary>
/// Perfil transversal (corte) desenhado a partir de uma cota de seção: fica onde o usuário clicar, é um grupo de detalhes
/// e acompanha as mudanças da via.
/// </summary>
public sealed class SectionProfileDefinition : MarkingDefinition, IProjectWideAnnotation, IPlacedAnnotation, IGroupedAnnotation
{
    /// <summary>Cota de seção de origem (linha de corte).</summary>
    public string SectionId { get; set; } = "";
    /// <summary>Canto superior esquerdo do perfil (m).</summary>
    public Vec2 Position { get; set; }
    public double ProfileScale { get; set; } = 50;
    public double VerticalExaggeration { get; set; } = 1;
    public double CrossSlopePct { get; set; } = 2;
    public bool ProfileLevels { get; set; } = true;
    public bool ProfileHeights { get; set; } = true;
    public bool ProfileLegend { get; set; } = true;
    public double TextMm { get; set; } = 2.0;
    public int Decimals { get; set; } = 2;
    public TerminalCota Terminal { get; set; } = TerminalCota.Traco;
    /// <summary>Moldura em volta do perfil.</summary>
    public bool FrameBox { get; set; }
    public Vec2? DrawnAnchor { get; set; }
    /// <summary>Letra do corte na última geração (nome do grupo).</summary>
    public string Letter { get; set; } = "A";

    public string? TargetId => SectionId;
    public string GroupName => $"SV - Perfil transversal {Letter}-{Letter}";
    public override void Translate(Vec2 delta, double dz) => Position += delta;
    public override string KindName => "Perfil transversal";
    public override string DisplayCode => "PERFIL-SEC";

    /// <summary>Copia as opções do perfil guardadas na cota (modelo da ferramenta).</summary>
    public static SectionProfileDefinition From(SectionDimensionDefinition sd, Vec2 position) => new()
    {
        SectionId = sd.Id, Position = position, ProfileScale = sd.ProfileScale, VerticalExaggeration = sd.VerticalExaggeration,
        CrossSlopePct = sd.CrossSlopePct, ProfileLevels = sd.ProfileLevels, ProfileHeights = sd.ProfileHeights, ProfileLegend = sd.ProfileLegend,
        TextMm = sd.TextMm, Decimals = sd.Decimals, Terminal = sd.Terminal, Letter = string.IsNullOrWhiteSpace(sd.SectionLetter) ? "A" : sd.SectionLetter,
        Output = sd.Output.Clone(),
    };
}

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

    /// <summary>Traçado da linha de chamada: reta, com cotovelo horizontal ou livre (vértices clicados).</summary>
    public EstiloChamada LeaderStyle { get; set; } = EstiloChamada.Reta;
    /// <summary>Terminal da chamada junto ao suporte.</summary>
    public TerminalChamada Terminal { get; set; } = TerminalChamada.Ponto;
    /// <summary>Vértices da chamada livre, em mm de papel relativos ao suporte (acompanham a placa).</summary>
    public List<Vec2> ElbowsMm { get; set; } = new();
    /// <summary>Ponta da chamada em relação ao suporte (mm de papel) – permite apontar para outro ponto.</summary>
    public Vec2 AnchorMm { get; set; }
    /// <summary>Número dentro de um balão ao lado do símbolo (além do texto).</summary>
    public bool NumberBubble { get; set; }

    public string? TargetId => SignId;
    public override string KindName => "Detalhe de placa";
    public override string DisplayCode => "DET-PLACA";
}

public enum EstiloChamada { Reta, Cotovelo, Livre }

/// <summary>Terminal das linhas de cota.</summary>
public enum TerminalCota { Traco, Seta, Ponto }

public enum TerminalChamada { Ponto, Seta, Nenhum }

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
public sealed class LegendDefinition : MarkingDefinition, IProjectWideAnnotation, IPlacedAnnotation
{
    /// <summary>Canto superior esquerdo (m).</summary>
    public Vec2 Position { get; set; }
    public string Title { get; set; } = "LEGENDA – SINALIZAÇÃO VERTICAL";
    public double RowMm { get; set; } = 10;
    public double SampleMm { get; set; } = 24;
    public double TextColumnMm { get; set; } = 110;
    public double TextMm { get; set; } = 2.0;
    public bool Horizontal { get; set; } = true;
    public bool Vertical { get; set; } = true;
    public bool Physical { get; set; } = true;

    public string? TargetId => null;
    public Vec2? DrawnAnchor { get; set; }
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
    /// <summary>Nome de cada trecho (calçada, faixa de rolamento, ciclofaixa, canteiro...) junto à cota.</summary>
    public bool Labels { get; set; } = true;
    /// <summary>Letra do corte (A → "SEÇÃO A–A" com marcas nas pontas). Vazio = sem marcas nem título.</summary>
    public string SectionLetter { get; set; } = "A";
    /// <summary>Marca o eixo da via (linha traço-ponto) onde a seção cruza o eixo.</summary>
    public bool AxisMarker { get; set; } = true;
    /// <summary>Divide a cadeia de cotas no eixo da via (meias-larguras).</summary>
    public bool SplitAtAxis { get; set; }
    public TerminalCota Terminal { get; set; } = TerminalCota.Traco;
    public int Decimals { get; set; } = 2;

    /// <summary>Cadeia de cotas em planta, ao longo da linha de corte.</summary>
    public bool PlanChain { get; set; } = true;
    /// <summary>Desenha o perfil transversal (corte) com as camadas, níveis, cotas horizontais e verticais e caimentos.</summary>
    public bool Profile { get; set; } = true;
    /// <summary>Canto superior esquerdo do perfil (m). Nulo = sem perfil.</summary>
    public Vec2? ProfilePosition { get; set; }
    /// <summary>Escala do perfil (1:n) – ampliado em relação à vista.</summary>
    public double ProfileScale { get; set; } = 50;
    /// <summary>Exagero vertical (1 = verdadeira grandeza; 2 a 5 realça meios-fios e desníveis).</summary>
    public double VerticalExaggeration { get; set; } = 1;
    /// <summary>Caimento transversal indicado na pista (%). 0 = não indicar.</summary>
    public double CrossSlopePct { get; set; } = 2;
    /// <summary>Níveis (+0,15 / ±0,00) de cada trecho no perfil.</summary>
    public bool ProfileLevels { get; set; } = true;
    /// <summary>Cotas verticais dos desníveis (meios-fios, plataformas, canteiros).</summary>
    public bool ProfileHeights { get; set; } = true;
    /// <summary>Tabela de materiais (legenda das camadas) ao lado do perfil.</summary>
    public bool ProfileLegend { get; set; } = true;
    /// <summary>Criação: um clique sobre a via gera o corte perpendicular ao eixo, de alinhamento a alinhamento.</summary>
    public bool PerpendicularToRoad { get; set; } = true;
    /// <summary>Folga além das calçadas no corte perpendicular (m).</summary>
    public double RoadMargin { get; set; } = 0.5;

    public string? TargetId => null;
    public override void Translate(Vec2 delta, double dz)
    {
        Start += delta;
        End += delta;
        if (ProfilePosition is { } pp) ProfilePosition = pp + delta;
    }
    public override string KindName => "Cota de seção";
    public override string DisplayCode => "COTA-SEC";
}

/// <summary>Detalhe típico cotado (em escala ampliada) de uma marca do projeto: linha, zebrado, vaga ou placa em elevação.</summary>
public sealed class TypicalDetailDefinition : MarkingDefinition, IAnnotationDefinition, IPlacedAnnotation
{
    public string MarkingTargetId { get; set; } = "";
    /// <summary>Canto superior esquerdo (m).</summary>
    public Vec2 Position { get; set; }
    /// <summary>Escala do detalhe (ex.: 20 para 1:20).</summary>
    public double DetailScale { get; set; } = 20;
    public double TextMm { get; set; } = 2.0;
    public string? Title { get; set; }
    public TerminalCota Terminal { get; set; } = TerminalCota.Traco;
    /// <summary>Moldura em volta do detalhe.</summary>
    public bool FrameBox { get; set; } = true;

    public string? TargetId => MarkingTargetId;
    public Vec2? DrawnAnchor { get; set; }
    public override void Translate(Vec2 delta, double dz) => Position += delta;
    public override string KindName => "Detalhe típico";
    public override string DisplayCode => "DET-TIP";
}

/// <summary>Quadro de quantitativos (ou de placas) desenhado na prancha, separado por categoria.</summary>
public sealed class QuantityTableDefinition : MarkingDefinition, IProjectWideAnnotation, IPlacedAnnotation
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
    public Vec2? DrawnAnchor { get; set; }
    public override void Translate(Vec2 delta, double dz) => Position += delta;
    public override string KindName => SignsOnly ? "Quadro de placas" : "Quadro de quantitativos";
    public override string DisplayCode => SignsOnly ? "QUADRO-PLACAS" : "QUADRO-QTD";
}

/// <summary>Bloco de notas gerais do projeto de sinalização.</summary>
public sealed class NotesDefinition : MarkingDefinition, IAnnotationDefinition, IPlacedAnnotation
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
    public Vec2? DrawnAnchor { get; set; }
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
    public EstiloNorte Style { get; set; } = EstiloNorte.Classico;

    public string? TargetId => null;
    public override void Translate(Vec2 delta, double dz) => Position += delta;
    public override string KindName => "Norte";
    public override string DisplayCode => "NORTE";
}

public enum EstiloNorte { Classico, RosaDosVentos, Seta }

public enum TipoPavimento
{
    Nenhum,
    Asfalto,
    /// <summary>Pavimento intertravado (bloquete / paver).</summary>
    Bloquete,
    Concreto,
}

/// <summary>Faixa (offset do eixo e largura) sem pavimento – canteiros elevados e sarjetas.</summary>
public sealed record PavementGap(double Offset, double Width, bool Median = true);

/// <summary>
/// Pavimento da pista gerado pelo "Sinalizar via". Também registra a seção transversal (larguras da pista e das
/// calçadas) usada para montar interseções e rotatórias com as outras vias.
/// </summary>
public sealed class RoadPavementDefinition : MarkingDefinition
{
    public PathReference PathRef { get; set; } = new();
    public TipoPavimento Material { get; set; } = TipoPavimento.Asfalto;
    /// <summary>Espessura (m). Nulo = padrão do material.</summary>
    public double? Thickness { get; set; }
    /// <summary>Distância do eixo ao bordo da pista do lado direito (face do meio-fio).</summary>
    public double RightWidth { get; set; } = 3.5;
    public double LeftWidth { get; set; } = 3.5;
    /// <summary>Largura da calçada (com meio-fio) em cada lado; 0 = sem calçada.</summary>
    public double RightSidewalk { get; set; }
    public double LeftSidewalk { get; set; }
    public double CurbWidth { get; set; } = 0.15;
    public double CurbHeight { get; set; } = 0.15;
    /// <summary>Mão dupla (falso = todas as faixas no sentido do eixo).</summary>
    public bool TwoWay { get; set; } = true;
    /// <summary>Faixas sem pavimento (canteiros físicos, sarjetas), offset + à esquerda.</summary>
    public List<PavementGap> Gaps { get; set; } = new();
    public double StartSetback { get; set; }
    public double EndSetback { get; set; }
    /// <summary>Raio das esquinas (face do meio-fio) nas conexões automáticas desta via. Nulo = pela hierarquia.</summary>
    public double? CornerRadius { get; set; }
    /// <summary>Seção transversal completa (RoadSetup em JSON) usada para gerar a via – permite editar a via inteira depois.</summary>
    public string? SetupJson { get; set; }
    /// <summary>
    /// Pontas que convergem em faixa paralela com outra via (ramos de nós viários): não geram interseção nem cul-de-sac.
    /// </summary>
    public bool MergeStart { get; set; }
    public bool MergeEnd { get; set; }

    public double DefaultThickness => Material switch { TipoPavimento.Bloquete => 0.08, TipoPavimento.Concreto => 0.15, _ => 0.05 };
    public double ActualThickness => Thickness is > 0 ? Thickness.Value : DefaultThickness;
    public double TotalRight => RightWidth + RightSidewalk;
    public double TotalLeft => LeftWidth + LeftSidewalk;

    public MarkingColor Color => Material switch
    {
        TipoPavimento.Bloquete => MarkingColor.Bloquete,
        TipoPavimento.Concreto => MarkingColor.PavimentoConcreto,
        _ => MarkingColor.Asfalto,
    };

    public override string KindName => "Pavimento da via";
    public override string DisplayCode => Material switch
    {
        TipoPavimento.Bloquete => "PAV-BLQ",
        TipoPavimento.Concreto => "PAV-CON",
        _ => "PAV-ASF",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

/// <summary>Interseção entre vias do "Sinalizar via": esquinas arredondadas, meio-fio, recortes e travessias.</summary>
public sealed class IntersectionDefinition : MarkingDefinition
{
    /// <summary>Perfil de borda (meio-fio, sarjeta, grama, calçada) copiado das vias ligadas – aplicado em volta da pista.</summary>
    public List<Automation.EdgeBand> EdgeProfile { get; set; } = new();

    /// <summary>Ponto de cruzamento dos eixos (m).</summary>
    public Vec2 Node { get; set; }
    public double Z { get; set; }
    /// <summary>Ids das definições de pavimento das vias que se cruzam.</summary>
    public List<string> RoadIds { get; set; } = new();
    /// <summary>Raio das esquinas na face do meio-fio (m).</summary>
    public double CornerRadius { get; set; } = 6.0;
    public bool Crosswalks { get; set; } = true;
    public double CrosswalkWidth { get; set; } = 4.0;
    /// <summary>Recuo da faixa de pedestres em relação ao fim da curva da esquina (m).</summary>
    public double CrosswalkSetback { get; set; } = 1.0;
    public bool StopLines { get; set; } = true;
    public bool Ramps { get; set; } = true;
    /// <summary>Esquinas com a mesma composição da calçada das vias (meio-fio + faixa de serviço gramada + passeio).</summary>
    public bool MatchRoadSection { get; set; } = true;

    /// <summary>Via preferencial (pavimento). Nulo = automática (a que atravessa o nó, mais larga e mais longa).</summary>
    public string? MainRoadId { get; set; }
    /// <summary>Controle do direito de passagem: define retenções, legendas e placas das aproximações.</summary>
    public ControleIntersecao Control { get; set; } = ControleIntersecao.Pare;
    /// <summary>Placas R-1/R-2 e legenda "PARE"/símbolo "Dê a preferência" nas aproximações secundárias.</summary>
    public bool Signs { get; set; } = true;

    /// <summary>Tipo II – ilha separadora (gota) nas aproximações das vias secundárias, com alargamento da pista.</summary>
    public TipoIlha SplitterIslands { get; set; } = TipoIlha.Nenhuma;
    public double SplitterLength { get; set; } = 15.0;
    public double SplitterWidth { get; set; } = 2.0;

    /// <summary>Tipo III – faixa de conversão livre à direita com ilha triangular (canalização das esquinas).</summary>
    public TipoIlha RightTurnIslands { get; set; } = TipoIlha.Nenhuma;
    public EsquinasCanalizadas RightTurnCorners { get; set; } = EsquinasCanalizadas.Todas;
    /// <summary>Raio da face externa da faixa de conversão (m).</summary>
    public double RightTurnRadius { get; set; } = 25.0;
    public double RightTurnLaneWidth { get; set; } = 5.0;

    /// <summary>Tipo IV – bolsão de conversão à esquerda na via principal (no canteiro central ou com alargamento).</summary>
    public bool LeftTurnPockets { get; set; }
    public double PocketLength { get; set; } = 30.0;
    public double PocketTaper { get; set; } = 20.0;
    public double PocketWidth { get; set; } = 3.0;

    /// <summary>Ids das marcas criadas pela interseção (faixas, retenções, rampas) – regeneradas com ela.</summary>
    public List<string> ChildIds { get; set; } = new();

    public override double? PointZ => Z;
    public override void Translate(Vec2 delta, double dz) { Node += delta; Z += dz; }
    public override string KindName => "Interseção";
    public override string DisplayCode => "INTERSECAO";
}

/// <summary>Controle do direito de passagem na interseção.</summary>
public enum ControleIntersecao
{
    /// <summary>Parada obrigatória (R-1) nas aproximações das vias secundárias.</summary>
    Pare,
    /// <summary>Dê a preferência (R-2) nas aproximações das vias secundárias.</summary>
    DePreferencia,
    /// <summary>Semafórica: linha de retenção em todas as aproximações.</summary>
    Semaforo,
    /// <summary>Sem sinalização de controle (apenas travessias).</summary>
    Nenhum,
}

/// <summary>Ilhas de canalização.</summary>
public enum TipoIlha
{
    Nenhuma,
    /// <summary>Ilha elevada com meio-fio.</summary>
    Fisica,
    /// <summary>Ilha pintada (zebrado com linha de canalização).</summary>
    Pintada,
}

/// <summary>Esquinas que recebem faixa de conversão livre (canalização).</summary>
public enum EsquinasCanalizadas
{
    Todas,
    /// <summary>Esquinas com ângulo agudo (&lt; 75°) – conversões fechadas, comuns em entroncamentos oblíquos.</summary>
    Agudas,
    /// <summary>Esquinas com ângulo obtuso (&gt; 105°).</summary>
    Obtusas,
}

/// <summary>Um ramo da rotatória (direção a partir do centro).</summary>
public sealed class RoundaboutLeg
{
    /// <summary>Ângulo do ramo (graus, anti-horário a partir do eixo X do projeto).</summary>
    public double AngleDeg { get; set; }
    /// <summary>Largura da pista do ramo (m).</summary>
    public double Width { get; set; } = 7.0;
    public double Sidewalk { get; set; } = 2.5;
    /// <summary>Pavimento da via ligada a este ramo (opcional).</summary>
    public string? RoadId { get; set; }
    /// <summary>Grupo (Sinalizar via) da via ligada – recebe o recorte da rotatória.</summary>
    public string? GroupId { get; set; }
    /// <summary>Via de mão dupla (ilha separadora pintada amarela, entre fluxos opostos; mão única = branca).</summary>
    public bool TwoWay { get; set; } = true;

    // ---- personalização por ramo (nulo/Padrao = valor geral da rotatória)
    public IlhaSeparadora Splitter { get; set; } = IlhaSeparadora.Padrao;
    public double? SplitterLength { get; set; }
    public bool? Crosswalk { get; set; }
    public double? EntryRadius { get; set; }
    public double? ExitRadius { get; set; }
    public ControleRamo Control { get; set; } = ControleRamo.Padrao;
    public bool? Signs { get; set; }

    [JsonIgnore]
    public string Label => $"Ramo a {AngleDeg:0}°";

    /// <summary>Copia a personalização de outro ramo (ramos redetectados a partir das vias mantêm os ajustes).</summary>
    public void CopySettingsFrom(RoundaboutLeg o)
    {
        Splitter = o.Splitter; SplitterLength = o.SplitterLength; Crosswalk = o.Crosswalk; EntryRadius = o.EntryRadius;
        ExitRadius = o.ExitRadius; Control = o.Control; Signs = o.Signs;
    }
}

/// <summary>Como a rotatória se integra às vias que chegam a ela.</summary>
public enum IntegracaoRotatoria
{
    /// <summary>Remodela as entradas (raios de entrada/saída, ilhas separadoras, calçada em volta); as vias são recortadas em toda a zona.</summary>
    Completa,
    /// <summary>Ilha + anel (pista giratória) + marcas; as vias são recortadas só dentro do anel – a geometria das entradas fica como está.</summary>
    Anel,
    /// <summary>Somente a ilha central (com faixa galgável) sobre a pista existente; a pintura das vias é interrompida no anel.</summary>
    SomenteIlha,
}

/// <summary>Ilha separadora (gota) do ramo.</summary>
public enum IlhaSeparadora
{
    /// <summary>Usa o padrão da rotatória.</summary>
    Padrao,
    /// <summary>Meio-fio e núcleo de concreto.</summary>
    Fisica,
    /// <summary>Zebrado com linha de canalização (amarelo entre fluxos opostos, branco em mão única).</summary>
    Pintada,
    Nenhuma,
}

/// <summary>Controle da entrada do ramo.</summary>
public enum ControleRamo
{
    Padrao,
    /// <summary>Linha de dê a preferência + símbolo + R-2.</summary>
    DeAPreferencia,
    /// <summary>Linha de retenção + legenda PARE + R-1.</summary>
    Pare,
}

/// <summary>Placas de sentido de circulação da rotatória.</summary>
public enum PlacaSentidoRotatoria
{
    /// <summary>R-33 nas entradas quando o raio da ilha é menor que 12 m (ou ilha pintada); senão R-24a na ilha (MBST Vol. I / DER).</summary>
    Automatico,
    R33NasEntradas,
    R24aNaIlha,
    Ambas,
    Nenhuma,
}

/// <summary>Tipos de rotatória (DNIT – Manual de Projeto de Interseções; CONTRAN/MBST; FHWA NCHRP 672).</summary>
public enum TipoRotatoria
{
    /// <summary>Minirrotatória: ilha central totalmente galgável (diâmetro inscrito 13–25 m), vias locais.</summary>
    Mini,
    /// <summary>Compacta urbana: uma faixa, diâmetro inscrito 25–35 m.</summary>
    Compacta,
    /// <summary>Convencional de uma faixa: diâmetro inscrito 30–45 m.</summary>
    UmaFaixa,
    /// <summary>Duas faixas na pista giratória: diâmetro inscrito 45–70 m.</summary>
    DuasFaixas,
    /// <summary>Turbo-rotatória: faixas separadas por divisores físicos (sem troca de faixa no anel).</summary>
    Turbo,
    /// <summary>Oval / elíptica: nós alongados, vias paralelas próximas ou canteiro largo.</summary>
    Oval,
    /// <summary>Com faixas de conversão livre à direita (by-pass) separadas por ilhas.</summary>
    ComBypass,
    /// <summary>Personalizada: nenhum valor é imposto.</summary>
    Personalizada,
}

/// <summary>Acabamento da ilha central.</summary>
public enum TipoIlhaCentral
{
    Ajardinada,
    Pavimentada,
    /// <summary>Cúpula galgável (pintada, sem meio-fio).</summary>
    Galgavel,
    /// <summary>Minirrotatória pintada (MIR): linha de canalização branca de 0,20 m com tachões, zebrado opcional – sem obra civil.</summary>
    Pintada,
    /// <summary>Calota galgável rampada: ilha em tronco de cone baixo (concreto), transponível pelas rodas traseiras de veículos longos.</summary>
    Calota,
}

/// <summary>Rotatória com ilha central, pista giratória, faixa galgável e ramos com ilhas separadoras.</summary>
public sealed class RoundaboutDefinition : MarkingDefinition
{
    /// <summary>Perfil de borda (meio-fio, sarjeta, grama, calçada) copiado das vias ligadas – aplicado em volta da pista.</summary>
    public List<Automation.EdgeBand> EdgeProfile { get; set; } = new();

    public TipoRotatoria Type { get; set; } = TipoRotatoria.UmaFaixa;
    /// <summary>Alongamento da ilha (1 = circular; 1,5 = oval com eixo maior 1,5× o menor).</summary>
    public double Elongation { get; set; } = 1.0;
    /// <summary>Direção do eixo maior da ilha oval (graus).</summary>
    public double OvalAngleDeg { get; set; }
    /// <summary>Raio de saída (m). Nulo = igual ao de entrada.</summary>
    public double? ExitRadius { get; set; }
    public TipoIlhaCentral IslandType { get; set; } = TipoIlhaCentral.Ajardinada;
    /// <summary>Altura da faixa galgável (m).</summary>
    public double ApronHeight { get; set; } = 0.06;
    /// <summary>Ilha em calota: altura no centro (m).</summary>
    public double DomeHeight { get; set; } = 0.15;
    /// <summary>Ilha em calota: proporção do platô superior em relação ao raio da ilha (0,1 a 0,9).</summary>
    public double DomeTopRatio { get; set; } = 0.35;
    /// <summary>Ilha em calota: platô superior gramado.</summary>
    public bool DomeGrassTop { get; set; }
    /// <summary>Ilha ajardinada: faixa pavimentada (concreto) entre o meio-fio e a grama (m).</summary>
    public double IslandPavedRing { get; set; }
    /// <summary>Ilha ajardinada: grama acima do meio-fio (m) – canteiro elevado.</summary>
    public double GrassRaise { get; set; }
    /// <summary>Calçadas da rotatória com a composição das vias ligadas (faixa de serviço e sarjeta lidas das vias).</summary>
    public bool MatchRoadSection { get; set; } = true;
    /// <summary>Faixa de serviço entre o meio-fio e o passeio (m); 0 = nenhuma. Preenchida pelas vias quando MatchRoadSection.</summary>
    public double ServiceStripWidth { get; set; }
    public bool ServiceStripGrass { get; set; } = true;
    /// <summary>Sarjeta junto ao meio-fio (m); 0 = nenhuma.</summary>
    public double GutterWidth { get; set; }
    /// <summary>Rotatória elevada (platô): a pista giratória fica acima das vias, com rampas nas entradas/saídas (moderação de tráfego).</summary>
    public bool Raised { get; set; }
    /// <summary>Altura do platô (m) – 0,08 a 0,15 m.</summary>
    public double RaisedHeight { get; set; } = 0.10;
    /// <summary>Comprimento das rampas de acesso ao platô (m).</summary>
    public double RampLength { get; set; } = 1.50;
    /// <summary>Turbo: divisores físicos entre as faixas do anel.</summary>
    public bool TurboDividers { get; set; }
    public double DividerWidth { get; set; } = 0.30;
    /// <summary>Faixas de conversão livre à direita (by-pass) em todos os ramos.</summary>
    public bool Bypass { get; set; }
    public double BypassWidth { get; set; } = 4.5;
    public double BypassRadius { get; set; } = 25.0;
    /// <summary>Distância da travessia de pedestres à borda do anel (m) – uma a duas faixas de veículo (5–10 m).</summary>
    public double CrosswalkDistance { get; set; } = 7.0;
    public double CrosswalkWidth { get; set; } = 3.0;
    /// <summary>Árvores na ilha central (0 = automático pelo tamanho).</summary>
    public int Trees { get; set; }

    // ---- integração com as vias e elementos opcionais (MBST Vol. IV – MIR; DER/SP projetos-tipo 15/16)
    public IntegracaoRotatoria Integration { get; set; } = IntegracaoRotatoria.Completa;
    /// <summary>Recorta as vias ligadas (pavimento e pintura) na área da rotatória. Desligado: nada das vias é alterado.</summary>
    public bool CutRoads { get; set; } = true;
    /// <summary>Estilo padrão das ilhas separadoras (cada ramo pode ter o seu).</summary>
    public IlhaSeparadora SplitterStyle { get; set; } = IlhaSeparadora.Fisica;
    /// <summary>Ilha pintada: preencher com zebrado (senão só a linha de canalização).</summary>
    public bool PaintedIslandFill { get; set; }
    /// <summary>Ilha pintada: largura da linha de canalização (m).</summary>
    public double PaintedLineWidth { get; set; } = 0.20;
    /// <summary>Ilha pintada: espaçamento dos tachões junto à linha (m; 0 = sem tachões). MBST: 0,25 a 0,50 m.</summary>
    public double StudSpacing { get; set; } = 0.50;
    /// <summary>Setas de movimento em curva (IMC) na pista giratória, após cada entrada.</summary>
    public bool RingArrows { get; set; }
    /// <summary>Linha de bordo (LBO) junto ao limite externo do anel, entre os ramos.</summary>
    public bool OuterEdgeLine { get; set; } = true;
    /// <summary>Linha de bordo em volta da ilha/faixa galgável.</summary>
    public bool InnerEdgeLine { get; set; } = true;
    /// <summary>Linha entre as faixas do anel (LMS-2 seccionada, LMS-1 contínua).</summary>
    public string RingLaneLine { get; set; } = "LMS-2";
    /// <summary>Linha dupla contínua (LFO-3) nas aproximações de mão dupla, entre a ilha separadora e o fim da zona remodelada.</summary>
    public bool ApproachDoubleLine { get; set; } = true;
    /// <summary>Velocidade de aproximação (km/h) – dimensiona o símbolo "dê a preferência" e a placa de advertência.</summary>
    public double ApproachSpeed { get; set; } = 40;
    /// <summary>Placa A-12 (interseção em círculo) com "A ... m" antes de cada entrada, à distância de desaceleração.</summary>
    public bool AdvanceWarning { get; set; }
    public PlacaSentidoRotatoria DirectionSigns { get; set; } = PlacaSentidoRotatoria.Automatico;
    /// <summary>Marcadores de alinhamento na ilha central, de frente para cada entrada.</summary>
    public bool AlignmentMarkers { get; set; }

    public Vec2 Center { get; set; }
    public double Z { get; set; }
    /// <summary>Raio da ilha central (face do meio-fio).</summary>
    public double IslandRadius { get; set; } = 8.0;
    /// <summary>Faixa galgável (apron) em torno da ilha, para veículos longos.</summary>
    public double ApronWidth { get; set; } = 1.5;
    public int Lanes { get; set; } = 1;
    public double LaneWidth { get; set; } = 5.0;
    public double SidewalkWidth { get; set; } = 2.5;
    public double EntryRadius { get; set; } = 12.0;
    public double CurbWidth { get; set; } = 0.15;
    public double CurbHeight { get; set; } = 0.15;
    public TipoPavimento Pavement { get; set; } = TipoPavimento.Asfalto;
    public List<RoundaboutLeg> Legs { get; set; } = new();
    /// <summary>Ilhas separadoras (gota) nos ramos.</summary>
    public bool SplitterIslands { get; set; } = true;
    public double SplitterLength { get; set; } = 12.0;
    public double SplitterWidth { get; set; } = 2.5;
    public bool Markings { get; set; } = true;
    public bool Crosswalks { get; set; } = true;
    public bool Signs { get; set; } = true;
    public bool Landscaping { get; set; } = true;
    public List<string> ChildIds { get; set; } = new();

    /// <summary>Maior distância do centro à borda externa do anel (eixo maior, nas ovais).</summary>
    public double OuterRadius => IslandRadius * Math.Max(1, Elongation) + ApronWidth + Lanes * LaneWidth;

    /// <summary>Aplica os valores de referência do tipo (depois tudo pode ser alterado).</summary>
    public void ApplyPreset(TipoRotatoria t)
    {
        Type = t;
        Elongation = 1; TurboDividers = false; Bypass = false; IslandType = TipoIlhaCentral.Ajardinada; ExitRadius = null;
        RingArrows = false; SplitterStyle = IlhaSeparadora.Fisica;
        switch (t)
        {
            case TipoRotatoria.Mini:
                // MBST Vol. IV (MIR 6.a) / DER projeto-tipo 15: ilha pintada (LCA 0,20 m + tachões), gotas pintadas e setas IMC no anel.
                IslandRadius = 2.5; ApronWidth = 0; Lanes = 1; LaneWidth = 6.0; EntryRadius = 8; ExitRadius = 10;
                IslandType = TipoIlhaCentral.Pintada; StudSpacing = 0.50; RingArrows = true; SplitterIslands = true; SplitterStyle = IlhaSeparadora.Pintada;
                SplitterLength = 8; SplitterWidth = 1.5; Landscaping = false; CrosswalkDistance = 5; break;
            case TipoRotatoria.Compacta:
                IslandRadius = 6; ApronWidth = 1.5; Lanes = 1; LaneWidth = 5.5; EntryRadius = 12; ExitRadius = 15;
                SplitterIslands = true; SplitterLength = 10; SplitterWidth = 2.0; CrosswalkDistance = 6; break;
            case TipoRotatoria.UmaFaixa:
                IslandRadius = 10; ApronWidth = 1.5; Lanes = 1; LaneWidth = 5.5; EntryRadius = 15; ExitRadius = 20;
                SplitterIslands = true; SplitterLength = 15; SplitterWidth = 2.5; CrosswalkDistance = 7; break;
            case TipoRotatoria.DuasFaixas:
                IslandRadius = 14; ApronWidth = 1.5; Lanes = 2; LaneWidth = 4.8; EntryRadius = 20; ExitRadius = 25;
                SplitterIslands = true; SplitterLength = 20; SplitterWidth = 3.0; CrosswalkDistance = 8; break;
            case TipoRotatoria.Turbo:
                IslandRadius = 14; ApronWidth = 1.0; Lanes = 2; LaneWidth = 4.5; EntryRadius = 18; ExitRadius = 22;
                TurboDividers = true; SplitterIslands = true; SplitterLength = 20; SplitterWidth = 3.0; CrosswalkDistance = 8; break;
            case TipoRotatoria.Oval:
                IslandRadius = 8; Elongation = 1.6; ApronWidth = 1.5; Lanes = 1; LaneWidth = 5.5; EntryRadius = 15; ExitRadius = 20;
                SplitterIslands = true; SplitterLength = 12; SplitterWidth = 2.5; CrosswalkDistance = 7; break;
            case TipoRotatoria.ComBypass:
                IslandRadius = 10; ApronWidth = 1.5; Lanes = 1; LaneWidth = 5.5; EntryRadius = 15; ExitRadius = 20;
                Bypass = true; BypassWidth = 4.5; BypassRadius = 30; SplitterIslands = true; SplitterLength = 15; SplitterWidth = 2.5; break;
        }
    }
    public override double? PointZ => Z;
    public override void Translate(Vec2 delta, double dz) { Center += delta; Z += dz; }
    public override string KindName => "Rotatória";
    public override string DisplayCode => "ROTATORIA";
}

/// <summary>Rota tátil (NBR 16537): piso direcional ao longo do percurso e alerta nas mudanças de direção, extremos e junções.</summary>
public sealed class TactileRouteDefinition : MarkingDefinition
{
    public PathReference PathRef { get; set; } = new();
    /// <summary>Lado da placa (módulo) – 0,25 m ou 0,40 m.</summary>
    public double Module { get; set; } = 0.25;
    /// <summary>Número de placas na largura da faixa direcional.</summary>
    public int Rows { get; set; } = 1;
    public MarkingColor Color { get; set; } = MarkingColor.Amarela;
    public bool AlertAtTurns { get; set; } = true;
    public bool AlertAtEnds { get; set; } = true;
    public bool AlertAtJunctions { get; set; } = true;
    /// <summary>Lado do quadrado de alerta em módulos (no mínimo a largura da direcional).</summary>
    public int AlertModules { get; set; } = 1;
    /// <summary>Extremos: profundidade da faixa de alerta em módulos.</summary>
    public int EndAlertModules { get; set; } = 1;
    /// <summary>Somente alerta ao longo do caminho (faixa de alerta), sem direcional.</summary>
    public bool AlertOnly { get; set; }
    public bool Relief { get; set; } = true;
    /// <summary>Nível do piso onde as placas são assentadas (m acima da pista – topo da calçada).</summary>
    public double Elevation { get; set; } = 0.15;

    public override string KindName => "Rota tátil";
    public override string DisplayCode => AlertOnly ? "PTA" : "ROTA-TATIL";
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

/// <summary>Bitola da via férrea (distância entre as faces internas dos boletos).</summary>
public enum BitolaFerroviaria
{
    /// <summary>Bitola larga (1,600 m) – malha brasileira de carga e passageiros.</summary>
    Larga,
    /// <summary>Bitola métrica (1,000 m).</summary>
    Metrica,
    /// <summary>Bitola padrão/internacional (1,435 m) – metrôs e VLTs.</summary>
    Padrao,
    /// <summary>Bitola mista (1,000 + 1,600 m, terceiro trilho).</summary>
    Mista,
    Personalizada,
}

/// <summary>Tipo de superestrutura da via.</summary>
public enum TipoViaFerrea
{
    /// <summary>Via em lastro de brita sobre sublastro (convencional).</summary>
    Lastro,
    /// <summary>Via em laje de concreto (fixação direta).</summary>
    Laje,
    /// <summary>Via embutida no pavimento (VLT/bonde em via urbana): trilhos de canaleta rentes ao piso.</summary>
    Embutida,
}

public enum TipoDormente
{
    ConcretoMonobloco,
    ConcretoBibloco,
    Madeira,
    Aco,
}

/// <summary>Perfil do trilho (altura, boleto e patim).</summary>
public enum PerfilTrilho
{
    TR45,
    TR57,
    TR68,
    UIC60,
    /// <summary>Trilho de canaleta (grooved) Ri60 – vias embutidas de VLT/bonde.</summary>
    Ri60,
}

/// <summary>Acabamento da superfície da via embutida.</summary>
public enum SuperficieViaEmbutida
{
    Asfalto,
    Concreto,
    Grama,
    Bloquete,
}

/// <summary>
/// Via férrea completa ao longo de um eixo: plataforma, sublastro, lastro com taludes, dormentes, placas de apoio e trilhos
/// – ou via em laje / embutida no pavimento (VLT) –, com uma ou mais linhas paralelas, valetas de drenagem e todos os
/// parâmetros personalizáveis.
/// </summary>
public sealed class RailwayDefinition : MarkingDefinition
{
    public PathReference PathRef { get; set; } = new();
    public TipoViaFerrea Type { get; set; } = TipoViaFerrea.Lastro;
    public BitolaFerroviaria Gauge { get; set; } = BitolaFerroviaria.Larga;
    /// <summary>Bitola personalizada (m).</summary>
    public double CustomGauge { get; set; } = 1.60;
    public PerfilTrilho Rail { get; set; } = PerfilTrilho.TR57;
    /// <summary>Número de linhas paralelas.</summary>
    public int Tracks { get; set; } = 1;
    /// <summary>Entrevia: distância entre eixos de linhas vizinhas (m).</summary>
    public double TrackSpacing { get; set; } = 4.50;
    /// <summary>Deslocamento lateral do conjunto em relação ao eixo desenhado (+ à esquerda).</summary>
    public double Offset { get; set; }

    // ---- dormentes
    public TipoDormente Sleeper { get; set; } = TipoDormente.ConcretoMonobloco;
    /// <summary>Espaçamento entre eixos de dormentes (m).</summary>
    public double SleeperSpacing { get; set; } = 0.60;
    /// <summary>Comprimento do dormente (m). 0 = pela bitola (larga 2,80; padrão 2,60; métrica 2,00).</summary>
    public double SleeperLength { get; set; }
    /// <summary>Largura da base do dormente (m). 0 = pelo tipo.</summary>
    public double SleeperWidth { get; set; }
    /// <summary>Altura do dormente (m). 0 = pelo tipo.</summary>
    public double SleeperHeight { get; set; }
    /// <summary>Placas de apoio e fixações (grampos) sobre os dormentes.</summary>
    public bool Fastenings { get; set; } = true;

    // ---- lastro e plataforma
    /// <summary>Altura do lastro sob o dormente (m).</summary>
    public double BallastDepth { get; set; } = 0.30;
    /// <summary>Ombro do lastro além da ponta do dormente (m).</summary>
    public double BallastShoulder { get; set; } = 0.40;
    /// <summary>Talude do lastro (horizontal : 1 vertical).</summary>
    public double BallastSlope { get; set; } = 1.5;
    /// <summary>Espessura do sublastro (m). 0 = sem sublastro.</summary>
    public double SubBallastDepth { get; set; } = 0.20;
    /// <summary>Largura do sublastro além do pé do lastro, de cada lado (m).</summary>
    public double SubBallastExtra { get; set; } = 0.60;
    /// <summary>Valetas de drenagem de concreto nas bordas da plataforma.</summary>
    public bool Ditches { get; set; } = true;
    public double DitchWidth { get; set; } = 0.60;
    public double DitchDepth { get; set; } = 0.40;

    // ---- laje / via embutida
    /// <summary>Espessura da laje de concreto (via em laje ou base da via embutida) (m).</summary>
    public double SlabThickness { get; set; } = 0.30;
    /// <summary>Largura da laje/faixa da via embutida por linha (m). 0 = bitola + 1,40 m.</summary>
    public double SlabWidth { get; set; }
    public SuperficieViaEmbutida Surface { get; set; } = SuperficieViaEmbutida.Concreto;
    /// <summary>Via embutida: nível do topo do trilho em relação ao pavimento (m) – 0 = rente.</summary>
    public double RailTopLevel { get; set; }

    public override string KindName => "Via férrea";
    public override string DisplayCode => Type switch
    {
        TipoViaFerrea.Embutida => "VLT-EMB",
        TipoViaFerrea.Laje => "FERROVIA-LAJE",
        _ => "FERROVIA",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}
