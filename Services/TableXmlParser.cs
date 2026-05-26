using AOTableEditor.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace AOTableEditor.Services;

public static class TableXmlParser
{
    public static List<TableDefinition> Load(string filePath)
    {
        return LoadDocument(XDocument.Load(filePath));
    }

    public static List<TableDefinition> LoadDocument(XDocument document)
    {
        XElement root = document.Root
            ?? throw new InvalidOperationException("XML file has no root element.");

        return root.Elements("table")
            .Select(ParseTable)
            .ToList();
    }

    private static TableDefinition ParseTable(XElement tableElement, int sourceIndex)
    {
        string name = tableElement.Attribute("name")?.Value
            ?? throw new InvalidOperationException("A table is missing a name attribute.");

        string comment = tableElement.Attribute("comment")?.Value
            ?? tableElement.Element("comment")?.Value.Trim()
            ?? "";
        string delimiter = tableElement.Attribute("delimiter")?.Value ?? ",";
        string dataType = TableValueFormatter.NormalizeDataType(tableElement.Attribute("dataType")?.Value);
        int? decimals = ParseDecimals(tableElement.Attribute("decimals")?.Value);

        var rowSets = tableElement
            .Element("rowKeys")?
            .Elements("rowSet")
            .Select(ParseKeySet)
            .ToList() ?? [];

        var colSets = tableElement
            .Element("colKeys")?
            .Elements("colSet")
            .Select(ParseKeySet)
            .ToList() ?? [];

        var pageKeys = tableElement
            .Element("pageKeys")?
            .Elements("key")
            .Select(key => key.Value.Trim())
            .ToList() ?? [];
        string pageSearchType = NormalizeSearchTypeForDisplay(
            tableElement.Element("pageKeys")?.Attribute("searchType")?.Value);

        var dataRows = tableElement
            .Element("data")?
            .Elements("row")
            .Select(row => row.Value.Split(delimiter).Select(x => x.Trim()).ToList())
            .ToList() ?? [];

        return new TableDefinition
        {
            SourceIndex = sourceIndex,
            Name = name,
            Comment = comment,
            Delimiter = delimiter,
            DataType = dataType,
            Decimals = TableValueFormatter.SupportsDecimals(dataType) ? decimals : null,
            RowSets = rowSets,
            ColSets = colSets,
            PageSearchType = pageSearchType,
            PageKeys = pageKeys,
            DataRows = dataRows
        };
    }

    private static KeySetDefinition ParseKeySet(XElement setElement)
    {
        string name = setElement.Attribute("name")?.Value
            ?? throw new InvalidOperationException("A rowSet or colSet is missing a name attribute.");

        List<string> keys = setElement
            .Elements("key")
            .Select(key => key.Value.Trim())
            .ToList();

        return new KeySetDefinition
        {
            Name = name,
            SearchType = NormalizeSearchTypeForDisplay(setElement.Attribute("searchType")?.Value),
            Keys = keys
        };
    }

    private static string NormalizeSearchTypeForDisplay(string? searchType)
    {
        return searchType?.Trim().ToLowerInvariant() switch
        {
            null or "" or "eq" or "=" => "=",
            "lt" or "<" => "<",
            "le" or "lte" or "<=" => "<=",
            "gt" or ">" => ">",
            "ge" or "gte" or ">=" => ">=",
            "range" => "Range",
            "interpolate" => "Interpolate",
            "graduated" => "Graduated",
            _ => "="
        };
    }

    private static int? ParseDecimals(string? value)
    {
        return int.TryParse(value, out int decimals) && decimals >= 0
            ? decimals
            : null;
    }
}
