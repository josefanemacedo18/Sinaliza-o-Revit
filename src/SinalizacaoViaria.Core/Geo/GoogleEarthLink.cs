using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Geo;

/// <summary>
/// Vista atual do Google Earth Pro recebida pelo NetworkLink (viewFormat): caixa geográfica da vista (WMS BBOX), rumo,
/// inclinação, distância da câmera, campo de visão e tamanho da janela em pixels.
/// </summary>
public sealed record GeView(double West, double South, double East, double North, double Heading, double Tilt, double Range,
    double HorizFov, int PixelsX, int PixelsY, double Lat, double Lon)
{
    /// <summary>Vista de cima e com o Norte para cima (tolerância de 0,5°) – condição para a imagem valer como planta.</summary>
    public bool IsTopDown => Math.Abs(NormalizeHeading(Heading)) <= 0.5 && Math.Abs(Tilt) <= 0.5;

    public static double NormalizeHeading(double h)
    {
        var d = h % 360;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }

    /// <summary>Largura × altura da área visível (m) pela TM local no centro.</summary>
    public (double Width, double Height) SizeMeters()
    {
        var tm = new LocalTransverseMercator((North + South) / 2, (East + West) / 2);
        var (e1, _) = tm.Forward((North + South) / 2, West);
        var (e2, _) = tm.Forward((North + South) / 2, East);
        var (_, n1) = tm.Forward(South, (East + West) / 2);
        var (_, n2) = tm.Forward(North, (East + West) / 2);
        return (Math.Abs(e2 - e1), Math.Abs(n2 - n1));
    }

    /// <summary>Lê a query enviada pelo Google Earth (formato de <see cref="GoogleEarthKml.ViewFormat"/>).</summary>
    public static GeView? Parse(string query)
    {
        var q = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0].ToUpperInvariant()).ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First()[1].Replace('+', ' ')));
        double D(string k, double def = 0) => q.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
        if (!q.TryGetValue("BBOX", out var bbox)) return null;
        var b = bbox.Split(',').Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
        if (b.Length != 4 || b.Any(double.IsNaN)) return null;
        return new GeView(b[0], b[1], b[2], b[3], D("HEADING"), D("TILT"), D("RANGE"), D("FOV"), (int)D("W"), (int)D("H"),
            D("LAT", (b[1] + b[3]) / 2), D("LON", (b[0] + b[2]) / 2));
    }

    /// <summary>Marcadores de controle: perto dos cantos noroeste e sudeste da vista (15 % para dentro).</summary>
    public ((double Lat, double Lon) Nw, (double Lat, double Lon) Se) ControlMarkers()
    {
        var dLat = North - South;
        var dLon = East - West;
        return ((North - 0.15 * dLat, West + 0.15 * dLon), (South + 0.15 * dLat, East - 0.15 * dLon));
    }
}

/// <summary>KML do vínculo com o Google Earth Pro (NetworkLink com viewRefreshMode = onStop) e dos marcadores de controle.</summary>
public static class GoogleEarthKml
{
    /// <summary>
    /// Parâmetros pedidos ao Google Earth. Um viewFormat informado substitui o BBOX padrão – por isso o BBOX é pedido
    /// explicitamente junto com rumo, inclinação, distância, campo de visão e pixels.
    /// </summary>
    public const string ViewFormat =
        "BBOX=[bboxWest],[bboxSouth],[bboxEast],[bboxNorth]&HEADING=[lookatHeading]&TILT=[lookatTilt]&RANGE=[lookatRange]" +
        "&FOV=[horizFov]&W=[horizPixels]&H=[vertPixels]&LAT=[lookatLat]&LON=[lookatLon]";

    public static string NetworkLink(string viewUrl, string name = "SinalizaBIM – recorte para o Revit") =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <kml xmlns="http://www.opengis.net/kml/2.2">
          <NetworkLink>
            <name>{SecurityElement.Escape(name)}</name>
            <open>1</open>
            <refreshVisibility>0</refreshVisibility>
            <flyToView>0</flyToView>
            <Link>
              <href>{SecurityElement.Escape(viewUrl)}</href>
              <viewRefreshMode>onStop</viewRefreshMode>
              <viewRefreshTime>1</viewRefreshTime>
              <viewFormat>{SecurityElement.Escape(ViewFormat)}</viewFormat>
            </Link>
          </NetworkLink>
        </kml>
        """;

    /// <summary>Resposta a cada parada da vista: os 2 marcadores de controle (mira magenta, sem rótulo).</summary>
    public static string Markers(string iconUrl, params (double Lat, double Lon)[] points)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<kml xmlns="http://www.opengis.net/kml/2.2"><Document>""");
        sb.AppendLine($"""<Style id="sv"><IconStyle><scale>1</scale><Icon><href>{SecurityElement.Escape(iconUrl)}</href></Icon><hotSpot x="0.5" y="0.5" xunits="fraction" yunits="fraction"/></IconStyle><LabelStyle><scale>0</scale></LabelStyle></Style>""");
        for (int i = 0; i < points.Length; i++)
            sb.AppendLine(string.Create(ci, $"<Placemark><name>SV{i + 1}</name><styleUrl>#sv</styleUrl><Point><altitudeMode>clampToGround</altitudeMode><coordinates>{points[i].Lon:R},{points[i].Lat:R},0</coordinates></Point></Placemark>"));
        sb.AppendLine("</Document></kml>");
        return sb.ToString();
    }
}

/// <summary>
/// Servidor HTTP mínimo só no loopback (127.0.0.1) para o NetworkLink do Google Earth Pro: /view recebe a vista e devolve
/// os marcadores; /marker.png serve o ícone. Sem dependências e sem porta aberta para a rede.
/// </summary>
public sealed class GoogleEarthServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly byte[] _icon;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Última vista e marcadores enviados.</summary>
    public GeView? LastView { get; private set; }
    public ((double Lat, double Lon) Nw, (double Lat, double Lon) Se)? LastMarkers { get; private set; }
    public event Action<GeView>? ViewChanged;

    public GoogleEarthServer(byte[] iconPng)
    {
        _icon = iconPng;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoop();
    }

    public int Port { get; }
    public string ViewUrl => $"http://127.0.0.1:{Port}/view";
    public string IconUrl => $"http://127.0.0.1:{Port}/marker.png";

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false); }
            catch { return; }
            _ = Handle(c);
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                var buf = new byte[16384];
                var n = await stream.ReadAsync(buf, _cts.Token).ConfigureAwait(false);
                var req = Encoding.ASCII.GetString(buf, 0, n);
                var first = req.Split("\r\n")[0].Split(' ');
                var target = first.Length >= 2 ? first[1] : "/";
                var path = target.Split('?')[0];
                var query = target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "";
                byte[] body;
                string type;
                if (path == "/marker.png") { body = _icon; type = "image/png"; }
                else if (path == "/view" && GeView.Parse(query) is { } v)
                {
                    LastView = v;
                    LastMarkers = v.ControlMarkers();
                    ViewChanged?.Invoke(v);
                    body = Encoding.UTF8.GetBytes(GoogleEarthKml.Markers(IconUrl, LastMarkers.Value.Nw, LastMarkers.Value.Se));
                    type = "application/vnd.google-earth.kml+xml";
                }
                else { body = Encoding.UTF8.GetBytes(GoogleEarthKml.Markers(IconUrl)); type = "application/vnd.google-earth.kml+xml"; }
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, _cts.Token).ConfigureAwait(false);
                await stream.WriteAsync(body, _cts.Token).ConfigureAwait(false);
            }
            catch { /* conexão encerrada */ }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _cts.Dispose();
    }
}

/// <summary>
/// Encontra os marcadores de controle (mira magenta) numa imagem BGR32: pixels com vermelho e azul altos e verde baixo,
/// agrupados por vizinhança, centro pela média ponderada pela "intensidade de magenta" (precisão de fração de pixel).
/// </summary>
public static class MarkerDetector
{
    public static double Weight(byte b, byte g, byte r)
    {
        if (r < 150 || b < 150 || g > 120 || Math.Abs(r - b) > 80) return 0;
        return Math.Clamp((Math.Min(r, b) - g) / 255.0, 0, 1);
    }

    /// <summary>Centros (u, v) em pixels contínuos (centro do pixel k em k + 0,5) das maiores manchas, da maior para a menor.</summary>
    public static List<(double U, double V, int Pixels)> Find(byte[] bgr32, int w, int h, int minPixels = 12)
    {
        var seen = new bool[w * h];
        var found = new List<(double, double, int)>();
        var stack = new Stack<int>();
        for (int i = 0; i < w * h; i++)
        {
            if (seen[i] || Weight(bgr32[i * 4], bgr32[i * 4 + 1], bgr32[i * 4 + 2]) <= 0) continue;
            double sw = 0, su = 0, sv = 0;
            var count = 0;
            stack.Push(i);
            seen[i] = true;
            while (stack.Count > 0)
            {
                var k = stack.Pop();
                var x = k % w;
                var y = k / w;
                var wt = Weight(bgr32[k * 4], bgr32[k * 4 + 1], bgr32[k * 4 + 2]);
                sw += wt; su += wt * (x + 0.5); sv += wt * (y + 0.5);
                count++;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var nk = ny * w + nx;
                        if (seen[nk]) continue;
                        // Borda suavizada (antialiasing/JPEG): entra com peso menor, mas entra.
                        if (Weight(bgr32[nk * 4], bgr32[nk * 4 + 1], bgr32[nk * 4 + 2]) <= 0 && !SoftEdge(bgr32, nk)) continue;
                        seen[nk] = true;
                        stack.Push(nk);
                    }
            }
            if (count >= minPixels && sw > 0) found.Add((su / sw, sv / sw, count));
        }
        return found.OrderByDescending(f => f.Item3).ToList();
    }

    private static bool SoftEdge(byte[] p, int k) => p[k * 4 + 2] - p[k * 4 + 1] > 40 && p[k * 4] - p[k * 4 + 1] > 40;
}

/// <summary>Calibração de uma imagem salva do Google Earth: plano da imagem (u, −v) → modelo.</summary>
public static class GoogleEarthCalibration
{
    /// <summary>Pelos 2 marcadores detectados (posição geográfica conhecida): escala, posição e Norte verdadeiro.</summary>
    public static Similarity FromMarkers(GeoReference geo, (double U, double V) m1, (double U, double V) m2, (double Lat, double Lon) g1, (double Lat, double Lon) g2) =>
        Similarity.FromGeo(geo, new Vec2(m1.U, -m1.V), new Vec2(m2.U, -m2.V), g1, g2, rotate: true);

    /// <summary>Estimativa pela caixa da vista (cantos noroeste e sudeste da imagem) – aproximada.</summary>
    public static Similarity FromBbox(GeoReference geo, GeView v, int imageW, int imageH) =>
        Similarity.FromGeo(geo, new Vec2(0, 0), new Vec2(imageW, -imageH), (v.North, v.West), (v.South, v.East), rotate: true);
}
