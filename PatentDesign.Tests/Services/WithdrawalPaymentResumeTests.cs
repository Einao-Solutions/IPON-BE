using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Utils;

namespace PatentDesign.Tests.Services
{
    public class WithdrawalPaymentResumeTests
    {
        private const string Rrr = "123456789012";

        private static Filling CreateFile(ApplicationStatuses status = ApplicationStatuses.AwaitingPayment, bool withLetter = true) => new()
        {
            Id = "file-1",
            FileId = "F/TM/O/2024/1",
            Attachments = withLetter
                ? new List<AttachmentType> { new() { name = "withdrawal_letter", url = new List<string> { "/letter.pdf" } } }
                : new List<AttachmentType>(),
            ApplicationHistory = new List<ApplicationInfo>
            {
                new()
                {
                    id = "app-1",
                    ApplicationType = FormApplicationTypes.WithdrawalRequest,
                    CurrentStatus = status,
                    PaymentId = Rrr,
                    StatusHistory = new List<ApplicationHistory>()
                }
            }
        };

        private static Task<RemitaResponseClass?> Paid(string rrr) =>
            Task.FromResult<RemitaResponseClass?>(new RemitaResponseClass { rrr = rrr, status = "00", amount = 5000 });

        private static Task<RemitaResponseClass?> Unpaid(string rrr) =>
            Task.FromResult<RemitaResponseClass?>(new RemitaResponseClass { rrr = rrr, status = "021", amount = 5000 });

        [Fact]
        public async Task ResumeAsync_PaidRrr_ConfirmsAndLeavesAwaitingPayment()
        {
            var file = CreateFile();
            var persisted = 0;

            var result = await WithdrawalPaymentResume.ResumeAsync(file, "app-1", Paid,
                (_, _, _) => { persisted++; return Task.CompletedTask; }, "user", "u1");

            Assert.Equal(ApplicantPaymentOutcome.Confirmed, result.Outcome);
            Assert.True(result.IsSuccess);
            Assert.Equal(ApplicationStatuses.RequestWithdrawal, file.ApplicationHistory[0].CurrentStatus);
            Assert.NotNull(file.WithdrawalRequestDate);
            Assert.Single(file.ApplicationHistory[0].StatusHistory);
            Assert.Equal(1, persisted);
        }

        [Fact]
        public async Task ResumeAsync_CalledTwice_DoesNotDuplicate()
        {
            var file = CreateFile();
            var persisted = 0;
            Task Persist(Filling f, ApplicationInfo a, RemitaResponseClass p) { persisted++; return Task.CompletedTask; }

            await WithdrawalPaymentResume.ResumeAsync(file, "app-1", Paid, Persist, "user", "u1");
            var second = await WithdrawalPaymentResume.ResumeAsync(file, "app-1", Paid, Persist, "user", "u1");

            Assert.Equal(ApplicantPaymentOutcome.AlreadyProcessed, second.Outcome);
            Assert.True(second.IsSuccess);
            Assert.Single(file.ApplicationHistory[0].StatusHistory);
            Assert.Equal(1, persisted);
        }

        [Fact]
        public async Task ResumeAsync_UnpaidRrr_ReturnsNotPaidAndChangesNothing()
        {
            var file = CreateFile();
            var persisted = false;

            var result = await WithdrawalPaymentResume.ResumeAsync(file, "app-1", Unpaid,
                (_, _, _) => { persisted = true; return Task.CompletedTask; }, "user", "u1");

            Assert.Equal(ApplicantPaymentOutcome.NotPaid, result.Outcome);
            Assert.Equal(ApplicationStatuses.AwaitingPayment, file.ApplicationHistory[0].CurrentStatus);
            Assert.Null(file.WithdrawalRequestDate);
            Assert.False(persisted);
        }

        [Theory]
        [InlineData("missing-app")]
        [InlineData(null)]
        public async Task ResumeAsync_NotFound_ReturnsNotFound(string? applicationId)
        {
            var file = applicationId == null ? null : CreateFile();

            var result = await WithdrawalPaymentResume.ResumeAsync(file, applicationId ?? "app-1", Paid,
                (_, _, _) => Task.CompletedTask, "user", "u1");

            Assert.Equal(ApplicantPaymentOutcome.NotFound, result.Outcome);
            Assert.False(result.IsSuccess);
        }
    }
}
