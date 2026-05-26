using AOTableEditor.Models;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace AOTableEditor.Services;

public static class TableMetadataService
{
    public const int MaxTableXmlFiles = 500;

    private const string LineOfBusinessAttributeName = "lineOfBusiness";
    private const string StateAttributeName = "state";
    private const string NewBusinessDateAttributeName = "newBusinessEffectiveDate";
    private const string RenewalDateAttributeName = "renewalEffectiveDate";

    public static IReadOnlyList<string> LineOfBusinessOptions { get; } =
    [
        "BOP",
        "TPP",
        "CPP"
    ];

    public static IReadOnlyList<string> StateOptions { get; } =
    [
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA",
        "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD",
        "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ",
        "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC",
        "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
        "DC"
    ];

    public static TableFileMetadata ReadMetadata(XDocument document)
    {
        XElement? root = document.Root;

        return new TableFileMetadata(
            ReadAttribute(root, LineOfBusinessAttributeName, "lob"),
            ReadAttribute(root, StateAttributeName),
            ParseDate(ReadAttribute(root, NewBusinessDateAttributeName)),
            ParseDate(ReadAttribute(root, RenewalDateAttributeName)));
    }

    public static void ApplyMetadata(XDocument document, TableFileMetadata metadata)
    {
        XElement root = document.Root ?? new XElement("tables");

        if (document.Root is null)
        {
            document.Add(root);
        }

        root.SetAttributeValue(
            LineOfBusinessAttributeName,
            string.IsNullOrWhiteSpace(metadata.LineOfBusiness) ? null : metadata.LineOfBusiness.Trim());
        root.SetAttributeValue(
            StateAttributeName,
            string.IsNullOrWhiteSpace(metadata.State) ? null : metadata.State.Trim());
        root.SetAttributeValue(
            NewBusinessDateAttributeName,
            FormatDate(metadata.NewBusinessEffectiveDate));
        root.SetAttributeValue(
            RenewalDateAttributeName,
            FormatDate(metadata.RenewalEffectiveDate));
    }

    public static TableXmlFileSearchResult FindTableXmlFiles(string folderPath)
    {
        var items = new List<XmlTableFileItem>();
        bool hitLimit = false;

        foreach (string filePath in EnumerateXmlFiles(folderPath))
        {
            if (TryReadTableXmlFile(filePath, out XmlTableFileItem? item) &&
                item is not null)
            {
                items.Add(item);

                if (items.Count >= MaxTableXmlFiles)
                {
                    hitLimit = true;
                    break;
                }
            }
        }

        return new TableXmlFileSearchResult(
            items
                .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            hitLimit,
            MaxTableXmlFiles);
    }

    private static IEnumerable<string> EnumerateXmlFiles(string folderPath)
    {
        var pendingFolders = new Stack<string>();
        pendingFolders.Push(folderPath);

        while (pendingFolders.Count > 0)
        {
            string currentFolder = pendingFolders.Pop();
            IEnumerable<string> files;
            IEnumerable<string> childFolders;

            try
            {
                files = Directory.EnumerateFiles(currentFolder, "*.xml");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            try
            {
                childFolders = Directory.EnumerateDirectories(currentFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string childFolder in childFolders)
            {
                pendingFolders.Push(childFolder);
            }
        }
    }

    private static bool TryReadTableXmlFile(string filePath, out XmlTableFileItem? item)
    {
        item = null;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };

            using XmlReader reader = XmlReader.Create(filePath, settings);
            XDocument document = XDocument.Load(reader);

            if (document.Root?.Name.LocalName != "tables")
            {
                return false;
            }

            TableFileMetadata metadata = ReadMetadata(document);
            item = new XmlTableFileItem
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                LineOfBusiness = metadata.LineOfBusiness,
                State = metadata.State,
                NewBusinessEffectiveDate = metadata.NewBusinessEffectiveDate,
                RenewalEffectiveDate = metadata.RenewalEffectiveDate
            };

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return false;
        }
    }

    private static string ReadAttribute(XElement? element, params string[] names)
    {
        if (element is null)
        {
            return "";
        }

        foreach (string name in names)
        {
            string? value = element.Attribute(name)?.Value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime exactDate)
            ? exactDate
            : DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime currentDate)
                ? currentDate.Date
                : null;
    }

    private static string? FormatDate(DateTime? date)
    {
        return date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
