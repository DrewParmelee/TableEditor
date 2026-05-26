namespace AOTableEditor.Models;

public sealed class KeySetDefinition
{
    public string Name { get; set; } = "";
    public string SearchType { get; set; } = "=";
    public List<string> Keys { get; set; } = [];
}
