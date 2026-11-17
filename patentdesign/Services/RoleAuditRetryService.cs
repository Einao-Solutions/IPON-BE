namespace patentdesign.Services;

public class RoleAuditRetryService(IServiceScopeFactory scopeFactory, ILogger<RoleAuditRetryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var roles = scope.ServiceProvider.GetRequiredService<RoleManagementService>();
                    await roles.RetryPendingRoleHistoryAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Role audit retry failed; retrying on the next tick");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Role audit retry stopped");
        }
    }
}
