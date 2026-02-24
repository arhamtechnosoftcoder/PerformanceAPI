namespace RestaurantPerformance.API.Helpers;
public static class MathExtensions
{
    public static double Log1p(double x)
    {
        if (x < -1 || double.IsNaN(x))
            return double.NaN;
        if (x == -1)
            return double.NegativeInfinity;

        if (Math.Abs(x) < 1e-8)
            return x - x * x / 2 + x * x * x / 3;

        return Math.Log(1 + x);
    }
}