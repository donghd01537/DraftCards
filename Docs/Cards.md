# Card Rules

> Related: [GameOverview.md](GameOverview.md) - high-level game loop and design pillars; [Units.md](Units.md) - how to author a new Unit card; [BattleField.md](BattleField.md) - how units fight once summoned.

## Card Categories

Two card types exist:

| Type | Behavior |
|---|---|
| **Unit** | Spawns N independent fighters at a formation lane when battle is confirmed. |
| **Spell** (`CardType.Support` with battlefield effects) | Instant effect on the existing battlefield army. Playable any time during the draft phase if MP allows; does not require a pending Unit selection. |

## Overall Card Rules

Cards are the player's main planning tools before automatic combat starts.

| Rule | Design intent |
|---|---|
| **Fresh wave hand** | At the start of each wave, draw 3 Unit Cards and 5 Spell Cards. Unused cards are discarded after the wave, and the next wave draws a fresh hand. |
| **One Unit Card per wave** | Unit Cards are the long-term army-building choice. Limiting them makes each added group a commitment. |
| **Unit Cards add groups** | A Unit Card adds a group of fighters, not one large stacked entity. Balance must account for group size. |
| **Multiple Spell Cards per wave** | Spells are the flexible tactical layer. The player can combo them, answer a wave, or save MP. |
| **Shared MP creates timing tension** | Base MP is 10 per wave. Spell Cards and Unit Upgrade spend the same MP pool. Spending MP now can solve the current wave; saving or investing it can improve later waves. |
| **Unit Upgrade is not a card pick** | The design target treats Unit Upgrade as a planning-phase MP spend, separate from choosing a Unit Card. |
| **Confirm starts combat** | Once the player confirms battle, combat starts automatically and resolves without realtime commands. |
| **Deck defines tools** | The intended pre-stage deck decides what can appear during a stage; in-stage choices decide how the army grows. |

Current prototype note: pre-stage deck building is not implemented yet. **Unit Upgrade is now implemented** as the **Upgrade Unit** spell (see [Units.md → Unit Evolution](Units.md#unit-evolution)); it shares the same MP pool as other spells. `DeckManager` loads generated player cards from `Resources/Cards`.

## Unit Cards

Each Unit Card has fixed attributes used to populate the spawned fighters. In runtime, a card with `Count = 3` creates three separate `UnitGroup` fighters, each with its own HP, targeting, movement, and death state.

| Attribute | Description |
|---|---|
| **ATK** | Damage dealt per attack. |
| **HP** | Starting health per unit; reduced by incoming damage. |
| **Speed** | Pixels per second movement on the battlefield. |
| **Range** | Distance at which the unit engages its target (smaller = melee, larger = ranged). |
| **Type** | `Ground` or `Flying`. Reserved for future targeting rules; currently informational. |
| **Line** | `Front`, `Middle`, or `Back` — the formation lane where new units spawn. |
| **Count** | Number of independent fighters this card spawns. |
| **Cooldown** | Base time in seconds between consecutive attacks. |
| **Attack Speed** | Multiplier applied to attack rate. Effective time between attacks = `Cooldown / AttackSpeed`. Default is `2` (twice the base rate). |
| **Projectile Speed** | Optional travel speed for ranged projectile sprites. `0` means use launcher default. |
| **Projectile AoE Radius** | Optional splash radius for lobbed projectiles. `0` means single-target direct fire, used by arrows. |
| **Shadow Scale** | Optional visual-only scale for the ground shadow. |

Design target stats also include **Defense** and **Target Priority**, but those are not implemented in `UnitData` yet.

### Current Unit Roster

| Card | Count | Line | ATK | HP | Speed | Range | Cooldown | AttackSpeed | Effective Rate | Type | Style |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Swordsman x3 | 3 | Front | 110 | 2600 | 230 | 80 | 1.2s | 1.8 | 0.67s/atk | Ground | Slow melee tank — evolves to Spartan (uses the `Knight` art/character folder) |
| Archer x2 | 2 | Back | 85 | 1000 | 260 | 360 | 0.9s | 2.0 | 0.45s/atk | Ground | Fast ranged shooter (Arrow) |
| Cleric x2 | 2 | Middle | 65 | 1600 | 245 | 260 | 1.4s | 1.8 | 0.78s/atk | Ground | Mid-range support frame |
| Spartan x3 | 3 | Front | 121 | 2860 | 253 | 88 | 1.2s | 1.98 | 0.61s/atk | Ground | Swordsman's evolution (not drafted directly) — see [Unit Evolution](Units.md#unit-evolution) |

File naming: `Assets/Art/Cards/Units/{Name}_{Count}.png` (e.g. `Knight_3.png` = card spawning 3 Knights). The character sprite art (idle / attack frames) lives under `Assets/Art/Characters/{Name}/`.

### Current Enemy Roster

Enemies use the same `UnitData` stats but always spawn with `count = 1`; per-round counts and lanes come from the `_waves` table in [GameManager](../Assets/Scripts/Managers/GameManager.cs) (see [BattleField.md → Enemy Waves](BattleField.md#enemy-waves)). Stats live in [Assets/Config/cards.json](../Assets/Config/cards.json) under `enemies`; art is attack frames only under `Assets/Art/Enemies/{Folder}/Attack/` (first frame doubles as the idle pose).

| Enemy | Line | ATK | HP | Speed | Range | Cooldown | AttackSpeed | Style |
|---|---|---|---|---|---|---|---|---|
| Goblin | Front | 45 | 900 | 280 | 70 | 0.8s | 1.8 | Fast swarm melee |
| Goblin Archer | Back | 65 | 700 | 250 | 340 | 1.0s | 1.8 | Ranged (Arrow) |
| Orc | Front | 150 | 2600 | 235 | 85 | 1.35s | 2.0 | Heavy bruiser |
| Wolf Rider | Front | 95 | 1700 | 320 | 80 | 0.9s | 2.2 | Fast charger |
| Shaman | Middle | 75 | 1300 | 245 | 300 | 1.2s | 1.8 | Magic ranged (Magic) |
| Cyclop | Back | 140 | 4800 | 210 | 460 | 1.1s | 1.2 | Siege thrower (Rock) |

## Spell Cards

Spells consume MP and discard the card on play. The active spell roster is defined in `Assets/Config/cards.json` under `supports`; generated `CardData` stores the name, MP cost, area/type metadata, short description, and full rules text from that config. The card art under `Assets/Art/Cards/Spells/` is now the illustration/background only.

| Card | MP | Area | Type | Short description |
|---|---:|---|---|---|
| **Mark** | 1 | Enemy line | Setup | Marked enemies take more damage. |
| **Quick Shield** | 1 | Ally line | Defense | Give a small shield to one ally line. |
| **Lucky Draw** | 1 | Player hand | Gamble | Draw 2 spells. First drawn spell costs 2 less. |
| **Mana Crystal** | 2 | Player economy | Scaling | Increase Max MP by 1 next wave. |
| **Slow Field** | 3 | Enemy Front + Middle path | Control | Slow enemies in Front and Middle path. |
| **Rally** | 3 | Ally line | Buff | Increase attack speed of one ally line. |
| **Barrier** | 4 | Ally line | Defense | Give a medium shield to one ally line. |
| **Lightning Strike** | 4 | Priority enemy area | Damage | Strike 1-2 high-threat enemies. |
| **Revive** | 5 | All ally lines | Recovery | Revive limited fallen units once. |
| **Meteor** | 5 | Enemy line | Big damage | At battle start +1s, drop meteors on one enemy line. |
| **Emergency Draft** | 6 | Unit reinforcement | Temporary units | Pick 1 of 2 rolled units; spawn it on the field for this wave only. |
| **Duplicated** | 6 | Ally line | Army swing | Duplicate current units on one ally line this battle. |
| **Upgrade Unit** | 5+ | Ally unit | Evolution | Upgrade a unit family on the field. Cost rises +1 each use. |

Fireball and Fortify are still defined in `cards.json` and have generated assets/art, but are flagged `excludeFromInitialDeck` so they do not enter the current player deck or temporary spell rolls.

File naming: `Assets/Art/Cards/Spells/{Name}.png`.

> **Upgrade Unit is special.** Unlike every other spell, its MP cost is **not** the static `mpCost` in `cards.json` (that's only the starting cost, `5`). The real cost is **dynamic**: it starts at 5 and rises by 1 each time you play Upgrade, **for the whole run** (it never resets). Playing it opens a **modal target picker** instead of resolving instantly. See [Unit Upgrade / Evolution](Units.md#unit-evolution) in Units.md for the full system.

> **Emergency Draft is a modal spell too** (same deferred-resolution shape as Upgrade, but a flat `mpCost`). Playing it rolls **2 random draftable units** and opens a picker; the chosen unit's full group spawns onto the field as **temporary battle-only** reinforcements for the current wave only.
> - **Respects evolution.** The roll samples the live deck piles, not `Resources.LoadAll`, so it always offers a family's **current** tier and never a locked higher tier. If Swordsman hasn't evolved, the option is Swordsman; once it has evolved (the pool was swapped to Spartan), the option is Spartan. Evolved forms that were never unlocked (`excludeFromInitialDeck`) never entered the piles, so they can't appear.
> - **Temporary lifecycle.** Spawned units carry `TemporaryBattleOnly` (the same flag the Duplicate-line spell uses) and are removed by `BattlefieldView.ClearTemporaryPlayerUnits()` at wave end (called from `GameManager.EndTurnSequence`). They never join the persistent army or count against the one-Unit-Card-per-wave limit. Code: `CardPlayManager.GetEmergencyDraftOptions`/`CommitEmergencyDraft`, `DeckManager.SampleDraftableUnits`, `BattlefieldView.SummonTemporaryUnit`, [EmergencySelectionPanel](../Assets/Scripts/UI/EmergencySelectionPanel.cs).

### How the `value` field maps per effect

Spell stats come from `Assets/Config/cards.json` (see [below](#adding-a-new-spell-card)). `value`, `value2`, and `value3` mean different things by `effectType`:

| `effectType` | `value` | `value2` | `value3` |
|---|---|---|---|
| `MarkEnemyLine` | bonus damage-taken fraction | unused | unused |
| `ShieldPlayerLine` | shield duration in seconds | unused | unused |
| `DrawTemporarySpellCards` | spell cards to draw | MP discount on first drawn spell | unused |
| `IncreaseMaxMpNextTurn` | Max MP increase | unused | unused |
| `DamageEnemyLine` | damage | fixed line enum; legacy `-1` selected-line assets route through Meteor compatibility | extra bonus fraction vs marked targets |
| `MeteorEnemyLine` | damage | unused | extra bonus fraction vs marked targets |
| `SlowEnemyOpeningLines` | slow fraction | slow duration in seconds | unused |
| `ReduceDamageFrontLine` | damage reduction fraction | unused | unused |
| `RallyPlayerLine` | move/attack-speed bonus fraction | rally duration in seconds | unused |
| `LightningStrikePriorityEnemies` | damage per strike | strike count | unused |
| `ReviveFirstDead` | fallen units to revive | HP fraction they return with | unused |
| `EmergencyDraftUnits` | unit options to roll into the picker | unused | unused |
| `DuplicatePlayerLineLimited` | unused | unused | unused |
| `UpgradeUnit` | unused (cost & effect are data-driven from the unit's `evolution` ladder, not these fields) | unused | unused |

Legacy effect types such as `DuplicateAllPlayerUnits`, `StrengthenAllPlayerUnits`, `ShieldFrontLine`, `RallyAllPlayerUnits`, `LightningStrikePriorityEnemy`, and `HoldSpellForNextTurn` are still implemented in code for possible later reuse, but they are not part of the active config.

### Targeting & lane requirements

- Ally line targets: **Quick Shield**, **Rally**, **Barrier**, and **Duplicated** must be dropped on a player lane.
- Enemy line targets: **Mark** and **Meteor** must be dropped on an enemy lane.
- Fixed or automatic targets: **Lucky Draw**, **Mana Crystal**, **Slow Field**, **Lightning Strike**, and **Revive** can be played without a lane drop. Fireball and Fortify use this shape too, but are currently excluded from the deck.
- Off-lane drops cancel for every lane-targeted spell.
- **Modal target:** **Upgrade Unit** and **Emergency Draft** do not use a lane drop. Playing either (click) opens a picker; choosing an option resolves the spell, and Cancel aborts with no MP spent (the card stays in hand). The card is consumed and MP charged only on confirm.
  - **Upgrade Unit** lists one card per on-field player unit family that can still upgrade.
  - **Emergency Draft** lists 2 randomly-rolled draftable units (in their **current** tier — see below); the chosen unit's full group spawns onto the field as **temporary** reinforcements that are removed when the wave ends. The summon does **not** count against the one-Unit-Card-per-wave rule.

### Adding a new spell card

A spell card is a `CardType.Support` `CardData` whose `supportEffects` carry a `SupportEffectType`. Two kinds exist, and the distinction drives almost everything:

- **Build-mutating supports** (e.g. `AddAttackFlat`, `MultiplyUnitCount`) only edit the *pending* unit before it's summoned. They flow through `CardPlayManager.ApplySupport`.
- **Battlefield/instant spells** act on units already on the field, pending previews, the hand/deck, or MP economy. They are recognized by `CardPlayManager.IsBattlefieldSpell`, applied in `ApplyBattlefieldEffects`, and implemented on [BattlefieldView](../Assets/Scripts/UI/BattlefieldView.cs), `DeckManager`, `HandManager`, or `MPManager` depending on the effect.

To add a battlefield spell like Fortify, touch these in order:

1. **[Enums.cs](../Assets/Scripts/Core/Enums.cs)** - add a `SupportEffectType` case.
2. **[BattlefieldView.cs](../Assets/Scripts/UI/BattlefieldView.cs)** - add the public method that applies the effect (e.g. `ShieldFrontLineUnits`). Remember to also handle the **pending** unit + its **preview** `BattleUnit`s, and propagate through `CloneUnitGroup` if Duplicate should carry the effect onto copies.
3. **[CardPlayManager.cs](../Assets/Scripts/Managers/CardPlayManager.cs)** - add the case to **both** `IsBattlefieldSpell` (so it isn't treated as a build-mutating support) and `ApplyBattlefieldEffects` (to route it to the new method).
4. **State that lives on the unit** (Fortify's timed shield) goes on [UnitGroup](../Assets/Scripts/Units/UnitGroup.cs) as plain fields/methods; it has no `Update`, so the per-frame tick is driven from [BattleUnit](../Assets/Scripts/Battle/BattleUnit.cs). Any matching visual goes on [UnitGroupView](../Assets/Scripts/UI/UnitGroupView.cs). If the effect must survive the summon, also add a field to [PendingUnitBuild](../Assets/Scripts/Cards/PendingUnitBuild.cs) and apply it in the `UnitGroup` constructor.
5. **Timed effects** must measure *combat* time, not draft time: arm the state when the card is played, but only start the countdown from `BattleUnit.SetActive(true)` (battle start). Clear one-battle state when the window ends so a surviving unit isn't re-buffed next round.
   - **Event-driven effects** (e.g. **Revive**) don't live on each unit at all. They arm a budget on the battlefield during the draft and fire from an existing battle event. Revive stores `_reviveBudget`/`_reviveHpFraction` on `BattlefieldView`; the spell is consumed inside `NotifyDeath`, which intercepts a dying player unit, calls `BattleUnit.ReviveInBattle(fraction)`, and returns before the unit is moved to `_deadUnits`.
6. **[cards.json](../Assets/Config/cards.json)** - add a `supports` entry (`id`, `name`, `mpCost`, `area`, `spellType`, `shortDescription`, `description`, `effectType`, `value`, optional `value2`, optional `value3`, `spriteFile`, optional `projectileSprite`). No editor-code change is needed unless the spell introduces new config fields.
7. Drop the card face at `Assets/Art/Cards/Spells/{Name}.png`, then run **DraftCards > Create Starter Cards**.

> **A third spell shape exists** beyond build-mutating supports and battlefield/instant spells: **deferred-resolution spells with a modal picker**. Two examples exist, both registered in `IsBattlefieldSpell` but short-circuited in `CardPlayManager.TryPlayCard` so they raise an event and open a modal instead of spending MP and resolving inline:
> - **Upgrade Unit** — raises `OnUpgradeRequested`; [UpgradeSelectionPanel](../Assets/Scripts/UI/UpgradeSelectionPanel.cs) opens; finished by `CommitUpgrade`, which spends the *dynamic* cost from [UpgradeManager](../Assets/Scripts/Managers/UpgradeManager.cs), not `card.mpCost`. See [Units.md → Unit Evolution](Units.md#unit-evolution).
> - **Emergency Draft** — raises `OnEmergencyDraftRequested`; [EmergencySelectionPanel](../Assets/Scripts/UI/EmergencySelectionPanel.cs) opens with options from `GetEmergencyDraftOptions`; finished by `CommitEmergencyDraft` (flat `card.mpCost`), which calls `BattlefieldView.SummonTemporaryUnit`. Both panels are self-building (construct their own overlay at runtime) and are ensured by `UIManager.EnsureUpgradePanel`/`EnsureEmergencyPanel`.

## Draft Phase Rules

1. At the start of each wave, your hand is fully reset to 3 randomly drawn Unit Cards and 5 randomly drawn Spell Cards.
2. You may choose up to **one Unit Card** per wave. After a Unit Card is selected, all remaining Unit Cards leave the hand immediately.
3. You may cast any number of **Spell Cards** in a wave, limited only by MP.
4. Playing a Unit card creates a preview — sprites appear at the unit's lane with a translucent tint.
5. Base MP is 10 per wave. Spell Cards and Unit Upgrade share the same MP pool.
6. Player may spend MP on **Upgrade Unit** during planning to upgrade/evolve a unit family already on the field (see [Units.md → Unit Evolution](Units.md#unit-evolution)).
7. Pressing **FIGHT** confirms battle, consumes the pending unit, spawns the real fighters at the preview positions, and starts combat automatically.

Design target: before a full stage, the player should choose a deck and Commander. Pre-stage deck building is not in the current prototype yet.

## Battle Phase Rules

1. All units activate when battle is confirmed.
2. Each unit independently:
   - Finds its closest opponent.
   - Walks toward it at its Speed value.
   - Engages when within Range, attacking every Cooldown seconds.
3. Damage = `Attacker.ATK`. Defender loses that much HP per hit; at 0 HP, the unit dies and disappears.
4. The battle continues until one side has no living units left.

## End-of-Round Reset

After a battle ends, before the next draft:

1. **Enemy clear** — the enemy side is destroyed and removed from both logic and visuals.
2. **Wave spawn** — the next scripted enemy wave is spawned. The current prototype loops through 6 waves indefinitely.
3. **Player revival** — dead player units revive at full HP.
4. **Lane regroup** — every alive player unit snaps back into its original lane formation.
5. **Hand reset** — unused cards are discarded, then 3 Unit Cards and 5 Spell Cards are drawn for the next wave.

The game loops indefinitely: draft → battle → reset → draft → ...

## Spawn Positioning Rules

- Each spawn picks a random spot inside the lane bounds (`±90` horizontal x `±180` vertical pixels around lane center).
- The chosen spot must be at least `55` pixels from any existing unit (player, enemy, or preview).
- If no empty spot is found after 40 attempts, the spawn falls back to a random position even if it overlaps.

## Physics During Battle

- Every active unit applies a soft repulsion force to nearby units. Allies use `_allySeparationDistance` (default `67`); opposing bodies use their body radii. Units push each other apart while walking and engaging — they touch but do not occupy the same space.
- Separation strength is `420`, and combined movement is clamped by `_maxSpeed` (`420` by default); both are tunable per-unit on `BattleUnit`.

## File Layout

```
Assets/
  Art/
    Cards/
      Units/   <Name>_<Count>.png — unit card faces
      Spells/  <Name>.png — spell card faces
    Characters/
      <Name>/
        Unit_<Name>_Idle.png    — idle sprite (one frame)
        Attack/
          Unit_<Name>_Attack_<n>.png — attack frames (numbered)
  Resources/
    Cards/    <id>.asset — CardData ScriptableObjects generated by the editor menu
```

## Generating Cards in the Editor

Menu: **DraftCards → Create Starter Cards**

This:
1. Force-imports the `Assets/Art/Cards/` and `Assets/Art/Characters/` folders so the texture postprocessor converts them to `Sprite` type.
2. Wipes any existing `CardData` assets under `Assets/Resources/Cards/` and `Assets/Resources/Enemies/`.
3. Re-generates every unit, enemy, and spell card declared in `Assets/Config/cards.json`.

The roster (stats, MP, effect values) is data-driven from `Assets/Config/cards.json` — edit that file, then re-run the menu. `CreateStarterCards.cs` only parses the JSON and builds assets; it does not hard-code the card list.

After running this, the deck is ready for play.
