using CrestApps.RetsSdk.Models;
using System.Threading.Tasks;

namespace CrestApps.RetsSdk.Services
{
    public interface IRetsSession
    {
        Task<bool> Start(bool backEnd);
        Task End();

        SessionResource Resource { get; }
        bool IsStarted();
    }
}
