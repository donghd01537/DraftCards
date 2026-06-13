# Game Overview

> Related: [Cards.md](Cards.md) - card rules and card data; [Units.md](Units.md) - unit authoring and stat model; [BattleField.md](BattleField.md) - wave, lane, and combat flow.

DraftCards is a PC PvE card-based auto battler. The player builds an army across enemy waves, then watches combat resolve automatically. The main decisions happen before battle: choosing one unit group, spending MP on spells, planning formation, and eventually investing MP into Unit Upgrade.

## Design Pillars

| Pillar | Meaning |
|---|---|
| Army-building through cards | Unit Cards add long-term army bodies. Spell Cards create tactical impact before a fight. |
| Automatic combat | Units move, target, attack, take damage, and die without realtime player control. |
| Persistent army | Player units carry forward across the stage. Dead player units revive and regroup after each wave. |
| Shared MP tension | MP should create a choice between immediate spell impact and longer-term Unit Upgrade. |
| Formation planning | Front, Middle, and Back lines should matter for unit role, protection, range, and enemy pressure. |

## Intended Stage Flow

The target stage flow from the design sheet is:

```
Build deck and choose Commander before stage
Enemy wave appears
Player draws 3 Unit Cards and 5 Spell Cards
Player chooses up to 1 Unit Card
Player may cast multiple Spell Cards if MP allows
Player may spend MP on Unit Upgrade
Player confirms battle
Combat starts automatically and resolves
Player army revives and regroups
Unused cards are discarded
Next wave begins
```

A full stage is expected to contain about 8-10 waves. The army should feel like it is being trained and shaped over the whole stage, not restarted every fight.

## Current Prototype Scope

The current Unity prototype implements the core draft -> battle -> reset loop, but not every design-sheet system yet.

| System | Current status |
|---|---|
| Pre-stage deck building | Not implemented. `DeckManager` loads generated player cards from `Resources/Cards`. |
| Commander choice | Not implemented. |
| Unit Upgrade | Not implemented. The design target spends MP on Unit Upgrade from the same pool as Spell Cards. |
| Wave count | Prototype uses 6 scripted waves in `GameManager`, then loops back to Wave 1. |
| Player card choice | Current hand is reset to 3 Unit Cards and 5 Spell Cards each wave. |
| Army persistence | Implemented. Player units revive and regroup between waves. |

When documenting or implementing new features, keep the split clear: design-target rules may describe where the game is going, while current-runtime rules should match the code that exists today.
