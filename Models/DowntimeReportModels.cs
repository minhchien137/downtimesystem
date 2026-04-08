namespace MachineStatusUpdate.Models
{
    public class DowntimeReportByOperationError
    {
        public string Operation { get; set; }
        public string ISS_Code { get; set; }
        public string ErrorName { get; set; }
        public int DowntimeCount { get; set; }
        public double TotalDowntimeMinutes { get; set; }
        public string TotalDowntimeFormatted { get; set; }
    }

    // Model tổng hợp theo Operation
    public class DowntimeReportByOperation
    {
        public string Operation { get; set; }
        public int TotalDowntimeCount { get; set; }
        public double TotalDowntimeMinutes { get; set; }
        public string TotalDowntimeFormatted { get; set; }
        public List<DowntimeReportByOperationError> ErrorDetails { get; set; }
    }

    // Model cho Chart data
    public class DowntimeChartData
    {
        public List<string> Operations { get; set; }
        public List<double> DowntimeMinutes { get; set; }
        public List<ChartErrorBreakdown> ErrorBreakdowns { get; set; }
    }

    public class ChartErrorBreakdown
    {
        public string Operation { get; set; }
        public List<string> ErrorNames { get; set; }
        public List<double> Minutes { get; set; }
    }

    public class IssCodeChartData
    {
        public List<string> IssCodeLabels { get; set; }
        public List<double> DowntimeMinutes { get; set; }
        public List<int> DowntimeCounts { get; set; }
    }

    public class DailyDowntimeChartData
    {
        public List<string> Dates { get; set; }
        public List<double> DowntimeMinutes { get; set; }
        public List<int> DowntimeCounts { get; set; }
    }

    // ── MỚI: Model báo cáo theo Line kèm % so với running time ──
    public class DowntimeReportByOperationWithPct : DowntimeReportByOperation
    {
        /// <summary>Thời gian chạy của line (phút)</summary>
        public double RunningTimeMinutes { get; set; }
        public string RunningTimeFormatted { get; set; }
        /// <summary>% Downtime / Running time</summary>
        public double DowntimePct { get; set; }
    }

    // ── MỚI: Model báo cáo theo EQ (Machine Code) ──
    public class DowntimeReportByMachine
    {
        public string MachineCode { get; set; }
        public string Operation { get; set; }
        public int DowntimeCount { get; set; }
        public double TotalDowntimeMinutes { get; set; }
        public string TotalDowntimeFormatted { get; set; }
        public List<DowntimeReportByMachineReason> ReasonDetails { get; set; }
    }

    public class DowntimeReportByMachineReason
    {
        public string MachineCode { get; set; }
        public string ReasonCode { get; set; }
        public string ReasonName { get; set; }
        public int DowntimeCount { get; set; }
        public double TotalDowntimeMinutes { get; set; }
        public string TotalDowntimeFormatted { get; set; }
    }

    // ── MỚI: Chart data cho EQ ──
    public class MachineDowntimeChartData
    {
        public List<string> MachineCodes { get; set; }
        public List<double> DowntimeMinutes { get; set; }
        public List<int> DowntimeCounts { get; set; }
    }
}
