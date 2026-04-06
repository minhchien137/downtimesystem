using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MachineStatusUpdate.Models
{
    [Table("SVN_Downtime_Info_Devel")]
    public class SVN_Downtime_Info_Devel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string? Code { get; set; }

        public string? Name { get; set; }

        public string? State { get; set; }

        /// <summary>生产线 / Operation (Line)</summary>
        public string? Operation { get; set; }

        /// <summary>预计时间 / Estimated Time</summary>
        public string? EstimateTime { get; set; }

        /// <summary>异常描述 / Problem Description</summary>
        public string? Description { get; set; }

        /// <summary>图片 / Image path</summary>
        public string? Image { get; set; }

        public DateTime? Datetime { get; set; }

        /// <summary>员工工号 / Employee Code (ID)</summary>
        public string? EmployeeCode { get; set; }

        /// <summary>员工姓名 / Employee Name</summary>
        public string? EmployeeName { get; set; }

        /// <summary>设备编号 / Machine/Fixture Code</summary>
        public string? MachineCode { get; set; }

        /// <summary>操作 / Location</summary>
        public string? Location { get; set; }

        /// <summary>原因 / Reason Code (replaces ISS-Code)</summary>
        public string? Reason { get; set; }

        /// <summary>影响 / Effect</summary>
        public string? Effect { get; set; }

        /// <summary>工位 / Station</summary>
        public string? Station { get; set; }

        /// <summary>维修动作 / Action taken</summary>
        public string? Action { get; set; }

        /// <summary>根本原因 / Root Cause</summary>
        public string? RootCause { get; set; }

        /// <summary>更换配件 / Spare parts used</summary>
        public string? SpareParts { get; set; }

        // === Not mapped (runtime only) ===
        [NotMapped]
        public string? ErrorName { get; set; }
    }

    [Table("SVN_target")]
    [Keyless]
    public class SVN_target
    {
        public string Operation { get; set; }
        public string Date_time { get; set; } // "yyyyMMdd"
    }

    [Table("SVN_Downtime_Reason")]
    public class SVN_Downtime_Reason
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string Reason_Code { get; set; }
        public string Reason_Name { get; set; }
    }

    [Table("SM_Downtime_Reason")]
    public class SM_Downtime_Reason
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string Reason_Code { get; set; }
        public string Reason_Name { get; set; }
    }
}
