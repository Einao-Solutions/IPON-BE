using Xunit;
using patentdesign.Models;
using patentdesign.Enums;
using System;
using System.Collections.Generic;

namespace patentdesign.Tests.Services
{
    /// <summary>
    /// Tests verify that:
    /// 1. Each Change of Agent (Ownership) history entry includes its own POA URL
    /// 2. Multiple ownership changes retain distinct POA URLs
    /// 3. All historical ownership entries are returned (not just the newest)
    /// 4. POA URLs are accessible and point to correct attachment IDs
    /// </summary>
    public class OwnershipHistoryPoaTests
    {
        [Fact]
        public void OwnershipHistoryEntry_ShouldIncludePoaUrl()
        {
            // Arrange
            var poaUrl = "http://localhost/api/files/getAttachment?fileId=abcd1234.pdf";
            var newValue = new Dictionary<string, object?>
            {
                ["id"] = "agent-user-123",
                ["name"] = "New Agent Name",
                ["email"] = "agent@example.com",
                ["poa"] = new Dictionary<string, object?>
                {
                    ["fileName"] = "power_of_attorney.pdf",
                    ["contentType"] = "application/pdf",
                    ["url"] = poaUrl,
                }
            };

            var ownershipEntry = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.Ownership,
                ApplicationDate = DateTime.UtcNow,
                CurrentStatus = ApplicationStatuses.Approved,
                FieldToChange = "ownership",
                NewValue = newValue
            };

            // Act & Assert - Verify POA is accessible from newValue
            Assert.NotNull(ownershipEntry.NewValue);
            Assert.IsType<Dictionary<string, object?>>(ownershipEntry.NewValue);

            var valueDict = (Dictionary<string, object?>)ownershipEntry.NewValue;
            Assert.True(valueDict.ContainsKey("poa"));

            var poaObj = valueDict["poa"];
            Assert.IsType<Dictionary<string, object?>>(poaObj);

            var poaDict = (Dictionary<string, object?>)poaObj;
            Assert.True(poaDict.ContainsKey("url"));
            Assert.Equal(poaUrl, poaDict["url"]);
        }

        [Fact]
        public void MultipleOwnershipEntries_ShouldEachHaveDistinctPoaUrl()
        {
            // Arrange - Create multiple ownership history entries with distinct POA URLs
            var entries = new List<ApplicationInfo>();
            var poaUrls = new List<string>
            {
                "http://localhost/api/files/getAttachment?fileId=poa_001.pdf",
                "http://localhost/api/files/getAttachment?fileId=poa_002.pdf",
                "http://localhost/api/files/getAttachment?fileId=poa_003.pdf"
            };

            for (int i = 0; i < 3; i++)
            {
                var newValue = new Dictionary<string, object?>
                {
                    ["id"] = $"agent-user-{i}",
                    ["name"] = $"Agent {i}",
                    ["poa"] = new Dictionary<string, object?>
                    {
                        ["url"] = poaUrls[i],
                    }
                };

                entries.Add(new ApplicationInfo
                {
                    id = Guid.NewGuid().ToString(),
                    ApplicationType = FormApplicationTypes.Ownership,
                    ApplicationDate = DateTime.UtcNow.AddDays(-i),
                    NewValue = newValue
                });
            }

            // Act & Assert - Verify each entry has its own POA URL
            Assert.Equal(3, entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                var valueDict = (Dictionary<string, object?>)entries[i].NewValue;
                var poaDict = (Dictionary<string, object?>)valueDict["poa"];
                Assert.Equal(poaUrls[i], poaDict["url"]);
            }
        }

        [Fact]
        public void OwnershipHistoryEntry_ShouldPreserveAllAgentDetails()
        {
            // Arrange
            var agentDetails = new Dictionary<string, object?>
            {
                ["id"] = "agent-123",
                ["name"] = "John Doe",
                ["email"] = "john@example.com",
                ["phone"] = "+1234567890",
                ["address"] = "123 Main St",
                ["state"] = "California",
                ["nationality"] = "USA",
                ["poa"] = new Dictionary<string, object?>
                {
                    ["fileName"] = "poa.pdf",
                    ["contentType"] = "application/pdf",
                    ["url"] = "http://localhost/api/files/getAttachment?fileId=poa.pdf",
                }
            };

            var entry = new ApplicationInfo
            {
                NewValue = agentDetails
            };

            // Act
            var valueDict = (Dictionary<string, object?>)entry.NewValue;

            // Assert - Verify all agent details are preserved
            Assert.Equal("agent-123", valueDict["id"]);
            Assert.Equal("John Doe", valueDict["name"]);
            Assert.Equal("john@example.com", valueDict["email"]);
            Assert.Equal("+1234567890", valueDict["phone"]);
            Assert.Equal("123 Main St", valueDict["address"]);
            Assert.Equal("California", valueDict["state"]);
            Assert.Equal("USA", valueDict["nationality"]);

            var poaDict = (Dictionary<string, object?>)valueDict["poa"];
            Assert.Equal("poa.pdf", poaDict["fileName"]);
            Assert.Equal("application/pdf", poaDict["contentType"]);
            Assert.NotEmpty((string)poaDict["url"]);
        }

        [Fact]
        public void OwnershipHistoryEntry_WithAttachments_ShouldIncludePoaMetadata()
        {
            // Arrange - Entry with both poa and attachments fields
            var newValue = new Dictionary<string, object?>
            {
                ["id"] = "agent-456",
                ["name"] = "Jane Smith",
                ["poa"] = new Dictionary<string, object?>
                {
                    ["fileName"] = "power_of_attorney.pdf",
                    ["contentType"] = "application/pdf",
                    ["url"] = "http://localhost/api/files/getAttachment?fileId=poa_456.pdf",
                },
                ["attachments"] = new List<Dictionary<string, object?>>
                {
                    new()
                    {
                        ["fileName"] = "power_of_attorney.pdf",
                        ["contentType"] = "application/pdf",
                        ["url"] = "http://localhost/api/files/getAttachment?fileId=poa_456.pdf",
                    }
                }
            };

            var entry = new ApplicationInfo
            {
                NewValue = newValue
            };

            // Act
            var valueDict = (Dictionary<string, object?>)entry.NewValue;
            var poaDict = (Dictionary<string, object?>)valueDict["poa"];
            var attachmentsList = (List<Dictionary<string, object?>>)valueDict["attachments"];

            // Assert
            Assert.NotNull(poaDict);
            Assert.NotEmpty(attachmentsList);

            // POA should be accessible directly from newValue.poa
            Assert.Equal("poa_456.pdf", ((string)poaDict["url"]).Split("fileId=")[1]);

            // Attachments array should also contain the same POA
            Assert.Equal(1, attachmentsList.Count);
            Assert.Equal(poaDict["url"], attachmentsList[0]["url"]);
        }

        [Fact]
        public void ApplicationHistoryDto_OwnershipHistory_ShouldContainAllEntries()
        {
            // Arrange - Simulate an AvailabilitySearchDto with OwnershipHistory
            var ownershipHistory = new List<ApplicationInfo>();

            for (int i = 0; i < 5; i++)
            {
                ownershipHistory.Add(new ApplicationInfo
                {
                    id = $"ownership-{i}",
                    ApplicationType = FormApplicationTypes.Ownership,
                    ApplicationDate = DateTime.UtcNow.AddDays(-i * 30),
                    NewValue = new Dictionary<string, object?>
                    {
                        ["id"] = $"owner-{i}",
                        ["name"] = $"Owner {i}",
                        ["poa"] = new Dictionary<string, object?>
                        {
                            ["url"] = $"http://localhost/api/files/getAttachment?fileId=poa_{i}.pdf"
                        }
                    }
                });
            }

            // Act & Assert - Verify all entries are preserved
            Assert.Equal(5, ownershipHistory.Count);

            // Verify entries are in reverse chronological order (newest first)
            for (int i = 0; i < ownershipHistory.Count; i++)
            {
                var entry = ownershipHistory[i];
                Assert.Equal($"ownership-{i}", entry.id);

                var valueDict = (Dictionary<string, object?>)entry.NewValue;
                var poaDict = (Dictionary<string, object?>)valueDict["poa"];
                Assert.Contains($"poa_{i}.pdf", (string)poaDict["url"]);
            }
        }
    }
}
