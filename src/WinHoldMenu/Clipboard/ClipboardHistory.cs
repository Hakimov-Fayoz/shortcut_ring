namespace WinHoldMenu.Clipboard;

internal sealed class ClipboardHistory
{
    private const int MaxTextLength = 20000;

    private readonly int _capacity;
    private readonly List<string> _texts = new();

    public ClipboardHistory(int capacity) => _capacity = capacity;

    public IReadOnlyList<string> Texts => _texts;

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];

        _texts.Remove(text);
        _texts.Insert(0, text);

        if (_texts.Count > _capacity) _texts.RemoveAt(_texts.Count - 1);
    }
}
