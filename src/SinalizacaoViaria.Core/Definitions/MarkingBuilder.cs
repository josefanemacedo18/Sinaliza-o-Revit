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

    /// <summary>Comprimento máximo das peças (m) – usado ao projetar sobre superfícies. 0 = sem divisão.</summary>
    public double MaxPieceLength { get; init; }
}

/// <summary>Dados descritivos de uma marca para parâmetros e tabelas.</summary>
public sealed record MarkingInfo(string Code, string Name, GrupoMarca Group, string Reference, string Unit);

/// <summary>Ponto único que transforma qualquer <see cref="MarkingDefinition"/> em geometria.</summary>
public static class MarkingBuilder
{
    public static MarkingGeometry Build(MarkingDefinition def, Polyline2? path, BuildContext ctx) => def switch
    {
        LinearMarkingDefinition l => BuildLinear(l, path, ctx),
        HatchMarkingDefinition h => BuildHatch(h, path, ctx),
        SymbolMarkingDefinition s => BuildSymbol(s, ctx),
        TextMarkingDefinition t => BuildText(t, ctx),
        ParkingMarkingDefinition p => BuildParking(p, path, ctx),
        RepeatedMarkingDefinition r => BuildRepeated(r, path, ctx),
        DeviceMarkingDefinition dv => BuildDevice(dv, path, ctx),
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

        return LinearPatternGenerator.Generate(path, type, variant, new LinearOptions
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

    public static MarkingGeometry BuildHatch(HatchMarkingDefinition d, Polyline2? path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var preset = ctx.Catalog.Hachura(d.Code);
        if (preset == null) { geo.Warnings.Add($"Código {d.Code} não existe no catálogo."); return geo; }
        if (d.IsStrip)
        {
            if (path == null) { geo.Warnings.Add("Caminho da faixa não encontrado."); return geo; }
            return BuildHatchStrip(d, preset, path, ctx);
        }
        if (path == null || path.Points.Count < 3) { geo.Warnings.Add("Contorno da área não encontrado ou aberto."); return geo; }

        var pts = path.Points.ToList();
        if (pts.Count > 3 && pts[0].AlmostEquals(pts[^1], 1e-4)) pts.RemoveAt(pts.Count - 1);
        // Resolve autointerseções/contornos invertidos.
        var regions = PolygonOps.FromContours(new[] { (IReadOnlyList<Vec2>)pts });
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
    /// Zebrado em faixa ao longo do caminho (canteiro pintado, faixa de segurança): dividido em trechos
    /// para que as barras mantenham o ângulo em relação ao eixo mesmo em curvas; contorno contínuo.
    /// </summary>
    public static MarkingGeometry BuildHatchStrip(HatchMarkingDefinition d, HachuraDef preset, Polyline2 path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var width = d.StripWidth!.Value;
        var border = d.BorderWidth ?? preset.LarguraBorda;
        var borderColor = d.BorderColor ?? preset.CorBorda;
        var axis = path.Offset(d.StripOffset);
        geo.PathLength = path.Length;

        if (border > 0 && width > 2 * border)
        {
            foreach (var side in new[] { 1.0, -1.0 })
            {
                var edge = path.Offset(d.StripOffset + side * (width / 2 - border / 2));
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
        var symbol = string.IsNullOrWhiteSpace(d.SymbolCode) ? null : ctx.Catalog.Simbolo(d.SymbolCode);
        if (symbol != null)
        {
            return RepeatedGenerator.Generate(path, d.Offset, d.Spacing, d.StartOffset, d.EndSetback, d.Reverse,
                f => SymbolBuilder.Build(symbol, d.Length, f, d.Color), d.Length);
        }
        var text = string.IsNullOrWhiteSpace(d.Text) ? "ÔNIBUS" : d.Text!;
        var opt = new TextOptions
        {
            Height = d.TextHeight, WidthFactor = d.WidthFactor, LetterSpacing = d.LetterSpacing, LineSpacing = d.LineSpacing,
            FontFamily = d.FontFamily, Bold = d.Bold, BottomToTop = true,
        };
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var total = lines * d.TextHeight + (lines - 1) * d.LineSpacing;
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
            Reverse = d.Reverse, MaxPieceLength = ctx.MaxPieceLength,
        });
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
        return SymbolBuilder.Build(def, d.Length, frame, d.ColorOverride, d.WidthFactor);
    }

    public static MarkingGeometry BuildText(TextMarkingDefinition d, BuildContext ctx)
    {
        var frame = new LocalFrame(d.Position, d.Direction.Length < 1e-9 ? Vec2.UnitY : d.Direction);
        return TextGenerator.Generate(d.Text, new TextOptions
        {
            Height = d.Height,
            WidthFactor = d.WidthFactor,
            LetterSpacing = d.LetterSpacing,
            LineSpacing = d.LineSpacing,
            FontFamily = d.FontFamily,
            Bold = d.Bold,
            BottomToTop = d.BottomToTop,
        }, frame, d.Color, ctx.Glyphs);
    }

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
            default:
                return new MarkingInfo(def.DisplayCode, def.KindName, GrupoMarca.Longitudinal, "", "");
        }
    }
}
