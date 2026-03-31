using patentdesign.Services;

namespace patentdesign.Utils
{
    public class PublishTrademarkJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PublishTrademarkJob> _log;
        private readonly TimeSpan _period = TimeSpan.FromHours(24);

        public PublishTrademarkJob(IServiceScopeFactory scopeFactory, ILogger<PublishTrademarkJob> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("PublishTrademarkJob started. Runs every {Hours}h", _period.TotalHours);

            using var timer = new PeriodicTimer(_period);

            // Run once on startup, then every 24 hours
            do
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var publicationService = scope.ServiceProvider.GetRequiredService<PublicationServices>();

                    var count = await publicationService.PublishTrademarks();
                    _log.LogInformation("PublishTrademarkJob completed. {Count} trademarks published", count);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "PublishTrademarkJob failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
