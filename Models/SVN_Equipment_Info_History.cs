using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MachineStatusUpdate.Models
{
    public class SVN_Equipment_Info_History
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

    }
}
