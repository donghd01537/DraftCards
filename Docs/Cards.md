# Card Rules

> Related: [Units.md](Units.md) — how to author a new Unit card · [BattleField.md](BattleField.md) — how units fight once summoned.

## Card Categories

Two card types exist:

| Type | Behavior |
|---|---|
| **Unit** | Spawns N independent fighters at a formation lane when summoned via END. |
| **Spell** (`CardType.Support` with battlefield effects) | Instant effect on the existing battlefield army. Playable any time during the draft phase if MP allows; does not require a pending Unit selection. |

## Unit Cards

Each Unit card has fixed attributes used to populate the spawned fighters:

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

### Current Unit Roster

| Card | Count | Line | ATK | HP | Speed | Range | Cooldown | AttackSpeed | Effective Rate | Type | Style |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Knight x3 | 3 | Front | 150 | 800 | 240 | 80 | 1.2s | 2 | 0.6s/atk | Ground | Slow melee tank |
| Archer x2 | 2 | Back | 80 | 300 | 280 | 350 | 0.8s | 2 | 0.4s/atk | Ground | Fast ranged shooter |
| Cleric x2 | 2 | Middle | 60 | 400 | 260 | 250 | 1.5s | 2 | 0.75s/atk | Ground | Mid-range support frame |

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

Spells are battlefield-wide instant effects. They consume MP and discard the card on play.

| Card | MP | Effect |
|---|---|---|
| **Duplicate** | 5 | For every alive player unit on the battlefield, spawn an identical copy at a nearby empty spot on the same lane. Doubles your current army. |
| **Strengthen** | 3 | +50% ATK to every alive player unit on the battlefield. Permanent for the unit's lifetime. |
| **Fortify** | 2 | Front-line player units gain a shield (full damage immunity) for the **first X seconds** of the coming battle (default 5s, configurable). The window starts counting when combat begins, not when the card is played, and is a one-battle effect. |
| **Revive** | 3 | Arms a one-turn resurrection budget: the **first X player units to fall** in the coming battle (default 3) spring back at **XX% HP** (default 50%) and rejoin the fight mid-battle, instead of dying. Leftover budget is discarded; it does not carry to a later round. |

File naming: `Assets/Art/Cards/Spells/{Name}.png`.

### How the `value` field maps per effect

Spell stats come from `Assets/Config/cards.json` (see [below](#adding-a-new-spell-card)). A spell's single `value` means different things by `effectType`:

| `effectType` | `value` meaning | `value2` meaning |
|---|---|---|
| `DuplicateAllPlayerUnits` | unused (the lane is the input, not a number) | unused |
| `StrengthenAllPlayerUnits` | ATK bonus fraction — `0.5` = +50% | unused |
| `ShieldFrontLine` | shield duration in **seconds** (the "first X seconds" of immunity) | unused |
| `RallyAllPlayerUnits` | move/attack-speed bonus fraction — `0.4` = +40% | rally window in **seconds** of combat |
| `ReviveFirstDead` | how many of the first player units to fall this battle to resurrect (e.g. `3`) | HP fraction they return with — `0.5` = 50% |

### Targeting & lane requirements

- **Duplicate** is the only lane-targeted spell: it must be dropped on a player lane (see `RequiresLaneTarget` in [UIManager](../Assets/Scripts/UI/UIManager.cs)). Off-lane drops cancel.
- **Strengthen**, **Fortify**, **Rally**, and **Revive** are not lane-targeted — they pick their own targets (all units / front line / the units that fall in combat) and can be dropped anywhere on the battlefield.

### Adding a new spell card

A spell card is a `CardType.Support` `CardData` whose `supportEffects` carry a `SupportEffectType`. Two kinds exist, and the distinction drives almost everything:

- **Build-mutating supports** (e.g. `AddAttackFlat`, `MultiplyUnitCount`) only edit the *pending* unit before it's summoned. They flow through `CardPlayManager.ApplySupport`.
- **Battlefield spells** (`Duplicate`, `Strengthen`, `Fortify`, `Rally`, `Revive`) act on units already on the field (or, for `Revive`, on units *as they fall*). They are recognized by `CardPlayManager.IsBattlefieldSpell`, applied in `ApplyBattlefieldEffects`, and implemented on [BattlefieldView](../Assets/Scripts/UI/BattlefieldView.cs).

To add a battlefield spell like Fortify, touch these in order:

1. **[Enums.cs](../Assets/Scripts/Core/Enums.cs)** — add a `SupportEffectType` case.
2. **[BattlefieldView.cs](../Assets/Scripts/UI/BattlefieldView.cs)** — add the public method that applies the effect (e.g. `ShieldFrontLineUnits`). Remember to also handle the **pending** unit + its **preview** `BattleUnit`s, and propagate through `CloneUnitGroup` if Duplicate should carry the effect onto copies.
3. **[CardPlayManager.cs](../Assets/Scripts/Managers/CardPlayManager.cs)** — add the case to **both** `IsBattlefieldSpell` (so it isn't treated as a build-mutating support) and `ApplyBattlefieldEffects` (to route it to the new method).
4. **State that lives on the unit** (Fortify's timed shield) goes on [UnitGroup](../Assets/Scripts/Units/UnitGroup.cs) as plain fields/methods; it has no `Update`, so the per-frame tick is driven from [BattleUnit](../Assets/Scripts/Battle/BattleUnit.cs). Any matching visual goes on [UnitGroupView](../Assets/Scripts/UI/UnitGroupView.cs). If the effect must survive the summon, also add a field to [PendingUnitBuild](../Assets/Scripts/Cards/PendingUnitBuild.cs) and apply it in the `UnitGroup` constructor.
5. **Timed effects** must measure *combat* time, not draft time: arm the state when the card is played, but only start the countdown from `BattleUnit.SetActive(true)` (battle start). Clear one-battle state when the window ends so a surviving unit isn't re-buffed next round.
   - **Event-driven effects** (e.g. **Revive**) don't live on each unit at all — they arm a *budget on the battlefield* during the draft and fire from an existing battle event. Revive stores `_reviveBudget`/`_reviveHpFraction` on `BattlefieldView`; the spell is consumed inside `NotifyDeath`, which intercepts a dying player unit, calls `BattleUnit.ReviveInBattle(fraction)` (stays `_active`, partial HP via `UnitGroup.ReviveWithHpFraction`), and returns *before* the unit is moved to `_deadUnits`. Because such state lives on the battlefield, not the unit, it can't ride the end-of-round full-HP `ReviveAllDead`; make it a **one-turn** effect by clearing it in `BattlefieldView.ResetTurnEffects`, called from `GameManager.BeginPlayerTurn`.
6. **[cards.json](../Assets/Config/cards.json)** — add a `supports` entry (`id`, `name`, `mpCost`, `effectType`, `value`, optional `value2`, `spriteFile`). No editor-code change is needed; `CreateSupportCard` is generic.
7. Drop the card face at `Assets/Art/Cards/Spells/{Name}.png`, then run **DraftCards → Create Starter Cards**.

## Draft Phase Rules

1. At the start of each round, your hand is fully reset to 5 randomly drawn cards. Leftover cards from the previous round are discarded back to the deck.
2. You may play at most **one Unit card** per round.
3. You may play any number of **Spell cards** in a round (limited only by MP).
4. Playing a Unit card creates a preview — sprites appear at the unit's lane with a translucent tint.
5. Pressing **END** consumes the pending unit, spawns the real fighters at the preview positions, and starts the battle.

## Battle Phase Rules

1. All units activate when END is pressed.
2. Each unit independently:
   - Finds its closest opponent.
   - Walks toward it at its Speed value.
   - Engages when within Range, attacking every Cooldown seconds.
3. Damage = `Attacker.ATK`. Defender loses that much HP per hit; at 0 HP, the unit dies and disappears.
4. The battle continues until one side has no living units left.

## End-of-Round Reset

After a battle ends, before the next draft:

1. **Dead unit revival** — every unit that died in the round (player and enemy) is revived at full HP.
2. **Wave respawn** — if all enemies died this round, a fresh enemy wave spawns alongside the revived enemies.
3. **Lane regroup** — every alive unit snaps back to a random empty position inside its original lane.
4. **Hand reset** — your hand is wiped and 5 new cards are drawn.

The game loops indefinitely: draft → battle → reset → draft → ...

## Spawn Positioning Rules

- Each spawn picks a random spot inside the lane bounds (`±60` horizontal × `±320` vertical pixels around lane center).
- The chosen spot must be at least `70` pixels from any existing unit (player, enemy, or preview).
- If no empty spot is found after 25 attempts, the spawn falls back to a random position even if it overlaps.

## Physics During Battle

- Every active unit applies a soft repulsion force to other units within `35 + 35 = 70` pixels. Units push each other apart while walking and engaging — they touch but do not occupy the same space.
- Strength `280`; tunable per-unit on `BattleUnit`.

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
