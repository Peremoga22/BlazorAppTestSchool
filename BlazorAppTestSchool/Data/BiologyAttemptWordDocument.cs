using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.FileProviders;

namespace BlazorAppTestSchool.Data;

public static class BiologyAttemptWordDocument
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // Matches the web app's design tokens (wwwroot/app.css) so the exported report looks like the same product.
    private const string ColorBrand900 = "0B3B2E";
    private const string ColorBrand700 = "0A6A50";
    private const string ColorBrand600 = "0C8161";
    private const string ColorBrandSoft = "EAF3EE";
    private const string ColorInk = "142420";
    private const string ColorInkSoft = "3F5A51";
    private const string ColorBorder = "D9E7DE";
    private const string ColorZebra = "F3F8F5";
    private const string ColorDanger = "C0392B";
    private const string ColorMuted = "8A9A93";

    private const int PageContentWidth = 9306; // A4 width minus the left/right margins below, in dxa.

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] Create(BiologyAttemptReport report, IFileInfo diagram)
    {
        var questions = report.Questions.Select((question, index) => (Question: question, Index: index + 1)).ToArray();
        var hasImage = diagram.Exists && questions.Any(item => NeedsRootDiagram(item.Question));
        var pending = report.Questions.Any(question => question.Points is null);

        var body = new XElement(W + "body",
            HeaderCard(report, pending),
            EmptyLine(),
            DetailsTable(report),
            EmptyLine(),
            Paragraph("Heading1", null, Run("Завдання та відповіді")),
            QuestionsTable(questions, hasImage));

        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz", new XAttribute(W + "w", 11906), new XAttribute(W + "h", 16838)),
            new XElement(W + "pgMar", new XAttribute(W + "top", 1100), new XAttribute(W + "right", 1300),
                new XAttribute(W + "bottom", 1100), new XAttribute(W + "left", 1300),
                new XAttribute(W + "header", 600), new XAttribute(W + "footer", 600), new XAttribute(W + "gutter", 0))));

        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteXml(package, "[Content_Types].xml", ContentTypes(hasImage));
            WriteXml(package, "_rels/.rels", new XElement(Relationships + "Relationships",
                Relationship("rId1", "officeDocument", "word/document.xml")));
            WriteXml(package, "word/_rels/document.xml.rels", new XElement(Relationships + "Relationships",
                Relationship("rIdStyles", "styles", "styles.xml"),
                hasImage ? Relationship("rIdRoot", "image", "media/root-tip.png") : null));
            WriteXml(package, "word/document.xml", new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W),
                new XAttribute(XNamespace.Xmlns + "r", R),
                new XAttribute(XNamespace.Xmlns + "wp", Wp),
                new XAttribute(XNamespace.Xmlns + "a", A),
                new XAttribute(XNamespace.Xmlns + "pic", Pic),
                body));
            WriteXml(package, "word/styles.xml", Styles());
            if (hasImage)
            {
                using var image = diagram.CreateReadStream();
                using var entry = package.CreateEntry("word/media/root-tip.png").Open();
                image.CopyTo(entry);
            }
        }

        return output.ToArray();
    }

    // A shaded "card" (a borderless 1x1 table, since OOXML has no plain div) that puts the student,
    // class and score up front and large, the way a reader wants to see the result immediately.
    private static XElement HeaderCard(BiologyAttemptReport report, bool pending)
    {
        var scoreLabel = pending ? "ПОПЕРЕДНЯ ОЦІНКА" : "ПІДСУМКОВА ОЦІНКА";
        var paragraphs = new List<XElement>
        {
            Paragraph(null, "center", Run("Контрольна робота з біології", bold: true, color: ColorBrand900, size: 40)),
            Paragraph(null, "center",
                Run($"{report.Grade} клас · {report.StudentName} · {BiologyAttemptReport.FormatDuration(report.Duration)}",
                    color: ColorInkSoft, size: 22)),
            Paragraph(null, "center", Run(scoreLabel, color: ColorInkSoft, size: 18)),
            Paragraph(null, "center", Run($"{report.Score} з {report.Questions.Length} балів", bold: true, color: ColorBrand600, size: 56))
        };

        return Table([PageContentWidth],
            noBorders: true,
            Row(Cell(PageContentWidth, ColorBrandSoft, paragraphs.ToArray())));
    }

    private static XElement DetailsTable(BiologyAttemptReport report)
    {
        const int labelWidth = 2400;
        var valueWidth = PageContentWidth - labelWidth;

        var rows = StudentDetailRows(report)
            .Append(("Клас", $"{report.Grade}"))
            .Select(field => DetailRow(labelWidth, valueWidth, field.Item1, field.Item2))
            .ToArray();

        return Table([labelWidth, valueWidth], noBorders: false, rows);
    }

    private static IEnumerable<(string, string)> StudentDetailRows(BiologyAttemptReport report)
    {
        if (report.LastName is not { Length: > 0 } lastName ||
            report.FirstName is not { Length: > 0 } firstName ||
            report.Patronymic is not { Length: > 0 } patronymic)
        {
            return [("Прізвище, ім'я, по батькові", report.StudentName)];
        }

        return [("Прізвище", lastName), ("Ім'я", firstName), ("По батькові", patronymic)];
    }

    private static XElement DetailRow(int labelWidth, int valueWidth, string label, string value) => Row(
        Cell(labelWidth, ColorZebra, [Paragraph(null, null, Run(label, bold: true, color: ColorInk))]),
        Cell(valueWidth, null, [Paragraph(null, null, Run(value, color: ColorInk))]));

    private static XElement QuestionsTable((BiologyQuestionResult Question, int Index)[] questions, bool hasImage)
    {
        const int numberWidth = 500;
        const int pointsWidth = 1150;
        const int timeWidth = 1150;
        var textWidth = (PageContentWidth - numberWidth - pointsWidth - timeWidth) / 2;

        var headerRow = Row(
            HeaderCell(numberWidth, "№"),
            HeaderCell(textWidth, "Завдання"),
            HeaderCell(textWidth, "Відповідь учня"),
            HeaderCell(pointsWidth, "Бали"),
            HeaderCell(timeWidth, "Час"));

        var rows = questions.Select(item =>
        {
            var (question, number) = item;
            var zebra = number % 2 == 0 ? ColorZebra : null;

            var questionParagraphs = new List<XElement> { Paragraph(null, null, Run(question.Question)) };
            if (hasImage && NeedsRootDiagram(question))
            {
                questionParagraphs.Add(new XElement(W + "p", DiagramRun(number)));
            }

            var (pointsText, pointsColor) = question.Points switch
            {
                1 => ("1 бал", ColorBrand700),
                0 => ("0 балів", ColorDanger),
                _ => ("Очікує перевірки", ColorMuted)
            };

            var timeParagraphs = new List<XElement> { Paragraph(null, "center", Run(BiologyAttemptReport.FormatDuration(question.Duration))) };
            if (question.TimeExpired)
            {
                timeParagraphs.Add(Paragraph(null, "center", Run("Час вичерпано", color: ColorDanger, size: 18)));
            }

            return Row(
                Cell(numberWidth, zebra, [Paragraph(null, "center", Run(number.ToString(), bold: true))]),
                Cell(textWidth, zebra, questionParagraphs.ToArray()),
                Cell(textWidth, zebra, [Paragraph(null, null, Run(question.Answer ?? "Без відповіді", color: question.Answer is null ? ColorMuted : ColorInk))]),
                Cell(pointsWidth, zebra, [Paragraph(null, "center", Run(pointsText, bold: true, color: pointsColor))]),
                Cell(timeWidth, zebra, timeParagraphs.ToArray()));
        });

        return Table([numberWidth, textWidth, textWidth, pointsWidth, timeWidth], noBorders: false, [headerRow, .. rows]);
    }

    private static XElement HeaderCell(int width, string text) =>
        Cell(width, ColorBrand600, [Paragraph(null, "center", Run(text, bold: true, color: "FFFFFF"))]);

    private static bool NeedsRootDiagram(BiologyQuestionResult question) =>
        question.HasRootDiagram || question.Question.StartsWith("На схемі кореня познач", StringComparison.Ordinal);

    // --- Table building blocks ------------------------------------------------

    private static XElement Table(int[] columnWidths, bool noBorders, params XElement[] rows) =>
        new(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "tblW", new XAttribute(W + "w", columnWidths.Sum()), new XAttribute(W + "type", "dxa")),
                noBorders ? NoBorders() : TableBorders()),
            new XElement(W + "tblGrid", columnWidths.Select(width => new XElement(W + "gridCol", new XAttribute(W + "w", width)))),
            rows);

    private static XElement TableBorders() =>
        new(W + "tblBorders",
            Border("top"), Border("left"), Border("bottom"), Border("right"), Border("insideH"), Border("insideV"));

    private static XElement NoBorders() =>
        new(W + "tblBorders",
            NoBorder("top"), NoBorder("left"), NoBorder("bottom"), NoBorder("right"), NoBorder("insideH"), NoBorder("insideV"));

    private static XElement Border(string edge) =>
        new(W + edge, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 4), new XAttribute(W + "color", ColorBorder));

    private static XElement NoBorder(string edge) =>
        new(W + edge, new XAttribute(W + "val", "none"), new XAttribute(W + "sz", 0), new XAttribute(W + "color", "auto"));

    private static XElement Row(params XElement[] cells) => new(W + "tr", cells);

    private static XElement Cell(int width, string? fill, XElement[] paragraphs) =>
        new(W + "tc",
            new XElement(W + "tcPr",
                new XElement(W + "tcW", new XAttribute(W + "w", width), new XAttribute(W + "type", "dxa")),
                fill is null ? null : new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", fill)),
                new XElement(W + "tcMar",
                    new XElement(W + "top", new XAttribute(W + "w", 80), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "bottom", new XAttribute(W + "w", 80), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "left", new XAttribute(W + "w", 120), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "right", new XAttribute(W + "w", 120), new XAttribute(W + "type", "dxa"))),
                new XElement(W + "vAlign", new XAttribute(W + "val", "center"))),
            paragraphs.Length == 0 ? new XElement(W + "p") : paragraphs);

    private static XElement EmptyLine() => new(W + "p");

    // --- Text ------------------------------------------------------------------

    private static XElement Paragraph(string? style, string? align, params XElement[] runs)
    {
        var properties = new List<XElement>();
        if (style is not null)
        {
            properties.Add(Property("pStyle", style));
        }

        if (align is not null)
        {
            properties.Add(Property("jc", align));
        }

        return new XElement(W + "p", properties.Count == 0 ? null : new XElement(W + "pPr", properties), runs);
    }

    private static XElement Run(string text, bool bold = false, string? color = null, int? size = null)
    {
        var validXml = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is 9 or 10 or 13 or >= 32 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000)
            {
                validXml.Append(rune.ToString());
            }
        }

        var properties = new List<XElement>();
        if (bold)
        {
            properties.Add(new XElement(W + "b"));
        }

        if (color is not null)
        {
            properties.Add(Property("color", color));
        }

        if (size is not null)
        {
            properties.Add(Property("sz", size.Value.ToString()));
        }

        var lines = validXml.ToString().ReplaceLineEndings("\n").Split('\n');
        var run = new XElement(W + "r", properties.Count == 0 ? null : new XElement(W + "rPr", properties));
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                run.Add(new XElement(W + "br"));
            }

            run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), lines[index]));
        }

        return run;
    }

    private static XElement Styles() =>
        new(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W),
            new XElement(W + "docDefaults",
                new XElement(W + "rPrDefault", new XElement(W + "rPr", RFonts(), Property("sz", "22"), Property("lang", "uk-UA")))),
            new XElement(W + "style", new XAttribute(W + "type", "paragraph"),
                new XAttribute(W + "default", 1), new XAttribute(W + "styleId", "Normal"),
                Property("name", "Normal"),
                new XElement(W + "pPr", Spacing(0, 100)),
                new XElement(W + "rPr", RFonts(), Property("sz", "22"), Property("color", ColorInk), Property("lang", "uk-UA"))),
            Style("Heading1", "heading 1", 30, 240, 170, color: ColorBrand900, outlineLevel: 0));

    private static XElement Style(string id, string name, int size, int before, int after, string color, bool bold = true, int? outlineLevel = null) =>
        new(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", id),
            Property("name", name), Property("basedOn", "Normal"),
            new XElement(W + "pPr", new XElement(W + "keepNext"), Spacing(before, after),
                outlineLevel is int level ? Property("outlineLvl", level.ToString()) : null),
            new XElement(W + "rPr", RFonts(), bold ? new XElement(W + "b") : null, Property("sz", size.ToString()), Property("color", color)));

    private static XElement RFonts() =>
        new(W + "rFonts", new XAttribute(W + "ascii", "Calibri"), new XAttribute(W + "hAnsi", "Calibri"), new XAttribute(W + "cs", "Calibri"));

    private static XElement Spacing(int before, int after) =>
        new(W + "spacing", new XAttribute(W + "before", before), new XAttribute(W + "after", after),
            new XAttribute(W + "line", 276), new XAttribute(W + "lineRule", "auto"));

    private static XElement Property(string name, string value) => new(W + name, new XAttribute(W + "val", value));

    private static XElement DiagramRun(int number)
    {
        const int width = 1_800_000;
        const int height = 1_706_667;
        return new XElement(W + "r", new XElement(W + "drawing", new XElement(Wp + "inline",
            new XElement(Wp + "extent", new XAttribute("cx", width), new XAttribute("cy", height)),
            new XElement(Wp + "docPr", new XAttribute("id", number), new XAttribute("name", "Схема кореня"),
                new XAttribute("descr", "Схема кінчика кореня з позначками А, Б, В і Г")),
            new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", Pic.NamespaceName),
                new XElement(Pic + "pic",
                    new XElement(Pic + "nvPicPr",
                        new XElement(Pic + "cNvPr", new XAttribute("id", number), new XAttribute("name", "root-tip.png")),
                        new XElement(Pic + "cNvPicPr")),
                    new XElement(Pic + "blipFill", new XElement(A + "blip", new XAttribute(R + "embed", "rIdRoot")),
                        new XElement(A + "stretch", new XElement(A + "fillRect"))),
                    new XElement(Pic + "spPr",
                        new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                            new XElement(A + "ext", new XAttribute("cx", width), new XAttribute("cy", height))),
                        new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")))))))));
    }

    private static XElement ContentTypes(bool hasImage) =>
        new(Types + "Types",
            ContentTypeEntry("Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml"),
            ContentTypeEntry("Default", "Extension", "xml", "application/xml"),
            hasImage ? ContentTypeEntry("Default", "Extension", "png", "image/png") : null,
            ContentTypeEntry("Override", "PartName", "/word/document.xml",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
            ContentTypeEntry("Override", "PartName", "/word/styles.xml",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"));

    private static XElement ContentTypeEntry(string element, string attribute, string value, string contentType) =>
        new(Types + element, new XAttribute(attribute, value), new XAttribute("ContentType", contentType));

    private static XElement Relationship(string id, string kind, string target) =>
        new(Relationships + "Relationship", new XAttribute("Id", id),
            new XAttribute("Type", $"{R.NamespaceName}/{kind}"), new XAttribute("Target", target));

    private static void WriteXml(ZipArchive package, string name, XElement root)
    {
        using var stream = package.CreateEntry(name).Open();
        new XDocument(root).Save(stream);
    }
}
