using Deerbalak.Data.Helpers.Constants;
using Deerbalak.Data.Helpers.Enums;
using Deerbalak.Data.Models;
using Deerbalak.Data.Services;
using DeerBalak.Controllers.Base;
using DeerBalak.ViewModels.Home;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DeerBalak.Controllers
{
    [Authorize(Roles = "User,Admin")]
    public class HomeController : BaseController
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IPostsService _postsService;
        private readonly IHashtagsService _hashtagsService;
        private readonly IFilesService _filesService;
        private readonly INotificationsService _notificationsService;

        public HomeController(
            ILogger<HomeController> logger,
            IPostsService postsService,
            IHashtagsService hashtagsService,
            IFilesService filesService,
            INotificationsService notificationsService)
        {
            _logger = logger;
            _postsService = postsService;
            _hashtagsService = hashtagsService;
            _filesService = filesService;
            _notificationsService = notificationsService;
        }

        public async Task<IActionResult> Index()
        {
            var loggedInUserId = GetUserId();
            if (loggedInUserId == null) return RedirectToLogin();

            var allPosts = await _postsService.GetAllPostsAsync(loggedInUserId.Value);

            return View(allPosts);
        }
        public async Task<IActionResult> Details(int postId)
        {
            //var post = await _postsService.GetPostByIdAsync(postId); // ✅ هذا يرجع Post واحد
            //return View(post);

            //var post = await _postsService.GetAllPostsAsync(postId);
            //return View(post);
            var post = await _postsService.GetPostByIdAsync(postId); // يرجع منشور واحد فقط
            if (post == null)
            {
                return NotFound();
            }
            return View(post);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePost(PostVM post)
        {
            var loggedInUserId = GetUserId();
            if (loggedInUserId == null) return RedirectToLogin();

            var imageUploadPath = await _filesService.UploadImageAsync(post.Image, ImageFileType.PostImage);

            var newPost = new Post
            {
                Content = post.Content,
                DateCreated = DateTime.UtcNow,
                DateUpdated = DateTime.UtcNow,
                ImageUrl = imageUploadPath,
                NrOfReports = 0,
                UserId = loggedInUserId.Value
            };

            await _postsService.CreatePostAsync(newPost);
            await _hashtagsService.ProcessHashtagsForNewPostAsync(post.Content);

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePostUseful(PostUsefulVM postUsefulVM)
        {
            var userId = GetUserId();
            var userName = GetUserFullName();
            if (userId == null) return RedirectToLogin();

            var result = await _postsService.TogglePostLikeAsync(postUsefulVM.PostId, userId.Value);
            var post = await _postsService.GetPostByIdAsync(postUsefulVM.PostId);

            if (result.SendNotification && userId != post.UserId)
                await _notificationsService.AddNewNotificationAsync(post.UserId, NotificationType.Like, userName, postUsefulVM.PostId);

            return PartialView("Home/_Post", post);
        }

        [HttpPost]
        public async Task<IActionResult> TogglePostFavorite(PostFavoriteVM postFavoriteVM)
        {
            var userId = GetUserId();
            var userName = GetUserFullName();
            if (userId == null) return RedirectToLogin();

            var result = await _postsService.TogglePostFavoriteAsync(postFavoriteVM.PostId, userId.Value);
            var post = await _postsService.GetPostByIdAsync(postFavoriteVM.PostId);

            if (result.SendNotification && userId != post.UserId)
                await _notificationsService.AddNewNotificationAsync(post.UserId, NotificationType.Favorite, userName, postFavoriteVM.PostId);

            return PartialView("Home/_Post", post);
        }

        [HttpPost]
        public async Task<IActionResult> TogglePostVisibility(PostVisibilityVM postVisibilityVM)
        {
            var loggedInUserId = GetUserId();
            if (loggedInUserId == null) return RedirectToLogin();

            await _postsService.TogglePostVisibilityAsync(postVisibilityVM.PostId, loggedInUserId.Value);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPostComment(PostCommentVM postCommentVM)
        {
            var userId = GetUserId();
            var userName = GetUserFullName();
            if (userId == null) return RedirectToLogin();

            var newComment = new Comment
            {
                UserId = userId.Value,
                PostId = postCommentVM.PostId,
                Content = postCommentVM.Content,
                DateCreated = DateTime.UtcNow,
                DateUpdated = DateTime.UtcNow
            };

            await _postsService.AddPostCommentAsync(newComment);
            var post = await _postsService.GetPostByIdAsync(postCommentVM.PostId);

            if (userId != post.UserId)
                await _notificationsService.AddNewNotificationAsync(post.UserId, NotificationType.Comment, userName, postCommentVM.PostId);

            return PartialView("Home/_Post", post);
        }

        [HttpPost]
        public async Task<IActionResult> AddPostReport(PostReportVM postReportVM)
        {
            var loggedInUserId = GetUserId();
            if (loggedInUserId == null) return RedirectToLogin();

            await _postsService.ReportPostAsync(postReportVM.PostId, loggedInUserId.Value);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePostComment(RemoveCommentVM removeCommentVM)
        {
            await _postsService.RemovePostCommentAsync(removeCommentVM.CommentId);
            var post = await _postsService.GetPostByIdAsync(removeCommentVM.PostId);

            return PartialView("Home/_Post", post);
        }

        [HttpPost]
        public async Task<IActionResult> PostRemove(PostRemoveVM postRemoveVM)
        {
            var postRemoved = await _postsService.RemovePostAsync(postRemoveVM.PostId);
            await _hashtagsService.ProcessHashtagsForRemovedPostAsync(postRemoved.Content);

            return RedirectToAction("Index");
        }
    }
}
