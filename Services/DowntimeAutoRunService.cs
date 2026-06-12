using MachineStatusUpdate.Models;
using Microsoft.EntityFrameworkCore;

namespace MachineStatusUpdate.Services
{
    public class DowntimeAutoRunService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DowntimeAutoRunService> _logger;

        public DowntimeAutoRunService(IServiceScopeFactory scopeFactory,
                                      ILogger<DowntimeAutoRunService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await ProcessAutoRuns(); }
                catch (Exception ex) { _logger.LogError(ex, "DowntimeAutoRunService error"); }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task ProcessAutoRuns()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.Now;

            // ✅ Chỉ lấy Stop có AutoRunEnabled = true
            var pendingStops = await context.SVN_Downtime_Infos
                .Where(x => x.State == "Stop"
                         && x.AutoRunEnabled == true
                         && x.Datetime.HasValue
                         && x.Datetime.Value.Date == now.Date
                         && !string.IsNullOrEmpty(x.EstimateTime))
                .OrderBy(x => x.Datetime)
                .ToListAsync();

            foreach (var stop in pendingStops)
            {
                // Parse EstimateTime (HH:mm)
                if (!TimeSpan.TryParse(stop.EstimateTime, out var estimateSpan))
                {
                    _logger.LogWarning("Không parse được EstimateTime={ET} cho Id={Id}", stop.EstimateTime, stop.Id);
                    continue;
                }

                // Thời điểm dự kiến Run = ngày của Stop + giờ EstimateTime
                var runAt = stop.Datetime!.Value.Date
                    .AddHours(estimateSpan.Hours)
                    .AddMinutes(estimateSpan.Minutes);

                // Chưa đến giờ → bỏ qua
                if (now < runAt)
                    continue;

                // Đã có Run sau record Stop này chưa?
                var alreadyRun = await context.SVN_Downtime_Infos
                    .AnyAsync(x => x.State == "Run"
                               && x.Operation == stop.Operation
                               && x.SVNCode == stop.SVNCode
                               && x.Datetime.HasValue
                               && x.Datetime.Value > stop.Datetime.Value);

                if (alreadyRun)
                    continue;

                // ✅ Insert Run tự động
                var runRecord = new SVN_Downtime_Info
                {
                    Code          = stop.Code,
                    Name          = stop.Name,
                    SVNCode       = stop.SVNCode,
                    Operation     = stop.Operation,
                    ISS_Code      = stop.ISS_Code,
                    State         = "Run",
                    Datetime      = runAt,
                    EstimateTime  = string.Empty,
                    AutoRunEnabled = false,
                    Description   = string.IsNullOrWhiteSpace(stop.AutoRunDescription) ? null : stop.AutoRunDescription,
                    Image         = string.Empty
                };

                context.SVN_Downtime_Infos.Add(runRecord);
                await context.SaveChangesAsync();

                _logger.LogInformation(
                    "Auto-Run inserted: Op={Op} SVN={Code} at {At}",
                    stop.Operation, stop.SVNCode, runAt);
            }
        }
    }
}