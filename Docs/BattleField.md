# How the Battle Field Works

This document explains the battlefield: how units get there, how they fight, and how the round loop drives it. For the overall game direction see [GameOverview.md](GameOverview.md); for card/unit authoring see [Units.md](Units.md); for gameplay rules see [Cards.md](Cards.md).

## Scene Flow

The game opens in `Scenes/HomeScene.unity` — a title screen whose **Start** button calls [HomeScreen.StartGame](../Assets/Scripts/UI/HomeScreen.cs), which loads `BattlePrototype` (the battle scene below) via `SceneManager.LoadScene`. Both scenes are registered in Build Settings (HomeScene first, so builds boot to the title screen). The battle starts immediately on load — `GameManager.Start()` kicks off the round loop.

Design target: a full stage should begin with deck and Commander preparation, then run through about 8-10 enemy waves. The current prototype starts directly from the generated player deck and uses 6 scripted waves that loop indefinitely.

## Two Layers: Logic vs. Visuals

The battlefield is split into a **logical** model and a **visual** model. Keeping them separate is the single most important thing to understand.

| Layer | Type | Responsibility |
|---|---|---|
| Logical | [UnitGroup](../Assets/Scripts/Units/UnitGroup.cs) | Pure data: ATK, HP, line, speed, range, alive/dead. No Unity scene presence. |
| Logical | [BattlefieldManager](../Assets/Scripts/Managers/BattlefieldManager.cs) | Holds the authoritative `_playerUnits` / `_enemyUnits` lists of `UnitGroup`. Raises events when a unit is added/removed. |
| Visual | [BattleUnit](../Assets/Scripts/Battle/BattleUnit.cs) | A `MonoBehaviour` on the battlefield that *drives* one `UnitGroup`: movement, targeting, attacking, separation. |
| Visual | [UnitGroupView](../Assets/Scripts/UI/UnitGroupView.cs) | The sprite(s) for a `BattleUnit` — idle/attack frame swapping, highlight tint, scale. |
| Visual | [BattlefieldView](../Assets/Scripts/UI/BattlefieldView.cs) | Owns all `BattleUnit`s, spawns/positions them, runs spatial queries, handles previews, death, revive, regroup. Implements `IBattleSpatial`. |

A `UnitGroup` is created first (logic). The `BattlefieldManager` raises `OnUnitPlaced`, and `BattlefieldView` reacts by instantiating a `BattleUnit` + `UnitGroupView` to represent it (visual). The `BattlefieldView._byGroup` dictionary maps each `UnitGroup` back to its `BattleUnit`.

> Note: every unit is a **single fighter** (`UnitGroup.Count` is 1). A card with `count: 3` spawns three separate `UnitGroup`s. The battlefield never renders a "stack".

> [BattleResolver](../Assets/Scripts/Battle/BattleResolver.cs) is legacy and currently unused — damage is applied directly in `BattleUnit.PerformAttack`. Ignore it.

## The Round Loop

[GameManager](../Assets/Scripts/Managers/GameManager.cs) is the conductor. It owns a [GameState](../Assets/Scripts/Core/Enums.cs) and runs the current prototype's endless `draft -> battle -> reset` loop.

```
StartBattle()                       ← once, on Start()
  ├─ build deck, clear hand/MP
  ├─ _battlefieldManager.Clear()
  ├─ SpawnWave(round 0)             ← initial enemies
  └─ BeginPlayerTurn()              ← draw 3 units + 5 spells, state = SelectCardPhase

... player drafts, previews a unit, presses FIGHT ...

OnConfirmPressed() → EndTurnSequence() (coroutine):
  ├─ SummonPendingUnit()            ← pending build → real player UnitGroups
  ├─ RunBattle()                    ← activate everyone, simulate until one side is empty
  ├─ ClearEnemyUnits() / ClearEnemies()   ← tear down the enemy side
  ├─ _roundIndex++; SpawnWave(_roundIndex) ← rebuild enemies for the new round
  ├─ ReviveAllDead()                ← dead PLAYER units come back at full HP
  ├─ RegroupAllUnits()              ← survivors snap into packed lane formation
  └─ BeginPlayerTurn()              ← fresh hand, next wave
```

`RunBattle()` flips every `BattleUnit` to active (`SetActive(true)`), then yields each frame while `BattlefieldView.BothSidesAlive` and `elapsed < _maxBattleSeconds` (default 30s timeout). When it ends it pauses all units and waits `_postBattlePause`.

See [Cards.md](Cards.md) for the player-facing draft and reset rules this loop implements. Future stage structure should keep the same planning-first rhythm: enemy wave appears, player draws 3 Unit Cards and 5 Spell Cards, adds up to one unit group, spends shared MP on spells and eventual Unit Upgrade, confirms, then combat resolves automatically.

## Lanes & Formation

A lane is a [FormationLineView](../Assets/Scripts/UI/FormationLineView.cs) placed in the scene — a `RectTransform` marking where a `FormationLine` (`Back` / `Middle` / `Front`) sits for one side. `BattlefieldView` holds two arrays, `_playerLines` and `_enemyLines`, each with one view per line.

- `FindLine(isPlayer, line)` looks up the lane view; `LaneAnchoredPosition` converts its world position into the battlefield's local coordinate space.
- The scene has **6 lane views** total (3 player + 3 enemy). If a wave targets a lane with no matching view, units fall back to position `(0,0)`.
- Enemy `BattleUnit`s are mirrored: their `localScale.x` is negated in `SpawnUnitView` so they face left.

Everything lives under `_battleFieldRoot` (a `RectTransform`) and uses `anchoredPosition`. Positions are clamped to `_battleBoundsMin`/`_battleBoundsMax` via `ClampToBounds`.

## Spawning a Unit

### Preview (during draft)
When the player selects a Unit card, `CardPlayManager` raises `OnPendingBuildChanged`. `BattlefieldView.HandlePendingChanged`:
1. Clears old previews, plays a `+N` summon effect at the lane.
2. `SpawnPreviewUnitsRoutine` drops `count` translucent preview units into the lane, staggered, packed into formation.

Previews are real `BattleUnit`s bound via `UnitGroupView.BindPreview` (tinted, inactive). They participate in separation so they spread out naturally.

### Real units (on FIGHT)
`SummonPendingUnit` consumes the pending build and creates one player `UnitGroup` per `count`. Each `PlaceUnit` raises `OnUnitPlaced` → `HandleUnitPlaced`, which:
- Snapshots the previews' settled positions so real units take over the exact spots (`_previewPositions` / `_placementCursor`).
- Otherwise finds an empty spot via `FindEmptyPosition`.
- Spawns the `BattleUnit`, binds it with `UnitGroupView.BindReal`, registers it in `_byGroup`.

### Positioning
- `FindEmptyPosition` — random point within the lane (`±_laneHalfWidth × ±_laneHalfHeight`) that is at least `_spawnMinDistance` from every other unit, retried up to `_spawnMaxAttempts` times, else a random fallback.
- `BuildPackedPositions` / `PackedOffset` — deterministic packing used for previews and regroup: vertical-first stacking that branches into side columns (`_packXSpacing` × `_packYSpacing`).
- `StartDrop` — a drop-in animation (fall + squash/stretch pop) on spawn.

## The Battle Simulation

The whole fight is driven by `BattleUnit.Update()` running per-frame on each active unit. There is no central tick — units act independently. Per frame a unit:

1. **Target & act** (only when `_active`, after a small random `_activationDelay`):
   - `FindClosestOpponent` picks the nearest living unit on the other side.
   - If farther than `AttackRange`: record a move direction toward it (applied at `EffectiveMoveSpeed` below).
   - If within range: count down `_cooldownTimer`; on zero, `PerformAttack` and reset to `EffectiveAttackCooldown` (= `AttackCooldown / AttackSpeed`).
2. **Separation** (`ComputeSeparation`) — soft repulsion from nearby units. Allies keep `_allySeparationDistance` apart; enemies use body radii (`_bodyRadius`). This keeps units touching but not overlapping. Pairs at the exact same spot get a deterministic scatter direction. A push against an **ally standing in the move direction** is softened toward `_blockedPushFactor`, so a rear unit presses the back of the line instead of bulldozing it forward.
3. **Settle** — if not yet active but has a settle target (post-spawn/regroup), drift toward it.
4. **Move** — sum separation + move + settle, **clamp the combined speed to `_maxSpeed`** (so a pile-up of pushes can't fling a unit across the field), then apply, clamped to bounds. A debounced moving/idle flag drives the walk bounce via `MoveBounceAnimator`.

### Attacking & damage
`PerformAttack` plays the attack frame animation (`UnitGroupView.PlayAttack`) and either calls `target.TakeDamage(Group.TotalDamage)` immediately for melee units, or launches the configured projectile sprite and applies damage on impact. `TotalDamage = Attack × Count` (Count is 1, so it's just `Attack`). `BattleUnit.TakeDamage` applies it to the `UnitGroup`; if HP hits 0, it notifies death. Direct projectiles such as arrows calculate one intercept point from the target's current velocity, fly there at fixed speed, and can miss if the target moves away; lobbed projectiles with `projectileAoeRadius > 0` deal splash damage around the impact point.

The melee swing plays for a **fixed** `_meleeAttackDuration` (clamped below the cooldown), not a fraction of it — otherwise fast attackers (e.g. a Goblin at a 0.4s effective cooldown) flash the strike pose for a tenth of a second and look idle while swarming. Ranged throwers use the analogous fixed `_projectileThrowDuration` instead.

### Death
`UnitGroup.TakeDamage` sets `CurrentHp = 0` and `Count = 0` (→ `IsDead`). `BattleUnit.TakeDamage` then calls `IBattleSpatial.NotifyDeath`, which:
- Removes the unit from its alive list (`_playerBattleUnits` / `_enemyBattleUnits`).
- Adds it to `_deadUnits` and deactivates the GameObject (it stays in `_byGroup`).

A side is "wiped" when its alive list is empty (`HasAlivePlayer` / `HasAliveEnemy`), which is what ends `RunBattle`.

## Reset: Revive, Regroup, Re-wave

After each battle `EndTurnSequence` does three things:

| Step | Method | Effect |
|---|---|---|
| Re-wave | `ClearEnemyUnits` + `ClearEnemies` + `SpawnWave` | Destroys all enemy views (alive **and** dead, purging `_byGroup`) and the enemy `UnitGroup` list, then spawns the next round's wave. Player units are untouched. |
| Revive | `ReviveAllDead` | Every unit still in `_deadUnits` (now only players) is revived at full HP and re-added to its alive list. |
| Regroup | `RegroupAllUnits` | Living units on each side are grouped by lane and snapped into packed formation positions. |

## Enemy Waves

Enemies are not drafted — `GameManager` scripts them per round in a static `_waves` table. Each entry is a list of `EnemySpawn(cardId, count, line)`. The round advances **every** battle cycle and loops back to the first wave after the last.

| Round | Composition |
|---|---|
| 1 | 8 Goblin (Front) |
| 2 | 8 Goblin (Front) + 2 Goblin Archer (Back) |
| 3 | 12 Goblin (Front) + 2 Goblin Archer (Back) |
| 4 | 1 Orc + 8 Goblin (Front) + 2 Goblin Archer (Back) |
| 5 | 2 Orc + 8 Goblin (Front) + 1 Shaman (Middle) + 4 Goblin Archer (Back) |
| 6 | 2 Orc + 2 Wolf Rider + 8 Goblin (Front) + 2 Shaman (Middle) + 4 Goblin Archer + 1 Cyclop (Back) |

`SpawnWave` loads each enemy `CardData` by id from the **`Resources/Enemies`** folder (kept separate from `Resources/Cards` so enemies never enter the player deck), builds a `PendingUnitBuild` per spawn, and places `count` enemy `UnitGroup`s into the given lane. To change difficulty, edit the `_waves` table (lanes/counts) in [GameManager](../Assets/Scripts/Managers/GameManager.cs) and the per-enemy stats in [Assets/Config/cards.json](../Assets/Config/cards.json) (see the enemy roster in [Cards.md](Cards.md)).

## Spells on the Battlefield

Spell cards act on units already on the field, pending previews, or battlefield-level turn state:

- **Mark** (`MarkEnemyLine`) marks enemies in a selected enemy lane so they take bonus damage.
- **Quick Shield** / **Barrier** (`ShieldPlayerLine`) arm combat-time shields on a selected player lane.
- **Meteor** (`MeteorEnemyLine`) arms a battle-start spell: 1 second after combat begins, meteors fall onto the selected enemy lane, explode, and gain extra bonus against marked targets. Fireball still uses the immediate `DamageEnemyLine` shape, but is currently excluded from the deck.
- **Slow Field** (`SlowEnemyOpeningLines`) slows enemies in the front and middle lanes for the opening combat window.
- **Fortify** (`ReduceFrontLineDamage`) gives front-line player units damage reduction, but is currently excluded from the deck.
- **Rally** (`RallyPlayerLine`) applies a temporary move-speed and attack-speed boost to a selected player lane.
- **Revive** (`RevivePlayerUnits`) arms a one-turn resurrection budget for the first player units that fall during the next battle.
- **Lightning Strike** (`LightningStrikePriorityEnemies`) arms battle-start strikes against high-priority living enemies, favoring support/ranged/backline threats.
- **Duplicated** (`DuplicatePlayerUnits`) clones every current unit from a selected player lane for the current battle only, including pending first-wave preview units before FIGHT.

These spells play a `SpawnEffect` floating-text burst at the affected lane/center or impact point.

### Battlefield VFX conventions

Spell and unit-feedback VFX are visual-only. They should not change `UnitGroup` state or battle rules; put gameplay state on `UnitGroup` / managers, and put presentation on `BattlefieldView` or `UnitGroupView`.

- Use **one group-level effect** for spells that affect a group or lane. Compute the center from the affected `BattleUnit.Rect.anchoredPosition` values, then spawn a single battlefield-root effect there.
- Use **per-unit feedback** only as a small secondary cue (flash, tint, scale pulse), so small units stay readable and a group spell does not become noisy.
- Prefer procedural UI sprites for simple battlefield effects (rings, glows, sparks, beams) when no authored art is needed. Optional sprites are fine for replaceable icons, but the effect should have a code fallback.
- Group effects should parent under `_battleFieldRoot`, use anchored positions, set `raycastTarget = false`, and clean themselves up after their short animation.

Current examples:

- `SpawnEffect` - floating text / smoke bursts for spell labels.
- `BattlefieldMarkVfx` in `BattlefieldView` - Mark feedback: one shrinking aim reticle over the selected enemy lane.
- `BattlefieldLevelUpVfx` in `BattlefieldView` - Upgrade Unit feedback: one golden group VFX at the affected family's center plus `UnitGroupView.PlayLevelUpPulse()` on each affected unit/preview.
- Lightning helpers in `BattlefieldView` - procedural beam, burst, branches, and impact ring.

## Key Tunables

All on `BattlefieldView` unless noted:

| Field | Default | Controls |
|---|---|---|
| `_laneHalfWidth` / `_laneHalfHeight` | 90 / 180 | Random spawn box per lane. |
| `_spawnMinDistance` | 55 | Min gap between units when finding a free spot. |
| `_packXSpacing` / `_packYSpacing` | 38 / 34 | Packed-formation cell size. |
| `_battleBoundsMin` / `_battleBoundsMax` | (-900,-40)/(900,220) | Hard movement clamp. |
| `_maxBattleSeconds` *(GameManager)* | 30 | Battle timeout. |
| `_postBattlePause` *(GameManager)* | 0.5 | Pause after a battle resolves. |
| `_allySeparationDistance` *(BattleUnit)* | 67 | How far allies stand apart. |
| `_separationStrength` *(BattleUnit)* | 420 | Repulsion force strength. |
| `_maxSpeed` *(BattleUnit)* | 420 | Cap on combined per-frame speed; stops pile-up shoving. 0 disables. |
| `_blockedPushFactor` *(BattleUnit)* | 0.35 | How much an ally directly ahead is pushed (1 = full, 0 = none). |
| `_meleeAttackDuration` *(BattleUnit)* | 0.28 | Fixed melee swing length (clamped below cooldown). |

## File Map

```
Scripts/
  Managers/
    GameManager.cs          round loop, wave table, state machine
    BattlefieldManager.cs   authoritative UnitGroup lists + events
  Units/
    UnitGroup.cs            per-fighter logical data
  Battle/
    BattleUnit.cs           per-frame movement / targeting / attack / death
    BattleResolver.cs       (unused legacy)
  UI/
    BattlefieldView.cs      spawn, position, spatial queries, preview, revive, regroup, battlefield VFX
    UnitGroupView.cs        sprite frames, highlight, scale, per-unit visual pulses
    FormationLineView.cs    a lane anchor in the scene
    SpriteFrameAnimator.cs  idle ↔ attack frame swapping
    MoveBounceAnimator.cs   walk bounce
    SpawnEffect.cs          floating text / smoke bursts
```
