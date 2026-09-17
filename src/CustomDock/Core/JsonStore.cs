using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomDock.Core;

/// <summary>JSON dosyalarını atomik olarak okur/yazar.</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"JSON okunamadı: {path}");
            TryBackupCorrupt(path);
        }
        return new T();
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"JSON yazılamadı: {path}");
        }
    }

    public static string DataPath(string name) => Path.Combine(AppPaths.DataDir, name + ".json");

    public static T LoadData<T>(string name) where T : new() => Load<T>(DataPath(name));

    public static void SaveData<T>(string name, T value) => Save(DataPath(name), value);

    private static void TryBackupCorrupt(string path)
    {
        try
        {
            File.Copy(path, $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
        }
        catch
        {
            // yoksay
        }
    }
}
