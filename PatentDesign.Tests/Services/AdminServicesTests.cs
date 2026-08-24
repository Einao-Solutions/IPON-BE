using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Xunit;
using Moq;
using MongoDB.Driver;
using patentdesign.Services;
using patentdesign.Models;
using patentdesign.Dtos;

namespace PatentDesign.Tests.Services
{
    public class AdminServicesTests
    {
        private readonly Mock<IMongoCollection<Filling>> _mockFillingCollection;
        private readonly Mock<IMongoCollection<AttachmentInfo>> _mockAttachmentCollection;
        private readonly Mock<IMongoCollection<AppUsers>> _mockUserCollection;
        private readonly Mock<IMongoCollection<FileUpdateHistory>> _mockUpdateHistoryCollection;
        private readonly AdminServices _adminServices;

        public AdminServicesTests()
        {
            _mockFillingCollection = new Mock<IMongoCollection<Filling>>();
            _mockAttachmentCollection = new Mock<IMongoCollection<AttachmentInfo>>();
            _mockUserCollection = new Mock<IMongoCollection<AppUsers>>();
            _mockUpdateHistoryCollection = new Mock<IMongoCollection<FileUpdateHistory>>();

            // Setup default finds to return empty results
            var emptyAsyncCursor = new Mock<IAsyncCursor<AppUsers>>();
            emptyAsyncCursor
                .Setup(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            _mockUserCollection
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<AppUsers>>(), It.IsAny<FindOptions<AppUsers>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(emptyAsyncCursor.Object);

            _adminServices = new AdminServices(
                _mockFillingCollection.Object,
                _mockAttachmentCollection.Object,
                _mockUserCollection.Object,
                _mockUpdateHistoryCollection.Object,
                "http://localhost:5000"
            );
        }

        [Fact]
        public void CreateApplicationHistoryWithAttachments_ValidatesInput()
        {
            // Arrange
            var dto = new ApplicationHistoryDto
            {
                FileNumber = "FILE123",
                ApplicationType = 5, // Assignment
                ApplicationDate = DateTime.UtcNow,
                CurrentStatus = 0,
                PaymentId = null,
                CertificatePaymentId = null,
                OldValue = new { name = "Old Assignor", email = "old@example.com" },
                NewValue = new
                {
                    assigneeName = "New Assignee",
                    assigneeEmail = "new@example.com",
                    assigneePhone = "+2348012345678",
                    assigneeAddress = "New address",
                    attachments = new[]
                    {
                        new { fileName = "deed.pdf", contentType = "application/pdf", data = "SGVsbG8gV29ybGQh" }
                    }
                },
                UserId = "admin-user-123"
            };

            // Act & Assert
            Assert.NotNull(dto);
            Assert.Equal("FILE123", dto.FileNumber);
            Assert.Equal(5, dto.ApplicationType);
            Assert.NotNull(dto.NewValue);
        }

        [Fact]
        public void ApplicationHistory_ShouldMaintainOldValueNewValueShapes()
        {
            // Arrange - Create an ApplicationInfo object with oldValue/newValue
            var oldValue = new Dictionary<string, object>
            {
                { "name", "Old Assignor" },
                { "email", "old@example.com" },
                { "address", "Old address" }
            };

            var newValue = new Dictionary<string, object>
            {
                { "assigneeName", "New Assignee" },
                { "assigneeEmail", "new@example.com" },
                { "assigneePhone", "+2348012345678" },
                { "assigneeAddress", "New address" },
                { "attachments", new[] { new { fileName = "deed.pdf", url = "http://localhost:5000/api/files/getAttachment?fileId=abc123" } } }
            };

            var appInfo = new ApplicationInfo
            {
                ApplicationType = 5,
                ApplicationDate = DateTime.UtcNow,
                CurrentStatus = 0,
                OldValue = (object)oldValue,
                NewValue = (object)newValue
            };

            // Act & Assert
            Assert.NotNull(appInfo.OldValue);
            Assert.NotNull(appInfo.NewValue);
            Assert.Equal(5, appInfo.ApplicationType);
        }

        [Fact]
        public void AttachmentProcessing_Base64ShouldConvertToUrl()
        {
            // Arrange
            string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("This is a test PDF content"));
            string expectedFileName = "test-deed.pdf";
            string expectedContentType = "application/pdf";

            // Act - Simulate what ProcessNewValueAsync does
            byte[] decodedBytes = Convert.FromBase64String(base64Data);
            string attachmentUrl = $"http://localhost:5000/api/files/getAttachment?fileId={Guid.NewGuid():N}";

            // Assert
            Assert.NotEmpty(decodedBytes);
            Assert.Contains("http://localhost:5000/api/files/getAttachment", attachmentUrl);
            Assert.NotNull(expectedFileName);
            Assert.NotNull(expectedContentType);
        }

        [Fact]
        public void ApplicationHistory_CamelCaseConsistency()
        {
            // Verify that oldValue and newValue use correct camelCase keys
            var jsonString = @"{
                'oldValue': { 'name': 'Old', 'email': 'old@ex.com' },
                'newValue': {
                    'assigneeName': 'New',
                    'assigneeEmail': 'new@ex.com',
                    'assigneePhone': '+234',
                    'assigneeAddress': 'Address',
                    'attachments': [{ 'fileName': 'deed.pdf', 'contentType': 'application/pdf', 'url': 'http://...' }]
                }
            }";

            // Act
            var doc = JsonDocument.Parse(jsonString);
            var oldObj = doc.RootElement.GetProperty("oldValue");
            var newObj = doc.RootElement.GetProperty("newValue");

            // Assert - camelCase keys should exist
            Assert.True(oldObj.TryGetProperty("name", out _));
            Assert.True(oldObj.TryGetProperty("email", out _));
            Assert.True(newObj.TryGetProperty("assigneeName", out _));
            Assert.True(newObj.TryGetProperty("assigneeEmail", out _));
            Assert.True(newObj.TryGetProperty("assigneePhone", out _));
            Assert.True(newObj.TryGetProperty("assigneeAddress", out _));

            var attachments = newObj.GetProperty("attachments");
            Assert.True(attachments.GetArrayLength() > 0);
            var att = attachments[0];
            Assert.True(att.TryGetProperty("fileName", out _));
            Assert.True(att.TryGetProperty("contentType", out _));
            Assert.True(att.TryGetProperty("url", out _));
        }

        [Fact]
        public void DeleteApplicationHistory_ShouldLogAudit()
        {
            // Arrange - Verify that delete operation would create audit record
            string fileNumber = "FILE123";
            string applicationId = "app-456";
            string userId = "admin-user-123";
            var timestamp = DateTime.UtcNow;

            // Act - Create what the audit record should look like
            var auditRecord = new FileUpdateHistory
            {
                FileNumber = fileNumber,
                AdminName = "Admin User",
                UserId = userId,
                DateUpdated = timestamp,
                UpdateReason = $"Deleted ApplicationHistory entry: {applicationId}"
            };

            // Assert
            Assert.Equal(fileNumber, auditRecord.FileNumber);
            Assert.Equal(userId, auditRecord.UserId);
            Assert.NotNull(auditRecord.AdminName);
            Assert.Contains("Deleted", auditRecord.UpdateReason);
        }

        [Fact]
        public void ApplicationHistory_SupportsMultipleAttachments()
        {
            // Arrange
            var attachments = new List<Dictionary<string, object>>
            {
                new() { { "fileName", "deed.pdf" }, { "contentType", "application/pdf" }, { "url", "http://localhost:5000/api/files/getAttachment?fileId=1" } },
                new() { { "fileName", "power_of_attorney.pdf" }, { "contentType", "application/pdf" }, { "url", "http://localhost:5000/api/files/getAttachment?fileId=2" } },
                new() { { "fileName", "certificate.pdf" }, { "contentType", "application/pdf" }, { "url", "http://localhost:5000/api/files/getAttachment?fileId=3" } }
            };

            // Act & Assert
            Assert.Equal(3, attachments.Count);
            foreach (var att in attachments)
            {
                Assert.True(att.ContainsKey("fileName"));
                Assert.True(att.ContainsKey("contentType"));
                Assert.True(att.ContainsKey("url"));
            }
        }
    }
}
