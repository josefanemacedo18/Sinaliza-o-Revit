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
    public void WebMercator_MetersPerPixel_FollowsTheCosineFormula()
    {
        var mpp = WebMercator.MetersPerPixel(-23, 18);
        _out.WriteLine($"zoom 18 a −23°: {mpp:0.000000} m/px");
        Assert.Equal(156543.03392 * Math.Cos(-23 * Math.PI / 180) / Math.Pow(2, 18), mpp, 9);
        Assert.Equal(0.5497, mpp, 4);
        // No equador e o dobro a cada zoom a menos.
        Assert.Equal(156543.03392, WebMercator.MetersPerPixel(0, 0), 3);
        Assert.Equal(2 * WebMercator.MetersPerPixel(-10, 19), WebMercator.MetersPerPixel(-10, 18), 12);
        // Ida e volta do pixel.
        var (x, y) = WebMercator.ToPixel(-23.5, -46.6, 19);
        var (la, lo) = WebMercator.FromPixel(x, y, 19);
        Assert.Equal(-23.5, la, 10);
        Assert.Equal(-46.6, lo, 10);
        // Zoom escolhido: menor com pixel ≤ resolução pedida.
        Assert.Equal(19, WebMercator.ZoomFor(-23, 0.3, 20));
        Assert.Equal(18, WebMercator.ZoomFor(-23, 0.55, 20));
        Assert.Equal(17, WebMercator.ZoomFor(-23, 0.3, 17));
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

    [Fact]
    public void Reprojection_PraçaDaSé_MapsToTheRightTileAndPixel()
    {
        // Praça da Sé (marco zero de São Paulo): −23,550520, −46,633308. Pixel global calculado à parte (fórmula do EPSG:3857):
        // zoom 19 → x = 49 722 706,642, y = 76 147 354,611 → tile (194229, 297450), pixel (82,64; 154,61) dentro dele.
        var geo = new GeoReference { Latitude = -23.550520, Longitude = -46.633308, Altitude = 760 };
        var plan = SatellitePlan.Create(geo, SatelliteSource.Google, Vec2.Zero, 200, 200, 0.3);
        Assert.Equal(19, plan.Zoom);
        var (sx, sy) = SatelliteReprojection.SourcePixel(plan, Vec2.Zero);
        Assert.Equal(49722706.642, sx, 2);
        Assert.Equal(76147354.611, sy, 2);
        Assert.Equal(194229, (long)Math.Floor(sx / 256));
        Assert.Equal(297450, (long)Math.Floor(sy / 256));
        Assert.InRange(plan.TileX0, 194220, 194229);
        Assert.InRange(plan.TileX1, 194229, 194240);

        // O centro do pixel de saída que contém a origem vai para o mesmo ponto da fonte (grade interpolada).
        var b = plan.Blocks.Single();
        var g = SatelliteReprojection.Grid(plan, b);
        var u = (0 - b.Min.X) / plan.OutputMpp;
        var v = (b.Max.Y - 0) / plan.OutputMpp;
        var at = g.At(u, v);
        Assert.True(Math.Abs(at.X - sx) < 0.01 && Math.Abs(at.Y - sy) < 0.01, $"{at.X - sx:0.0000}, {at.Y - sy:0.0000}");

        // 100 m para leste e para norte no terreno: deslocamento na fonte pelos raios do elipsoide – e NÃO pelo fator único
        // de "m/px", que erraria ~0,5 % no sentido norte–sul (a Web Mercator não é conforme ao elipsoide).
        var z = Math.Pow(2, plan.Zoom) * 256 / (2 * Math.PI);
        var lat = geo.Latitude * Math.PI / 180;
        var east = SatelliteReprojection.SourcePixel(plan, new Vec2(100, 0));
        var north = SatelliteReprojection.SourcePixel(plan, new Vec2(0, 100));
        var ef = geo.ElevationFactor;
        var expEast = 100 * ef / (El.PrimeVerticalRadius(geo.Latitude) * Math.Cos(lat)) * z;
        var expNorth = 100 * ef / El.MeridianRadius(geo.Latitude) / Math.Cos(lat) * z;
        Assert.Equal(expEast, east.X - sx, 1);
        Assert.Equal(expNorth, sy - north.Y, 1);
        var naive = 100 / plan.SourceMpp;
        _out.WriteLine($"100 m: leste {east.X - sx:0.000} px, norte {sy - north.Y:0.000} px; fator único daria {naive:0.000} px " +
                       $"(erro {(naive / (sy - north.Y) - 1) * 100:0.00} % no norte–sul)");
        Assert.True(Math.Abs(naive / (sy - north.Y) - 1) > 0.004);
    }

    [Fact]
    public void Plan_LimitsPixelsAndTiles_AndInterpolationErrorIsSubMillimetre()
    {
        var geo = new GeoReference { Latitude = -15.7939, Longitude = -47.8828, Altitude = 1170, NorthAngle = 0.3 };
        // 3 km × 3 km a 0,3 m/px passaria de 100 milhões de pixels: resolução ajustada e blocos de até 4000 px.
        var big = SatellitePlan.Create(geo, SatelliteSource.Google, new Vec2(100, 100), 3000, 3000, 0.3);
        Assert.True((long)big.PixelWidth * big.PixelHeight <= SatellitePlan.MaxPixels);
        Assert.All(big.Blocks, b => Assert.True(b.Width <= SatellitePlan.BlockSize && b.Height <= SatellitePlan.BlockSize));
        Assert.Equal(big.PixelWidth, big.Blocks.Where(b => b.Row == 0).Sum(b => b.Width));
        Assert.Equal(big.Width, big.PixelWidth * big.OutputMpp, 6);
        Assert.True(big.TileCount <= SatelliteSource.Google.MaxTiles);
        Assert.NotEmpty(big.Notes);
        // Blocos contíguos: a borda de um é a do vizinho.
        foreach (var b in big.Blocks.Where(b => b.Col > 0))
            Assert.Equal(big.Blocks.Single(o => o.Row == b.Row && o.Col == b.Col - 1).Max.X, b.Min.X, 9);
        // OSM: limite baixo de tiles (política de uso).
        var osm = SatellitePlan.Create(geo, SatelliteSource.Osm, Vec2.Zero, 3000, 3000, 0.3);
        Assert.True(osm.TileCount <= SatelliteSource.Osm.MaxTiles);
        // Erro geométrico da grade de interpolação.
        var small = SatellitePlan.Create(geo, SatelliteSource.Google, Vec2.Zero, 800, 600, 0.3);
        var err = SatelliteReprojection.MaxInterpolationError(small);
        _out.WriteLine($"800×600 m: zoom {small.Zoom}, {small.OutputMpp:0.000} m/px, {small.TileCount} tiles, erro da grade {err * 1000:0.0000} mm");
        Assert.True(err < 0.001, $"{err * 1000:0.000} mm");
    }

    [Fact]
    public void Resample_PutsTheSourceColourAtTheRightPlace()
    {
        // Mosaico sintético: cada tile pinta de vermelho o pixel global da Praça da Sé e o resto de azul.
        var geo = new GeoReference { Latitude = -23.550520, Longitude = -46.633308 };
        var plan = SatellitePlan.Create(geo, SatelliteSource.Google, Vec2.Zero, 60, 60, 0.3);
        var (sx, sy) = SatelliteReprojection.SourcePixel(plan, Vec2.Zero);
        long gx = (long)Math.Floor(sx), gy = (long)Math.Floor(sy);
        byte[] Tile(long tx, long ty)
        {
            var t = new byte[256 * 256 * 4];
            for (int i = 0; i < 256 * 256; i++) { t[i * 4] = 255; t[i * 4 + 3] = 255; }
            if (gx / 256 == tx && gy / 256 == ty)
            {
                var o = ((int)(gy % 256) * 256 + (int)(gx % 256)) * 4;
                t[o] = 0; t[o + 2] = 255;
            }
            return t;
        }
        var b = plan.Blocks.Single();
        var img = SatelliteReprojection.Resample(plan, b, Tile);
        // Pixel mais vermelho da saída = o que contém a origem do modelo (± 1 px).
        int best = -1, bestR = -1;
        for (int i = 0; i < b.Width * b.Height; i++) if (img[i * 4 + 2] > bestR) { bestR = img[i * 4 + 2]; best = i; }
        var u = best % b.Width;
        var v = best / b.Width;
        var model = SatelliteReprojection.ModelOf(plan, b, u + 0.5, v + 0.5);
        Assert.True(model.Length < 2 * Math.Max(plan.OutputMpp, plan.SourceMpp), $"{model}");
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
        // O plano continua válido com a altitude saneada.
        var geo = new GeoReference { Latitude = -6.344007, Longitude = -47.396659, Altitude = alt };
        var plan = SatellitePlan.Create(geo, SatelliteSource.Osm, Vec2.Zero, 800, 600, 0.3);
        Assert.True(plan.PixelWidth > 0 && plan.TileCount > 0);
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
}
