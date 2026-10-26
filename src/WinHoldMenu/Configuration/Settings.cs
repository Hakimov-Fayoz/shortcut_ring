using System.Text.Encodings.Web;
using System.Text.Json;

namespace WinHoldMenu.Configuration;

internal sealed class Settings
{
    private const int MaxPinnedTexts = 5;

    public int HoldMs = 150;
    public bool TriggerOnHover = true;
    public int HoverDwellMs = 180;
    public int HistorySize = 5;
    public int MaxWindows = 8;
    public string[] PinnedTexts =
    {
        "Здравствуйте!",
        "Спасибо, с уважением",
        "me@example.com",
    };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load()
    {
        var settings = new Settings();

        if (File.Exists(FilePath)) settings.TryReadFile();
        else settings.TrySave();

        settings.ClampToAllowedRanges();
        return settings;
    }

    public void TrySave()
    {
        try
        {
            Save();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void TryReadFile()
    {
        try
        {
            ReadFile();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    private void ReadFile()
    {
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath), options);
        JsonElement root = document.RootElement;

        HoldMs = ReadInt(root, "holdMs", HoldMs);
        HoverDwellMs = ReadInt(root, "hoverDwellMs", HoverDwellMs);
        HistorySize = ReadInt(root, "historySize", HistorySize);
        MaxWindows = ReadInt(root, "maxWindows", MaxWindows);

        if (root.TryGetProperty("trigger", out JsonElement trigger) && trigger.ValueKind == JsonValueKind.String)
            TriggerOnHover = !string.Equals(trigger.GetString(), "release", StringComparison.OrdinalIgnoreCase);

        if (root.TryGetProperty("pinned", out JsonElement pinned) && pinned.ValueKind == JsonValueKind.Array)
            PinnedTexts = ReadNonEmptyStrings(pinned);
    }

    private static int ReadInt(JsonElement root, string name, int defaultValue) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : defaultValue;

    private static string[] ReadNonEmptyStrings(JsonElement array) =>
        array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(text => text.Length > 0)
            .ToArray();

    private void ClampToAllowedRanges()
    {
        HoldMs = Math.Clamp(HoldMs, 50, 2000);
        HoverDwellMs = Math.Clamp(HoverDwellMs, 0, 2000);
        HistorySize = Math.Clamp(HistorySize, 1, 10);
        MaxWindows = Math.Clamp(MaxWindows, 1, 12);
        PinnedTexts = PinnedTexts.Take(MaxPinnedTexts).ToArray();
    }

    private void Save()
    {
        using var file = File.Create(FilePath);
        using var writer = new Utf8JsonWriter(file, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        writer.WriteStartObject();
        writer.WriteNumber("holdMs", HoldMs);
        writer.WriteString("trigger", TriggerOnHover ? "hover" : "release");
        writer.WriteNumber("hoverDwellMs", HoverDwellMs);
        writer.WriteNumber("historySize", HistorySize);
        writer.WriteNumber("maxWindows", MaxWindows);
        writer.WriteStartArray("pinned");
        foreach (string text in PinnedTexts) writer.WriteStringValue(text);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
