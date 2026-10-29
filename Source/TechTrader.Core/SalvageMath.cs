using System;

namespace TechTraderPrevention.Core
{
    public static class SalvageMath
    {
        // One sampled fraction is shared by all ingredients in a broken item.
        public static float RecoveryFraction(bool random, float fixedFraction, float min, float max, float unitRoll)
        {
            if (!random) return (float)TechRules.Fraction(fixedFraction);
            double low = TechRules.Fraction(min), high = TechRules.Fraction(max);
            if (low > high) { double swap = low; low = high; high = swap; }
            return (float)(low + (high - low) * TechRules.Fraction(unitRoll));
        }

        public static int RecoveredCount(double materialCostPerItem, int originalStackCount, float fraction)
        {
            if (double.IsNaN(materialCostPerItem) || double.IsInfinity(materialCostPerItem)
                || materialCostPerItem <= 0 || originalStackCount <= 0) return 0;
            // Float sliders represent user percentages imprecisely (0.7f is below 0.7).
            // Decimal's float conversion restores their seven significant digits before
            // multiplication, so 70% of ten components is seven, not six.
            double recovery = (double)(decimal)(float)TechRules.Fraction(fraction);
            double amount = materialCostPerItem * originalStackCount * recovery;
            if (amount >= int.MaxValue) return int.MaxValue;
            return (int)Math.Floor(amount + 1e-9);
        }
    }
}
