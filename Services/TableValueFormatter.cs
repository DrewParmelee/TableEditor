using AOTableEditor.Models;
using System.Globalization;

namespace AOTableEditor.Services;

public static class TableValueFormatter
{
    public const string DefaultDataType = "string";

    public static IReadOnlyList<string> DataTypeOptions { get; } =
    [
        DefaultDataType,
        "int",
        "float",
        "double",
        "decimal"
    ];

    public static string NormalizeDataType(string? dataType)
    {
        string normalized = dataType?.Trim().ToLowerInvariant() ?? "";

        return normalized switch
        {
            "" or "text" or "string" => DefaultDataType,
            "integer" or "int" => "int",
            "single" or "float" => "float",
            "double" => "double",
            "number" or "decimal" => "decimal",
            _ => DefaultDataType
        };
    }

    public static bool IsDefaultDataType(string? dataType)
    {
        return NormalizeDataType(dataType) == DefaultDataType;
    }

    public static bool SupportsDecimals(string? dataType)
    {
        return NormalizeDataType(dataType) is "float" or "double" or "decimal";
    }

    public static bool TryNormalizeValue(
        TableDefinition table,
        string value,
        out string normalizedValue,
        out string error)
    {
        string dataType = NormalizeDataType(table.DataType);
        string trimmedValue = value.Trim();

        normalizedValue = trimmedValue;
        error = "";

        if (string.IsNullOrEmpty(trimmedValue) ||
            dataType == DefaultDataType)
        {
            return true;
        }

        if (trimmedValue == "-")
        {
            normalizedValue = "-";
            return true;
        }

        if (dataType == "int")
        {
            if (!TryParseLong(trimmedValue, out long integerValue))
            {
                error = "Expected an integer value.";
                return false;
            }

            normalizedValue = integerValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (!TryParseDecimal(trimmedValue, out decimal decimalValue))
        {
            error = $"Expected a {dataType} value.";
            return false;
        }

        normalizedValue = table.Decimals is int decimals
            ? decimalValue.ToString($"F{decimals}", CultureInfo.InvariantCulture)
            : trimmedValue;
        return true;
    }

    private static bool TryParseLong(string value, out long result)
    {
        return long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result) ||
            long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out result);
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        return decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result) ||
            decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out result);
    }
}
