using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

public interface INotifyDictionaryChanged<out TKey>
{
    event Action<TKey> Changed;
}

public interface IReadOnlyDictionary<TKey, TValue>
{
    TValue this[TKey key] { get; }
    IEnumerable<KeyValuePair<TKey, TValue>> Pairs { get; }
    int Count { get; }
    public bool ContainsKey(TKey key);
    public bool TryGetValue(TKey key, out TValue value);
}

public interface IReadOnlyObservableDictionary<TKey, TValue> : INotifyDictionaryChanged<TKey>,
    IReadOnlyDictionary<TKey, TValue> {}

[Serializable]
[GenerateSerializationForGenericParameter(0)]
[GenerateSerializationForGenericParameter(1)]
public class NetworkDictionary<TKey, TValue> : NetworkVariableBase, IReadOnlyObservableDictionary<TKey, TValue>
{
    private readonly Dictionary<TKey, TValue> _dictionary = new();
    private readonly List<TKey> dirtyKeys = new();
    public event Action<TKey> Changed;

    // implement the actual dictionary functionality as needed
    // Mutations (don't forget the callback):
    private void AssertCanWrite() {
        if (!CanClientWrite(GetBehaviour().NetworkManager.LocalClientId)) {
            throw new("Don't have permission to write the value");
        }
    }
    public TValue this[TKey key] {
        get => _dictionary[key];
        set {
            AssertCanWrite();
            SetAndNotifyUnchecked(key, value);
        }
    }
    public bool Remove(TKey key) {
        AssertCanWrite();
        return RemoveAndNotifyUnchecked(key);
    }
    public void Clear() {
        var keys = _dictionary.Keys.ToArray();
        foreach (var key in keys) {
            Remove(key);
        }
    }
    private bool RemoveAndNotifyUnchecked(TKey key, bool markDirty = true)
    {
        if (!_dictionary.Remove(key)) {
            return false;
        }

        if (markDirty)
        {
            dirtyKeys.Add(key);
            SetDirty(true);
        }

        Changed?.Invoke(key);
        return true;
    }
    private void SetAndNotifyUnchecked(TKey key, TValue value, bool markDirty = true) {
        if (_dictionary.TryGetValue(key, out var existing) &&
            EqualityComparer<TValue>.Default.Equals(existing, value)) return;
        _dictionary[key] = value;
        if (markDirty)
        {
            dirtyKeys.Add(key);
            SetDirty(true);
        }

        Changed?.Invoke(key);
    }

    // Fetches (don't forget to add to IReadOnlyDictionary)
    public IEnumerable<KeyValuePair<TKey, TValue>> Pairs => _dictionary;
    public int Count => _dictionary.Count;
    public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);
    
    ~NetworkDictionary()
    {
        Dispose();
    }
    public override void ResetDirty()
    {
        base.ResetDirty();
        dirtyKeys.Clear();
    }
    public override bool IsDirty()
    {
        return base.IsDirty() || dirtyKeys.Count > 0;
    }
    public override void WriteField(FastBufferWriter writer) {
        writer.WriteValueSafe(_dictionary.Count);
        foreach (var pair in _dictionary) {
            var key = pair.Key;
            NetworkVariableSerialization<TKey>.Write(writer, ref key);
            var value = pair.Value;
            NetworkVariableSerialization<TValue>.Write(writer, ref value);
        }
    }
    public override void ReadField(FastBufferReader reader) {
        var removedKeys = _dictionary.Keys.ToArray();
        _dictionary.Clear();
        dirtyKeys.Clear();
        for (int i = 0; i < removedKeys.Length; i++)
        {
            Changed?.Invoke(removedKeys[i]);
        }

        reader.ReadValueSafe(out int itemsToUpdate);
        for (int i = 0; i < itemsToUpdate; i++) {
            TKey newKey = default;
            NetworkVariableSerialization<TKey>.Read(reader, ref newKey);
            TValue newValue = default;
            NetworkVariableSerialization<TValue>.Read(reader, ref newValue);
            SetAndNotifyUnchecked(newKey, newValue, false);
        }

        ResetDirty();
    }
    
    public override void WriteDelta(FastBufferWriter writer) {
        var keysToUpdate = 0;
        var keysToRemove = 0;
        foreach (var key in dirtyKeys) {
            if (ContainsKey(key)) {
                keysToUpdate += 1;
            }
            else {
                keysToRemove += 1;
            }
        }

        writer.WriteValueSafe(keysToUpdate);
        foreach (var key in dirtyKeys) {
            if (!ContainsKey(key)) {
                continue;
            }
            var key1 = key;
            NetworkVariableSerialization<TKey>.Write(writer, ref key1);
            var value = this[key];
            NetworkVariableSerialization<TValue>.Write(writer, ref value);
        }
        
        writer.WriteValueSafe(keysToRemove);
        foreach (var key in dirtyKeys) {
            if (ContainsKey(key)) {
                continue;
            }
            var key1 = key;
            NetworkVariableSerialization<TKey>.Write(writer, ref key1);
        }
    }

    public override void ReadDelta(FastBufferReader reader, bool keepDirtyDelta) {
        reader.ReadValueSafe(out int itemsToUpdate);
        for (int i = 0; i < itemsToUpdate; i++)
        {
            TKey newKey = default;
            NetworkVariableSerialization<TKey>.Read(reader, ref newKey);
            TValue newValue = default;
            NetworkVariableSerialization<TValue>.Read(reader, ref newValue);
            SetAndNotifyUnchecked(newKey, newValue, false);
        }
        
        reader.ReadValueSafe(out int itemsToRemove);
        for (int i = 0; i < itemsToRemove; i++)
        {
            TKey newKey = default;
            NetworkVariableSerialization<TKey>.Read(reader, ref newKey);
            RemoveAndNotifyUnchecked(newKey, false);
        }

        if (keepDirtyDelta) {
            return;
        }

        dirtyKeys.Clear();
        ResetDirty();
    }
}
