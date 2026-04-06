using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MachineStatusUpdate.Models;
using MachineStatusUpdate.Models;


[ApiController]
[Route("api/[controller]")]
public class OdooController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly ApplicationDbContext _db;

    private const string OdooApiUrl = "https://sigmaworldwide.io/web/dataset/call_kw/mrp.production/web_search_read";

    private const string OdooApiEquipment = "https://sigmaworldwide.io/web/dataset/call_kw/maintenance.equipment/web_search_read";

    public OdooController(HttpClient httpClient, ApplicationDbContext db)
    {
        _httpClient = httpClient;
        _db = db;
    }

    // Hàm lấy cookie từ bảng SVN_Defect_Cookie

    private async Task<string?> GetCookieFromDbAsync()
    {

        var record = await _db.SVN_Defect_Cookies.FirstOrDefaultAsync();
        if (record == null || string.IsNullOrWhiteSpace(record.cookie))
        {
            Console.WriteLine("Cookie not found in SVN_Defect_Cookie table.");
            return null;
        }
        return record.cookie;
    }


    [HttpGet("equipments/sm-eq")]
    public async Task<IActionResult> GetAllSmEqEquipments()
    {
        var cookie = "cids=1; session_id=ce413ab411620a1dac7fd73d3b0651f2a0f25cb1; frontend_lang=en_US";

        var allRecords = new List<EquipmentDto>();
        int offset = 0;
        const int limit = 80;
        int total = int.MaxValue;

        while (offset < total)
        {
            string payload = $@"
        {{
            ""id"": 12,
            ""jsonrpc"": ""2.0"",
            ""method"": ""call"",
            ""params"": {{
                ""model"": ""maintenance.equipment"",
                ""method"": ""web_search_read"",
                ""args"": [],
                ""kwargs"": {{
                    ""limit"": {limit},
                    ""offset"": {offset},
                    ""order"": """",
                    ""context"": {{
                        ""lang"": ""en_US"",
                        ""tz"": ""Asia/Ho_Chi_Minh"",
                        ""uid"": 2,
                        ""allowed_company_ids"": [1],
                        ""bin_size"": true,
                        ""default_company_id"": 1
                    }},
                    ""count_limit"": 10001,
                    ""domain"": [[""category_id"", ""="", 12]],
                    ""fields"": [
                        ""id"", ""name"", ""serial_no"", ""model"", ""category_id"",
                        ""technician_user_id"", ""employee_id"", ""owner_user_id"",
                        ""maintenance_open_count"", ""next_action_date"", ""__last_update""
                    ]
                }}
            }}
        }}";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, OdooApiEquipment)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Cookie", cookie);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(body);

                if (offset == 0)
                    total = json["result"]?["length"]?.Value<int>() ?? 0;

                var records = json["result"]?["records"] as JArray;
                if (records == null || records.Count == 0) break;

                foreach (var r in records)
                {
                    allRecords.Add(new EquipmentDto
                    {
                        Id = r["id"]?.Value<int>() ?? 0,
                        Name = r["name"]?.ToString() ?? "",
                        SerialNo = r["serial_no"]?.Type == JTokenType.Boolean ? "" : r["serial_no"]?.ToString() ?? "",
                        Model = r["model"]?.ToString() ?? "",
                        Category = (r["category_id"] as JArray)?[1]?.ToString() ?? "",
                        TechnicianName = (r["technician_user_id"] as JArray)?[1]?.ToString() ?? "",
                        EmployeeName = (r["employee_id"] as JArray)?[1]?.ToString() ?? "",
                        OwnerName = (r["owner_user_id"] as JArray)?[1]?.ToString() ?? "",
                        MaintenanceOpenCount = r["maintenance_open_count"]?.Value<int>() ?? 0,
                        NextActionDate = r["next_action_date"]?.Type == JTokenType.Boolean ? null : r["next_action_date"]?.ToString(),
                        LastUpdate = r["__last_update"]?.ToString() ?? ""
                    });
                }

                offset += records.Count;
                if (records.Count < limit) break;
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(500, new { message = $"Error at offset {offset}", details = ex.Message });
            }
        }

        return Ok(new
        {
            total = allRecords.Count,
            records = allRecords
        });
    }

    

    // DTO
    public class EquipmentDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string SerialNo { get; set; }
        public string Model { get; set; }
        public string Category { get; set; }
        public string TechnicianName { get; set; }
        public string EmployeeName { get; set; }
        public string OwnerName { get; set; }
        public int MaintenanceOpenCount { get; set; }
        public string? NextActionDate { get; set; }
        public string LastUpdate { get; set; }
    }


}