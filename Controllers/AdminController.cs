using System.Threading.Tasks;
using Deerbalak.Data.Helpers.Constants;
using Deerbalak.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Deerbalak.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    public class AdminController : Controller
    {
        private readonly IAdminService _adminService;
        public AdminController(IAdminService adminService)
        {
            _adminService = adminService;
        }

        public async Task<IActionResult> Index()
        {
            var reportedPosts = await _adminService.GetReportedPostsAsync();
            return View(reportedPosts);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveReport(int postId)
        {
            await _adminService.ApproveReportAsync(postId);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> RejectReport(int postId)
        {
            await _adminService.RejectReportAsync(postId);
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Dashboard()
        {
            var stats = await _adminService.GetPostStatisticsAsync();
            return View(stats);
        }
    }
}