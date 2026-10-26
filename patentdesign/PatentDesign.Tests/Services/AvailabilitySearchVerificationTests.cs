using patentdesign.Dtos.Response;
using Xunit;

namespace patentdesign.Tests.Services;

public class AvailabilitySearchVerificationTests : IClassFixture<AvsFlowFixture>
{
    private readonly AvsFlowFixture f;
    public AvailabilitySearchVerificationTests(AvsFlowFixture fixture) => f = fixture;

    private async Task<(string user, string rrr)> Create(string term)
    {
        var user = await f.CreateUser();
        var dto = await f.Files.AvailabilitySearchCost("n", "e@x.com", user, term);
        return (user, dto.rrr);
    }

    [Fact]
    public async Task PaidAndMatchingAmount_IsApproved_ThenIdempotent()
    {
        var (user, rrr) = await Create("ok");
        f.Remita.Statuses[rrr] = "00";
        var first = await f.Files.ConfirmAvailabilitySearchPayment(user, rrr);
        var second = await f.Files.ConfirmAvailabilitySearchPayment(user, rrr);
        Assert.Equal(AvailabilitySearchPaymentOutcome.Confirmed, first.Outcome);
        Assert.Equal(AvailabilitySearchPaymentOutcome.AlreadyProcessed, second.Outcome);
    }

    [Fact]
    public async Task WrongAmount_IsRejected()
    {
        var (user, rrr) = await Create("amount");
        f.Remita.Statuses[rrr] = "00";
        f.Remita.Amount = 1;
        try
        {
            var res = await f.Files.ConfirmAvailabilitySearchPayment(user, rrr);
            Assert.Equal(AvailabilitySearchPaymentOutcome.NotPaid, res.Outcome);
        }
        finally { f.Remita.Amount = 1000; }
    }

    [Fact]
    public async Task PendingStatus_IsNotApproved()
    {
        var (user, rrr) = await Create("pending");
        f.Remita.Statuses[rrr] = "021";
        Assert.Equal(AvailabilitySearchPaymentOutcome.NotPaid, (await f.Files.ConfirmAvailabilitySearchPayment(user, rrr)).Outcome);
    }

    [Fact]
    public async Task RemitaOutage_IsUnverifiable_AndStaysPending()
    {
        var (user, rrr) = await Create("outage");
        f.Remita.Statuses[rrr] = "00";
        f.Remita.Unreachable = true;
        try
        {
            Assert.Equal(AvailabilitySearchPaymentOutcome.Unverifiable, (await f.Files.ConfirmAvailabilitySearchPayment(user, rrr)).Outcome);
        }
        finally { f.Remita.Unreachable = false; }
        Assert.Equal(AvailabilitySearchPaymentOutcome.Confirmed, (await f.Files.ConfirmAvailabilitySearchPayment(user, rrr)).Outcome);
    }

    [Fact]
    public async Task UnknownRrr_AndOtherUsersRrr_AreNotFound()
    {
        var (_, rrr) = await Create("owner");
        var other = await f.CreateUser();
        f.Remita.Statuses[rrr] = "00";
        Assert.Equal(AvailabilitySearchPaymentOutcome.NotFound, (await f.Files.ConfirmAvailabilitySearchPayment(other, rrr)).Outcome);
        Assert.Equal(AvailabilitySearchPaymentOutcome.NotFound, (await f.Files.ConfirmAvailabilitySearchPayment(other, "999999999")).Outcome);
    }
}
