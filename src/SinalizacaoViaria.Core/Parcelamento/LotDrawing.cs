using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>Desenho do loteamento: lotes coloridos por categoria, detalhamento na vista e quadros.</summary>
public static class LotDrawing
{
    /// <summary>Camada das peças de lote (a chave do lote vem depois dos dois-pontos).</summary>
    public const string LayerPrefix = "LOTE:";

    public static bool IsParcelamento(MarkingDefinition d) =>
        d is LoteamentoDefinition or QuadraDefinition or LoteamentoDetalheDefinition or QuadroLoteamentoDefinition;

    /// <summary>Lotes da quadra com as categorias e os mínimos do loteamento.</summary>
    public static MarkingGeometry Quadra(QuadraDefinition q, LoteamentoDefinition? lot) =>
        Quadra(q, lot?.Categorias, lot?.FrenteMinimaLote ?? 5, lot?.AreaMinimaLote ?? 125);

    /// <summary>Lotes da quadra como peças com a cor e a hachura da categoria (pré-visualização e imagens).</summary>
    public static MarkingGeometry Quadra(QuadraDefinition q, IReadOnlyList<CategoriaLote>? categorias, double frenteMinimaLote = 5, double areaMinimaLote = 125)
    {
        var cats = categorias ?? CategoriaLote.Padrao();
        var geo = new MarkingGeometry();
        var d = LotDivider.Divide(q, cats, frenteMinimaLote, areaMinimaLote);
        geo.Warnings.AddRange(d.Avisos);
        foreach (var p in d.Parcelas)
        {
            var cat = cats.FirstOrDefault(c => string.Equals(c.Nome, p.Categoria, StringComparison.OrdinalIgnoreCase)) ?? cats[0];
            geo.Pieces.Add(new MarkingPiece(p.Forma, MarkingColor.Terra) { Rgb = cat.Cor, Hatch = cat.Hachura, Layer = LayerPrefix + p.Chave });
        }
        foreach (var p in d.Parcelas)
        {
            var c = InteriorPoint(p.Forma);
            geo.Annotations.Add(new AnnotationText(c, p.Numero, 2.0));
        }
        return geo;
    }

    /// <summary>Ponto bem dentro do polígono (para rótulos): o mais afastado do contorno numa grade.</summary>
    public static Vec2 InteriorPoint(Polygon2 p)
    {
        var c = p.Centroid;
        var (mn, mx) = p.Bounds;
        var best = c;
        var bestD = p.Contains(c) ? p.DistanceTo(c) : -1;
        const int n = 14;
        for (int pass = 0; pass < 2; pass++)
        {
            var w = (mx.X - mn.X) / n;
            var h = (mx.Y - mn.Y) / n;
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    var q = new Vec2(mn.X + i * w, mn.Y + j * h);
                    if (!p.Contains(q)) continue;
                    // Preferência leve pelo centro de massa (rótulos alinhados em lotes iguais).
                    var dq = p.DistanceTo(q) - 0.02 * q.DistanceTo(c);
                    if (dq > bestD) { bestD = dq; best = q; }
                }
            mn = best - new Vec2(w, h);
            mx = best + new Vec2(w, h);
        }
        return best;
    }

    private static double Readable(Vec2 dir)
    {
        var a = Math.Atan2(dir.Y, dir.X);
        if (a > Math.PI / 2 + 1e-9) a -= Math.PI;
        if (a < -Math.PI / 2 - 1e-9) a += Math.PI;
        return a;
    }

    /// <summary>Lados retos do anel (vértices quase alinhados juntados), com o comprimento real (desenvolvimento).</summary>
    public static List<(Vec2 A, Vec2 B, double Length)> Sides(IReadOnlyList<Vec2> ring, double turnDeg = 3)
    {
        var n = ring.Count;
        var res = new List<(Vec2, Vec2, double)>();
        if (n < 3) return res;
        // Começa num vértice de quina.
        int start = 0;
        double maxTurn = -1;
        for (int i = 0; i < n; i++)
        {
            var u = (ring[i] - ring[(i - 1 + n) % n]).Normalized();
            var v = (ring[(i + 1) % n] - ring[i]).Normalized();
            var t = Math.Abs(Math.Atan2(u.Cross(v), u.Dot(v)));
            if (t > maxTurn) { maxTurn = t; start = i; }
        }
        var a = ring[start];
        var len = 0.0;
        var prev = a;
        for (int k = 1; k <= n; k++)
        {
            var cur = ring[(start + k) % n];
            len += prev.DistanceTo(cur);
            var next = ring[(start + k + 1) % n];
            var u = (cur - prev).Normalized();
            var v = (next - cur).Normalized();
            var t = Math.Abs(Math.Atan2(u.Cross(v), u.Dot(v))) * 180 / Math.PI;
            if (t > turnDeg || k == n)
            {
                if (len > 1e-3) res.Add((a, cur, len));
                a = cur;
                len = 0;
            }
            prev = cur;
        }
        return res;
    }

    // ------------------------------------------------------------------ detalhamento

    /// <summary>
    /// Detalhamento dos lotes: número e área no meio do lote, medida de cada lado junto a ele, categoria (áreas públicas),
    /// letra da quadra num círculo no meio dela, nome das vias; opcionalmente cores das categorias e divisas como linhas.
    /// </summary>
    public static MarkingGeometry Detalhe(LoteamentoDetalheDefinition d, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var all = ctx.AllDefinitions?.Invoke() ?? Array.Empty<MarkingDefinition>();
        var lot = all.OfType<LoteamentoDefinition>().FirstOrDefault(l => l.Id == d.LoteamentoId)
                  ?? (ctx.Lookup?.Invoke(d.LoteamentoId) as LoteamentoDefinition);
        if (lot == null)
        {
            geo.Warnings.Add(DetailGenerator.MissingTarget + " (loteamento excluído).");
            return geo;
        }
        var quadras = all.OfType<QuadraDefinition>().Where(q => q.LoteamentoId == lot.Id).ToList();
        var th = d.TextMm;
        var mm = ctx.Mm(1);
        if (d.Divisas && lot.GlebaPolygon is { } gl)
            geo.Annotations.Add(new AnnotationLine(gl.Outer.Append(gl.Outer[0]).ToList(), MarkingColor.Preta));
        foreach (var q in quadras)
        {
            var firstText = geo.Annotations.Count;
            var div = LotDivider.Divide(q, lot);
            foreach (var p in div.Parcelas)
            {
                var cat = lot.Categoria(p.Categoria);
                if (d.Cores)
                    geo.Pieces.Add(new MarkingPiece(p.Forma, MarkingColor.Terra) { Rgb = cat.Cor, Hatch = cat.Hachura, Layer = LayerPrefix + p.Chave });
                if (d.Divisas)
                    geo.Annotations.Add(new AnnotationLine(p.Forma.Outer.Append(p.Forma.Outer[0]).ToList(), MarkingColor.Preta));
                var c = InteriorPoint(p.Forma);
                var lines = LabelLines(d, p, p.Forma.DistanceTo(c), mm);
                var total = lines.Sum(l => l.Mm * 1.35);
                var y = c.Y + total * mm / 2;
                foreach (var (text, h) in lines)
                {
                    geo.Annotations.Add(new AnnotationText(new Vec2(c.X, y), text, h));
                    y -= h * 1.35 * mm;
                }
                if (d.Cotas)
                {
                    var h = th * 0.85;
                    foreach (var (a, b, len) in Sides(p.Forma.Outer))
                    {
                        var chord = a.DistanceTo(b);
                        var text = LotReport.M(len);
                        if (chord < DetailGenerator.LineWidthFactor(text) * h * mm * 1.2) continue;
                        var dir = (b - a).Normalized();
                        var inward = dir.PerpLeft;
                        var rot = Readable(dir);
                        var up = Vec2.FromAngle(rot).PerpLeft;
                        var mid = (a + b) / 2;
                        var gap = 0.8 * mm;
                        var pos = up.Dot(inward) < 0 ? mid + inward * gap : mid + inward * (gap + h * mm);
                        geo.Annotations.Add(new AnnotationText(pos, text, h) { Rotation = rot });
                    }
                }
            }
            if (d.Quadra) QuadraLabel(geo, q, div, th, mm, firstText);
        }
        // Sobra da gleba em volta das vias: área verde do loteamento (sem letra de quadra nem cotas – o contorno é o da gleba e o
        // das vias).
        if (lot.Remanescente == ModoRemanescente.AreaVerde)
        {
            var rem = lot.RemanescentePolygons.ToList();
            var cat = LotReport.CategoriaRemanescente(lot);
            for (int i = 0; i < rem.Count; i++)
            {
                var p = new Parcela { Chave = "r:" + i, Forma = rem[i], Categoria = cat.Nome, Numero = LotReport.NumeroRemanescente(lot, i, rem.Count) };
                if (d.Cores)
                    geo.Pieces.Add(new MarkingPiece(p.Forma, MarkingColor.Terra) { Rgb = cat.Cor, Hatch = cat.Hachura, Layer = LayerPrefix + p.Chave });
                if (d.Divisas)
                    foreach (var ring in p.Forma.Holes.Prepend(p.Forma.Outer))
                        geo.Annotations.Add(new AnnotationLine(ring.Append(ring[0]).ToList(), MarkingColor.Preta));
                var c = InteriorPoint(p.Forma);
                var lines = LabelLines(d, p, p.Forma.DistanceTo(c), mm);
                var y = c.Y + lines.Sum(l => l.Mm * 1.35) * mm / 2;
                foreach (var (text, h) in lines)
                {
                    geo.Annotations.Add(new AnnotationText(new Vec2(c.X, y), text, h));
                    y -= h * 1.35 * mm;
                }
            }
        }
        if (d.Vias)
        {
            var gleba = lot.GlebaPolygon;
            foreach (var v in lot.Vias.Where(v => v.Eixo.Count >= 2 && !string.IsNullOrWhiteSpace(v.Nome)))
            {
                var line = new Polyline2(v.Eixo);
                var h = th * 1.3;
                var len = line.Length;
                var count = Math.Max(1, (int)(len / 150));
                for (int i = 0; i < count; i++)
                {
                    var s = len * (i + 0.5) / count;
                    var p = line.PointAt(s);
                    if (gleba != null && !gleba.Contains(p) && gleba.DistanceTo(p) > 5) continue;
                    var rot = Readable(line.TangentAt(s));
                    var up = Vec2.FromAngle(rot).PerpLeft;
                    geo.Annotations.Add(new AnnotationText(p + up * (h * mm / 2), v.Nome.ToUpperInvariant(), h) { Rotation = rot });
                }
            }
        }
        return geo;
    }

    /// <summary>Camada do disco branco sob a letra da quadra.</summary>
    public const string QuadraLayerPrefix = "QUADRA:";

    /// <summary>
    /// Letra da quadra dentro de um círculo, no meio da quadra (sobre a divisa de fundos), fora dos números, áreas e cotas dos lotes
    /// (textos da quadra a partir de <paramref name="firstText"/>). O miolo branco do círculo esconde as divisas sob ele.
    /// </summary>
    private static void QuadraLabel(MarkingGeometry geo, QuadraDefinition q, DivisaoQuadra div, double th, double mm, int firstText)
    {
        var quad = div.Quadra;
        var hq = th * 2.2;
        var r = 0.95 * hq * mm;
        var target = InteriorPoint(quad);
        // Quadra pequena: círculo menor (cabe com folga no meio dela).
        var room = quad.DistanceTo(target);
        if (r > room * 0.7)
        {
            r = room * 0.7;
            hq = r / (0.95 * mm);
        }
        var boxes = geo.Annotations.Skip(firstText).OfType<AnnotationText>().Select(t => TextBox(t, mm)).ToList();
        var gap = 1.5 * mm;
        bool Free(Vec2 c) => quad.Contains(c) && quad.DistanceTo(c) >= r * 1.25 && boxes.All(b => !b.Contains(c) && b.DistanceTo(c) >= r + gap);
        var at = target;
        if (!Free(at))
        {
            var (mn, mx) = quad.Bounds;
            const int n = 40;
            var best = double.MaxValue;
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    var c = new Vec2(mn.X + (mx.X - mn.X) * i / n, mn.Y + (mx.Y - mn.Y) * j / n);
                    var dist = c.DistanceTo(target);
                    if (dist < best && Free(c)) { best = dist; at = c; }
                }
        }
        // Círculo como regiões (aparece também no masterplan, que oculta as linhas de detalhe): anel preto e miolo branco.
        List<Vec2> Circle(double rad) => Enumerable.Range(0, 64).Select(i => at + Vec2.FromAngle(2 * Math.PI * i / 64) * rad).ToList();
        var inner = Circle(r - Math.Min(0.35 * mm, r * 0.1));
        geo.Pieces.Add(new MarkingPiece(new Polygon2(inner), MarkingColor.Branca) { Rgb = "#FFFFFF", Layer = QuadraLayerPrefix + q.Nome });
        geo.Pieces.Add(new MarkingPiece(new Polygon2(Circle(r), new[] { inner }), MarkingColor.Preta) { Rgb = "#000000", Layer = QuadraLayerPrefix + q.Nome });
        geo.Annotations.Add(new AnnotationText(new Vec2(at.X, at.Y + hq * mm / 2), q.Nome, hq));
    }

    /// <summary>Retângulo ocupado pelo texto (posição = topo do texto, girado em torno dela).</summary>
    public static Polygon2 TextBox(AnnotationText t, double mm)
    {
        var w = DetailGenerator.LineWidthFactor(t.Text) * t.PaperHeightMm * mm;
        var h = t.PaperHeightMm * mm;
        var (x0, x1) = t.Align switch { TextAlign.Left => (0.0, w), TextAlign.Right => (-w, 0.0), _ => (-w / 2, w / 2) };
        var u = Vec2.FromAngle(t.Rotation);
        var v = u.PerpLeft;
        Vec2 P(double x, double y) => t.Position + u * x + v * y;
        return new Polygon2(new[] { P(x0, -h), P(x1, -h), P(x1, 0), P(x0, 0) });
    }

    /// <summary>
    /// Linhas do rótulo do lote (número, categoria, área). Em lotes estreitos, só o número – reduzido até caber na largura
    /// livre (<paramref name="room"/>, m do ponto ao contorno).
    /// </summary>
    private static List<(string Text, double Mm)> LabelLines(LoteamentoDetalheDefinition d, Parcela p, double room, double mm)
    {
        var th = d.TextMm;
        var lines = new List<(string Text, double Mm)>();
        if (d.Numero) lines.Add((p.Numero, th * 1.4));
        if (d.Categoria && !string.Equals(p.Categoria, CategoriaLote.Lote, StringComparison.OrdinalIgnoreCase))
            lines.Add((p.Categoria.ToUpperInvariant(), th * 0.9));
        if (d.Area) lines.Add(($"{LotReport.M(p.Area)} m²", th * 0.9));
        bool Fits(IEnumerable<(string Text, double Mm)> ls) =>
            ls.All(l => DetailGenerator.LineWidthFactor(l.Text) * l.Mm * mm / 2 <= room * 1.15) && ls.Sum(l => l.Mm * 1.35) * mm / 2 <= room * 1.6;
        if (lines.Count <= 1 || Fits(lines)) return lines;
        // Sem a categoria (a cor/hachura já a mostra), com a área.
        var shorter = lines.Where(l => l.Text != p.Categoria.ToUpperInvariant()).ToList();
        if (shorter.Count < lines.Count && shorter.Count > 0 && Fits(shorter)) return shorter;
        if (!d.Numero) return new();
        var h = th * 1.4;
        while (h > th * 0.6 && !Fits(new[] { (p.Numero, h) })) h *= 0.85;
        return new() { (p.Numero, h) };
    }

    // ------------------------------------------------------------------ quadros

    /// <summary>Quadro do loteamento (áreas, quadras ou lotes), com o canto superior esquerdo em <see cref="QuadroLoteamentoDefinition.Position"/>.</summary>
    public static MarkingGeometry Quadro(QuadroLoteamentoDefinition d, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var all = ctx.AllDefinitions?.Invoke() ?? Array.Empty<MarkingDefinition>();
        var lot = all.OfType<LoteamentoDefinition>().FirstOrDefault(l => l.Id == d.LoteamentoId)
                  ?? (ctx.Lookup?.Invoke(d.LoteamentoId) as LoteamentoDefinition);
        if (lot == null)
        {
            geo.Warnings.Add(DetailGenerator.MissingTarget + " (loteamento excluído).");
            return geo;
        }
        var r = LotReport.Resumo(lot, all.OfType<QuadraDefinition>());
        var title = string.IsNullOrWhiteSpace(d.Titulo) ? d.TituloPadrao : d.Titulo!;
        switch (d.Conteudo)
        {
            case TipoQuadroLoteamento.Quadras:
            {
                var (headers, rows) = LotMemorial.LinhasQuadras(r);
                var aligns = headers.Select((_, i) => i == 0 ? TextAlign.Center : TextAlign.Right).ToArray();
                Table(geo, ctx, d.Position, title, headers, aligns, rows.Select((x, i) => (x.ToArray(), i == rows.Count - 1)).ToList(), d.RowMm, d.TextMm, null);
                break;
            }
            case TipoQuadroLoteamento.Lotes:
            {
                // Lote a lote: quadra, número, categoria, via da frente, medidas dos lados, área e observações.
                var headers = LotReport.CabecalhosLotes.ToList();
                var aligns = new List<TextAlign> { TextAlign.Center, TextAlign.Center, TextAlign.Left, TextAlign.Left, TextAlign.Right, TextAlign.Right,
                    TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Left };
                var cells = LotReport.LinhasLotes(r, string.IsNullOrWhiteSpace(d.Quadra) ? null : d.Quadra).Select(x => x.ToList()).ToList();
                if (cells.Count == 0) geo.Warnings.Add(string.IsNullOrWhiteSpace(d.Quadra) ? "Loteamento sem lotes." : $"Quadra {d.Quadra} não encontrada no loteamento.");
                // Sem observações: sem a coluna; com o filtro de uma quadra: sem a coluna da quadra (vai no título).
                if (cells.All(c => c[^1].Length == 0)) { headers.RemoveAt(headers.Count - 1); aligns.RemoveAt(aligns.Count - 1); cells.ForEach(c => c.RemoveAt(c.Count - 1)); }
                if (!string.IsNullOrWhiteSpace(d.Quadra)) { headers.RemoveAt(0); aligns.RemoveAt(0); cells.ForEach(c => c.RemoveAt(0)); }
                var rows = cells.Select(c => (c.ToArray(), false)).ToList();
                var per = Math.Max(5, d.LinhasPorColuna);
                var x = d.Position.X;
                var blocks = (rows.Count + per - 1) / per;
                for (int b = 0; b < Math.Max(1, blocks); b++)
                {
                    var chunk = rows.Skip(b * per).Take(per).ToList();
                    var size = Table(geo, ctx, new Vec2(x, d.Position.Y), b == 0 ? title : title + " (continuação)", headers, aligns, chunk, d.RowMm, d.TextMm, null);
                    x += size.X + ctx.Mm(4);
                }
                break;
            }
            default:
            {
                var lines = LotMemorial.LinhasAreas(r);
                var headers = new List<string> { "", "ESPECIFICAÇÃO", "QUANT.", "ÁREA (m²)", "%", "MÍNIMO" };
                var aligns = new[] { TextAlign.Center, TextAlign.Left, TextAlign.Center, TextAlign.Right, TextAlign.Right, TextAlign.Right };
                var rows = lines.Select((l, i) => (new[] { "", l.Item, l.Qtd, l.Area, l.Pct, l.Min }, l.Item.StartsWith("Área total"))).ToList();
                var hasMin = lines.Any(l => l.Min.Length > 0);
                if (!hasMin)
                {
                    headers.RemoveAt(5);
                    aligns = aligns.Take(5).ToArray();
                    rows = rows.Select(x => (x.Item1.Take(5).ToArray(), x.Item2)).ToList();
                }
                var swatches = new List<(int Row, CategoriaLote Cat)>();
                for (int i = 0; i < r.Categorias.Count; i++) swatches.Add((i, lot.Categoria(r.Categorias[i].Nome)));
                Table(geo, ctx, d.Position, title, headers, aligns, rows, d.RowMm, d.TextMm, swatches);
                var bottom = geo.Annotations.OfType<AnnotationLine>().SelectMany(l => l.Points).Min(p => p.Y);
                geo.Annotations.Add(new AnnotationText(new Vec2(d.Position.X, bottom - ctx.Mm(2)),
                    "Mínimos: informados no projeto (legislação municipal e Lei Federal nº 6.766/1979 – a confirmar).", d.TextMm * 0.8, TextAlign.Left));
                break;
            }
        }
        return geo;
    }

    /// <summary>
    /// Tabela com título, cabeçalho e linhas (a linha marcada como total ganha um traço duplo acima); amostras de cor das
    /// categorias na primeira coluna. Devolve a largura e a altura (m).
    /// </summary>
    public static Vec2 Table(MarkingGeometry geo, BuildContext ctx, Vec2 topLeft, string title, IReadOnlyList<string> headers, IReadOnlyList<TextAlign> aligns,
        IReadOnlyList<(string[] Cells, bool Total)> rows, double rowMm, double textMm, IReadOnlyList<(int Row, CategoriaLote Cat)>? swatches)
    {
        var rowH = ctx.Mm(rowMm);
        var pad = ctx.Mm(1.5);
        var th = textMm;
        var widths = new double[headers.Count];
        for (int c = 0; c < headers.Count; c++)
            widths[c] = rows.Where(r => r.Cells.Length > c).Select(r => r.Cells[c]).Append(headers[c])
                .Max(s => DetailGenerator.LineWidthFactor(s)) * ctx.Mm(th) + 2 * pad;
        if (swatches != null && widths.Length > 0) widths[0] = Math.Max(widths[0], ctx.Mm(10));
        var totalW = widths.Sum();
        var titleH = rowH * 1.3;
        var totalH = titleH + rowH * (rows.Count + 1);
        var x0 = topLeft.X;
        var y0 = topLeft.Y;
        void Line(double ax, double ay, double bx, double by) => geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(ax, ay), new Vec2(bx, by) }, MarkingColor.Preta));
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(x0, y0), new Vec2(x0 + totalW, y0), new Vec2(x0 + totalW, y0 - totalH), new Vec2(x0, y0 - totalH), new Vec2(x0, y0) }, MarkingColor.Preta));
        Line(x0, y0 - titleH, x0 + totalW, y0 - titleH);
        geo.Annotations.Add(new AnnotationText(new Vec2(x0 + totalW / 2, y0 - (titleH - ctx.Mm(th * 1.2)) / 2), title, th * 1.2));
        var y = y0 - titleH;
        void Cells(string[] cells)
        {
            var x = x0;
            for (int c = 0; c < widths.Length && c < cells.Length; c++)
            {
                var tx = aligns[c] switch { TextAlign.Left => x + pad, TextAlign.Right => x + widths[c] - pad, _ => x + widths[c] / 2 };
                if (cells[c].Length > 0) geo.Annotations.Add(new AnnotationText(new Vec2(tx, y - (rowH - ctx.Mm(th)) / 2), cells[c], th, aligns[c]));
                x += widths[c];
            }
            y -= rowH;
            Line(x0, y, x0 + totalW, y);
        }
        Cells(headers.ToArray());
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Total) Line(x0, y - ctx.Mm(0.6), x0 + totalW, y - ctx.Mm(0.6));
            var top = y;
            Cells(rows[i].Cells);
            if (swatches?.FirstOrDefault(s => s.Row == i) is { Cat: not null } sw)
            {
                var m = ctx.Mm(1.2);
                var box = Polygon2.Rectangle(new Vec2(x0 + m, top - rowH + m), new Vec2(x0 + widths[0] - m, top - m));
                geo.Pieces.Add(new MarkingPiece(box, MarkingColor.Terra) { Rgb = sw.Cat.Cor, Hatch = sw.Cat.Hachura });
                geo.Annotations.Add(new AnnotationLine(box.Outer.Append(box.Outer[0]).ToList(), MarkingColor.Preta));
            }
        }
        var xs = x0;
        for (int c = 0; c + 1 < widths.Length; c++)
        {
            xs += widths[c];
            Line(xs, y0 - titleH, xs, y0 - totalH);
        }
        return new Vec2(totalW, totalH);
    }
}
