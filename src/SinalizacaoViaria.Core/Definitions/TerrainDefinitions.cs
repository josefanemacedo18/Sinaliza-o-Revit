using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>Superfície lida pelo tema do terreno.</summary>
public enum SuperficieTema
{
    /// <summary>Terreno natural (o guardado pela terraplenagem; sem terraplenagem, o atual).</summary>
    Natural,
    /// <summary>Terreno atual do Toposolid (com a terraplenagem).</summary>
    Atual,
}

/// <summary>
/// Tema do terreno numa vista (2D): regiões coloridas por classe de elevação, declividade, orientação ou diferença entre o
/// terreno atual e o natural, com a legenda (cor, intervalo, área e fração, em barras). Refeito a partir do terreno a cada
/// regeneração – a amostragem é na malha de <see cref="Passo"/> m dentro do retângulo <see cref="Min"/>–<see cref="Max"/>.
/// </summary>
public sealed class TemaTerrenoDefinition : MarkingDefinition, IAnnotationDefinition, IPlacedAnnotation
{
    public TemaTerreno Tema { get; set; } = TemaTerreno.Declividade;
    public SuperficieTema Superficie { get; set; } = SuperficieTema.Natural;
    public Vec2 Min { get; set; }
    public Vec2 Max { get; set; }
    /// <summary>Lado da célula da malha (m) – aumentado quando a área pede mais de 40 mil células.</summary>
    public double Passo { get; set; } = 2.0;
    /// <summary>Elevação: número de intervalos iguais (padrão do plugin).</summary>
    public int Classes { get; set; } = 8;
    /// <summary>Limites das classes informados (crescentes); vazio = as classes padrão do plugin.</summary>
    public List<double> Limites { get; set; } = new();
    /// <summary>Canto superior esquerdo da legenda (m); nulo = à direita do retângulo.</summary>
    public Vec2? Legenda { get; set; }
    public double TextoMm { get; set; } = 2.5;
    public string Titulo { get; set; } = "";

    public string? TargetId => null;
    public Vec2? DrawnAnchor { get; set; }
    public override void Translate(Vec2 delta, double dz)
    {
        Min += delta;
        Max += delta;
        if (Legenda is { } l) Legenda = l + delta;
    }
    public override string KindName => "Tema do terreno";
    public override string DisplayCode => Tema switch
    {
        TemaTerreno.Elevacao => "TEMA-ELEV",
        TemaTerreno.Orientacao => "TEMA-ORIENT",
        TemaTerreno.Diferenca => "TEMA-DIF",
        _ => "TEMA-DECLIV",
    };
}

/// <summary>Como a cota da plataforma é definida.</summary>
public enum CotaPlataforma
{
    /// <summary>A cota informada.</summary>
    Informada,
    /// <summary>A cota média do terreno natural dentro do contorno.</summary>
    Media,
    /// <summary>A cota em que o corte iguala o aterro × homogeneização dentro do contorno (sem os taludes).</summary>
    Equilibrio,
}

/// <summary>
/// Plataforma (área nivelada): contorno fechado numa cota, com taludes de corte e aterro até o terreno – entra na
/// Terraplenagem como as plataformas das obras e no registro dela com o próprio volume. O caminho é o contorno, na cota da
/// plataforma; no modelo fica a borda (faixa fina) sobre ela.
/// </summary>
public sealed class PlataformaDefinition : MarkingDefinition, ITerrainAware
{
    public PathReference PathRef { get; set; } = new() { Closed = true };
    public CotaPlataforma Modo { get; set; } = CotaPlataforma.Equilibrio;
    /// <summary>Cota da plataforma (m, absoluta – a mesma do caminho).</summary>
    public double Cota { get; set; }
    public double CutSlope { get; set; } = 1.0;
    public double FillSlope { get; set; } = 1.5;
    /// <summary>Fator de homogeneização da cota de equilíbrio (aterro compactado / corte).</summary>
    public double Homogeneizacao { get; set; } = 1.0;
    /// <summary>Taludes até o terreno (falso = só a área nivelada, o terreno de fora fica como está).</summary>
    public bool Taludes { get; set; } = true;
    public bool FollowTerrain { get; set; }
    public bool AdjustTerrain { get; set; } = true;
    public List<Vec2>? GroundLine { get; set; }

    public override string KindName => "Plataforma";
    public override string DisplayCode => "PLATAFORMA";
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) { path.Closed = true; PathRef = path; }
}
