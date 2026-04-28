using System.Drawing;
using ClosedXML.Excel;
using MachineStatusUpdate.Models;
using MachineStatusUpdate.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZXing;


namespace MachineStatusUpdate.Controllers
{
    public class StatusController : Controller
    {

        private readonly ApplicationDbContext _context;

        private readonly IWebHostEnvironment _webHostEnvironment;

        private readonly IStatusUpdateService _statusUpdateService;


        public StatusController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, IStatusUpdateService statusUpdateService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _statusUpdateService = statusUpdateService;

        }

        
        [HttpGet]
        public async Task<IActionResult> GetLatestDowntimeForOperation(string operation)
        {
            if (string.IsNullOrWhiteSpace(operation))
                return Json(new { exists = false });

            var op = operation.Trim();
            var today = DateTime.Now.Date;

            var latest = await _context.SVN_Downtime_Infos
                .Where(x => x.Operation != null
                            && x.Operation.Trim() == op
                            && x.Datetime.HasValue
                            && x.Datetime.Value.Date == today)
                .OrderByDescending(x => x.Datetime)
                .Select(x => new
                {
                    state = (x.State ?? "").Trim(),
                    ISS_Code = (x.ISS_Code ?? "").Trim()
                })
                .FirstOrDefaultAsync();

            if (latest == null)
                return Json(new { exists = false });

            return Json(new { exists = true, state = latest.state, ISSCode = latest.ISS_Code });
        }


        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }


        /*==============================================Machine Status Update=====================================================*/

        // Hàm check mã máy có khớp không
        [HttpPost]
        public async Task<IActionResult> ValidateCode([FromBody] ValidateCodeRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Code))
                {
                    return Json(new { exists = false });
                }

                var exists = await _context.sVN_Equipment_Machine_Info
                    .AnyAsync(x => x.SVNCode == request.Code);

                return Json(new { exists = exists });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error validating code: {ex.Message}");
                return Json(new { exists = false });
            }
        }

        // Hàm xác định Operation dựa trên Code
        private async Task<string> GetOperationFromCodeAsync(string code)
        {
            if (string.IsNullOrEmpty(code))
                return "";

            try
            {
                var machineInfo = await _context.sVN_Equipment_Machine_Info
                    .FirstOrDefaultAsync(x => x.SVNCode == code);

                return machineInfo?.Project ?? "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting operation from code: {ex.Message}");
                return "";
            }
        }

        // Hàm decode mã QR từ upload
        [HttpPost]
        public async Task<IActionResult> DecodeQR(IFormFile qrImage)
        {
            if (qrImage == null || qrImage.Length == 0)
                return Json(new { success = false, message = "Chưa chọn ảnh!" });

            try
            {
                using var stream = qrImage.OpenReadStream();
                using var skBitmap = SkiaSharp.SKBitmap.Decode(stream);

                var reader = new ZXing.SkiaSharp.BarcodeReader();
                var result = reader.Decode(skBitmap);

                if (result != null)
                {
                    return Json(new { success = true, code = result.Text });
                }
                else
                {
                    return Json(new { success = false, message = "Không đọc được mã từ ảnh!" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi xử lý: " + ex.Message });
            }
        }

        // Method để xử lý lại toàn bộ dữ liệu (nếu cần)
        public async Task<IActionResult> ProcessAllHistoryToDetail()
        {
            try
            {
                // Xóa toàn bộ dữ liệu cũ trong bảng Detail
                var existingDetails = _context.SVN_Equipment_Status_Update_Detail.ToList();
                _context.SVN_Equipment_Status_Update_Detail.RemoveRange(existingDetails);
                await _context.SaveChangesAsync();

                // Lấy tất cả records từ History, group by Code và xử lý
                var allHistoryRecords = await _context.SVN_Equipment_Info_History_Test
                    .OrderBy(x => x.Code)
                    .ThenBy(x => x.Datetime)
                    .ToListAsync();

                var groupedByCode = allHistoryRecords.GroupBy(x => x.Code).ToList();

                foreach (var codeGroup in groupedByCode)
                {
                    var records = codeGroup.OrderBy(x => x.Datetime).ToList();

                    for (int i = 0; i < records.Count; i++)
                    {
                        var currentRecord = records[i];
                        if (!currentRecord.Datetime.HasValue) continue;

                        // Tính EstimateTime (số phút từ EstimateTime - DateTime)
                        double? estimateTimeMinutes = null;
                        if (!string.IsNullOrEmpty(currentRecord.EstimateTime))
                        {
                            if (TimeSpan.TryParse(currentRecord.EstimateTime, out TimeSpan estimateTimeSpan))
                            {
                                var estimateDateTime = currentRecord.Datetime.Value.Date.Add(estimateTimeSpan);
                                var timeDifference = estimateDateTime - currentRecord.Datetime.Value;
                                estimateTimeMinutes = timeDifference.TotalMinutes;
                            }
                        }

                        // Xử lý ToTime và DurationMinutes
                        string toTime = "";
                        float durationMinutes = 0;

                        if (i < records.Count - 1)
                        {
                            var nextRecord = records[i + 1];
                            if (nextRecord.Datetime.HasValue)
                            {
                                durationMinutes = (float)(nextRecord.Datetime.Value - currentRecord.Datetime.Value).TotalMinutes;
                                toTime = Math.Round(durationMinutes, 2).ToString(); // ToTime là số phút
                            }
                        }

                        // Tạo record mới cho bảng Detail
                        var detailRecord = new SVN_Equipment_Status_Update_Detail
                        {
                            Name = currentRecord.Name ?? "",
                            Operation = currentRecord.Operation ?? "",
                            State = currentRecord.State ?? "",
                            EstimateTime = estimateTimeMinutes?.ToString("F2") ?? "",
                            FromTime = currentRecord.Datetime.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                            ToTime = toTime, // Lưu số phút, để rỗng nếu chưa có
                            DurationMinutes = durationMinutes
                        };

                        _context.SVN_Equipment_Status_Update_Detail.Add(detailRecord);
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã xử lý thành công toàn bộ dữ liệu History vào Detail!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ProcessAllHistoryToDetail: {ex.Message}");
                return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(SVN_Equipment_Info_History_Test model, IFormFile imageFile)
        {
            try
            {
                if (string.IsNullOrEmpty(model.Code) || string.IsNullOrEmpty(model.State))
                {
                    return Json(new { success = false, message = "Vui lòng điền đầy đủ thông tin bắt buộc!" });
                }

                var machineExists = await _context.sVN_Equipment_Machine_Info
                    .AnyAsync(x => x.SVNCode == model.Code);

                if (!machineExists)
                {
                    return Json(new { success = false, message = "Không tồn tại mã máy này trong hệ thống!" });
                }

                string imagePath = null;
                if (imageFile != null && imageFile.Length > 0)
                {
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
                    var fileExtension = Path.GetExtension(imageFile.FileName).ToLower();

                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        return Json(new { success = false, message = "Chỉ cho phép upload ảnh với định dạng: jpg, jpeg, png, gif, bmp" });
                    }
                    if (imageFile.Length > 5 * 1024 * 1024)
                    {
                        return Json(new { success = false, message = "Kích thước ảnh không được vượt quá 5MB" });
                    }

                    var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "status-images");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }
                    var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{fileExtension}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(stream);
                    }
                    imagePath = $"/uploads/status-images/{fileName}";
                }

                string generateName = model.Code;
                if (!string.IsNullOrEmpty(model.Code) && model.Code.Contains("-"))
                {
                    var parts = model.Code.Split('-');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int number))
                    {
                        generateName = $"#{number}";
                    }
                }

                model.Name = generateName;
                model.Operation = await GetOperationFromCodeAsync(model.Code);
                model.Datetime = DateTime.Now;

                int insertedId = 0;
                using (var command = _context.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "EXEC [dbo].[SVN_InsertMachineStatus_Test] @Code, @Name, @State, @Operation, @EstimateTime, @Description, @Image, @Datetime";
                    command.CommandType = System.Data.CommandType.Text;

                    command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@Code", model.Code ?? ""));
                    command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@Name", model.Name ?? ""));
                    command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@State", model.State ?? ""));
                    command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@Operation", model.Operation ?? ""));
                    command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@EstimateTime", model.EstimateTime ?? ""));
                    command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@Description", model.Description ?? ""));
                    command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@Image", imagePath ?? ""));
                    command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@Datetime", model.Datetime));

                    if (command.Connection.State != System.Data.ConnectionState.Open)
                        await command.Connection.OpenAsync();

                    var result = await command.ExecuteScalarAsync();
                    insertedId = Convert.ToInt32(result);
                }


                var insertedRecord = await _context.SVN_Equipment_Info_History_Test
                    .FirstOrDefaultAsync(x => x.Id == insertedId);

                await _statusUpdateService.ProcessSingleRecordToUpdateDetail(insertedRecord);

                return Json(new { success = true, message = "Lưu trạng thái thành công!", data = insertedRecord });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Create: {ex.Message}");
                return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
            }
        }

        // Method để xử lý dữ liệu từ Detail sang Status Update
        [HttpPost]
        public async Task<IActionResult> ProcessToStatusUpdate(DateTime? filterDate = null)
        {
            try
            {
                await _statusUpdateService.ProcessDataToStatusUpdate(filterDate);
                return Json(new { success = true, message = "Đã xử lý thành công dữ liệu vào bảng Status Update!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ProcessToStatusUpdate: {ex.Message}");
                return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        // Method hiển thị Status Update Report
        public async Task<IActionResult> StatusUpdateReport(DateTime? filterDate = null, string operation = "", int page = 1, int pageSize = 25)
        {
            try
            {
                var query = _context.SVN_Equipment_Status_Update.AsQueryable();

                // Apply date filter
                if (filterDate.HasValue)
                {
                    query = query.Where(x => x.Datetime.Date == filterDate.Value.Date);
                }

                if (!string.IsNullOrEmpty(operation))
                {
                    query = query.Where(x => x.Operation.Contains(operation));
                }

                var totalRecords = await query.CountAsync();

                var results = await query
                    .OrderByDescending(x => x.Datetime)
                    .ThenBy(x => x.Name)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .AsNoTracking()
                    .ToListAsync();

                var totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

                // Pagination ViewBag
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalRecords = totalRecords;
                ViewBag.HasPreviousPage = page > 1;
                ViewBag.HasNextPage = page < totalPages;

                // Filter ViewBag
                ViewBag.FilterDate = filterDate?.ToString("yyyy-MM-dd") ?? "";
                ViewBag.Operation = operation ?? "";

                return View(results);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Lỗi: {ex.Message}";
                ViewBag.FilterDate = filterDate?.ToString("yyyy-MM-dd") ?? "";
                ViewBag.Operation = operation ?? "";

                // Set default pagination values for error case
                ViewBag.CurrentPage = 1;
                ViewBag.TotalPages = 0;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalRecords = 0;
                ViewBag.HasPreviousPage = false;
                ViewBag.HasNextPage = false;

                return View(new List<SVN_Equipment_Status_Update>());
            }
        }

        // Method xuất Excel cho Status Update
        public async Task<IActionResult> ExportStatusUpdateToExcel(DateTime? filterDate = null, string operation = "")
        {
            try
            {
                var query = _context.SVN_Equipment_Status_Update.AsQueryable();

                if (filterDate.HasValue)
                {
                    query = query.Where(x => x.Datetime.Date == filterDate.Value.Date);
                }

                if (!string.IsNullOrEmpty(operation))
                {
                    query = query.Where(x => x.Operation.Contains(operation));
                }

                var data = await query
                    .OrderByDescending(x => x.Datetime)
                    .ThenBy(x => x.Name)
                    .ToListAsync();

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("StatusUpdateReport");
                    var currentRow = 1;

                    // Font mặc định
                    ws.Style.Font.FontName = "Times New Roman";
                    ws.Style.Font.FontSize = 11;

                    // Header
                    string[] headers = { "Id", "Name", "Operation", "Start Time", "Duration (min)", "Total Downtime (min)", "Date" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = ws.Cell(currentRow, i + 1);
                        cell.Value = headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5);
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    }

                    // Data rows
                    foreach (var item in data)
                    {
                        currentRow++;
                        ws.Cell(currentRow, 1).Value = item.Id;
                        ws.Cell(currentRow, 2).Value = item.Name;
                        ws.Cell(currentRow, 3).Value = item.Operation;
                        ws.Cell(currentRow, 4).Value = item.StartTime;
                        ws.Cell(currentRow, 5).Value = Math.Round(item.Duration, 2);
                        ws.Cell(currentRow, 6).Value = Math.Round(item.TotalDuration, 2);
                        ws.Cell(currentRow, 7).Value = item.Datetime.ToString("yyyyMMdd");
                    }

                    // Styling
                    ws.Columns(1, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Columns(1, 7).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                    // Column widths
                    ws.Column(1).Width = 8;   // Id
                    ws.Column(2).Width = 15;  // Name
                    ws.Column(3).Width = 20;  // Operation
                    ws.Column(4).Width = 20;  // Start Time
                    ws.Column(5).Width = 15;  // Duration
                    ws.Column(6).Width = 18;  // Total Downtime
                    ws.Column(7).Width = 12;  // Date

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        return File(stream.ToArray(),
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"StatusUpdateReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi ExportStatusUpdateToExcel: {ex.Message}");
                return Json(new { success = false, message = $"Lỗi xuất Excel: {ex.Message}" });
            }
        }

        // API endpoint để xử lý dữ liệu cho ngày cụ thể
        [HttpPost]
        public async Task<IActionResult> ProcessDataForDate([FromBody] ProcessDateRequest request)
        {
            try
            {
                DateTime? filterDate = null;
                if (!string.IsNullOrEmpty(request.Date))
                {
                    if (DateTime.TryParse(request.Date, out DateTime parsedDate))
                    {
                        filterDate = parsedDate;
                    }
                }

                await _statusUpdateService.ProcessDataToStatusUpdate(filterDate);
                return Json(new { success = true, message = "Đã xử lý thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        // Method hiển thị kết quả nhập trạng thái
        public async Task<IActionResult> Result(string code = "", string state = "", string operation = "", string fromInsDateTime = "", string toInsDateTime = "", int page = 1, int pageSize = 25)
        {
            try
            {
                var query = _context.SVN_Equipment_Info_History_Test.AsQueryable();

                // Apply filter

                if (!string.IsNullOrEmpty(code))
                    query = query.Where(x => x.Code.Contains(code));

                if (!string.IsNullOrEmpty(state))
                    query = query.Where(x => x.State.Contains(state));

                if (!string.IsNullOrEmpty(operation))
                    query = query.Where(x => x.Operation.Contains(operation));

                if (!string.IsNullOrEmpty(fromInsDateTime) && DateTime.TryParse(fromInsDateTime, out var fromDate))
                {
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= fromDate.Date);
                }

                if (!string.IsNullOrEmpty(toInsDateTime) && DateTime.TryParse(toInsDateTime, out var toDate))
                {
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= toDate.Date);
                }

                var totalRecords = await query.CountAsync();

                var results = await query
                    .OrderByDescending(x => x.Datetime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .AsNoTracking()
                    .ToListAsync();

                var totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalRecords = totalRecords;
                ViewBag.HasPreviousPage = page > 1;
                ViewBag.HasNextPage = page < totalPages;

                // Truyền giá trị filter ra View

                ViewBag.Code = code ?? "";
                ViewBag.State = state ?? "";
                ViewBag.Operation = operation ?? "";
                ViewBag.fromInsDateTime = fromInsDateTime ?? "";
                ViewBag.toInsDateTime = toInsDateTime ?? "";

                return View(results);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Lỗi: {ex.Message}";
                ViewBag.Code = code ?? "";
                ViewBag.State = state ?? "";
                ViewBag.Operation = operation ?? "";
                ViewBag.fromInsDateTime = fromInsDateTime ?? "";
                ViewBag.toInsDateTime = toInsDateTime ?? "";

                // Set default pagination values for error case
                ViewBag.CurrentPage = 1;
                ViewBag.TotalPages = 0;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalRecords = 0;
                ViewBag.HasPreviousPage = false;
                ViewBag.HasNextPage = false;

                return View(new List<SVN_Equipment_Info_History_Test>());
            }
        }

        // Xuất File Excel kết quả
        public async Task<IActionResult> ExportToExcel(string code = "", string state = "", string operation = "", string fromInsDateTime = "", string toInsDateTime = "")
        {
            var query = _context.SVN_Equipment_Info_History_Test.AsQueryable();

            if (!string.IsNullOrEmpty(code))
                query = query.Where(x => x.Code.Contains(code));

            if (!string.IsNullOrEmpty(state))
                query = query.Where(x => x.State.Contains(state));

            if (!string.IsNullOrEmpty(operation))
                query = query.Where(x => x.Operation.Contains(operation));

            if (!string.IsNullOrEmpty(fromInsDateTime) && DateTime.TryParse(fromInsDateTime, out var fromDate))
            {
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= fromDate.Date);
            }

            if (!string.IsNullOrEmpty(toInsDateTime) && DateTime.TryParse(toInsDateTime, out var toDate))
            {
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= toDate.Date);
            }

            // Sắp xếp bản ghi theo thời gian ASC
            var data = await query.OrderBy(x => x.Datetime).ToListAsync();

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("StatusHistory");
                var currentRow = 1;

                // Font mặc định
                ws.Style.Font.FontName = "Times New Roman";
                ws.Style.Font.FontSize = 11;

                // Header
                string[] headers = { "Id", "Code", "Name", "State", "Operation", "Description", "Image", "Datetime" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(currentRow, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5);
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }

                // Thiết lập chiều cao hàng cho data (để ảnh hiển thị đẹp)
                const double rowHeight = 70;

                foreach (var item in data)
                {
                    currentRow++;
                    ws.Row(currentRow).Height = rowHeight;
                    ws.Cell(currentRow, 1).Value = item.Id;
                    ws.Cell(currentRow, 2).Value = item.Code;
                    ws.Cell(currentRow, 3).Value = item.Name;
                    ws.Cell(currentRow, 4).Value = item.State;
                    ws.Cell(currentRow, 5).Value = item.Operation;
                    ws.Cell(currentRow, 6).Value = item.Description;

                    if (!string.IsNullOrEmpty(item.Image))
                    {
                        try
                        {
                            string imagePath = "";
                            if (item.Image.StartsWith("/uploads/"))
                            {
                                imagePath = Path.Combine(_webHostEnvironment.WebRootPath, item.Image.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                            }
                            else
                            {
                                imagePath = item.Image;
                            }

                            if (System.IO.File.Exists(imagePath))
                            {

                                var picture = ws.AddPicture(imagePath);
                                picture.MoveTo(ws.Cell(currentRow, 7), 8, 5);
                                picture.WithSize(100, 70);


                                var imageCell = ws.Cell(currentRow, 7);
                                imageCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                imageCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            }
                            else
                            {

                                ws.Cell(currentRow, 7).Value = "No image";
                                ws.Cell(currentRow, 7).Style.Font.FontColor = XLColor.Gray;
                            }
                        }
                        catch (Exception ex)
                        {

                            ws.Cell(currentRow, 7).Value = $"Error: {ex.Message}";
                            ws.Cell(currentRow, 7).Style.Font.FontColor = XLColor.Red;
                        }
                    }
                    else
                    {
                        ws.Cell(currentRow, 7).Value = "No image";
                        ws.Cell(currentRow, 7).Style.Font.FontColor = XLColor.Gray;
                    }
                    ws.Cell(currentRow, 8).Value = item.Datetime?.ToString("yyyy-MM-dd HH:mm:ss");
                }

                // Canh giữa các cột số và ngày
                ws.Columns(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Columns(2, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Columns(3, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Columns(4, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Columns(5, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Columns(7, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Column(1).Width = 8;
                ws.Column(2).Width = 15;
                ws.Column(3).Width = 15;
                ws.Column(4).Width = 15;
                ws.Column(5).Width = 15;
                ws.Column(6).Width = 15;
                ws.Column(7).Width = 15;
                ws.Column(8).Width = 18;

                using (var stream = new MemoryStream())
                {

                    workbook.SaveAs(stream);
                    return File(stream.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "StatusHistory.xlsx");
                }

            }

        }


        public async Task<IActionResult> DowntimeDetailReport(string code = "",string state = "",string operation = "",string fromInsDateTime = "",string toInsDateTime = "",int page = 1,int pageSize = 25)
        {
            try
            {
                IQueryable<SVN_Equipment_Status_Update_Detail> query = _context.SVN_Equipment_Status_Update_Detail;

                // Lọc theo Code
                if (!string.IsNullOrEmpty(code))
                {
                    query = query.Where(x => x.Name.Contains(code));
                }

                // Lọc theo State
                if (!string.IsNullOrEmpty(state))
                {
                    query = query.Where(x => x.State.Contains(state));
                }

                // Lọc theo Operation
                if (!string.IsNullOrEmpty(operation))
                {
                    query = query.Where(x => x.Operation.Contains(operation));
                }

                // Lọc theo khoảng thời gian
                if (!string.IsNullOrEmpty(fromInsDateTime) && DateTime.TryParse(fromInsDateTime, out DateTime fromDate))
                {
                    query = query.Where(x => x.FromTime.CompareTo(fromDate.ToString("yyyy-MM-dd HH:mm:ss")) >= 0);
                }

                if (!string.IsNullOrEmpty(toInsDateTime) && DateTime.TryParse(toInsDateTime, out DateTime toDate))
                {
                    query = query.Where(x => x.FromTime.CompareTo(toDate.ToString("yyyy-MM-dd HH:mm:ss")) <= 0);
                }

                // Lấy tổng số bản ghi
                var totalRecords = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

                // Áp dụng phân trang
                var pagedResults = await query
                    .OrderByDescending(x => x.FromTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // Gán dữ liệu vào ViewBag để truyền sang View
                ViewBag.Code = code;
                ViewBag.State = state;
                ViewBag.Operation = operation;
                ViewBag.fromInsDateTime = fromInsDateTime;
                ViewBag.toInsDateTime = toInsDateTime;
                ViewBag.CurrentPage = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalRecords = totalRecords;
                ViewBag.HasPreviousPage = page > 1;
                ViewBag.HasNextPage = page < totalPages;

                return View(pagedResults);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Lỗi DowntimeDetailReport: {ex.Message}";
                return View(new List<SVN_Equipment_Status_Update_Detail>());
            }
        }

        // Xuất Excel cho Downtime Detail
        public async Task<IActionResult> ExportDowntimeDetailToExcel(string code = "",string state = "",string operation = "",string fromInsDateTime = "",string toInsDateTime = "")
        {
            try
            {
                IQueryable<SVN_Equipment_Status_Update_Detail> query = _context.SVN_Equipment_Status_Update_Detail;

                // Lọc theo Code
                if (!string.IsNullOrEmpty(code))
                    query = query.Where(x => x.Name.Contains(code));

                // Lọc theo State
                if (!string.IsNullOrEmpty(state))
                    query = query.Where(x => x.State.Contains(state));

                // Lọc theo Operation
                if (!string.IsNullOrEmpty(operation))
                    query = query.Where(x => x.Operation.Contains(operation));

                // Lọc theo khoảng thời gian
                if (!string.IsNullOrEmpty(fromInsDateTime) && DateTime.TryParse(fromInsDateTime, out DateTime fromDate))
                {
                    query = query.Where(x => x.FromTime.CompareTo(fromDate.ToString("yyyy-MM-dd HH:mm:ss")) >= 0);
                }

                if (!string.IsNullOrEmpty(toInsDateTime) && DateTime.TryParse(toInsDateTime, out DateTime toDate))
                {
                    query = query.Where(x => x.FromTime.CompareTo(toDate.ToString("yyyy-MM-dd HH:mm:ss")) <= 0);
                }

                var data = await query.OrderByDescending(x => x.FromTime).ToListAsync();

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("DowntimeDetail");
                    var currentRow = 1;

                    // Font mặc định
                    ws.Style.Font.FontName = "Times New Roman";
                    ws.Style.Font.FontSize = 11;

                    // Header
                    string[] headers = { "Id", "Name", "Operation", "State", "Estimate Time (min)", "From Time", "To Time (min)", "Duration (min)" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = ws.Cell(currentRow, i + 1);
                        cell.Value = headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5);
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    }

                    // Data rows
                    foreach (var item in data)
                    {
                        currentRow++;
                        ws.Cell(currentRow, 1).Value = item.Id;
                        ws.Cell(currentRow, 2).Value = item.Name;
                        ws.Cell(currentRow, 3).Value = item.Operation;
                        ws.Cell(currentRow, 4).Value = item.State;

                        // Estimate Time
                        if (!string.IsNullOrEmpty(item.EstimateTime) && double.TryParse(item.EstimateTime, out double estimateMinutes))
                        {
                            ws.Cell(currentRow, 5).Value = Math.Round(estimateMinutes, 1);
                            ws.Cell(currentRow, 5).Style.NumberFormat.Format = "0.0";
                        }
                        else
                        {
                            ws.Cell(currentRow, 5).Value = ""; // để trống
                        }

                        // FromTime
                        if (!string.IsNullOrEmpty(item.FromTime))
                        {
                            ws.Cell(currentRow, 6).Value = item.FromTime;
                        }
                        else
                        {
                            ws.Cell(currentRow, 6).Value = "";
                        }

                        // ToTime
                        if (!string.IsNullOrEmpty(item.ToTime) && double.TryParse(item.ToTime, out double toTimeMinutes))
                        {
                            ws.Cell(currentRow, 7).Value = Math.Round(toTimeMinutes, 1);
                            ws.Cell(currentRow, 7).Style.NumberFormat.Format = "0.0";
                        }
                        else
                        {
                            ws.Cell(currentRow, 7).Value = "";
                        }

                        // Duration
                        if (item.DurationMinutes > 0)
                        {
                            ws.Cell(currentRow, 8).Value = Math.Round(item.DurationMinutes, 1);
                            ws.Cell(currentRow, 8).Style.NumberFormat.Format = "0.0";
                        }
                        else
                        {
                            ws.Cell(currentRow, 8).Value = "";
                        }
                    }
                    // Styling
                    ws.Columns().AdjustToContents();

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        return File(stream.ToArray(),
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"DowntimeDetail_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi ExportDowntimeDetailToExcel: {ex.Message}");
                return Json(new { success = false, message = $"Lỗi xuất Excel: {ex.Message}" });
            }
        }




        /*============================================================ Downtime ========================================================================**/


        /* Hàm GET Nhập Downtime */
        [HttpGet]
        public async Task<IActionResult> CreateDownTime()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            var ops = await _context.SVN_targets
                .AsNoTracking()
                .Where(x => x.Date_time == today 
                && x.Operation != null 
                && x.Operation != ""
                && !x.Operation.StartsWith("Sakura")
                
                )
                .Select(x => x.Operation)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            ViewBag.OperationOptions = ops;

            var rea = await _context.SVN_Downtime_Reasons
                .AsNoTracking()
                .OrderBy(r => r.Reason_Name)
                .Select(r => new { r.Reason_Code, r.Reason_Name })
                .ToListAsync();

            ViewBag.ReasonOptions = rea;
            return View("CreateDownTime"); // <- chỉ rõ, khớp file .cshtml
        }

        /* Hàm GET Lịch sử Downtime */
        [HttpGet]
        public async Task<IActionResult> DowntimeList(string operation = "", string fromDate = "", string toDate = "",int page = 1, int pageSize = 25)
        {
            try
            {
                // JOIN với bảng Reasons để lấy ErrorName
                var query = from d in _context.SVN_Downtime_Infos
                            join r in _context.SVN_Downtime_Reasons
                            on d.ISS_Code equals r.Reason_Code into reasons
                            from r in reasons.DefaultIfEmpty()
                            select new SVN_Downtime_Info
                            {
                                Id = d.Id,
                                Code = d.Code,
                                SVNCode = d.SVNCode,
                                Name = d.Name,
                                Operation = d.Operation,
                                State = d.State,
                                ISS_Code = d.ISS_Code,
                                ErrorName = r != null ? r.Reason_Name : "", // Lấy Reason_Name
                                Description = d.Description,
                                Datetime = d.Datetime,
                                EstimateTime = d.EstimateTime,
                                Image = d.Image
                            };

                // ----- Filters -----
                if (!string.IsNullOrWhiteSpace(operation))
                {
                    var op = operation.Trim();
                    query = query.Where(x => x.Operation != null && x.Operation.Contains(op));
                }

                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                {
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);
                }

                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                {
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);
                }

                // ----- Pagination -----
                var totalRecords = await query.CountAsync();
                var results = await query
                    .OrderByDescending(x => x.Datetime)
                    .ThenBy(x => x.Operation)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

                // Distinct operations cho dropdown filter
                ViewBag.OperationOptions = await _context.SVN_Downtime_Infos
                    .Where(x => x.Operation != null && x.Operation != "")
                    .Select(x => x.Operation!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();

                // Pass filter & pagination to View
                ViewBag.Operation = operation ?? "";
                ViewBag.FromDate = fromDate ?? "";
                ViewBag.ToDate = toDate ?? "";
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalRecords = totalRecords;
                ViewBag.HasPreviousPage = page > 1;
                ViewBag.HasNextPage = page < totalPages;

                return View(results);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Lỗi: {ex.Message}";
                // Defaults khi lỗi
                ViewBag.Operation = operation ?? "";
                ViewBag.FromDate = fromDate ?? "";
                ViewBag.ToDate = toDate ?? "";
                ViewBag.CurrentPage = 1;
                ViewBag.TotalPages = 0;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalRecords = 0;
                ViewBag.HasPreviousPage = false;
                ViewBag.HasNextPage = false;

                return View(new List<SVN_Downtime_Info>());
            }
        }

        // Xuất Excel DowntimeList với ErrorName
        public async Task<IActionResult> ExportDowntimeListToExcel(string operation = "", string fromDate = "", string toDate = "")
        {
            try
            {
                // JOIN với bảng Reasons để lấy ErrorName
                var query = from d in _context.SVN_Downtime_Infos
                            join r in _context.SVN_Downtime_Reasons
                            on d.ISS_Code equals r.Reason_Code into reasons
                            from r in reasons.DefaultIfEmpty()
                            select new SVN_Downtime_Info
                            {
                                Id = d.Id,
                                SVNCode = d.SVNCode,
                                Code = d.Code,
                                Name = d.Name,
                                Operation = d.Operation,
                                State = d.State,
                                ISS_Code = d.ISS_Code,
                                ErrorName = r != null ? r.Reason_Name : "",
                                Description = d.Description,
                                Datetime = d.Datetime,
                                EstimateTime = d.EstimateTime,
                                Image = d.Image
                            };

                // Apply filters
                if (!string.IsNullOrWhiteSpace(operation))
                {
                    var op = operation.Trim();
                    query = query.Where(x => x.Operation != null && x.Operation.Contains(op));
                }

                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                {
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);
                }

                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                {
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);
                }

                var data = await query
                    .OrderByDescending(x => x.Datetime)
                    .ThenBy(x => x.Operation)
                    .ToListAsync();

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("DowntimeList");
                    var currentRow = 1;

                    // Font mặc định
                    ws.Style.Font.FontName = "Times New Roman";
                    ws.Style.Font.FontSize = 11;

                    // Header
                    string[] headers = { "#", "SVN Code", "Operation", "ISS Code", "Tên lỗi", "State", "Mô tả", "Thời gian", "Ước tính", "Ảnh" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = ws.Cell(currentRow, i + 1);
                        cell.Value = headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5);
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    }

                    const double rowHeight = 70;

                    int rowIndex = 0;
                    foreach (var item in data)
                    {
                        currentRow++;
                        rowIndex++;
                        ws.Row(currentRow).Height = rowHeight;

                        ws.Cell(currentRow, 1).Value = rowIndex;
                        ws.Cell(currentRow, 2).Value = item.SVNCode;
                        ws.Cell(currentRow, 3).Value = item.Operation;
                        ws.Cell(currentRow, 4).Value = item.ISS_Code;
                        ws.Cell(currentRow, 5).Value = item.ErrorName;
                        ws.Cell(currentRow, 6).Value = item.State;
                        ws.Cell(currentRow, 7).Value = item.Description;
                        ws.Cell(currentRow, 8).Value = item.Datetime?.ToString("dd/MM/yyyy HH:mm") ?? "-";
                        ws.Cell(currentRow, 9).Value = string.IsNullOrEmpty(item.EstimateTime) ? "-" : item.EstimateTime;

                        // Xử lý ảnh
                        if (!string.IsNullOrEmpty(item.Image))
                        {
                            try
                            {
                                string imagePath = "";
                                if (item.Image.StartsWith("/uploads/"))
                                {
                                    imagePath = Path.Combine(_webHostEnvironment.WebRootPath, item.Image.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                                }
                                else
                                {
                                    imagePath = item.Image;
                                }

                                if (System.IO.File.Exists(imagePath))
                                {
                                    var picture = ws.AddPicture(imagePath);
                                    picture.MoveTo(ws.Cell(currentRow, 10), 8, 5);
                                    picture.WithSize(100, 70);

                                    var imageCell = ws.Cell(currentRow, 10);
                                    imageCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                    imageCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                                }
                                else
                                {
                                    ws.Cell(currentRow, 10).Value = "No image";
                                    ws.Cell(currentRow, 10).Style.Font.FontColor = XLColor.Gray;
                                }
                            }
                            catch (Exception ex)
                            {
                                ws.Cell(currentRow, 10).Value = $"Error: {ex.Message}";
                                ws.Cell(currentRow, 10).Style.Font.FontColor = XLColor.Red;
                            }
                        }
                        else
                        {
                            ws.Cell(currentRow, 10).Value = "-";
                            ws.Cell(currentRow, 10).Style.Font.FontColor = XLColor.Gray;
                        }
                    }

                    // Styling
                    ws.Columns(1, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Columns(1, 9).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    ws.Column(7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left; // Mô tả căn trái

                    // Column widths
                    ws.Column(1).Width = 8;
                    ws.Column(2).Width = 15;
                    ws.Column(3).Width = 20;
                    ws.Column(4).Width = 15;
                    ws.Column(5).Width = 25;
                    ws.Column(6).Width = 12;
                    ws.Column(7).Width = 30;
                    ws.Column(8).Width = 18;
                    ws.Column(9).Width = 15;
                    ws.Column(10).Width = 15;

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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDownTime(SVN_Downtime_Info model, IFormFile? imageFile)
        {
            // ===== 1) Chuẩn hoá/điền mặc định =====
            if (string.IsNullOrWhiteSpace(model.Code))
                model.Code = model.Operation ?? string.Empty;

            if (string.IsNullOrWhiteSpace(model.Name))
                model.Name = model.Operation ?? string.Empty;

            if (!model.Datetime.HasValue || model.Datetime.Value == default)
                model.Datetime = DateTime.Now;

            if (string.IsNullOrWhiteSpace(model.EstimateTime))
                model.EstimateTime = string.Empty;

            if (string.IsNullOrWhiteSpace(model.Description))
                model.Description = string.Empty;

            // ✅ Đọc AutoRunHint từ form
            var autoRunHint = Request.Form["AutoRunHint"].FirstOrDefault();
            model.AutoRunEnabled = autoRunHint == "1";

            // ===== 2) Xử lý upload ảnh (tuỳ chọn) =====
            string imagePath = string.Empty;
            if (imageFile != null && imageFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
                var ext = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(ext))
                    return Json(new { success = false, message = "Chỉ cho phép upload ảnh: jpg, jpeg, png, gif, bmp" });

                if (imageFile.Length > 5 * 1024 * 1024)
                    return Json(new { success = false, message = "Kích thước ảnh không được vượt quá 5MB" });

                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "status-images");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                imagePath = $"/uploads/status-images/{fileName}";
            }

            model.Image = imagePath;

            // ===== 3) Validate ModelState & Lưu DB =====
            if (!ModelState.IsValid)
            {
                await RefillOpsForToday();
                await RefillReasonsAsync();
                TempData["Error"] = "Dữ liệu không hợp lệ!";
                return View("CreateDownTime", model);
            }

            _context.SVN_Downtime_Infos.Add(model);
            await _context.SaveChangesAsync();

            // ===== 4) Trả JSON cho AJAX =====
            return Json(new { success = true, message = "Đã lưu downtime!" });
        }


        /* Hàm fill danh sách dropdown mã lỗi ISS Code - Error Reason */
        private async Task RefillReasonsAsync()
        {
            ViewBag.ReasonOptions = await _context.SVN_Downtime_Reasons
                .AsNoTracking()
                .OrderBy(r => r.Reason_Name)
                .Select(r => new { r.Reason_Code, r.Reason_Name })
                .ToListAsync();
        }

        /* Hàm fill danh sách Operation đang chạy trong ngày hôm nay */
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


        /* Hàm báo cáo downtime */
        [HttpGet]
        public async Task<IActionResult> ReportDowntime(string fromDate = "", string toDate = "", int page = 1, int pageSize = 10)
        {
            try
            {
                var allData = await GetDowntimeReportData(fromDate, toDate);

                // Phân trang CHỈ cho danh sách operation
                var totalRecords = allData.Count;
                var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

                // Đảm bảo page không vượt quá totalPages
                page = Math.Max(1, Math.Min(page, totalPages));

                var pagedData = allData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;

                // Chart data vẫn dùng TOÀN BỘ dữ liệu

                ViewBag.ChartData = PrepareChartData(allData);
                ViewBag.IssCodeChartData = PrepareIssCodeChartData(allData);
                ViewBag.DailyChartData = PrepareDailyDowntimeChartData(allData, fromDate, toDate);

                // Truyền cả toàn bộ dữ liệu cho thống kê
                ViewBag.AllData = allData;

                // Thêm thông tin phân trang
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
                ViewBag.ErrorMessage = $"Lỗi: {ex.Message}";
                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
                ViewBag.CurrentPage = 1;
                ViewBag.PageSize = 10;
                ViewBag.TotalRecords = 0;
                ViewBag.TotalPages = 1;
                ViewBag.HasPreviousPage = false;
                ViewBag.HasNextPage = false;
                return View(new List<DowntimeReportByOperation>());
            }
        }

        /* Hàm chuẩn bị dữ liệu downtime ngày => Chart biểu đồ xu hướng downtime */
        private DailyDowntimeChartData PrepareDailyDowntimeChartData(List<DowntimeReportByOperation> reportData, string fromDate, string toDate)
        {
            var dailyData = new DailyDowntimeChartData
            {
                Dates = new List<string>(),
                DowntimeMinutes = new List<double>(),
                DowntimeCounts = new List<int>()
            };

            try
            {
                // Lấy tất cả records downtime trong khoảng thời gian
                var query = from d in _context.SVN_Downtime_Infos
                            join r in _context.SVN_Downtime_Reasons
                            on d.ISS_Code equals r.Reason_Code into reasons
                            from r in reasons.DefaultIfEmpty()
                            select new
                            {
                                d.Operation,
                                d.State,
                                d.ISS_Code,
                                ErrorName = r != null ? r.Reason_Name : "Chưa xác định",
                                d.Datetime,
                                d.SVNCode
                            };

                // Lọc theo ngày
                if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
                {
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);
                }

                if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
                {
                    query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);
                }

                var allRecords = query.ToList();

                // Tính toán downtime theo ngày
                var downtimeByDate = new Dictionary<DateTime, (double Minutes, int Count)>();

                // Nhóm records theo SVNCode và Operation
                var groupedBySVNAndOp = allRecords
                    .Where(x => !string.IsNullOrEmpty(x.Operation) && !string.IsNullOrEmpty(x.SVNCode))
                    .GroupBy(x => new { x.SVNCode, x.Operation });

                foreach (var group in groupedBySVNAndOp)
                {
                    var records = group.OrderBy(x => x.Datetime).ToList();

                    for (int i = 0; i < records.Count - 1; i++)
                    {
                        var current = records[i];
                        var next = records[i + 1];

                        // Tính downtime từ Stop đến Run
                        if (current.State?.Trim().ToUpper() == "STOP" &&
                            next.State?.Trim().ToUpper() == "RUN" &&
                            current.Datetime.HasValue &&
                            next.Datetime.HasValue)
                        {
                            var downtimeMinutes = (next.Datetime.Value - current.Datetime.Value).TotalMinutes;
                            var dateKey = current.Datetime.Value.Date;

                            if (downtimeByDate.ContainsKey(dateKey))
                            {
                                downtimeByDate[dateKey] = (downtimeByDate[dateKey].Minutes + downtimeMinutes,
                                                         downtimeByDate[dateKey].Count + 1);
                            }
                            else
                            {
                                downtimeByDate[dateKey] = (downtimeMinutes, 1);
                            }
                        }
                    }
                }

                // Sắp xếp theo ngày
                var sortedDates = downtimeByDate.Keys.OrderBy(date => date).ToList();

                foreach (var date in sortedDates)
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


        /* Hàm lấy dữ liệu downtime trong khoảng STOP -> RUN, sau đó tổng hợp theo Operation và ISS Code */
        private async Task<List<DowntimeReportByOperation>> GetDowntimeReportData(string fromDate, string toDate)
        {
            // Lấy tất cả dữ liệu downtime có JOIN với Reasons
            var query = from d in _context.SVN_Downtime_Infos
                        join r in _context.SVN_Downtime_Reasons
                        on d.ISS_Code equals r.Reason_Code into reasons
                        from r in reasons.DefaultIfEmpty()
                        select new
                        {
                            d.Operation,
                            d.State,
                            d.ISS_Code,
                            ErrorName = r != null ? r.Reason_Name : "Chưa xác định",
                            d.Datetime,
                            d.SVNCode
                        };

            // Lọc theo ngày
            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var from))
            {
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date >= from.Date);
            }

            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var to))
            {
                query = query.Where(x => x.Datetime.HasValue && x.Datetime.Value.Date <= to.Date);
            }

            var allRecords = await query
                .OrderBy(x => x.SVNCode)
                .ThenBy(x => x.Operation)
                .ThenBy(x => x.Datetime)
                .ToListAsync();

            // Tính toán downtime: từ Stop đến Run, nhóm theo ISS_Code của record Stop
            var downtimeRecords = new List<DowntimeRecord>();

            var groupedBySVNAndOp = allRecords
                .Where(x => !string.IsNullOrEmpty(x.Operation) && !string.IsNullOrEmpty(x.SVNCode))
                .GroupBy(x => new { x.SVNCode, x.Operation });

            foreach (var group in groupedBySVNAndOp)
            {
                var records = group.OrderBy(x => x.Datetime).ToList();

                for (int i = 0; i < records.Count - 1; i++)
                {
                    var current = records[i];
                    var next = records[i + 1];

                    // Tính downtime từ Stop đến Run
                    if (current.State?.Trim().ToUpper() == "STOP" &&
                        next.State?.Trim().ToUpper() == "RUN" &&
                        current.Datetime.HasValue &&
                        next.Datetime.HasValue)
                    {
                        var downtimeMinutes = (next.Datetime.Value - current.Datetime.Value).TotalMinutes;

                        // Lấy ISS_Code và ErrorName từ record STOP (current)
                        var issCode = string.IsNullOrWhiteSpace(current.ISS_Code) ? "N/A" : current.ISS_Code.Trim();
                        var errorName = string.IsNullOrWhiteSpace(current.ErrorName) ? "Không xác định" : current.ErrorName.Trim();

                        downtimeRecords.Add(new DowntimeRecord
                        {
                            Operation = current.Operation.Trim(),
                            ISS_Code = issCode,
                            ErrorName = errorName,
                            DowntimeMinutes = downtimeMinutes
                        });
                    }
                }
            }

            // Nhóm theo Operation và ISS_Code
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

        /* Hàm chuyển đổi tổng số phút sang chuỗi thời gian */
        private string FormatMinutesToTime(double minutes)
        {
            if (minutes < 0) return "0h 0m";

            int hours = (int)(minutes / 60);
            int mins = (int)(minutes % 60);

            return $"{hours}h {mins}m";
        }


        /* Hàm chuẩn bị dữ liệu downtime theo từng Operation */
        private DowntimeChartData PrepareChartData(List<DowntimeReportByOperation> reportData)
        {
            var chartData = new DowntimeChartData
            {
                Operations = reportData.Select(x => x.Operation).ToList(),
                DowntimeMinutes = reportData.Select(x => Math.Round(x.TotalDowntimeMinutes, 2)).ToList(),
                ErrorBreakdowns = new List<ChartErrorBreakdown>()
            };

            foreach (var op in reportData)
            {
                chartData.ErrorBreakdowns.Add(new ChartErrorBreakdown
                {
                    Operation = op.Operation,
                    ErrorNames = op.ErrorDetails.Select(e => e.ErrorName).ToList(),
                    Minutes = op.ErrorDetails.Select(e => Math.Round(e.TotalDowntimeMinutes, 2)).ToList()
                });
            }

            return chartData;
        }

        /* Hàm tổng hợp và chuẩn bị dữ liệu Downtime theo ISS Code */
        private IssCodeChartData PrepareIssCodeChartData(List<DowntimeReportByOperation> reportData)
        {
            // Tổng hợp tất cả errors từ các operation
            var allErrors = reportData
                .SelectMany(op => op.ErrorDetails)
                .GroupBy(e => new { e.ISS_Code, e.ErrorName })
                .Select(g => new
                {
                    IssCode = g.Key.ISS_Code,
                    ErrorName = g.Key.ErrorName,
                    TotalMinutes = g.Sum(e => e.TotalDowntimeMinutes),
                    Count = g.Sum(e => e.DowntimeCount)
                })
                .OrderByDescending(x => x.TotalMinutes)
                .ToList();

            return new IssCodeChartData
            {
                IssCodeLabels = allErrors.Select(e => $"{e.IssCode} - {e.ErrorName}").ToList(),
                DowntimeMinutes = allErrors.Select(e => Math.Round(e.TotalMinutes, 2)).ToList(),
                DowntimeCounts = allErrors.Select(e => e.Count).ToList()
            };
        }

        /* Hàm xuất Excel cho báo cáo downtime */
        [HttpGet]
        public async Task<IActionResult> ExportDowntimeReportToExcel(string fromDate = "", string toDate = "")
        {
            try
            {
                var reportData = await GetDowntimeReportData(fromDate, toDate);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Downtime Report");
                    var currentRow = 1;

                    ws.Style.Font.FontName = "Times New Roman";
                    ws.Style.Font.FontSize = 11;

                    // Title
                    ws.Cell(currentRow, 1).Value = "BÁO CÁO DOWNTIME THEO OPERATION VÀ LỖI";
                    ws.Range(currentRow, 1, currentRow, 6).Merge();
                    ws.Cell(currentRow, 1).Style.Font.Bold = true;
                    ws.Cell(currentRow, 1).Style.Font.FontSize = 14;
                    ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    currentRow += 2;

                    // Date range
                    if (!string.IsNullOrEmpty(fromDate) || !string.IsNullOrEmpty(toDate))
                    {
                        ws.Cell(currentRow, 1).Value = $"Từ ngày: {fromDate} - Đến ngày: {toDate}";
                        ws.Range(currentRow, 1, currentRow, 6).Merge();
                        currentRow += 2;
                    }

                    foreach (var operation in reportData)
                    {
                        // Operation header
                        ws.Cell(currentRow, 1).Value = $"Operation: {operation.Operation}";
                        ws.Cell(currentRow, 1).Style.Font.Bold = true;
                        ws.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
                        ws.Range(currentRow, 1, currentRow, 6).Merge();
                        currentRow++;

                        // Tổng hợp Operation
                        ws.Cell(currentRow, 1).Value = "Tổng số lần downtime:";
                        ws.Cell(currentRow, 2).Value = operation.TotalDowntimeCount;
                        ws.Cell(currentRow, 3).Value = "Tổng thời gian:";
                        ws.Cell(currentRow, 4).Value = operation.TotalDowntimeFormatted;
                        ws.Cell(currentRow, 1).Style.Font.Bold = true;
                        ws.Cell(currentRow, 3).Style.Font.Bold = true;
                        currentRow++;

                        // Chi tiết lỗi header
                        string[] headers = { "ISS Code", "Tên lỗi", "Số lần", "Tổng thời gian (phút)", "Thời gian", "%" };
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

                        // Chi tiết từng lỗi
                        foreach (var error in operation.ErrorDetails)
                        {
                            ws.Cell(currentRow, 1).Value = error.ISS_Code;
                            ws.Cell(currentRow, 2).Value = error.ErrorName;
                            ws.Cell(currentRow, 3).Value = error.DowntimeCount;
                            ws.Cell(currentRow, 4).Value = Math.Round(error.TotalDowntimeMinutes, 2);
                            ws.Cell(currentRow, 5).Value = error.TotalDowntimeFormatted;

                            double percentage = operation.TotalDowntimeMinutes > 0
                                ? (error.TotalDowntimeMinutes / operation.TotalDowntimeMinutes * 100)
                                : 0;
                            ws.Cell(currentRow, 6).Value = $"{Math.Round(percentage, 1)}%";

                            currentRow++;
                        }

                        currentRow += 2; // Khoảng cách giữa các operation
                    }

                    // Adjust columns
                    ws.Column(1).Width = 15;
                    ws.Column(2).Width = 30;
                    ws.Column(3).Width = 12;
                    ws.Column(4).Width = 20;
                    ws.Column(5).Width = 15;
                    ws.Column(6).Width = 10;

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
                return Json(new { success = false, message = $"Lỗi xuất Excel: {ex.Message}" });
            }
        }

        // Helper class
        private class DowntimeRecord
        {
            public string Operation { get; set; }
            public string ISS_Code { get; set; }
            public string ErrorName { get; set; }
            public double DowntimeMinutes { get; set; }
        }


        // Hàm test
        [HttpGet]
        public async Task<IActionResult> TestProcessAll()
        {
            try
            {
                return await ProcessAllHistoryToDetail();
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, stackTrace = ex.StackTrace });
            }
        }


        // DTO class cho request
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