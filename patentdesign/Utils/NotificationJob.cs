using patentdesign.Services;

namespace patentdesign.Utils
{
    public class NotificationJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NotificationJob> _log;
        private readonly TimeSpan _period = TimeSpan.FromMinutes(1);

        public NotificationJob(IServiceScopeFactory scopeFactory, ILogger<NotificationJob> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("NotificationJob started. Email retries run every {Minutes} minute", _period.TotalMinutes);

            using var timer = new PeriodicTimer(_period);
            var nextRenewalCheck = DateTime.MinValue;

            try
            {
                do
                {
                    using var scope = _scopeFactory.CreateScope();
                    var notificationService = scope.ServiceProvider.GetRequiredService<NotificationServices>();

                    try
                    {
                        var sent = await notificationService.RetryPendingEmailsAsync(stoppingToken);
                        _log.LogInformation("Pending email processing completed. {Count} emails sent", sent);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Pending email processing failed; retrying on the next tick");
                    }

                    if (DateTime.UtcNow >= nextRenewalCheck && !stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            var count = await notificationService.RenewalNotifications();
                            nextRenewalCheck = DateTime.UtcNow.Date.AddDays(1);
                            _log.LogInformation("Renewal scan completed. {Count} eligible files processed", count);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Renewal scan failed; retrying on the next tick");
                        }
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _log.LogInformation("NotificationJob stopped");
            }
        }
    }
}
