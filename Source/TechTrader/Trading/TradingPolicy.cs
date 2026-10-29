using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using TechTraderPrevention.Core;
using Verse;

namespace TechTraderPrevention.Trading
{
    /// <summary>
    /// Evaluates offered goods without deleting stock, changing definitions, or
    /// restricting the colony's sale-side inventory.
    /// </summary>
    public static class TradingPolicy
    {
        private enum TraderChannel { Caravan, Settlement, Orbital, Other }
        private static readonly HashSet<string> ReportedGetterFailures = new HashSet<string>();

        // This snapshot lives for one stock-list build or one transaction check.
        // It is never cached by game tick or retained across trade sessions.
        internal readonly struct OfferContext
        {
            internal readonly TechTraderSettings Settings;
            internal readonly int ColonyLevel;
            internal readonly bool Enabled;
            internal readonly bool FactionBlocked;
            internal readonly bool FactionAllowed;

            internal OfferContext(TechTraderSettings settings, int colonyLevel, bool blocked, bool allowed)
            {
                Settings = settings;
                ColonyLevel = colonyLevel;
                Enabled = true;
                FactionBlocked = blocked;
                FactionAllowed = allowed;
            }
        }

        public static bool ChannelEnabled(ITrader trader)
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            return settings != null && settings.TraderEnabled && trader != null
                && ChannelEnabled(settings, ChannelOf(trader));
        }

        private static bool ChannelEnabled(TechTraderSettings settings, TraderChannel channel)
        {
            switch (channel)
            {
                case TraderChannel.Caravan: return settings.FilterCaravans;
                case TraderChannel.Settlement: return settings.FilterSettlements;
                case TraderChannel.Orbital: return settings.FilterOrbitalTraders;
                default: return settings.FilterOtherTraders;
            }
        }

        private static TraderChannel ChannelOf(ITrader trader)
        {
            // These built-in types need no virtual/custom TraderKind getter call.
            if (trader is TradeShip) return TraderChannel.Orbital;
            if (trader is Settlement) return TraderChannel.Settlement;
            if (trader is Pawn) return TraderChannel.Caravan;

            try
            {
                TraderKindDef kind = trader.TraderKind;
                return kind != null && kind.orbital ? TraderChannel.Orbital : TraderChannel.Other;
            }
            catch (Exception exception)
            {
                ReportGetterFailure(trader, "TraderKind", exception, "using the Other trader channel");
                return TraderChannel.Other;
            }
        }

        private static Faction ReadFaction(ITrader trader)
        {
            try { return trader.Faction; }
            catch (Exception exception)
            {
                ReportGetterFailure(trader, "Faction", exception,
                    "using the configured factionless-trader policy; item rules remain active");
                return null;
            }
        }

        private static void ReportGetterFailure(ITrader trader, string member, Exception exception, string fallback)
        {
            string key = trader.GetType().FullName + "." + member;
            lock (ReportedGetterFailures)
            {
                if (!ReportedGetterFailures.Add(key)) return;
            }
            Log.Warning("[Tech Trader Prevention] Could not read " + key + "; " + fallback
                + ". " + exception.GetBaseException().Message);
        }

        internal static OfferContext CreateOfferContext(ITrader trader)
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            if (settings == null || !settings.TraderEnabled || trader == null)
                return default(OfferContext);
            TraderChannel channel = ChannelOf(trader);
            if (!ChannelEnabled(settings, channel))
                return default(OfferContext);

            Faction faction = ReadFaction(trader);
            int colonyLevel = (int)ColonyTech.CurrentLevel;
            bool blocked = DefPolicy.FactionBlocked(faction);
            bool allowed = !blocked && FactionAllowed(faction, channel == TraderChannel.Orbital, settings, colonyLevel);
            return new OfferContext(settings, colonyLevel, blocked, allowed);
        }

        public static bool FactionAllowed(Faction faction, bool orbital = false)
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            return settings == null || !settings.TraderEnabled
                || FactionAllowed(faction, orbital, settings, (int)ColonyTech.CurrentLevel);
        }

        internal static bool FactionAllowed(Faction faction, bool orbital, TechTraderSettings settings, int colonyLevel)
        {
            if (faction == null)
            {
                if (settings.AllowFactionlessTraders) return true;
                return orbital && LevelAllowed(settings.OrbitalTraderTechLevel, settings, colonyLevel);
            }
            if (DefPolicy.FactionBlocked(faction)) return false;
            if (DefPolicy.FactionExempt(faction)) return true;
            return LevelAllowed(DefPolicy.EffectiveTech(faction), settings, colonyLevel);
        }

        public static bool LevelAllowed(TechLevel level)
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            return settings == null || LevelAllowed(level, settings, (int)ColonyTech.CurrentLevel);
        }

        private static bool LevelAllowed(TechLevel level, TechTraderSettings settings, int colonyLevel)
        {
            return TechRules.Allows((int)level, colonyLevel,
                settings.TraderRule, settings.AllowedTechAbove, settings.AllowedTechBelow);
        }

        public static bool OfferedThingAllowed(Thing thing, ITrader trader)
        {
            return thing == null || thing.def == null || OfferedThingAllowed(thing, CreateOfferContext(trader));
        }

        internal static bool OfferedThingAllowed(Thing thing, OfferContext context)
        {
            if (!context.Enabled || thing == null || thing.def == null)
                return true;

            TechTraderSettings settings = context.Settings;
            // An advanced installed building must not inherit its wrapper's
            // Undefined technology. Ownership and the wrapper stay untouched.
            Thing product = thing.GetInnerIfMinified() ?? thing;
            ThingDef productDef = product.def ?? thing.def;
            bool wrapped = !ReferenceEquals(productDef, thing.def);

            // Silver balances transactions; Royal Favor is a special Tradeable.
            // Custom currencies can declare a per-definition exemption.
            if (productDef == ThingDefOf.Silver) return true;
            if (context.FactionBlocked || DefPolicy.TradeBlocked(productDef)
                || (wrapped && DefPolicy.TradeBlocked(thing.def)))
                return false;
            if (product is Pawn && !settings.FilterPawnTrade) return true;
            if (DefPolicy.TradeExempt(productDef) || (wrapped && DefPolicy.TradeExempt(thing.def)))
                return true;
            if (!context.FactionAllowed) return false;
            if (!settings.FilterTraderItems) return true;

            TechLevel level = DefPolicy.EffectiveTech(productDef);
            if (level == TechLevel.Undefined) return settings.AllowUndefinedItemTech;
            return LevelAllowed(level, settings, context.ColonyLevel);
        }
    }
}
