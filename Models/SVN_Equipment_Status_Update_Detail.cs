using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MachineStatusUpdate.Models
{
    public class SVN_Equipment_Status_Update_Detail
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        [Required]
        [MaxLength(100)]
        public string Operation { get; set; }

        [Required]
        [MaxLength(50)]
        public string State { get; set; }

        [MaxLength(50)]
        public string EstimateTime { get; set; }

        [Required]
        [MaxLength(50)]
        public string FromTime { get; set; }


        [MaxLength(50)]
        public string ToTime { get; set; }

        public double DurationMinutes { get; set; }


    }
}