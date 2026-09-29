using System.Text.Json;
using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Core.Geo;

/// <summary>
/// Georreferenciamento do projeto: qual ponto do modelo (m) é a origem geográfica, com que altitude e com que rotação
/// para o Norte verdadeiro. Gravado no documento na primeira imagem – as seguintes usam a mesma origem e encaixam
/// exatamente nela (e nos eixos já desenhados).
/// Cadeia: modelo → (rotação do Norte) → plano local no terreno → (fator de altitude) → TM local no elipsoide → lat/lon.
/// </summary>
public sealed class GeoReference
{
    /// <summary>Latitude da origem (graus, SIRGAS 2000/WGS84; sul negativo).</summary>
    public double Latitude { get; set; }
    /// <summary>Longitude da origem (graus; oeste negativo).</summary>
    public double Longitude { get; set; }
    /// <summary>Altitude média do local (m) – as medidas do plano valem no terreno, não no elipsoide.</summary>
    public double Altitude { get; set; }
    /// <summary>
    /// Ângulo do Norte de projeto para o Norte verdadeiro (rad, anti-horário), como o <c>ProjectPosition.Angle</c> do
    /// Revit: coordenadas verdadeiras = rotação(ângulo) × (modelo − origem).
    /// </summary>
    public double NorthAngle { get; set; }
    /// <summary>Ponto do modelo (m) que corresponde à latitude/longitude da origem.</summary>
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    /// <summary>Elementos da última imagem colocada (imagens, escala gráfica, créditos) – para substituí-la de uma vez.</summary>
    public List<long> LastImageElements { get; set; } = new();
    public string? LastImageInfo { get; set; }

    private LocalTransverseMercator? _tm;
    [JsonIgnore]
    public LocalTransverseMercator Projection => _tm is { } t && t.Lat0 == Latitude && t.Lon0 == Longitude ? t : _tm = new LocalTransverseMercator(Latitude, Longitude);

    [JsonIgnore] public Vec2 Origin => new(OriginX, OriginY);

    /// <summary>Fator de altitude R/(R+h) (distância no elipsoide = distância no terreno × fator).</summary>
    [JsonIgnore] public double ElevationFactor => Ellipsoid.Grs80.ElevationFactor(Latitude, Altitude);

    /// <summary>Ponto do modelo (m) → leste/norte verdadeiros no terreno (m), a partir da origem.</summary>
    public Vec2 ModelToGround(Vec2 p) => (p - Origin).Rotate(NorthAngle);

    public Vec2 GroundToModel(Vec2 g) => Origin + g.Rotate(-NorthAngle);

    public (double Lat, double Lon) ModelToGeo(Vec2 p)
    {
        var g = ModelToGround(p) * ElevationFactor;
        return Projection.Inverse(g.X, g.Y);
    }

    public Vec2 GeoToModel(double lat, double lon)
    {
        var (e, n) = Projection.Forward(lat, lon);
        return GroundToModel(new Vec2(e, n) / ElevationFactor);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonConfig.Compact);

    public static GeoReference? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<GeoReference>(json, JsonConfig.Options); }
        catch { return null; }
    }

    /// <summary>
    /// Lê "lat, lon" como o Google Maps copia ("-23.550520, -46.633308"), também com ponto e vírgula, espaço ou graus
    /// com hemisfério ("23.5505 S 46.6333 W").
    /// </summary>
    public static bool TryParseLatLon(string? text, out double lat, out double lon)
    {
        lat = lon = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().Replace("°", " ").ToUpperInvariant();
        var nums = new List<double>();
        var signs = new List<int>();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var tokens = System.Text.RegularExpressions.Regex.Matches(t, @"[-+]?\d+(?:[.,]\d+)?|[NSEWLO]");
        // Número com ponto ou vírgula decimal; letras de hemisfério depois do número (N/S, E/L, W/O).
        foreach (System.Text.RegularExpressions.Match m in tokens)
        {
            var v = m.Value;
            if (v is "N" or "E" or "L") { if (signs.Count < nums.Count) signs.Add(1); continue; }
            if (v is "S" or "W" or "O") { if (signs.Count < nums.Count) signs.Add(-1); continue; }
            while (signs.Count < nums.Count) signs.Add(0);
            if (!double.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Float, ci, out var d)) return false;
            nums.Add(d);
        }
        while (signs.Count < nums.Count) signs.Add(0);
        if (nums.Count != 2) return false;
        lat = signs[0] != 0 ? signs[0] * Math.Abs(nums[0]) : nums[0];
        lon = signs[1] != 0 ? signs[1] * Math.Abs(nums[1]) : nums[1];
        return lat is >= -85 and <= 85 && lon is >= -180 and <= 180;
    }
}
