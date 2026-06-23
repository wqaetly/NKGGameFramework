namespace NKGGameFramework.Runtime;

public interface ILocalizationService
{
    string CurrentCulture { get; }

    string GetText(string key);
}

