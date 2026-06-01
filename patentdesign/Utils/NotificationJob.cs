using patentdesign.Services;

namespace patentdesign.Utils
{
    public class NotificationJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NotificationJob> _log;
        private readonly TimeSpan _period = TimeSpan.FromHours(24);

        public NotificationJob(IServiceScopeFactory scopeFactory, ILogger<NotificationJob> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("NotificationJob started. Runs every {Hours}h", _period.TotalHours);

            using var timer = new PeriodicTimer(_period);

            // Run once on startup, then every 24 hours
            do
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var notificationService = scope.ServiceProvider.GetRequiredService<NotificationServices>();

                    var count = await notificationService.RenewalNotifications();
                    _log.LogInformation("NotificationJob completed. {Count} renewal notifications sent", count);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "NotificationJob failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
