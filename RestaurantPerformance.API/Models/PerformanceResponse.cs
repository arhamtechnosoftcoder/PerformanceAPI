namespace RestaurantPerformanceApi.Models;

public class PerformanceResponse
{
    public CoreMetrics Metrics { get; set; }
    public WeeklyComparison WeeklyComparison { get; set; }
    public MlInsights MlInsights { get; set; }  // Renamed for C# convention
}

public class CoreMetrics
{
    public SalesMetrics Sales { get; set; }
    public OperationsMetrics Operations { get; set; }
    public ProductPerformance ProductPerformance { get; set; }
    public PaymentBreakdown PaymentBreakdown { get; set; }
}

public class SalesMetrics
{
    public double TotalSalesGross { get; set; }
    public double NetSales { get; set; }
    public int TotalOrders { get; set; }
    public double AverageOrderValue { get; set; }
    public double TotalDiscount { get; set; }
    public double TotalTax { get; set; }
}

public class OperationsMetrics
{
    public int CancelledOrders { get; set; }
    public double RefundAmount { get; set; }
    public TimeSpan AvgOrderProcessingTime { get; set; }
    public string PeakHour { get; set; }  // e.g., "8-9 PM"
    public string PeakDay { get; set; }  // e.g., "Saturday"
}

public class ProductPerformance
{
    public List<TopItem> Top5ByRevenue { get; set; }
    public List<TopItem> Top5ByQuantity { get; set; }
    public List<TopItem> WorstPerformingItems { get; set; }
}

public class TopItem
{
    public string ItemName { get; set; }
    public double Value { get; set; }  // Revenue or Quantity
}

public class PaymentBreakdown
{
    public double CashPercentage { get; set; }
    public double UpiPercentage { get; set; }
    public double CardPercentage { get; set; }
    public double DigitalPercentage { get; set; }
}

public class WeeklyComparison
{
    public double SalesGrowthPercentage { get; set; }
    public double OrderGrowthPercentage { get; set; }
    public double AovChangePercentage { get; set; }
    public double CancelRateChangePercentage { get; set; }
    public double CustomerGrowthPercentage { get; set; }
    public string PeakHourShift { get; set; }  // e.g., "Shifted from 7-8 PM to 8-9 PM"
}

public class MlInsights
{
    public Forecast Forecast { get; set; }
    public List<Anomaly> Anomalies { get; set; }
    public List<CustomerSegment> CustomerSegments { get; set; }
    public List<ItemPrediction> ItemDemandPredictions { get; set; }
    public HealthScore HealthScore { get; set; }
    public string AiSummary { get; set; }
}

public class DailyPrediction
{
    public DateTime Date { get; set; }
    public double PredictedSales { get; set; }
    public double LowerBoundSales { get; set; }
    public double UpperBoundSales { get; set; }
}

public class Forecast
{
    public List<DailyPrediction> Next7DaysSales { get; set; }
    public double NextWeekRevenue { get; set; }
    public int ExpectedOrderVolume { get; set; }
    public double Confidence { get; set; }
}

public class Anomaly
{
    public string Description { get; set; }
    public DateTime? Date { get; set; }
    public double Score { get; set; }
    public double PercentDeviation { get; set; }
}

public class CustomerSegment
{
    public string SegmentName { get; set; }
    public int Count { get; set; }
    public double Percentage { get; set; }
    public double AvgSpend { get; set; }
    public double AvgVisitFrequency { get; set; }
    public double AvgDiscountUsage { get; set; }
}

public class ItemPrediction
{
    public string ItemName { get; set; }
    public int PredictedQuantity { get; set; }
    public double Confidence { get; set; }
}

public class HealthScore
{
    public int Score { get; set; }  // e.g., 82
    public string RiskLevel { get; set; }  // "Low", "Medium", "High"
    public string Recommendation { get; set; }
}