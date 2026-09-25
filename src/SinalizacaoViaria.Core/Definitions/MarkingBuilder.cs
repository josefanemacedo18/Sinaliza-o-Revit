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
        var geo = BuildRaw(def, path, ctx);
        return def.Exclusions.Count == 0 ? geo : ApplyExclusions(geo, def.Exclusions);
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
        if (d.Code is "PTA" or "PTD")
        {
            // Piso tátil: placas moduladas com relevo (NBR 16537), assentadas no topo da calçada.
            var w = d.WidthOverride ?? variant.Faixas.Max(f => f.Largura);
            var trimmed = RoadGenerator.Trimmed(path, d.StartSetback, d.EndSetback);
            return TactileGenerator.Linear(trimmed, w, d.Code == "PTA", d.ColorOverride ?? type.Cor, d.Offset);
        }

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
            case IAnnotationDefinition:
                return new MarkingInfo(def.DisplayCode, def.KindName, GrupoMarca.Detalhamento, "", "");
            default:
                return new MarkingInfo(def.DisplayCode, def.KindName, GrupoMarca.Longitudinal, "", "");
        }
    }
}
