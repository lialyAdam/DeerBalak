using Deerbalak.Data.Helpers.Constants;
using Deerbalak.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DeerBalak.Controllers
{
    [Authorize(Roles = AppRoles.User)]

    public class FavoritesController : Controller
    {
        private readonly IPostsService _postsService;
        public FavoritesController(IPostsService postsService)
        {
            _postsService = postsService;
            
        }
        public async Task <IActionResult> Index()
        {
            var loggedInUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var myFavorites = await _postsService.GetAllFavoritedPostsAsync(int.Parse(loggedInUserId));

            return View(myFavorites);
        }
    }
}
