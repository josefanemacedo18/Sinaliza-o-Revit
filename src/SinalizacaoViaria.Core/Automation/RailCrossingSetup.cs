using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Cruzamento rodoferroviário completo (MBST Vol. IX / Vol. IV 5.8 – MCF; DER/SP projeto-tipo 13): em cada aproximação,
/// linha de retenção dupla paralela ao trilho, retângulo de advertência com a cruz de Santo André (SIF) por faixa, linha
/// dupla contínua (LFO-3) na aproximação, legenda PARE, LRV opcional e placas A-41 + R-1 (a 3,60 m do eixo da ferrovia)
/// e A-39/A-40 antecipadas. Tudo paramétrico.
/// </summary>
public sealed class RailCrossingSetup
{
    /// <summary>Estação do cruzamento (eixo da ferrovia) ao longo do eixo da via.</summary>
    public double Station { get; set; }
    /// <summary>Direção da ferrovia em planta (vetor unitário). Nulo = perpendicular à via.</summary>
    public Vec2? RailDirection { get; set; }
    /// <summary>Distância do eixo da ferrovia ao trilho externo (m): 0,80 m por linha (bitola larga ≈ 1,60 m).</summary>
    public double RailHalfWidth { get; set; } = 0.80;
    public int Tracks { get; set; } = 1;
    public bool TwoWay { get; set; } = true;
    public double RightWidth { get; set; } = 3.5;
    public double LeftWidth { get; set; } = 3.5;
    public int LanesPerDirection { get; set; } = 1;
    public double Speed { get; set; } = 60;
    /// <summary>Passagem em nível com barreira (cancela): A-40 em vez de A-39; linha de retenção a 2,0 m da cancela.</summary>
    public bool Barrier { get; set; }
    /// <summary>Distância da linha de retenção ao trilho externo (m): MBST ≥ 3,0 m; DER/SP 5,0 m sem barreira.</summary>
    public double StopLineDistance { get; set; } = 3.0;
    /// <summary>Distância do retângulo de advertência à linha de retenção (m): 15 a 150 m.</summary>
    public double RectangleDistance { get; set; } = 30;
    public double RectangleLength { get; set; } = 15;
    public double LineWidth { get; set; } = 0.40;
    /// <summary>Extensão da linha dupla contínua antes da linha de retenção (m). 0 = até a placa de advertência.</summary>
    public double NoPassingLength { get; set; }
    public bool PareLegend { get; set; } = true;
    public bool Lrv { get; set; }
    public bool Signs { get; set; } = true;
    public bool ConflictArea { get; set; }

    /// <summary>Distância de colocação da placa de advertência (A-39/A-40) antes da linha de retenção.</summary>
    public double WarningDistance => Math.Max(50, DesignRules.DecelerationDistance(Speed, 0) + 10);

    public List<MarkingDefinition> Build(PathReference axisRef, Polyline2 axis, double z, OutputSettings output)
    {
        var groupId = Guid.NewGuid().ToString("N");
        var res = new List<MarkingDefinition>();
        T Add<T>(T d) where T : MarkingDefinition
        {
            d.Output = output.Clone();
            d.GroupId = groupId;
            res.Add(d);
            return d;
        }
        var sRail = Math.Clamp(Station, 0, axis.Length);
        var tRail = axis.TangentAt(sRail);
        var nRail = tRail.PerpLeft;
        var rd = RailDirection is { } r && r.Length > 0.5 ? r.Normalized() : nRail;
        if (Math.Abs(rd.Dot(nRail)) < 0.35) rd = nRail;   // ferrovia quase paralela à via: usa a normal
        var halfTrack = RailHalfWidth + Math.Max(0, Tracks - 1) * 2.0;   // 2,0 m entre eixos de linhas contíguas (aproximação)
        var stopOffset = halfTrack + Math.Max(1, StopLineDistance) + (Barrier ? 0 : 0);
        var noPass = NoPassingLength > 0.5 ? NoPassingLength : WarningDistance;
        Vec2 P(double s, double o) => axis.PointAt(Math.Clamp(s, 0, axis.Length)) + axis.TangentAt(Math.Clamp(s, 0, axis.Length)).PerpLeft * o;

        // Linha dupla contínua (mão dupla) de um lado ao outro da ferrovia.
        if (TwoWay)
        {
            var s0 = Math.Max(0, sRail - stopOffset - noPass);
            var s1 = Math.Min(axis.Length, sRail + stopOffset + noPass);
            var pts = new List<Vec2>();
            for (var s = s0; s < s1; s += 2.0) pts.Add(P(s, 0));
            pts.Add(P(s1, 0));
            Add(new LinearMarkingDefinition { Code = "LFO-3", PathRef = PathReference.FromPoints(pts, z) });
        }
        foreach (var dir in TwoWay ? new[] { 1, -1 } : new[] { 1 })
        {
            // Faixas de aproximação: mão dupla = lado direito de quem se aproxima; mão única = toda a pista.
            double oInner, oOuter;
            if (TwoWay) { oInner = 0; oOuter = dir > 0 ? -RightWidth : LeftWidth; }
            else { oInner = LeftWidth; oOuter = -RightWidth; }
            var lanes = Math.Max(1, LanesPerDirection);
            var laneW = Math.Abs(oOuter - oInner) / lanes;
            var sStop = sRail - dir * stopOffset;
            // Linha de retenção dupla paralela ao trilho: pontos pela interseção com os bordos da aproximação.
            var q = P(sStop, 0);
            var t = axis.TangentAt(Math.Clamp(sStop, 0, axis.Length));
            var n = t.PerpLeft;
            var k = rd.Dot(n);
            Vec2 OnRail(double o) => q + rd * (o / k);
            Add(new LinearMarkingDefinition { Code = "MCF", WidthOverride = LineWidth, PathRef = PathReference.FromPoints(new[] { OnRail(oInner), OnRail(oOuter) }, z) });
            // Retângulo de advertência: duas linhas transversais a 15 m, com a cruz por faixa.
            var sR1 = sStop - dir * Math.Max(5, RectangleDistance);
            var sR2 = sR1 - dir * Math.Max(3, RectangleLength);
            foreach (var sr in new[] { sR1, sR2 })
                Add(new LinearMarkingDefinition { Code = "LRE", WidthOverride = LineWidth, PathRef = PathReference.FromPoints(new[] { P(sr, oInner), P(sr, oOuter) }, z) });
            var travel = dir > 0 ? t : -t;
            for (int i = 0; i < lanes; i++)
            {
                var oc = oInner + (oOuter - oInner) * (i + 0.5) / lanes;
                Add(new SymbolMarkingDefinition { Code = "CSA", Length = 6.0, WidthFactor = laneW > 3.5 ? 1.0 : 0.8, Position = P((sR1 + sR2) / 2, oc), Direction = travel, Z = z });
                if (PareLegend)
                {
                    var h = DesignRules.LegendHeight(Speed);
                    Add(new TextMarkingDefinition { Text = "PARE", Height = h, Position = P(sStop - dir * (LineWidth + 1.6 + h / 2), oc), Direction = travel, Z = z });
                }
                if (Lrv)
                {
                    var em = DesignRules.LrvStations(Speed, 20)[^1] + 2;
                    var pts = new List<Vec2>();
                    var sa = sR2 - dir * 2;
                    for (var s = 0.0; s <= em; s += 2.0) pts.Add(P(sa - dir * (em - s), oc));
                    Add(new LinearMarkingDefinition { Code = "LRV", LrvFromKmh = Speed, LrvToKmh = 20, WidthOverride = laneW - 0.2, PathRef = PathReference.FromPoints(pts, z) });
                }
            }
            if (ConflictArea)
                Add(new HatchMarkingDefinition
                {
                    Code = "MAC", ReferenceDirection = travel,
                    Boundary = PathReference.FromPoints(new[] { P(sRail - dir * (halfTrack + 1), oInner), P(sRail + dir * (halfTrack + 1), oInner), P(sRail + dir * (halfTrack + 1), oOuter), P(sRail - dir * (halfTrack + 1), oOuter) }, z, true),
                });
            if (Signs)
            {
                var side = oOuter + Math.Sign(oOuter - oInner) * 0.9;   // lado direito de quem se aproxima
                var sSign = sRail - dir * 3.60;
                Add(new SignDefinition { Code = "R-1", Position = P(sSign, side), Direction = travel, Z = z, Width = 0.60 });
                Add(new SignDefinition { Code = "A-41", Position = P(sSign, side), Direction = travel, Z = z, Width = 0.90, MountHeight = 2.10 + 0.60 + 0.15, Support = TipoSuporte.Nenhum,
                    Legend = Tracks > 1 ? $"{Tracks} LINHAS" : "1 LINHA" });
                Add(new SignDefinition { Code = Barrier ? "A-40" : "A-39", Position = P(sStop - dir * WarningDistance, side), Direction = travel, Z = z, Width = 0.60 });
                if (Speed > 40)
                    Add(new SignDefinition { Code = "R-19", Position = P(sStop - dir * (WarningDistance + 20), side), Direction = travel, Z = z, Width = 0.50, Legend = "40" });
            }
        }
        return res;
    }
}
