using System.Text;

namespace CsvImportApi.Csv;

/// <summary>A parsed CSV record. <see cref="LineNumber"/> is the 1-based physical line where the record starts.</summary>
public sealed record CsvRecord(int LineNumber, IReadOnlyList<string> Fields);

public sealed class CsvParseException(int lineNumber, string message) : Exception(message)
{
    public int LineNumber { get; } = lineNumber;
}

/// <summary>
/// Minimal RFC 4180 parser: comma delimiter, optional double-quoted fields with "" escapes,
/// quoted fields may contain commas and line breaks, LF / CRLF / CR line endings.
/// </summary>
public static class CsvParser
{
    public static IReadOnlyList<CsvRecord> Parse(string text)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length;)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                if (c == '\n') line++;
                field.Append(c);
                i++;
                continue;
            }

            if (c == '"' && field.Length == 0 && !fieldWasQuoted)
            {
                inQuotes = true;
                fieldWasQuoted = true;
                i++;
                continue;
            }

            if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
                fieldWasQuoted = false;
                i++;
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                fields.Add(field.ToString());
                records.Add(new CsvRecord(recordLine, fields.ToArray()));
                fields.Clear();
                field.Clear();
                fieldWasQuoted = false;
                line++;
                recordLine = line;
                i++;
                continue;
            }

            field.Append(c);
            i++;
        }

        if (inQuotes)
        {
            throw new CsvParseException(recordLine, "Quoted field is not closed before end of file.");
        }

        if (field.Length > 0 || fields.Count > 0 || fieldWasQuoted)
        {
            fields.Add(field.ToString());
            records.Add(new CsvRecord(recordLine, fields.ToArray()));
        }

        return records;
    }
}
