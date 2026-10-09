using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Como a faixa de pedestres atravessa o canteiro central.</summary>
public enum TipoTravessiaCanteiro
{
    /// <summary>Pela largura do canteiro: rampas e patamar quando cabem; senão, passagem rebaixada no nível da pista.</summary>
    Automatica,
    /// <summary>Passagem rebaixada no nível da pista na largura da faixa, com piso tátil de alerta nas duas bordas.</summary>
    NivelDaPista,
    /// <summary>Canteiro mantido: rampa dos dois lados e patamar no topo, com piso tátil.</summary>
    Rampas,
}

/// <summary>
/// Travessia de pedestres sobre canteiro central (na interseção e no meio da quadra): escolha entre rampas com patamar e
/// passagem rebaixada, faixas de piso tátil de alerta e as rampas dos dois lados.
/// </summary>
public static class MedianCrossing
{
    /// <summary>
    /// Largura mínima do canteiro como refúgio – 1,20 m, o mesmo valor que o plugin já usa para a ilha separadora (NBR 9050;
    /// item da norma [a confirmar]).
    /// </summary>
    public const double MinRefugeWidth = 1.20;

    /// <summary>Patamar mínimo entre as duas rampas no topo do canteiro (m) – [a confirmar].</summary>
    public const double MinLanding = 1.20;

    /// <summary>Profundidade da faixa de alerta em cada borda da passagem rebaixada (2 placas de 0,25 m) – [a confirmar].</summary>
    public const double AlertDepth = 0.50;

    /// <summary>Largura de canteiro a partir da qual cabem as duas rampas e o patamar: 2 × comprimento da rampa + patamar.</summary>
    public static double RampsMinWidth(RampDefinition template)
    {
        var (_, run, _) = RampGenerator.Dimensions(template);
        return 2 * run + MinLanding;
    }

    /// <summary>Tipo efetivo: o pedido, ou (automático) rampas quando o canteiro comporta as duas rampas e o patamar.</summary>
    public static TipoTravessiaCanteiro Resolve(double medianWidth, RampDefinition template, TipoTravessiaCanteiro asked)
    {
        if (asked == TipoTravessiaCanteiro.Rampas && medianWidth < RampsMinWidth(template)) return TipoTravessiaCanteiro.NivelDaPista;
        if (asked != TipoTravessiaCanteiro.Automatica) return asked;
        return medianWidth >= RampsMinWidth(template) ? TipoTravessiaCanteiro.Rampas : TipoTravessiaCanteiro.NivelDaPista;
    }

    /// <summary>Avisos da travessia (canteiro estreito para refúgio, rampas pedidas que não cabem).</summary>
    public static List<string> Warnings(double medianWidth, RampDefinition template, TipoTravessiaCanteiro asked)
    {
        var res = new List<string>();
        if (medianWidth < MinRefugeWidth - 1e-6)
            res.Add($"canteiro de {medianWidth:0.00} m: mais estreito que o refúgio mínimo de {MinRefugeWidth:0.00} m (NBR 9050 – item a confirmar); o pedestre não tem onde esperar");
        if (asked == TipoTravessiaCanteiro.Rampas && medianWidth < RampsMinWidth(template))
            res.Add($"canteiro de {medianWidth:0.00} m não comporta as duas rampas e o patamar ({RampsMinWidth(template):0.00} m) – feita a passagem rebaixada no nível da pista");
        return res;
    }

    /// <summary>
    /// Faixas de alerta da passagem rebaixada: uma em cada borda do canteiro, no nível da pista, ao longo de toda a largura da
    /// faixa de pedestres. <paramref name="at"/>(s, o) dá o ponto na estaca s e no deslocamento o (+ à esquerda do eixo);
    /// [s0, s1] é a largura da faixa e [lo, hi] o canteiro.
    /// </summary>
    public static List<TactileRouteDefinition> AlertStrips(Func<double, double, Vec2> at, double s0, double s1, double lo, double hi, double z, string? groupId)
    {
        var res = new List<TactileRouteDefinition>();
        if (hi - lo < 2 * AlertDepth + 0.05 || s1 - s0 < 0.5) return res;
        foreach (var o in new[] { lo + AlertDepth / 2, hi - AlertDepth / 2 })
            res.Add(new TactileRouteDefinition
            {
                PathRef = PathReference.FromPoints(new[] { at(s0 + 0.05, o), at(s1 - 0.05, o) }, z),
                AlertOnly = true,
                Module = 0.25,
                Rows = (int)Math.Round(AlertDepth / 0.25),
                Elevation = 0.0,
                GroupId = groupId,
            });
        return res;
    }

    /// <summary>
    /// Rampas da travessia sobre canteiro largo: uma em cada face do canteiro, subindo para dentro dele; as duas se encontram
    /// no meio (patamar = canteiro − 2 rampas). <paramref name="template"/> dá largura, altura e inclinação.
    /// </summary>
    public static List<RampDefinition> Ramps(Func<double, double, Vec2> at, double s, double lo, double hi, double z, RampDefinition template, string? groupId)
    {
        var res = new List<RampDefinition>();
        var (_, run, _) = RampGenerator.Dimensions(template);
        var landing = Math.Max(0, (hi - lo) / 2 - run);
        foreach (var (face, inward) in new[] { (lo, 1.0), (hi, -1.0) })
        {
            var r = (RampDefinition)template.CloneWithNewId();
            r.LandingDepth = Math.Round(landing, 3);
            r.CutSidewalk = false;
            r.GroupId = groupId;
            var p0 = at(s, face);
            var p1 = at(s, face + inward);
            r.PathRef = PathReference.FromPoints(new[] { p0, p0 + (p1 - p0).Normalized() }, z);
            res.Add(r);
        }
        return res;
    }
}

/// <summary>
/// Travessia de pedestres sobre o canteiro central no meio da quadra (gravada na seção da via, como os retornos): a via é
/// gerada com a passagem rebaixada (ou as rampas e o patamar), o piso tátil e a faixa de pedestres.
/// </summary>
public sealed class TravessiaCanteiro
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Estaca do eixo da faixa de pedestres (m).</summary>
    public double Estaca { get; set; }
    /// <summary>Largura da faixa de pedestres (m).</summary>
    public double Largura { get; set; } = 4.0;
    public TipoTravessiaCanteiro Tipo { get; set; } = TipoTravessiaCanteiro.Automatica;
    /// <summary>Pinta a faixa de pedestres de bordo a bordo da pista (sem pintura sobre o canteiro).</summary>
    public bool Faixa { get; set; } = true;

    public TravessiaCanteiro Clone() => (TravessiaCanteiro)MemberwiseClone();
}

public sealed partial class RoadSetup
{
    /// <summary>Travessias de pedestres sobre o canteiro central no meio da quadra – geradas com a via.</summary>
    public List<TravessiaCanteiro> TravessiasCanteiro { get; set; } = new();

    /// <summary>
    /// Travessias das extensões de calçada: linhas pintadas da via interrompidas na faixa e vagas a 5 m de cada lado dela
    /// (distância [a confirmar]). Estacas no eixo: acompanham a via.
    /// </summary>
    private void ExtensionBreaks(List<MarkingDefinition> res, RoadPavementDefinition pav)
    {
        var roadPts = pav.PathRef.Points;
        bool OnRoadPath(PathReference pr) => pr.Points.Count == roadPts.Count && pr.Points.Count > 0 && pr.Points[0].DistanceTo(roadPts[0]) < 1e-6;
        foreach (var e in ExtensoesCalcada.Where(e => e.Travessia))
        {
            var sc = e.EstacaTravessia;
            var hw = Math.Max(1.0, e.LarguraFaixa) / 2;
            foreach (var l in res.OfType<LinearMarkingDefinition>().Where(l => !IntersectionGenerator.IsPhysical(l) && l.Code != ParkingLineCode && OnRoadPath(l.PathRef)))
                l.Breaks.Add(new StationRange { Start = sc - hw, End = sc + hw });
            foreach (var pk in res.OfType<ParkingMarkingDefinition>().Where(p => OnRoadPath(p.PathRef)))
                pk.Breaks.Add(new StationRange { Start = sc - hw - 5, End = sc + hw + 5 });
            // Estacionamento só delimitado: a linha de delimitação para junto com as vagas.
            foreach (var pl in res.OfType<LinearMarkingDefinition>().Where(l => l.Code == ParkingLineCode && OnRoadPath(l.PathRef)))
                pl.Breaks.Add(new StationRange { Start = sc - hw - 5, End = sc + hw + 5 });
        }
    }

    /// <summary>Rampa-modelo das travessias sobre o canteiro (NBR 9050: 8,33 %, abas de 10 %, piso tátil de alerta).</summary>
    private RampDefinition MedianRampTemplate(double width) => new()
    {
        Type = TipoRampa.RebaixamentoComAbas,
        Width = Math.Max(IntersectionGenerator.MinRampWidth, width),
        Height = Math.Clamp(MedianHeight, 0.02, 0.40),
        Slope = IntersectionGenerator.MaxRampSlope,
        FlareSlope = IntersectionGenerator.MaxFlareSlope,
        Tactile = true,
        SquareCut = true,
        CutSidewalk = false,
    };

    /// <summary>
    /// Travessias sobre o canteiro na geração da via: abertura do canteiro com pontas retas (passagem no nível da pista) ou
    /// sob as rampas, interrupção dos elementos físicos do canteiro no trecho refeito, piso tátil, rampas e a faixa.
    /// </summary>
    private void ApplyTravessias(List<MarkingDefinition> res, RoadPavementDefinition? pav, Polyline2? axis, double z,
        Func<MarkingDefinition, MarkingDefinition> add, List<TravessiaCanteiro> crossings)
    {
        if (crossings.Count == 0 || axis == null || axis.Length < 1) return;
        if (!(TwoWay && Center == CenterTreatment.Canteiro && MedianType == TipoCanteiro.Fisico && MedianWidth > 0.3))
        {
            Warnings.Add("Travessia sobre o canteiro: a via não tem canteiro central físico – travessia ignorada.");
            return;
        }
        var (lo, hi) = (-MedianWidth / 2, MedianWidth / 2);
        Vec2 At(double s, double o)
        {
            var ss = Math.Clamp(s, 0, axis.Length);
            return axis.PointAt(ss) + axis.TangentAt(ss).PerpLeft * o;
        }
        foreach (var tv in crossings)
        {
            var s = Math.Clamp(tv.Estaca, 0, axis.Length);
            var w = Math.Max(1.0, tv.Largura);
            var hw = w / 2;
            var tpl = MedianRampTemplate(w);
            var tag = $"Travessia na estaca {PontoLargura.FormatEstaca(s)}";
            foreach (var msg in MedianCrossing.Warnings(MedianWidth, tpl, tv.Tipo)) Warnings.Add($"{tag}: {msg}.");
            var mode = MedianCrossing.Resolve(MedianWidth, tpl, tv.Tipo);
            if (!tv.Faixa && pav != null && ExtensoesCalcada.Any(e => e.Id == tv.Id && !e.Rampa)) mode = TipoTravessiaCanteiro.NivelDaPista;
            // Trecho do canteiro refeito: a faixa (passagem rebaixada) ou as rampas com as abas.
            var half = mode == TipoTravessiaCanteiro.Rampas ? IntersectionGenerator.RampHalfExtent(tpl) : hw - 0.1;
            if (s - half < 0.5 || s + half > axis.Length - 0.5) { Warnings.Add($"{tag}: fora do trecho da via – ignorada."); continue; }
            var op = new MedianOpening { Start = s - half, End = s + half, Pedestrian = true, Source = tv.Id };
            if (pav != null) pav.MedianOpenings.Add(op);
            var (s0, s1) = RoadGenerator.MedianOpeningSpan(op, MedianWidth);
            foreach (var l in res.OfType<LinearMarkingDefinition>())
                if (Math.Abs(l.Offset) < MedianHalf + 1e-6 && IntersectionGenerator.IsPhysical(l))
                    l.Breaks.Add(new StationRange { Start = s0, End = s1 });
            if (mode == TipoTravessiaCanteiro.Rampas)
                foreach (var r in MedianCrossing.Ramps(At, s, lo, hi, z, tpl, null)) add(r);
            else
                foreach (var t in MedianCrossing.AlertStrips(At, s - hw + 0.1, s + hw - 0.1, lo, hi, z, null)) add(t);
            if (!tv.Faixa || pav == null) continue;
            // Linhas pintadas da via interrompidas na faixa; vagas também a 5 m de cada lado (visibilidade do pedestre e da
            // rampa da calçada – distância [a confirmar]).
            var roadPts = pav.PathRef.Points;
            bool OnRoadPath(PathReference pr) => pr.Points.Count == roadPts.Count && pr.Points.Count > 0 && pr.Points[0].DistanceTo(roadPts[0]) < 1e-6;
            foreach (var l in res.OfType<LinearMarkingDefinition>().Where(l => !IntersectionGenerator.IsPhysical(l) && l.Code != ParkingLineCode && OnRoadPath(l.PathRef)))
                l.Breaks.Add(new StationRange { Start = s - hw, End = s + hw });
            foreach (var pk in res.OfType<ParkingMarkingDefinition>().Where(p => OnRoadPath(p.PathRef)))
                pk.Breaks.Add(new StationRange { Start = s - hw - 5, End = s + hw + 5 });
            // Estacionamento só delimitado: a linha de delimitação para junto com as vagas.
            foreach (var pl in res.OfType<LinearMarkingDefinition>().Where(l => l.Code == ParkingLineCode && OnRoadPath(l.PathRef)))
                pl.Breaks.Add(new StationRange { Start = s - hw - 5, End = s + hw + 5 });
            var cw = new CrosswalkSetup { CrosswalkWidth = w, StopLines = false, EdgeSetback = 0.3 }
                .Build(At(s, -pav.RightWidth), At(s, pav.LeftWidth), z, new OutputSettings(), w, 0.40);
            var medRect = new[] { At(s - hw - 0.5, lo), At(s + hw + 0.5, lo), At(s + hw + 0.5, hi), At(s - hw - 0.5, hi) }.ToList();
            foreach (var c in cw)
            {
                c.Exclusions.Add(new ExclusionZone { SourceId = tv.Id, Points = medRect });
                add(c);
            }
        }
    }
}
