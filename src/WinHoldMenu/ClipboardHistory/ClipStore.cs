namespace WinHoldMenu.ClipboardHistory;

/// <summary>Last N distinct clipboard texts, newest first.</summary>
internal sealed class ClipStore
{
    private const int MaxLen = 20000;
    private readonly int _cap;
    private readonly List<string> _items = new();

    public ClipStore(int capacity) => _cap = capacity;

    public IReadOnlyList<string> Items => _items;

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > MaxLen) text = text[..MaxLen];
        _items.Remove(text);
        _items.Insert(0, text);
        if (_items.Count > _cap) _items.RemoveAt(_items.Count - 1);
    }
}
