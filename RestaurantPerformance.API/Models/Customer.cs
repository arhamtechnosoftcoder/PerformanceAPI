namespace RestaurantPerformanceApi.Models;

public class Customer
{
    public int Id { get; set; }
    public double AvgSpend { get; set; }
    public int VisitFrequency { get; set; }  // e.g., visits per month
    public double DiscountUsage { get; set; }  // Total discount availed
}