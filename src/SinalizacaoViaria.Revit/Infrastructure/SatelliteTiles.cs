using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SinalizacaoViaria.Core.Geo;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Falha ao obter a imagem – mensagem já pronta para o usuário (sem chave nem endereço com chave).</summary>
public sealed class SatelliteException : Exception
{
    public SatelliteException(string message) : base(message) { }
}

/// <summary>
/// Tiles das fontes oficiais, com cache em disco (%AppData%\SinalizaBIM\tiles):
/// Google – Map Tiles API (sessão + chave do usuário); OpenStreetMap – tile.openstreetmap.org, seguindo a política de uso
/// (User-Agent próprio, no máximo 2 conexões, cache respeitando a validade, sem download em massa).
/// </summary>
public static class SatelliteTiles
{
    private const string UserAgent = "SinalizaBIM/1.0 (plugin Revit de sinalizacao viaria; +https://github.com/josefanemacedo18/Sinaliza-o-Revit)";
    private static readonly HttpClient Http = CreateClient();
    private static readonly object SessionGate = new();
    private static (string Token, DateTime Expiry)? _session;

    public static string CacheRoot => Path.Combine(PluginPaths.Root, "tiles");

    private static HttpClient CreateClient()
    {
        var h = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        h.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return h;
    }

    private static string Folder(FonteImagem f) => f == FonteImagem.GoogleSatelite ? "google" : "osm";
    private static string TileFile(FonteImagem f, int z, long x, long y) => Path.Combine(CacheRoot, Folder(f), z.ToString(), x.ToString(), y + ".img");

    /// <summary>Tile no cache e ainda válido.</summary>
    private static bool Fresh(string file)
    {
        try
        {
            var exp = file + ".exp";
            return File.Exists(file) && File.Exists(exp) && long.TryParse(File.ReadAllText(exp), out var t) && t > DateTime.UtcNow.Ticks;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------ Google (Map Tiles API)

    private static async Task<string> GoogleSessionAsync(string key, CancellationToken ct)
    {
        lock (SessionGate)
            if (_session is { } s && s.Expiry > DateTime.UtcNow.AddMinutes(5)) return s.Token;
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://tile.googleapis.com/v1/createSession?key=" + Uri.EscapeDataString(key))
        {
            Content = new StringContent("{\"mapType\":\"satellite\",\"language\":\"pt-BR\",\"region\":\"BR\"}", Encoding.UTF8, "application/json"),
        };
        HttpResponseMessage res;
        try { res = await Http.SendAsync(req, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { throw new SatelliteException("Sem conexão com o Google (tile.googleapis.com). Verifique a internet, o proxy ou o firewall."); }
        using (res)
        {
            if (!res.IsSuccessStatusCode) throw new SatelliteException(GoogleError(res.StatusCode));
            using var js = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var token = js.RootElement.GetProperty("session").GetString() ?? throw new SatelliteException("O Google não devolveu a sessão de tiles.");
            var expiry = DateTime.UtcNow.AddHours(12);
            if (js.RootElement.TryGetProperty("expiry", out var ex) && long.TryParse(ex.ValueKind == JsonValueKind.String ? ex.GetString() : ex.GetRawText(), out var sec))
                expiry = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;
            lock (SessionGate) _session = (token, expiry);
            return token;
        }
    }

    private static string GoogleError(HttpStatusCode code) => code switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
            $"O Google recusou a chave ({(int)code}). Confira em Configurações a chave da Google Maps Platform e, no Google Cloud, se a Map Tiles API está ativada e a cobrança configurada.",
        HttpStatusCode.TooManyRequests => "Limite de requisições da Map Tiles API atingido (429). Aguarde alguns minutos ou reduza a área.",
        HttpStatusCode.NotFound => "O Google não tem imagem neste zoom para toda a área (404). Diminua a resolução.",
        _ => $"Erro do servidor do Google ({(int)code}). Tente novamente mais tarde.",
    };

    /// <summary>Créditos e zoom máximo disponível na área (Map Tiles API – viewport). Nulo se não der para consultar.</summary>
    public static async Task<(string? Copyright, int? MaxZoom)> GoogleViewportAsync(string key, double north, double south, double east, double west, int zoom, CancellationToken ct)
    {
        try
        {
            var session = await GoogleSessionAsync(key, ct).ConfigureAwait(false);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var url = $"https://tile.googleapis.com/tile/v1/viewport?session={Uri.EscapeDataString(session)}&key={Uri.EscapeDataString(key)}" +
                      $"&zoom={zoom}&north={north.ToString(ci)}&south={south.ToString(ci)}&east={east.ToString(ci)}&west={west.ToString(ci)}";
            using var res = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return (null, null);
            using var js = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            string? copy = js.RootElement.TryGetProperty("copyright", out var c) ? c.GetString() : null;
            int? maxZ = null;
            if (js.RootElement.TryGetProperty("maxZoomRects", out var rects) && rects.ValueKind == JsonValueKind.Array)
            {
                // Zoom que cobre a área inteira: o menor entre os retângulos que tocam a área.
                foreach (var r in rects.EnumerateArray())
                    if (r.TryGetProperty("maxZoom", out var mz) && mz.TryGetInt32(out var z)) maxZ = maxZ is { } m ? Math.Min(m, z) : z;
            }
            return (copy, maxZ);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (SatelliteException) { throw; }
        catch (Exception ex)
        {
            Log.Info($"Imagem de satélite – viewport não consultado: {ex.GetType().Name}");
            return (null, null);
        }
    }

    // ------------------------------------------------------------------ download

    /// <summary>Baixa para o cache todos os tiles do plano (os válidos no cache não são baixados de novo).</summary>
    public static async Task DownloadAsync(SatellitePlan plan, string? googleKey, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        var src = plan.Source.Fonte;
        if (src == FonteImagem.GoogleSatelite && string.IsNullOrWhiteSpace(googleKey))
            throw new SatelliteException("Para o Google Satélite informe sua chave da Google Maps Platform em SinalizaBIM → Configurações.");
        var tiles = plan.Tiles().ToList();
        var missing = tiles.Where(t => !Fresh(TileFile(src, plan.Zoom, t.X, t.Y))).ToList();
        var done = tiles.Count - missing.Count;
        progress?.Report((done, tiles.Count));
        if (missing.Count == 0) return;
        string? session = src == FonteImagem.GoogleSatelite ? await GoogleSessionAsync(googleKey!, ct).ConfigureAwait(false) : null;
        using var gate = new SemaphoreSlim(src == FonteImagem.OpenStreetMap ? 2 : 6);
        var tasks = missing.Select(async t =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await FetchAsync(src, plan.Zoom, t.X, t.Y, session, googleKey, ct).ConfigureAwait(false);
                progress?.Report((Interlocked.Increment(ref done), tiles.Count));
            }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task FetchAsync(FonteImagem src, int z, long x, long y, string? session, string? key, CancellationToken ct)
    {
        var file = TileFile(src, z, x, y);
        var url = src == FonteImagem.GoogleSatelite
            ? $"https://tile.googleapis.com/v1/2dtiles/{z}/{x}/{y}?session={Uri.EscapeDataString(session!)}&key={Uri.EscapeDataString(key!)}"
            : $"https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        for (int attempt = 0; ; attempt++)
        {
            HttpResponseMessage? res = null;
            try
            {
                res = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    var tmp = file + ".tmp";
                    await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
                    File.Move(tmp, file, true);
                    // Validade: Cache-Control max-age ou Expires do servidor; sem cabeçalho, 1 dia (Google) ou 7 dias (OSM).
                    var exp = res.Headers.CacheControl?.MaxAge is { } age ? DateTime.UtcNow + age
                        : res.Content.Headers.Expires is { } e ? e.UtcDateTime
                        : DateTime.UtcNow.AddDays(src == FonteImagem.GoogleSatelite ? 1 : 7);
                    await File.WriteAllTextAsync(file + ".exp", exp.Ticks.ToString(), ct).ConfigureAwait(false);
                    return;
                }
                var code = res.StatusCode;
                var transient = (int)code >= 500 || code == HttpStatusCode.TooManyRequests;
                if (!transient || attempt >= 2)
                {
                    if (File.Exists(file)) return;                     // cópia antiga no cache ainda serve
                    throw new SatelliteException(src == FonteImagem.GoogleSatelite ? GoogleError(code)
                        : code is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or (HttpStatusCode)418
                            ? $"O servidor de tiles do OpenStreetMap recusou o pedido ({(int)code}) pela política de uso. Aguarde e tente uma área menor."
                            : $"Erro do servidor do OpenStreetMap ({(int)code}). Tente novamente mais tarde.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (SatelliteException) { throw; }
            catch (Exception)
            {
                if (attempt >= 2)
                {
                    if (File.Exists(file)) return;
                    throw new SatelliteException($"Sem conexão com {(src == FonteImagem.GoogleSatelite ? "o Google (tile.googleapis.com)" : "o OpenStreetMap (tile.openstreetmap.org)")}. Verifique a internet, o proxy ou o firewall.");
                }
            }
            finally { res?.Dispose(); }
            await Task.Delay(1000 * (attempt + 1), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Tile do cache em BGR32 (256×256×4 bytes) ou nulo.</summary>
    public static byte[]? Load(FonteImagem src, int z, long x, long y)
    {
        var file = TileFile(src, z, x, y);
        if (!File.Exists(file)) return null;
        try
        {
            using var fs = File.OpenRead(file);
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = dec.Frames[0];
            if (frame.PixelWidth != WebMercator.TileSize || frame.PixelHeight != WebMercator.TileSize)
                frame = new TransformedBitmap(frame, new ScaleTransform((double)WebMercator.TileSize / frame.PixelWidth, (double)WebMercator.TileSize / frame.PixelHeight));
            var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
            var buf = new byte[WebMercator.TileSize * WebMercator.TileSize * 4];
            conv.CopyPixels(buf, WebMercator.TileSize * 4, 0);
            return buf;
        }
        catch (Exception ex)
        {
            Log.Info($"Imagem de satélite – tile ilegível {Folder(src)}/{z}/{x}/{y}: {ex.GetType().Name}");
            return null;
        }
    }
}
