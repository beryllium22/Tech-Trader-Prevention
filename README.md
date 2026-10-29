# Tech Trader Prevention

A source-based rewrite of the original RimWorld mod: configurable trader technology restrictions, colony technology derived from research, and optional equipment salvage.

**Game builds:** RimWorld 1.4, 1.5, 1.6. **Dependency:** [Harmony](https://github.com/pardeike/HarmonyRimWorld). **Required DLC:** none.

Version 2.0.0 compiles separately against all three game APIs and includes deterministic policy tests. It has **not yet been playtested inside RimWorld**. See [validation status](docs/VALIDATION.md) and [compatibility notes](docs/COMPATIBILITY.md). RimWorld 1.7 is an unstable branch and is not declared supported.

## Install

1. Download the built `TechTraderPrevention-2.0.0.zip` from this branch's **Actions → Build and verify mod** artifact, or use a repository ZIP containing the committed `1.4`, `1.5`, and `1.6` assembly folders.
2. Extract the mod folder into RimWorld's `Mods` directory. `About/About.xml` must sit immediately inside it, without an extra nested mod folder.
3. Enable Harmony, then Tech Trader Prevention. Disable the old **Prevent Higher Tech Traders** and **Replace Higher Tech Traders** variants.
4. Open **Options → Mod settings → Tech Trader Prevention**.

The old 2023 `RimworldTraderFixer` release is the incomplete legacy version, not this rewrite. Settings from the old package IDs are not migrated. Originals remain in Git history.

## Defaults

| Setting | Default |
|---|---|
| Trader tech rule | Colony tech or lower, with zero higher-tier allowance |
| Colony tech | 75% of eligible base research cost in each tier |
| Tier progression | Earlier nonempty tiers must qualify; Neolithic minimum |
| Minimum finished projects per qualifying tier | 1 |
| Caravan, settlement, orbital and other standard traders | All enabled |
| Individual purchase-item filtering | Enabled |
| Undefined-tech goods | Allowed |
| Pawn trading, Silver and Royal Favor | Preserved by default |
| Quest-linked caravan identities | Preserved |
| Factionless traders | Faction-rule exemption; their items are still filtered |
| Equipment salvage | Off until enabled |
| Salvage trigger | Death |
| Affected pawns | Hostile humanlikes only |
| Eligible gear | Equipped weapons/apparel above the allowed tech ceiling |
| Break chance / survival | 50% per item / fixed 50% materials |
| Protected equipment | Relics, biocoded gear, quest-tagged gear, configured exclusions |

Here, same tech normally means **no higher technology**, allowing lower-tier traders. Choose **Exact** for only the same tier, or **Range** for a custom band above/below it.

## Trader restrictions

The mod filters caravan faction candidates after the game's own suitability checks. A late check handles already-selected factions. Replacement randomly selects a suitable alternative and lets the normal resolver choose its trader kind. A bounded retry avoids recursive replacement. If no alternative exists, cancel the event or explicitly enable the allow-original fallback.

Standard trade sessions filter **purchases without deleting stock**. Colony sales and gifts remain available. An execution-time check rejects prohibited purchases inserted later or left in a dialog after rules change. Reopen a trade dialog after editing settings to refresh its display. Minified items are checked using the contained item's technology rather than the wrapper's undefined tier.

Options include:

- Master switch; separate caravan, settlement, orbital and other standard-trader switches.
- At-or-below, exact, or bounded-range rules and configurable allowances above/below the colony.
- Individual-item filtering, undefined-tech items and optional pawn filtering.
- Replacement/cancellation, no-alternative fallback and quest-identity preservation.
- Factionless exemption and an assumed tier for independent orbital ships.
- Item/faction exemptions, blocklists and tech overrides by internal definition name.

When factionless exemption is off, independent ships use the assumed orbital tier; unknown factionless nonship traders are blocked. Orbital rules restrict purchases rather than removing ships from orbit or changing arrival frequency.

Blocks override ordinary exemptions, including the pawn-category exemption. Silver remains available as transaction currency; Royal Favor is a special currency tradeable. Quest preservation applies to **arrival identity only**: offered stock still follows faction and item purchase rules. Item exemptions bypass technology rules, but not an explicitly blocked faction.

## Research-derived colony technology

| Method | Behavior |
|---|---|
| `TierCompletion` | A tier qualifies after meeting its completion percentage and minimum finished-project count. |
| `HighestCompleted` | Uses the highest eligible finished project; one advanced discovery can promote the colony. |
| `Faction` | Uses the player's faction tier, including changes made by another mod. |
| `Manual` | Uses your selected level. |

For tier completion, choose base-research-cost weighting or equal project weighting. Set a common required percentage and optional overrides for each tier. Configure earlier-tier requirements, how empty tiers behave, minimum/maximum colony tech, and whether faction tech acts as a floor. The minimum tier requires no research. Empty tiers are skipped by default and never unlock a level merely because no projects exist.

Only loaded content definitions count. Normal modded research participates automatically. Exclude specific projects or entire package IDs, override project tiers, include zero-cost/hidden research, and optionally include Anomaly's separate knowledge progression. Knowledge projects use their knowledge cost when included; other projects use base research cost. Finished hidden projects remain counted. Hidden-project detection uses the game API on 1.5/1.6; the same API is absent on 1.4.

Completion/reset hooks invalidate a per-game cache; periodic on-demand refresh catches changes made through other mods. Research is not scanned every tick. Faction/manual modes use cheap live reads. **Status & help** displays the current level and tier progress.

The mod does not rewrite faction definitions, prerequisites, research costs or research-speed penalties. These rules determine this mod's trading/salvage threshold. Use `Faction` mode when another mod should control advancement.

## Optional equipment salvage

Enable salvage to roll independently for each eligible equipped weapon or worn apparel item:

- **Death:** captures gear still equipped/worn when death begins.
- **Downed:** captures gear before a genuine downing transition.
- **DeathOrDowned:** both; nested transitions process one snapshot.

Downing affects weapons only by default; apparel is optional. A weapon already dropped during downing cannot be caught by a later death-only snapshot. Unrelated loose loot, carried inventory and world-caravan events without a map are skipped. Separate later events can roll again for retained apparel if you enable downing-apparel salvage.

Configure player pawns (including colony slaves), hostile/neutral/allied pawns, prisoners separately, humanlikes only, weapons/apparel, and only gear above the allowed ceiling. Ceiling means colony tech plus allowed higher tiers, even if trader restrictions are disabled. Relics, biocoded gear, quest-tagged items and listed definitions can be protected independently.

Recovery supports a fixed 0–100% survival fraction or a fresh random fraction within a chosen range for each broken item. Optionally multiply recovery by remaining hit points. All material types from one item share its sampled percentage. Whole counts round down: **80 steel + 3 components at 50% yields 40 steel + 1 component**. Known-cost gear at 0% is deliberately destroyed without output.

### Which materials drop?

The preferred source is the game's adjusted crafting cost list, including the item's actual stuff and other mods' cost patches. An optional recipe fallback accepts only unambiguous producing recipes that agree on normalized ingredient costs. Unknown ingredients, mixed-stuff ambiguity and uncertain alternatives preserve equipment rather than inventing materials.

This reconstructs **definition-specified crafting costs**, not a historical receipt for a particular crafting bill. Noncraftable/unknown-cost equipment is preserved. “Components” means crafting ingredients: steel, plasteel, cloth, components, advanced components, and other materials actually specified by known costs.

Outputs are staged before removing equipment. New stacks avoid merging with existing resources so a failed placement can roll back only the generated outputs. A stack-count bound prevents malformed definitions from causing extreme spawn loops. Mods that extensively replace destruction/placement may still need integration work.

## DLC and other mods

No DLC is required. The mod uses loaded definitions and the shared game API:

- **Royalty:** preserves quest-linked visit identities and Royal Favor by default.
- **Ideology:** relic protection; pawn-trade exemption by default.
- **Biotech:** discovers normal research dynamically; player-pawn protection and humanlike-only salvage defaults.
- **Anomaly:** separate knowledge progression is excluded unless explicitly enabled.
- **Odyssey:** the 1.6 build retains the game's faction suitability checks, including map/layer/content conditions.

These are implementation safeguards, not a claim of playtesting every combination. Total trade-system replacements, custom incidents or loot workers may need adapters. Multiplayer synchronization is not claimed. Details: [COMPATIBILITY.md](docs/COMPATIBILITY.md).

## Advanced customization

Use exact `defName` identifiers, separated by commas, semicolons or newlines. Technology overrides use `DefName=Industrial` or `DefName=4`; supported names are `Undefined`, `Animal`, `Neolithic`, `Medieval`, `Industrial`, `Spacer`, `Ultra`, `Archotech`. Malformed entries appear as warnings and are ignored. Missing definitions are harmless when content is absent.

Examples (replace names with actual installed definitions):

```text
Item tech overrides: MyMod_Rifle=Industrial, MyMod_Pistol=Medieval
Ignored research: MyMod_DebugResearch, MyMod_UnreachableProject
Ignored research packages: example.contentpack
Salvage exclusions: MyMod_QuestSword
```

Other mod authors can attach this optional extension inside a definition's `modExtensions`, using an XML patch conditional on Tech Trader Prevention being active:

```xml
<li Class="TechTraderPrevention.TechTraderDefExtension">
  <techLevelOverride>4</techLevelOverride>
  <tradeExempt>false</tradeExempt>
  <tradeBlocked>false</tradeBlocked>
  <salvageExcluded>true</salvageExcluded>
  <excludeResearch>false</excludeResearch>
</li>
```

User technology overrides take priority over extension technology. Boolean exemption/block flags combine; a block wins. The extension class is unavailable if this mod is inactive, so compatibility patches must be conditional.

Settings apply to the entire installation. Derived research state is per game and recalculated on load. Removing the mod leaves faction technology unchanged; salvage already performed is an ordinary world change and is not automatically reversed.

## Build

Install a .NET 8 SDK and Python 3, then run:

```bash
bash scripts/build.sh
```

This runs policy scenarios, builds each game target, and creates `dist/TechTraderPrevention-2.0.0.zip` with a SHA-256 manifest. [Krafs.Rimworld.Ref](https://github.com/krafs/RimRef) provides reference-only APIs with Ludeon's permission, so no game installation is required to compile.

Individual commands:

```bash
dotnet run --project Tests/TechTrader.Core.Tests -c Release
dotnet build Source/TechTrader/TechTrader.csproj -c Release -p:RimWorldVersion=1.6
python3 scripts/package.py
```

The runtime ZIP includes only this mod's two assemblies per version, metadata and language files. It never bundles the game, reference assemblies or another Harmony copy. References are pinned to 1.4.3901, 1.5.4409 and 1.6.4871. CI repeats the checks and uploads the install ZIP.

`Source/TechTrader.Core` contains deterministic tech/recovery calculations. `Source/TechTrader/Research`, `Trading` and `Salvage` contain game adapters. Shared settings, overrides and the five-tab settings UI are in `Source/TechTrader`.

The rewrite replaces the legacy missing V2 trader hook, binary-only development setup, placeholder URL and incorrect Harmony dependency metadata. Original packages remain in Git history.
