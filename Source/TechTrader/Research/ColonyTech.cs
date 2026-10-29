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
                int minimum = TechRules.ClampTier((int)(settings?.MinimumColonyTech ?? TechLevel.Neolithic));
                int maximum = Math.Max(minimum, TechRules.ClampTier((int)(settings?.MaximumColonyTech ?? TechLevel.Archotech)));
                int value = settings?.ColonyTechMode == ResearchMode.Manual ? (int)settings.ManualColonyTech
                    : (int)(PlayerFaction?.def?.techLevel ?? (TechLevel)minimum);
                return (TechLevel)Math.Max(minimum, Math.Min(maximum, value));
            }
        }

        public static ResearchResult CurrentResult => Current.Game?.GetComponent<GameComponent_TechTrader>()?.GetResult();
        public static void Invalidate() => Current.Game?.GetComponent<GameComponent_TechTrader>()?.Invalidate();
        internal static Faction PlayerFaction => Current.Game?.World?.factionManager?.OfPlayer;

        public static void Apply(Harmony harmony)
        {
            // Observe completion/reset without adding work to every research work tick.
            bool finishFound = false;
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ResearchManager)))
            {
                string name = method.Name;
                if (name == "FinishProject" || name == "ResetAllProgress" || name == "DebugSetAllProjectsFinished")
                {
                    finishFound |= name == "FinishProject";
                    CompatibilityDiagnostics.Patch(harmony, method, "Research refresh: " + name,
                        postfix: new HarmonyMethod(typeof(ColonyTech), nameof(ResearchChanged)));
                }
                else if (name == "AddProgress")
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 0 || parameters[0].ParameterType != typeof(ResearchProjectDef)) continue;
                    // Some research mods remove progress or bypass FinishProject.
                    // Compare one completion bit, not every loaded project.
                    CompatibilityDiagnostics.Patch(harmony, method, "Research completion boundary: " + name,
                        prefix: new HarmonyMethod(typeof(ColonyTech), nameof(ProgressChanging)),
                        postfix: new HarmonyMethod(typeof(ColonyTech), nameof(ProgressChanged)));
                }
            }
            if (!finishFound)
                CompatibilityDiagnostics.Patch(harmony, null, "Research refresh: FinishProject (periodic fallback remains active)");
        }

        private static void ResearchChanged() => Invalidate();

        private static void ProgressChanging(ResearchProjectDef __0, out int __state)
        {
            __state = -1;
            if (__0 == null || Current.Game?.researchManager == null) return;
            try { __state = __0.IsFinished ? 1 : 0; }
            catch { /* A third-party completion getter must not break its progress update. */ }
        }

        private static void ProgressChanged(ResearchProjectDef __0, int __state)
        {
            if (__state < 0 || __0 == null || Current.Game?.researchManager == null) return;
            try { if ((__0.IsFinished ? 1 : 0) != __state) Invalidate(); }
            catch { /* The bounded periodic refresh remains the compatibility fallback. */ }
        }
    }

    public sealed class GameComponent_TechTrader : GameComponent
    {
        private ResearchResult result;
        private ResearchResult directResult;
        private int lastTick = -1;
        private int settingsRevision = -1;
        private bool dirty = true;
        private bool calculating;
        private int invalidationVersion;
        private ResearchManager observedManager;
        private List<ResearchProjectDef> observedDefinitions;
        private int observedDefinitionCount = -1;
        private Faction observedFaction;
        private int observedFactionTier = -1;
        private TechTraderSettings observedSettings;
        private ResearchMode observedMode;
        private readonly Dictionary<int, double> thresholds = new Dictionary<int, double>(8);
        private static readonly PropertyInfo HiddenProperty = AccessTools.Property(typeof(ResearchProjectDef), "IsHidden");
        private static readonly FieldInfo KnowledgeCategory = AccessTools.Field(typeof(ResearchProjectDef), "knowledgeCategory");
        private static readonly FieldInfo KnowledgeCost = AccessTools.Field(typeof(ResearchProjectDef), "knowledgeCost");

        public GameComponent_TechTrader(Game game) { }
        public override void StartedNewGame() => Invalidate();
        public override void LoadedGame() => Invalidate();
        public override void FinalizeInit() => Invalidate();
        public void Invalidate() { dirty = true; unchecked { invalidationVersion++; } }

        public ResearchResult GetResult()
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            if (settings == null) return DirectResult((int)ColonyTech.FallbackLevel);
            ResearchMode mode = settings.ColonyTechMode;
            int tick = Current.Game?.tickManager?.TicksGame ?? 0;
            Faction playerFaction = ColonyTech.PlayerFaction;
            int factionTier = (int)(playerFaction?.def?.techLevel ?? settings.MinimumColonyTech);
            // These modes are cheap live reads, including another mod's faction-tech changes.
            if (mode == ResearchMode.Faction || mode == ResearchMode.Manual)
            {
                int level = mode == ResearchMode.Manual ? (int)settings.ManualColonyTech
                    : factionTier;
                int minimum = TechRules.ClampTier((int)settings.MinimumColonyTech);
                int maximum = Math.Max(minimum, TechRules.ClampTier((int)settings.MaximumColonyTech));
                return DirectResult(Math.Max(minimum, Math.Min(maximum, level)));
            }
            if (calculating) return result ?? DirectResult((int)ColonyTech.FallbackLevel);
            ResearchManager manager = Current.Game?.researchManager;
            List<ResearchProjectDef> definitions = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            int definitionCount = definitions.Count;
            int revision = TechTraderMod.SettingsRevision;
            int refreshTicks = Math.Max(60, Math.Min(60000, settings.ResearchRefreshTicks));
            if (result != null && !dirty && settingsRevision == revision
                && ReferenceEquals(observedSettings, settings) && observedMode == mode
                && ReferenceEquals(observedManager, manager) && ReferenceEquals(observedDefinitions, definitions)
                && observedDefinitionCount == definitionCount && ReferenceEquals(observedFaction, playerFaction)
                && observedFactionTier == factionTier
                && tick >= lastTick && (long)tick - lastTick < refreshTicks) return result;
            if (manager == null) return DirectResult((int)ColonyTech.FallbackLevel);

            calculating = true;
            int refreshVersion = invalidationVersion;
            try
            {
                double commonThreshold = Percentage(settings.CompletionThreshold, 0.75);
                thresholds.Clear();
                if (settings.TierThresholds != null)
                    foreach (KeyValuePair<int, float> entry in settings.TierThresholds)
                        if (entry.Key >= 0 && entry.Key <= 7) thresholds[entry.Key] = Percentage(entry.Value, commonThreshold);
                result = ResearchTechCalculator.Calculate(ReadSamples(definitions, definitionCount, settings), new ResearchRules
                {
                    Mode = mode,
                    Metric = settings.CompletionMetric,
                    FactionTier = factionTier,
                    ManualTier = (int)settings.ManualColonyTech,
                    MinimumTier = (int)settings.MinimumColonyTech,
                    MaximumTier = (int)settings.MaximumColonyTech,
                    UseFactionFloor = settings.UseFactionTechAsFloor,
                    CompletionThreshold = commonThreshold,
                    MinimumCompletedProjects = settings.MinimumCompletedProjects,
                    RequireLowerTiers = settings.RequireLowerTiers,
                    EmptyTierBlocks = settings.EmptyTierBlocks,
                    TierThresholds = thresholds
                });
                return result;
            }
            catch (Exception e)
            {
                // A problematic third-party research definition must not break trading/death.
                Log.ErrorOnce("[Tech Trader Prevention] Could not evaluate research; using the faction/manual fallback. " + e, 812197421);
                result = DirectResult((int)ColonyTech.FallbackLevel);
                return result;
            }
            finally
            {
                // Do not lose an invalidation raised by a third-party getter while
                // the snapshot was being evaluated. A future lookup retries it.
                dirty = invalidationVersion != refreshVersion;
                settingsRevision = revision;
                lastTick = tick;
                observedManager = manager;
                observedDefinitions = definitions;
                observedDefinitionCount = definitionCount;
                observedFaction = playerFaction;
                observedFactionTier = factionTier;
                observedSettings = settings;
                observedMode = mode;
                calculating = false;
            }
        }

        private ResearchResult DirectResult(int level)
        {
            if (directResult == null || directResult.Level != level)
                directResult = new ResearchResult(level, Array.Empty<TierProgress>());
            return directResult;
        }

        private static double Percentage(float value, double fallback)
            => (double)(decimal)(float)TechRules.Fraction(value, fallback);

        private static IEnumerable<ResearchSample> ReadSamples(List<ResearchProjectDef> definitions, int count, TechTraderSettings settings)
        {
            // Stream into the calculator's eight tier buckets. A 20k-project pack
            // no longer needs a second growing project-sized array on each refresh.
            for (int i = 0; i < count && i < definitions.Count; i++)
                if (TryReadSample(definitions[i], settings, out ResearchSample sample)) yield return sample;
        }

        private static bool TryReadSample(ResearchProjectDef project, TechTraderSettings settings, out ResearchSample sample)
        {
            sample = default;
            try
            {
                if (!DefPolicy.TryGetResearchTech(project, out TechLevel level)) return false;
                bool special = KnowledgeCategory?.GetValue(project) != null;
                // Anomaly knowledge remains a separate opt-in research track.
                if (special && !settings.IncludeSpecialResearch) return false;
                double cost = special && KnowledgeCost != null ? Convert.ToDouble(KnowledgeCost.GetValue(project)) : project.baseCost;
                if (!settings.IncludeZeroCostResearch && cost <= 0) return false;
                bool complete = project.IsFinished;
                if (!settings.IncludeHiddenResearch && !complete && HiddenProperty?.GetValue(project, null) is bool hidden && hidden) return false;
                sample = new ResearchSample((int)level, cost, complete);
                return true;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Tech Trader Prevention] Ignored unreadable research " + (project?.defName ?? "<null>")
                    + ": " + e.GetBaseException().Message, 812197422 ^ (project?.shortHash ?? 0));
                return false;
            }
        }
    }
}
