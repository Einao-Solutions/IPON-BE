using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using patentdesign.Models;
using patentdesign.Services;
using Xunit;

namespace patentdesign.Tests.Services;

/// <summary>
/// Tests to diagnose the BSON deserialization error with AppUser.Id
/// "Cannot deserialize a 'String' from BsonType 'ObjectId'"
/// </summary>
public class AppUserBsonDeserializationTests
{
    [Fact]
    public void AppUser_Model_HasBsonIdAttribute_OnIdProperty()
    {
        // Verify the model is correctly decorated
        var idProperty = typeof(AppUser).GetProperty("Id");
        Assert.NotNull(idProperty);

        var bsonIdAttr = idProperty.GetCustomAttributes(typeof(BsonIdAttribute), inherit: false);
        Assert.NotEmpty(bsonIdAttr);
    }

    [Fact]
    public void AppUser_OtherApplications_ListIsNotNull()
    {
        var user = new AppUser { Id = "test-id" };
        Assert.NotNull(user.OtherApplications);
        Assert.Empty(user.OtherApplications);
    }

    [Fact]
    public void ApplicationInfo_StructureIsSerializable()
    {
        var app = new ApplicationInfo
        {
            ApplicationType = FormApplicationTypes.AvailabilitySearch,
            CurrentStatus = ApplicationStatuses.AwaitingPayment,
            Title = "Test",
            ApplicationDate = DateTime.UtcNow,
            PaymentId = "RRR-123"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(app);
        Assert.NotNull(json);
        Assert.Contains("Test", json);
    }

    [Fact]
    public void AppUser_WithOtherApplications_CanBeCreatedInMemory()
    {
        var user = new AppUser
        {
            Id = "test-user-id",
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User",
            OtherApplications = new List<ApplicationInfo>
            {
                new ApplicationInfo
                {
                    ApplicationType = FormApplicationTypes.AvailabilitySearch,
                    CurrentStatus = ApplicationStatuses.AwaitingPayment,
                    Title = "Search 1",
                    PaymentId = "RRR-001"
                },
                new ApplicationInfo
                {
                    ApplicationType = FormApplicationTypes.AvailabilitySearch,
                    CurrentStatus = ApplicationStatuses.AutoApproved,
                    Title = "Search 2",
                    PaymentId = "RRR-002"
                }
            }
        };

        Assert.Equal("test-user-id", user.Id);
        Assert.Equal(2, user.OtherApplications.Count);
        Assert.Equal("Search 1", user.OtherApplications[0].Title);
    }

    [Fact]
    public void BsonSerialization_CanSerializeAppUserWithOtherApplications()
    {
        var user = new AppUser
        {
            Id = "65a1b2c3d4e5f6a7b8c9d0e1", // ObjectId-like string
            Email = "test@example.com",
            FirstName = "John",
            OtherApplications = new List<ApplicationInfo>
            {
                new ApplicationInfo
                {
                    ApplicationType = FormApplicationTypes.AvailabilitySearch,
                    CurrentStatus = ApplicationStatuses.AwaitingPayment,
                    Title = "Test Search"
                }
            }
        };

        try
        {
            var doc = user.ToBsonDocument();
            Assert.NotNull(doc);
            Assert.True(doc.Contains("_id"));
            Assert.True(doc.Contains("OtherApplications"));

            // Try to deserialize back
            var deserialized = BsonSerializer.Deserialize<AppUser>(doc);
            Assert.NotNull(deserialized);
            Assert.Equal("65a1b2c3d4e5f6a7b8c9d0e1", deserialized.Id);
            Assert.Single(deserialized.OtherApplications);
        }
        catch (Exception ex)
        {
            Assert.False(true, $"BSON serialization failed: {ex.Message}");
        }
    }

    [Fact]
    public void BsonDocument_WithObjectId_DeserializesToString()
    {
        // Create a BSON document with an actual ObjectId in the _id field
        var id = ObjectId.GenerateNewId();
        var doc = new BsonDocument
        {
            { "_id", id },
            { "Email", "test@example.com" },
            { "FirstName", "John" },
            { "OtherApplications", new BsonArray() }
        };

        var user = BsonSerializer.Deserialize<AppUser>(doc);

        Assert.Equal(id.ToString(), user.Id);
        Assert.Equal("John", user.FirstName);
        Assert.Empty(user.OtherApplications!);
    }

    [Theory]
    [InlineData("2837bddd-2d4b-4919-bf2f-7a1f41049f61")]
    [InlineData("65a1b2c3d4e5f6a7b8c9d0e1")]
    [InlineData("existing-user-id")]
    public void StringIds_PreserveStoredRepresentationAndQueryValues(string id)
    {
        var user = BsonSerializer.Deserialize<AppUser>(new BsonDocument("_id", id));

        Assert.Equal(id, user.Id);
        var stored = user.ToBsonDocument();
        Assert.Equal(BsonType.String, stored["_id"].BsonType);
        Assert.Equal(id, stored["_id"].AsString);

        var filter = Builders<AppUser>.Filter.Eq(u => u.Id, id).Render(
            BsonSerializer.LookupSerializer<AppUser>(), BsonSerializer.SerializerRegistry);
        Assert.Equal(new BsonDocument("_id", id), filter);
    }
}
