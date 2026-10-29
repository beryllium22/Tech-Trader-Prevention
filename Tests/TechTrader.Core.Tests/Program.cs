using System;
using System.Collections.Generic;
using TechTraderPrevention.Core;

namespace TechTraderPrevention.Core.Tests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            TraderRules();
            ResearchProgression();
            SalvageRecovery();
            Console.WriteLine($"\nPolicy scenarios: {passed} passed, {failed} failed.");
            Console.WriteLine("These are deterministic policy tests; game hooks still require RimWorld playtesting.");
            return failed == 0 ? 0 : 1;
        }

        private static void TraderRules()
        {
            Check("Same-or-lower default rejects every higher faction tier", () =>
            {
                for (int colony = 0; colony <= 7; colony++)
                    for (int faction = 0; faction <= 7; faction++)
                        Equal(faction <= colony, TechRules.Allows(faction, colony, TechRule.AtOrBelow, 0, 0));
            });
            Check("Exact-tier restriction rejects lower and higher traders", () =>
            {
                for (int colony = 0; colony <= 7; colony++)
                    for (int faction = 0; faction <= 7; faction++)
                        Equal(faction == colony, TechRules.Allows(faction, colony, TechRule.Exact, 2, 2));
            });
            Check("Configured upper allowance permits one level without allowing two", () =>
            {
                True(TechRules.Allows(5, 4, TechRule.AtOrBelow, 1, 0));
                False(TechRules.Allows(6, 4, TechRule.AtOrBelow, 1, 0));
                True(TechRules.Allows(2, 4, TechRule.AtOrBelow, 1, 0));
            });
            Check("Technology range enforces both boundaries", () =>
            {
                False(TechRules.Allows(2, 4, TechRule.Range, 2, 1));
                True(TechRules.Allows(3, 4, TechRule.Range, 2, 1));
                True(TechRules.Allows(6, 4, TechRule.Range, 2, 1));
                False(TechRules.Allows(7, 4, TechRule.Range, 2, 1));
            });
            Check("Malformed tier settings cannot wrap or admit undefined out-of-range tiers", () =>
            {
                False(TechRules.Allows(-1, 4, TechRule.AtOrBelow, int.MaxValue, 0));
                False(TechRules.Allows(8, 4, TechRule.AtOrBelow, int.MaxValue, 0));
                False(TechRules.Allows(5, 4, TechRule.AtOrBelow, -1, 0));
                True(TechRules.Allows(7, int.MaxValue, TechRule.Range, int.MaxValue, int.MaxValue));
                Equal(0, TechRules.ClampTier(int.MinValue));
            });
            Check("Non-finite percentages use fallback and normal values stay bounded", () =>
            {
                Near(0.75, TechRules.Fraction(double.NaN, 0.75));
                Near(0, TechRules.Fraction(double.PositiveInfinity));
                Near(0, TechRules.Fraction(-4));
                Near(1, TechRules.Fraction(4));
            });
        }

        private static void ResearchProgression()
        {
            Check("A new colony with no research stays at configured starting tier", () =>
            {
                Equal(2, Calculate(Array.Empty<ResearchSample>()).Level);
                Equal(2, Calculate(null).Level);
            });
            Check("Research-cost weighting resists a cheap advanced-tech shortcut", () =>
            {
                var samples = new[] { R(3, 100, true), R(3, 900, false) };
                var result = Calculate(samples);
                Equal(2, result.Level);
                Near(0.1, result.Tiers[3].Completion);
            });
            Check("Project-count mode deliberately weights cheap and costly projects equally", () =>
            {
                var rules = new ResearchRules { Metric = ResearchMetric.ProjectCount, CompletionThreshold = 0.5 };
                Equal(3, Calculate(new[] { R(3, 100, true), R(3, 900, false) }, rules).Level);
            });
            Check("Reaching exactly the configured research threshold advances a tier", () =>
            {
                var result = Calculate(new[] { R(3, 300, true), R(3, 100, false) });
                Equal(3, result.Level);
                True(result.Tiers[3].Qualifies);
                Near(0.75, result.Tiers[3].Completion);
            });
            Check("Falling below a research threshold does not advance", () =>
            {
                Equal(2, Calculate(new[] { R(3, 299, true), R(3, 101, false) }).Level);
            });
            Check("Lower-tier requirements prevent jumping unfinished research tiers", () =>
            {
                var result = Calculate(new[] { R(3, 100, false), R(4, 100, true) });
                Equal(2, result.Level);
                True(result.Tiers[4].Qualifies);
            });
            Check("Disabling lower-tier requirements permits a configured research leap", () =>
            {
                var rules = new ResearchRules { RequireLowerTiers = false };
                Equal(4, Calculate(new[] { R(3, 100, false), R(4, 100, true) }, rules).Level);
            });
            Check("Absent DLC or mod research tiers do not block progression by default", () =>
            {
                Equal(5, Calculate(new[] { R(3, 100, true), R(5, 100, true) }).Level);
            });
            Check("Empty tiers alone never unlock technology", () =>
            {
                Equal(3, Calculate(new[] { R(3, 100, true) }).Level);
            });
            Check("Players can make missing tiers stop progression", () =>
            {
                var rules = new ResearchRules { EmptyTierBlocks = true };
                Equal(3, Calculate(new[] { R(3, 100, true), R(5, 100, true) }, rules).Level);
            });
            Check("Minimum project count stops one costly discovery from unlocking an era", () =>
            {
                var rules = new ResearchRules { MinimumCompletedProjects = 2 };
                Equal(2, Calculate(new[] { R(3, 1000, true), R(3, 1, false) }, rules).Level);
                Equal(3, Calculate(new[] { R(3, 1000, true), R(3, 1, true) }, rules).Level);
            });
            Check("Per-tier thresholds override the common threshold", () =>
            {
                var rules = new ResearchRules();
                rules.TierThresholds[3] = 1;
                Equal(2, Calculate(new[] { R(3, 300, true), R(3, 100, false) }, rules).Level);
                rules.TierThresholds[3] = 0.5;
                Equal(3, Calculate(new[] { R(3, 100, true), R(3, 100, false) }, rules).Level);
            });
            Check("Malformed per-tier thresholds fall back to the configured common threshold", () =>
            {
                var rules = new ResearchRules { CompletionThreshold = 0.5 };
                rules.TierThresholds[3] = double.NaN;
                var result = Calculate(new[] { R(3, 100, true), R(3, 100, false) }, rules);
                Equal(3, result.Level);
                Near(0.5, result.Tiers[3].Threshold);
                rules.CompletionThreshold = double.NaN;
                rules.TierThresholds[3] = double.PositiveInfinity;
                result = Calculate(new[] { R(3, 100, true), R(3, 100, false) }, rules);
                Equal(2, result.Level);
                Near(0.75, result.Tiers[3].Threshold);
            });
            Check("Zero-cost included research has finite weight and can count as a project", () =>
            {
                var result = Calculate(new[] { R(3, 0, true) });
                Equal(3, result.Level);
                Near(1, result.Tiers[3].TotalWeight);
            });
            Check("Invalid third-party research costs cannot poison the whole calculation", () =>
            {
                var result = Calculate(new[] { R(3, double.NaN, true), R(3, double.PositiveInfinity, false), R(3, -10, true) },
                    new ResearchRules { CompletionThreshold = 0.6 });
                Equal(3, result.Level);
                Near(3, result.Tiers[3].TotalWeight);
                Near(2.0 / 3, result.Tiers[3].Completion);
            });
            Check("Out-of-range mod research tiers are ignored", () =>
            {
                var result = Calculate(new[] { R(-1, 100, true), R(8, 100, true) },
                    new ResearchRules { Mode = ResearchMode.HighestCompleted });
                Equal(2, result.Level);
            });
            Check("Highest-completed mode promotes from a single advanced discovery", () =>
            {
                var rules = new ResearchRules { Mode = ResearchMode.HighestCompleted };
                Equal(6, Calculate(new[] { R(3, 1000, false), R(6, 1, true) }, rules).Level);
            });
            Check("Unfinished advanced research never raises highest-completed mode", () =>
            {
                var rules = new ResearchRules { Mode = ResearchMode.HighestCompleted };
                Equal(3, Calculate(new[] { R(3, 100, true), R(7, 100, false) }, rules).Level);
            });
            Check("Faction floor preserves an advanced scenario start when requested", () =>
            {
                var rules = new ResearchRules { UseFactionFloor = true, FactionTier = 4 };
                Equal(4, Calculate(Array.Empty<ResearchSample>(), rules).Level);
                rules.UseFactionFloor = false;
                Equal(2, Calculate(Array.Empty<ResearchSample>(), rules).Level);
            });
            Check("Manual and faction modes do not depend on completed research", () =>
            {
                var rules = new ResearchRules { Mode = ResearchMode.Manual, ManualTier = 3, FactionTier = 5 };
                Equal(3, Calculate(new[] { R(7, 100, true) }, rules).Level);
                rules.Mode = ResearchMode.Faction;
                Equal(5, Calculate(Array.Empty<ResearchSample>(), rules).Level);
            });
            Check("Configured maximum contains advanced discoveries and faction floors", () =>
            {
                var rules = new ResearchRules { Mode = ResearchMode.HighestCompleted, MaximumTier = 4, UseFactionFloor = true, FactionTier = 7 };
                Equal(4, Calculate(new[] { R(7, 100, true) }, rules).Level);
            });
            Check("Corrupt threshold values fall back to 75 percent", () =>
            {
                var rules = new ResearchRules { CompletionThreshold = double.NaN, TierThresholds = null };
                var result = Calculate(new[] { R(3, 100, true), R(3, 100, false) }, rules);
                Equal(2, result.Level);
                Near(0.75, result.Tiers[3].Threshold);
            });
            Check("Invalid reversed level bounds stay at a valid configured minimum", () =>
            {
                var rules = new ResearchRules { MinimumTier = 5, MaximumTier = 2 };
                Equal(5, Calculate(Array.Empty<ResearchSample>(), rules).Level);
            });
            Check("A large modpack research tree retains exact tier counts and thresholds", () =>
            {
                IEnumerable<ResearchSample> LargeTree()
                {
                    for (int tier = 3; tier <= 6; tier++)
                        for (int project = 0; project < 5000; project++)
                            yield return R(tier, tier * 100, project % 4 != 3);
                }
                foreach (ResearchMetric metric in Enum.GetValues<ResearchMetric>())
                {
                    var result = Calculate(LargeTree(), new ResearchRules { Metric = metric });
                    Equal(6, result.Level);
                    for (int tier = 3; tier <= 6; tier++)
                    {
                        Equal(5000, result.Tiers[tier].TotalProjects);
                        Equal(3750, result.Tiers[tier].CompletedProjects);
                        Near(0.75, result.Tiers[tier].Completion);
                        True(result.Tiers[tier].Qualifies);
                    }
                }
            });
            Check("Research is consumed in one pass even when a mod supplies a lazy sequence", () =>
            {
                int enumerations = 0;
                IEnumerable<ResearchSample> Samples()
                {
                    if (++enumerations > 1) throw new InvalidOperationException("Research was enumerated again.");
                    yield return R(3, 100, true);
                    yield return R(4, 100, true);
                    yield return R(5, 100, false);
                }
                Equal(4, Calculate(Samples()).Level);
                Equal(1, enumerations);
            });
            Check("Huge modded research costs remain finite and cannot poison another tier", () =>
            {
                var result = Calculate(new[] { R(3, double.MaxValue, true), R(3, double.MaxValue, false), R(4, 100, true) },
                    new ResearchRules { RequireLowerTiers = false });
                Equal(4, result.Level);
                Near(0.5, result.Tiers[3].Completion);
                True(double.IsFinite(result.Tiers[3].TotalWeight));
                True(result.Tiers[4].Qualifies);
            });
        }

        private static void SalvageRecovery()
        {
            Check("Zero percent yields no materials and full recovery respects stack size", () =>
            {
                Equal(0, SalvageMath.RecoveredCount(50, 1, 0));
                Equal(150, SalvageMath.RecoveredCount(50, 3, 1));
            });
            Check("Whole-number percentage outcomes are not lost to float precision", () =>
            {
                Equal(7, SalvageMath.RecoveredCount(10, 1, 0.7f));
                Equal(29, SalvageMath.RecoveredCount(100, 1, 0.29f));
                Equal(58, SalvageMath.RecoveredCount(100, 2, 0.29f));
            });
            Check("Large stack mods do not lose whole materials to floating-point multiplication", () =>
            {
                Equal(87000000, SalvageMath.RecoveredCount(3, 100000000, 0.29f));
                Equal(203000000, SalvageMath.RecoveredCount(7, 100000000, 0.29f));
            });
            Check("True fractional materials round down instead of creating free ingredients", () =>
            {
                Equal(1, SalvageMath.RecoveredCount(3, 1, 0.5f));
                Equal(0, SalvageMath.RecoveredCount(1, 1, 0.99f));
                Equal(2, SalvageMath.RecoveredCount(2.75, 1, 1));
            });
            Check("Fixed recovery ignores random limits and random roll", () =>
            {
                Near(0.5, SalvageMath.RecoveryFraction(false, 0.5f, 0, 1, 0.99f));
            });
            Check("Random survival samples the configured interval including its bounds", () =>
            {
                Near(0.2, SalvageMath.RecoveryFraction(true, 1, 0.2f, 0.8f, 0), 1e-6);
                Near(0.5, SalvageMath.RecoveryFraction(true, 1, 0.2f, 0.8f, 0.5f), 1e-6);
                Near(0.8, SalvageMath.RecoveryFraction(true, 1, 0.2f, 0.8f, 1), 1e-6);
            });
            Check("A single item's material types share the same sampled yield", () =>
            {
                float fraction = SalvageMath.RecoveryFraction(true, 1, 0.2f, 0.8f, 0.5f);
                Equal(25, SalvageMath.RecoveredCount(50, 1, fraction));
                Equal(2, SalvageMath.RecoveredCount(4, 1, fraction));
            });
            Check("Reversed random bounds and invalid rolls cannot exceed recovery limits", () =>
            {
                Near(0.2, SalvageMath.RecoveryFraction(true, 0, 0.8f, 0.2f, -100), 1e-6);
                Near(0.8, SalvageMath.RecoveryFraction(true, 0, 0.8f, 0.2f, 100), 1e-6);
                Near(0, SalvageMath.RecoveryFraction(true, 0, float.NaN, float.PositiveInfinity, 0.5f));
            });
            Check("Invalid ingredient costs and stack counts generate nothing", () =>
            {
                foreach (double cost in new[] { -1.0, 0, double.NaN, double.PositiveInfinity })
                    Equal(0, SalvageMath.RecoveredCount(cost, 1, 1));
                Equal(0, SalvageMath.RecoveredCount(50, 0, 1));
                Equal(0, SalvageMath.RecoveredCount(50, -10, 1));
            });
            Check("Out-of-range recovery is bounded and enormous recipes cannot integer-wrap", () =>
            {
                Equal(0, SalvageMath.RecoveredCount(50, 1, -1));
                Equal(50, SalvageMath.RecoveredCount(50, 1, 2));
                Equal(0, SalvageMath.RecoveredCount(50, 1, float.NaN));
                Equal(int.MaxValue, SalvageMath.RecoveredCount(1e12, int.MaxValue, 1));
            });
            Check("Zero recovery remains zero when a malformed finite cost would overflow", () =>
            {
                Equal(0, SalvageMath.RecoveredCount(double.MaxValue, 2, 0));
                Equal(0, SalvageMath.RecoveredCount(double.MaxValue, int.MaxValue, 0));
                Equal(0, SalvageMath.RecoveredCount(double.MaxValue, int.MaxValue, float.NaN));
                Equal(int.MaxValue, SalvageMath.RecoveredCount(double.MaxValue, 2, 1));
            });
            Check("Every whole survival percentage recovers the corresponding count from one hundred", () =>
            {
                for (int percent = 0; percent <= 100; percent++)
                    Equal(percent, SalvageMath.RecoveredCount(100, 1, percent / 100f));
            });
            Check("Increasing survival never reduces recovery or exceeds original crafting costs", () =>
            {
                foreach (double cost in new[] { 0.25, 1, 3, 80, 137.25, 10000 })
                {
                    int previous = 0;
                    for (int percent = 0; percent <= 100; percent++)
                    {
                        int current = SalvageMath.RecoveredCount(cost, 3, percent / 100f);
                        True(current >= previous);
                        True(current <= Math.Floor(cost * 3));
                        previous = current;
                    }
                }
            });
            Check("Salvage rollback preserves pre-existing destination materials", () =>
            {
                Equal(10, SalvageMath.RollbackCount(110, 100, 10));
                Equal(5, SalvageMath.RollbackCount(105, 100, 10));
                Equal(0, SalvageMath.RollbackCount(95, 100, 10));
                Equal(10, SalvageMath.RollbackCount(150, 100, 10));
            });
            Check("Salvage rollback safely handles invalid or enormous material counts", () =>
            {
                Equal(int.MaxValue, SalvageMath.RollbackCount(int.MaxValue, 0, long.MaxValue));
                Equal(0, SalvageMath.RollbackCount(-1, 0, 10));
                Equal(0, SalvageMath.RollbackCount(100, 0, -10));
                Equal(0, SalvageMath.RollbackCount(100, int.MaxValue, long.MaxValue));
                Equal(10, SalvageMath.RollbackCount(100, -1, 10));
            });
        }

        private static ResearchSample R(int tier, double cost, bool done) => new ResearchSample(tier, cost, done);
        private static ResearchResult Calculate(IEnumerable<ResearchSample> samples, ResearchRules rules = null)
            => ResearchTechCalculator.Calculate(samples, rules ?? new ResearchRules());

        private static void Check(string name, Action scenario)
        {
            try { scenario(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}, received {actual}.");
        }

        private static void True(bool actual) => Equal(true, actual);
        private static void False(bool actual) => Equal(false, actual);
        private static void Near(double expected, double actual, double tolerance = 1e-9)
        {
            if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException($"Expected {expected} ± {tolerance}, received {actual}.");
        }
    }
}
