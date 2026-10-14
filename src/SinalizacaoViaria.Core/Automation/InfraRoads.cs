using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Liga as obras de infraestrutura às vias do plugin: a via (pisos, faixas, conexões) é sempre a mesma ferramenta de vias e
/// as obras são trechos dela – ponte, viaduto, túnel ou trincheira hospedados no eixo, com o greide da via.
/// </summary>
public static class InfraRoads
{
    private static ElementoSecao E(TipoElementoSecao t, double w) => new() { Tipo = t, Largura = w };

    /// <summary>Seção de via equivalente às faixas, acostamentos e passeios informados no formulário da obra.</summary>
    public static RoadSetup Setup(int lanes, double laneWidth, double shoulder, double sidewalk, HierarquiaViaria h = HierarquiaViaria.Arterial, double speed = 60,
        bool? oneWayOrTwo = null)
    {
        lanes = Math.Max(1, lanes);
        // Mão dupla só com 2 ou mais faixas; nulo = como antes (2 ou mais faixas = mão dupla).
        var twoWay = lanes >= 2 && (oneWayOrTwo ?? true);
        var s = new RoadSetup
        {
            Hierarchy = h, Speed = speed, TwoWay = twoWay, Center = twoWay ? (lanes >= 4 ? CenterTreatment.LFO3 : CenterTreatment.LFO2) : CenterTreatment.Nenhum,
            PhysicalElements = true, Inscriptions = false,
        };
        var right = twoWay ? (lanes + 1) / 2 : lanes;
        var left = twoWay ? lanes / 2 : 0;
        for (int i = 0; i < right; i++) s.Right.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth));
        for (int i = 0; i < left; i++) s.Left.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth));
        if (!twoWay)
        {
            // Mão única: metade das faixas de cada lado do eixo (o eixo fica no meio da pista).
            s.Right.Clear();
            var r = (lanes + 1) / 2;
            for (int i = 0; i < r; i++) s.Right.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth));
            for (int i = r; i < lanes; i++) s.Left.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth));
            if (s.Left.Count == 0)
            {
                // Faixa única centrada no eixo: as duas metades são a mesma faixa (sem divisória no meio dela).
                s.Right[0].Largura = laneWidth / 2;
                s.Left.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth / 2));
                s.FaixaUnicaCentrada = true;
            }
        }
        foreach (var side in new[] { s.Right, s.Left })
        {
            if (shoulder > 0.05) side.Add(E(TipoElementoSecao.Acostamento, shoulder));
            if (sidewalk > 0.05) side.Add(E(TipoElementoSecao.Calcada, sidewalk));
        }
        return s;
    }

    public static RoadSetup Setup(BridgeDefinition b) => b.Kind == TipoObraDeArte.Passarela
        ? PedestrianSetup(Math.Max(1.5, b.LaneWidth))
        : Setup(b.Lanes, b.LaneWidth, b.ShoulderWidth, b.SidewalkWidth, b.Hierarchy ?? HierarquiaViaria.Arterial, oneWayOrTwo: b.TwoWay);

    public static RoadSetup Setup(TunnelDefinition t) => Setup(t.Lanes, t.LaneWidth, t.ShoulderWidth, t.WalkwayWidth, t.Hierarchy ?? HierarquiaViaria.Arterial, oneWayOrTwo: t.TwoWay);
    public static RoadSetup Setup(TrenchDefinition t) => Setup(t.Lanes, t.LaneWidth, t.ShoulderWidth, 0, t.Hierarchy ?? HierarquiaViaria.Arterial, oneWayOrTwo: t.TwoWay);

    /// <summary>Passarela: piso de concreto contínuo, sem faixas de veículos.</summary>
    public static RoadSetup PedestrianSetup(double width)
    {
        var s = new RoadSetup { TwoWay = true, Center = CenterTreatment.Nenhum, EdgeLines = false, Inscriptions = false, Pavement = TipoPavimento.Concreto, Speed = 5 };
        s.Right.Add(E(TipoElementoSecao.FaixaRolamento, width / 2));
        s.Left.Add(E(TipoElementoSecao.FaixaRolamento, width / 2));
        return s;
    }

    /// <summary>
    /// Trechos da via que as obras tiram da terraplenagem da via (a obra trata o terreno do próprio trecho) e como a via
    /// termina junto a elas: face vertical nos encontros de pontes e nos emboques de túneis (com a testa limitada à altura do
    /// emboque e talude acima). Pala: a via termina na testa, à frente do túnel.
    /// </summary>
    public static List<(double A, double B, bool Face, double? Cap, bool Spill)> TerrainGaps(IEnumerable<MarkingDefinition> works, double subgrade)
    {
        var res = new List<(double, double, bool, double?, bool)>();
        foreach (var w in works)
        {
            if (w is not IHostedStructure h) continue;
            if (w is ITerrainAware { AdjustTerrain: false } && w is not BridgeDefinition) continue;
            var a = Math.Min(h.HostStart, h.HostEnd);
            var b = Math.Max(h.HostStart, h.HostEnd);
            switch (w)
            {
                case TunnelDefinition tn:
                {
                    var ext = tn.Portal == TipoEmboque.Pala ? Math.Max(0, tn.PortalLength) : 0;
                    res.Add((a - ext, b + ext, true, EarthworksGenerator.PortalCap(tn) + subgrade, false));
                    break;
                }
                case BridgeDefinition br:
                    // Alas abertas: saia de aterro à frente do encontro; alas paralelas/terra armada: face vertical (contida).
                    res.Add((a, b, true, null, br.WingWalls == TipoAla.Abertas && !br.ApproachWalls));
                    break;
                default:
                    res.Add((a, b, false, null, false));
                    break;
            }
        }
        return res.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>Faixas de terraplenagem da via fora dos trechos em obra (plataforma no greide, taludes, faces junto às obras).</summary>
    public static List<GradeCorridor> RoadCorridors(Polyline2 axis, VerticalProfile prof, double right, double left, double s0, double s1, double subgrade,
        double cut, double fill, IEnumerable<MarkingDefinition> works)
    {
        var res = new List<GradeCorridor>();
        var cursor = s0;
        (bool Face, double? Cap, bool Spill) before = (false, null, false);
        foreach (var (a, b, face, cap, spill) in TerrainGaps(works, subgrade).Append((s1 + 1, s1 + 2, false, null, false)))
        {
            var e = Math.Min(a, s1);
            if (e - cursor > 2)
            {
                // Estacas a cada 1 m: a plataforma acompanha as curvas verticais do greide a milímetros (com 3 m a corda das
                // curvas de "acompanhar terreno" ficava até 1,7 cm fora do fundo do pavimento).
                var c = Infra.Corridor(axis, prof, right, left, cursor, e, subgrade, cut, fill, step: 1, label: "Via");
                c.WallStart = before.Face;
                c.WallEnd = face && a <= s1;
                // Uma faixa só tem um limite de testa: vale o do emboque (o encontro de ponte não tem limite).
                c.WallCap = c.WallEnd ? cap : before.Cap;
                if (c.WallStart && c.WallEnd && before.Cap == null) c.WallCap = cap;
                c.EndSpill = c.WallEnd ? spill : before.Spill;
                // Pontas livres da via (sem obra): fecham em talude até o terreno.
                c.SpillStart = !c.WallStart && Math.Abs(cursor - s0) < 1e-6;
                c.SpillEnd = !c.WallEnd && e >= s1 - 1e-6;
                res.Add(c);
            }
            before = (face, cap, spill);
            cursor = Math.Max(cursor, b);
            if (cursor >= s1) break;
        }
        return res;
    }

    /// <summary>
    /// Plataforma da via sob os pisos: bordas e linhas internas (as quebras da seção em <paramref name="breaks"/> – eixo do
    /// abaulamento, fim do caimento – e a cada <paramref name="spacing"/> m) na cota da superfície menos
    /// <paramref name="subgrade"/>. Antes as bordas ficavam na cota do eixo e a faixa era reta de borda a borda: a plataforma
    /// passava até 0,4 m fora do fundo do pavimento com abaulamento, superelevação ou concordância com os nós.
    /// </summary>
    /// <param name="left">Afastamento (m, positivo à esquerda) da borda <see cref="GradeCorridor.Left"/>.</param>
    /// <param name="right">Afastamento da borda <see cref="GradeCorridor.Right"/> (negativo à direita).</param>
    public static void FitToSurface(IEnumerable<GradeCorridor> corridors, double left, double right, Func<Vec2, double> surface, double subgrade,
        IEnumerable<double> breaks, double spacing = 1.0)
    {
        if (left - right < 1e-6) return;
        var ys = breaks.Where(y => y < left - 0.1 && y > right + 0.1).ToList();
        for (var y = right + spacing; y < left - 0.1; y += spacing)
            if (ys.All(b => Math.Abs(b - y) > spacing * 0.4)) ys.Add(y);
        ys = ys.Distinct().OrderByDescending(y => y).ToList();
        foreach (var c in corridors)
        {
            var n = Math.Min(c.Left.Count, c.Right.Count);
            c.Inner.Clear();
            c.Surface = p => surface(p) - subgrade;
            var lines = ys.Select(_ => new List<Vec3>(n)).ToList();
            for (int i = 0; i < n; i++)
            {
                var a = c.Left[i].XY;
                var b = c.Right[i].XY;
                c.Left[i] = Vec3.At(a, surface(a) - subgrade);
                c.Right[i] = Vec3.At(b, surface(b) - subgrade);
                for (int k = 0; k < ys.Count; k++)
                {
                    var q = a + (b - a) * ((left - ys[k]) / (left - right));
                    lines[k].Add(Vec3.At(q, surface(q) - subgrade));
                }
            }
            c.Inner.AddRange(lines);
        }
    }

    /// <summary>
    /// Bordos da seção da via numa estaca (m, positivo à esquerda do eixo): faces dos meios-fios (fim da pista), alinhamentos e
    /// o nível do acabamento no alinhamento acima da pista (topo da calçada; 0 sem calçada).
    /// </summary>
    public readonly record struct PlatformEdges(double PavedRight, double PavedLeft, double OuterRight, double OuterLeft, double RiseRight, double RiseLeft);

    /// <summary>
    /// Largura (m) das transições da plataforma entre camadas: sob o meio-fio (do fundo da estrutura do pavimento ao fundo da
    /// laje da calçada) e na borda da calçada junto ao alinhamento (do fundo da laje ao topo, onde o terreno encontra o acabamento).
    /// Ficam escondidas sob o meio-fio e dentro da laje; com 0,25 m (o espaçamento dos pontos) a triangulação do Toposolid não
    /// passa por cima delas – com 5 cm ela ligava a fileira de dentro ao pé do talude e afundava a borda até 27 cm.
    /// </summary>
    public const double LayerStep = 0.25;

    /// <summary>
    /// Seção da plataforma da via ao longo do eixo: pista (largura variável incluída), alinhamentos e o topo da calçada no
    /// alinhamento (altura, inclinação transversal e níveis no alinhamento da seção salva; sem a seção, a altura do meio-fio).
    /// </summary>
    public static Func<double, PlatformEdges> Edges(RoadPavementDefinition road, Polyline2? axis)
    {
        var setup = RoadTemplates.FromJson(road.SetupJson);
        var (fn, _) = road.EdgeFunctions(axis);
        var riseL = LotRise(road, setup, true);
        var riseR = LotRise(road, setup, false);
        return s =>
        {
            var v = fn(s);
            var pl = Math.Max(0, road.LeftWidth + v.CurbL);
            var pr = -Math.Max(0.5, road.RightWidth + v.CurbR);
            var ol = road.LeftSidewalk > 0.01 ? Math.Max(pl + 0.2, road.TotalLeft + v.LotL) : pl;
            var or = road.RightSidewalk > 0.01 ? Math.Min(pr - 0.2, -(road.TotalRight + v.LotR)) : pr;
            return new PlatformEdges(pr, pl, or, ol, riseR(s), riseL(s));
        };
    }

    /// <summary>Nível do acabamento no alinhamento de um lado (m acima da pista) em função da estaca.</summary>
    private static Func<double, double> LotRise(RoadPavementDefinition road, RoadSetup? setup, bool left)
    {
        var side = setup == null ? null : left ? setup.Left : setup.Right;
        if ((left ? road.LeftSidewalk : road.RightSidewalk) <= 0.01)
        {
            // Sem calçada: o último elemento elevado (ciclovia no nível da calçada...) é o acabamento do bordo.
            var last = side?.LastOrDefault();
            var h = last is { Elevado: true } ? Math.Max(0, last.AlturaEfetiva) : 0;
            return _ => h;
        }
        var walk = side?.LastOrDefault(e => e.Tipo == TipoElementoSecao.Calcada);
        if (walk == null) return _ => road.CurbHeight;
        if (walk.NiveisAlinhamento.Count > 0)
        {
            var pts = walk.NiveisAlinhamento.OrderBy(n => n.Estaca).ToList();
            return s =>
            {
                if (s <= pts[0].Estaca) return pts[0].Nivel;
                for (int i = 0; i + 1 < pts.Count; i++)
                    if (s <= pts[i + 1].Estaca)
                        return pts[i].Nivel + (pts[i + 1].Nivel - pts[i].Nivel) * (s - pts[i].Estaca) / Math.Max(1e-9, pts[i + 1].Estaca - pts[i].Estaca);
                return pts[^1].Nivel;
            };
        }
        // Inclinação transversal subindo para o lote (como SidewalkLevelProfiles).
        var lot = walk.AlturaEfetiva + Math.Clamp(walk.InclinacaoTransversal, -8, 8) / 100.0 * Math.Max(0.05, walk.Largura);
        return _ => lot;
    }

    /// <summary>
    /// Desnível da plataforma em relação à superfície da via no afastamento <paramref name="y"/>: sob a pista a estrutura
    /// (−<paramref name="subgrade"/>); sob o meio-fio e a calçada o fundo da laje (na superfície da via, como os pisos); na borda
    /// externa o topo do acabamento – transições de <see cref="LayerStep"/> m sob o meio-fio e dentro da borda da calçada.
    /// </summary>
    public static double LayerOffset(PlatformEdges e, double y, double subgrade)
    {
        double Side(double d, double w, double rise)
        {
            // Sem calçada: do fundo da estrutura ao acabamento no bordo da pista.
            if (w < 2 * LayerStep + 1e-6) return -subgrade + (rise + subgrade) * Math.Clamp((d - (w - LayerStep)) / LayerStep, 0, 1);
            if (d <= 0) return -subgrade;
            if (d < LayerStep) return -subgrade * (1 - d / LayerStep);
            return rise * Math.Clamp((d - (w - LayerStep)) / LayerStep, 0, 1);
        }
        return y >= 0.5 * (e.PavedLeft + e.PavedRight)
            ? Side(y - e.PavedLeft, e.OuterLeft - e.PavedLeft, e.RiseLeft)
            : Side(e.PavedRight - y, e.PavedRight - e.OuterRight, e.RiseRight);
    }

    /// <summary>
    /// Largura (m) do degrau do meio-fio no acabamento da testa de uma ponta livre – acima dos 5 cm em que o Toposolid junta pontos
    /// vizinhos (com 2 cm o vértice do degrau sumia e o terreno à frente do meio-fio ficava 12 cm abaixo do topo dele).
    /// </summary>
    public const double FinishStep = 0.06;

    /// <summary>Acabamento no afastamento <paramref name="y"/> em relação à superfície da via: pista 0, calçada no nível do alinhamento.</summary>
    public static double FinishOffset(PlatformEdges e, double y)
    {
        static double Side(double d, double w, double rise) =>
            w < 2 * LayerStep + 1e-6 ? rise * Math.Clamp((d - (w - FinishStep)) / FinishStep, 0, 1) : rise * Math.Clamp(d / FinishStep, 0, 1);
        return y >= 0.5 * (e.PavedLeft + e.PavedRight)
            ? Side(y - e.PavedLeft, e.OuterLeft - e.PavedLeft, e.RiseLeft)
            : Side(e.PavedRight - y, e.PavedRight - e.OuterRight, e.RiseRight);
    }

    /// <summary>
    /// Plataforma da via por camadas (rodada VT): a estrutura do pavimento (<paramref name="subgrade"/>) só sob a pista; sob o
    /// meio-fio, a calçada e a faixa gramada o terreno fica no fundo da laje; na borda da calçada ele sobe ao topo dela e o talude
    /// parte dali. Antes a plataforma ficava inteira <paramref name="subgrade"/> abaixo da superfície e 0,3 m além da calçada: o
    /// terreno junto ao lote ficava 0,55 m abaixo do topo da calçada (degrau em toda a borda). Bordas e linhas internas nas
    /// transições, nas quebras da seção (<paramref name="breaks"/>) e a cada <paramref name="spacing"/> m na pista; nas pontas livres
    /// (talude à frente) a plataforma sobe ao acabamento nos últimos <see cref="LayerStep"/> m. <paramref name="left"/> e
    /// <paramref name="right"/> são os afastamentos com que as faixas foram criadas (só para achar o eixo de cada estaca).
    /// </summary>
    public static void FitToSection(IEnumerable<GradeCorridor> corridors, double left, double right, GradeSurface surf, Func<double, PlatformEdges> edges,
        double subgrade, IEnumerable<double> breaks, double spacing = 1.0)
    {
        if (left - right < 1e-6) return;
        var G = LayerStep;
        foreach (var c in corridors)
        {
            var n0 = Math.Min(c.Left.Count, c.Right.Count);
            if (n0 < 2) continue;
            var ptl = new List<Vec2>(n0 + 2);
            var nrl = new List<Vec2>(n0 + 2);
            for (int i = 0; i < n0; i++)
            {
                var a = c.Left[i].XY;
                var b = c.Right[i].XY;
                nrl.Add((a - b) * (1.0 / (left - right)));
                ptl.Add(b + (a - b) * (-right / (left - right)));
            }
            // Pontas livres: uma estaca a LayerStep da testa – a subida da plataforma ao acabamento fica só nela (sem ela a
            // triangulação ligava a testa à estaca de 1 m e o terreno subia aos poucos sob a pista e a calçada).
            void Insert(int at, int from, int to)
            {
                var len = ptl[from].DistanceTo(ptl[to]);
                if (len < 3 * G) return;
                var t = G / len;
                ptl.Insert(at, ptl[from] + (ptl[to] - ptl[from]) * t);
                nrl.Insert(at, (nrl[from] + (nrl[to] - nrl[from]) * t).Normalized());
            }
            if (c.SpillEnd) Insert(ptl.Count - 1, ptl.Count - 1, ptl.Count - 2);
            if (c.SpillStart) Insert(1, 0, 1);
            var n = ptl.Count;
            var pts = ptl.ToArray();
            var nrm = nrl.ToArray();
            var st = pts.Select(q => surf.Locate(q).S).ToArray();
            var es = st.Select(edges).ToArray();
            // Linhas da pista (quebras e espaçamento) – afastamentos fixos, dentro da menor pista das estacas.
            var minL = es.Min(e => e.PavedLeft);
            var maxR = es.Max(e => e.PavedRight);
            var ys = breaks.Where(y => y < minL - 0.1 && y > maxR + 0.1).ToList();
            for (var y = maxR + spacing; y < minL - 0.1; y += spacing)
                if (ys.All(b => Math.Abs(b - y) > spacing * 0.4)) ys.Add(y);
            ys = ys.Distinct().OrderByDescending(y => y).ToList();
            // Pontas livres: a plataforma sobe ao acabamento na testa (o talude à frente parte do topo, sem degrau).
            double s0 = st[0], s1 = st[n - 1];
            bool up = s1 >= s0;
            var spill0 = c.SpillStart;
            var spill1 = c.SpillEnd;
            double Z(Vec2 q, double s, double y)
            {
                var e = edges(s);
                var off = LayerOffset(e, y, subgrade);
                var dEnd = Math.Min(spill0 ? (up ? s - s0 : s0 - s) : double.MaxValue, spill1 ? (up ? s1 - s : s - s1) : double.MaxValue);
                if (dEnd < G)
                {
                    var finish = FinishOffset(e, y);
                    off += (finish - off) * Math.Clamp(1 - dEnd / G, 0, 1);
                }
                return surf.Z(q) + off;
            }
            c.Surface = p =>
            {
                var (s, y) = surf.Locate(p);
                return Z(p, s, y);
            };
            var lines = new List<List<Vec3>>();
            c.StepLines.Clear();
            // Linhas no miolo das calçadas (a cada ≤ 1 m): na concordância com os nós a superfície empena também na calçada.
            int Count(Func<PlatformEdges, double> w) => Math.Max(0, (int)Math.Ceiling(es.Max(w) / spacing) - 1);
            var kL = Count(e => e.OuterLeft - e.PavedLeft - 2 * G);
            var kR = Count(e => e.PavedRight - e.OuterRight - 2 * G);
            void Step(Func<PlatformEdges, double> at) { c.StepLines.Add(lines.Count); Line(at); }
            void Line(Func<PlatformEdges, double> at)
            {
                var l = new List<Vec3>(n);
                for (int i = 0; i < n; i++)
                {
                    var y = at(es[i]);
                    var q = pts[i] + nrm[i] * y;
                    l.Add(Vec3.At(q, Z(q, st[i], y)));
                }
                lines.Add(l);
            }
            // Da esquerda para a direita: alinhamento (topo), fundo da laje, miolo da calçada, sob o meio-fio, face do meio-fio,
            // pista, ...
            Step(e => e.OuterLeft);
            Step(e => Math.Max(e.PavedLeft, e.OuterLeft - G));
            for (int j = kL; j >= 1; j--)
            {
                var f = (double)j / (kL + 1);
                Line(e => Math.Max(e.PavedLeft, e.PavedLeft + G + (e.OuterLeft - e.PavedLeft - 2 * G) * f));
            }
            Step(e => Math.Min(e.OuterLeft - G, e.PavedLeft + G));
            Step(e => Math.Min(e.OuterLeft - G, e.PavedLeft));
            foreach (var y0 in ys) Line(e => Math.Clamp(y0, e.PavedRight + 0.05, e.PavedLeft - 0.05));
            Step(e => Math.Max(e.OuterRight + G, e.PavedRight));
            Step(e => Math.Max(e.OuterRight + G, e.PavedRight - G));
            for (int j = 1; j <= kR; j++)
            {
                var f = (double)j / (kR + 1);
                Line(e => Math.Min(e.PavedRight, e.PavedRight - G - (e.PavedRight - e.OuterRight - 2 * G) * f));
            }
            Step(e => Math.Min(e.PavedRight, e.OuterRight + G));
            Step(e => e.OuterRight);
            c.Left.Clear();
            c.Left.AddRange(lines[0]);
            c.Right.Clear();
            c.Right.AddRange(lines[^1]);
            c.Inner.Clear();
            c.Inner.AddRange(lines.Skip(1).Take(lines.Count - 2));
            // Acabamento das testas livres: o perfil da seção pronta, com o degrau do meio-fio – o talude à frente parte dele.
            List<Vec3> Finish(int i)
            {
                var e = es[i];
                var fy = new List<double> { e.OuterLeft, e.PavedLeft + FinishStep, e.PavedLeft, e.OuterLeft - FinishStep };
                fy.AddRange(ys);
                fy.AddRange(new[] { e.PavedRight, e.PavedRight - FinishStep, e.OuterRight, e.OuterRight + FinishStep });
                return fy.Where(y => y <= e.OuterLeft + 1e-9 && y >= e.OuterRight - 1e-9).Distinct().OrderByDescending(y => y)
                    .Select(y => { var q = pts[i] + nrm[i] * y; return Vec3.At(q, surf.Z(q) + FinishOffset(e, y)); }).ToList();
            }
            c.StartFinish.Clear();
            c.EndFinish.Clear();
            if (spill0) c.StartFinish.AddRange(Finish(0));
            if (spill1) c.EndFinish.AddRange(Finish(n - 1));
            // Pista (onde a estrutura do pavimento fica): faixas entre as faces dos meios-fios, para as plataformas vizinhas.
            c.Paved.Clear();
            for (int i = 0; i + 1 < n; i++)
            {
                Vec2 P(int k, double y) => pts[k] + nrm[k] * y;
                c.Paved.Add(new Polygon2(new[] { P(i, es[i].PavedLeft), P(i + 1, es[i + 1].PavedLeft), P(i + 1, es[i + 1].PavedRight), P(i, es[i].PavedRight) }));
            }
        }
    }

    /// <summary>
    /// Camadas de uma plataforma de nó a partir das peças no chão: pista = peças rentes à pista (pavimento, sarjeta – topo abaixo
    /// de 5 cm; a pintura não conta), onde fica a estrutura do pavimento; o resto (calçadas, meios-fios, ilhas, canteiros) fica no fundo
    /// das lajes. <c>EdgeRise</c> dá o topo da peça elevada junto ao ponto (o acabamento no contorno externo).
    /// </summary>
    public static (List<Polygon2> Paved, Func<Vec2, double> EdgeRise) PadLayers(IEnumerable<MarkingPiece> pieces)
    {
        var flat = pieces.Where(p => p.Solid == null && p.Profile == null).ToList();
        // Pintura não diz o que há embaixo (fica sobre a pista, a calçada ou a extensão): só pavimento, sarjeta e afins.
        var paved = flat.Where(p => p.Elevation + p.Thickness < 0.05 && !MarkingColors.IsPaint(p.Color)).Select(p => p.Shape).ToList();
        var raised = flat.Where(p => p.Elevation + p.Thickness >= 0.05).Select(p => (p.Shape, Top: p.Elevation + p.Thickness)).ToList();
        double Rise(Vec2 q)
        {
            var best = 0.0;
            foreach (var (shape, top) in raised)
                if (top > best && (shape.Contains(q) || shape.DistanceTo(q) <= 0.2)) best = top;
            return best;
        }
        return (paved.Count == 0 ? paved : PolygonOps.Union(paved).Where(u => u.Area > 1e-4).ToList(), Rise);
    }

    /// <summary>
    /// Linhas de quebra da superfície de um nó (<see cref="GradePad.Breaklines"/>): eixo do abaulamento e fim do caimento da
    /// via principal, prolongados 30 m além das pontas do eixo (como a superfície do nó).
    /// </summary>
    public static List<List<Vec2>> NodeBreaklines(NodeSurface node)
    {
        var res = new List<List<Vec2>>();
        if (node.Major?.Surface is not { } surf) return res;
        var g = surf.Grade;
        if (Math.Abs(g.Crossfall) < 1e-9 && g.Superelevation.Count == 0) return res;
        var L = surf.Axis.Length;
        foreach (var y in SectionBreaks(g))
        {
            var line = new List<Vec2>();
            for (var s = -30.0; s <= L + 30 + 1e-9; s += 1.0) line.Add(FloorPlanes.AxisPoint(surf, s, y));
            res.Add(line);
        }
        return res;
    }

    /// <summary>Quebras da seção da via (eixo do abaulamento e fim do caimento) para <see cref="FitToSurface"/>.</summary>
    public static IEnumerable<double> SectionBreaks(RoadGrade g)
    {
        yield return 0;
        if (g.CrossfallWidth > 0.1) { yield return g.CrossfallWidth; yield return -g.CrossfallWidth; }
    }

    /// <summary>Greide a partir de um perfil do gerador (mesmos PIVs e curva).</summary>
    public static RoadGrade FromProfile(VerticalProfile p, double curve)
    {
        var g = new RoadGrade { DefaultCurve = curve };
        foreach (var (s, z) in p.Pvis.Where(q => q.S < 1e8)) g.Points.Add(new GradePoint(s, z));
        g.Normalize();
        return g;
    }

    /// <summary>
    /// Greide de uma nova via com ponte/viaduto e o trecho em estrutura, conforme o perfil escolhido: rampas de acesso (a
    /// estrutura começa onde o aterro passaria da altura máxima), horizontal, entre margens (reta de cabeceira a cabeceira) ou
    /// convexo.
    /// </summary>
    public static (RoadGrade Grade, double S0, double S1, List<string> Warnings) BridgeGrade(BridgeDefinition b, Polyline2 path, Func<Vec2, double> ground)
    {
        var L = path.Length;
        var z0 = ground(path.PointAt(0));
        var z1 = ground(path.PointAt(L));
        var warnings = new List<string>();
        switch (b.ProfileKind)
        {
            case PerfilObra.Horizontal:
            {
                var g = new RoadGrade { DefaultCurve = b.VerticalCurve, Points = { new(0, b.Height), new(L, b.Height) } };
                return (g, 0, L, warnings);
            }
            case PerfilObra.EntreMargens:
            {
                var g = new RoadGrade { DefaultCurve = b.VerticalCurve, Points = { new(0, z0), new(L, z1) } };
                if (Math.Abs(z1 - z0) / Math.Max(1, L) > b.MaxGrade + 1e-6) warnings.Add("Rampa entre as margens acima da rampa máxima.");
                return (g, 0, L, warnings);
            }
            case PerfilObra.Convexo:
            {
                var g = new RoadGrade { DefaultCurve = Math.Max(20, L * 0.9), Points = { new(0, z0), new(L / 2, Math.Max(b.Height, Math.Max(z0, z1) + 1)), new(L, z1) } };
                var (s0, s1) = StructureRange(path, g, ground, BridgeGenerator.MaxFillHeight, Math.Max(10, b.SpanLength));
                return (g, s0, s1, warnings);
            }
            case PerfilObra.Concavo:
            {
                // Ponto baixo no meio (cota "Altura"), subindo às cabeceiras: vale entre duas cristas.
                var mid = Math.Min(b.Height, Math.Min(z0, z1) - 0.5);
                var g = new RoadGrade { DefaultCurve = Math.Max(20, L * 0.9), Points = { new(0, z0), new(L / 2, mid), new(L, z1) } };
                if (b.Height > Math.Min(z0, z1)) warnings.Add("Perfil côncavo: a cota do meio deve ficar abaixo das cabeceiras – usada 0,5 m abaixo da mais baixa.");
                var (s0, s1) = StructureRange(path, g, ground, BridgeGenerator.MaxFillHeight, Math.Max(10, b.SpanLength));
                return (g, s0, s1, warnings);
            }
            case PerfilObra.Inclinado:
            {
                // Rampa de acesso até a altura do início, tabuleiro inclinado até a altura do fim, rampa até o terreno.
                var gmax = Math.Max(0.005, b.MaxGrade);
                var ra = b.ApproachStart ? Math.Abs(b.Height - z0) / gmax : 0;
                var rb = b.ApproachEnd ? Math.Abs(b.EndHeight - z1) / gmax : 0;
                if (ra + rb > L * 0.8) { var k = L * 0.8 / (ra + rb); ra *= k; rb *= k; warnings.Add("Eixo curto para as rampas de acesso: rampas acima da máxima."); }
                var g = new RoadGrade { DefaultCurve = b.VerticalCurve };
                if (ra > 0.5) g.Points.Add(new GradePoint(0, z0));
                g.Points.Add(new GradePoint(ra, b.Height));
                g.Points.Add(new GradePoint(L - rb, b.EndHeight));
                if (rb > 0.5) g.Points.Add(new GradePoint(L, z1));
                g.Normalize();
                var slope = Math.Abs(b.EndHeight - b.Height) / Math.Max(1, L - ra - rb);
                if (slope > gmax + 1e-6) warnings.Add($"Tabuleiro com {(slope * 100):0.0} % de inclinação – acima da rampa máxima.");
                var (s0, s1) = StructureRange(path, g, ground, BridgeGenerator.MaxFillHeight, Math.Max(10, b.SpanLength));
                return (g, Math.Min(s0, Math.Max(0, ra)), Math.Max(s1, Math.Min(L, L - rb)), warnings);
            }
            case PerfilObra.Personalizado:
            {
                var pv = RoadGrade.ParsePvis(b.ProfilePvis);
                if (pv.Count < 2) { warnings.Add("PIVs do perfil personalizado inválidos (uma linha por PIV: estaca; cota; curva) – usado o perfil com rampas."); goto default; }
                var g = new RoadGrade { DefaultCurve = b.VerticalCurve };
                g.Points.AddRange(pv);
                g.Normalize();
                var (s0, s1) = StructureRange(path, g, ground, BridgeGenerator.MaxFillHeight, Math.Max(10, b.SpanLength));
                return (g, s0, s1, warnings);
            }
            default:
            {
                var (prof, s0, s1, w) = BridgeGenerator.Profile(b, path, ground);
                return (FromProfile(prof, b.VerticalCurve), s0, s1, w);
            }
        }
    }

    /// <summary>Trecho em estrutura: onde o greide passa mais de <paramref name="maxFill"/> acima do terreno (aterro alto demais).</summary>
    public static (double S0, double S1) StructureRange(Polyline2 path, RoadGrade g, Func<Vec2, double> ground, double maxFill, double minLength)
    {
        var L = path.Length;
        double? a = null, b = null;
        for (var s = 0.0; s <= L; s += 1)
            if (g.Z(s) - ground(path.PointAt(s)) > maxFill) { a ??= s; b = s; }
        if (a == null) { var m = L / 2; return (Math.Max(0, m - minLength / 2), Math.Min(L, m + minLength / 2)); }
        var s0 = a.Value;
        var s1 = b!.Value;
        if (s1 - s0 < minLength) { var m = (s0 + s1) / 2; s0 = Math.Max(0, m - minLength / 2); s1 = Math.Min(L, s0 + minLength); }
        return (s0, s1);
    }

    /// <summary>
    /// Greide de uma via existente com uma ponte/viaduto em [s0, s1], conforme o perfil escolhido (as rampas de acesso ficam
    /// fora do trecho, até reencontrar o greide atual).
    /// </summary>
    public static RoadGrade BridgeOnRoad(RoadGrade current, BridgeDefinition b, double s0, double s1, double length, Func<double, double> groundAt)
    {
        var g = current.Clone();
        if (g.Points.Count == 0) g.Points.AddRange(new[] { new GradePoint(0, 0), new GradePoint(length, 0) });
        g.DefaultCurve = Math.Max(g.DefaultCurve, b.VerticalCurve);
        switch (b.ProfileKind)
        {
            case PerfilObra.GreideDaVia:
                return g;
            case PerfilObra.EntreMargens:
                Replace(g, s0, s1, new[] { new GradePoint(s0, groundAt(s0)), new GradePoint(s1, groundAt(s1)) });
                return g;
            case PerfilObra.Convexo:
            {
                var za = g.Z(s0);
                var zb = g.Z(s1);
                Replace(g, s0, s1, new[] { new GradePoint(s0, za), new GradePoint((s0 + s1) / 2, Math.Max(b.Height, Math.Max(za, zb) + 1), (s1 - s0) * 0.9), new GradePoint(s1, zb) });
                return g;
            }
            case PerfilObra.Concavo:
            {
                var za = g.Z(s0);
                var zb = g.Z(s1);
                Replace(g, s0, s1, new[] { new GradePoint(s0, za), new GradePoint((s0 + s1) / 2, Math.Min(b.Height, Math.Min(za, zb) - 0.5), (s1 - s0) * 0.9), new GradePoint(s1, zb) });
                return g;
            }
            case PerfilObra.Inclinado:
                g.RaiseBetween(s0, s1, b.Height, b.MaxGrade, length, b.VerticalCurve, b.EndHeight);
                return g;
            case PerfilObra.Personalizado:
            {
                var all = RoadGrade.ParsePvis(b.ProfilePvis, s0);
                if (all.Count < 2) { g.RaiseBetween(s0, s1, b.Height, b.MaxGrade, length, b.VerticalCurve); return g; }
                // PIVs relativos ao início da obra, recortados ao trecho (as pontas do trecho seguem a linha dos PIVs).
                var pg = new RoadGrade { DefaultCurve = 0 };
                pg.Points.AddRange(all);
                var inner = all.Where(q => q.S > s0 + 0.5 && q.S < s1 - 0.5).ToList();
                inner.Insert(0, new GradePoint(s0, pg.Z(s0)));
                inner.Add(new GradePoint(s1, pg.Z(s1)));
                Replace(g, s0, s1, inner);
                return g;
            }
            default:
                g.RaiseBetween(s0, s1, b.Height, b.MaxGrade, length, b.VerticalCurve);
                return g;
        }
    }

    /// <summary>Trincheira numa via existente: a pista desce <paramref name="depth"/> abaixo do terreno dentro do trecho (rampas dentro dele).</summary>
    public static RoadGrade TrenchOnRoad(RoadGrade current, double s0, double s1, double depth, double maxGrade, double curve, Func<double, double> groundAt)
    {
        var g = current.Clone();
        var za = g.Z(s0);
        var zb = g.Z(s1);
        var bottom = Math.Min(groundAt(s0), groundAt(s1)) - Math.Max(1, depth);
        var run0 = Math.Abs(za - bottom) / Math.Max(0.01, maxGrade);
        var run1 = Math.Abs(zb - bottom) / Math.Max(0.01, maxGrade);
        var k = Math.Min(1, (s1 - s0) * 0.9 / Math.Max(1e-6, run0 + run1));
        run0 *= k; run1 *= k;
        g.DefaultCurve = Math.Max(g.DefaultCurve, curve);
        Replace(g, s0, s1, new[] { new GradePoint(s0, za), new GradePoint(s0 + run0, bottom), new GradePoint(s1 - run1, bottom), new GradePoint(s1, zb) });
        return g;
    }

    /// <summary>Túnel numa via existente: mantém o greide ou faz uma rampa constante entre os emboques.</summary>
    public static RoadGrade TunnelOnRoad(RoadGrade current, double s0, double s1, bool straight)
    {
        var g = current.Clone();
        if (straight) Replace(g, s0, s1, new[] { new GradePoint(s0, g.Z(s0)), new GradePoint(s1, g.Z(s1)) });
        return g;
    }

    /// <summary>Trecho em túnel: onde a cobertura (terreno acima da coroa) passa de <paramref name="minCover"/>.</summary>
    public static (double S0, double S1)? TunnelRange(Polyline2 path, RoadGrade g, Func<Vec2, double?> ground, double crown, double minCover = 3)
    {
        var L = path.Length;
        double? a = null, b = null;
        for (var s = 0.0; s <= L; s += 1)
            if (ground(path.PointAt(s)) is { } z && z - (g.Z(s) + crown) > minCover) { a ??= s; b = s; }
        return a == null || b!.Value - a.Value < 10 ? null : (a.Value, b.Value);
    }

    private static void Replace(RoadGrade g, double s0, double s1, IEnumerable<GradePoint> inner)
    {
        g.Points.RemoveAll(p => p.S >= s0 - 0.25 && p.S <= s1 + 0.25);
        g.Points.AddRange(inner);
        g.Normalize();
    }
}
