using System.IO.Compression;
using System.Security;
using System.Text;

namespace SinalizacaoViaria.Core.Reports;

/// <summary>
/// Documento Word (.docx) escrito direto no pacote OpenXML (ZipArchive + WordprocessingML), sem dependências: estilos de
/// título, tabelas com cabeçalho repetido e bordas, e campos editáveis (controles de conteúdo) para preencher no Word.
/// </summary>
public static class DocxWriter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    // A4 retrato, margens 2,5 / 2,0 cm: largura útil em twips.
    private const int PageW = 11906, PageH = 16838, MarginL = 1418, MarginR = 1134, MarginT = 1418, MarginB = 1134;
    private const int ContentW = PageW - MarginL - MarginR;
    private const string Accent = "1F5FA8";

    private static string X(string s) => SecurityElement.Escape(s) ?? "";

    /// <summary>Partes obrigatórias do pacote (para conferência).</summary>
    public static readonly string[] Parts =
    {
        "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml", "word/settings.xml", "word/_rels/document.xml.rels",
        "docProps/core.xml", "docProps/app.xml",
    };

    public static byte[] Write(MemorialDocument doc)
    {
        var body = new StringBuilder();
        var sdtId = 1000;
        Para(body, doc.Title, "Title");
        foreach (var blk in doc.Blocks)
        {
            switch (blk)
            {
                case MemHeading h:
                    Para(body, h.Text, h.Level <= 1 ? "Heading1" : "Heading2");
                    break;
                case MemParagraph p:
                    Para(body, p.Text, null, italic: p.Italic);
                    break;
                case MemBullets bl:
                    foreach (var it in bl.Items)
                        body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Lista\"/></w:pPr>{Run("•\t" + it)}</w:p>");
                    break;
                case MemField f:
                    // Rótulo em negrito + controle de conteúdo de texto em branco (texto de instrução em cinza).
                    body.Append("<w:p><w:pPr><w:spacing w:before=\"60\" w:after=\"60\"/></w:pPr>");
                    body.Append(Run(f.Label + ": ", bold: true));
                    body.Append($"<w:sdt><w:sdtPr><w:alias w:val=\"{X(f.Label)}\"/><w:tag w:val=\"{X(f.Tag)}\"/><w:id w:val=\"{sdtId++}\"/>" +
                                "<w:showingPlcHdr/><w:text/></w:sdtPr><w:sdtContent>" +
                                "<w:r><w:rPr><w:color w:val=\"808080\"/><w:u w:val=\"dotted\"/></w:rPr><w:t xml:space=\"preserve\">clique para preencher</w:t></w:r>" +
                                "</w:sdtContent></w:sdt>");
                    body.Append("</w:p>");
                    break;
                case MemTable t:
                    Table(body, t);
                    break;
            }
        }
        body.Append($"<w:sectPr><w:pgSz w:w=\"{PageW}\" w:h=\"{PageH}\"/><w:pgMar w:top=\"{MarginT}\" w:right=\"{MarginR}\" w:bottom=\"{MarginB}\" w:left=\"{MarginL}\" " +
                    "w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/></w:sectPr>");
        var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"{W}\" " +
                       "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>" + body + "</w:body></w:document>";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string path, string content)
            {
                var e = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            Put("[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
                "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/></Types>");
            Put("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>");
            Put("word/_rels/document.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/></Relationships>");
            Put("word/document.xml", document);
            Put("word/styles.xml", Styles);
            Put("word/settings.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:settings xmlns:w=\"{W}\"><w:defaultTabStop w:val=\"284\"/>" +
                                     "<w:characterSpacingControl w:val=\"doNotCompress\"/><w:themeFontLang w:val=\"pt-BR\"/></w:settings>");
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            Put("docProps/core.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                $"<dc:title>{X(doc.Title)}</dc:title><dc:creator>SinalizaBIM</dc:creator><dc:language>pt-BR</dc:language>" +
                $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:modified></cp:coreProperties>");
            Put("docProps/app.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\">" +
                "<Application>SinalizaBIM</Application></Properties>");
        }
        return ms.ToArray();
    }

    private static string Run(string text, bool bold = false, bool italic = false, bool white = false)
    {
        var rpr = (bold ? "<w:b/>" : "") + (italic ? "<w:i/>" : "") + (white ? "<w:color w:val=\"FFFFFF\"/>" : "");
        var sb = new StringBuilder("<w:r>");
        if (rpr.Length > 0) sb.Append("<w:rPr>").Append(rpr).Append("</w:rPr>");
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append("<w:br/>");
            var parts = lines[i].Split('\t');
            for (int k = 0; k < parts.Length; k++)
            {
                if (k > 0) sb.Append("<w:tab/>");
                if (parts[k].Length > 0) sb.Append($"<w:t xml:space=\"preserve\">{X(parts[k])}</w:t>");
            }
        }
        return sb.Append("</w:r>").ToString();
    }

    private static void Para(StringBuilder sb, string text, string? style, bool italic = false, string? jc = null)
    {
        sb.Append("<w:p>");
        if (style != null || jc != null)
        {
            sb.Append("<w:pPr>");
            if (style != null) sb.Append($"<w:pStyle w:val=\"{style}\"/>");
            if (jc != null) sb.Append($"<w:jc w:val=\"{jc}\"/>");
            sb.Append("</w:pPr>");
        }
        sb.Append(Run(text, italic: italic)).Append("</w:p>");
    }

    private static void Table(StringBuilder sb, MemTable t)
    {
        var total = t.Widths.Sum();
        var widths = t.Widths.Select(w => (int)Math.Round(ContentW * w / total)).ToList();
        var numeric = new HashSet<int>(t.Numeric ?? Array.Empty<int>());
        const string border = "w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"9AA4B0\"";
        // Ordem do esquema: tblW, tblBorders, tblLayout, tblCellMar.
        sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>")
          .Append($"<w:tblBorders><w:top {border}/><w:left {border}/><w:bottom {border}/><w:right {border}/><w:insideH {border}/><w:insideV {border}/></w:tblBorders>")
          .Append("<w:tblLayout w:type=\"fixed\"/><w:tblCellMar><w:left w:w=\"70\" w:type=\"dxa\"/><w:right w:w=\"70\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");
        foreach (var w in widths) sb.Append($"<w:gridCol w:w=\"{w}\"/>");
        sb.Append("</w:tblGrid>");
        void Row(IReadOnlyList<string> cells, bool header, bool totalRow)
        {
            sb.Append("<w:tr>");
            if (header) sb.Append("<w:trPr><w:tblHeader/><w:cantSplit/></w:trPr>");
            else sb.Append("<w:trPr><w:cantSplit/></w:trPr>");
            for (int c = 0; c < widths.Count; c++)
            {
                var text = c < cells.Count ? cells[c] : "";
                sb.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{widths[c]}\" w:type=\"dxa\"/>");
                if (header) sb.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{Accent}\"/>");
                else if (totalRow) sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"E8EDF3\"/>");
                sb.Append("</w:tcPr><w:p><w:pPr><w:pStyle w:val=\"Tabela\"/>");
                if (!header && numeric.Contains(c)) sb.Append("<w:jc w:val=\"right\"/>");
                else if (header) sb.Append("<w:jc w:val=\"center\"/>");
                sb.Append("</w:pPr>").Append(Run(text, bold: header || totalRow, white: header)).Append("</w:p></w:tc>");
            }
            sb.Append("</w:tr>");
        }
        Row(t.Headers, true, false);
        for (int i = 0; i < t.Rows.Count; i++) Row(t.Rows[i], false, t.TotalRow && i == t.Rows.Count - 1);
        sb.Append("</w:tbl>");
        // Espaço depois da tabela (e parágrafo obrigatório antes de outra tabela ou do fim do corpo).
        sb.Append("<w:p><w:pPr><w:spacing w:after=\"120\"/></w:pPr></w:p>");
    }

    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"" + W + "\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:eastAsia=\"Arial\" w:cs=\"Arial\"/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/>" +
        "<w:lang w:val=\"pt-BR\" w:eastAsia=\"pt-BR\" w:bidi=\"ar-SA\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"100\" w:line=\"276\" w:lineRule=\"auto\"/><w:jc w:val=\"both\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
        "<w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"12\" w:space=\"4\" w:color=\"" + Accent + "\"/></w:pBdr><w:spacing w:after=\"300\"/><w:jc w:val=\"center\"/></w:pPr>" +
        "<w:rPr><w:b/><w:color w:val=\"" + Accent + "\"/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
        "<w:pPr><w:keepNext/><w:spacing w:before=\"280\" w:after=\"120\"/><w:jc w:val=\"left\"/><w:outlineLvl w:val=\"0\"/></w:pPr>" +
        "<w:rPr><w:b/><w:color w:val=\"" + Accent + "\"/><w:sz w:val=\"26\"/><w:szCs w:val=\"26\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
        "<w:pPr><w:keepNext/><w:spacing w:before=\"200\" w:after=\"80\"/><w:jc w:val=\"left\"/><w:outlineLvl w:val=\"1\"/></w:pPr>" +
        "<w:rPr><w:b/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Lista\"><w:name w:val=\"Lista SV\"/><w:basedOn w:val=\"Normal\"/>" +
        "<w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"284\"/></w:tabs><w:spacing w:after=\"60\"/><w:ind w:left=\"284\" w:hanging=\"284\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Tabela\"><w:name w:val=\"Tabela SV\"/><w:basedOn w:val=\"Normal\"/>" +
        "<w:pPr><w:spacing w:before=\"20\" w:after=\"20\" w:line=\"240\" w:lineRule=\"auto\"/><w:jc w:val=\"left\"/></w:pPr><w:rPr><w:sz w:val=\"16\"/><w:szCs w:val=\"16\"/></w:rPr></w:style>" +
        "</w:styles>";
}
