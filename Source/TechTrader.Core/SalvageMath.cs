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
            decimal recovery = (decimal)(float)TechRules.Fraction(fraction);
            // Avoid overflowing cost * stack first and then evaluating Infinity * 0.
            // Zero recovery is meaningful even for an enormous modded recipe.
            if (recovery <= 0) return 0;
            // Keep the multiplication decimal too: binary doubles can lose a whole
            // component at large stack sizes (3 * 100,000,000 * 29%, for example).
            // This bound keeps even int.MaxValue items well inside decimal's range.
            if (materialCostPerItem <= 1e18)
            {
                decimal amountDecimal = (decimal)materialCostPerItem * originalStackCount * recovery;
                if (amountDecimal >= int.MaxValue) return int.MaxValue;
                return (int)Math.Floor(amountDecimal + 0.000000001m);
            }
            double amount = materialCostPerItem * (double)recovery * originalStackCount;
            if (amount >= int.MaxValue) return int.MaxValue;
            return (int)Math.Floor(amount + 1e-9);
        }

        // Never debit a destination below its pre-placement count, or remove more
        // units than the placement callback attributed to this transaction.
        public static int RollbackCount(int currentCount, int protectedCount, long addedCount)
        {
            if (currentCount <= 0 || addedCount <= 0) return 0;
            long available = Math.Max(0L, (long)currentCount - Math.Max(0, protectedCount));
            return (int)Math.Min(addedCount, available);
        }
    }
}
