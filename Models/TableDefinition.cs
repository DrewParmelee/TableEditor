using System.Data;

namespace AOTableEditor.Models;

public sealed class TableDefinition
{
    public string Name { get; set; } = "";
    public string Delimiter { get; set; } = ",";
    public List<KeySetDefinition> RowSets { get; set; } = [];
    public List<KeySetDefinition> ColSets { get; set; } = [];
    public List<List<string>> DataRows { get; set; } = [];

    public DataTable? RenderedDataTable { get; set; }
}