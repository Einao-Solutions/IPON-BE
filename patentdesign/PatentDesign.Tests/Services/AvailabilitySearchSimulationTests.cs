using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using patentdesign.Controllers;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using Xunit;
using ApplicationStatuses = patentdesign.Models.ApplicationStatuses;

namespace patentdesign.Tests.Services;

public sealed class SimDevFixture() : AvsFlowFixture(true, "Development");
public sealed class SimProdFixture() : AvsFlowFixture(true, "Production");

public class AvailabilitySearchSimulationTests(SimDevFixture dev) : IClassFixture<SimDevFixture>
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Fact]
    public async Task Simulation_InDevelopment_ApprovesWithoutRemita_AndWritesE2EJson()
    {
        var user = await dev.CreateUser();
        var cost = (ObjectResult)await dev.FilesController(user).AvailabilitySearchCost("n", "e@x.com", user, "acme");
        var rrr = ((AvailabilitySearchDto)cost.Value!).rrr;
        var before = JsonSerializer.Serialize(((ObjectResult)await dev.UsersController(user).GetOtherApplications(user)).Value, Opts);
        var confirm = (ObjectResult)await dev.FilesController(user).UpdateAvailabilitySearchPayment(
            rrr, null, null!, dev.Users, null!, NullLogger<FilesController>.Instance);
        var after = JsonSerializer.Serialize(((ObjectResult)await dev.UsersController(user).GetOtherApplications(user)).Value, Opts);

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "avs_e2e.txt"),
            $"1) COST\n{JsonSerializer.Serialize(cost.Value, Opts)}\n\n2) LIST BEFORE\n{before}\n\n" +
            $"3) CONFIRM ({confirm.StatusCode ?? 200})\n{JsonSerializer.Serialize(confirm.Value, Opts)}\n\n4) LIST AFTER\n{after}\n");

        Assert.Equal(200, confirm.StatusCode ?? 200);
        Assert.Equal(0, dev.Remita.VerifyCalls);
        Assert.Contains("\"currentStatus\": " + (int)ApplicationStatuses.AwaitingPayment, before);
        Assert.Contains("\"currentStatus\": " + (int)ApplicationStatuses.AutoApproved, after);
        Assert.Contains("Payment simulated (test mode)", after);
    }
}

public class AvailabilitySearchSimulationProductionTests(SimProdFixture prod) : IClassFixture<SimProdFixture>
{
    [Fact]
    public async Task Simulation_InProduction_IsIgnored_EvenWhenFlagSet()
    {
        var user = await prod.CreateUser();
        var cost = (AvailabilitySearchDto)((ObjectResult)await prod.FilesController(user).AvailabilitySearchCost("n", "e", user, "t")).Value!;
        var res = (ObjectResult)await prod.FilesController(user).UpdateAvailabilitySearchPayment(
            cost.rrr, null, null!, prod.Users, null!, NullLogger<FilesController>.Instance);

        Assert.Equal(400, res.StatusCode);
        Assert.Equal(1, prod.Remita.VerifyCalls);
    }
}
