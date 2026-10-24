using System.Collections;

namespace TodoCore;

/// <summary>An immutable list with structural equality, so records holding lists compare by content.</summary>
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    public static ValueList<T> Empty { get; } = new(Array.Empty<T>());

    public ValueList(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
    }

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public ValueList<T> Add(T item)
    {
        var copy = new T[_items.Length + 1];
        _items.CopyTo(copy, 0);
        copy[^1] = item;
        return new ValueList<T>(copy);
    }

    public ValueList<T> AddRange(IEnumerable<T> items) => new(_items.Concat(items));

    public ValueList<T> SetItem(int index, T item)
    {
        if ((uint)index >= (uint)_items.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var copy = (T[])_items.Clone();
        copy[index] = item;
        return new ValueList<T>(copy);
    }

    public ValueList<T> RemoveAll(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return new ValueList<T>(_items.Where(item => !match(item)));
    }

    public int FindIndex(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return Array.FindIndex(_items, match);
    }

    public bool Equals(ValueList<T>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _items.AsSpan().SequenceEqual(other._items);
    }

    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items) hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class ValueList
{
    public static ValueList<T> Of<T>(params T[] items) => new(items);
}
