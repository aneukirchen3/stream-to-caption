using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PodtextCaption.Web.Pages;

public class ListenModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    public void OnGet(string id)
    {
        Id = id;
    }
}
