using MachineStatusUpdate.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MachineStatusUpdate.Services
{
    public class StatusUpdateService : IStatusUpdateService
    {
        private readonly ApplicationDbContext _context;

        public StatusUpdateService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task ProcessSingleRecordToUpdateDetail(SVN_Equipment_Info_History_Test model)
        {
            try
            {
                var historyRecords = await _context.SVN_Equipment_Info_History_Test
                    .Where(x => x.Code == model.Code)
                    .OrderBy(x => x.Datetime)
                    .ToListAsync();

                var currentRecordIndex = historyRecords.FindIndex(x => x.Id == model.Id);
                if (currentRecordIndex == -1) return;

                var currentRecord = historyRecords[currentRecordIndex];

                double? estimateTimeMinutes = null;
                if (!string.IsNullOrEmpty(currentRecord.EstimateTime) && currentRecord.Datetime.HasValue)
                {
                    if (TimeSpan.TryParse(currentRecord.EstimateTime, out TimeSpan estimateTimeSpan))
                    {
                        var estimateDateTime = currentRecord.Datetime.Value.Date.Add(estimateTimeSpan);
                        var timeDifference = estimateDateTime - currentRecord.Datetime.Value;
                        estimateTimeMinutes = timeDifference.TotalMinutes;
                    }
                }

                string toTime = "";
                double durationMinutes = 0;

                if (currentRecordIndex < historyRecords.Count - 1)
                {
                    var nextRecord = historyRecords[currentRecordIndex + 1];
                    if (nextRecord.Datetime.HasValue)
                    {
                        durationMinutes = (nextRecord.Datetime.Value - currentRecord.Datetime.Value).TotalMinutes;
                        toTime = Math.Round(durationMinutes, 2).ToString();
                    }
                }

                var detailRecord = new SVN_Equipment_Status_Update_Detail
                {
                    Name = currentRecord.Name ?? "",
                    Operation = currentRecord.Operation ?? "",
                    State = currentRecord.State ?? "",
                    EstimateTime = estimateTimeMinutes?.ToString("F2") ?? "",
                    FromTime = currentRecord.Datetime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    ToTime = toTime,
                    DurationMinutes = durationMinutes
                };

                var existingRecord = await _context.SVN_Equipment_Status_Update_Detail
                    .FirstOrDefaultAsync(x => x.Name == detailRecord.Name
                                           && x.Operation == detailRecord.Operation
                                           && x.State == detailRecord.State
                                           && x.FromTime == detailRecord.FromTime);

                if (existingRecord == null)
                {
                    _context.SVN_Equipment_Status_Update_Detail.Add(detailRecord);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    existingRecord.ToTime = detailRecord.ToTime;
                    existingRecord.DurationMinutes = detailRecord.DurationMinutes;
                    await _context.SaveChangesAsync();
                }

                if (currentRecordIndex > 0)
                {
                    var previousRecord = historyRecords[currentRecordIndex - 1];
                    var previousDetailRecord = await _context.SVN_Equipment_Status_Update_Detail
                        .FirstOrDefaultAsync(x => x.Name == previousRecord.Name
                                               && x.Operation == previousRecord.Operation
                                               && x.State == previousRecord.State
                                               && x.FromTime == previousRecord.Datetime.Value.ToString("yyyy-MM-dd HH:mm:ss"));

                    if (previousDetailRecord != null && string.IsNullOrEmpty(previousDetailRecord.ToTime))
                    {
                        var durationFromPrevious = (currentRecord.Datetime.Value - previousRecord.Datetime.Value).TotalMinutes;
                        previousDetailRecord.ToTime = Math.Round(durationFromPrevious, 2).ToString();
                        previousDetailRecord.DurationMinutes = durationFromPrevious;
                        await _context.SaveChangesAsync();
                    }
                }

                if (currentRecord.Datetime.HasValue)
                {
                    await ProcessDataToStatusUpdate(currentRecord.Datetime.Value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ProcessSingleRecordToUpdateDetail service: {ex.Message}");
                throw;
            }
        }

        public async Task ProcessDataToStatusUpdate(DateTime? filterDate = null)
        {
            try
            {
                var query = _context.SVN_Equipment_Status_Update_Detail.AsQueryable();

                if (filterDate.HasValue)
                {
                    var dateString = filterDate.Value.ToString("yyyy-MM-dd");
                    query = query.Where(x => x.FromTime.StartsWith(dateString));
                }

                var detailRecords = await query.ToListAsync();

                var groupedByNameAndDate = detailRecords
                    .Where(x => !string.IsNullOrEmpty(x.FromTime))
                    .GroupBy(x => new
                    {
                        Name = x.Name,
                        Operation = x.Operation, // Thêm Operation vào khóa nhóm
                        Date = x.FromTime.Substring(0, 10)
                    })
                    .ToList();

                foreach (var group in groupedByNameAndDate)
                {
                    var records = group.ToList();
                    if (!records.Any()) continue;

                    var name = group.Key.Name;
                    var operation = group.Key.Operation; // Lấy Operation từ khóa nhóm
                    var dateStr = group.Key.Date;

                    var latestDetailRecord = records.OrderByDescending(x => x.FromTime).FirstOrDefault();
                    var latestStartTime = latestDetailRecord?.FromTime ?? records.First().FromTime;

                    var nonRunRecords = records.Where(x => !string.Equals(x.State, "Run", StringComparison.OrdinalIgnoreCase)).ToList();

                    decimal totalDowntimeDuration = (decimal)nonRunRecords.Sum(x => x.DurationMinutes) / 60m;

                    decimal duration = 0;
                    if (!string.Equals(latestDetailRecord?.State, "Run", StringComparison.OrdinalIgnoreCase))
                    {
                        if (nonRunRecords.Any())
                        {
                            var durations = nonRunRecords.Select(x =>
                            {
                                if (!string.IsNullOrEmpty(x.ToTime) && x.DurationMinutes > 0)
                                {
                                    return (decimal)x.DurationMinutes;
                                }
                                else if (double.TryParse(x.EstimateTime, out double val))
                                {
                                    return (decimal)val;
                                }
                                return 0;
                            });
                            duration = durations.Any() ? durations.Max() / 60m : 0;
                        }
                    }

                    var datetime = DateTime.ParseExact(dateStr, "yyyy-MM-dd", null);

                    var existingUpdate = await _context.SVN_Equipment_Status_Update
                        .FirstOrDefaultAsync(x => x.Name == name && x.Operation == operation && x.Datetime.Date == datetime.Date); // Tìm kiếm theo cả Operation

                    if (existingUpdate != null)
                    {
                        existingUpdate.Operation = operation;
                        existingUpdate.StartTime = latestStartTime;
                        existingUpdate.Duration = duration;
                        existingUpdate.TotalDuration = totalDowntimeDuration;
                        existingUpdate.Datetime = datetime;
                    }
                    else
                    {
                        var statusUpdate = new SVN_Equipment_Status_Update
                        {
                            Name = name,
                            Operation = operation,
                            StartTime = latestStartTime,
                            Duration = duration,
                            TotalDuration = totalDowntimeDuration,
                            Datetime = datetime
                        };

                        _context.SVN_Equipment_Status_Update.Add(statusUpdate);
                    }
                }

                await _context.SaveChangesAsync();
                Console.WriteLine($"Đã xử lý thành công dữ liệu vào bảng SVN_Equipment_Status_Update");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ProcessDataToStatusUpdate: {ex.Message}");
                throw;
            }
        }

        public async Task<List<SVN_Equipment_Status_Update>> GetStatusUpdates(DateTime? filterDate = null)
        {
            try
            {
                var query = _context.SVN_Equipment_Status_Update.AsQueryable();

                if (filterDate.HasValue)
                {
                    query = query.Where(x => x.Datetime.Date == filterDate.Value.Date);
                }

                return await query
                    .OrderByDescending(x => x.Datetime)
                    .ThenBy(x => x.Name)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetStatusUpdates: {ex.Message}");
                throw;
            }
        }
    }
}