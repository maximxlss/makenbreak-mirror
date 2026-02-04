using System;
using System.Collections.Generic;

public interface INotifyDictionaryChanged<out TKey>
{
    event Action<TKey> Changed;
}

public interface IReadOnlyDictionary<TKey, TValue>
{
    TValue this[TKey key] { get; }
    public bool ContainsKey(TKey key);
    public bool TryGetValue(TKey key, out TValue value);
}

public interface IReadOnlyObservableDictionary<TKey, TValue> : INotifyDictionaryChanged<TKey>,
    IReadOnlyDictionary<TKey, TValue> {}


public class ObservableDictionary<TKey, TValue> : IReadOnlyObservableDictionary<TKey, TValue>
{
    private readonly Dictionary<TKey, TValue> _dictionary = new();
    public event Action<TKey> Changed;

    // implement the actual dictionary functionality as needed
    // Mutations (don't forget the callback):
    public TValue this[TKey key]
    {
        get => _dictionary[key];
        set
        {
            if (_dictionary.TryGetValue(key, out var existing) &&
                EqualityComparer<TValue>.Default.Equals(existing, value)) return;
            _dictionary[key] = value;
            Changed?.Invoke(key);
        }
    }
    public bool Remove(TKey key)
    {
        var result = _dictionary.Remove(key);
        if (result) Changed?.Invoke(key);
        return result;
    }

    // Fetches (don't forget to add to IReadOnlyDictionary)
    public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);
}