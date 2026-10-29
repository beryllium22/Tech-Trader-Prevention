using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using TechTraderPrevention.Core;
using Verse;

namespace TechTraderPrevention
{
    public static class ColonyTech
    {
        public static TechLevel CurrentLevel
        {
            get
            {
                GameComponent_TechTrader component = Current.Game?.GetComponent<GameComponent_TechTrader>();
                return component == null ? FallbackLevel : (TechLevel)component.GetResult().Level;
            }
        }

        public static TechLevel FallbackLevel
        {
            get
            {
                TechTraderSettings settings = TechTraderMod.Settings;
                int value = settings.ColonyTechMode == ResearchMode.Manual ? (int)settings.ManualColonyTech
                    : (int)(Faction.OfPlayer?.def?.techLevel ?? settings.MinimumColonyTech);
                return (TechLevel)Math.Max((int)settings.MinimumColonyTech, Math.Min((int)settings.MaximumColonyTech, value));
            }
        }

        public static ResearchResult CurrentResult => Current.Game?.GetComponent<GameComponent_TechTrader>()?.GetResult();
        public static void Invalidate() => Current.Game?.GetComponent<GameComponent_TechTrader>()?.Invalidate();

        public static void Apply(Harmony harmony)
        {
            // No signature/version-specific DLC dependency; only observe completion/reset.
            foreach (string name in new[] { "FinishProject", "ResetAllProgress", "DebugSetAllProjectsFinished" })
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ResearchManager)))
                {
                    if (method.Name != name) continue;
                    try { harmony.Patch(method, postfix: new HarmonyMethod(typeof(ColonyTech), nameof(ResearchChanged))); }
                    catch (Exception e) { Log.Warning("[Tech Trader Prevention] Research refresh hook " + name + " unavailable; periodic refresh remains active. " + e.Message); }
                }
            }
        }

        private static void ResearchChanged() => Invalidate();
    }

    public sealed class GameComponent_TechTrader : GameComponent
    {
        private ResearchResult result;
        private int lastTick = -1;
        private int settingsRevision = -1;
        private bool dirty = true;
        private bool calculating;
        private static readonly PropertyInfo HiddenProperty = AccessTools.Property(typeof(ResearchProjectDef), "IsHidden");
        private static readonly FieldInfo KnowledgeCategory = AccessTools.Field(typeof(ResearchProjectDef), "knowledgeCategory");
        private static readonly FieldInfo KnowledgeCost = AccessTools.Field(typeof(ResearchProjectDef), "knowledgeCost");

        public GameComponent_TechTrader(Game game) { }
        public override void StartedNewGame() => Invalidate();
        public override void LoadedGame() => Invalidate();
        public override void FinalizeInit() => Invalidate();
        public void Invalidate() { dirty = true; }

        public ResearchResult GetResult()
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            int tick = Find.TickManager?.TicksGame ?? 0;
            // These modes are cheap live reads, including another mod's faction-tech changes.
            if (settings.ColonyTechMode == ResearchMode.Faction || settings.ColonyTechMode == ResearchMode.Manual)
            {
                int level = settings.ColonyTechMode == ResearchMode.Manual ? (int)settings.ManualColonyTech
                    : (int)(Faction.OfPlayer?.def?.techLevel ?? settings.MinimumColonyTech);
                return new ResearchResult(Math.Max((int)settings.MinimumColonyTech,
                    Math.Min((int)settings.MaximumColonyTech, level)), new TierProgress[0]);
            }
            if (calculating) return result ?? new ResearchResult((int)settings.MinimumColonyTech, new TierProgress[0]);
            if (result != null && !dirty && settingsRevision == TechTraderMod.SettingsRevision
                && tick >= lastTick && tick - lastTick < settings.ResearchRefreshTicks) return result;
            if (Find.ResearchManager == null)
                return new ResearchResult((int)ColonyTech.FallbackLevel, new TierProgress[0]);

            calculating = true;
            try
            {
                var samples = new List<ResearchSample>();
                foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                {
                    try
                    {
                        if (DefPolicy.ResearchIgnored(project)) continue;
                        bool special = KnowledgeCategory?.GetValue(project) != null;
                        // Anomaly knowledge is a separate track, counted only by explicit choice.
                        if (special && !settings.IncludeSpecialResearch) continue;
                        double cost = special && KnowledgeCost != null ? Convert.ToDouble(KnowledgeCost.GetValue(project)) : project.baseCost;
                        if (!settings.IncludeZeroCostResearch && cost <= 0) continue;
                        bool complete = project.IsFinished;
                        // Finished discoveries stay counted even if a mod hides them afterward.
                        if (!settings.IncludeHiddenResearch && !complete && HiddenProperty?.GetValue(project, null) is bool hidden && hidden) continue;
                        samples.Add(new ResearchSample((int)DefPolicy.EffectiveTech(project), cost, complete));
                    }
                    catch (Exception e)
                    {
                        Log.WarningOnce("[Tech Trader Prevention] Ignored unreadable research " + (project?.defName ?? "<null>")
                            + ": " + e.GetBaseException().Message, 812197422 ^ (project?.shortHash ?? 0));
                    }
                }
                var thresholds = new Dictionary<int, double>();
                foreach (KeyValuePair<int, float> entry in settings.TierThresholds) thresholds[entry.Key] = (double)(decimal)entry.Value;
                result = ResearchTechCalculator.Calculate(samples, new ResearchRules
                {
                    Mode = settings.ColonyTechMode,
                    Metric = settings.CompletionMetric,
                    FactionTier = (int)(Faction.OfPlayer?.def?.techLevel ?? settings.MinimumColonyTech),
                    ManualTier = (int)settings.ManualColonyTech,
                    MinimumTier = (int)settings.MinimumColonyTech,
                    MaximumTier = (int)settings.MaximumColonyTech,
                    UseFactionFloor = settings.UseFactionTechAsFloor,
                    CompletionThreshold = (double)(decimal)settings.CompletionThreshold,
                    MinimumCompletedProjects = settings.MinimumCompletedProjects,
                    RequireLowerTiers = settings.RequireLowerTiers,
                    EmptyTierBlocks = settings.EmptyTierBlocks,
                    TierThresholds = thresholds
                });
                dirty = false;
                settingsRevision = TechTraderMod.SettingsRevision;
                lastTick = tick;
                return result;
            }
            catch (Exception e)
            {
                // A problematic third-party research definition must not break trading/death.
                Log.ErrorOnce("[Tech Trader Prevention] Could not evaluate research; using the faction/manual fallback. " + e, 812197421);
                result = new ResearchResult((int)ColonyTech.FallbackLevel, new TierProgress[0]);
                dirty = false;
                settingsRevision = TechTraderMod.SettingsRevision;
                lastTick = tick;
                return result;
            }
            finally { calculating = false; }
        }
    }
}
