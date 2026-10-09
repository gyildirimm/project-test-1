using System.Text;

namespace CsvImportApi.People;

/// <summary>
/// Preview-then-commit import. A preview is stored server-side for <see cref="PersonCsvContract.PreviewTtl"/>;
/// the commit imports exactly the rows that were previewed, re-checking existence against storage.
/// </summary>
public sealed class PersonImportService(IPersonRepository people, TimeProvider time)
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, StoredPreview> _previews = [];

    private sealed class StoredPreview(Guid id, DateTimeOffset expiresAt, IReadOnlyList<ImportRowDto> rows)
    {
        public Guid Id { get; } = id;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public IReadOnlyList<ImportRowDto> Rows { get; } = rows;
        public bool Committed { get; set; }
    }

    public ImportPreviewDto Preview(Stream content, long length)
    {
        if (length == 0)
        {
            throw new ImportException(400, ImportErrorCodes.EmptyFile, "Uploaded file is empty.");
        }

        if (length > PersonCsvContract.MaxFileBytes)
        {
            throw new ImportException(413, ImportErrorCodes.FileTooLarge,
                $"File is larger than {PersonCsvContract.MaxFileBytes} bytes.");
        }

        var text = Decode(content);
        var rows = PersonCsvAnalyzer.Analyze(text, key => people.ExistsByEmail(key));

        var now = time.GetUtcNow();
        var id = Guid.NewGuid();
        var expiresAt = now + PersonCsvContract.PreviewTtl;

        lock (_gate)
        {
            PruneExpired(now);
            _previews[id] = new StoredPreview(id, expiresAt, rows);
        }

        return new ImportPreviewDto(id, expiresAt, Summarize(rows), rows);
    }

    public ImportCommitDto Commit(Guid importId)
    {
        lock (_gate)
        {
            if (!_previews.TryGetValue(importId, out var stored))
            {
                throw new ImportException(404, ImportErrorCodes.ImportNotFound, "Import not found.");
            }

            if (stored.Committed)
            {
                throw new ImportException(409, ImportErrorCodes.ImportAlreadyCommitted,
                    "This import has already been committed.");
            }

            if (time.GetUtcNow() > stored.ExpiresAt)
            {
                _previews.Remove(importId);
                throw new ImportException(410, ImportErrorCodes.ImportExpired,
                    "This preview has expired. Upload the file again.");
            }

            var invalid = stored.Rows.Where(r => r.Status == ImportRowStatus.Invalid).ToList();
            if (invalid.Count > 0)
            {
                throw new ImportException(409, ImportErrorCodes.ImportHasInvalidRows,
                    "Fix the invalid rows and upload the file again.",
                    invalid.Select(r => (object)new { line = r.Line, errors = r.Errors }).ToList());
            }

            var createdIds = new List<int>();
            var duplicateInFile = 0;
            var alreadyExists = 0;

            foreach (var row in stored.Rows)
            {
                switch (row.Status)
                {
                    case ImportRowStatus.DuplicateInFile:
                        duplicateInFile++;
                        break;
                    case ImportRowStatus.AlreadyExists:
                        alreadyExists++;
                        break;
                    case ImportRowStatus.Valid:
                        // Storage may have changed since the preview; re-check so the commit never creates duplicates.
                        if (people.ExistsByEmail(PersonCsvContract.NormalizeEmail(row.Values.Email!)))
                        {
                            alreadyExists++;
                            break;
                        }

                        var person = people.Add(row.Values.FirstName!, row.Values.LastName!,
                            row.Values.Email!, row.Values.Phone);
                        createdIds.Add(person.Id);
                        break;
                }
            }

            stored.Committed = true;

            return new ImportCommitDto(importId, createdIds.Count,
                new ImportCommitSkippedDto(duplicateInFile, alreadyExists), createdIds);
        }
    }

    public IReadOnlyList<PersonDto> GetPeople() =>
        people.GetAll().Select(p => new PersonDto(p.Id, p.FirstName, p.LastName, p.Email, p.Phone)).ToList();

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var key in _previews.Where(p => p.Value.ExpiresAt < now).Select(p => p.Key).ToList())
        {
            _previews.Remove(key);
        }
    }

    private static string Decode(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);

        string text;
        try
        {
            text = StrictUtf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (DecoderFallbackException)
        {
            throw new ImportException(400, ImportErrorCodes.InvalidEncoding, "File must be UTF-8 encoded.");
        }

        if (text.StartsWith('﻿'))
        {
            text = text[1..];
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ImportException(400, ImportErrorCodes.EmptyFile, "Uploaded file is empty.");
        }

        return text;
    }

    private static ImportSummaryDto Summarize(IReadOnlyList<ImportRowDto> rows) => new(
        TotalRows: rows.Count,
        Valid: rows.Count(r => r.Status == ImportRowStatus.Valid),
        Invalid: rows.Count(r => r.Status == ImportRowStatus.Invalid),
        DuplicateInFile: rows.Count(r => r.Status == ImportRowStatus.DuplicateInFile),
        AlreadyExists: rows.Count(r => r.Status == ImportRowStatus.AlreadyExists));
}
