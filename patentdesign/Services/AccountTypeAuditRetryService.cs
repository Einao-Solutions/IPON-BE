namespace patentdesign.Services;

public class AccountTypeAuditRetryService(IServiceScopeFactory scopeFactory, ILogger<AccountTypeAuditRetryService> logger) : BackgroundService
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
                    var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
                    await accounts.RetryPendingAccountTypeHistoryAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Account type audit retry failed; retrying on the next tick");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Account type audit retry stopped");
        }
    }
}
