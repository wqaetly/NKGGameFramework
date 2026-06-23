namespace NKGGameFramework.Core;

public abstract class Module
{
    public virtual int Priority => 0;

    public bool IsInitialized { get; private set; }

    public void Initialize(IRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsInitialized)
        {
            return;
        }

        OnInitialize(context);
        IsInitialized = true;
    }

    public void Shutdown()
    {
        if (!IsInitialized)
        {
            return;
        }

        OnShutdown();
        IsInitialized = false;
    }

    protected virtual void OnInitialize(IRuntimeContext context)
    {
    }

    protected virtual void OnShutdown()
    {
    }
}

public interface IUpdateModule
{
    void Update(double deltaTime, double realDeltaTime);
}

