using Microsoft.AspNetCore.Mvc;
using RestaurantPerformance.API.Models;
using RestaurantPerformanceApi.Models;
using RestaurantPerformanceApi.Services;

namespace RestaurantPerformanceApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PerformanceController : ControllerBase
{
    private readonly DataService _dataService;
    private readonly MetricsService _metricsService;
    private readonly MLService _mlService;

    public PerformanceController(DataService dataService, MetricsService metricsService, MLService mlService)
    {
        _dataService = dataService;
        _metricsService = metricsService;
        _mlService = mlService;
    }

    [HttpPost("get_outlet_performance_detail")]
    public async Task<PerformanceResponse> GetPerformance([FromBody] PerformanceRequest request)
    {
        if (request == null || request.OutletId <= 0)
            return new PerformanceResponse(); 

        var currentStart = request.WeekDate.Date;
        var currentEnd = currentStart.AddDays(6).AddDays(1).AddTicks(-1);
        var prevStart = currentStart.AddDays(-7);
        var prevEnd = currentStart.AddDays(-1);

        var currentOrders = await _dataService.GetOrdersAsync(request.OutletId, currentStart, currentEnd);
        var prevOrders = await _dataService.GetOrdersAsync(request.OutletId, prevStart, prevEnd);
        var historicalOrders = await _dataService.GetHistoricalOrdersAsync(request.OutletId, DateTime.UtcNow.AddYears(-2));
        var customers = await _dataService.GetCustomersAsync(request.OutletId);

        // Core business metrics
        var currentMetrics = _metricsService.CalculateCoreMetrics(currentOrders);
        var prevMetrics = _metricsService.CalculateCoreMetrics(prevOrders ?? new List<Order>());
        var comparison = _metricsService.CalculateWeeklyComparison(currentMetrics, prevMetrics);

        // ML Insights
        var forecast = _mlService.GetSalesForecast(historicalOrders);
        var anomalies = _mlService.DetectAnomalies(currentOrders);
        var segments = _mlService.SegmentCustomers(customers);
        var itemPreds = _mlService.PredictItemDemand(historicalOrders);

        // Health score – now with required additional arguments
        var health = _mlService.CalculateHealthScore(
            comparison: comparison,
            anomalies: anomalies,
            currentMetrics: currentMetrics,
            previousMetrics: prevMetrics,
            customerSegments: segments,
            currentOrders: currentOrders
        );

        // AI-generated smart summary – now with full context
        var summary = _mlService.GenerateAiSummary(
            metrics: currentMetrics,
            comparison: comparison,
            forecast: forecast,
            anomalies: anomalies,
            healthScore: health,
            topItemPredictions: itemPreds?.OrderByDescending(p => p.PredictedQuantity).Take(5).ToList()
        );

        // Build final response
        return new PerformanceResponse
        {
            Metrics = currentMetrics,
            WeeklyComparison = comparison,
            MlInsights = new MlInsights
            {
                Forecast = forecast,
                Anomalies = anomalies,
                CustomerSegments = segments,
                ItemDemandPredictions = itemPreds,
                HealthScore = health,
                AiSummary = summary
            }
        };
    }
}