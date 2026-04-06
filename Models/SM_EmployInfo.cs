using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MachineStatusUpdate.Models
{

    [Table("SM_EmployInfo")]
    public class SM_EmployInfo
    {

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }


        [Column("EmployeeID")]
        [StringLength(10)]
        public string EmployeeID { get; set; }

        [Column("ChineseName")]
        [StringLength(50)]
        public string ChineseName { get; set; }


        [Column("EnglishName")]
        [StringLength(50)]
        public string EnglishName { get; set; }
    }

}
