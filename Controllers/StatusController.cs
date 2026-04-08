using System.Drawing;
using ClosedXML.Excel;
using MachineStatusUpdate.Models;
using MachineStatusUpdate.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using MachineStatusUpdate.Hubs;
using ZXing;


namespace MachineStatusUpdate.Controllers
{
    public class StatusController : Controller
    {

        private readonly IHubContext<DowntimeHub> _hubContext;

        private readonly ApplicationDbContext _context;

        private readonly IWebHostEnvironment _webHostEnvironment;

        public StatusController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, IHubContext<DowntimeHub> hubContext)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _hubContext = hubContext;

        }


        // ============================================================
        // GET: GetLatestDowntimeForOperation
        // Trả về bản ghi downtime mới nhất trong ngày theo Operation
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> GetLatestDowntimeForOperation(string operation)
        {
            if (string.IsNullOrWhiteSpace(operation))
                return Json(new { exists = false });

            var op = operation.Trim();
            var today = DateTime.Now.Date;

            var latest = await _context.SVN_Downtime_Infos_Devel
                .Where(x => x.Operation != null
                            && x.Operation.Trim() == op
                            && x.Datetime.HasValue
                            && x.Datetime.Value.Date == today)
                .OrderByDescending(x => x.Datetime)
                .Select(x => new
                {
                    state  = (x.State  ?? "").Trim(),
                    reason = (x.Reason ?? "").Trim()
                })
                .FirstOrDefaultAsync();

            if (latest == null)
                return Json(new { exists = false });

            return Json(new { exists = true, state = latest.state, reason = latest.reason });
        }


        // ============================================================
        // GET: GetLatestStopByMachine
        // Trả về bản ghi Stop gần nhất trong ngày theo MachineCode
        // Dùng để auto-fill form khi chọn Machine lúc nhấn Run
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> GetLatestStopByMachine(string machineNo)
        {
            if (string.IsNullOrWhiteSpace(machineNo))
                return Json(new { exists = false });

            var latest = await _context.SVN_Downtime_Infos_Devel
                .Where(x => x.MachineCode != null
                            && x.MachineCode.Trim() == machineNo.Trim()
                            && x.State != null
                            && x.State.Trim().ToUpper() == "STOP"
                            && x.Datetime.HasValue)
                .OrderByDescending(x => x.Datetime)
                .Select(x => new
                {
                    employeeCode = (x.EmployeeCode ?? "").Trim(),
                    employeeName = (x.EmployeeName ?? "").Trim(),
                    operation    = (x.Operation    ?? "").Trim(),
                    opValue      = (x.Location     ?? "").Trim(),
                    reason       = (x.Reason       ?? "").Trim(),
                    effect       = (x.Effect       ?? "").Trim(),
                    description  = (x.Description  ?? "").Trim()
                })
                .FirstOrDefaultAsync();

            if (latest == null)
                return Json(new { exists = false });

            return Json(new { exists = true, data = latest });
        }


        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }


        /*============================================================ Downtime ========================================================================**/


        /* Hàm GET Nhập Downtime */
        [HttpGet]
        public async Task<IActionResult> CreateDownTime()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");

            // 生产线 / Production Line
            var line = await _context.SVN_targets
                .AsNoTracking()
                .Where(x => x.Date_time == today && x.Operation != null && x.Operation != "" && x.Operation.Contains("(SM)"))
                .Select(x => x.Operation)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            ViewBag.OperationOptions = line;

            // 原因 (Reason)
            var rea = await _context.SVN_Downtime_Reasons
                .AsNoTracking()
                .OrderBy(r => r.Reason_Name)
                .Select(r => new { r.Reason_Code, r.Reason_Name })
                .ToListAsync();

            ViewBag.ReasonOptions = rea;

            // 员工工号 / Employee ID no.
            var employees = await _context.SM_EmployInfos
                .AsNoTracking()
                .OrderBy(e => e.EnglishName)
                .Select(e => new { e.Id, e.ChineseName, e.EnglishName })
                .ToListAsync();

            ViewBag.Employees = employees;

            // 操作 / Location
            var operation = await _context.SM_Operations
                .AsNoTracking()
                .Where(x => x.Operation != null && x.Operation != "")
                .Select(x => x.Operation)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            ViewBag.Operations = operation;

            return View("CreateDownTime");
        }

        /* Hàm GET Lịch sử Downtime */
        [HttpGet]
        public async Task<IActionResult> DowntimeList(
            string operation = "",
            string fromDate  = "",
            string toDate    = "",
            string station   = "",   // ← MỚI
            string machine   = "",   // ← MỚI
            string location  = "",   // ← MỚI
            string employee  = "",   // ← MỚI
            string reason    = "",   // ← MỚI
            string effect    = "",   // ← MỚI
            int page         = 1,
            int pageSize     = 25)
        {
            try
            {
                var query = from d in _context.SVN_Downtime_Infos_Devel
                            join r in _context.SVN_Downtime_Reasons
                            on d.Reason equals r.Reason_Code into reasons
                            from r in reasons.DefaultIfEmpty()
                            select new SVN_Downtime_Info_Devel
                            {
                                Id           = d.Id,
                                Code         = d.Code,
                                Name         = d.Name,
                                EmployeeCode = d.EmployeeCode,
                                EmployeeName = d.EmployeeName,
                                MachineCode  = d.MachineCode,
                                Location     = d.Location,
                                Operation    = d.Operation,
                                State        = d.State,
                                Reason       = d.Reason,
                                ErrorName    = r != null ? r.Reason_Name : "",
                                Effect       = d.Effect,
                                Station      = d.Station,
                                Description  = d.Description,
                                Action       = d.Action,
                                RootCause    = d.RootCause,
                                SpareParts   = d.SpareParts,
                                Datetime     = d.Datetime,
                                EstimateTime = d.EstimateTime,
                                Image        = d.Image
                            };

                // ----- Filter cố định: chỉ lấy SM -----
                query = query.Where(x => x.Operation != null && x.Operation.Contains("(SM)"));

                // ----- Filters cũ -----
                if (!string.IsNullOrWhiteSpace(operation))
                {
                    var op = operation.Trim();
                    query = query.Where(x => x.Operation != null && x.Operation.Contains(op));
                }

                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);

                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

                // ----- Filters mới -----
                if (!string.IsNullOrWhiteSpace(station))
                    query = query.Where(x => x.Station != null && x.Station == station.Trim());

                if (!string.IsNullOrWhiteSpace(machine))
                    query = query.Where(x => x.MachineCode != null && x.MachineCode == machine.Trim());

                if (!string.IsNullOrWhiteSpace(location))
                    query = query.Where(x => x.Location != null && x.Location == location.Trim());

                if (!string.IsNullOrWhiteSpace(employee))
                    query = query.Where(x => x.EmployeeCode != null && x.EmployeeCode == employee.Trim());

                if (!string.IsNullOrWhiteSpace(reason))
                    query = query.Where(x => x.Reason != null && x.Reason == reason.Trim());

                if (!string.IsNullOrWhiteSpace(effect))
                    query = query.Where(x => x.Effect != null && x.Effect == effect.Trim());

                // ----- Lấy toàn bộ kết quả đã filter (không paginate) để build dropdown -----
                var allFilteredData = await query
                    .OrderByDescending(x => x.Datetime)
                    .ThenBy(x => x.Operation)
                    .ToListAsync();

                var totalRecords = allFilteredData.Count;
                var totalPages   = (int)Math.Ceiling((double)totalRecords / pageSize);

                // ----- Paginate từ allFilteredData -----
                var results = allFilteredData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // ----- ViewBag: dropdown options lấy distinct từ toàn bộ kết quả đã filter -----
                ViewBag.OperationOptions = allFilteredData
                    .Where(x => !string.IsNullOrEmpty(x.Operation))
                    .Select(x => x.Operation!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                ViewBag.StationOptions = allFilteredData
                    .Where(x => !string.IsNullOrEmpty(x.Station))
                    .Select(x => x.Station!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                ViewBag.MachineOptions = allFilteredData
                    .Where(x => !string.IsNullOrEmpty(x.MachineCode))
                    .Select(x => x.MachineCode!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                ViewBag.LocationOptions = allFilteredData
                    .Where(x => !string.IsNullOrEmpty(x.Location))
                    .Select(x => x.Location!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                ViewBag.EmployeeOptions = allFilteredData
                    .Where(x => !string.IsNullOrEmpty(x.EmployeeCode))
                    .Select(x => x.EmployeeCode!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                ViewBag.ReasonOptions = allFilteredData
                    .Where(x => !string.IsNullOrEmpty(x.Reason))
                    .Select(x => x.Reason!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                ViewBag.EffectOptions = allFilteredData
                    .Where(x => !string.IsNullOrEmpty(x.Effect))
                    .Select(x => x.Effect!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                // ----- ViewBag: giá trị filter hiện tại để giữ state -----
                ViewBag.Operation       = operation  ?? "";
                ViewBag.FromDate        = fromDate   ?? "";
                ViewBag.ToDate          = toDate     ?? "";
                ViewBag.Station         = station    ?? "";
                ViewBag.Machine         = machine    ?? "";
                ViewBag.Location        = location   ?? "";
                ViewBag.Employee        = employee   ?? "";
                ViewBag.Reason          = reason     ?? "";
                ViewBag.Effect          = effect     ?? "";
                ViewBag.CurrentPage     = page;
                ViewBag.TotalPages      = totalPages;
                ViewBag.PageSize        = pageSize;
                ViewBag.TotalRecords    = totalRecords;
                ViewBag.HasPreviousPage = page > 1;
                ViewBag.HasNextPage     = page < totalPages;

                return View(results);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage    = $"Lỗi: {ex.Message}";
                ViewBag.Operation       = operation  ?? "";
                ViewBag.FromDate        = fromDate   ?? "";
                ViewBag.ToDate          = toDate     ?? "";
                ViewBag.Station         = station    ?? "";
                ViewBag.Machine         = machine    ?? "";
                ViewBag.Location        = location   ?? "";
                ViewBag.Employee        = employee   ?? "";
                ViewBag.Reason          = reason     ?? "";
                ViewBag.Effect          = effect     ?? "";
                ViewBag.CurrentPage     = 1;
                ViewBag.TotalPages      = 0;
                ViewBag.PageSize        = pageSize;
                ViewBag.TotalRecords    = 0;
                ViewBag.HasPreviousPage = false;
                ViewBag.HasNextPage     = false;
                // Trả về ViewBag options rỗng để tránh lỗi null trong View
                ViewBag.OperationOptions = new List<string>();
                ViewBag.StationOptions   = new List<string>();
                ViewBag.MachineOptions   = new List<string>();
                ViewBag.LocationOptions  = new List<string>();
                ViewBag.EmployeeOptions  = new List<string>();
                ViewBag.ReasonOptions    = new List<string>();
                ViewBag.EffectOptions    = new List<string>();
                return View(new List<SVN_Downtime_Info_Devel>());
            }
        }

        /* Xuất Excel DowntimeList */
        public async Task<IActionResult> ExportDowntimeListToExcel(
            string operation = "",
            string fromDate  = "",
            string toDate    = "",
            string station   = "",   // ← MỚI
            string machine   = "",   // ← MỚI
            string location  = "",   // ← MỚI
            string employee  = "",   // ← MỚI
            string reason    = "",   // ← MỚI
            string effect    = "")   // ← MỚI
        {
            try
            {
                var query = from d in _context.SVN_Downtime_Infos_Devel
                            join r in _context.SVN_Downtime_Reasons
                            on d.Reason equals r.Reason_Code into reasons
                            from r in reasons.DefaultIfEmpty()
                            select new SVN_Downtime_Info_Devel
                            {
                                Id           = d.Id,
                                Code         = d.Code,
                                Name         = d.Name,
                                EmployeeCode = d.EmployeeCode,
                                EmployeeName = d.EmployeeName,
                                MachineCode  = d.MachineCode,
                                Location     = d.Location,
                                Operation    = d.Operation,
                                State        = d.State,
                                Reason       = d.Reason,
                                ErrorName    = r != null ? r.Reason_Name : "",
                                Effect       = d.Effect,
                                Station      = d.Station,
                                Description  = d.Description,
                                Action       = d.Action,
                                RootCause    = d.RootCause,
                                SpareParts   = d.SpareParts,
                                Datetime     = d.Datetime,
                                EstimateTime = d.EstimateTime,
                                Image        = d.Image
                            };

                query = query.Where(x => x.Operation != null && x.Operation.Contains("(SM)"));

                if (!string.IsNullOrWhiteSpace(operation))
                {
                    var op = operation.Trim();
                    query = query.Where(x => x.Operation != null && x.Operation.Contains(op));
                }

                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);

                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

                if (!string.IsNullOrWhiteSpace(station))
                    query = query.Where(x => x.Station != null && x.Station == station.Trim());

                if (!string.IsNullOrWhiteSpace(machine))
                    query = query.Where(x => x.MachineCode != null && x.MachineCode == machine.Trim());

                if (!string.IsNullOrWhiteSpace(location))
                    query = query.Where(x => x.Location != null && x.Location == location.Trim());

                if (!string.IsNullOrWhiteSpace(employee))
                    query = query.Where(x => x.EmployeeCode != null && x.EmployeeCode == employee.Trim());

                if (!string.IsNullOrWhiteSpace(reason))
                    query = query.Where(x => x.Reason != null && x.Reason == reason.Trim());

                if (!string.IsNullOrWhiteSpace(effect))
                    query = query.Where(x => x.Effect != null && x.Effect == effect.Trim());

                var data = await query
                    .OrderByDescending(x => x.Datetime)
                    .ThenBy(x => x.Operation)
                    .ToListAsync();

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("DowntimeList");
                    var currentRow = 1;

                    ws.Style.Font.FontName = "Times New Roman";
                    ws.Style.Font.FontSize = 11;

                    // Header theo thứ tự cột SQL
                    string[] headers = {
                        "#", "Code", "Name", "Operation", "Machine", "Location",
                        "Reason", "Error Name", "Effect", "Station", "Estimate Time",
                        "State", "Action", "Description", "Root Cause", "Spare Parts",
                        "Employee Code", "Employee Name", "Datetime", "Image"
                    };

                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = ws.Cell(currentRow, i + 1);
                        cell.Value = headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5);
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                    }

                    const double rowHeight = 70;
                    int rowIndex = 0;

                    foreach (var item in data)
                    {
                        currentRow++;
                        rowIndex++;
                        ws.Row(currentRow).Height = rowHeight;

                        string effLabel = item.Effect == "1" ? "影响生产"
                                        : item.Effect == "2" ? "不影响生产"
                                        : (item.Effect ?? "-");

                        ws.Cell(currentRow,  1).Value = rowIndex;
                        ws.Cell(currentRow,  2).Value = item.Code;
                        ws.Cell(currentRow,  3).Value = item.Name;
                        ws.Cell(currentRow,  4).Value = item.Operation;
                        ws.Cell(currentRow,  5).Value = item.MachineCode;
                        ws.Cell(currentRow,  6).Value = item.Location;
                        ws.Cell(currentRow,  7).Value = item.Reason;
                        ws.Cell(currentRow,  8).Value = item.ErrorName;
                        ws.Cell(currentRow,  9).Value = effLabel;
                        ws.Cell(currentRow, 10).Value = item.Station;
                        ws.Cell(currentRow, 11).Value = string.IsNullOrEmpty(item.EstimateTime) ? "-" : item.EstimateTime;
                        ws.Cell(currentRow, 12).Value = item.State;
                        ws.Cell(currentRow, 13).Value = item.Action;
                        ws.Cell(currentRow, 14).Value = item.Description;
                        ws.Cell(currentRow, 15).Value = item.RootCause;
                        ws.Cell(currentRow, 16).Value = item.SpareParts;
                        ws.Cell(currentRow, 17).Value = item.EmployeeCode;
                        ws.Cell(currentRow, 18).Value = item.EmployeeName;
                        ws.Cell(currentRow, 19).Value = item.Datetime?.ToString("dd/MM/yyyy HH:mm") ?? "-";

                        if (!string.IsNullOrEmpty(item.Image))
                        {
                            try
                            {
                                string imagePath = item.Image.StartsWith("/uploads/")
                                    ? Path.Combine(_webHostEnvironment.WebRootPath, item.Image.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
                                    : item.Image;

                                if (System.IO.File.Exists(imagePath))
                                {
                                    var picture = ws.AddPicture(imagePath);
                                    picture.MoveTo(ws.Cell(currentRow, 20), 8, 5);
                                    picture.WithSize(100, 70);
                                    ws.Cell(currentRow, 20).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                    ws.Cell(currentRow, 20).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                                }
                                else
                                {
                                    ws.Cell(currentRow, 20).Value = "No image";
                                    ws.Cell(currentRow, 20).Style.Font.FontColor = XLColor.Gray;
                                }
                            }
                            catch (Exception ex)
                            {
                                ws.Cell(currentRow, 20).Value = $"Error: {ex.Message}";
                                ws.Cell(currentRow, 20).Style.Font.FontColor = XLColor.Red;
                            }
                        }
                        else
                        {
                            ws.Cell(currentRow, 20).Value = "-";
                            ws.Cell(currentRow, 20).Style.Font.FontColor = XLColor.Gray;
                        }
                    }

                    ws.Columns(1, 19).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Columns(1, 19).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                    // Description, Action, RootCause, SpareParts căn trái
                    ws.Column(13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    ws.Column(14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    ws.Column(15).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    ws.Column(16).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    ws.Column(1).Width  = 6;
                    ws.Column(2).Width  = 12;
                    ws.Column(3).Width  = 15;
                    ws.Column(4).Width  = 28;
                    ws.Column(5).Width  = 22;
                    ws.Column(6).Width  = 18;
                    ws.Column(7).Width  = 14;
                    ws.Column(8).Width  = 25;
                    ws.Column(9).Width  = 15;
                    ws.Column(10).Width = 14;
                    ws.Column(11).Width = 14;
                    ws.Column(12).Width = 12;
                    ws.Column(13).Width = 28;
                    ws.Column(14).Width = 30;
                    ws.Column(15).Width = 28;
                    ws.Column(16).Width = 22;
                    ws.Column(17).Width = 15;
                    ws.Column(18).Width = 18;
                    ws.Column(19).Width = 18;
                    ws.Column(20).Width = 15;

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        return File(stream.ToArray(),
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"DowntimeList_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi ExportDowntimeListToExcel: {ex.Message}");
                return Json(new { success = false, message = $"Lỗi xuất Excel: {ex.Message}" });
            }
        }

        /* Hàm POST Nhập Downtime */
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateDownTime(SVN_Downtime_Info_Devel model, IFormFile? imageFile)
        {
            // ===== 1) Chuẩn hoá/điền mặc định =====
            if (string.IsNullOrWhiteSpace(model.Code))
                model.Code = model.EmployeeCode ?? string.Empty;

            if (string.IsNullOrWhiteSpace(model.Name))
                model.Name = model.EmployeeName ?? model.EmployeeCode ?? string.Empty;

            if (!model.Datetime.HasValue || model.Datetime.Value == default)
                model.Datetime = DateTime.Now;

            if (string.IsNullOrWhiteSpace(model.EstimateTime))
                model.EstimateTime = string.Empty;

            if (string.IsNullOrWhiteSpace(model.Description))
                model.Description = string.Empty;

            // ===== 2) Xử lý upload ảnh =====
            string imagePath = string.Empty;
            if (imageFile != null && imageFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
                var ext = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(ext))
                    return Json(new { success = false, message = "只允许上传图片: jpg, jpeg, png, gif, bmp" });

                if (imageFile.Length > 5 * 1024 * 1024)
                    return Json(new { success = false, message = "图片大小不能超过5MB" });

                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "status-images");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                    await imageFile.CopyToAsync(stream);

                imagePath = $"/uploads/status-images/{fileName}";
            }

            model.Image = imagePath;

            // ===== 3) Validate ModelState =====
            ModelState.Remove("Name");
            ModelState.Remove("Code");
            ModelState.Remove("EstimateTime");
            ModelState.Remove("Description");
            ModelState.Remove("Image");
            ModelState.Remove("EmployeeCode");
            ModelState.Remove("EmployeeName");
            ModelState.Remove("MachineCode");
            ModelState.Remove("Location");
            ModelState.Remove("Reason");
            ModelState.Remove("Effect");
            ModelState.Remove("Station");
            ModelState.Remove("Action");
            ModelState.Remove("RootCause");
            ModelState.Remove("SpareParts");

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value.Errors.Any())
                    .Select(x => $"{x.Key}: {string.Join(", ", x.Value.Errors.Select(e => e.ErrorMessage))}")
                    .ToList();
                return Json(new { success = false, message = "数据无效: " + string.Join(" | ", errors) });
            }

            // ===== 4) Lưu DB =====
            try
            {
                _context.SVN_Downtime_Infos_Devel.Add(model);
                await _context.SaveChangesAsync();

                // ===== 5) SignalR: Push real-time notification khi State = STOP =====
                var state = (model.State ?? "").Trim().ToUpper();

                if (state == "STOP")
                {
                    // ── Lưu vào bảng TechResponses để Tech thấy khi refresh ──
                    var techResp = new MachineStatusUpdate.Models.SVN_Downtime_TechResponse
                    {
                        DowntimeId       = model.Id,
                        MachineCode      = model.MachineCode  ?? "",
                        Operation        = model.Operation    ?? "",
                        EmployeeCode     = model.EmployeeCode ?? "",
                        EmployeeName     = model.EmployeeName ?? "",
                        OperatorUsername = HttpContext.Session.GetString("UserName") ?? "",
                        Reason           = model.Reason       ?? "",
                        Effect           = model.Effect       ?? "",
                        EstimateTime     = model.EstimateTime ?? "",
                        Station          = model.Station      ?? "",
                        Description      = model.Description  ?? "",
                        Location         = model.Location     ?? "",
                        StopDatetime     = model.Datetime ?? DateTime.Now,
                        TechAction       = null   // chưa xử lý
                    };
                    _context.SVN_Downtime_TechResponses.Add(techResp);
                    await _context.SaveChangesAsync();

                    await SaveNotificationAsync(
                        recipientUsername : "ALL_TECH",
                        recipientRole     : "Technical",
                        notifType         : "STOP",
                        title             : $"🛑 STOP — Máy: {model.MachineCode ?? "-"}",
                        body              : $"Operation: {model.Operation ?? "-"} | Nhân viên: {model.EmployeeName ?? model.EmployeeCode ?? "-"} | Lý do: {model.Reason ?? "-"}",
                        machineCode       : model.MachineCode,
                        operation         : model.Operation,
                        techResponseId    : techResp.Id
                    );

                    // ── Lưu notification cho Admin ──
                    await SaveNotificationAsync(
                        recipientUsername: "ALL_ADMIN",
                        recipientRole: "Admin",
                        notifType: "STOP",
                        title: $"🛑 STOP — Máy: {model.MachineCode ?? "-"}",
                        body: $"Operation: {model.Operation ?? "-"} | Nhân viên: {model.EmployeeName ?? model.EmployeeCode ?? "-"} | Lý do: {model.Reason ?? "-"}",
                        machineCode: model.MachineCode,
                        operation: model.Operation,
                        techResponseId: techResp.Id
                    );
    

                    // Gửi thông báo đến tất cả Kỹ thuật trong TechnicianGroup
                    await _hubContext.Clients.Group("TechnicianGroup").SendAsync("ReceiveStopNotification", new
                    {
                        techResponseId   = techResp.Id,
                        operation        = model.Operation    ?? "",
                        machineCode      = model.MachineCode  ?? "",
                        location         = model.Location     ?? "",
                        employeeCode     = model.EmployeeCode ?? "",
                        employeeName     = model.EmployeeName ?? "",
                        reason           = model.Reason       ?? "",
                        effect           = model.Effect       ?? "",
                        description      = model.Description  ?? "",
                        station          = model.Station      ?? "",
                        estimateTime     = model.EstimateTime ?? "",
                        datetime         = model.Datetime?.ToString("dd/MM/yyyy HH:mm") ?? "",
                        insertedId       = model.Id,
                        operatorUsername = HttpContext.Session.GetString("UserName") ?? ""
                    });
                }
                else if (state == "RUN")
                {
                    await SaveNotificationAsync(
                        recipientUsername : "ALL_TECH",
                        recipientRole     : "Technical",
                        notifType         : "RUN",
                        title             : $"✅ RUN — Máy: {model.MachineCode ?? "-"} đã chạy lại",
                        body              : $"Operation: {model.Operation ?? "-"}",
                        machineCode       : model.MachineCode,
                        operation         : model.Operation
                    );

                    await SaveNotificationAsync(
                        recipientUsername: "ALL_ADMIN",
                        recipientRole: "Admin",
                        notifType: "RUN",
                        title: $"✅ RUN — Máy: {model.MachineCode ?? "-"} đã chạy lại",
                        body: $"Operation: {model.Operation ?? "-"}",
                        machineCode: model.MachineCode,
                        operation: model.Operation
                    );
    
                    // Thông báo máy đã chạy lại để Kỹ thuật biết
                    await _hubContext.Clients.Group("TechnicianGroup").SendAsync("ReceiveRunNotification", new
                    {
                        operation = model.Operation ?? "",
                        machineCode = model.MachineCode ?? "",
                        datetime = model.Datetime?.ToString("dd/MM/yyyy HH:mm") ?? ""
                    });
                }

                return Json(new { success = true, message = "停机时间已保存!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"数据库错误: {ex.InnerException?.Message ?? ex.Message}" });
            }
        }


        /* Hàm fill danh sách dropdown mã lỗi */
        private async Task RefillReasonsAsync()
        {
            ViewBag.ReasonOptions = await _context.SVN_Downtime_Reasons
                .AsNoTracking()
                .OrderBy(r => r.Reason_Name)
                .Select(r => new { r.Reason_Code, r.Reason_Name })
                .ToListAsync();
        }

        [HttpGet]
        public async Task<IActionResult> NotificationDashboard()
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "Technical" && role != "Admin")
                return RedirectToAction("Login", "Account");

            // Load tất cả bản ghi STOP hôm nay để hiển thị khi Tech vào/refresh trang
            var today = DateTime.Now.Date;
            var pending = await _context.SVN_Downtime_TechResponses
                .AsNoTracking()
                .Where(x => x.StopDatetime.HasValue && x.StopDatetime.Value.Date == today)
                .OrderByDescending(x => x.StopDatetime)
                .ToListAsync();

            ViewBag.PendingNotifications = pending;
            return View();
        }

        // ── API: Load danh sách thông báo hôm nay khi Tech refresh trang ──
        [HttpGet]
        public async Task<IActionResult> GetPendingNotifications()
        {
            var today = DateTime.Now.Date;
            var list = await _context.SVN_Downtime_TechResponses
                .AsNoTracking()
                .Where(x => x.StopDatetime.HasValue && x.StopDatetime.Value.Date == today)
                .OrderByDescending(x => x.StopDatetime)
                .Select(x => new {
                    id               = x.Id,
                    downtimeId       = x.DowntimeId,
                    machineCode      = x.MachineCode      ?? "",
                    operation        = x.Operation        ?? "",
                    employeeCode     = x.EmployeeCode     ?? "",
                    employeeName     = x.EmployeeName     ?? "",
                    operatorUsername = x.OperatorUsername ?? "",
                    reason           = x.Reason           ?? "",
                    effect           = x.Effect           ?? "",
                    estimateTime     = x.EstimateTime     ?? "",
                    station          = x.Station          ?? "",
                    description      = x.Description      ?? "",
                    location         = x.Location         ?? "",
                    datetime         = x.StopDatetime.HasValue
                                       ? x.StopDatetime.Value.ToString("dd/MM/yyyy HH:mm") : "",
                    techAction       = x.TechAction   ?? "",  // "" | "ACCEPT" | "WAIT"
                    techUsername     = x.TechUsername  ?? "",
                    respondDatetime  = x.RespondDatetime.HasValue
                                       ? x.RespondDatetime.Value.ToString("dd/MM/yyyy HH:mm") : ""
                })
                .ToListAsync();

            return Json(list);
        }


        /* Hàm fill danh sách Operation đang chạy trong ngày hôm nay */
        private async Task RefillOpsForToday()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            ViewBag.OperationOptions = await _context.SVN_targets
                .AsNoTracking()
                .Where(x => x.Date_time == today && x.Operation != null && x.Operation != "" && x.Operation.Contains("(SM)"))
                .Select(x => x.Operation)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();
        }


        /* Hàm báo cáo downtime */
        [HttpGet]
        public async Task<IActionResult> ReportDowntime(string fromDate = "", string toDate = "", int page = 1, int pageSize = 10)
        {
            try
            {
                var allData    = await GetDowntimeReportData(fromDate, toDate);
                var allDataPct = await GetDowntimeReportDataWithPct(fromDate, toDate);
                var machineData = await GetDowntimeReportByMachine(fromDate, toDate);

                var totalRecords = allData.Count;
                var totalPages   = (int)Math.Ceiling(totalRecords / (double)pageSize);

                page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

                var pagedData = allData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.FromDate            = fromDate;
                ViewBag.ToDate              = toDate;
                ViewBag.ChartData           = PrepareChartData(allData);
                ViewBag.IssCodeChartData    = PrepareIssCodeChartData(allData);
                ViewBag.DailyChartData      = PrepareDailyDowntimeChartData(allData, fromDate, toDate);
                ViewBag.AllData             = allData;
                ViewBag.AllDataWithPct      = allDataPct;
                ViewBag.MachineData         = machineData;
                ViewBag.MachineChartData    = PrepareMachineChartData(machineData);
                ViewBag.CurrentPage         = page;
                ViewBag.PageSize            = pageSize;
                ViewBag.TotalRecords        = totalRecords;
                ViewBag.TotalPages          = totalPages;
                ViewBag.HasPreviousPage     = page > 1;
                ViewBag.HasNextPage         = page < totalPages;

                return View(pagedData);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage    = $"Lỗi: {ex.Message}";
                ViewBag.FromDate        = fromDate;
                ViewBag.ToDate          = toDate;
                ViewBag.CurrentPage     = 1;
                ViewBag.PageSize        = 10;
                ViewBag.TotalRecords    = 0;
                ViewBag.TotalPages      = 1;
                ViewBag.HasPreviousPage = false;
                ViewBag.HasNextPage     = false;
                return View(new List<DowntimeReportByOperation>());
            }
        }

        /* Hàm chuẩn bị dữ liệu downtime ngày => Chart */
        private DailyDowntimeChartData PrepareDailyDowntimeChartData(
    List<DowntimeReportByOperation> reportData, string fromDate, string toDate)
        {
            var dailyData = new DailyDowntimeChartData
            {
                Dates = new List<string>(),
                DowntimeMinutes = new List<double>(),
                DowntimeCounts = new List<int>()
            };

            try
            {
                var query = from d in _context.SVN_Downtime_Infos_Devel
                            select new
                            {
                                d.Operation,
                                d.MachineCode,      // ← THÊM
                                d.State,
                                d.Datetime
                            };

                query = query.Where(x => x.Operation != null && x.Operation.Contains("(SM)"));

                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);

                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

                var allRecords = query.ToList();
                var downtimeByDate = new Dictionary<DateTime, (double Minutes, int Count)>();

                // ── Group theo MachineCode + Operation ──
                var grouped = allRecords
                    .Where(x => !string.IsNullOrEmpty(x.Operation) && !string.IsNullOrEmpty(x.MachineCode))
                    .GroupBy(x => new { x.MachineCode, x.Operation });   // ← ĐỔI

                foreach (var group in grouped)
                {
                    var records = group.OrderBy(x => x.Datetime).ToList();

                    for (int i = 0; i < records.Count - 1; i++)
                    {
                        var current = records[i];
                        var next = records[i + 1];

                        if (current.State?.Trim().ToUpper() == "STOP" &&
                            next.State?.Trim().ToUpper() == "RUN" &&
                            current.Datetime.HasValue &&
                            next.Datetime.HasValue)
                        {
                            var downtimeMinutes = (next.Datetime.Value - current.Datetime.Value).TotalMinutes;
                            var dateKey = current.Datetime.Value.Date;

                            if (downtimeByDate.ContainsKey(dateKey))
                                downtimeByDate[dateKey] = (downtimeByDate[dateKey].Minutes + downtimeMinutes,
                                                           downtimeByDate[dateKey].Count + 1);
                            else
                                downtimeByDate[dateKey] = (downtimeMinutes, 1);
                        }
                    }
                }

                foreach (var date in downtimeByDate.Keys.OrderBy(d => d))
                {
                    dailyData.Dates.Add(date.ToString("dd/MM"));
                    dailyData.DowntimeMinutes.Add(Math.Round(downtimeByDate[date].Minutes, 2));
                    dailyData.DowntimeCounts.Add(downtimeByDate[date].Count);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi PrepareDailyDowntimeChartData: {ex.Message}");
            }

            return dailyData;
        }



        /* Hàm lấy dữ liệu downtime STOP -> RUN */
        private async Task<List<DowntimeReportByOperation>> GetDowntimeReportData(string fromDate, string toDate)
        {
            var query = from d in _context.SVN_Downtime_Infos_Devel
                        join r in _context.SVN_Downtime_Reasons
                        on d.Reason equals r.Reason_Code into reasons
                        from r in reasons.DefaultIfEmpty()
                        select new
                        {
                            d.Operation,
                            d.MachineCode,          // ← THÊM
                            d.State,
                            d.Reason,
                            ErrorName = r != null ? r.Reason_Name : "未确定",
                            d.Datetime
                        };

            query = query.Where(x => x.Operation != null && x.Operation.Contains("(SM)"));

            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);

            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

            var allRecords = await query
                .OrderBy(x => x.MachineCode)    // ← ĐỔI (bỏ EmployeeCode)
                .ThenBy(x => x.Operation)
                .ThenBy(x => x.Datetime)
                .ToListAsync();

            var downtimeRecords = new List<DowntimeRecord>();

            // ── Group theo MachineCode + Operation thay vì EmployeeCode + Operation ──
            var grouped = allRecords
                .Where(x => !string.IsNullOrEmpty(x.Operation) && !string.IsNullOrEmpty(x.MachineCode))
                .GroupBy(x => new { x.MachineCode, x.Operation });   // ← ĐỔI

            foreach (var group in grouped)
            {
                var records = group.OrderBy(x => x.Datetime).ToList();

                for (int i = 0; i < records.Count - 1; i++)
                {
                    var current = records[i];
                    var next = records[i + 1];

                    if (current.State?.Trim().ToUpper() == "STOP" &&
                        next.State?.Trim().ToUpper() == "RUN" &&
                        current.Datetime.HasValue &&
                        next.Datetime.HasValue)
                    {
                        var downtimeMinutes = (next.Datetime.Value - current.Datetime.Value).TotalMinutes;
                        var reason = string.IsNullOrWhiteSpace(current.Reason) ? "N/A" : current.Reason.Trim();
                        var errorName = string.IsNullOrWhiteSpace(current.ErrorName) ? "未确定" : current.ErrorName.Trim();

                        downtimeRecords.Add(new DowntimeRecord
                        {
                            Operation = current.Operation.Trim(),
                            ISS_Code = reason,
                            ErrorName = errorName,
                            DowntimeMinutes = downtimeMinutes
                        });
                    }
                }
            }

            var groupedReport = downtimeRecords
                .GroupBy(x => x.Operation)
                .Select(opGroup => new DowntimeReportByOperation
                {
                    Operation = opGroup.Key,
                    TotalDowntimeCount = opGroup.Count(),
                    TotalDowntimeMinutes = opGroup.Sum(x => x.DowntimeMinutes),
                    TotalDowntimeFormatted = FormatMinutesToTime(opGroup.Sum(x => x.DowntimeMinutes)),
                    ErrorDetails = opGroup
                        .GroupBy(x => new { x.ISS_Code, x.ErrorName })
                        .Select(errGroup => new DowntimeReportByOperationError
                        {
                            Operation = opGroup.Key,
                            ISS_Code = errGroup.Key.ISS_Code,
                            ErrorName = errGroup.Key.ErrorName,
                            DowntimeCount = errGroup.Count(),
                            TotalDowntimeMinutes = errGroup.Sum(x => x.DowntimeMinutes),
                            TotalDowntimeFormatted = FormatMinutesToTime(errGroup.Sum(x => x.DowntimeMinutes))
                        })
                        .OrderByDescending(x => x.TotalDowntimeMinutes)
                        .ToList()
                })
                .OrderByDescending(x => x.TotalDowntimeMinutes)
                .ToList();

            return groupedReport;
        }


        /* Hàm chuyển đổi phút sang chuỗi thời gian */
        private string FormatMinutesToTime(double minutes)
        {
            if (minutes < 0) return "0h 0m";
            int hours = (int)(minutes / 60);
            int mins  = (int)(minutes % 60);
            return $"{hours}h {mins}m";
        }

        /* Hàm chuẩn bị chart data theo Operation */
        private DowntimeChartData PrepareChartData(List<DowntimeReportByOperation> reportData)
        {
            var chartData = new DowntimeChartData
            {
                Operations      = reportData.Select(x => x.Operation).ToList(),
                DowntimeMinutes = reportData.Select(x => Math.Round(x.TotalDowntimeMinutes, 2)).ToList(),
                ErrorBreakdowns = new List<ChartErrorBreakdown>()
            };

            foreach (var op in reportData)
            {
                chartData.ErrorBreakdowns.Add(new ChartErrorBreakdown
                {
                    Operation  = op.Operation,
                    ErrorNames = op.ErrorDetails.Select(e => e.ErrorName).ToList(),
                    Minutes    = op.ErrorDetails.Select(e => Math.Round(e.TotalDowntimeMinutes, 2)).ToList()
                });
            }

            return chartData;
        }

        /* Hàm chuẩn bị chart data theo Reason Code */
        private IssCodeChartData PrepareIssCodeChartData(List<DowntimeReportByOperation> reportData)
        {
            var allErrors = reportData
                .SelectMany(op => op.ErrorDetails)
                .GroupBy(e => new { e.ISS_Code, e.ErrorName })
                .Select(g => new
                {
                    IssCode      = g.Key.ISS_Code,
                    ErrorName    = g.Key.ErrorName,
                    TotalMinutes = g.Sum(e => e.TotalDowntimeMinutes),
                    Count        = g.Sum(e => e.DowntimeCount)
                })
                .OrderByDescending(x => x.TotalMinutes)
                .ToList();

            return new IssCodeChartData
            {
                IssCodeLabels = allErrors.Select(e => e.IssCode == "N/A" ? "Không xác định": $"{e.ErrorName} ({e.IssCode})").ToList(),
                DowntimeMinutes = allErrors.Select(e => Math.Round(e.TotalMinutes, 2)).ToList(),
                DowntimeCounts = allErrors.Select(e => e.Count).ToList()
            };
        }

        /* Hàm lấy dữ liệu downtime STOP -> RUN kèm % running time */
        private async Task<List<DowntimeReportByOperationWithPct>> GetDowntimeReportDataWithPct(string fromDate, string toDate)
        {
            var query = from d in _context.SVN_Downtime_Infos_Devel
                        join r in _context.SVN_Downtime_Reasons
                        on d.Reason equals r.Reason_Code into reasons
                        from r in reasons.DefaultIfEmpty()
                        select new
                        {
                            d.Operation,
                            d.MachineCode,          // ← THÊM
                            d.State,
                            d.Reason,
                            ErrorName = r != null ? r.Reason_Name : "未确定",
                            d.Datetime
                        };

            query = query.Where(x => x.Operation != null && x.Operation.Contains("(SM)"));

            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);

            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

            var allRecords = await query
                .OrderBy(x => x.MachineCode)    // ← ĐỔI
                .ThenBy(x => x.Operation)
                .ThenBy(x => x.Datetime)
                .ToListAsync();

            var downtimeByOp = new Dictionary<string, List<(double Minutes, string Reason, string ErrorName)>>();

            // ── Group theo MachineCode + Operation ──
            var grouped = allRecords
                .Where(x => !string.IsNullOrEmpty(x.Operation) && !string.IsNullOrEmpty(x.MachineCode))
                .GroupBy(x => new { x.MachineCode, x.Operation });   // ← ĐỔI

            foreach (var group in grouped)
            {
                var records = group.OrderBy(x => x.Datetime).ToList();
                for (int i = 0; i < records.Count - 1; i++)
                {
                    var cur = records[i];
                    var next = records[i + 1];
                    if (cur.State?.Trim().ToUpper() == "STOP" &&
                        next.State?.Trim().ToUpper() == "RUN" &&
                        cur.Datetime.HasValue && next.Datetime.HasValue)
                    {
                        var mins = (next.Datetime.Value - cur.Datetime.Value).TotalMinutes;
                        var op = cur.Operation!.Trim();
                        if (!downtimeByOp.ContainsKey(op))
                            downtimeByOp[op] = new List<(double, string, string)>();
                        downtimeByOp[op].Add((mins,
                            string.IsNullOrWhiteSpace(cur.Reason) ? "N/A" : cur.Reason.Trim(),
                            string.IsNullOrWhiteSpace(cur.ErrorName) ? "未确定" : cur.ErrorName.Trim()));
                    }
                }
            }

            // Running time: từ bản ghi đầu → cuối của mỗi Operation (không đổi logic này)
            var runningByOp = allRecords
                .Where(x => !string.IsNullOrEmpty(x.Operation) && x.Datetime.HasValue)
                .GroupBy(x => x.Operation!.Trim())
                .ToDictionary(
                    g => g.Key,
                    g => (g.Min(x => x.Datetime!.Value), g.Max(x => x.Datetime!.Value))
                );

            var result = downtimeByOp
                .Select(kvp =>
                {
                    var op = kvp.Key;
                    var items = kvp.Value;
                    var totalDt = items.Sum(x => x.Minutes);

                    double runningMins = 0;
                    if (runningByOp.TryGetValue(op, out var range))
                        runningMins = (range.Item2 - range.Item1).TotalMinutes;

                    var pct = runningMins > 0 ? Math.Round(totalDt / runningMins * 100, 2) : 0;

                    return new DowntimeReportByOperationWithPct
                    {
                        Operation = op,
                        TotalDowntimeCount = items.Count,
                        TotalDowntimeMinutes = totalDt,
                        TotalDowntimeFormatted = FormatMinutesToTime(totalDt),
                        RunningTimeMinutes = runningMins,
                        RunningTimeFormatted = FormatMinutesToTime(runningMins),
                        DowntimePct = pct,
                        ErrorDetails = items
                            .GroupBy(x => new { ISS_Code = x.Reason, ErrorName = x.ErrorName })
                            .Select(g => new DowntimeReportByOperationError
                            {
                                Operation = op,
                                ISS_Code = g.Key.ISS_Code,
                                ErrorName = g.Key.ErrorName,
                                DowntimeCount = g.Count(),
                                TotalDowntimeMinutes = g.Sum(x => x.Minutes),
                                TotalDowntimeFormatted = FormatMinutesToTime(g.Sum(x => x.Minutes))
                            })
                            .OrderByDescending(x => x.TotalDowntimeMinutes)
                            .ToList()
                    };
                })
                .OrderByDescending(x => x.TotalDowntimeMinutes)
                .ToList();

            return result;
        }



        /* Hàm lấy dữ liệu downtime theo EQ (Machine Code) */
        private async Task<List<DowntimeReportByMachine>> GetDowntimeReportByMachine(string fromDate, string toDate)
        {
            var query = from d in _context.SVN_Downtime_Infos_Devel
                        join r in _context.SVN_Downtime_Reasons
                        on d.Reason equals r.Reason_Code into reasons
                        from r in reasons.DefaultIfEmpty()
                        select new
                        {
                            d.Operation,
                            d.MachineCode,
                            d.State,
                            d.Reason,
                            ReasonName = r != null ? r.Reason_Name : "未确定",
                            d.Datetime
                        };

            query = query.Where(x => x.Operation != null && x.Operation.Contains("(SM)"));

            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);

            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

            var allRecords = await query
                .OrderBy(x => x.MachineCode)
                .ThenBy(x => x.Operation)
                .ThenBy(x => x.Datetime)
                .ToListAsync();

            var machineDowntimes = new Dictionary<string, List<(string Operation, double Minutes, string ReasonCode, string ReasonName)>>();

            // ── Group theo MachineCode + Operation (bỏ EmployeeCode) ──
            var grouped = allRecords
                .Where(x => !string.IsNullOrEmpty(x.MachineCode) && !string.IsNullOrEmpty(x.Operation))
                .GroupBy(x => new { x.MachineCode, x.Operation });   // ← ĐỔI

            foreach (var group in grouped)
            {
                var records = group.OrderBy(x => x.Datetime).ToList();
                for (int i = 0; i < records.Count - 1; i++)
                {
                    var cur = records[i];
                    var next = records[i + 1];
                    if (cur.State?.Trim().ToUpper() == "STOP" &&
                        next.State?.Trim().ToUpper() == "RUN" &&
                        cur.Datetime.HasValue && next.Datetime.HasValue)
                    {
                        var mins = (next.Datetime.Value - cur.Datetime.Value).TotalMinutes;
                        var machineKey = cur.MachineCode!.Trim();
                        if (!machineDowntimes.ContainsKey(machineKey))
                            machineDowntimes[machineKey] = new();
                        machineDowntimes[machineKey].Add((
                            cur.Operation?.Trim() ?? "",
                            mins,
                            string.IsNullOrWhiteSpace(cur.Reason) ? "N/A" : cur.Reason.Trim(),
                            string.IsNullOrWhiteSpace(cur.ReasonName) ? "未确定" : cur.ReasonName.Trim()
                        ));
                    }
                }
            }

            return machineDowntimes
                .Select(kvp =>
                {
                    var machine = kvp.Key;
                    var items = kvp.Value;
                    var totalDt = items.Sum(x => x.Minutes);
                    var mainOp = items.GroupBy(x => x.Operation).OrderByDescending(g => g.Count()).First().Key;
                    return new DowntimeReportByMachine
                    {
                        MachineCode = machine,
                        Operation = mainOp,
                        DowntimeCount = items.Count,
                        TotalDowntimeMinutes = totalDt,
                        TotalDowntimeFormatted = FormatMinutesToTime(totalDt),
                        ReasonDetails = items
                            .GroupBy(x => new { x.ReasonCode, x.ReasonName })
                            .Select(g => new DowntimeReportByMachineReason
                            {
                                MachineCode = machine,
                                ReasonCode = g.Key.ReasonCode,
                                ReasonName = g.Key.ReasonName,
                                DowntimeCount = g.Count(),
                                TotalDowntimeMinutes = g.Sum(x => x.Minutes),
                                TotalDowntimeFormatted = FormatMinutesToTime(g.Sum(x => x.Minutes))
                            })
                            .OrderByDescending(x => x.TotalDowntimeMinutes)
                            .ToList()
                    };
                })
                .OrderByDescending(x => x.TotalDowntimeMinutes)
                .ToList();
        }



        /* Hàm chuẩn bị chart data theo Machine */
        private MachineDowntimeChartData PrepareMachineChartData(List<DowntimeReportByMachine> machineData)
        {
            return new MachineDowntimeChartData
            {
                MachineCodes    = machineData.Select(x => x.MachineCode).ToList(),
                DowntimeMinutes = machineData.Select(x => Math.Round(x.TotalDowntimeMinutes, 2)).ToList(),
                DowntimeCounts  = machineData.Select(x => x.DowntimeCount).ToList()
            };
        }


        /* Hàm xuất Excel báo cáo downtime */
        [HttpGet]
        public async Task<IActionResult> ExportDowntimeReportToExcel(string fromDate = "", string toDate = "")
        {
            try
            {
                var reportData   = await GetDowntimeReportDataWithPct(fromDate, toDate);
                var machineData  = await GetDowntimeReportByMachine(fromDate, toDate);

                using (var workbook = new XLWorkbook())
                {
                    // ══════════ Sheet 1: By Line ══════════
                    var ws = workbook.Worksheets.Add("By Line");
                    var currentRow = 1;

                    ws.Style.Font.FontName = "Times New Roman";
                    ws.Style.Font.FontSize = 11;

                    ws.Cell(currentRow, 1).Value = "按工序停机时间报告 (Downtime Report by Line)";
                    ws.Range(currentRow, 1, currentRow, 8).Merge();
                    ws.Cell(currentRow, 1).Style.Font.Bold = true;
                    ws.Cell(currentRow, 1).Style.Font.FontSize = 14;
                    ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    currentRow += 2;

                    if (!string.IsNullOrEmpty(fromDate) || !string.IsNullOrEmpty(toDate))
                    {
                        ws.Cell(currentRow, 1).Value = $"From: {fromDate}   To: {toDate}";
                        ws.Range(currentRow, 1, currentRow, 8).Merge();
                        currentRow += 2;
                    }

                    foreach (var operation in reportData)
                    {
                        ws.Cell(currentRow, 1).Value = $"Line: {operation.Operation}";
                        ws.Cell(currentRow, 1).Style.Font.Bold = true;
                        ws.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
                        ws.Range(currentRow, 1, currentRow, 8).Merge();
                        currentRow++;

                        ws.Cell(currentRow, 1).Value = "Downtime Count:";
                        ws.Cell(currentRow, 2).Value = operation.TotalDowntimeCount;
                        ws.Cell(currentRow, 3).Value = "Total DT:";
                        ws.Cell(currentRow, 4).Value = operation.TotalDowntimeFormatted;
                        ws.Cell(currentRow, 5).Value = "Running Time:";
                        ws.Cell(currentRow, 6).Value = operation.RunningTimeFormatted;
                        ws.Cell(currentRow, 7).Value = "DT %:";
                        ws.Cell(currentRow, 8).Value = $"{operation.DowntimePct}%";
                        ws.Cell(currentRow, 1).Style.Font.Bold = true;
                        ws.Cell(currentRow, 3).Style.Font.Bold = true;
                        ws.Cell(currentRow, 5).Style.Font.Bold = true;
                        ws.Cell(currentRow, 7).Style.Font.Bold = true;
                        ws.Cell(currentRow, 8).Style.Font.FontColor = operation.DowntimePct >= 10 ? XLColor.Red : XLColor.DarkGreen;
                        currentRow++;

                        string[] headers = { "#", "Reason Code", "Reason Name", "Count", "Total (min)", "Total Time", "%" };
                        for (int i = 0; i < headers.Length; i++)
                        {
                            var cell = ws.Cell(currentRow, i + 1);
                            cell.Value = headers[i];
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5);
                            cell.Style.Font.FontColor = XLColor.White;
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                        currentRow++;

                        int seq = 1;
                        foreach (var error in operation.ErrorDetails)
                        {
                            ws.Cell(currentRow, 1).Value = seq++;
                            ws.Cell(currentRow, 2).Value = error.ISS_Code;
                            ws.Cell(currentRow, 3).Value = error.ErrorName;
                            ws.Cell(currentRow, 4).Value = error.DowntimeCount;
                            ws.Cell(currentRow, 5).Value = Math.Round(error.TotalDowntimeMinutes, 2);
                            ws.Cell(currentRow, 6).Value = error.TotalDowntimeFormatted;
                            double pct = operation.TotalDowntimeMinutes > 0
                                ? error.TotalDowntimeMinutes / operation.TotalDowntimeMinutes * 100 : 0;
                            ws.Cell(currentRow, 7).Value = $"{Math.Round(pct, 1)}%";
                            currentRow++;
                        }
                        currentRow += 2;
                    }

                    ws.Column(1).Width = 6;  ws.Column(2).Width = 14; ws.Column(3).Width = 30;
                    ws.Column(4).Width = 10; ws.Column(5).Width = 14; ws.Column(6).Width = 14;
                    ws.Column(7).Width = 10; ws.Column(8).Width = 10;

                    // ══════════ Sheet 2: By EQ (Machine) ══════════
                    var ws2 = workbook.Worksheets.Add("By EQ");
                    ws2.Style.Font.FontName = "Times New Roman";
                    ws2.Style.Font.FontSize = 11;
                    int r2 = 1;

                    ws2.Cell(r2, 1).Value = "按设备停机时间报告 (Downtime Report by EQ No.)";
                    ws2.Range(r2, 1, r2, 7).Merge();
                    ws2.Cell(r2, 1).Style.Font.Bold = true;
                    ws2.Cell(r2, 1).Style.Font.FontSize = 14;
                    ws2.Cell(r2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    r2 += 2;

                    if (!string.IsNullOrEmpty(fromDate) || !string.IsNullOrEmpty(toDate))
                    {
                        ws2.Cell(r2, 1).Value = $"From: {fromDate}   To: {toDate}";
                        ws2.Range(r2, 1, r2, 7).Merge();
                        r2 += 2;
                    }

                    // Summary table header
                    string[] sumHdr = { "#", "EQ No.", "Operation / Line", "DT Count", "Total (min)", "Total Time", "% of Total DT" };
                    for (int i = 0; i < sumHdr.Length; i++)
                    {
                        var c = ws2.Cell(r2, i + 1);
                        c.Value = sumHdr[i];
                        c.Style.Font.Bold = true;
                        c.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5);
                        c.Style.Font.FontColor = XLColor.White;
                        c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                    r2++;

                    var grandTotal = machineData.Sum(x => x.TotalDowntimeMinutes);
                    int mSeq = 1;
                    foreach (var m in machineData)
                    {
                        ws2.Cell(r2, 1).Value = mSeq++;
                        ws2.Cell(r2, 2).Value = m.MachineCode;
                        ws2.Cell(r2, 3).Value = m.Operation;
                        ws2.Cell(r2, 4).Value = m.DowntimeCount;
                        ws2.Cell(r2, 5).Value = Math.Round(m.TotalDowntimeMinutes, 2);
                        ws2.Cell(r2, 6).Value = m.TotalDowntimeFormatted;
                        double pctGrand = grandTotal > 0 ? m.TotalDowntimeMinutes / grandTotal * 100 : 0;
                        ws2.Cell(r2, 7).Value = $"{Math.Round(pctGrand, 1)}%";
                        r2++;
                    }

                    r2 += 2;

                    // Detail by machine
                    foreach (var m in machineData)
                    {
                        ws2.Cell(r2, 1).Value = $"EQ: {m.MachineCode}  |  Line: {m.Operation}  |  Total DT: {m.TotalDowntimeFormatted}  |  Count: {m.DowntimeCount}";
                        ws2.Cell(r2, 1).Style.Font.Bold = true;
                        ws2.Cell(r2, 1).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
                        ws2.Range(r2, 1, r2, 7).Merge();
                        r2++;

                        string[] dHdr = { "#", "Reason Code", "Reason Name", "Count", "Total (min)", "Total Time", "%" };
                        for (int i = 0; i < dHdr.Length; i++)
                        {
                            var c = ws2.Cell(r2, i + 1);
                            c.Value = dHdr[i];
                            c.Style.Font.Bold = true;
                            c.Style.Fill.BackgroundColor = XLColor.LightGray;
                        }
                        r2++;

                        int dSeq = 1;
                        foreach (var rd in m.ReasonDetails)
                        {
                            ws2.Cell(r2, 1).Value = dSeq++;
                            ws2.Cell(r2, 2).Value = rd.ReasonCode;
                            ws2.Cell(r2, 3).Value = rd.ReasonName;
                            ws2.Cell(r2, 4).Value = rd.DowntimeCount;
                            ws2.Cell(r2, 5).Value = Math.Round(rd.TotalDowntimeMinutes, 2);
                            ws2.Cell(r2, 6).Value = rd.TotalDowntimeFormatted;
                            double pct = m.TotalDowntimeMinutes > 0 ? rd.TotalDowntimeMinutes / m.TotalDowntimeMinutes * 100 : 0;
                            ws2.Cell(r2, 7).Value = $"{Math.Round(pct, 1)}%";
                            r2++;
                        }
                        r2 += 2;
                    }

                    ws2.Column(1).Width = 6;  ws2.Column(2).Width = 16; ws2.Column(3).Width = 30;
                    ws2.Column(4).Width = 10; ws2.Column(5).Width = 14; ws2.Column(6).Width = 14;
                    ws2.Column(7).Width = 12;

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        return File(stream.ToArray(),
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"DowntimeReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"导出Excel时出错: {ex.Message}" });
            }
        }

        // ══════════════════════════════════════════════════════
        // POST /Status/TechnicianRespond
        // Tech nhấn Accept hoặc Wait → cập nhật DB + push SignalR về Operator
        // ══════════════════════════════════════════════════════
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> TechnicianRespond([FromBody] TechRespondDto dto)
        {
            var techUser = HttpContext.Session.GetString("UserName") ?? "Kỹ thuật";

            // Cập nhật trạng thái trong bảng TechResponses
            var record = await _context.SVN_Downtime_TechResponses.FindAsync(dto.TechResponseId);
            if (record == null)
                return Json(new { success = false, message = "Record not found" });

            record.TechAction = dto.Action;   // "ACCEPT" hoặc "WAIT"
            record.TechUsername = techUser;
            record.RespondDatetime = DateTime.Now;
            await _context.SaveChangesAsync();

            // ── Lưu notification phản hồi của Tech → Prod ──
            if (!string.IsNullOrWhiteSpace(dto.OperatorUsername))
            {
                var notifTitle = dto.Action == "ACCEPT"
                    ? $"✅ Kỹ thuật [{techUser}] đang đến sửa máy {dto.MachineCode ?? "-"}"
                    : $"⏳ Kỹ thuật [{techUser}] đã xem — vui lòng chờ thêm";

                // Gửi cho Prod cụ thể
                await SaveNotificationAsync(
                    recipientUsername: dto.OperatorUsername,
                    recipientRole: "Production",
                    notifType: "TECH_RESPONSE",
                    title: notifTitle,
                    machineCode: dto.MachineCode,
                    techResponseId: dto.TechResponseId,
                    techAction: dto.Action,
                    techName: techUser
                );

                // Gửi thêm cho Admin
                await SaveNotificationAsync(
                    recipientUsername: "ALL_ADMIN",
                    recipientRole: "Admin",
                    notifType: "TECH_RESPONSE",
                    title: notifTitle,
                    machineCode: dto.MachineCode,
                    techResponseId: dto.TechResponseId,
                    techAction: dto.Action,
                    techName: techUser
                );
            }

            // Push SignalR ngược về Operator
            if (!string.IsNullOrWhiteSpace(dto.OperatorUsername))
            {
                await _hubContext.Clients
                    .Group($"Operator_{dto.OperatorUsername}")
                    .SendAsync("ReceiveTechResponse", new
                    {
                        action = dto.Action,
                        techName = techUser,
                        machineCode = dto.MachineCode ?? "",
                        message = dto.Action == "ACCEPT"
                            ? $"✅ Kỹ thuật [{techUser}] đã nhận thông tin và đang chuẩn bị đến sửa máy {dto.MachineCode}."
                            : $"⏳ Kỹ thuật [{techUser}] đã xem thông báo, vui lòng chờ thêm.",
                        datetime = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                    });
            }

            return Json(new { success = true });
        }
        // ══════════════════════════════════════════════════════════════════════
// NOTIFICATION API — thêm vào StatusController.cs
// ══════════════════════════════════════════════════════════════════════
// Vị trí: dán vào trong class StatusController, gần khu vực TechnicianRespond

// ── Helper: lưu notification vào DB ──────────────────────────────────
private async Task SaveNotificationAsync(
    string recipientUsername,
    string recipientRole,
    string notifType,
    string title,
    string? body        = null,
    string? machineCode = null,
    string? operation   = null,
    int?    techResponseId = null,
    string? techAction  = null,
    string? techName    = null)
{
    var notif = new SVN_Notification
    {
        RecipientUsername = recipientUsername,
        RecipientRole     = recipientRole,
        NotifType         = notifType,
        Title             = title,
        Body              = body,
        MachineCode       = machineCode,
        Operation         = operation,
        TechResponseId    = techResponseId,
        CreatedAt         = DateTime.Now,
        IsRead            = false,
        TechAction        = techAction,
        TechName          = techName
    };
    _context.SVN_Notifications.Add(notif);
    await _context.SaveChangesAsync();
}


// ── GET /Status/GetMyNotifications ────────────────────────────────────
// Trả về notifications cho user hiện tại (dùng khi load trang / refresh)
[HttpGet]
public async Task<IActionResult> GetMyNotifications()
{
    var username = HttpContext.Session.GetString("UserName") ?? "";
    var role     = HttpContext.Session.GetString("UserRole") ?? "";

    if (string.IsNullOrEmpty(username))
        return Json(new { success = false });

    IQueryable<SVN_Notification> query = _context.SVN_Notifications.AsNoTracking();

    if (role == "Technical")
    {
        // Tech nhận tất cả thông báo STOP và RUN gửi đến "ALL_TECH"
        query = query.Where(n => n.RecipientUsername == "ALL_TECH"
                              || n.RecipientUsername == username);
    }
    else if (role == "Admin")
    {
        // Admin nhận tất cả
        query = query.Where(n => n.RecipientUsername == "ALL_TECH"
                              || n.RecipientUsername == "ALL_ADMIN"
                              || n.RecipientUsername == username);
    }
    else
    {
        // Production: chỉ nhận thông báo gửi đến chính username họ
        query = query.Where(n => n.RecipientUsername == username);
    }

    var today = DateTime.Now.Date;
    var list = await query
        .Where(n => n.CreatedAt.Date == today)          // chỉ lấy hôm nay (tùy chỉnh nếu muốn)
        .OrderByDescending(n => n.CreatedAt)
        .Take(50)
        .Select(n => new {
            id             = n.Id,
            notifType      = n.NotifType,
            title          = n.Title,
            body           = n.Body ?? "",
            machineCode    = n.MachineCode ?? "",
            operation      = n.Operation  ?? "",
            techResponseId = n.TechResponseId,
            techAction     = n.TechAction  ?? "",
            techName       = n.TechName    ?? "",
            createdAt      = n.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
            isRead         = n.IsRead,
            readAt         = n.ReadAt.HasValue ? n.ReadAt.Value.ToString("dd/MM/yyyy HH:mm") : ""
        })
        .ToListAsync();

    int unreadCount = await query
        .Where(n => !n.IsRead && n.CreatedAt.Date == today)
        .CountAsync();

    return Json(new { success = true, notifications = list, unreadCount });
}


// ── POST /Status/MarkNotificationRead ─────────────────────────────────
// Đánh dấu 1 notification là đã đọc
[HttpPost]
[IgnoreAntiforgeryToken]
public async Task<IActionResult> MarkNotificationRead([FromBody] MarkReadDto dto)
{
    var username = HttpContext.Session.GetString("UserName") ?? "";
    if (string.IsNullOrEmpty(username)) return Json(new { success = false });

    var notif = await _context.SVN_Notifications.FindAsync(dto.Id);
    if (notif == null) return Json(new { success = false, message = "Not found" });

    notif.IsRead = true;
    notif.ReadAt = DateTime.Now;
    await _context.SaveChangesAsync();

    return Json(new { success = true });
}


// ── POST /Status/MarkAllNotificationsRead ─────────────────────────────
// Đánh dấu tất cả notifications của user hiện tại là đã đọc
[HttpPost]
[IgnoreAntiforgeryToken]
public async Task<IActionResult> MarkAllNotificationsRead()
{
    var username = HttpContext.Session.GetString("UserName") ?? "";
    var role     = HttpContext.Session.GetString("UserRole") ?? "";
    if (string.IsNullOrEmpty(username)) return Json(new { success = false });

    var today = DateTime.Now.Date;

    IQueryable<SVN_Notification> query = _context.SVN_Notifications
        .Where(n => !n.IsRead && n.CreatedAt.Date == today);

    if (role == "Technical")
        query = query.Where(n => n.RecipientUsername == "ALL_TECH" || n.RecipientUsername == username);
    else if (role == "Admin")
        query = query.Where(n => n.RecipientUsername == "ALL_TECH"
                              || n.RecipientUsername == "ALL_ADMIN"
                              || n.RecipientUsername == username);
    else
        query = query.Where(n => n.RecipientUsername == username);

    var items = await query.ToListAsync();
    var now = DateTime.Now;
    foreach (var n in items) { n.IsRead = true; n.ReadAt = now; }
    await _context.SaveChangesAsync();

    return Json(new { success = true, markedCount = items.Count });
}


        // ── DTO ──
        public class MarkReadDto
        {
            public int Id { get; set; }
        }



        public class TechRespondDto
        {
            public int     TechResponseId   { get; set; }   // Id trong SVN_Downtime_TechResponses
            public string  Action           { get; set; } = "";
            public string? OperatorUsername { get; set; }
            public string? MachineCode      { get; set; }
        }

        // ── Admin: Render panel ──
        [HttpGet]
        public IActionResult AdminPanel()
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "Admin") return RedirectToAction("Login", "Account");
            return View("~/Views/Account/AdminPanel.cshtml");
        }


        // ── Admin: List records (paginated + filtered) ──
        [HttpGet]
        public async Task<IActionResult> AdminGetRecords(
            string operation = "", string state = "",
            string fromDate = "", string toDate = "",
            int page = 1, int pageSize = 20)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            var q = _context.SVN_Downtime_Infos_Devel.AsQueryable();

            if (!string.IsNullOrWhiteSpace(operation))
                q = q.Where(x => x.Operation != null && x.Operation.Contains(operation));

            if (!string.IsNullOrWhiteSpace(state))
                q = q.Where(x => x.State != null && x.State.Trim().ToUpper() == state.ToUpper());

            if (DateTime.TryParse(fromDate, out var fd))
                q = q.Where(x => x.Datetime >= fd);

            if (DateTime.TryParse(toDate, out var td))
                q = q.Where(x => x.Datetime <= td.AddDays(1));

            var total = await q.CountAsync();
            var records = await q
                .OrderByDescending(x => x.Datetime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Id,
                    x.State,
                    x.Operation,
                    x.EmployeeCode,
                    x.EmployeeName,
                    x.MachineCode,
                    x.Location,
                    x.Reason,
                    x.Effect,
                    x.Station,
                    x.Action,
                    x.RootCause,
                    x.SpareParts,
                    x.EstimateTime,
                    x.Description,
                    x.Datetime
                })
                .ToListAsync();

            return Json(new
            {
                records,
                totalPages = (int)Math.Ceiling((double)total / pageSize),
                totalCount = total
            });
        }


        // ── Admin: Update record ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminUpdateRecord([FromBody] AdminRecordDto dto)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            var rec = await _context.SVN_Downtime_Infos_Devel.FindAsync(dto.Id);
            if (rec == null)
                return Json(new { success = false, message = "Record not found" });

            rec.EmployeeCode = dto.EmployeeCode;
            rec.EmployeeName = dto.EmployeeName;
            rec.MachineCode = dto.MachineCode;
            rec.Location = dto.Location;
            rec.Operation = dto.Operation;
            rec.State = dto.State;
            rec.Reason = dto.Reason;
            rec.Effect = dto.Effect;
            rec.Station = dto.Station;
            rec.Action = dto.Action;
            rec.RootCause = dto.RootCause;
            rec.SpareParts = dto.SpareParts;
            rec.EstimateTime = dto.EstimateTime;
            rec.Description = dto.Description;

            if (!string.IsNullOrWhiteSpace(dto.Datetime) &&
                DateTime.TryParse(dto.Datetime, out var dt))
                rec.Datetime = dt;

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }


        // ── Admin: Delete record ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminDeleteRecord([FromBody] AdminDeleteDto dto)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            var rec = await _context.SVN_Downtime_Infos_Devel.FindAsync(dto.Id);
            if (rec == null)
                return Json(new { success = false, message = "Record not found" });

            _context.SVN_Downtime_Infos_Devel.Remove(rec);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }


        // ── DTOs ──
        public class AdminRecordDto
        {
            public int Id { get; set; }
            public string? EmployeeCode { get; set; }
            public string? EmployeeName { get; set; }
            public string? MachineCode { get; set; }
            public string? Location { get; set; }
            public string? Operation { get; set; }
            public string? State { get; set; }
            public string? Reason { get; set; }
            public string? Effect { get; set; }
            public string? Station { get; set; }
            public string? Action { get; set; }
            public string? RootCause { get; set; }
            public string? SpareParts { get; set; }
            public string? EstimateTime { get; set; }
            public string? Description { get; set; }
            public string? Datetime { get; set; }
        }


        public class AdminDeleteDto
        {
            public int Id { get; set; }
        }


        // Helper classes
        private class DowntimeRecord
        {
            public string Operation       { get; set; }
            public string ISS_Code        { get; set; }
            public string ErrorName       { get; set; }
            public double DowntimeMinutes { get; set; }
        }

        public class ProcessDateRequest
        {
            public string Date { get; set; }
        }

        public class InsertedIdResult
        {
            public int InsertedId { get; set; }
        }

        public class ValidateCodeRequest
        {
            public string Code { get; set; }
        }
    }
}