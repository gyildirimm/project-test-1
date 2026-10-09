namespace Expenses.Tests;

internal static class SqliteConnectionCleanup
{
    // SQLite connection pooling can keep the file locked briefly after dispose; ignore cleanup failures.
    public static void TryDeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
    }
}
