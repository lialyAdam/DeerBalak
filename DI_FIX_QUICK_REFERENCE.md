# Quick Reference: Fixed Code Snippets

## 1. Program.cs - Corrected DI Registration

```csharp
// ✅ CORRECT - Use interfaces for dependency injection
builder.Services.AddSingleton<TextProcessor>();
builder.Services.AddSingleton<ISimilarityService, SimilarityService>();
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddScoped<IHybridDetector, HybridDetector>();
builder.Services.AddScoped<FakeNewsService>();
builder.Services.AddScoped<ClaimTrackingService>();

// Configure HttpClient for FakeNewsDetectionService
builder.Services
    .AddHttpClient<FakeNewsDetectionService>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });
```

## 2. PostsService - Corrected Constructor

```csharp
public class PostsService : IPostsService
{
    private readonly AppDbContext _context;
    private readonly INotificationsService _notificationService;
    private readonly IHybridDetector _hybridDetector;  // ✅ Use interface
    private readonly IFriendsService _friendsService;

    public PostsService(
        AppDbContext context,
        INotificationsService notificationService,
        IHybridDetector hybridDetector,  // ✅ Inject interface
        IFriendsService friendsService)
    {
        _context = context;
        _notificationService = notificationService;
        _hybridDetector = hybridDetector;
        _friendsService = friendsService;
    }
```

## 3. GetFeedAsync - Corrected Implementation

```csharp
public async Task<List<PostFeedDto>> GetFeedAsync(int loggedInUserId, int page, int pageSize)
{
    // ✅ Validate pagination parameters
    if (page < 1) page = 1;
    if (pageSize < 1 || pageSize > 100) pageSize = 10;

    // ✅ Get user's friends
    var friendships = await _friendsService.GetFriendsAsync(loggedInUserId);
    var friendIds = friendships
        .Select(f => f.SenderId == loggedInUserId ? f.ReceiverId : f.SenderId)
        .ToList();
    friendIds.Add(loggedInUserId);  // Include own posts

    // ✅ Query with proper filtering and pagination
    var posts = await _context.Posts
        .AsNoTracking()  // Performance
        .Where(p => friendIds.Contains(p.UserId)
            && (!p.IsPrivate || p.UserId == loggedInUserId)  // Privacy
            && p.Reports.Count < 5  // Quality control
            && !p.IsDeleted)
        .Include(p => p.User)
        .OrderByDescending(p => p.DateCreated)  // Newest first
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(p => new PostFeedDto
        {
            Id = p.Id,
            Content = p.Content,
            CreatedAt = p.DateCreated,
            User = new UserDto
            {
                Id = p.User.Id,
                UserName = p.User.UserName ?? string.Empty,
                FullName = p.User.FullName ?? string.Empty
            }
        })
        .ToListAsync();

    return posts;
}
```

## 4. HybridDetector - Corrected Constructor (Deerbalak.Data)

```csharp
public class HybridDetector : IHybridDetector
{
    private readonly FakeNewsService _fakeNewsService;
    private readonly TextProcessor _textProcessor;
    private readonly ISimilarityService _similarityService;  // ✅ Use interface
    private readonly IAIService? _aiService;  // ✅ Use interface (nullable)

    public HybridDetector(
        FakeNewsService fakeNewsService,
        TextProcessor textProcessor,
        ISimilarityService similarityService,  // ✅ Inject interface
        IAIService? aiService = null)  // ✅ Optional AI
    {
        _fakeNewsService = fakeNewsService;
        _textProcessor = textProcessor;
        _similarityService = similarityService;
        _aiService = aiService;
    }

    // ✅ Hybrid prediction: 70% AI + 30% Local
    public async Task<dynamic> PredictAsync(string text, DateTime dateCreated, int nrOfReports, string userName)
    {
        var localResult = Predict(text, dateCreated, nrOfReports, userName);
        if (_aiService == null)
            return localResult;

        var aiResult = await _aiService.AnalyzeTextAsync(text);
        var blendedScore = (int)Math.Round(aiResult.Score * 0.7 + localResult.score * 0.3);
        blendedScore = Math.Clamp(blendedScore, 0, 10);
        
        // Return blended results...
    }
}
```

## 5. SimilarityService - Corrected Implementation

```csharp
public class SimilarityService : ISimilarityService
{
    /// <summary>
    /// Calculates similarity between two texts using cosine similarity (0-1).
    /// </summary>
    public double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0;

        var words1 = text1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words2 = text2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var common = words1.Intersect(words2).Count();
        var total = Math.Sqrt(words1.Length * words2.Length);

        return total == 0 ? 0 : common / total;  // Returns 0-1, NOT 0-100
    }
}
```

## 6. AIService - Corrected Interface Implementation

```csharp
public class AIService : IAIService
{
    private readonly string? _apiKey;
    private readonly HttpClient _httpClient;
    private readonly bool _isEnabled;

    public AIService(IConfiguration configuration)
    {
        _apiKey = configuration["OpenAI:ApiKey"];
        _httpClient = new HttpClient();
        
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _isEnabled = false;
            Console.WriteLine("⚠️ OpenAI API key not configured. Using fallback.");
            return;
        }

        _isEnabled = true;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    // ✅ Implements IAIService
    public async Task<AIDetectionResult> AnalyzeTextAsync(string text)
    {
        if (!_isEnabled)
            return BuildFallback("API key not configured");

        // OpenAI API call with fallback...
    }
}
```

## 7. Interfaces Created in Deerbalak.Data.Services

### IAIService
```csharp
public interface IAIService
{
    Task<AIDetectionResult> AnalyzeTextAsync(string text);
}
```

### ISimilarityService
```csharp
public interface ISimilarityService
{
    double CalculateSimilarity(string text1, string text2);
}
```

### IHybridDetector
```csharp
public interface IHybridDetector
{
    Task<dynamic> PredictAsync(string content, DateTime dateCreated, int nrOfReports, string userName);
}
```

---

## Migration Checklist

- [ ] Update `Program.cs` with interface-based registrations
- [ ] Replace `HybridDetector` with `IHybridDetector` in PostsService
- [ ] Replace `AIService` with `IAIService` in HybridDetector
- [ ] Replace `SimilarityService` with `ISimilarityService` in HybridDetector
- [ ] Delete duplicate service files from main project (or replace with stubs)
- [ ] Test build: `dotnet build`
- [ ] Test DI resolution: Can projects start without errors?
- [ ] Test GetFeedAsync with various page/pageSize values
- [ ] Test HybridDetector with and without OpenAI key

---

## Common Errors & Solutions

| Error | Cause | Solution |
|-------|-------|----------|
| **CS0436** | Duplicate types | Remove duplicate from main project |
| **Unable to resolve service** | Wrong type registered | Use interface instead of concrete type |
| **NotImplementedException** | Missing implementation | Verify GetFeedAsync exists and is complete |
| **NullReferenceException in HybridDetector** | Missing dependency | Verify ISimilarityService registered |
| **401 Unauthorized (AI API)** | Missing/invalid key | Add OpenAI key to appsettings.json |

