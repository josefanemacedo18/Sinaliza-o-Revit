using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AD (item 24): sinalização automática ao criar ou editar a via – R-19 pela velocidade (hierarquia), R-24a na mão única,
/// A-32b nas travessias, R-1/R-2 + retenção + legenda nas aproximações controladas (interseção); sem duplicatas, grupos
/// desligáveis e o que foi editado à mão preservado.
/// </summary>
public class AD6AutoSignageTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static RoadSetup Main(double speed = 50)
    {
        var s = RoadTemplates.All[1].Create();          // coletora, 2 + 2 faixas
        s.Speed = speed;
        s.Sinalizacao = new SinalizacaoAutomatica();
        return s;
    }

    private static RoadSetup Secondary()
    {
        var s = RoadTemplates.All[0].Create();          // local
        s.Sinalizacao = new SinalizacaoAutomatica();
        return s;
    }

    /// <summary>Cruz: principal (eixo x) e secundária (eixo y) – ids de grupo e pavimento fixos para simular a edição.</summary>
    private static Y34TipTests.World Cross(RoadSetup main, RoadSetup secondary)
    {
        var w = new Y34TipTests.World();
        var axes = new[] { new[] { new Vec2(-120, 0), new Vec2(120, 0) }, new[] { new Vec2(0, -120), new Vec2(0, 120) } };
        var setups = new[] { main, secondary };
        for (int i = 0; i < 2; i++)
        {
            var pr = PathReference.FromPoints(axes[i], 0);
            var axis = new Polyline2(pr.Points);
            var g = setups[i].Build(pr, new OutputSettings(), Cat, $"grupo{i}", $"via{i}", axis);
            foreach (var m in g) if (m.Path is { } p && p.Points.Count == pr.Points.Count && p.Points[0].DistanceTo(pr.Points[0]) < 1e-9) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            var d = new IntersectionDefinition { Control = ControleIntersecao.Pare, Node = node };
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, Cat));
        }
        return w;
    }

    private static void NoDuplicates(IEnumerable<SignDefinition> signs)
    {
        var list = signs.ToList();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                Assert.False(list[i].Code == list[j].Code && list[i].Position.DistanceTo(list[j].Position) < 3 && list[i].Direction.Dot(list[j].Direction) > 0.5,
                    $"placa {list[i].Code} duplicada em {list[i].Position}");
    }

    [Fact]
    public void Via_nova_em_cruz_com_PARE_R1_so_na_secundaria_sem_duplicatas_e_regenera_ao_editar()
    {
        var w = Cross(Main(), Secondary());
        var L = w.Nodes.Single().Layout;
        Assert.Equal(0, L.Main);                         // a coletora é a principal
        var signs = w.Defs.OfType<SignDefinition>().ToList();
        var mainHalf = w.Roads[0].Def.LeftWidth;
        var secHalf = w.Roads[1].Def.LeftWidth + w.Roads[1].Def.LeftSidewalk;
        // R-1 só nas duas aproximações da secundária (eixo y), fora da pista da principal.
        var r1 = signs.Where(s => s.Code == "R-1").ToList();
        Assert.Equal(2, r1.Count);
        Assert.All(r1, s =>
        {
            Assert.InRange(Math.Abs(s.Position.X), 0, secHalf + 1.0);
            Assert.True(Math.Abs(s.Position.Y) > mainHalf, $"R-1 dentro da principal em {s.Position}");
        });
        Assert.DoesNotContain(signs, s => s.Code == "R-1" && Math.Abs(s.Position.Y) < secHalf);
        // Retenção e legenda PARE nas aproximações da secundária.
        Assert.Equal(2, w.Defs.OfType<LinearMarkingDefinition>().Count(l => l.Code == "LRE"));
        Assert.Equal(2, w.Defs.OfType<TextMarkingDefinition>().Count(t => t.Text == "PARE"));
        // R-19 com a velocidade de cada via, um por sentido.
        var r19 = signs.Where(s => s.Code == "R-19").ToList();
        Assert.Equal(4, r19.Count);
        Assert.Equal(2, r19.Count(s => s.Legend == "50" && s.GroupId == "grupo0"));
        Assert.Equal(2, r19.Count(s => s.Legend == Secondary().Speed.ToString("0") && s.GroupId == "grupo1"));
        Assert.DoesNotContain(signs, s => s.Code == "R-24a");        // mão dupla
        NoDuplicates(signs);

        // Edição da via (velocidade 50 → 60): a sinalização é refeita com os mesmos ids, nada duplicado.
        var ids0 = r19.Where(s => s.GroupId == "grupo0").Select(s => s.Id).OrderBy(x => x).ToList();
        var w2 = Cross(Main(60), Secondary());
        var signs2 = w2.Defs.OfType<SignDefinition>().ToList();
        var r19b = signs2.Where(s => s.Code == "R-19" && s.GroupId == "grupo0").ToList();
        Assert.Equal(ids0, r19b.Select(s => s.Id).OrderBy(x => x).ToList());
        Assert.All(r19b, s => Assert.Equal("60", s.Legend));
        Assert.Equal(2, signs2.Count(s => s.Code == "R-1"));
        NoDuplicates(signs2);
    }

    [Fact]
    public void Mao_unica_recebe_R24a_e_travessia_recebe_A32b()
    {
        var s = AD1OneWayTests.OneWay(2, true);
        s.Sinalizacao = new SinalizacaoAutomatica();
        s.Right.Insert(1, new ElementoSecao { Tipo = TipoElementoSecao.Estacionamento, Largura = 2.2 });
        s.ExtensoesCalcada.Add(new ExtensaoCalcada { LadoEsquerdo = false, Estaca = 90, Comprimento = 20, Travessia = true });
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(200, 0) });
        var defs = s.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, axis: axis);
        var signs = defs.OfType<SignDefinition>().ToList();
        Assert.Equal(2, signs.Count(x => x.Code == "R-24a"));
        Assert.Single(signs, x => x.Code == "R-19");                 // um sentido só
        // A-32b antecipada e junto à travessia (estaca 100), no lado direito de quem trafega.
        var a32 = signs.Where(x => x.Code == "A-32b").OrderBy(x => x.Position.X).ToList();
        Assert.Equal(2, a32.Count);
        Assert.InRange(a32[1].Position.X, 95, 99.9);
        Assert.InRange(a32[0].Position.X, 100 - 80 - 1, 100 - 50 + 1);
        Assert.All(signs, x => Assert.True(x.Position.Y < 0 || x.Code == "R-24a", $"{x.Code} fora do lado direito"));
        Assert.All(signs, x => Assert.True(x.Direction.X > 0.9));
        NoDuplicates(signs);
        // Placas na calçada: além da face do meio-fio.
        var face = s.Lado(false).Face;
        Assert.All(signs.Where(x => x.Position.Y < 0), x => Assert.True(-x.Position.Y > face + 0.3));
    }

    [Fact]
    public void Grupos_desligaveis_e_vias_antigas_sem_placas_automaticas()
    {
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(150, 0) });
        var path = PathReference.FromPoints(axis.Points, 0);
        var old = RoadTemplates.All[0].Create();
        Assert.Null(old.Sinalizacao);
        Assert.Empty(old.Build(path, new OutputSettings(), Cat, axis: axis).OfType<SignDefinition>());
        var off = Secondary();
        off.Sinalizacao!.Velocidade = false;
        Assert.DoesNotContain(off.Build(path, new OutputSettings(), Cat, axis: axis).OfType<SignDefinition>(), x => x.Code == "R-19");
        var json = RoadTemplates.ToJson(off);
        Assert.False(RoadTemplates.FromJson(json)!.Sinalizacao!.Velocidade);
    }

    [Fact]
    public void Placa_editada_a_mao_e_preservada()
    {
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(150, 0) });
        var s = Secondary();
        var gen = s.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, "g", "v", axis);
        var r19 = gen.OfType<SignDefinition>().First(x => x.Code == "R-19");
        // O usuário moveu a placa 8 m e trocou a legenda: a regeneração não a refaz nem cria outra.
        var edited = (SignDefinition)MarkingDefinition.FromJson(r19.ToJson())!;
        edited.Position += new Vec2(8, 0);
        edited.Legend = "30";
        edited.EditadoManualmente = true;
        var regen = s.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, "g", "v", axis);
        var kept = AutoSignage.Preserve(regen, new MarkingDefinition[] { edited });
        Assert.DoesNotContain(kept, d => d.Id == r19.Id);
        Assert.Equal(regen.Count - 1, kept.Count);
        // Placa da interseção editada (sem id igual): a gerada perto dela, com o mesmo código, também sai.
        var w = Cross(Main(), Secondary());
        var r1 = w.Defs.OfType<SignDefinition>().First(x => x.Code == "R-1");
        var moved = (SignDefinition)MarkingDefinition.FromJson(r1.ToJson())!;
        moved.Id = "outro";
        moved.Position += new Vec2(0.8, 0);
        moved.EditadoManualmente = true;
        var children = w.Defs.Where(d => d.GroupId == w.Nodes[0].Layout.Definition!.Id).ToList();
        var keptChildren = AutoSignage.Preserve(children, new MarkingDefinition[] { moved });
        Assert.Equal(children.Count - 1, keptChildren.Count);
        Assert.DoesNotContain(keptChildren, d => d.Id == r1.Id);
        // Sem edição manual: nada muda.
        Assert.Equal(children.Count, AutoSignage.Preserve(children, children).Count);
    }
}
