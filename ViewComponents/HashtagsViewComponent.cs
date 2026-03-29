using Deerbalak.Data.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Deerbalak.ViewComponents
{
    // Temporary safe implementation: returns an empty list so no DB access happens.
    public class HashtagsViewComponent : ViewComponent
    {
        public HashtagsViewComponent()
        {
        }

        public Task<IViewComponentResult> InvokeAsync()
        {
            var top3Hashtags = new List<Hashtag>(); // empty to avoid DB queries
            return Task.FromResult<IViewComponentResult>(View(top3Hashtags));
        }
    }
}