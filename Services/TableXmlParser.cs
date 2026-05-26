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
        XDocument document = XDocument.Load(filePath);

        XElement root = document.Root
            ?? throw new InvalidOperationException("XML file has no root element.");

        return root.Elements("table")
            .Select(ParseTable)
            .ToList();
    }

    private static TableDefinition ParseTable(XElement tableElement)
    {
        string name = tableElement.Attribute("name")?.Value
            ?? throw new InvalidOperationException("A table is missing a name attribute.");

        string delimiter = tableElement.Attribute("delimiter")?.Value ?? ",";

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

        var dataRows = tableElement
            .Element("data")?
            .Elements("row")
            .Select(row => row.Value.Split(delimiter).Select(x => x.Trim()).ToList())
            .ToList() ?? [];

        return new TableDefinition
        {
            Name = name,
            Delimiter = delimiter,
            RowSets = rowSets,
            ColSets = colSets,
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
            Keys = keys
        };
    }
}