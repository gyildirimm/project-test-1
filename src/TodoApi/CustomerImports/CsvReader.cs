using System.Text;

namespace TodoApi.CustomerImports;

public sealed record CsvRecord(int LineNumber, IReadOnlyList<string> Fields);

// Minimal RFC 4180 reader: comma-delimited, double-quoted fields with "" as an escaped quote.
// Quoted fields may contain commas and line breaks. Accepts CRLF, LF or CR line endings.
// Blank lines are skipped but still count toward line numbers.
public static class CsvReader
{
    public static List<CsvRecord> Parse(string text)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        void EndRecord()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (fields.Any(f => f.Trim().Length > 0))
                records.Add(new CsvRecord(recordLine, fields));
            fields = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else if (c is '\r' or '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    field.Append('\n');
                    line++;
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r' or '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    EndRecord();
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (inQuotes)
            throw new FormatException("The CSV file has an unterminated quoted field.");
        if (field.Length > 0 || fields.Count > 0)
            EndRecord();

        return records;
    }
}
