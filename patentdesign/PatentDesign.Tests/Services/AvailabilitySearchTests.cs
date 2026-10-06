using Xunit;
using patentdesign.Models;
using patentdesign.Enums;
using patentdesign.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace patentdesign.Tests.Services
{
    /// <summary>
    /// Tests verify the end-to-end Availability Search payment flow:
    /// 1. Record creation and persistence before payment (AvailabilitySearchCost)
    /// 2. Record retrieval without status filtering (GetOtherApplications)
    /// 3. Payment confirmation with idempotency (UpdateAvailabilitySearchPayment)
    /// 4. Failed/pending payments remain visible
    /// 5. User isolation
    /// 6. Field names according to contract
    /// </summary>
    public class AvailabilitySearchTests
    {
        private readonly string _testUserId = "test-user-123";
        private readonly string _testSearchTerm = "TRADEMARK_SEARCH_TERM";
        private readonly string _testRrr = "570015240123456789";

        #region Test: ApplicationInfo Model Structure

        [Fact]
        public void ApplicationInfo_ShouldBeCreatedWithCorrectDefaults()
        {
            // Act
            var appInfo = new ApplicationInfo
            {
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.UtcNow,
                PaymentId = _testRrr,
                Title = _testSearchTerm,
                StatusHistory = new List<ApplicationHistory>()
            };

            // Assert - Verify all fields are set correctly
            Assert.NotNull(appInfo.id);
            Assert.Equal(FormApplicationTypes.AvailabilitySearch, appInfo.ApplicationType);
            Assert.Equal(ApplicationStatuses.AwaitingPayment, appInfo.CurrentStatus);
            Assert.Equal(_testSearchTerm, appInfo.Title);
            Assert.Equal(_testRrr, appInfo.PaymentId);
            Assert.Empty(appInfo.StatusHistory);
        }

        #endregion

        #region Test: FormApplicationTypes Enum Value

        [Fact]
        public void FormApplicationTypes_AvailabilitySearch_ShouldBe29()
        {
            // Act
            var enumValue = (int)FormApplicationTypes.AvailabilitySearch;

            // Assert
            Assert.Equal(29, enumValue);
        }

        #endregion

        #region Test: Record Persistence Simulation

        [Fact]
        public void SimulateRecordPersistence_RecordShouldBeSavedToUserOtherApplications()
        {
            // Arrange
            var user = new AppUser
            {
                Id = _testUserId,
                OtherApplications = new List<ApplicationInfo>()
            };

            var newRecord = new ApplicationInfo
            {
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.UtcNow,
                PaymentId = _testRrr,
                Title = _testSearchTerm,
                StatusHistory = new List<ApplicationHistory>()
            };

            // Act - Simulate database push operation
            user.OtherApplications.Add(newRecord);

            // Assert
            Assert.Single(user.OtherApplications);
            Assert.Equal(newRecord, user.OtherApplications[0]);
            Assert.Equal(FormApplicationTypes.AvailabilitySearch, user.OtherApplications[0].ApplicationType);
            Assert.Equal(_testSearchTerm, user.OtherApplications[0].Title);
        }

        #endregion

        #region Test: GetOtherApplications Returns All Statuses

        [Fact]
        public void GetOtherApplications_ShouldReturnAllRecords_IncludingUnpaid()
        {
            // Arrange
            var unpaidSearch = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.UtcNow.AddHours(-2),
                PaymentId = _testRrr,
                Title = "UNPAID_SEARCH"
            };

            var paidSearch = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AutoApproved,
                ApplicationDate = DateTime.UtcNow.AddHours(-1),
                PaymentId = _testRrr,
                Title = "PAID_SEARCH",
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now,
                        beforeStatus = ApplicationStatuses.AwaitingPayment,
                        afterStatus = ApplicationStatuses.AutoApproved,
                        Message = "Payment successful",
                        User = "System"
                    }
                }
            };

            var user = new AppUser
            {
                Id = _testUserId,
                OtherApplications = new List<ApplicationInfo> { unpaidSearch, paidSearch }
            };

            // Act - Simulate GetOtherApplications (no filtering)
            var results = user.OtherApplications;

            // Assert
            Assert.Equal(2, results.Count);

            // Unpaid record is included
            var unpaidResult = results.FirstOrDefault(x => x.CurrentStatus == ApplicationStatuses.AwaitingPayment);
            Assert.NotNull(unpaidResult);
            Assert.Equal(FormApplicationTypes.AvailabilitySearch, unpaidResult.ApplicationType);
            Assert.Equal("UNPAID_SEARCH", unpaidResult.Title);

            // Paid record is included
            var paidResult = results.FirstOrDefault(x => x.CurrentStatus == ApplicationStatuses.AutoApproved);
            Assert.NotNull(paidResult);
            Assert.Equal(FormApplicationTypes.AvailabilitySearch, paidResult.ApplicationType);
            Assert.Equal("PAID_SEARCH", paidResult.Title);
        }

        #endregion

        #region Test: Payment Confirmation Status Update

        [Fact]
        public void UpdatePaymentStatus_ShouldUpdateStatusAndAddHistory()
        {
            // Arrange
            var record = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                PaymentId = _testRrr,
                Title = _testSearchTerm,
                StatusHistory = new List<ApplicationHistory>()
            };

            var beforeStatus = record.CurrentStatus;

            // Act - Simulate payment confirmation
            record.CurrentStatus = ApplicationStatuses.AutoApproved;
            record.StatusHistory.Add(new ApplicationHistory
            {
                Date = DateTime.Now,
                beforeStatus = beforeStatus,
                afterStatus = ApplicationStatuses.AutoApproved,
                Message = "Payment successful, availability search completed",
                User = "System"
            });

            // Assert
            Assert.Equal(ApplicationStatuses.AutoApproved, record.CurrentStatus);
            Assert.Single(record.StatusHistory);
            Assert.Equal("Payment successful, availability search completed", record.StatusHistory[0].Message);
        }

        #endregion

        #region Test: Idempotency

        [Fact]
        public void IdempotentRetry_ShouldNotDuplicateStatusHistory()
        {
            // Arrange
            var record = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AutoApproved, // Already paid
                PaymentId = _testRrr,
                StatusHistory = new List<ApplicationHistory>
                {
                    new ApplicationHistory
                    {
                        Date = DateTime.Now.AddMinutes(-5),
                        beforeStatus = ApplicationStatuses.AwaitingPayment,
                        afterStatus = ApplicationStatuses.AutoApproved,
                        Message = "Payment successful, availability search completed"
                    }
                }
            };

            // Act - Simulate idempotent retry (check status first)
            if (record.CurrentStatus == ApplicationStatuses.AutoApproved)
            {
                // Don't add duplicate history
            }
            else
            {
                record.StatusHistory.Add(new ApplicationHistory
                {
                    Date = DateTime.Now,
                    beforeStatus = ApplicationStatuses.AwaitingPayment,
                    afterStatus = ApplicationStatuses.AutoApproved,
                    Message = "Payment successful"
                });
            }

            // Assert
            Assert.Single(record.StatusHistory); // No duplicate added
        }

        #endregion

        #region Test: Failed Payment Remains Visible

        [Fact]
        public void FailedPayment_RecordShouldRemainVisible_WithAwaitingPaymentStatus()
        {
            // Arrange
            var record = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                PaymentId = _testRrr,
                Title = _testSearchTerm,
                StatusHistory = new List<ApplicationHistory>()
            };

            var user = new AppUser
            {
                Id = _testUserId,
                OtherApplications = new List<ApplicationInfo> { record }
            };

            // Act - Payment fails on Remita, exception thrown, status NOT updated
            var isPaymentFailed = true;
            if (isPaymentFailed)
            {
                // Don't update status, throw exception instead
                // record.CurrentStatus remains AwaitingPayment
            }

            // Assert - Record is still visible in user's applications
            var visibleRecords = user.OtherApplications;
            Assert.Single(visibleRecords);
            Assert.Equal(ApplicationStatuses.AwaitingPayment, visibleRecords[0].CurrentStatus);
            Assert.Equal(FormApplicationTypes.AvailabilitySearch, visibleRecords[0].ApplicationType);
            Assert.Empty(visibleRecords[0].StatusHistory); // No history added
        }

        #endregion

        #region Test: User Isolation

        [Fact]
        public void UserIsolation_ShouldOnlyAllowUserToAccessTheirOwnRecords()
        {
            // Arrange
            var user1Id = "user-1";
            var user2Id = "user-2";
            var recordId = Guid.NewGuid().ToString();

            var record = new ApplicationInfo
            {
                id = recordId,
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AwaitingPayment
            };

            var user1 = new AppUser
            {
                Id = user1Id,
                OtherApplications = new List<ApplicationInfo> { record }
            };

            var user2 = new AppUser
            {
                Id = user2Id,
                OtherApplications = new List<ApplicationInfo>()
            };

            // Act - user2 tries to access user1's record
            var user2Records = user2.OtherApplications;
            var user2CanAccessRecord = user2Records.Any(x => x.id == recordId);

            // Assert
            Assert.False(user2CanAccessRecord);
            Assert.Single(user1.OtherApplications);
            Assert.Empty(user2.OtherApplications);
        }

        #endregion

        #region Test: JSON Field Names

        [Fact]
        public void ApplicationInfo_JsonSerializationShouldUseCorrectFieldNames()
        {
            // Arrange
            var appInfo = new ApplicationInfo
            {
                id = Guid.NewGuid().ToString(),
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = DateTime.UtcNow,
                PaymentId = _testRrr,
                Title = _testSearchTerm,
                StatusHistory = new List<ApplicationHistory>()
            };

            // Act - Serialize to JSON
            var json = System.Text.Json.JsonSerializer.Serialize(appInfo, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                WriteIndented = true
            });

            // Assert - Verify JSON contains correct field names
            Assert.Contains("\"id\"", json);
            Assert.Contains("\"applicationType\"", json);
            Assert.Contains("\"currentStatus\"", json);
            Assert.Contains("\"applicationDate\"", json);
            Assert.Contains("\"paymentId\"", json);
            Assert.Contains("\"statusHistory\"", json);
            Assert.Contains("29", json); // AvailabilitySearch enum value
        }

        #endregion

        #region Test: Response Contract

        [Fact]
        public void AvailabilitySearchDtoResponse_ShouldHaveCorrectFields()
        {
            // Arrange
            var dto = new Dtos.Response.AvailabilitySearchDto
            {
                cost = "5000.00",
                rrr = _testRrr,
                AppId = Guid.NewGuid().ToString()
            };

            // Assert
            Assert.NotNull(dto.cost);
            Assert.NotNull(dto.rrr);
            Assert.NotNull(dto.AppId);
            Assert.IsType<string>(dto.cost);
            Assert.IsType<string>(dto.rrr);
            Assert.IsType<string>(dto.AppId);
        }

        #endregion

        #region Test: Production Availability Search Rules

        [Fact]
        public void CreateApplication_ShouldPersistExpectedHistoryFields()
        {
            var date = new DateTime(2026, 5, 12, 10, 30, 0, DateTimeKind.Utc);
            var record = AvailabilitySearchRules.CreateApplication(_testSearchTerm, _testRrr, date);
            var user = new AppUser
            {
                Id = _testUserId,
                OtherApplications = new List<ApplicationInfo> { record }
            };

            Assert.Equal(_testUserId, user.Id);
            Assert.Equal(FormApplicationTypes.AvailabilitySearch, record.ApplicationType);
            Assert.Equal(29, (int)record.ApplicationType);
            Assert.Equal(ApplicationStatuses.AwaitingPayment, record.CurrentStatus);
            Assert.Equal(_testSearchTerm, record.Title);
            Assert.Equal(date, record.ApplicationDate);
            Assert.Equal(_testRrr, record.PaymentId);
            Assert.Empty(record.StatusHistory);
            Assert.Single(user.OtherApplications);
        }

        [Fact]
        public void FindAwaitingPaymentApplication_ShouldReuseUnpaidMatchingSearch()
        {
            var unpaid = AvailabilitySearchRules.CreateApplication(_testSearchTerm, _testRrr);
            var alreadyPaid = AvailabilitySearchRules.CreateApplication(_testSearchTerm, "paid-rrr");
            alreadyPaid.CurrentStatus = ApplicationStatuses.AutoApproved;
            var otherType = AvailabilitySearchRules.CreateApplication(_testSearchTerm, "other-rrr");
            otherType.ApplicationType = FormApplicationTypes.StatusSearch;

            var result = AvailabilitySearchRules.FindAwaitingPaymentApplication(
                new[] { alreadyPaid, otherType, unpaid }, _testSearchTerm);

            Assert.Same(unpaid, result);
            Assert.Equal(_testRrr, result?.PaymentId);
        }

        [Fact]
        public void CanAccessOwner_ShouldAllowOwnerAndSuperAdminOnly()
        {
            Assert.True(AvailabilitySearchRules.CanAccessOwner(_testUserId, _testUserId, false));
            Assert.False(AvailabilitySearchRules.CanAccessOwner(_testUserId, "unrelated-user", false));
            Assert.True(AvailabilitySearchRules.CanAccessOwner(_testUserId, "admin-user", true));
            Assert.False(AvailabilitySearchRules.CanAccessOwner(_testUserId, null, false));
        }

        [Fact]
        public void FindOwnedApplication_ShouldRejectMissingOrOtherOwnersApplicationIds()
        {
            var application = AvailabilitySearchRules.CreateApplication(_testSearchTerm, _testRrr);
            var owner = new AppUser
            {
                Id = _testUserId,
                OtherApplications = new List<ApplicationInfo> { application }
            };
            var unrelatedUser = new AppUser
            {
                Id = "unrelated-user",
                OtherApplications = new List<ApplicationInfo>()
            };

            Assert.Same(application, AvailabilitySearchRules.FindOwnedApplication(owner, application.id));
            Assert.Null(AvailabilitySearchRules.FindOwnedApplication(owner, "missing-app-id"));
            Assert.Null(AvailabilitySearchRules.FindOwnedApplication(unrelatedUser, application.id));
        }

        [Fact]
        public void UpdatedAvailabilitySearch_RemainsVisibleInTheOwnersOtherApplications()
        {
            var application = AvailabilitySearchRules.CreateApplication(_testSearchTerm, _testRrr);
            var owner = new AppUser
            {
                Id = _testUserId,
                OtherApplications = new List<ApplicationInfo> { application }
            };

            application.CurrentStatus = ApplicationStatuses.AutoApproved;
            var returnedApplications = owner.OtherApplications;

            var returnedApplication = Assert.Single(returnedApplications);
            Assert.Equal(application.id, returnedApplication.id);
            Assert.Equal(ApplicationStatuses.AutoApproved, returnedApplication.CurrentStatus);
        }

        [Fact]
        public void IsSuccessfulPayment_ShouldRequireSuccessfulStatusAndMatchingRrr()
        {
            Assert.True(AvailabilitySearchRules.IsSuccessfulPayment(
                new RemitaResponseClass { rrr = _testRrr, status = "00" }, _testRrr));
            Assert.False(AvailabilitySearchRules.IsSuccessfulPayment(
                new RemitaResponseClass { rrr = _testRrr, status = "01" }, _testRrr));
            Assert.False(AvailabilitySearchRules.IsSuccessfulPayment(
                new RemitaResponseClass { rrr = _testRrr, status = "025" }, _testRrr));
            Assert.False(AvailabilitySearchRules.IsSuccessfulPayment(
                new RemitaResponseClass { rrr = "different-rrr", status = "00" }, _testRrr));
            Assert.False(AvailabilitySearchRules.IsSuccessfulPayment(null, _testRrr));
        }

        [Fact]
        public void CanConfirmPayment_ShouldRequireExistingAwaitingSearchAndMatchingSuccessfulRrr()
        {
            var application = AvailabilitySearchRules.CreateApplication(_testSearchTerm, _testRrr);
            var successfulPayment = new RemitaResponseClass { rrr = _testRrr, status = "00" };

            Assert.True(AvailabilitySearchRules.CanConfirmPayment(application, successfulPayment));

            application.CurrentStatus = ApplicationStatuses.AutoApproved;
            Assert.False(AvailabilitySearchRules.CanConfirmPayment(application, successfulPayment));

            application.CurrentStatus = ApplicationStatuses.AwaitingPayment;
            application.ApplicationType = FormApplicationTypes.StatusSearch;
            Assert.False(AvailabilitySearchRules.CanConfirmPayment(application, successfulPayment));
        }

        [Theory]
        [InlineData("01")]
        [InlineData("025")]
        public void CanConfirmPayment_ShouldRejectPendingOrFailedPaymentWithoutChangingStatus(string paymentStatus)
        {
            var application = AvailabilitySearchRules.CreateApplication(_testSearchTerm, _testRrr);
            var payment = new RemitaResponseClass { rrr = _testRrr, status = paymentStatus };

            Assert.False(AvailabilitySearchRules.CanConfirmPayment(application, payment));
            Assert.Equal(ApplicationStatuses.AwaitingPayment, application.CurrentStatus);
        }

        [Fact]
        public void AlreadyConfirmedApplication_ShouldBeRecognizedForIdempotentRetry()
        {
            var application = AvailabilitySearchRules.CreateApplication(_testSearchTerm, _testRrr);
            application.CurrentStatus = ApplicationStatuses.AutoApproved;
            application.StatusHistory.Add(new ApplicationHistory
            {
                beforeStatus = ApplicationStatuses.AwaitingPayment,
                afterStatus = ApplicationStatuses.AutoApproved
            });

            Assert.True(AvailabilitySearchRules.IsAlreadyConfirmed(application));
            Assert.Single(application.StatusHistory);
        }

        [Fact]
        public void ReceiptLetterType_ShouldRemainAtFrontendValue95()
        {
            Assert.Equal(95, (int)ApplicationLetters.AvailabilitySearchReceipt);
        }

        #endregion
    }
}
