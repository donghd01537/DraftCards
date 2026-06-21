# How the Battle Field Works

This document explains the battlefield: how units get there, how they fight, and how the round loop drives it. For the overall game direction see [GameOverview.md](GameOverview.md); for card/unit authoring see [Units.md](Units.md); for gameplay rules see [Cards.md](Cards.md).

## Scene Flow

The game opens in `Scenes/HomeScene.unity` — a title screen whose **Start** button calls [HomeScreen.StartGame](../Assets/Scripts/UI/HomeScreen.cs), which loads `BattlePrototype` (the battle scene below) via `SceneManager.LoadScene`. Both scenes are registered in Build Settings (HomeScene first, so builds boot to the title screen). The battle starts immediately on load — `GameManager.Start()` kicks off the round loop.

Design target: a full stage should begin with deck and Commander preparation, then run through about 8-10 enemy waves. The current prototype starts directly from the generated player deck and uses 8 scripted waves that loop indefinitely.

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

- `FindLine(isPlayer, line)` looks up the lane view; `LaneAnchoredPosition` converts its world position into the battlefield's local coordinate space. The 3 lanes per side carry `anchoredPosition (0,0)` in the scene — their X is supplied at runtime by the `HorizontalLayoutGroup` on the parent area. Because the **first** wave spawns from `GameManager.Start()` on the scene-load frame, *before* Unity's end-of-frame layout pass, `LaneAnchoredPosition` calls `EnsureLanesLaidOut()` (a one-time `Canvas.ForceUpdateCanvases` + `LayoutRebuilder.ForceRebuildLayoutImmediate` on each area) first; without it Back/Middle/Front all resolve to the same point and the Back archer spawns inside the Front cluster.
- The scene has **6 lane views** total (3 player + 3 enemy). If a wave targets a lane with no matching view, units fall back to position `(0,0)`.
- **Where the two armies sit** is set in the scene by the `PlayerArea` / `EnemyArea` RectTransform anchors (each holds its 3 lanes in a `HorizontalLayoutGroup`). They're anchored toward the screen center (player X ≈ 0.10–0.42, enemy ≈ 0.58–0.90 of a 1920×1080 canvas) so the armies nearly meet, leaving only a small no-man's-land — widen/narrow the gap by moving these anchors, not in code. The vertical band is `y 0.34–0.80` of the canvas, sized to fill the playable sand strip of the battlefield background; the movement-clamp bounds above are the gameplay limits **within** that band.
- **Facing is dynamic.** Each unit flips to face the way it moves or attacks: `BattleUnit.Update` picks a horizontal facing (toward the target while attacking, else the velocity's X) past a small `_facingDeadzone`, and calls `UnitGroupView.SetFacing`, which flips the **sprite container's** X scale (not the unit root — so it never fights the heal/level-up scale pulses or the move bounce). The source art faces right, so players start facing right and enemies left (`SpawnUnitView` seeds it via `SetFacing(forPlayer)`); from then on it's driven by motion. This matters for cavalry, which attack the backline from odd angles and would otherwise strike backwards.

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
- `BuildPackedPositions` / `PackedOffset` — deterministic packing used for previews and regroup: fills a column top-to-bottom (5 rows), then branches into adjacent side columns, each also filled vertically (`_packXSpacing` × `_packYSpacing`), so a group forms a compact block of short files. `PackedJitter` then adds a small deterministic per-slot offset (~±40% of a cell) so the block reads as a natural mob rather than a ruler-straight grid — same index always yields the same jitter so preview units and the real units they hand off to land on the same spots. (Enemies skip packing entirely; they scatter via `FindEmptyPosition`.)
- `StartDrop` — a drop-in animation (fall + squash/stretch pop) on spawn.

## The Battle Simulation

The whole fight is driven by `BattleUnit.Update()` running per-frame on each active unit. There is no central tick — units act independently. Per frame a unit:

1. **Target & act** (only when `_active`, after a small random `_activationDelay`):
   - `FindClosestOpponent` picks the nearest living unit on the other side.
   - If farther than `AttackRange`: record a move direction toward it (applied at `EffectiveMoveSpeed` below).
   - If within range: count down `_cooldownTimer`; on zero, `PerformAttack` and reset to `EffectiveAttackCooldown` (= `AttackCooldown / AttackSpeed`).
   - **Front-rank gating (melee only).** Before attacking, a melee unit (`ProjectileSprite == null`, non-cavalry) asks `IBattleSpatial.IsFrontBlocked(self, target)`: is a living ally standing in the corridor between it and the target, closer to the target than it is? If so it's a rear-rank unit screened by its own front line — it does **not** attack, and keeps pressing toward the target instead. Without this, every unit in a deep mob attacks *through* the bodies ahead (so 16 goblins all hit one front-liner at once) and each also contributes a separation push to the enemy, summing into a line-shoving force. Ranged units (they shoot over heads) and cavalry (flankers) are exempt. The screened unit also gets its cross-side separation push **zeroed** while blocked (`_frontBlocked` in `BattleUnit.ComputeSeparation`), so it packs in behind the front rather than bulldozing it. Corridor tunables live on `BattlefieldView`: `_frontBlockMinAdvance` (how far ahead an ally must be to count) and `_frontBlockCorridor` (half-width of the screening lane).
2. **Separation** (`ComputeSeparation`) — soft repulsion from nearby units. Allies keep `_allySeparationDistance` apart; enemies use body radii (`_bodyRadius`). This keeps units touching but not overlapping. Pairs at the exact same spot get a deterministic scatter direction. A push against an **ally standing in the move direction** is softened toward `_blockedPushFactor`, so a rear unit presses the back of the line instead of bulldozing it forward. A push against an **opponent** is scaled by `_enemyPushFactor`, so a deep formation can't sum its contact pushes and slide the whole enemy line off the field.
3. **Settle** — if not yet active but has a settle target (post-spawn/regroup), drift toward it.
4. **Move** — sum separation + move + settle, **clamp the combined speed to `_maxSpeed`** (so a pile-up of pushes can't fling a unit across the field), then apply, clamped to bounds. A debounced moving/idle flag drives the walk bounce via `MoveBounceAnimator`.

### Attacking & damage
`PerformAttack` plays the attack frame animation (`UnitGroupView.PlayAttack`) and either calls `target.TakeDamage(Group.TotalDamage)` immediately for melee units, launches the configured projectile sprite and applies damage on impact, or for Thunder Bird calls a small top-down lightning strike attack. `TotalDamage = Attack × Count` (Count is 1, so it's just `Attack`). `BattleUnit.TakeDamage` applies it to the `UnitGroup`; if HP hits 0, it notifies death. Direct projectiles such as arrows calculate one intercept point from the target's current velocity, fly there at fixed speed, and can miss if the target moves away; lobbed projectiles with `projectileAoeRadius > 0` deal splash damage around the impact point.

### Cavalry skirmishers

Most units chase the **nearest** living opponent (`FindClosestOpponent`). A unit whose `UnitType` is `Cavalry` (Wolf Rider) behaves differently in two ways:

- **Targeting** — it goes for the enemy **backline**, by priority tier in `BattlefieldView.FindClosestOpponent`: (1) **enemy Cavalry** — if both sides field cavalry, they clash in the open *first*; (2) **Back** line — the intended prize; (3) **Middle**; (4) **Front** as a last resort so it never idles. Each tier takes the closest member. The filter is general (works for cavalry on either side).
- **Approach (flank charge)** — instead of running straight into the front line, it sweeps out to the nearest top/bottom **runway** edge, crosses the contact line *there* (where there are no front-liners), then dives in at its Middle/Back target. This is a movement-shaping state machine in `BattleUnit` (`FlankPhase` / `TryGetFlankDir`). **Cavalry have their own movement bounds.** Line units are clamped to the tight infantry band (`_battleBoundsMin/Max`, the dense strip the formation occupies); cavalry are clamped to a much taller **runway** band (`_cavalryBoundsMin/Max`) that reaches into the empty space above and below the army. That runway *is* the visible top/bottom lane the player sees cavalry ride through, and `TryGetFlankDir` sweeps along **its** edge (not the infantry band's). The cavalry-aware clamp is `IBattleSpatial.ClampToBounds(pos, forCavalry)`; `BattleUnit.IsCavalry` selects it for movement and settling. On activation a cavalry unit enters `Arcing` and latches its edge (whichever of top/bottom it spawned nearer) and a contact line (the X midpoint between it and the target). It then steers along **one continuous curve** (not two hard legs, which used to kink into a right angle): the X (cross) and Y (swing-out) weights are blended with a smootherstep against how far it has climbed toward the edge, so it leaves mostly vertical, rounds the corner gradually, and arrives mostly horizontal. `_flankEdgeBand` is the Y window near the edge within which the curve has fully blended to the horizontal cross — a larger band rounds the corner sooner/more gently (raised now the runway is taller). Once it crosses the contact line (onto the target's side) the arc is spent (`Done`) and it resumes normal chasing for the final dive. Tunables on `BattleUnit`: `_flankEdgeReach`, `_flankEdgeInset`, `_flankEdgeBand`. The flank only shapes the *approach*; it never changes combat or targeting, and it re-plans each battle (reset in `SetActive`) so a regrouped survivor flanks fresh. **Cavalry-vs-cavalry:** when the chosen target is itself cavalry, the arc is skipped (`targetIsCavalry` guard) and the unit charges straight — the two skirmishers duel in the open. A survivor that then retargets a line unit flanks normally (the arc wasn't consumed during the duel).

Cavalry are deployed on the **Middle** line (behind their own front melee, but still pushing forward) so the flank charge starts from just behind the wall. The type is parsed from `unitType` in [cards.json](../Assets/Config/cards.json) and flows through `UnitData` → `PendingUnitBuild` → `UnitGroup.UnitType` like any other stat. Movement bounds for the arc come from `IBattleSpatial.BattleBoundsMin/Max`.

### Support healers (Cleric / Shaman)

Some units are **support healers**. After every `HealEveryAttacks` normal attacks (counted per unit, reset at battle start in `SetActive`), the unit heals **one ally — the most-hurt living unit on its own side by HP fraction, with the healer itself eligible** — for `HealAmount` HP (a single Cleric heals a single unit, not the whole line). The cadence/amount come from the card data (`UnitData.healEveryAttacks` / `healAmount` in [cards.json](../Assets/Config/cards.json)), flow through `PendingUnitBuild` onto `UnitGroup` (`IsHealer`, `HealEveryAttacks`, `HealAmount`), and fire from `BattleUnit.PerformAttack`. The heal itself is `UnitGroup.Heal` (caps at max HP, won't resurrect the dead); the target pick + the green `UnitGroupView.PlayHealPulse` cue with small floating plus signs live in `BattlefieldView.HealAlly` (`IBattleSpatial`). The side-wide `HealAllies` is kept on the interface for possible later use (e.g. a group-heal spell) but isn't wired to anything. The Cleric (player) and the Shaman (enemy) both use this — the Shaman heals the enemy army, the Cleric the player army. Counting happens on the attack **swing**, so it works the same for the ranged Cleric and Shaman regardless of projectile travel time.

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
| 1 | 10 Goblin (Front) + 1 Goblin Archer (Back) |
| 2 | 10 Goblin (Front) + 2 Wolf Rider (Middle) + 3 Goblin Archer (Back) |
| 3 | 2 Orc + 10 Goblin (Front) + 4 Wolf Rider (Middle) + 4 Goblin Archer (Back) |
| 4 | 3 Orc + 10 Goblin (Front) + 2 Shaman + 6 Wolf Rider (Middle) + 4 Goblin Archer (Back) |
| 5 | 4 Orc + 12 Goblin (Front) + 3 Shaman + 8 Wolf Rider (Middle) + 5 Goblin Archer (Back) |
| 6 | 4 Orc + 14 Goblin (Front) + 3 Shaman + 8 Wolf Rider (Middle) + 5 Goblin Archer + 2 Thunder Bird (Back) |
| 7 | 5 Orc + 14 Goblin (Front) + 3 Shaman + 8 Wolf Rider (Middle) + 5 Goblin Archer + 1 Cyclop (Back) |
| 8 | 6 Orc + 16 Goblin (Front) + 4 Shaman + 10 Wolf Rider (Middle) + 6 Goblin Archer + 2 Cyclop + 3 Thunder Bird (Back) |

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

These spells play a `SpellBurst` icon burst at the affected lane/center or impact point. The battlefield is **text-free** — feedback reads by icon silhouette and color, not words.

### Battlefield VFX conventions

Spell and unit-feedback VFX are visual-only. They should not change `UnitGroup` state or battle rules; put gameplay state on `UnitGroup` / managers, and put presentation on `BattlefieldView` or `UnitGroupView`.

- Use **one group-level effect** for spells that affect a group or lane. Compute the center from the affected `BattleUnit.Rect.anchoredPosition` values, then spawn a single battlefield-root effect there.
- Use **per-unit feedback** only as a small secondary cue (flash, tint, scale pulse), so small units stay readable and a group spell does not become noisy.
- Prefer procedural UI sprites for simple battlefield effects (rings, glows, sparks, beams) when no authored art is needed. Optional sprites are fine for replaceable icons, but the effect should have a code fallback.
- Group effects should parent under `_battleFieldRoot`, use anchored positions, set `raycastTarget = false`, and clean themselves up after their short animation.

Current examples:

- `SpellBurst` - the text-free spell feedback: an expanding glow ring plus a procedural icon (shield / up-arrow / down-arrow / crosshair / snowflake / plus / bolt / swirl) keyed per spell, so the player reads *which* spell fired by silhouette and color. Replaces the old floating word labels (SHIELD, +50% ATK, ZAP 40, …).
- `SpawnEffect` - legacy floating text / smoke burst (no longer used on the battlefield; kept for reference).
- `BattlefieldMarkVfx` in `BattlefieldView` - Mark feedback: one shrinking aim reticle over the selected enemy lane.
- `BattlefieldLevelUpVfx` in `BattlefieldView` - Upgrade Unit feedback: one golden group VFX at the affected family's center plus `UnitGroupView.PlayLevelUpPulse()` on each affected unit/preview.
- Lightning helpers in `BattlefieldView` - procedural beam, burst, branches, and impact ring.
- `GroundCrackVfx` in `Projectile` - Cyclop rock impact feedback: procedural cracks, dust, and shock ring at the lobbed projectile's impact point.

## Key Tunables

All on `BattlefieldView` unless noted:

| Field | Default | Controls |
|---|---|---|
| `_laneHalfWidth` / `_laneHalfHeight` | 70 / 150 | Random spawn box per lane. |
| `_spawnMinDistance` | 40 | Min gap between units when finding a free spot. |
| `_packXSpacing` / `_packYSpacing` | 30 / 26 | Packed-formation cell size. Tight; 5-row columns plus per-slot jitter give a natural block — see `PackedOffset` / `PackedJitter`. |
| `_battleBoundsMin` / `_battleBoundsMax` | (-1000,-120)/(1000,320) | **Infantry** movement clamp — the band the formation occupies. Widened to fill more of the new background's sand strip. |
| `_cavalryBoundsMin` / `_cavalryBoundsMax` | (-1000,-260)/(1000,440) | **Cavalry** movement clamp — a taller runway reaching into the empty space just above/below the infantry band (but kept within the visible sand strip, not the screen edges), the lane cavalry flank along. Kept distinctly taller than the infantry band so Wolf Riders still ride around the outside. |
| `_maxBattleSeconds` *(GameManager)* | 30 | Battle timeout. |
| `_postBattlePause` *(GameManager)* | 0.5 | Pause after a battle resolves. |
| `_allySeparationDistance` *(BattleUnit)* | 46 | How far allies stand apart. |
| `_separationStrength` *(BattleUnit)* | 420 | Repulsion force strength. |
| `_maxSpeed` *(BattleUnit)* | 420 | Cap on combined per-frame speed; stops pile-up shoving. 0 disables. |
| `_blockedPushFactor` *(BattleUnit)* | 0.35 | How much an ally directly ahead is pushed (1 = full, 0 = none). |
| `_enemyPushFactor` *(BattleUnit)* | 0.4 | How much an opponent is pushed by separation (1 = full, 0 = none). Lower it so a big formation can't shove enemies off the field. |
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
