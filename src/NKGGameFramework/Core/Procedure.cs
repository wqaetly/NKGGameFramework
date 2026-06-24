namespace NKGGameFramework.Core;

public interface IProcedureModule
{
    bool IsProcedureInitialized { get; }

    ProcedureBase CurrentProcedure { get; }

    double CurrentProcedureTime { get; }

    IReadOnlyCollection<ProcedureBase> Procedures { get; }

    void Initialize(params ProcedureBase[] procedures);

    void StartProcedure<TProcedure>()
        where TProcedure : ProcedureBase;

    void StartProcedure(Type procedureType);

    bool HasProcedure<TProcedure>()
        where TProcedure : ProcedureBase;

    bool HasProcedure(Type procedureType);

    TProcedure GetProcedure<TProcedure>()
        where TProcedure : ProcedureBase;

    ProcedureBase GetProcedure(Type procedureType);

    bool RestartProcedure(params ProcedureBase[] procedures);

    bool TryGetCurrentProcedure(out ProcedureBase? procedure);
}

public abstract class ProcedureBase : FsmState<IProcedureModule>
{
    protected static void ChangeProcedure<TProcedure>(Fsm<IProcedureModule> procedureOwner)
        where TProcedure : ProcedureBase
    {
        procedureOwner.ChangeState<TProcedure>();
    }
}

public sealed class ProcedureModule : Module, IUpdateModule, IProcedureModule
{
    private Fsm<IProcedureModule>? _procedureFsm;

    public override int Priority => -2;

    public bool IsProcedureInitialized => _procedureFsm is not null;

    public ProcedureBase CurrentProcedure
    {
        get
        {
            EnsureInitialized();
            return (ProcedureBase)_procedureFsm!.CurrentState!;
        }
    }

    public double CurrentProcedureTime
    {
        get
        {
            EnsureInitialized();
            return _procedureFsm!.CurrentStateTime;
        }
    }

    public IReadOnlyCollection<ProcedureBase> Procedures => _procedureFsm is null
        ? Array.Empty<ProcedureBase>()
        : _procedureFsm.States.Cast<ProcedureBase>().ToArray();

    public void Initialize(params ProcedureBase[] procedures)
    {
        ArgumentNullException.ThrowIfNull(procedures);

        if (_procedureFsm is not null)
        {
            throw new FrameworkException("Procedure module is already initialized.");
        }

        _procedureFsm = new Fsm<IProcedureModule>("Procedure", this, procedures);
    }

    public void StartProcedure<TProcedure>()
        where TProcedure : ProcedureBase
    {
        EnsureInitialized();
        _procedureFsm!.Start<TProcedure>();
    }

    public void StartProcedure(Type procedureType)
    {
        EnsureInitialized();
        _procedureFsm!.Start(procedureType);
    }

    public bool HasProcedure<TProcedure>()
        where TProcedure : ProcedureBase
    {
        EnsureInitialized();
        return _procedureFsm!.HasState<TProcedure>();
    }

    public bool HasProcedure(Type procedureType)
    {
        EnsureInitialized();
        return _procedureFsm!.HasState(procedureType);
    }

    public TProcedure GetProcedure<TProcedure>()
        where TProcedure : ProcedureBase
    {
        EnsureInitialized();
        return (TProcedure)_procedureFsm!.GetState<TProcedure>();
    }

    public ProcedureBase GetProcedure(Type procedureType)
    {
        EnsureInitialized();
        return (ProcedureBase)_procedureFsm!.GetState(procedureType);
    }

    public bool RestartProcedure(params ProcedureBase[] procedures)
    {
        ArgumentNullException.ThrowIfNull(procedures);

        if (procedures.Length == 0)
        {
            throw new FrameworkException("RestartProcedure requires at least one procedure.");
        }

        ShutdownProcedureFsm();
        Initialize(procedures);
        StartProcedure(procedures[0].GetType());
        return true;
    }

    public bool TryGetCurrentProcedure(out ProcedureBase? procedure)
    {
        if (_procedureFsm?.CurrentState is ProcedureBase current)
        {
            procedure = current;
            return true;
        }

        procedure = null;
        return false;
    }

    public void Update(in GameFrameTime time)
    {
        _procedureFsm?.Update(in time);
    }

    protected override void OnShutdown()
    {
        ShutdownProcedureFsm();
    }

    private void ShutdownProcedureFsm()
    {
        _procedureFsm?.Shutdown();
        _procedureFsm = null;
    }

    private void EnsureInitialized()
    {
        if (_procedureFsm is null)
        {
            throw new FrameworkException("You must initialize procedure first.");
        }
    }
}
