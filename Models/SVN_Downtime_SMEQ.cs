using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MachineStatusUpdate.Models
{
    [Table("SVN_Downtime_SMEQ")]
    public class SVN_Downtime_SMEQ
    {
         [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int id { get; set; }
 
        [Column("name")]
        
        public string name { get; set; }

        [Column("namechinese")]
        public string namechinese { get; set; }
 
        [Column("serialnumber")]
        public string serialnumber { get; set; }
 
        [Column("model")]
        public string model { get; set; }
 
        [Column("operation")]
        public string operation { get; set; }
 
        [Column("location")]
        public string location { get; set; }


    }
}