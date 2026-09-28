using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>Obras que se apoiam na topografia (Toposolid) e podem ajustá-la (corte/aterro).</summary>
public interface ITerrainAware
{
    /// <summary>Usa o terreno natural na geração (pilares até o chão, base de muros e taludes no terreno, emboques).</summary>
    bool FollowTerrain { get; }
    /// <summary>Ajusta o Toposolid (terraplenagem) ao criar/editar.</summary>
    bool AdjustTerrain { get; }

    /// <summary>
    /// Terreno natural de referência ao longo do eixo (estação, cota relativa), lido na primeira geração e guardado: depois da
    /// terraplenagem o Toposolid muda, mas a obra continua projetada sobre o terreno original (sem "afundar" a cada
    /// regeneração). Nulo = ler de novo.
    /// </summary>
    List<Vec2>? GroundLine { get; set; }
}

/// <summary>
/// Obra hospedada num trecho de uma via do plugin (ponte, viaduto, túnel, trincheira): a pista, as calçadas e a
/// sinalização são da via (pisos do Revit com o greide) e a obra gera só a estrutura no trecho [início, fim] do eixo.
/// </summary>
public interface IHostedStructure
{
    /// <summary>Grupo (GroupId) da via hospedeira; nulo = obra avulsa (formato antigo).</summary>
    string? HostRoad { get; set; }
    /// <summary>Estações (m) do início e do fim da obra no eixo da via.</summary>
    double HostStart { get; set; }
    double HostEnd { get; set; }
    /// <summary>Cópia da seção da via no momento da criação (usada se a via não for encontrada).</summary>
    double HostHalf { get; set; }
    double HostWear { get; set; }
    double HostEdgeRise { get; set; }
    /// <summary>Greide da via antes desta obra (para refazer o trecho ao editar a obra).</summary>
    RoadGrade? GradeBefore { get; set; }
}

/// <summary>Perfil (greide) da obra ao criá-la.</summary>
public enum PerfilObra
{
    /// <summary>Rampas de acesso até a altura da obra, trecho em nível e curvas verticais (viadutos).</summary>
    RampasDeAcesso,
    /// <summary>Tabuleiro em nível na altura indicada, sem rampas (pontes retas e planas).</summary>
    Horizontal,
    /// <summary>Reta de uma margem à outra, na cota do terreno das cabeceiras (pontes sobre vales e rios).</summary>
    EntreMargens,
    /// <summary>Curva vertical convexa única com o ponto alto no meio (pontes em "lombo").</summary>
    Convexo,
    /// <summary>Mantém o greide que a via já tem.</summary>
    GreideDaVia,
    /// <summary>Tabuleiro inclinado: rampa constante da altura no início à altura no fim (encosta, cais, acesso em desnível).</summary>
    Inclinado,
    /// <summary>Curva vertical côncava ("barriga") com o ponto baixo no meio – ponte sobre vale entre duas cristas.</summary>
    Concavo,
    /// <summary>Perfil personalizado: PIVs digitados (estaca a partir do início da obra; cota; curva).</summary>
    Personalizado,
}

// ====================================================================== drenagem

public enum TipoDrenagem
{
    /// <summary>Boca de lobo simples (abertura na guia – "guia chapéu" – e caixa sob a calçada).</summary>
    BocaDeLoboSimples,
    /// <summary>Boca de lobo dupla (duas aberturas e caixa contínua).</summary>
    BocaDeLoboDupla,
    /// <summary>Boca de lobo com grelha na sarjeta (sem abertura na guia).</summary>
    BocaDeLoboGrelha,
    /// <summary>Boca de lobo combinada (abertura na guia + grelha na sarjeta).</summary>
    BocaDeLoboCombinada,
    /// <summary>Grelha de sarjeta (ralo) com caixa.</summary>
    GrelhaSarjeta,
    /// <summary>Grelha quadrada de piso (pátios, praças, calçadões).</summary>
    GrelhaQuadrada,
    /// <summary>Canaleta com grelha contínua ao longo de uma linha.</summary>
    GrelhaContinua,
    /// <summary>Poço de visita (PV) com tampão circular.</summary>
    PocoDeVisita,
}

public enum OrientacaoBarras
{
    /// <summary>Barras transversais ao fluxo (seguras para ciclistas – recomendado em vias).</summary>
    Transversal,
    Longitudinal,
    Diagonal,
}

public enum MaterialGrelha { FerroFundido, AcoGalvanizado, Concreto }

/// <summary>Desenho do tampo da grelha.</summary>
public enum EstiloGrelha
{
    /// <summary>Barras paralelas com nervura central (padrão de sarjeta).</summary>
    Barras,
    /// <summary>Malha quadriculada (barras nos dois sentidos).</summary>
    Malha,
    /// <summary>Chapa com fendas (rasgos) desencontradas – calçadões e áreas de pedestres.</summary>
    Fendas,
}

/// <summary>Tampa de inspeção da caixa.</summary>
public enum TipoTampa
{
    /// <summary>Tampa de concreto removível com alças, rente à calçada.</summary>
    Concreto,
    /// <summary>Tampão de ferro fundido circular em aro quadrado (NBR 10160).</summary>
    FerroFundido,
    /// <summary>Sem tampa (laje contínua).</summary>
    Nenhuma,
}

/// <summary>Saída do tubo de ligação da caixa.</summary>
public enum SaidaTubo
{
    /// <summary>Para baixo da pista (ramal até a galeria / PV no eixo).</summary>
    SobAPista,
    /// <summary>Ao longo do meio-fio.</summary>
    AoLongo,
}

/// <summary>
/// Dispositivos de drenagem superficial urbana: bocas de lobo, grelhas, canaletas e poços de visita – com a caixa de
/// captação, a tampa de inspeção e o rebaixo da sarjeta. Posicionados junto ao meio-fio (alinhados à via) ou livres.
/// </summary>
public sealed class DrainageDefinition : MarkingDefinition
{
    public TipoDrenagem Type { get; set; } = TipoDrenagem.BocaDeLoboSimples;
    /// <summary>Ponto na face do meio-fio (ou centro, nos tipos livres) (m).</summary>
    public Vec2 Position { get; set; }
    /// <summary>Direção ao longo do meio-fio (unitária).</summary>
    public Vec2 Along { get; set; } = Vec2.UnitX;
    /// <summary>Calçada à esquerda de <see cref="Along"/> (a pista fica do outro lado).</summary>
    public bool SidewalkLeft { get; set; } = true;
    public double Z { get; set; }
    /// <summary>Canaleta contínua: linha do eixo.</summary>
    public PathReference PathRef { get; set; } = new();
    /// <summary>Número de módulos lado a lado (bocas de lobo triplas, grelhas em série).</summary>
    public int Modules { get; set; } = 1;

    // ---- grelha
    public double GrateLength { get; set; } = 1.00;
    public double GrateWidth { get; set; } = 0.40;
    public double BarWidth { get; set; } = 0.025;
    public double BarGap { get; set; } = 0.035;
    public OrientacaoBarras Bars { get; set; } = OrientacaoBarras.Transversal;
    public EstiloGrelha GrateStyle { get; set; } = EstiloGrelha.Barras;
    public double FrameWidth { get; set; } = 0.05;
    public MaterialGrelha Material { get; set; } = MaterialGrelha.FerroFundido;
    /// <summary>Afastamento da grelha em relação à face do meio-fio (m).</summary>
    public double GrateOffset { get; set; } = 0.05;

    // ---- boca na guia
    public double OpeningLength { get; set; } = 1.00;
    public double OpeningHeight { get; set; } = 0.15;
    public double CurbHeight { get; set; } = 0.15;
    public double CurbWidth { get; set; } = 0.15;
    /// <summary>Rebaixo da sarjeta junto à boca (m) – aumenta a captação.</summary>
    public double Depression { get; set; } = 0.05;
    /// <summary>Transição do rebaixo antes e depois da boca (m).</summary>
    public double DepressionLength { get; set; } = 1.00;
    public double GutterWidth { get; set; } = 0.60;

    // ---- caixa
    public double BoxLength { get; set; } = 1.10;
    public double BoxWidth { get; set; } = 0.80;
    public double BoxDepth { get; set; } = 1.00;
    public double WallThickness { get; set; } = 0.15;
    /// <summary>Tampa de inspeção na laje da caixa.</summary>
    public bool Lid { get; set; } = true;
    public TipoTampa LidType { get; set; } = TipoTampa.Concreto;
    public double LidDiameter { get; set; } = 0.60;
    /// <summary>Espessura da laje de cobertura da caixa (m).</summary>
    public double SlabThickness { get; set; } = 0.12;
    /// <summary>Tubo de ligação (saída) da caixa.</summary>
    public bool OutletPipe { get; set; } = true;
    public double PipeDiameter { get; set; } = 0.40;
    public SaidaTubo PipeDirection { get; set; } = SaidaTubo.SobAPista;
    public double PipeLength { get; set; } = 2.5;
    /// <summary>Degraus de acesso (poço de visita).</summary>
    public bool Steps { get; set; } = true;

    // ---- encaixe na via
    /// <summary>Recorta os pisos da via, sarjeta, meio-fio, calçada e as faixas pintadas sob o dispositivo (ele os substitui).</summary>
    public bool CutFloors { get; set; } = true;
    /// <summary>Ao clicar junto a um meio-fio, adota a altura e a largura dele.</summary>
    public bool AutoFit { get; set; } = true;
    /// <summary>Espaçamento na colocação em série ao longo do meio-fio (m).</summary>
    public double SeriesSpacing { get; set; } = 40;

    public override string KindName => "Drenagem";
    public override string DisplayCode => Type switch
    {
        TipoDrenagem.BocaDeLoboSimples => "BL-S",
        TipoDrenagem.BocaDeLoboDupla => "BL-D",
        TipoDrenagem.BocaDeLoboGrelha => "BL-G",
        TipoDrenagem.BocaDeLoboCombinada => "BL-C",
        TipoDrenagem.GrelhaSarjeta => "GR-S",
        TipoDrenagem.GrelhaQuadrada => "GR-Q",
        TipoDrenagem.GrelhaContinua => "CAN-G",
        _ => "PV",
    };
    public bool IsLinear => Type == TipoDrenagem.GrelhaContinua;
    public override PathReference? Path => IsLinear ? PathRef : null;
    public override double? PointZ => IsLinear ? null : Z;
    public override void SetPath(PathReference path) => PathRef = path;
    public override void Translate(Vec2 delta, double dz) { Position += delta; Z += dz; }

    /// <summary>Medidas usuais de cada tipo (PMSP/DER-SP, NBR 10160) – aplicadas ao trocar o tipo.</summary>
    public void ApplyDefaults()
    {
        switch (Type)
        {
            case TipoDrenagem.BocaDeLoboSimples:
                OpeningLength = 1.00; BoxLength = 1.10; BoxWidth = 0.80; BoxDepth = 1.00; Depression = 0.05; Modules = 1; break;
            case TipoDrenagem.BocaDeLoboDupla:
                OpeningLength = 2.00; BoxLength = 2.20; BoxWidth = 0.80; BoxDepth = 1.00; Depression = 0.05; Modules = 1; break;
            case TipoDrenagem.BocaDeLoboGrelha:
            case TipoDrenagem.BocaDeLoboCombinada:
                OpeningLength = 1.00; GrateLength = 1.00; GrateWidth = 0.40; BoxLength = 1.10; BoxWidth = 0.80; BoxDepth = 1.00; Depression = 0.03; break;
            case TipoDrenagem.GrelhaSarjeta:
                GrateLength = 0.90; GrateWidth = 0.35; BoxLength = 1.00; BoxWidth = 0.60; BoxDepth = 0.80; Depression = 0.02; break;
            case TipoDrenagem.GrelhaQuadrada:
                GrateLength = 0.60; GrateWidth = 0.60; BoxLength = 0.60; BoxWidth = 0.60; BoxDepth = 0.80; Depression = 0; break;
            case TipoDrenagem.GrelhaContinua:
                GrateWidth = 0.30; BoxDepth = 0.40; WallThickness = 0.10; Bars = OrientacaoBarras.Transversal; break;
            case TipoDrenagem.PocoDeVisita:
                BoxLength = 1.20; BoxDepth = 2.00; WallThickness = 0.20; LidDiameter = 0.60; break;
        }
    }
}

// ====================================================================== viaduto / ponte

public enum TipoObraDeArte { Viaduto, Ponte, Passarela }

public enum SistemaEstrutural
{
    /// <summary>Vigas pré-moldadas (longarinas "I") com laje e transversinas.</summary>
    VigasPreMoldadas,
    /// <summary>Viga caixão celular.</summary>
    CaixaoCelular,
    /// <summary>Laje maciça (vãos curtos).</summary>
    LajeMacica,
    /// <summary>Arco inferior (tabuleiro superior sobre montantes).</summary>
    ArcoInferior,
    /// <summary>Arco superior atirantado (bowstring) com pendurais.</summary>
    ArcoSuperior,
    /// <summary>Estaiada (mastro central e estais em leque).</summary>
    Estaiada,
    /// <summary>Treliça metálica (Warren) lateral.</summary>
    Trelica,
}

public enum TipoPilar
{
    Circular,
    DuplaCircular,
    /// <summary>Pilar-parede com as pontas arredondadas.</summary>
    Parede,
    /// <summary>Pórtico: colunas e travessa.</summary>
    Portico,
    /// <summary>Martelo: coluna única e capitel em balanço.</summary>
    Martelo,
    /// <summary>Pilar em "Y" (fuste único que se abre em dois braços).</summary>
    Y,
    /// <summary>Coluna oblonga (seção de pista de atletismo), comum em viadutos urbanos.</summary>
    Oblongo,
}

public enum TipoGuarda { NewJersey, GuardaCorpoMetalico, NewJerseyComGuardaCorpo }

/// <summary>Preenchimento do guarda-corpo de pedestres.</summary>
public enum TipoGuardaCorpo
{
    /// <summary>Três tubos horizontais e montantes.</summary>
    Tubular,
    /// <summary>Balaústres verticais (vão ≤ 11 cm – seguro para crianças).</summary>
    Balaustres,
    /// <summary>Painéis de vidro laminado entre os montantes.</summary>
    Vidro,
}

/// <summary>Forma do mastro de pontes estaiadas.</summary>
public enum FormaMastro { H, A, Central }

/// <summary>Arranjo dos estais.</summary>
public enum ArranjoEstais { Leque, Harpa }

/// <summary>Alas do encontro.</summary>
public enum TipoAla
{
    /// <summary>Alas paralelas ao eixo (aterro contido).</summary>
    Paralelas,
    /// <summary>Alas inclinadas acompanhando a saia do aterro.</summary>
    Abertas,
}

/// <summary>
/// Viaduto, ponte ou passarela ao longo de um eixo: greide com rampas de acesso em aterro, encontros, pilares, vigas,
/// tabuleiro, pavimento, passeios, barreiras/guarda-corpos, juntas, iluminação e faixas pintadas.
/// </summary>
public sealed class BridgeDefinition : MarkingDefinition, ITerrainAware, IHostedStructure
{
    public PathReference PathRef { get; set; } = new();
    public string? HostRoad { get; set; }
    public double HostStart { get; set; }
    public double HostEnd { get; set; }
    public double HostHalf { get; set; } = 6;
    public double HostWear { get; set; } = 0.05;
    public double HostEdgeRise { get; set; }
    public RoadGrade? GradeBefore { get; set; }
    /// <summary>Greide da obra ao ser criada (rampas, horizontal, entre margens, convexo).</summary>
    public PerfilObra ProfileKind { get; set; } = PerfilObra.RampasDeAcesso;
    /// <summary>Traçado reto entre as pontas do eixo desenhado (ignora os vértices intermediários).</summary>
    public bool StraightAxis { get; set; }
    /// <summary>Perfil inclinado: cota do tabuleiro no fim da obra (m sobre a base; o início usa <see cref="Height"/>).</summary>
    public double EndHeight { get; set; } = 4.0;
    /// <summary>Perfil personalizado: PIVs "estaca; cota; curva" (estaca a partir do início da obra, cota sobre a base).</summary>
    public string ProfilePvis { get; set; } = "";
    /// <summary>Esconsidade dos apoios (graus, ±60) – pilares e encontros paralelos ao rio/via cruzada.</summary>
    public double Skew { get; set; }
    /// <summary>Estações dos pilares (m, a partir do início da obra) – vazio = distribuídos pelo vão.</summary>
    public List<double>? PierStations { get; set; }
    public TipoObraDeArte Kind { get; set; } = TipoObraDeArte.Viaduto;
    public SistemaEstrutural System { get; set; } = SistemaEstrutural.VigasPreMoldadas;
    public int Lanes { get; set; } = 2;
    public double LaneWidth { get; set; } = 3.50;
    /// <summary>Faixa de segurança / acostamento junto a cada barreira (m).</summary>
    public double ShoulderWidth { get; set; } = 1.00;
    /// <summary>Passeio de cada lado (m, 0 = sem passeio).</summary>
    public double SidewalkWidth { get; set; } = 1.50;
    public double DeckThickness { get; set; } = 0.25;
    /// <summary>Altura das vigas (m). 0 = pelo vão (≈ vão/16).</summary>
    public double GirderDepth { get; set; }
    public double GirderSpacing { get; set; } = 2.80;
    /// <summary>Cota do topo do tabuleiro sobre a base (m).</summary>
    public double Height { get; set; } = 7.50;
    /// <summary>Rampas de acesso em aterro no início / fim.</summary>
    public bool ApproachStart { get; set; } = true;
    public bool ApproachEnd { get; set; } = true;
    /// <summary>Rampa máxima das rampas de acesso (m/m).</summary>
    public double MaxGrade { get; set; } = 0.05;
    public double VerticalCurve { get; set; } = 60;
    /// <summary>Contenção das rampas de acesso com muros de terra armada (senão, taludes).</summary>
    public bool ApproachWalls { get; set; }
    public double FillSlope { get; set; } = 1.5;
    public double SpanLength { get; set; } = 30;
    /// <summary>Vão principal (arcos, estaiadas) – 0 = toda a estrutura.</summary>
    public double MainSpan { get; set; }
    public TipoPilar PierType { get; set; } = TipoPilar.Portico;
    public double PierSize { get; set; } = 1.20;
    public TipoGuarda Barrier { get; set; } = TipoGuarda.NewJersey;
    public bool Lighting { get; set; } = true;
    public double LightSpacing { get; set; } = 30;
    public bool LaneMarkings { get; set; } = true;
    /// <summary>Caimento transversal do tabuleiro (m/m) – 2 %, abaulado a partir do eixo.</summary>
    public double CrossSlope { get; set; } = 0.02;
    /// <summary>Tabuleiro contínuo (juntas só nos encontros); falso = vãos isostáticos com junta sobre cada pilar.</summary>
    public bool Continuous { get; set; } = true;
    /// <summary>Viga caixão com altura variável (mísulas parabólicas sobre os pilares).</summary>
    public bool VariableDepth { get; set; } = true;
    public TipoGuardaCorpo RailingStyle { get; set; } = TipoGuardaCorpo.Tubular;
    public FormaMastro Pylon { get; set; } = FormaMastro.H;
    public ArranjoEstais Stays { get; set; } = ArranjoEstais.Leque;
    /// <summary>Flecha do arco / vão (1/6 usual).</summary>
    public double ArchRise { get; set; } = 1.0 / 6;
    public TipoAla WingWalls { get; set; } = TipoAla.Abertas;
    /// <summary>Buzinotes (drenos do tabuleiro).</summary>
    public bool Drains { get; set; } = true;
    /// <summary>Lâmina d'água sob a ponte (cota em relação à base, m) – só referência visual.</summary>
    public bool Water { get; set; }
    public double WaterLevel { get; set; } = 1.0;
    public double WaterWidth { get; set; } = 30;
    public bool FollowTerrain { get; set; } = true;
    public bool AdjustTerrain { get; set; } = true;
    public List<Vec2>? GroundLine { get; set; }

    public override string KindName => Kind switch { TipoObraDeArte.Ponte => "Ponte", TipoObraDeArte.Passarela => "Passarela", _ => "Viaduto" };
    public override string DisplayCode => Kind switch { TipoObraDeArte.Ponte => "OAE-PONTE", TipoObraDeArte.Passarela => "OAE-PASS", _ => "OAE-VIAD" };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;

    public double RoadWidth => Math.Max(1, Lanes) * LaneWidth + 2 * ShoulderWidth;

    public void ApplyKindDefaults()
    {
        switch (Kind)
        {
            case TipoObraDeArte.Ponte:
                SpanLength = 40; Height = 8.0; System = SistemaEstrutural.VigasPreMoldadas; Water = true; StraightAxis = true;
                ProfileKind = PerfilObra.RampasDeAcesso; PierType = TipoPilar.Parede; break;
            case TipoObraDeArte.Passarela:
                Lanes = 0; LaneWidth = 3.0; ShoulderWidth = 0; SidewalkWidth = 0; Height = 7.0; SpanLength = 35; System = SistemaEstrutural.Trelica;
                Barrier = TipoGuarda.GuardaCorpoMetalico; MaxGrade = 0.0833; LaneMarkings = false; PierType = TipoPilar.Circular; PierSize = 0.8; break;
            default:
                SpanLength = 30; Height = 7.5; Water = false; break;
        }
    }
}

// ====================================================================== túnel

public enum SecaoTunel
{
    /// <summary>Ferradura (NATM): abóbada em arco com paredes e arco invertido.</summary>
    Ferradura,
    /// <summary>Circular (TBM, anéis segmentados).</summary>
    Circular,
    /// <summary>Retangular (vala a céu aberto / "cut and cover").</summary>
    Retangular,
}

public enum TipoEmboque
{
    /// <summary>Parede de testa vertical.</summary>
    Testa,
    /// <summary>Emboque em bisel (chanfrado, acompanha o talude).</summary>
    Bisel,
    /// <summary>Pala (marquise) em balanço à frente da testa.</summary>
    Pala,
}

/// <summary>Túnel rodoviário com revestimento, pavimento, passeios de serviço, iluminação, ventiladores e emboques.</summary>
public sealed class TunnelDefinition : MarkingDefinition, ITerrainAware, IHostedStructure
{
    public PathReference PathRef { get; set; } = new();
    public string? HostRoad { get; set; }
    public double HostStart { get; set; }
    public double HostEnd { get; set; }
    public double HostHalf { get; set; } = 6;
    public double HostWear { get; set; } = 0.05;
    public double HostEdgeRise { get; set; }
    public RoadGrade? GradeBefore { get; set; }
    /// <summary>Greide do túnel: em rampa constante entre os emboques (falso = segue o greide da via).</summary>
    public bool StraightAxis { get; set; }
    public SecaoTunel Section { get; set; } = SecaoTunel.Ferradura;
    public int Lanes { get; set; } = 2;
    public double LaneWidth { get; set; } = 3.50;
    public double ShoulderWidth { get; set; } = 0.50;
    /// <summary>Passeio de serviço elevado de cada lado (m).</summary>
    public double WalkwayWidth { get; set; } = 0.90;
    public double WalkwayHeight { get; set; } = 0.25;
    /// <summary>Gabarito vertical livre sobre a pista (m).</summary>
    public double ClearHeight { get; set; } = 5.50;
    public double LiningThickness { get; set; } = 0.40;
    public double InvertThickness { get; set; } = 0.50;
    /// <summary>Cota da pista no início / fim do túnel (m, relativa à base).</summary>
    public double StartZ { get; set; }
    public double EndZ { get; set; }
    public TipoEmboque Portal { get; set; } = TipoEmboque.Testa;
    public double PortalLength { get; set; } = 6.0;
    public bool Lighting { get; set; } = true;
    public bool JetFans { get; set; } = true;
    public double FanSpacing { get; set; } = 100;
    /// <summary>Trincheira de acesso escavada diante de cada emboque (m).</summary>
    public double ApproachCut { get; set; } = 25;
    public bool FollowTerrain { get; set; } = true;
    public bool AdjustTerrain { get; set; } = true;
    public List<Vec2>? GroundLine { get; set; }

    public override string KindName => "Túnel";
    public override string DisplayCode => "OAE-TUNEL";
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
    public double RoadWidth => Math.Max(1, Lanes) * LaneWidth + 2 * ShoulderWidth;
}

// ====================================================================== trincheira

public enum TipoContencaoTrincheira { Flexao, CortinaAtirantada, TerraArmada }

/// <summary>Trincheira (via rebaixada): rampas de descida e subida, muros de contenção, guarda-corpos e laje de travessia.</summary>
public sealed class TrenchDefinition : MarkingDefinition, ITerrainAware, IHostedStructure
{
    public PathReference PathRef { get; set; } = new();
    public string? HostRoad { get; set; }
    public double HostStart { get; set; }
    public double HostEnd { get; set; }
    public double HostHalf { get; set; } = 6;
    public double HostWear { get; set; } = 0.05;
    public double HostEdgeRise { get; set; }
    public RoadGrade? GradeBefore { get; set; }
    public bool StraightAxis { get; set; }
    /// <summary>Só os muros: o greide da via (já rebaixado pelo Perfil da via) é mantido.</summary>
    public bool KeepRoadGrade { get; set; }
    public int Lanes { get; set; } = 2;
    public double LaneWidth { get; set; } = 3.50;
    public double ShoulderWidth { get; set; } = 0.60;
    /// <summary>Rebaixo da pista no trecho central (m).</summary>
    public double Depth { get; set; } = 6.5;
    public double MaxGrade { get; set; } = 0.06;
    public double VerticalCurve { get; set; } = 40;
    public TipoContencaoTrincheira Wall { get; set; } = TipoContencaoTrincheira.Flexao;
    public double WallThickness { get; set; } = 0.40;
    public double ParapetHeight { get; set; } = 1.10;
    /// <summary>Laje de travessia (viaduto da via transversal) sobre a trincheira – comprimento (m), 0 = sem.</summary>
    public double CoverLength { get; set; } = 14;
    /// <summary>Posição do centro da laje ao longo do eixo (0..1).</summary>
    public double CoverAt { get; set; } = 0.5;
    public bool Drainage { get; set; } = true;
    public bool Lighting { get; set; } = true;
    public bool FollowTerrain { get; set; } = true;
    public bool AdjustTerrain { get; set; } = true;
    public List<Vec2>? GroundLine { get; set; }

    public override string KindName => "Trincheira";
    public override string DisplayCode => "OAE-TRINCH";
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
    public double RoadWidth => Math.Max(1, Lanes) * LaneWidth + 2 * ShoulderWidth;
}

// ====================================================================== muro de arrimo

public enum TipoMuro
{
    /// <summary>Gravidade (concreto ciclópico / pedra argamassada) – seção trapezoidal.</summary>
    Gravidade,
    /// <summary>Flexão (concreto armado em "L"/"T invertido": parede + sapata com ponta e talão).</summary>
    Flexao,
    /// <summary>Flexão com contrafortes.</summary>
    Contrafortes,
    /// <summary>Gabião (caixas de pedra em degraus).</summary>
    Gabiao,
    /// <summary>Terra armada (placas de concreto cruciformes).</summary>
    TerraArmada,
    /// <summary>Cortina atirantada (parede com cabeças de tirantes).</summary>
    CortinaAtirantada,
}

/// <summary>Muro de arrimo ao longo de uma linha, com fundação, drenagem (barbacãs), coroamento e guarda-corpo.</summary>
public sealed class RetainingWallDefinition : MarkingDefinition, ITerrainAware
{
    public PathReference PathRef { get; set; } = new();
    public TipoMuro Type { get; set; } = TipoMuro.Flexao;
    public double HeightStart { get; set; } = 3.0;
    public double HeightEnd { get; set; } = 3.0;
    public double TopWidth { get; set; } = 0.30;
    /// <summary>Largura da base (gravidade) / sapata (flexão) – 0 = automática (≈ 0,5–0,7 H).</summary>
    public double BaseWidth { get; set; }
    public double FootingThickness { get; set; } = 0.40;
    public double ToeLength { get; set; } = 0.50;
    /// <summary>Ficha: embutimento abaixo do terreno (m).</summary>
    public double Embedment { get; set; } = 0.60;
    public double CounterfortSpacing { get; set; } = 3.0;
    public double GabionSize { get; set; } = 1.0;
    public double PanelSize { get; set; } = 1.5;
    public double AnchorSpacing { get; set; } = 2.5;
    public bool Coping { get; set; } = true;
    /// <summary>Barbacãs (drenos Ø 75 mm) a cada 2 m.</summary>
    public bool WeepHoles { get; set; } = true;
    public bool Guardrail { get; set; }
    /// <summary>Solo contido à esquerda do sentido da linha.</summary>
    public bool RetainedLeft { get; set; } = true;
    /// <summary>Reaterro do lado contido até o topo, com esta largura (m).</summary>
    public double BackfillWidth { get; set; } = 6.0;
    public bool FollowTerrain { get; set; } = true;
    public bool AdjustTerrain { get; set; } = true;
    public List<Vec2>? GroundLine { get; set; }

    public override string KindName => "Muro de arrimo";
    public override string DisplayCode => Type switch
    {
        TipoMuro.Gravidade => "MURO-GRAV",
        TipoMuro.Contrafortes => "MURO-CONTR",
        TipoMuro.Gabiao => "MURO-GAB",
        TipoMuro.TerraArmada => "MURO-TA",
        TipoMuro.CortinaAtirantada => "CORT-ATIR",
        _ => "MURO-FLEX",
    };
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

// ====================================================================== talude

public enum TipoTalude { Aterro, Corte }

public enum RevestimentoTalude { Grama, ConcretoProjetado, Enrocamento, SoloExposto }

/// <summary>Talude de corte ou aterro com bermas, canaletas (crista, pé e bermas), descidas d'água e revestimento.</summary>
public sealed class SlopeDefinition : MarkingDefinition, ITerrainAware
{
    public PathReference PathRef { get; set; } = new();
    public TipoTalude Type { get; set; } = TipoTalude.Aterro;
    /// <summary>Altura total (m).</summary>
    public double Height { get; set; } = 6.0;
    /// <summary>Inclinação horizontal : 1 vertical (aterro 1,5 : 1; corte 1 : 1).</summary>
    public double Ratio { get; set; } = 1.5;
    /// <summary>Uma berma a cada tantos metros de altura (0 = sem bermas).</summary>
    public double BermEvery { get; set; } = 8.0;
    public double BermWidth { get; set; } = 3.0;
    public RevestimentoTalude Lining { get; set; } = RevestimentoTalude.Grama;
    public bool CrestChannel { get; set; } = true;
    public bool ToeChannel { get; set; } = true;
    public bool BermChannels { get; set; } = true;
    /// <summary>Descidas d'água em degraus a cada tantos metros (0 = sem).</summary>
    public double DowndrainSpacing { get; set; } = 40;
    /// <summary>A linha desenhada é o pé do talude; o talude sobe para a esquerda do sentido da linha.</summary>
    /// <summary>Corte: largura da plataforma rebaixada à frente do pé (m), antes de concordar com o terreno.</summary>
    public double ToePlatform { get; set; } = 3.0;
    public bool UphillLeft { get; set; } = true;
    public bool FollowTerrain { get; set; } = true;
    public bool AdjustTerrain { get; set; } = true;
    public List<Vec2>? GroundLine { get; set; }

    public override string KindName => Type == TipoTalude.Corte ? "Talude de corte" : "Talude de aterro";
    public override string DisplayCode => Type == TipoTalude.Corte ? "TAL-CORTE" : "TAL-ATERRO";
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) => PathRef = path;
}

// ====================================================================== interseção em desnível

public enum TipoNoViario
{
    /// <summary>Diamante: viaduto da transversal e quatro rampas diagonais.</summary>
    Diamante,
    /// <summary>Diamante com rotatórias nos terminais das rampas ("halteres").</summary>
    DiamanteRotatorias,
    /// <summary>Trevo completo: quatro laços (loops) e quatro rampas externas.</summary>
    TrevoCompleto,
    /// <summary>Trevo parcial (parclo): laços em dois quadrantes e rampas diagonais nos outros.</summary>
    TrevoParcial,
    /// <summary>Trombeta: entroncamento em "T" com laço e rampa semidireta.</summary>
    Trombeta,
    /// <summary>Rotatória em desnível: anel elevado sobre a via principal, com dois viadutos.</summary>
    RotatoriaElevada,
}

/// <summary>Ramo de um nó viário (via do plugin) e suas ligações.</summary>
public sealed class RampRecord
{
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public Generators.PapelRamo Role { get; set; }
    public Automation.LigacaoRamo StartLink { get; set; }
    public Automation.LigacaoRamo EndLink { get; set; }
    public string? StartRoad { get; set; }
    public string? EndRoad { get; set; }
    public double LaneWidth { get; set; } = 4.0;
    public double Shoulder { get; set; } = 2.0;
    public double Taper { get; set; } = 60;
}

/// <summary>
/// Interseção em desnível (nó viário): via principal, via transversal em viaduto, rampas e laços com greide, aterros
/// com taludes, barreiras, iluminação e faixas pintadas – montados a partir do centro e das direções das vias.
/// </summary>
public sealed class InterchangeDefinition : MarkingDefinition, ITerrainAware
{
    public TipoNoViario Type { get; set; } = TipoNoViario.Diamante;
    public Vec2 Position { get; set; }
    public double Z { get; set; }
    /// <summary>Direção da via principal (graus, anti-horário a partir de X).</summary>
    public double AngleDeg { get; set; }
    /// <summary>Ângulo entre as vias (graus).</summary>
    public double CrossAngleDeg { get; set; } = 90;
    /// <summary>Via principal passa por baixo (a transversal sobe no viaduto).</summary>
    public bool MainBelow { get; set; } = true;
    public int MainLanes { get; set; } = 2;
    public double MainLaneWidth { get; set; } = 3.60;
    public double MainMedian { get; set; } = 3.0;
    public double MainShoulder { get; set; } = 2.50;
    public int CrossLanes { get; set; } = 1;
    public double CrossLaneWidth { get; set; } = 3.50;
    public double CrossShoulder { get; set; } = 1.50;
    public double RampWidth { get; set; } = 6.00;
    public double LoopRadius { get; set; } = 50;
    public double RampGrade { get; set; } = 0.05;
    /// <summary>Gabarito vertical livre (m) – DNIT: 5,50 m.</summary>
    public double Clearance { get; set; } = 5.50;
    public double DeckDepth { get; set; } = 1.80;
    public double MainLength { get; set; } = 700;
    public double CrossLength { get; set; } = 600;
    public double TerminalDistance { get; set; } = 130;
    public double RoundaboutRadius { get; set; } = 25;
    public double FillSlope { get; set; } = 2.0;
    public bool Barriers { get; set; } = true;
    public bool Markings { get; set; } = true;
    public bool Lighting { get; set; } = true;
    /// <summary>Superelevação dos laços (m/m) – DNIT: até 8 %.</summary>
    public double Superelevation { get; set; } = 0.06;
    /// <summary>Comprimento das espirais de transição (clotoides) dos ramos (m).</summary>
    public double SpiralLength { get; set; } = 40;
    /// <summary>Raio das curvas dos ramos diagonais e externos (m).</summary>
    public double RampRadius { get; set; } = 150;
    /// <summary>Comprimento da faixa de desaceleração/aceleração paralela (m).</summary>
    public double SpeedChangeLength { get; set; } = 90;
    /// <summary>Zebrados nos narizes de bifurcação e setas nos ramos.</summary>
    public bool GoreMarkings { get; set; } = true;
    /// <summary>Pórticos de sinalização indicativa antes das saídas.</summary>
    public bool Gantries { get; set; } = true;
    /// <summary>Torres de iluminação (30 m) no centro dos laços e rotatórias.</summary>
    public bool HighMasts { get; set; } = true;
    /// <summary>Defensas metálicas nos aterros altos dos ramos.</summary>
    public bool Guardrails { get; set; } = true;
    public bool FollowTerrain { get; set; } = true;
    public bool AdjustTerrain { get; set; } = true;
    public List<Vec2>? GroundLine { get; set; }

    // ---- nó montado sobre vias do plugin
    /// <summary>Grupo da via principal (a que passa por baixo ou por cima) – nulo = nó avulso (formato antigo).</summary>
    public string? MainRoad { get; set; }
    public string? CrossRoad { get; set; }
    /// <summary>Ramos criados (cada um é uma via do plugin).</summary>
    public List<RampRecord> Ramps { get; set; } = new();
    /// <summary>Obras e conexões criadas pelo nó (viadutos hospedados, rotatórias) – removidas ao refazer o nó.</summary>
    public List<string> WorkIds { get; set; } = new();
    /// <summary>Greide da via de cima antes do nó (restaurado ao refazer/remover).</summary>
    public RoadGrade? OverBaseGrade { get; set; }
    /// <summary>Rotatórias dos terminais / anel (centro, raio, cota absoluta) e trechos do anel em tabuleiro.</summary>
    public List<Vec2> RoundaboutCenters { get; set; } = new();
    public List<double> RoundaboutData { get; set; } = new();
    public List<Vec2> RingDecks { get; set; } = new();
    /// <summary>Cota absoluta da base usada pelos acabamentos do nó (m).</summary>
    public double BaseZ { get; set; }
    /// <summary>Preservar as vias e só (re)fazer ramos, viaduto e acabamentos.</summary>
    public bool Integrated => MainRoad != null && CrossRoad != null;

    public override string KindName => "Interseção em desnível";
    public override string DisplayCode => Type switch
    {
        TipoNoViario.DiamanteRotatorias => "NO-DIAM-ROT",
        TipoNoViario.TrevoCompleto => "NO-TREVO",
        TipoNoViario.TrevoParcial => "NO-PARCLO",
        TipoNoViario.Trombeta => "NO-TROMB",
        TipoNoViario.RotatoriaElevada => "NO-ROT-ELEV",
        _ => "NO-DIAM",
    };
    public override double? PointZ => Z;
    public override void Translate(Vec2 delta, double dz) { Position += delta; Z += dz; }
    public double MainWidth => 2 * (MainLanes * MainLaneWidth + MainShoulder + 0.6) + MainMedian;
    public double CrossWidth => 2 * (CrossLanes * CrossLaneWidth + CrossShoulder);
    /// <summary>Cota do greide da via que passa por cima (m).</summary>
    public double Height => Clearance + DeckDepth;
}
