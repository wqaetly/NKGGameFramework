namespace NKGGameFramework.Gameplay;

public sealed class GameplayTagQuery
{
    public static readonly GameplayTagQuery Empty = new(null);

    public GameplayTagQuery(GameplayTagQueryExpression? rootExpression)
    {
        RootExpression = rootExpression;
    }

    public GameplayTagQueryExpression? RootExpression { get; }

    public bool IsEmpty => RootExpression is null;

    public static GameplayTagQuery MatchTag(GameplayTag tag)
    {
        return MatchAnyTags(new GameplayTagContainer(tag));
    }

    public static GameplayTagQuery MatchAnyTags(GameplayTagContainer tags)
    {
        return new GameplayTagQuery(GameplayTagQueryExpression.AnyTagsMatch(tags));
    }

    public static GameplayTagQuery MatchAllTags(GameplayTagContainer tags)
    {
        return new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch(tags));
    }

    public static GameplayTagQuery MatchNoTags(GameplayTagContainer tags)
    {
        return new GameplayTagQuery(GameplayTagQueryExpression.NoTagsMatch(tags));
    }

    public static GameplayTagQuery ExactMatchAnyTags(GameplayTagContainer tags)
    {
        return new GameplayTagQuery(GameplayTagQueryExpression.AnyTagsExactMatch(tags));
    }

    public static GameplayTagQuery ExactMatchAllTags(GameplayTagContainer tags)
    {
        return new GameplayTagQuery(GameplayTagQueryExpression.AllTagsExactMatch(tags));
    }

    public bool Matches(GameplayTagContainer tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return RootExpression?.Matches(tags) == true;
    }
}

public enum GameplayTagQueryExpressionType
{
    AnyTagsMatch,
    AllTagsMatch,
    NoTagsMatch,
    AnyTagsExactMatch,
    AllTagsExactMatch,
    AnyExprMatch,
    AllExprMatch,
    NoExprMatch,
}

public sealed class GameplayTagQueryExpression
{
    private GameplayTagQueryExpression(
        GameplayTagQueryExpressionType expressionType,
        GameplayTagContainer? tags = null,
        IReadOnlyList<GameplayTagQueryExpression>? expressions = null)
    {
        ExpressionType = expressionType;
        Tags = tags ?? new GameplayTagContainer();
        Expressions = expressions ?? [];
    }

    public GameplayTagQueryExpressionType ExpressionType { get; }

    public GameplayTagContainer Tags { get; }

    public IReadOnlyList<GameplayTagQueryExpression> Expressions { get; }

    public static GameplayTagQueryExpression AnyTagsMatch(GameplayTagContainer tags)
    {
        return new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AnyTagsMatch, tags);
    }

    public static GameplayTagQueryExpression AllTagsMatch(GameplayTagContainer tags)
    {
        return new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AllTagsMatch, tags);
    }

    public static GameplayTagQueryExpression NoTagsMatch(GameplayTagContainer tags)
    {
        return new GameplayTagQueryExpression(GameplayTagQueryExpressionType.NoTagsMatch, tags);
    }

    public static GameplayTagQueryExpression AnyTagsExactMatch(GameplayTagContainer tags)
    {
        return new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AnyTagsExactMatch, tags);
    }

    public static GameplayTagQueryExpression AllTagsExactMatch(GameplayTagContainer tags)
    {
        return new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AllTagsExactMatch, tags);
    }

    public static GameplayTagQueryExpression AnyExprMatch(params GameplayTagQueryExpression[] expressions)
    {
        return new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AnyExprMatch, expressions: expressions);
    }

    public static GameplayTagQueryExpression AllExprMatch(params GameplayTagQueryExpression[] expressions)
    {
        return new GameplayTagQueryExpression(GameplayTagQueryExpressionType.AllExprMatch, expressions: expressions);
    }

    public static GameplayTagQueryExpression NoExprMatch(params GameplayTagQueryExpression[] expressions)
    {
        return new GameplayTagQueryExpression(GameplayTagQueryExpressionType.NoExprMatch, expressions: expressions);
    }

    public bool Matches(GameplayTagContainer tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        return ExpressionType switch
        {
            GameplayTagQueryExpressionType.AnyTagsMatch => tags.HasAny(Tags),
            GameplayTagQueryExpressionType.AllTagsMatch => tags.HasAll(Tags),
            GameplayTagQueryExpressionType.NoTagsMatch => !tags.HasAny(Tags),
            GameplayTagQueryExpressionType.AnyTagsExactMatch => tags.HasAnyExact(Tags),
            GameplayTagQueryExpressionType.AllTagsExactMatch => tags.HasAllExact(Tags),
            GameplayTagQueryExpressionType.AnyExprMatch => Expressions.Any(expression => expression.Matches(tags)),
            GameplayTagQueryExpressionType.AllExprMatch => Expressions.All(expression => expression.Matches(tags)),
            GameplayTagQueryExpressionType.NoExprMatch => !Expressions.Any(expression => expression.Matches(tags)),
            _ => false,
        };
    }
}
