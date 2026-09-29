using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace SinalizacaoViaria.Core.Geo;

/// <summary>Camada de um serviço WMS (GetCapabilities).</summary>
public sealed record WmsLayer(string Name, string Title, IReadOnlyList<string> Crs);

public sealed record WmsCapabilities(string Version, IReadOnlyList<string> Formats, IReadOnlyList<WmsLayer> Layers);

/// <summary>
/// Cliente WMS (ortofotos de prefeituras, estados, IBGE): GetCapabilities e GetMap. No WMS 1.3.0 os sistemas geográficos
/// EPSG:4674/4326 usam a ordem latitude, longitude no BBOX; no 1.1.1 (SRS) sempre x = longitude.
/// </summary>
public static class Wms
{
    /// <summary>CRS aceitos para pedir a imagem (em ordem de preferência): UTM SIRGAS 2000 e geográficas SIRGAS/WGS84.</summary>
    public static string? PreferredCrs(WmsLayer layer, int utmZone, bool south)
    {
        var utm = south ? $"EPSG:{31960 + utmZone}" : $"EPSG:{31954 + utmZone}";
        foreach (var c in new[] { utm, "EPSG:4674", "EPSG:4326" })
            if (layer.Crs.Any(x => x.Equals(c, StringComparison.OrdinalIgnoreCase))) return c;
        return null;
    }

    public static bool IsGeographic(string crs) => crs.EndsWith(":4674") || crs.EndsWith(":4326") || crs.EndsWith(":4618");

    private static string Base(string url)
    {
        // Mantém parâmetros próprios do serviço (ex.: map=…), tira os do protocolo.
        var q = url.IndexOf('?');
        if (q < 0) return url + "?";
        var keep = url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !new[] { "service", "request", "version" }.Contains(p.Split('=')[0].ToLowerInvariant()));
        var rest = string.Join("&", keep);
        return url[..(q + 1)] + (rest.Length > 0 ? rest + "&" : "");
    }

    public static string CapabilitiesUrl(string url) => Base(url.Trim()) + "SERVICE=WMS&REQUEST=GetCapabilities";

    /// <summary>
    /// GetMap para uma caixa no sistema <paramref name="crs"/>: (minX, minY, maxX, maxY) com X = leste/longitude e
    /// Y = norte/latitude – a ordem dos eixos do protocolo é resolvida aqui.
    /// </summary>
    public static string GetMapUrl(string url, string version, string layer, string crs, double minX, double minY, double maxX, double maxY, int width, int height, string format)
    {
        var ci = CultureInfo.InvariantCulture;
        var v13 = version.StartsWith("1.3");
        var bbox = v13 && IsGeographic(crs)
            ? string.Join(",", new[] { minY, minX, maxY, maxX }.Select(d => d.ToString("R", ci)))
            : string.Join(",", new[] { minX, minY, maxX, maxY }.Select(d => d.ToString("R", ci)));
        return Base(url.Trim()) +
               $"SERVICE=WMS&VERSION={version}&REQUEST=GetMap&LAYERS={Uri.EscapeDataString(layer)}&STYLES=" +
               $"&{(v13 ? "CRS" : "SRS")}={Uri.EscapeDataString(crs)}&BBOX={bbox}&WIDTH={width}&HEIGHT={height}&FORMAT={Uri.EscapeDataString(format)}";
    }

    public static WmsCapabilities ParseCapabilities(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new FormatException("Resposta vazia do WMS.");
        var version = (string?)root.Attribute("version") ?? "1.3.0";
        var formats = root.Descendants().Where(e => e.Name.LocalName == "GetMap").Elements().Where(e => e.Name.LocalName == "Format")
            .Select(e => e.Value.Trim()).ToList();
        var layers = new List<WmsLayer>();
        void Walk(XElement layer, List<string> inherited)
        {
            var crs = inherited.Concat(layer.Elements().Where(e => e.Name.LocalName is "CRS" or "SRS")
                .SelectMany(e => e.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))).Distinct().ToList();
            var name = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value.Trim();
            var title = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "Title")?.Value.Trim() ?? name ?? "";
            if (!string.IsNullOrEmpty(name)) layers.Add(new WmsLayer(name!, title, crs));
            foreach (var child in layer.Elements().Where(e => e.Name.LocalName == "Layer")) Walk(child, crs);
        }
        var capability = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Capability");
        foreach (var top in capability?.Elements().Where(e => e.Name.LocalName == "Layer") ?? Enumerable.Empty<XElement>()) Walk(top, new List<string>());
        return new WmsCapabilities(version, formats, layers);
    }

    /// <summary>Formato preferido: JPEG (foto) e depois PNG.</summary>
    public static string PreferredFormat(IReadOnlyList<string> formats) =>
        formats.FirstOrDefault(f => f.Contains("jpeg", StringComparison.OrdinalIgnoreCase))
        ?? formats.FirstOrDefault(f => f.Contains("png", StringComparison.OrdinalIgnoreCase)) ?? "image/jpeg";
}

/// <summary>Resultado de busca de endereço (Nominatim/OpenStreetMap).</summary>
public sealed record GeoPlace(string Name, double Lat, double Lon);

public static class Geocoder
{
    /// <summary>Endereço de busca do Nominatim (limite de uso: 1 requisição por segundo, User-Agent próprio).</summary>
    public static string NominatimUrl(string text) =>
        "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=8&accept-language=pt-BR&q=" + Uri.EscapeDataString(text.Trim());

    public static List<GeoPlace> ParseNominatim(string json)
    {
        var list = new List<GeoPlace>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var name = e.TryGetProperty("display_name", out var n) ? n.GetString() ?? "" : "";
            if (e.TryGetProperty("lat", out var la) && e.TryGetProperty("lon", out var lo)
                && double.TryParse(la.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                && double.TryParse(lo.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                list.Add(new GeoPlace(name, lat, lon));
        }
        return list;
    }

    /// <summary>"lat, lon" colado vira resultado direto (sem consultar a internet).</summary>
    public static GeoPlace? Direct(string text) =>
        GeoReference.TryParseLatLon(text, out var lat, out var lon) ? new GeoPlace($"{lat:0.000000}, {lon:0.000000}", lat, lon) : null;
}
