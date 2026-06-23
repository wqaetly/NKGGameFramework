using NKGGameFramework.Serialization;
using OdinSerializer;

namespace NKGGameFramework.Tests.Serialization;

public sealed class OdinGameSerializerTests
{
    [Fact]
    public void RoundTripsPrivateFieldsWithoutAttributes()
    {
        var serializer = new OdinGameSerializer();
        var snapshot = SaveGameSnapshot.Create();

        var payload = serializer.SerializeToBytes(snapshot);
        var restored = serializer.DeserializeFromBytes<SaveGameSnapshot>(payload);

        Assert.NotNull(restored);
        Assert.Equal("knight:12:sword:41:potion,sword", restored.Describe());
    }

    [Fact]
    public void PreservesPolymorphicFieldsAndCollections()
    {
        var serializer = new OdinGameSerializer();
        var snapshot = SaveGameSnapshot.Create();

        var restored = serializer.DeserializeFromBytes<SaveGameSnapshot>(
            serializer.SerializeToBytes(snapshot));

        Assert.NotNull(restored);
        Assert.IsType<SwordItem>(restored.Equipped);
        Assert.Collection(
            restored.Items,
            item => Assert.IsType<PotionItem>(item),
            item => Assert.IsType<SwordItem>(item));
    }

    [Fact]
    public void StringContractUsesBase64Payload()
    {
        IGameSerializer serializer = new OdinGameSerializer();
        var snapshot = SaveGameSnapshot.Create();

        var payload = serializer.Serialize(snapshot);
        var restored = serializer.Deserialize<SaveGameSnapshot>(payload);

        Assert.False(string.IsNullOrWhiteSpace(payload));
        Assert.NotNull(restored);
        Assert.Equal(snapshot.Describe(), restored.Describe());
    }

    [Fact]
    public void JsonContractUsesOdinJsonPayload()
    {
        IJsonGameSerializer serializer = new OdinGameSerializer();
        var snapshot = SaveGameSnapshot.Create();

        var payload = serializer.SerializeToJson(snapshot);
        var restored = serializer.DeserializeFromJson<SaveGameSnapshot>(payload);

        Assert.StartsWith("{", payload.TrimStart());
        Assert.Contains("knight", payload);
        Assert.NotNull(restored);
        Assert.Equal(snapshot.Describe(), restored.Describe());
    }

    [Fact]
    public void StringContractUsesJsonPayloadWhenConfigured()
    {
        IGameSerializer serializer = new OdinGameSerializer(DataFormat.JSON);
        var snapshot = SaveGameSnapshot.Create();

        var payload = serializer.Serialize(snapshot);
        var restored = serializer.Deserialize<SaveGameSnapshot>(payload);

        Assert.StartsWith("{", payload.TrimStart());
        Assert.NotNull(restored);
        Assert.Equal(snapshot.Describe(), restored.Describe());
    }

    private sealed class SaveGameSnapshot
    {
        private string _playerId = string.Empty;
        private int _level;
        private InventoryItem _equipped = null!;
        private List<InventoryItem> _items = [];

        private SaveGameSnapshot()
        {
        }

        private SaveGameSnapshot(string playerId, int level, InventoryItem equipped, List<InventoryItem> items)
        {
            _playerId = playerId;
            _level = level;
            _equipped = equipped;
            _items = items;
        }

        public InventoryItem Equipped => _equipped;

        public IReadOnlyList<InventoryItem> Items => _items;

        public static SaveGameSnapshot Create()
        {
            return new SaveGameSnapshot(
                "knight",
                12,
                new SwordItem("sword", 41),
                [new PotionItem("potion", 3), new SwordItem("sword", 41)]);
        }

        public string Describe()
        {
            return $"{_playerId}:{_level}:{_equipped.Id}:{_equipped.Power}:{string.Join(",", _items.Select(item => item.Id))}";
        }
    }

    private abstract class InventoryItem
    {
        private string _id = string.Empty;

        protected InventoryItem()
        {
        }

        protected InventoryItem(string id)
        {
            _id = id;
        }

        public string Id => _id;

        public abstract int Power { get; }
    }

    private sealed class SwordItem : InventoryItem
    {
        private int _damage;

        private SwordItem()
        {
        }

        public SwordItem(string id, int damage)
            : base(id)
        {
            _damage = damage;
        }

        public override int Power => _damage;
    }

    private sealed class PotionItem : InventoryItem
    {
        private int _charges;

        private PotionItem()
        {
        }

        public PotionItem(string id, int charges)
            : base(id)
        {
            _charges = charges;
        }

        public override int Power => _charges;
    }
}
