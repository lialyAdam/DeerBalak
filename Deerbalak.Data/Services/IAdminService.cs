using Deerbalak.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deerbalak.Data.Services
{
    public interface IAdminService
    {
        Task<List<Post>> GetReportedPostsAsync();
        Task ApproveReportAsync(int postId);
        Task RejectReportAsync(int postId);
    }
}
