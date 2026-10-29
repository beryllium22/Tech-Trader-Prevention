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

        [ThreadStatic] private static object suitabilityWorker;
        [ThreadStatic] private static Faction suitabilityFaction;
        [ThreadStatic] private static object suitabilityContext;
        [ThreadStatic] private static object replacementWorker;
        [ThreadStatic] private static IncidentParms replacementParms;
        [ThreadStatic] private static IncidentScope currentIncident;
        [ThreadStatic] private static TradeBuildScope currentTradeBuild;

        private static readonly Dictionary<Type, FieldInfo[]> ParameterFields = new Dictionary<Type, FieldInfo[]>();

        private sealed class IncidentScope
        {
            internal IncidentScope Previous;
            internal object Worker;
            internal IncidentParms Parms;
            internal int SettingsRevision;
            internal int ColonyLevel;
        }

        private sealed class TradeBuildScope
        {
            internal TradeBuildScope Previous;
            internal TradeDeal Deal;
            internal ITrader Trader;
            internal int SettingsRevision;
            internal TradingPolicy.OfferContext Context;
        }

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
                }
                else
                {
                    factionSourceMethod = null;
                }
            }
            Patch(harmony, factionSourceMethod, "Caravan faction suitability", null,
                sourceUsesMap ? nameof(FactionSourceMapPostfix) : nameof(FactionSourceParmsPostfix));
            Patch(harmony, resolveMethod, "Caravan faction resolution", nameof(ResolvePrefix), nameof(ResolvePostfix), nameof(ResolveFinalizer));
            Patch(harmony, AccessTools.Method(worker, "CanFireNowSub", new[] { typeof(IncidentParms) }), "Quest caravan availability",
                nameof(ResolvePrefix), null, nameof(ResolveFinalizer));
            Patch(harmony, AccessTools.Method(typeof(TradeDeal), "AddAllTradeables", Type.EmptyTypes), "Trade stock evaluation scope",
                nameof(BuildTradePrefix), null, nameof(BuildTradeFinalizer));
            Patch(harmony, AccessTools.Method(typeof(TradeDeal), "AddToTradeables",
                new[] { typeof(Thing), typeof(Transactor) }), "Trader offered-stock filter", nameof(AddToTradeablesPrefix), null);
            Patch(harmony, AccessTools.Method(typeof(TradeDeal), "TryExecute",
                new[] { typeof(bool).MakeByRefType() }), "Trade purchase validation", nameof(ExecuteTradePrefix), null);
        }

        private static void Patch(Harmony harmony, MethodBase target, string feature, string prefix, string postfix, string finalizer = null)
        {
            CompatibilityDiagnostics.Patch(harmony, target, feature,
                prefix == null ? null : new HarmonyMethod(typeof(TradingPatches), prefix)
                {
                    // Scope setup must run before ordinary prefixes that may
                    // replace the method; Harmony can skip later stateful prefixes.
                    priority = prefix == nameof(AddToTradeablesPrefix) ? Priority.Last : Priority.First
                },
                postfix == null ? null : new HarmonyMethod(typeof(TradingPatches), postfix) { priority = Priority.Last },
                finalizer == null ? null : new HarmonyMethod(typeof(TradingPatches), finalizer));
        }

        // Typed postfixes avoid Harmony allocating/boxing object[] arguments for
        // every candidate check, even with restrictions disabled.
        private static void FactionSourceMapPostfix(object __instance, Faction __0, Map __1, ref bool __result)
        {
            FilterFactionSource(__instance, __0, null, __1, ref __result);
        }

        private static void FactionSourceParmsPostfix(object __instance, Faction __0, IncidentParms __1, ref bool __result)
        {
            FilterFactionSource(__instance, __0, __1, __1 == null ? null : __1.target as Map, ref __result);
        }

        private static void FilterFactionSource(object worker, Faction faction, IncidentParms parms, Map map, ref bool result)
        {
            if (!result || !(worker is IncidentWorker_TraderCaravanArrival) || !CaravanRulesEnabled())
                return;

            object context = sourceUsesMap ? (object)map : parms;
            if (ReferenceEquals(suitabilityWorker, worker) && ReferenceEquals(suitabilityFaction, faction)
                && ReferenceEquals(suitabilityContext, context))
                return;

            IncidentScope scope = MatchingScope(worker, parms, map);
            TechTraderSettings settings = TechTraderMod.Settings;
            if (PreserveQuest(parms ?? scope?.Parms))
                return;

            // In fallback mode the normal game can pick a faction first. The late
            // resolver below then attempts a compatible replacement, retaining the
            // original only if there genuinely is no suitable replacement.
            if (settings.AllowOriginalWhenNoReplacement)
                return;

            if (scope == null)
            {
                result = TradingPolicy.FactionAllowed(faction);
                return;
            }
            if (scope.SettingsRevision != TechTraderMod.SettingsRevision)
            {
                scope.ColonyLevel = (int)ColonyTech.CurrentLevel;
                scope.SettingsRevision = TechTraderMod.SettingsRevision;
            }
            result = TradingPolicy.FactionAllowed(faction, false, settings, scope.ColonyLevel);
        }

        private static IncidentScope MatchingScope(object worker, IncidentParms parms, Map map)
        {
            IncidentScope scope = currentIncident;
            if (scope == null || !ReferenceEquals(scope.Worker, worker)) return null;
            return parms != null ? (ReferenceEquals(scope.Parms, parms) ? scope : null)
                : (ReferenceEquals(scope.Parms?.target, map) ? scope : null);
        }

        private static void ResolvePrefix(object __instance, IncidentParms __0, out IncidentScope __state)
        {
            __state = null;
            if (!(__instance is IncidentWorker_TraderCaravanArrival) || !CaravanRulesEnabled()) return;
            // Record ordinary incidents too: a nested unrelated incident must not
            // inherit the outer quest's exemption simply because it is on the stack.
            __state = new IncidentScope
            {
                Previous = currentIncident,
                Worker = __instance,
                Parms = __0,
                ColonyLevel = (int)ColonyTech.CurrentLevel,
                SettingsRevision = TechTraderMod.SettingsRevision
            };
            currentIncident = __state;
        }

        private static Exception ResolveFinalizer(IncidentScope __state, Exception __exception)
        {
            if (__state != null) currentIncident = __state.Previous;
            return __exception;
        }

        private static void ResolvePostfix(object __instance, IncidentParms __0, ref bool __result)
        {
            IncidentWorker_TraderCaravanArrival worker = __instance as IncidentWorker_TraderCaravanArrival;
            if (!__result || worker == null || __0 == null
                || (ReferenceEquals(replacementWorker, worker) && ReferenceEquals(replacementParms, __0))
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

            // Retain the IncidentParms identity for mods which attach state to it.
            // Successful resolver changes to fields beyond faction/traderKind must
            // survive too. On failure, restore writable fields from a shallow
            // snapshot; arbitrary nested-object/external mod side effects cannot
            // be undone here (nor by resolving a shallow copy).
            FieldInfo[] fields = GetParameterFields(__0.GetType());
            object[] before = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++) before[i] = fields[i].GetValue(__0);
            object previousWorker = replacementWorker;
            IncidentParms previousParms = replacementParms;
            __0.faction = replacement;
            __0.traderKind = null;
            bool resolved = false;
            try
            {
                replacementWorker = worker;
                replacementParms = __0;
                resolved = resolveMethod != null && (bool)resolveMethod.Invoke(worker, new object[] { __0 });
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Tech Trader Prevention] Caravan replacement could not be resolved; following the configured fallback. "
                    + exception.GetBaseException().Message, 189314201);
            }
            finally
            {
                replacementWorker = previousWorker;
                replacementParms = previousParms;
            }

            if (resolved && __0.faction != null && TradingPolicy.FactionAllowed(__0.faction))
            {
                Trace("Replaced a disallowed caravan faction with " + __0.faction.Name + ".");
            }
            else
            {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(__0, before[i]);
                __result = settings.AllowOriginalWhenNoReplacement;
                Trace(__result ? "Replacement rejected by the event resolver; allowing the original caravan by configuration."
                    : "Replacement rejected by the event resolver; cancelled the caravan.");
            }
        }

        private static FieldInfo[] GetParameterFields(Type type)
        {
            lock (ParameterFields)
            {
                FieldInfo[] fields;
                if (ParameterFields.TryGetValue(type, out fields)) return fields;
                var writable = new List<FieldInfo>();
                for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                    foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!field.IsInitOnly && !field.IsLiteral) writable.Add(field);
                fields = writable.ToArray();
                ParameterFields.Add(type, fields);
                return fields;
            }
        }

        private static Faction PickReplacement(IncidentWorker_TraderCaravanArrival worker, IncidentParms parms)
        {
            if (factionSourceMethod == null || Find.FactionManager == null)
                return null;

            TechTraderSettings settings = TechTraderMod.Settings;
            int colonyLevel = (int)ColonyTech.CurrentLevel;
            IncidentParms candidateParms = parms.ShallowCopy();

            // Keep the game's normal pass before its desperate/temperature-relaxed
            // pass. Do not recreate or weaken relationship, layer, or DLC checks.
            for (int pass = 0; pass < 2; pass++)
            {
                List<Faction> choices = new List<Faction>();
                foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
                {
                    if (faction == null || faction == parms.faction || faction.IsPlayer
                        || faction.defeated || !TradingPolicy.FactionAllowed(faction, false, settings, colonyLevel))
                        continue;

                    object previousWorker = suitabilityWorker;
                    Faction previousFaction = suitabilityFaction;
                    object previousContext = suitabilityContext;
                    try
                    {
                        // Mimic normal candidate selection, before a faction or a
                        // faction-specific trader kind has been selected.
                        candidateParms.faction = null;
                        candidateParms.traderKind = null;
                        object target = sourceUsesMap ? (object)(parms.target as Map) : candidateParms;
                        suitabilityWorker = worker;
                        suitabilityFaction = faction;
                        suitabilityContext = target;
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
                        suitabilityWorker = previousWorker;
                        suitabilityFaction = previousFaction;
                        suitabilityContext = previousContext;
                    }
                }

                if (choices.Count != 0)
                    return choices[Rand.Range(0, choices.Count)];
            }
            return null;
        }

        private static void BuildTradePrefix(TradeDeal __instance, out TradeBuildScope __state)
        {
            __state = null;
            if (TradeSession.giftMode || TechTraderMod.Settings == null || !TechTraderMod.Settings.TraderEnabled)
                return;

            ITrader trader = TradeSession.trader;
            __state = new TradeBuildScope
            {
                Previous = currentTradeBuild,
                Deal = __instance,
                Trader = trader,
                Context = TradingPolicy.CreateOfferContext(trader),
                SettingsRevision = TechTraderMod.SettingsRevision
            };
            currentTradeBuild = __state;
        }

        private static Exception BuildTradeFinalizer(TradeBuildScope __state, Exception __exception)
        {
            if (__state != null) currentTradeBuild = __state.Previous;
            return __exception;
        }

        private static bool AddToTradeablesPrefix(TradeDeal __instance, Thing __0, Transactor __1)
        {
            // Only offered stock is filtered. A scope amortizes trader/colony
            // evaluation over hundreds of items without retaining session objects.
            if (__1 != Transactor.Trader || TradeSession.giftMode) return true;
            ITrader trader = TradeSession.trader;
            TradeBuildScope scope = currentTradeBuild;
            if (scope == null || !ReferenceEquals(scope.Deal, __instance) || !ReferenceEquals(scope.Trader, trader))
                return TradingPolicy.OfferedThingAllowed(__0, trader);

            if (scope.SettingsRevision != TechTraderMod.SettingsRevision)
            {
                scope.Context = TradingPolicy.CreateOfferContext(trader);
                scope.SettingsRevision = TechTraderMod.SettingsRevision;
            }
            return TradingPolicy.OfferedThingAllowed(__0, scope.Context);
        }

        private static bool ExecuteTradePrefix(TradeDeal __instance, ref bool __0, ref bool __result)
        {
            if (TradeSession.giftMode) return true;
            TradingPolicy.OfferContext context = TradingPolicy.CreateOfferContext(TradeSession.trader);
            if (!context.Enabled) return true;

            // Recheck the actual purchase, including stock another mod may have
            // added after our normal list filter. This also handles a technology
            // or settings change while the trade dialog remains open.
            foreach (Tradeable tradeable in __instance.AllTradeables)
            {
                if (tradeable == null || tradeable.IsCurrency || tradeable.ActionToDo != TradeAction.PlayerBuys)
                    continue;
                // A mod can combine heterogeneous objects into one tradeable.
                // Checking only FirstThingTrader would permit prohibited objects
                // hidden behind an allowed first stack in that combined row.
                List<Thing> offered = tradeable.thingsTrader;
                if (offered == null) continue;
                foreach (Thing thing in offered)
                {
                    if (thing == null || thing.Destroyed || thing.stackCount <= 0
                        || TradingPolicy.OfferedThingAllowed(thing, context))
                        continue;
                    __0 = false;
                    __result = false;
                    Messages.Message("TTP_TradeRejected".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
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
