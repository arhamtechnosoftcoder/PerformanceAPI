namespace RestaurantPerformance.API.Models;

public class PerformanceRequest
{
    public int OutletId { get; set; }
    public DateTime WeekDate { get; set; }  // e.g., start of the week
}
