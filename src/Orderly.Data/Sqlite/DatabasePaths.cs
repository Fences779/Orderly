namespace Orderly.Data.Sqlite;

public static class DatabasePaths
{
    public static string GetDefaultDatabasePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Orderly");

        Directory.CreateDirectory(root);
        return Path.Combine(root, "orderly.db");
    }
}
