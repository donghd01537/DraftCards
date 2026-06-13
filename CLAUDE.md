# CLAUDE.md

DraftCards — a Unity card-battler. The player drafts cards each round, summons units into formation lanes, and an auto-battle resolves against scripted enemy waves.

## Before you start a task

1. **Read the relevant doc(s) in [Docs/](Docs/) first** — they describe intended behavior and the architecture, which is not always obvious from the code alone.
2. **Read the actual source** for the files you'll touch. Don't trust docs over code if they disagree — fix the doc.
3. **Locate the entry point** for the system. Most gameplay flows start in [GameManager](Assets/Scripts/Managers/GameManager.cs).

### Which doc to read

| Working on… | Read |
|---|---|
| Overall game direction, stage flow, planning loop | [Docs/GameOverview.md](Docs/GameOverview.md) |
| Card stats, spells, draft/battle/reset rules | [Docs/Cards.md](Docs/Cards.md) |
| **Adding a new Spell card** | [Docs/Cards.md → Adding a new spell card](Docs/Cards.md#adding-a-new-spell-card) — read this **before** writing any code; it lists every file to touch and the build-mutating-vs-battlefield-spell distinction that drives the whole design. |
| Adding/editing a Unit (art → data → asset) | [Docs/Units.md](Docs/Units.md) |
| Spawning, movement, targeting, combat, waves | [Docs/BattleField.md](Docs/BattleField.md) |

If you add a new system, add or update a doc for it and cross-link it from the related docs. When you add a new card (unit, enemy, or spell), update the relevant table in [Docs/Cards.md](Docs/Cards.md).

## Things that are easy to get wrong

- **Logic vs. visuals are separate layers.** Logical units are `UnitGroup` (held by `BattlefieldManager`); their on-screen representation is `BattleUnit` + `UnitGroupView` (managed by `BattlefieldView`). Changing one usually means touching the other. See [Docs/BattleField.md](Docs/BattleField.md).
- **Cards are generated from data, not authored by hand.** The roster lives in [Assets/Config/cards.json](Assets/Config/cards.json); [CreateStarterCards.cs](Assets/Editor/CreateStarterCards.cs) parses it and the editor menu **DraftCards → Create Starter Cards** regenerates the `CardData` assets, **wiping** the output folders first. A card only survives if it has a `cards.json` entry — add stats there, not in code.
- **Resources layout matters.** Player/draftable cards live in `Resources/Cards`; enemy cards live in `Resources/Enemies` so `DeckManager` never deals them to the player. Don't mix them.
- **Enemy waves are scripted** in the `_waves` table in [GameManager](Assets/Scripts/Managers/GameManager.cs), not drafted.
- **Art import is automatic** for files under `Art/Characters/`, `Art/Cards/`, `Art/Enemies/`, `Art/Effects/` via [CharacterArtPostprocessor.cs](Assets/Editor/CharacterArtPostprocessor.cs). New art folders outside these won't import as sprites.

## Building & verifying

- This repo has **no .NET SDK on the command line** — code compiles inside the Unity editor. Don't claim a change compiles unless it was built in Unity; say it's unverified otherwise.
- After changing the card roster or enemy stats, the user must run **DraftCards → Create Starter Cards** to regenerate assets.
- This is **not a git repository** — there's no commit history to lean on.

## Layout

```
Assets/
  Scripts/   Core (enums) · Data (CardData/UnitData) · Units · Cards
             Managers (GameManager, BattlefieldManager, DeckManager, …)
             Battle (BattleUnit) · UI (BattlefieldView, views, animators)
  Editor/    CreateStarterCards.cs, CharacterArtPostprocessor.cs
  Art/       Characters/ · Cards/ · Enemies/ · Effects/
  Resources/ Cards/ (player) · Enemies/ (waves)
  Scenes/    BattlePrototype.unity (main), SampleScene.unity
Docs/        GameOverview.md · Cards.md · Units.md · BattleField.md
```
