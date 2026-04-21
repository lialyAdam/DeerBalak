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
        private readonly FakeNewsDetectionService _fakeNewsService;
        private readonly ClaimTrackingService _claimTrackingService;

        public HomeController(
            ILogger<HomeController> logger,
            IPostsService postsService,
            IHashtagsService hashtagsService,
            IFilesService filesService,
            INotificationsService notificationsService,
            FakeNewsDetectionService fakeNewsService,
            ClaimTrackingService claimTrackingService)
        {
            _logger = logger;
            _postsService = postsService;
            _hashtagsService = hashtagsService;
            _filesService = filesService;
            _notificationsService = notificationsService;
            _fakeNewsService = fakeNewsService;
            _claimTrackingService = claimTrackingService;
        }

        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 10)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var loggedInUserId = GetUserId();
            if (loggedInUserId == null) return RedirectToLogin();

            var posts = await _postsService.GetFeedAsync(loggedInUserId.Value, page, pageSize);
            var totalPosts = await _postsService.GetTotalPostsCountAsync(loggedInUserId.Value);

            var pagedPosts = new PagedPostsVM
            {
                Posts = posts,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPosts = totalPosts
            };

            stopwatch.Stop();
            _logger.LogInformation($"Home Index loaded in {stopwatch.ElapsedMilliseconds}ms for user {loggedInUserId}");

            return View(pagedPosts);
        }
        public async Task<IActionResult> Details(int? id, int? postId)
        {
            var resolvedPostId = postId ?? id;
            if (!resolvedPostId.HasValue)
            {
                return NotFound();
            }

            var post = await _postsService.GetPostByIdAsync(resolvedPostId.Value);
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

            // AI Analysis for fake news detection
            var analysisResult = await _fakeNewsService.AnalyzePostAsync(post.Content, loggedInUserId.ToString());
            _logger.LogInformation($"Post analysis: Score {analysisResult.Score}, Label {analysisResult.Label}, Category {analysisResult.Category}");

            var imageUploadPath = await _filesService.UploadImageAsync(post.Image, ImageFileType.PostImage);

            var newPost = new Post
            {
                Content = post.Content,
                DateCreated = DateTime.UtcNow,
                DateUpdated = DateTime.UtcNow,
                ImageUrl = imageUploadPath,
                NrOfReports = 0,
                UserId = loggedInUserId.Value,
                FakeNewsScore = analysisResult.Score,
                FakeNewsLabel = analysisResult.Label,
                FakeNewsCategory = analysisResult.Category,
                FakeNewsExplanation = analysisResult.Explanation,
                FakeNewsRecommendedAction = analysisResult.RecommendedAction,
                // Set claim tracking with initial values
                AppearedCount = 1,
                UniqueUsersCount = 1,
                FirstSeen = DateTime.UtcNow
            };

            await _postsService.CreatePostAsync(newPost);
            await _hashtagsService.ProcessHashtagsForNewPostAsync(post.Content);

            // Analyze claim tracking asynchronously (don't block post creation)
            _ = Task.Run(async () =>
            {
                try
                {
                    var claimTracking = await _claimTrackingService.AnalyzeClaimSpreadAsync(post.Content, newPost.Id);
                    await _claimTrackingService.UpdatePostClaimTrackingAsync(newPost.Id, claimTracking);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error analyzing claim tracking");
                }
            });

            // Show analysis result to user if high risk
            if (analysisResult.Score >= 7)
            {
                TempData["FakeNewsAlert"] = $"⚠️ تحذير: {analysisResult.Explanation}. {analysisResult.RecommendedAction}";
                TempData["FakeNewsScore"] = analysisResult.Score;
                TempData["FakeNewsLabel"] = analysisResult.Label;
            }
            else
            {
                // Show success message with analysis type
                string analysisSource = analysisResult.Score == 0 ? "✅ تم التحقق المحلي" : "✅ تم التحليل بواسطة AI";
                TempData["SuccessMessage"] = $"{analysisSource} | النتيجة: {analysisResult.Label}";
            }

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
                await _notificationsService.AddNewNotificationAsync(post.UserId, NotificationType.Like, userName, postUsefulVM.PostId, userId.Value);

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
                await _notificationsService.AddNewNotificationAsync(post.UserId, NotificationType.Favorite, userName, postFavoriteVM.PostId, userId.Value);

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
                await _notificationsService.AddNewNotificationAsync(post.UserId, NotificationType.Comment, userName, postCommentVM.PostId, userId.Value);

            return PartialView("Home/_Post", post);
        }

        [HttpPost]
        public async Task<IActionResult> AddPostReport(PostReportVM postReportVM)
        {
            var loggedInUserId = GetUserId();
            if (loggedInUserId == null) return RedirectToLogin();

            await _postsService.ReportPostAsync(postReportVM.PostId, loggedInUserId.Value, postReportVM.Reason);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePostComment(RemoveCommentVM removeCommentVM)
        {
            var loggedInUserId = GetUserId();
            if (loggedInUserId == null) return RedirectToLogin();

            await _postsService.RemovePostCommentAsync(removeCommentVM.CommentId, loggedInUserId.Value);
            var post = await _postsService.GetPostByIdAsync(removeCommentVM.PostId);

            return PartialView("Home/_Post", post);
        }

        [HttpPost]
        public async Task<IActionResult> PostRemove(PostRemoveVM postRemoveVM)
        {
            var loggedInUserId = GetUserId();
            if (loggedInUserId == null) return RedirectToLogin();

            var postRemoved = await _postsService.RemovePostAsync(postRemoveVM.PostId, loggedInUserId.Value);
            await _hashtagsService.ProcessHashtagsForRemovedPostAsync(postRemoved.Content);

            return RedirectToAction("Index");
        }
    }
}
