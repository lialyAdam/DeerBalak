using Deerbalak.Data.Models;
using Deerbalak.Data;

namespace DeerBalak.ViewModels.Users
{
    public class GetUserProfileVM
    {
        public User User { get; set; }
        public List<Post> Posts { get; set; }
    }
}
