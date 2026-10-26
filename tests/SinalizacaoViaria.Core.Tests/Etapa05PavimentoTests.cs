using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 5 do plano – terraplenagem e pavimentação (Prompt Master §9, §10, §14; Motores §8, §9): estrutura do pavimento em
/// camadas por via, usada no fundo da caixa da terraplenagem, no corte transversal e no quantitativo por camada; registro da
/// terraplenagem aplicada e V15 (terraplenagem desatualizada).
/// </summary>
public class Etapa05PavimentoTests
{
    private static List<CopiaGravada> Copias(params MarkingDefinition[] defs) => defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e-" + d.Id)).ToList();

    private static RoadPavementDefinition Via(bool molda = true, string? camadas = null)
    {
        var v = new RoadPavementDefinition
        {
            PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(100, 0) }, 0), GroupId = "g-" + Guid.NewGuid().ToString("N"),
        };
        if (molda)
        {
            v.Output.Grade = RoadGrade.Flat(100);
            v.Output.Grade.AdjustTerrain = true;
        }
        if (camadas != null) v.Estrutura = EstruturaPavimento.Ler(camadas).Estrutura;
        return v;
    }

    // ------------------------------------------------------------------ estrutura do pavimento

    [Fact]
    public void Estrutura_LeTextoEDevolveOMesmoTexto_ComProfundidadeECamadasDoCorte()
    {
        var (e, problemas) = EstruturaPavimento.Ler("Base; 15; brita graduada simples; base\nSub-base; 20,5; solo-brita; sub-base\nReforço; 30; ; reforço do subleito");
        Assert.Empty(problemas);
        Assert.Equal(3, e.Camadas.Count);
        Assert.Equal(FuncaoCamada.SubBase, e.Camadas[1].Funcao);
        Assert.Null(e.Camadas[2].Material);
        Assert.Equal(Hachura.Solo, e.Camadas[2].Hachura);                  // reforço e regularização: solo
        Assert.Equal(0.655, e.EspessuraCamadas, 9);
        Assert.Equal(0.705, e.Profundidade(0.05), 9);                      // revestimento + camadas
        Assert.Equal("Base; 15; brita graduada simples; base\nSub-base; 20,5; solo-brita; sub-base\nReforço; 30; ; reforço do subleito", e.Texto());
        var (de_novo, p2) = EstruturaPavimento.Ler(e.Texto());
        Assert.Empty(p2);
        Assert.Equal(e.Texto(), de_novo.Texto());
        var corte = e.ParaCorte();
        Assert.Equal("Base (brita graduada simples)", corte[0].Name);
        Assert.Equal("Reforço", corte[2].Name);
        Assert.Equal(20.5, corte[1].ThicknessCm);
        // Texto vazio = sem camadas, sem problema.
        var (vazia, p3) = EstruturaPavimento.Ler("  ");
        Assert.Empty(vazia.Camadas);
        Assert.Empty(p3);
    }

    [Fact]
    public void Estrutura_ProblemasDeLeitura_SemAplicarNadaInventado()
    {
        var (_, p) = EstruturaPavimento.Ler("Base\nSub-base; vinte; solo\nBase; 15; BGS; drenante\nCamada; 0\n; 10\nGrossa; 250");
        Assert.Contains(p, x => x.StartsWith("\"Base\": use"));
        Assert.Contains(p, x => x.StartsWith("\"Sub-base; vinte; solo\": use"));
        Assert.Contains(p, x => x.Contains("função \"drenante\" desconhecida"));
        Assert.Contains(p, x => x == "Camada: espessura ausente ou nula");
        Assert.Contains(p, x => x.EndsWith("sem nome"));
        Assert.Contains(p, x => x.StartsWith("Grossa: espessura de 250 cm"));
    }

    [Fact]
    public void Via_GuardaAEstruturaNoJson_EProfundidadeSoComCamadas()
    {
        var v = Via(camadas: "Base; 15; BGS; base\nSub-base; 15; solo-brita; sub-base");
        v.Estrutura!.Fonte = "Projeto de pavimentação rev. 2";
        var lida = (RoadPavementDefinition)MarkingDefinition.FromJson(v.ToJson())!;
        Assert.Equal(v.Estrutura.Texto(), lida.Estrutura!.Texto());
        Assert.Equal("Projeto de pavimentação rev. 2", lida.Estrutura.Fonte);
        Assert.Equal(0.05 + 0.30, lida.ProfundidadeEstrutura!.Value, 9);   // CBUQ 5 cm + 30 cm
        Assert.Null(Via().ProfundidadeEstrutura);
        Assert.Null(new RoadPavementDefinition { Estrutura = new EstruturaPavimento() }.ProfundidadeEstrutura);
        // A profundidade na terraplenagem: a estrutura da via ou o subleito.
        Assert.Equal(0.35, EarthworkRecord.Profundidade(lida, 0.40), 9);
        Assert.Equal(0.40, EarthworkRecord.Profundidade(Via(), 0.40), 9);
    }

    [Fact]
    public void No_UsaAViaLigadaMaisFunda_OuOSubleito()
    {
        var rasa = Via(camadas: "Base; 10; BGS");
        var funda = Via(camadas: "Base; 20; BGS\nSub-base; 30; solo");
        var sem = Via();
        var todas = new MarkingDefinition[] { rasa, funda, sem };
        var no = new IntersectionDefinition { RoadIds = { rasa.Id, funda.Id, sem.Id } };
        Assert.Equal(0.05 + 0.50, EarthworkRecord.ProfundidadeDoNo(no, todas, 0.40), 9);
        Assert.Equal(0.40, EarthworkRecord.ProfundidadeDoNo(new IntersectionDefinition { RoadIds = { sem.Id } }, todas, 0.40), 9);
        // Via sem estrutura num nó com uma via rasa: o subleito (0,40) é mais fundo que 0,15.
        Assert.Equal(0.40, EarthworkRecord.ProfundidadeDoNo(new IntersectionDefinition { RoadIds = { rasa.Id, sem.Id } }, todas, 0.40), 9);
        Assert.Equal(0.40, EarthworkRecord.ProfundidadeDoNo(new IntersectionDefinition(), todas, 0.40), 9);
    }

    // ------------------------------------------------------------------ registro da terraplenagem

    [Fact]
    public void Participantes_SaoOsQueMoldamOTerreno()
    {
        var molda = Via();
        var plana = Via(molda: false);
        var no = new IntersectionDefinition { RoadIds = { molda.Id, plana.Id } };
        var noPlano = new IntersectionDefinition { RoadIds = { plana.Id } };
        var caixa = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(10, 5), Z = 10 };
        var muro = new RetainingWallDefinition();
        var muroSemTerreno = new RetainingWallDefinition { AdjustTerrain = false };
        var placa = new SignDefinition();
        var p = EarthworkRecord.Participantes(new MarkingDefinition[] { molda, plana, no, noPlano, caixa, muro, muroSemTerreno, placa }).Select(d => d.Id).ToHashSet();
        Assert.Equal(new[] { molda.Id, no.Id, caixa.Id, muro.Id }.ToHashSet(), p);
    }

    [Fact]
    public void Assinatura_MudaComAMarca_MasNaoComALinhaDoTerrenoLida()
    {
        var v = Via();
        var a = EarthworkRecord.Assinatura(v);
        Assert.Equal(a, EarthworkRecord.Assinatura((RoadPavementDefinition)MarkingDefinition.FromJson(v.ToJson())!));
        v.Estrutura = EstruturaPavimento.Ler("Base; 15; BGS").Estrutura;
        Assert.NotEqual(a, EarthworkRecord.Assinatura(v));
        var muro = new RetainingWallDefinition();
        var m = EarthworkRecord.Assinatura(muro);
        muro.GroundLine = new List<Vec2> { new(0, 0), new(10, 0.3) };    // terreno natural lido na geração: não é escolha do projeto
        Assert.Equal(m, EarthworkRecord.Assinatura(muro));
        muro.HeightStart = 4;
        Assert.NotEqual(m, EarthworkRecord.Assinatura(muro));
    }

    [Fact]
    public void Registro_GravaLeEComparaAlteradaRemovidaENova()
    {
        var v1 = Via();
        var v2 = Via();
        var caixa = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(10, 5), Z = 10 };
        var antes = new MarkingDefinition[] { v1, v2, caixa };
        var reg = EarthworkRecord.Gravar(EarthworkRecord.Participantes(antes), 0.40, 1.0, 1.5, new DateTime(2026, 10, 26, 9, 0, 0), "2.11.0");
        var lido = RegistroTerraplenagem.FromJson(reg.ToJson())!;
        Assert.Equal(reg.Assinaturas, lido.Assinaturas);
        Assert.Equal(0.40, lido.Subleito);
        Assert.Equal("2.11.0", lido.Versao);
        Assert.Null(RegistroTerraplenagem.FromJson("{ quebrado"));
        Assert.Null(RegistroTerraplenagem.FromJson(null));
        Assert.Empty(EarthworkRecord.Comparar(lido, antes));

        v1.Estrutura = EstruturaPavimento.Ler("Base; 15; BGS").Estrutura;   // mudou depois
        var v3 = Via();                                                     // nova, ainda não aplicada
        var depois = new MarkingDefinition[] { v1, caixa, v3 };             // v2 apagada
        var m = EarthworkRecord.Comparar(lido, depois);
        Assert.Equal(SituacaoTerraplenagem.Alterada, m.Single(x => x.Id == v1.Id).Situacao);
        Assert.Equal(SituacaoTerraplenagem.Removida, m.Single(x => x.Id == v2.Id).Situacao);
        Assert.Equal(v2.DisplayCode, m.Single(x => x.Id == v2.Id).Nome);   // o nome vem do registro
        Assert.Equal(SituacaoTerraplenagem.Nova, m.Single(x => x.Id == v3.Id).Situacao);
        Assert.Equal(3, m.Count);
        // Via que deixou de moldar o terreno (sem ser apagada): o terreno ainda tem a terraplenagem dela.
        v1.Output.Grade!.AdjustTerrain = false;
        Assert.Equal(SituacaoTerraplenagem.Retirada, Assert.Single(EarthworkRecord.Comparar(lido, new MarkingDefinition[] { v1, v2, caixa })).Situacao);
    }

    // ------------------------------------------------------------------ V15 no Validar Modelo

    [Fact]
    public void V15_NaoVerificadoSemRegistro_ENadaComRegistroEmDia()
    {
        var v = Via();
        var sem = ModelValidator.Validate(Copias(v), incluirNaoVerificados: true);
        var n = Assert.Single(sem, a => a.Codigo == "V15");
        Assert.Equal(Severidade.NaoVerificavel, n.Severidade);
        Assert.Contains("Sem registro da terraplenagem", n.Mensagem);
        Assert.Contains("rodar a Terraplenagem", n.AcaoRecomendada);
        // Nada molda o terreno: nada a dizer.
        Assert.DoesNotContain(ModelValidator.Validate(Copias(Via(molda: false)), incluirNaoVerificados: true), a => a.Codigo == "V15");
        var reg = EarthworkRecord.Gravar(new[] { v }, 0.4, 1, 1.5, DateTime.Now, null);
        Assert.DoesNotContain(ModelValidator.Validate(Copias(v), incluirNaoVerificados: true, terraplenagem: reg), a => a.Codigo == "V15");
        Assert.Equal("Terraplenagem", ModelValidator.Regras["V15"].Modulo);
    }

    [Fact]
    public void V15_TerraplenagemDesatualizada_UmAchadoPorSituacao()
    {
        var v1 = Via();
        var v2 = Via();
        var reg = EarthworkRecord.Gravar(new MarkingDefinition[] { v1, v2 }, 0.4, 1, 1.5, new DateTime(2026, 10, 26, 9, 5, 0), "2.11.0");
        v1.Thickness = 0.08;
        var v3 = Via();
        var r = ModelValidator.Validate(Copias(v1, v3), terraplenagem: reg).Where(a => a.Codigo == "V15").ToList();
        Assert.Equal(3, r.Count);
        Assert.All(r, a => Assert.Equal(Severidade.Aviso, a.Severidade));
        var alt = r.Single(a => a.Ids.Contains(v1.Id));
        Assert.Contains("1 marca(s) que moldam o terreno mudaram depois da terraplenagem de 26/10/2026 09:05", alt.Mensagem);
        var rem = r.Single(a => a.Ids.Contains(v2.Id));
        Assert.Contains("foram apagadas e o terreno ainda tem a terraplenagem delas", rem.Mensagem);
        Assert.Contains("terreno original", rem.AcaoRecomendada);
        Assert.Contains("não estavam na terraplenagem", r.Single(a => a.Ids.Contains(v3.Id)).Mensagem);
        var txt = ModelValidator.Report(r, 2, 2, "P", DateTime.Now);
        Assert.Contains("V15 · Terraplenagem", txt);
    }

    // ------------------------------------------------------------------ quantitativo por camada

    private static MarkingGeometry Pista(double comprimento, double largura) => new()
    {
        Pieces = { new MarkingPiece(Polygon2.Rectangle(new Vec2(0, -largura / 2), new Vec2(comprimento, largura / 2)), MarkingColor.Asfalto) { Thickness = 0.05 } },
    };

    [Fact]
    public void Camadas_RevestimentoMaisEstrutura_AreaDoPavimentoVezesEspessura()
    {
        var v = Via(camadas: "Base; 15; BGS; base\nSub-base; 20; solo-brita; sub-base");
        v.Hierarchy = HierarquiaViaria.Coletora;
        v.Estrutura!.Fonte = "dimensionamento DNIT";
        var semEstrutura = new List<string>();
        var l = PavementLayerQuantities.Compute(new[] { ((MarkingDefinition)v, Pista(100, 7)) }, new MarkingDefinition[] { v }, semEstrutura);
        Assert.Equal(3, l.Count);
        Assert.Empty(semEstrutura);
        Assert.Equal("Revestimento", l[0].Camada);
        Assert.Equal("CBUQ (asfáltico)", l[0].Material);
        Assert.Equal(700, l[0].Area, 6);
        Assert.Equal(35, l[0].Volume, 6);             // 700 m² × 5 cm
        Assert.Equal(105, l[1].Volume, 6);            // × 15 cm
        Assert.Equal(140, l[2].Volume, 6);            // × 20 cm
        Assert.Equal("sub-base", l[2].Funcao);
        Assert.All(l, x => Assert.Equal("dimensionamento DNIT", x.Fonte));
        Assert.All(l, x => Assert.Equal(HierarquiaViaria.Coletora, x.Hierarquia));
        var csv = PavementLayerQuantities.Csv(l);
        Assert.Contains("CAMADAS DO PAVIMENTO", csv);
        Assert.Equal("Via 1", l[0].Origem);                 // PAV-ASF é o código de todas as vias asfálticas: numeradas
        Assert.Contains($"Via 1;{Hierarquia.Label(HierarquiaViaria.Coletora)};Base;BGS;base;15;700,00;105,000;dimensionamento DNIT", csv);
        Assert.Contains("TOTAL;;Sub-base;solo-brita;;;700,00;140,000;", csv);
    }

    [Fact]
    public void Camadas_NoPelaViaMaisFunda_ViaSemEstruturaSoRevestimento()
    {
        var rasa = Via(camadas: "Base; 10; BGS");
        var funda = Via(camadas: "Base; 20; BGS\nSub-base; 30; solo");
        var sem = Via();
        var no = new IntersectionDefinition { RoadIds = { rasa.Id, funda.Id } };
        var todas = new MarkingDefinition[] { rasa, funda, sem, no };
        var semEstrutura = new List<string>();
        var l = PavementLayerQuantities.Compute(new[]
        {
            ((MarkingDefinition)no, Pista(20, 20)), ((MarkingDefinition)sem, Pista(50, 6)),
            ((MarkingDefinition)new SignDefinition(), Pista(1, 1)),           // fora do pavimento
            ((MarkingDefinition)rasa, new MarkingGeometry()),                   // sem área: nada
        }, todas, semEstrutura);
        var doNo = l.Where(x => x.Origem.EndsWith("(pela Via 2)")).ToList();
        Assert.StartsWith(no.KindName + " 1", doNo[0].Origem);
        Assert.Equal(new[] { "Revestimento", "Base", "Sub-base" }, doNo.Select(x => x.Camada));
        Assert.Equal(400 * 0.30, doNo[2].Volume, 6);
        var daVia = Assert.Single(l, x => x.Origem == "Via 3");
        Assert.Equal("Revestimento", daVia.Camada);
        Assert.Equal(new[] { "Via 3" }, semEstrutura);
        Assert.Contains("Sem estrutura do pavimento informada (só o revestimento): Via 3", PavementLayerQuantities.Csv(l, semEstrutura));
        // Totais por camada e material, de cima para baixo.
        var t = PavementLayerQuantities.Totais(l);
        Assert.Equal("Revestimento", t[0].Camada);
        Assert.Equal(400 + 300, t[0].Area, 6);
    }

    // ------------------------------------------------------------------ corte transversal

    private static (SectionProfileDefinition Pd, BuildContext Ctx, RoadPavementDefinition Via) Corte()
    {
        var (defs, geo, path) = DetailGenerator.SectionSample();
        var sd = new SectionDimensionDefinition { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45), DesenhoVersao = 2 };
        var all = defs.Append(sd).ToList();
        var ctx = new BuildContext
        {
            Catalog = CatalogService.LoadDefault(), ViewScale = 100, AllDefinitions = () => all, GeometryOf = geo, PathOf = path,
            Lookup = id => all.FirstOrDefault(d => d.Id == id),
        };
        return (SectionProfileDefinition.From(sd, new Vec2(0, -11)), ctx, defs.OfType<RoadPavementDefinition>().Single());
    }

    private static List<string> Textos(MarkingGeometry g) => g.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();

    [Fact]
    public void Corte_UsaAEstruturaDaVia_SemCamadasProprias_ESenaoAsTipicas()
    {
        var (pd, ctx, via) = Corte();
        Assert.Null(pd.Layers);
        // Sem estrutura na via: as camadas típicas (como antes).
        Assert.Contains("Base de brita graduada simples (BGS) – e = 15 cm", Textos(DetailGenerator.SectionProfileDrawing(pd, ctx)));
        via.Estrutura = EstruturaPavimento.Ler("Base; 18; BGS; base\nReforço; 25; solo selecionado; reforço").Estrutura;
        var t = Textos(DetailGenerator.SectionProfileDrawing(pd, ctx));
        Assert.Contains("Base (BGS) – e = 18 cm", t);
        Assert.Contains("Reforço (solo selecionado) – e = 25 cm", t);
        Assert.DoesNotContain("Base de brita graduada simples (BGS) – e = 15 cm", t);
        // Camadas escolhidas no próprio perfil continuam valendo.
        pd.Layers = new List<SectionLayer> { new() { Name = "Base do perfil", ThicknessCm = 12 } };
        Assert.Contains("Base do perfil – e = 12 cm", Textos(DetailGenerator.SectionProfileDrawing(pd, ctx)));
    }

    [Fact]
    public void Corte_ViasComEstruturasDiferentes_AvisoEAPrimeira()
    {
        var a = Via(camadas: "Base; 15; BGS");
        var b = Via(camadas: "Base; 25; BGS");
        var segs = new List<DetailGenerator.ProfileSegment>
        {
            new(6, 9, -0.05, 0, MarkingColor.Asfalto, b, false),
            new(0, 3, -0.05, 0, MarkingColor.Asfalto, a, false),
        };
        var g = new MarkingGeometry();
        var l = DetailGenerator.RoadLayers(segs, g)!;
        Assert.Equal(15, Assert.Single(l).ThicknessCm);                       // a primeira ao longo do corte
        Assert.Contains(g.Warnings, w => w.Contains("estruturas de pavimento diferentes") && w.Contains(a.DisplayCode));
        Assert.Null(DetailGenerator.RoadLayers(new[] { new DetailGenerator.ProfileSegment(0, 3, -0.05, 0, MarkingColor.Asfalto, Via(), false) }, new MarkingGeometry()));
    }
}
