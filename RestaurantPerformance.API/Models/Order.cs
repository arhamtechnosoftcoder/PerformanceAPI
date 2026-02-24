namespace RestaurantPerformanceApi.Models;

public class Order
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public double TotalAmount { get; set; }  // Gross sales
    public double NetAmount { get; set; }
    public double Discount { get; set; }
    public double Tax { get; set; }
    public string Status { get; set; }  // "Completed", "Cancelled", "Refunded"
    public TimeSpan ProcessingTime { get; set; }
    public List<OrderItem> Items { get; set; }
    public string PaymentMethod { get; set; }  // "Cash", "UPI", "Card", "Digital"
    public int CustomerId { get; set; }
    public double RefundAmount { get; set; } = 0;
}

public class OrderItem
{
    public string ItemName { get; set; }
    public int Quantity { get; set; }
    public double Revenue { get; set; }
}