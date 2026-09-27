using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>Recursos necessários para gerar a geometria de qualquer definição.</summary>
public sealed class BuildContext
{
    public required Catalogo Catalog { get; init; }
    public IGlyphOutlineProvider Glyphs { get; init; } = new BlockFontProvider();

    /// <summary>
    /// Comprimento máximo das peças (m). 0 = sem divisão – ao projetar sobre superfícies as peças contínuas são divididas
    /// só onde o terreno dobra (no Revit), e não em pedaços fixos.
    /// </summary>
    public double MaxPieceLength { get; init; }

    /// <summary>Trechos (m) dos dispositivos contínuos (barreiras, defensas) ao acompanhar superfícies.</summary>
    public double DeviceMaxPieceLength { get; init; }

    /// <summary>Escala da vista (ex.: 100 para 1:100) – dimensões de detalhamento em mm de papel.</summary>
    public double ViewScale { get; init; } = 100;

    /// <summary>Busca outra marca do projeto pelo identificador (detalhes e anotações).</summary>
    public Func<string, MarkingDefinition?>? Lookup { get; init; }

    /// <summary>Todas as marcas do projeto (quadro de legenda).</summary>
    public Func<IReadOnlyList<MarkingDefinition>>? AllDefinitions { get; init; }

    /// <summary>Eixo (primeiro trecho) de outra marca – interseções e rotatórias.</summary>
    public Func<MarkingDefinition, Polyline2?>? PathOf { get; init; }

    /// <summary>Geometria de outra marca do projeto (cotas de seção, quadros de quantitativos).</summary>
    public Func<MarkingDefinition, MarkingGeometry?>? GeometryOf { get; init; }

    /// <summary>Converte mm de papel em metros de modelo.</summary>
    public double Mm(double paperMm) => paperMm * ViewScale / 1000.0;
}

/// <summary>Dados descritivos de uma marca para parâmetros e tabelas.</summary>
public sealed record MarkingInfo(string Code, string Name, GrupoMarca Group, string Reference, string Unit);

/// <summary>Ponto único que transforma qualquer <see cref="MarkingDefinition"/> em geometria.</summary>
public static class MarkingBuilder
{
    public static MarkingGeometry Build(MarkingDefinition def, Polyline2? path, BuildContext ctx)
    {
        if (path != null && def.Justify is Justificacao.Esquerda or Justificacao.Direita && SupportsJustify(def) && path.Points.Count >= 2)
            path = JustifiedPath(def, path, ctx);
        var geo = BuildRaw(def, path, ctx);
        return def.Exclusions.Count == 0 ? geo : ApplyExclusions(geo, def.Exclusions);
    }

    /// <summary>Marcas ao longo de caminho que aceitam borda sobre a linha (em vez de centralizadas).</summary>
    public static bool SupportsJustify(MarkingDefinition d) =>
        d is LinearMarkingDefinition or DeviceMarkingDefinition or RepeatedMarkingDefinition or PlanterDefinition
            or HatchMarkingDefinition { IsStrip: true };

    /// <summary>Deslocamento lateral informado na marca, no sentido da linha de referência (+ à esquerda).</summary>
    private static double LateralOffset(MarkingDefinition d) => d switch
    {
        LinearMarkingDefinition l => l.Reverse ? -l.Offset : l.Offset,
        DeviceMarkingDefinition dv => dv.Offset,
        RepeatedMarkingDefinition r => r.Offset,
        PlanterDefinition p => p.Offset,
        HatchMarkingDefinition h => h.StripOffset,
        _ => 0,
    };

    /// <summary>
    /// Caminho deslocado para que a borda do elemento (e não o centro) fique sobre a linha de referência – mais o
    /// deslocamento informado. A largura é medida na própria geometria gerada.
    /// </summary>
    public static Polyline2 JustifiedPath(MarkingDefinition def, Polyline2 path, BuildContext ctx)
    {
        MarkingGeometry geo;
        try { geo = BuildRaw(def, path, ctx); }
        catch { return path; }
        var (lo, hi) = LateralExtent(geo, path);
        if (lo > hi) return path;
        var o = LateralOffset(def);
        var shift = def.Justify == Justificacao.Esquerda ? o - lo : o - hi;
        return Math.Abs(shift) < 1e-6 ? path : path.Offset(shift);
    }

    /// <summary>Menor e maior distância lateral (+ à esquerda) das peças em relação ao caminho.</summary>
    public static (double Lo, double Hi) LateralExtent(MarkingGeometry geo, Polyline2 path)
    {
        var pts = geo.Pieces.SelectMany(p => p.Shape.Outer).ToList();
        if (pts.Count == 0) return (1, -1);
        var step = Math.Max(1, pts.Count / 600);
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < pts.Count; i += step)
        {
            var d = path.SignedDistance(pts[i]);
            lo = Math.Min(lo, d);
            hi = Math.Max(hi, d);
        }
        return (lo, hi);
    }

    /// <summary>Recorta as peças pelas zonas de exclusão (ex.: calçada sob um rebaixamento).</summary>
    public static MarkingGeometry ApplyExclusions(MarkingGeometry geo, IEnumerable<ExclusionZone> zones)
    {
        var holes = zones.Where(z => z.Points.Count >= 3).Select(z => new Polygon2(z.Points)).ToList();
        if (holes.Count == 0) return geo;
        var res = new MarkingGeometry { PaintedLength = geo.PaintedLength, PathLength = geo.PathLength, UnitCount = geo.UnitCount };
        res.Warnings.AddRange(geo.Warnings);
        foreach (var p in geo.Pieces)
        {
            if (p.Profile != null || p.Solid != null)
            {
                if (!holes.Any(h => h.Contains(p.Shape.Centroid))) res.Pieces.Add(p);
                continue;
            }
            foreach (var part in PolygonOps.Difference(new[] { p.Shape }, holes))
            {
                var s = part.Simplified();
                if (s != null) res.Pieces.Add(p with { Shape = s });
            }
        }
        return res;
    }

    private static MarkingGeometry BuildRaw(MarkingDefinition def, Polyline2? path, BuildContext ctx) => def switch
    {
        LinearMarkingDefinition l => BuildLinear(l, path, ctx),
        HatchMarkingDefinition h => BuildHatch(h, path, ctx),
        SymbolMarkingDefinition s => BuildSymbol(s, ctx),
        TextMarkingDefinition t => BuildText(t, ctx),
        ParkingMarkingDefinition p => BuildParking(p, path, ctx),
        RepeatedMarkingDefinition r => BuildRepeated(r, path, ctx),
        DeviceMarkingDefinition dv => BuildDevice(dv, path, ctx),
        SignDefinition sg => BuildSign(sg, ctx),
        UrbanElementDefinition ue => BuildUrban(ue, path, ctx),
        RampDefinition rp => path == null || path.Points.Count < 2 ? Missing("Pontos da rampa não encontrados.") : RampGenerator.Generate(rp, path),
        SignPlanDetailDefinition sd => DetailGenerator.SignDetail(sd, ctx),
        LabelDefinition lb => DetailGenerator.Label(lb, ctx),
        LegendDefinition lg => DetailGenerator.Legend(lg, ctx),
        SectionDimensionDefinition sd2 => DetailGenerator.SectionDimensions(sd2, ctx),
        TypicalDetailDefinition td => DetailGenerator.TypicalDetail(td, ctx),
        QuantityTableDefinition qt => DetailGenerator.QuantityTable(qt, ctx),
        NotesDefinition nt => DetailGenerator.Notes(nt, ctx),
        NorthArrowDefinition na => DetailGenerator.NorthArrow(na, ctx),
        RoadPavementDefinition rp2 => path == null ? Missing("Eixo da via não encontrado.") : RoadGenerator.Pavement(rp2, path),
        IntersectionDefinition it => IntersectionGenerator.Build(it, ctx),
        TactileRouteDefinition tr => path == null ? Missing("Caminho da rota tátil não encontrado.") : TactileGenerator.Route(tr, new[] { path }, tr.Elevation),
        RoundaboutDefinition rb => RoundaboutGenerator.Build(rb, ctx),
        CurbExtensionDefinition ce => path == null ? Missing("Linha da face do meio-fio não encontrada.") : SidewalkGenerator.CurbExtension(ce, path, ctx),
        SidewalkAreaDefinition sa => path == null ? Missing("Contorno da área não encontrado.") : SidewalkGenerator.Area(sa, path),
        PlanterDefinition pl => path == null ? Missing("Linha dos canteiros não encontrada.") : SidewalkGenerator.Planter(pl, path, ctx),
        CulDeSacDefinition cd => path == null ? Missing("Eixo do cul-de-sac não encontrado.") : SidewalkGenerator.CulDeSac(cd, path),
        TrafficCalmingDefinition tc => path == null || path.Points.Count < 2 ? Missing("Bordos da pista não encontrados.") : TrafficCalmingGenerator.Generate(tc, path, ctx.Catalog),
        RailwayDefinition rw => path == null || path.Points.Count < 2 ? Missing("Eixo da via férrea não encontrado.") : RailwayGenerator.Build(rw, path),
        ChannelizationDefinition cz => path == null || path.Points.Count < 2 ? Missing("Linha de referência da canalização não encontrada.") : ChannelizationGenerator.Generate(cz, path, ctx),
        _ => throw new NotSupportedException(def.GetType().Name),
    };

    public static MarkingGeometry BuildLinear(LinearMarkingDefinition d, Polyline2? path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var type = ctx.Catalog.Linear(d.Code);
        if (type == null) { geo.Warnings.Add($"Código {d.Code} não existe no catálogo."); return geo; }
        if (path == null) { geo.Warnings.Add("Caminho da marca não encontrado (a linha de referência foi excluída?)."); return geo; }
        var variant = ResolveVariant(type, d.Variant, d.Speed);
        if (variant == null) { geo.Warnings.Add($"{d.Code}: nenhuma variante no catálogo."); return geo; }
        if (d.Code == "LRV" && d.LrvFromKmh is > 0)
        {
            // Linhas de estímulo à redução de velocidade dimensionadas pelo método do MBST Vol. IV (5.2).
            var v0 = d.LrvFromKmh.Value;
            var vf = Math.Clamp(d.LrvToKmh ?? v0 / 2, 0, v0 - 5);
            var lw = Automation.DesignRules.LrvLineWidth(v0);
            var pattern = Automation.DesignRules.LrvPattern(v0, vf, lw);
            var vc = new VarianteDef { Nome = variant.Nome, VelocidadeMax = variant.VelocidadeMax, Observacao = variant.Observacao };
            foreach (var f in variant.Faixas)
            {
                var fc = f.Clone();
                fc.Padrao = pattern;
                fc.Repetir = false;
                vc.Faixas.Add(fc);
            }
            variant = vc;
        }
        if (d.Code == "SARJETAO")
        {
            var w = d.WidthOverride ?? variant.Faixas.Max(f => f.Largura);
            var trimmed = RoadGenerator.Trimmed(path, d.StartSetback, d.EndSetback);
            return SarjetaoGenerator.Build(Math.Abs(d.Offset) > 1e-6 ? trimmed.Offset(d.Offset) : trimmed, w, d.Depth ?? 0.05);
        }
        if (d.Code is "PTA" or "PTD")
        {
            // Piso tátil: placas moduladas com relevo (NBR 16537), assentadas no topo da calçada.
            var w = d.WidthOverride ?? variant.Faixas.Max(f => f.Largura);
            var trimmed = RoadGenerator.Trimmed(path, d.StartSetback, d.EndSetback);
            return TactileGenerator.Linear(trimmed, w, d.Code == "PTA", d.ColorOverride ?? type.Cor, d.Offset);
        }

        var lin = LinearPatternGenerator.Generate(path, type, variant, new LinearOptions
        {
            Offset = d.Offset,
            Alignment = d.Alignment,
            Phase = d.Phase,
            StartSetback = d.StartSetback,
            EndSetback = d.EndSetback,
            Reverse = d.Reverse,
            InvertSides = d.InvertSides,
            WidthOverride = d.WidthOverride,
            PatternOverride = d.PatternOverride,
            ColorOverride = d.ColorOverride,
            MaxPieceLength = ctx.MaxPieceLength,
        });
        if (d.Height is { } top) ApplyTopLevel(lin, top);
        return lin;
    }

    /// <summary>Espessura mínima da laje de um elemento rebaixado ou no nível da pista (m).</summary>
    public const double FlushSlab = 0.10;

    /// <summary>
    /// Leva o topo das peças ao nível <paramref name="top"/> (m, relativo ao topo da pista): acima da pista o sólido
    /// nasce na pista; no nível ou abaixo dela vira uma laje de <see cref="FlushSlab"/> com o topo no nível pedido.
    /// </summary>
    public static void ApplyTopLevel(MarkingGeometry geo, double top)
    {
        for (int i = 0; i < geo.Pieces.Count; i++)
        {
            var p = geo.Pieces[i];
            if (p.Solid != null || p.Profile != null) continue;
            geo.Pieces[i] = top >= 0.01
                ? p with { Elevation = 0, Thickness = top }
                : p with { Elevation = top - FlushSlab, Thickness = FlushSlab };
        }
    }

    public static VarianteDef? ResolveVariant(TipoLinearDef type, string? variant, double? speed)
    {
        if (!string.IsNullOrWhiteSpace(variant))
        {
            var v = type.Variantes.FirstOrDefault(x => string.Equals(x.Nome, variant, StringComparison.OrdinalIgnoreCase));
            if (v != null) return v;
        }
        if (speed is > 0) return type.VariantePorVelocidade(speed.Value);
        return type.Variantes.FirstOrDefault();
    }

    public static MarkingGeometry BuildHatch(HatchMarkingDefinition d, Polyline2? path, BuildContext ctx) =>
        BuildHatch(d, path == null ? Array.Empty<Polyline2>() : new[] { path }, ctx);

    /// <summary>
    /// Zebrado de vários contornos: contornos fechados um dentro do outro formam furos (regra par-ímpar) – ex.: anel
    /// em volta de uma rotatória (círculo externo + círculo interno); contornos separados viram áreas independentes.
    /// </summary>
    public static MarkingGeometry BuildHatch(HatchMarkingDefinition d, IReadOnlyList<Polyline2> contours, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var preset = ctx.Catalog.Hachura(d.Code);
        if (preset == null) { geo.Warnings.Add($"Código {d.Code} não existe no catálogo."); return geo; }
        if (d.IsStrip)
        {
            if (contours.Count == 0) { geo.Warnings.Add("Caminho da faixa não encontrado."); return geo; }
            foreach (var path in contours) geo.Merge(BuildHatchStrip(d, preset, path, ctx));
            return geo;
        }
        var rings = new List<IReadOnlyList<Vec2>>();
        foreach (var path in contours)
        {
            if (path.Points.Count < 3) continue;
            var pts = path.Points.ToList();
            if (pts.Count > 3 && pts[0].AlmostEquals(pts[^1], 1e-4)) pts.RemoveAt(pts.Count - 1);
            rings.Add(pts);
        }
        if (rings.Count == 0) { geo.Warnings.Add("Contorno da área não encontrado ou aberto."); return geo; }

        // Resolve autointerseções/contornos invertidos; com mais de um contorno, o interno vira furo (par-ímpar).
        var regions = rings.Count > 1 ? PolygonOps.FromContoursEvenOdd(rings) : PolygonOps.FromContours(rings);
        foreach (var region in regions)
        {
            geo.Merge(HatchGenerator.Generate(region, preset, new HatchOptions
            {
                BarWidth = d.BarWidth,
                Gap = d.Gap,
                AngleDeg = d.AngleDeg,
                BorderWidth = d.BorderWidth,
                Chevron = d.Chevron,
                Crossed = d.Crossed,
                BarColor = d.BarColor,
                BorderColor = d.BorderColor,
                ReferenceDirection = d.ReferenceDirection,
                AxisPoint = d.AxisPoint,
                Phase = d.Phase,
            }));
        }
        if (regions.Count == 0) geo.Warnings.Add("O contorno selecionado não forma uma área válida.");
        return geo;
    }

    /// <summary>
    /// Deslocamento lateral da faixa conforme o lado escolhido: à esquerda/direita do caminho ou, em contornos
    /// fechados, para dentro/para fora (independentemente do sentido em que o contorno foi desenhado).
    /// </summary>
    public static double StripShift(HatchMarkingDefinition d, Polyline2 path, double width)
    {
        switch (d.StripSide)
        {
            case LadoFaixa.Esquerda: return width / 2;
            case LadoFaixa.Direita: return -width / 2;
            case LadoFaixa.Interno:
            case LadoFaixa.Externo:
            {
                // Área com sinal: positiva = anti-horário = o interior fica à esquerda do sentido.
                var ccw = SignedArea(path.Points) >= 0;
                var inside = d.StripSide == LadoFaixa.Interno;
                return (ccw == inside ? 1 : -1) * width / 2;
            }
            default: return 0;
        }
    }

    private static double SignedArea(IReadOnlyList<Vec2> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }

    /// <summary>
    /// Zebrado em faixa ao longo do caminho (canteiro pintado, faixa de segurança): dividido em trechos
    /// para que as barras mantenham o ângulo em relação ao eixo mesmo em curvas; contorno contínuo.
    /// </summary>
    public static MarkingGeometry BuildHatchStrip(HatchMarkingDefinition d, HachuraDef preset, Polyline2 path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var width = d.StripWidth!.Value;
        var border = d.BorderWidth ?? preset.LarguraBorda;
        var borderColor = d.BorderColor ?? preset.CorBorda;
        var off = d.StripOffset + StripShift(d, path, width);
        var axis = path.Offset(off);
        geo.PathLength = path.Length;

        if (border > 0 && width > 2 * border)
        {
            foreach (var side in new[] { 1.0, -1.0 })
            {
                var edge = path.Offset(off + side * (width / 2 - border / 2));
                foreach (var (c0, c1) in LinearPatternGenerator.Chunk(0, path.Length, ctx.MaxPieceLength))
                    geo.AddRange(PolygonOps.Strip(edge.SubPoints(path.ParamAt(c0), path.ParamAt(c1)), border), borderColor);
            }
            geo.PaintedLength += 2 * path.Length;
        }

        var inner = Math.Max(0, width - 2 * border);
        if (inner < 0.05) return geo;
        const double chunk = 12.0;
        double phase = d.Phase;
        foreach (var (c0, c1) in LinearPatternGenerator.Chunk(0, path.Length, chunk))
        {
            var pts = axis.SubPoints(path.ParamAt(c0), path.ParamAt(c1));
            if (pts.Count < 2) continue;
            var regions = PolygonOps.Strip(pts, inner);
            var dir = path.TangentAt((c0 + c1) / 2);
            foreach (var region in regions)
            {
                var g = HatchGenerator.Generate(region, preset, new HatchOptions
                {
                    BarWidth = d.BarWidth, Gap = d.Gap, AngleDeg = d.AngleDeg, BorderWidth = 0,
                    Chevron = d.Chevron, Crossed = d.Crossed, BarColor = d.BarColor,
                    ReferenceDirection = dir, AxisPoint = path.PointAt(0) , Phase = phase,
                });
                geo.Merge(g);
            }
        }
        return geo;
    }

    public static MarkingGeometry BuildRepeated(RepeatedMarkingDefinition d, Polyline2? path, BuildContext ctx)
    {
        if (path == null)
        {
            var g = new MarkingGeometry();
            g.Warnings.Add("Caminho das inscrições não encontrado.");
            return g;
        }
        var k = Sc(d.Scale);
        var symbol = string.IsNullOrWhiteSpace(d.SymbolCode) ? null : ctx.Catalog.Simbolo(d.SymbolCode);
        if (symbol != null)
        {
            return RepeatedGenerator.Generate(path, d.Offset, d.Spacing, d.StartOffset, d.EndSetback, d.Reverse,
                f => SymbolBuilder.Build(symbol, d.Length * k, f, d.Color), d.Length * k);
        }
        var text = string.IsNullOrWhiteSpace(d.Text) ? "ÔNIBUS" : d.Text!;
        var opt = new TextOptions
        {
            Height = d.TextHeight * k, WidthFactor = d.WidthFactor, LetterSpacing = d.LetterSpacing * k, LineSpacing = d.LineSpacing * k,
            FontFamily = d.FontFamily, Bold = d.Bold, BottomToTop = true,
        };
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var total = (lines * d.TextHeight + (lines - 1) * d.LineSpacing) * k;
        var geo = RepeatedGenerator.Generate(path, d.Offset, d.Spacing, d.StartOffset, d.EndSetback, d.Reverse,
            f => TextGenerator.Generate(text, opt, f, d.Color ?? MarkingColor.Branca, ctx.Glyphs), total);
        // Avisos de largura se repetem em cada inscrição: mantém apenas um.
        var distinct = geo.Warnings.Distinct().ToList();
        geo.Warnings.Clear();
        geo.Warnings.AddRange(distinct);
        return geo;
    }

    public static MarkingGeometry BuildDevice(DeviceMarkingDefinition d, Polyline2? path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var def = ctx.Catalog.Dispositivo(d.Code);
        if (def == null) { geo.Warnings.Add($"Dispositivo {d.Code} não existe no catálogo."); return geo; }
        if (path == null) { geo.Warnings.Add("Caminho dos dispositivos não encontrado."); return geo; }
        return DeviceGenerator.Generate(path, def, new DeviceOptions
        {
            Offset = d.Offset, Spacing = d.Spacing, StartSetback = d.StartSetback, EndSetback = d.EndSetback,
            Length = d.LengthOverride, Width = d.WidthOverride, Height = d.HeightOverride, Color = d.Color,
            Reverse = d.Reverse, MaxPieceLength = Math.Max(ctx.MaxPieceLength, ctx.DeviceMaxPieceLength),
        });
    }

    private static MarkingGeometry Missing(string msg)
    {
        var g = new MarkingGeometry();
        g.Warnings.Add(msg);
        return g;
    }

    public static MarkingGeometry BuildSign(SignDefinition d, BuildContext ctx)
    {
        var p = ctx.Catalog.Placa(d.Code);
        return p == null ? Missing($"Placa {d.Code} não existe no catálogo.") : Lift(SignGenerator.Generate(d, p, ctx.Glyphs), d.BaseElevation);
    }

    public static MarkingGeometry BuildUrban(UrbanElementDefinition d, Polyline2? path, BuildContext ctx)
    {
        var m = ctx.Catalog.Movel(d.Code);
        if (m == null) return Missing($"Elemento {d.Code} não existe no catálogo.");
        if (d.UsePath && path == null) return Missing("Caminho do elemento não encontrado.");
        return Lift(UrbanGenerator.Generate(d, m, path), d.BaseElevation);
    }

    /// <summary>Eleva todas as peças (elementos assentados sobre a calçada).</summary>
    public static MarkingGeometry Lift(MarkingGeometry geo, double dz)
    {
        if (Math.Abs(dz) < 1e-9) return geo;
        for (int i = 0; i < geo.Pieces.Count; i++) geo.Pieces[i] = geo.Pieces[i] with { Elevation = geo.Pieces[i].Elevation + dz };
        return geo;
    }

    public static MarkingGeometry BuildSymbol(SymbolMarkingDefinition d, BuildContext ctx)
    {
        var def = ctx.Catalog.Simbolo(d.Code);
        if (def == null)
        {
            var g = new MarkingGeometry();
            g.Warnings.Add($"Símbolo {d.Code} não existe no catálogo.");
            return g;
        }
        var frame = new LocalFrame(d.Position, d.Direction.Length < 1e-9 ? Vec2.UnitY : d.Direction, 1.0, d.Mirror);
        return SymbolBuilder.Build(def, d.Length * Sc(d.Scale), frame, d.ColorOverride, d.WidthFactor);
    }

    public static MarkingGeometry BuildText(TextMarkingDefinition d, BuildContext ctx)
    {
        var frame = new LocalFrame(d.Position, d.Direction.Length < 1e-9 ? Vec2.UnitY : d.Direction);
        return TextGenerator.Generate(d.Text, new TextOptions
        {
            Height = d.Height * Sc(d.Scale),
            WidthFactor = d.WidthFactor,
            LetterSpacing = d.LetterSpacing * Sc(d.Scale),
            LineSpacing = d.LineSpacing * Sc(d.Scale),
            FontFamily = d.FontFamily,
            Bold = d.Bold,
            BottomToTop = d.BottomToTop,
        }, frame, d.Color, ctx.Glyphs);
    }

    /// <summary>Escala válida (0,1× a 10×; 0 ou ausente = 1).</summary>
    private static double Sc(double s) => s is > 0.05 and < 20 ? s : 1.0;

    public static MarkingGeometry BuildParking(ParkingMarkingDefinition d, Polyline2? path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var preset = ctx.Catalog.Vaga(d.Code);
        if (preset == null) { geo.Warnings.Add($"Vaga {d.Code} não existe no catálogo."); return geo; }
        if (path == null) { geo.Warnings.Add("Meio-fio (caminho) não encontrado."); return geo; }
        return ParkingGenerator.Generate(path, preset, new ParkingOptions
        {
            Angle = d.Angle,
            StallWidth = d.StallWidth,
            StallLength = d.StallLength,
            LineWidth = d.LineWidth,
            Color = d.Color,
            Count = d.Count,
            RightSide = d.RightSide,
            StartOffset = d.StartOffset,
            CurbOffset = d.CurbOffset,
            FlipAngle = d.FlipAngle,
            BackLine = d.BackLine,
            FullOutline = d.FullOutline,
            IncludeSymbols = d.IncludeSymbols,
            FillColor = d.FillColor,
            SymbolSize = d.SymbolSize,
            LegendHeight = d.LegendHeight,
        }, ctx.Catalog, ctx.Glyphs);
    }

    /// <summary>Informações descritivas (nome, grupo, referência normativa, unidade de medição).</summary>
    public static MarkingInfo Describe(MarkingDefinition def, Catalogo cat)
    {
        switch (def)
        {
            case LinearMarkingDefinition l:
            {
                var t = cat.Linear(l.Code);
                return new MarkingInfo(l.Code, t?.Nome ?? l.Code, t?.Grupo ?? GrupoMarca.Longitudinal, t?.Referencia ?? "", t?.Unidades == true ? "un" : "m");
            }
            case HatchMarkingDefinition h:
            {
                var t = cat.Hachura(h.Code);
                return new MarkingInfo(h.Code, t?.Nome ?? h.Code, t?.Grupo ?? GrupoMarca.Canalizacao, t?.Referencia ?? "", "m²");
            }
            case SymbolMarkingDefinition s:
            {
                var t = cat.Simbolo(s.Code);
                return new MarkingInfo(s.Code, t?.Nome ?? s.Code, GrupoMarca.Inscricao, t?.Referencia ?? "", "un");
            }
            case TextMarkingDefinition t:
                return new MarkingInfo("LEG", $"Legenda \"{t.Text.Replace("\n", " ")}\"", GrupoMarca.Inscricao, "MBST Vol. IV – Inscrições no pavimento (legendas)", "un");
            case ParkingMarkingDefinition p:
            {
                var t = cat.Vaga(p.Code);
                return new MarkingInfo(p.Code, t?.Nome ?? p.Code, GrupoMarca.Estacionamento, t?.Referencia ?? "", "vaga");
            }
            case RepeatedMarkingDefinition r:
            {
                var t = string.IsNullOrWhiteSpace(r.SymbolCode) ? null : cat.Simbolo(r.SymbolCode);
                var name = t != null ? $"{t.Nome} (a cada {r.Spacing:0.#} m)" : $"Legenda \"{(r.Text ?? "").Replace("\n", " ")}\" (a cada {r.Spacing:0.#} m)";
                return new MarkingInfo(r.DisplayCode, name, GrupoMarca.Inscricao, t?.Referencia ?? "MBST Vol. IV – Inscrições no pavimento", "un");
            }
            case DeviceMarkingDefinition dv:
            {
                var t = cat.Dispositivo(dv.Code);
                var continuous = (dv.Spacing ?? t?.Espacamento ?? 1) <= 0;
                return new MarkingInfo(dv.Code, t?.Nome ?? dv.Code, GrupoMarca.Dispositivo, t?.Referencia ?? "", continuous ? "m" : "un");
            }
            case SignDefinition sg:
            {
                var t = cat.Placa(sg.Code);
                return new MarkingInfo(sg.Code, t?.Nome ?? sg.Code, GrupoMarca.SinalizacaoVertical, t?.Referencia ?? "", "un");
            }
            case UrbanElementDefinition ue:
            {
                var t = cat.Movel(ue.Code);
                return new MarkingInfo(ue.Code, t?.Nome ?? ue.Code, GrupoMarca.Mobiliario, t?.Referencia ?? "", "un");
            }
            case RampDefinition rp:
                return new MarkingInfo(rp.DisplayCode, rp.Type switch
                {
                    TipoRampa.AcessoVeiculos => "Rampa de acesso de veículos (guia rebaixada)",
                    TipoRampa.RebaixamentoSemAbas => "Rebaixamento de calçada sem abas",
                    TipoRampa.RebaixamentoTotal => "Rebaixamento total da calçada com rampas laterais",
                    _ => "Rebaixamento de calçada com abas laterais",
                }, GrupoMarca.Urbanizacao, "ABNT NBR 9050 / NBR 16537", "un");
            case TactileRouteDefinition tr:
                return new MarkingInfo(tr.DisplayCode, tr.AlertOnly ? "Piso tátil de alerta (placas com domos)"
                    : $"Rota tátil – direcional {tr.Rows * tr.Module:0.00} m com alertas (NBR 16537)", GrupoMarca.Acessibilidade, "ABNT NBR 16537 / NBR 9050", "m");
            case RoadPavementDefinition pv:
                return new MarkingInfo(pv.DisplayCode, pv.Material switch
                {
                    TipoPavimento.Bloquete => "Pavimento intertravado (bloquete)",
                    TipoPavimento.Concreto => "Pavimento de concreto",
                    _ => "Pavimento asfáltico (CBUQ)",
                } + $" – e = {pv.ActualThickness * 100:0} cm", GrupoMarca.Urbanizacao, "Projeto de pavimentação (espessura indicativa – conferir dimensionamento)", "m²");
            case RoundaboutDefinition rb:
                return new MarkingInfo("ROTATORIA", $"Rotatória – ilha central Ø {2 * rb.IslandRadius:0.0} m, {rb.Lanes} faixa(s), {rb.Legs.Count} ramo(s)",
                    GrupoMarca.Urbanizacao, "Projeto geométrico – interseções em rotatória (conferir manual do DNIT / órgão local)", "un");
            case IntersectionDefinition:
                return new MarkingInfo("INTERSECAO", "Interseção – esquinas, meio-fio e calçadas do cruzamento", GrupoMarca.Urbanizacao,
                    "Projeto geométrico viário (raios de esquina)", "un");
            case CurbExtensionDefinition:
                return new MarkingInfo("ORELHA", "Orelha (avanço) de calçada", GrupoMarca.Urbanizacao, "Projeto urbano – desenho de calçadas / NBR 9050", "un");
            case SidewalkAreaDefinition sa:
                return new MarkingInfo(sa.DisplayCode, sa.Type switch
                {
                    TipoAreaCalcada.Canteiro => "Canteiro gramado (área)",
                    TipoAreaCalcada.Ciclovia => "Ciclovia no nível da calçada (pintura vermelha)",
                    TipoAreaCalcada.Deck => "Parklet / área de estar em deck",
                    TipoAreaCalcada.Pavimento => "Pavimento (área)",
                    TipoAreaCalcada.FaixaServico => "Faixa de serviço ajardinada",
                    _ => "Avanço / área de calçada",
                }, GrupoMarca.Urbanizacao, "Projeto urbano – desenho de calçadas / NBR 9050", "m²");
            case PlanterDefinition pl:
                return new MarkingInfo(pl.DisplayCode, pl.Type switch
                {
                    TipoCanteiroCalcada.Jardineira => "Jardineira elevada na calçada",
                    TipoCanteiroCalcada.GrelhaArvore => "Grelha de proteção de árvore",
                    _ => "Canteiro gramado na calçada",
                }, GrupoMarca.Urbanizacao, "Projeto urbano – arborização / faixa de serviço (NBR 9050)", pl.Spacing <= 0 ? "m" : "un");
            case CulDeSacDefinition cd:
                return new MarkingInfo(cd.DisplayCode, cd.Type switch
                {
                    TipoCulDeSac.Martelo => "Cul-de-sac em \"T\" (martelo)",
                    TipoCulDeSac.EmY => "Cul-de-sac em \"Y\"",
                    TipoCulDeSac.EmLEsquerda or TipoCulDeSac.EmLDireita => "Cul-de-sac em \"L\"",
                    TipoCulDeSac.ExcentricoEsquerda or TipoCulDeSac.ExcentricoDireita => "Cul-de-sac circular excêntrico",
                    TipoCulDeSac.Gota => "Cul-de-sac em gota",
                    _ => "Cul-de-sac circular (balão de retorno)",
                }, GrupoMarca.Urbanizacao, "Projeto viário – balão de retorno (legislação municipal de parcelamento)", "un");
            case TrafficCalmingDefinition tc:
                return new MarkingInfo(tc.DisplayCode, tc.Type switch
                {
                    TipoModeracao.OndulacaoA => "Ondulação transversal tipo A (quebra-mola)",
                    TipoModeracao.OndulacaoB => "Ondulação transversal tipo B (quebra-mola)",
                    TipoModeracao.FaixaElevada => "Faixa elevada para travessia de pedestres",
                    _ => "Lombada invertida (valeta transversal)",
                }, GrupoMarca.Moderacao, "Resoluções CONTRAN sobre ondulações transversais e faixas elevadas (conferir versão vigente)", "un");
            case RailwayDefinition rw:
                return new MarkingInfo(rw.DisplayCode, rw.Type switch
                {
                    TipoViaFerrea.Embutida => $"Via férrea embutida no pavimento (VLT) – {RailwayGenerator.GaugeLabel(rw)}, trilho {RailwayGenerator.Profile(rw.Rail).Name}",
                    TipoViaFerrea.Laje => $"Via férrea em laje (fixação direta) – {RailwayGenerator.GaugeLabel(rw)}, trilho {RailwayGenerator.Profile(rw.Rail).Name}",
                    _ => $"Via férrea em lastro – {RailwayGenerator.GaugeLabel(rw)}, trilho {RailwayGenerator.Profile(rw.Rail).Name}, dormente de " + rw.Sleeper switch
                    {
                        TipoDormente.Madeira => "madeira",
                        TipoDormente.Aco => "aço",
                        TipoDormente.ConcretoBibloco => "concreto bibloco",
                        _ => "concreto monobloco",
                    },
                } + (rw.Tracks > 1 ? $" ({rw.Tracks} linhas)" : ""), GrupoMarca.Urbanizacao,
                    "ABNT NBR 7641 (via permanente), NBR 7590 (trilhos), NBR 11709 (dormentes), NBR 5564 (lastro)", "m");
            case IAnnotationDefinition:
                return new MarkingInfo(def.DisplayCode, def.KindName, GrupoMarca.Detalhamento, "", "");
            default:
                return new MarkingInfo(def.DisplayCode, def.KindName, GrupoMarca.Longitudinal, "", "");
        }
    }
}
