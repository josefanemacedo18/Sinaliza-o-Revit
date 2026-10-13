using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Cenas do Loteamento: quadras de formas variadas e um loteamento completo gerado com as vias do plugin.</summary>
public static class LTScenes
{
    public static readonly List<Vec2> Rect = new() { new(0, 0), new(100, 0), new(100, 60), new(0, 60) };

    public static QuadraDefinition Q(string name, IReadOnlyList<Vec2> ring, params Vec2[] testadas)
    {
        var q = new QuadraDefinition { Nome = name, Contorno = new Polygon2(ring).Outer.ToList() };
        var i = 0;
        foreach (var t in testadas) q.Testadas.Add(new TestadaLote { Id = ((char)('A' + i++)).ToString(), Ponto = t });
        return q;
    }

    public static List<Vec2> Arc()
    {
        var arc = new List<Vec2> { new(0, 0), new(120, 0) };
        for (int i = 1; i < 40; i++) { var t = i / 40.0 * Math.PI / 2; arc.Add(new Vec2(40 + 80 * Math.Cos(t), 80 * Math.Sin(t))); }
        arc.Add(new(40, 80));
        arc.Add(new(0, 80));
        return arc;
    }

    /// <summary>Quadras de teste por nome.</summary>
    public static QuadraDefinition Scene(string name) => name switch
    {
        "retangular" => Q("H", Rect, new Vec2(50, 0), new Vec2(100, 30), new Vec2(50, 60), new Vec2(0, 30)),
        "arredondada" => Q("H", CurveTools.FilletCorners(Rect, 6, true), new Vec2(50, 0), new Vec2(100, 30), new Vec2(50, 60), new Vec2(0, 30)),
        "rasa" => Q("G", new List<Vec2> { new(0, 0), new(100, 0), new(100, 50), new(0, 50) }, new Vec2(50, 0), new Vec2(50, 50)),
        "obliqua" => Q("O", new List<Vec2> { new(0, 0), new(110, 0), new(140, 70), new(20, 70) }, new Vec2(55, 0), new Vec2(125, 35), new Vec2(80, 70), new Vec2(10, 35)),
        "curva" => Q("K", Arc(), new Vec2(60, 0), new Vec2(40 + 80 * Math.Cos(0.8), 80 * Math.Sin(0.8)), new Vec2(0, 40)),
        "uma-testada" => Q("U", Rect, new Vec2(50, 0)),
        "L" => Q("L", new List<Vec2> { new(0, 0), new(90, 0), new(90, 40), new(40, 40), new(40, 90), new(0, 90) }, new Vec2(45, 0), new Vec2(0, 45), new Vec2(65, 40), new Vec2(40, 65)),
        "personalizada" => Personalizada(),
        _ => throw new ArgumentException(name),
    };

    public static readonly string[] Names = { "retangular", "arredondada", "rasa", "obliqua", "curva", "uma-testada", "L", "personalizada" };

    public static QuadraDefinition Personalizada()
    {
        var c = Scene("retangular");
        c.Esquinas.Add(new EsquinaLote { Ponto = new(100, 0), Modo = ModoEsquina.TestadaPosterior });
        c.Esquinas.Add(new EsquinaLote { Ponto = new(100, 60), Modo = ModoEsquina.MedidaPropria, FrenteAnterior = 15, FrentePosterior = 20 });
        c.Ajustes.Add(new LoteAjuste { Chave = "A:3", Frente = 20 });
        c.Ajustes.Add(new LoteAjuste { Chave = "A:5", Partes = 2 });
        c.Ajustes.Add(new LoteAjuste { Chave = "A:2", Categoria = CategoriaLote.AreaVerde });
        return c;
    }

    public sealed record Loteamento(LoteamentoDefinition Lot, List<QuadraDefinition> Quadras, ResultadoReconhecimento Rec, List<Polygon2> Occupied);

    /// <summary>
    /// Loteamento sobre vias do plugin: gleba 240 × 180 m cortada por uma via local leste-oeste e duas norte-sul (local e
    /// coletora) – seis quadras reconhecidas, lotes 12 × 30.
    /// </summary>
    public static Loteamento World(string kind = "grade")
    {
        var w = kind switch
        {
            "cruz" => Y34TipTests.Make(AB2Matrix.Template(), (0, new[] { new Vec2(-120, 0), new Vec2(120, 0) }), (0, new[] { new Vec2(0, -90), new Vec2(0, 90) })),
            "T" => Y34TipTests.Make(AB2Matrix.Template(), (0, new[] { new Vec2(-120, 0), new Vec2(120, 0) }), (0, new[] { new Vec2(0, 0), new Vec2(0, 90) })),
            "avenida" => Y34TipTests.Make(AB2Matrix.Template(), (2, new[] { new Vec2(-120, 0), new Vec2(120, 0) }), (0, new[] { new Vec2(0, -90), new Vec2(0, 90) })),
            _ => Y34TipTests.Make(AB2Matrix.Template(), (0, new[] { new Vec2(-120, 0), new Vec2(120, 0) }), (0, new[] { new Vec2(-50, -90), new Vec2(-50, 90) }),
                (1, new[] { new Vec2(50, -90), new Vec2(50, 90) })),
        };
        var occ = Y34TipTests.Geometry(w).Where(x => Reconhecimento.Viaria(x.Piece)).Select(x => x.Piece.Shape).ToList();
        var gleba = Polygon2.Rectangle(new Vec2(-120, -90), new Vec2(120, 90));
        var rec = Reconhecimento.Quadras(gleba, occ);
        var lot = new LoteamentoDefinition { Nome = "Jardim Teste", Gleba = gleba.Outer.ToList() };
        lot.Vias.Add(new ViaNomeada { Nome = "Rua das Flores", Eixo = new() { new(-120, 0), new(120, 0) } });
        lot.Vias.Add(new ViaNomeada { Nome = "Rua A", Eixo = new() { new(-50, -90), new(-50, 90) } });
        lot.Vias.Add(new ViaNomeada { Nome = "Avenida B", Eixo = new() { new(50, -90), new(50, 90) } });
        var quadras = rec.Quadras.Select(r => Reconhecimento.NovaQuadra(r, lot, 12, 30)).ToList();
        return new Loteamento(lot, quadras, rec, occ);
    }
}
