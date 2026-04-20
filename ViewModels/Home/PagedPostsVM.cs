using Deerbalak.Data.Models;

namespace DeerBalak.ViewModels.Home
{
    public class PagedPostsVM
    {
        public List<Post> Posts { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPosts { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalPosts / PageSize);
    }
}