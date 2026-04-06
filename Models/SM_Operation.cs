using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MachineStatusUpdate.Models
{

    [Table("SM_Operation")]
    public class SM_Operation
    {

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }


        [Column("Operation")]
        [StringLength(100)]
        public string Operation { get; set; }

 
    }

}
