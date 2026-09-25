using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Parâmetros de instância de um conjunto de vagas.</summary>
public sealed class ParkingOptions
{
    public double? Angle { get; set; }
    public double? StallWidth { get; set; }
    public double? StallLength { get; set; }
    public double? LineWidth { get; set; }
    public MarkingColor? Color { get; set; }

    /// <summary>Quantidade de vagas. 0 = preencher todo o caminho.</summary>
    public int Count { get; set; }

    /// <summary>Vagas à direita do sentido do caminho (padrão) ou à esquerda.</summary>
    public bool RightSide { get; set; } = true;

    /// <summary>Recuo inicial ao longo do meio-fio (m).</summary>
    public double StartOffset { get; set; }

    /// <summary>Afastamento entre o meio-fio (caminho) e o início das vagas (m).</summary>
    public double CurbOffset { get; set; }

    /// <summary>Inclina as vagas no sentido contrário ao do caminho.</summary>
    public bool FlipAngle { get; set; }

    /// <summary>Desenha a linha de fundo (paralela ao meio-fio) delimitando as vagas.</summary>
    public bool BackLine { get; set; } = true;

    /// <summary>Desenha o contorno completo de cada vaga (retângulos).</summary>
    public bool FullOutline { get; set; }

    /// <summary>Inclui símbolo/legenda dentro das vagas especiais.</summary>
    public bool IncludeSymbols { get; set; } = true;

    /// <summary>Pinta o fundo da vaga (ex.: azul em vagas PcD, conforme prática local).</summary>
    public MarkingColor? FillColor { get; set; }

    public double SymbolSize { get; set; } = 1.20;
    public double LegendHeight { get; set; } = 0.50;
}

/// <summary>
/// Gera marcas de estacionamento (MER/MVE) ao longo de um meio-fio: vagas paralelas ou em ângulo,
/// vagas especiais com faixa adicional zebrada, símbolo SIA e legendas.
/// </summary>
public static class ParkingGenerator
{
    public static MarkingGeometry Generate(Polyline2 curb, VagaDef preset, ParkingOptions opt, Catalogo catalog, IGlyphOutlineProvider? glyphs)
    {
        var geo = new MarkingGeometry();
        if (curb.Length < 0.5) { geo.Warnings.Add("Meio-fio (caminho) muito curto."); return geo; }

        var angleDeg = Math.Clamp(opt.Angle ?? preset.Angulo, 0, 90);
        var w = opt.StallWidth ?? preset.Largura;
        var len = opt.StallLength ?? preset.Comprimento;
        var lw = opt.LineWidth ?? preset.LarguraLinha;
        var color = opt.Color ?? preset.Cor;
        var sideSign = opt.RightSide ? -1.0 : 1.0;
        var parallel = angleDeg < 1;
        var alpha = Angles.ToRad(angleDeg);

        // Sequência de "slots": vaga (S) e faixa adicional (A).
        var aisle = preset.FaixaAdicional;
        var stallFront = parallel ? len : w / Math.Sin(alpha);
        var aisleFront = parallel ? aisle : aisle / Math.Sin(alpha);
        var depth = parallel ? w : len * Math.Sin(alpha) + w * Math.Abs(Math.Cos(alpha));

        var available = curb.Length - opt.StartOffset;
        var slots = new List<(bool IsStall, double Front)>();
        double used = 0;
        var n = 0;
        while (true)
        {
            if (opt.Count > 0 && n >= opt.Count) break;
            var need = stallFront + (aisle > 0 && !parallel ? aisleFront : 0);
            // Em vagas inclinadas a última vaga projeta-se além da última divisória.
            var tail = parallel ? 0 : len * Math.Abs(Math.Cos(alpha));
            if (used + stallFront + tail > available + 1e-6) break;
            slots.Add((true, stallFront));
            used += stallFront;
            n++;
            if (aisle > 0 && !parallel && used + aisleFront <= available + 1e-6)
            {
                slots.Add((false, aisleFront));
                used += aisleFront;
            }
            if (need <= 0) break;
        }
        if (opt.Count > 0 && n < opt.Count)
            geo.Warnings.Add($"Somente {n} de {opt.Count} vagas cabem no trecho selecionado.");
        if (n == 0) { geo.Warnings.Add("Nenhuma vaga cabe no trecho selecionado."); return geo; }

        var curbOff = curb.Offset(sideSign * opt.CurbOffset);

        Vec2 Base(double s) => curbOff.PointAtParam(curb.ParamAt(s));
        Vec2 Into(double s) => (curb.TangentAt(s).PerpLeft * sideSign).Normalized();
        Vec2 AxisDir(double s)
        {
            var t = curb.TangentAt(s);
            if (opt.FlipAngle) t = -t;
            return (t * Math.Cos(alpha) + Into(s) * Math.Sin(alpha)).Normalized();
        }

        var lines = new List<Polygon2>();
        var boundaries = new List<double>();
        double st = opt.StartOffset;
        boundaries.Add(st);
        foreach (var sl in slots) { st += sl.Front; boundaries.Add(st); }

        var hatch = catalog.Hachura("PCD-FA");
        var sia = catalog.Simbolo("SIA");
        double painted = 0;

        // Divisórias
        foreach (var s in boundaries)
        {
            var p0 = Base(s);
            if (parallel)
            {
                var p1 = p0 + Into(s) * w;
                lines.AddRange(PolygonOps.Strip(new[] { p0, p1 }, lw));
                painted += w;
            }
            else
            {
                var p1 = p0 + AxisDir(s) * len;
                lines.AddRange(PolygonOps.Strip(new[] { p0, p1 }, lw));
                painted += len;
            }
        }

        // Linha de fundo
        if (opt.BackLine || opt.FullOutline)
        {
            var pts = new List<Vec2>();
            if (parallel)
            {
                var off = curb.Offset(sideSign * (opt.CurbOffset + w));
                pts = off.SubPoints(curb.ParamAt(boundaries[0]), curb.ParamAt(boundaries[^1]));
            }
            else
            {
                pts = boundaries.Select(s => Base(s) + AxisDir(s) * len).ToList();
            }
            if (pts.Count >= 2)
            {
                lines.AddRange(PolygonOps.Strip(pts, lw));
                painted += new Polyline2(pts).Length;
            }
        }
        if (opt.FullOutline)
        {
            var pts = curbOff.SubPoints(curb.ParamAt(boundaries[0]), curb.ParamAt(boundaries[^1]));
            lines.AddRange(PolygonOps.Strip(pts, lw));
            painted += new Polyline2(pts).Length;
        }

        var merged = PolygonOps.Union(lines);
        geo.AddRange(merged, color);
        geo.PaintedLength = painted;

        // Conteúdo de cada slot
        for (int i = 0; i < slots.Count; i++)
        {
            var s0 = boundaries[i];
            var s1 = boundaries[i + 1];
            var sm = (s0 + s1) / 2;
            var quad = StallQuad(s0, s1);

            if (!slots[i].IsStall)
            {
                if (hatch != null)
                {
                    var inner = PolygonOps.Offset(new[] { quad }, -lw / 2);
                    foreach (var reg in inner)
                    {
                        var h = HatchGenerator.Generate(reg, hatch, new HatchOptions { BorderWidth = 0, ReferenceDirection = parallel ? curb.TangentAt(sm) : AxisDir(sm) });
                        geo.Merge(h);
                    }
                }
                continue;
            }

            var symbols = new MarkingGeometry();
            if (opt.IncludeSymbols) AddStallSymbols(symbols, quad, s0, s1, sm);
            if (opt.FillColor is { } fill)
            {
                // Fundo pintado sem sobrepor símbolos/legendas (áreas corretas e regiões 2D válidas).
                var inner = PolygonOps.Offset(new[] { quad }, -lw / 2);
                geo.AddRange(PolygonOps.Difference(inner, symbols.Pieces.Select(p => p.Shape)), fill);
            }
            geo.Merge(symbols);
        }

        geo.PathLength = boundaries[^1] - boundaries[0];
        geo.UnitCount = n;
        if (!parallel && depth > 6.5)
            geo.Warnings.Add($"Profundidade ocupada pelas vagas: {depth:0.00} m – verifique a largura disponível da via.");
        return geo;

        void AddStallSymbols(MarkingGeometry target, Polygon2 quad, double s0, double s1, double sm)
        {
            var center = Polygon2.CentroidOf(quad.Outer);
            // O condutor entra na vaga vindo da pista: o "para cima" do símbolo aponta para o meio-fio.
            var forward = parallel ? curb.TangentAt(sm) : -AxisDir(sm);

            if (preset.Tipo == TipoVaga.PessoaComDeficiencia && sia != null)
            {
                var size = Math.Min(opt.SymbolSize, Math.Min(w, len) - 0.3);
                if (size > 0.3)
                {
                    var frame = new LocalFrame(center - forward.Normalized() * (size / 2), forward);
                    target.Merge(WithoutUnits(SymbolBuilder.Build(sia, size, frame)));
                }
            }
            else if (!string.IsNullOrWhiteSpace(preset.Legenda) && glyphs != null)
            {
                var lines2 = preset.Legenda!.Split('\n').Length;
                var h = opt.LegendHeight;
                var total = lines2 * h + (lines2 - 1) * h * 0.6;
                var frame = new LocalFrame(center - forward.Normalized() * (total / 2), forward);
                var txt = TextGenerator.Generate(preset.Legenda!, new TextOptions { Height = h, WidthFactor = 0.7, LetterSpacing = h * 0.08, LineSpacing = h * 0.6 }, frame, color, glyphs);
                txt.UnitCount = 0;
                txt.PathLength = 0;
                target.Merge(txt);
            }
        }

        Polygon2 StallQuad(double s0, double s1)
        {
            var a0 = Base(s0);
            var a1 = Base(s1);
            if (parallel)
                return new Polygon2(new[] { a0, a1, a1 + Into(s1) * w, a0 + Into(s0) * w });
            return new Polygon2(new[] { a0, a1, a1 + AxisDir(s1) * len, a0 + AxisDir(s0) * len });
        }
    }

    private static MarkingGeometry WithoutUnits(MarkingGeometry g)
    {
        g.UnitCount = 0;
        g.PathLength = 0;
        return g;
    }
}
