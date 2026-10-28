using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Definitions;

public enum TipoCorpoDagua
{
    /// <summary>Rio (por eixo, no sentido da correnteza).</summary>
    Rio,
    /// <summary>Córrego (por eixo, no sentido da correnteza).</summary>
    Corrego,
    /// <summary>Lago (por contorno da margem no nível d'água).</summary>
    Lago,
    /// <summary>Lagoa (por contorno da margem no nível d'água).</summary>
    Lagoa,
}

/// <summary>
/// Corpo d'água parametrizado: rio e córrego por eixo (primeiro ponto a montante) com o fundo por estaca, a largura do fundo, a
/// lâmina d'água e o talude das margens; lago e lagoa pelo contorno da margem no nível d'água, com a cota do fundo. O leito é
/// escavado no Toposolid pela Terraplenagem (com registro), a superfície de água fica no modelo e a faixa de proteção é a
/// informada pelo usuário, com a lei que ele cita. As cotas são absolutas (o caminho fica na cota 0).
/// </summary>
public sealed class CorpoDaguaDefinition : MarkingDefinition, ITerrainAware
{
    public PathReference PathRef { get; set; } = new();
    /// <summary>Rio, córrego, lago ou lagoa (não "Tipo": o nome é o do discriminador do JSON das marcas).</summary>
    public TipoCorpoDagua Corpo { get; set; } = TipoCorpoDagua.Corrego;
    public string Nome { get; set; } = "";
    /// <summary>Rio/córrego: largura do fundo do leito (m).</summary>
    public double LarguraFundo { get; set; } = 2.0;
    /// <summary>Rio/córrego: lâmina d'água sobre o fundo (m).</summary>
    public double Lamina { get; set; } = 0.8;
    /// <summary>Rio/córrego sem fundo informado: profundidade do fundo abaixo do terreno natural no eixo (m).</summary>
    public double Profundidade { get; set; } = 1.5;
    /// <summary>Rio/córrego: fundo por estaca (estaca, cota absoluta); vazio = o terreno natural no eixo menos a profundidade, sem subir para jusante.</summary>
    public List<Vec2> Fundo { get; set; } = new();
    /// <summary>Lago/lagoa: cota do fundo (m).</summary>
    public double CotaFundo { get; set; }
    /// <summary>Lago/lagoa: nível d'água (m) – o contorno é a margem nessa cota.</summary>
    public double NivelAgua { get; set; }
    /// <summary>Talude das margens (H : 1 V) – padrão do plugin; o do projeto (geotecnia) quando houver.</summary>
    public double TaludeMargem { get; set; } = 2.0;
    /// <summary>Faixa de proteção em cada margem, a partir da borda da água (m); 0 = não informada.</summary>
    public double FaixaProtecao { get; set; }
    /// <summary>Lei e artigo da faixa de proteção, como o usuário informou (o plugin não fixa a largura legal).</summary>
    public string FonteFaixa { get; set; } = "";
    public bool FollowTerrain { get; set; } = true;
    public bool AdjustTerrain { get; set; } = true;
    public List<Vec2>? GroundLine { get; set; }

    public bool EhRio => Corpo is TipoCorpoDagua.Rio or TipoCorpoDagua.Corrego;

    public override string KindName => Corpo switch
    {
        TipoCorpoDagua.Rio => "Rio",
        TipoCorpoDagua.Lago => "Lago",
        TipoCorpoDagua.Lagoa => "Lagoa",
        _ => "Córrego",
    };
    public override string DisplayCode => EhRio ? "AGUA-RIO" : "AGUA-LAGO";
    public override PathReference? Path => PathRef;
    public override void SetPath(PathReference path) { if (!EhRio) path.Closed = true; PathRef = path; }
}
