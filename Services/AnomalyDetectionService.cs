using MachineStatusUpdate.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MachineStatusUpdate.Services
{
    // ─── Models ──────────────────────────────────────────────────────────────────

    public class AnomalyResult
    {
        public string Operation   { get; set; } = "";
        public string IssCode     { get; set; } = "";
        public string ErrorName   { get; set; } = "";
        public string Shift       { get; set; } = "";
        public AnomalyType     Type     { get; set; }
        public AnomalySeverity Severity { get; set; }
        public string InsightText { get; set; } = "";
        public double TodayMinutes  { get; set; }
        public double TodayRate     { get; set; }
        public double HistoryMean   { get; set; }
        public double ZScore        { get; set; }
        public int    RecentCount   { get; set; }
        public int    PrevCount     { get; set; }
        public DateTime DetectedAt  { get; set; } = DateTime.Now;
    }

    public enum AnomalyType    { Spike, Frequency, Severity }
    public enum AnomalySeverity { Warning, Critical }

    // ─── Interface ───────────────────────────────────────────────────────────────

    public interface IAnomalyDetectionService
    {
        Task<List<AnomalyResult>> DetectTodayAnomaliesAsync();
    }

    // ─── Implementation ──────────────────────────────────────────────────────────

    public class AnomalyDetectionService : IAnomalyDetectionService
    {
        private readonly ApplicationDbContext _context;

        public AnomalyDetectionService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<AnomalyResult>> DetectTodayAnomaliesAsync()
        {
            var results = new List<AnomalyResult>();

            // Gọi Stored Procedure
            var conn = _context.Database.GetDbConnection();
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText    = "EXEC [dbo].[SVN_DetectAnomalies]";
            cmd.CommandTimeout = 60;

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                // Đọc dữ liệu từ store procedure
                string op        = reader["Operation"].ToString()  ?? "";
                string issCode   = reader["IssCode"].ToString()    ?? "";
                string errorName = reader["ErrorName"].ToString()  ?? "";
                string shift     = reader["Shift"].ToString()      ?? "";

                double todayMins     = Convert.ToDouble(reader["TodayTotalMinutes"]);
                double todayRate     = Convert.ToDouble(reader["TodayRatePct"]);
                double mean30        = Convert.ToDouble(reader["Mean30"]);
                double std30         = Convert.ToDouble(reader["Std30"]);
                double zScore        = Convert.ToDouble(reader["ZScore"]);
                double pctDiff       = Convert.ToDouble(reader["PctDiffFromMean"]);
                int    histDayCount  = Convert.ToInt32(reader["HistoryDayCount"]);
                bool   isNewError    = Convert.ToInt32(reader["IsNewError"]) == 1;

                double freqRatio     = Convert.ToDouble(reader["FreqRatio"]);
                int    recentCount   = Convert.ToInt32(reader["RecentCount"]);
                int    prevCount     = Convert.ToInt32(reader["PrevCount"]);
                int    prodDaysRecent= Convert.ToInt32(reader["ProdDaysRecent"]);
                int    prodDaysPrev  = Convert.ToInt32(reader["ProdDaysPrev"]);

                double sevMean       = Convert.ToDouble(reader["SevMean"]);
                double zScoreSev     = Convert.ToDouble(reader["ZScoreSev"]);
                double pctDiffSev    = Convert.ToDouble(reader["PctDiffSev"]);
                int    sevEventCount = Convert.ToInt32(reader["SevEventCount"]);
                double todayAvgPerEvent = Convert.ToDouble(reader["TodayAvgPerEvent"]);

                double zWarning      = Convert.ToDouble(reader["ZWarningThreshold"]);
                double zCritical     = Convert.ToDouble(reader["ZCriticalThreshold"]);
                double freqThreshold = Convert.ToDouble(reader["FreqRatioThreshold"]);
                double sevZThreshold = Convert.ToDouble(reader["SevZThreshold"]);
                int    minHistDays   = Convert.ToInt32(reader["MinHistoryDays"]);

                // Layer 0 : Lỗi lần đầu tiên được phát hiện
                if (isNewError && todayMins > 0)
                {
                    results.Add(new AnomalyResult
                    {
                        Operation   = op, IssCode = issCode, ErrorName = errorName, Shift = shift,
                        Type        = AnomalyType.Spike,
                        Severity    = AnomalySeverity.Warning,
                        TodayMinutes = todayMins, TodayRate = todayRate,
                        InsightText = $"Lỗi \"{errorName}\" ({issCode}) xuất hiện lần đầu tiên" +
                                      $" tại operation {op} trong {shift} hôm nay ({FormatMin(todayMins)})." +
                                      $" Chưa có lịch sử trước đó — cần theo dõi và xác định nguyên nhân."
                    });
                    continue;
                }

                // Layer 1 :  Phát hiện giá trị hôm nay tăng bất thường so với phân bố 30 ngày gần nhất
                if (todayMins > 0 && histDayCount >= minHistDays && std30 > 0.1 && zScore >= zWarning)
                {
                    var severity = zScore >= zCritical ? AnomalySeverity.Critical : AnomalySeverity.Warning;
                    int pct      = (int)Math.Round(pctDiff);
                    double meanRate = mean30 / Convert.ToDouble(reader["ShiftTotalMinutes"]) * 100;

                    results.Add(new AnomalyResult
                    {
                        Operation = op, IssCode = issCode, ErrorName = errorName, Shift = shift,
                        Type      = AnomalyType.Spike, Severity = severity,
                        TodayMinutes = todayMins, TodayRate = todayRate,
                        HistoryMean = mean30, ZScore = Math.Round(zScore, 2),
                        InsightText = severity == AnomalySeverity.Critical
                            ? $"Thời gian dừng do {errorName} trong {shift} hôm nay là {FormatMin(todayMins)}" +
                              $" ({todayRate:F0}% thời gian ca), cao hơn bình thường {pct}%" +
                              $" — trung bình 30 ngày chỉ {FormatMin(mean30)} ({meanRate:F0}% ca)." +
                              $" Cần xử lý ngay tại operation {op}."
                            : $"Thời gian dừng do {errorName} trong {shift} hôm nay là {FormatMin(todayMins)}" +
                              $" ({todayRate:F0}% thời gian ca), cao hơn bình thường {pct}%" +
                              $" so với trung bình 30 ngày ({FormatMin(mean30)}). Theo dõi thêm tại operation {op}."
                    });
                }

                // Layer 2 : Phát hiện tần suất lỗi tăng bất thường so với tháng trước
                if (freqRatio >= freqThreshold && prevCount >= 3)
                {
                    int pctInc = (int)Math.Round((freqRatio - 1) * 100);
                    results.Add(new AnomalyResult
                    {
                        Operation = op, IssCode = issCode, ErrorName = errorName, Shift = shift,
                        Type      = AnomalyType.Frequency, Severity = AnomalySeverity.Warning,
                        RecentCount = recentCount, PrevCount = prevCount,
                        InsightText = $"Tần suất dừng do {errorName} ({shift}) tại operation {op} tăng {pctInc}%" +
                                      $" so với tháng trước — {recentCount} lần/{prodDaysRecent} ngày" +
                                      $" so với {prevCount} lần/{prodDaysPrev} ngày." +
                                      $" Lỗi đang lặp lại nhiều hơn bình thường."
                    });
                }

                // Layer 3 : Phát hiện mức độ nghiêm trọng của lỗi tăng bất thường so với lịch sử gần đây
                if (todayMins > 0 && sevEventCount >= minHistDays && zScoreSev >= sevZThreshold)
                {
                    int pctSev = (int)Math.Round(pctDiffSev);
                    results.Add(new AnomalyResult
                    {
                        Operation = op, IssCode = issCode, ErrorName = errorName, Shift = shift,
                        Type      = AnomalyType.Severity, Severity = AnomalySeverity.Warning,
                        TodayMinutes = todayAvgPerEvent, HistoryMean = sevMean,
                        ZScore       = Math.Round(zScoreSev, 2),
                        InsightText = $"Mỗi lần dừng do {errorName} trong {shift} hôm nay mất trung bình {FormatMin(todayAvgPerEvent)}" +
                                      $", lâu hơn bình thường {pctSev}%" +
                                      $" (lịch sử gần đây: {FormatMin(sevMean)} mỗi lần)." +
                                      $" Thời gian xử lý tại operation {op} đang kéo dài hơn trước."
                    });
                }
            }

            // Khi có trạng thái Stop của lỗi : Nguyên liệu và Thao tác người dùng (ISS-MAT-001 / ISS-EMP-001)
            await reader.NextResultAsync();
            while (await reader.ReadAsync())
            {
                string op          = reader["Operation"].ToString()        ?? "";
                string issCode     = reader["IssCode"].ToString()          ?? "";
                string errorName   = reader["ErrorName"].ToString()        ?? "";
                string description = reader["StopDescription"].ToString()  ?? "";
                DateTime stopTime  = Convert.ToDateTime(reader["StopTime"]);
                int minsSinceStop  = Convert.ToInt32(reader["MinutesSinceStop"]);

                // Dùng Description nếu có, không thì dùng tên lỗi mặc định
                string reason = !string.IsNullOrWhiteSpace(description)
                    ? description.ToLower()
                    : (issCode == "ISS-MAT-001" ? "thiếu nguyên liệu" : "thao tác công nhân");

                results.Add(new AnomalyResult
                {
                    Operation    = op,
                    IssCode      = issCode,
                    ErrorName    = errorName,
                    Shift        = "",
                    Type         = AnomalyType.Spike,
                    Severity     = AnomalySeverity.Critical,
                    TodayMinutes = minsSinceStop,
                    InsightText  = $"{op} đang dừng vì {reason} vào lúc {stopTime.Hour}h {stopTime:dd/MM/yyyy}."
                });
            }

            await conn.CloseAsync();

            return results
                .OrderByDescending(x => x.Severity)
                .ThenBy(x => x.Shift)
                .ThenBy(x => x.Operation)
                .ToList();
        }

        // ─── Helper ──────────────────────────────────────────────────────────────
        private static string FormatMin(double minutes)
        {
            int h = (int)(minutes / 60);
            int m = (int)(minutes % 60);
            return h > 0 ? $"{h} giờ {m} phút" : $"{m} phút";
        }
    }
}