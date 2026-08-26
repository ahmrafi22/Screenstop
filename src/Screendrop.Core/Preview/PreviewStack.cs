namespace Screendrop.Core.Preview;

public sealed class PreviewStack
{
    private readonly int _maxCount;
    private readonly List<PreviewEntry> _items = new();

    public PreviewStack(int maxCount = 6)
    {
        _maxCount = Math.Max(1, maxCount);
    }

    public event Action? Changed;

    /// Raised when an entry is evicted to make room for a newer one. The
    /// evicted entry has already been removed from <see cref="Items"/>.
    public event Action<PreviewEntry>? Evicted;

    public IReadOnlyList<PreviewEntry> Items => _items;

    public PreviewEntry? Newest => _items.Count > 0 ? _items[0] : null;

    public void Push(PreviewEntry entry)
    {
        _items.Insert(0, entry);
        if (_items.Count > _maxCount)
        {
            var evicted = _items[_items.Count - 1];
            _items.RemoveAt(_items.Count - 1);
            Evicted?.Invoke(evicted);
        }

        Changed?.Invoke();
    }

    public bool Remove(PreviewEntry entry)
    {
        bool removed = _items.Remove(entry);
        if (removed)
        {
            Changed?.Invoke();
        }

        return removed;
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Changed?.Invoke();
    }
}
