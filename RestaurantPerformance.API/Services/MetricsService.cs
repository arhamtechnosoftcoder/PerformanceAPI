using RestaurantPerformanceApi.Models;

namespace RestaurantPerformanceApi.Services;

public class MetricsService
{
    public CoreMetrics CalculateCoreMetrics(List<Order> orders)
    {
        var metrics = new CoreMetrics
        {
            Sales = new SalesMetrics
            {
                TotalSalesGross = orders.Sum(o => o.TotalAmount),
                NetSales = orders.Sum(o => o.NetAmount),
                TotalOrders = orders.Count,
                AverageOrderValue = orders.Any() ? orders.Average(o => o.NetAmount) : 0,
                TotalDiscount = orders.Sum(o => o.Discount),
                TotalTax = orders.Sum(o => o.Tax)
            },
            Operations = new OperationsMetrics
            {
                CancelledOrders = orders.Count(o => o.Status == "Cancelled"),
                RefundAmount = orders.Sum(o => o.RefundAmount),
                AvgOrderProcessingTime = TimeSpan.FromSeconds(orders.Average(o => o.ProcessingTime.TotalSeconds)),
                PeakHour = CalculatePeakHour(orders),  
                PeakDay = CalculatePeakDay(orders) 
            },
            ProductPerformance = new ProductPerformance
            {
                Top5ByRevenue = orders.SelectMany(o => o.Items).GroupBy(i => i.ItemName)
                    .Select(g => new TopItem { ItemName = g.Key, Value = g.Sum(i => i.Revenue) })
                    .OrderByDescending(t => t.Value).Take(5).ToList(),
                Top5ByQuantity = orders.SelectMany(o => o.Items).GroupBy(i => i.ItemName)
                    .Select(g => new TopItem { ItemName = g.Key, Value = g.Sum(i => i.Quantity) })
                    .OrderByDescending(t => t.Value).Take(5).ToList(),
                WorstPerformingItems = orders.SelectMany(o => o.Items).GroupBy(i => i.ItemName)
                    .Select(g => new TopItem { ItemName = g.Key, Value = g.Sum(i => i.Revenue) })
                    .OrderBy(t => t.Value).Take(5).ToList() 
            },
            PaymentBreakdown = CalculatePaymentBreakdown(orders)
        };
        return metrics;
    }

    private PaymentBreakdown CalculatePaymentBreakdown(List<Order> orders)
    {
        if (orders == null || !orders.Any())
        {
            return new PaymentBreakdown
            {
                CashPercentage = 0,
                UpiPercentage = 0,
                CardPercentage = 0,
                DigitalPercentage = 0
            };
        }

        double totalNetAmount = (double)orders.Sum(o => o.NetAmount);

        if (totalNetAmount <= 0)
        {
            return new PaymentBreakdown
            {
                CashPercentage = 0,
                UpiPercentage = 0,
                CardPercentage = 0,
                DigitalPercentage = 0
            };
        }

        var paymentTotals = orders
            .GroupBy(o => o.PaymentMethod?.Trim().ToUpperInvariant() ?? "Unknown")
            .ToDictionary(
                g => g.Key,
                g => g.Sum(o => o.NetAmount)
            );

        static double GetPercentage(double amount, double total) =>
            total > 0 ? Math.Round((amount / total) * 100, 2) : 00;

        return new PaymentBreakdown
        {
            CashPercentage = GetPercentage(paymentTotals.GetValueOrDefault("CASH", 0), totalNetAmount),
            UpiPercentage = GetPercentage(paymentTotals.GetValueOrDefault("UPI", 0), totalNetAmount),
            CardPercentage = GetPercentage(paymentTotals.GetValueOrDefault("CARD", 0), totalNetAmount),
            DigitalPercentage = GetPercentage(paymentTotals.GetValueOrDefault("DIGITAL", 0), totalNetAmount)
        };
    }

    private string CalculatePeakHour(List<Order> orders)
    {
        var peak = orders.GroupBy(o => o.Date.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).FirstOrDefault();
        return peak != null ? $"{peak.Hour}-{peak.Hour + 1} {(peak.Hour < 12 ? "AM" : "PM")}" : "N/A";
    }

    private string CalculatePeakDay(List<Order> orders)
    {
        var peak = orders.GroupBy(o => o.Date.DayOfWeek)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).FirstOrDefault();
        return peak?.Day.ToString() ?? "N/A";
    }

    public WeeklyComparison CalculateWeeklyComparison(CoreMetrics current, CoreMetrics previous)
    {
        if (current == null || previous == null)
        {
            return new WeeklyComparison
            {
                SalesGrowthPercentage = 0,
                OrderGrowthPercentage = 0,
                AovChangePercentage = 0,
                CancelRateChangePercentage = 0,
                CustomerGrowthPercentage = 0,
                PeakHourShift = "N/A"
            };
        }

        // Helper to safely calculate percentage change
        static double SafePctChange(double currentVal, double prevVal)
        {
            if (prevVal == 0) return currentVal > 0 ? 100.0 : 0.0; // avoid division by zero
            return ((currentVal - prevVal) / prevVal) * 100.0;
        }

        // ────────────────────────────────────────────────
        // Core percentage changes
        // ────────────────────────────────────────────────
        double salesGrowth = SafePctChange(current.Sales.NetSales, previous.Sales.NetSales);
        double orderGrowth = SafePctChange(current.Sales.TotalOrders, previous.Sales.TotalOrders);
        double aovChange = SafePctChange(current.Sales.AverageOrderValue, previous.Sales.AverageOrderValue);

        // Cancellation rate (percentage of total orders)
        double cancelRateCurrent = previous.Sales.TotalOrders > 0
            ? (double)current.Operations.CancelledOrders / current.Sales.TotalOrders * 100
            : 0;

        double cancelRatePrev = previous.Sales.TotalOrders > 0
            ? (double)previous.Operations.CancelledOrders / previous.Sales.TotalOrders * 100
            : 0;

        double cancelRateChange = cancelRateCurrent - cancelRatePrev;

        // Customer growth (very rough proxy — real growth needs historical customer data)
        // If you don't have actual new vs returning count, this can be skipped or approximated
        double customerGrowth = SafePctChange(
            current.Sales.TotalOrders,      // proxy using order volume change
            previous.Sales.TotalOrders
        );

        // ────────────────────────────────────────────────
        // Peak Hour Shift detection
        // ────────────────────────────────────────────────
        string peakHourShift = "No change";

        if (!string.IsNullOrWhiteSpace(current.Operations.PeakHour) &&
            !string.IsNullOrWhiteSpace(previous.Operations.PeakHour))
        {
            if (current.Operations.PeakHour != previous.Operations.PeakHour)
            {
                peakHourShift = $"Shifted from {previous.Operations.PeakHour} to {current.Operations.PeakHour}";
            }
        }

        // Optional: also consider PeakDay change
        if (!string.IsNullOrWhiteSpace(current.Operations.PeakDay) &&
            !string.IsNullOrWhiteSpace(previous.Operations.PeakDay) &&
            current.Operations.PeakDay != previous.Operations.PeakDay)
        {
            peakHourShift += $", Peak day moved to {current.Operations.PeakDay}";
        }

        return new WeeklyComparison
        {
            SalesGrowthPercentage = Math.Round(salesGrowth, 1),
            OrderGrowthPercentage = Math.Round(orderGrowth, 1),
            AovChangePercentage = Math.Round(aovChange, 1),
            CancelRateChangePercentage = Math.Round(cancelRateChange, 1),
            CustomerGrowthPercentage = Math.Round(customerGrowth, 1),
            PeakHourShift = peakHourShift
        };
    }
}