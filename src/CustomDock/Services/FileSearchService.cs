using System.Data.OleDb;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>A file or folder found by <see cref="FileSearchService"/>.</summary>
public sealed record FileHit(string Path, string Name, DateTime? Modified, bool IsFolder);

/// <summary>
/// The launcher's file search: asks the Windows Search index (the same one File Explorer uses) for files and folders in
/// the user's folder, AppData left out (see <see cref="WindowsSearchQuery"/>). Runs off the UI thread and stops when
/// cancelled. When Windows Search is turned off or missing, it quietly finds nothing and tries again ten minutes later.
/// </summary>
public static class FileSearchService
{
    private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);
    private static DateTime s_unavailableUntil;

    public static async Task<IReadOnlyList<FileHit>> SearchAsync(string text, CancellationToken token)
    {
        if (DateTime.UtcNow < s_unavailableUntil) return Array.Empty<FileHit>();
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return Array.Empty<FileHit>();
        string? sql = WindowsSearchQuery.Build(text, profile, new[] { Path.Combine(profile, "AppData") });
        if (sql is null) return Array.Empty<FileHit>();
        return await Task.Run(() => Run(sql, token), token).ConfigureAwait(true);
    }

    private static IReadOnlyList<FileHit> Run(string sql, CancellationToken token)
    {
        var hits = new List<FileHit>();
        try
        {
            using var connection = new OleDbConnection(ConnectionString);
            connection.Open();
            using var command = new OleDbCommand(sql, connection) { CommandTimeout = 5 };
            using var registration = token.Register(() =>
            {
                try { command.Cancel(); }
                catch (Exception) { /* the query may just have finished */ }
            });
            using var reader = command.ExecuteReader();
            while (!token.IsCancellationRequested && reader.Read())
            {
                if (reader.GetValue(0) is not string path || string.IsNullOrEmpty(path)) continue;
                string name = reader.GetValue(1) as string ?? Path.GetFileName(path);
                DateTime? modified = reader.GetValue(2) is DateTime date ? date.ToLocalTime() : null;
                bool folder = string.Equals(reader.GetValue(3) as string, "Directory", StringComparison.OrdinalIgnoreCase);
                hits.Add(new FileHit(path, name, modified, folder));
            }
        }
        catch (Exception ex) when (token.IsCancellationRequested)
        {
            Log.Debug($"File search cancelled: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Windows Search is off (the WSearch service is disabled) or not installed.
            s_unavailableUntil = DateTime.UtcNow + RetryAfter;
            Log.Warn($"File search unavailable: {ex.Message}");
        }
        token.ThrowIfCancellationRequested();
        return hits;
    }
}
