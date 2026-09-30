using SinalizacaoViaria.Core.Geo;
using SinalizacaoViaria.Core.Geometry;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada X: imagem de satélite na escala métrica real – projeção local, Web Mercator e reprojeção.</summary>
public class X31GeoTests
{
    private readonly ITestOutputHelper _out;
    public X31GeoTests(ITestOutputHelper output) => _out = output;

    private static readonly Ellipsoid El = Ellipsoid.Grs80;

    /// <summary>Diferença entre dois pontos geográficos em metros (pelos raios de curvatura locais).</summary>
    private static double MetersBetween(double lat1, double lon1, double lat2, double lon2)
    {
        var dn = (lat2 - lat1) * Math.PI / 180 * El.MeridianRadius(lat1);
        var de = (lon2 - lon1) * Math.PI / 180 * El.PrimeVerticalRadius(lat1) * Math.Cos(lat1 * Math.PI / 180);
        return Math.Sqrt(dn * dn + de * de);
    }

    [Theory]
    [InlineData(0.0, -50.0)]
    [InlineData(-15.0, -47.9)]
    [InlineData(-30.0, -51.2)]
    public void LocalTm_RoundTrip_IsBelowOneMillimetre(double lat0, double lon0)
    {
        var tm = new LocalTransverseMercator(lat0, lon0);
        var worst = 0.0;
        // Pontos até 5 km do centro, em 16 direções e 5 distâncias.
        for (int k = 0; k < 16; k++)
            foreach (var r in new[] { 10.0, 500, 1500, 3000, 5000 })
            {
                var a = k * Math.PI / 8;
                var (lat, lon) = tm.Inverse(r * Math.Cos(a), r * Math.Sin(a));
                var (e, n) = tm.Forward(lat, lon);
                var (lat2, lon2) = tm.Inverse(e, n);
                worst = Math.Max(worst, MetersBetween(lat, lon, lat2, lon2));
                worst = Math.Max(worst, Math.Sqrt((e - r * Math.Cos(a)) * (e - r * Math.Cos(a)) + (n - r * Math.Sin(a)) * (n - r * Math.Sin(a))));
            }
        _out.WriteLine($"lat0 {lat0}: ida e volta pior caso = {worst * 1000:0.000000} mm");
        Assert.True(worst < 0.001, $"{worst * 1000:0.0000} mm");
        // A origem é (0, 0).
        var (e0, n0) = tm.Forward(lat0, lon0);
        Assert.True(Math.Abs(e0) < 1e-6 && Math.Abs(n0) < 1e-6);
    }

    [Theory]
    [InlineData(0.0, -50.0)]
    [InlineData(-15.0, -47.9)]
    [InlineData(-23.55, -46.63)]
    [InlineData(-30.0, -51.2)]
    public void LocalTm_Distances_MatchTheGeodesicWithinTwoPpm(double lat0, double lon0)
    {
        var tm = new LocalTransverseMercator(lat0, lon0);
        var worst = 0.0;
        // Pares de 1 e 3 km em várias direções, com o ponto inicial até ~2 km do centro.
        foreach (var start in new[] { new Vec2(0, 0), new Vec2(1500, -800), new Vec2(-2000, 1200), new Vec2(300, 1900) })
            foreach (var d in new[] { 1000.0, 3000.0 })
                for (int k = 0; k < 12; k++)
                {
                    var end = start + Vec2.FromAngle(k * Math.PI / 6) * d;
                    var (la1, lo1) = tm.Inverse(start.X, start.Y);
                    var (la2, lo2) = tm.Inverse(end.X, end.Y);
                    var geo = Geodesic.Distance(la1, lo1, la2, lo2);
                    worst = Math.Max(worst, Math.Abs(geo - d) / d * 1e6);
                }
        _out.WriteLine($"lat0 {lat0}: plano × geodésica, pior caso = {worst:0.000} ppm");
        Assert.True(worst < 2, $"{worst:0.00} ppm");
    }

    [Fact]
    public void LocalTm_OneKilometreOnThePlane_IsOneKilometreOnTheEllipsoid()
    {
        // Referência do motivo: no UTM a 150 km do meridiano central a escala é ~0,99968 + … (até 40 cm/km). No TM local
        // com k0 = 1 no centro do projeto, 1 km medido no plano é 1 km no elipsoide.
        var tm = new LocalTransverseMercator(-23.55, -46.63);
        var (la1, lo1) = tm.Inverse(0, 0);
        var (la2, lo2) = tm.Inverse(1000, 0);
        Assert.Equal(1000, Geodesic.Distance(la1, lo1, la2, lo2), 3);
    }

    [Fact]
    public void ElevationFactor_At800m_IsAbout1Minus1Point25e4()
    {
        var f = El.ElevationFactor(-23, 800);
        _out.WriteLine($"fator de altitude a 800 m: 1 − {(1 - f):0.0000000e+0} ({(1 - f) * 1e5:0.0} cm/km)");
        Assert.Equal(1.2571e-4, 1 - f, 7);
        Assert.Equal(1.0, El.ElevationFactor(-23, 0), 12);
    }

    [Fact]
    public void GeoReference_ModelMetresAreGroundMetres_WithRotationAndAltitude()
    {
        var geo = new GeoReference { Latitude = -15.7939, Longitude = -47.8828, Altitude = 1170, NorthAngle = 12 * Math.PI / 180, OriginX = 250, OriginY = -80 };
        // Origem do modelo = origem geográfica.
        var (la0, lo0) = geo.ModelToGeo(geo.Origin);
        Assert.True(MetersBetween(la0, lo0, geo.Latitude, geo.Longitude) < 1e-6);
        // 1 km no modelo, em qualquer direção, é 1 km no terreno: geodésica = 1000 × fator de altitude.
        foreach (var dir in new[] { Vec2.UnitX, Vec2.UnitY, new Vec2(1, 1).Normalized(), new Vec2(-3, 1).Normalized() })
        {
            var a = geo.ModelToGeo(geo.Origin + new Vec2(300, 200));
            var b = geo.ModelToGeo(geo.Origin + new Vec2(300, 200) + dir * 1000);
            var d = Geodesic.Distance(a.Lat, a.Lon, b.Lat, b.Lon);
            Assert.True(Math.Abs(d - 1000 * geo.ElevationFactor) < 0.002, $"{d:0.0000} m");
        }
        // Ângulo de 12° (verdadeiro = rotação anti-horária do modelo, como o ProjectPosition.Angle do Revit): o Norte de
        // projeto (+Y) fica 12° a oeste do Norte verdadeiro, logo o Norte verdadeiro no modelo é (sin 12°, cos 12°).
        var north = geo.GeoToModel(geo.Latitude + 0.005, geo.Longitude) - geo.Origin;
        Assert.Equal(Math.Sin(12 * Math.PI / 180), north.Normalized().X, 3);
        Assert.Equal(Math.Cos(12 * Math.PI / 180), north.Normalized().Y, 3);
        // Ida e volta modelo ↔ geografia.
        var p = new Vec2(1234.5, -987.6);
        var (lat, lon) = geo.ModelToGeo(p);
        Assert.True(geo.GeoToModel(lat, lon).DistanceTo(p) < 0.001);
    }

    [Theory]
    [InlineData("-23.550520, -46.633308", -23.550520, -46.633308)]
    [InlineData("-23,550520; -46,633308", -23.550520, -46.633308)]
    [InlineData("23.5505° S 46.6333° W", -23.5505, -46.6333)]
    [InlineData("-15.7939,-47.8828", -15.7939, -47.8828)]
    public void ParsesCoordinatesPastedFromGoogleMaps(string text, double lat, double lon)
    {
        Assert.True(GeoReference.TryParseLatLon(text, out var la, out var lo));
        Assert.Equal(lat, la, 9);
        Assert.Equal(lon, lo, 9);
    }

    [Theory]
    [InlineData(8633.0)]          // valor visto na captura (elevação do local do Revit sem significado)
    [InlineData(-900.0)]
    [InlineData(double.NaN)]
    public void Altitude_OutOfRangeOrMissing_BecomesZeroWithWarning_AndThePlanStillWorks(double value)
    {
        var alt = GeoReference.SanitizeAltitude(value, out var warn);
        Assert.Equal(0, alt);
        Assert.NotNull(warn);
        Assert.Equal(0, GeoReference.SanitizeAltitude(null, out var w2));
        Assert.NotNull(w2);
        Assert.Equal(812, GeoReference.SanitizeAltitude(812, out var ok));
        Assert.Null(ok);
        // A georreferência continua válida com a altitude saneada (1 km no modelo = 1 km no terreno).
        var geo = new GeoReference { Latitude = -6.344007, Longitude = -47.396659, Altitude = alt };
        var a = geo.ModelToGeo(Vec2.Zero);
        var b = geo.ModelToGeo(new Vec2(1000, 0));
        Assert.Equal(1000, Geodesic.Distance(a.Lat, a.Lon, b.Lat, b.Lon), 2);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(30.0, 30.0)]
    [InlineData(-45.0, -45.0)]
    [InlineData(315.0, -45.0)]
    [InlineData(390.0, 30.0)]
    public void NorthAngle_DegreesConvertToRadiansAndBack(double degrees, double expected)
    {
        var rad = GeoReference.NorthAngleFromDegrees(degrees);
        Assert.Equal(expected * Math.PI / 180, rad, 12);
        Assert.Equal(expected, GeoReference.NorthAngleToDegrees(rad), 9);
        // 30°: o Norte verdadeiro fica, no modelo, na direção (sin 30°, cos 30°).
        if (expected == 30)
        {
            var geo = new GeoReference { Latitude = -6.34, Longitude = -47.39, NorthAngle = rad };
            var n = (geo.GeoToModel(-6.335, -47.39) - geo.Origin).Normalized();
            Assert.Equal(0.5, n.X, 3);
            Assert.Equal(Math.Sqrt(3) / 2, n.Y, 3);
        }
    }

    // ------------------------------------------------------------------ rodada X2: calibração, world file, UTM

    [Fact]
    public void Calibration_ByTypedDistance_ScalesAroundTheFirstPoint()
    {
        var p1 = new Vec2(12.5, -40);
        var p2 = p1 + Vec2.FromAngle(0.7) * 93.70;       // medido na imagem
        var s = Similarity.FromDistance(p1, p2, 100.00); // digitado
        Assert.Equal(1.06724, s.Scale, 5);
        Assert.True(s.Apply(p1).DistanceTo(p1) < 1e-9);
        Assert.Equal(100.0, s.Apply(p1).DistanceTo(s.Apply(p2)), 9);
        // Outro ponto da imagem se afasta do 1º na mesma proporção.
        var q = new Vec2(50, 10);
        Assert.Equal(q.DistanceTo(p1) * s.Scale, s.Apply(q).DistanceTo(p1), 9);
    }

    [Fact]
    public void Calibration_ByLatLon_MatchesTheGeodesic()
    {
        var geo = new GeoReference { Latitude = -6.344007, Longitude = -47.396659 };
        var g1 = (-6.343000, -47.398000);
        var g2 = (-6.349500, -47.389200);
        // Imagem com escala errada (fator 0,8) e girada 5°: os pontos clicados não estão onde deveriam.
        var t1 = geo.GeoToModel(g1.Item1, g1.Item2);
        var t2 = geo.GeoToModel(g2.Item1, g2.Item2);
        var p1 = new Vec2(3, 4);
        var p2 = p1 + (t2 - t1).Rotate(0.087) * 0.8;
        var s = Similarity.FromGeo(geo, p1, p2, g1, g2);
        var d = s.Apply(p1).DistanceTo(s.Apply(p2));
        var geod = Geodesic.Distance(g1.Item1, g1.Item2, g2.Item1, g2.Item2);
        _out.WriteLine($"calibração por lat/lon: {d:0.0000} m × geodésica {geod:0.0000} m ({Math.Abs(d - geod) / geod * 1e6:0.000} ppm)");
        Assert.True(Math.Abs(d - geod) / geod * 1e6 < 2);
        Assert.True(s.Apply(p1).DistanceTo(t1) < 1e-6 && s.Apply(p2).DistanceTo(t2) < 1e-6);
    }

    [Fact]
    public void Utm_Sirgas2000_Zone23S_MatchesPyproj()
    {
        // Referência (pyproj, EPSG:4674 → EPSG:31983): (−6,344007; −47,396659) → E 234 858,1714 N 9 298 154,8854;
        // (−23,550520; −46,633308) → E 333 287,1236 N 7 394 586,0946; (−23; −45) → E 500 000 N 7 456 480,2365.
        var utm = new Utm(23, true);
        foreach (var (lat, lon, e, n) in new[] { (-6.344007, -47.396659, 234858.17142, 9298154.88538), (-23.550520, -46.633308, 333287.12362, 7394586.09457), (-23.0, -45.0, 500000.0, 7456480.23648) })
        {
            var (fe, fn) = utm.Forward(lat, lon);
            _out.WriteLine($"UTM 23S ({lat}; {lon}): ΔE {(fe - e) * 1000:0.000} mm ΔN {(fn - n) * 1000:0.000} mm");
            Assert.True(Math.Abs(fe - e) < 0.01 && Math.Abs(fn - n) < 0.01, $"{fe - e:0.0000} {fn - n:0.0000}");
            var (la, lo) = utm.Inverse(e, n);
            Assert.True(MetersBetween(la, lo, lat, lon) < 0.01);
        }
        Assert.Equal(23, Utm.ZoneOf(-47.39));
        Assert.Equal(new ImageCrs(TipoCrs.Utm, 23, true), ImageCrs.FromEpsg(31983));
        Assert.Equal(ImageCrs.Geografico, ImageCrs.FromEpsg(4674));
        Assert.Equal(new ImageCrs(TipoCrs.Utm, 23, true), ImageCrs.FromPrj("PROJCS[\"SIRGAS 2000 / UTM zone 23S\",GEOGCS[...]]"));
    }

    [Fact]
    public void WorldFile_PixelSizeAndCorner_PlaceTheImageInTheLocalPlane()
    {
        // Ortofoto de 0,30 m em UTM 23S, canto superior esquerdo (borda do pixel) em E 234 700, N 9 298 300.
        var wf = WorldFile.Parse("0.30\n0\n0\n-0.30\n234700.15\n9298299.85\n")!;
        Assert.NotNull(wf);
        var corner = wf.At(0, 0);
        Assert.Equal(234700.0, corner.X, 9);
        Assert.Equal(9298300.0, corner.Y, 9);
        var (u, v) = wf.Inverse(234700.0 + 150, 9298300.0 - 90);
        Assert.Equal(500, u, 6);
        Assert.Equal(300, v, 6);
        // GeoTIFF equivalente (ModelPixelScale + ModelTiepoint).
        var gt = WorldFile.FromGeoTiff(new[] { 0.30, 0.30, 0 }, new[] { 0.0, 0, 0, 234700, 9298300, 0 })!;
        Assert.Equal(wf.C, gt.C, 9);
        Assert.Equal(wf.F, gt.F, 9);
        // No plano do projeto: 1000 px × 0,30 m no UTM = 300 m × (1/k do UTM) no terreno – posição e escala < 1 mm.
        var crs = new ImageCrs(TipoCrs.Utm, 23, true);
        var utm = new Utm(23, true);
        var (lat0, lon0) = utm.Inverse(234850, 9298150);
        var geo = new GeoReference { Latitude = lat0, Longitude = lon0 };
        var a = crs.ToModel(geo, wf.At(0, 0).X, wf.At(0, 0).Y);
        var b = crs.ToModel(geo, wf.At(1000, 0).X, wf.At(1000, 0).Y);
        var (la1, lo1) = utm.Inverse(wf.At(0, 0).X, wf.At(0, 0).Y);
        var (la2, lo2) = utm.Inverse(wf.At(1000, 0).X, wf.At(1000, 0).Y);
        var geod = Geodesic.Distance(la1, lo1, la2, lo2);
        _out.WriteLine($"world file UTM: 300 m de grade = {a.DistanceTo(b):0.0000} m no terreno (geodésica {geod:0.0000} m)");
        Assert.True(Math.Abs(a.DistanceTo(b) - geod) < 0.001);
        // Canto ~150 m a oeste/norte da origem: a grade UTM está girada ~0,27° (convergência meridiana a 265 km do meridiano
        // central do fuso) e com escala 1,0005 em relação ao plano local – por isso a imagem é reamostrada, não só posicionada.
        Assert.True(a.AlmostEquals(new Vec2(-150, 150), 2.0), $"{a}");
        var back = crs.FromModel(geo, a);
        Assert.True(Math.Abs(back.X - 234700) < 0.001 && Math.Abs(back.Y - 9298300) < 0.001);
        Assert.Equal(wf, WorldFile.Parse(wf.Format()));
    }

    [Fact]
    public void GeoTiffKeys_ReadTheEpsgCode()
    {
        // Diretório: versão 1.1.0, 3 chaves; GTModelType, GTRasterType, ProjectedCSType = 31983.
        var keys = new ushort[] { 1, 1, 0, 3, 1024, 0, 1, 1, 1025, 0, 1, 1, 3072, 0, 1, 31983 };
        Assert.Equal(31983, GeoTiffKeys.Epsg(keys));
        Assert.Equal(4674, GeoTiffKeys.Epsg(new ushort[] { 1, 1, 0, 1, 2048, 0, 1, 4674 }));
    }

    [Fact]
    public void ImageReprojection_KeepsAMarkedPixelAtItsGroundPosition()
    {
        // Imagem 200 × 200 px a 0,5 m/px no plano do projeto, com um pixel vermelho em (120, 80).
        var src = new byte[200 * 200 * 4];
        for (int i = 0; i < 200 * 200; i++) { src[i * 4] = 255; src[i * 4 + 3] = 255; }
        var o = (80 * 200 + 120) * 4; src[o] = 0; src[o + 2] = 255;
        var wf = new WorldFile(0.5, 0, 0, -0.5, 0.25, 99.75);        // canto (0, 100)
        var geo = new GeoReference { Latitude = -6.34, Longitude = -47.39 };
        var outp = ImageReprojection.Resample(new Vec2(0, 0), new Vec2(100, 100), 0.25, 400, 400,
            p => wf.Inverse(p.X, p.Y), src, 200, 200);
        int best = 0, bestR = -1;
        for (int i = 0; i < 400 * 400; i++) if (outp[i * 4 + 2] - outp[i * 4] > bestR) { bestR = outp[i * 4 + 2] - outp[i * 4]; best = i; }
        var x = (best % 400 + 0.5) * 0.25;
        var y = 100 - (best / 400 + 0.5) * 0.25;
        Assert.True(Math.Abs(x - 60.25) <= 0.5 && Math.Abs(y - 59.75) <= 0.5, $"({x}; {y})");
    }
}
