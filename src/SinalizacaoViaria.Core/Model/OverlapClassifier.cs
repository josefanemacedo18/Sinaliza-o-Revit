using System.Globalization;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Model;

/// <summary>Classe de um par de pisos que se sobrepõem (RT-1.3) – as cinco classes pedidas pelo usuário.</summary>
public enum ClasseSobreposicao
{
    /// <summary>Camada construtiva intencional: um piso apoiado sobre o outro, sem volume em comum (pintura sobre o pavimento).</summary>
    CamadaIntencional,
    /// <summary>Interseção legítima: peças de duas vias que se cruzam de fato, sem nó que una os pisos.</summary>
    InterseccaoLegitima,
    /// <summary>Duplicação acidental: dois pisos do mesmo tipo ocupando o mesmo lugar.</summary>
    Duplicacao,
    /// <summary>Conflito real: volume em comum entre pisos que não deveriam se cruzar.</summary>
    Conflito,
    /// <summary>Os dados não bastam para decidir: piso que não é do plugin, volume não conferido, piso inteiro dentro de outro.</summary>
    Inconclusivo,
}

/// <summary>
/// O que se mediu de um par de pisos.
/// <list type="bullet">
/// <item><c>CodigoA/B</c>: código da marca (camada), ou o nome do tipo de piso quando o piso não é do plugin.</item>
/// <item><c>MarcaA/B</c>: Id da marca (nulo = piso que não é do plugin); <c>GrupoA/B</c>: grupo da marca (a via).</item>
/// <item><c>Volume</c>: volume em comum (m³; nulo = não conferido); <c>VolumeA/B</c>: volume de cada piso (m³; nulo = não medido).</item>
/// <item><c>PinturaA/B</c>: a peça é pintura (sinalização horizontal).</item>
/// <item><c>Empilhados</c>: um piso apoiado no outro (base de um na face de cima do outro); nulo = não medido.</item>
/// <item><c>ViasComNo</c>: os pisos são de duas vias diferentes, e elas têm (true) ou não (false) nó em comum – interseção ou
/// rotatória; nulo = não são de duas vias, ou não se sabe.</item>
/// </list>
/// </summary>
public sealed record FatosSobreposicao(string CodigoA, string CodigoB, string? MarcaA, string? MarcaB, string? GrupoA, string? GrupoB,
    double? Volume, double? VolumeA = null, double? VolumeB = null, bool PinturaA = false, bool PinturaB = false,
    bool? Empilhados = null, bool? ViasComNo = null)
{
    public bool MesmaMarca => MarcaA != null && MarcaA == MarcaB;
    public bool DoPlugin => MarcaA != null && MarcaB != null;
}

/// <summary>Classe, o motivo (o que foi medido) e a ação recomendada – o plugin não corrige nem apaga nada sozinho.</summary>
public sealed record Classificacao(ClasseSobreposicao Classe, string Motivo, string Acao);

/// <summary>
/// Classificação dos pares de pisos sobrepostos (RT-1.3) pelo que foi medido, não pela intenção presumida. Em ordem:
/// <list type="number">
/// <item>volume não conferido → <see cref="ClasseSobreposicao.Inconclusivo"/>;</item>
/// <item>sem volume em comum (até <see cref="SemVolume"/>, o limite do Autoteste): empilhados →
/// <see cref="ClasseSobreposicao.CamadaIntencional"/>; senão inconclusivo (o aviso foi de passagem ou os pisos só se tocam);</item>
/// <item>mesmo código, um dentro do outro (≥ <see cref="Contido"/>) e volumes parecidos → <see cref="ClasseSobreposicao.Duplicacao"/>
/// (vale também para pisos que não são do plugin, pelo nome do tipo);</item>
/// <item>piso que não é do plugin → inconclusivo (a intenção não é conhecida);</item>
/// <item>peças de duas vias que se cruzam sem nó entre elas (pavimento, calçada, meio-fio, pintura) →
/// <see cref="ClasseSobreposicao.InterseccaoLegitima"/>: o cruzamento existe de fato, falta o nó;</item>
/// <item>pavimentos (PAV-…) de vias diferentes com nó em comum → <see cref="ClasseSobreposicao.Conflito"/> (o recorte do nó não
/// tirou o cruzamento); sem saber se há nó → inconclusivo;</item>
/// <item>um piso inteiro dentro de outro de outro código: pintura → conflito (escondida); outro piso → inconclusivo (pode ser
/// camada de propósito – o plugin não gera pisos assim);</item>
/// <item>o resto → <see cref="ClasseSobreposicao.Conflito"/>.</item>
/// </list>
/// A classe não esconde o par: todos continuam contados, com volume, no relatório.
/// </summary>
public static class OverlapClassifier
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Volume em comum até o qual o par conta como "sem volume" (m³): 1e-4 pé³, o mesmo limite do Autoteste.</summary>
    public const double SemVolume = 1e-4 * 0.0283168;

    /// <summary>Fração do menor piso dentro do outro para contar como "um dentro do outro".</summary>
    public const double Contido = 0.95;

    /// <summary>Pavimento de via (pista) pelo código: PAV-ASF, PAV-BLQ, PAV-CON, PAV-TER.</summary>
    public static bool Pavimento(string codigo) => codigo.StartsWith("PAV-", StringComparison.Ordinal);

    public static Classificacao Classificar(FatosSobreposicao f)
    {
        if (f.Volume is not double v)
            return new(ClasseSobreposicao.Inconclusivo, "o volume em comum não pôde ser conferido", "conferir o par no modelo (revisão humana)");
        if (v <= SemVolume)
            return f.Empilhados == true
                ? new(ClasseSobreposicao.CamadaIntencional, "um piso apoiado sobre o outro, sem volume em comum", "nenhuma")
                : new(ClasseSobreposicao.Inconclusivo, "sem volume em comum no modelo (aviso de passagem ou pisos que só se tocam)",
                    "nenhuma, se o aviso não voltar no modelo final");
        var menor = f.VolumeA is > 0 && f.VolumeB is > 0 ? Math.Min(f.VolumeA.Value, f.VolumeB.Value) : (double?)null;
        var contido = menor is double m ? v / m : (double?)null;
        var parecidos = menor is double mn && Math.Max(f.VolumeA!.Value, f.VolumeB!.Value) <= mn / Contido;
        if (f.CodigoA == f.CodigoB && contido >= Contido && parecidos)
        {
            var quanto = (Math.Min(1, contido!.Value) * 100).ToString("0", Pt);
            return f.MesmaMarca
                ? new(ClasseSobreposicao.Duplicacao, $"a mesma marca gerou duas peças {f.CodigoA} no mesmo lugar ({quanto} % em comum)",
                    "atualizar a marca; se voltar, é defeito do gerador – reproduzir no núcleo pelo arquivo de dados")
                : new(ClasseSobreposicao.Duplicacao, $"dois pisos {f.CodigoA} no mesmo lugar ({quanto} % em comum)",
                    "conferir e apagar um dos dois (o plugin não apaga sozinho)");
        }
        if (!f.DoPlugin)
            return new(ClasseSobreposicao.Inconclusivo, "um dos pisos não é do plugin – a intenção não é conhecida", "revisão humana");
        if (f.ViasComNo == false)
            return new(ClasseSobreposicao.InterseccaoLegitima, $"peças de duas vias que se cruzam sem nó entre elas ({f.CodigoA} × {f.CodigoB})",
                "criar a interseção ou a rotatória entre as duas vias – o nó une os pisos");
        if (Pavimento(f.CodigoA) && Pavimento(f.CodigoB) && !f.MesmaMarca && (f.GrupoA == null || f.GrupoA != f.GrupoB))
            return f.ViasComNo == true
                ? new(ClasseSobreposicao.Conflito, "as vias têm nó em comum, mas os pavimentos ainda se cruzam",
                    "atualizar o nó; se voltar, reproduzir no núcleo pelo arquivo de dados")
                : new(ClasseSobreposicao.Inconclusivo, "pavimentos de duas vias que se cruzam (não se sabe se há nó)", "conferir se há interseção entre as vias");
        if (contido >= Contido && f.CodigoA != f.CodigoB)
        {
            var pinturaDentro = (f.PinturaA && f.VolumeA <= f.VolumeB) || (f.PinturaB && f.VolumeB <= f.VolumeA);
            return pinturaDentro
                ? new(ClasseSobreposicao.Conflito, "pintura inteira dentro de outro piso (escondida)", "conferir a cota e a espessura da pintura e do piso de baixo")
                : new(ClasseSobreposicao.Inconclusivo, "um piso inteiro dentro de outro de outro tipo – pode ser camada de propósito",
                    "revisão humana (o plugin não gera pisos assim)");
        }
        return f.MesmaMarca
            ? new(ClasseSobreposicao.Conflito, "peças da mesma marca se invadem", "reproduzir no núcleo pelo arquivo de dados e corrigir o contorno")
            : new(ClasseSobreposicao.Conflito, "pisos de marcas diferentes se invadem", "reproduzir no núcleo pelo arquivo de dados e corrigir a causa");
    }

    public static string Nome(ClasseSobreposicao c) => c switch
    {
        ClasseSobreposicao.CamadaIntencional => "camada intencional",
        ClasseSobreposicao.InterseccaoLegitima => "interseção legítima",
        ClasseSobreposicao.Duplicacao => "duplicação acidental",
        ClasseSobreposicao.Conflito => "conflito real",
        _ => "inconclusivo",
    };

    /// <summary>Contagem e volume por classe, da mais grave para a menos grave ("Por classe: conflito real 12 (0,031 m³); …").</summary>
    public static string? Resumo(IEnumerable<(Classificacao C, double VolumeM3)> pares)
    {
        var lista = pares.ToList();
        if (lista.Count == 0) return null;
        var ordem = new[] { ClasseSobreposicao.Conflito, ClasseSobreposicao.Duplicacao, ClasseSobreposicao.InterseccaoLegitima,
            ClasseSobreposicao.Inconclusivo, ClasseSobreposicao.CamadaIntencional };
        var partes = ordem.Select(c => (c, l: lista.Where(p => p.C.Classe == c).ToList())).Where(x => x.l.Count > 0)
            .Select(x => string.Format(Pt, "{0} {1} ({2:0.####} m³)", Nome(x.c), x.l.Count, x.l.Sum(p => p.VolumeM3)));
        return "Por classe: " + string.Join("; ", partes) + ".";
    }

    /// <summary>
    /// Pares de pisos sobrepostos das peças geradas no núcleo (mesmo critério do Autoteste, <see cref="PieceOverlaps"/>) já
    /// classificados. O volume de cada piso é área × espessura; <paramref name="viasComNo"/> diz se os grupos de duas peças são
    /// vias com nó em comum (ver <see cref="NosEntreVias"/>).
    /// </summary>
    public static List<(PieceOverlap Par, Classificacao C)> DasPecas(IReadOnlyList<PecaPiso> pecas, Func<string, string, bool?>? viasComNo = null)
    {
        var pieces = pecas.Select(p => new MarkingPiece(p.Shape, MarkingColor.Branca) { Thickness = p.Thickness, Elevation = p.Elevation }).ToList();
        var res = new List<(PieceOverlap, Classificacao)>();
        foreach (var o in PieceOverlaps.Find(pieces, pecas.Select(p => p.BaseZ).ToList()))
        {
            var (a, b) = (pecas[o.A], pecas[o.B]);
            bool? no = viasComNo != null && a.Grupo != null && b.Grupo != null ? viasComNo(a.Grupo, b.Grupo) : null;
            var f = new FatosSobreposicao(a.Codigo, b.Codigo, a.Marca, b.Marca, a.Grupo, b.Grupo, o.Volume,
                a.Shape.Area * Math.Max(0.001, a.Thickness), b.Shape.Area * Math.Max(0.001, b.Thickness), a.Pintura, b.Pintura, ViasComNo: no);
            res.Add((o, Classificar(f)));
        }
        return res;
    }

    /// <summary>
    /// "Os grupos <c>a</c> e <c>b</c> são duas vias com nó em comum?", pelas definições. Grupo de via = grupo com pavimento
    /// (<see cref="Definitions.RoadPavementDefinition"/>); nó = interseção (<c>RoadIds</c>) ou rotatória (<c>Legs[].RoadId</c>)
    /// que liga os pavimentos dos dois grupos. Nulo quando os grupos são o mesmo ou um deles não é de via (a peça de um nó, por
    /// exemplo).
    /// </summary>
    public static Func<string, string, bool?> NosEntreVias(IEnumerable<Definitions.MarkingDefinition> defs)
    {
        var lista = defs.ToList();
        var grupoDoPavimento = new Dictionary<string, string>();
        foreach (var p in lista.OfType<Definitions.RoadPavementDefinition>())
            if (p.GroupId != null) grupoDoPavimento[p.Id] = p.GroupId;
        var gruposDeVia = grupoDoPavimento.Values.ToHashSet();
        var ligados = new HashSet<(string, string)>();
        void Ligar(IEnumerable<string?> pavimentos)
        {
            var gs = pavimentos.Where(id => id != null && grupoDoPavimento.ContainsKey(id)).Select(id => grupoDoPavimento[id!]).Distinct().ToList();
            foreach (var a in gs)
                foreach (var b in gs)
                    if (a != b) ligados.Add((a, b));
        }
        foreach (var d in lista)
            if (d is Definitions.IntersectionDefinition i) Ligar(i.RoadIds);
            else if (d is Definitions.RoundaboutDefinition r) Ligar(r.Legs.Select(l => l.RoadId));
        return (a, b) => a == b || !gruposDeVia.Contains(a) || !gruposDeVia.Contains(b) ? null : ligados.Contains((a, b));
    }
}

/// <summary>Peça plana que vira piso, com o que a classificação precisa: cota da marca, código, marca, grupo e se é pintura.</summary>
public sealed record PecaPiso(Polygon2 Shape, double Thickness, double Elevation, double BaseZ, string Codigo, string? Marca, string? Grupo, bool Pintura);
