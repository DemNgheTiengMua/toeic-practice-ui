using System.Linq;
using System.Threading.Tasks;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public interface IPremiumPathService
{
    Task UnlockPathAsync(int userId, int learnerPathId);
    Task<bool> IsPathUnlockedAsync(int userId, int learnerPathId);
}
