using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Opções da terraplenagem.</summary>
public sealed class GradingOptions
{
    /// <summary>Talude de corte padrão (H:V) para vias e conexões.</summary>
    public double CutSlope { get; set; } = 1.0;
    /// <summary>Talude de aterro padrão (H:V) para vias e conexões.</summary>
    public double FillSlope { get; set; } = 1.5;
    /// <summary>Afastamento máximo procurado para o encontro do talude com o terreno (m).</summary>
    public double MaxDaylight { get; set; } = 60;
    /// <summary>Profundidade do subleito sob o topo do pavimento (m) – o terreno fica sob a estrutura do pavimento.</summary>
    public double Subgrade { get; set; } = 0.40;
    /// <summary>Escavar o Toposolid com os elementos enterrados (túneis, caixas de drenagem), quando o Revit permitir.</summary>
    public bool Excavate { get; set; } = true;
    /// <summary>Sem Toposolid no projeto: cria um terreno nativo plano sob as obras (margem de <see cref="NewTerrainMargin"/> m).</summary>
    public bool CreateIfMissing { get; set; }
    public double NewTerrainMargin { get; set; } = 80;
    /// <summary>Grama de taludes e ilhas como subdivisões do Toposolid (material nativo).</summary>
    public bool Finishes { get; set; } = true;
    /// <summary>Só calcular (volumes e relatório), sem alterar o Toposolid.</summary>
    public bool DryRun { get; set; }
    /// <summary>
    /// Fator de homogeneização (volume de corte in situ necessário por m³ de aterro compactado; usual 1,2–1,4 – DNIT).
    /// </summary>
    public double Homogenization { get; set; } = 1.25;
    /// <summary>Mapa de corte (vermelho) e aterro (verde) como subdivisões do Toposolid.</summary>
    public bool CutFillMap { get; set; }
}

public sealed class TerrainReport
{
    public int Toposolids { get; set; }
    public int Removed { get; set; }
    public int Added { get; set; }
    public int Excavated { get; set; }
    public int Subdivisions { get; set; }
    public bool Created { get; set; }
    public int Skipped { get; set; }
    public int Checked { get; set; }
    public double MaxDeviation { get; set; }
    public double Cut { get; set; }
    public double Fill { get; set; }
    public double Area { get; set; }
    public List<string> Notes { get; } = new();
    public bool DryRun { get; set; }
    public double Homogenization { get; set; } = 1.25;
    /// <summary>Simulação × terraplenagem aplicada (registro), quando há registro com volumes.</summary>
    public string? Comparison { get; set; }
    /// <summary>Volumes de cada elemento: a parte dele no projeto conjunto (GradingResult.ByOwner) – a soma é o total.</summary>
    public List<(string Name, double Cut, double Fill, double Area)> Items { get; } = new();

    /// <summary>Balanço de massas: corte disponível menos o corte necessário para os aterros (× fator de homogeneização).</summary>
    public double Balance => Cut - Fill * Homogenization;

    public string Text()
    {
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        string N(double v) => v.ToString("N0", pt);
        var s = DryRun
            ? "SIMULAÇÃO – o terreno NÃO foi alterado.\n"
            : $"Toposolid(s) ajustado(s): {Toposolids} · pontos removidos / inseridos: {Removed} / {Added}\n";
        s += $"Área terraplenada: {N(Area)} m²\n" +
             $"Corte: {N(Cut)} m³ · Aterro (compactado): {N(Fill)} m³\n" +
             $"Corte necessário para os aterros (× {Homogenization.ToString("0.00", pt)}): {N(Fill * Homogenization)} m³\n" +
             (Balance >= 0
                 ? $"Balanço: sobram {N(Balance)} m³ de corte → BOTA-FORA (ou aproveitar em outros aterros)"
                 : $"Balanço: faltam {N(-Balance)} m³ → EMPRÉSTIMO (jazida)");
        if (Items.Count > 1)
        {
            s += "\n\nPor elemento (corte / aterro, m³):";
            foreach (var it in Items.OrderByDescending(i => i.Cut + i.Fill).Take(15))
                s += $"\n  • {it.Name}: {N(it.Cut)} / {N(it.Fill)}";
            if (Items.Count > 15) s += $"\n  … e mais {Items.Count - 15}";
        }
        if (Created) s = "Toposolid criado sob as obras (Massa e terreno).\n" + s;
        if (Checked > 0) s += $"\nConferência no terreno real: {Checked} pontos, desvio máximo {MaxDeviation.ToString("0.00", pt)} m";
        if (Skipped > 0) s += $"\nPontos recusados pelo Revit: {Skipped}";
        if (Excavated > 0) s += $"\nEscavações pelo modelo (túneis, caixas): {Excavated}";
        if (Subdivisions > 0) s += $"\nAcabamentos do terreno (subdivisões com grama/revestimento): {Subdivisions}";
        if (Comparison != null) s += "\n\n" + Comparison;
        if (Notes.Count > 0) s += "\n\n" + string.Join("\n", Notes.Distinct().Take(12));
        return s;
    }
}

/// <summary>
/// Terraplenagem no terreno nativo do Revit (Massa e terreno → Toposolid): as vias, conexões e obras ficam no seu greide e o
/// Toposolid é ajustado a elas – pontos do terreno dentro da área de projeto são substituídos pela superfície de projeto
/// (plataformas + taludes de corte e aterro até encontrar o terreno natural). Também escava o terreno com elementos
/// enterrados quando o Revit permite.
/// </summary>
public static class TerrainService
{
    /// <summary>Faixas e plataformas de projeto de uma marca (cotas absolutas, m).</summary>
    public static (List<GradeCorridor> Corridors, List<GradePad> Pads) Features(Document doc, MarkingDefinition def, MarkingGeometry geo, double baseZ, GradingOptions opt,
        IReadOnlyCollection<MarkingDefinition>? all = null, MarkingService? service = null)
    {
        var corridors = new List<GradeCorridor>();
        var pads = new List<GradePad>();
        var z0 = baseZ + def.Output.ElevationOffset;
        foreach (var c in geo.Corridors)
        {
            // Todas as marcações da faixa vão junto (degraus de muro, testa de emboque, saia) – não só os taludes.
            var a = new GradeCorridor
            {
                CutSlope = c.CutSlope, FillSlope = c.FillSlope, DaylightLeft = c.DaylightLeft, DaylightRight = c.DaylightRight, Label = c.Label,
                WallLeft = c.WallLeft, WallRight = c.WallRight, WallStart = c.WallStart, WallEnd = c.WallEnd, WallCap = c.WallCap, EndSpill = c.EndSpill,
                Owner = def.Id,
            };
            a.Left.AddRange(c.Left.Select(p => p with { Z = p.Z + baseZ }));
            a.Right.AddRange(c.Right.Select(p => p with { Z = p.Z + baseZ }));
            a.Inner.AddRange(c.Inner.Select(l => l.Select(p => p with { Z = p.Z + baseZ }).ToList()));
            corridors.Add(a);
        }
        pads.AddRange(geo.Pads.Select(p => new GradePad(p.Area, p.Z + baseZ)
        {
            CutSlope = p.CutSlope, FillSlope = p.FillSlope, Daylight = p.Daylight, Label = p.Label,
            ZAt = p.ZAt == null ? null : q => p.ZAt(q) + baseZ, Owner = def.Id,
        }));
        switch (def)
        {
            case RoadPavementDefinition road when PathResolver.Resolve(doc, road.Path)?.Main is { } axis:
            {
                // Via: plataforma de alinhamento a alinhamento, no greide, por camadas (rodada VT) – a estrutura do pavimento só sob a
                // pista, o fundo das lajes sob meio-fio e calçada e o topo da calçada no alinhamento, de onde o talude parte –, fora dos
                // trechos em obra (sob pontes e viadutos o terreno fica natural; túneis e trincheiras cuidam do próprio trecho).
                var g = road.Output.Grade;
                var prof = g != null ? g.ToProfile(2) : VerticalProfile.Flat(0);
                var cut = g?.CutSlope ?? opt.CutSlope;
                var fill = g?.FillSlope ?? opt.FillSlope;
                var right = g != null ? -road.TotalRight : -(road.TotalRight + 0.3);
                var left = g != null ? road.TotalLeft : road.TotalLeft + 0.3;
                var s0 = Math.Max(0, road.StartSetback);
                var s1 = Math.Max(s0 + 0.5, axis.Length - Math.Max(0, road.EndSetback));
                var works = (all ?? Array.Empty<MarkingDefinition>()).Where(h => road.GroupId != null && h is IHostedStructure hs && hs.HostRoad == road.GroupId);
                // Fundo da estrutura sob a pista: a estrutura do pavimento da via (revestimento + camadas, Etapa 5) ou o subleito padrão.
                var depth = EarthworkRecord.Profundidade(road, opt.Subgrade);
                // Fora dos trechos em obra; junto a encontros e emboques a via termina numa face vertical (emboque: testa + talude).
                var roadCorr = InfraRoads.RoadCorridors(axis, prof, right, left, s0, s1, depth, cut, fill, works);
                // A plataforma acompanha a mesma superfície dos pisos (abaulamento, superelevação e concordância com os nós).
                if (g != null)
                {
                    var surf = service?.SurfaceFor(road, axis) ?? new Core.Geometry.GradeSurface(axis, g);
                    InfraRoads.FitToSection(roadCorr, left, right, surf, InfraRoads.Edges(road, axis), depth, InfraRoads.SectionBreaks(surf.Grade));
                    // Nós em que a via é secundária: a crista da via principal, prolongada, cruza a concordância.
                    foreach (var b in surf.Blends)
                        foreach (var c in roadCorr) c.Breaklines.AddRange(InfraRoads.NodeBreaklines(b.Node));
                }
                foreach (var c in roadCorr) c.Owner = road.Id;
                corridors.AddRange(roadCorr.Select(c => c.WithOffset(z0)));
                break;
            }
            case IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition:
            {
                // Conexões: plataforma com o contorno de todas as peças (calçadas incluídas), na superfície costurada ao greide das
                // vias que chegam (plana quando nenhuma tem greide), sob a estrutura do pavimento.
                var node = service?.NodeFor(def);
                var onGround = geo.Pieces.Where(p => p.Elevation < 0.5).ToList();
                var union = Core.Geometry.PolygonOps.Union(onGround.Select(p => p.Shape));
                var (cutS, fillS) = RoadSlopes(def, all, opt);
                // Estrutura sob a pista do nó: a mais funda entre as vias que chegam (cada uma pela estrutura dela ou o subleito padrão).
                var depth = EarthworkRecord.ProfundidadeDoNo(def, all ?? Array.Empty<MarkingDefinition>(), opt.Subgrade);
                // Por camadas: estrutura sob a pista do nó, fundo das lajes sob calçadas e ilhas, topo das calçadas na quina do lote.
                var (paved, rise) = InfraRoads.PadLayers(onGround);
                foreach (var u in union.Where(u => u.Area > 1))
                {
                    var pad = new GradePad(u, z0 - depth)
                    {
                        CutSlope = cutS, FillSlope = fillS, Label = def.KindName,
                        ZAt = node == null ? null : q => node.Z(q) - depth,
                        Structure = depth, EdgeRise = rise, Owner = def.Id,
                    };
                    pad.Paved.AddRange(paved);
                    // Crista e fim do caimento da via principal: pontos do Toposolid sobre as quebras da superfície do nó.
                    if (node != null) pad.Breaklines.AddRange(InfraRoads.NodeBreaklines(node));
                    pads.Add(pad);
                }
                break;
            }
            case var _ when service?.MemberSurface(def) is { } members && service.NodeFor(def) is { } memberNode:
            {
                // Marcas do nó no chão (calçadas de esquina, extensões, rampas): plataforma na superfície delas – a do nó dentro
                // dele e a da via além dele –, sem talude próprio (ficam dentro das plataformas do nó e das vias).
                var onGround = geo.Pieces.Where(p => p.Elevation < 0.5).ToList();
                var union = Core.Geometry.PolygonOps.Union(onGround.Select(p => p.Shape));
                var (paved, rise) = InfraRoads.PadLayers(onGround);
                var nodeDef = service.NodeOwner(def);
                var owner = nodeDef?.Id ?? def.Id;
                var depth = nodeDef == null ? opt.Subgrade : EarthworkRecord.ProfundidadeDoNo(nodeDef, all ?? Array.Empty<MarkingDefinition>(), opt.Subgrade);
                foreach (var u in union.Where(u => u.Area > 0.05))
                {
                    var pad = new GradePad(u, z0 - depth)
                    {
                        Daylight = false, Label = def.KindName, ZAt = q => members(q) - depth,
                        Structure = depth, EdgeRise = rise, Owner = owner,
                    };
                    pad.Paved.AddRange(paved);
                    pad.Breaklines.AddRange(InfraRoads.NodeBreaklines(memberNode));
                    pads.Add(pad);
                }
                break;
            }
        }
        return (corridors, pads);
    }

    /// <summary>
    /// Opções da última terraplenagem aplicada (registro, Etapa 5): subleito, taludes padrão, alcance e homogeneização. Sem
    /// registro, as padrão. Assim o quantitativo usa as mesmas opções do terreno aplicado.
    /// </summary>
    public static GradingOptions AppliedOptions(Document doc)
    {
        var o = new GradingOptions();
        if (EarthworkRecordStore.Load(doc) is not { } r) return o;
        o.Subgrade = r.Subleito;
        o.CutSlope = r.TaludeCorte;
        o.FillSlope = r.TaludeAterro;
        if (r.AlcanceTaludes is { } d) o.MaxDaylight = d;
        if (r.Homogeneizacao is { } h) o.Homogenization = h;
        return o;
    }

    /// <summary>
    /// Terraplenagem para o quantitativo (rodada VT): o mesmo projeto da Terraplenagem – todas as vias e obras que moldam o
    /// terreno, por camadas – contra o terreno original guardado no Toposolid (sem a terraplenagem aplicada, o terreno atual é
    /// o natural): corte, aterro e área de cada elemento (a parte dele no projeto conjunto) e os totais. Calculado na hora, nada
    /// é gravado; vazio sem Toposolid ou sem obras que moldem o terreno. Usa os taludes do greide de cada via, a estrutura do
    /// pavimento de cada via e, sem <paramref name="opt"/>, as opções da última terraplenagem aplicada (<see cref="AppliedOptions"/>).
    /// </summary>
    /// <param name="sections">Quando dado, recebe os volumes por estaca de cada via (método das seções sobre a mesma superfície
    /// de projeto, só na parte que é da via – F7b), com a parte dela na malha para conferir.</param>
    public static (List<EarthworkItem> Items, double Cut, double Fill) Quantities(Document doc, MarkingService service, IReadOnlyCollection<MarkingDefinition> all,
        GradingOptions? opt = null, List<SecoesDaVia>? sections = null)
    {
        opt ??= AppliedOptions(doc);
        var items = new List<EarthworkItem>();
        var topos = TerrainModel.Hosts(doc);
        if (topos.Count == 0) return (items, 0, 0);
        var list = Participants(doc, all);
        if (list.Count == 0) return (items, 0, 0);
        var corridors = new List<GradeCorridor>();
        var pads = new List<GradePad>();
        var defs = new List<MarkingDefinition>();
        foreach (var def in list)
        {
            try
            {
                var geo = service.BuildGeometry(def, out var baseZ);
                var (c, p) = Features(doc, def, geo, baseZ, opt, all, service);
                corridors.AddRange(c);
                pads.AddRange(p);
                if (c.Count + p.Count > 0) defs.Add(def);
            }
            catch (Exception ex) { Log.Error($"Quantitativo de terraplenagem {def.DisplayCode}", ex); }
        }
        if (defs.Count == 0) return (items, 0, 0);
        var originals = topos.Select(TerrainModel.Original).Where(m => m.Count > 0).ToList();
        if (originals.Count == 0) return (items, 0, 0);
        double? Ground(Vec2 p)
        {
            double? best = null;
            foreach (var m in originals) if (m.Z(p) is { } z && (best == null || z > best)) best = z;
            return best;
        }
        var design = Grading.Design(corridors, pads, Ground, 0, Math.Max(5, opt.MaxDaylight), 0.5, 1.0, 1.0);
        var count = new Dictionary<string, int>();
        foreach (var d in defs)
        {
            if (!design.ByOwner.TryGetValue(d.Id, out var v) || v.Cut + v.Fill < 1e-3) continue;
            var kind = d is RoadPavementDefinition ? "Via" : d.KindName;
            count[kind] = count.GetValueOrDefault(kind) + 1;
            var label = d.Hierarchy is { } h && h != HierarquiaViaria.NaoDefinida ? $" {Hierarquia.Label(h).ToLowerInvariant()}" : "";
            items.Add(new EarthworkItem($"{kind}{label} {count[kind]}", d.Hierarchy, v.Cut, v.Fill, v.Area));
            if (sections != null && d is RoadPavementDefinition road && PathResolver.Resolve(doc, road.Path)?.Main is { Length: > 1 } axis)
            {
                try
                {
                    // Seções a cada 20 m, cobrindo a plataforma e o alcance dos taludes.
                    var half = Math.Max(road.TotalLeft, road.TotalRight) + Math.Max(5, opt.MaxDaylight);
                    var vol = SectionVolumes.ForOwner(design, d.Id, axis, Ground, half, 20, 0.25);
                    sections.Add(new SecoesDaVia(items[^1].Name, d.Hierarchy, vol, v.Cut, v.Fill, road.Estaqueamento));
                }
                catch (Exception ex) { Log.Error($"Volumes por estaca {d.DisplayCode}", ex); }
            }
        }
        return (items, design.CutM3, design.FillM3);
    }

    /// <summary>Vias ligadas a um nó (interseção, rotatória, cul-de-sac).</summary>
    public static IEnumerable<string> NodeRoads(MarkingDefinition d) => EarthworkRecord.NodeRoads(d);

    /// <summary>Taludes do nó: os das vias ligadas (o mais suave), senão os padrões.</summary>
    private static (double Cut, double Fill) RoadSlopes(MarkingDefinition d, IReadOnlyCollection<MarkingDefinition>? all, GradingOptions opt)
    {
        var ids = NodeRoads(d).ToHashSet();
        var grades = (all ?? Array.Empty<MarkingDefinition>()).OfType<RoadPavementDefinition>().Where(r => ids.Contains(r.Id) && r.Output.Grade != null)
            .Select(r => r.Output.Grade!).ToList();
        return grades.Count == 0 ? (opt.CutSlope, opt.FillSlope) : (grades.Max(g => g.CutSlope), grades.Max(g => g.FillSlope));
    }

    /// <summary>Marcas que podem participar da terraplenagem.</summary>
    public static bool Grades(MarkingDefinition d) =>
        d is ITerrainAware or RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition or DrainageDefinition;

    /// <summary>
    /// Marcas que moldam o terreno de fato: obras com "ajustar o terreno", vias com greide (e as conexões nas pontas delas) e
    /// caixas de drenagem (escavação).
    /// </summary>
    public static List<MarkingDefinition> Participants(Document doc, IReadOnlyCollection<MarkingDefinition> all)
    {
        _ = doc;
        // Nós (interseções, rotatórias, cul-de-sacs) moldam o terreno quando QUALQUER via ligada a eles molda – antes só os
        // que ficavam perto das pontas das vias entravam, e os cruzamentos no meio das vias ficavam sem corte/aterro. A escolha
        // fica no núcleo (EarthworkRecord), a mesma do registro da terraplenagem e da V15.
        return EarthworkRecord.Participantes(all);
    }

    /// <summary>
    /// Refaz o terreno nativo (Toposolid) com TODAS as obras que o moldam, a partir do terreno original: pontos dentro da área
    /// terraplenada (atual e anterior) são substituídos pelos pontos originais fora do projeto + a superfície de projeto.
    /// <paramref name="changed"/> traz as definições recém-editadas (ainda não gravadas). Deve ser chamado dentro de uma transação.
    /// </summary>
    public static TerrainReport Apply(Document doc, MarkingService service, IEnumerable<MarkingDefinition> changed, GradingOptions opt)
    {
        var report = new TerrainReport();
        var byId = new Dictionary<string, MarkingDefinition>();
        foreach (var d in MarkingStorage.Definitions(doc)) byId[d.Id] = d;
        foreach (var d in changed) byId[d.Id] = d;
        var all = byId.Values.ToList();
        var list = Participants(doc, all);
        if (TerrainModel.Hosts(doc).Count == 0)
        {
            if (!opt.CreateIfMissing || !CreateTerrain(doc, service, list, opt, report))
            {
                if (!report.Notes.Any())
                    report.Notes.Add("Nenhum Toposolid no projeto (Massa e terreno → Sólido topográfico). Crie o terreno ou use Terraplenagem com \"criar terreno\".");
                if (new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Topography).WhereElementIsNotElementType().Any())
                    report.Notes.Add("Há uma superfície topográfica antiga: converta-a em Sólido topográfico (Massa e terreno → Sólido topográfico → Criar a partir da topografia).");
                return report;
            }
            service.InvalidateTerrain();
        }

        report.DryRun = opt.DryRun;
        report.Homogenization = opt.Homogenization;
        // Superfície suavizada do Toposolid arredonda as faces de corte/aterro e os degraus dos muros: desligada para a terraplenagem.
        try
        {
            if (!opt.DryRun && Toposolid.IsSmoothedSurfaceEnabled(doc))
            {
                Toposolid.SetSmoothedSurface(doc, false);
                report.Notes.Add("A \"superfície suavizada\" dos Toposolids foi desligada (ela arredonda os taludes e as faces dos muros).");
            }
        }
        catch (Exception ex) { Log.Error("Superfície suavizada", ex); }

        var corridors = new List<GradeCorridor>();
        var pads = new List<GradePad>();
        var excavators = new List<ElementId>();
        var finishes = new List<(Polygon2 Area, MarkingColor Color, string Id)>();
        var perDef = new List<(MarkingDefinition Def, List<GradeCorridor> C, List<GradePad> P)>();
        // Marcas cuja geometria falhou: não entram no registro (a V15 continua dizendo que não foram aplicadas).
        var notApplied = new HashSet<string>();
        foreach (var def in list)
        {
            try
            {
                var geo = service.BuildGeometry(def, out var baseZ);
                var (c, p) = Features(doc, def, geo, baseZ, opt, all, service);
                corridors.AddRange(c);
                pads.AddRange(p);
                if (c.Count + p.Count > 0) perDef.Add((def, c, p));
                finishes.AddRange(geo.TerrainFinishes.Select(f => (f.Area, f.Color, def.Id)));
                if (opt.Excavate && def is TunnelDefinition or DrainageDefinition)
                {
                    var els = MarkingStorage.ById(doc, def.Id).Select(r => r.Element).ToList();
                    // Túnel: a Massa de escavação (envoltória do revestimento) abre o furo inteiro no terreno.
                    var bores = els.Where(MarkingService.IsBore).ToList();
                    excavators.AddRange((bores.Count > 0 ? bores : els.Where(e => e is DirectShape)).Select(e => e.Id));
                }
            }
            catch (Exception ex)
            {
                notApplied.Add(def.Id);
                Log.Error($"Terraplenagem {def.DisplayCode}", ex);
                report.Notes.Add($"{def.DisplayCode}: {ex.Message}");
            }
        }

        var topos = TerrainModel.Hosts(doc);
        var originals = topos.ToDictionary(t => t.Id, TerrainModel.Original);
        double? Ground(Vec2 p)
        {
            double? best = null;
            foreach (var m in originals.Values) if (m.Z(p) is { } z && (best == null || z > best)) best = z;
            return best;
        }
        var design = Grading.Design(corridors, pads, Ground, 0, Math.Max(5, opt.MaxDaylight), 0.5, 1.0, 1.0);
        report.Cut = design.CutM3;
        report.Fill = design.FillM3;
        report.Area = design.AreaM2;
        report.Notes.AddRange(design.Warnings);
        // Fundo da caixa sob a pista (Etapa 5): a estrutura do pavimento de cada via ou o subleito das opções.
        var roads = list.OfType<RoadPavementDefinition>().ToList();
        if (roads.Count > 0)
        {
            var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            var withStructure = roads.Count(r => r.ProfundidadeEstrutura != null);
            report.Notes.Add(withStructure == 0
                ? $"Fundo da caixa sob a pista: subleito de {opt.Subgrade.ToString("0.00", pt)} m em todas as vias (nenhuma com estrutura do pavimento informada)."
                : $"Fundo da caixa sob a pista: estrutura do pavimento em {withStructure} de {roads.Count} via(s); subleito de {opt.Subgrade.ToString("0.00", pt)} m nas demais.");
        }
        // Volumes por elemento: a parte de cada um no projeto conjunto (a soma é o total). Antes cada um era projetado isolado e as
        // áreas comuns – o nó e o começo das vias que chegam nele – contavam duas vezes.
        if (perDef.Count > 1)
            foreach (var (d, _, _) in perDef)
                if (design.ByOwner.TryGetValue(d.Id, out var v))
                    report.Items.Add((d is RoadPavementDefinition rp ? $"Via {rp.DisplayCode}" : d.KindName, v.Cut, v.Fill, v.Area));
        if (opt.DryRun)
        {
            // Alternativa simulada × terraplenagem aplicada (Motores §8).
            var applied = EarthworkRecordStore.Load(doc);
            report.Comparison = EarthworkRecord.Comparacao(applied, report.Cut, report.Fill, opt.Homogenization,
                applied == null ? 0 : EarthworkRecord.Comparar(applied, all).Count);
            return report;
        }
        var footprint = design.Footprint.Count == 0 ? new List<Polygon2>() : PolygonOps.Union(design.Footprint).Where(f => f.Area > 0.5).ToList();

        var failed = false;
        foreach (var topo in topos)
        {
            var orig = originals[topo.Id];
            var last = TerrainModel.LastFootprint(topo);
            var region = footprint.Concat(last).ToList();
            if (region.Count == 0 || orig.Count == 0) continue;
            if (!Overlaps(topo, Bounds(region))) continue;
            try
            {
                var editor = topo.GetSlabShapeEditor();
                if (editor == null) continue;
                if (!editor.IsEnabled) editor.Enable();
                // 1 m: os pontos naturais colados na área de projeto saem também do Toposolid (ver TerrainRebuild.Points).
                var inRegion = TerrainRebuild.Mask(region, 1.0);
                var pts = TerrainRebuild.Points(orig, design, inRegion);
                ApplyPoints(doc, topo, editor, inRegion, pts, report);
                report.Toposolids++;
                TerrainModel.Save(topo, orig, footprint);
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Terraplenagem do Toposolid", ex);
                report.Notes.Add($"Toposolid {topo.Id}: {ex.Message}");
            }
        }
        service.InvalidateTerrain();
        // Registro da terraplenagem aplicada (Etapa 5): quem moldou o terreno e com que assinatura, o subleito e os taludes. Na
        // mesma transação – desfazer a terraplenagem desfaz o registro. Com falha num Toposolid o registro antigo fica (a V15
        // continua acusando o que não foi aplicado).
        if (!failed)
        {
            try
            {
                var rec = EarthworkRecord.Gravar(list.Where(d => !notApplied.Contains(d.Id)), opt.Subgrade, opt.CutSlope, opt.FillSlope, DateTime.Now,
                    typeof(TerrainService).Assembly.GetName().Version?.ToString(3), report.Cut, report.Fill, report.Area, opt.Homogenization);
                rec.AlcanceTaludes = opt.MaxDaylight;
                EarthworkRecordStore.Save(doc, rec);
            }
            catch (Exception ex) { Log.Error("Registro da terraplenagem", ex); }
        }

        // 3) Escavação pelo modelo (túneis, caixas de drenagem) – quando o Revit aceita o elemento.
        if (opt.Excavate && excavators.Count > 0)
        {
            var refused = 0;
            foreach (var id in excavators)
            {
                if (doc.GetElement(id) is not { } el) continue;
                foreach (var topo in topos.Where(tp => Overlaps(tp, ElementBounds(el))))
                {
                    try
                    {
                        if (Excavates(topo, id)) { report.Excavated++; continue; }
                        if (topo.CanBeExcavatedBy(id)) { topo.ExcavateBy(id); report.Excavated++; continue; }
                        // Massa recusada: cópia como Modelo genérico (oculta nas vistas) só para escavar.
                        if (MarkingService.IsBore(el) && GenericCopy(doc, el) is { } copy && topo.CanBeExcavatedBy(copy))
                        {
                            topo.ExcavateBy(copy);
                            report.Excavated++;
                            continue;
                        }
                        refused++;
                    }
                    catch (Exception ex)
                    {
                        refused++;
                        Log.Error("Toposolid.ExcavateBy", ex);
                    }
                }
            }
            if (report.Excavated == 0 && refused > 0 && list.Any(d => d is TunnelDefinition))
                report.Notes.Add("O Revit não permitiu escavar o Toposolid com o volume do túnel. Os emboques foram cortados; para abrir o furo " +
                                 "selecione o Toposolid → Escavar e clique a Massa \"SV … escavação do terreno\" (ative Mostrar massa).");
        }
        if (opt.CutFillMap)
        {
            try
            {
                var (cutR, fillR) = Grading.CutFillRegions(design, Ground, 2.0, 0.10);
                var taken = finishes.Select(f => f.Area).ToList();
                foreach (var (regions, color, key) in new[] { (cutR, MarkingColor.Vermelha, "mapa-corte"), (fillR, MarkingColor.Verde, "mapa-aterro") })
                    foreach (var r in taken.Count > 0 ? PolygonOps.Difference(regions, taken) : regions)
                        if (r.Area > 4) finishes.Add((r, color, key));
                report.Notes.Add($"Mapa de corte e aterro: {cutR.Count} região(ões) de corte (vermelho) e {fillR.Count} de aterro (verde) como subdivisões do Toposolid.");
            }
            catch (Exception ex) { Log.Error("Mapa de corte e aterro", ex); }
        }
        if (opt.Finishes || opt.CutFillMap) ApplyFinishes(doc, opt.Finishes ? finishes : finishes.Where(f => f.Id.StartsWith("mapa-")).ToList(), report, list.Select(d => d.Id).ToHashSet());
        return report;
    }

    private static (Vec2 Min, Vec2 Max)? ElementBounds(Element e)
    {
        var bb = e.get_BoundingBox(null);
        return bb == null ? null : (new Vec2(UnitConv.M(bb.Min.X), UnitConv.M(bb.Min.Y)), new Vec2(UnitConv.M(bb.Max.X), UnitConv.M(bb.Max.Y)));
    }

    /// <summary>O elemento já escava o Toposolid?</summary>
    private static bool Excavates(Toposolid topo, ElementId id)
    {
        try
        {
            foreach (var d in topo.GetIntersectingElementData())
                if (d.IntersectingElementId == id && d.IntersectionType == IntersectionType.Excavate) return true;
        }
        catch { /* versão sem os dados de interseção */ }
        return false;
    }

    /// <summary>Cópia do volume de escavação como Modelo genérico, oculta em todas as vistas (só escava o terreno).</summary>
    private static ElementId? GenericCopy(Document doc, Element bore)
    {
        try
        {
            var tag = $"SV escavação (cópia) {bore.UniqueId}";
            var old = new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == tag).ToList();
            foreach (var o in old) try { doc.Delete(o.Id); } catch { /* ignorado */ }
            var geom = new List<GeometryObject>();
            if (bore.get_Geometry(new Options()) is { } ge) foreach (var o in ge) if (o is Solid s && s.Volume > 1e-6) geom.Add(s);
            if (geom.Count == 0) return null;
            var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = "SinalizacaoViaria";
            ds.ApplicationDataId = bore.UniqueId;
            ds.SetShape(geom);
            ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(tag);
            try { ds.SetName("SV escavação do terreno"); } catch { /* opcional */ }
            foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate))
                try { if (ds.CanBeHidden(v)) v.HideElements(new List<ElementId> { ds.Id }); } catch { /* vista sem suporte */ }
            doc.Regenerate();
            return ds.Id;
        }
        catch (Exception ex)
        {
            Log.Error("Cópia do volume de escavação", ex);
            return null;
        }
    }

    /// <summary>Esquece o terreno original dos Toposolids (o atual vira o terreno natural). Exige transação.</summary>
    public static int ResetOriginal(Document doc)
    {
        var n = 0;
        foreach (var t in TerrainModel.Hosts(doc)) if (TerrainModel.HasOriginal(t)) { TerrainModel.Reset(t); n++; }
        return n;
    }

    /// <summary>
    /// Troca os pontos do Toposolid na região: apaga os vértices de dentro, insere os pontos novos (em lotes) e CONFERE o
    /// resultado pela geometria real do Toposolid (cota absoluta do topo nos pontos inseridos). Se o Revit tiver lido as cotas
    /// com um deslocamento constante (nível/deslocamento do Toposolid), os pontos são refeitos com a correção.
    /// </summary>
    private static void ApplyPoints(Document doc, Toposolid topo, SlabShapeEditor editor, Func<Vec2, bool> inRegion, List<Vec3> pts, TerrainReport report)
    {
        static (long, long) Key(double xFt, double yFt) => ((long)Math.Round(xFt * 20), (long)Math.Round(yFt * 20));
        // 1) Sai tudo o que está na região (pontos de terraplenagens passadas e do levantamento).
        // Posições lidas antes de apagar (os objetos de vértice podem deixar de valer depois de cada exclusão).
        var vertices = new List<(SlabShapeVertex V, XYZ P)>();
        foreach (SlabShapeVertex v in editor.SlabShapeVertices) vertices.Add((v, v.Position));
        var kept = new HashSet<(long, long)>();
        foreach (var (v, pos) in vertices)
        {
            var xy = new Vec2(UnitConv.M(pos.X), UnitConv.M(pos.Y));
            if (!inRegion(xy)) { kept.Add(Key(pos.X, pos.Y)); continue; }
            var ok = false;
            try { ok = v.IsValidObject && editor.DeletePoint(v); } catch { /* vértice do contorno */ }
            if (ok) report.Removed++;
            else kept.Add(Key(pos.X, pos.Y));
        }
        // 2) Entram os pontos novos – distintos em planta e fora dos vértices que ficaram (o Revit recusa repetidos).
        var add = new List<XYZ>();
        foreach (var p in pts)
        {
            var q = new XYZ(UnitConv.Ft(p.X), UnitConv.Ft(p.Y), UnitConv.Ft(p.Z));
            if (kept.Add(Key(q.X, q.Y))) add.Add(q);
        }
        if (add.Count == 0) return;
        // O topo não pode descer abaixo do fundo do Toposolid ("muito fino para seu tipo"): engrossa o tipo antes, se preciso.
        EnsureThickness(doc, topo, add.Min(q => q.Z), report);
        AddPoints(editor, add, report, true);
        doc.Regenerate();

        // 3) Conferência pela geometria (cota absoluta real do topo do Toposolid nos pontos inseridos).
        var (median, spread, n) = Check(topo, add, 0);
        if (n == 0) { report.Notes.Add("Aviso: não foi possível conferir o Toposolid depois da terraplenagem (geometria não lida)."); return; }
        if (Math.Abs(median) > 0.02 && spread < 0.10)
        {
            // Deslocamento constante: refaz os pontos com a correção.
            var wanted = add.Select(q => Key(q.X, q.Y)).ToHashSet();
            var redo = new List<SlabShapeVertex>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                if (wanted.Contains(Key(v.Position.X, v.Position.Y))) redo.Add(v);
            foreach (var v in redo) try { if (v.IsValidObject) editor.DeletePoint(v); } catch { /* ignorado */ }
            var fixedPts = add.Select(q => new XYZ(q.X, q.Y, q.Z - UnitConv.Ft(median))).ToList();
            AddPoints(editor, fixedPts, report, false);
            doc.Regenerate();
            var (m2, s2, n2) = Check(topo, add, 0);
            report.Notes.Add($"Cotas dos pontos do Toposolid corrigidas em {-median:+0.00;-0.00} m (o Revit as lia relativas ao nível/deslocamento do Toposolid)." +
                             (n2 > 0 ? $" Conferência depois da correção: desvio médio {m2 * 100:0} cm." : ""));
            median = m2; spread = s2; n = n2;
        }
        report.Checked += n;
        report.MaxDeviation = Math.Max(report.MaxDeviation, Math.Abs(median) + spread);
        if (Math.Abs(median) > 0.05 || spread > 0.25)
            report.Notes.Add($"Aviso: o topo do Toposolid ficou até {Math.Abs(median) + spread:0.00} m fora da superfície de projeto em parte dos pontos – " +
                             "confira se o Toposolid tem \"superfície suavizada\" ativa ou pontos de outra fonte na área.");
    }

    /// <summary>
    /// Garante que o fundo do Toposolid fique pelo menos 1,5 m abaixo do ponto mais baixo do projeto: senão o Revit recusa a
    /// edição ("o sólido topográfico é muito fino"). O tipo é duplicado ("… SV") com a camada mais grossa aumentada.
    /// </summary>
    public static void EnsureThickness(Document doc, Toposolid topo, double minZFt, TerrainReport report)
    {
        try
        {
            var bb = topo.get_BoundingBox(null);
            if (bb == null) return;
            var margin = UnitConv.Ft(1.5);
            var lack = bb.Min.Z - (minZFt - margin);
            if (lack <= 0) return;
            if (doc.GetElement(topo.GetTypeId()) is not ToposolidType type) return;
            var cs = type.GetCompoundStructure();
            if (cs == null || cs.LayerCount == 0) return;
            // Folga extra (redondo em metros) para as próximas obras não exigirem outro tipo.
            var addM = Math.Ceiling(UnitConv.M(lack) + 2);
            var baseName = type.Name.Contains(" SV +") ? type.Name[..type.Name.IndexOf(" SV +", StringComparison.Ordinal)] : type.Name;
            var total = UnitConv.M(cs.GetWidth()) + addM;
            var name = $"{baseName} SV +{total:0} m";
            var target = new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).Cast<ToposolidType>().FirstOrDefault(x => x.Name == name);
            if (target == null)
            {
                target = (ToposolidType)type.Duplicate(name);
                var ncs = target.GetCompoundStructure();
                var idx = Enumerable.Range(0, ncs.LayerCount).OrderByDescending(i => ncs.GetLayerWidth(i)).First();
                ncs.SetLayerWidth(idx, ncs.GetLayerWidth(idx) + UnitConv.Ft(addM));
                target.SetCompoundStructure(ncs);
            }
            topo.ChangeTypeId(target.Id);
            doc.Regenerate();
            report.Notes.Add($"Toposolid engrossado para {total:0} m (tipo \"{name}\"): o corte chega a {UnitConv.M(minZFt):0.0} m e o fundo estava acima disso.");
        }
        catch (Exception ex)
        {
            Log.Error("Espessura do Toposolid", ex);
            report.Notes.Add("Aviso: não foi possível engrossar o Toposolid para o corte – aumente a espessura do tipo (Editar tipo → Estrutura) se o Revit recusar.");
        }
    }

    private static void AddPoints(SlabShapeEditor editor, List<XYZ> add, TerrainReport report, bool count)
    {
        const int batch = 400;
        for (int i = 0; i < add.Count; i += batch)
        {
            var part = add.Skip(i).Take(batch).ToList();
            try { editor.AddPoints(part); if (count) report.Added += part.Count; }
            catch (Exception ex)
            {
                Log.Error("Toposolid.AddPoints (lote)", ex);
                foreach (var q in part)
                    try { editor.AddPoint(q); if (count) report.Added++; } catch { if (count) report.Skipped++; }
            }
        }
    }

    /// <summary>Desvio (mediana e dispersão p90−p10, m) entre o topo real do Toposolid e as cotas pedidas, numa amostra dos pontos.</summary>
    private static (double Median, double Spread, int N) Check(Toposolid topo, List<XYZ> wanted, double offsetM)
    {
        var live = TerrainModel.Live(topo);
        if (live.Count == 0) return (0, 0, 0);
        var step = Math.Max(1, wanted.Count / 300);
        var d = new List<double>();
        for (int i = 0; i < wanted.Count; i += step)
        {
            var w = wanted[i];
            if (live.Z(new Vec2(UnitConv.M(w.X), UnitConv.M(w.Y))) is { } z) d.Add(z - UnitConv.M(w.Z) - offsetM);
        }
        if (d.Count == 0) return (0, 0, 0);
        d.Sort();
        return (d[d.Count / 2], d[(int)(d.Count * 0.9)] - d[(int)(d.Count * 0.1)], d.Count);
    }

    /// <summary>
    /// Cria um Toposolid plano (Massa e terreno) sob todas as obras, com margem, na cota da base delas – o terreno nativo que
    /// a terraplenagem vai moldar.
    /// </summary>
    public static bool CreateTerrain(Document doc, MarkingService service, IEnumerable<MarkingDefinition> defs, GradingOptions opt, TerrainReport report)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var zs = new List<double>();
        foreach (var d in defs)
        {
            try
            {
                var geo = service.BuildGeometry(d, out var baseZ);
                if (geo.Bounds is not { } b) continue;
                minX = Math.Min(minX, b.Min.X); minY = Math.Min(minY, b.Min.Y);
                maxX = Math.Max(maxX, b.Max.X); maxY = Math.Max(maxY, b.Max.Y);
                zs.Add(baseZ + d.Output.ElevationOffset);
            }
            catch (Exception ex) { Log.Error("Terreno – limites", ex); }
        }
        if (zs.Count == 0) return false;
        var m = Math.Max(10, opt.NewTerrainMargin);
        var z = UnitConv.Ft(zs.Min() - 0.02);
        var corners = new[] { (minX - m, minY - m), (maxX + m, minY - m), (maxX + m, maxY + m), (minX - m, maxY + m) }
            .Select(c => new XYZ(UnitConv.Ft(c.Item1), UnitConv.Ft(c.Item2), z)).ToList();
        var typeId = new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).FirstElementId();
        var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.ProjectElevation - z)).FirstOrDefault();
        if (typeId == ElementId.InvalidElementId || level == null)
        {
            report.Notes.Add("Não foi possível criar o Toposolid: o projeto não tem tipo de Toposolid ou nível.");
            return false;
        }
        try
        {
            var loop = new CurveLoop();
            var flat = corners.Select(c => new XYZ(c.X, c.Y, level.ProjectElevation)).ToList();
            for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(flat[i], flat[(i + 1) % 4]));
            var topo = Toposolid.Create(doc, new List<CurveLoop> { loop }, corners, typeId, level.Id);
            try { topo.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("SV - terreno criado pelo plugin"); } catch { /* opcional */ }
            doc.Regenerate();
            report.Created = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Toposolid.Create", ex);
            report.Notes.Add("Não foi possível criar o Toposolid: " + ex.Message);
            return false;
        }
    }

    /// <summary>Grama de taludes, ilhas e laços como subdivisões nativas do Toposolid, com o material da cor.</summary>
    private static void ApplyFinishes(Document doc, List<(Polygon2 Area, MarkingColor Color, string Id)> finishes, TerrainReport report, HashSet<string> graded)
    {
        var styles = new StyleService(doc);
        var topos = TerrainModel.Hosts(doc);
        // Subdivisões anteriores do plugin saem: das obras regeneradas e das que não moldam mais o terreno (apagadas).
        foreach (var old in new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Cast<Toposolid>()
                     .Where(t => t.HostTopoId != ElementId.InvalidElementId && t.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() is { } c
                                 && c.StartsWith("SV acabamento ", StringComparison.Ordinal)).ToList())
            try { doc.Delete(old.Id); } catch { /* já removida */ }
        _ = graded;
        foreach (var grp in finishes.GroupBy(f => f.Id))
        {
            var tag = $"SV acabamento {grp.Key}";
            foreach (var (area, color, _) in grp)
            {
                var host = topos.FirstOrDefault(t => Contains(t, area.Centroid));
                if (host == null) continue;
                foreach (var part in PolygonOps.Clean(new[] { area }, 0.05))
                {
                    try
                    {
                        var z = host.get_BoundingBox(null)?.Max.Z ?? 0;
                        // Pontos a menos de 0,01 pé saem antes de montar o contorno (pular só a reta curta deixava o anel aberto).
                        if (PlanLoops.Lines(part.Outer, z, 0.01) is not { } loop) continue;
                        var sub = host.CreateSubDivision(doc, new List<CurveLoop> { loop });
                        sub.GetParameter(ParameterTypeId.ToposolidSubdivideMaterial)?.Set(styles.Material(color));
                        sub.GetParameter(ParameterTypeId.ToposolidSubdivideHeight)?.Set(UnitConv.Ft(0.02));
                        sub.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(tag);
                        report.Subdivisions++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Toposolid.CreateSubDivision", ex);
                        report.Notes.Add($"Acabamento do terreno não criado ({ex.Message}).");
                    }
                }
            }
        }
    }

    private static bool Contains(Element e, Vec2 p)
    {
        var bb = e.get_BoundingBox(null);
        if (bb == null) return false;
        return UnitConv.M(bb.Min.X) <= p.X && p.X <= UnitConv.M(bb.Max.X) && UnitConv.M(bb.Min.Y) <= p.Y && p.Y <= UnitConv.M(bb.Max.Y);
    }

    private static (Vec2 Min, Vec2 Max)? Bounds(IReadOnlyList<Polygon2> polys)
    {
        if (polys.Count == 0) return null;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var f in polys)
        {
            var (mn, mx) = f.Bounds;
            minX = Math.Min(minX, mn.X); minY = Math.Min(minY, mn.Y);
            maxX = Math.Max(maxX, mx.X); maxY = Math.Max(maxY, mx.Y);
        }
        return (new Vec2(minX, minY), new Vec2(maxX, maxY));
    }

    private static bool Overlaps(Element e, (Vec2 Min, Vec2 Max)? b)
    {
        if (b == null) return false;
        var bb = e.get_BoundingBox(null);
        if (bb == null) return true;
        var (mn, mx) = b.Value;
        return UnitConv.M(bb.Max.X) >= mn.X && UnitConv.M(bb.Min.X) <= mx.X && UnitConv.M(bb.Max.Y) >= mn.Y && UnitConv.M(bb.Min.Y) <= mx.Y;
    }
}
