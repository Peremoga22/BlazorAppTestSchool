using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.FileProviders;

namespace BlazorAppTestSchool.Data;

public static class BiologyAttemptWordDocument
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

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
            Paragraph("Title", Run("Контрольна робота з біології")),
            StudentDetails(report),
            Field("Клас", report.Grade.ToString()),
            Field(pending ? "Попередня оцінка" : "Підсумкова оцінка", $"{report.Score} з {report.Questions.Length}"),
            Field("Загальний час", BiologyAttemptReport.FormatDuration(report.Duration)),
            Paragraph("Heading1", Run("Завдання та відповіді")));

        foreach (var (question, number) in questions)
        {
            body.Add(Paragraph("Heading2", Run($"Завдання {number}")));
            body.Add(Paragraph("Question", Run(question.Question)));
            if (hasImage && NeedsRootDiagram(question))
            {
                body.Add(Paragraph("Question", DiagramRun(number)));
            }

            var points = question.Points switch
            {
                1 => "1 бал",
                0 => "0 балів",
                _ => "Очікує перевірки"
            };
            var time = BiologyAttemptReport.FormatDuration(question.Duration);
            var timeStatus = question.TimeExpired ? " · Час вичерпано" : string.Empty;
            body.Add(Paragraph("Question", Run($"{points} · {time}{timeStatus}")));
            body.Add(Field("Відповідь учня", question.Answer ?? "Без відповіді"));
        }

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

    private static IEnumerable<XElement> StudentDetails(BiologyAttemptReport report)
    {
        if (report.LastName is not { Length: > 0 } lastName ||
            report.FirstName is not { Length: > 0 } firstName ||
            report.Patronymic is not { Length: > 0 } patronymic)
        {
            return [Field("Прізвище, ім’я, по батькові", report.StudentName)];
        }

        return [Field("Прізвище", lastName), Field("Ім’я", firstName), Field("По батькові", patronymic)];
    }

    private static bool NeedsRootDiagram(BiologyQuestionResult question) =>
        question.HasRootDiagram || question.Question.StartsWith("На схемі кореня познач", StringComparison.Ordinal);

    private static XElement Field(string label, string value) =>
        Paragraph(null, Run($"{label}  ", bold: true), Run(value));

    private static XElement Paragraph(string? style, params XElement[] runs) =>
        new(W + "p", style is null ? null : new XElement(W + "pPr", Property("pStyle", style)), runs);

    private static XElement Run(string text, bool bold = false)
    {
        var validXml = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is 9 or 10 or 13 or >= 32 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000)
            {
                validXml.Append(rune.ToString());
            }
        }

        var lines = validXml.ToString().ReplaceLineEndings("\n").Split('\n');
        var run = new XElement(W + "r", bold ? new XElement(W + "rPr", new XElement(W + "b")) : null);
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
            new XElement(W + "style", new XAttribute(W + "type", "paragraph"),
                new XAttribute(W + "default", 1), new XAttribute(W + "styleId", "Normal"),
                Property("name", "Normal"),
                new XElement(W + "pPr", Spacing(0, 100)),
                new XElement(W + "rPr",
                    new XElement(W + "rFonts", new XAttribute(W + "ascii", "Calibri"), new XAttribute(W + "hAnsi", "Calibri")),
                    Property("sz", "22"), Property("color", "000000"), Property("lang", "uk-UA"))),
            Style("Title", "Title", 42, 0, 280),
            Style("Heading1", "heading 1", 30, 240, 170, outlineLevel: 0),
            Style("Heading2", "heading 2", 24, 130, 65, outlineLevel: 1),
            Style("Question", "Question", 22, 0, 65, bold: false));

    private static XElement Style(string id, string name, int size, int before, int after, bool bold = true, int? outlineLevel = null) =>
        new(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", id),
            Property("name", name), Property("basedOn", "Normal"),
            new XElement(W + "pPr", new XElement(W + "keepNext"), Spacing(before, after),
                outlineLevel is int level ? Property("outlineLvl", level.ToString()) : null),
            new XElement(W + "rPr", bold ? new XElement(W + "b") : null, Property("sz", size.ToString())));

    private static XElement Spacing(int before, int after) =>
        new(W + "spacing", new XAttribute(W + "before", before), new XAttribute(W + "after", after),
            new XAttribute(W + "line", 276), new XAttribute(W + "lineRule", "auto"));

    private static XElement Property(string name, string value) => new(W + name, new XAttribute(W + "val", value));

    private static XElement DiagramRun(int number)
    {
        const int width = 2_000_000;
        const int height = 1_896_296;
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
