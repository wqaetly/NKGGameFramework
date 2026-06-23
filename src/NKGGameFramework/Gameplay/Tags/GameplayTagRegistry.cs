namespace NKGGameFramework.Gameplay;

public sealed class GameplayTagRegistry
{
    private readonly Dictionary<GameplayTag, GameplayTagDefinition> _definitions = [];
    private readonly Dictionary<GameplayTag, GameplayTag> _redirects = [];

    public static GameplayTagRegistry Global { get; } = new();

    public IReadOnlyCollection<GameplayTag> RegisteredTags => _definitions.Keys.ToArray();

    public IReadOnlyCollection<GameplayTagDefinition> Definitions => _definitions.Values.ToArray();

    public IReadOnlyDictionary<GameplayTag, GameplayTag> Redirects => _redirects;

    public GameplayTag Register(
        string tagName,
        string source = "",
        string devComment = "",
        bool isRestricted = false,
        bool allowNonRestrictedChildren = false)
    {
        var tag = GameplayTag.From(tagName);
        RegisterParentTags(tag, source);

        _definitions[tag] = new GameplayTagDefinition(
            tag,
            source,
            devComment,
            IsExplicit: true,
            isRestricted,
            allowNonRestrictedChildren);

        return tag;
    }

    public void RegisterRange(IEnumerable<string> tagNames, string source = "")
    {
        ArgumentNullException.ThrowIfNull(tagNames);

        foreach (var tagName in tagNames)
        {
            Register(tagName, source);
        }
    }

    public GameplayTagRedirect RegisterRedirect(string oldTagName, string newTagName)
    {
        var oldTag = GameplayTag.From(oldTagName);
        var newTag = ResolveRedirect(GameplayTag.From(newTagName));

        if (!newTag.IsValid)
        {
            throw new InvalidOperationException($"Gameplay tag redirect '{oldTagName}' points to an invalid target '{newTagName}'.");
        }

        _redirects[oldTag] = newTag;
        return new GameplayTagRedirect(oldTag, newTag);
    }

    public GameplayTag Request(string tagName, bool errorIfNotFound = true)
    {
        var tag = ResolveRedirect(GameplayTag.From(tagName));
        if (_definitions.ContainsKey(tag))
        {
            return tag;
        }

        if (errorIfNotFound)
        {
            throw new KeyNotFoundException($"Gameplay tag '{tagName}' is not registered.");
        }

        return GameplayTag.Empty;
    }

    public bool TryRequest(string tagName, out GameplayTag tag)
    {
        if (!GameplayTag.TryFrom(tagName, out tag))
        {
            return false;
        }

        tag = ResolveRedirect(tag);
        if (_definitions.ContainsKey(tag))
        {
            return true;
        }

        tag = GameplayTag.Empty;
        return false;
    }

    public bool Contains(GameplayTag tag)
    {
        return _definitions.ContainsKey(ResolveRedirect(tag));
    }

    public bool TryGetDefinition(GameplayTag tag, out GameplayTagDefinition definition)
    {
        return _definitions.TryGetValue(ResolveRedirect(tag), out definition!);
    }

    public GameplayTagContainer RequestParents(GameplayTag tag)
    {
        return new GameplayTagContainer(ResolveRedirect(tag).GetParentTags());
    }

    public GameplayTag RequestDirectParent(GameplayTag tag)
    {
        return ResolveRedirect(tag).GetDirectParent();
    }

    public GameplayTagContainer RequestChildren(GameplayTag tag, bool explicitOnly = false)
    {
        var root = ResolveRedirect(tag);
        var result = new GameplayTagContainer();

        foreach (var definition in _definitions.Values)
        {
            if (explicitOnly && !definition.IsExplicit)
            {
                continue;
            }

            if (!definition.Tag.MatchesTagExact(root) && definition.Tag.MatchesTag(root))
            {
                result.AddTag(definition.Tag);
            }
        }

        return result;
    }

    public GameplayTagContainer RequestDirectChildren(GameplayTag tag, bool explicitOnly = false)
    {
        var root = ResolveRedirect(tag);
        var result = new GameplayTagContainer();

        foreach (var definition in _definitions.Values)
        {
            if (explicitOnly && !definition.IsExplicit)
            {
                continue;
            }

            if (definition.Tag.GetDirectParent().MatchesTagExact(root))
            {
                result.AddTag(definition.Tag);
            }
        }

        return result;
    }

    public GameplayTag ResolveRedirect(GameplayTag tag)
    {
        var current = tag;
        var visited = new HashSet<GameplayTag>();

        while (current.IsValid && _redirects.TryGetValue(current, out var next))
        {
            if (!visited.Add(current))
            {
                throw new InvalidOperationException($"Gameplay tag redirect cycle detected at '{current}'.");
            }

            current = next;
        }

        return current;
    }

    public void Clear()
    {
        _definitions.Clear();
        _redirects.Clear();
    }

    private void RegisterParentTags(GameplayTag tag, string source)
    {
        foreach (var parent in tag.GetParentTags())
        {
            if (_definitions.ContainsKey(parent))
            {
                continue;
            }

            _definitions[parent] = new GameplayTagDefinition(parent, source, IsExplicit: false);
        }
    }
}
