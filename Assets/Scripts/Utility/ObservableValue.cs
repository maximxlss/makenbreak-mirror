using System;

public interface IReadOnlyObservable<out T>
{
    T Value { get; }
    event Action<T> Changed;
}

public class ObservableValue<T> : IReadOnlyObservable<T>
{
    private T _value;
    public event Action<T> Changed;

    public ObservableValue(T initial = default) => _value = initial;

    public T Value
    {
        get => _value;
        set
        {
            if (Equals(_value, value)) return;
            _value = value;
            Changed?.Invoke(_value);
        }
    }

    public void Bind(Action<T> listener, bool fireImmediately = false)
    {
        Changed += listener;
        if (fireImmediately) listener(_value);
    }

    public void Unbind(Action<T> listener) => Changed -= listener;
}
