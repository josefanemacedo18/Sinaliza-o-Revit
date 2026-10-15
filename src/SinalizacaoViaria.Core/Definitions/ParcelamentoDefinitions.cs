using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>
/// Categoria de área do loteamento (Lote, Institucional, Área verde ou outra criada pelo usuário): cor e hachura
/// em planta e no 3D, filtro e numeração.
/// </summary>
public sealed class CategoriaLote
{
    public string Nome { get; set; } = "Lote";
    /// <summary>Cor (#RRGGBB) da área em planta e do material no 3D.</summary>
    public string Cor { get; set; } = "#F2D7A6";
    public Hachura Hachura { get; set; }
    /// <summary>Área pública (entra no total de áreas públicas do quadro).</summary>
    public bool Publica { get; set; }
    /// <summary>
    /// Prefixo da numeração (ex.: "AV" → AV-1, AV-2 na quadra). Vazio = numerada como lote (1 a N na quadra, junto com os
    /// demais lotes sem prefixo).
    /// </summary>
    public string Prefixo { get; set; } = "";

    public CategoriaLote Clone() => (CategoriaLote)MemberwiseClone();

    public const string Lote = "Lote";
    public const string Institucional = "Institucional";
    public const string AreaVerde = "Área verde";

    /// <summary>Categorias padrão de um loteamento novo.</summary>
    public static List<CategoriaLote> Padrao() => new()
    {
        new() { Nome = Lote, Cor = "#F2D7A6", Hachura = Hachura.Nenhuma, Publica = false, Prefixo = "" },
        new() { Nome = Institucional, Cor = "#E8A3A3", Hachura = Hachura.Diagonal, Publica = true, Prefixo = "AI" },
        new() { Nome = AreaVerde, Cor = "#9BCB8A", Hachura = Hachura.Grama, Publica = true, Prefixo = "AV" },
    };
}

/// <summary>O que fazer com o comprimento de testada que sobra depois do último lote inteiro.</summary>
public enum ModoSobra
{
    /// <summary>A sobra é somada ao último lote da testada.</summary>
    IncorporarUltimo,
    /// <summary>A sobra é dividida igualmente entre os lotes de medida padrão da testada.</summary>
    Distribuir,
    /// <summary>
    /// A sobra vira o último lote da testada (lote normal) quando atinge a frente e a área mínimas do loteamento; menor que
    /// isso, é somada ao último lote.
    /// </summary>
    LoteSobra,
}

/// <summary>O que fazer com o miolo da quadra (a área atrás dos lotes que nenhuma testada alcança).</summary>
public enum ModoMiolo
{
    /// <summary>Os lotes vão até o fundo: cada um fica com o miolo atrás dele (até a divisa ou outros lotes).</summary>
    AosLotes,
    /// <summary>O miolo vira uma área própria, na categoria escolhida (ex.: área verde).</summary>
    AreaPropria,
}

/// <summary>Testada apontada na quadra: um ponto sobre o lado (o lado é reencontrado se o contorno mudar) e medidas próprias.</summary>
public sealed class TestadaLote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>Ponto sobre a testada (m) – o lado da quadra mais próximo dele é a testada.</summary>
    public Vec2 Ponto { get; set; }
    /// <summary>Frente dos lotes desta testada (m). Nulo = a da quadra.</summary>
    public double? Frente { get; set; }
    /// <summary>Profundidade dos lotes desta testada (m). Nulo = a da quadra.</summary>
    public double? Profundidade { get; set; }
    /// <summary>Nome da via em frente (memorial). Nulo = reconhecido pelo plugin.</summary>
    public string? Via { get; set; }

    public TestadaLote Clone() => (TestadaLote)MemberwiseClone();
}

/// <summary>Via com nome dado pelo usuário: o eixo (m) e os elementos selecionados.</summary>
public sealed class ViaNomeada
{
    public string Nome { get; set; } = "";
    public List<Vec2> Eixo { get; set; } = new();
    /// <summary>UniqueIds dos elementos selecionados (via do plugin, linha ou piso) – para renomear depois.</summary>
    public List<string> Elementos { get; set; } = new();

    public ViaNomeada Clone() => new() { Nome = Nome, Eixo = new List<Vec2>(Eixo), Elementos = new List<string>(Elementos) };
}

/// <summary>Como fica o lote de esquina entre duas testadas.</summary>
public enum ModoEsquina
{
    /// <summary>Pela prioridade das testadas (a primeira da lista fica com a esquina).</summary>
    Prioridade,
    /// <summary>O lote de esquina é da testada que chega à esquina (antes dela no contorno).</summary>
    TestadaAnterior,
    /// <summary>O lote de esquina é da testada que sai da esquina (depois dela no contorno).</summary>
    TestadaPosterior,
    /// <summary>Lote de esquina com medida própria: frente em cada uma das duas testadas.</summary>
    MedidaPropria,
}

/// <summary>Escolha feita para uma esquina da quadra (encontro de duas testadas), identificada pelo ponto da quina.</summary>
public sealed class EsquinaLote
{
    /// <summary>Quina (m) – a esquina da quadra mais próxima dele.</summary>
    public Vec2 Ponto { get; set; }
    public ModoEsquina Modo { get; set; }
    /// <summary>Medida própria: frente (m) na testada que chega à esquina.</summary>
    public double FrenteAnterior { get; set; } = 15;
    /// <summary>Medida própria: frente (m) na testada que sai da esquina.</summary>
    public double FrentePosterior { get; set; } = 15;
    /// <summary>Chanfro da quina nesta esquina: comprimento da face (m). Nulo = o da quadra; 0 = sem chanfro aqui.</summary>
    public double? Chanfro { get; set; }

    public EsquinaLote Clone() => (EsquinaLote)MemberwiseClone();
}

/// <summary>
/// Personalização de um lote (chave estável: testada + ordem ao longo dela, e a parte quando o lote é dividido): medidas,
/// divisão e categoria. A chave não muda com a renumeração.
/// </summary>
public sealed class LoteAjuste
{
    /// <summary>"testada:ordem" (lote), "testada:ordem.parte" (parte de lote dividido), "testada:s" (sobra), "esq:a-b" (lote de esquina com medida própria) ou "m:k" (miolo).</summary>
    public string Chave { get; set; } = "";
    public double? Frente { get; set; }
    public double? Profundidade { get; set; }
    /// <summary>Divide o lote em partes iguais ao longo da testada (1 = não divide).</summary>
    public int Partes { get; set; } = 1;
    public string? Categoria { get; set; }

    public LoteAjuste Clone() => (LoteAjuste)MemberwiseClone();
}

/// <summary>O que fazer com a sobra da gleba (fora das quadras e das vias).</summary>
public enum ModoRemanescente
{
    /// <summary>Vira área verde no desenho (piso, rótulo e área) e no quadro de áreas.</summary>
    AreaVerde,
    /// <summary>Entra só no quadro de áreas (como área verde), sem piso nem rótulo no desenho.</summary>
    SoNoQuadro,
}

/// <summary>
/// Área remanescente da gleba: parte livre de vias que não é quadra (fica em volta do sistema viário, com o furo dele) – contorno
/// e furos (m), gravados ao reconhecer as quadras.
/// </summary>
public sealed class AreaRemanescente
{
    public List<Vec2> Contorno { get; set; } = new();
    public List<List<Vec2>> Furos { get; set; } = new();

    [JsonIgnore] public Polygon2? Polygon => Contorno.Count >= 3 ? new Polygon2(Contorno, Furos) : null;

    public static AreaRemanescente From(Polygon2 p) => new() { Contorno = p.Outer.ToList(), Furos = p.Holes.Select(h => h.ToList()).ToList() };
}

/// <summary>
/// Loteamento: limite da gleba, categorias de área (cores, hachuras, numeração), mínimos de áreas públicas editáveis e dados
/// do memorial. Fica num elemento de dados do projeto; as quadras apontam para ele.
/// </summary>
public sealed class LoteamentoDefinition : MarkingDefinition
{
    public string Nome { get; set; } = "Loteamento";
    /// <summary>Limite da gleba (m, anti-horário).</summary>
    public List<Vec2> Gleba { get; set; } = new();
    /// <summary>Elementos do Revit que definiram a gleba (linha de propriedade ou linhas) – informativo.</summary>
    public List<string> GlebaElementos { get; set; } = new();
    public List<CategoriaLote> Categorias { get; set; } = CategoriaLote.Padrao();
    /// <summary>
    /// Percentuais mínimos (% da gleba) exigidos pela legislação aplicável – editáveis, sem valor padrão (a Lei Federal
    /// nº 6.766/1979 remete à lei municipal; conferir). Chave: nome da categoria, "Sistema viário" ou "Áreas públicas".
    /// </summary>
    public Dictionary<string, double> Minimos { get; set; } = new();
    /// <summary>
    /// Frente mínima de um lote (m). Padrão 5 m, informado pela usuária (norma ou lei de parcelamento – [a confirmar]); lotes
    /// menores são avisados e a sobra de testada menor que isso não vira lote.
    /// </summary>
    public double FrenteMinimaLote { get; set; } = 5;
    /// <summary>Área mínima de um lote (m²). Padrão 125 m², informado pela usuária ([a confirmar]).</summary>
    public double AreaMinimaLote { get; set; } = 125;
    /// <summary>Município/UF (memorial). Vazio = campo a preencher no Word.</summary>
    public string? Municipio { get; set; }
    /// <summary>Nomes das vias (memorial, confrontações e detalhamento), dados pelo usuário selecionando as vias.</summary>
    public List<ViaNomeada> Vias { get; set; } = new();
    /// <summary>Espessura (m) da subdivisão do Toposolid de cada lote – destaca a divisão no 3D.</summary>
    public double AlturaLote { get; set; } = 0.03;
    /// <summary>Elevação (m) da base quando não há Toposolid.</summary>
    public double Z { get; set; }
    /// <summary>Sobra da gleba fora das quadras e das vias (reconhecida com as quadras).</summary>
    public List<AreaRemanescente> Remanescentes { get; set; } = new();
    /// <summary>Como a sobra da gleba aparece: área verde desenhada ou só no quadro de áreas.</summary>
    public ModoRemanescente Remanescente { get; set; }

    [JsonIgnore] public IEnumerable<Polygon2> RemanescentePolygons => Remanescentes.Select(r => r.Polygon).Where(p => p is { IsValid: true }).Cast<Polygon2>();

    [JsonIgnore] public Polygon2? GlebaPolygon => Gleba.Count >= 3 ? new Polygon2(Gleba) : null;

    public CategoriaLote Categoria(string? nome) =>
        Categorias.FirstOrDefault(c => string.Equals(c.Nome, nome, StringComparison.OrdinalIgnoreCase))
        ?? Categorias.FirstOrDefault()
        ?? CategoriaLote.Padrao()[0];

    /// <summary>Nome da via mais próxima de <paramref name="p"/> (até <paramref name="maxDist"/> m do eixo); nulo sem via nomeada perto.</summary>
    public string? ViaEm(Vec2 p, double maxDist = 60)
    {
        string? best = null;
        var bd = maxDist;
        foreach (var v in Vias)
        {
            if (v.Eixo.Count < 2 || string.IsNullOrWhiteSpace(v.Nome)) continue;
            var d = Math.Abs(new Polyline2(v.Eixo).Project(p).Signed);
            if (d < bd) { bd = d; best = v.Nome; }
        }
        return best;
    }

    public override void Translate(Vec2 delta, double dz)
    {
        Gleba = Gleba.Select(p => p + delta).ToList();
        foreach (var v in Vias) v.Eixo = v.Eixo.Select(p => p + delta).ToList();
        foreach (var r in Remanescentes)
        {
            r.Contorno = r.Contorno.Select(p => p + delta).ToList();
            r.Furos = r.Furos.Select(h => h.Select(p => p + delta).ToList()).ToList();
        }
        Z += dz;
    }

    public override string KindName => "Loteamento";
    public override string DisplayCode => "LOTEAMENTO";
}

/// <summary>
/// Quadra do loteamento: contorno (alinhamento predial), testadas apontadas, medida padrão dos lotes, sobra, numeração e as
/// personalizações por lote. Os lotes são refeitos a partir disso a cada mudança.
/// </summary>
public sealed class QuadraDefinition : MarkingDefinition
{
    public string LoteamentoId { get; set; } = "";
    public string Nome { get; set; } = "A";
    /// <summary>Contorno da quadra (m, anti-horário).</summary>
    public List<Vec2> Contorno { get; set; } = new();
    /// <summary>Testadas em ordem de prioridade: a primeira fica com o lote de esquina.</summary>
    public List<TestadaLote> Testadas { get; set; } = new();
    /// <summary>Frente padrão dos lotes (m).</summary>
    public double Frente { get; set; } = 12;
    /// <summary>Profundidade padrão dos lotes (m).</summary>
    public double Profundidade { get; set; } = 30;
    /// <summary>Quadra mais rasa que 2 × profundidade entre testadas opostas: os fundos ficam no meio da quadra.</summary>
    public bool FundoNoMeio { get; set; } = true;
    /// <summary>Sobra de testada a partir da qual vira um lote a mais (m). 0 = a frente mínima do loteamento.</summary>
    public double FrenteMinima { get; set; }
    /// <summary>Miolo da quadra: para os lotes (fundos até a divisa) ou área própria.</summary>
    public ModoMiolo Miolo { get; set; }
    /// <summary>Categoria do miolo quando ele é área própria.</summary>
    public string MioloCategoria { get; set; } = CategoriaLote.AreaVerde;
    /// <summary>Lotes unidos: cada grupo (chaves de lotes vizinhos) vira um lote só, com a chave do primeiro.</summary>
    public List<List<string>> Unioes { get; set; } = new();
    public ModoSobra Sobra { get; set; } = ModoSobra.LoteSobra;

    /// <summary>Grupo de união que contém a chave (nulo = lote não unido).</summary>
    public List<string>? UniaoDe(string chave) => Unioes.FirstOrDefault(u => u.Contains(chave));
    /// <summary>Numeração no sentido horário (falso = anti-horário).</summary>
    public bool SentidoHorario { get; set; } = true;
    /// <summary>Chave do lote que recebe o número 1. Nulo = lote de esquina mais a noroeste.</summary>
    public string? LoteInicial { get; set; }
    /// <summary>Categoria dos lotes sem ajuste.</summary>
    public string CategoriaPadrao { get; set; } = CategoriaLote.Lote;
    /// <summary>Quadra inteira numa área só (praça, área verde, institucional): não é dividida.</summary>
    public bool Inteira { get; set; }
    public List<LoteAjuste> Ajustes { get; set; } = new();
    /// <summary>Escolha do lote de esquina em cada esquina entre testadas (sem escolha = prioridade das testadas).</summary>
    public List<EsquinaLote> Esquinas { get; set; } = new();
    /// <summary>
    /// Chanfra as quinas da quadra voltadas para os cruzamentos (entre duas testadas): a quina sai do lote de esquina (cotada e
    /// descontada da área) e a calçada da interseção ocupa o chanfro.
    /// </summary>
    public bool Chanfro { get; set; }
    /// <summary>Comprimento da face do chanfro (m) – a hipotenusa, recuando o mesmo nas duas testadas; o padrão é a largura da calçada.</summary>
    public double ChanfroFace { get; set; }
    /// <summary>Elevação (m) da base quando não há Toposolid.</summary>
    public double Z { get; set; }

    [JsonIgnore] public Polygon2? Polygon => Contorno.Count >= 3 ? new Polygon2(Contorno) : null;

    public LoteAjuste? Ajuste(string chave) => Ajustes.FirstOrDefault(a => a.Chave == chave);

    /// <summary>Ajuste do lote (criado se não existir).</summary>
    public LoteAjuste AjusteOuNovo(string chave)
    {
        var a = Ajuste(chave);
        if (a != null) return a;
        a = new LoteAjuste { Chave = chave };
        Ajustes.Add(a);
        return a;
    }

    public override void Translate(Vec2 delta, double dz)
    {
        Contorno = Contorno.Select(p => p + delta).ToList();
        foreach (var t in Testadas) t.Ponto += delta;
        foreach (var e in Esquinas) e.Ponto += delta;
        Z += dz;
    }

    public override string KindName => "Quadra";
    public override string DisplayCode => "QUADRA-" + Nome;
}

/// <summary>Detalhamento dos lotes na vista: número, área, medidas dos lados, categoria e nome da quadra (cada item opcional).</summary>
public sealed class LoteamentoDetalheDefinition : MarkingDefinition, IAnnotationDefinition, IProjectWideAnnotation
{
    public string LoteamentoId { get; set; } = "";
    public bool Numero { get; set; } = true;
    public bool Area { get; set; } = true;
    public bool Cotas { get; set; } = true;
    public bool Categoria { get; set; } = true;
    public bool Quadra { get; set; } = true;
    /// <summary>Nome das vias ao longo do eixo.</summary>
    public bool Vias { get; set; } = true;
    /// <summary>Contorno da gleba e divisas dos lotes como linhas de detalhe (vistas sem o Toposolid, masterplan).</summary>
    public bool Divisas { get; set; }
    /// <summary>Cores das categorias como regiões preenchidas (masterplan 2D sem filtros).</summary>
    public bool Cores { get; set; }
    public double TextMm { get; set; } = 2.0;

    public string? TargetId => LoteamentoId;
    public override string KindName => "Detalhamento de lotes";
    public override string DisplayCode => "DET-LOTES";
}

/// <summary>Tipo de quadro do loteamento.</summary>
public enum TipoQuadroLoteamento
{
    /// <summary>Quadro de áreas: gleba, lotes, áreas institucionais, verdes e sistema viário, com m² e %.</summary>
    Areas,
    /// <summary>Quadro por quadra: número de lotes e áreas por categoria de cada quadra.</summary>
    Quadras,
    /// <summary>Quadro de lotes: um a um – quadra, lote, categoria, rua da frente, medidas dos lados, área e observações.</summary>
    Lotes,
}

/// <summary>Quadro do loteamento (independente de vista: vai para uma Legenda própria e para qualquer folha).</summary>
public sealed class QuadroLoteamentoDefinition : MarkingDefinition, IProjectWideAnnotation, IPlacedAnnotation
{
    public string LoteamentoId { get; set; } = "";
    /// <summary>Conteúdo do quadro (o nome "tipo" é reservado ao discriminador do JSON).</summary>
    public TipoQuadroLoteamento Conteudo { get; set; }
    /// <summary>Canto superior esquerdo (m).</summary>
    public Vec2 Position { get; set; }
    public string? Titulo { get; set; }
    public double RowMm { get; set; } = 6;
    public double TextMm { get; set; } = 2.0;
    /// <summary>Linhas por coluna no quadro de lotes (o quadro continua ao lado).</summary>
    public int LinhasPorColuna { get; set; } = 40;
    /// <summary>Quadro de lotes de uma quadra só (nome); nulo = todas as quadras.</summary>
    public string? Quadra { get; set; }

    public string? TargetId => LoteamentoId;
    public Vec2? DrawnAnchor { get; set; }
    public override void Translate(Vec2 delta, double dz) => Position += delta;

    public string TituloPadrao => Conteudo switch
    {
        TipoQuadroLoteamento.Quadras => "QUADRO DE QUADRAS",
        TipoQuadroLoteamento.Lotes => string.IsNullOrWhiteSpace(Quadra) ? "QUADRO DE LOTES" : $"QUADRO DE LOTES – QUADRA {Quadra}",
        _ => "QUADRO DE ÁREAS DO LOTEAMENTO",
    };

    public override string KindName => Conteudo switch
    {
        TipoQuadroLoteamento.Quadras => "Quadro de quadras",
        TipoQuadroLoteamento.Lotes => string.IsNullOrWhiteSpace(Quadra) ? "Quadro de lotes" : $"Quadro de lotes – Quadra {Quadra}",
        _ => "Quadro de áreas",
    };
    public override string DisplayCode => "QUADRO-LOTEAMENTO";
}
