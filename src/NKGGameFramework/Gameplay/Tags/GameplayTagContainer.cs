using System.Collections;

namespace NKGGameFramework.Gameplay;

public sealed class GameplayTagContainer : IReadOnlyCollection<GameplayTag>, IEquatable<GameplayTagContainer>
{
    private readonly HashSet<GameplayTag> _tags = [];
    private readonly HashSet<GameplayTag> _parentTags = [];

    public GameplayTagContainer()
    {
    }

    public GameplayTagContainer(GameplayTag tag)
    {
        AddTag(tag);
    }

    public GameplayTagContainer(IEnumerable<GameplayTag> tags)
    {
        AddTags(tags);
    }

    public IReadOnlyCollection<GameplayTag> Tags => _tags;

    public IReadOnlyCollection<GameplayTag> ParentTags => _parentTags;

    public int Count => _tags.Count;

    public bool IsEmpty => _tags.Count == 0;

    public static GameplayTagContainer From(params string[] tagNames)
    {
        ArgumentNullException.ThrowIfNull(tagNames);

        var container = new GameplayTagContainer();
        foreach (var tagName in tagNames)
        {
            container.AddTag(GameplayTag.From(tagName));
        }

        return container;
    }

    public static GameplayTagContainer From(IEnumerable<string> tagNames)
    {
        ArgumentNullException.ThrowIfNull(tagNames);

        var container = new GameplayTagContainer();
        foreach (var tagName in tagNames)
        {
            container.AddTag(GameplayTag.From(tagName));
        }

        return container;
    }

    public bool AddTag(GameplayTag tag)
    {
        if (!tag.IsValid || !_tags.Add(tag))
        {
            return false;
        }

        AddParentTags(tag);
        return true;
    }

    public bool AddLeafTag(GameplayTag tag)
    {
        if (!tag.IsValid || HasTagExact(tag))
        {
            return tag.IsValid;
        }

        if (HasTag(tag))
        {
            return false;
        }

        foreach (var parent in tag.GetParentTags())
        {
            RemoveTag(parent);
        }

        return AddTag(tag);
    }

    public void AddTags(IEnumerable<GameplayTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        foreach (var tag in tags)
        {
            AddTag(tag);
        }
    }

    public void AppendTags(GameplayTagContainer other)
    {
        ArgumentNullException.ThrowIfNull(other);
        AddTags(other.Tags);
    }

    public void AppendMatchingTags(GameplayTagContainer otherA, GameplayTagContainer otherB)
    {
        ArgumentNullException.ThrowIfNull(otherA);
        ArgumentNullException.ThrowIfNull(otherB);

        foreach (var tag in otherA.Tags)
        {
            if (tag.MatchesAny(otherB))
            {
                AddTag(tag);
            }
        }
    }

    public bool RemoveTag(GameplayTag tag)
    {
        if (!_tags.Remove(tag))
        {
            return false;
        }

        RebuildParentTags();
        return true;
    }

    public int RemoveTags(GameplayTagContainer tagsToRemove)
    {
        ArgumentNullException.ThrowIfNull(tagsToRemove);

        var removed = 0;
        foreach (var tag in tagsToRemove.Tags)
        {
            if (_tags.Remove(tag))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            RebuildParentTags();
        }

        return removed;
    }

    public bool RemoveTagByExplicitName(string tagName)
    {
        if (!GameplayTag.TryFrom(tagName, out var tag))
        {
            return false;
        }

        return RemoveTag(tag);
    }

    public void Clear()
    {
        _tags.Clear();
        _parentTags.Clear();
    }

    public bool HasTag(GameplayTag tagToCheck)
    {
        return tagToCheck.IsValid && (_tags.Contains(tagToCheck) || _parentTags.Contains(tagToCheck));
    }

    public bool HasTagExact(GameplayTag tagToCheck)
    {
        return tagToCheck.IsValid && _tags.Contains(tagToCheck);
    }

    public bool HasAny(GameplayTagContainer containerToCheck)
    {
        ArgumentNullException.ThrowIfNull(containerToCheck);

        if (containerToCheck.IsEmpty)
        {
            return false;
        }

        foreach (var tag in containerToCheck.Tags)
        {
            if (HasTag(tag))
            {
                return true;
            }
        }

        return false;
    }

    public bool HasAnyExact(GameplayTagContainer containerToCheck)
    {
        ArgumentNullException.ThrowIfNull(containerToCheck);

        if (containerToCheck.IsEmpty)
        {
            return false;
        }

        foreach (var tag in containerToCheck.Tags)
        {
            if (HasTagExact(tag))
            {
                return true;
            }
        }

        return false;
    }

    public bool HasAll(GameplayTagContainer containerToCheck)
    {
        ArgumentNullException.ThrowIfNull(containerToCheck);

        foreach (var tag in containerToCheck.Tags)
        {
            if (!HasTag(tag))
            {
                return false;
            }
        }

        return true;
    }

    public bool HasAllExact(GameplayTagContainer containerToCheck)
    {
        ArgumentNullException.ThrowIfNull(containerToCheck);

        foreach (var tag in containerToCheck.Tags)
        {
            if (!HasTagExact(tag))
            {
                return false;
            }
        }

        return true;
    }

    public GameplayTagContainer GetGameplayTagParents()
    {
        var result = new GameplayTagContainer(_tags);
        result.AddTags(_parentTags);
        return result;
    }

    public GameplayTagContainer Filter(GameplayTagContainer other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var result = new GameplayTagContainer();
        foreach (var tag in _tags)
        {
            if (tag.MatchesAny(other))
            {
                result.AddTag(tag);
            }
        }

        return result;
    }

    public GameplayTagContainer FilterExact(GameplayTagContainer other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var result = new GameplayTagContainer();
        foreach (var tag in _tags)
        {
            if (tag.MatchesAnyExact(other))
            {
                result.AddTag(tag);
            }
        }

        return result;
    }

    public bool MatchesQuery(GameplayTagQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Matches(this);
    }

    public string ToStringSimple(bool quoted = false)
    {
        var tagNames = _tags
            .Order()
            .Select(tag => quoted ? $"\"{tag.Name}\"" : tag.Name);

        return string.Join(", ", tagNames);
    }

    public bool Equals(GameplayTagContainer? other)
    {
        return other is not null && _tags.SetEquals(other._tags);
    }

    public override bool Equals(object? obj)
    {
        return obj is GameplayTagContainer other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var tag in _tags.Order())
        {
            hash.Add(tag);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(GameplayTagContainer? left, GameplayTagContainer? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(GameplayTagContainer? left, GameplayTagContainer? right)
    {
        return !(left == right);
    }

    public override string ToString()
    {
        return ToStringSimple();
    }

    public IEnumerator<GameplayTag> GetEnumerator()
    {
        return _tags.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void AddParentTags(GameplayTag tag)
    {
        foreach (var parent in tag.GetParentTags())
        {
            _parentTags.Add(parent);
        }
    }

    private void RebuildParentTags()
    {
        _parentTags.Clear();
        foreach (var tag in _tags)
        {
            AddParentTags(tag);
        }
    }
}
