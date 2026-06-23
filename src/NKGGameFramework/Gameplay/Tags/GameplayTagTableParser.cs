namespace NKGGameFramework.Gameplay;

public readonly record struct GameplayTagTableParseResult(int RowsRegistered);

public static class GameplayTagTableParser
{
    public static GameplayTagTableParseResult ApplyCsvToRegistry(
        string csvText,
        GameplayTagRegistry registry,
        string source = "",
        bool isRestricted = false)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        ArgumentNullException.ThrowIfNull(registry);

        var rows = ParseCsv(csvText).ToList();
        if (rows.Count == 0)
        {
            return new GameplayTagTableParseResult(0);
        }

        var header = rows[0];
        var tagIndex = FindColumn(header, "Tag");
        if (tagIndex < 0)
        {
            throw new InvalidOperationException("Gameplay tag table CSV must contain a 'Tag' column.");
        }

        var devCommentIndex = FindColumn(header, "DevComment");
        var allowChildrenIndex = FindColumn(header, "bAllowNonRestrictedChildren");
        var registered = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var tagName = GetColumn(row, tagIndex);
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            registry.Register(
                tagName,
                source,
                GetColumn(row, devCommentIndex),
                isRestricted,
                ParseBool(GetColumn(row, allowChildrenIndex)));
            registered++;
        }

        return new GameplayTagTableParseResult(registered);
    }

    private static int FindColumn(IReadOnlyList<string> header, string columnName)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string GetColumn(IReadOnlyList<string> row, int index)
    {
        return index >= 0 && index < row.Count ? row[index] : string.Empty;
    }

    private static IEnumerable<IReadOnlyList<string>> ParseCsv(string csvText)
    {
        foreach (var rawLine in csvText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            yield return ParseCsvLine(line);
        }
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new List<char>();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Add('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                values.Add(new string(current.ToArray()).Trim());
                current.Clear();
            }
            else
            {
                current.Add(ch);
            }
        }

        values.Add(new string(current.ToArray()).Trim());
        return values;
    }

    private static bool ParseBool(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal);
    }
}
