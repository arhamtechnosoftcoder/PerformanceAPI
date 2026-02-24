using RestaurantPerformanceApi.Models;

namespace RestaurantPerformance.API.Models;

public class DummyDataRoot
{
    public List<Order> Orders { get; set; }
    public List<Customer> Customers { get; set; }
}
