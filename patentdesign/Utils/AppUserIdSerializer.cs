using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace patentdesign.Utils;

public sealed class AppUserIdSerializer : SerializerBase<string>
{
    public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
        context.Reader.GetCurrentBsonType() == BsonType.ObjectId
            ? context.Reader.ReadObjectId().ToString()
            : StringSerializer.Instance.Deserialize(context, args);

    // Registration uses GUID strings; accepting legacy ObjectIds must not change string writes.
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value) =>
        StringSerializer.Instance.Serialize(context, args, value);
}
