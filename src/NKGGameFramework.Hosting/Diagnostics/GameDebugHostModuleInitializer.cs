using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;

namespace NKGGameFramework.Hosting.Diagnostics;

internal static class GameDebugHostModuleInitializer
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        if (!GameDebugHostAutoStart.IsEnabled)
        {
            return;
        }

        StartAutoAsync().Forget();
    }

    private static async UniTaskVoid StartAutoAsync()
    {
        try
        {
            await GameDebugHostAutoStart.TryStartFromEnvironmentAsync().ConfigureAwait(false);
        }
        catch
        {
            // Auto-start must never fail assembly load. Explicit StartAsync surfaces errors to callers.
        }
    }
}
