# Compatibility and integration

## Supported builds and validation boundary

The rewrite has separate build targets for RimWorld **1.4, 1.5, and 1.6**. The pinned reference packages are `Krafs.Rimworld.Ref` **1.4.3901**, **1.5.4409**, and **1.6.4871**. These packages supply compile-time references; the mod does not redistribute game assemblies. Harmony is the only mod dependency (`brrainz.harmony`); the build does not bundle a second Harmony DLL.

Compilation and inspection of the actual reference metadata establish that the expected APIs exist. They do **not** establish that every DLC/mod combination works in a running game. In-game checks remain necessary, particularly for mods replacing trade, incident selection, pawn death/downing, or crafting costs. No multiplayer compatibility is claimed.

RimWorld 1.7 is not a supported release target. Do not assume a build for 1.6 is validated for an unstable or future game version.

## Runtime hooks

The entry point explicitly installs the following trading patches. It does not depend on unused helper methods being discovered automatically.

| Game method | Patch purpose | Compatibility behavior |
|---|---|---|
| `IncidentWorker_TraderCaravanArrival.FactionCanBeGroupSource` | Remove technologically disallowed candidates from ordinary caravan selection. | A postfix can only restrict an already-valid vanilla/modded candidate. The original suitability rules remain in force. |
| `IncidentWorker_TraderCaravanArrival.TryResolveParmsGeneral` | Check the selected faction, including factions assigned explicitly by an incident. | A replacement receives one normal-resolver retry with its trader kind cleared. Failed replacement follows the configured fallback. There is no unbounded reroll loop. |
| `IncidentWorker_TraderCaravanArrival.CanFireNowSub` | Carry the quest-preservation exception through early candidate checks. | Uses a scoped prefix/finalizer; it does not replace the game's availability calculation. |
| `TradeDeal.AddAllTradeables()` | Share a purchase-policy context while the normal stock list is built. | Scoped by trade-deal identity; nested builds restore the previous context. Direct custom calls to `AddToTradeables` still get their own policy check. |
| `TradeDeal.AddToTradeables(Thing, Transactor)` | Filter the trader's offered stock when the standard trade interface is populated. | Only `Transactor.Trader` is filtered. Colony sales and gifts remain available; rejected goods stay with the trader. |
| `TradeDeal.TryExecute(out bool)` | Recheck an actual purchase immediately before execution. | Rejects a prohibited purchase added by a later patch or exposed by a stale trade window. It reports a localizable message and asks the player to reopen trade. |

The faction suitability signature changes between supported versions:

| RimWorld | Inspected signature |
|---|---|
| 1.4 / 1.5 | `FactionCanBeGroupSource(Faction, Map, bool)` |
| 1.6 | `FactionCanBeGroupSource(Faction, IncidentParms, bool)` |

The adapter discovers that signature at startup and installs a typed postfix for the matching version, avoiding an allocated and boxed argument array on every faction probe. Quest context is carried through both the availability check and the resolver, so older Map-only calls do not lose the incident's quest exception. Quest, suitability-probe and replacement scopes match the actual worker/event identities; an unrelated nested incident does not inherit the outer exception. Finalizers restore prior contexts when another mod throws and do not suppress that exception.

A replacement retry uses the original `IncidentParms` object so other mods retain its identity and successful resolver changes. On failure, cached writable fields are restored to their prior values. This is shallow field restoration; it cannot undo another mod's external side effects or mutations inside referenced objects.

Research completion/reset methods on `ResearchManager` are observed when present: `FinishProject`, `ResetAllProgress`, and `DebugSetAllProjectsFinished`. The mod also invalidates its per-game cache on new-game/load/final initialization. A configurable periodic refresh catches research changes made through other paths. Faction and Manual modes read their selected level directly without scanning the research tree.

Equipment salvage uses prefixes, postfixes, and finalizers on `Pawn.Kill(DamageInfo?, Hediff)` and `Pawn_HealthTracker.MakeDowned(DamageInfo?, Hediff)`. It snapshots equipped/worn items before the transition, then handles only surviving eligible items. A per-pawn guard prevents duplicate handling of nested death/downing transitions. Both hooks are installed, but the configured trigger determines which is active; salvage is disabled by default.

## Trading policy and exceptions

- Same-or-lower technology is the default, with a zero-tier upward allowance. Exact and Range rules are configurable.
- Faction selection retains the game's relationships, faction viability, map/layer, trader-kind, and other suitability checks. Replacement is chosen randomly among eligible candidates, rather than always taking the first faction.
- Explicit item and faction block lists take priority over ordinary exemptions. Silver is protected so the transaction can still balance. Royal Favor is a special tradeable and is not treated as ordinary item stock.
- Pawn trading is exempt by default unless the player enables pawn filtering. An explicit item/faction block still takes priority over that category exemption.
- Item technology and overrides inspect the inner product of a `MinifiedThing`. An advanced building cannot bypass the rule merely because its wrapper has Undefined technology. Wrapper and inner-product explicit block/exemption entries are recognized; ownership is untouched.
- Independent orbital traders can be exempted from faction-level rules, or assigned a configurable assumed technology tier. Individual stock filtering still applies. Unknown factionless non-orbital traders are blocked when factionless exemption is disabled.
- Preserving a quest-linked caravan preserves its **faction identity and visit**, not a blanket permission to buy its stock. Faction and item purchase rules continue to apply. This also means an explicitly blocked faction can retain a protected quest visit while its ordinary purchases remain blocked.

Settlement and orbital restrictions operate through purchases in the standard trade interface. The mod does not remove settlements, destroy trade ships, or delete their stored inventories. It does not change raids or stop advanced equipment from existing elsewhere in the world.

If another mod completely replaces `TradeDeal`, bypasses the standard buy/sell transaction, or implements a custom incident worker without calling the patched methods, that path may need an explicit adapter. Settings offer separate toggles for caravan, settlement, orbital, and other standard trader channels.

## Optional DLC behavior

No expansion is listed as a dependency, and the mod does not require a DLC-only definition by name.

| Content | Integration choice |
|---|---|
| Royalty | Preserve quest-linked trader identities by default; retain ordinary currency/Favor handling. Purchase restrictions still apply where the standard trade interface is used. |
| Ideology | Relics and quest-tagged equipment are preserved by default. Pawn trading remains exempt unless enabled. Colony slaves follow the configurable player-pawn salvage policy. |
| Biotech | Gene-related items, equipment, and research use their loaded definitions and normal material costs. Pawn eligibility follows the configured pawn-group and humanlike checks. |
| Anomaly | Knowledge research is excluded from colony-tier calculation by default because it is a separate progression track. When explicitly included, its knowledge cost is used. Ordinary loaded research/items still use the generic rules. |
| Odyssey / 1.6 | Caravan replacement reuses the current game's suitability checks, including world-layer restrictions, rather than assuming a single planet/map arrangement. Salvage requires an actual held map. |

`ResearchProjectDef.IsHidden` is available in the inspected 1.5/1.6 references, but not 1.4. The hidden-research option uses that API when available. It cannot infer a third-party mod's separate research-visibility system on 1.4. Finished hidden projects remain counted when their research category is included. Only loaded definitions are examined, so an absent content pack does not add its unloaded projects to completion thresholds.

## Equipment and material integrations

The preferred material source is `ThingDef.CostListAdjusted(item.Stuff)`, preserving the actual stuff material and allowing normal game/mod cost adjustments. A configurable recipe fallback is accepted only when ingredients and per-product quantities can be resolved unambiguously. Multiple incompatible recipes, mixed/uncertain ingredient choices, custom recipe workers and unsupported materials preserve the original equipment when no usable adjusted cost list is available. A custom worker can change ingredient consumption beyond what a definition-only fallback can reconstruct.

Material recovery reflects current definitions; it is not a historical record of the exact crafting bill that created an item. A fixed or per-item random survival percentage is applied to all of that item's resolved materials, with whole quantities rounded down. Hit-point scaling is optional. Explicitly configuring zero survival intentionally destroys qualifying broken gear without producing materials.

Outputs are staged with a request to avoid merging into existing item stacks. Placement callbacks record destination stacks and material counts, allowing standard merge/split behavior to be accounted for if a stacking mod redirects placement. The source item's stack size and ownership are checked again before destruction. If ordinary placement cannot complete, recorded output is rolled back and the original remains.

These safeguards depend on the normal placement/destruction contracts. A mod that suppresses placement callbacks, deposits into unrelated virtual storage, or creates its own resources during destruction may need an adapter or equipment exclusions. Arbitrary external side effects cannot be reversed generically. The mod does not scan an entire map to try to infer unreported transfers.

Loose inventory, items equipped only after a transition, gear already dropped before the selected trigger, and world-caravan transitions without a map are outside the salvage scope. Death-only mode cannot retroactively capture a weapon dropped during an earlier downing. Downed or DeathOrDowned mode can capture that earlier event. With downed-apparel handling enabled, apparel that survives downing can receive a separate later death roll; those are distinct events.

## Integration for mod authors

No faction technology level, research cost, trader stock generator, recipe, or shared definition is mutated. Loaded modded definitions are read dynamically. Recipe lookup is cached by product; research results and parsed user overrides are cached and invalidated separately.

Optional compatibility data can be attached to a ThingDef, FactionDef, or ResearchProjectDef using `TechTraderPrevention.TechTraderDefExtension`:

```xml
<modExtensions>
  <li Class="TechTraderPrevention.TechTraderDefExtension">
    <techLevelOverride>4</techLevelOverride>
    <tradeExempt>false</tradeExempt>
    <tradeBlocked>false</tradeBlocked>
    <salvageExcluded>false</salvageExcluded>
    <excludeResearch>false</excludeResearch>
  </li>
</modExtensions>
```

| Field | Meaning |
|---|---|
| `techLevelOverride` | Integer tier 0–7; default `-1` means use the definition's own tier. Applicable to item, faction, and research definitions. |
| `tradeExempt` | Exempt an item or faction from the relevant ordinary technology check; an explicit block still wins. A faction exemption does not exempt every item from item-level checks. |
| `tradeBlocked` | Block the item/faction from purchases and, where relevant, faction selection. Transaction currency and configured quest-identity preservation have the documented exceptions above. |
| `salvageExcluded` | Preserve this equipment definition during salvage. |
| `excludeResearch` | Exclude this research definition from calculated colony technology. |

Settings-based **technology overrides** take priority over extension technology values. Boolean flags combine with player lists; block flags win over exemption flags. An author-provided block is therefore not cleared by adding the definition to a player exemption list.

Content mods that do not require this mod should add this extension through a conditional compatibility patch only when `SummerSausages2TTV.TechTraderPrevention` is installed. Do not place a reference to an unavailable extension class into an unconditional definition.

For runtime integration, call `TechTraderMod.Changed()` after changing public settings programmatically, and `ColonyTech.Invalidate()` after changing research through an unobserved path. Runtime recipe-definition changes can invalidate the product index with `SalvagePatches.ClearCaches()`. User-level exclusions accept exact definition names, and research package exclusions use package IDs. Optional/unloaded definition names are harmless; they become relevant if that content is loaded later.

## Large-modpack operation and diagnosis

| Mod behavior | Integration and limit |
|---|---|
| Adds research, item, material or faction definitions | Loaded definitions participate automatically, subject to the configured exclusions and tech overrides. No fixed content-pack list is required. |
| Changes the player's faction technology | Faction mode delegates the colony tier to that value. Research modes can optionally use it as a floor. |
| Replaces a research UI | The calculation reads research definitions and completion state, not UI nodes. A separate progression system must expose normal completion or explicitly invalidate/provide faction technology. |
| Extends the normal trade interface | The stock-build scope reuses trader/faction decisions; the transaction guard rechecks the trader-side items using fresh settings. A complete replacement that bypasses `TradeDeal` needs an adapter. |
| Adds crafting-cost patches or stuffable materials | The adjusted-cost API is consulted for each eligible item; the recipe product index is cached. Runtime recipe mutations should explicitly clear that index. |
| Adds death/downing, inventory, stacking or salvage behavior | Snapshots, transition guards, ownership rechecks and placement receipts cover normal shared API paths. Equipment-specific exclusions are available for unsupported behavior. |
| Moves simulation work onto background threads or synchronizes multiplayer | This release does not claim support for those execution models. The ordinary game simulation-thread contract is assumed. |

Harmony loads before this mod. Do not use an unconditional "load last" rule as a substitute for the other mods' own ordering requirements. The mod installs narrow prefixes/postfixes/finalizers and does not replace entire game methods with transpilers.

Independent hooks are installed separately, so one failed target does not prevent attempts to install the remaining hooks. **Status & help** shows installation problems and can generate a local compatibility report. The report checks the current callbacks (including callbacks removed after startup), lists the Harmony owners on each target, and records the active mod IDs in load order. Another mod sharing a target is evidence to investigate, not a confirmed conflict. The report is generated only on request; it does not scan the patch registry during ordinary gameplay or upload logs.

The same tab can refresh research, parsed overrides and recipe caches after runtime definition changes. For a reproducible issue, record that report along with the affected `defName`, trader type, selected feature settings, game version and reproduction steps. Report contents do not include save contents or local file paths, although the surrounding player log can contain information from other mods.
