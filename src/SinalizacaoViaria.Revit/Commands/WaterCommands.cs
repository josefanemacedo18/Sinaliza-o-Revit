using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Janela do corpo d'água (criação e Editar): leito, água, taludes e a faixa de proteção informada, com a prévia.</summary>
internal static class WaterForms
{
    public static FormWindow Janela(CorpoDaguaDefinition d, Polyline2 path, Func<Vec2, double?>? natural, string title, string ok)
    {
        var fundoTexto = string.Join("\n", d.Fundo.Select(v => $"{TerrainTools.N(v.X, "0.##")}; {TerrainTools.N(v.Y, "0.00")}"));
        var verPerfil = false;
        FormPreview? Preview()
        {
            var geo = new MarkingGeometry();
            foreach (var f in CorposDagua.Faixa(d, path)) geo.Add(f, MarkingColor.Verde);
            foreach (var a in CorposDagua.Agua(d, path)) geo.Add(a, MarkingColor.Agua);
            var info = $"Superfície de água {TerrainTools.N(CorposDagua.Agua(d, path).Sum(p => Math.Abs(p.Area)), "#,##0")} m²";
            if (d.FaixaProtecao > 0.01)
                info += $" · faixa de proteção {TerrainTools.N(d.FaixaProtecao)} m: {TerrainTools.N(CorposDagua.Faixa(d, path).Sum(p => p.Area), "#,##0")} m²" +
                        (string.IsNullOrWhiteSpace(d.FonteFaixa) ? " (sem lei citada)" : $" ({d.FonteFaixa.Trim()})");
            else info += " · faixa de proteção não informada";
            var ctx = new BuildContext { Catalog = PluginContext.Catalog, Ground = natural };
            var copia = (CorpoDaguaDefinition)MarkingDefinition.FromJson(d.ToJson())!;
            var leito = WaterGenerator.Build(copia, path, ctx);
            if (natural != null && (leito.Corridors.Count > 0 || leito.Pads.Count > 0))
            {
                // Malha da estimativa: 1 m, aumentada nos corpos d'água grandes (até ~40 mil células na caixa do caminho).
                var xs = path.Points.Select(p => p.X).ToList();
                var ys = path.Points.Select(p => p.Y).ToList();
                var caixa = (xs.Max() - xs.Min() + 40) * (ys.Max() - ys.Min() + 40);
                var cel = Math.Max(1.0, Math.Sqrt(caixa / 40000));
                var res = Grading.Design(leito.Corridors, leito.Pads, natural, 0, 60, 0.5, Math.Max(4, cel), cel);
                Grading.Volumes(res, natural, cel);
                info += $"\nEscavação do leito (estimativa da Terraplenagem): corte {TerrainTools.N(res.CutM3, "#,##0")} m³, aterro {TerrainTools.N(res.FillM3, "#,##0")} m³";
            }
            if (d.EhRio)
            {
                var terreno = natural == null ? null : CorposDagua.Perfil(Amostras(path, natural));
                var fundo = CorposDagua.Fundo(d, path.Length, terreno);
                var s0 = CorposDagua.Secao(d, fundo, 0);
                var s1 = CorposDagua.Secao(d, fundo, path.Length);
                info += $"\nFundo de {TerrainTools.N(s0.ZFundo)} a {TerrainTools.N(s1.ZFundo)} m; água de {TerrainTools.N(s0.ZAgua)} a {TerrainTools.N(s1.ZAgua)} m; " +
                        $"largura na água {TerrainTools.N(s0.LarguraAgua)} m; área molhada {TerrainTools.N(s0.AreaMolhada)} m²";
                if (verPerfil)
                {
                    var ss = Enumerable.Range(0, 101).Select(i => path.Length * i / 100).ToList();
                    var linhas = new List<(string, IReadOnlyList<Vec2>, MarkingColor)>();
                    if (terreno != null) linhas.Add(("terreno natural no eixo", ss.Select(x => new Vec2(x, terreno.Z(x))).ToList(), MarkingColor.Concreto));
                    linhas.Add(("fundo do leito", ss.Select(x => new Vec2(x, fundo.Z(x))).ToList(), MarkingColor.Vermelha));
                    linhas.Add(("nível d'água", ss.Select(x => new Vec2(x, fundo.Z(x) + d.Lamina)).ToList(), MarkingColor.Agua));
                    geo = TerrainCharts.Perfil(linhas, 5, $"Perfil do {d.KindName.ToLowerInvariant()} (de montante para jusante)");
                }
            }
            info += string.Concat(leito.Warnings.Select(w => "\n⚠ " + w));
            return new FormPreview(geo, null, null, info, Paper: verPerfil && d.EhRio);
        }
        var w = new FormWindow(title, d.EhRio ? "Rio ou córrego" : "Lago ou lagoa",
            "O leito é escavado no Toposolid pela Terraplenagem (com registro) e a água fica no modelo. A faixa de proteção é a que você informar, " +
            "medida da borda da água, com a lei que a define – o plugin não fixa largura legal. Os taludes das margens são valores do projeto.",
            null, Preview, false, ok, 1000, 700);
        w.Choice("Tipo", d.EhRio ? new[] { ("Rio", TipoCorpoDagua.Rio), ("Córrego", TipoCorpoDagua.Corrego) } : new[] { ("Lago", TipoCorpoDagua.Lago), ("Lagoa", TipoCorpoDagua.Lagoa) },
                () => d.Corpo, v => d.Corpo = v)
         .Text("Nome", () => d.Nome, v => d.Nome = v ?? "");
        if (d.EhRio)
            w.Number("Largura do fundo (m)", () => d.LarguraFundo, v => d.LarguraFundo = v, 0.1, 500, "0.00")
             .Number("Lâmina d'água sobre o fundo (m)", () => d.Lamina, v => d.Lamina = v, 0, 50, "0.00")
             .Number("Profundidade do fundo abaixo do terreno (m, sem fundo informado)", () => d.Profundidade, v => d.Profundidade = v, 0, 50, "0.00")
             .Text("Fundo por estaca (\"estaca; cota\", uma por linha; vazio = do terreno)", () => fundoTexto, v => { fundoTexto = v ?? ""; d.Fundo = CorposDagua.LerFundo(fundoTexto); }, multiline: true);
        else
            w.Number("Nível d'água (m) – o contorno é a margem nele", () => d.NivelAgua, v => d.NivelAgua = v, -1000, 10000, "0.00")
             .Number("Cota do fundo (m)", () => d.CotaFundo, v => d.CotaFundo = v, -1000, 10000, "0.00");
        if (d.EhRio) w.Check("Prévia: perfil do leito (terreno, fundo e nível d'água)", () => verPerfil, v => verPerfil = v);
        w.Number("Talude das margens (H : 1 V)", () => d.TaludeMargem, v => d.TaludeMargem = v, 0.1, 20, "0.0#")
         .Check("Escavar o leito no Toposolid (Terraplenagem)", () => d.AdjustTerrain, v => d.AdjustTerrain = v)
         .Section("Faixa de proteção (informada)")
         .Number("Largura em cada margem (m; 0 = não informada)", () => d.FaixaProtecao, v => d.FaixaProtecao = v, 0, 1000, "0.##")
         .Text("Lei e artigo que a definem (como informado)", () => d.FonteFaixa, v => d.FonteFaixa = v ?? "");
        return w;
    }

    /// <summary>Terreno no eixo a cada ~2 m (estaca, cota), para o fundo padrão e o perfil.</summary>
    private static List<Vec2> Amostras(Polyline2 path, Func<Vec2, double?> natural)
    {
        var n = Math.Max(2, (int)Math.Ceiling(path.Length / 2.0) + 1);
        var res = new List<Vec2>();
        for (int i = 0; i < n; i++)
        {
            var s = path.Length * i / (n - 1);
            if (natural(path.PointAt(s)) is { } z) res.Add(new Vec2(s, z));
        }
        return res;
    }

    public static (FormWindow Window, MarkingDefinition Working)? ForEdit(UIDocument uidoc, MarkingDefinition def)
    {
        if (def is not CorpoDaguaDefinition ca) return null;
        var d = (CorpoDaguaDefinition)MarkingDefinition.FromJson(ca.ToJson())!;
        var path = PathResolver.Resolve(uidoc.Document, d.PathRef)?.Main ?? new Polyline2(d.PathRef.Points, !d.EhRio);
        return (Janela(d, path, TerrainModel.Ground(uidoc.Document), $"Editar {d.KindName.ToLowerInvariant()}", "Aplicar"), d);
    }

    /// <summary>Cria o corpo d'água pelo eixo (rio) ou pelo contorno (lago) clicado.</summary>
    public static List<RenderResult>? Criar(UIDocument uidoc, bool rio)
    {
        var doc = uidoc.Document;
        var natural = TerrainModel.Ground(doc);
        var pts = TerrainTools.Pontos(uidoc, rio ? "Rio ou córrego: eixo, de montante para jusante" : "Lago ou lagoa: margem no nível d'água", !rio);
        if (pts == null) return null;
        var d = UiHelpers.Remembered<CorpoDaguaDefinition>(rio ? "corpo-rio" : "corpo-lago") ?? new CorpoDaguaDefinition { Corpo = rio ? TipoCorpoDagua.Corrego : TipoCorpoDagua.Lago };
        d = (CorpoDaguaDefinition)d.CloneWithNewId();
        if (d.EhRio != rio) d.Corpo = rio ? TipoCorpoDagua.Corrego : TipoCorpoDagua.Lago;
        d.Fundo.Clear();
        d.GroundLine = null;
        var path = new Polyline2(pts, !rio);
        if (!rio)
        {
            // Nível padrão: o ponto mais baixo do terreno na margem (a água não transborda); fundo 2 m abaixo (padrão do plugin).
            var zs = pts.Select(p => natural?.Invoke(p)).Where(z => z != null).Select(z => z!.Value).ToList();
            d.NivelAgua = zs.Count > 0 ? Math.Round(zs.Min(), 2) : 0;
            d.CotaFundo = d.NivelAgua - 2;
        }
        if (UiHelpers.ShowModal(Janela(d, path, natural, rio ? "Rio ou córrego" : "Lago ou lagoa", "Criar")) != true) return null;
        UiHelpers.Remember(rio ? "corpo-rio" : "corpo-lago", d);
        d.Output = PluginContext.Settings.NewOutput();
        d.Output.Mode = OutputMode.Modelo3D;
        d.Output.Drape = false;
        d.GroundLine = null;
        d.SetPath(PathReference.FromPoints(pts, 0, closed: !rio));
        var results = MarkingCreator.Commit(uidoc, new MarkingDefinition[] { d }, rio ? "SV - Rio ou córrego" : "SV - Lago ou lagoa");
        if (d.AdjustTerrain) TerrainActions.AfterCreate(uidoc, d);
        return results;
    }
}

/// <summary>Rio ou Córrego: leito por eixo (fundo por estaca, largura, lâmina, taludes), escavado no Toposolid, com a água.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdRioCorrego : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var r = WaterForms.Criar(uidoc, true);
        if (r == null) return Result.Cancelled;
        Report("Rio ou córrego", r);
        return Result.Succeeded;
    }
}

/// <summary>Lago ou Lagoa: margem no nível d'água e cota do fundo, com os taludes das margens, escavado no Toposolid, com a água.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdLagoLagoa : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var r = WaterForms.Criar(uidoc, false);
        if (r == null) return Result.Cancelled;
        Report("Lago ou lagoa", r);
        return Result.Succeeded;
    }
}
