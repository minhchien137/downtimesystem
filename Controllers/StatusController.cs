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
        // 根据工序返回当天最新的停机记录
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
        // GET: GetMachineRunStatus
        // 检查设备是否处于STOP状态，以及技术员是否已接受（ACCEPT）
        // Prod dùng để quyết định có cho phép bấm RUN không
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> GetMachineRunStatus(string machineNo)
        {
            if (string.IsNullOrWhiteSpace(machineNo))
                return Json(new { isStop = false, techAccepted = false, currentState = "" });

            // Lấy record mới nhất của máy này (không giới hạn ngày)
            var latest = await _context.SVN_Downtime_Infos_Devel
                .Where(x => x.MachineCode != null
                            && x.MachineCode.Trim() == machineNo.Trim()
                            && x.Datetime.HasValue)
                .OrderByDescending(x => x.Datetime)
                .Select(x => new { x.State })
                .FirstOrDefaultAsync();

            if (latest == null)
                return Json(new { isStop = false, techAccepted = false, currentState = "" });

            var stateNorm = (latest.State ?? "").Trim().ToUpper();

            // Nếu bản ghi mới nhất là RUN → máy đã chạy rồi, không cần Run nữa
            if (stateNorm == "RUN")
                return Json(new { isStop = false, techAccepted = false, currentState = "RUN" });

            // STOP hoặc RESPONSE đều là trạng thái "đang dừng / đang sửa"
            bool isStop = stateNorm == "STOP" || stateNorm == "RESPONSE";

            if (!isStop)
                return Json(new { isStop = false, techAccepted = false, currentState = stateNorm });

            // Tìm TechResponse mới nhất cho máy này (không giới hạn ngày)
            var techResp = await _context.SVN_Downtime_TechResponses
                .Where(x => x.MachineCode != null
                            && x.MachineCode.Trim() == machineNo.Trim()
                            && x.StopDatetime.HasValue)
                .OrderByDescending(x => x.StopDatetime)
                .Select(x => new { x.TechAction })
                .FirstOrDefaultAsync();

            bool techAccepted = techResp?.TechAction == "ACCEPT";

            return Json(new { isStop = true, techAccepted, currentState = stateNorm });
        }

        // ============================================================
        // GET: GetLatestStopByMachine
        // 根据设备号返回当天最新的停机记录，用于按"运行"按钮时自动填充表单
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
                    svnCode      = (x.EmployeeCode ?? "").Trim(),
                    employeeCode = (x.EmployeeCode ?? "").Trim(),
                    employeeName = (x.EmployeeName ?? "").Trim(),
                    operation    = (x.Operation    ?? "").Trim(),
                    opValue      = (x.Location     ?? "").Trim(),
                    reason       = (x.Reason       ?? "").Trim(),
                    effect       = (x.Effect       ?? "").Trim(),
                    description  = (x.Description  ?? "").Trim(),
                    rootCause    = (x.RootCause    ?? "").Trim(),
                    spareParts   = (x.SpareParts   ?? "").Trim()
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


        /*============================================================ 停机时间 ========================================================================**/


        /* GET 录入停机时间 */
        [HttpGet]
        public async Task<IActionResult> CreateDownTime()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");

            // 生产线 / Production Line
            var line = await _context.SVN_targets
                .AsNoTracking()
                .Where(x => x.Date_time == today && x.Operation != null && x.Operation != "")
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
            var userRole = HttpContext.Session.GetString("UserRole") ?? "";

            var allEmployees = await _context.SM_EmployInfos
                .AsNoTracking()
                .OrderBy(e => e.EnglishName)
                .Select(e => new { e.Id, e.ChineseName, e.EnglishName, e.NameDepart })
                .ToListAsync();

            var employees = userRole == "Production"
                ? allEmployees.Where(e => (e.NameDepart ?? "").Trim().ToUpper() == "PROD").ToList()
                : allEmployees;

            ViewBag.Employees = employees;

           var locations = await _context.SVN_Downtime_SMEQs
                .AsNoTracking()
                .Where(x => x.location != null && x.location != "")
                .Select(x => x.location)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            ViewBag.Operations = locations;


            // 设备、模治具编号 / Machine/Fixture no. — từ bảng SVN_Downtime_SMEQ
            var smeqs = await _context.SVN_Downtime_SMEQs
                .AsNoTracking()
                .OrderBy(e => e.name)
                .Select(e => new { e.name, e.serialnumber })
                .ToListAsync();

            ViewBag.SMEQs = smeqs;

            return View("CreateDownTime");
        }

        // SAU — thêm operations distinct vào response
        [HttpGet]
        public async Task<IActionResult> GetMachinesByLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return Json(new { machines = new List<object>(), operations = new List<string>() });

            var loc = location.Trim();

            var machines = await _context.SVN_Downtime_SMEQs
                .AsNoTracking()
                .Where(e => e.location != null && e.location.Trim() == loc)
                .OrderBy(e => e.name)
                .Select(e => new
                {
                    e.name,
                    e.serialnumber,
                    label = e.name + (!string.IsNullOrEmpty(e.serialnumber) ? $" - {e.serialnumber}" : "")
                })
                .ToListAsync();

            // Lấy distinct operation thuộc location đó
            var operations = await _context.SVN_Downtime_SMEQs
                .AsNoTracking()
                .Where(e => e.location != null && e.location.Trim() == loc
                         && e.operation != null && e.operation != "")
                .Select(e => e.operation.Trim())
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return Json(new { machines, operations });
        }



        /* GET 停机历史记录 */
        [HttpGet]
        public async Task<IActionResult> DowntimeList(
            string operation = "",
            string fromDate  = "",
            string toDate    = "",
            string station   = "",   // ← 新增
            string machine   = "",   // ← 新增
            string location  = "",   // ← 新增
            string employee  = "",   // ← 新增
            string reason    = "",   // ← 新增
            string effect    = "",   // ← 新增
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

                // ----- 固定筛选：只取SM -----
              //  

                // ----- 原有筛选条件 -----
                if (!string.IsNullOrWhiteSpace(operation))
                {
                    var op = operation.Trim();
                    query = query.Where(x => x.Operation != null && x.Operation.Contains(op));
                }

                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);

                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

                // ----- 新增筛选条件 -----
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

                // ----- 获取全部筛选结果（不分页）用于构建下拉框 -----
                var allFilteredData = await query
                    .OrderByDescending(x => x.Datetime)
                    .ThenBy(x => x.Operation)
                    .ToListAsync();

                var totalRecords = allFilteredData.Count;
                var totalPages   = (int)Math.Ceiling((double)totalRecords / pageSize);

                // ----- 从全部结果中分页 -----
                var results = allFilteredData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // ----- ViewBag: 下拉选项从全部筛选结果中取唯一值 -----
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

                // ----- ViewBag: 当前筛选值以保持状态 -----
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
                ViewBag.ErrorMessage    = $"错误: {ex.Message}";
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
                // 返回空ViewBag选项以避免View中出现空引用错误
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

        /* 导出停机列表到Excel */
        public async Task<IActionResult> ExportDowntimeListToExcel(
            string operation = "",
            string fromDate  = "",
            string toDate    = "",
            string station   = "",   // ← 新增
            string machine   = "",   // ← 新增
            string location  = "",   // ← 新增
            string employee  = "",   // ← 新增
            string reason    = "",   // ← 新增
            string effect    = "")   // ← 新增
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

                    ws.Style.Font.FontName = "Times New Roman";
                    ws.Style.Font.FontSize = 11;

                    // ── 标题 "停机历史" ──
                    int totalCols = 18;
                    var titleCell = ws.Cell(1, 1);
                    titleCell.Value = "停机历史";
                    titleCell.Style.Font.Bold     = true;
                    titleCell.Style.Font.FontSize = 16;
                    titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    titleCell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                    ws.Range(1, 1, 1, totalCols).Merge();
                    ws.Row(1).Height = 32;

                    var currentRow = 2;

                    // 按SQL列顺序的表头
                    string[] headers = {
                        "#", "工序", "设备号", "位置",
                        "故障代码", "故障名称", "影响程度", "工位", "预估时间",
                        "状态", "处理措施", "详细描述", "根本原因", "备件",
                        "员工编号", "员工姓名", "时间", "图片"
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
                        ws.Cell(currentRow,  2).Value = item.Operation;
                        ws.Cell(currentRow,  3).Value = item.MachineCode;
                        ws.Cell(currentRow,  4).Value = item.Location;
                        ws.Cell(currentRow,  5).Value = item.Reason;
                        ws.Cell(currentRow,  6).Value = item.ErrorName;
                        ws.Cell(currentRow,  7).Value = effLabel;
                        ws.Cell(currentRow,  8).Value = item.Station;
                        ws.Cell(currentRow,  9).Value = string.IsNullOrEmpty(item.EstimateTime) ? "-" : item.EstimateTime;
                        ws.Cell(currentRow, 10).Value = item.State;
                        ws.Cell(currentRow, 11).Value = item.Action;
                        ws.Cell(currentRow, 12).Value = item.Description;
                        ws.Cell(currentRow, 13).Value = item.RootCause;
                        ws.Cell(currentRow, 14).Value = item.SpareParts;
                        ws.Cell(currentRow, 15).Value = item.EmployeeCode;
                        ws.Cell(currentRow, 16).Value = item.EmployeeName;
                        ws.Cell(currentRow, 17).Value = item.Datetime?.ToString("dd/MM/yyyy HH:mm") ?? "-";

                        if (!string.IsNullOrEmpty(item.Image))
                        {
                            try
                            {
                                var imgRelative = item.Image;
                                if (imgRelative.StartsWith("/downtime"))
                                    imgRelative = imgRelative.Substring("/downtime".Length);

                                string imagePath = Path.Combine(
                                    _webHostEnvironment.WebRootPath,
                                    imgRelative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                                if (System.IO.File.Exists(imagePath))
                                {
                                    var picture = ws.AddPicture(imagePath);
                                    picture.MoveTo(ws.Cell(currentRow, 18), 8, 5);
                                    picture.WithSize(100, 70);
                                    ws.Cell(currentRow, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                    ws.Cell(currentRow, 18).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                                }
                                else
                                {
                                    ws.Cell(currentRow, 18).Value = "No image";
                                    ws.Cell(currentRow, 18).Style.Font.FontColor = XLColor.Gray;
                                }
                            }
                            catch
                            {
                                ws.Cell(currentRow, 18).Value = "Error";
                                ws.Cell(currentRow, 18).Style.Font.FontColor = XLColor.Red;
                            }
                        }
                        else
                        {
                            ws.Cell(currentRow, 18).Value = "-";
                            ws.Cell(currentRow, 18).Style.Font.FontColor = XLColor.Gray;
                        }
                    }

                    ws.Columns(1, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Columns(1, 18).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                    // 描述、根本原因、备件列左对齐
                    ws.Column(12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    ws.Column(13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    ws.Column(14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    ws.Column(1).Width  = 6;   // #
                    ws.Column(2).Width  = 28;  // 工序
                    ws.Column(3).Width  = 22;  // 设备号
                    ws.Column(4).Width  = 18;  // 位置
                    ws.Column(5).Width  = 14;  // 故障代码
                    ws.Column(6).Width  = 25;  // 故障名称
                    ws.Column(7).Width  = 15;  // 影响程度
                    ws.Column(8).Width  = 14;  // 工位
                    ws.Column(9).Width  = 14;  // 预估时间
                    ws.Column(10).Width = 12;  // 状态
                    ws.Column(11).Width = 28;  // 处理措施
                    ws.Column(12).Width = 30;  // 详细描述
                    ws.Column(13).Width = 28;  // 根本原因
                    ws.Column(14).Width = 22;  // 备件
                    ws.Column(15).Width = 15;  // 员工编号
                    ws.Column(16).Width = 18;  // 员工姓名
                    ws.Column(17).Width = 18;  // 时间
                    ws.Column(18).Width = 15;  // 图片

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
                Console.WriteLine($"导出停机列表出错: {ex.Message}");
                return Json(new { success = false, message = $"导出Excel时出错: {ex.Message}" });
            }
        }

        /* POST 录入停机时间 */
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateDownTime(SVN_Downtime_Info_Devel model, IFormFile? imageFile)
        {
            // ===== 1) 标准化/默认值 =====
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

            // RUN record
            if ((model.State ?? "").Trim().ToUpper() == "RUN")
            {
                if (string.IsNullOrWhiteSpace(model.Action) || model.Action == "CreateDownTime")
                    model.Action = null;

                // Tìm STOP gốc để copy fields sang RUN
                var latestStop = await _context.SVN_Downtime_Infos_Devel
                    .Where(x => x.MachineCode == model.MachineCode
                             && x.State != null
                             && x.State.ToUpper() == "STOP")
                    .OrderByDescending(x => x.Datetime)
                    .FirstOrDefaultAsync();

                if (latestStop != null)
                {
                    // RUN chỉ fill: Reason, Effect, Station, Action, SpareParts
                    if (string.IsNullOrWhiteSpace(model.Reason))     model.Reason     = latestStop.Reason;
                    if (string.IsNullOrWhiteSpace(model.Effect))     model.Effect     = latestStop.Effect;
                    if (string.IsNullOrWhiteSpace(model.Station))    model.Station    = latestStop.Station;
                    if (string.IsNullOrWhiteSpace(model.Action))     model.Action     = latestStop.Action;
                    if (string.IsNullOrWhiteSpace(model.SpareParts)) model.SpareParts = latestStop.SpareParts;

                    // Description RUN = TechDesc đã lưu tạm trong EstimateTime của STOP
                    if (!string.IsNullOrWhiteSpace(latestStop.EstimateTime)
                        && latestStop.EstimateTime.StartsWith("[TECHDESC]"))
                    {
                        model.Description = latestStop.EstimateTime.Substring(10).Trim();
                        // Dọn sạch staging field
                        latestStop.EstimateTime = null;
                    }
                }
            }

            // ===== 2) 处理图片上传 =====
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

            // ===== 3) 验证ModelState =====
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

            // ===== 4) 保存数据库 =====
            try
            {
                _context.SVN_Downtime_Infos_Devel.Add(model);
                await _context.SaveChangesAsync();

                // ===== 5) SignalR: 当状态为STOP时推送实时通知 =====
                var state = (model.State ?? "").Trim().ToUpper();

                if (state == "STOP")
                {
                    // ── 保存到TechResponses表，以便技术员刷新时看到 ──
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
                        TechAction       = null   // 尚未处理
                    };
                    _context.SVN_Downtime_TechResponses.Add(techResp);
                    await _context.SaveChangesAsync();

                    await SaveNotificationAsync(
                        recipientUsername : "ALL_TECH",
                        recipientRole     : "Technical",
                        notifType         : "STOP",
                        title             : $"🛑 停机 — 设备: {model.MachineCode ?? "-"}",
                        body              : $"工序: {model.Operation ?? "-"} | 员工: {model.EmployeeName ?? model.EmployeeCode ?? "-"} | 原因: {model.Reason ?? "-"}",
                        machineCode       : model.MachineCode,
                        operation         : model.Operation,
                        techResponseId    : techResp.Id
                    );

                    // ── 保存通知给管理员 ──
                    await SaveNotificationAsync(
                        recipientUsername: "ALL_ADMIN",
                        recipientRole: "Admin",
                        notifType: "STOP",
                        title: $"🛑 停机 — 设备: {model.MachineCode ?? "-"}",
                        body: $"工序: {model.Operation ?? "-"} | 员工: {model.EmployeeName ?? model.EmployeeCode ?? "-"} | 原因: {model.Reason ?? "-"}",
                        machineCode: model.MachineCode,
                        operation: model.Operation,
                        techResponseId: techResp.Id
                    );
    

                    // 发送通知给技术员组
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
                        image            = imagePath          ?? "",
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
                        title             : $"✅ 运行 — 设备: {model.MachineCode ?? "-"} 已恢复",
                        body              : $"工序: {model.Operation ?? "-"}",
                        machineCode       : model.MachineCode,
                        operation         : model.Operation
                    );

                    await SaveNotificationAsync(
                        recipientUsername: "ALL_ADMIN",
                        recipientRole: "Admin",
                        notifType: "RUN",
                        title: $"✅ 运行 — 设备: {model.MachineCode ?? "-"} 已恢复",
                        body: $"工序: {model.Operation ?? "-"}",
                        machineCode: model.MachineCode,
                        operation: model.Operation
                    );
    
                    // 通知技术员设备已恢复
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


        /* 填充错误代码下拉列表 */
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

            // Admin đi sang trang riêng xem tất cả thông báo
            if (role == "Admin")
                return RedirectToAction("AdminNotifications");

            // 加载今天所有的停机记录，以便技术员进入/刷新页面时显示
            var today = DateTime.Now.Date;
            var pending = await _context.SVN_Downtime_TechResponses
                .AsNoTracking()
                .Where(x => x.StopDatetime.HasValue && x.StopDatetime.Value.Date == today)
                .OrderByDescending(x => x.StopDatetime)
                .ToListAsync();

            ViewBag.PendingNotifications = pending;
            
            var allEmp = await _context.SM_EmployInfos
                .AsNoTracking()
                .OrderBy(e => e.EnglishName)
                .Select(e => new { e.EnglishName, e.ChineseName, e.NameDepart })
                .ToListAsync();

            var pieList = allEmp
                .Where(e => (e.NameDepart ?? "").Trim().ToUpper() == "PIE")
                .Select(e => new { englishName = e.EnglishName, chineseName = e.ChineseName })
                .ToList();

            ViewBag.PieEmployeesJson = System.Text.Json.JsonSerializer.Serialize(pieList);

            // Reason list cho PIE điền khi Response
            ViewBag.PieReasonOptions = await _context.SVN_Downtime_Reasons
                .AsNoTracking()
                .OrderBy(r => r.Reason_Name)
                .Select(r => new { r.Reason_Code, r.Reason_Name })
                .ToListAsync();

            return View();
        }

        // Trang thông báo riêng cho Admin — xem TẤT CẢ thông báo Prod & Tech
        [HttpGet]
        public async Task<IActionResult> AdminNotifications()
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "Admin")
                return RedirectToAction("Login", "Account");

            var today = DateTime.Now.Date;

            var notifications = await _context.SVN_Notifications
                .AsNoTracking()
                .Where(n => n.CreatedAt.Date == today)
                .OrderByDescending(n => n.CreatedAt)
                .Take(500)
                .ToListAsync();

            return View("AdminNotifications", notifications);
        }

        // ══════════════════════════════════════════════════════════════
        // DRI FLOW
        // PIE → "Not E&F" → notify PROD → PROD bấm "Call DRI" → DRI nhận
        // ══════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> DRIDashboard()
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "DRI" && role != "Admin")
                return RedirectToAction("Login", "Account");

            ViewBag.DriReasonOptions = await _context.SVN_Downtime_Reasons
                .AsNoTracking().OrderBy(r => r.Reason_Name)
                .Select(r => new { r.Reason_Code, r.Reason_Name }).ToListAsync();

            return View("DRIDashboard");
        }

        [HttpGet]
        public async Task<IActionResult> GetDRIPendingNotifications(string date = "")
        {
            DateTime targetDate = DateTime.Now.Date;
            if (!string.IsNullOrWhiteSpace(date) &&
                DateTime.TryParseExact(date, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                targetDate = parsedDate.Date;

            var list = await _context.SVN_Notifications
                .AsNoTracking()
                .Where(n => n.CreatedAt.Date == targetDate
                         && (n.RecipientUsername == "ALL_DRI" || n.RecipientRole == "DRI")
                         && n.NotifType == "DRI_CALL")
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new {
                    id             = n.Id,
                    techResponseId = n.TechResponseId ?? 0,
                    machineCode    = n.MachineCode ?? "",
                    operation      = n.Operation  ?? "",
                    datetime       = n.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    body           = n.Body ?? "",
                    techAction     = _context.SVN_Notifications
                        .Where(x => x.TechResponseId == n.TechResponseId
                                 && (x.NotifType == "DRI_ACCEPT" || x.NotifType == "DRI_UNRESOLVED"))
                        .OrderByDescending(x => x.CreatedAt)
                        .Select(x => x.NotifType).FirstOrDefault() ?? ""
                })
                .ToListAsync();

            return Json(list);
        }

        // STEP 1: PIE bấm "Not E&F" → notify PROD
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> PIENotEF([FromBody] PIENotEFDto dto)
        {
            var techUser = HttpContext.Session.GetString("UserName") ?? "PIE";

            var techResp = await _context.SVN_Downtime_TechResponses.FindAsync(dto.TechResponseId);
            if (techResp == null)
                return Json(new { success = false, message = "Record not found" });

            await SaveNotificationAsync(
                recipientUsername : techResp.OperatorUsername ?? "",
                recipientRole     : "Production",
                notifType         : "CALL_DRI_REQUEST",
                title             : $"🏢 Not E&F issue — Please call relevant department — Machine: {techResp.MachineCode ?? "-"}",
                body              : $"PIE [{techUser}] determined this is not an E&F problem. Please call PDE/QUAL/FAC/IT.",
                machineCode       : techResp.MachineCode,
                operation         : techResp.Operation,
                techResponseId    : dto.TechResponseId,
                techName          : techUser
            );

            if (!string.IsNullOrWhiteSpace(techResp.OperatorUsername))
            {
                await _hubContext.Clients
                    .Group($"Operator_{techResp.OperatorUsername}")
                    .SendAsync("ReceiveCallDRIRequest", new
                    {
                        techResponseId = dto.TechResponseId,
                        machineCode    = techResp.MachineCode ?? "",
                        operation      = techResp.Operation   ?? "",
                        techName       = techUser,
                        datetime       = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                    });
            }

            return Json(new { success = true });
        }

        // STEP 2: PROD bấm "Call DRI" → gửi notification đến DRI group
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ProdCallDRI([FromBody] ProdCallDRIDto dto)
        {
            var prodUser = HttpContext.Session.GetString("UserName") ?? "Production";

            var techResp = await _context.SVN_Downtime_TechResponses.FindAsync(dto.TechResponseId);
            if (techResp == null)
                return Json(new { success = false, message = "Record not found" });

            await SaveNotificationAsync(
                recipientUsername : "ALL_DRI",
                recipientRole     : "DRI",
                notifType         : "DRI_CALL",
                title             : $"🏢 Department Call — Machine: {techResp.MachineCode ?? "-"}",
                body              : $"Production [{prodUser}] requesting DRI support. Operation: {techResp.Operation ?? "-"}",
                machineCode       : techResp.MachineCode,
                operation         : techResp.Operation,
                techResponseId    : dto.TechResponseId
            );

            await _hubContext.Clients.Group("DRIGroup").SendAsync("ReceiveDRINotification", new
            {
                techResponseId   = dto.TechResponseId,
                machineCode      = techResp.MachineCode      ?? "",
                operation        = techResp.Operation        ?? "",
                employeeCode     = techResp.EmployeeCode     ?? "",
                employeeName     = techResp.EmployeeName     ?? "",
                operatorUsername = techResp.OperatorUsername ?? "",
                reason           = techResp.Reason           ?? "",
                description      = techResp.Description      ?? "",
                location         = techResp.Location         ?? "",
                datetime         = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
            });

            return Json(new { success = true });
        }

        // STEP 3a: DRI Accept
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DRIAccept([FromBody] DRIRespondDto dto)
        {
            var driUser  = HttpContext.Session.GetString("UserName") ?? "DRI";
            var techResp = await _context.SVN_Downtime_TechResponses.FindAsync(dto.TechResponseId);
            if (techResp == null) return Json(new { success = false, message = "Record not found" });

            await SaveNotificationAsync("ALL_DRI", "DRI", "DRI_ACCEPT",
                $"🔧 DRI [{driUser}] is handling — Machine: {techResp.MachineCode ?? "-"}",
                null, techResp.MachineCode, techResp.Operation, dto.TechResponseId, techName: driUser);

            if (!string.IsNullOrWhiteSpace(techResp.OperatorUsername))
            {
                await SaveNotificationAsync(techResp.OperatorUsername, "Production", "DRI_ACCEPT",
                    $"🔧 DRI [{driUser}] is handling — Machine: {techResp.MachineCode ?? "-"}",
                    null, techResp.MachineCode, techResp.Operation, dto.TechResponseId, techName: driUser);

                await _hubContext.Clients.Group($"Operator_{techResp.OperatorUsername}")
                    .SendAsync("ReceiveTechResponse", new {
                        action = "ACCEPT", techName = $"[DRI] {driUser}",
                        machineCode = techResp.MachineCode ?? "",
                        message = $"🔧 DRI [{driUser}] is handling machine {techResp.MachineCode}",
                        datetime = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                    });
            }
            return Json(new { success = true });
        }

        // STEP 3b: DRI Problem Solved → notify Prod to Run
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DRIProblemSolved([FromBody] DRIRespondDto dto)
        {
            var driUser  = HttpContext.Session.GetString("UserName") ?? "DRI";
            var techResp = await _context.SVN_Downtime_TechResponses.FindAsync(dto.TechResponseId);
            if (techResp == null) return Json(new { success = false, message = "Record not found" });

            var stopRecord = await _context.SVN_Downtime_Infos_Devel.FindAsync(techResp.DowntimeId);
            if (stopRecord != null)
            {
                if (!string.IsNullOrWhiteSpace(dto.Description))
                    stopRecord.EstimateTime = $"[TECHDESC]{dto.Description.Trim()}";
                if (!string.IsNullOrWhiteSpace(dto.RootCause))  stopRecord.RootCause  = dto.RootCause;
                if (!string.IsNullOrWhiteSpace(dto.Action))     stopRecord.Action     = dto.Action;
                if (!string.IsNullOrWhiteSpace(dto.SpareParts)) stopRecord.SpareParts = dto.SpareParts;
                await _context.SaveChangesAsync();
            }

            if (!string.IsNullOrWhiteSpace(techResp.OperatorUsername))
            {
                await SaveNotificationAsync(techResp.OperatorUsername, "Production", "FIX_COMPLETE",
                    $"✅ Problem solved — Machine: {techResp.MachineCode ?? "-"} — Please click Run!",
                    $"DRI [{driUser}] resolved the issue.",
                    techResp.MachineCode, techResp.Operation, dto.TechResponseId, techName: driUser);

                await _hubContext.Clients.Group($"Operator_{techResp.OperatorUsername}")
                    .SendAsync("ReceiveFixComplete", new {
                        machineCode = techResp.MachineCode ?? "",
                        operation   = techResp.Operation   ?? "",
                        techName    = $"[DRI] {driUser}",
                        message     = $"✅ Machine {techResp.MachineCode} resolved. Please click Run!",
                        datetime    = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                    });
            }
            return Json(new { success = true });
        }

        // STEP 3c: DRI Problem NOT Solved → record details, notify Admin + Prod
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DRIProblemNotSolved([FromBody] DRIRespondDto dto)
        {
            var driUser  = HttpContext.Session.GetString("UserName") ?? "DRI";
            var techResp = await _context.SVN_Downtime_TechResponses.FindAsync(dto.TechResponseId);
            if (techResp == null) return Json(new { success = false, message = "Record not found" });

            var stopRecord = await _context.SVN_Downtime_Infos_Devel.FindAsync(techResp.DowntimeId);
            if (stopRecord != null)
            {
                if (!string.IsNullOrWhiteSpace(dto.Description))
                    stopRecord.Description = $"[DRI-UNRESOLVED] {dto.Description.Trim()}";
                if (!string.IsNullOrWhiteSpace(dto.RootCause))  stopRecord.RootCause  = dto.RootCause;
                if (!string.IsNullOrWhiteSpace(dto.Action))     stopRecord.Action     = dto.Action;
                if (!string.IsNullOrWhiteSpace(dto.SpareParts)) stopRecord.SpareParts = dto.SpareParts;
                await _context.SaveChangesAsync();
            }

            await SaveNotificationAsync("ALL_ADMIN", "Admin", "DRI_UNRESOLVED",
                $"⚠️ Unresolved — Machine: {techResp.MachineCode ?? "-"} | DRI: {driUser}",
                dto.Description, techResp.MachineCode, techResp.Operation,
                dto.TechResponseId, techName: driUser);

            if (!string.IsNullOrWhiteSpace(techResp.OperatorUsername))
                await SaveNotificationAsync(techResp.OperatorUsername, "Production", "DRI_UNRESOLVED",
                    $"⚠️ Issue unresolved — Machine: {techResp.MachineCode ?? "-"}",
                    $"DRI [{driUser}] recorded details. Root Cause: {dto.RootCause ?? "-"}",
                    techResp.MachineCode, techResp.Operation, dto.TechResponseId, techName: driUser);

            return Json(new { success = true });
        }

        public class PIENotEFDto     { public int TechResponseId { get; set; } }
        public class ProdCallDRIDto  { public int TechResponseId { get; set; } }

        public class DRIRespondDto
        {
            public int     TechResponseId { get; set; }
            public string? Department     { get; set; }
            public string? Description    { get; set; }
            public string? RootCause      { get; set; }
            public string? Action         { get; set; }
            public string? SpareParts     { get; set; }
        }




        // ── API: 技术员刷新页面时加载通知列表 (支持按日期筛选) ──
        [HttpGet]
        public async Task<IActionResult> GetPendingNotifications(string date = "")
        {
            DateTime targetDate = DateTime.Now.Date;
            if (!string.IsNullOrWhiteSpace(date) &&
                DateTime.TryParseExact(date, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
            {
                targetDate = parsedDate.Date;
            }

            var list = await _context.SVN_Downtime_TechResponses
                .AsNoTracking()
                .Where(x => x.StopDatetime.HasValue && x.StopDatetime.Value.Date == targetDate)
                .OrderByDescending(x => x.StopDatetime)
                .Select(x => new {
                    id               = x.Id,
                    techResponseId   = x.Id,
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
                    techAction       = x.TechAction   ?? "",
                    techUsername     = x.TechUsername  ?? "",
                    respondDatetime  = x.RespondDatetime.HasValue
                                    ? x.RespondDatetime.Value.ToString("dd/MM/yyyy HH:mm") : "",
                    image            = _context.SVN_Downtime_Infos_Devel
                                        .Where(d => d.Id == x.DowntimeId)
                                        .Select(d => d.Image ?? "")
                                        .FirstOrDefault() ?? "",
                    fixComplete      = x.TechAction != null && x.TechAction != "" &&
                                    _context.SVN_Notifications
                                        .Any(n => n.TechResponseId == x.Id && n.NotifType == "FIX_COMPLETE"),
                    prodRun          = _context.SVN_Downtime_Infos_Devel
                                        .Any(r => r.MachineCode == x.MachineCode
                                                && r.State == "RUN"
                                                && r.Datetime > x.StopDatetime)
                })
                .ToListAsync();

            return Json(list);
        }


        /* 填充今天正在运行的工序列表 */
        private async Task RefillOpsForToday()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            ViewBag.OperationOptions = await _context.SVN_targets
                .AsNoTracking()
                .Where(x => x.Date_time == today && x.Operation != null && x.Operation != "")
                .Select(x => x.Operation)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();
        }


        /* 停机报告 */
        [HttpGet]
        public async Task<IActionResult> ReportDowntime(
                    string fromDate = "",
                    string toDate = "",
                    string operation = "",
                    string reason = "",
                    string location = "",
                    string machine = "",
                    string effect = "",
                    string station = "",
                    int page = 1,
                    int pageSize = 10)
        {
            try
            {
                var allData = await GetDowntimeReportData(fromDate, toDate, operation, reason, location, machine, effect, station);
                var allDataPct = await GetDowntimeReportDataWithPct(fromDate, toDate, operation, reason, location, machine, effect, station);
                var machineData = await GetDowntimeReportByMachine(fromDate, toDate, operation, reason, location, machine, effect, station);

                var totalRecords = allData.Count;
                var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

                page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

                var pagedData = allData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // ── Dropdown options ──
                var allOperations = await _context.SVN_targets
                    .AsNoTracking()
                    .Where(x => x.Operation != null && x.Operation != "")
                    .Select(x => x.Operation).Distinct().OrderBy(x => x).ToListAsync();

                var allReasons = await _context.SVN_Downtime_Reasons
                    .AsNoTracking()
                    .OrderBy(r => r.Reason_Name)
                    .Select(r => new { r.Reason_Code, r.Reason_Name })
                    .ToListAsync();

                var allLocations = await _context.SVN_Downtime_Infos_Devel
                    .AsNoTracking()
                    .Where(x => x.Location != null && x.Location != "")
                    .Select(x => x.Location!).Distinct().OrderBy(x => x).ToListAsync();

                var allMachines = await _context.SVN_Downtime_Infos_Devel
                    .AsNoTracking()
                    .Where(x => x.MachineCode != null && x.MachineCode != "")
                    .Select(x => x.MachineCode!).Distinct().OrderBy(x => x).ToListAsync();

                var allStations = await _context.SVN_Downtime_Infos_Devel
                    .AsNoTracking()
                    .Where(x => x.Station != null && x.Station != "")
                    .Select(x => x.Station!).Distinct().OrderBy(x => x).ToListAsync();

                ViewBag.OperationOptions = allOperations;
                ViewBag.ReasonOptions = allReasons;
                ViewBag.LocationOptions = allLocations;
                ViewBag.MachineOptions = allMachines;
                ViewBag.StationOptions = allStations;

                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
                ViewBag.Operation = operation;
                ViewBag.Reason = reason;
                ViewBag.Location = location;
                ViewBag.Machine = machine;
                ViewBag.Effect = effect;
                ViewBag.Station = station;
                ViewBag.ChartData = PrepareChartData(allData);
                ViewBag.IssCodeChartData = PrepareIssCodeChartData(allData);
                ViewBag.DailyChartData = PrepareDailyDowntimeChartData(allData, fromDate, toDate);
                ViewBag.AllData = allData;
                ViewBag.AllDataWithPct = allDataPct;
                ViewBag.MachineData      = machineData;
                ViewBag.MachineChartData = PrepareMachineChartData(machineData);
                ViewBag.MttrData         = await GetMttrByMachine(fromDate, toDate, operation, reason, location, machine, effect, station);
                ViewBag.Top5Data         = await GetTop5Machines(fromDate, toDate, operation, location, machine);
                ViewBag.ResponseTimeData = await GetResponseTimeData(fromDate, toDate, operation, machine);

                // Tech response records cho EQ detail expand — kèm RunDatetime
                var techRespQuery = _context.SVN_Downtime_TechResponses.AsQueryable();
                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var trFd))
                    techRespQuery = techRespQuery.Where(x => x.StopDatetime.HasValue && x.StopDatetime.Value.Date >= trFd.Date);
                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var trTd))
                    techRespQuery = techRespQuery.Where(x => x.StopDatetime.HasValue && x.StopDatetime.Value.Date <= trTd.Date);
                if (!string.IsNullOrWhiteSpace(machine))
                    techRespQuery = techRespQuery.Where(x => x.MachineCode == machine.Trim());

                var rawResps = await techRespQuery
                    .OrderByDescending(x => x.StopDatetime)
                    .Select(x => new {
                        x.MachineCode, x.Operation,
                        x.StopDatetime, x.RespondDatetime,
                        x.TechAction, x.TechUsername,
                        x.DowntimeId
                    })
                    .ToListAsync();

                // Lấy STOP records để join thêm Location, Reason, Station, etc.
                var downIds = rawResps.Select(r => r.DowntimeId).Distinct().ToList();
                var stopDetails = await _context.SVN_Downtime_Infos_Devel
                    .Where(x => downIds.Contains(x.Id))
                    .Select(x => new {
                        x.Id, x.Location, x.Reason, x.Station,
                        x.Description, x.RootCause, x.Action,
                        x.SpareParts, x.Effect, x.Image,
                        x.EmployeeName, x.EmployeeCode
                    })
                    .ToListAsync();

                // Lookup EnglishName từ SM_EmployInfos theo EmployeeCode
                var empCodes = stopDetails.Select(s => s.EmployeeCode).Where(c => c != null).Distinct().ToList();
                var empEnglishNames = await _context.SM_EmployInfos
                    .AsNoTracking()
                    .Where(e => empCodes.Contains(e.EmployeeID))
                    .ToDictionaryAsync(e => e.EmployeeID ?? "", e => e.EnglishName ?? "");

                // Join reason names
                var reasonNames = await _context.SVN_Downtime_Reasons
                    .AsNoTracking()
                    .ToDictionaryAsync(r => r.Reason_Code, r => r.Reason_Name);

                // Lấy RUN records để tính repair time
                var runTimes  = await _context.SVN_Downtime_Infos_Devel
                    .Where(x => x.State != null && x.State.ToUpper() == "RUN"
                             && rawResps.Select(r => r.MachineCode).Contains(x.MachineCode))
                    .Select(x => new { x.MachineCode, x.Datetime })
                    .ToListAsync();

                var allTechResps = rawResps.Select(r => {
                    var runDt = runTimes
                        .Where(rn => rn.MachineCode == r.MachineCode
                                  && rn.Datetime.HasValue
                                  && r.StopDatetime.HasValue
                                  && rn.Datetime > r.StopDatetime)
                        .OrderBy(rn => rn.Datetime)
                        .Select(rn => rn.Datetime)
                        .FirstOrDefault();

                    var stop = stopDetails.FirstOrDefault(s => s.Id == r.DowntimeId);
                    var reasonName = stop?.Reason != null && reasonNames.TryGetValue(stop.Reason, out var rn2) ? rn2 : (stop?.Reason ?? "");
                    var effStr = stop?.Effect == "1" ? "Affects Production" : stop?.Effect == "2" ? "No Effect" : "-";

                    return new {
                        r.MachineCode, r.Operation,
                        r.StopDatetime, r.RespondDatetime,
                        r.TechAction, r.TechUsername,
                        RunDatetime  = runDt,
                        Location     = stop?.Location    ?? "",
                        Reason       = reasonName,
                        Station      = stop?.Station     ?? "",
                        Description  = stop?.Description ?? "",
                        RootCause    = stop?.RootCause   ?? "",
                        Action       = stop?.Action      ?? "",
                        SpareParts   = stop?.SpareParts  ?? "",
                        Effect       = effStr,
                        Image        = stop?.Image       ?? "",
                        EmployeeName = (stop?.EmployeeCode != null && empEnglishNames.TryGetValue(stop.EmployeeCode, out var engName) && !string.IsNullOrEmpty(engName))
                                       ? engName
                                       : (stop?.EmployeeName ?? stop?.EmployeeCode ?? "")
                    };
                }).Cast<dynamic>().ToList();

                ViewBag.AllTechResponses = allTechResps;
                ViewBag.CurrentPage = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalRecords = totalRecords;
                ViewBag.TotalPages = totalPages;
                ViewBag.HasPreviousPage = page > 1;
                ViewBag.HasNextPage = page < totalPages;

                return View(pagedData);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"错误: {ex.Message}";
                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
                ViewBag.Operation = operation;
                ViewBag.Reason = reason;
                ViewBag.Location = location;
                ViewBag.Machine = machine;
                ViewBag.Effect = effect;
                ViewBag.Station = station;
                ViewBag.CurrentPage = 1;
                ViewBag.PageSize = 10;
                ViewBag.TotalRecords = 0;
                ViewBag.TotalPages = 1;
                ViewBag.HasPreviousPage = false;
                ViewBag.HasNextPage = false;
                ViewBag.OperationOptions = new List<string>();
                ViewBag.ReasonOptions = new List<object>();
                ViewBag.LocationOptions = new List<string>();
                ViewBag.MachineOptions = new List<string>();
                ViewBag.StationOptions = new List<string>();
                return View(new List<DowntimeReportByOperation>());
            }
        
        }

        /* 准备每日停机图表数据 */
        private DailyDowntimeChartData PrepareDailyDowntimeChartData(
                List<DowntimeReportByOperation> reportData, string fromDate, string toDate,
                string operation = "", string reason = "", string location = "",
                string machine = "", string effect = "", string station = "")
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
                                d.MachineCode,      // ← 新增
                                d.State,
                                d.Datetime
                            };

                

                if (!string.IsNullOrWhiteSpace(operation))
                { var op = operation.Trim(); query = query.Where(x => x.Operation != null && x.Operation.Contains(op)); }

                if (!string.IsNullOrWhiteSpace(machine))
                { var mc = machine.Trim(); query = query.Where(x => x.MachineCode != null && x.MachineCode == mc); }

                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);

                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

                var allRecords = query.ToList();
                var downtimeMinutesByDate = new Dictionary<DateTime, double>();
                // Count = tất cả STOP records trong ngày (không cần đợi RUN)
                var countByDate = allRecords
                    .Where(x => x.State?.Trim().ToUpper() == "STOP" && x.Datetime.HasValue)
                    .GroupBy(x => x.Datetime!.Value.Date)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Tính downtime minutes: chỉ STOP→RUN pairs
                var grouped = allRecords
                    .Where(x => !string.IsNullOrEmpty(x.Operation) && !string.IsNullOrEmpty(x.MachineCode))
                    .GroupBy(x => new { x.MachineCode, x.Operation });

                foreach (var group in grouped)
                {
                    var records = group.OrderBy(x => x.Datetime).ToList();

                    for (int i = 0; i < records.Count; i++)
                    {
                        var current = records[i];
                        if (current.State?.Trim().ToUpper() != "STOP" || !current.Datetime.HasValue) continue;

                        for (int j = i + 1; j < records.Count; j++)
                        {
                            var next = records[j];
                            if (!next.Datetime.HasValue) continue;
                            var nextState = next.State?.Trim().ToUpper();

                            if (nextState == "RUN")
                            {
                                var mins = (next.Datetime.Value - current.Datetime.Value).TotalMinutes;
                                var dateKey = current.Datetime.Value.Date;
                                downtimeMinutesByDate[dateKey] = (downtimeMinutesByDate.TryGetValue(dateKey, out var existing) ? existing : 0) + mins;
                                break;
                            }
                            // Skip RESPONSE/REJECT, tiếp tục tìm RUN
                            else if (nextState == "STOP") break; // Gặp STOP mới → dừng tìm
                        }
                    }
                }

                // Gộp tất cả ngày có STOP hoặc có downtime
                var allDates = countByDate.Keys.Union(downtimeMinutesByDate.Keys).OrderBy(d => d);
                foreach (var date in allDates)
                {
                    dailyData.Dates.Add(date.ToString("dd/MM"));
                    dailyData.DowntimeMinutes.Add(Math.Round(downtimeMinutesByDate.TryGetValue(date, out var mins2) ? mins2 : 0, 2));
                    dailyData.DowntimeCounts.Add(countByDate.TryGetValue(date, out var cnt) ? cnt : 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"准备每日停机图表数据出错: {ex.Message}");
            }

            return dailyData;
        }



        /* 获取停机报告数据 (STOP -> RUN) */
        private async Task<List<DowntimeReportByOperation>> GetDowntimeReportData(
    string fromDate, string toDate,
    string operation = "", string reason = "", string location = "",
    string machine = "", string effect = "", string station = "")
        {
            var query = from d in _context.SVN_Downtime_Infos_Devel
                        join r in _context.SVN_Downtime_Reasons
                        on d.Reason equals r.Reason_Code into reasons
                        from r in reasons.DefaultIfEmpty()
                        select new
                        {
                            d.Operation,
                            d.MachineCode,
                            d.Location,
                            d.Effect,
                            d.Station,
                            d.State,
                            d.Reason,
                            ErrorName = r != null ? r.Reason_Name : "未确定",
                            d.Datetime
                        };

            

            if (!string.IsNullOrWhiteSpace(operation))
            { var _op = operation.Trim(); query = query.Where(x => x.Operation != null && x.Operation.Contains(_op)); }
            if (!string.IsNullOrWhiteSpace(reason))
            { var _re = reason.Trim(); query = query.Where(x => x.Reason != null && x.Reason == _re); }
            if (!string.IsNullOrWhiteSpace(location))
            { var _loc = location.Trim(); query = query.Where(x => x.Location != null && x.Location == _loc); }
            if (!string.IsNullOrWhiteSpace(machine))
            { var _mc = machine.Trim(); query = query.Where(x => x.MachineCode != null && x.MachineCode == _mc); }
            if (!string.IsNullOrWhiteSpace(effect))
            { var _ef = effect.Trim(); query = query.Where(x => x.Effect != null && x.Effect == _ef); }
            if (!string.IsNullOrWhiteSpace(station))
            { var _st = station.Trim(); query = query.Where(x => x.Station != null && x.Station == _st); }

            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);
            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

            var allRecords = await query
                .OrderBy(x => x.MachineCode)
                .ThenBy(x => x.Operation)
                .ThenBy(x => x.Datetime)
                .ToListAsync();

            var downtimeRecords = new List<DowntimeRecord>();

            var grouped = allRecords
                .Where(x => !string.IsNullOrEmpty(x.Operation) && !string.IsNullOrEmpty(x.MachineCode))
                .GroupBy(x => new { x.MachineCode, x.Operation });

            foreach (var group in grouped)
            {
                var records = group.OrderBy(x => x.Datetime).ToList();
                for (int i = 0; i < records.Count; i++)
{
    var current = records[i];
    if (current.State?.Trim().ToUpper() != "STOP" || !current.Datetime.HasValue) continue;

    for (int j = i + 1; j < records.Count; j++)
    {
        var next = records[j];
        if (!next.Datetime.HasValue) continue;
        var nextState = next.State?.Trim().ToUpper();

        if (nextState == "RUN")
        {
            var downtimeMinutes = (next.Datetime.Value - current.Datetime.Value).TotalMinutes;
            var _reason = string.IsNullOrWhiteSpace(current.Reason) ? "N/A" : current.Reason.Trim();
            var _errorName = string.IsNullOrWhiteSpace(current.ErrorName) ? "未确定" : current.ErrorName.Trim();

            downtimeRecords.Add(new DowntimeRecord
            {
                Operation = current.Operation.Trim(),
                ISS_Code = _reason,
                ErrorName = _errorName,
                DowntimeMinutes = downtimeMinutes
            });
            break;
        }
        else if (nextState == "STOP") break;
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



        /* 将分钟转换为时间字符串 */
        private string FormatMinutesToTime(double minutes)
        {
            if (minutes < 0) return "0h 0m";
            int hours = (int)(minutes / 60);
            int mins  = (int)(minutes % 60);
            return $"{hours}h {mins}m";
        }

        /* 按工序准备图表数据 */
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

        /* 按故障代码准备图表数据 */
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
                IssCodeLabels = allErrors.Select(e => e.IssCode == "N/A" ? "未确定": $"{e.ErrorName} ({e.IssCode})").ToList(),
                DowntimeMinutes = allErrors.Select(e => Math.Round(e.TotalMinutes, 2)).ToList(),
                DowntimeCounts = allErrors.Select(e => e.Count).ToList()
            };
        }

        /* 获取停机报告数据 (含运行时间占比) */
        private async Task<List<DowntimeReportByOperationWithPct>> GetDowntimeReportDataWithPct(
  string fromDate, string toDate,
  string operation = "", string reason = "", string location = "",
  string machine = "", string effect = "", string station = "")
        {
            var query = from d in _context.SVN_Downtime_Infos_Devel
                        join r in _context.SVN_Downtime_Reasons
                        on d.Reason equals r.Reason_Code into reasons
                        from r in reasons.DefaultIfEmpty()
                        select new
                        {
                            d.Operation,
                            d.MachineCode,
                            d.Location,
                            d.Effect,
                            d.Station,
                            d.State,
                            d.Reason,
                            ErrorName = r != null ? r.Reason_Name : "未确定",
                            d.Datetime
                        };

            

            if (!string.IsNullOrWhiteSpace(operation))
            { var _op = operation.Trim(); query = query.Where(x => x.Operation != null && x.Operation.Contains(_op)); }
            if (!string.IsNullOrWhiteSpace(reason))
            { var _re = reason.Trim(); query = query.Where(x => x.Reason != null && x.Reason == _re); }
            if (!string.IsNullOrWhiteSpace(location))
            { var _loc = location.Trim(); query = query.Where(x => x.Location != null && x.Location == _loc); }
            if (!string.IsNullOrWhiteSpace(machine))
            { var _mc = machine.Trim(); query = query.Where(x => x.MachineCode != null && x.MachineCode == _mc); }
            if (!string.IsNullOrWhiteSpace(effect))
            { var _ef = effect.Trim(); query = query.Where(x => x.Effect != null && x.Effect == _ef); }
            if (!string.IsNullOrWhiteSpace(station))
            { var _st = station.Trim(); query = query.Where(x => x.Station != null && x.Station == _st); }

            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);
            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

            var allRecords = await query
                .OrderBy(x => x.MachineCode)
                .ThenBy(x => x.Operation)
                .ThenBy(x => x.Datetime)
                .ToListAsync();

            var downtimeByOp = new Dictionary<string, List<(double Minutes, string Reason, string ErrorName)>>();

            var grouped = allRecords
                .Where(x => !string.IsNullOrEmpty(x.Operation) && !string.IsNullOrEmpty(x.MachineCode))
                .GroupBy(x => new { x.MachineCode, x.Operation });

            foreach (var group in grouped)
            {
                var records = group.OrderBy(x => x.Datetime).ToList();
               for (int i = 0; i < records.Count; i++)
{
    var cur = records[i];
    if (cur.State?.Trim().ToUpper() != "STOP" || !cur.Datetime.HasValue) continue;

    for (int j = i + 1; j < records.Count; j++)
    {
        var next = records[j];
        if (!next.Datetime.HasValue) continue;
        var nextState = next.State?.Trim().ToUpper();

        if (nextState == "RUN")
        {
            var mins = (next.Datetime.Value - cur.Datetime.Value).TotalMinutes;
            var _opKey = cur.Operation!.Trim();
            if (!downtimeByOp.ContainsKey(_opKey))
                downtimeByOp[_opKey] = new List<(double, string, string)>();
            downtimeByOp[_opKey].Add((
                mins,
                string.IsNullOrWhiteSpace(cur.Reason) ? "N/A" : cur.Reason.Trim(),
                string.IsNullOrWhiteSpace(cur.ErrorName) ? "未确定" : cur.ErrorName.Trim()
            ));
            break;
        }
        else if (nextState == "STOP") break;
                    }
                }
            }

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
                    // ✅ FIX: đổi 'op' → '_opKey' để tránh conflict với parameter 'operation'
                    var _opKey = kvp.Key;
                    var items = kvp.Value;
                    var totalDt = items.Sum(x => x.Minutes);

                    double runningMins = 0;
                    if (runningByOp.TryGetValue(_opKey, out var range))
                        runningMins = (range.Item2 - range.Item1).TotalMinutes;

                    var pct = runningMins > 0 ? Math.Round(totalDt / runningMins * 100, 2) : 0;

                    return new DowntimeReportByOperationWithPct
                    {
                        Operation = _opKey,
                        TotalDowntimeCount = items.Count,
                        TotalDowntimeMinutes = totalDt,
                        TotalDowntimeFormatted = FormatMinutesToTime(totalDt),
                        RunningTimeMinutes = runningMins,
                        RunningTimeFormatted = FormatMinutesToTime(runningMins),
                        DowntimePct = pct,
                        ErrorDetails = items
                            .GroupBy(x => new { ISS_Code = x.Reason, x.ErrorName })
                            .Select(g => new DowntimeReportByOperationError
                            {
                                Operation = _opKey,
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




        /* 按设备(EQ)获取停机数据 */
        private async Task<List<DowntimeReportByMachine>> GetDowntimeReportByMachine(
    string fromDate, string toDate,
    string operation = "", string reason = "", string location = "",
    string machine = "", string effect = "", string station = "")
        {
            var query = from d in _context.SVN_Downtime_Infos_Devel
                        join r in _context.SVN_Downtime_Reasons
                        on d.Reason equals r.Reason_Code into reasons
                        from r in reasons.DefaultIfEmpty()
                        select new
                        {
                            d.Operation,
                            d.MachineCode,
                            d.Location,
                            d.Effect,
                            d.Station,
                            d.State,
                            d.Reason,
                            // ✅ FIX: đặt tên alias là 'ErrorName' — NHẤT QUÁN với 2 method trên
                            ErrorName = r != null ? r.Reason_Name : "未确定",
                            d.Datetime
                        };

            

            if (!string.IsNullOrWhiteSpace(operation))
            { var _op = operation.Trim(); query = query.Where(x => x.Operation != null && x.Operation.Contains(_op)); }
            if (!string.IsNullOrWhiteSpace(reason))
            { var _re = reason.Trim(); query = query.Where(x => x.Reason != null && x.Reason == _re); }
            if (!string.IsNullOrWhiteSpace(location))
            { var _loc = location.Trim(); query = query.Where(x => x.Location != null && x.Location == _loc); }
            if (!string.IsNullOrWhiteSpace(machine))
            { var _mc = machine.Trim(); query = query.Where(x => x.MachineCode != null && x.MachineCode == _mc); }
            if (!string.IsNullOrWhiteSpace(effect))
            { var _ef = effect.Trim(); query = query.Where(x => x.Effect != null && x.Effect == _ef); }
            if (!string.IsNullOrWhiteSpace(station))
            { var _st = station.Trim(); query = query.Where(x => x.Station != null && x.Station == _st); }

            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);
            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);

            var allRecords = await query
                .OrderBy(x => x.MachineCode)
                .ThenBy(x => x.Operation)
                .ThenBy(x => x.Datetime)
                .ToListAsync();

            // tuple: (Operation, Minutes, ReasonCode, ReasonName)
            var machineDowntimes = new Dictionary<string, List<(string Operation, double Minutes, string ReasonCode, string ReasonName)>>();

            var grouped = allRecords
                .Where(x => !string.IsNullOrEmpty(x.MachineCode) && !string.IsNullOrEmpty(x.Operation))
                .GroupBy(x => new { x.MachineCode, x.Operation });

            foreach (var group in grouped)
            {
                var records = group.OrderBy(x => x.Datetime).ToList();
                for (int i = 0; i < records.Count; i++)
{
    var cur = records[i];
    if (cur.State?.Trim().ToUpper() != "STOP" || !cur.Datetime.HasValue) continue;

                    for (int j = i + 1; j < records.Count; j++)
                    {
                        var next = records[j];
                        if (!next.Datetime.HasValue) continue;
                        var nextState = next.State?.Trim().ToUpper();

                        if (nextState == "RUN")
                        {
                            var mins = (next.Datetime.Value - cur.Datetime.Value).TotalMinutes;
                            var machineKey = cur.MachineCode!.Trim();
                            if (!machineDowntimes.ContainsKey(machineKey))
                                machineDowntimes[machineKey] = new();

                            machineDowntimes[machineKey].Add((
                                cur.Operation?.Trim() ?? "",
                                mins,
                                string.IsNullOrWhiteSpace(cur.Reason) ? "N/A" : cur.Reason.Trim(),
                                string.IsNullOrWhiteSpace(cur.ErrorName) ? "未确定" : cur.ErrorName.Trim()
                            ));
                            break;
                        }
                        else if (nextState == "STOP") break;
                    }
    
                }
            }

            return machineDowntimes
                .Select(kvp =>
                {
                    // ✅ FIX: đổi 'machine' → '_machineKey' để tránh conflict với parameter 'machine'
                    var _machineKey = kvp.Key;
                    var items = kvp.Value;
                    var totalDt = items.Sum(x => x.Minutes);
                    var mainOp = items.GroupBy(x => x.Operation).OrderByDescending(g => g.Count()).First().Key;

                    return new DowntimeReportByMachine
                    {
                        MachineCode = _machineKey,
                        Operation = mainOp,
                        DowntimeCount = items.Count,
                        TotalDowntimeMinutes = totalDt,
                        TotalDowntimeFormatted = FormatMinutesToTime(totalDt),
                        ReasonDetails = items
                            .GroupBy(x => new { x.ReasonCode, x.ReasonName })
                            .Select(g => new DowntimeReportByMachineReason
                            {
                                MachineCode = _machineKey,
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




        /* 按设备准备图表数据 */
        private MachineDowntimeChartData PrepareMachineChartData(List<DowntimeReportByMachine> machineData)
        {
            return new MachineDowntimeChartData
            {
                MachineCodes    = machineData.Select(x => x.MachineCode).ToList(),
                DowntimeMinutes = machineData.Select(x => Math.Round(x.TotalDowntimeMinutes, 2)).ToList(),
                DowntimeCounts  = machineData.Select(x => x.DowntimeCount).ToList()
            };
        }

        /* 导出停机报告到Excel */

        [HttpGet]
        public async Task<IActionResult> ExportDowntimeReportToExcel(
    string fromDate = "", string toDate = "", string operation = "",
    string reason = "", string location = "", string machine = "",
    string effect = "", string station = "")
        {
            try
            {
                var reportData   = await GetDowntimeReportDataWithPct(fromDate, toDate, operation, reason, location, machine, effect, station);
                var machineData  = await GetDowntimeReportByMachine(fromDate, toDate, operation, reason, location, machine, effect, station);

                // ── Raw detail records for Sheet 3 ──
                var rawQuery = from d in _context.SVN_Downtime_Infos_Devel
                               join rsn in _context.SVN_Downtime_Reasons on d.Reason equals rsn.Reason_Code into reasons
                               from rsn in reasons.DefaultIfEmpty()
                               where d.State != null && d.State.ToUpper() == "STOP"
                               select new { d, ReasonName = rsn != null ? rsn.Reason_Name : "" };

                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var rfd))
                    rawQuery = rawQuery.Where(x => x.d.Datetime.HasValue && x.d.Datetime.Value.Date >= rfd.Date);
                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var rtd))
                    rawQuery = rawQuery.Where(x => x.d.Datetime.HasValue && x.d.Datetime.Value.Date <= rtd.Date);
                if (!string.IsNullOrWhiteSpace(machine))   rawQuery = rawQuery.Where(x => x.d.MachineCode == machine.Trim());
                if (!string.IsNullOrWhiteSpace(operation)) { var _op = operation.Trim(); rawQuery = rawQuery.Where(x => x.d.Operation != null && x.d.Operation.Contains(_op)); }
                if (!string.IsNullOrWhiteSpace(location))  rawQuery = rawQuery.Where(x => x.d.Location == location.Trim());
                if (!string.IsNullOrWhiteSpace(reason))    rawQuery = rawQuery.Where(x => x.d.Reason == reason.Trim());
                if (!string.IsNullOrWhiteSpace(effect))    rawQuery = rawQuery.Where(x => x.d.Effect == effect.Trim());
                if (!string.IsNullOrWhiteSpace(station))   rawQuery = rawQuery.Where(x => x.d.Station == station.Trim());

                var rawStops = await rawQuery.OrderBy(x => x.d.Datetime).ToListAsync();

                // Join TechResponses for Start/Response/End times
                var techResps = await _context.SVN_Downtime_TechResponses
                    .Where(x => x.TechAction == "ACCEPT")
                    .Select(x => new { x.DowntimeId, x.TechUsername, x.RespondDatetime, x.StopDatetime })
                    .ToListAsync();

                var runRecords = await _context.SVN_Downtime_Infos_Devel
                    .Where(x => x.State != null && x.State.ToUpper() == "RUN")
                    .Select(x => new { x.MachineCode, x.Datetime })
                    .ToListAsync();

                using var workbook = new XLWorkbook();

                // ── Helper styles ──
                XLColor headerBg   = XLColor.FromHtml("#1F3864");
                XLColor subHeaderBg = XLColor.FromHtml("#2E75B6");
                XLColor altRow     = XLColor.FromHtml("#EBF3FB");
                XLColor totalBg    = XLColor.FromHtml("#D6E4F0");
                XLColor titleColor = XLColor.White;

                void StyleHeader(IXLCell cell, bool dark = true) {
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = dark ? headerBg : subHeaderBg;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                    cell.Style.Alignment.WrapText   = true;
                    cell.Style.Border.OutsideBorder  = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.White;
                }

                void StyleData(IXLCell cell, bool alt = false) {
                    if (alt) cell.Style.Fill.BackgroundColor = altRow;
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                    cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#BDD7EE");
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Alignment.WrapText = true;
                }

                string FilterInfo() {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(fromDate)) parts.Add($"From: {fromDate}");
                    if (!string.IsNullOrEmpty(toDate))   parts.Add($"To: {toDate}");
                    if (!string.IsNullOrEmpty(machine))  parts.Add($"Machine: {machine}");
                    if (!string.IsNullOrEmpty(operation)) parts.Add($"Line: {operation}");
                    if (!string.IsNullOrEmpty(location)) parts.Add($"Location: {location}");
                    if (!string.IsNullOrEmpty(effect))   parts.Add($"Effect: {(effect=="1"?"Affects Production":"No Effect")}");
                    return parts.Any() ? string.Join("  |  ", parts) : "All data (no filter)";
                }

                // ════════════════════════════════════════════════
                // SHEET 1: Summary by Line
                // ════════════════════════════════════════════════
                var ws1 = workbook.Worksheets.Add("Summary by Line");
                ws1.Style.Font.FontName = "Arial";
                ws1.Style.Font.FontSize = 10;

                int r = 1;
                // Title
                ws1.Cell(r, 1).Value = "DOWNTIME REPORT — Summary by Line";
                ws1.Range(r, 1, r, 5).Merge();
                ws1.Cell(r, 1).Style.Font.Bold = true;
                ws1.Cell(r, 1).Style.Font.FontSize = 14;
                ws1.Cell(r, 1).Style.Font.FontColor = XLColor.White;
                ws1.Cell(r, 1).Style.Fill.BackgroundColor = headerBg;
                ws1.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws1.Row(r).Height = 28; r++;

                ws1.Cell(r, 1).Value = FilterInfo();
                ws1.Range(r, 1, r, 5).Merge();
                ws1.Cell(r, 1).Style.Font.Italic = true;
                ws1.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#1F3864");
                ws1.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DEEAF1");
                ws1.Row(r).Height = 16; r++;
                ws1.Cell(r, 1).Value = $"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}";
                ws1.Range(r, 1, r, 5).Merge();
                ws1.Cell(r, 1).Style.Font.Italic = true;
                ws1.Cell(r, 1).Style.Font.FontColor = XLColor.Gray;
                r += 2;

                // Headers
                string[] sh1 = { "#", "Line / Operation", "Downtime Count", "Total Downtime", "Total Downtime (min)" };
                for (int c = 0; c < sh1.Length; c++) { StyleHeader(ws1.Cell(r, c + 1)); ws1.Cell(r, c + 1).Value = sh1[c]; }
                ws1.Row(r).Height = 22; r++;

                int seq1 = 0;
                foreach (var op in reportData)
                {
                    seq1++;
                    bool alt = seq1 % 2 == 0;
                    var row = ws1.Row(r);
                    ws1.Cell(r, 1).Value = seq1;
                    ws1.Cell(r, 2).Value = op.Operation;
                    ws1.Cell(r, 3).Value = op.TotalDowntimeCount;
                    ws1.Cell(r, 4).Value = op.TotalDowntimeFormatted;
                    ws1.Cell(r, 5).Value = Math.Round(op.TotalDowntimeMinutes, 1);
                    for (int c = 1; c <= 5; c++) StyleData(ws1.Cell(r, c), alt);
                    ws1.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws1.Cell(r, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws1.Cell(r, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    r++;
                }
                // Total row
                ws1.Cell(r, 1).Value = "TOTAL";
                ws1.Range(r, 1, r, 2).Merge();
                ws1.Cell(r, 3).Value = reportData.Sum(x => x.TotalDowntimeCount);
                ws1.Cell(r, 4).Value = FormatMinutesToTime(reportData.Sum(x => x.TotalDowntimeMinutes));
                ws1.Cell(r, 5).Value = Math.Round(reportData.Sum(x => x.TotalDowntimeMinutes), 1);
                for (int c = 1; c <= 5; c++) {
                    ws1.Cell(r, c).Style.Font.Bold = true;
                    ws1.Cell(r, c).Style.Fill.BackgroundColor = totalBg;
                    ws1.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                }

                ws1.Column(1).Width = 5; ws1.Column(2).Width = 35; ws1.Column(3).Width = 16;
                ws1.Column(4).Width = 18; ws1.Column(5).Width = 20;

                // ════════════════════════════════════════════════
                // SHEET 2: Summary by Machine
                // ════════════════════════════════════════════════
                var ws2 = workbook.Worksheets.Add("Summary by Machine");
                ws2.Style.Font.FontName = "Arial";
                ws2.Style.Font.FontSize = 10;

                r = 1;
                ws2.Cell(r, 1).Value = "DOWNTIME REPORT — Summary by Machine";
                ws2.Range(r, 1, r, 6).Merge();
                ws2.Cell(r, 1).Style.Font.Bold = true;
                ws2.Cell(r, 1).Style.Font.FontSize = 14;
                ws2.Cell(r, 1).Style.Font.FontColor = XLColor.White;
                ws2.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1A5276");
                ws2.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws2.Row(r).Height = 28; r++;
                ws2.Cell(r, 1).Value = FilterInfo();
                ws2.Range(r, 1, r, 6).Merge();
                ws2.Cell(r, 1).Style.Font.Italic = true;
                ws2.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D6EAF8");
                r += 2;

                string[] sh2 = { "#", "Machine No.", "Line", "Downtime Count", "Total Downtime", "Total Downtime (min)" };
                for (int c = 0; c < sh2.Length; c++) { StyleHeader(ws2.Cell(r, c + 1), false); ws2.Cell(r, c + 1).Value = sh2[c]; }
                ws2.Row(r).Height = 22; r++;

                int seq2 = 0;
                double grandTotal = machineData.Sum(x => x.TotalDowntimeMinutes);
                foreach (var m in machineData)
                {
                    seq2++;
                    bool alt = seq2 % 2 == 0;
                    ws2.Cell(r, 1).Value = seq2;
                    ws2.Cell(r, 2).Value = m.MachineCode;
                    ws2.Cell(r, 3).Value = m.Operation;
                    ws2.Cell(r, 4).Value = m.DowntimeCount;
                    ws2.Cell(r, 5).Value = m.TotalDowntimeFormatted;
                    ws2.Cell(r, 6).Value = Math.Round(m.TotalDowntimeMinutes, 1);
                    for (int c = 1; c <= 6; c++) StyleData(ws2.Cell(r, c), alt);
                    ws2.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws2.Cell(r, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    r++;
                }
                ws2.Cell(r, 1).Value = "TOTAL";
                ws2.Range(r, 1, r, 3).Merge();
                ws2.Cell(r, 4).Value = machineData.Sum(x => x.DowntimeCount);
                ws2.Cell(r, 5).Value = FormatMinutesToTime(grandTotal);
                ws2.Cell(r, 6).Value = Math.Round(grandTotal, 1);
                for (int c = 1; c <= 6; c++) {
                    ws2.Cell(r, c).Style.Font.Bold = true;
                    ws2.Cell(r, c).Style.Fill.BackgroundColor = totalBg;
                }

                ws2.Column(1).Width = 5; ws2.Column(2).Width = 32; ws2.Column(3).Width = 28;
                ws2.Column(4).Width = 15; ws2.Column(5).Width = 16; ws2.Column(6).Width = 20;

                // ════════════════════════════════════════════════
                // SHEET 3: Detail Records (template with AutoFilter)
                // ════════════════════════════════════════════════
                var ws3 = workbook.Worksheets.Add("Detail Records");
                ws3.Style.Font.FontName = "Arial";
                ws3.Style.Font.FontSize = 10;

                r = 1;
                ws3.Cell(r, 1).Value = "DOWNTIME REPORT — Detail Records";
                ws3.Range(r, 1, r, 18).Merge();
                ws3.Cell(r, 1).Style.Font.Bold = true;
                ws3.Cell(r, 1).Style.Font.FontSize = 14;
                ws3.Cell(r, 1).Style.Font.FontColor = XLColor.White;
                ws3.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E8449");
                ws3.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws3.Row(r).Height = 28; r++;
                ws3.Cell(r, 1).Value = FilterInfo();
                ws3.Range(r, 1, r, 18).Merge();
                ws3.Cell(r, 1).Style.Font.Italic = true;
                ws3.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D5F5E3");
                r += 2;

                string[] sh3 = {
                    "Datetime", "Operation", "Machine", "Location", "Reason",
                    "Station", "Image", "Problem Description", "Root Cause",
                    "Start Time", "Response Time", "End Time",
                    "Response Duration (min)", "Downtime (min)",
                    "Action", "Spare Parts", "Employee Name", "Effect"
                };
                int headerRow3 = r;
                for (int c = 0; c < sh3.Length; c++) {
                    var cell = ws3.Cell(r, c + 1);
                    cell.Value = sh3[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E8449");
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                    cell.Style.Alignment.WrapText   = true;
                    cell.Style.Border.OutsideBorder  = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.White;
                }
                ws3.Row(r).Height = 32; r++;

                int seq3 = 0;
                foreach (var item in rawStops)
                {
                    seq3++;
                    bool alt = seq3 % 2 == 0;
                    var d = item.d;

                    // Find tech response
                    var tr = techResps.Where(t => t.DowntimeId == d.Id).FirstOrDefault();
                    DateTime? startDt  = d.Datetime;
                    DateTime? respDt   = tr?.RespondDatetime;
                    DateTime? endDt    = runRecords
                        .Where(rr => rr.MachineCode == d.MachineCode && rr.Datetime.HasValue && rr.Datetime > d.Datetime)
                        .OrderBy(rr => rr.Datetime)
                        .Select(rr => rr.Datetime)
                        .FirstOrDefault();

                    double respDuration = (startDt.HasValue && respDt.HasValue) ? (respDt.Value - startDt.Value).TotalMinutes : 0;
                    double totalDT      = (startDt.HasValue && endDt.HasValue)  ? (endDt.Value  - startDt.Value).TotalMinutes  : 0;

                    string effStr = d.Effect == "1" ? "Yes" : d.Effect == "2" ? "No" : "-";

                    ws3.Cell(r, 1).Value  = d.Datetime?.ToString("dd/MM/yyyy HH:mm") ?? "-";
                    ws3.Cell(r, 2).Value  = d.Operation   ?? "-";
                    ws3.Cell(r, 3).Value  = d.MachineCode ?? "-";
                    ws3.Cell(r, 4).Value  = d.Location    ?? "-";
                    ws3.Cell(r, 5).Value  = item.ReasonName.Length > 0 ? item.ReasonName : (d.Reason ?? "-");
                    ws3.Cell(r, 6).Value  = d.Station     ?? "-";
                    if (!string.IsNullOrEmpty(d.Image))
                    {
                        try
                        {
                            var imgRelative = d.Image;
                            if (imgRelative.StartsWith("/downtime"))
                                imgRelative = imgRelative.Substring("/downtime".Length);

                            var imgPath = Path.Combine(
                                _webHostEnvironment.WebRootPath,
                                imgRelative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                            if (System.IO.File.Exists(imgPath))
                            {
                                ws3.Column(7).Width = 20;
                                ws3.Row(r).Height = 75;

                                var pic = ws3.AddPicture(imgPath);
                                pic.MoveTo(ws3.Cell(r, 7), 5, 4);
                                pic.WithSize(95, 65);
                                ws3.Cell(r, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                ws3.Cell(r, 7).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            }
                            else
                            {
                                ws3.Cell(r, 7).Value = "No image";
                            }
                        }
                        catch (Exception ex)
                        {
                            ws3.Cell(r, 7).Value = "Error";
                        }
                    }



                    ws3.Cell(r, 8).Value  = d.Description ?? "-";
                    ws3.Cell(r, 9).Value  = d.RootCause   ?? "-";
                    ws3.Cell(r, 10).Value = startDt?.ToString("dd/MM/yyyy HH:mm") ?? "-";
                    ws3.Cell(r, 11).Value = respDt?.ToString("dd/MM/yyyy HH:mm")  ?? "-";
                    ws3.Cell(r, 12).Value = endDt?.ToString("dd/MM/yyyy HH:mm")   ?? "-";
                    if (respDuration > 0) ws3.Cell(r, 13).Value = Math.Round(respDuration, 1);
                    else ws3.Cell(r, 13).Value = "-";
                    if (totalDT > 0) ws3.Cell(r, 14).Value = Math.Round(totalDT, 1);
                    else ws3.Cell(r, 14).Value = "-";
                    ws3.Cell(r, 15).Value = d.Action     ?? "-";
                    ws3.Cell(r, 16).Value = d.SpareParts ?? "-";
                    ws3.Cell(r, 17).Value = d.EmployeeName ?? d.EmployeeCode ?? "-";
                    ws3.Cell(r, 18).Value = effStr;

                    for (int c = 1; c <= 18; c++) {
                        var cell = ws3.Cell(r, c);
                        if (alt) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAFAF1");
                        cell.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                        cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#A9DFBF");
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    }
                    // Right-align numbers
                    ws3.Cell(r, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws3.Cell(r, 14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws3.Cell(r, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Highlight unresolved rows
                    if (totalDT <= 0 && endDt == null)
                        ws3.Cell(r, 14).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDEDEC");

                    ws3.Row(r).Height = 18;
                    r++;
                }

                // AutoFilter on header row
                ws3.Range(headerRow3, 1, r - 1, 18).SetAutoFilter();

                // Freeze pane below header
                ws3.SheetView.FreezeRows(headerRow3);

                // Column widths
                int[] ws3Widths = { 18, 28, 32, 16, 28, 14, 20, 32, 28, 18, 18, 18, 20, 16, 28, 20, 20, 14 };
                for (int c = 0; c < ws3Widths.Length; c++)
                    ws3.Column(c + 1).Width = ws3Widths[c];

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return File(stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"DowntimeReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Export error: {ex.Message}" });
            }
        }


        // ══════════════════════════════════════════════════════
        // POST /Status/TechnicianRespond
        // 技术员点击接受或等待 → 更新数据库 + SignalR推送回操作员
        // ══════════════════════════════════════════════════════
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> TechnicianRespond([FromBody] TechRespondDto dto)
        {
            var techUser = !string.IsNullOrWhiteSpace(dto.TechEmployeeName)
            ? dto.TechEmployeeName
            : HttpContext.Session.GetString("UserName") ?? "技术员";

            var record = await _context.SVN_Downtime_TechResponses.FindAsync(dto.TechResponseId);
            if (record == null)
                return Json(new { success = false, message = "记录未找到" });

            record.TechAction = dto.Action;   // "ACCEPT" | "REJECT"
            record.TechUsername = techUser;
            record.RespondDatetime = DateTime.Now;
            await _context.SaveChangesAsync();

            // ── ACCEPT → cập nhật record STOP gốc với thông tin kỹ thuật + tạo RESPONSE ──
            if (dto.Action == "ACCEPT")
            {
                var stopRecord = await _context.SVN_Downtime_Infos_Devel.FindAsync(record.DowntimeId);

                // Patch các field kỹ thuật vào record STOP gốc (PIE mới biết)
                if (stopRecord != null)
                {
                    if (!string.IsNullOrWhiteSpace(dto.Reason))       stopRecord.Reason       = dto.Reason;
                    if (!string.IsNullOrWhiteSpace(dto.Effect))       stopRecord.Effect       = dto.Effect;
                    if (!string.IsNullOrWhiteSpace(dto.EstimateTime)) stopRecord.EstimateTime = dto.EstimateTime;
                    if (!string.IsNullOrWhiteSpace(dto.Description))  stopRecord.Description  = dto.Description;
                    await _context.SaveChangesAsync();
                }

                var responseRecord = new SVN_Downtime_Info_Devel
                {
                    State        = "RESPONSE",
                    Operation    = record.Operation,
                    MachineCode  = record.MachineCode,
                    Location     = record.Location,
                    EmployeeCode = !string.IsNullOrWhiteSpace(dto.TechEmployeeName)
                                ? dto.TechEmployeeName
                                : record.EmployeeCode,
                    EmployeeName = !string.IsNullOrWhiteSpace(dto.TechEmployeeName)
                                ? dto.TechEmployeeName
                                : record.EmployeeName,
                    Reason       = dto.Reason ?? record.Reason,
                    Effect       = dto.Effect ?? record.Effect,
                    EstimateTime = dto.EstimateTime ?? record.EstimateTime,
                    Station      = record.Station,
                    Description  = $"{techUser} accepted the call",
                    Code         = stopRecord?.Code,
                    Name         = stopRecord?.Name,
                    Datetime     = DateTime.Now
                };
                _context.SVN_Downtime_Infos_Devel.Add(responseRecord);
                await _context.SaveChangesAsync();
            }

            // ── REJECT → tạo bản ghi MỚI giống STOP nhưng State = "REJECT", lưu lý do từ chối ──
            if (dto.Action == "REJECT")
            {
                var stopRecord = await _context.SVN_Downtime_Infos_Devel.FindAsync(record.DowntimeId);

                var rejectRecord = new SVN_Downtime_Info_Devel
                {
                    State = "REJECT",
                    Operation = record.Operation,
                    MachineCode = record.MachineCode,
                    Location = record.Location,
                    EmployeeCode = record.EmployeeCode,
                    EmployeeName = record.EmployeeName,
                    Reason = record.Reason,
                    Effect = record.Effect,
                    Station = record.Station,
                    // Ghi lý do từ chối vào Description
                    Description = string.IsNullOrWhiteSpace(dto.RejectReason)
                                       ? record.Description
                                       : $"[REJECT] {dto.RejectReason}",
                    EstimateTime = record.EstimateTime,
                    Code = stopRecord?.Code,
                    Name = stopRecord?.Name,
                    Image = stopRecord?.Image,
                    Datetime = DateTime.Now
                };
                _context.SVN_Downtime_Infos_Devel.Add(rejectRecord);
                await _context.SaveChangesAsync();
            }

            // ── Thông báo về Prod & Admin ──
            if (!string.IsNullOrWhiteSpace(dto.OperatorUsername))
            {
                string notifTitle = dto.Action switch
                {
                    "ACCEPT" => $"✅ 技术员 [{techUser}] 正在前往维修设备 {dto.MachineCode ?? "-"}",
                    "REJECT" => $"❌ 技术员 [{techUser}] 已拒绝 — 设备: {dto.MachineCode ?? "-"}"
                                 + (string.IsNullOrWhiteSpace(dto.RejectReason) ? "" : $" | 原因: {dto.RejectReason}"),
                    _ => $"⏳ 技术员 [{techUser}] 已查看 — 请稍候"
                };

                string notifType = dto.Action == "REJECT" ? "TECH_REJECT" : "TECH_RESPONSE";

                await SaveNotificationAsync(
                    recipientUsername: dto.OperatorUsername,
                    recipientRole: "Production",
                    notifType: notifType,
                    title: notifTitle,
                    body: dto.Action == "REJECT" ? dto.RejectReason : null,
                    machineCode: dto.MachineCode,
                    techResponseId: dto.TechResponseId,
                    techAction: dto.Action,
                    techName: techUser
                );

                await SaveNotificationAsync(
                    recipientUsername: "ALL_ADMIN",
                    recipientRole: "Admin",
                    notifType: notifType,
                    title: notifTitle,
                    body: dto.Action == "REJECT" ? dto.RejectReason : null,
                    machineCode: dto.MachineCode,
                    techResponseId: dto.TechResponseId,
                    techAction: dto.Action,
                    techName: techUser
                );
            }

            // ── SignalR → Prod ──
            if (!string.IsNullOrWhiteSpace(dto.OperatorUsername))
            {
                if (dto.Action == "REJECT")
                {
                    await _hubContext.Clients
                        .Group($"Operator_{dto.OperatorUsername}")
                        .SendAsync("ReceiveTechReject", new
                        {
                            techName = techUser,
                            machineCode = dto.MachineCode ?? "",
                            rejectReason = dto.RejectReason ?? "",
                            message = $"❌ 技术员 [{techUser}] 已拒绝 — 设备: {dto.MachineCode ?? "-"}"
                                           + (string.IsNullOrWhiteSpace(dto.RejectReason) ? "" : $" 理由: {dto.RejectReason}"),
                            datetime = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                        });
                }
                else
                {
                    await _hubContext.Clients
                        .Group($"Operator_{dto.OperatorUsername}")
                        .SendAsync("ReceiveTechResponse", new
                        {
                            action = dto.Action,
                            techName = techUser,
                            machineCode = dto.MachineCode ?? "",
                            message = dto.Action == "ACCEPT"
                                ? $"✅ 技术员 [{techUser}] 已收到信息并正在准备维修设备 {dto.MachineCode}。"
                                : $"⏳ 技术员 [{techUser}] 已查看通知，请稍候。",
                            datetime = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                        });
                }
            }

            return Json(new { success = true });
        }


        // ══════════════════════════════════════════════════════════════════════
        // POST /Status/TechnicianFixComplete
        // PIE bấm "已修复完成" → chỉ thông báo PROD bấm Run, KHÔNG tạo record RUN
        // Thông tin kỹ thuật sẽ được patch vào STOP record qua PostRunEdit
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> TechnicianFixComplete([FromBody] TechFixCompleteDto dto)
        {
            var techUser = HttpContext.Session.GetString("UserName") ?? "技术员";

            var techResp = await _context.SVN_Downtime_TechResponses.FindAsync(dto.TechResponseId);
            if (techResp == null)
                return Json(new { success = false, message = "记录未找到" });

            var title = $"✅ 设备 {techResp.MachineCode ?? "-"} 已修复完成，请按运行！";
            var body  = $"技术员 [{techUser}] 已完成维修。工序: {techResp.Operation ?? "-"} | 请生产员按下运行按钮。";

            // ── 通知生产端按 Run ──
            if (!string.IsNullOrWhiteSpace(techResp.OperatorUsername))
            {
                await SaveNotificationAsync(
                    recipientUsername : techResp.OperatorUsername,
                    recipientRole     : "Production",
                    notifType         : "FIX_COMPLETE",
                    title             : title,
                    body              : body,
                    machineCode       : techResp.MachineCode,
                    operation         : techResp.Operation,
                    techResponseId    : dto.TechResponseId,
                    techName          : techUser
                );

                // SignalR → 生产端
                await _hubContext.Clients
                    .Group($"Operator_{techResp.OperatorUsername}")
                    .SendAsync("ReceiveFixComplete", new
                    {
                        machineCode = techResp.MachineCode ?? "",
                        operation   = techResp.Operation   ?? "",
                        techName    = techUser,
                        message     = $"✅ 设备 {techResp.MachineCode} 已维修完成，请按运行按钮！",
                        datetime    = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                    });
            }

            // ── 通知管理员 ──
            await SaveNotificationAsync(
                recipientUsername : "ALL_ADMIN",
                recipientRole     : "Admin",
                notifType         : "FIX_COMPLETE",
                title             : title,
                body              : body,
                machineCode       : techResp.MachineCode,
                operation         : techResp.Operation,
                techResponseId    : dto.TechResponseId,
                techName          : techUser
            );

            // ── SignalR → Tech group (badge update) ──
            await _hubContext.Clients.Group("TechnicianGroup").SendAsync("ReceiveFixCompleteAck", new
            {
                techResponseId = dto.TechResponseId,
                machineCode    = techResp.MachineCode ?? "",
                operation      = techResp.Operation   ?? "",
                datetime       = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
            });

            return Json(new { success = true });
        }

        public class TechFixCompleteDto
        {
            public int TechResponseId { get; set; }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 通知API — 添加到StatusController.cs中
        // ══════════════════════════════════════════════════════════════════════
        // 位置：粘贴到StatusController类中，靠近TechnicianRespond区域

        // ══════════════════════════════════════════════════════════════════════
        // GET /Status/GetStopRecord?techResponseId=xxx
        // Lấy record STOP gốc qua TechResponse để PIE patch thông tin kỹ thuật
        // ══════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetRunRecord(string machineNo)
        {
            if (string.IsNullOrWhiteSpace(machineNo))
                return Json(new { success = false });

            // Lấy TechResponse mới nhất của máy → tìm DowntimeId → STOP record
            var techResp = await _context.SVN_Downtime_TechResponses
                .Where(x => x.MachineCode != null && x.MachineCode.Trim() == machineNo.Trim())
                .OrderByDescending(x => x.StopDatetime)
                .FirstOrDefaultAsync();

            if (techResp == null)
                return Json(new { success = false });

            var stopRecord = await _context.SVN_Downtime_Infos_Devel.FindAsync(techResp.DowntimeId);
            if (stopRecord == null)
                return Json(new { success = false });

            return Json(new { success = true, data = new
            {
                stopRecord.Id,
                stopRecord.State,
                stopRecord.MachineCode,
                stopRecord.Operation,
                stopRecord.Reason,
                stopRecord.Effect,
                stopRecord.EstimateTime,
                stopRecord.Description,
                stopRecord.RootCause,
                stopRecord.Action,
                stopRecord.SpareParts
            }});
        }

        // ── Overload: tìm theo techResponseId trực tiếp ──
        [HttpGet]
        public async Task<IActionResult> GetStopRecordByTechResponse(int techResponseId)
        {
            var techResp = await _context.SVN_Downtime_TechResponses.FindAsync(techResponseId);
            if (techResp == null)
                return Json(new { success = false });

            var stopRecord = await _context.SVN_Downtime_Infos_Devel.FindAsync(techResp.DowntimeId);
            if (stopRecord == null)
                return Json(new { success = false });

            return Json(new { success = true, data = new
            {
                stopRecord.Id,
                stopRecord.Reason,
                stopRecord.Effect,
                stopRecord.EstimateTime,
                stopRecord.Description,
                stopRecord.RootCause,
                stopRecord.Action,
                stopRecord.SpareParts
            }});
        }

        // ── Lấy tên Tech PIE từ RESPONSE record mới nhất của máy ──
        [HttpGet]
        public async Task<IActionResult> GetTechNameByMachine(string machineNo)
        {
            if (string.IsNullOrWhiteSpace(machineNo))
                return Json(new { techName = "" });

            // Lấy TechResponse mới nhất → TechUsername là PIE employee name
            var techResp = await _context.SVN_Downtime_TechResponses
                .Where(x => x.MachineCode != null
                         && x.MachineCode.Trim() == machineNo.Trim()
                         && x.TechAction == "ACCEPT")
                .OrderByDescending(x => x.RespondDatetime)
                .Select(x => new { x.TechUsername, x.EmployeeName })
                .FirstOrDefaultAsync();

            // TechUsername là PIE employee name (vd: "Alex Jin")
            var name = techResp?.TechUsername ?? techResp?.EmployeeName ?? "";
            return Json(new { techName = name });
        }

        // ══════════════════════════════════════════════════════════════════════
        // POST /Status/PostRunEdit
        // PROD / PIE bổ sung thông tin sau khi bấm Run
        // Cho phép sửa: Description, RootCause, Action, SpareParts
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> PostRunEdit([FromBody] PostRunEditDto dto)
        {
            if (dto == null)
                return Json(new { success = false, message = "Invalid request body" });

            var stopRecord = await _context.SVN_Downtime_Infos_Devel.FindAsync(dto.Id);
            if (stopRecord == null)
                return Json(new { success = false, message = $"Record #{dto.Id} not found" });

            // ── Patch STOP: tất cả trừ Description (giữ nguyên của Prod) ──
            if (dto.Reason     != null) stopRecord.Reason     = dto.Reason;
            if (dto.Effect     != null) stopRecord.Effect     = dto.Effect;
            if (dto.RootCause  != null) stopRecord.RootCause  = dto.RootCause;
            if (dto.Action     != null) stopRecord.Action     = dto.Action;
            if (dto.SpareParts != null) stopRecord.SpareParts = dto.SpareParts;
            // Description STOP: KHÔNG đụng vào

            // Lưu Tech Description tạm vào EstimateTime (field ít dùng) để copy sang RUN sau
            // Dùng prefix [TECHDESC] để phân biệt
            if (!string.IsNullOrWhiteSpace(dto.Description))
                stopRecord.EstimateTime = $"[TECHDESC]{dto.Description.Trim()}";

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        public class PostRunEditDto
        {
            public int     Id           { get; set; }
            public string? Reason       { get; set; }
            public string? Effect       { get; set; }
            public string? EstimateTime { get; set; }
            public string? Description  { get; set; }
            public string? RootCause    { get; set; }
            public string? Action       { get; set; }
            public string? SpareParts   { get; set; }
        }

        // ── Helper: 保存通知到数据库 ──────────────────────────────────
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
        // 返回当前用户的通知（用于加载页面/刷新）
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
                // 技术员接收所有发送给"ALL_TECH"和自己的通知
                query = query.Where(n => n.RecipientUsername == "ALL_TECH"
                                      || n.RecipientUsername == username);
            }
            else if (role == "Admin")
            {
                // 管理员接收所有
                query = query.Where(n => n.RecipientUsername == "ALL_TECH"
                                      || n.RecipientUsername == "ALL_ADMIN"
                                      || n.RecipientUsername == username);
            }
            else
            {
                // 生产端：只接收发送给自己用户名的通知
                query = query.Where(n => n.RecipientUsername == username);
            }

            var today = DateTime.Now.Date;
            var list = await query
                .Where(n => n.CreatedAt.Date == today)          // 只取今天（可根据需要调整）
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
        // 将单条通知标记为已读
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> MarkNotificationRead([FromBody] MarkReadDto dto)
        {
            var username = HttpContext.Session.GetString("UserName") ?? "";
            if (string.IsNullOrEmpty(username)) return Json(new { success = false });

            var notif = await _context.SVN_Notifications.FindAsync(dto.Id);
            if (notif == null) return Json(new { success = false, message = "未找到通知" });

            notif.IsRead = true;
            notif.ReadAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // Thêm vào StatusController.cs, cạnh MarkNotificationRead:
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteMyNotification([FromBody] MarkReadDto dto)
        {
            var username = HttpContext.Session.GetString("UserName") ?? "";
            if (string.IsNullOrEmpty(username)) return Json(new { success = false });

            var notif = await _context.SVN_Notifications.FindAsync(dto.Id);
            if (notif == null) return Json(new { success = false, message = "未找到通知" });

            // Chỉ cho xóa notification của chính mình
            if (notif.RecipientUsername != username)
                return Json(new { success = false, message = "无权限删除" });

            _context.SVN_Notifications.Remove(notif);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ── POST /Status/DeleteTechNotification ───────────────────────────────
        // Dành cho Tech: xóa notification theo TechResponseId (vì recipient = "ALL_TECH")
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteTechNotification([FromBody] DeleteTechNotifDto dto)
        {
            var username = HttpContext.Session.GetString("UserName") ?? "";
            var role     = HttpContext.Session.GetString("UserRole") ?? "";
            if (string.IsNullOrEmpty(username) || role != "Technical")
                return Json(new { success = false, message = "无权限" });

            // Xóa tất cả notification liên quan đến TechResponseId này mà Tech nhận được
            var notifs = await _context.SVN_Notifications
                .Where(n => n.TechResponseId == dto.TechResponseId
                         && (n.RecipientUsername == "ALL_TECH" || n.RecipientUsername == username))
                .ToListAsync();

            if (!notifs.Any())
                return Json(new { success = false, message = "未找到通知" });

            _context.SVN_Notifications.RemoveRange(notifs);
            await _context.SaveChangesAsync();
            return Json(new { success = true, deletedCount = notifs.Count });
        }


        // ── POST /Status/MarkAllNotificationsRead ─────────────────────────────
        // 将当前用户的所有通知标记为已读
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

        public class DeleteTechNotifDto
        {
            public int TechResponseId { get; set; }
        }



        public class TechRespondDto
        {
            public int     TechResponseId   { get; set; }
            public string  Action           { get; set; } = "";
            public string? OperatorUsername { get; set; }
            public string? MachineCode      { get; set; }
            public string? RejectReason     { get; set; }
            public string? TechEmployeeName { get; set; }

            // Các field kỹ thuật PIE điền khi ACCEPT (PROD không điền)
            public string? Reason           { get; set; }
            public string? Effect           { get; set; }
            public string? EstimateTime     { get; set; }
            public string? Description      { get; set; }
        }

        // ── GET /Status/ProductionNotifications ──
        // Trang thông báo riêng cho Production role
        [HttpGet]
        public async Task<IActionResult> ProductionNotifications()
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (string.IsNullOrEmpty(role))
                return RedirectToAction("Login", "Account");

            var username = HttpContext.Session.GetString("UserName") ?? "";
            var today    = DateTime.Now.Date;

            var notifications = await _context.SVN_Notifications
                .AsNoTracking()
                .Where(n => n.RecipientUsername == username && n.CreatedAt.Date == today)
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToListAsync();

            // Tính prodRun cho từng FIX_COMPLETE notification
            ViewBag.ProdRunMap = notifications
                .Where(n => n.NotifType == "FIX_COMPLETE" && n.TechResponseId.HasValue)
                .ToDictionary(
                    n => n.Id,
                    n => {
                        var techResp = _context.SVN_Downtime_TechResponses
                            .Where(tr => tr.Id == n.TechResponseId)
                            .Select(tr => new { tr.MachineCode, tr.StopDatetime })
                            .FirstOrDefault();
                        if (techResp == null) return false;
                        return _context.SVN_Downtime_Infos_Devel
                            .Any(r => r.MachineCode == techResp.MachineCode
                                   && r.State == "RUN"
                                   && r.Datetime > techResp.StopDatetime);
                    }
                );

            // Check xem CALL_DRI_REQUEST đã được gọi DRI chưa
            ViewBag.DRICalledMap = notifications
                .Where(n => n.NotifType == "CALL_DRI_REQUEST" && n.TechResponseId.HasValue)
                .ToDictionary(
                    n => n.Id,
                    n => _context.SVN_Notifications
                            .Any(x => x.TechResponseId == n.TechResponseId
                                   && x.NotifType == "DRI_CALL")
                );

            // Đánh dấu tất cả là đã đọc
            var unread = await _context.SVN_Notifications
                .Where(n => n.RecipientUsername == username && !n.IsRead && n.CreatedAt.Date == today)
                .ToListAsync();
            if (unread.Any())
            {
                var now = DateTime.Now;
                foreach (var n in unread) { n.IsRead = true; n.ReadAt = now; }
                await _context.SaveChangesAsync();
            }

            return View("ProductionNotifications", notifications);
        }

        // ── 管理员：渲染面板 ──
        [HttpGet]
        public IActionResult AdminPanel()
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "Admin") return RedirectToAction("Login", "Account");
            return View("~/Views/Account/AdminPanel.cshtml");
        }


        // ── 管理员：获取记录列表（分页+筛选） ──
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


        // ── 管理员：更新记录 ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminUpdateRecord([FromBody] AdminRecordDto dto)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            var rec = await _context.SVN_Downtime_Infos_Devel.FindAsync(dto.Id);
            if (rec == null)
                return Json(new { success = false, message = "未找到记录" });

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


        // ── 管理员：删除记录 ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminDeleteRecord([FromBody] AdminDeleteDto dto)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            var rec = await _context.SVN_Downtime_Infos_Devel.FindAsync(dto.Id);
            if (rec == null)
                return Json(new { success = false, message = "未找到记录" });

            _context.SVN_Downtime_Infos_Devel.Remove(rec);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ────────────────────────────────────────────────────────────────
        // EMPLOYEE MANAGEMENT  (bảng SM_EmployInfo)
        // ────────────────────────────────────────────────────────────────

        /// <summary>GET tất cả nhân viên</summary>
        [HttpGet]
        public async Task<IActionResult> AdminGetEmployees()
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            var list = await _context.SM_EmployInfos
                .AsNoTracking()
                .OrderBy(e => e.EnglishName)
                .Select(e => new
                {
                    e.Id,
                    EmployeeID  = e.EmployeeID  ?? "",
                    ChineseName = e.ChineseName ?? "",
                    EnglishName = e.EnglishName ?? "",
                })
                .ToListAsync();

            return Json(new { employees = list });
        }

        /// <summary>POST thêm mới hoặc cập nhật nhân viên (id == 0 → insert)</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminSaveEmployee([FromBody] EmployeeDto dto)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.EmployeeID))
                return Json(new { success = false, message = "工号不能为空 / EmployeeID is required" });

            if (dto.Id == 0)
            {
                // INSERT
                _context.SM_EmployInfos.Add(new SM_EmployInfo
                {
                    EmployeeID  = dto.EmployeeID.Trim(),
                    ChineseName = dto.ChineseName?.Trim() ?? "",
                    EnglishName = dto.EnglishName?.Trim() ?? "",
                });
            }
            else
            {
                // UPDATE
                var rec = await _context.SM_EmployInfos.FindAsync(dto.Id);
                if (rec == null)
                    return Json(new { success = false, message = "未找到记录 / Record not found" });

                rec.EmployeeID  = dto.EmployeeID.Trim();
                rec.ChineseName = dto.ChineseName?.Trim() ?? "";
                rec.EnglishName = dto.EnglishName?.Trim() ?? "";
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        /// <summary>POST xóa nhân viên</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminDeleteEmployee([FromBody] AdminDeleteDto dto)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            var rec = await _context.SM_EmployInfos.FindAsync(dto.Id);
            if (rec == null)
                return Json(new { success = false, message = "未找到记录 / Record not found" });

            _context.SM_EmployInfos.Remove(rec);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ────────────────────────────────────────────────────────────────
        // ACCOUNT MANAGEMENT  (bảng SVN_Downtime_Account)
        // ────────────────────────────────────────────────────────────────

        /// <summary>GET tất cả tài khoản</summary>
        [HttpGet]
        public async Task<IActionResult> AdminGetAccounts()
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            var list = await _context.SVN_Downtime_Accounts
                .AsNoTracking()
                .OrderBy(a => a.Username)
                .Select(a => new
                {
                    a.Id,
                    a.Username,
                    a.Password,
                    a.Role,
                })
                .ToListAsync();

            return Json(new { accounts = list });
        }

        /// <summary>POST thêm mới hoặc cập nhật tài khoản (id == 0 → insert)</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminSaveAccount([FromBody] AccountDto dto)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
                return Json(new { success = false, message = "用户名和密码不能为空 / Username & Password are required" });

            if (dto.Id == 0)
            {
                // Kiểm tra username đã tồn tại chưa
                var exists = await _context.SVN_Downtime_Accounts
                    .AnyAsync(a => a.Username.ToLower() == dto.Username.Trim().ToLower());
                if (exists)
                    return Json(new { success = false, message = "用户名已存在 / Username already exists" });

                _context.SVN_Downtime_Accounts.Add(new SVN_Downtime_Account
                {
                    Username = dto.Username.Trim(),
                    Password = dto.Password.Trim(),
                    Role     = dto.Role ?? "Production",
                });
            }
            else
            {
                var rec = await _context.SVN_Downtime_Accounts.FindAsync(dto.Id);
                if (rec == null)
                    return Json(new { success = false, message = "未找到记录 / Record not found" });

                // Nếu đổi username thì kiểm tra trùng
                if (!rec.Username.Equals(dto.Username.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var dup = await _context.SVN_Downtime_Accounts
                        .AnyAsync(a => a.Id != dto.Id && a.Username.ToLower() == dto.Username.Trim().ToLower());
                    if (dup)
                        return Json(new { success = false, message = "用户名已存在 / Username already exists" });
                }

                rec.Username = dto.Username.Trim();
                rec.Password = dto.Password.Trim();
                rec.Role     = dto.Role ?? rec.Role;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        /// <summary>POST xóa tài khoản</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminDeleteAccount([FromBody] AdminDeleteDto dto)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            // Không cho xóa tài khoản đang đăng nhập
            var currentUser = HttpContext.Session.GetString("UserName") ?? "";
            var rec = await _context.SVN_Downtime_Accounts.FindAsync(dto.Id);
            if (rec == null)
                return Json(new { success = false, message = "未找到记录 / Record not found" });

            if (rec.Username.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "不能删除当前登录账号 / Cannot delete the currently logged-in account" });

            _context.SVN_Downtime_Accounts.Remove(rec);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ────────────────────────────────────────────────────────────────
        // RESET PASSWORD
        // ────────────────────────────────────────────────────────────────

        /// <summary>POST reset mật khẩu tài khoản</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return Json(new { success = false, message = "新密码不能为空 / New password is required" });

            var rec = await _context.SVN_Downtime_Accounts.FindAsync(dto.Id);
            if (rec == null)
                return Json(new { success = false, message = "未找到记录 / Record not found" });

            rec.Password = dto.NewPassword.Trim();
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // DÁN VÀO StatusController.cs
        // Vị trí: ngay bên dưới action AdminNotifications (hoặc bất kỳ chỗ nào
        //         trong class StatusController, trước dấu } cuối cùng)
        // ══════════════════════════════════════════════════════════════════════

        // ── GET /Status/TechGetRecords ──
        // Tech lấy danh sách records để xem và sửa 4 trường kỹ thuật
        [HttpGet]
        public async Task<IActionResult> TechGetRecords(
            string state    = "",
            string fromDate = "",
            string toDate   = "",
            int page        = 1,
            int pageSize    = 15)
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "Technical" && role != "Admin")
                return Unauthorized();

            var q = _context.SVN_Downtime_Infos_Devel
                        .AsQueryable();

            if (!string.IsNullOrWhiteSpace(state))
                q = q.Where(x => x.State != null && x.State.Trim().ToUpper() == state.ToUpper());

            if (DateTime.TryParse(fromDate, out var fd))
                q = q.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= fd.Date);

            if (DateTime.TryParse(toDate, out var td))
                q = q.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= td.Date);

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
                    x.MachineCode,
                    x.Location,
                    x.EmployeeCode,
                    x.EmployeeName,
                    x.Reason,
                    x.Effect,
                    x.Station,
                    x.Action,
                    x.RootCause,
                    x.SpareParts,
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


        // ── POST /Status/TechUpdateRecord ──
        // Tech chỉ được cập nhật 4 trường: Station, Action, RootCause, SpareParts
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> TechUpdateRecord([FromBody] TechUpdateDto dto)
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "Technical" && role != "Admin")
                return Unauthorized();

            var rec = await _context.SVN_Downtime_Infos_Devel.FindAsync(dto.Id);
            if (rec == null)
                return Json(new { success = false, message = "Không tìm thấy record" });

            // CHỈ cho phép sửa 4 trường kỹ thuật
            rec.Station    = dto.Station    ?? rec.Station;
            rec.Action     = dto.Action     ?? rec.Action;
            rec.RootCause  = dto.RootCause  ?? rec.RootCause;
            rec.SpareParts = dto.SpareParts ?? rec.SpareParts;

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ── DTO ──
        public class TechUpdateDto
        {
            public int Id { get; set; }
            public string? Station { get; set; }
            public string? Action { get; set; }
            public string? RootCause { get; set; }
            public string? SpareParts { get; set; }
        }



        public class ResetPasswordDto
        {
            public int Id { get; set; }
            public string? NewPassword { get; set; }
        }


// ────────────────────────────────────────────────────────────────
// DTOs mới — thêm vào ngay dưới AdminDeleteDto
// ────────────────────────────────────────────────────────────────

public class EmployeeDto
{
    public int     Id          { get; set; }
    public string? EmployeeID  { get; set; }
    public string? ChineseName { get; set; }
    public string? EnglishName { get; set; }
}

        public class AccountDto
        {
            public int Id { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
            public string? Role { get; set; }
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


        // ── MTTR theo máy ──────────────────────────────────────────────
        private async Task<List<MttrByMachine>> GetMttrByMachine(
            string fromDate, string toDate,
            string operation = "", string reason = "", string location = "",
            string machine = "", string effect = "", string station = "")
        {
            var q = _context.SVN_Downtime_Infos_Devel.AsQueryable();
            if (!string.IsNullOrWhiteSpace(operation)) { var _op = operation.Trim(); q = q.Where(x => x.Operation != null && x.Operation.Contains(_op)); }
            if (!string.IsNullOrWhiteSpace(machine))   { var _mc = machine.Trim();   q = q.Where(x => x.MachineCode != null && x.MachineCode == _mc); }
            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var fd)) q = q.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= fd.Date);
            if (!string.IsNullOrEmpty(toDate)   && DateTime.TryParse(toDate,   out var td)) q = q.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= td.Date);

            var records = await q
                .Where(x => x.MachineCode != null && x.Datetime.HasValue)
                .OrderBy(x => x.MachineCode).ThenBy(x => x.Datetime)
                .Select(x => new { x.MachineCode, x.Operation, x.State, x.Datetime })
                .ToListAsync();

            var result = new List<MttrByMachine>();
            foreach (var g in records.Where(x => !string.IsNullOrEmpty(x.MachineCode)).GroupBy(x => new { x.MachineCode, x.Operation }))
            {
                var recs = g.OrderBy(x => x.Datetime).ToList();
                var repairs = new List<double>();
                for (int i = 0; i < recs.Count; i++)
                {
                    if (recs[i].State?.ToUpper() != "STOP") continue;
                    for (int j = i + 1; j < recs.Count; j++)
                    {
                        if (recs[j].State?.ToUpper() == "RUN" && recs[j].Datetime.HasValue && recs[i].Datetime.HasValue)
                            { repairs.Add((recs[j].Datetime.Value - recs[i].Datetime.Value).TotalMinutes); break; }
                        if (recs[j].State?.ToUpper() == "STOP") break;
                    }
                }
                if (!repairs.Any()) continue;
                var avg = repairs.Average();
                result.Add(new MttrByMachine { MachineCode = g.Key.MachineCode ?? "", Operation = g.Key.Operation ?? "", AvgRepairMinutes = Math.Round(avg, 1), AvgRepairFormatted = FormatMinutesToTime(avg), RepairCount = repairs.Count });
            }
            return result.OrderByDescending(x => x.AvgRepairMinutes).ToList();
        }

        // ── Top 5 máy hỏng nhiều nhất ─────────────────────────────────
        private async Task<List<Top5MachineData>> GetTop5Machines(
            string fromDate, string toDate,
            string operation = "", string location = "", string machine = "")
        {
            var q = _context.SVN_Downtime_Infos_Devel
                .Where(x => x.MachineCode != null && x.State != null && x.State.ToUpper() == "STOP").AsQueryable();
            if (!string.IsNullOrWhiteSpace(operation)) { var _op = operation.Trim(); q = q.Where(x => x.Operation != null && x.Operation.Contains(_op)); }
            if (!string.IsNullOrWhiteSpace(machine))   { var _mc = machine.Trim();   q = q.Where(x => x.MachineCode == _mc); }

            bool hasFrom = DateTime.TryParse(fromDate, out var fd);
            bool hasTo   = DateTime.TryParse(toDate,   out var td);

            DateTime today      = DateTime.Now.Date;
            // Thứ 2 của tuần hiện tại
            DateTime weekMon    = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            // Chủ nhật của tuần hiện tại
            DateTime weekSun    = weekMon.AddDays(6);

            DateTime rangeStart = hasFrom ? fd.Date : weekMon;
            DateTime rangeEnd   = hasTo   ? td.Date : weekSun;

            // Giới hạn tối đa 14 ngày để sparkline không quá dày
            int totalDays = Math.Min((int)(rangeEnd - rangeStart).TotalDays + 1, 14);
            // Lấy `totalDays` ngày cuối trong range
            var trendStart = rangeEnd.AddDays(-(totalDays - 1));
            var trendDays  = Enumerable.Range(0, totalDays).Select(i => trendStart.AddDays(i)).ToList();

            // Query với date filter
            q = q.Where(x => x.Datetime.HasValue
                           && x.Datetime.Value.Date >= rangeStart
                           && x.Datetime.Value.Date <= rangeEnd);

            var stops = await q.Select(x => new { x.MachineCode, x.Operation, x.Datetime }).ToListAsync();

            return stops.GroupBy(x => x.MachineCode)
                .Select(g => new { MC = g.Key ?? "", Op = g.First().Operation ?? "", Count = g.Count(), Items = g.ToList() })
                .OrderByDescending(x => x.Count).Take(5)
                .Select(m => new Top5MachineData
                {
                    MachineCode    = m.MC,
                    Operation      = m.Op,
                    DowntimeCount  = m.Count,
                    TotalMinutes   = 0,
                    TotalFormatted = "-",
                    DailyTrend     = trendDays.Select(d => (double)m.Items.Count(r => r.Datetime.HasValue && r.Datetime.Value.Date == d)).ToList(),
                    TrendDates     = trendDays.Select(d => d.ToString("dd/MM")).ToList()
                }).ToList();
        }

        // ── Response Time ──────────────────────────────────────────────
        private async Task<ResponseTimeData> GetResponseTimeData(
            string fromDate, string toDate, string operation = "", string machine = "")
        {
            var q = _context.SVN_Downtime_TechResponses
                .Where(x => x.RespondDatetime.HasValue && x.StopDatetime.HasValue && x.TechAction == "ACCEPT");
            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var fd)) q = q.Where(x => x.StopDatetime.Value.Date >= fd.Date);
            if (!string.IsNullOrEmpty(toDate)   && DateTime.TryParse(toDate,   out var td)) q = q.Where(x => x.StopDatetime.Value.Date <= td.Date);
            if (!string.IsNullOrWhiteSpace(operation)) { var _op = operation.Trim(); q = q.Where(x => x.Operation != null && x.Operation.Contains(_op)); }
            if (!string.IsNullOrWhiteSpace(machine))   { var _mc = machine.Trim();   q = q.Where(x => x.MachineCode == _mc); }

            var records = await q.Select(x => new {
                x.TechUsername,
                x.StopDatetime,
                x.RespondDatetime
            }).ToListAsync();

            if (!records.Any()) return new ResponseTimeData();

            var result = new ResponseTimeData();

            // Group by Technician
            foreach (var g in records
                .Where(x => !string.IsNullOrWhiteSpace(x.TechUsername))
                .GroupBy(x => x.TechUsername)
                .OrderBy(g => g.Key))
            {
                var times = g.Select(r => (r.RespondDatetime!.Value - r.StopDatetime!.Value).TotalMinutes)
                             .Where(t => t >= 0 && t < 1440).ToList();
                if (!times.Any()) continue;
                result.Labels.Add(g.Key ?? "");
                result.AvgResponseMins.Add(Math.Round(times.Average(), 1));
                result.MinResponseMins.Add(Math.Round(times.Min(), 1));
                result.MaxResponseMins.Add(Math.Round(times.Max(), 1));
            }

            var all = records.Select(r => (r.RespondDatetime!.Value - r.StopDatetime!.Value).TotalMinutes)
                             .Where(t => t >= 0 && t < 1440).ToList();
            result.OverallAvgMins = all.Any() ? Math.Round(all.Average(), 1) : 0;
            result.TotalResponded = records.Count;
            return result;
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