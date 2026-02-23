using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MachineStatusUpdate.Models
{
    public class SVN_Equipment_Status_Update
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        [Required]
        [MaxLength(150)]
        public string Operation { get; set; }

        [Required]
        [MaxLength(50)]
        public string StartTime { get; set; }

        [Required]
        public decimal Duration { get; set; }

        [Required]
        public decimal TotalDuration { get; set; }

        [Required]
        public DateTime Datetime { get; set; }
    }
}