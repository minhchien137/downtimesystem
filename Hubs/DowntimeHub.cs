using Microsoft.AspNetCore.SignalR;

namespace MachineStatusUpdate.Hubs
{
    /// <summary>
    /// SignalR Hub xử lý real-time notification giữa Operator và Kỹ thuật.
    /// </summary>
    public class DowntimeHub : Hub
    {
        // ── Kỹ thuật join group để nhận thông báo STOP ──
        public async Task JoinTechnicianGroup()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "TechnicianGroup");
        }

        public async Task LeaveTechnicianGroup()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "TechnicianGroup");
        }

        // ── Operator join group riêng theo tên username để nhận phản hồi từ Tech ──
        // operatorUsername: ví dụ "prod01"
        public async Task JoinOperatorGroup(string operatorUsername)
        {
            if (!string.IsNullOrWhiteSpace(operatorUsername))
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Operator_{operatorUsername}");
        }

        public async Task LeaveOperatorGroup(string operatorUsername)
        {
            if (!string.IsNullOrWhiteSpace(operatorUsername))
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Operator_{operatorUsername}");
        }

        // ── DRI (PDE/QUAL/FAC/IT) join group để nhận thông báo từ Production ──
        public async Task JoinDRIGroup()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "DRIGroup");
        }

        public async Task LeaveDRIGroup()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "DRIGroup");
        }
    }
}