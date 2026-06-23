using NKGGameFramework.Gameplay;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Tests.Gameplay;

public sealed class GameplayTagTests
{
    [Fact]
    public void Child_tag_matches_parent_but_parent_does_not_match_child()
    {
        var burn = GameplayTag.From("Ability.Fire.Burn");
        var fire = GameplayTag.From("Ability.Fire");

        Assert.True(burn.MatchesTag(fire));
        Assert.False(fire.MatchesTag(burn));
        Assert.False(burn.MatchesTagExact(fire));
        Assert.Equal("Burn", burn.GetLeafName());
        Assert.Equal(GameplayTag.From("Ability.Fire"), burn.GetDirectParent());
    }

    [Fact]
    public void Container_checks_explicit_and_parent_tags_like_unreal()
    {
        var tags = GameplayTagContainer.From("Ability.Fire.Burn", "State.Stunned");

        Assert.True(tags.HasTag(GameplayTag.From("Ability.Fire")));
        Assert.False(tags.HasTagExact(GameplayTag.From("Ability.Fire")));
        Assert.True(tags.HasAll(GameplayTagContainer.From("Ability", "State")));
        Assert.True(tags.HasAny(GameplayTagContainer.From("Ability.Ice", "State")));
        Assert.False(tags.HasAnyExact(GameplayTagContainer.From("Ability.Fire")));
    }

    [Fact]
    public void Container_supports_unreal_style_collection_mutation()
    {
        var tags = GameplayTagContainer.From("Ability.Fire.Burn", "State.Stunned", "State.Control.Silenced");

        Assert.True(tags.RemoveTagByExplicitName("State.Stunned"));
        Assert.False(tags.HasTagExact(GameplayTag.From("State.Stunned")));
        Assert.True(tags.HasTag(GameplayTag.From("State.Control")));

        var removed = tags.RemoveTags(GameplayTagContainer.From("State.Control.Silenced", "State.Missing"));

        Assert.Equal(1, removed);
        Assert.False(tags.HasTag(GameplayTag.From("State.Control")));
        Assert.True(tags.HasTag(GameplayTag.From("Ability.Fire")));
        Assert.False(tags.RemoveTagByExplicitName("State.Invalid Name"));
    }

    [Fact]
    public void Container_appends_matching_tags_from_two_sources()
    {
        var result = GameplayTagContainer.From("State.Ready");
        var source = GameplayTagContainer.From("Ability.Fire.Burn", "Ability.Ice.Freeze", "Item.Potion");
        var filter = GameplayTagContainer.From("Ability.Fire", "Item");

        result.AppendMatchingTags(source, filter);

        Assert.True(result.HasTagExact(GameplayTag.From("State.Ready")));
        Assert.True(result.HasTagExact(GameplayTag.From("Ability.Fire.Burn")));
        Assert.True(result.HasTagExact(GameplayTag.From("Item.Potion")));
        Assert.False(result.HasTagExact(GameplayTag.From("Ability.Ice.Freeze")));
    }

    [Fact]
    public void Container_equality_and_simple_string_use_explicit_tags()
    {
        var left = GameplayTagContainer.From("State.Burning", "Ability.Fire.Burn");
        var right = GameplayTagContainer.From("Ability.Fire.Burn", "State.Burning");
        var parentOnly = GameplayTagContainer.From("Ability.Fire");

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.False(left == parentOnly);
        Assert.Equal("Ability.Fire.Burn, State.Burning", left.ToStringSimple());
        Assert.Equal("\"Ability.Fire.Burn\", \"State.Burning\"", left.ToStringSimple(quoted: true));
    }

    [Fact]
    public void Query_supports_tag_and_expression_matching()
    {
        var tags = GameplayTagContainer.From("Unit.Hero.Warrior", "State.Burning");
        var query = new GameplayTagQuery(GameplayTagQueryExpression.AllExprMatch(
            GameplayTagQueryExpression.AnyTagsMatch(GameplayTagContainer.From("Unit.Hero")),
            GameplayTagQueryExpression.NoTagsMatch(GameplayTagContainer.From("State.Stunned"))));

        Assert.True(query.Matches(tags));
        Assert.False(GameplayTagQuery.ExactMatchAnyTags(GameplayTagContainer.From("Unit.Hero")).Matches(tags));
    }

    [Fact]
    public void Registry_tracks_metadata_redirects_and_children()
    {
        var registry = new GameplayTagRegistry();

        var burn = registry.Register("Ability.Fire.Burn", source: "Native", devComment: "Damage over time");
        registry.Register("Ability.Ice.Freeze", source: "Native");
        registry.RegisterRedirect("Ability.OldBurn", "Ability.Fire.Burn");

        Assert.Equal(burn, registry.Request("Ability.OldBurn"));
        Assert.True(registry.Contains(GameplayTag.From("Ability.Fire")));
        Assert.True(registry.TryGetDefinition(burn, out var definition));
        Assert.Equal("Damage over time", definition.DevComment);
        Assert.True(definition.IsExplicit);

        var descendants = registry.RequestChildren(GameplayTag.From("Ability"));
        Assert.True(descendants.HasTagExact(GameplayTag.From("Ability.Fire.Burn")));
        Assert.True(descendants.HasTagExact(GameplayTag.From("Ability.Ice.Freeze")));

        var directChildren = registry.RequestDirectChildren(GameplayTag.From("Ability"));
        Assert.True(directChildren.HasTagExact(GameplayTag.From("Ability.Fire")));
        Assert.True(directChildren.HasTagExact(GameplayTag.From("Ability.Ice")));
    }

    [Fact]
    public void Config_parser_reads_unreal_gameplay_tag_entries()
    {
        var registry = new GameplayTagRegistry();
        var config = """
            [/Script/GameplayTags.GameplayTagsList]
            +GameplayTagList=(Tag="State.Control.Silenced",DevComment="Cannot cast skills")
            +RestrictedGameplayTagList=(Tag="State.Restricted.Admin",DevComment="Restricted",bAllowNonRestrictedChildren=True)
            +GameplayTagRedirects=(OldTagName="State.Silence",NewTagName="State.Control.Silenced")
            """;

        var result = GameplayTagConfigParser.ApplyToRegistry(config, registry, "DefaultGameplayTags.ini");

        Assert.Equal(2, result.TagsRegistered);
        Assert.Equal(1, result.RedirectsRegistered);
        Assert.Equal(GameplayTag.From("State.Control.Silenced"), registry.Request("State.Silence"));
        Assert.True(registry.TryGetDefinition(GameplayTag.From("State.Restricted.Admin"), out var restricted));
        Assert.True(restricted.IsRestricted);
        Assert.True(restricted.AllowNonRestrictedChildren);
        Assert.Equal("DefaultGameplayTags.ini", restricted.Source);
    }

    [Fact]
    public void Entity_asset_interface_queries_owned_and_buff_granted_tags()
    {
        using var scene = new Scene("battle");
        var source = scene.CreateEntity();
        var entity = scene.CreateEntity()
            .Add(new GameplayTagComponent(GameplayTagContainer.From("Unit.Hero.Mage")));
        var buff = new BuffDefinition
        {
            Id = "burning",
            Tags = GameplayTagContainer.From("State.Burning"),
        };

        BuffManager.Apply(source, entity, buff);

        IGameplayTagAsset asset = new EntityGameplayTagAsset(entity);

        Assert.True(asset.HasMatchingGameplayTag(GameplayTag.From("Unit.Hero")));
        Assert.True(asset.HasMatchingGameplayTag(GameplayTag.From("State")));
        Assert.True(asset.HasAllMatchingGameplayTags(GameplayTagContainer.From("Unit", "State.Burning")));
        Assert.True(GameplayTagUtility.HasAnyMatchingGameplayTags(entity, GameplayTagContainer.From("State.Control", "State")));
    }

    [Fact]
    public void Table_parser_reads_unreal_datatable_csv_shape()
    {
        var registry = new GameplayTagRegistry();
        var csv = """
            Name,Tag,DevComment,bAllowNonRestrictedChildren
            Row_Burn,Ability.Fire.Burn,"Burn, over time",false
            Row_Admin,State.Restricted.Admin,Restricted,true
            """;

        var result = GameplayTagTableParser.ApplyCsvToRegistry(
            csv,
            registry,
            source: "GameplayTags.csv",
            isRestricted: true);

        Assert.Equal(2, result.RowsRegistered);
        Assert.True(registry.TryGetDefinition(GameplayTag.From("Ability.Fire.Burn"), out var burn));
        Assert.Equal("Burn, over time", burn.DevComment);
        Assert.True(burn.IsRestricted);
        Assert.Equal("GameplayTags.csv", burn.Source);

        Assert.True(registry.TryGetDefinition(GameplayTag.From("State.Restricted.Admin"), out var restricted));
        Assert.True(restricted.AllowNonRestrictedChildren);
    }
}
