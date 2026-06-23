using Cysharp.Threading.Tasks;

namespace NKGGameFramework.Runtime;

public interface IAudioHandle : IDisposable
{
    string Location { get; }
}

public interface IAudioService
{
    UniTask<IAudioHandle> PlayAsync(string location, AudioPlaybackOptions options = default, CancellationToken cancellationToken = default);
}

public readonly record struct AudioPlaybackOptions(float Volume = 1f, bool Loop = false);
