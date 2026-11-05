using patentdesign.Enums;
using patentdesign.Utils;
using Xunit;

namespace patentdesign.Tests.Services;

public class PaymentVerifierTests
{
    private static Func<string, Task<RemitaResponseClass?>> Lookup(string? status, string rrr, double amount = 1000) =>
        _ => Task.FromResult<RemitaResponseClass?>(status == null ? null : new RemitaResponseClass { status = status, rrr = rrr, amount = amount });

    [Fact]
    public async Task Paid_WhenStatus00AndRrrMatches()
    {
        var r = await PaymentVerifier.VerifyAsync(Lookup("00", "123456789012"), "123456789012");
        Assert.True(r.IsPaid);
    }

    [Theory]
    [InlineData("021")]
    [InlineData("01")]
    [InlineData("")]
    public async Task NotPaid_WhenStatusIsNot00(string status)
    {
        var r = await PaymentVerifier.VerifyAsync(Lookup(status, "123456789012"), "123456789012");
        Assert.Equal(PaymentVerificationOutcome.NotPaid, r.Outcome);
    }

    [Fact]
    public async Task Unverifiable_WhenProviderReturnsNull()
    {
        var r = await PaymentVerifier.VerifyAsync(Lookup(null, ""), "123456789012");
        Assert.Equal(PaymentVerificationOutcome.Unverifiable, r.Outcome);
    }

    [Fact]
    public async Task Unverifiable_WhenProviderThrows()
    {
        var r = await PaymentVerifier.VerifyAsync(_ => throw new HttpRequestException(), "123456789012");
        Assert.Equal(PaymentVerificationOutcome.Unverifiable, r.Outcome);
    }

    [Fact]
    public async Task Mismatch_WhenRrrDiffers()
    {
        var r = await PaymentVerifier.VerifyAsync(Lookup("00", "999999999999"), "123456789012");
        Assert.Equal(PaymentVerificationOutcome.ReferenceMismatch, r.Outcome);
    }

    [Fact]
    public async Task Mismatch_WhenReferenceMissing()
    {
        var r = await PaymentVerifier.VerifyAsync(Lookup("00", "x"), null);
        Assert.Equal(PaymentVerificationOutcome.ReferenceMismatch, r.Outcome);
    }

    [Fact]
    public async Task NotPaid_WhenAmountDiffers()
    {
        var r = await PaymentVerifier.VerifyAsync(Lookup("00", "123456789012", 500), "123456789012", 1000m);
        Assert.Equal(PaymentVerificationOutcome.NotPaid, r.Outcome);
    }
}
