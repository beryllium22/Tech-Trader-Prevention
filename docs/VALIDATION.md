# Validation and in-game acceptance

This rewrite is checked with deterministic policy scenarios and separate builds
against the RimWorld 1.4, 1.5, and 1.6 reference assemblies. Reference assemblies
verify API compatibility at compile time; they do not run the game, simulate a
save, or prove compatibility with every mod combination.

No RimWorld game installation is available in the development environment used
for this rewrite. The in-game checks below remain to be performed before treating
the rewrite as playtested.

## Automated checks

Run the complete verification and packaging workflow from the repository root:

```bash
bash scripts/build.sh
```

This requires the .NET 8 SDK and Python 3. `DOTNET_COMMAND` can point to a specific
`dotnet` executable. On Windows, the equivalent commands can be run individually:

```text
dotnet run --project Tests/TechTrader.Core.Tests -c Release
dotnet build Source/TechTrader/TechTrader.csproj -c Release -p:RimWorldVersion=1.4
dotnet build Source/TechTrader/TechTrader.csproj -c Release -p:RimWorldVersion=1.5
dotnet build Source/TechTrader/TechTrader.csproj -c Release -p:RimWorldVersion=1.6
python scripts/package.py
```

### Compile matrix

| Game target | Pinned reference package | Output folder |
|---|---|---|
| RimWorld 1.4 | `Krafs.Rimworld.Ref` 1.4.3901 | `1.4/Assemblies` |
| RimWorld 1.5 | `Krafs.Rimworld.Ref` 1.5.4409 | `1.5/Assemblies` |
| RimWorld 1.6 | `Krafs.Rimworld.Ref` 1.6.4871 | `1.6/Assemblies` |

Each target compiles the actual game adapters separately. The outputs contain
this mod's `TechTraderPrevention.dll` and `TechTraderPrevention.Core.dll`. The
packaging script includes only those two DLLs per target, the mod metadata,
language resources, load-folder configuration, and README. It does not include
RimWorld assemblies, Harmony, intermediate files, or debugging symbols.

RimWorld 1.7 is not a declared supported target. Ludeon's October 12, 2026
[announcement](https://ludeon.com/blog/2026/10/announcing-the-waylith-engine-and-update-1-7/)
identifies 1.7 as an unstable branch and 1.6 as the main branch. A future 1.7
compatibility declaration needs its own verification.

### Policy scenarios

The console test suite contains 37 scenario groups covering these behaviors:

- Same-or-lower, exact-tier, and bounded-range trader rules, including invalid
  values and upper-tier boundaries.
- Research-cost versus project-count weighting; exact threshold boundaries;
  per-tier overrides; minimum completed projects; lower-tier prerequisites;
  absent mod/DLC tiers; starting-tier floors; maximum levels; and manual,
  faction, and highest-completed modes.
- Empty, zero-cost, invalid-cost, and out-of-range research data.
- Zero/full material recovery, stack sizes, genuine fractional-material
  flooring, fixed/random yield, a shared sampled yield per item, reversed bounds,
  invalid inputs, and integer overflow prevention.
- A regression where floating-point representation of 70% could incorrectly
  recover six materials from a cost of ten instead of seven.

These tests execute the same pure policy library used by the game adapters.
They do not mock an entire RimWorld game or claim to test Harmony at runtime.

### Game-hook metadata inspection

The following method signatures were independently checked in the actual
reference metadata for all three pinned game versions:

| Hook | 1.4 / 1.5 | 1.6 |
|---|---|---|
| Trader faction suitability | `FactionCanBeGroupSource(Faction, Map, bool)` | `FactionCanBeGroupSource(Faction, IncidentParms, bool)` |
| Trader incident resolution | `TryResolveParmsGeneral(IncidentParms)` | Same |
| Offering trade stock | `TradeDeal.AddToTradeables(Thing, Transactor)` | Same |
| Final trade execution | `TradeDeal.TryExecute(out bool)` | Same |
| Pawn death | `Pawn.Kill(DamageInfo?, Hediff)` | Same |

The trading adapter handles the observed faction-suitability signature change.
The salvage adapter's optional downing target and backing pawn field were also
checked in the three reference versions. Missing or changed hooks produce a log
message rather than silently pretending that their feature is active.

## In-game acceptance checklist

Use a disposable test colony first. Enable development mode for reproducible
incidents and research completion. Record the game version, active DLC, mod list,
settings, and any relevant player-log entries for each test.

### Startup, settings, and saving

- [ ] With only Core, Harmony, and this mod enabled, the mod loads without patch
      errors or missing-DLC errors.
- [ ] Each settings tab opens, scrolls, and accepts changes without overlapping
      controls or exceptions at the player's display scale.
- [ ] Save and restart the game; settings retain their values.
- [ ] Load an existing colony and start a new colony; displayed technology
      reflects the correct colony's research rather than the previous save.
- [ ] Disable each optional feature and confirm its corresponding game behavior
      remains unchanged.

### Research-derived colony technology

- [ ] With a known research set, complete projects just below and exactly at a
      tier's threshold. Verify the technology display advances at the threshold.
- [ ] Switch between research-cost and project-count weighting and verify the
      expected difference using one expensive and several cheap projects.
- [ ] Complete one high-tier project while lower tiers remain unfinished. Verify
      strict lower-tier mode and highest-completed mode behave differently.
- [ ] Change per-tier overrides, minimum project counts, starting/max levels,
      and faction-floor settings; verify the displayed result recalculates.
- [ ] Add exclusions for a specific research definition or content package and
      verify it no longer contributes to progress.
- [ ] Test with missing DLC/research tiers. Empty tiers should follow the selected
      empty-tier policy, without inventing completed research.

### Trader arrivals and offered goods

- [ ] Force a normal caravan without a preassigned faction. Confirm the selected
      faction satisfies the colony's configured rule.
- [ ] Force an incident with a preassigned disallowed faction. Confirm compatible
      replacement or cancellation follows the selected settings.
- [ ] Create a case with no eligible trading faction. Verify both strict
      cancellation and the explicit allow-original fallback.
- [ ] Verify quest-trader preservation with an unassigned faction and with a
      preassigned faction, particularly on 1.4/1.5.
- [ ] Check normal caravan, settlement, and orbital-trader stock separately.
      Disallowed purchases should be unavailable while selling colony-owned
      goods and gifting remain possible.
- [ ] Open a trade window, select a purchase, then change the applicable
      technology/settings before completing it. Confirm the final transaction
      guard rejects a newly forbidden purchase without consuming currency.
- [ ] Offer an advanced minified building and confirm the building's technology,
      rather than the wrapper's undefined technology, controls the restriction.
- [ ] Verify explicit item/faction allow/block overrides and package exclusions,
      including the documented precedence when rules overlap.

### Equipment breakage and material recovery

- [ ] With break chance 0%, kill/down a qualifying pawn. Gear remains intact.
- [ ] With break chance 100% and recovery 100%, use gear with known material
      costs. Confirm eligible gear is removed once and the expected materials
      appear once, including the actual stuff used for a stuffed item.
- [ ] With recovery 0%, verify successful breakage produces no materials.
- [ ] Test a fixed partial recovery and random recovery within a narrow range.
      The same sampled percentage should apply to all ingredients of one item.
- [ ] Check death-only, downing-only, and combined triggers. Down a pawn and then
      kill it; an already broken item must not generate materials a second time.
- [ ] Verify the optional downing weapons-only policy preserves apparel.
- [ ] Exercise player, prisoner/slave, allied, neutral, hostile, and factionless
      pawn classifications with the relevant settings enabled and disabled.
- [ ] Confirm relics, biocoded gear, and quest-tagged items are preserved when
      their protection options are enabled.
- [ ] Test a modded item with explicit costs, a recipe-only item with an
      unambiguous ingredient list, and an item with unsupported/ambiguous costs.
      Unsupported recovery must preserve the item rather than invent materials.
- [ ] Test placement failure near blocked terrain. A failed material placement
      must preserve the original gear and roll back any partial material output.
- [ ] Check gear moved or destroyed by another death-handling mod. It must not
      be recovered again or taken from another pawn.
- [ ] Test an off-map/world-caravan death. The mod should skip material placement
      without an available map rather than creating invisible or duplicated loot.

### Optional DLC and other mods

- [ ] Run Core-only and then each available DLC configuration: Royalty, Ideology,
      Biotech, Anomaly, and Odyssey. No DLC should be required to start the mod.
- [ ] Include quest traders and protected equipment from installed DLC; verify
      the preservation settings and game-specific eligibility checks remain
      effective.
- [ ] On Odyssey, check trader eligibility on relevant map/layer types and
      salvage on a loaded map used by a gravship colony.
- [ ] Test the actual intended mod pack, including added research, factions,
      materials, weapons/apparel, and altered trade/death behavior.
- [ ] Inspect the log for missing-hook or skipped-item messages. Report the
      affected definition names and mod versions when an adapter is needed.

Compatibility is designed around loaded definitions and narrow hooks rather
than hardcoded DLC requirements. A custom trading system that bypasses
`TradeDeal`, or a mod that replaces the ordinary death/downing flow, may need an
additional adapter. Compile success alone is not a certification of those mod
combinations.
