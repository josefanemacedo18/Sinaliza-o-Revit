namespace SinalizacaoViaria.Core.Geo;

/// <summary>
/// Web Mercator (EPSG:3857) dos mosaicos de tiles do Google e do OpenStreetMap: tiles de 256 px, pixel global
/// x = (λ + 180)/360 · 256·2^z e y = (1 − ln(tan φ + sec φ)/π)/2 · 256·2^z.
/// A escala dessa projeção varia com 1/cos φ e não é a mesma no sentido norte–sul e leste–oeste do elipsoide
/// (≈ 0,5 % de diferença no Brasil): por isso a imagem é sempre reamostrada para o plano local, nunca esticada.
/// </summary>
public static class WebMercator
{
    public const double EarthRadius = 6378137.0;
    public const int TileSize = 256;
    public const double MaxLatitude = 85.05112878;

    /// <summary>Metros por pixel no equador do tile (esfera de raio a): 156543,03392·cos φ / 2^z.</summary>
    public static double MetersPerPixel(double latDeg, int zoom) =>
        2 * Math.PI * EarthRadius * Math.Cos(latDeg * Math.PI / 180) / (TileSize * Math.Pow(2, zoom));

    /// <summary>Latitude/longitude (graus) → pixel global contínuo no zoom (o pixel k ocupa [k, k+1)).</summary>
    public static (double X, double Y) ToPixel(double latDeg, double lonDeg, int zoom)
    {
        var size = TileSize * Math.Pow(2, zoom);
        var lat = Math.Clamp(latDeg, -MaxLatitude, MaxLatitude) * Math.PI / 180;
        var x = (lonDeg + 180) / 360 * size;
        var y = (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 * size;
        return (x, y);
    }

    /// <summary>Pixel global → latitude/longitude (graus).</summary>
    public static (double Lat, double Lon) FromPixel(double x, double y, int zoom)
    {
        var size = TileSize * Math.Pow(2, zoom);
        var lon = x / size * 360 - 180;
        var lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / size))) * 180 / Math.PI;
        return (lat, lon);
    }

    /// <summary>Menor zoom cujo pixel é igual ou menor que a resolução pedida (limitado ao zoom máximo da fonte).</summary>
    public static int ZoomFor(double latDeg, double metersPerPixel, int maxZoom)
    {
        for (int z = 1; z <= maxZoom; z++)
            if (MetersPerPixel(latDeg, z) <= metersPerPixel) return z;
        return maxZoom;
    }
}

/// <summary>Distância geodésica no elipsoide (Vincenty, problema inverso) – referência para conferir a projeção.</summary>
public static class Geodesic
{
    public static double Distance(double lat1, double lon1, double lat2, double lon2, Ellipsoid? ellipsoid = null)
    {
        var el = ellipsoid ?? Ellipsoid.Grs80;
        double a = el.A, b = el.B, f = el.F;
        var L = (lon2 - lon1) * Math.PI / 180;
        var u1 = Math.Atan((1 - f) * Math.Tan(lat1 * Math.PI / 180));
        var u2 = Math.Atan((1 - f) * Math.Tan(lat2 * Math.PI / 180));
        double su1 = Math.Sin(u1), cu1 = Math.Cos(u1), su2 = Math.Sin(u2), cu2 = Math.Cos(u2);
        double lambda = L, prev;
        double sinS, cosS, sigma, cos2A, cos2Sm;
        var iter = 0;
        do
        {
            double sl = Math.Sin(lambda), cl = Math.Cos(lambda);
            sinS = Math.Sqrt(cu2 * sl * (cu2 * sl) + (cu1 * su2 - su1 * cu2 * cl) * (cu1 * su2 - su1 * cu2 * cl));
            if (sinS == 0) return 0;
            cosS = su1 * su2 + cu1 * cu2 * cl;
            sigma = Math.Atan2(sinS, cosS);
            var sinA = cu1 * cu2 * sl / sinS;
            cos2A = 1 - sinA * sinA;
            cos2Sm = cos2A != 0 ? cosS - 2 * su1 * su2 / cos2A : 0;
            var c = f / 16 * cos2A * (4 + f * (4 - 3 * cos2A));
            prev = lambda;
            lambda = L + (1 - c) * f * sinA * (sigma + c * sinS * (cos2Sm + c * cosS * (-1 + 2 * cos2Sm * cos2Sm)));
        }
        while (Math.Abs(lambda - prev) > 1e-13 && ++iter < 200);
        var u2b = cos2A * (a * a - b * b) / (b * b);
        var A = 1 + u2b / 16384 * (4096 + u2b * (-768 + u2b * (320 - 175 * u2b)));
        var B = u2b / 1024 * (256 + u2b * (-128 + u2b * (74 - 47 * u2b)));
        var dS = B * sinS * (cos2Sm + B / 4 * (cosS * (-1 + 2 * cos2Sm * cos2Sm) - B / 6 * cos2Sm * (-3 + 4 * sinS * sinS) * (-3 + 4 * cos2Sm * cos2Sm)));
        return b * A * (sigma - dS);
    }
}
