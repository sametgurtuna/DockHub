using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomDock.Core;

/// <summary>
/// A shareable look (.dockhub-theme): colors, glass, size and shape. Items, widgets and the screen edge are not
/// included (an edge in older files is ignored), so importing a theme never moves the dock. Importing is one undoable step.
/// </summary>
public static class ThemeFile
{
    public const string Extension = ".dockhub-theme";
    private const string Format = "dockhub-theme";

    private static readonly string[] Properties =
    {
        nameof(AppConfig.Theme), nameof(AppConfig.Backdrop), nameof(AppConfig.TintOpacity), nameof(AppConfig.Size),
        nameof(AppConfig.Layout), nameof(AppConfig.WidthMode), nameof(AppConfig.Alignment), nameof(AppConfig.EdgeMargin),
        nameof(AppConfig.HoverEffect), nameof(AppConfig.RunningIndicator), nameof(AppConfig.AlignWidgetWidths),
    };

    public static void Export(AppConfig config, string path)
    {
        var appearance = new JsonObject();
        foreach (var name in Properties)
        {
            var property = typeof(AppConfig).GetProperty(name)!;
            appearance[name] = JsonSerializer.SerializeToNode(property.GetValue(config), property.PropertyType, JsonStore.Options);
        }
        var root = new JsonObject
        {
            ["format"] = Format,
            ["version"] = 1,
            ["name"] = Path.GetFileNameWithoutExtension(path),
            ["appearance"] = appearance,
        };
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Applies a theme file. Returns an error message, or null on success.</summary>
    public static string? Import(ConfigService service, string path)
    {
        JsonObject? appearance;
        string name;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root?["format"]?.GetValue<string>() != Format) return L.T("This is not a DockHub theme file.");
            appearance = root["appearance"] as JsonObject;
            name = root["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception ex)
        {
            return L.T("The theme file could not be read: {0}", ex.Message);
        }
        if (appearance is null) return L.T("This is not a DockHub theme file.");

        // Only known look settings are taken over; anything else in the file is ignored.
        var filtered = new JsonObject();
        foreach (var property in Properties)
            if (appearance[property] is { } value) filtered[property] = value.DeepClone();

        service.History.Push(service.Config, L.T("Applied the {0} theme", name), includeAppearance: true);
        ConfigHistory.RestoreAppearance(service.Config, filtered);
        Log.Info($"Theme imported: {name}");
        return null;
    }
}
