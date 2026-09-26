using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Quantities;

/// <summary>Subcategorias de mobiliário e elementos urbanos (detalhamento do quantitativo e do orçamento).</summary>
public enum CategoriaUrbana
{
    Assentos,
    Iluminacao,
    Vegetacao,
    Residuos,
    AbrigosTransporte,
    Ciclismo,
    Seguranca,
    Comunicacao,
    Infraestrutura,
    Lazer,
    Acessibilidade,
    Outros,
}

/// <summary>Rótulos e classificação automática das subcategorias urbanas.</summary>
public static class UrbanCategories
{
    public static IReadOnlyList<CategoriaUrbana> All { get; } = Enum.GetValues<CategoriaUrbana>();

    public static string Label(CategoriaUrbana c) => c switch
    {
        CategoriaUrbana.Assentos => "Bancos, mesas e assentos",
        CategoriaUrbana.Iluminacao => "Iluminação pública",
        CategoriaUrbana.Vegetacao => "Arborização e paisagismo",
        CategoriaUrbana.Residuos => "Lixeiras e coleta de resíduos",
        CategoriaUrbana.AbrigosTransporte => "Abrigos e pontos de transporte",
        CategoriaUrbana.Ciclismo => "Paraciclos e bicicletários",
        CategoriaUrbana.Seguranca => "Proteção e segurança (frades, gradis, balizadores)",
        CategoriaUrbana.Comunicacao => "Comunicação e placas de logradouro",
        CategoriaUrbana.Infraestrutura => "Infraestrutura (hidrantes, bocas de lobo, caixas)",
        CategoriaUrbana.Lazer => "Lazer, esporte e parklets",
        CategoriaUrbana.Acessibilidade => "Acessibilidade",
        CategoriaUrbana.Outros => "Outros elementos urbanos",
        _ => c.ToString(),
    };

    /// <summary>Classificação a partir do rótulo gravado no parâmetro (tolerante a rótulos antigos/editados).</summary>
    public static CategoriaUrbana Parse(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return CategoriaUrbana.Outros;
        foreach (var c in All)
            if (string.Equals(Label(c), label.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(c.ToString(), label.Trim(), StringComparison.OrdinalIgnoreCase))
                return c;
        return Guess(label);
    }

    /// <summary>Subcategoria dos elementos urbanos do próprio plugin.</summary>
    public static CategoriaUrbana Of(FormaMobiliario f) => f switch
    {
        FormaMobiliario.Banco => CategoriaUrbana.Assentos,
        FormaMobiliario.Lixeira => CategoriaUrbana.Residuos,
        FormaMobiliario.PosteIluminacao or FormaMobiliario.PostePedestre => CategoriaUrbana.Iluminacao,
        FormaMobiliario.Arvore or FormaMobiliario.Palmeira or FormaMobiliario.Arbusto or FormaMobiliario.Floreira => CategoriaUrbana.Vegetacao,
        FormaMobiliario.AbrigoOnibus => CategoriaUrbana.AbrigosTransporte,
        FormaMobiliario.Paraciclo => CategoriaUrbana.Ciclismo,
        FormaMobiliario.Hidrante => CategoriaUrbana.Infraestrutura,
        FormaMobiliario.PlacaLogradouro => CategoriaUrbana.Comunicacao,
        FormaMobiliario.Semaforo => CategoriaUrbana.Seguranca,
        _ => CategoriaUrbana.Outros,
    };

    /// <summary>Sugestão de subcategoria pelo nome da família/tipo ou pela categoria do Revit.</summary>
    public static CategoriaUrbana Guess(string? text)
    {
        var t = (text ?? "").ToLowerInvariant();
        bool Any(params string[] k) => k.Any(t.Contains);
        if (Any("banco", "bench", "mesa", "table", "cadeira", "chair", "assento", "seat", "furniture", "mobiliário")) return CategoriaUrbana.Assentos;
        if (Any("poste", "lumin", "light", "ilumin", "refletor", "lamp", "balizador solar")) return CategoriaUrbana.Iluminacao;
        if (Any("árvore", "arvore", "tree", "palm", "arbust", "shrub", "planta", "planting", "jardim", "vaso", "floreira", "grama", "flor")) return CategoriaUrbana.Vegetacao;
        if (Any("lixe", "trash", "bin", "waste", "coleta", "container", "resíduo", "residuo", "papeleira")) return CategoriaUrbana.Residuos;
        if (Any("abrigo", "shelter", "ponto de ônibus", "ponto de onibus", "bus stop", "parada")) return CategoriaUrbana.AbrigosTransporte;
        if (Any("paraciclo", "bike", "bicic", "cicl")) return CategoriaUrbana.Ciclismo;
        if (Any("frade", "bollard", "gradil", "guarda", "pilarete", "balizador", "barreira", "fence", "cerca")) return CategoriaUrbana.Seguranca;
        if (Any("placa", "sign", "totem", "mapa", "painel", "logradouro")) return CategoriaUrbana.Comunicacao;
        if (Any("hidrante", "hydrant", "boca de lobo", "bueiro", "caixa", "grelha", "tampa", "drain", "manhole", "semáforo", "semaforo")) return CategoriaUrbana.Infraestrutura;
        if (Any("parklet", "playground", "brinquedo", "academia", "quadra", "fitness", "esport")) return CategoriaUrbana.Lazer;
        if (Any("tátil", "tatil", "rampa", "acessib", "pcd")) return CategoriaUrbana.Acessibilidade;
        return CategoriaUrbana.Outros;
    }
}

/// <summary>Posição de uma família distribuída ao longo de um caminho.</summary>
public readonly record struct FamilySlot(Vec2 Position, Vec2 Direction);

/// <summary>Distribuição de famílias do Revit ao longo de linhas (espaçamento, recuos, lados).</summary>
public static class FamilyLayout
{
    /// <param name="spacing">Espaçamento entre elementos (m).</param>
    /// <param name="offset">Afastamento lateral do caminho (m, + à esquerda).</param>
    /// <param name="bothSides">Repete do outro lado (afastamento espelhado, voltado para o caminho).</param>
    /// <param name="stagger">Nos dois lados, desloca meia distância (disposição em quincôncio).</param>
    /// <param name="rotationDeg">Rotação em relação à tangente do caminho.</param>
    public static List<FamilySlot> Along(Polyline2 path, double spacing, double start, double end, double offset, bool bothSides,
        bool stagger, double rotationDeg)
    {
        var res = new List<FamilySlot>();
        if (path.Length < 1e-6) return res;
        spacing = Math.Max(0.2, spacing);
        var s0 = Math.Clamp(start, 0, path.Length);
        var s1 = Math.Max(s0, path.Length - Math.Max(0, end));
        void Side(double off, double shift, double rot)
        {
            for (var s = s0 + shift; s <= s1 + 1e-6; s += spacing)
            {
                var t = path.TangentAt(s);
                var n = t.PerpLeft;
                var dir = Vec2.FromAngle(t.Angle + rot * Math.PI / 180);
                res.Add(new FamilySlot(path.PointAt(s) + n * off, dir));
            }
        }
        Side(offset, 0, rotationDeg);
        if (bothSides) Side(-offset, stagger ? spacing / 2 : 0, rotationDeg + 180);
        return res;
    }
}
