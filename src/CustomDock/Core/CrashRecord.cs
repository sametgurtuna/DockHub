using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomDock.Core;

/// <summary>
/// What Windows recorded about a DockHub crash in the Application event log, reduced to what is safe to share: the
/// faulting module, exception code and offset, the .NET exception type and the first stack frames. Exception messages,
/// file paths and process details are left out because they can contain personal data.
/// </summary>
public sealed class CrashRecord
{
    public const int MaxStackFrames = 5;
    private const int MaxFrameLength = 200;

    public DateTime Time { get; set; }

    /// <summary>"Application Error" or ".NET Runtime".</summary>
    public string Source { get; set; } = "";

    public string? AppVersion { get; set; }
    public string? RuntimeVersion { get; set; }
    public string? Module { get; set; }
    public string? ModuleVersion { get; set; }

    /// <summary>Lower-case hex without 0x, such as "c0000005".</summary>
    public string? ExceptionCode { get; set; }

    /// <summary>"0x1d45dc": hex with the leading zeros removed.</summary>
    public string? Offset { get; set; }

    public string? ExceptionType { get; set; }

    /// <summary>The runtime's own description, such as "internal error in the .NET Runtime" (no user data).</summary>
    public string? Description { get; set; }

    public List<string> Stack { get; set; } = new();

    public const string ApplicationErrorSource = "Application Error";
    public const string RuntimeSource = ".NET Runtime";

    /// <summary>
    /// Event 1000 from its event data, which is the same in every Windows language: application, version, time stamp,
    /// module, module version, module time stamp, exception code, offset, process id... Null when it isn't DockHub's.
    /// </summary>
    public static CrashRecord? FromApplicationError(IReadOnlyList<string?> data, DateTime time, string executable = "DockHub.exe")
    {
        if (data.Count < 8 || !string.Equals(data[0]?.Trim(), executable, StringComparison.OrdinalIgnoreCase)) return null;
        return new CrashRecord
        {
            Time = time,
            Source = ApplicationErrorSource,
            AppVersion = Clean(data[1]),
            Module = Clean(data[3]),
            ModuleVersion = Clean(data[4]),
            ExceptionCode = Hex(data[6], withPrefix: false),
            Offset = Hex(data[7], withPrefix: true),
        };
    }

    /// <summary>
    /// A crash message as the event log shows it: the English "Application Error" text or the ".NET Runtime" text
    /// (1023, 1025, 1026), which the runtime always writes in English. Null when it isn't about <paramref name="executable"/>.
    /// </summary>
    public static CrashRecord? Parse(string? message, DateTime time, string executable = "DockHub.exe")
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var lines = message.Replace("\r\n", "\n").Split('\n');

        if (Field(lines, "Faulting application name") is { } faulting)
        {
            // "Faulting application name: DockHub.exe, version: 0.9.1.0, time stamp: 0x68d9f0a1"
            var app = Regex.Match(faulting, @"^(?<name>[^,]+),\s*version:\s*(?<version>[^,]+)");
            if (!app.Success || !Is(app.Groups["name"].Value, executable)) return null;
            var module = Regex.Match(Field(lines, "Faulting module name") ?? "", @"^(?<name>[^,]+),\s*version:\s*(?<version>[^,]+)");
            return new CrashRecord
            {
                Time = time,
                Source = ApplicationErrorSource,
                AppVersion = Clean(app.Groups["version"].Value),
                Module = module.Success ? Clean(module.Groups["name"].Value) : null,
                ModuleVersion = module.Success ? Clean(module.Groups["version"].Value) : null,
                ExceptionCode = Hex(Field(lines, "Exception code"), withPrefix: false),
                Offset = Hex(Field(lines, "Fault offset"), withPrefix: true),
            };
        }

        if (Field(lines, "Application") is not { } application || !Is(application, executable)) return null;
        var record = new CrashRecord
        {
            Time = time,
            Source = RuntimeSource,
            RuntimeVersion = Clean(Field(lines, ".NET Version") ?? Field(lines, "Framework Version")),
            Description = RuntimeDescription(Field(lines, "Description")),
        };
        if (Field(lines, "Exception Info") is { } info)
            record.ExceptionType = ExceptionTypeOf(info);
        record.Stack = lines
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("at ", StringComparison.Ordinal))
            .Select(Frame)
            .Where(f => f.Length > 0)
            .Take(MaxStackFrames)
            .ToList();
        return record;
    }

    /// <summary>Joins the records Windows wrote for one crash (usually one of each source) into one.</summary>
    public static CrashRecord? Merge(IEnumerable<CrashRecord> records)
    {
        var list = records.OrderBy(r => r.Time).ToList();
        if (list.Count == 0) return null;
        var native = list.FirstOrDefault(r => r.Source == ApplicationErrorSource);
        var managed = list.FirstOrDefault(r => r.Source == RuntimeSource);
        var first = native ?? managed ?? list[0];
        return new CrashRecord
        {
            Time = list[0].Time,
            Source = string.Join(" + ", list.Select(r => r.Source).Distinct()),
            AppVersion = native?.AppVersion ?? first.AppVersion,
            RuntimeVersion = managed?.RuntimeVersion,
            Module = native?.Module,
            ModuleVersion = native?.ModuleVersion,
            ExceptionCode = native?.ExceptionCode,
            Offset = native?.Offset,
            ExceptionType = managed?.ExceptionType,
            Description = managed?.Description,
            Stack = managed?.Stack ?? new(),
        };
    }

    /// <summary>One line: "coreclr.dll c0000005 at 0x1d45dc" and/or "System.InvalidOperationException at Type.Method()".</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (Module is not null)
            parts.Add(string.Join(" ", new[] { Module, ExceptionCode, Offset is null ? null : "at " + Offset }.Where(p => p is not null)));
        if (ExceptionType is not null)
            parts.Add(Stack.Count > 0 ? $"{ExceptionType} at {Stack[0]}" : ExceptionType);
        if (parts.Count == 0 && Description is not null)
            parts.Add(Description);
        return parts.Count == 0 ? "no details" : string.Join("; ", parts);
    }

    /// <summary>"28.09.2026 14:02 · DockHub 0.9.1.0 · coreclr.dll c0000005 at 0x1d45dc" for Settings and diagnostics.</summary>
    public string OneLine() => $"{Time:g} · DockHub {AppVersion ?? "?"} · {Summary()}";

    /// <summary>The text for a bug report's "crash" field.</summary>
    public string Report()
    {
        var sb = new StringBuilder();
        sb.Append("DockHub ").Append(AppVersion ?? "?");
        if (RuntimeVersion is not null) sb.Append(", .NET ").Append(RuntimeVersion);
        sb.Append(", ").AppendLine(Time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        if (Module is not null) sb.Append("Module: ").Append(Module).AppendLine(ModuleVersion is null ? "" : " " + ModuleVersion);
        if (ExceptionCode is not null || Offset is not null)
            sb.Append("Exception code: ").Append(ExceptionCode ?? "?").AppendLine(Offset is null ? "" : " at " + Offset);
        if (Description is not null) sb.Append("Description: ").AppendLine(Description);
        if (ExceptionType is not null) sb.Append("Exception: ").AppendLine(ExceptionType);
        foreach (var frame in Stack) sb.Append("   at ").AppendLine(frame);
        return sb.ToString().TrimEnd();
    }

    private static string? Field(string[] lines, string name)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                return trimmed[(name.Length + 1)..].Trim();
        }
        return null;
    }

    private static bool Is(string name, string executable)
        => string.Equals(Path.GetFileName(name.Trim()), executable, StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string v = value.Trim();
        return v.Length > 64 ? v[..64] : v;
    }

    /// <summary>"0x00000000001d45dc" or "c0000005" → "0x1d45dc" / "c0000005"; anything that isn't hex → null.</summary>
    private static string? Hex(string? value, bool withPrefix)
    {
        if (value is null) return null;
        string v = value.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v[2..];
        if (v.Length == 0 || v.Length > 16 || !v.All(Uri.IsHexDigit)) return null;
        v = v.ToLowerInvariant();
        if (withPrefix)
        {
            v = v.TrimStart('0');
            return "0x" + (v.Length == 0 ? "0" : v);
        }
        return v;
    }

    /// <summary>"System.IO.IOException: The file C:\Users\... is locked" → "System.IO.IOException" (the message is dropped).</summary>
    private static string? ExceptionTypeOf(string info)
    {
        var match = Regex.Match(info, @"^(?<type>[A-Za-z_][\w.`+\[\],]*)(:|$)");
        return match.Success ? match.Groups["type"].Value : null;
    }

    /// <summary>"at Type.Method(Args) in C:\src\File.cs:line 12" → "Type.Method(Args)".</summary>
    private static string Frame(string line)
    {
        string frame = line[3..].Trim();
        int source = frame.IndexOf(" in ", StringComparison.Ordinal);
        if (source >= 0) frame = frame[..source];
        return frame.Length > MaxFrameLength ? frame[..MaxFrameLength] : frame;
    }

    /// <summary>Only the runtime's fixed wording, never text that could come from the app.</summary>
    private static string? RuntimeDescription(string? description)
    {
        if (description is null) return null;
        if (description.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase)) return "unhandled exception";
        if (description.Contains("FailFast", StringComparison.OrdinalIgnoreCase)) return "Environment.FailFast";
        if (description.Contains("internal error in the .NET Runtime", StringComparison.OrdinalIgnoreCase))
        {
            var code = Regex.Match(description, @"exit code (?<code>[0-9A-Fa-f]+)");
            return code.Success ? $"internal error in the .NET Runtime (exit code {code.Groups["code"].Value.ToLowerInvariant()})" : "internal error in the .NET Runtime";
        }
        if (description.Contains("stack overflow", StringComparison.OrdinalIgnoreCase)) return "stack overflow";
        return "other";
    }
}
