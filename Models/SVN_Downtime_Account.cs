using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MachineStatusUpdate.Models
{
    [Table("SVN_Downtime_Account")]
    public class SVN_Downtime_Account
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string Username { get; set; } = "";

        public string Password { get; set; } = "";
        public string Role { get; set; } = "Production";
    }
}