using System.Globalization;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Geo;

/// <summary>UTM no SIRGAS 2000 (GRS80): k0 = 0,9996, falso leste 500 000 m, falso norte 10 000 000 m no hemisfério sul.</summary>
public sealed class Utm
{
    public const double K0 = 0.9996;
    private readonly LocalTransverseMercator _tm;

    public Utm(int zone, bool south)
    {
        Zone = zone;
        South = south;
        _tm = new LocalTransverseMercator(0, -183 + 6 * zone);
    }

    public int Zone { get; }
    public bool South { get; }

    public (double E, double N) Forward(double lat, double lon)
    {
        var (e, n) = _tm.Forward(lat, lon);
        return (500000 + K0 * e, (South ? 10000000 : 0) + K0 * n);
    }

    public (double Lat, double Lon) Inverse(double e, double n) =>
        _tm.Inverse((e - 500000) / K0, (n - (South ? 10000000 : 0)) / K0);

    /// <summary>Fuso de uma longitude.</summary>
    public static int ZoneOf(double lon) => Math.Clamp((int)Math.Floor((lon + 180) / 6) + 1, 1, 60);
}

/// <summary>Sistema de coordenadas de uma imagem georreferenciada.</summary>
public enum TipoCrs { Geografico, Utm, PlanoDoProjeto }

public sealed record ImageCrs(TipoCrs Tipo, int Zone = 0, bool South = true)
{
    public static readonly ImageCrs Geografico = new(TipoCrs.Geografico);
    public static readonly ImageCrs Projeto = new(TipoCrs.PlanoDoProjeto);

    public override string ToString() => Tipo switch
    {
        TipoCrs.Geografico => "Geográficas (lat/lon, SIRGAS 2000/WGS84)",
        TipoCrs.Utm => $"UTM SIRGAS 2000 fuso {Zone}{(South ? "S" : "N")}",
        _ => "Metros do projeto (já no plano do Revit)",
    };

    /// <summary>Código EPSG → sistema (4326/4674 geográficas; 31978–31985 SIRGAS 2000 UTM 18S–25S; 327xx/326xx WGS84 UTM).</summary>
    public static ImageCrs? FromEpsg(int epsg) => epsg switch
    {
        4326 or 4674 or 4618 => Geografico,
        >= 31978 and <= 31985 => new ImageCrs(TipoCrs.Utm, epsg - 31960, true),
        >= 31972 and <= 31977 => new ImageCrs(TipoCrs.Utm, epsg - 31954, false),
        >= 32701 and <= 32760 => new ImageCrs(TipoCrs.Utm, epsg - 32700, true),
        >= 32601 and <= 32660 => new ImageCrs(TipoCrs.Utm, epsg - 32600, false),
        _ => null,
    };

    /// <summary>Lê o essencial de um arquivo .prj (WKT): geográfico ou UTM zona NN S/N.</summary>
    public static ImageCrs? FromPrj(string wkt)
    {
        var m = System.Text.RegularExpressions.Regex.Match(wkt, @"UTM[ _]zone[ _](\d{1,2})\s*([NS])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return new ImageCrs(TipoCrs.Utm, int.Parse(m.Groups[1].Value), m.Groups[2].Value.Equals("S", StringComparison.OrdinalIgnoreCase));
        if (wkt.TrimStart().StartsWith("GEOGCS", StringComparison.OrdinalIgnoreCase) || wkt.TrimStart().StartsWith("GEOGCRS", StringComparison.OrdinalIgnoreCase)) return Geografico;
        return null;
    }

    /// <summary>Coordenada do sistema (x, y) → ponto do modelo (m).</summary>
    public Vec2 ToModel(GeoReference geo, double x, double y) => Tipo switch
    {
        TipoCrs.Geografico => geo.GeoToModel(y, x),
        TipoCrs.Utm => ToModelUtm(geo, x, y),
        _ => new Vec2(x, y),
    };

    public (double X, double Y) FromModel(GeoReference geo, Vec2 p)
    {
        switch (Tipo)
        {
            case TipoCrs.Geografico:
            {
                var (lat, lon) = geo.ModelToGeo(p);
                return (lon, lat);
            }
            case TipoCrs.Utm:
            {
                var (lat, lon) = geo.ModelToGeo(p);
                return new Utm(Zone, South).Forward(lat, lon);
            }
            default:
                return (p.X, p.Y);
        }
    }

    private Vec2 ToModelUtm(GeoReference geo, double e, double n)
    {
        var (lat, lon) = new Utm(Zone, South).Inverse(e, n);
        return geo.GeoToModel(lat, lon);
    }
}

/// <summary>
/// World file (.jgw/.pgw/.tfw): 6 linhas A, D, B, E, C, F – x = A·col + B·lin + C, y = D·col + E·lin + F, com (C, F) no
/// CENTRO do pixel superior esquerdo. Também montado a partir das tags do GeoTIFF (ModelPixelScale + ModelTiepoint).
/// </summary>
public sealed record WorldFile(double A, double D, double B, double E, double C, double F)
{
    public static WorldFile? Parse(string text)
    {
        var v = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => double.TryParse(l.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (double?)d : null)
            .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        if (v.Count < 6 || v[0] == 0 && v[2] == 0 || v[1] == 0 && v[3] == 0) return null;
        return new WorldFile(v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    /// <summary>GeoTIFF: ModelPixelScale (sx, sy) e ModelTiepoint (i, j, k, x, y, z) – o tiepoint é o canto do pixel (PixelIsArea).</summary>
    public static WorldFile? FromGeoTiff(double[]? pixelScale, double[]? tiepoint)
    {
        if (pixelScale is not { Length: >= 2 } s || tiepoint is not { Length: >= 6 } t || s[0] == 0 || s[1] == 0) return null;
        var x0 = t[3] - t[0] * s[0];
        var y0 = t[4] + t[1] * s[1];
        return new WorldFile(s[0], 0, 0, -s[1], x0 + s[0] / 2, y0 - s[1] / 2);
    }

    /// <summary>Coordenada do sistema no ponto contínuo (u, v) da imagem (u, v em pixels a partir do canto superior esquerdo).</summary>
    public (double X, double Y) At(double u, double v) => (A * (u - 0.5) + B * (v - 0.5) + C, D * (u - 0.5) + E * (v - 0.5) + F);

    /// <summary>Inversa: coordenada do sistema → (u, v) contínuos na imagem.</summary>
    public (double U, double V) Inverse(double x, double y)
    {
        var det = A * E - B * D;
        var dx = x - C;
        var dy = y - F;
        return ((E * dx - B * dy) / det + 0.5, (-D * dx + A * dy) / det + 0.5);
    }

    public string Format() => string.Join("\n", new[] { A, D, B, E, C, F }.Select(d => d.ToString("R", CultureInfo.InvariantCulture)));
}

/// <summary>
/// Chaves do GeoTIFF (GeoKeyDirectory, tag 34735): ProjectedCSTypeGeoKey (3072) ou GeographicTypeGeoKey (2048) → EPSG.
/// </summary>
public static class GeoTiffKeys
{
    public static int? Epsg(ushort[]? keys)
    {
        if (keys is not { Length: >= 4 }) return null;
        int? geog = null;
        for (int i = 4; i + 3 < keys.Length; i += 4)
        {
            var id = keys[i];
            var loc = keys[i + 1];
            var val = keys[i + 3];
            if (loc != 0) continue;                // valor em outra tag: não usado aqui
            if (id == 3072 && val != 32767) return val;
            if (id == 2048 && val != 32767) geog = val;
        }
        return geog;
    }
}

/// <summary>
/// Calibração de uma imagem por 2 pontos: semelhança (escala + rotação + translação) que leva os pontos clicados às
/// posições reais – pela distância digitada (só escala, o 1º ponto fica) ou por lat/lon dos dois pontos.
/// </summary>
public sealed record Similarity(Vec2 Pivot, double Scale, double Rotation, Vec2 Target)
{
    /// <summary>Leva um ponto da imagem atual à posição calibrada.</summary>
    public Vec2 Apply(Vec2 p) => Target + (p - Pivot).Rotate(Rotation) * Scale;

    /// <summary>Inversa: posição calibrada → ponto da imagem original.</summary>
    public Vec2 Inverse(Vec2 q) => Pivot + ((q - Target) / Scale).Rotate(-Rotation);

    /// <summary>Só escala, em torno do 1º ponto: distância digitada / medida.</summary>
    public static Similarity FromDistance(Vec2 p1, Vec2 p2, double realDistance)
    {
        var measured = p1.DistanceTo(p2);
        if (measured < 1e-6 || realDistance <= 0) throw new ArgumentException("Os dois pontos precisam ser diferentes e a distância real positiva.");
        return new Similarity(p1, realDistance / measured, 0, p1);
    }

    /// <summary>Escala, rotação (Norte verdadeiro) e posição pelos lat/lon reais dos dois pontos.</summary>
    public static Similarity FromGeo(GeoReference geo, Vec2 p1, Vec2 p2, (double Lat, double Lon) g1, (double Lat, double Lon) g2, bool rotate = true)
    {
        var t1 = geo.GeoToModel(g1.Lat, g1.Lon);
        var t2 = geo.GeoToModel(g2.Lat, g2.Lon);
        var a = p2 - p1;
        var b = t2 - t1;
        if (a.Length < 1e-6 || b.Length < 1e-6) throw new ArgumentException("Os dois pontos precisam ser diferentes.");
        return new Similarity(p1, b.Length / a.Length, rotate ? b.Angle - a.Angle : 0, rotate ? t1 : p1);
    }
}

/// <summary>Reamostra uma imagem importada para a grade métrica do projeto (mesma grade de 32 px da reprojeção dos tiles).</summary>
public static class ImageReprojection
{
    /// <summary>
    /// <paramref name="toSource"/> leva um ponto do modelo ao (u, v) contínuo na imagem de origem (BGR32, sw × sh). Fora da
    /// imagem fica branco.
    /// </summary>
    public static byte[] Resample(Vec2 min, Vec2 max, double mpp, int w, int h, Func<Vec2, (double U, double V)> toSource, byte[] src, int sw, int sh)
    {
        const int step = 32;
        var us = new List<double>(); for (var k = 0; k < w; k += step) us.Add(k); us.Add(w);
        var vs = new List<double>(); for (var k = 0; k < h; k += step) vs.Add(k); vs.Add(h);
        var nodes = new (double U, double V)[us.Count, vs.Count];
        for (int i = 0; i < us.Count; i++)
            for (int j = 0; j < vs.Count; j++)
                nodes[i, j] = toSource(new Vec2(min.X + us[i] * mpp, max.Y - vs[j] * mpp));
        var outp = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            var fy = y + 0.5;
            var j = Math.Clamp((int)(fy / step), 0, vs.Count - 2);
            var ty = (fy - vs[j]) / (vs[j + 1] - vs[j]);
            for (int x = 0; x < w; x++)
            {
                var fx = x + 0.5;
                var i = Math.Clamp((int)(fx / step), 0, us.Count - 2);
                var tx = (fx - us[i]) / (us[i + 1] - us[i]);
                var a = nodes[i, j]; var b = nodes[i + 1, j]; var c = nodes[i, j + 1]; var d = nodes[i + 1, j + 1];
                var su = (1 - tx) * (1 - ty) * a.U + tx * (1 - ty) * b.U + (1 - tx) * ty * c.U + tx * ty * d.U - 0.5;
                var sv = (1 - tx) * (1 - ty) * a.V + tx * (1 - ty) * b.V + (1 - tx) * ty * c.V + tx * ty * d.V - 0.5;
                var o = (y * w + x) * 4;
                var x0 = (int)Math.Floor(su);
                var y0 = (int)Math.Floor(sv);
                if (x0 < -1 || y0 < -1 || x0 >= sw || y0 >= sh) { outp[o] = outp[o + 1] = outp[o + 2] = outp[o + 3] = 255; continue; }
                var ax = su - x0;
                var ay = sv - y0;
                for (int ch = 0; ch < 3; ch++)
                {
                    double S(int px, int py) => src[(Math.Clamp(py, 0, sh - 1) * sw + Math.Clamp(px, 0, sw - 1)) * 4 + ch];
                    var val = (1 - ax) * (1 - ay) * S(x0, y0) + ax * (1 - ay) * S(x0 + 1, y0) + (1 - ax) * ay * S(x0, y0 + 1) + ax * ay * S(x0 + 1, y0 + 1);
                    outp[o + ch] = (byte)Math.Clamp(Math.Round(val), 0, 255);
                }
                outp[o + 3] = 255;
            }
        }
        return outp;
    }
}
