using System.Runtime.CompilerServices;

namespace CustomDock.Tests;

/// <summary>Points DockHub's data folder at a throwaway directory before any test touches AppPaths.</summary>
internal static class TestEnvironment
{
    public static readonly string Home = Path.Combine(Path.GetTempPath(), "dockhub-tests-" + Guid.NewGuid().ToString("N")[..8]);

    [ModuleInitializer]
    public static void Initialize()
    {
        Directory.CreateDirectory(Home);
        Environment.SetEnvironmentVariable("DOCKHUB_HOME", Home);
    }

    public static string Fixture(string relativePath)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath));

    /// <summary>The repository checkout the tests were built from (the folder holding CustomDock.sln).</summary>
    public static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CustomDock.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("CustomDock.sln not found above the test folder");
        }
    }
}

/// <summary>Tests that read or write config.json share one file, so they must not run in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConfigFileCollection
{
    public const string Name = "config.json";
}

/// <summary>A temporary directory deleted after the test.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dockhub-t-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string relative, string content = "")
    {
        string full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
