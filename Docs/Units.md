# Implementing a Unit

This guide walks through adding a new **Unit** card end-to-end: art, data, registration, and how the unit flows through the runtime. See [Cards.md](Cards.md) for the gameplay rules a Unit obeys once it exists, and [BattleField.md](BattleField.md) for how it fights once summoned.

## The Pipeline at a Glance

```
Art files            CreateStarterCards.cs        Resources/Cards/*.asset
(.png)        ──►    (editor menu: defines  ──►   (CardData ScriptableObjects)
                      stats + loads sprites)              │
                                                          ▼
                                              DeckManager.Resources.LoadAll<CardData>
                                                          │
                            play card ──► PendingUnitBuild ──► UnitGroup ──► battle
```

A Unit is **not** authored by hand in the Inspector. It is declared in code (`CreateStarterCards.cs`), and the editor menu generates the `CardData` asset from that declaration plus the art on disk. This keeps the roster reproducible and version-controlled.

## Step 1 — Add the Art

Two kinds of sprites are needed. Drop the PNGs at these exact paths:

| Asset | Path | Notes |
|---|---|---|
| Card face | `Assets/Art/Cards/Units/{Name}_{Count}.png` | Shown in hand. e.g. `Goblin_4.png`. |
| Idle frame | `Assets/Art/Characters/{Name}/Unit_{Name}_Idle.png` | One frame, drawn while walking/standing. |
| Attack frames | `Assets/Art/Characters/{Name}/Attack/Unit_{Name}_Attack_{n}.png` | Numbered; played in order during an attack. |

`{Name}` is the **character folder name** (the 3rd argument to `CreateUnitCard`), which can differ from the card's display name — e.g. the "Archer x2" card uses the `Ranger` character folder.

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

The card also carries `idleSprite` and `attackFrames`, loaded from the character art folder in Step 1.

## Step 3 — Register the Unit

Add a `CreateUnitCard(...)` call inside `Create()` in [CreateStarterCards.cs](../Assets/Editor/CreateStarterCards.cs), next to the existing Knight/Archer/Cleric entries:

```csharp
CreateUnitCard("goblin_4", "Goblin x4", "Goblin", "Units/Goblin_4.png",
    attack: 40, hp: 180, count: 4, line: FormationLine.Front,
    moveSpeed: 300f, attackRange: 60f, attackCooldown: 0.7f, attackSpeed: 2f,
    unitType: UnitType.Ground);
```

Argument order: `id`, `displayName`, `characterFolder`, `cardSpriteFile`, then the stats. The signature lives at [CreateStarterCards.cs:61](../Assets/Editor/CreateStarterCards.cs#L61).

- **`id`** becomes the asset filename `Card_{id}.asset` and the `cardId`. Keep it unique and kebab/snake-case.
- **`characterFolder`** must match the `Assets/Art/Characters/{folder}/` directory from Step 1.
- **`cardSpriteFile`** is relative to `Assets/Art/Cards/`.

## Step 4 — Generate the Card Asset

Run the menu: **DraftCards → Create Starter Cards**.

This ([CreateStarterCards.cs:17](../Assets/Editor/CreateStarterCards.cs#L17)):
1. Force-imports `Art/Cards/` and `Art/Characters/` so the postprocessor converts new PNGs to sprites.
2. **Deletes every existing `CardData`** under `Assets/Resources/Cards/` — the roster is rebuilt from scratch each run, so a card only survives if it has a `CreateUnitCard`/`CreateSupportCard` call.
3. Re-creates each declared card as `Resources/Cards/Card_{id}.asset`.

Watch the Console: missing sprites log `[DraftCards] ... not found` warnings but do not stop generation. The card will still spawn, just without art.

[DeckManager](../Assets/Scripts/Managers/DeckManager.cs#L30) picks the new card up automatically via `Resources.LoadAll<CardData>` — no further wiring needed.

## Step 5 — Runtime Flow (for reference)

You rarely touch these, but it helps to know where the data goes:

1. When the card is played, a [PendingUnitBuild](../Assets/Scripts/Cards/PendingUnitBuild.cs) is constructed from the `CardData`, copying every `UnitData` field plus the sprites. Spell cards can then mutate this pending build before END.
2. On END, a [UnitGroup](../Assets/Scripts/Units/UnitGroup.cs) is created from the build for each spawned fighter (`Count` is tracked as alive/dead state). It exposes `EffectiveAttackCooldown`, `TakeDamage`, `Revive`, and `ApplyAttackMultiplier` (used by the Strengthen spell).
3. The battlefield manager spawns the physical fighters, which walk, engage at `AttackRange`, and attack every `EffectiveAttackCooldown` seconds.

## Checklist

- [ ] Card face at `Art/Cards/Units/{Name}_{Count}.png`
- [ ] Idle + attack frames under `Art/Characters/{folder}/`
- [ ] `CreateUnitCard(...)` call added with a unique `id`
- [ ] `characterFolder` arg matches the art folder name
- [ ] Ran **DraftCards → Create Starter Cards**, Console shows no "not found" warnings
- [ ] (Optional) Added the unit to the roster table in [Cards.md](Cards.md)
