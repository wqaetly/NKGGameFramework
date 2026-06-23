namespace NKGGameFramework.Gameplay;

public readonly record struct GameplayTag : IComparable<GameplayTag>
{
    public static readonly GameplayTag Empty = default;

    private readonly string? _name;

    private GameplayTag(string name)
    {
        _name = name;
    }

    public string Name => _name ?? string.Empty;

    public bool IsValid => !string.IsNullOrEmpty(_name);

    public static GameplayTag From(string tagName)
    {
        if (!TryFrom(tagName, out var tag, out var error))
        {
            throw new ArgumentException(error, nameof(tagName));
        }

        return tag;
    }

    public static bool TryFrom(string? tagName, out GameplayTag tag)
    {
        return TryFrom(tagName, out tag, out _);
    }

    public static bool TryFrom(string? tagName, out GameplayTag tag, out string? error)
    {
        tag = Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(tagName))
        {
            error = "Gameplay tag cannot be null, empty, or whitespace.";
            return false;
        }

        var normalized = tagName.Trim();
        if (normalized.StartsWith('.') || normalized.EndsWith('.') || normalized.Contains("..", StringComparison.Ordinal))
        {
            error = $"Gameplay tag '{tagName}' has an empty segment.";
            return false;
        }

        foreach (var segment in normalized.Split('.'))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                error = $"Gameplay tag '{tagName}' has an empty segment.";
                return false;
            }

            foreach (var ch in segment)
            {
                if (char.IsWhiteSpace(ch))
                {
                    error = $"Gameplay tag '{tagName}' cannot contain whitespace.";
                    return false;
                }
            }
        }

        tag = new GameplayTag(normalized);
        return true;
    }

    public bool MatchesTag(GameplayTag tagToCheck)
    {
        if (!IsValid || !tagToCheck.IsValid)
        {
            return false;
        }

        return MatchesTagExact(tagToCheck)
            || (Name.Length > tagToCheck.Name.Length
                && Name.StartsWith(tagToCheck.Name, StringComparison.Ordinal)
                && Name[tagToCheck.Name.Length] == '.');
    }

    public bool MatchesTagExact(GameplayTag tagToCheck)
    {
        return IsValid && tagToCheck.IsValid && string.Equals(Name, tagToCheck.Name, StringComparison.Ordinal);
    }

    public bool MatchesAny(GameplayTagContainer containerToCheck)
    {
        ArgumentNullException.ThrowIfNull(containerToCheck);

        foreach (var tag in containerToCheck.Tags)
        {
            if (MatchesTag(tag))
            {
                return true;
            }
        }

        return false;
    }

    public bool MatchesAnyExact(GameplayTagContainer containerToCheck)
    {
        ArgumentNullException.ThrowIfNull(containerToCheck);

        foreach (var tag in containerToCheck.Tags)
        {
            if (MatchesTagExact(tag))
            {
                return true;
            }
        }

        return false;
    }

    public int MatchesTagDepth(GameplayTag tagToCheck)
    {
        if (!MatchesTag(tagToCheck))
        {
            return 0;
        }

        var thisSegments = Name.Split('.');
        var otherSegments = tagToCheck.Name.Split('.');
        var count = Math.Min(thisSegments.Length, otherSegments.Length);
        var depth = 0;

        for (var i = 0; i < count; i++)
        {
            if (!string.Equals(thisSegments[i], otherSegments[i], StringComparison.Ordinal))
            {
                break;
            }

            depth++;
        }

        return depth;
    }

    public GameplayTag GetDirectParent()
    {
        if (!IsValid)
        {
            return Empty;
        }

        var lastDot = Name.LastIndexOf('.');
        return lastDot < 0 ? Empty : new GameplayTag(Name[..lastDot]);
    }

    public IReadOnlyList<GameplayTag> GetParentTags()
    {
        if (!IsValid)
        {
            return [];
        }

        var parents = new List<GameplayTag>();
        var current = GetDirectParent();
        while (current.IsValid)
        {
            parents.Add(current);
            current = current.GetDirectParent();
        }

        return parents;
    }

    public string GetLeafName()
    {
        if (!IsValid)
        {
            return string.Empty;
        }

        var lastDot = Name.LastIndexOf('.');
        return lastDot < 0 ? Name : Name[(lastDot + 1)..];
    }

    public GameplayTagContainer GetSingleTagContainer()
    {
        return new GameplayTagContainer(this);
    }

    public int CompareTo(GameplayTag other)
    {
        return string.CompareOrdinal(Name, other.Name);
    }

    public override string ToString()
    {
        return Name;
    }
}
