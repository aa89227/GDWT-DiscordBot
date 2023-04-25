using DiscordBot.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

internal class ScheduledService : BackgroundService
{
    private readonly ILogger<ScheduledService> _logger;
    private readonly MongoKogRepository _repository;

    public ScheduledService(ILogger<ScheduledService> logger, MongoKogRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextMidnight = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Local).AddDays(1);
            var delay = nextMidnight - now;
            _logger.LogInformation("{nextMidnight} 時抓取資料", nextMidnight.ToString());
            await Task.Delay(delay, stoppingToken);

            // 在晚上 12 點執行的程式碼
            _logger.LogInformation("午夜了，開始抓取Kog資料");
            await _repository.UpdateAllUserData();
            await _repository.UpdateMapData();
        }
    }
}