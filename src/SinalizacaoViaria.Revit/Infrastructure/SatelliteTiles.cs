using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SinalizacaoViaria.Core.Geo;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Falha ao obter a imagem – mensagem já pronta para o usuário.</summary>
public sealed class SatelliteException : Exception
{
    public SatelliteException(string message) : base(message) { }
}

/// <summary>
/// Acesso à internet da Imagem Aérea, só por fontes abertas ou configuradas pelo usuário: tiles do OpenStreetMap
/// (política de uso: User-Agent próprio, no máximo 2 conexões, cache respeitando a validade, sem download em massa),
/// fonte própria XYZ, WMS e busca de endereço no Nominatim (1 requisição por segundo).
/// </summary>
public static class SatelliteTiles
{
    private const string UserAgent = "SinalizaBIM/1.0 (plugin Revit de sinalizacao viaria; +https://github.com/josefanemacedo18/Sinaliza-o-Revit)";
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim TileGate = new(2);
    private static readonly SemaphoreSlim SearchGate = new(1);
    private static DateTime _lastSearch = DateTime.MinValue;

    public static string CacheRoot => Path.Combine(PluginPaths.Root, "tiles");

    private static HttpClient CreateClient()
    {
        var h = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        h.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return h;
    }

    private static string Folder(FonteImagem f) => f switch
    {
        FonteImagem.OpenStreetMap => "osm",
        // Fonte própria: uma pasta por endereço (trocar o endereço não mistura tiles de fontes diferentes).
        _ => "xyz-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(PluginContext.Settings.CustomTilesUrl ?? "")))[..10].ToLowerInvariant(),
    };

    private static string TileFile(FonteImagem f, int z, long x, long y) => Path.Combine(CacheRoot, Folder(f), z.ToString(), x.ToString(), y + ".img");

    private static string? TileUrl(FonteImagem f, int z, long x, long y) => f == FonteImagem.OpenStreetMap
        ? $"https://tile.openstreetmap.org/{z}/{x}/{y}.png"
        : SatelliteSource.TileUrl(PluginContext.Settings.CustomTilesUrl, z, x, y);

    private static bool Fresh(string file)
    {
        try
        {
            var exp = file + ".exp";
            return File.Exists(file) && File.Exists(exp) && long.TryParse(File.ReadAllText(exp), out var t) && t > DateTime.UtcNow.Ticks;
        }
        catch { return false; }
    }

    private static string SourceName(FonteImagem f) => f == FonteImagem.OpenStreetMap ? "o OpenStreetMap (tile.openstreetmap.org)" : "a fonte própria";

    // ------------------------------------------------------------------ tiles

    /// <summary>Baixa para o cache todos os tiles do plano (os válidos no cache não são baixados de novo).</summary>
    public static async Task DownloadAsync(SatellitePlan plan, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        var src = plan.Source.Fonte;
        if (src == FonteImagem.Personalizada && !SatelliteSource.IsValidTemplate(PluginContext.Settings.CustomTilesUrl))
            throw new SatelliteException("Configure o endereço da fonte própria (https://…/{z}/{x}/{y}) em SinalizaBIM → Configurações → Imagem aérea.");
        var tiles = plan.Tiles().ToList();
        var missing = tiles.Where(t => !Fresh(TileFile(src, plan.Zoom, t.X, t.Y))).ToList();
        var done = tiles.Count - missing.Count;
        progress?.Report((done, tiles.Count));
        var tasks = missing.Select(async t =>
        {
            await FetchAsync(src, plan.Zoom, t.X, t.Y, ct).ConfigureAwait(false);
            progress?.Report((Interlocked.Increment(ref done), tiles.Count));
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>Um tile para o mapa da janela (do cache ou baixado). Nulo se não houver.</summary>
    public static async Task<string?> TileAsync(FonteImagem src, int z, long x, long y, CancellationToken ct)
    {
        var file = TileFile(src, z, x, y);
        if (Fresh(file)) return file;
        try { await FetchAsync(src, z, x, y, ct).ConfigureAwait(false); }
        catch (SatelliteException) { }
        return File.Exists(file) ? file : null;
    }

    private static async Task FetchAsync(FonteImagem src, int z, long x, long y, CancellationToken ct)
    {
        var file = TileFile(src, z, x, y);
        var url = TileUrl(src, z, x, y) ?? throw new SatelliteException("Endereço da fonte própria inválido.");
        await TileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
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
                        // Validade: Cache-Control max-age ou Expires; sem cabeçalho, 7 dias.
                        var exp = res.Headers.CacheControl?.MaxAge is { } age ? DateTime.UtcNow + age
                            : res.Content.Headers.Expires is { } e ? e.UtcDateTime : DateTime.UtcNow.AddDays(7);
                        await File.WriteAllTextAsync(file + ".exp", exp.Ticks.ToString(), ct).ConfigureAwait(false);
                        return;
                    }
                    var code = res.StatusCode;
                    var transient = (int)code >= 500 || code == HttpStatusCode.TooManyRequests;
                    if (!transient || attempt >= 2)
                    {
                        if (File.Exists(file)) return;
                        throw new SatelliteException(src == FonteImagem.Personalizada
                            ? $"A fonte própria recusou o tile ({(int)code}). Confira o endereço, o acesso da sua conta e o zoom máximo em Configurações."
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
                        throw new SatelliteException($"Sem conexão com {SourceName(src)}. Verifique a internet, o proxy ou o firewall.");
                    }
                }
                finally { res?.Dispose(); }
                await Task.Delay(1000 * (attempt + 1), ct).ConfigureAwait(false);
            }
        }
        finally { TileGate.Release(); }
    }

    /// <summary>Tile do cache em BGR32 (256×256×4 bytes) ou nulo.</summary>
    public static byte[]? Load(FonteImagem src, int z, long x, long y)
    {
        var file = TileFile(src, z, x, y);
        if (!File.Exists(file)) return null;
        try { return DecodeBgr32(File.ReadAllBytes(file), WebMercator.TileSize, WebMercator.TileSize).Pixels; }
        catch (Exception ex)
        {
            Log.Info($"Imagem aérea – tile ilegível {Folder(src)}/{z}/{x}/{y}: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>Decodifica JPEG/PNG em BGR32; com tamanho informado, redimensiona para ele.</summary>
    public static (byte[] Pixels, int W, int H) DecodeBgr32(byte[] data, int? w = null, int? h = null)
    {
        using var ms = new MemoryStream(data);
        var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = dec.Frames[0];
        if (w is { } tw && h is { } th && (frame.PixelWidth != tw || frame.PixelHeight != th))
            frame = new TransformedBitmap(frame, new ScaleTransform((double)tw / frame.PixelWidth, (double)th / frame.PixelHeight));
        var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
        var buf = new byte[conv.PixelWidth * conv.PixelHeight * 4];
        conv.CopyPixels(buf, conv.PixelWidth * 4, 0);
        return (buf, conv.PixelWidth, conv.PixelHeight);
    }

    // ------------------------------------------------------------------ WMS e busca

    public static async Task<string> GetTextAsync(string url, string what, CancellationToken ct)
    {
        try
        {
            using var res = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) throw new SatelliteException($"{what}: o servidor respondeu {(int)res.StatusCode}.");
            return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (SatelliteException) { throw; }
        catch (Exception) { throw new SatelliteException($"{what}: sem conexão com o servidor. Verifique o endereço, a internet e o proxy."); }
    }

    public static async Task<byte[]> GetImageAsync(string url, string what, CancellationToken ct)
    {
        try
        {
            using var res = await Http.GetAsync(url, ct).ConfigureAwait(false);
            var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var type = res.Content.Headers.ContentType?.MediaType ?? "";
            if (!res.IsSuccessStatusCode || !type.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {
                var msg = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 300));
                throw new SatelliteException($"{what}: o servidor não devolveu imagem ({(int)res.StatusCode}). {msg}");
            }
            return bytes;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (SatelliteException) { throw; }
        catch (Exception) { throw new SatelliteException($"{what}: sem conexão com o servidor."); }
    }

    /// <summary>Busca de endereço no Nominatim, no máximo 1 requisição por segundo.</summary>
    public static async Task<List<GeoPlace>> SearchAsync(string text, CancellationToken ct)
    {
        if (Geocoder.Direct(text) is { } direct) return new List<GeoPlace> { direct };
        await SearchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = _lastSearch.AddSeconds(1.05) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
            _lastSearch = DateTime.UtcNow;
            return Geocoder.ParseNominatim(await GetTextAsync(Geocoder.NominatimUrl(text), "Busca de endereço", ct).ConfigureAwait(false));
        }
        finally { SearchGate.Release(); }
    }
}
