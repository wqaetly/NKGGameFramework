using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NKGGameFramework.Runtime;

public interface IViewModel : INotifyPropertyChanged
{
}

public abstract class ViewModelBase : IViewModel, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual void Dispose()
    {
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public interface IViewModelHost
{
    object? BindingContext { get; set; }
}

public interface IBindingTarget<T>
{
    T Value { get; set; }

    event Action<T>? ValueChanged;
}

public sealed class BindableValue<T> : IBindingTarget<T>, INotifyPropertyChanged
{
    private T _value = default!;

    public BindableValue()
    {
    }

    public BindableValue(T value)
    {
        _value = value;
    }

    public event Action<T>? ValueChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
            {
                return;
            }

            _value = value;
            ValueChanged?.Invoke(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}

public enum BindingMode
{
    OneTime,
    OneWay,
    TwoWay,
}

public sealed class BindingSet : IDisposable
{
    private readonly List<IDisposable> _bindings = [];
    private bool _disposed;

    public IDisposable Bind<T>(
        INotifyPropertyChanged source,
        string propertyName,
        Func<T> getSourceValue,
        IBindingTarget<T> target,
        BindingMode mode = BindingMode.OneWay,
        Action<T>? setSourceValue = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(getSourceValue);
        ArgumentNullException.ThrowIfNull(target);

        var binding = new PropertyBinding<T>(source, propertyName, getSourceValue, target, mode, setSourceValue);
        _bindings.Add(binding);
        return new BindingHandle(() =>
        {
            if (_bindings.Remove(binding))
            {
                binding.Dispose();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (var i = _bindings.Count - 1; i >= 0; i--)
        {
            _bindings[i].Dispose();
        }

        _bindings.Clear();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BindingSet));
        }
    }

    private sealed class BindingHandle(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dispose();
        }
    }

    private sealed class PropertyBinding<T> : IDisposable
    {
        private readonly INotifyPropertyChanged _source;
        private readonly string _propertyName;
        private readonly Func<T> _getSourceValue;
        private readonly IBindingTarget<T> _target;
        private readonly BindingMode _mode;
        private readonly Action<T>? _setSourceValue;
        private bool _disposed;
        private bool _updating;

        public PropertyBinding(
            INotifyPropertyChanged source,
            string propertyName,
            Func<T> getSourceValue,
            IBindingTarget<T> target,
            BindingMode mode,
            Action<T>? setSourceValue)
        {
            if (mode == BindingMode.TwoWay && setSourceValue is null)
            {
                throw new ArgumentException("Two-way binding requires a source setter.", nameof(setSourceValue));
            }

            _source = source;
            _propertyName = propertyName;
            _getSourceValue = getSourceValue;
            _target = target;
            _mode = mode;
            _setSourceValue = setSourceValue;

            UpdateTarget();

            if (_mode != BindingMode.OneTime)
            {
                _source.PropertyChanged += OnSourcePropertyChanged;
            }

            if (_mode == BindingMode.TwoWay)
            {
                _target.ValueChanged += OnTargetValueChanged;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _source.PropertyChanged -= OnSourcePropertyChanged;
            _target.ValueChanged -= OnTargetValueChanged;
            _disposed = true;
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == _propertyName)
            {
                UpdateTarget();
            }
        }

        private void OnTargetValueChanged(T value)
        {
            if (_updating)
            {
                return;
            }

            _updating = true;
            try
            {
                _setSourceValue?.Invoke(value);
            }
            finally
            {
                _updating = false;
            }
        }

        private void UpdateTarget()
        {
            if (_updating)
            {
                return;
            }

            _updating = true;
            try
            {
                _target.Value = _getSourceValue();
            }
            finally
            {
                _updating = false;
            }
        }
    }
}
