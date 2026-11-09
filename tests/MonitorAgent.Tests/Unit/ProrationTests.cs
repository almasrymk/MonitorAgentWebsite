using MonitorAgent.Modules.Licenses.Application;

namespace MonitorAgent.Tests.Unit;

/// <summary>INT-014 (ADR-020): the prorated upgrade price and its rounding.</summary>
public class ProrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1200, 2400, 73, 365, 240.00)]
    [InlineData(1200, 2400, 365, 365, 1200.00)]
    [InlineData(1200, 2400, 1, 365, 3.29)]     // 3.2876... rounds up
    [InlineData(100, 101, 1, 365, 0.01)]       // 0.0027... would be free; the minimum is one piastre / cent
    [InlineData(1000, 1500, 30, 30, 500.00)]
    [InlineData(99.99, 149.99, 15, 30, 25.00)]
    [InlineData(10, 11.10, 1, 2, 0.55)]        // exactly 0.55
    [InlineData(10, 11.09, 1, 2, 0.55)]        // 0.545 is a midpoint: away from zero
    public void Price_is_the_difference_for_the_days_left(decimal from, decimal to, int left, int term, decimal expected) =>
        Assert.Equal(expected, Proration.UpgradePrice(from, to, left, term));

    [Fact]
    public void A_part_day_is_not_counted_and_the_term_caps_the_days()
    {
        Assert.Equal(73, Proration.RemainingDays(Now.AddDays(73).AddHours(23), Now, 365));
        Assert.Equal(0, Proration.RemainingDays(Now.AddHours(23), Now, 365));
        Assert.Equal(0, Proration.RemainingDays(Now.AddDays(-3), Now, 365));
        Assert.Equal(365, Proration.RemainingDays(Now.AddDays(400), Now, 365));
    }

    [Fact]
    public void Only_a_dearer_plan_with_days_left_has_a_price()
    {
        Assert.Throws<ArgumentException>(() => Proration.UpgradePrice(2400, 1200, 10, 365));
        Assert.Throws<ArgumentException>(() => Proration.UpgradePrice(1200, 1200, 10, 365));
        Assert.Throws<ArgumentOutOfRangeException>(() => Proration.UpgradePrice(1200, 2400, 0, 365));
        Assert.Throws<ArgumentOutOfRangeException>(() => Proration.UpgradePrice(1200, 2400, 10, 0));
    }
}
