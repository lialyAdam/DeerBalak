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
        public async Task<IActionResult> Details(int userId)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            var userPost = await _usersService.GetUserPosts(userId);

            var userProfileVM = new GetUserProfileVM()
            {
                User = user,
                Posts = userPost
            };
            return View(userProfileVM);
        }
    }
}
