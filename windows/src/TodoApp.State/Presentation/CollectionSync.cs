using System.Collections.ObjectModel;

namespace TodoApp.State;

public static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="target"/> equal to <paramref name="source"/> with as few notifications as it takes:
    /// items whose key is gone are removed, new ones inserted, moved ones moved, and an item with the same key but
    /// different content is replaced. An item equal to its counterpart is left alone, so its UI element survives.
    /// </summary>
    public static void Sync<T, TKey>(ObservableCollection<T> target, IReadOnlyList<T> source, Func<T, TKey> key)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);

        var wanted = new HashSet<TKey>(source.Select(key));
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(key(target[i]))) target.RemoveAt(i);
        }

        for (var i = 0; i < source.Count; i++)
        {
            var incoming = source[i];
            var incomingKey = key(incoming);
            if (i < target.Count && EqualityComparer<TKey>.Default.Equals(key(target[i]), incomingKey))
            {
                Refresh(target, i, incoming);
                continue;
            }

            var from = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (EqualityComparer<TKey>.Default.Equals(key(target[j]), incomingKey))
                {
                    from = j;
                    break;
                }
            }

            if (from >= 0)
            {
                target.Move(from, i);
                Refresh(target, i, incoming);
            }
            else
            {
                target.Insert(i, incoming);
            }
        }

        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }

    private static void Refresh<T>(ObservableCollection<T> target, int index, T incoming)
    {
        if (!EqualityComparer<T>.Default.Equals(target[index], incoming)) target[index] = incoming;
    }
}
