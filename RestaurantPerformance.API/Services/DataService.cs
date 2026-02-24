using RestaurantPerformance.API.Models;
using RestaurantPerformanceApi.Models;
using System.Text.Json;

namespace RestaurantPerformanceApi.Services;

public class DataService
{
    private readonly IWebHostEnvironment _env;
    private readonly string _filePath;
    private DummyDataRoot _cachedData;

    public DataService(IWebHostEnvironment env)
    {
        _env = env;
        _filePath = Path.Combine(
            _env.ContentRootPath,
            "Data",
            "DummyData.json");

        LoadData();
    }

    private void LoadData()
    {
        var json = File.ReadAllText(_filePath);

        _cachedData = JsonSerializer.Deserialize<DummyDataRoot>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
    }

    public Task<List<Order>> GetOrdersAsync(int outletId, DateTime startDate, DateTime endDate)
    {
        var orders = _cachedData.Orders
            .Where(o => o.Date >= startDate && o.Date <= endDate)
            .ToList();

        return Task.FromResult(orders);
    }

    public Task<List<Customer>> GetCustomersAsync(int outletId)
    {
        return Task.FromResult(_cachedData.Customers);
    }

    public Task<List<Order>> GetHistoricalOrdersAsync(int outletId, DateTime fromDate)
    {
        var orders = _cachedData.Orders
            .Where(o => o.Date >= fromDate)
            .ToList();

        return Task.FromResult(orders);
    }
}