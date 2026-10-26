using System.Text.Encodings.Web;
using System.Text.Json;

namespace WinHoldMenu;

/// <summary>Portable settings: settings.json lives next to the exe.</summary>
internal sealed class Settings
{
    public int HoldMs = 150;                 // how long Win must be held
    public bool TriggerOnHover = true;       // true: act on hover, false: act when Win is released
    public int HoverDwellMs = 180;           // hover time before action (hover mode)
    public int HistorySize = 5;              // last N clipboard items
    public int MaxWindows = 8;               // windows shown on the right
    public string[] Pinned =                 // pre-written snippets (shown first)
    {
        "Здравствуйте!",
        "Спасибо, с уважением",
        "me@example.com",
    };

    public static string FilePath => System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (File.Exists(FilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                var r = doc.RootElement;
                s.HoldMs = GetInt(r, "holdMs", s.HoldMs);
                s.HoverDwellMs = GetInt(r, "hoverDwellMs", s.HoverDwellMs);
                s.HistorySize = GetInt(r, "historySize", s.HistorySize);
                s.MaxWindows = GetInt(r, "maxWindows", s.MaxWindows);

                if (r.TryGetProperty("trigger", out JsonElement t) && t.ValueKind == JsonValueKind.String)
                    s.TriggerOnHover = !string.Equals(t.GetString(), "release", StringComparison.OrdinalIgnoreCase);

                if (r.TryGetProperty("pinned", out JsonElement p) && p.ValueKind == JsonValueKind.Array)
                {
                    s.Pinned = p.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString() ?? "")
                        .Where(x => x.Length > 0)
                        .ToArray();
                }
            }
            else
            {
                s.Save();
            }
        }
        catch
        {
            // broken file -> defaults
        }

        s.HoldMs = Math.Clamp(s.HoldMs, 50, 2000);
        s.HoverDwellMs = Math.Clamp(s.HoverDwellMs, 0, 2000);
        s.HistorySize = Math.Clamp(s.HistorySize, 1, 10);
        s.MaxWindows = Math.Clamp(s.MaxWindows, 1, 12);
        if (s.Pinned.Length > 5) s.Pinned = s.Pinned.Take(5).ToArray();
        return s;
    }

    private static int GetInt(JsonElement root, string name, int def) =>
        root.TryGetProperty(name, out JsonElement v) && v.TryGetInt32(out int i) ? i : def;

    public void Save()
    {
        using var fs = File.Create(FilePath);
        using var w = new Utf8JsonWriter(fs, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        w.WriteStartObject();
        w.WriteNumber("holdMs", HoldMs);
        w.WriteString("trigger", TriggerOnHover ? "hover" : "release");
        w.WriteNumber("hoverDwellMs", HoverDwellMs);
        w.WriteNumber("historySize", HistorySize);
        w.WriteNumber("maxWindows", MaxWindows);
        w.WriteStartArray("pinned");
        foreach (var p in Pinned) w.WriteStringValue(p);
        w.WriteEndArray();
        w.WriteEndObject();
    }
}
