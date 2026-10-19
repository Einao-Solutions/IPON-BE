namespace patentdesign.Services;

public class TicketAutoCloseService(IServiceScopeFactory scopeFactory, ILogger<TicketAutoCloseService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var tickets = scope.ServiceProvider.GetRequiredService<TicketServices>();
                    var closed = await tickets.CloseResolvedTicketsAsync(stoppingToken);
                    if (closed > 0)
                    {
                        logger.LogInformation("Automatically closed {Count} resolved tickets", closed);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ticket auto-closure failed; retrying on the next tick");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Ticket auto-closure stopped");
        }
    }
}
