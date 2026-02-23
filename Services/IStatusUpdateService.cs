using MachineStatusUpdate.Models;

namespace MachineStatusUpdate.Services
{
    public interface IStatusUpdateService
    {
        Task ProcessSingleRecordToUpdateDetail(SVN_Equipment_Info_History_Test model);
        Task ProcessDataToStatusUpdate(DateTime? filterDate = null);
        Task<List<SVN_Equipment_Status_Update>> GetStatusUpdates(DateTime? filterDate = null);

    }
}