using Deerbalak.Data.Helpers.Constants;
using Deerbalak.Data.Models;
using Deerbalak.Data.Services;
using DeerBalak.Controllers.Base;
using DeerBalak.ViewModels.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DeerBalak.Controllers
{
    [Authorize(Roles = AppRoles.User)]


    public class UsersController : BaseController
    {
        private readonly IUsersService _usersService;
        private readonly UserManager<User> _userManager;

        public UsersController(IUsersService usersService, UserManager<User> userManager)
        {
            _usersService = usersService;
            _userManager = userManager;

        }
        public IActionResult Index()
        {
            return View();
        }
        public async Task<IActionResult> Details(int? id, int? userId)
        {
            var resolvedUserId = userId ?? id;
            if (!resolvedUserId.HasValue)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(resolvedUserId.Value.ToString());
            if (user == null)
            {
                return NotFound();
            }

            var userPost = await _usersService.GetUserPosts(resolvedUserId.Value);
            var userProfileVM = new GetUserProfileVM()
            {
                User = user,
                Posts = userPost
            };
            return View(userProfileVM);
        }
    }
}
