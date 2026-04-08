using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MachineStatusUpdate.Models
{
    [Table("SVN_Notifications")]
    public class SVN_Notification
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>Username người nhận thông báo (có thể là "ALL_TECH", "ALL_ADMIN", hoặc username cụ thể)</summary>
        public string RecipientUsername { get; set; } = "";

        /// <summary>Role nhận: "Technical", "Production", "Admin", hoặc tên username cụ thể</summary>
        public string RecipientRole { get; set; } = "";

        /// <summary>Loại thông báo: "STOP" (Prod→Tech), "TECH_RESPONSE" (Tech→Prod), "RUN" (Prod→Tech)</summary>
        public string NotifType { get; set; } = "";

        /// <summary>Tiêu đề ngắn</summary>
        public string Title { get; set; } = "";

        /// <summary>Nội dung chi tiết (JSON hoặc text)</summary>
        public string? Body { get; set; }

        /// <summary>MachineCode liên quan</summary>
        public string? MachineCode { get; set; }

        /// <summary>Operation liên quan</summary>
        public string? Operation { get; set; }

        /// <summary>ID của SVN_Downtime_TechResponse liên quan (nếu có)</summary>
        public int? TechResponseId { get; set; }

        /// <summary>Thời gian tạo thông báo</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Đã đọc chưa?</summary>
        public bool IsRead { get; set; } = false;

        /// <summary>Thời gian đọc</summary>
        public DateTime? ReadAt { get; set; }

        // Action của Tech: "ACCEPT" | "WAIT" | null
        public string? TechAction { get; set; }

        // Tên kỹ thuật viên respond
        public string? TechName { get; set; }
    }
}