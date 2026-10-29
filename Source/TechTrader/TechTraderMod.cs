using System;
using HarmonyLib;
using TechTraderPrevention.Trading;
using UnityEngine;
using Verse;

namespace TechTraderPrevention
{
    public sealed class TechTraderMod : Mod
    {
        internal const string HarmonyId = "summersausages2.techtraderprevention";
        public static TechTraderSettings Settings { get; private set; } = new TechTraderSettings();
        public static int SettingsRevision { get; private set; }
        public static TechTraderMod Instance { get; private set; }
        private readonly SettingsWindow window = new SettingsWindow();

        public TechTraderMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<TechTraderSettings>();
            Settings.Normalize();
            SettingsRevision++;
            var harmony = new Harmony(HarmonyId);
            Install("research", () => ColonyTech.Apply(harmony));
            Install("trading", () => TradingPatches.Apply(harmony));
            Install("equipment salvage", () => SalvagePatches.Apply(harmony));
        }

        private static void Install(string feature, Action install)
        {
            try { install(); }
            catch (Exception e) { CompatibilityDiagnostics.RecordFailure(feature, e); }
        }

        public override string SettingsCategory() => "TTP_Name".Translate();
        public override void DoSettingsWindowContents(Rect inRect) => window.Draw(inRect);
        public override void WriteSettings()
        {
            Settings.Normalize();
            Changed();
            base.WriteSettings();
        }

        public static void Changed()
        {
            Settings.Normalize();
            unchecked { SettingsRevision++; }
            ColonyTech.Invalidate();
        }
    }
}
