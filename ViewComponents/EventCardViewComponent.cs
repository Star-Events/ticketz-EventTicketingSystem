using Microsoft.AspNetCore.Mvc;
using EventTicketingSystem.Models;

namespace EventTicketingSystem.ViewComponents
{
    public class EventCardViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(EventCardVm model)
        {
            // you can add conditional logic here (e.g., Sold Out badge)
            return View(model);
        }
    }
}
