using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.ML.Transforms.TimeSeries;
using RestaurantPerformance.API.Helpers;
using RestaurantPerformanceApi.Models;
using System.Text;

namespace RestaurantPerformanceApi.Services;

public class MLService
{
    private readonly MLContext _mlContext = new MLContext();

    // 1. Sales Forecasting (SSA Time Series)
    public Forecast GetSalesForecast(List<Order> historicalOrders)
    {
        if (historicalOrders == null || historicalOrders.Count == 0)
        {
            return CreateEmptyForecast();
        }

        var dailySales = historicalOrders
            .GroupBy(o => o.Date.Date)
            .Select(g => new SalesData
            {
                Date = g.Key,
                Sales = (float)g.Sum(o => o.NetAmount),
                DayOfWeek = (float)g.Key.DayOfWeek,
                IsWeekend = g.Key.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1f : 0f,
            })
            .OrderBy(x => x.Date)
            .ToList();

        if (dailySales.Count < 30)
        {
            return CreateEmptyForecast("Insufficient historical data (< 30 days)");
        }

        IDataView dataView = _mlContext.Data.LoadFromEnumerable(dailySales);

        const int windowSize = 7;
        const int seasonalityWindow = 30;
        int seriesLength = Math.Min(365, dailySales.Count);
        int trainSize = dailySales.Count;

        var pipeline = _mlContext.Forecasting.ForecastBySsa(
            outputColumnName: nameof(SalesPrediction.ForecastedSales),
            inputColumnName: nameof(SalesData.Sales),
            windowSize: windowSize,
            seriesLength: seriesLength,
            trainSize: trainSize,
            horizon: 7,
            confidenceLevel: 0.95f,
            confidenceLowerBoundColumn: nameof(SalesPrediction.LowerBoundSales),
            confidenceUpperBoundColumn: nameof(SalesPrediction.UpperBoundSales),
            isAdaptive: false,
            discountFactor: 1f,
            rankSelectionMethod: RankSelectionMethod.Exact,
            shouldStabilize: true
        );

        ITransformer model = pipeline.Fit(dataView);
        var engine = model.CreateTimeSeriesEngine<SalesData, SalesPrediction>(_mlContext);
        var forecast = engine.Predict(7);

        var lastKnownDate = dailySales.Max(d => d.Date);
        var next7Days = new List<DailyPrediction>(7);

        double avgHistoricalOrderValue = historicalOrders.Any()
            ? historicalOrders.Average(o => o.NetAmount)
            : 00;

        for (int i = 0; i < 7; i++)
        {
            var predDate = lastKnownDate.AddDays(i + 1);

            float predicted = forecast.ForecastedSales[i];
            float lower = forecast.LowerBoundSales?[i] ?? predicted * 0.7f;
            float upper = forecast.UpperBoundSales?[i] ?? predicted * 1.3f;

            predicted = Math.Max(0f, predicted);
            lower = Math.Max(0f, lower);
            upper = Math.Max(0f, upper);

            next7Days.Add(new DailyPrediction
            {
                Date = predDate,
                PredictedSales = predicted,
                LowerBoundSales = lower,
                UpperBoundSales = upper
            });
        }

        double nextWeekTotal = next7Days.Sum(p => (double)p.PredictedSales);

        int expectedOrders = avgHistoricalOrderValue > 0
            ? (int)Math.Round(nextWeekTotal / avgHistoricalOrderValue)
            : 0;

        double avgWidth = next7Days.Average(p => (double)(p.UpperBoundSales - p.LowerBoundSales));
        double confidenceProxy = Math.Clamp(0.95 - (avgWidth / (double)nextWeekTotal * 10), 0.5, 0.95);

        return new Forecast
        {
            Next7DaysSales = next7Days,
            NextWeekRevenue = (double)nextWeekTotal,
            ExpectedOrderVolume = expectedOrders,
            Confidence = confidenceProxy
        };
    }

    private Forecast CreateEmptyForecast(string note = null)
    {
        return new Forecast
        {
            Next7DaysSales = new List<DailyPrediction>(),
            NextWeekRevenue = 0,
            ExpectedOrderVolume = 0,
            Confidence = 0.0,
        };
    }

    public class SalesData
    {
        public DateTime Date { get; set; }
        public float Sales { get; set; }
        public float DayOfWeek { get; set; }
        public float IsWeekend { get; set; }
    }

    public class SalesPrediction
    {
        public float[] ForecastedSales { get; set; }
        public float[] LowerBoundSales { get; set; }
        public float[] UpperBoundSales { get; set; }
    }


    // 2. Anomaly Detection (SSA)
    public List<Anomaly> DetectAnomalies(List<Order> orders)
    {
        if (orders == null || orders.Count == 0)
        {
            return new List<Anomaly>();
        }

        var dailySales = orders
            .GroupBy(o => o.Date.Date)
            .Select(g => new SalesDataPoint
            {
                Date = g.Key,
                Sales = (float)g.Sum(o => o.NetAmount)
            })
            .OrderBy(x => x.Date)
            .ToList();

        if (dailySales.Count < 30)
        {
            return new List<Anomaly>();
        }

        IDataView dataView = _mlContext.Data.LoadFromEnumerable(dailySales);

        var detector = _mlContext.Transforms.DetectSpikeBySsa(
            outputColumnName: "AnomalyPrediction",
            inputColumnName: nameof(SalesDataPoint.Sales),
            confidence: 98.0,
            pvalueHistoryLength: 50,
            trainingWindowSize: Math.Min(365, dailySales.Count),
            seasonalityWindowSize: 7);

        var model = detector.Fit(dataView);
        var transformed = model.Transform(dataView);

        var predictionRows = _mlContext.Data.CreateEnumerable<SpikePredictionRow>(transformed, reuseRowObject: false)
            .ToList();

        var anomalies = new List<Anomaly>();

        for (int i = 0; i < predictionRows.Count; i++)
        {
            var row = predictionRows[i];
            if (row.AnomalyPrediction?.Length < 3)
            {
                continue;
            }

            double[] prediction = row.AnomalyPrediction;
            bool isAnomaly = prediction[0] > 0;
            double pValue = prediction[1];
            double martingaleScore = prediction[2];

            if (!isAnomaly)
            {
                continue;
            }

            var currentDay = dailySales[i];
            var salesToday = (double)currentDay.Sales;

            double lookbackAvg = i >= 14
                ? dailySales.Skip(Math.Max(0, i - 14)).Take(14).Average(d => d.Sales)
                : salesToday;

            double percentDeviation = lookbackAvg > 0
                ? ((salesToday - lookbackAvg) / lookbackAvg) * 100
                : 0;

            string severity = percentDeviation switch
            {
                < -35 => "major drop",
                < -20 => "significant drop",
                > 50 => "major spike",
                > 30 => "significant spike",
                _ => "unusual movement"
            };

            string description = $"Anomaly detected on {currentDay.Date:yyyy-MM-dd}: {severity} " +
                                $"({percentDeviation:F1}%, martingale score: {martingaleScore:F2}, p-value: {pValue:F4})";

            if (Math.Abs(percentDeviation) > 40)
            {
                description += " – requires immediate review";
            }

            anomalies.Add(new Anomaly
            {
                Description = description,
                Date = currentDay.Date,
                Score = martingaleScore,
                PercentDeviation = percentDeviation
            });
        }

        return anomalies
            .OrderByDescending(a => Math.Abs(a.PercentDeviation))
            .ThenByDescending(a => a.Score)
            .ToList();
    }

    

    public class SpikePredictionRow
    {
        [VectorType(3)]
        public double[] AnomalyPrediction { get; set; }
    }

    public class SalesDataPoint
    {
        public DateTime Date { get; set; }
        public float Sales { get; set; }
    }

    // 3. Customer Segmentation (KMeans)
    public List<CustomerSegment> SegmentCustomers(List<Customer> customers)
    {
        if (customers == null || customers.Count < 10)
        {
            return new List<CustomerSegment>();
        }

        var validCustomers = customers
            .Where(c => c.AvgSpend >= 0 && c.VisitFrequency >= 0 && c.DiscountUsage >= 0)
            .ToList();

        if (validCustomers.Count < 10)
        {
            return new List<CustomerSegment>();
        }

        var data = validCustomers.Select(c => new CustomerData
        {
            AvgSpend = (float)MathExtensions.Log1p(c.AvgSpend),
            VisitFrequency = (float)MathExtensions.Log1p(c.VisitFrequency),
            DiscountUsage = (float)MathExtensions.Log1p(c.DiscountUsage + 1)
        }).ToList();

        IDataView dataView = _mlContext.Data.LoadFromEnumerable(data);

        var pipeline = _mlContext.Transforms
             .Concatenate("Features",
                 nameof(CustomerData.AvgSpend),
                 nameof(CustomerData.VisitFrequency),
                 nameof(CustomerData.DiscountUsage))
             .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
             .Append(_mlContext.Clustering.Trainers.KMeans(new KMeansTrainer.Options
             {
                 FeatureColumnName = "Features",
                 NumberOfClusters = 5,
                 MaximumNumberOfIterations = 300,
                 OptimizationTolerance = 1e-6f,
                 InitializationAlgorithm = KMeansTrainer.InitializationAlgorithm.KMeansPlusPlus,
                 NumberOfThreads = 1,
             }));

        var model = pipeline.Fit(dataView);
        var transformed = model.Transform(dataView);

        var predictions = _mlContext.Data.CreateEnumerable<CustomerPrediction>(transformed, reuseRowObject: false)
            .ToList();

        var clusterStats = predictions
            .GroupBy(p => p.PredictedLabel)
            .Select(g => new
            {
                ClusterId = g.Key,
                Count = g.Count(),
                AvgSpend = g.Average(p => validCustomers[predictions.IndexOf(p)].AvgSpend),
                AvgFrequency = g.Average(p => validCustomers[predictions.IndexOf(p)].VisitFrequency),
                AvgDiscount = g.Average(p => validCustomers[predictions.IndexOf(p)].DiscountUsage)
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        var segments = new List<CustomerSegment>();

        foreach (var stat in clusterStats)
        {
            string name = DetermineSegmentName(
                stat.AvgSpend,
                stat.AvgFrequency,
                stat.AvgDiscount,
                stat.Count,
                validCustomers.Count
            );

            segments.Add(new CustomerSegment
            {
                SegmentName = name,
                Count = stat.Count,
                Percentage = Math.Round((double)stat.Count / validCustomers.Count * 100, 1),
                AvgSpend = Math.Round(stat.AvgSpend, 2),
                AvgVisitFrequency = Math.Round(stat.AvgFrequency, 1),
                AvgDiscountUsage = Math.Round(stat.AvgDiscount, 2)
            });
        }

        return segments
            .OrderByDescending(s => s.Percentage)
            .ToList();
    }

    private string DetermineSegmentName(double avgSpend, double avgFreq, double avgDiscount, int count, int total)
    {
        double pct = (double)count / total;

        if (avgSpend > 1500 && avgFreq > 4 && avgDiscount < 200)
            return "High-Value Loyal";

        if (avgSpend > 800 && avgFreq > 6)
            return "Frequent High-Spenders";

        if (avgFreq > 5 && avgSpend < 400)
            return "Frequent Low-Spend";

        if (avgDiscount > 500 && avgSpend < 600)
            return "Discount Seekers";

        if (avgFreq <= 1.5 && avgSpend < 300)
            return "One-Time / Casual";

        if (avgSpend > 1200)
            return "Premium Customers";

        if (avgFreq > 3)
            return "Regular Visitors";

        return "Standard / Mixed";
    }


    private class CustomerData
    {
        public float AvgSpend { get; set; }
        public float VisitFrequency { get; set; }
        public float DiscountUsage { get; set; }

        [VectorType(3)]
        public float[] Features => new[] { AvgSpend, VisitFrequency, DiscountUsage };
    }

    private class CustomerPrediction
    {
        public uint PredictedLabel { get; set; }
    }

    // 4. Predict Item Demand

    public List<ItemPrediction> PredictItemDemand(List<Order> historicalOrders)
    {
        if (historicalOrders?.Any() != true)
        {
            return new List<ItemPrediction>();
        }

        var cutoffDate = DateTime.UtcNow.Date.AddDays(-90); // ignore very old data for short-term prediction

        var itemTransactions = historicalOrders
            .Where(o => o.Date.Date >= cutoffDate)
            .SelectMany(o => o.Items, (o, item) => new ItemTransaction
            {
                ItemName = item.ItemName?.Trim(),
                Quantity = item.Quantity,
                OrderDate = o.Date.Date,
                OrderId = o.Id // optional – helps debugging duplicates
            })
            .Where(t => t.ItemName != null && t.Quantity > 0)
            .ToList();

        if (!itemTransactions.Any())
        {
            return new List<ItemPrediction>();
        }

        var grouped = itemTransactions
            .GroupBy(t => t.ItemName, StringComparer.OrdinalIgnoreCase);

        var predictions = new List<ItemPrediction>(grouped.Count());

        const int recentLookbackDays = 14;
        const int minRequiredDays = 5;
        const double trendWeight = 0.6; // blend recent trend + longer average

        foreach (var group in grouped)
        {
            var itemName = group.Key;

            var dailySales = group
                .GroupBy(t => t.OrderDate)
                .Select(g => new
                {
                    Date = g.Key,
                    TotalQty = g.Sum(t => t.Quantity)
                })
                .OrderByDescending(d => d.Date)
                .ToList();

            if (dailySales.Count < minRequiredDays)
            {
                continue;
            }

            // Recent period (stronger weight)
            var recent = dailySales.Take(recentLookbackDays).ToList();
            double recentAvg = recent.Any() ? recent.Average(d => d.TotalQty) : 0;

            // Longer-term average (more stable)
            double longTermAvg = dailySales.Average(d => d.TotalQty);

            // Weighted average – favors recent behavior
            double blendedDaily = (recentAvg * trendWeight) + (longTermAvg * (1 - trendWeight));

            // Optional: simple linear trend adjustment (if enough points)
            double trendAdjustment = 1.0;
            if (dailySales.Count >= 10)
            {
                var first = dailySales.Last();  // oldest in recent window
                var last = dailySales.First();  // newest
                double daysDiff = (last.Date - first.Date).TotalDays;
                if (daysDiff > 3)
                {
                    double qtyDiff = last.TotalQty - first.TotalQty;
                    trendAdjustment = 1.0 + (qtyDiff / first.TotalQty) * 0.15; // modest trend boost
                    trendAdjustment = Math.Clamp(trendAdjustment, 0.7, 1.4);
                }
            }

            double predictedDaily = blendedDaily * trendAdjustment;
            int predictedNextWeek = (int)Math.Round(predictedDaily * 7);

            // Safety bounds
            predictedNextWeek = Math.Max(0, predictedNextWeek);

            predictions.Add(new ItemPrediction
            {
                ItemName = itemName,
                PredictedQuantity = predictedNextWeek,
                Confidence = CalculateItemConfidence(dailySales.Count, recent.Count, trendAdjustment)
            });
        }

        return predictions
            .Where(p => p.PredictedQuantity > 0)
            .OrderByDescending(p => p.PredictedQuantity)
            .ThenBy(p => p.ItemName)
            .ToList();
    }

    private double CalculateItemConfidence(int totalDays, int recentDays, double trendFactor)
    {
        double baseConf = Math.Min(1.0, totalDays / 30.0);
        double recencyBoost = recentDays / 14.0;
        double stability = Math.Abs(trendFactor - 1.0) < 0.2 ? 1.0 : 0.8;

        return Math.Round(baseConf * recencyBoost * stability * 0.95, 2);
    }

    private class ItemTransaction
    {
        public string ItemName { get; set; }
        public int Quantity { get; set; }
        public DateTime OrderDate { get; set; }
        public int? OrderId { get; set; }
    }


    // 5. Calculate Health Score
    public HealthScore CalculateHealthScore(
        WeeklyComparison comparison,
        List<Anomaly> anomalies,
        CoreMetrics currentMetrics,
        CoreMetrics previousMetrics,
        List<CustomerSegment> customerSegments,
        List<Order> currentOrders)
    {
        int score = 100;

        if (comparison.SalesGrowthPercentage < -10) score -= 25;
        else if (comparison.SalesGrowthPercentage < 0) score -= 12;

        if (comparison.OrderGrowthPercentage < -10) score -= 15;
        else if (comparison.OrderGrowthPercentage < 0) score -= 8;

        if (comparison.AovChangePercentage < -15) score -= 10;

        double cancelRateCurrent = currentMetrics.Sales.TotalOrders > 0
            ? (double)currentMetrics.Operations.CancelledOrders / currentMetrics.Sales.TotalOrders * 100
            : 0;

        double cancelRatePrev = previousMetrics.Sales.TotalOrders > 0
            ? (double)previousMetrics.Operations.CancelledOrders / previousMetrics.Sales.TotalOrders * 100
            : 0;

        double cancelChange = cancelRateCurrent - cancelRatePrev;

        if (cancelRateCurrent > 8 || cancelChange > 3) score -= 18;
        else if (cancelChange > 1.5) score -= 9;

        if (anomalies?.Any(a => a.Description.Contains("DROP", StringComparison.OrdinalIgnoreCase)) == true) score -= 15;

        if (anomalies?.Count >= 2) score -= 12 * (anomalies.Count - 1);

        var highValueSegment = customerSegments?.FirstOrDefault(s => s.SegmentName.Contains("High Value", StringComparison.OrdinalIgnoreCase));
        var frequentSegment = customerSegments?.FirstOrDefault(s => s.SegmentName.Contains("Frequent", StringComparison.OrdinalIgnoreCase));

        int highValueCount = highValueSegment?.Count ?? 0;
        int frequentCount = frequentSegment?.Count ?? 0;

        if (highValueCount < 10 || frequentCount < 20) score -= 20;

        if (currentOrders?.Any() == true)
        {
            var uniqueCustomers = currentOrders.Select(o => o.CustomerId).Distinct().Count();
            if (uniqueCustomers > 0)
            {
                var repeatCount = currentOrders
                    .GroupBy(o => o.CustomerId)
                    .Count(g => g.Count() > 1);

                double repeatRate = (double)repeatCount / uniqueCustomers * 100;

                if (repeatRate < 25) score -= 18;
                else if (repeatRate < 40) score -= 10;
            }
        }

        score = Math.Clamp(score, 0, 100);

        string riskLevel = score switch
        {
            >= 85 => "Low",
            >= 65 => "Medium",
            >= 40 => "High",
            _ => "Critical"
        };

        string recommendation = riskLevel switch
        {
            "Low" => "Maintain current strategy — solid performance.",
            "Medium" => "Monitor closely — review cancellations and weekday sales trends.",
            "High" => "Immediate action needed — investigate sales drop, cancellations, and customer retention urgently.",
            "Critical" => "High risk — urgent review of operations, menu, pricing, and customer feedback required to prevent further decline.",
            _ => "Unknown state"
        };

        if (anomalies?.Any() == true)
        {
            recommendation += $" Anomalies detected: {anomalies.Count}.";
        }

        return new HealthScore
        {
            Score = score,
            RiskLevel = riskLevel,
            Recommendation = recommendation
        };
    }


    // 6. Generate AI Summary
    public string GenerateAiSummary(
        CoreMetrics metrics,
        WeeklyComparison comparison,
        Forecast forecast,
        List<Anomaly> anomalies,
        HealthScore healthScore,
        List<ItemPrediction> topItemPredictions = null)
    {
        var sb = new StringBuilder();

        // ────────────────────────────────
        // Opening – Overall performance tone
        // ────────────────────────────────
        double salesChange = comparison.SalesGrowthPercentage;

        if (salesChange > 15)
            sb.Append("Strong week! ");
        else if (salesChange > 5)
            sb.Append("Solid performance this week. ");
        else if (salesChange > -5)
            sb.Append("Steady results this week. ");
        else if (salesChange > -15)
            sb.Append("Challenging week — ");
        else
            sb.Append("Concerning drop in performance — ");

        sb.Append($"Sales {FormatChange(salesChange)} compared to last week ");

        if (Math.Abs(salesChange) < 2)
            sb.Append("(almost flat). ");
        else
            sb.Append(". ");

        // ────────────────────────────────
        // Key highlights – ordered by importance
        // ────────────────────────────────
        // Peak performance
        if (!string.IsNullOrWhiteSpace(metrics.Operations.PeakDay) &&
            !string.IsNullOrWhiteSpace(metrics.Operations.PeakHour))
        {
            sb.Append($"Peak traffic occurred on **{metrics.Operations.PeakDay}** during **{metrics.Operations.PeakHour}**. ");
        }

        // Cancellation trend
        double cancelChange = comparison.CancelRateChangePercentage;
        if (Math.Abs(cancelChange) > 1.5)
        {
            string direction = cancelChange < 0 ? "dropped" : "rose";
            string implication = cancelChange < -2 ? "clear operational improvement" :
                                 cancelChange < 0 ? "positive movement in operations" :
                                 cancelChange > 5 ? "significant concern — review urgently" :
                                                     "slight increase — keep monitoring";

            sb.Append($"Cancellation rate {direction} by {Math.Abs(cancelChange):F1}%, indicating {implication}. ");
        }

        // AOV / Order volume change
        if (Math.Abs(comparison.AovChangePercentage) > 8)
        {
            string aovDir = comparison.AovChangePercentage > 0 ? "improved" : "declined";
            sb.Append($"Average order value {aovDir} by {Math.Abs(comparison.AovChangePercentage):F1}%. ");
        }

        // Anomalies – if any serious ones
        if (anomalies?.Any() == true)
        {
            var serious = anomalies
                .Where(a => a.Description.Contains("DROP", StringComparison.OrdinalIgnoreCase) ||
                            a.Description.Contains("spike", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (serious.Any())
            {
                sb.Append($"Alert: {serious.Count} unusual pattern{(serious.Count > 1 ? "s" : "")} detected ");
                sb.Append($"({string.Join(", ", serious.Take(2).Select(a => a.Description.Split(" — ").FirstOrDefault() ?? a.Description))}). ");
            }
        }

        // Health score callout
        if (healthScore != null)
        {
            string tone = healthScore.Score >= 85 ? "healthy" :
                          healthScore.Score >= 65 ? "stable but watch closely" :
                          healthScore.Score >= 40 ? "at risk" : "critical";

            sb.Append($"Overall outlet health: **{healthScore.Score}/100** ({tone}). ");
        }

        // ────────────────────────────────
        // Forward-looking – forecast
        // ────────────────────────────────
        if (forecast?.NextWeekRevenue > 0)
        {
            string predTone = forecast.NextWeekRevenue > metrics.Sales.NetSales * 1.1 ? "optimistic" :
                              forecast.NextWeekRevenue > metrics.Sales.NetSales * 0.9 ? "realistic" : "cautious";

            sb.Append($"Next week revenue forecast: **{forecast.NextWeekRevenue:N0}** ({predTone}). ");
        }

        // Top item demand hint (if provided)
        if (topItemPredictions?.Any() == true)
        {
            var leaders = topItemPredictions.Take(3)
                .Where(p => p.PredictedQuantity > 0)
                .Select(p => $"{p.ItemName} ({p.PredictedQuantity})");

            if (leaders.Any())
            {
                sb.Append($"Watch: {string.Join(", ", leaders)} expected to lead demand next week. ");
            }
        }

        // ────────────────────────────────
        // Closing remark
        // ────────────────────────────────
        if (healthScore?.RiskLevel == "Low")
            sb.Append("Keep up the momentum!");
        else if (healthScore?.RiskLevel == "Medium")
            sb.Append("Focus on weak days and cancellation root causes.");
        else
            sb.Append("Urgent attention recommended — review operations, menu, and customer feedback.");

        return sb.ToString().TrimEnd('.', ' ') + ".";
    }

    // Helper for clean ± formatting
    private static string FormatChange(double value)
    {
        if (Math.Abs(value) < 0.1) return "remained stable";
        string sign = value >= 0 ? "grew" : "declined";
        return $"{sign} by {Math.Abs(value):F1}%";
    }
}