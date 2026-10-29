using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TechTraderPrevention
{
    /// <summary>Install independent hooks and inspect their current owners only on request.</summary>
    internal static class CompatibilityDiagnostics
    {
        private sealed class Hook
        {
            internal string Feature;
            internal MethodBase Target;
            internal MethodInfo Prefix;
            internal MethodInfo Postfix;
            internal MethodInfo Finalizer;
            internal string Error;
        }

        private static readonly List<Hook> Hooks = new List<Hook>();
        internal static string InstallationSummary => Hooks.Count + " hook targets registered; "
            + Hooks.Count(hook => hook.Error != null) + " installation problems. Generate a report to check their current state.";

        internal static bool Patch(Harmony harmony, MethodBase target, string feature,
            HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null)
        {
            var hook = new Hook
            {
                Feature = feature, Target = target, Prefix = prefix?.method,
                Postfix = postfix?.method, Finalizer = finalizer?.method
            };
            Hooks.Add(hook);
            if (target == null)
            {
                hook.Error = "Target method is unavailable in this game build.";
                Log.Warning("[Tech Trader Prevention] " + feature + ": " + hook.Error);
                return false;
            }
            try
            {
                harmony.Patch(target, prefix: prefix, postfix: postfix, finalizer: finalizer);
                return true;
            }
            catch (Exception exception)
            {
                hook.Error = exception.GetBaseException().Message;
                Log.Error("[Tech Trader Prevention] Could not install " + feature + ": " + exception);
                return false;
            }
        }

        internal static void RecordFailure(string feature, Exception exception)
        {
            Hooks.Add(new Hook { Feature = feature, Error = exception.GetBaseException().Message });
            Log.Error("[Tech Trader Prevention] Could not install " + feature + " hooks: " + exception);
        }

        internal static void WriteReport()
        {
            try { Log.Message(BuildReport()); }
            catch (Exception exception)
            {
                Log.Warning("[Tech Trader Prevention] Could not finish the compatibility report: " + exception);
            }
        }

        private static string BuildReport()
        {
            var report = new StringBuilder();
            report.AppendLine("[Tech Trader Prevention] Compatibility report");
            report.AppendLine("Mod assembly: " + typeof(TechTraderMod).Assembly.GetName().Version
                + " | RimWorld: " + VersionControl.CurrentVersionStringWithRev);
            report.AppendLine("Harmony assembly: " + typeof(Harmony).Assembly.GetName().Version);
            report.AppendLine("Loaded definitions: " + DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count
                + " research; " + DefDatabase<ThingDef>.AllDefsListForReading.Count + " things; "
                + DefDatabase<RecipeDef>.AllDefsListForReading.Count + " recipes.");
            TechTraderSettings settings = TechTraderMod.Settings;
            report.AppendLine("Trading: " + settings.TraderEnabled + " / " + settings.TraderRule
                + " | colony mode: " + settings.ColonyTechMode + " | refresh ticks: " + settings.ResearchRefreshTicks
                + " | salvage: " + settings.SalvageEnabled + " / " + settings.SalvageTriggerMode);
            if (Current.Game != null) report.AppendLine("Effective colony technology: " + ColonyTech.CurrentLevel);
            report.AppendLine("Hook inspection (other owners sharing a target do not by themselves indicate a conflict):");
            foreach (Hook hook in Hooks)
            {
                report.Append("- ").Append(hook.Feature).Append(": ");
                if (hook.Error != null)
                {
                    report.AppendLine("INSTALLATION PROBLEM: " + hook.Error);
                    continue;
                }
                Patches patches = Harmony.GetPatchInfo(hook.Target);
                bool present = patches != null && HasPatch(patches.Prefixes, hook.Prefix)
                    && HasPatch(patches.Postfixes, hook.Postfix) && HasPatch(patches.Finalizers, hook.Finalizer);
                report.AppendLine(present ? "expected callbacks present" : "CALLBACKS MISSING OR REMOVED");
                report.AppendLine("  " + hook.Target.DeclaringType?.FullName + "." + hook.Target.Name
                    + "(" + string.Join(", ", hook.Target.GetParameters().Select(parameter => parameter.ParameterType.Name)) + ")");
                if (patches == null) continue;
                AppendPatches(report, "prefix", patches.Prefixes);
                AppendPatches(report, "postfix", patches.Postfixes);
                AppendPatches(report, "transpiler", patches.Transpilers);
                AppendPatches(report, "finalizer", patches.Finalizers);
            }
            foreach (string warning in DefPolicy.ConfigurationWarnings)
                report.AppendLine("Configuration: " + warning);
            report.AppendLine("Loaded mods in load order:");
            foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
                report.AppendLine("- " + mod.PackageIdPlayerFacing + " | " + mod.Name);
            report.AppendLine("This report checks installed hooks, not gameplay compatibility. It stays in the local player log; nothing is uploaded.");
            return report.ToString();
        }

        private static bool HasPatch(IEnumerable<Patch> patches, MethodInfo expected)
            => expected == null || patches.Any(patch => patch.owner == TechTraderMod.HarmonyId && patch.PatchMethod == expected);

        private static void AppendPatches(StringBuilder report, string kind, IEnumerable<Patch> patches)
        {
            foreach (Patch patch in patches.OrderByDescending(value => value.priority).ThenBy(value => value.index))
            {
                report.AppendLine("  " + kind + ": " + patch.owner + " | priority " + patch.priority
                    + " | " + patch.PatchMethod?.DeclaringType?.FullName + "." + patch.PatchMethod?.Name);
                if (patch.before != null && patch.before.Length > 0)
                    report.AppendLine("    before: " + string.Join(", ", patch.before));
                if (patch.after != null && patch.after.Length > 0)
                    report.AppendLine("    after: " + string.Join(", ", patch.after));
            }
        }
    }
}
