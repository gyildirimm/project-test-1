using System.Text;

namespace TodoApi.CustomerImports;

// Raw cell values for one data row. Null means the column is absent from the file or the row is short.
public sealed record CsvRowInput(int LineNumber, string? Name, string? Email, string? Phone);

// Thrown for file-level problems; the endpoints turn these into 400 responses.
public sealed class CsvImportException(string message) : Exception(message);

public static class CustomerCsv
{
    public static async Task<List<CsvRowInput>> ReadRowsAsync(Stream stream)
    {
        string text;
        try
        {
            // detectEncodingFromByteOrderMarks is off so a UTF-16 BOM is not silently honored; the BOM is stripped below.
            using var reader = new StreamReader(stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: false);
            text = await reader.ReadToEndAsync();
        }
        catch (DecoderFallbackException)
        {
            throw new CsvImportException("The CSV file must be UTF-8 encoded.");
        }

        if (text.StartsWith('﻿'))
            text = text[1..];

        List<CsvRecord> records;
        try
        {
            records = CsvReader.Parse(text);
        }
        catch (FormatException ex)
        {
            throw new CsvImportException(ex.Message);
        }

        if (records.Count == 0)
            throw new CsvImportException("The CSV file is empty.");

        var header = records[0].Fields.Select(f => f.Trim().ToLowerInvariant()).ToList();
        var nameIndex = header.IndexOf("name");
        if (nameIndex < 0)
            throw new CsvImportException("The CSV header must contain a 'name' column.");
        var emailIndex = header.IndexOf("email");
        var phoneIndex = header.IndexOf("phone");

        if (records.Count == 1)
            throw new CsvImportException("The CSV file has a header but no data rows.");

        return records.Skip(1)
            .Select(r => new CsvRowInput(
                r.LineNumber,
                Cell(r, nameIndex),
                Cell(r, emailIndex),
                Cell(r, phoneIndex)))
            .ToList();
    }

    private static string? Cell(CsvRecord record, int index) =>
        index >= 0 && index < record.Fields.Count ? record.Fields[index] : null;
}
