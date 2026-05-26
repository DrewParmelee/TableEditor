namespace AOTableEditor.Models;

public sealed record TableFileMetadata(
    string LineOfBusiness,
    string State,
    DateTime? NewBusinessEffectiveDate,
    DateTime? RenewalEffectiveDate);

public sealed class XmlTableFileItem
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string LineOfBusiness { get; init; }
    public required string State { get; init; }
    public DateTime? NewBusinessEffectiveDate { get; init; }
    public DateTime? RenewalEffectiveDate { get; init; }

    public string NewBusinessEffectiveDateText => FormatDate(NewBusinessEffectiveDate);
    public string RenewalEffectiveDateText => FormatDate(RenewalEffectiveDate);

    private static string FormatDate(DateTime? date)
    {
        return date?.ToString("yyyy-MM-dd") ?? "";
    }
}

public sealed record TableXmlFileSearchResult(
    List<XmlTableFileItem> Files,
    bool HitLimit,
    int Limit);
