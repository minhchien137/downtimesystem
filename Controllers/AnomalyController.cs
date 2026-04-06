using MachineStatusUpdate.Services;
using Microsoft.AspNetCore.Mvc;

namespace MachineStatusUpdate.Controllers
{
    public class AnomalyController : Controller
    {
        private readonly IAnomalyDetectionService _anomalyService;

        public AnomalyController(IAnomalyDetectionService anomalyService)
        {
            _anomalyService = anomalyService;
        }

        /// <summary>
        /// Trang chính — /Anomaly/Index
        /// Load view rỗng, JS sẽ gọi /Anomaly/CheckToday để lấy data.
        /// </summary>
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// API endpoint — trả JSON danh sách anomalies hôm nay.
        /// Gọi: GET /Anomaly/CheckToday
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CheckToday()
        {
            try
            {
                var anomalies = await _anomalyService.DetectTodayAnomaliesAsync();

                var result = anomalies.Select(a => new
                {
                    operation   = a.Operation,
                    issCode     = a.IssCode,
                    errorName   = a.ErrorName,
                    type        = a.Type.ToString(),        // "Spike" | "Frequency" | "Severity"
                    severity    = a.Severity.ToString(),    // "Warning" | "Critical"
                    insightText = a.InsightText,
                    detectedAt  = a.DetectedAt.ToString("HH:mm dd/MM/yyyy")
                });

                return Json(new { success = true, count = anomalies.Count, data = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Lỗi phát hiện bất thường: {ex.Message}" });
            }
        }
    }
}
