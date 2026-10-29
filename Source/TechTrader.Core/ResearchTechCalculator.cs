using System;
using System.Collections.Generic;

namespace TechTraderPrevention.Core
{
    public readonly struct ResearchSample
    {
        public ResearchSample(int tier, double cost, bool completed)
        { Tier = tier; Cost = cost; Completed = completed; }
        public int Tier { get; }
        public double Cost { get; }
        public bool Completed { get; }
    }

    public sealed class ResearchRules
    {
        public ResearchMode Mode = ResearchMode.TierCompletion;
        public ResearchMetric Metric = ResearchMetric.ResearchCost;
        public int FactionTier = 2;
        public int ManualTier = 4;
        public int MinimumTier = 2;
        public int MaximumTier = 7;
        public bool UseFactionFloor;
        public double CompletionThreshold = 0.75;
        public int MinimumCompletedProjects = 1;
        public bool RequireLowerTiers = true;
        public bool EmptyTierBlocks;
        public Dictionary<int, double> TierThresholds = new Dictionary<int, double>();
    }

    public sealed class TierProgress
    {
        public int Tier { get; internal set; }
        public int TotalProjects { get; internal set; }
        public int CompletedProjects { get; internal set; }
        public double TotalWeight { get; internal set; }
        public double CompletedWeight { get; internal set; }
        public double Completion => TotalWeight <= 0 ? 0 : CompletedWeight / TotalWeight;
        public double Threshold { get; internal set; }
        public bool Qualifies { get; internal set; }
    }

    public sealed class ResearchResult
    {
        public ResearchResult(int level, IReadOnlyList<TierProgress> tiers)
        { Level = level; Tiers = tiers; }
        public int Level { get; }
        public IReadOnlyList<TierProgress> Tiers { get; }
    }

    public static class ResearchTechCalculator
    {
        public static ResearchResult Calculate(IEnumerable<ResearchSample> samples, ResearchRules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            int minimum = TechRules.ClampTier(rules.MinimumTier);
            int maximum = Math.Max(minimum, TechRules.ClampTier(rules.MaximumTier));
            int floor = rules.UseFactionFloor ? Math.Max(minimum, TechRules.ClampTier(rules.FactionTier)) : minimum;
            floor = Math.Min(floor, maximum);
            var tiers = new TierProgress[8];
            double commonThreshold = TechRules.Fraction(rules.CompletionThreshold, 0.75);
            for (int i = 0; i < tiers.Length; i++)
            {
                double threshold;
                if (rules.TierThresholds == null || !rules.TierThresholds.TryGetValue(i, out threshold))
                    threshold = commonThreshold;
                tiers[i] = new TierProgress { Tier = i, Threshold = TechRules.Fraction(threshold, commonThreshold) };
            }
            int highest = floor;
            if (samples != null)
            {
                foreach (ResearchSample sample in samples)
                {
                    if (sample.Tier < 0 || sample.Tier > 7) continue;
                    // Exclusions belong to the adapter. Included zero-cost projects count once.
                    double weight = rules.Metric == ResearchMetric.ProjectCount ? 1 : sample.Cost;
                    if (double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0) weight = 1;
                    weight = Math.Min(weight, 1e12);
                    TierProgress tier = tiers[sample.Tier];
                    tier.TotalProjects++;
                    tier.TotalWeight += weight;
                    if (sample.Completed)
                    {
                        tier.CompletedProjects++;
                        tier.CompletedWeight += weight;
                        highest = Math.Max(highest, sample.Tier);
                    }
                }
            }
            foreach (TierProgress tier in tiers)
                tier.Qualifies = tier.TotalProjects > 0
                    && tier.CompletedProjects >= Math.Max(0, rules.MinimumCompletedProjects)
                    && tier.Completion + 1e-12 >= tier.Threshold;

            int level = floor;
            switch (rules.Mode)
            {
                case ResearchMode.Faction: level = rules.FactionTier; break;
                case ResearchMode.Manual: level = rules.ManualTier; break;
                case ResearchMode.HighestCompleted: level = highest; break;
                default:
                    for (int tier = floor + 1; tier <= maximum; tier++)
                    {
                        if (tiers[tier].TotalProjects == 0)
                        {
                            if (rules.RequireLowerTiers && rules.EmptyTierBlocks) break;
                            continue; // An absent DLC/mod tier neither unlocks nor blocks by default.
                        }
                        if (tiers[tier].Qualifies) level = tier;
                        else if (rules.RequireLowerTiers) break;
                    }
                    break;
            }
            return new ResearchResult(Math.Max(minimum, Math.Min(maximum, level)), tiers);
        }
    }
}
