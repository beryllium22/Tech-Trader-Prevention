using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TechTraderPrevention.Trading
{
    /// <summary>
    /// Narrow method patches retain the game's faction suitability checks and
    /// trade transaction logic. No global Defs or trader inventories are edited.
    /// </summary>
    public static class TradingPatches
    {
        private static MethodInfo factionSourceMethod;
        private static MethodInfo resolveMethod;
        private static bool sourceUsesMap;

        [ThreadStatic] private static int originalSuitabilityDepth;
        [ThreadStatic] private static int replacementDepth;
        [ThreadStatic] private static int preservedQuestDepth;

        public static void Apply(Harmony harmony)
        {
            Type worker = typeof(IncidentWorker_TraderCaravanArrival);
            factionSourceMethod = AccessTools.Method(worker, "FactionCanBeGroupSource");
            resolveMethod = AccessTools.Method(worker, "TryResolveParmsGeneral", new[] { typeof(IncidentParms) });

            if (factionSourceMethod != null)
            {
                ParameterInfo[] parameters = factionSourceMethod.GetParameters();
                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(Faction)
                    && parameters[2].ParameterType == typeof(bool)
                    && (parameters[1].ParameterType == typeof(Map)
                        || parameters[1].ParameterType == typeof(IncidentParms)))
                {
                    sourceUsesMap = parameters[1].ParameterType == typeof(Map);
                    Patch(harmony, factionSourceMethod, null, nameof(FactionSourcePostfix));
                }
                else
                {
                    factionSourceMethod = null;
                    Log.Error("[Tech Trader Prevention] Unsupported faction-selection signature; caravan replacement is disabled.");
                }
            }
            else
            {
                Log.Error("[Tech Trader Prevention] Faction-selection hook was not found; caravan replacement is disabled.");
            }

            Patch(harmony, resolveMethod, nameof(ResolvePrefix), nameof(ResolvePostfix), nameof(ResolveFinalizer));
            Patch(harmony, AccessTools.Method(worker, "CanFireNowSub", new[] { typeof(IncidentParms) }),
                nameof(ResolvePrefix), null, nameof(ResolveFinalizer));
            Patch(harmony, AccessTools.Method(typeof(TradeDeal), "AddToTradeables",
                new[] { typeof(Thing), typeof(Transactor) }), nameof(AddToTradeablesPrefix), null);
            Patch(harmony, AccessTools.Method(typeof(TradeDeal), "TryExecute",
                new[] { typeof(bool).MakeByRefType() }), nameof(ExecuteTradePrefix), null);
        }

        private static void Patch(Harmony harmony, MethodBase target, string prefix, string postfix, string finalizer = null)
        {
            if (target == null)
            {
                Log.Error("[Tech Trader Prevention] Required trading hook is unavailable: " + (prefix ?? postfix));
                return;
            }

            try
            {
                harmony.Patch(target,
                    prefix == null ? null : new HarmonyMethod(typeof(TradingPatches), prefix)
                    { priority = prefix == nameof(ExecuteTradePrefix) ? Priority.First : Priority.Last },
                    postfix == null ? null : new HarmonyMethod(typeof(TradingPatches), postfix) { priority = Priority.Last },
                    finalizer: finalizer == null ? null : new HarmonyMethod(typeof(TradingPatches), finalizer));
            }
            catch (Exception exception)
            {
                Log.Error("[Tech Trader Prevention] Could not patch " + target.DeclaringType.FullName + "." + target.Name + ": " + exception);
            }
        }

        // __0 makes the patch independent of parameter-name changes between game builds.
        private static void FactionSourcePostfix(object __instance, Faction __0, object[] __args, ref bool __result)
        {
            if (!__result || !(__instance is IncidentWorker_TraderCaravanArrival)
                || originalSuitabilityDepth != 0 || preservedQuestDepth != 0 || !CaravanRulesEnabled())
                return;

            TechTraderSettings settings = TechTraderMod.Settings;
            IncidentParms parms = __args.Length > 1 ? __args[1] as IncidentParms : null;
            if (PreserveQuest(parms))
                return;

            // In fallback mode the normal game can pick a faction first. The late
            // resolver below then attempts a compatible replacement, retaining the
            // original only if there genuinely is no suitable replacement.
            if (settings.AllowOriginalWhenNoReplacement)
                return;

            __result = TradingPolicy.FactionAllowed(__0);
        }

        private static void ResolvePrefix(object __instance, IncidentParms __0, out bool __state)
        {
            __state = __instance is IncidentWorker_TraderCaravanArrival && CaravanRulesEnabled() && PreserveQuest(__0);
            if (__state)
                preservedQuestDepth++;
        }

        private static Exception ResolveFinalizer(bool __state, Exception __exception)
        {
            // Older game versions pass only a Map to faction suitability checks.
            // Carry the quest exception through the resolver without losing it, and
            // always restore it even if another mod's resolver throws an exception.
            if (__state)
                preservedQuestDepth--;
            return __exception;
        }

        private static void ResolvePostfix(object __instance, IncidentParms __0, ref bool __result)
        {
            IncidentWorker_TraderCaravanArrival worker = __instance as IncidentWorker_TraderCaravanArrival;
            if (!__result || worker == null || __0 == null || replacementDepth != 0
                || !CaravanRulesEnabled() || PreserveQuest(__0)
                || TradingPolicy.FactionAllowed(__0.faction))
                return;

            TechTraderSettings settings = TechTraderMod.Settings;
            if (!settings.ReplaceDisallowedCaravans)
            {
                __result = false;
                Trace("Cancelled a caravan from a disallowed faction.");
                return;
            }

            Faction replacement = PickReplacement(worker, __0);
            if (replacement == null)
            {
                __result = settings.AllowOriginalWhenNoReplacement;
                Trace(__result ? "No eligible replacement; allowing the original caravan by configuration."
                    : "No eligible replacement; cancelled the caravan.");
                return;
            }

            // Re-run the normal resolver once, so it can choose a valid trader kind
            // for the new faction and retain any other mods' suitability logic.
            // A depth guard prevents recursive replacement, and a copy preserves
            // the original event if a compatibility patch rejects the replacement.
            IncidentParms retry = __0.ShallowCopy();
            retry.faction = replacement;
            retry.traderKind = null;
            bool resolved = false;
            try
            {
                replacementDepth++;
                resolved = resolveMethod != null && (bool)resolveMethod.Invoke(worker, new object[] { retry });
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Tech Trader Prevention] Caravan replacement could not be resolved; following the configured fallback. "
                    + exception.GetBaseException().Message, 189314201);
            }
            finally
            {
                replacementDepth--;
            }

            if (resolved && retry.faction != null && TradingPolicy.FactionAllowed(retry.faction))
            {
                __0.faction = retry.faction;
                __0.traderKind = retry.traderKind;
                __0.spawnCenter = retry.spawnCenter;
                Trace("Replaced a disallowed caravan faction with " + retry.faction.Name + ".");
            }
            else
            {
                __result = settings.AllowOriginalWhenNoReplacement;
                Trace(__result ? "Replacement rejected by the event resolver; allowing the original caravan by configuration."
                    : "Replacement rejected by the event resolver; cancelled the caravan.");
            }
        }

        private static Faction PickReplacement(IncidentWorker_TraderCaravanArrival worker, IncidentParms parms)
        {
            if (factionSourceMethod == null || Find.FactionManager == null)
                return null;

            // Keep the game's normal pass before its desperate/temperature-relaxed
            // pass. Do not recreate or weaken relationship, layer, or DLC checks.
            for (int pass = 0; pass < 2; pass++)
            {
                List<Faction> choices = new List<Faction>();
                foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
                {
                    if (faction == null || faction == parms.faction || faction.IsPlayer
                        || faction.defeated || !TradingPolicy.FactionAllowed(faction))
                        continue;

                    try
                    {
                        originalSuitabilityDepth++;
                        object target = sourceUsesMap ? (object)(parms.target as Map) : parms;
                        if ((bool)factionSourceMethod.Invoke(worker, new[] { (object)faction, target, pass != 0 }))
                            choices.Add(faction);
                    }
                    catch (Exception exception)
                    {
                        Log.WarningOnce("[Tech Trader Prevention] A faction suitability check failed; the faction was skipped. "
                            + exception.GetBaseException().Message, 189314202);
                    }
                    finally
                    {
                        originalSuitabilityDepth--;
                    }
                }

                if (choices.Count != 0)
                    return choices[Rand.Range(0, choices.Count)];
            }
            return null;
        }

        private static bool AddToTradeablesPrefix(Thing __0, Transactor __1)
        {
            // Apply only to offered stock. Selling or gifting colony goods remains
            // vanilla, and rejected stock stays with its original trader/owner.
            return __1 != Transactor.Trader || TradeSession.giftMode
                || TradingPolicy.OfferedThingAllowed(__0, TradeSession.trader);
        }

        private static bool ExecuteTradePrefix(TradeDeal __instance, ref bool __0, ref bool __result)
        {
            if (TradeSession.giftMode || !TradingPolicy.ChannelEnabled(TradeSession.trader))
                return true;

            // Recheck the actual purchase, including stock another mod may have
            // added after our normal list filter. This also handles a technology
            // or settings change while the trade dialog remains open.
            foreach (Tradeable tradeable in __instance.AllTradeables)
            {
                if (tradeable == null || tradeable.IsCurrency || tradeable.ActionToDo != TradeAction.PlayerBuys)
                    continue;
                Thing offered = tradeable.FirstThingTrader;
                if (offered == null || TradingPolicy.OfferedThingAllowed(offered, TradeSession.trader))
                    continue;

                __0 = false;
                __result = false;
                Messages.Message("TTP_TradeRejected".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return true;
        }

        private static bool CaravanRulesEnabled()
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            return settings != null && settings.TraderEnabled && settings.FilterCaravans;
        }

        private static bool PreserveQuest(IncidentParms parms)
        {
            if (parms == null || !TechTraderMod.Settings.PreserveQuestTraders)
                return false;
            return parms.quest != null || !string.IsNullOrEmpty(parms.questTag);
        }

        private static void Trace(string message)
        {
            if (TechTraderMod.Settings.DeveloperLogging)
                Log.Message("[Tech Trader Prevention] " + message);
        }
    }
}
