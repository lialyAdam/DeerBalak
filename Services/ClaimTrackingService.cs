using Deerbalak.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Deerbalak.Data.Services
{
    /// <summary>
    /// Service for tracking and analyzing claim spread across the platform.
    /// Identifies similar claims, counts occurrences, and tracks spread patterns.
    /// </summary>
    public class ClaimTrackingService
    {
        private readonly AppDbContext _context;
            private readonly ISimilarityService _similarityService;

        private readonly TextProcessor _textProcessor;
        private readonly ILogger<ClaimTrackingService> _logger;


public ClaimTrackingService(
    AppDbContext context,
    ISimilarityService similarityService, // ✅ الحل
    TextProcessor textProcessor,
    ILogger<ClaimTrackingService> logger)
{
    _context = context;
    _similarityService = similarityService;
    _textProcessor = textProcessor;
    _logger = logger;
}


        /// <summary>
        /// Analyzes claim spread for a given post content.
        /// Finds all similar posts, counts occurrences and unique users.
        /// </summary>
        public async Task<ClaimTrackingResult> AnalyzeClaimSpreadAsync(string postContent, int currentPostId = 0)
        {
            if (string.IsNullOrWhiteSpace(postContent))
            {
                return GetEmptyResult();
            }

            try
            {
                _logger.LogInformation($"🔵 [ClaimTrackingService] Analyzing claim spread for content");

                // Get all posts from database (exclude current post and deleted posts)
                var allPosts = await _context.Posts
                    .Where(p => !p.IsDeleted && p.Id != currentPostId)
                    .Include(p => p.User)
                    .ToListAsync();

                if (allPosts.Count == 0)
                {
                    _logger.LogInformation("No similar posts found");
                    return GetEmptyResult();
                }

                // Find similar posts using similarity service
                var similarPosts = new List<Post>();
                var similarityScores = new Dictionary<int, double>();

                foreach (var post in allPosts)
                {
var similarity = _similarityService.CalculateSimilarity(postContent, post.Content) * 100;                    
                    // Consider posts with similarity >= 60% as similar claims
                    if (similarity >= 60)
                    {
                        similarPosts.Add(post);
                        similarityScores[post.Id] = similarity;
                    }
                }

                if (similarPosts.Count == 0)
                {
                    _logger.LogInformation("No similar posts found");
                    return GetEmptyResult();
                }

                // Calculate claim tracking metrics
                int appearedCount = similarPosts.Count + 1; // Including current post
                int uniqueUsersCount = similarPosts.Select(p => p.UserId).Distinct().Count() + 1; // Including current user
                var firstSeen = similarPosts.Min(p => p.DateCreated);
                var spreadLevel = DetermineSpreadLevel(appearedCount);
                var highestSimilarity = similarityScores.Values.Max();

                _logger.LogInformation($"✅ [ClaimTrackingService] Found {similarPosts.Count} similar posts, spread level: {spreadLevel}");

                return new ClaimTrackingResult
                {
                    IsRepeatedClaim = true,
                    SimilarClaimsCount = similarPosts.Count,
                    AppearedCount = appearedCount,
                    UniqueUsersCount = uniqueUsersCount,
                    FirstSeenDate = firstSeen,
                    SpreadLevel = spreadLevel,
                    HighestSimilarity = Math.Round(highestSimilarity, 2),
                    SimilarPostIds = similarPosts.Select(p => p.Id).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [ClaimTrackingService] Error analyzing claim spread");
                return GetEmptyResult();
            }
        }

        /// <summary>
        /// Updates post claim tracking fields in database.
        /// </summary>
        public async Task UpdatePostClaimTrackingAsync(int postId, ClaimTrackingResult trackingResult)
        {
            try
            {
                var post = await _context.Posts.FindAsync(postId);
                if (post == null)
                {
                    _logger.LogWarning($"Post {postId} not found");
                    return;
                }

                post.AppearedCount = trackingResult.AppearedCount;
                post.UniqueUsersCount = trackingResult.UniqueUsersCount;
                post.FirstSeen = trackingResult.FirstSeenDate;

                _context.Posts.Update(post);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ [ClaimTrackingService] Updated claim tracking for post {postId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ [ClaimTrackingService] Error updating claim tracking for post {postId}");
            }
        }

        /// <summary>
        /// Gets human-readable spread level based on occurrence count.
        /// </summary>
        private string DetermineSpreadLevel(int count)
        {
            return count switch
            {
                <= 2 => "LOW",
                <= 5 => "MEDIUM",
                <= 10 => "HIGH",
                _ => "VIRAL"
            };
        }

        private ClaimTrackingResult GetEmptyResult()
        {
            return new ClaimTrackingResult
            {
                IsRepeatedClaim = false,
                SimilarClaimsCount = 0,
                AppearedCount = 1,
                UniqueUsersCount = 1,
                FirstSeenDate = DateTime.UtcNow,
                SpreadLevel = "LOW",
                HighestSimilarity = 0,
                SimilarPostIds = new List<int>()
            };
        }
    }

    /// <summary>
    /// Result of claim tracking analysis.
    /// </summary>
    public class ClaimTrackingResult
    {
        public bool IsRepeatedClaim { get; set; }
        public int SimilarClaimsCount { get; set; }
        public int AppearedCount { get; set; }
        public int UniqueUsersCount { get; set; }
        public DateTime FirstSeenDate { get; set; }
        public required string SpreadLevel { get; set; }
        public double HighestSimilarity { get; set; }
        public List<int> SimilarPostIds { get; set; } = new List<int>();
    }
}
