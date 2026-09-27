using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SinalizacaoViaria.Core.Quantities;

/// <summary>
/// Planilha Excel (.xlsx) do quantitativo com o mesmo desenho da janela: título, um bloco por categoria e subcategoria,
/// miniatura de cada item, colunas fixas alinhadas, subtotais, resumos, larguras de coluna, bordas e formatos numéricos.
/// Escrita direta do pacote OpenXML (sem dependências externas).
/// </summary>
public static class QuantityWorkbook
{
    private static readonly string[] Headers =
    {
        "Item", "Imagem", "Código", "Descrição", "Quantidade", "Un.", "Área (m²)", "Extensão (m)", "Unidades", "Elementos", "Material", "Consumo est.", "Hierarquia viária", "Referência",
    };
    private static readonly double[] Widths = { 6, 14, 12, 46, 12, 6, 11, 12, 10, 10, 24, 13, 18, 48 };

    // estilos (índices de cellXfs)
    private const int StTitle = 1, StHeader = 2, StCategory = 3, StSub = 4, StText = 5, StNum = 6, StInt = 7, StSubTotText = 8, StSubTotNum = 9, StMeta = 10, StTextCenter = 11;

    public static byte[] Build(IEnumerable<QuantityRow> rows, IEnumerable<QuantityRow>? summary, string? projectName, Func<QuantityRow, byte[]?>? thumbnail = null)
    {
        var list = rows.ToList();
        var sheet = new StringBuilder();
        var merges = new List<string>();
        var images = new List<(int Row, byte[] Png)>();
        var r = 1;   // linha em construção (as referências das células usam este valor; Row() a fecha e avança)
        var ci = CultureInfo.InvariantCulture;

        string Col(int c) { var s = ""; c++; while (c > 0) { var m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; } return s; }
        string X(string v) => System.Security.SecurityElement.Escape(v) ?? "";
        var cells = new StringBuilder();
        void Text(int c, string v, int st) { if (v.Length > 0) cells.Append($"<c r=\"{Col(c)}{r}\" s=\"{st}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{X(v)}</t></is></c>"); else cells.Append($"<c r=\"{Col(c)}{r}\" s=\"{st}\"/>"); }
        void Num(int c, double v, int st) => cells.Append($"<c r=\"{Col(c)}{r}\" s=\"{st}\"><v>{v.ToString("0.####", ci)}</v></c>");
        void Blank(int c, int st) => cells.Append($"<c r=\"{Col(c)}{r}\" s=\"{st}\"/>");
        void Row(double? height = null)
        {
            sheet.Append(height is { } h ? $"<row r=\"{r}\" ht=\"{h.ToString("0.#", ci)}\" customHeight=\"1\">" : $"<row r=\"{r}\">");
            sheet.Append(cells);
            sheet.Append("</row>");
            cells.Clear();
            r++;
        }
        void Merge(int c0, int c1) => merges.Add($"{Col(c0)}{r}:{Col(c1)}{r}");
        void Line(string text, int st, double? height = null, bool merge = true)
        {
            Text(0, text, st);
            for (int c = 1; c < Headers.Length; c++) Blank(c, st == StHeader ? StHeader : 0);
            if (merge) Merge(0, Headers.Length - 1);
            Row(height);
        }

        Line("QUANTITATIVO DE SINALIZAÇÃO VIÁRIA E URBANIZAÇÃO", StTitle, 24);
        if (!string.IsNullOrWhiteSpace(projectName)) Line("Projeto: " + projectName, StMeta);
        Line($"Emitido em {DateTime.Now:dd/MM/yyyy HH:mm} – SinalizaBIM", StMeta);
        Line($"{list.Count} item(ns) em {list.Select(x => x.Category).Distinct().Count()} categoria(s)", StMeta);
        Row();

        var n = 0;
        foreach (var g in list.GroupBy(x => x.Category).OrderBy(x => x.Key))
        {
            var memorial = QuantityRow.IsMemorialCategory(g.Key);
            Line(QuantityRow.CategoryLabel(g.Key).ToUpperInvariant(), StCategory, 20);
            for (int c = 0; c < Headers.Length; c++) Text(c, Headers[c], StHeader);
            Row(30);
            foreach (var sg in g.GroupBy(x => x.SubcategoryName).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Line(sg.Key, StSub, null, true);
                foreach (var q in sg)
                {
                    n++;
                    var png = thumbnail?.Invoke(q);
                    Num(0, n, StInt);
                    Blank(1, StText);
                    Text(2, q.Code, StText);
                    Text(3, q.Name, StText);
                    Num(4, q.MainQuantity, StNum);
                    Text(5, q.Unit, StTextCenter);
                    if (memorial) { Blank(6, StText); Blank(7, StText); } else { Num(6, q.Area, StNum); Num(7, q.PaintedLength, StNum); }
                    Num(8, q.Units, StInt);
                    Num(9, q.Elements, StInt);
                    Text(10, memorial ? "" : q.Material, StText);
                    Text(11, memorial || q.MaterialConsumption <= 1e-6 ? "" : q.ConsumptionText, StText);
                    Text(12, q.HierarchyName, StText);
                    Text(13, q.Reference, StText);
                    if (png != null) images.Add((r, png));
                    Row(png != null ? 42 : null);
                }
            }
            var sub = QuantityCalculator.CategorySummary(g).First();
            Blank(0, StSubTotText); Blank(1, StSubTotText); Text(2, "SUBTOTAL", StSubTotText); Blank(3, StSubTotText); Blank(4, StSubTotText); Blank(5, StSubTotText);
            if (memorial) { Blank(6, StSubTotText); Blank(7, StSubTotText); } else { Num(6, sub.Area, StSubTotNum); Num(7, sub.PaintedLength, StSubTotNum); }
            Num(8, sub.Units, StSubTotNum); Num(9, sub.Elements, StSubTotNum);
            Blank(10, StSubTotText);
            Text(11, memorial || sub.MaterialConsumption <= 1e-6 ? "" : (sub.MaterialConsumption.ToString("N2", CultureInfo.GetCultureInfo("pt-BR")) + " " + sub.ConsumptionUnit).Trim(), StSubTotText);
            Blank(12, StSubTotText); Blank(13, StSubTotText);
            Row();
            Row();
        }

        // Resumos
        Line("RESUMO POR CATEGORIA", StCategory, 20);
        foreach (var (h, i) in new[] { "Categoria", "Itens", "Área (m²)", "Extensão (m)", "Unidades", "Consumo de tinta" }.Select((h, i) => (h, i))) Text(i, h, StHeader);
        Row();
        foreach (var c in QuantityCalculator.CategorySummary(list))
        {
            Text(0, c.Name, StText); Num(1, c.Elements, StInt); Num(2, c.Area, StNum); Num(3, c.PaintedLength, StNum); Num(4, c.Units, StInt); Num(5, c.MaterialConsumption, StNum);
            Row();
        }
        Row();
        Line("RESUMO POR HIERARQUIA VIÁRIA (CTB art. 60)", StCategory, 20);
        foreach (var (h, i) in new[] { "Hierarquia", "Extensão de vias (m)", "Pavimento (m²)", "Área pintada (m²)", "Extensão pintada (m)", "Placas (un)", "Elementos", "Consumo de tinta" }.Select((h, i) => (h, i))) Text(i, h, StHeader);
        Row();
        foreach (var h in QuantityCalculator.HierarchySummary(list))
        {
            Text(0, h.Name, StText); Num(1, h.RoadLength, StNum); Num(2, h.PavementArea, StNum); Num(3, h.PaintedArea, StNum); Num(4, h.PaintedLength, StNum); Num(5, h.Signs, StInt); Num(6, h.Elements, StInt); Num(7, h.PaintConsumption, StNum);
            Row();
        }
        if (summary != null && summary.Any())
        {
            Row();
            Line("PINTURA POR COR E MATERIAL", StCategory, 20);
            foreach (var (h, i) in new[] { "Cor", "Material", "Área (m²)", "Consumo estimado", "Un.", "Microesferas (kg)" }.Select((h, i) => (h, i))) Text(i, h, StHeader);
            Row();
            foreach (var q in summary)
            {
                Text(0, q.ColorLabel, StText); Text(1, q.Material, StText); Num(2, q.Area, StNum); Num(3, q.MaterialConsumption, StNum); Text(4, q.ConsumptionUnit, StTextCenter); Num(5, q.GlassBeadsKg, StNum);
                Row();
            }
        }

        var cols = new StringBuilder("<cols>");
        for (int c = 0; c < Widths.Length; c++) cols.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{Widths[c].ToString("0.#", ci)}\" customWidth=\"1\"/>");
        cols.Append("</cols>");
        var ws = new StringBuilder();
        ws.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        ws.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        ws.Append("<sheetViews><sheetView workbookViewId=\"0\" showGridLines=\"0\"/></sheetViews><sheetFormatPr defaultRowHeight=\"15\"/>");
        ws.Append(cols).Append("<sheetData>").Append(sheet).Append("</sheetData>");
        if (merges.Count > 0)
        {
            ws.Append($"<mergeCells count=\"{merges.Count}\">");
            foreach (var m in merges) ws.Append($"<mergeCell ref=\"{m}\"/>");
            ws.Append("</mergeCells>");
        }
        ws.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/><pageSetup orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
        if (images.Count > 0) ws.Append("<drawing r:id=\"rId1\"/>");
        ws.Append("</worksheet>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string path, string content)
            {
                var e = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            var ct = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            if (images.Count > 0) ct.Append("<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
            ct.Append("</Types>");
            Put("[Content_Types].xml", ct.ToString());
            Put("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Put("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Quantitativo\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Put("xl/styles.xml", Styles);
            Put("xl/worksheets/sheet1.xml", ws.ToString());
            if (images.Count > 0)
            {
                Put("xl/worksheets/_rels/sheet1.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing1.xml\"/></Relationships>");
                var dr = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
                var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                const long emu = 9525;
                for (int i = 0; i < images.Count; i++)
                {
                    var (row, png) = images[i];
                    var id = i + 1;
                    var (pw, ph) = PngSize(png);
                    const double boxW = 90, boxH = 50;
                    var k = pw > 0 && ph > 0 ? Math.Min(boxW / pw, boxH / ph) : 1;
                    var iw = pw > 0 ? pw * k : boxW;
                    var ih = ph > 0 ? ph * k : boxH;
                    var ox = (long)((100 - iw) / 2 * emu);
                    var oy = (long)((56 - ih) / 2 * emu);
                    long cx = (long)(iw * emu), cy = (long)(ih * emu);
                    dr.Append($"<xdr:oneCellAnchor><xdr:from><xdr:col>1</xdr:col><xdr:colOff>{Math.Max(0, ox)}</xdr:colOff><xdr:row>{row - 1}</xdr:row><xdr:rowOff>{Math.Max(0, oy)}</xdr:rowOff></xdr:from>" +
                              $"<xdr:ext cx=\"{cx}\" cy=\"{cy}\"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"{id + 1}\" name=\"Imagem {id}\"/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr>" +
                              $"<xdr:blipFill><a:blip r:embed=\"rId{id}\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor>");
                    rels.Append($"<Relationship Id=\"rId{id}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image{id}.png\"/>");
                    var e = zip.CreateEntry($"xl/media/image{id}.png", CompressionLevel.NoCompression);
                    using var st = e.Open();
                    st.Write(png, 0, png.Length);
                }
                dr.Append("</xdr:wsDr>");
                rels.Append("</Relationships>");
                Put("xl/drawings/drawing1.xml", dr.ToString());
                Put("xl/drawings/_rels/drawing1.xml.rels", rels.ToString());
            }
        }
        return ms.ToArray();
    }

    /// <summary>Largura e altura (px) lidas do cabeçalho IHDR do PNG.</summary>
    private static (int W, int H) PngSize(byte[] png)
    {
        if (png.Length < 24 || png[0] != 0x89 || png[1] != 0x50) return (0, 0);
        int Be(int o) => (png[o] << 24) | (png[o + 1] << 16) | (png[o + 2] << 8) | png[o + 3];
        return (Be(16), Be(20));
    }

    private const string Styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.00\"/></numFmts>" +
        "<fonts count=\"5\"><font><sz val=\"10\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"10\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"15\"/><color rgb=\"FF1F5FA8\"/><name val=\"Calibri\"/></font>" +
        "<font><i/><sz val=\"10\"/><color rgb=\"FF5F6B7A\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"5\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F5FA8\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE8EFF8\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF4F6F9\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left style=\"thin\"><color rgb=\"FFD5DBE3\"/></left><right style=\"thin\"><color rgb=\"FFD5DBE3\"/></right><top style=\"thin\"><color rgb=\"FFD5DBE3\"/></top><bottom style=\"thin\"><color rgb=\"FFD5DBE3\"/></bottom><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"12\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"><alignment vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"><alignment vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"><alignment vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"164\" fontId=\"1\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
        "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
}
