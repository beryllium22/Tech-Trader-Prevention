using System;

namespace TechTraderPrevention.Core
{
    public enum TechRule { AtOrBelow, Exact, Range }
    public enum ResearchMode { TierCompletion, HighestCompleted, Faction, Manual }
    public enum ResearchMetric { ResearchCost, ProjectCount }
    public enum SalvageTrigger { Death, Downed, DeathOrDowned }

    public static class TechRules
    {
        public static int ClampTier(int value) => Math.Max(0, Math.Min(7, value));

        public static bool Allows(int candidate, int colony, TechRule rule, int allowedAbove, int allowedBelow)
        {
            if (candidate < 0 || candidate > 7) return false;
            colony = ClampTier(colony);
            int maximum = Math.Min(7, colony + ClampTier(allowedAbove));
            switch (rule)
            {
                case TechRule.Exact: return candidate == colony;
                case TechRule.Range:
                    return candidate >= Math.Max(0, colony - ClampTier(allowedBelow)) && candidate <= maximum;
                default: return candidate <= maximum;
            }
        }

        public static double Fraction(double value, double fallback = 0)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = fallback;
            return Math.Max(0, Math.Min(1, value));
        }
    }
}
