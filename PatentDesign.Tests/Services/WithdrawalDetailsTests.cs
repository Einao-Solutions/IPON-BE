using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using MongoDB.Driver;
using patentdesign.Services;
using patentdesign.Models;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace PatentDesign.Tests.Services
{
    public class WithdrawalDetailsTests
    {
        private Mock<IMongoCollection<Filling>> _mockFillingCollection;
        private Mock<IMongoDatabase> _mockDatabase;
        private Mock<IOptions<PatentDesignDBSettings>> _mockSettings;
        private Mock<PaymentUtils> _mockPaymentUtils;
        private Mock<ILogger<FilesServices>> _mockLogger;
        private Mock<PaymentService> _mockPaymentService;
        private Mock<PublicationServices> _mockPublicationServices;
        private Mock<NotificationServices> _mockNotificationServices;
        private FilesServices _filesServices;

        public WithdrawalDetailsTests()
        {
            SetupMocks();
        }

        private void SetupMocks()
        {
            _mockFillingCollection = new Mock<IMongoCollection<Filling>>();
            _mockDatabase = new Mock<IMongoDatabase>();
            _mockSettings = new Mock<IOptions<PatentDesignDBSettings>>();
            _mockPaymentUtils = new Mock<PaymentUtils>();
            _mockLogger = new Mock<ILogger<FilesServices>>();
            _mockPaymentService = new Mock<PaymentService>();
            _mockPublicationServices = new Mock<PublicationServices>();
            _mockNotificationServices = new Mock<NotificationServices>();

            // Setup settings
            _mockSettings.Setup(s => s.Value).Returns(new PatentDesignDBSettings
            {
                ConnectionString = "mongodb://localhost:27017",
                DatabaseName = "testdb",
                FilesCollectionName = "files",
                CountersCollectionName = "counters",
                FinanceCollectionName = "finance",
                AttachmentCollectionName = "attachments",
                UsersCollectionName = "users",
                TicketCollectionName = "tickets",
                OppositionCollectionName = "oppositions",
                CounterStatementsCollectionName = "counterStatements"
            });

            // Setup database mock
            _mockDatabase.Setup(d => d.GetCollection<Filling>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(_mockFillingCollection.Object);
            _mockDatabase.Setup(d => d.GetCollection<Counters>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<Counters>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<FinanceHistory>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<FinanceHistory>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<AttachmentInfo>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<AttachmentInfo>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<AppUser>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<AppUser>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<StaffPerformance>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<StaffPerformance>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<StatusRequests>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<StatusRequests>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<TicketInfo>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<TicketInfo>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<OppositionType>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<OppositionType>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<FileUpdateHistory>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<FileUpdateHistory>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<PublicationInfo>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<PublicationInfo>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<SignatureInfo>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<SignatureInfo>>().Object);
            _mockDatabase.Setup(d => d.GetCollection<OfflineRenewalRequest>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(new Mock<IMongoCollection<OfflineRenewalRequest>>().Object);

            _filesServices = new FilesServices(
                _mockDatabase.Object,
                _mockSettings.Object,
                _mockPaymentUtils.Object,
                _mockLogger.Object,
                _mockPaymentService.Object,
                _mockPublicationServices.Object,
                _mockNotificationServices.Object
            );
        }

        private IAsyncCursor<T> CreateMockCursor<T>(T? item)
        {
            var mockCursor = new Mock<IAsyncCursor<T>>();
            mockCursor.Setup(c => c.Current).Returns(item == null ? new T[] { } : new T[] { item });
            mockCursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(item != null);
            mockCursor.Setup(c => c.FirstOrDefaultAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(item);
            return mockCursor.Object;
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithValidFileIdAndWithdrawalRequest_ShouldReturnCompleteDetails()
        {
            // Arrange
            var fileId = "F/TM/O/2016/88119";
            var filing = new Filling
            {
                FileId = fileId,
                Type = FileTypes.Trademark,
                WithdrawalRequestDate = new DateTime(2026, 8, 24, 10, 0, 0),
                WithdrawalDate = new DateTime(2026, 8, 25, 14, 30, 0),
                Attachments = new List<AttachmentType>
                {
                    new AttachmentType
                    {
                        name = "withdrawal_letter",
                        url = new List<string> { "/api/files/GetAttachment?fileId=withdrawal_123.pdf" }
                    },
                    new AttachmentType
                    {
                        name = "withdrawal_supporting_documents",
                        url = new List<string> 
                        { 
                            "/api/files/GetAttachment?fileId=support_001.pdf",
                            "/api/files/GetAttachment?fileId=support_002.pdf"
                        }
                    }
                },
                ApplicationHistory = new List<ApplicationInfo>
                {
                    new ApplicationInfo
                    {
                        id = "app-123",
                        ApplicationType = FormApplicationTypes.WithdrawalRequest,
                        CurrentStatus = ApplicationStatuses.RequestWithdrawal,
                        ApplicationDate = new DateTime(2026, 8, 24),
                        PaymentId = "RRR123456789"
                    }
                }
            };

            _mockFillingCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Filling>>(),
                    It.IsAny<FindOptions<Filling>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockCursor(filing));

            _mockPaymentService
                .Setup(p => p.GetPaymentRecordByFileIdAsync(fileId, "File Withdrawal"))
                .ReturnsAsync((PaymentRecord)null);

            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync(fileId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(fileId, result.FileId);
            Assert.Equal("Trademark", result.FileType);
            Assert.Equal(new DateTime(2026, 8, 24, 10, 0, 0), result.WithdrawalRequestDate);
            Assert.Equal(new DateTime(2026, 8, 25, 14, 30, 0), result.WithdrawalDate);
            Assert.Equal("RequestWithdrawal", result.ApplicationStatus);
            Assert.Equal("RRR123456789", result.PaymentId);
            Assert.Single(result.WithdrawalLetterAttachments);
            Assert.Equal(2, result.SupportingDocumentAttachments.Count);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithWithdrawalLetterOnly_ShouldReturnOnlyLetterAttachments()
        {
            // Arrange
            var fileId = "F/TM/O/2016/88119";
            var filing = new Filling
            {
                FileId = fileId,
                Type = FileTypes.Trademark,
                WithdrawalRequestDate = new DateTime(2026, 8, 24),
                WithdrawalDate = new DateTime(2026, 8, 25),
                Attachments = new List<AttachmentType>
                {
                    new AttachmentType
                    {
                        name = "withdrawal_letter",
                        url = new List<string> { "/api/files/GetAttachment?fileId=letter.pdf" }
                    }
                },
                ApplicationHistory = new List<ApplicationInfo>
                {
                    new ApplicationInfo
                    {
                        id = "app-123",
                        ApplicationType = FormApplicationTypes.WithdrawalRequest,
                        CurrentStatus = ApplicationStatuses.Approved,
                        PaymentId = "RRR987654321"
                    }
                }
            };

            _mockFillingCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Filling>>(),
                    It.IsAny<FindOptions<Filling>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockCursor(filing));

            _mockPaymentService
                .Setup(p => p.GetPaymentRecordByFileIdAsync(fileId, "File Withdrawal"))
                .ReturnsAsync((PaymentRecord)null);

            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync(fileId);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.WithdrawalLetterAttachments);
            Assert.Empty(result.SupportingDocumentAttachments);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithSupportingDocumentsOnly_ShouldReturnOnlyDocumentAttachments()
        {
            // Arrange
            var fileId = "F/TM/O/2016/88119";
            var filing = new Filling
            {
                FileId = fileId,
                Type = FileTypes.Design,
                WithdrawalRequestDate = new DateTime(2026, 8, 24),
                Attachments = new List<AttachmentType>
                {
                    new AttachmentType
                    {
                        name = "withdrawal_supporting_documents",
                        url = new List<string> 
                        { 
                            "/api/files/GetAttachment?fileId=doc1.pdf",
                            "/api/files/GetAttachment?fileId=doc2.pdf"
                        }
                    }
                },
                ApplicationHistory = new List<ApplicationInfo>
                {
                    new ApplicationInfo
                    {
                        id = "app-456",
                        ApplicationType = FormApplicationTypes.WithdrawalRequest,
                        CurrentStatus = ApplicationStatuses.RequestWithdrawal,
                        PaymentId = "PAY123"
                    }
                }
            };

            _mockFillingCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Filling>>(),
                    It.IsAny<FindOptions<Filling>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockCursor(filing));

            _mockPaymentService
                .Setup(p => p.GetPaymentRecordByFileIdAsync(fileId, "File Withdrawal"))
                .ReturnsAsync((PaymentRecord)null);

            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync(fileId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.WithdrawalLetterAttachments);
            Assert.Equal(2, result.SupportingDocumentAttachments.Count);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithBothDocumentTypes_ShouldReturnBothAttachments()
        {
            // Arrange
            var fileId = "F/TM/O/2016/88119";
            var filing = new Filling
            {
                FileId = fileId,
                Type = FileTypes.Patent,
                WithdrawalRequestDate = new DateTime(2026, 8, 24),
                WithdrawalDate = new DateTime(2026, 8, 25),
                Attachments = new List<AttachmentType>
                {
                    new AttachmentType
                    {
                        name = "withdrawal_letter",
                        url = new List<string> { "/api/files/GetAttachment?fileId=letter.pdf" }
                    },
                    new AttachmentType
                    {
                        name = "withdrawal_supporting_documents",
                        url = new List<string> 
                        { 
                            "/api/files/GetAttachment?fileId=doc1.pdf",
                            "/api/files/GetAttachment?fileId=doc2.pdf"
                        }
                    }
                },
                ApplicationHistory = new List<ApplicationInfo>
                {
                    new ApplicationInfo
                    {
                        id = "app-789",
                        ApplicationType = FormApplicationTypes.WithdrawalRequest,
                        CurrentStatus = ApplicationStatuses.Approved,
                        PaymentId = "RRR111222333"
                    }
                }
            };

            _mockFillingCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Filling>>(),
                    It.IsAny<FindOptions<Filling>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockCursor(filing));

            _mockPaymentService
                .Setup(p => p.GetPaymentRecordByFileIdAsync(fileId, "File Withdrawal"))
                .ReturnsAsync((PaymentRecord)null);

            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync(fileId);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.WithdrawalLetterAttachments);
            Assert.Equal(2, result.SupportingDocumentAttachments.Count);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithNoOptionalDocuments_ShouldReturnEmptyAttachmentLists()
        {
            // Arrange
            var fileId = "F/TM/O/2016/88119";
            var filing = new Filling
            {
                FileId = fileId,
                Type = FileTypes.Trademark,
                WithdrawalRequestDate = new DateTime(2026, 8, 24),
                Attachments = new List<AttachmentType>(),
                ApplicationHistory = new List<ApplicationInfo>
                {
                    new ApplicationInfo
                    {
                        id = "app-001",
                        ApplicationType = FormApplicationTypes.WithdrawalRequest,
                        CurrentStatus = ApplicationStatuses.RequestWithdrawal,
                        PaymentId = "PAY456"
                    }
                }
            };

            _mockFillingCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Filling>>(),
                    It.IsAny<FindOptions<Filling>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockCursor(filing));

            _mockPaymentService
                .Setup(p => p.GetPaymentRecordByFileIdAsync(fileId, "File Withdrawal"))
                .ReturnsAsync((PaymentRecord)null);

            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync(fileId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.WithdrawalLetterAttachments);
            Assert.Empty(result.SupportingDocumentAttachments);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithInvalidFileId_ShouldReturnNull()
        {
            // Arrange
            _mockFillingCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Filling>>(),
                    It.IsAny<FindOptions<Filling>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockCursor<Filling>(null));

            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync("INVALID_FILE_ID");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithEmptyFileId_ShouldReturnNull()
        {
            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync("");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithNullFileId_ShouldReturnNull()
        {
            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync(null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithMissingWithdrawalRequest_ShouldReturnNull()
        {
            // Arrange
            var fileId = "F/TM/O/2016/88119";
            var filing = new Filling
            {
                FileId = fileId,
                Type = FileTypes.Trademark,
                ApplicationHistory = new List<ApplicationInfo>
                {
                    new ApplicationInfo
                    {
                        id = "app-123",
                        ApplicationType = FormApplicationTypes.RenewalRequest,
                        CurrentStatus = ApplicationStatuses.Approved
                    }
                }
            };

            _mockFillingCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Filling>>(),
                    It.IsAny<FindOptions<Filling>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockCursor(filing));

            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync(fileId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithMissingPaymentRecord_ShouldReturnNullPaymentId()
        {
            // Arrange
            var fileId = "F/TM/O/2016/88119";
            var filing = new Filling
            {
                FileId = fileId,
                Type = FileTypes.Trademark,
                WithdrawalRequestDate = new DateTime(2026, 8, 24),
                Attachments = new List<AttachmentType>(),
                ApplicationHistory = new List<ApplicationInfo>
                {
                    new ApplicationInfo
                    {
                        id = "app-123",
                        ApplicationType = FormApplicationTypes.WithdrawalRequest,
                        CurrentStatus = ApplicationStatuses.RequestWithdrawal,
                        PaymentId = null  // No payment ID stored
                    }
                }
            };

            _mockFillingCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Filling>>(),
                    It.IsAny<FindOptions<Filling>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockCursor(filing));

            _mockPaymentService
                .Setup(p => p.GetPaymentRecordByFileIdAsync(fileId, "File Withdrawal"))
                .ReturnsAsync((PaymentRecord)null);

            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync(fileId);

            // Assert
            Assert.NotNull(result);
            Assert.Null(result.PaymentId);
        }

        [Fact]
        public async Task GetWithdrawalDetailsAsync_WithPaymentRecordFallback_ShouldRetrievePaymentFromRecord()
        {
            // Arrange
            var fileId = "F/TM/O/2016/88119";
            var filing = new Filling
            {
                FileId = fileId,
                Type = FileTypes.Trademark,
                WithdrawalRequestDate = new DateTime(2026, 8, 24),
                Attachments = new List<AttachmentType>(),
                ApplicationHistory = new List<ApplicationInfo>
                {
                    new ApplicationInfo
                    {
                        id = "app-123",
                        ApplicationType = FormApplicationTypes.WithdrawalRequest,
                        CurrentStatus = ApplicationStatuses.RequestWithdrawal,
                        PaymentId = null
                    }
                }
            };

            var paymentRecord = new PaymentRecord
            {
                FileId = fileId,
                PaymentType = "File Withdrawal",
                RemitaResponse = new RemitaResponseClass { rrr = "RRR987654321" }
            };

            _mockFillingCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<Filling>>(),
                    It.IsAny<FindOptions<Filling>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockCursor(filing));

            _mockPaymentService
                .Setup(p => p.GetPaymentRecordByFileIdAsync(fileId, "File Withdrawal"))
                .ReturnsAsync(paymentRecord);

            // Act
            var result = await _filesServices.GetWithdrawalDetailsAsync(fileId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("RRR987654321", result.PaymentId);
        }
    }
}
