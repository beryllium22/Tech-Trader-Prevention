using RimWorld;
using RimWorld.Planet;
using TechTraderPrevention.Core;
using Verse;

namespace TechTraderPrevention.Trading
{
    /// <summary>
    /// Evaluates a trader's offered goods without deleting stock, changing definitions,
    /// or restricting the colony's sale-side inventory.
    /// </summary>
    public static class TradingPolicy
    {
        public static bool ChannelEnabled(ITrader trader)
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            if (settings == null || !settings.TraderEnabled || trader == null)
                return false;

            if (trader is TradeShip || (trader.TraderKind != null && trader.TraderKind.orbital))
                return settings.FilterOrbitalTraders;
            if (trader is Settlement)
                return settings.FilterSettlements;
            if (trader is Pawn)
                return settings.FilterCaravans;
            return settings.FilterOtherTraders;
        }

        public static bool FactionAllowed(Faction faction, bool orbital = false)
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            if (settings == null || !settings.TraderEnabled)
                return true;

            if (faction == null)
            {
                // Independent ships have no FactionDef to inspect. The player can
                // exempt them or apply a configurable assumed technology level.
                if (settings.AllowFactionlessTraders)
                    return true;
                return orbital && LevelAllowed(settings.OrbitalTraderTechLevel);
            }

            if (DefPolicy.FactionBlocked(faction))
                return false;
            if (DefPolicy.FactionExempt(faction))
                return true;
            return LevelAllowed(DefPolicy.EffectiveTech(faction));
        }

        public static bool LevelAllowed(TechLevel level)
        {
            TechTraderSettings settings = TechTraderMod.Settings;
            return TechRules.Allows((int)level, (int)ColonyTech.CurrentLevel,
                settings.TraderRule, settings.AllowedTechAbove, settings.AllowedTechBelow);
        }

        public static bool OfferedThingAllowed(Thing thing, ITrader trader)
        {
            if (thing == null || thing.def == null || !ChannelEnabled(trader))
                return true;

            TechTraderSettings settings = TechTraderMod.Settings;
            // Installed buildings are sold inside MinifiedThing wrappers, whose
            // own tech level is often Undefined. Inspect the actual product while
            // leaving the wrapper and its ownership entirely untouched.
            Thing product = thing.GetInnerIfMinified() ?? thing;
            ThingDef productDef = product.def ?? thing.def;

            // Silver must remain available for balancing the transaction. Royal
            // Favor uses a special Tradeable and never enters AddToTradeables as
            // an ordinary Thing. Mod currencies can use a per-def exemption.
            if (productDef == ThingDefOf.Silver)
                return true;
            // Explicit block lists win over ordinary exemption lists and the pawn
            // category exception. Currency is the sole transaction-safety exception.
            if (DefPolicy.TradeBlocked(thing.def) || DefPolicy.TradeBlocked(productDef)
                || DefPolicy.FactionBlocked(trader.Faction))
                return false;
            if (product is Pawn && !settings.FilterPawnTrade)
                return true;
            if (DefPolicy.TradeExempt(thing.def) || DefPolicy.TradeExempt(productDef))
                return true;

            bool orbital = trader is TradeShip || (trader.TraderKind != null && trader.TraderKind.orbital);
            if (!FactionAllowed(trader.Faction, orbital))
                return false;
            if (!settings.FilterTraderItems)
                return true;

            TechLevel level = DefPolicy.EffectiveTech(productDef);
            if (level == TechLevel.Undefined)
                return settings.AllowUndefinedItemTech;
            return LevelAllowed(level);
        }
    }
}
