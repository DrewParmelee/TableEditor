using AOTableEditor.Models;
using System.Text;

namespace AOTableEditor.Services;

public static class TableCsvExporter
{
    public static string ToCsv(TableDefinition table)
    {
        int rowCombinationCount = CountCombinations(table.RowSets);
        int columnCombinationCount = CountCombinations(table.ColSets);
        int pageCount = Math.Max(1, table.PageKeys.Count);
        int[] rowStrides = BuildStrides(table.RowSets);
        int[] columnStrides = BuildStrides(table.ColSets);
        bool hasExplicitPages = table.PageKeys.Count > 0;

        var builder = new StringBuilder();
        var header = new List<string>();

        if (hasExplicitPages)
        {
            header.Add("Page");
        }

        header.AddRange(table.RowSets.Select(set => set.Name));
        header.AddRange(table.ColSets.Select(set => set.Name));
        header.Add("Value");
        AppendCsvRow(builder, header);

        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            for (int rowIndex = 0; rowIndex < rowCombinationCount; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < columnCombinationCount; columnIndex++)
                {
                    var values = new List<string>();

                    if (hasExplicitPages)
                    {
                        values.Add(table.PageKeys[pageIndex]);
                    }

                    values.AddRange(GetCombinationValues(table.RowSets, rowStrides, rowIndex));
                    values.AddRange(GetCombinationValues(table.ColSets, columnStrides, columnIndex));
                    values.Add(GetDataValue(table, pageIndex, rowCombinationCount, rowIndex, columnIndex));

                    AppendCsvRow(builder, values);
                }
            }
        }

        return builder.ToString();
    }

    private static string GetDataValue(
        TableDefinition table,
        int pageIndex,
        int rowCombinationCount,
        int rowIndex,
        int columnIndex)
    {
        int sourceRowIndex = (pageIndex * rowCombinationCount) + rowIndex;

        return sourceRowIndex < table.DataRows.Count &&
            columnIndex < table.DataRows[sourceRowIndex].Count
                ? table.DataRows[sourceRowIndex][columnIndex]
                : "";
    }

    private static IEnumerable<string> GetCombinationValues(
        List<KeySetDefinition> sets,
        int[] strides,
        int combinationIndex)
    {
        for (int setIndex = 0; setIndex < sets.Count; setIndex++)
        {
            yield return GetCombinationValue(sets, strides, combinationIndex, setIndex);
        }
    }

    private static int CountCombinations(List<KeySetDefinition> sets)
    {
        if (sets.Count == 0)
        {
            return 1;
        }

        int total = 1;

        foreach (KeySetDefinition set in sets)
        {
            if (set.Keys.Count == 0)
            {
                return 0;
            }

            total = checked(total * set.Keys.Count);
        }

        return total;
    }

    private static int[] BuildStrides(List<KeySetDefinition> sets)
    {
        var strides = new int[sets.Count];
        int stride = 1;

        for (int index = sets.Count - 1; index >= 0; index--)
        {
            strides[index] = stride;

            if (sets[index].Keys.Count == 0)
            {
                stride = 0;
                continue;
            }

            stride = checked(stride * sets[index].Keys.Count);
        }

        return strides;
    }

    private static string GetCombinationValue(
        List<KeySetDefinition> sets,
        int[] strides,
        int combinationIndex,
        int setIndex)
    {
        if (setIndex < 0 ||
            setIndex >= sets.Count ||
            combinationIndex < 0 ||
            strides[setIndex] == 0 ||
            sets[setIndex].Keys.Count == 0)
        {
            return "";
        }

        int keyIndex = (combinationIndex / strides[setIndex]) % sets[setIndex].Keys.Count;
        return sets[setIndex].Keys[keyIndex];
    }

    private static void AppendCsvRow(StringBuilder builder, IReadOnlyList<string> values)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendCsvValue(builder, values[index]);
        }

        builder.AppendLine();
    }

    private static void AppendCsvValue(StringBuilder builder, string value)
    {
        bool requiresQuotes = value.IndexOfAny(['"', ',', '\r', '\n']) >= 0;

        if (!requiresQuotes)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        builder.Append(value.Replace("\"", "\"\""));
        builder.Append('"');
    }
}
