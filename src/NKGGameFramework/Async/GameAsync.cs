using Cysharp.Threading.Tasks;

namespace NKGGameFramework.Async;

public static class GameAsync
{
    public static UniTask CompletedTask => UniTask.CompletedTask;

    public static UniTask<T> FromResult<T>(T value) => UniTask.FromResult(value);

    public static UniTask FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return UniTask.FromException(exception);
    }

    public static UniTask<T> FromException<T>(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return UniTask.FromException<T>(exception);
    }

    public static UniTask FromCanceled(CancellationToken cancellationToken) => UniTask.FromCanceled(cancellationToken);

    public static UniTask<T> FromCanceled<T>(CancellationToken cancellationToken) => UniTask.FromCanceled<T>(cancellationToken);

    public static UniTask WhenAll(params UniTask[] tasks) => UniTask.WhenAll(tasks);

    public static UniTask<T[]> WhenAll<T>(params UniTask<T>[] tasks) => UniTask.WhenAll(tasks);

    public static UniTask<int> WhenAny(params UniTask[] tasks) => UniTask.WhenAny(tasks);
}
