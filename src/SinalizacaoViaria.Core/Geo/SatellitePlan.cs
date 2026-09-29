using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Geo;

/// <summary>Fonte da imagem aérea.</summary>
public enum FonteImagem { OpenStreetMap, Personalizada }

/// <summary>Limites e créditos de cada fonte (uso responsável: nada de download em massa).</summary>
public sealed record SatelliteSource(FonteImagem Fonte, string Nome, int MaxZoom, int MaxTiles, double KbPerTile, string Attribution, bool IsPhoto)
{
    /// <summary>OpenStreetMap (tile.openstreetmap.org) – mapa, não foto; limite baixo pela política de uso dos tiles.</summary>
    public static readonly SatelliteSource Osm = new(FonteImagem.OpenStreetMap, "OpenStreetMap (mapa)", 19, 300, 14, "© OpenStreetMap contributors", false);

    /// <summary>
    /// Fonte XYZ configurada pelo próprio usuário ({z}/{x}/{y}, de uma conta ou serviço que ele tem direito de usar). O
    /// plugin não traz endereços pré-preenchidos; a licença e os créditos são responsabilidade de quem configura.
    /// </summary>
    public static SatelliteSource Custom(string? name, string? attribution, int maxZoom) =>
        new(FonteImagem.Personalizada, string.IsNullOrWhiteSpace(name) ? "Fonte própria (XYZ)" : name!.Trim(), Math.Clamp(maxZoom, 1, 22), 1500, 20,
            string.IsNullOrWhiteSpace(attribution) ? "Imagem: fonte configurada pelo usuário" : attribution!.Trim(), true);


    /// <summary>Endereço de tile de um modelo XYZ ({z}, {x}, {y}; {-y} para TMS). Nulo se o modelo for inválido.</summary>
    public static string? TileUrl(string? template, int z, long x, long y)
    {
        if (!IsValidTemplate(template)) return null;
        var tmsY = (1L << z) - 1 - y;
        return template!.Trim().Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{-y}", tmsY.ToString()).Replace("{y}", y.ToString());
    }

    public static bool IsValidTemplate(string? template) =>
        !string.IsNullOrWhiteSpace(template) && template.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && template.Contains("{z}") && template.Contains("{x}") && (template.Contains("{y}") || template.Contains("{-y}"));
}

/// <summary>Um bloco da imagem (vira uma imagem do Revit): pixels na imagem inteira e extensão no modelo (m).</summary>
public sealed record SatelliteBlock(int Col, int Row, int Px0, int Py0, int Width, int Height, Vec2 Min, Vec2 Max);

/// <summary>
/// Plano da imagem: retângulo no modelo (alinhado aos eixos do modelo), zoom da fonte, resolução de saída, blocos e tiles.
/// </summary>
public sealed class SatellitePlan
{
    /// <summary>Limite de pixels da imagem inteira (memória do Revit): acima disso a resolução é reduzida.</summary>
    public const long MaxPixels = 36_000_000;
    /// <summary>Lado máximo de cada imagem do Revit (px).</summary>
    public const int BlockSize = 4000;
    public const double MaxSide = 3000;

    public required GeoReference Geo { get; init; }
    public required SatelliteSource Source { get; init; }
    public Vec2 Min { get; init; }
    public Vec2 Max { get; init; }
    public int Zoom { get; init; }
    /// <summary>Pixel da fonte no centro (m, aproximado pela esfera do Web Mercator).</summary>
    public double SourceMpp { get; init; }
    /// <summary>Tamanho do pixel da imagem colocada no Revit (m) – exato no plano do projeto.</summary>
    public double OutputMpp { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public List<SatelliteBlock> Blocks { get; } = new();
    public long TileX0 { get; init; }
    public long TileY0 { get; init; }
    public long TileX1 { get; init; }
    public long TileY1 { get; init; }
    public int TileCount => (int)((TileX1 - TileX0 + 1) * (TileY1 - TileY0 + 1));
    public double EstimatedMB => TileCount * Source.KbPerTile / 1024;
    public double CenterLat { get; init; }
    public double CenterLon { get; init; }
    public List<string> Notes { get; } = new();
    public double Width => Max.X - Min.X;
    public double Height => Max.Y - Min.Y;

    /// <summary>
    /// Monta o plano: menor zoom com pixel ≤ resolução pedida (limitado pela fonte), resolução de saída nunca mais fina
    /// que a da fonte, limite de pixels e de tiles (reduz a resolução avisando), blocos de até 4000 px.
    /// </summary>
    public static SatellitePlan Create(GeoReference geo, SatelliteSource src, Vec2 center, double width, double height, double resolution)
    {
        width = Math.Clamp(width, 20, MaxSide);
        height = Math.Clamp(height, 20, MaxSide);
        resolution = Math.Clamp(resolution, 0.05, 20);
        var notes = new List<string>();
        var (lat, lon) = geo.ModelToGeo(center);
        var outMpp = resolution;
        var z = WebMercator.ZoomFor(lat, outMpp, src.MaxZoom);
        if (WebMercator.MetersPerPixel(lat, z) > outMpp * 1.001)
        {
            outMpp = WebMercator.MetersPerPixel(lat, z);
            notes.Add($"A fonte vai até o zoom {src.MaxZoom}: a resolução fica em {outMpp:0.00} m/px.");
        }
        long Pixels(double m) => (long)Math.Ceiling(width / m) * (long)Math.Ceiling(height / m);
        if (Pixels(outMpp) > MaxPixels)
        {
            outMpp *= Math.Sqrt((double)Pixels(outMpp) / MaxPixels) * 1.001;
            notes.Add($"Área grande: resolução ajustada para {outMpp:0.00} m/px (limite de {MaxPixels / 1_000_000} milhões de pixels no Revit).");
            z = WebMercator.ZoomFor(lat, outMpp, src.MaxZoom);
        }
        var range = TileRange(geo, center, width, height, z);
        while (Count(range) > src.MaxTiles && z > 1)
        {
            z--;
            range = TileRange(geo, center, width, height, z);
            outMpp = Math.Max(outMpp, WebMercator.MetersPerPixel(lat, z));
        }
        if (z < WebMercator.ZoomFor(lat, resolution, src.MaxZoom))
            notes.Add($"Limite de {src.MaxTiles} tiles por imagem ({src.Nome}): zoom {z}, {outMpp:0.00} m/px. Para mais detalhe, reduza a área.");
        var pw = (int)Math.Ceiling(width / outMpp);
        var ph = (int)Math.Ceiling(height / outMpp);
        var min = center - new Vec2(pw * outMpp / 2, ph * outMpp / 2);
        var plan = new SatellitePlan
        {
            Geo = geo, Source = src, Zoom = z, SourceMpp = WebMercator.MetersPerPixel(lat, z), OutputMpp = outMpp,
            PixelWidth = pw, PixelHeight = ph, Min = min, Max = min + new Vec2(pw * outMpp, ph * outMpp),
            TileX0 = range.X0, TileY0 = range.Y0, TileX1 = range.X1, TileY1 = range.Y1, CenterLat = lat, CenterLon = lon,
        };
        plan.Notes.AddRange(notes);
        var nx = (pw + BlockSize - 1) / BlockSize;
        var ny = (ph + BlockSize - 1) / BlockSize;
        for (int r = 0; r < ny; r++)
            for (int c = 0; c < nx; c++)
            {
                var px0 = c * pw / nx;
                var px1 = (c + 1) * pw / nx;
                var py0 = r * ph / ny;
                var py1 = (r + 1) * ph / ny;
                var bmin = new Vec2(min.X + px0 * outMpp, plan.Max.Y - py1 * outMpp);
                var bmax = new Vec2(min.X + px1 * outMpp, plan.Max.Y - py0 * outMpp);
                plan.Blocks.Add(new SatelliteBlock(c, r, px0, py0, px1 - px0, py1 - py0, bmin, bmax));
            }
        return plan;

        static long Count((long X0, long Y0, long X1, long Y1) t) => (t.X1 - t.X0 + 1) * (t.Y1 - t.Y0 + 1);
    }

    /// <summary>Tiles que cobrem o retângulo (contorno amostrado – o retângulo do modelo vira um quadrilátero curvo no mapa).</summary>
    private static (long X0, long Y0, long X1, long Y1) TileRange(GeoReference geo, Vec2 center, double width, double height, int z)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        const int k = 16;
        for (int i = 0; i <= k; i++)
            foreach (var p in new[]
                     {
                         new Vec2(-0.5 + (double)i / k, -0.5), new Vec2(-0.5 + (double)i / k, 0.5),
                         new Vec2(-0.5, -0.5 + (double)i / k), new Vec2(0.5, -0.5 + (double)i / k),
                     })
            {
                var m = center + new Vec2(p.X * width * 1.01, p.Y * height * 1.01);
                var (la, lo) = geo.ModelToGeo(m);
                var (px, py) = WebMercator.ToPixel(la, lo, z);
                x0 = Math.Min(x0, px); y0 = Math.Min(y0, py); x1 = Math.Max(x1, px); y1 = Math.Max(y1, py);
            }
        const int margin = 2;          // vizinhos da interpolação bilinear
        return ((long)Math.Floor((x0 - margin) / WebMercator.TileSize), (long)Math.Floor((y0 - margin) / WebMercator.TileSize),
                (long)Math.Floor((x1 + margin) / WebMercator.TileSize), (long)Math.Floor((y1 + margin) / WebMercator.TileSize));
    }

    /// <summary>Tiles (x, y) do plano, do centro para fora (a prévia aparece primeiro onde interessa).</summary>
    public IEnumerable<(long X, long Y)> Tiles()
    {
        var cx = (TileX0 + TileX1) / 2.0;
        var cy = (TileY0 + TileY1) / 2.0;
        var list = new List<(long, long)>();
        for (var y = TileY0; y <= TileY1; y++)
            for (var x = TileX0; x <= TileX1; x++) list.Add((x, y));
        return list.OrderBy(t => (t.Item1 - cx) * (t.Item1 - cx) + (t.Item2 - cy) * (t.Item2 - cy));
    }
}

/// <summary>
/// Reprojeção: cada pixel da imagem de saída (grade métrica do projeto) busca a cor na posição exata do mosaico Web
/// Mercator – modelo → terreno → TM local → lat/lon → pixel do tile – com interpolação bilinear. A conta exata é feita numa
/// grade de 32 px e interpolada dentro de cada célula; o erro dessa interpolação é medido no centro das células.
/// </summary>
public static class SatelliteReprojection
{
    public const int GridStep = 32;

    /// <summary>Pixel global da fonte (contínuo) de um ponto do modelo (m).</summary>
    public static (double X, double Y) SourcePixel(SatellitePlan plan, Vec2 model)
    {
        var (lat, lon) = plan.Geo.ModelToGeo(model);
        return WebMercator.ToPixel(lat, lon, plan.Zoom);
    }

    /// <summary>Ponto do modelo do pixel de saída (coordenadas contínuas: u para a direita, v para baixo, a partir do canto superior esquerdo do bloco).</summary>
    public static Vec2 ModelOf(SatellitePlan plan, SatelliteBlock b, double u, double v) =>
        new(b.Min.X + u * plan.OutputMpp, b.Max.Y - v * plan.OutputMpp);

    /// <summary>Grade de posições exatas na fonte (a cada <see cref="GridStep"/> px, mais a borda).</summary>
    public sealed class SourceGrid
    {
        public required double[] Us { get; init; }
        public required double[] Vs { get; init; }
        public required (double X, double Y)[,] Nodes { get; init; }

        public (double X, double Y) At(double u, double v)
        {
            var i = Index(Us, u);
            var j = Index(Vs, v);
            var fu = (u - Us[i]) / (Us[i + 1] - Us[i]);
            var fv = (v - Vs[j]) / (Vs[j + 1] - Vs[j]);
            var a = Nodes[i, j]; var b = Nodes[i + 1, j]; var c = Nodes[i, j + 1]; var d = Nodes[i + 1, j + 1];
            return ((1 - fu) * (1 - fv) * a.X + fu * (1 - fv) * b.X + (1 - fu) * fv * c.X + fu * fv * d.X,
                    (1 - fu) * (1 - fv) * a.Y + fu * (1 - fv) * b.Y + (1 - fu) * fv * c.Y + fu * fv * d.Y);
        }

        private static int Index(double[] arr, double x)
        {
            var i = (int)(x / GridStep);
            return Math.Clamp(i, 0, arr.Length - 2);
        }
    }

    public static SourceGrid Grid(SatellitePlan plan, SatelliteBlock b)
    {
        static double[] Axis(int n)
        {
            var list = new List<double>();
            for (var k = 0; k < n; k += GridStep) list.Add(k);
            list.Add(n);
            return list.ToArray();
        }
        var us = Axis(b.Width);
        var vs = Axis(b.Height);
        var nodes = new (double, double)[us.Length, vs.Length];
        for (int i = 0; i < us.Length; i++)
            for (int j = 0; j < vs.Length; j++)
                nodes[i, j] = SourcePixel(plan, ModelOf(plan, b, us[i], vs[j]));
        return new SourceGrid { Us = us, Vs = vs, Nodes = nodes };
    }

    /// <summary>
    /// Erro geométrico máximo da reprojeção (m): diferença entre a posição interpolada e a exata no centro de cada célula
    /// da grade, convertida pelo pixel da fonte. Não inclui o erro de posição da própria imagem (alguns metros).
    /// </summary>
    public static double MaxInterpolationError(SatellitePlan plan)
    {
        var worst = 0.0;
        foreach (var b in plan.Blocks)
        {
            var g = Grid(plan, b);
            for (int i = 0; i + 1 < g.Us.Length; i++)
                for (int j = 0; j + 1 < g.Vs.Length; j++)
                {
                    var u = (g.Us[i] + g.Us[i + 1]) / 2;
                    var v = (g.Vs[j] + g.Vs[j + 1]) / 2;
                    var exact = SourcePixel(plan, ModelOf(plan, b, u, v));
                    var interp = g.At(u, v);
                    var d = Math.Sqrt((exact.X - interp.X) * (exact.X - interp.X) + (exact.Y - interp.Y) * (exact.Y - interp.Y));
                    worst = Math.Max(worst, d);
                }
        }
        return worst * plan.SourceMpp;
    }

    /// <summary>
    /// Reamostra um bloco (BGRA, 4 bytes por pixel, linha a linha de cima para baixo). <paramref name="tile"/> devolve o
    /// tile (x, y) em BGRA 256×256 ou null (fica cinza).
    /// </summary>
    public static byte[] Resample(SatellitePlan plan, SatelliteBlock b, Func<long, long, byte[]?> tile, CancellationToken cancel = default)
    {
        var g = Grid(plan, b);
        var outp = new byte[b.Width * b.Height * 4];
        var cache = new Dictionary<(long, long), byte[]?>();
        (long X, long Y) lastKey = (long.MinValue, long.MinValue);
        byte[]? last = null;
        const int ts = WebMercator.TileSize;
        byte[]? TileOf(long tx, long ty)
        {
            if (tx == lastKey.X && ty == lastKey.Y) return last;
            if (!cache.TryGetValue((tx, ty), out var t)) cache[(tx, ty)] = t = tile(tx, ty);
            lastKey = (tx, ty);
            return last = t;
        }
        for (int v = 0; v < b.Height; v++)
        {
            cancel.ThrowIfCancellationRequested();
            for (int u = 0; u < b.Width; u++)
            {
                var (sx, sy) = g.At(u + 0.5, v + 0.5);
                // Centro do pixel k está em k + 0,5.
                var fx = sx - 0.5;
                var fy = sy - 0.5;
                var x0 = (long)Math.Floor(fx);
                var y0 = (long)Math.Floor(fy);
                var ax = fx - x0;
                var ay = fy - y0;
                double c0 = 0, c1 = 0, c2 = 0;
                for (int k = 0; k < 4; k++)
                {
                    var gx = x0 + (k & 1);
                    var gy = y0 + (k >> 1);
                    var w = ((k & 1) == 1 ? ax : 1 - ax) * ((k >> 1) == 1 ? ay : 1 - ay);
                    var tx = FloorDiv(gx, ts);
                    var ty = FloorDiv(gy, ts);
                    var t = TileOf(tx, ty);
                    if (t == null) { c0 += 0xD8 * w; c1 += 0xD8 * w; c2 += 0xD8 * w; continue; }
                    var o = ((int)(gy - ty * ts) * ts + (int)(gx - tx * ts)) * 4;
                    c0 += t[o] * w; c1 += t[o + 1] * w; c2 += t[o + 2] * w;
                }
                var p = (v * b.Width + u) * 4;
                outp[p] = (byte)Math.Clamp(Math.Round(c0), 0, 255);
                outp[p + 1] = (byte)Math.Clamp(Math.Round(c1), 0, 255);
                outp[p + 2] = (byte)Math.Clamp(Math.Round(c2), 0, 255);
                outp[p + 3] = 255;
            }
        }
        return outp;

        static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
