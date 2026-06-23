using Cysharp.Threading.Tasks;

namespace NKGGameFramework.Runtime;

public interface IConfigService
{
    UniTask<TConfig> LoadAsync<TConfig>(string key, CancellationToken cancellationToken = default)
        where TConfig : class;
}
