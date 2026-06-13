using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MachineStatusUpdate.Models
{
    [Table("SVN_Downtime_Info")]
    public class SVN_Downtime_Info
    {

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string? Code { get; set; }

        public string? Name { get; set; }

        public string? State { get; set; }

        public string? Operation { get; set; }

        public string? EstimateTime { get; set; }

        public string? Description { get; set; }

        public string? Image { get; set; }

        public DateTime? Datetime { get; set; }

        [Column("ISS-Code")]
        public string? ISS_Code { get; set; }

        [NotMapped]
        public string? ErrorName { get; set; }

        [Column("SVNCode")]
        public string? SVNCode { get; set; }

        [Column("AutoRunEnabled")]
        public bool AutoRunEnabled { get; set; } = false;

        [Column("AutoRunDescription")]
        public string? AutoRunDescription { get; set; }

        [Column("IsAutoRunExecuted")]
        public bool IsAutoRunExecuted { get; set; } = false;

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
}
