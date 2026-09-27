namespace CustomDock.Core;

/// <summary>
/// File paths handed from a second DockHub process (started by File Explorer) to the running one: a text file in
/// %AppData%\DockHub guarded by a named mutex. The second process appends and signals; the running one takes all.
/// </summary>
public sealed class RequestQueue
{
    /// <summary>"Pin to DockHub" from File Explorer.</summary>
    public static readonly RequestQueue Pins = new("pin-requests.txt", @"Local\DockHub.PinQueue.Mutex");

    /// <summary>Opened .dockwidget packages waiting to be installed.</summary>
    public static readonly RequestQueue WidgetPackages = new("widget-requests.txt", @"Local\DockHub.WidgetQueue.Mutex");

    private readonly string _fileName;
    private readonly string _mutexName;

    public RequestQueue(string fileName, string mutexName)
    {
        _fileName = fileName;
        _mutexName = mutexName;
    }

    private string QueueFile => Path.Combine(AppPaths.Root, _fileName);

    public void Enqueue(string path)
    {
        try
        {
            WithLock(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(QueueFile)!);
                File.AppendAllLines(QueueFile, new[] { Path.GetFullPath(path) });
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to write {_fileName}");
        }
    }

    /// <summary>Takes every queued path (each once) and clears the queue.</summary>
    public List<string> Dequeue()
    {
        var lines = new List<string>();
        try
        {
            WithLock(() =>
            {
                if (!File.Exists(QueueFile)) return;
                lines.AddRange(File.ReadAllLines(QueueFile)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                File.Delete(QueueFile);
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to read {_fileName}");
        }
        return lines;
    }

    private void WithLock(Action action)
    {
        using var mutex = new Mutex(false, _mutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(3));
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }
        if (!acquired) return;
        try
        {
            action();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
