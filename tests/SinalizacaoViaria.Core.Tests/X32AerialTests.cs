using System.Net.Http;
using System.Xml.Linq;
using SinalizacaoViaria.Core.Geo;
using SinalizacaoViaria.Core.Geometry;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada X3: Google Earth Pro (NetworkLink + marcadores), WMS e busca – sem chave de API.</summary>
public class X32AerialTests
{
    private readonly ITestOutputHelper _out;
    public X32AerialTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Kml_NetworkLink_RefreshesOnStopAndSendsTheView()
    {
        var kml = GoogleEarthKml.NetworkLink("http://127.0.0.1:5123/view");
        var doc = XDocument.Parse(kml);
        XNamespace k = "http://www.opengis.net/kml/2.2";
        var link = doc.Descendants(k + "NetworkLink").Single().Element(k + "Link")!;
        Assert.Equal("http://127.0.0.1:5123/view", link.Element(k + "href")!.Value);
        Assert.Equal("onStop", link.Element(k + "viewRefreshMode")!.Value);
        var vf = link.Element(k + "viewFormat")!.Value;
        foreach (var p in new[] { "[bboxWest]", "[bboxSouth]", "[bboxEast]", "[bboxNorth]", "[lookatHeading]", "[lookatTilt]", "[lookatRange]", "[horizFov]" })
            Assert.Contains(p, vf);
        // Marcadores: 2 Placemarks com o ícone servido localmente, sem rótulo.
        var mk = XDocument.Parse(GoogleEarthKml.Markers("http://127.0.0.1:5123/marker.png", (-6.3, -47.4), (-6.31, -47.39)));
        Assert.Equal(2, mk.Descendants(k + "Placemark").Count());
        Assert.Contains("-47.4,-6.3,0", mk.Descendants(k + "coordinates").First().Value);
    }

    [Fact]
    public void View_ParsesBboxHeadingTilt_AndChecksTopDown()
    {
        var v = GeView.Parse("BBOX=-47.4012,-6.3478,-47.3921,-6.3402&HEADING=359.8&TILT=0.2&RANGE=950.5&FOV=60&W=1600&H=900&LAT=-6.344&LON=-47.3966")!;
        Assert.Equal(-47.4012, v.West, 9);
        Assert.Equal(-6.3402, v.North, 9);
        Assert.Equal(1600, v.PixelsX);
        Assert.True(v.IsTopDown);
        Assert.False((v with { Tilt = 12 }).IsTopDown);
        Assert.False((v with { Heading = 20 }).IsTopDown);
        var (w, h) = v.SizeMeters();
        _out.WriteLine($"área visível {w:0.0} × {h:0.0} m");
        Assert.InRange(w, 1000, 1010);                    // 0,0091° de longitude a −6,34° ≈ 1006 m
        Assert.InRange(h, 838, 843);
        var (nw, se) = v.ControlMarkers();
        Assert.True(nw.Lat < v.North && nw.Lat > se.Lat && nw.Lon > v.West && nw.Lon < se.Lon);
        Assert.Null(GeView.Parse("HEADING=0"));
    }

    [Fact]
    public async Task Server_AnswersTheViewWithMarkers_OnlyOnLoopback()
    {
        using var server = new GoogleEarthServer(new byte[] { 1, 2, 3 });
        GeView? got = null;
        server.ViewChanged += v => got = v;
        Assert.StartsWith("http://127.0.0.1:", server.ViewUrl);
        using var http = new HttpClient();
        var kml = await http.GetStringAsync(server.ViewUrl + "?BBOX=-47.40,-6.35,-47.39,-6.34&HEADING=0&TILT=0&RANGE=900&FOV=60&W=1200&H=800&LAT=-6.345&LON=-47.395");
        Assert.Contains("<Placemark>", kml);
        Assert.NotNull(got);
        Assert.NotNull(server.LastMarkers);
        Assert.Equal(new byte[] { 1, 2, 3 }, await http.GetByteArrayAsync(server.IconUrl));
    }

    /// <summary>Mira magenta (anel + cruz) desenhada com antialiasing (4×4 subamostras) sobre um fundo com ruído.</summary>
    private static void DrawCross(byte[] img, int w, int h, double cx, double cy)
    {
        for (int y = (int)cy - 26; y <= (int)cy + 26; y++)
            for (int x = (int)cx - 26; x <= (int)cx + 26; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                var cover = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        var px = x + (sx + 0.5) / 4 - cx;
                        var py = y + (sy + 0.5) / 4 - cy;
                        var r = Math.Sqrt(px * px + py * py);
                        var on = Math.Abs(r - 15) <= 2 || Math.Abs(px) <= 2 && Math.Abs(py) <= 22 || Math.Abs(py) <= 2 && Math.Abs(px) <= 22;
                        if (on) cover++;
                    }
                if (cover == 0) continue;
                var a = cover / 16.0;
                var o = (y * w + x) * 4;
                img[o] = (byte)(img[o] * (1 - a) + 255 * a);
                img[o + 1] = (byte)(img[o + 1] * (1 - a));
                img[o + 2] = (byte)(img[o + 2] * (1 - a) + 255 * a);
            }
    }

    [Fact]
    public void Markers_AreFoundWithSubpixelAccuracy_AndCalibrateTheScale()
    {
        int w = 1200, h = 800;
        var rnd = new Random(7);
        var img = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            // Fundo "urbano": telhados, vegetação, asfalto – nada magenta.
            var kind = rnd.Next(3);
            img[i * 4] = (byte)(kind == 0 ? 60 + rnd.Next(40) : kind == 1 ? 40 + rnd.Next(30) : 90 + rnd.Next(30));
            img[i * 4 + 1] = (byte)(kind == 0 ? 90 + rnd.Next(40) : kind == 1 ? 120 + rnd.Next(60) : 90 + rnd.Next(30));
            img[i * 4 + 2] = (byte)(kind == 0 ? 170 + rnd.Next(60) : kind == 1 ? 50 + rnd.Next(40) : 95 + rnd.Next(30));
            img[i * 4 + 3] = 255;
        }
        var p1 = (U: 180.37, V: 120.61);
        var p2 = (U: 1019.82, V: 679.29);
        DrawCross(img, w, h, p1.U, p1.V);
        DrawCross(img, w, h, p2.U, p2.V);
        var found = MarkerDetector.Find(img, w, h);
        Assert.True(found.Count >= 2, $"{found.Count} marcadores");
        var two = found.Take(2).OrderBy(m => m.U + m.V).ToList();
        var e1 = Math.Sqrt(Math.Pow(two[0].U - p1.U, 2) + Math.Pow(two[0].V - p1.V, 2));
        var e2 = Math.Sqrt(Math.Pow(two[1].U - p2.U, 2) + Math.Pow(two[1].V - p2.V, 2));
        _out.WriteLine($"erro dos marcadores: {e1:0.000} px e {e2:0.000} px");
        Assert.True(e1 < 0.5 && e2 < 0.5);

        // Calibração: os marcadores estão em posições geográficas conhecidas.
        var geo = new GeoReference { Latitude = -6.344, Longitude = -47.3966 };
        var g1 = (-6.3405, -47.4005);
        var g2 = (-6.3470, -47.3925);
        var sim = GoogleEarthCalibration.FromMarkers(geo, (two[0].U, two[0].V), (two[1].U, two[1].V), g1, g2);
        var a = sim.Apply(new Vec2(p1.U, -p1.V));
        var b = sim.Apply(new Vec2(p2.U, -p2.V));
        var geod = Geodesic.Distance(g1.Item1, g1.Item2, g2.Item1, g2.Item2);
        var err = Math.Abs(a.DistanceTo(b) - geod) / geod;
        _out.WriteLine($"distância real entre as miras {a.DistanceTo(b):0.000} m × geodésica {geod:0.000} m ({err * 100:0.0000} %) · {1 / sim.Scale:0.0000} m/px");
        Assert.True(err < 0.001);
        // A inversa volta ao pixel.
        var back = sim.Inverse(sim.Apply(new Vec2(300, -200)));
        Assert.True(back.AlmostEquals(new Vec2(300, -200), 1e-9));
    }

    [Fact]
    public void Wms_GetMapUrl_UsesTheRightAxisOrder()
    {
        // WMS 1.3.0 + EPSG:4674 → BBOX em latitude, longitude.
        var u = Wms.GetMapUrl("https://geo.exemplo.gov.br/wms?map=orto", "1.3.0", "ortofoto_2020", "EPSG:4674", -47.40, -6.35, -47.39, -6.34, 1000, 800, "image/jpeg");
        Assert.Contains("map=orto&SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap", u);
        Assert.Contains("CRS=EPSG%3A4674", u);
        Assert.Contains("BBOX=-6.35,-47.4,-6.34,-47.39", u);
        // UTM (projetado): leste, norte.
        var v = Wms.GetMapUrl("https://x/wms", "1.3.0", "l", "EPSG:31983", 234700, 9298000, 235000, 9298300, 1000, 1000, "image/png");
        Assert.Contains("BBOX=234700,9298000,235000,9298300", v);
        // WMS 1.1.1: SRS e sempre longitude, latitude.
        var x = Wms.GetMapUrl("https://x/wms?", "1.1.1", "l", "EPSG:4674", -47.40, -6.35, -47.39, -6.34, 10, 10, "image/png");
        Assert.Contains("SRS=EPSG%3A4674", x);
        Assert.Contains("BBOX=-47.4,-6.35,-47.39,-6.34", x);
        Assert.Equal("https://x/wms?a=1&SERVICE=WMS&REQUEST=GetCapabilities", Wms.CapabilitiesUrl("https://x/wms?a=1&request=GetMap"));
    }

    [Fact]
    public void Wms_ParsesCapabilities()
    {
        const string xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <WMS_Capabilities version="1.3.0" xmlns="http://www.opengis.net/wms">
          <Capability>
            <Request><GetMap><Format>image/png</Format><Format>image/jpeg</Format></GetMap></Request>
            <Layer>
              <Title>Geoportal</Title>
              <CRS>EPSG:4674</CRS>
              <Layer><Name>orto2020</Name><Title>Ortofoto 2020</Title><CRS>EPSG:31983</CRS></Layer>
              <Layer><Name>lotes</Name><Title>Lotes</Title></Layer>
            </Layer>
          </Capability>
        </WMS_Capabilities>
        """;
        var caps = Wms.ParseCapabilities(xml);
        Assert.Equal("1.3.0", caps.Version);
        Assert.Equal("image/jpeg", Wms.PreferredFormat(caps.Formats));
        var orto = caps.Layers.Single(l => l.Name == "orto2020");
        Assert.Contains("EPSG:31983", orto.Crs);
        Assert.Contains("EPSG:4674", orto.Crs);                      // herdado da camada-mãe
        Assert.Equal("EPSG:31983", Wms.PreferredCrs(orto, 23, true));
        Assert.Equal("EPSG:4674", Wms.PreferredCrs(caps.Layers.Single(l => l.Name == "lotes"), 23, true));
    }

    [Fact]
    public void Geocoder_ParsesNominatim_AndDirectCoordinates()
    {
        var list = Geocoder.ParseNominatim("""[{"display_name":"Praça da Sé, São Paulo","lat":"-23.5503","lon":"-46.6339"}]""");
        Assert.Single(list);
        Assert.Equal(-23.5503, list[0].Lat, 9);
        Assert.NotNull(Geocoder.Direct("-6.344007, -47.396659"));
        Assert.Null(Geocoder.Direct("Rua Coronel Manoel Bandeira, Porto Franco"));
        Assert.Contains("q=Rua%20A%2C%20Cidade", Geocoder.NominatimUrl("Rua A, Cidade"));
    }
}
