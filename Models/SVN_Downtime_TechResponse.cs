using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MachineStatusUpdate.Models
{
    [Table("SVN_Downtime_TechResponses")]
    public class SVN_Downtime_TechResponse
    {
        [Key]
        public int Id { get; set; }

        public int DowntimeId { get; set; }           // FK đến SVN_Downtime_Infos_Devel

        public string? MachineCode      { get; set; }
        public string? Operation        { get; set; }
        public string? EmployeeCode     { get; set; }
        public string? EmployeeName     { get; set; }
        public string? OperatorUsername { get; set; } // username của prod01, prod02...
        public string? Reason           { get; set; }
        public string? Effect           { get; set; }
        public string? EstimateTime     { get; set; }
        public string? Station          { get; set; }
        public string? Description      { get; set; }
        public string? Location         { get; set; }

        public DateTime? StopDatetime    { get; set; } // thời điểm STOP

        // NULL = chưa xử lý | "ACCEPT" | "WAIT"
        public string?   TechAction      { get; set; }
        public string?   TechUsername    { get; set; }
        public DateTime? RespondDatetime { get; set; }

        // Repair data filled by Tech before notifying Prod to Run
        public string? RepairAction      { get; set; }
        public string? RepairRootCause   { get; set; }
        public string? RepairSpareParts  { get; set; }
    }
}