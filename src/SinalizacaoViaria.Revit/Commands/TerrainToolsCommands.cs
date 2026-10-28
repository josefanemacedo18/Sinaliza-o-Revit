using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Terreno natural e atual (cotas absolutas) e a extensão do terreno; erro claro sem Toposolid.</summary>
internal static class TerrainTools
{
    public static (Func<Vec2, double?> Natural, Func<Vec2, double?> Atual, Vec2 Min, Vec2 Max) Superficies(Document doc)
    {
        var atual = TerrainModel.Current(doc) ?? throw new UserMessageException("O projeto não tem Toposolid (Massa e terreno → Sólido topográfico).");
        var natural = TerrainModel.Ground(doc) ?? atual;
        var b = TerrainModel.CurrentMesh(doc)?.Bounds ?? throw new UserMessageException("O Toposolid não tem superfície legível.");
        return (natural, atual, b.Min, b.Max);
    }

    private static readonly System.Globalization.CultureInfo Pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
    public static string N(double v, string f = "#,##0.##") => double.IsNaN(v) ? "—" : v.ToString(f, Pt);

    /// <summary>Retângulo por dois cliques; ESC no primeiro = o terreno inteiro.</summary>
    public static (Vec2 Min, Vec2 Max)? Retangulo(UIDocument uidoc, string what, Vec2 min, Vec2 max)
    {
        var a = Picking.PickPoint(uidoc, $"{what}: clique um canto da área – ESC: o terreno inteiro");
        if (a == null) return (min, max);
        var b = Picking.PickPoint(uidoc, $"{what}: clique o canto oposto – ESC cancela");
        if (b == null) return null;
        var (pa, pb) = (DetailHelpers.ToCore(a), DetailHelpers.ToCore(b));
        return (new Vec2(Math.Min(pa.X, pb.X), Math.Min(pa.Y, pb.Y)), new Vec2(Math.Max(pa.X, pb.X), Math.Max(pa.Y, pb.Y)));
    }

    /// <summary>Polilinha clicada (pontos em m); nula = cancelada.</summary>
    public static List<Vec2>? Pontos(UIDocument uidoc, string prompt, bool closed)
    {
        var r = Picking.PickPolyline(uidoc, prompt, closed, false);
        return r?.Points.Select(DetailHelpers.ToCore).ToList();
    }

    /// <summary>
    /// Tema do terreno na vista ativa (ou a comparação, com <paramref name="diferenca"/>): janela com o mapa, a legenda e o
    /// resumo; ao continuar, as regiões e a legenda viram uma marca do plugin na vista (refeita na atualização).
    /// </summary>
    public static List<RenderResult>? Tema(UIDocument uidoc, bool diferenca, string titulo)
    {
        var doc = uidoc.Document;
        var view = DetailHelpers.RequireDetailView(uidoc);
        var (nat, atu, tmin, tmax) = Superficies(doc);
        if (diferenca && !TerrainModel.Hosts(doc).Any(TerrainModel.HasOriginal))
            throw new UserMessageException("O terreno ainda não foi terraplenado: o natural e o atual são iguais (nada a comparar).");
        var rect = Retangulo(uidoc, titulo, tmin, tmax);
        if (rect == null) return null;
        var d = UiHelpers.Remembered<TemaTerrenoDefinition>(diferenca ? "tema-diferenca" : "tema-terreno") ?? new TemaTerrenoDefinition();
        d = (TemaTerrenoDefinition)d.CloneWithNewId();
        if (diferenca) d.Tema = TemaTerreno.Diferenca;
        else if (d.Tema == TemaTerreno.Diferenca) d.Tema = TemaTerreno.Declividade;
        (d.Min, d.Max) = rect.Value;
        d.Legenda = null;
        var limites = string.Join("; ", d.Limites.Select(x => N(x, "0.###")));
        var scale = Math.Max(1, view.Scale);
        double Mm(double x) => x * scale / 1000.0;

        FormPreview? Preview()
        {
            var (mapa, comp) = TerrainGenerators.Calcular(d, nat, atu);
            var geo = new MarkingGeometry();
            TerrainGenerators.Desenhar(geo, d, mapa, comp, Mm);
            var info = $"{TerrainGenerators.NomeDoTema(d.Tema)}: {N(mapa.AreaTotal, "#,##0")} m² amostrados.";
            if (comp != null) info += $"\nCorte {N(comp.Corte, "#,##0")} m³ · aterro {N(comp.Aterro, "#,##0")} m³ (diferença de {N(comp.Min)} a {N(comp.Max)} m).";
            if (mapa.AreaTotal < 1) info += "\n⚠ Nenhuma célula com terreno na área escolhida.";
            return new FormPreview(geo, null, null, info, Paper: true, ViewScale: scale);
        }

        var w = new FormWindow(titulo, diferenca ? "Comparar superfícies" : "Tema do terreno",
            diferenca
                ? "Diferença entre o terreno atual (com a terraplenagem) e o natural guardado: corte e aterro por classe de profundidade, com os volumes."
                : "Regiões coloridas por classe – elevação, declividade (%) ou orientação das encostas (azimute a partir do Norte do projeto) – com a área de cada uma. " +
                  "As classes padrão são do plugin; informe outros limites se quiser.",
            null, Preview, false, "Criar na vista", 1080, 720);
        if (!diferenca)
            w.Choice("Tema", new[] { ("Elevação", TemaTerreno.Elevacao), ("Declividade (%)", TemaTerreno.Declividade), ("Orientação das encostas", TemaTerreno.Orientacao) },
                    () => d.Tema, v => d.Tema = v, preset: v => { d.Tema = v; d.Limites.Clear(); limites = ""; })
             .Choice("Superfície", new[] { ("Terreno natural (guardado pela terraplenagem)", SuperficieTema.Natural), ("Terreno atual (com a terraplenagem)", SuperficieTema.Atual) },
                    () => d.Superficie, v => d.Superficie = v)
             .If(() => d.Tema == TemaTerreno.Elevacao, f => f.Integer("Intervalos (sem limites informados)", () => d.Classes, v => d.Classes = v, 2, 12));
        w.Text(diferenca ? "Limites das classes (m; ex.: -2; -1; -0,25; 0,25; 1; 2)" : "Limites das classes (vazio = padrão do plugin)", () => limites, v =>
            {
                limites = v ?? "";
                d.Limites = TemasTerreno.LerLimites(limites);
            })
         .Number("Malha (m)", () => d.Passo, v => d.Passo = v, 0.25, 50, "0.0#", "Lado da célula; aumentado sozinho quando a área pede mais de 40 mil células.")
         .Text("Título da legenda (vazio = o do tema)", () => d.Titulo, v => d.Titulo = v ?? "")
         .Number("Altura do texto (mm)", () => d.TextoMm, v => d.TextoMm = v, 1, 10, "0.0");
        if (UiHelpers.ShowModal(w) != true) return null;
        UiHelpers.Remember(diferenca ? "tema-diferenca" : "tema-terreno", d);
        DetailHelpers.PrepareOutput(d, view);
        return MarkingCreator.Commit(uidoc, new MarkingDefinition[] { d }, "SV - " + titulo);
    }
}

/// <summary>Temas do Terreno: elevação, declividade ou orientação em regiões coloridas e legenda na vista ativa.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdTemasTerreno : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var r = TerrainTools.Tema(uidoc, false, "Temas do terreno");
        if (r == null) return Result.Cancelled;
        Report("Temas do terreno", r);
        return Result.Succeeded;
    }
}

/// <summary>Comparar Superfícies: terreno atual × natural, com os volumes e a distribuição da diferença na vista ativa.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCompararSuperficies : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var r = TerrainTools.Tema(uidoc, true, "Comparar superfícies");
        if (r == null) return Result.Cancelled;
        Report("Comparar superfícies", r);
        return Result.Succeeded;
    }
}

/// <summary>Medir no Terreno: distância, desnível e declividades entre dois pontos ou ao longo de um caminho, com o perfil. Não altera nada.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdMedirTerreno : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var (nat, atu, _, _) = TerrainTools.Superficies(doc);
        var pts = TerrainTools.Pontos(uidoc, "Medir no terreno (2 pontos = linha reta; mais = caminho)", false);
        if (pts == null) return Result.Cancelled;
        var exagero = 5.0;
        var passo = 0.5;
        var natural = false;
        FormPreview? Preview()
        {
            var m = MedidasTerreno.Medir(pts, natural ? nat : atu, passo);
            var outra = MedidasTerreno.Medir(pts, natural ? atu : nat, passo);
            var linhas = new List<(string, IReadOnlyList<Vec2>, MarkingColor)> { (natural ? "terreno natural" : "terreno atual", m.Perfil, MarkingColor.Vermelha) };
            if (outra.Perfil.Count >= 2 && outra.Perfil.Zip(m.Perfil).Any(z => Math.Abs(z.First.Y - z.Second.Y) > 0.01))
                linhas.Add((natural ? "terreno atual" : "terreno natural", outra.Perfil, MarkingColor.Concreto));
            var geo = TerrainCharts.Perfil(linhas, exagero, "Perfil do terreno");
            var info = $"Distância horizontal {TerrainTools.N(m.Horizontal)} m · sobre o terreno {TerrainTools.N(m.SobreOTerreno)} m\n" +
                       $"Cotas: início {TerrainTools.N(m.ZInicio ?? double.NaN)} m, fim {TerrainTools.N(m.ZFim ?? double.NaN)} m · desnível {TerrainTools.N(m.Desnivel)} m\n" +
                       $"Declividade início → fim {TerrainTools.N(m.DeclividadeMedia, "0.##")} % · máxima no caminho {TerrainTools.N(m.DeclividadeMaxima, "0.##")} %\n" +
                       $"Subida acumulada {TerrainTools.N(m.Subida)} m · descida {TerrainTools.N(m.Descida)} m" +
                       (m.SemTerreno > 0.01 ? $"\n⚠ {TerrainTools.N(m.SemTerreno)} m do caminho fora do terreno." : "");
            return new FormPreview(geo, null, null, info, Paper: true);
        }
        var w = new FormWindow("Medir no terreno", "Distância e declividade no terreno",
            "Medida ao longo dos pontos clicados (em planta), com o terreno amostrado no passo pedido. Nada é alterado no projeto.", null, Preview, false, "Fechar", 1000, 640);
        w.Check("Medir no terreno natural (em vez do atual)", () => natural, v => natural = v)
         .Number("Passo da amostragem (m)", () => passo, v => passo = v, 0.05, 20, "0.0#")
         .Number("Exagero vertical do perfil", () => exagero, v => exagero = v, 0.2, 50, "0.#");
        UiHelpers.ShowModal(w);
        return Result.Succeeded;
    }
}

/// <summary>
/// Plataforma: área nivelada por um contorno clicado, na cota informada, na média do terreno ou na de equilíbrio entre corte e
/// aterro, com taludes até o terreno – entra na Terraplenagem e no registro dela.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdPlataforma : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var (nat, _, _, _) = TerrainTools.Superficies(doc);
        var pts = TerrainTools.Pontos(uidoc, "Plataforma: contorno", true);
        if (pts == null) return Result.Cancelled;
        var ring = new Polygon2(pts);
        if (Math.Abs(ring.Area) < 1) throw new UserMessageException("O contorno da plataforma tem menos de 1 m².");
        var d = UiHelpers.Remembered<PlataformaDefinition>("plataforma") ?? new PlataformaDefinition();
        d = (PlataformaDefinition)d.CloneWithNewId();
        var media = Plataformas.CotaMedia(ring, nat) ?? throw new UserMessageException("O contorno está fora do terreno.");
        if (d.Modo == CotaPlataforma.Informada && Math.Abs(d.Cota) < 1e-9) d.Cota = Math.Round(media, 2);
        double Cota() => d.Modo switch
        {
            CotaPlataforma.Media => media,
            CotaPlataforma.Equilibrio => Plataformas.CotaEquilibrio(ring, nat, d.Homogeneizacao) ?? media,
            _ => d.Cota,
        };
        FormPreview? Preview()
        {
            var z = Cota();
            var pad = new GradePad(ring, z) { CutSlope = d.CutSlope, FillSlope = d.FillSlope, Daylight = d.Taludes, Owner = "plataforma" };
            var res = Grading.Design(Array.Empty<GradeCorridor>(), new[] { pad }, nat, 0, 60, 0.5, 4, 1);
            Grading.Volumes(res, nat, 1.0);
            var (c, f, a) = Plataformas.Volumes(ring, z, nat);
            var geo = new MarkingGeometry();
            foreach (var fp in PolygonOps.Union(res.Footprint)) geo.Add(fp, MarkingColor.Terra);
            geo.Add(ring, MarkingColor.Concreto);
            var info = $"Cota da plataforma {TerrainTools.N(z)} m (média do terreno {TerrainTools.N(media)} m) · área {TerrainTools.N(a, "#,##0")} m²\n" +
                       $"Dentro do contorno: corte {TerrainTools.N(c, "#,##0")} m³, aterro {TerrainTools.N(f, "#,##0")} m³\n" +
                       $"Com os taludes (estimativa da Terraplenagem): corte {TerrainTools.N(res.CutM3, "#,##0")} m³, aterro {TerrainTools.N(res.FillM3, "#,##0")} m³";
            return new FormPreview(geo, null, null, info);
        }
        var w = new FormWindow("Plataforma", "Plataforma (área nivelada)",
            "A área do contorno fica nivelada na cota escolhida e os taludes descem ou sobem até o terreno. A cota de equilíbrio iguala o corte ao " +
            "aterro × homogeneização dentro do contorno (os taludes ficam fora dessa conta). Os taludes são valores do projeto (geotecnia).",
            null, Preview, false, "Criar", 1000, 660);
        w.Choice("Cota", new[] { ("Informada", CotaPlataforma.Informada), ("Média do terreno no contorno", CotaPlataforma.Media), ("Equilíbrio entre corte e aterro", CotaPlataforma.Equilibrio) },
                () => d.Modo, v => d.Modo = v)
         .If(() => d.Modo == CotaPlataforma.Informada, f => f.Number("Cota da plataforma (m)", () => d.Cota, v => d.Cota = v, -1000, 10000, "0.00"))
         .If(() => d.Modo == CotaPlataforma.Equilibrio, f => f.Number("Homogeneização (aterro / corte)", () => d.Homogeneizacao, v => d.Homogeneizacao = v, 0.5, 2, "0.00"))
         .Check("Taludes até o terreno", () => d.Taludes, v => d.Taludes = v)
         .Number("Talude de corte (H : 1 V)", () => d.CutSlope, v => d.CutSlope = v, 0.1, 10, "0.0#")
         .Number("Talude de aterro (H : 1 V)", () => d.FillSlope, v => d.FillSlope = v, 0.1, 10, "0.0#");
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        d.Cota = Math.Round(Cota(), 3);
        UiHelpers.Remember("plataforma", d);
        d.Output = PluginContext.Settings.NewOutput();
        d.Output.Mode = OutputMode.Modelo3D;
        d.Output.Drape = false;
        d.SetPath(PathReference.FromPoints(pts, d.Cota, closed: true));
        var results = MarkingCreator.Commit(uidoc, new MarkingDefinition[] { d }, "SV - Plataforma");
        TerrainActions.AfterCreate(uidoc, d);
        Report("Plataforma", results);
        return Result.Succeeded;
    }
}

/// <summary>Edição do terreno natural (linha de quebra, suavização) e a terraplenagem refeita sobre ele.</summary>
internal static class NaturalEdit
{
    /// <summary>
    /// Aplica a edição <paramref name="editar"/> aos pontos naturais de cada Toposolid que toca a região, numa transação, e refaz
    /// a terraplenagem (ela parte do terreno natural).
    /// </summary>
    public static Result Aplicar(UIDocument uidoc, string nome, Polygon2 regiao, Func<IReadOnlyList<Vec3>, List<Vec3>> editar)
    {
        var doc = uidoc.Document;
        var report = new TerrainReport();
        using (var t = new Transaction(doc, "SV - " + nome))
        {
            t.Start();
            foreach (var topo in TerrainModel.Hosts(doc))
            {
                var orig = TerrainModel.Original(topo);
                if (orig.Bounds is not { } b || b.Max.X < regiao.Bounds.Min.X || b.Min.X > regiao.Bounds.Max.X || b.Max.Y < regiao.Bounds.Min.Y || b.Min.Y > regiao.Bounds.Max.Y) continue;
                var novos = editar(orig.DistinctPoints());
                TerrainService.EditarNatural(doc, topo, novos, regiao, report);
            }
            if (report.Toposolids == 0) { t.RollBack(); throw new UserMessageException("Nenhum Toposolid sob a região indicada."); }
            t.Commit();
        }
        // A terraplenagem das obras que moldam o terreno é refeita sobre o terreno natural editado.
        var participantes = EarthworkRecord.Participantes(MarkingStorage.Definitions(doc));
        if (participantes.Count > 0) TerrainActions.AfterCreate(uidoc, participantes, quiet: true);
        var msg = $"{nome}: {report.Toposolids} Toposolid(s); {report.Added} ponto(s) inserido(s), {report.Removed} removido(s)." +
                  (participantes.Count > 0 ? $" Terraplenagem refeita ({participantes.Count} obra(s))." : "") +
                  (report.Notes.Count > 0 ? "\n" + string.Join("\n", report.Notes) : "");
        TaskDialog.Show(CommandBase.AppTitle, msg);
        return Result.Succeeded;
    }
}

/// <summary>Linha de Quebra: linha 3D clicada (cotas por vértice) que o terreno natural passa a seguir (crista, pé, margem).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdLinhaQuebra : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var (nat, _, _, _) = TerrainTools.Superficies(doc);
        var pts = TerrainTools.Pontos(uidoc, "Linha de quebra", false);
        if (pts == null) return Result.Cancelled;
        var cotas = pts.Select(p => nat(p) ?? 0).ToList();
        var texto = string.Join("\n", cotas.Select(z => TerrainTools.N(z, "0.00")));
        var folga = 0.5;
        var passo = 1.0;
        List<Vec3> Linha() => pts.Select((p, i) => Vec3.At(p, i < cotas.Count ? cotas[i] : cotas[^1])).ToList();
        FormPreview? Preview()
        {
            var linha = Linha();
            var m = MedidasTerreno.Medir(pts, nat, 0.5);
            var lp = new List<Vec2>();
            double s = 0;
            for (int i = 0; i < linha.Count; i++)
            {
                if (i > 0) s += pts[i - 1].DistanceTo(pts[i]);
                lp.Add(new Vec2(s, linha[i].Z));
            }
            var geo = TerrainCharts.Perfil(new List<(string, IReadOnlyList<Vec2>, MarkingColor)> { ("terreno natural hoje", m.Perfil, MarkingColor.Concreto), ("linha de quebra", lp, MarkingColor.Vermelha) }, 5, "Linha de quebra × terreno natural");
            var dmax = linha.Select((v, i) => Math.Abs(v.Z - (nat(v.XY) ?? v.Z))).DefaultIfEmpty(0).Max();
            return new FormPreview(geo, null, null, $"{pts.Count} vértice(s); maior diferença para o terreno nos vértices {TerrainTools.N(dmax)} m. " +
                "Os pontos do terreno a menos da folga da linha saem e entram os da linha, a cada passo.", Paper: true);
        }
        var w = new FormWindow("Linha de quebra", "Linha de quebra no terreno natural",
            "O terreno natural (o levantamento guardado) passa a seguir a linha: cotas dos vértices, uma por linha (o padrão é o terreno no vértice). " +
            "A terraplenagem das obras é refeita sobre ele.", null, Preview, false, "Aplicar", 1000, 660);
        w.Text("Cotas dos vértices (m, uma por linha)", () => texto, v =>
            {
                texto = v ?? "";
                var lidas = TemasTerreno.LerLimites(texto.Replace("\r", ""));
                if (lidas.Count > 0) cotas = Enumerable.Range(0, pts.Count).Select(i => i < lidas.Count ? lidas[i] : lidas[^1]).ToList();
            }, multiline: true)
         .Number("Folga – pontos do terreno retirados junto à linha (m)", () => folga, v => folga = v, 0.05, 10, "0.0#")
         .Number("Passo dos pontos da linha (m)", () => passo, v => passo = v, 0.1, 10, "0.0#");
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        var linha = Linha();
        var regiao = PolygonOps.Union(Enumerable.Range(0, pts.Count - 1).SelectMany(i => PolygonOps.Strip(new[] { pts[i], pts[i + 1] }, 2 * (folga + 1.0)))).OrderByDescending(p => p.Area).First();
        return NaturalEdit.Aplicar(uidoc, "Linha de quebra", regiao, nats => EdicaoTerreno.ComLinha(nats, linha, folga, passo));
    }
}

/// <summary>Suavizar Terreno: suaviza o terreno natural dentro de um contorno (sem degrau na borda) e refaz a terraplenagem.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdSuavizarTerreno : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        TerrainTools.Superficies(doc);
        var pts = TerrainTools.Pontos(uidoc, "Suavizar terreno: contorno da região", true);
        if (pts == null) return Result.Cancelled;
        var regiao = new Polygon2(pts);
        int passadas = 3;
        double raio = 3.0, peso = 0.5;
        var naturais = TerrainModel.Hosts(doc).SelectMany(t => TerrainModel.Original(t).DistinctPoints()).ToList();
        FormPreview? Preview()
        {
            var dentro = naturais.Where(p => regiao.Contains(p.XY)).ToList();
            var sv = EdicaoTerreno.Suavizar(naturais, regiao, passadas, raio, peso);
            var diffs = naturais.Select((p, i) => sv[i].Z - p.Z).Where(x => Math.Abs(x) > 1e-9).ToList();
            var geo = new MarkingGeometry();
            geo.Add(regiao, MarkingColor.Concreto);
            var info = $"{dentro.Count} ponto(s) do terreno natural dentro do contorno; " +
                       (diffs.Count == 0 ? "nada muda." : $"variação de {TerrainTools.N(diffs.Min())} a {TerrainTools.N(diffs.Max())} m (média {TerrainTools.N(diffs.Average(Math.Abs))} m).");
            return new FormPreview(geo, null, null, info);
        }
        var w = new FormWindow("Suavizar terreno", "Suavizar o terreno natural numa região",
            "Cada ponto do levantamento dentro do contorno vai para a média dos vizinhos (raio), com o peso pedido, em várias passadas; junto ao contorno " +
            "o peso cai a zero – sem degrau. A terraplenagem das obras é refeita sobre o terreno suavizado.", null, Preview, false, "Aplicar", 900, 560);
        w.Integer("Passadas", () => passadas, v => passadas = v, 1, 20)
         .Number("Raio dos vizinhos (m)", () => raio, v => raio = v, 0.2, 50, "0.0#")
         .Number("Peso (0 a 1)", () => peso, v => peso = v, 0, 1, "0.00");
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        return NaturalEdit.Aplicar(uidoc, "Suavizar terreno", regiao, nats => EdicaoTerreno.Suavizar(nats, regiao, passadas, raio, peso));
    }
}

/// <summary>Editar sobre um tema do terreno ou uma plataforma: os mesmos campos da criação.</summary>
internal static class TerrainForms
{
    public static (FormWindow Window, MarkingDefinition Working)? ForEdit(MarkingDefinition def)
    {
        switch (def)
        {
            case TemaTerrenoDefinition tema:
            {
                var d = (TemaTerrenoDefinition)MarkingDefinition.FromJson(tema.ToJson())!;
                var limites = string.Join("; ", d.Limites.Select(x => TerrainTools.N(x, "0.###")));
                var w = new FormWindow("Editar tema do terreno", "Tema do terreno", "O tema é refeito com o terreno de agora.", null, null, false, "Aplicar", 640, 460);
                if (d.Tema != TemaTerreno.Diferenca)
                    w.Choice("Tema", new[] { ("Elevação", TemaTerreno.Elevacao), ("Declividade (%)", TemaTerreno.Declividade), ("Orientação das encostas", TemaTerreno.Orientacao) },
                            () => d.Tema, v => d.Tema = v)
                     .Choice("Superfície", new[] { ("Terreno natural", SuperficieTema.Natural), ("Terreno atual", SuperficieTema.Atual) }, () => d.Superficie, v => d.Superficie = v)
                     .Integer("Intervalos da elevação", () => d.Classes, v => d.Classes = v, 2, 12);
                w.Text("Limites das classes (vazio = padrão do plugin)", () => limites, v => { limites = v ?? ""; d.Limites = TemasTerreno.LerLimites(limites); })
                 .Number("Malha (m)", () => d.Passo, v => d.Passo = v, 0.25, 50, "0.0#")
                 .Text("Título da legenda", () => d.Titulo, v => d.Titulo = v ?? "")
                 .Number("Altura do texto (mm)", () => d.TextoMm, v => d.TextoMm = v, 1, 10, "0.0");
                return (w, d);
            }
            case PlataformaDefinition pl:
            {
                var d = (PlataformaDefinition)MarkingDefinition.FromJson(pl.ToJson())!;
                var w = new FormWindow("Editar plataforma", "Plataforma", "Cota e taludes; a terraplenagem é refeita.", null, null, false, "Aplicar", 600, 420);
                w.Number("Cota da plataforma (m)", () => d.Cota, v => { d.Cota = v; d.Modo = CotaPlataforma.Informada; d.PathRef.Z = v; }, -1000, 10000, "0.00",
                        "A cota passa a ser a informada (a média e o equilíbrio são calculados na criação).")
                 .Check("Taludes até o terreno", () => d.Taludes, v => d.Taludes = v)
                 .Number("Talude de corte (H : 1 V)", () => d.CutSlope, v => d.CutSlope = v, 0.1, 10, "0.0#")
                 .Number("Talude de aterro (H : 1 V)", () => d.FillSlope, v => d.FillSlope = v, 0.1, 10, "0.0#");
                return (w, d);
            }
            default: return null;
        }
    }
}
