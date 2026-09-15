namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Pure billing math kept separate from database access so tariff behaviour
    /// can be tested with deterministic known values.
    /// </summary>
    public static class BillingCalculationEngine
    {
        public static decimal CalculateTieredCharge(
            decimal consumption,
            decimal baseRate,
            decimal threshold1,
            decimal threshold1Rate,
            decimal threshold2,
            decimal threshold2Rate)
        {
            consumption = Math.Max(0m, consumption);
            baseRate = Math.Max(0m, baseRate);
            threshold1 = Math.Max(0m, threshold1);
            threshold2 = Math.Max(threshold1, threshold2);
            threshold1Rate = Math.Max(0m, threshold1Rate);
            threshold2Rate = Math.Max(0m, threshold2Rate);

            var firstBand = Math.Min(consumption, threshold1);
            var secondBand = Math.Min(
                Math.Max(consumption - threshold1, 0m),
                threshold2 - threshold1);
            var thirdBand = Math.Max(consumption - threshold2, 0m);

            return Math.Round(
                firstBand * baseRate +
                secondBand * threshold1Rate +
                thirdBand * threshold2Rate,
                2);
        }

        public static decimal CalculateTax(decimal taxableAmount, decimal taxRate)
        {
            var safeAmount = Math.Max(0m, taxableAmount);
            var safeRate = Math.Max(0m, taxRate);
            return Math.Round(safeAmount * safeRate, 2);
        }
    }
}
