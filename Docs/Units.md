# Implementing a Unit

This guide walks through adding a new **Unit** card end-to-end: art, data, registration, and how the unit flows through the runtime. See [GameOverview.md](GameOverview.md) for the overall game loop, [Cards.md](Cards.md) for the gameplay rules a Unit obeys once it exists, and [BattleField.md](BattleField.md) for how it fights once summoned.

## The Pipeline at a Glance

```
Art files + cards.json       CreateStarterCards.cs        Resources/Cards/*.asset
(.png + data)          ──►    (editor menu: parses  ──►   (CardData ScriptableObjects)
                               stats + loads sprites)              │
                                                          ▼
                                              DeckManager.Resources.LoadAll<CardData>
                                                          │
                            play card ──► PendingUnitBuild ──► UnitGroup ──► battle
```

A Unit is **not** authored by hand in the Inspector. It is declared in [Assets/Config/cards.json](../Assets/Config/cards.json), and the editor menu generates the `CardData` asset from that data plus the art on disk. This keeps the roster reproducible and version-controlled.

## Unit Design Model

The design sheet treats units as readable battlefield roles, not isolated stat blocks.

| Design axis | Meaning |
|---|---|
| **HP** | How long the fighter survives. Front-line roles usually need more HP. |
| **Attack** | Damage per hit or attack value. Balance it together with attack speed and group size. |
| **Attack Speed** | How often the unit attacks. Fast attacks feel responsive but scale strongly with buffs. |
| **Move Speed** | How quickly the unit reaches combat. High speed can change battle tempo dramatically. |
| **Range** | Whether the unit fights from melee, middle distance, or long distance. Range is a major part of line identity. |
| **Group Size** | A weak unit in a large group can outvalue a strong single unit. Always balance stats against `count`. |
| **Preferred Line** | Front, Middle, and Back should communicate role, protection needs, and target exposure. |

Design target stats also include **Defense** and **Target Priority**. Those are planned design concepts, but they are not implemented in the current `UnitData` class.

<a id="unit-evolution"></a>
## Unit Evolution (Upgrade Unit spell)

The in-stage Unit Upgrade system is implemented as the **Upgrade Unit** spell. It spends MP from the shared pool (so it competes with Spell Cards), and evolution happens during the planning phase, never during realtime battle.

### Player-facing flow

1. Play the **Upgrade Unit** spell (click it in hand). This opens a **modal picker**.
2. The picker shows one card per distinct on-field player unit **family** that can still upgrade, with a hint of the next step (`+10% all stats` or `→ Spartan`).
3. Choose one to upgrade that family; **Cancel** aborts with no MP spent and the card kept.

### Rules

- **Dynamic cost:** the first Upgrade this run costs **5 MP**, and each subsequent Upgrade costs **+1 more** (6, 7, …). The counter **persists for the whole run** and never resets. The `mpCost: 5` in `cards.json` is only the starting cost.
- **Upgrade ladder per family:** a unit family is identified by its base (root) unit's `cardId`. Upgrading walks the family's `evolution` ladder one rung per use:
  - **Stat-only rung** (`statMultiplier`, no `evolveToId`): scales the on-field units' stats (and the pending summon) by that factor. The Swordsman's first rung is `+10%` all stats.
  - **Evolution rung** (`evolveToId` set): the family changes identity. On-field units **re-skin** to the evolved card (new art, name, stats), and the evolved card **replaces the base in the deck pool** — future Unit draws deal the evolved form and the base never reappears this run. Swordsman's second rung evolves it to **Spartan**.
- **What an upgrade affects:** the units of that family already on the battlefield, the current pending summon (and its previews) if it belongs to the family, and — on an evolution — the draftable card pool. A stat-only upgrade is a one-time buff to the units present; it does **not** retroactively buff Swordsmen drafted later (only the evolution changes the pool).
- **Upgrade feedback:** the battlefield plays one group-level level-up VFX at the affected family's center, then applies a small flash/scale pulse to each affected unit and preview. This is presentation-only; it does not affect upgrade rules or stats.
- More than one upgrade may happen in a wave if MP allows.

### Authoring the `evolution` ladder

Add an `evolution` array and `familyRootId` to the base unit in [cards.json](../Assets/Config/cards.json), and add the evolved form as its own `units` entry flagged `excludeFromInitialDeck` so it can't be drafted before it's unlocked. The evolved card's authored stats should **bake in** any carried stat multiplier from earlier rungs (re-skin uses the evolved card's absolute stats).

```json
{
  "id": "swordsman_3", "name": "Swordsman x3", "characterFolder": "Knight", ...,
  "familyRootId": "swordsman_3",
  "evolution": [
    { "statMultiplier": 1.10 },
    { "statMultiplier": 1.0, "evolveToId": "spartan_3" }
  ]
},
{
  "id": "spartan_3", "name": "Spartan x3", "characterFolder": "Spartan",
  "spriteFile": "Units/Spartan_3.png",
  "attack": 121, "hp": 2860, ...,   // = Swordsman base x1.10, baked in
  "familyRootId": "swordsman_3",
  "excludeFromInitialDeck": true
}
```

### Code map

- [UpgradeManager](../Assets/Scripts/Managers/UpgradeManager.cs) — per-family level + dynamic MP cost; resolves the next `UpgradeStep`.
- [CardPlayManager](../Assets/Scripts/Managers/CardPlayManager.cs) — `IsUpgradeUnitCard`, dynamic `EffectiveMpCost`, `GetUpgradeableFamiliesOnField`, `OnUpgradeRequested`, `CommitUpgrade`.
- [UpgradeSelectionPanel](../Assets/Scripts/UI/UpgradeSelectionPanel.cs) — the modal picker (reuses the `CardView` prefab).
- [BattlefieldView.UpgradeOnFieldUnits](../Assets/Scripts/UI/BattlefieldView.cs) — applies the step to live units / pending build, then triggers the group VFX; [UnitGroup](../Assets/Scripts/Units/UnitGroup.cs) `ApplyStatMultiplier` / `ReskinTo`.
- `BattlefieldLevelUpVfx` in [BattlefieldView](../Assets/Scripts/UI/BattlefieldView.cs) plus [UnitGroupView.PlayLevelUpPulse](../Assets/Scripts/UI/UnitGroupView.cs) — visual-only upgrade feedback.
- [DeckManager.ReplaceCardInPool](../Assets/Scripts/Managers/DeckManager.cs) — swaps base→evolved in the draw/discard piles; honors `excludeFromInitialDeck`.

### Future evolution direction

- Each base unit should first pass through a simple stat bump before specializing; later tiers should branch into clear roles. Only the Swordsman→Spartan chain is authored today; more chains are config-only additions.

## Step 1 — Add the Art

Two kinds of sprites are needed. Drop the PNGs at these exact paths:

| Asset | Path | Notes |
|---|---|---|
| Card face | `Assets/Art/Cards/Units/{Name}_{Count}.png` | Shown in hand. e.g. `Goblin_4.png`. |
| Idle frame | `Assets/Art/Characters/{Name}/Unit_{Name}_Idle.png` | One frame, drawn while walking/standing. |
| Attack frames | `Assets/Art/Characters/{Name}/Attack/Unit_{Name}_Attack_{n}.png` | Numbered; played in order during an attack. |

`{Name}` is the **character folder name** (`characterFolder` in `cards.json`), which can differ from the card's display name — e.g. the "Archer x2" card uses the `Ranger` character folder.

You do **not** need to set the texture type to `Sprite` manually. [CharacterArtPostprocessor.cs](../Assets/Editor/CharacterArtPostprocessor.cs) auto-imports anything under `Art/Characters/`, `Art/Cards/`, `Art/Enemies/`, or `Art/Effects/` as a `Sprite` (100 PPU, no mipmaps, alpha-as-transparency).

**Art size sets on-screen size.** Units render at their original art proportions, scaled by a single shared factor (`UnitGroupView._spriteScale`, default `0.47`). A unit drawn on a larger canvas appears larger in battle — e.g. the Orc's `143×164` art is bigger than the Goblin's `97×120`. Author each character's idle frame at the size you want it to appear relative to the others. The ground shadow tracks each sprite's feet automatically.

## Step 2 — Understand the Data Model

A Unit card is a [CardData](../Assets/Scripts/Data/CardData.cs) ScriptableObject with `cardType = Unit` and a populated [UnitData](../Assets/Scripts/Data/UnitData.cs):

| `UnitData` field | Meaning | Default |
|---|---|---|
| `attack` | Damage dealt per attack. | — |
| `hp` | Starting health. | — |
| `count` | Number of fighters spawned by the card. | — |
| `spawnLine` | `Front` / `Middle` / `Back` formation lane ([FormationLine](../Assets/Scripts/Core/Enums.cs)). | — |
| `unitType` | `Ground` or `Flying` ([UnitType](../Assets/Scripts/Core/Enums.cs)). Informational for now. | `Ground` |
| `moveSpeed` | Pixels/second on the battlefield. | `120` |
| `attackRange` | Engage distance (small = melee, large = ranged). | `40` |
| `attackCooldown` | Base seconds between attacks. | `1.0` |
| `attackSpeed` | Rate multiplier. Effective gap = `attackCooldown / attackSpeed`. | `1` |
| `projectileSpeed` | Optional ranged projectile travel speed. `0` uses the launcher default. | `650` |
| `projectileAoeRadius` | Optional splash radius for lobbed projectiles. `0` means a direct single-target projectile, such as an arrow. | `0` |
| `shadowScale` | Visual-only ground shadow scale. | `1` |

The card also carries `idleSprite` and `attackFrames`, loaded from the character art folder in Step 1.

## Step 3 — Register the Unit

Add an entry to the `units` array in [cards.json](../Assets/Config/cards.json):

```json
{
  "id": "soldier_3",
  "name": "Soldier x3",
  "characterFolder": "Soldier",
  "spriteFile": "Units/Soldier_3.png",
  "attack": 80,
  "hp": 1400,
  "count": 3,
  "line": "Front",
  "moveSpeed": 250,
  "attackRange": 75,
  "attackCooldown": 1.0,
  "attackSpeed": 1.8,
  "unitType": "Ground"
}
```

- **`id`** becomes the asset filename `Card_{id}.asset` and the `cardId`. Keep it unique and kebab/snake-case.
- **`characterFolder`** must match the `Assets/Art/Characters/{folder}/` directory from Step 1.
- **`spriteFile`** is relative to `Assets/Art/Cards/`.
- Optional fields include `projectileSprite`, `projectileSpeed`, `projectileAoeRadius`, and `shadowScale`.

## Step 4 — Generate the Card Asset

Run the menu: **DraftCards → Create Starter Cards**.

This ([CreateStarterCards.cs](../Assets/Editor/CreateStarterCards.cs)):
1. Force-imports `Art/Cards/` and `Art/Characters/` so the postprocessor converts new PNGs to sprites.
2. **Deletes every existing `CardData`** under `Assets/Resources/Cards/` and `Assets/Resources/Enemies/` — the roster is rebuilt from scratch each run, so a card only survives if it has a matching `cards.json` entry.
3. Re-creates each declared card as `Resources/Cards/Card_{id}.asset`.

Watch the Console: missing sprites log `[DraftCards] ... not found` warnings but do not stop generation. The card will still spawn, just without art.

[DeckManager](../Assets/Scripts/Managers/DeckManager.cs#L30) picks the new card up automatically via `Resources.LoadAll<CardData>` — no further wiring needed.

## Step 5 — Runtime Flow (for reference)

You rarely touch these, but it helps to know where the data goes:

1. When the card is played, a [PendingUnitBuild](../Assets/Scripts/Cards/PendingUnitBuild.cs) is constructed from the `CardData`, copying every `UnitData` field plus the sprites. Spell cards can then mutate this pending build before battle is confirmed.
2. When battle is confirmed, a [UnitGroup](../Assets/Scripts/Units/UnitGroup.cs) is created from the build for each spawned fighter (`Count` is tracked as alive/dead state). It exposes `EffectiveAttackCooldown`, `TakeDamage`, `Revive`, and spell-facing state such as shield, rally, slow, mark, and damage reduction.
3. The battlefield manager spawns the physical fighters, which walk, engage at `AttackRange`, and attack every `EffectiveAttackCooldown` seconds.

## Checklist

- [ ] Card face at `Art/Cards/Units/{Name}_{Count}.png`
- [ ] Idle + attack frames under `Art/Characters/{folder}/`
- [ ] `cards.json` unit entry added with a unique `id`
- [ ] `characterFolder` arg matches the art folder name
- [ ] Ran **DraftCards → Create Starter Cards**, Console shows no "not found" warnings
- [ ] (Optional) Added the unit to the roster table in [Cards.md](Cards.md)
