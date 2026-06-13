using System.Collections;
using System.Collections.Generic;
using DraftCards.Battle;
using DraftCards.Cards;
using DraftCards.Core;
using DraftCards.Data;
using DraftCards.Managers;
using DraftCards.Units;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    public class BattlefieldView : MonoBehaviour, IBattleSpatial
    {
        [SerializeField] private CardPlayManager _cardPlayManager;
        [SerializeField] private BattlefieldManager _battlefieldManager;
        [SerializeField] private UnitGroupView _unitViewPrefab;
        [SerializeField] private RectTransform _battleFieldRoot;
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Sprite _backgroundSprite;
        [SerializeField] private FormationLineView[] _playerLines;
        [SerializeField] private FormationLineView[] _enemyLines;

        [SerializeField] private float _laneHalfWidth = 90f;
        [SerializeField] private float _laneHalfHeight = 180f;
        [SerializeField] private float _spawnMinDistance = 55f;
        [SerializeField] private int _spawnMaxAttempts = 40;
        [SerializeField] private float _packXSpacing = 38f;
        [SerializeField] private float _packYSpacing = 34f;
        [SerializeField] private float _previewSpawnStagger = 0.08f;
        [SerializeField] private float _previewSpawnOffset = 26f;

        [SerializeField] private Vector2 _battleBoundsMin = new(-900f, -40f);
        [SerializeField] private Vector2 _battleBoundsMax = new(900f, 220f);
        [SerializeField] private Sprite _smokeSprite;
        [SerializeField] private Color _summonTextColor = new(1f, 0.85f, 0.2f);
        [SerializeField] private Color _spellTextColor = new(0.55f, 0.85f, 1f);
        [SerializeField] private Color _markTextColor = new(1f, 0.16f, 0.12f);
        [SerializeField] private Color _lightningTextColor = new(0.45f, 0.9f, 1f);
        [SerializeField] private Sprite _levelUpArrowSprite;
        [SerializeField] private float _lightningBeamWidth = 82f;
        [SerializeField] private float _lightningStrikeDuration = 0.46f;
        [SerializeField] private float _lightningStartHeight = 390f;
        [SerializeField] private float _meteorBattleStartDelay = 1f;
        [SerializeField] private float _meteorTravelDuration = 0.82f;
        [SerializeField] private float _meteorBurnDuration = 2.6f;

        private readonly List<BattleUnit> _playerBattleUnits = new();
        private readonly List<BattleUnit> _enemyBattleUnits = new();
        private readonly List<BattleUnit> _deadUnits = new();
        private readonly Dictionary<UnitGroup, BattleUnit> _byGroup = new();
        private readonly List<BattleUnit> _previewUnits = new();
        private readonly List<Vector2> _previewPositions = new();
        private int _placementCursor;
        private Coroutine _previewSpawnRoutine;

        // Revive spell budget for the current turn: the next _reviveBudget player units to
        // die this battle are resurrected at _reviveHpFraction of max HP (see NotifyDeath).
        // Armed during the draft by RevivePlayerUnits, reset each turn by ResetTurnEffects.
        private int _reviveBudget;
        private float _reviveHpFraction;

        private readonly List<PendingLightningStrike> _pendingLightningStrikes = new();
        private readonly List<PendingMeteorStrike> _pendingMeteorStrikes = new();
        private int _battleStartSpellToken;

        public bool BothSidesAlive => _playerBattleUnits.Count > 0 && _enemyBattleUnits.Count > 0;
        public bool HasAlivePlayer => _playerBattleUnits.Count > 0;
        public bool HasAliveEnemy => _enemyBattleUnits.Count > 0;

        private void Awake()
        {
            ApplyBackground();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyBackground();
        }
#endif

        public void StartBattle()
        {
            _battleStartSpellToken++;
            TriggerBattleStartSpells(_battleStartSpellToken);
            foreach (BattleUnit u in _playerBattleUnits) u.SetActive(true);
            foreach (BattleUnit u in _enemyBattleUnits) u.SetActive(true);
            TriggerSlowFieldVfx();
        }

        public void PauseBattle()
        {
            _battleStartSpellToken++;
            foreach (BattleUnit u in _playerBattleUnits) u.SetActive(false);
            foreach (BattleUnit u in _enemyBattleUnits) u.SetActive(false);
            ClearProjectiles();
        }

        // Remove any in-flight thrown projectiles (rocks). Includes inactive ones so a
        // stalled projectile can't survive on the field between battles.
        public void ClearProjectiles()
        {
            if (_battleFieldRoot == null) return;
            Projectile[] projectiles = _battleFieldRoot.GetComponentsInChildren<Projectile>(true);
            foreach (Projectile p in projectiles)
            {
                if (p != null) Destroy(p.gameObject);
            }
        }

        public void RegroupAllUnits()
        {
            RegroupSide(true, _playerBattleUnits);
            RegroupSide(false, _enemyBattleUnits);
        }

        private void RegroupSide(bool isPlayer, List<BattleUnit> units)
        {
            Dictionary<FormationLine, List<BattleUnit>> byLine = new();
            foreach (BattleUnit u in units)
            {
                if (u == null || u.IsDead || u.Group == null) continue;
                FormationLine line = u.Group.Line;
                if (!byLine.TryGetValue(line, out List<BattleUnit> list))
                {
                    list = new List<BattleUnit>();
                    byLine[line] = list;
                }
                list.Add(u);
            }

            Vector2 limbo = new(-99999f, -99999f);
            foreach (KeyValuePair<FormationLine, List<BattleUnit>> kv in byLine)
            {
                List<Vector2> positions = BuildPackedPositions(isPlayer, kv.Key, kv.Value.Count);
                foreach (BattleUnit u in kv.Value)
                {
                    u.Rect.anchoredPosition = limbo;
                }

                for (int i = 0; i < kv.Value.Count && i < positions.Count; i++)
                {
                    kv.Value[i].Rect.anchoredPosition = positions[i];
                }
            }
        }

        public BattleUnit FindClosestOpponent(BattleUnit self)
        {
            List<BattleUnit> pool = self.IsPlayerUnit ? _enemyBattleUnits : _playerBattleUnits;
            BattleUnit closest = null;
            float bestSqr = float.MaxValue;
            Vector2 myPos = self.Rect.anchoredPosition;
            foreach (BattleUnit candidate in pool)
            {
                if (candidate == null || candidate.IsDead) continue;
                float sqr = (candidate.Rect.anchoredPosition - myPos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    closest = candidate;
                }
            }
            return closest;
        }

        public void NotifyDeath(BattleUnit unit)
        {
            if (unit == null) return;

            // Revive spell: the first X player units to fall this battle spring back at
            // partial HP and rejoin the fight instead of dying. The budget is a one-turn
            // effect (armed during the draft, reset at the start of each turn).
            if (unit.IsPlayerUnit && _reviveBudget > 0)
            {
                _reviveBudget--;
                unit.ReviveInBattle(_reviveHpFraction);
                Vector2 fxPos = unit.Rect != null ? unit.Rect.anchoredPosition : Vector2.zero;
                int pct = Mathf.RoundToInt(_reviveHpFraction * 100f);
                SpawnEffect.Play(_battleFieldRoot, fxPos, $"REVIVE {pct}%", _smokeSprite, _spellTextColor);
                return;
            }

            if (unit.IsPlayerUnit) _playerBattleUnits.Remove(unit);
            else _enemyBattleUnits.Remove(unit);

            _deadUnits.Add(unit);
            unit.gameObject.SetActive(false);
        }

        public void ClearEnemyUnits()
        {
            ClearProjectiles();
            // Destroy every enemy view — alive and dead — so the next round's wave
            // starts from a clean battlefield. Player units (and their dead) are kept.
            foreach (BattleUnit u in _enemyBattleUnits)
            {
                DestroyEnemyUnit(u);
            }
            _enemyBattleUnits.Clear();

            for (int i = _deadUnits.Count - 1; i >= 0; i--)
            {
                BattleUnit u = _deadUnits[i];
                if (u == null || !u.IsPlayerUnit)
                {
                    DestroyEnemyUnit(u);
                    _deadUnits.RemoveAt(i);
                }
            }
        }

        public void ClearTemporaryPlayerUnits()
        {
            for (int i = _playerBattleUnits.Count - 1; i >= 0; i--)
            {
                BattleUnit u = _playerBattleUnits[i];
                if (u == null || u.Group == null || !u.Group.TemporaryBattleOnly) continue;
                _playerBattleUnits.RemoveAt(i);
                DestroyPlayerUnit(u);
            }

            for (int i = _deadUnits.Count - 1; i >= 0; i--)
            {
                BattleUnit u = _deadUnits[i];
                if (u == null || u.Group == null || !u.Group.IsPlayerUnit || !u.Group.TemporaryBattleOnly) continue;
                _deadUnits.RemoveAt(i);
                DestroyPlayerUnit(u);
            }
        }

        private void DestroyEnemyUnit(BattleUnit unit)
        {
            if (unit == null) return;
            if (unit.Group != null) _byGroup.Remove(unit.Group);
            Destroy(unit.gameObject);
        }

        private void DestroyPlayerUnit(BattleUnit unit)
        {
            if (unit == null) return;
            if (unit.Group != null)
            {
                _battlefieldManager?.RemoveSilent(unit.Group);
                _byGroup.Remove(unit.Group);
            }
            Destroy(unit.gameObject);
        }

        public void ReviveAllDead()
        {
            foreach (BattleUnit u in _deadUnits)
            {
                if (u == null) continue;
                u.Revive();
                u.gameObject.SetActive(true);
                if (u.IsPlayerUnit) _playerBattleUnits.Add(u);
                else _enemyBattleUnits.Add(u);
            }
            _deadUnits.Clear();
        }

        public IEnumerable<BattleUnit> GetAllUnits()
        {
            foreach (BattleUnit u in _playerBattleUnits) yield return u;
            foreach (BattleUnit u in _enemyBattleUnits) yield return u;
            foreach (BattleUnit u in _previewUnits) yield return u;
        }

        public FormationLine? FindPlayerLaneAtScreenPoint(Vector2 screenPos)
        {
            if (_playerLines == null) return null;
            // Strict containment only. Off-lane drops resolve to null so the hover highlight
            // matches what the user is actually pointing at.
            foreach (FormationLineView lineView in _playerLines)
            {
                if (lineView == null || lineView.transform == null) continue;
                RectTransform rect = (RectTransform)lineView.transform;
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null))
                {
                    return lineView.Line;
                }
            }
            return null;
        }

        public FormationLine? FindEnemyLaneAtScreenPoint(Vector2 screenPos)
        {
            if (_enemyLines == null) return null;
            foreach (FormationLineView lineView in _enemyLines)
            {
                if (lineView == null || lineView.transform == null) continue;
                RectTransform rect = (RectTransform)lineView.transform;
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null))
                {
                    return lineView.Line;
                }
            }
            return null;
        }

        private readonly List<GameObject> _laneTargetOverlays = new();
        private readonly Dictionary<FormationLine, GameObject> _overlayByLane = new();
        private FormationLine? _hoveredLaneOverlay;

        private bool _laneTargetsActive;
        private bool _laneTargetsPlayerSide = true;

        public void ShowPlayerLaneTargets()
        {
            HidePlayerLaneTargets();
            _laneTargetsActive = true;
            _laneTargetsPlayerSide = true;
            Debug.Log("[BattlefieldView] ShowPlayerLaneTargets: drag mode active (unit highlight)");
        }

        public void ShowEnemyLaneTargets()
        {
            HidePlayerLaneTargets();
            _laneTargetsActive = true;
            _laneTargetsPlayerSide = false;
            Debug.Log("[BattlefieldView] ShowEnemyLaneTargets: drag mode active (unit highlight)");
        }

        public void HighlightHoveredLane(FormationLine? lane)
        {
            if (!_laneTargetsActive) return;
            if (_hoveredLaneOverlay == lane) return;
            _hoveredLaneOverlay = lane;

            List<BattleUnit> targetUnits = _laneTargetsPlayerSide ? _playerBattleUnits : _enemyBattleUnits;
            foreach (BattleUnit u in targetUnits)
            {
                if (u == null || u.IsDead || u.Group == null || u.View == null) continue;
                bool isInLane = lane.HasValue && u.Group.Line == lane.Value;
                u.View.SetHighlight(isInLane);
            }

            List<BattleUnit> nonTargetUnits = _laneTargetsPlayerSide ? _enemyBattleUnits : _playerBattleUnits;
            foreach (BattleUnit u in nonTargetUnits)
            {
                if (u == null || u.View == null) continue;
                u.View.SetHighlight(false);
            }

            // Preview units (pending summons) participate in highlight too.
            foreach (BattleUnit u in _previewUnits)
            {
                if (u == null || u.Group == null || u.View == null) continue;
                bool isInLane = _laneTargetsPlayerSide && lane.HasValue && u.Group.Line == lane.Value;
                u.View.SetHighlight(isInLane);
            }
        }

        public FormationLine? FindMostPopulatedPlayerLane()
        {
            Dictionary<FormationLine, int> counts = new();
            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null) continue;
                counts.TryGetValue(u.Group.Line, out int c);
                counts[u.Group.Line] = c + 1;
            }
            FormationLine? best = null;
            int bestCount = 0;
            foreach (KeyValuePair<FormationLine, int> kv in counts)
            {
                if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }
            }
            return best;
        }

        private static Color ColorForLane(FormationLine line, float alpha)
        {
            return line switch
            {
                FormationLine.Back => new Color(0.95f, 0.35f, 0.35f, alpha),    // red
                FormationLine.Middle => new Color(0.35f, 0.85f, 0.45f, alpha),  // green
                FormationLine.Front => new Color(0.30f, 0.65f, 1f, alpha),      // blue
                _ => new Color(1f, 1f, 1f, alpha),
            };
        }

        public void HidePlayerLaneTargets()
        {
            foreach (GameObject go in _laneTargetOverlays)
            {
                if (go != null) Destroy(go);
            }
            _laneTargetOverlays.Clear();
            _overlayByLane.Clear();
            _hoveredLaneOverlay = null;
            _laneTargetsActive = false;

            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.View == null) continue;
                u.View.SetHighlight(false);
            }
            foreach (BattleUnit u in _enemyBattleUnits)
            {
                if (u == null || u.View == null) continue;
                u.View.SetHighlight(false);
            }
            foreach (BattleUnit u in _previewUnits)
            {
                if (u == null || u.View == null) continue;
                u.View.SetHighlight(false);
            }
        }

        public Vector2 ClampToBounds(Vector2 position)
        {
            return new Vector2(
                Mathf.Clamp(position.x, _battleBoundsMin.x, _battleBoundsMax.x),
                Mathf.Clamp(position.y, _battleBoundsMin.y, _battleBoundsMax.y));
        }

        public void DuplicatePlayerUnits(PendingUnitBuild pendingBuild, FormationLine? targetLane = null,
            int maxCopies = int.MaxValue, bool temporaryBattleOnly = false)
        {
            // Duplicate requires a lane. Fall back to the pending unit's lane if the caller
            // didn't supply one (e.g. card was clicked, not dragged).
            FormationLine? effectiveLane = targetLane;
            if (!effectiveLane.HasValue && pendingBuild != null)
            {
                effectiveLane = pendingBuild.line;
            }
            if (!effectiveLane.HasValue)
            {
                effectiveLane = FindMostPopulatedPlayerLane();
            }
            Debug.Log($"[BattlefieldView] DuplicatePlayerUnits: targetLane={targetLane?.ToString() ?? "null"} pendingLine={pendingBuild?.line.ToString() ?? "null"} effective={effectiveLane?.ToString() ?? "null"}");
            if (!effectiveLane.HasValue)
            {
                Debug.Log("[BattlefieldView] DuplicatePlayerUnits: no lane → no-op");
                return;
            }

            FormationLine lane = effectiveLane.Value;
            Debug.Log($"[BattlefieldView] Duplicate cloning lane={lane} from {_playerBattleUnits.Count} player units");

            int copiesMade = 0;
            int copyLimit = Mathf.Max(0, maxCopies);
            BattleUnit[] realSnapshot = _playerBattleUnits.ToArray();
            foreach (BattleUnit src in realSnapshot)
            {
                if (copiesMade >= copyLimit) break;
                if (src == null || src.IsDead || src.Group == null) continue;
                if (src.Group.Line != lane)
                {
                    Debug.Log($"  skip {src.Group.DisplayName} (line={src.Group.Line})");
                    continue;
                }
                Debug.Log($"  CLONE {src.Group.DisplayName} (line={src.Group.Line})");
                UnitGroup clone = CloneUnitGroup(src.Group, temporaryBattleOnly);
                _battlefieldManager?.AddSilent(clone);
                SpawnCloneView(clone);
                copiesMade++;
            }

            bool pendingMatches = pendingBuild != null && pendingBuild.line == lane;

            if (pendingMatches)
            {
                if (temporaryBattleOnly)
                {
                    int pendingCopies = Mathf.Max(1, pendingBuild.count);
                    bool oldTemporary = pendingBuild.temporaryBattleOnly;
                    pendingBuild.temporaryBattleOnly = true;
                    for (int i = 0; i < pendingCopies; i++)
                    {
                        if (copiesMade >= copyLimit) break;
                        UnitGroup clone = new(pendingBuild, isPlayerUnit: true);
                        _battlefieldManager?.AddSilent(clone);
                        SpawnCloneView(clone);
                        copiesMade++;
                    }
                    pendingBuild.temporaryBattleOnly = oldTemporary;
                }
                else if (_previewUnits.Count > 0)
                {
                    BattleUnit[] previewSnapshot = _previewUnits.ToArray();
                    foreach (BattleUnit src in previewSnapshot)
                    {
                        if (copiesMade >= copyLimit) break;
                        if (src == null || src.Group == null) continue;
                        FormationLine line = src.Group.Line;
                        Vector2 pos = FindGroupSpawnPosition(forPlayer: true, line);
                        BattleUnit pu = SpawnUnitView(forPlayer: true, line: line, position: pos);
                        if (pu == null) continue;
                        bool oldTemporary = pendingBuild.temporaryBattleOnly;
                        pendingBuild.temporaryBattleOnly = pendingBuild.temporaryBattleOnly || temporaryBattleOnly;
                        UnitGroup previewGroup = new(pendingBuild, isPlayerUnit: true);
                        pendingBuild.temporaryBattleOnly = oldTemporary;
                        pu.Init(previewGroup, this);
                        pu.SetActive(false);
                        UnitGroupView view = pu.View;
                        if (view != null) view.BindPreview(pendingBuild, isPlayerSide: true);
                        _previewUnits.Add(pu);
                        copiesMade++;
                    }
                    pendingBuild.count = _previewUnits.Count;
                    AssignPackedTargets(_previewUnits, forPlayer: true, pendingBuild.line);
                }
            }

            Vector2 fxPos = LaneAnchoredPosition(FindLine(true, lane));
            SpawnEffect.Play(_battleFieldRoot, fxPos, "x2", _smokeSprite, _spellTextColor);
        }

        // Emergency Draft: spawns the chosen unit card's full group onto the field as TEMPORARY
        // reinforcements. They fight this wave like real units but carry TemporaryBattleOnly, so
        // ClearTemporaryPlayerUnits removes them when the wave ends (they never join the army).
        // Spawned directly onto the field (not as a pending preview) so they're available the
        // instant the spell resolves, with no FIGHT-time summon step.
        public int SummonTemporaryUnit(CardData unitCard)
        {
            if (unitCard == null) return 0;

            PendingUnitBuild build = new(unitCard) { temporaryBattleOnly = true };
            int count = Mathf.Max(1, build.count);
            Vector2 fxPos = Vector2.zero;
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                UnitGroup unit = new(build, isPlayerUnit: true);
                _battlefieldManager?.AddSilent(unit);
                SpawnCloneView(unit);
                if (_byGroup.TryGetValue(unit, out BattleUnit bu) && bu != null)
                {
                    fxPos += bu.Rect.anchoredPosition;
                    spawned++;
                }
            }

            if (spawned > 0) fxPos /= spawned;
            else fxPos = LineCenter(isPlayer: true, build.line, build);
            SpawnEffect.Play(_battleFieldRoot, fxPos, $"+{count}", _smokeSprite, _summonTextColor);
            return spawned;
        }

        public void StrengthenPlayerUnits(float percent, PendingUnitBuild pendingBuild)
        {
            float multiplier = 1f + percent;
            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null) continue;
                u.Group.ApplyAttackMultiplier(multiplier);
            }

            // Also strengthen any pending unit so summoned units inherit the boost.
            if (pendingBuild != null)
            {
                pendingBuild.attack = Mathf.Max(0, Mathf.RoundToInt(pendingBuild.attack * multiplier));
            }

            SpawnEffect.Play(_battleFieldRoot, ComputePlayerCenter(pendingBuild), "+50% ATK", _smokeSprite, _spellTextColor);
        }

        // Fortify: grants front-line player units full damage immunity for the first
        // `seconds` of the coming battle. The window only starts counting once combat
        // begins (see UnitGroup.ActivateShield), so casting it during planning is fine.
        public void ShieldFrontLineUnits(float seconds, PendingUnitBuild pendingBuild)
        {
            if (seconds <= 0f) return;

            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null) continue;
                if (u.Group.Line != FormationLine.Front) continue;
                u.Group.ApplyShield(seconds);
            }

            // Carry the shield onto any pending front-line summon and its previews so a
            // unit summoned this turn marches out already fortified.
            bool pendingIsFront = pendingBuild != null && pendingBuild.line == FormationLine.Front;
            if (pendingIsFront)
            {
                pendingBuild.shieldDuration = Mathf.Max(pendingBuild.shieldDuration, seconds);
                foreach (BattleUnit u in _previewUnits)
                {
                    if (u == null || u.Group == null) continue;
                    if (u.Group.Line != FormationLine.Front) continue;
                    u.Group.ApplyShield(seconds);
                }
            }

            Vector2 fxPos = pendingIsFront
                ? ComputePlayerCenter(pendingBuild)
                : FrontLineCenter(pendingBuild);
            SpawnEffect.Play(_battleFieldRoot, fxPos, "SHIELD", _smokeSprite, _spellTextColor);
        }

        // Rally: grants every player unit a move/attack-speed bonus for the first
        // `seconds` of the coming battle. Like Fortify, the window only starts counting
        // once combat begins (see UnitGroup.ActivateRally), so casting it during planning
        // is fine, and it's a one-battle effect that clears itself when the window ends.
        public void RallyPlayerUnits(float bonus, float seconds, PendingUnitBuild pendingBuild)
        {
            if (bonus <= 0f || seconds <= 0f) return;

            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null) continue;
                u.Group.ApplyRally(bonus, seconds);
            }

            // Carry the rally onto any pending summon and its previews so a unit summoned
            // this turn marches out already rallied.
            if (pendingBuild != null)
            {
                pendingBuild.rallyBonus = Mathf.Max(pendingBuild.rallyBonus, bonus);
                pendingBuild.rallyDuration = Mathf.Max(pendingBuild.rallyDuration, seconds);
            }
            foreach (BattleUnit u in _previewUnits)
            {
                if (u == null || u.Group == null) continue;
                u.Group.ApplyRally(bonus, seconds);
            }

            string label = $"+{Mathf.RoundToInt(bonus * 100f)}% SPD";
            SpawnEffect.Play(_battleFieldRoot, ComputePlayerCenter(pendingBuild), label, _smokeSprite, _spellTextColor);
        }

        // Revive: arms a budget so the first `count` player units to fall in the coming
        // battle spring back at `hpFraction` of their max HP and rejoin the fight (see
        // NotifyDeath). It's a one-turn effect — the budget is wiped by ResetTurnEffects at
        // the start of each turn, so unspent revives don't carry over. Casting twice in a
        // turn keeps the larger budget and the more generous HP fraction.
        public void RevivePlayerUnits(int count, float hpFraction)
        {
            if (count <= 0 || hpFraction <= 0f) return;

            _reviveBudget = Mathf.Max(_reviveBudget, count);
            _reviveHpFraction = Mathf.Max(_reviveHpFraction, Mathf.Clamp01(hpFraction));

            int pct = Mathf.RoundToInt(_reviveHpFraction * 100f);
            SpawnEffect.Play(_battleFieldRoot, ComputePlayerCenter(null), $"REVIVE x{count}", _smokeSprite, _spellTextColor);
            Debug.Log($"[BattlefieldView] Revive armed: budget={_reviveBudget} hp={pct}%");
        }

        // Unit Upgrade: applies an upgrade step to every player unit of a family already on
        // the field, plus the pending summon and its previews so a unit built this turn evolves
        // too. A stat-only step scales the units' stats; an evolution step re-skins them to the
        // evolved card (new art, name, and stats) and rebinds their views. Returns the number of
        // distinct on-field units affected (for the FX label).
        public int UpgradeOnFieldUnits(string familyRootId, UpgradeManager.UpgradeStep step, PendingUnitBuild pendingBuild)
        {
            if (string.IsNullOrEmpty(familyRootId) || !step.Valid) return 0;

            // Build a template from the evolved card once, so every matching unit re-skins to
            // the same form. Null for stat-only upgrades.
            PendingUnitBuild evolvedTemplate = step.IsEvolution && step.EvolvedCard != null
                ? new PendingUnitBuild(step.EvolvedCard)
                : null;

            int affected = 0;
            Vector2 fxPos = Vector2.zero;
            List<BattleUnit> vfxUnits = new();

            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null) continue;
                if (u.Group.FamilyId != familyRootId) continue;
                ApplyUpgradeToUnit(u, step, evolvedTemplate);
                fxPos += u.Rect.anchoredPosition;
                vfxUnits.Add(u);
                affected++;
            }

            // Preview units (the pending summon's translucent copies) upgrade in place too.
            foreach (BattleUnit u in _previewUnits)
            {
                if (u == null || u.Group == null) continue;
                if (u.Group.FamilyId != familyRootId) continue;
                ApplyUpgradeToUnit(u, step, evolvedTemplate);
                vfxUnits.Add(u);
            }

            // Carry the upgrade onto the pending build so units not yet summoned spawn evolved.
            if (pendingBuild != null && pendingBuild.familyId == familyRootId)
            {
                if (step.IsEvolution && evolvedTemplate != null)
                {
                    ApplyEvolvedBuild(pendingBuild, evolvedTemplate);
                }
                else
                {
                    ApplyStatMultiplierToBuild(pendingBuild, step.IncrementalMultiplier);
                }
            }

            if (affected > 0) fxPos /= affected;
            string label = step.IsEvolution && step.EvolvedCard != null
                ? step.EvolvedCard.cardName?.ToUpperInvariant()
                : $"+{Mathf.RoundToInt((step.IncrementalMultiplier - 1f) * 100f)}%";
            PlayLevelUpFeedback(vfxUnits);
            SpawnEffect.Play(_battleFieldRoot, fxPos + new Vector2(0f, 42f), label, _smokeSprite, _spellTextColor);
            return affected;
        }

        private void PlayLevelUpFeedback(List<BattleUnit> units)
        {
            if (_battleFieldRoot == null || units == null || units.Count == 0) return;

            List<Vector2> positions = new(units.Count);
            foreach (BattleUnit u in units)
            {
                if (u == null || u.Rect == null) continue;
                positions.Add(u.Rect.anchoredPosition);
                u.View?.PlayLevelUpPulse();
            }
            if (positions.Count == 0) return;

            BattlefieldLevelUpVfx.Play(_battleFieldRoot, positions, _levelUpArrowSprite);
        }

        private void ApplyUpgradeToUnit(BattleUnit unit, UpgradeManager.UpgradeStep step, PendingUnitBuild evolvedTemplate)
        {
            if (step.IsEvolution && evolvedTemplate != null)
            {
                unit.Group.ReskinTo(evolvedTemplate);
                // Rebuild the unit's sprites from its now-evolved art.
                unit.View?.BindReal(unit.Group);
            }
            else
            {
                unit.Group.ApplyStatMultiplier(step.IncrementalMultiplier);
            }
        }

        private static void ApplyStatMultiplierToBuild(PendingUnitBuild build, float multiplier)
        {
            if (multiplier <= 0f || Mathf.Approximately(multiplier, 1f)) return;
            build.attack = Mathf.Max(0, Mathf.RoundToInt(build.attack * multiplier));
            build.hp = Mathf.Max(1, Mathf.RoundToInt(build.hp * multiplier));
            build.moveSpeed *= multiplier;
            build.attackRange *= multiplier;
            build.attackSpeed *= multiplier;
            if (build.projectileSpeed > 0f) build.projectileSpeed *= multiplier;
        }

        // Copies the evolved card's identity/stats/art onto a pending build, so the pending
        // summon spawns as the evolved form. Keeps the pending build's lane.
        private static void ApplyEvolvedBuild(PendingUnitBuild build, PendingUnitBuild evolved)
        {
            FormationLine keepLine = build.line;
            build.attack = evolved.attack;
            build.hp = evolved.hp;
            build.line = keepLine;
            build.artwork = evolved.artwork;
            build.idleSprite = evolved.idleSprite;
            build.attackFrames = evolved.attackFrames;
            build.projectileSprite = evolved.projectileSprite;
            build.displayName = evolved.displayName;
            build.familyId = evolved.familyId;
            build.moveSpeed = evolved.moveSpeed;
            build.attackRange = evolved.attackRange;
            build.attackCooldown = evolved.attackCooldown;
            build.attackSpeed = evolved.attackSpeed;
            build.projectileSpeed = evolved.projectileSpeed;
            build.projectileAoeRadius = evolved.projectileAoeRadius;
            build.unitType = evolved.unitType;
            build.shadowScale = evolved.shadowScale;
        }

        // Lightning Strike: arms a battle-start spell. The target is chosen when combat
        // begins, not when the card is played, so pending enemy/player state settles first.
        public void LightningStrikePriorityEnemy(int damage, float battleStartDelay, Sprite projectileSprite)
        {
            if (damage <= 0 || _battleFieldRoot == null) return;

            _pendingLightningStrikes.Add(new PendingLightningStrike(damage, Mathf.Max(0f, battleStartDelay), projectileSprite));
            SpawnEffect.Play(_battleFieldRoot, ComputeEnemyCenter(), "STORM READY", _smokeSprite, _lightningTextColor);
        }

        public void MarkEnemyLine(FormationLine? targetLane, float bonusDamageTaken)
        {
            if (!targetLane.HasValue || bonusDamageTaken <= 0f) return;

            FormationLine lane = targetLane.Value;
            List<Vector2> markedPositions = new();
            foreach (BattleUnit u in _enemyBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null || u.Group.Line != lane) continue;
                u.Group.ApplyMark(bonusDamageTaken);
                if (u.Rect != null) markedPositions.Add(u.Rect.anchoredPosition);
            }

            Vector2 fxCenter = LineCenter(isPlayer: false, lane, null);
            BattlefieldMarkVfx.Play(_battleFieldRoot, markedPositions, fxCenter, _markTextColor);
            SpawnEffect.Play(_battleFieldRoot, fxCenter,
                "MARK", _smokeSprite, _markTextColor);
        }

        public void ShieldPlayerLine(FormationLine? targetLane, float seconds, PendingUnitBuild pendingBuild,
            bool playBarrierDome = false)
        {
            if (!targetLane.HasValue || seconds <= 0f) return;

            FormationLine lane = targetLane.Value;
            Vector2 fxCenter = LineCenter(isPlayer: true, lane, pendingBuild);
            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null || u.Group.Line != lane) continue;
                u.Group.ApplyShield(seconds);
            }

            if (pendingBuild != null && pendingBuild.line == lane)
            {
                pendingBuild.shieldDuration = Mathf.Max(pendingBuild.shieldDuration, seconds);
                foreach (BattleUnit u in _previewUnits)
                {
                    if (u == null || u.Group == null || u.Group.Line != lane) continue;
                    u.Group.ApplyShield(seconds);
                }
            }

            if (playBarrierDome)
            {
                BattlefieldBarrierVfx.Play(_battleFieldRoot, PlayerLinePositions(lane), fxCenter, _spellTextColor);
            }

            SpawnEffect.Play(_battleFieldRoot, fxCenter, "SHIELD", _smokeSprite, _spellTextColor);
        }

        public void RallyPlayerLine(FormationLine? targetLane, float bonus, float seconds, PendingUnitBuild pendingBuild)
        {
            if (!targetLane.HasValue || bonus <= 0f || seconds <= 0f) return;

            FormationLine lane = targetLane.Value;
            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null || u.Group.Line != lane) continue;
                u.Group.ApplyRally(bonus, seconds);
            }

            if (pendingBuild != null && pendingBuild.line == lane)
            {
                pendingBuild.rallyBonus = Mathf.Max(pendingBuild.rallyBonus, bonus);
                pendingBuild.rallyDuration = Mathf.Max(pendingBuild.rallyDuration, seconds);
                foreach (BattleUnit u in _previewUnits)
                {
                    if (u == null || u.Group == null || u.Group.Line != lane) continue;
                    u.Group.ApplyRally(bonus, seconds);
                }
            }

            SpawnEffect.Play(_battleFieldRoot, LineCenter(isPlayer: true, lane, pendingBuild),
                $"+{Mathf.RoundToInt(bonus * 100f)}% SPD", _smokeSprite, _spellTextColor);
        }

        public void DamageEnemyLine(FormationLine? targetLane, float damageValue, float fixedLineValue, float markedBonusFraction)
        {
            int damage = Mathf.RoundToInt(damageValue);
            if (damage <= 0) return;

            FormationLine lane;
            if (fixedLineValue >= 0f)
            {
                lane = (FormationLine)Mathf.RoundToInt(fixedLineValue);
            }
            else if (targetLane.HasValue)
            {
                lane = targetLane.Value;
                // Compatibility for existing generated Meteor assets that still serialize as
                // DamageEnemyLine with a selected lane. Fixed-line DamageEnemyLine stays instant
                // for Fireball-style effects.
                MeteorEnemyLine(lane, damage, markedBonusFraction);
                return;
            }
            else
            {
                return;
            }

            ApplyDamageToEnemyLine(lane, damage, markedBonusFraction);

            SpawnEffect.Play(_battleFieldRoot, LineCenter(isPlayer: false, lane, null),
                $"{damage}", _smokeSprite, _lightningTextColor);
        }

        public void MeteorEnemyLine(FormationLine? targetLane, float damageValue, float markedBonusFraction)
        {
            int damage = Mathf.RoundToInt(damageValue);
            if (!targetLane.HasValue || damage <= 0 || _battleFieldRoot == null) return;

            FormationLine lane = targetLane.Value;
            _pendingMeteorStrikes.Add(new PendingMeteorStrike(lane, damage, markedBonusFraction, _meteorBattleStartDelay));
            SpawnEffect.Play(_battleFieldRoot, LineCenter(isPlayer: false, lane, null),
                "METEOR READY", _smokeSprite, new Color(1f, 0.48f, 0.12f));
        }

        public void SlowEnemyOpeningLines(float slowPercent, float seconds)
        {
            if (slowPercent <= 0f || seconds <= 0f) return;

            foreach (BattleUnit u in _enemyBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null) continue;
                if (u.Group.Line != FormationLine.Front && u.Group.Line != FormationLine.Middle) continue;
                u.Group.ApplySlow(slowPercent, seconds);
            }
        }

        public void ReduceFrontLineDamage(float reduction, PendingUnitBuild pendingBuild)
        {
            if (reduction <= 0f) return;

            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null || u.Group.Line != FormationLine.Front) continue;
                u.Group.ApplyDamageReduction(reduction);
            }

            if (pendingBuild != null && pendingBuild.line == FormationLine.Front)
            {
                pendingBuild.damageReduction = Mathf.Max(pendingBuild.damageReduction, reduction);
                foreach (BattleUnit u in _previewUnits)
                {
                    if (u == null || u.Group == null || u.Group.Line != FormationLine.Front) continue;
                    u.Group.ApplyDamageReduction(reduction);
                }
            }

            SpawnEffect.Play(_battleFieldRoot, FrontLineCenter(pendingBuild),
                $"-{Mathf.RoundToInt(reduction * 100f)}% DMG", _smokeSprite, _spellTextColor);
        }

        public void LightningStrikePriorityEnemies(int damage, int count, Sprite projectileSprite)
        {
            if (damage <= 0) return;

            int strikes = Mathf.Max(1, count);
            for (int i = 0; i < strikes; i++)
            {
                _pendingLightningStrikes.Add(new PendingLightningStrike(damage, 0.2f + i * 0.15f, projectileSprite));
            }
            SpawnEffect.Play(_battleFieldRoot, ComputeEnemyCenter(), $"STORM x{strikes}", _smokeSprite, _lightningTextColor);
        }

        private void TriggerBattleStartSpells(int token)
        {
            if (_pendingLightningStrikes.Count > 0)
            {
                PendingLightningStrike[] strikes = _pendingLightningStrikes.ToArray();
                _pendingLightningStrikes.Clear();
                foreach (PendingLightningStrike strike in strikes)
                {
                    StartCoroutine(CastLightningStrikeAfterDelay(strike, token));
                }
            }

            if (_pendingMeteorStrikes.Count > 0)
            {
                PendingMeteorStrike[] meteors = _pendingMeteorStrikes.ToArray();
                _pendingMeteorStrikes.Clear();
                foreach (PendingMeteorStrike strike in meteors)
                {
                    StartCoroutine(CastMeteorAfterDelay(strike, token));
                }
            }
        }

        private IEnumerator CastLightningStrikeAfterDelay(PendingLightningStrike strike, int token)
        {
            if (strike.BattleStartDelay > 0f)
            {
                yield return new WaitForSeconds(strike.BattleStartDelay);
            }
            if (token != _battleStartSpellToken) yield break;

            CastLightningStrike(strike.Damage, strike.ProjectileSprite);
        }

        private IEnumerator CastMeteorAfterDelay(PendingMeteorStrike strike, int token)
        {
            if (strike.BattleStartDelay > 0f)
            {
                yield return new WaitForSeconds(strike.BattleStartDelay);
            }
            if (token != _battleStartSpellToken) yield break;

            yield return MeteorStrikeRoutine(strike, token);
        }

        private void CastLightningStrike(int damage, Sprite projectileSprite)
        {
            BattleUnit target = FindLightningStrikeTarget();
            if (target == null)
            {
                SpawnEffect.Play(_battleFieldRoot, ComputeEnemyCenter(), "NO TARGET", _smokeSprite, _lightningTextColor);
                return;
            }

            StartCoroutine(LightningStrikeRoutine(target, damage, projectileSprite));
        }

        private void TriggerSlowFieldVfx()
        {
            List<Vector2> affectedPositions = new();
            float duration = 0f;
            float slowPercent = 0f;

            foreach (BattleUnit u in _enemyBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null || !u.Group.IsSlowed) continue;
                if (u.Rect != null) affectedPositions.Add(SlowFieldVfxPosition(u));
                duration = Mathf.Max(duration, u.Group.SlowDuration);
                slowPercent = Mathf.Max(slowPercent, u.Group.SlowPercent);
            }

            if (affectedPositions.Count == 0 || duration <= 0f) return;

            List<Vector2> areaPositions = EnemyOpeningLineAreaPositions(affectedPositions);
            Vector2 fxCenter = OpeningEnemyLinesCenter(areaPositions);
            BattlefieldSlowFieldVfx.Play(_battleFieldRoot, areaPositions, fxCenter, duration);
            SpawnEffect.Play(_battleFieldRoot, fxCenter,
                $"SLOW {Mathf.RoundToInt(slowPercent * 100f)}%", _smokeSprite, _lightningTextColor);
        }

        private static Vector2 SlowFieldVfxPosition(BattleUnit unit)
        {
            float spriteHeight = unit.View != null ? unit.View.SpriteRenderedHeight : 0f;
            float yOffset = Mathf.Max(46f, spriteHeight * 0.34f);
            return unit.Rect.anchoredPosition + new Vector2(0f, yOffset);
        }

        private List<Vector2> EnemyOpeningLineAreaPositions(IReadOnlyList<Vector2> fallbackPositions)
        {
            List<Vector2> positions = new();
            AddEnemyLineAreaPositions(positions, FormationLine.Front);
            AddEnemyLineAreaPositions(positions, FormationLine.Middle);

            if (positions.Count > 0) return positions;
            return fallbackPositions != null ? new List<Vector2>(fallbackPositions) : positions;
        }

        private void AddEnemyLineAreaPositions(List<Vector2> positions, FormationLine line)
        {
            FormationLineView lineView = FindLine(isPlayer: false, line);
            if (lineView == null) return;

            Vector2 center = LaneAnchoredPosition(lineView) + new Vector2(0f, 58f);
            float halfWidth = Mathf.Max(_laneHalfWidth, 125f);
            float halfHeight = Mathf.Max(_laneHalfHeight * 0.68f, 125f);

            positions.Add(center + new Vector2(-halfWidth, -halfHeight));
            positions.Add(center + new Vector2(-halfWidth, halfHeight));
            positions.Add(center + new Vector2(halfWidth, -halfHeight));
            positions.Add(center + new Vector2(halfWidth, halfHeight));
        }

        // Clears one-turn battlefield effects. Called at the start of each draft turn so a
        // spell's effect never bleeds into a later round, even if combat ended before a
        // timed effect naturally counted down.
        public void ResetTurnEffects()
        {
            _battleStartSpellToken++;
            _reviveBudget = 0;
            _reviveHpFraction = 0f;
            _pendingLightningStrikes.Clear();
            _pendingMeteorStrikes.Clear();
            ClearBattlefieldSpellEffects(_playerBattleUnits);
            ClearBattlefieldSpellEffects(_enemyBattleUnits);
            ClearBattlefieldSpellEffects(_deadUnits);
            ClearBattlefieldSpellEffects(_previewUnits);
        }

        private static void ClearBattlefieldSpellEffects(List<BattleUnit> units)
        {
            if (units == null) return;
            foreach (BattleUnit u in units)
            {
                if (u == null || u.Group == null) continue;
                u.ClearBattlefieldSpellEffects();
            }
        }

        private readonly struct PendingLightningStrike
        {
            public readonly int Damage;
            public readonly float BattleStartDelay;
            public readonly Sprite ProjectileSprite;

            public PendingLightningStrike(int damage, float battleStartDelay, Sprite projectileSprite)
            {
                Damage = damage;
                BattleStartDelay = battleStartDelay;
                ProjectileSprite = projectileSprite;
            }
        }

        private readonly struct PendingMeteorStrike
        {
            public readonly FormationLine Lane;
            public readonly int Damage;
            public readonly float MarkedBonusFraction;
            public readonly float BattleStartDelay;

            public PendingMeteorStrike(FormationLine lane, int damage, float markedBonusFraction, float battleStartDelay)
            {
                Lane = lane;
                Damage = damage;
                MarkedBonusFraction = markedBonusFraction;
                BattleStartDelay = Mathf.Max(0f, battleStartDelay);
            }
        }

        private readonly struct MeteorShard
        {
            public readonly GameObject Root;
            public readonly RectTransform Rect;
            public readonly Image Trail;
            public readonly Image Glow;
            public readonly Image Core;
            public readonly Vector2 Start;
            public readonly Vector2 Impact;
            public readonly float Delay;
            public readonly float Duration;
            public readonly float Scale;

            public MeteorShard(GameObject root, RectTransform rect, Image trail, Image glow, Image core,
                Vector2 start, Vector2 impact, float delay, float duration, float scale)
            {
                Root = root;
                Rect = rect;
                Trail = trail;
                Glow = glow;
                Core = core;
                Start = start;
                Impact = impact;
                Delay = delay;
                Duration = duration;
                Scale = scale;
            }
        }

        private struct MeteorParticle
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Start;
            public Vector2 End;
        }

        // Average position of living front-line player units, for placing the Fortify FX.
        // Falls back to the front lane anchor (or the pending build's lane) when none exist.
        private Vector2 FrontLineCenter(PendingUnitBuild fallbackBuild)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null) continue;
                if (u.Group.Line != FormationLine.Front) continue;
                sum += u.Rect.anchoredPosition;
                count++;
            }
            if (count > 0) return sum / count;

            FormationLineView lv = FindLine(isPlayer: true, FormationLine.Front);
            if (lv != null) return LaneAnchoredPosition(lv);

            if (fallbackBuild != null)
            {
                FormationLineView buildLane = FindLine(isPlayer: true, fallbackBuild.line);
                if (buildLane != null) return LaneAnchoredPosition(buildLane);
            }
            return Vector2.zero;
        }

        private Vector2 ComputePlayerCenter(PendingUnitBuild fallbackBuild)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead) continue;
                sum += u.Rect.anchoredPosition;
                count++;
            }
            foreach (BattleUnit u in _previewUnits)
            {
                if (u == null) continue;
                sum += u.Rect.anchoredPosition;
                count++;
            }
            if (count > 0) return sum / count;

            if (fallbackBuild != null)
            {
                FormationLineView lv = FindLine(isPlayer: true, fallbackBuild.line);
                if (lv != null) return LaneAnchoredPosition(lv);
            }
            return Vector2.zero;
        }

        private Vector2 LineCenter(bool isPlayer, FormationLine lane, PendingUnitBuild fallbackBuild)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            List<BattleUnit> source = isPlayer ? _playerBattleUnits : _enemyBattleUnits;
            foreach (BattleUnit u in source)
            {
                if (u == null || u.IsDead || u.Group == null || u.Group.Line != lane) continue;
                sum += u.Rect.anchoredPosition;
                count++;
            }
            if (isPlayer)
            {
                foreach (BattleUnit u in _previewUnits)
                {
                    if (u == null || u.Group == null || u.Group.Line != lane) continue;
                    sum += u.Rect.anchoredPosition;
                    count++;
                }
            }
            if (count > 0) return sum / count;

            FormationLineView lv = FindLine(isPlayer, lane);
            if (lv != null) return LaneAnchoredPosition(lv);

            if (isPlayer && fallbackBuild != null)
            {
                FormationLineView buildLane = FindLine(isPlayer: true, fallbackBuild.line);
                if (buildLane != null) return LaneAnchoredPosition(buildLane);
            }
            return Vector2.zero;
        }

        private Vector2 ComputeEnemyCenter()
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (BattleUnit u in _enemyBattleUnits)
            {
                if (u == null || u.IsDead) continue;
                sum += u.Rect.anchoredPosition;
                count++;
            }
            if (count > 0) return sum / count;
            return Vector2.zero;
        }

        private Vector2 OpeningEnemyLinesCenter(IReadOnlyList<Vector2> affectedPositions)
        {
            if (affectedPositions != null && affectedPositions.Count > 0)
            {
                return AveragePosition(affectedPositions, ComputeEnemyCenter());
            }

            Vector2 sum = Vector2.zero;
            int count = 0;
            FormationLineView front = FindLine(isPlayer: false, FormationLine.Front);
            if (front != null)
            {
                sum += LaneAnchoredPosition(front);
                count++;
            }
            FormationLineView middle = FindLine(isPlayer: false, FormationLine.Middle);
            if (middle != null)
            {
                sum += LaneAnchoredPosition(middle);
                count++;
            }

            if (count > 0) return sum / count;
            return ComputeEnemyCenter();
        }

        private BattleUnit FindLightningStrikeTarget()
        {
            BattleUnit best = null;
            float bestScore = float.NegativeInfinity;
            foreach (BattleUnit u in _enemyBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null) continue;
                float score = LightningTargetScore(u);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = u;
                }
            }
            return best;
        }

        private static float LightningTargetScore(BattleUnit unit)
        {
            UnitGroup group = unit.Group;
            float score = group.MaxHp * 0.25f + group.Attack * 8f;
            score += group.ProjectileSprite != null ? 900f : 0f;
            score += group.Line switch
            {
                FormationLine.Back => 2200f,
                FormationLine.Middle => 1400f,
                _ => 0f
            };

            string name = group.DisplayName ?? string.Empty;
            if (ContainsIgnoreCase(name, "Shaman")) score += 4500f;
            if (ContainsIgnoreCase(name, "Necromancer")) score += 4500f;
            if (ContainsIgnoreCase(name, "Totem")) score += 4000f;
            if (ContainsIgnoreCase(name, "Captain")) score += 3500f;
            if (ContainsIgnoreCase(name, "Cyclop")) score += 2500f;
            if (ContainsIgnoreCase(name, "Archer")) score += 1800f;

            return score;
        }

        private static bool ContainsIgnoreCase(string value, string needle)
        {
            return value.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int ApplyDamageToEnemyLine(FormationLine lane, int damage, float markedBonusFraction)
        {
            int hitCount = 0;
            BattleUnit[] snapshot = _enemyBattleUnits.ToArray();
            foreach (BattleUnit u in snapshot)
            {
                if (u == null || u.IsDead || u.Group == null || u.Group.Line != lane) continue;
                int finalDamage = damage;
                if (u.Group.IsMarked && markedBonusFraction > 0f)
                {
                    finalDamage = Mathf.RoundToInt(finalDamage * (1f + markedBonusFraction));
                }
                u.TakeDamage(finalDamage);
                hitCount++;
            }
            return hitCount;
        }

        private List<Vector2> EnemyLinePositions(FormationLine lane)
        {
            List<Vector2> positions = new();
            foreach (BattleUnit u in _enemyBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null || u.Group.Line != lane || u.Rect == null) continue;
                positions.Add(u.Rect.anchoredPosition);
            }
            return positions;
        }

        private List<Vector2> PlayerLinePositions(FormationLine lane)
        {
            List<Vector2> positions = new();
            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null || u.Group.Line != lane || u.Rect == null) continue;
                positions.Add(u.Rect.anchoredPosition);
            }
            foreach (BattleUnit u in _previewUnits)
            {
                if (u == null || u.Group == null || u.Group.Line != lane || u.Rect == null) continue;
                positions.Add(u.Rect.anchoredPosition);
            }
            return positions;
        }

        private Vector2 PredictMeteorImpact(FormationLine lane, Vector2 fallbackCenter, float secondsAhead)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (BattleUnit u in _enemyBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null || u.Group.Line != lane || u.Rect == null) continue;
                sum += u.Rect.anchoredPosition + u.CurrentVelocity * Mathf.Max(0f, secondsAhead);
                count++;
            }

            return count > 0 ? ClampToBounds(sum / count) : fallbackCenter;
        }

        private static Vector2 AveragePosition(IReadOnlyList<Vector2> positions, Vector2 fallback)
        {
            if (positions == null || positions.Count == 0) return fallback;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < positions.Count; i++) sum += positions[i];
            return sum / positions.Count;
        }

        private IEnumerator MeteorStrikeRoutine(PendingMeteorStrike strike, int token)
        {
            List<Vector2> targetPositions = EnemyLinePositions(strike.Lane);
            float travelDuration = Mathf.Max(0.12f, _meteorTravelDuration);
            Vector2 launchCenter = AveragePosition(targetPositions, LineCenter(isPlayer: false, strike.Lane, null));
            Vector2 impact = PredictMeteorImpact(strike.Lane, launchCenter, travelDuration);
            Vector2 start = impact + new Vector2(-520f, 390f);
            Vector2 direction = (impact - start).normalized;
            Vector2 side = new(-direction.y, direction.x);

            MeteorShard[] shards =
            {
                CreateMeteorShard("MeteorMain", start, impact, 0f, travelDuration, 1.15f, 100f, 235f, cardStyleMain: true),
                CreateMeteorShard("MeteorFollower_0", start - direction * 92f + side * 42f, impact + side * 38f, 0.11f, travelDuration * 0.84f, 0.45f, 42f, 118f),
                CreateMeteorShard("MeteorFollower_1", start - direction * 138f - side * 34f, impact - side * 32f, 0.18f, travelDuration * 0.8f, 0.38f, 36f, 105f),
                CreateMeteorShard("MeteorFollower_2", start - direction * 188f + side * 12f, impact + side * 12f + new Vector2(0f, 24f), 0.26f, travelDuration * 0.76f, 0.32f, 32f, 94f),
                CreateMeteorShard("MeteorFollower_3", start - direction * 226f + side * 50f, impact + side * 28f + new Vector2(0f, -20f), 0.31f, travelDuration * 0.72f, 0.28f, 28f, 82f),
                CreateMeteorShard("MeteorFollower_4", start - direction * 260f - side * 44f, impact - side * 26f + new Vector2(0f, 18f), 0.36f, travelDuration * 0.7f, 0.25f, 25f, 76f),
            };

            bool impacted = false;
            float elapsed = 0f;
            float totalDuration = travelDuration + 0.86f;
            while (elapsed < totalDuration)
            {
                if (token != _battleStartSpellToken)
                {
                    DestroyMeteorShards(shards);
                    yield break;
                }

                elapsed += Time.deltaTime;
                for (int i = 0; i < shards.Length; i++)
                {
                    UpdateMeteorShard(shards[i], elapsed);
                }

                if (!impacted && elapsed >= travelDuration)
                {
                    impacted = true;
                    targetPositions = EnemyLinePositions(strike.Lane);
                    ApplyDamageToEnemyLine(strike.Lane, strike.Damage, strike.MarkedBonusFraction);
                    SpawnEffect.Play(_battleFieldRoot, impact + new Vector2(0f, 34f),
                        $"METEOR {strike.Damage}", _smokeSprite, new Color(1f, 0.55f, 0.12f));
                    StartCoroutine(MeteorExplosionRoutine(impact, targetPositions, token));
                    StartCoroutine(MeteorBurnRoutine(impact, targetPositions, token));
                }

                yield return null;
            }

            DestroyMeteorShards(shards);
        }

        private MeteorShard CreateMeteorShard(string name, Vector2 start, Vector2 impact, float delay, float duration,
            float scale, float coreSize, float trailLength, bool cardStyleMain = false)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(_battleFieldRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = start;
            rect.sizeDelta = Vector2.zero;
            go.AddComponent<Projectile>();

            Vector2 tailDirection = (start - impact).normalized;
            float angle = Mathf.Atan2(tailDirection.y, tailDirection.x) * Mathf.Rad2Deg - 90f;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);

            Sprite trailSprite = cardStyleMain ? GetMeteorMainFlameSprite() : GetMeteorTrailSprite();
            Color trailColor = cardStyleMain ? Color.white : new Color(1f, 0.34f, 0.05f, 0.62f);
            Image trail = SpawnChildImage(rect, "Trail", trailSprite, new Vector2(0f, 10f),
                new Vector2(coreSize * (cardStyleMain ? 2.05f : 1.12f), trailLength), trailColor, new Vector2(0.5f, 0f));
            Image glow = SpawnChildImage(rect, "Glow", GetLightningBurstSprite(), Vector2.zero,
                Vector2.one * (coreSize * (cardStyleMain ? 1.85f : 1.55f)), new Color(1f, 0.48f, 0.08f, 0.72f), new Vector2(0.5f, 0.5f));
            Sprite coreSprite = cardStyleMain ? GetMeteorMainRockSprite() : GetMeteorCoreSprite();
            Color coreColor = cardStyleMain ? Color.white : new Color(0.38f, 0.18f, 0.09f, 1f);
            Image core = SpawnChildImage(rect, "Core", coreSprite, Vector2.zero,
                Vector2.one * coreSize, coreColor, new Vector2(0.5f, 0.5f));

            SetMeteorShardAlpha(new MeteorShard(go, rect, trail, glow, core, start, impact, delay, duration, scale), 0f);
            return new MeteorShard(go, rect, trail, glow, core, start, impact, delay, duration, scale);
        }

        private static void UpdateMeteorShard(MeteorShard shard, float elapsed)
        {
            if (shard.Rect == null) return;
            float raw = (elapsed - shard.Delay) / Mathf.Max(0.01f, shard.Duration);
            if (raw < 0f)
            {
                SetMeteorShardAlpha(shard, 0f);
                return;
            }

            float u = Mathf.Clamp01(raw);
            float eased = u * u;
            shard.Rect.anchoredPosition = Vector2.Lerp(shard.Start, shard.Impact, eased);
            float fade = raw <= 1f ? 1f : 1f - Smooth01(Mathf.InverseLerp(1f, 1.45f, raw));
            shard.Rect.localScale = Vector3.one * Mathf.Lerp(shard.Scale * 0.72f, shard.Scale * 1.08f, u);
            SetMeteorShardAlpha(shard, fade);
        }

        private static void SetMeteorShardAlpha(MeteorShard shard, float alpha)
        {
            SetImageAlpha(shard.Trail, alpha * 0.72f);
            SetImageAlpha(shard.Glow, alpha * 0.82f);
            SetImageAlpha(shard.Core, alpha);
        }

        private static void DestroyMeteorShards(IReadOnlyList<MeteorShard> shards)
        {
            if (shards == null) return;
            for (int i = 0; i < shards.Count; i++)
            {
                if (shards[i].Root != null) Destroy(shards[i].Root);
            }
        }

        private IEnumerator MeteorExplosionRoutine(Vector2 impact, IReadOnlyList<Vector2> targetPositions, int token)
        {
            GameObject go = new("MeteorExplosionVFX", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(_battleFieldRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = impact;
            rect.sizeDelta = Vector2.zero;
            go.AddComponent<Projectile>();

            Vector2 boundsSize = MeteorBoundsSize(targetPositions);
            float width = Mathf.Clamp(boundsSize.x + 110f, 150f, 220f);
            Image bloom = SpawnChildImage(rect, "FireBloom", GetLightningBurstSprite(), Vector2.zero,
                Vector2.one * width, new Color(1f, 0.45f, 0.04f, 0.95f), new Vector2(0.5f, 0.5f));
            Image core = SpawnChildImage(rect, "WhiteHotCore", GetLightningBurstSprite(), Vector2.zero,
                Vector2.one * (width * 0.46f), new Color(1f, 0.95f, 0.58f, 1f), new Vector2(0.5f, 0.5f));
            Image ring = SpawnChildImage(rect, "BlastRing", GetLightningRingSprite(), Vector2.zero,
                new Vector2(width * 0.72f, width * 0.42f), new Color(1f, 0.62f, 0.1f, 0.95f), new Vector2(0.5f, 0.5f));

            const int sparkCount = 18;
            MeteorParticle[] sparks = new MeteorParticle[sparkCount];
            for (int i = 0; i < sparkCount; i++)
            {
                float angle = (i / (float)sparkCount) * Mathf.PI * 2f + Noise01(i, width) * 0.5f;
                float distance = Mathf.Lerp(width * 0.14f, width * 0.38f, Noise01(i + 33, width));
                Image image = SpawnChildImage(rect, $"Spark_{i}", GetMeteorSparkSprite(), Vector2.zero,
                    Vector2.one * Mathf.Lerp(14f, 28f, Noise01(i + 67, width)),
                    new Color(1f, Mathf.Lerp(0.35f, 0.82f, Noise01(i + 91, width)), 0.08f, 0f),
                    new Vector2(0.5f, 0.5f));
                sparks[i] = new MeteorParticle
                {
                    Rect = (RectTransform)image.transform,
                    Image = image,
                    Start = Vector2.zero,
                    End = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.58f) * distance
                };
            }

            const float duration = 0.58f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (token != _battleStartSpellToken)
                {
                    if (go != null) Destroy(go);
                    yield break;
                }

                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float fade = 1f - Smooth01(Mathf.InverseLerp(0.28f, 1f, u));
                bloom.transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 1.45f, Smooth01(u));
                core.transform.localScale = Vector3.one * Mathf.Lerp(0.45f, 1.05f, u);
                ring.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 2.15f, Smooth01(u));
                SetImageAlpha(bloom, 0.95f * fade);
                SetImageAlpha(core, 1f - Smooth01(Mathf.InverseLerp(0.08f, 0.55f, u)));
                SetImageAlpha(ring, 0.95f * fade);

                for (int i = 0; i < sparks.Length; i++)
                {
                    MeteorParticle spark = sparks[i];
                    if (spark.Rect == null) continue;
                    spark.Rect.anchoredPosition = Vector2.Lerp(spark.Start, spark.End, Smooth01(u));
                    spark.Rect.localScale = Vector3.one * Mathf.Lerp(0.65f, 1.2f, u);
                    SetImageAlpha(spark.Image, Mathf.Sin(u * Mathf.PI) * 0.9f);
                }

                yield return null;
            }

            Destroy(go);
        }

        private IEnumerator MeteorBurnRoutine(Vector2 impact, IReadOnlyList<Vector2> targetPositions, int token)
        {
            GameObject go = new("MeteorBurnVFX", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(_battleFieldRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = impact + new Vector2(0f, -7f);
            rect.sizeDelta = Vector2.zero;
            go.AddComponent<Projectile>();

            Vector2 boundsSize = MeteorBoundsSize(targetPositions);
            Vector2 scorchSize = new(
                Mathf.Clamp(boundsSize.x + 90f, 140f, 210f),
                Mathf.Clamp(boundsSize.y * 0.35f + 64f, 70f, 115f));
            Image scorch = SpawnChildImage(rect, "Scorch", GetMeteorBurnSprite(), Vector2.zero, scorchSize,
                new Color(0.36f, 0.08f, 0.02f, 0.76f), new Vector2(0.5f, 0.5f));

            const int emberCount = 20;
            MeteorParticle[] embers = new MeteorParticle[emberCount];
            for (int i = 0; i < emberCount; i++)
            {
                float nx = Mathf.Lerp(-0.48f, 0.48f, Noise01(i, scorchSize.x));
                float ny = Mathf.Lerp(-0.24f, 0.2f, Noise01(i + 45, scorchSize.y));
                Vector2 start = new(nx * scorchSize.x, ny * scorchSize.y);
                Image image = SpawnChildImage(rect, $"Ember_{i}", GetMeteorSparkSprite(), start,
                    Vector2.one * Mathf.Lerp(8f, 18f, Noise01(i + 90, scorchSize.x)),
                    new Color(1f, Mathf.Lerp(0.28f, 0.68f, Noise01(i + 133, scorchSize.y)), 0.05f, 0f),
                    new Vector2(0.5f, 0.5f));
                embers[i] = new MeteorParticle
                {
                    Rect = (RectTransform)image.transform,
                    Image = image,
                    Start = start,
                    End = start + new Vector2(Mathf.Lerp(-20f, 20f, Noise01(i + 177, scorchSize.x)),
                        Mathf.Lerp(26f, 70f, Noise01(i + 211, scorchSize.y)))
                };
            }

            float duration = Mathf.Clamp(_meteorBurnDuration, 2f, 3f);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (token != _battleStartSpellToken)
                {
                    if (go != null) Destroy(go);
                    yield break;
                }

                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float scorchFade = 1f - Smooth01(Mathf.InverseLerp(0.72f, 1f, u));
                SetImageAlpha(scorch, 0.76f * scorchFade);

                for (int i = 0; i < embers.Length; i++)
                {
                    MeteorParticle ember = embers[i];
                    if (ember.Rect == null) continue;
                    float phase = Mathf.Repeat(u * 1.8f + i * 0.137f, 1f);
                    ember.Rect.anchoredPosition = Vector2.Lerp(ember.Start, ember.End, Smooth01(phase));
                    float flicker = 0.45f + 0.55f * Mathf.Sin((elapsed * 8.5f) + i);
                    SetImageAlpha(ember.Image, Mathf.Sin(phase * Mathf.PI) * flicker * scorchFade);
                }

                yield return null;
            }

            Destroy(go);
        }

        private static Vector2 MeteorBoundsSize(IReadOnlyList<Vector2> positions)
        {
            if (positions == null || positions.Count == 0) return new Vector2(160f, 90f);
            Vector2 min = positions[0];
            Vector2 max = positions[0];
            for (int i = 1; i < positions.Count; i++)
            {
                min = Vector2.Min(min, positions[i]);
                max = Vector2.Max(max, positions[i]);
            }
            return max - min;
        }

        private static Image SpawnChildImage(RectTransform parent, string name, Sprite sprite, Vector2 anchoredPosition,
            Vector2 size, Color color, Vector2 pivot)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform child = go.GetComponent<RectTransform>();
            child.SetParent(parent, false);
            child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
            child.pivot = pivot;
            child.anchoredPosition = anchoredPosition;
            child.sizeDelta = size;

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = false;
            return image;
        }

        private static void SetImageAlpha(Image image, float alpha)
        {
            if (image == null) return;
            Color c = image.color;
            c.a = Mathf.Clamp01(alpha);
            image.color = c;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Noise01(int index, float salt)
        {
            float v = Mathf.Sin(index * 12.9898f + salt * 0.073f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static Sprite _meteorCoreSprite;
        private static Sprite GetMeteorCoreSprite()
        {
            if (_meteorCoreSprite != null) return _meteorCoreSprite;

            const int res = 128;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[res * res];
            Vector2 center = new(res * 0.5f, res * 0.5f);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    Vector2 p = new(x + 0.5f, y + 0.5f);
                    Vector2 delta = p - center;
                    float angle = Mathf.Atan2(delta.y, delta.x);
                    float radiusNoise = 0.08f * Mathf.Sin(angle * 5f) + 0.05f * Mathf.Sin(angle * 9f + 1.7f);
                    float d = delta.magnitude / (res * (0.38f + radiusNoise));
                    float alpha = Mathf.SmoothStep(1f, 0f, d);
                    float hotEdge = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.78f) / 0.18f) * 0.45f;
                    pixels[y * res + x] = new Color(1f, Mathf.Lerp(0.72f, 1f, hotEdge), Mathf.Lerp(0.35f, 1f, hotEdge), Mathf.Clamp01(alpha + hotEdge));
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _meteorCoreSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _meteorCoreSprite;
        }

        private static Sprite _meteorMainRockSprite;
        private static Sprite GetMeteorMainRockSprite()
        {
            if (_meteorMainRockSprite != null) return _meteorMainRockSprite;

            const int res = 96;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[res * res];
            Vector2 center = new(res * 0.5f, res * 0.5f);
            Vector2[] craters =
            {
                new(res * 0.34f, res * 0.36f),
                new(res * 0.59f, res * 0.39f),
                new(res * 0.66f, res * 0.60f),
                new(res * 0.40f, res * 0.66f),
                new(res * 0.30f, res * 0.54f)
            };
            float[] craterRadius = { 12f, 8f, 13f, 16f, 9f };

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    Vector2 p = new(x + 0.5f, y + 0.5f);
                    Vector2 delta = p - center;
                    float angle = Mathf.Atan2(delta.y, delta.x);
                    float radius = res * (0.39f + 0.015f * Mathf.Sin(angle * 4f + 0.2f) + 0.01f * Mathf.Sin(angle * 8f));
                    float d = delta.magnitude / Mathf.Max(1f, radius);
                    if (d > 1.08f)
                    {
                        pixels[y * res + x] = Color.clear;
                        continue;
                    }

                    float light = Mathf.Clamp01(Vector2.Dot(delta.normalized, new Vector2(-0.45f, 0.88f)) * 0.5f + 0.5f);
                    Color color = Color.Lerp(new Color(0.20f, 0.08f, 0.035f, 1f), new Color(0.72f, 0.30f, 0.11f, 1f), light);
                    if (d > 0.88f)
                    {
                        color = Color.Lerp(new Color(0.055f, 0.035f, 0.025f, 1f), color, Mathf.InverseLerp(1.08f, 0.88f, d));
                    }

                    for (int i = 0; i < craters.Length; i++)
                    {
                        float cd = Vector2.Distance(p, craters[i]) / craterRadius[i];
                        if (cd >= 1f) continue;
                        float hollow = Mathf.SmoothStep(1f, 0f, cd);
                        float rim = Mathf.SmoothStep(1f, 0f, Mathf.Abs(cd - 0.74f) / 0.24f);
                        color = Color.Lerp(color, new Color(0.08f, 0.035f, 0.018f, 1f), hollow * 0.78f);
                        color = Color.Lerp(color, new Color(0.34f, 0.13f, 0.05f, 1f), rim * 0.42f);
                    }

                    color.a = d <= 1f ? 1f : Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(1f, 1.08f, d));
                    pixels[y * res + x] = color;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _meteorMainRockSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _meteorMainRockSprite;
        }

        private static Sprite _meteorMainFlameSprite;
        private static Sprite GetMeteorMainFlameSprite()
        {
            if (_meteorMainFlameSprite != null) return _meteorMainFlameSprite;

            const int width = 144;
            const int height = 320;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            float center = (width - 1) * 0.5f;

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float jag = Mathf.Sin(v * 22f) * 0.055f + Mathf.Sin(v * 43f + 1.2f) * 0.026f;
                float halfOuter = width * Mathf.Lerp(0.58f, 0.09f, v) * (1f + jag);
                float halfMiddle = halfOuter * 0.68f;
                float halfInner = halfOuter * 0.38f;
                float centerOffset = Mathf.Sin(v * 12f + 0.7f) * width * 0.052f;
                float lengthFade = 1f - Smooth01(Mathf.InverseLerp(0.82f, 1f, v));

                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Abs(x - center - centerOffset);
                    float outer = Mathf.SmoothStep(1f, 0f, dx / Mathf.Max(1f, halfOuter));
                    float middle = Mathf.SmoothStep(1f, 0f, dx / Mathf.Max(1f, halfMiddle));
                    float inner = Mathf.SmoothStep(1f, 0f, dx / Mathf.Max(1f, halfInner));
                    float alpha = outer * lengthFade;
                    Color color = Color.Lerp(new Color(0.92f, 0.08f, 0.015f, alpha), new Color(1f, 0.50f, 0.035f, alpha), middle);
                    color = Color.Lerp(color, new Color(1f, 0.94f, 0.18f, alpha), inner);
                    if (inner > 0.76f)
                    {
                        color = Color.Lerp(color, new Color(1f, 1f, 0.72f, alpha), (inner - 0.76f) / 0.24f);
                    }
                    color.a = alpha;
                    pixels[y * width + x] = color;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _meteorMainFlameSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 100f);
            return _meteorMainFlameSprite;
        }

        private static Sprite _meteorTrailSprite;
        private static Sprite GetMeteorTrailSprite()
        {
            if (_meteorTrailSprite != null) return _meteorTrailSprite;

            const int width = 96;
            const int height = 256;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            float center = (width - 1) * 0.5f;
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float halfWidth = Mathf.Lerp(width * 0.34f, width * 0.06f, v);
                float lengthFade = 1f - v;
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Abs(x - center);
                    float cross = Mathf.SmoothStep(1f, 0f, dx / Mathf.Max(1f, halfWidth));
                    float alpha = cross * lengthFade * lengthFade;
                    pixels[y * width + x] = new Color(1f, Mathf.Lerp(0.16f, 0.72f, v), 0.02f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _meteorTrailSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 100f);
            return _meteorTrailSprite;
        }

        private static Sprite _meteorBurnSprite;
        private static Sprite GetMeteorBurnSprite()
        {
            if (_meteorBurnSprite != null) return _meteorBurnSprite;

            const int width = 256;
            const int height = 128;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            Vector2 center = new(width * 0.5f, height * 0.5f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float nx = (x + 0.5f - center.x) / (width * 0.5f);
                    float ny = (y + 0.5f - center.y) / (height * 0.5f);
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float edge = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.62f) / 0.24f);
                    float core = Mathf.SmoothStep(1f, 0f, d / 0.55f) * 0.72f;
                    float crack = Mathf.Abs(Mathf.Sin((x * 0.12f) + Mathf.Sin(y * 0.09f) * 3f));
                    float alpha = d <= 1f ? Mathf.Clamp01(core + edge * 0.45f + (crack > 0.93f ? 0.25f : 0f)) : 0f;
                    pixels[y * width + x] = new Color(1f, 0.36f, 0.04f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _meteorBurnSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _meteorBurnSprite;
        }

        private static Sprite _meteorSparkSprite;
        private static Sprite GetMeteorSparkSprite()
        {
            if (_meteorSparkSprite != null) return _meteorSparkSprite;

            const int res = 32;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[res * res];
            Vector2 center = new(res * 0.5f, res * 0.5f);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (res * 0.5f);
                    float alpha = Mathf.SmoothStep(1f, 0f, d);
                    pixels[y * res + x] = new Color(1f, 0.7f, 0.08f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _meteorSparkSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _meteorSparkSprite;
        }

        private IEnumerator LightningStrikeRoutine(BattleUnit target, int damage, Sprite projectileSprite)
        {
            if (target == null || target.Rect == null) yield break;

            Vector2 impact = target.Rect.anchoredPosition;
            Vector2 start = new(
                Mathf.Clamp(impact.x, _battleBoundsMin.x, _battleBoundsMax.x),
                _battleBoundsMax.y + _lightningStartHeight);

            GameObject sourceGlow = SpawnLightningImage("LightningSourceGlow", start, GetLightningBurstSprite(),
                new Vector2(150f, 150f), new Color(0.35f, 0.85f, 1f, 0.75f));
            GameObject preGlow = SpawnLightningImage("LightningImpactCharge", impact, GetLightningBurstSprite(),
                new Vector2(120f, 120f), new Color(0.25f, 0.9f, 1f, 0.55f));
            yield return LightningChargeRoutine(sourceGlow, preGlow, 0.1f);

            if (target != null && !target.IsDead && target.Rect != null)
            {
                impact = target.Rect.anchoredPosition;
                start.x = Mathf.Clamp(impact.x, _battleBoundsMin.x, _battleBoundsMax.x);
                if (sourceGlow != null) ((RectTransform)sourceGlow.transform).anchoredPosition = start;
            }

            GameObject beam = SpawnLightningBeam(start, impact, projectileSprite);
            List<GameObject> branches = SpawnLightningBranches(start, impact);
            GameObject impactGlow = SpawnLightningImage("LightningImpactBloom", impact, GetLightningBurstSprite(),
                new Vector2(230f, 230f), new Color(0.35f, 0.9f, 1f, 0.95f));
            GameObject shockwave = SpawnLightningImage("LightningShockwave", impact, GetLightningRingSprite(),
                new Vector2(110f, 110f), new Color(0.55f, 0.95f, 1f, 0.95f));

            StartCoroutine(LightningImpactBurstRoutine(impact));

            if (target != null && !target.IsDead)
            {
                target.TakeDamage(damage);
            }

            SpawnEffect.Play(_battleFieldRoot, impact + new Vector2(0f, 24f), $"ZAP {damage}", _smokeSprite, _lightningTextColor);

            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, _lightningStrikeDuration);
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float flash = 1f - u;
                PulseLightningObject(beam, Mathf.Lerp(1.08f, 0.88f, u), flash);
                PulseLightningObject(sourceGlow, Mathf.Lerp(1.2f, 0.65f, u), flash * 0.8f);
                PulseLightningObject(impactGlow, Mathf.Lerp(0.9f, 1.7f, u), flash);
                PulseLightningObject(shockwave, Mathf.Lerp(0.45f, 2.6f, u), flash);

                for (int i = 0; i < branches.Count; i++)
                {
                    float jitter = 0.92f + 0.16f * Mathf.Sin((elapsed * 45f) + i * 1.7f);
                    PulseLightningObject(branches[i], jitter, flash * 0.75f);
                }
                yield return null;
            }

            DestroyLightningObject(beam);
            DestroyLightningObject(sourceGlow);
            DestroyLightningObject(preGlow);
            DestroyLightningObject(impactGlow);
            DestroyLightningObject(shockwave);
            foreach (GameObject branch in branches) DestroyLightningObject(branch);
        }

        private IEnumerator LightningChargeRoutine(GameObject sourceGlow, GameObject impactGlow, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                PulseLightningObject(sourceGlow, Mathf.Lerp(0.25f, 1.15f, u), Mathf.Lerp(0.15f, 0.8f, u));
                PulseLightningObject(impactGlow, Mathf.Lerp(0.35f, 1.0f, u), Mathf.Lerp(0.15f, 0.55f, u));
                yield return null;
            }
        }

        private GameObject SpawnLightningBeam(Vector2 start, Vector2 impact, Sprite projectileSprite)
        {
            Vector2 direction = start - impact;
            float length = Mathf.Max(1f, direction.magnitude);
            GameObject go = SpawnLightningImage("LightningBeam", impact, GetLightningBeamSprite(),
                new Vector2(_lightningBeamWidth, length), Color.white);
            RectTransform rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0.5f, 0f);
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);

            if (projectileSprite != null)
            {
                GameObject core = new("LightningBeamCore", typeof(RectTransform));
                RectTransform coreRect = (RectTransform)core.transform;
                coreRect.SetParent(rect, false);
                coreRect.anchorMin = coreRect.anchorMax = new Vector2(0.5f, 0f);
                coreRect.pivot = new Vector2(0.5f, 0.5f);
                coreRect.anchoredPosition = new Vector2(0f, length * 0.5f);
                coreRect.sizeDelta = new Vector2(_lightningBeamWidth * 1.15f, length * 0.75f);

                Image coreImage = core.AddComponent<Image>();
                coreImage.sprite = projectileSprite;
                coreImage.raycastTarget = false;
                coreImage.preserveAspect = false;
                coreImage.color = new Color(1f, 1f, 1f, 0.72f);
            }

            return go;
        }

        private List<GameObject> SpawnLightningBranches(Vector2 start, Vector2 impact)
        {
            List<GameObject> branches = new(7);
            Vector2 direction = start - impact;
            float length = Mathf.Max(1f, direction.magnitude);
            Vector2 right = new Vector2(direction.y, -direction.x).normalized;

            for (int i = 0; i < 4; i++)
            {
                float t = 0.22f + i * 0.16f;
                Vector2 anchor = impact + direction * t + right * (i % 2 == 0 ? -26f : 26f);
                GameObject branch = SpawnLightningImage($"LightningBranch_{i}", anchor, GetFallbackLightningSprite(),
                    new Vector2(46f, length * 0.28f), new Color(0.45f, 0.95f, 1f, 0.72f));
                RectTransform rect = (RectTransform)branch.transform;
                float side = i % 2 == 0 ? -1f : 1f;
                rect.localRotation = Quaternion.Euler(0f, 0f, side * (38f + i * 8f));
                branches.Add(branch);
            }

            for (int i = 0; i < 3; i++)
            {
                float angle = i * 120f + 18f;
                Vector2 offset = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                GameObject branch = SpawnLightningImage($"LightningGroundArc_{i}", impact + offset * 48f,
                    GetFallbackLightningSprite(), new Vector2(40f, 120f), new Color(0.35f, 0.95f, 1f, 0.72f));
                RectTransform rect = (RectTransform)branch.transform;
                rect.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
                branches.Add(branch);
            }

            return branches;
        }

        private GameObject SpawnLightningImage(string name, Vector2 position, Sprite sprite, Vector2 size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(_battleFieldRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = false;
            image.color = color;
            go.AddComponent<Projectile>();
            return go;
        }

        private static void PulseLightningObject(GameObject go, float scale, float alpha)
        {
            if (go == null) return;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            Image[] images = go.GetComponentsInChildren<Image>();
            foreach (Image image in images)
            {
                if (image == null) continue;
                Color c = image.color;
                c.a = Mathf.Clamp01(alpha);
                image.color = c;
            }
        }

        private static void DestroyLightningObject(GameObject go)
        {
            if (go != null) Destroy(go);
        }

        private IEnumerator LightningImpactBurstRoutine(Vector2 impact)
        {
            GameObject go = new("LightningImpactFx", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(_battleFieldRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = impact;
            rect.sizeDelta = new Vector2(150f, 150f);

            Image image = go.AddComponent<Image>();
            image.sprite = GetLightningBurstSprite();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.color = new Color(0.45f, 0.95f, 1f, 0.85f);

            const float duration = 0.35f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float scale = Mathf.Lerp(0.35f, 1.35f, u);
                rect.localScale = new Vector3(scale, scale, 1f);
                Color c = image.color;
                c.a = Mathf.Lerp(0.85f, 0f, u);
                image.color = c;
                yield return null;
            }
            Destroy(go);
        }

        private static Sprite _fallbackLightningSprite;
        private static Sprite GetFallbackLightningSprite()
        {
            if (_fallbackLightningSprite != null) return _fallbackLightningSprite;

            const int width = 64;
            const int height = 192;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            Vector2[] points =
            {
                new(width * 0.58f, height - 4f),
                new(width * 0.36f, height * 0.64f),
                new(width * 0.54f, height * 0.64f),
                new(width * 0.28f, 6f),
                new(width * 0.70f, height * 0.45f),
                new(width * 0.50f, height * 0.45f)
            };

            for (int i = 0; i < points.Length - 1; i++)
            {
                DrawLine(pixels, width, height, points[i], points[i + 1], 5, new Color(0.2f, 0.9f, 1f, 0.8f));
                DrawLine(pixels, width, height, points[i], points[i + 1], 2, Color.white);
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _fallbackLightningSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _fallbackLightningSprite;
        }

        private static Sprite _lightningBeamSprite;
        private static Sprite GetLightningBeamSprite()
        {
            if (_lightningBeamSprite != null) return _lightningBeamSprite;

            const int width = 64;
            const int height = 256;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            float center = (width - 1) * 0.5f;
            for (int y = 0; y < height; y++)
            {
                float waviness = Mathf.Sin(y * 0.19f) * 4f + Mathf.Sin(y * 0.047f) * 7f;
                for (int x = 0; x < width; x++)
                {
                    float dist = Mathf.Abs(x - center - waviness);
                    float core = Mathf.SmoothStep(1f, 0f, dist / 5f);
                    float glow = Mathf.SmoothStep(1f, 0f, dist / 24f);
                    float alpha = Mathf.Clamp01(core + glow * 0.55f);
                    Color color = Color.Lerp(new Color(0.1f, 0.65f, 1f, alpha), Color.white, core);
                    color.a = alpha;
                    pixels[y * width + x] = color;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _lightningBeamSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 100f);
            return _lightningBeamSprite;
        }

        private static Sprite _lightningBurstSprite;
        private static Sprite GetLightningBurstSprite()
        {
            if (_lightningBurstSprite != null) return _lightningBurstSprite;

            const int res = 128;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Vector2 center = new(res * 0.5f, res * 0.5f);
            Color[] pixels = new Color[res * res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (res * 0.5f);
                    float ring = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.45f) / 0.18f);
                    float core = Mathf.SmoothStep(1f, 0f, d / 0.25f);
                    float alpha = Mathf.Clamp01(ring * 0.75f + core * 0.55f);
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _lightningBurstSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _lightningBurstSprite;
        }

        private static Sprite _lightningRingSprite;
        private static Sprite GetLightningRingSprite()
        {
            if (_lightningRingSprite != null) return _lightningRingSprite;

            const int res = 128;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Vector2 center = new(res * 0.5f, res * 0.5f);
            Color[] pixels = new Color[res * res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (res * 0.5f);
                    float ring = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.55f) / 0.055f);
                    float outer = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.78f) / 0.04f) * 0.55f;
                    float alpha = Mathf.Clamp01(ring + outer);
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _lightningRingSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _lightningRingSprite;
        }

        private static void DrawLine(Color[] pixels, int width, int height, Vector2 a, Vector2 b, int radius, Color color)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / Mathf.Max(1f, steps));
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        int px = Mathf.RoundToInt(p.x) + x;
                        int py = Mathf.RoundToInt(p.y) + y;
                        if (px < 0 || px >= width || py < 0 || py >= height) continue;
                        if (x * x + y * y > radius * radius) continue;
                        int index = py * width + px;
                        Color existing = pixels[index];
                        pixels[index] = Color.Lerp(existing, color, color.a);
                    }
                }
            }
        }

        private void SpawnCloneView(UnitGroup unit)
        {
            Vector2 pos = FindEmptyPosition(unit.IsPlayerUnit, unit.Line);
            BattleUnit bu = SpawnUnitView(unit.IsPlayerUnit, unit.Line, pos);
            if (bu == null) return;
            bu.Init(unit, this);
            bu.SetActive(false);
            UnitGroupView view = bu.View;
            if (view != null) view.BindReal(unit);

            List<BattleUnit> list = unit.IsPlayerUnit ? _playerBattleUnits : _enemyBattleUnits;
            list.Add(bu);
            _byGroup[unit] = bu;
        }

        private static UnitGroup CloneUnitGroup(UnitGroup source, bool temporaryBattleOnly = false)
        {
            PendingUnitBuild build = new()
            {
                attack = source.Attack,
                hp = source.MaxHp,
                count = 1,
                line = source.Line,
                artwork = source.Artwork,
                idleSprite = source.IdleSprite,
                attackFrames = source.AttackFrames,
                projectileSprite = source.ProjectileSprite,
                displayName = source.DisplayName,
                moveSpeed = source.MoveSpeed,
                attackRange = source.AttackRange,
                attackCooldown = source.AttackCooldown,
                attackSpeed = source.AttackSpeed,
                projectileSpeed = source.ProjectileSpeed,
                projectileAoeRadius = source.ProjectileAoeRadius,
                unitType = source.UnitType,
                shadowScale = source.ShadowScale,
                shieldDuration = source.ShieldDuration,
                rallyBonus = source.RallyBonus,
                rallyDuration = source.RallyDuration,
                damageReduction = source.DamageReduction,
                temporaryBattleOnly = source.TemporaryBattleOnly || temporaryBattleOnly
            };
            return new UnitGroup(build, source.IsPlayerUnit);
        }

        private void OnEnable()
        {
            if (_cardPlayManager != null) _cardPlayManager.OnPendingBuildChanged += HandlePendingChanged;
            if (_battlefieldManager != null)
            {
                _battlefieldManager.OnUnitPlaced += HandleUnitPlaced;
                _battlefieldManager.OnUnitRemoved += HandleUnitRemoved;
            }
        }

        private void OnDisable()
        {
            if (_cardPlayManager != null) _cardPlayManager.OnPendingBuildChanged -= HandlePendingChanged;
            if (_battlefieldManager != null)
            {
                _battlefieldManager.OnUnitPlaced -= HandleUnitPlaced;
                _battlefieldManager.OnUnitRemoved -= HandleUnitRemoved;
            }
        }

        private void HandlePendingChanged(PendingUnitBuild build)
        {
            if (build == null)
            {
                return;
            }

            ClearPreviewObjects();
            _previewPositions.Clear();
            _placementCursor = 0;

            FormationLineView lineView = FindLine(isPlayer: true, build.line);
            if (lineView != null)
            {
                Vector2 fxPos = LaneAnchoredPosition(lineView);
                int summonCount = Mathf.Max(1, build.count);
                SpawnEffect.Play(_battleFieldRoot, fxPos, $"+{summonCount}", _smokeSprite, _summonTextColor);
            }

            _previewSpawnRoutine = StartCoroutine(SpawnPreviewUnitsRoutine(build));
        }

        private IEnumerator SpawnPreviewUnitsRoutine(PendingUnitBuild build)
        {
            int n = Mathf.Max(1, build.count);
            Vector2 center = FindGroupSpawnPosition(forPlayer: true, build.line);
            List<Vector2> targets = BuildPackedPositions(forPlayer: true, build.line, n);
            for (int i = 0; i < n; i++)
            {
                Vector2 spawnPosition = i < targets.Count ? targets[i] : PreviewSpawnPosition(center, i);
                BattleUnit pu = SpawnUnitView(forPlayer: true, line: build.line, position: spawnPosition);
                if (pu == null) continue;
                // Give previews a real UnitGroup so the separation force pushes them apart
                UnitGroup previewGroup = new(build, isPlayerUnit: true);
                pu.Init(previewGroup, this);
                pu.SetActive(false);
                UnitGroupView view = pu.View;
                if (view != null) view.BindPreview(build, isPlayerSide: true);
                _previewUnits.Add(pu);
                if (i < targets.Count) pu.SetSettleTarget(targets[i]);

                if (i < n - 1)
                {
                    float delay = i < 2 ? _previewSpawnStagger : _previewSpawnStagger * 0.35f;
                    yield return new WaitForSeconds(delay);
                }
            }

            _previewSpawnRoutine = null;
        }

        private void HandleUnitPlaced(UnitGroup unit)
        {
            if (unit.IsPlayerUnit && _previewUnits.Count > 0)
            {
                // Snapshot the previews' settled positions before destroying them,
                // so real units take over the exact spots the previews ended up at.
                _previewPositions.Clear();
                _placementCursor = 0;
                foreach (BattleUnit pu in _previewUnits)
                {
                    if (pu != null) _previewPositions.Add(pu.Rect.anchoredPosition);
                }
                ClearPreviewObjects();
            }

            bool consumingPreviewSlot = unit.IsPlayerUnit && _placementCursor < _previewPositions.Count;
            Vector2 pos = consumingPreviewSlot
                ? _previewPositions[_placementCursor++]
                : FindEmptyPosition(unit.IsPlayerUnit, unit.Line);
            BattleUnit battleUnit = SpawnUnitView(unit.IsPlayerUnit, unit.Line, pos, playDrop: !consumingPreviewSlot);
            if (battleUnit == null) return;
            battleUnit.Init(unit, this);
            battleUnit.SetActive(false);
            UnitGroupView view = battleUnit.View;
            if (view != null) view.BindReal(unit);

            List<BattleUnit> list = unit.IsPlayerUnit ? _playerBattleUnits : _enemyBattleUnits;
            list.Add(battleUnit);
            _byGroup[unit] = battleUnit;
        }

        private void HandleUnitRemoved(UnitGroup unit)
        {
            if (!_byGroup.TryGetValue(unit, out BattleUnit battleUnit)) return;
            if (battleUnit.IsPlayerUnit) _playerBattleUnits.Remove(battleUnit);
            else _enemyBattleUnits.Remove(battleUnit);
            _byGroup.Remove(unit);
            if (battleUnit != null)
            {
                Destroy(battleUnit.gameObject);
            }
        }

        private void ClearPreviewObjects()
        {
            if (_previewSpawnRoutine != null)
            {
                StopCoroutine(_previewSpawnRoutine);
                _previewSpawnRoutine = null;
            }

            foreach (BattleUnit pu in _previewUnits)
            {
                if (pu != null) Destroy(pu.gameObject);
            }
            _previewUnits.Clear();
        }

        private void ApplyBackground()
        {
            if (_backgroundImage == null || _backgroundSprite == null) return;

            _backgroundImage.sprite = _backgroundSprite;
            _backgroundImage.color = Color.white;
            _backgroundImage.type = Image.Type.Simple;
            _backgroundImage.preserveAspect = false;
            _backgroundImage.raycastTarget = false;
        }

        private Vector2 FindEmptyPosition(bool forPlayer, FormationLine line)
        {
            FormationLineView lineView = FindLine(forPlayer, line);
            if (lineView == null) return Vector2.zero;
            Vector2 laneCenter = LaneAnchoredPosition(lineView);

            for (int attempt = 0; attempt < _spawnMaxAttempts; attempt++)
            {
                Vector2 candidate = laneCenter + new Vector2(
                    Random.Range(-_laneHalfWidth, _laneHalfWidth),
                    Random.Range(-_laneHalfHeight, _laneHalfHeight));
                candidate = ClampToBounds(candidate);
                if (IsPositionFree(candidate, _spawnMinDistance))
                {
                    return candidate;
                }
            }

            // Fallback: give a random road-clamped spot inside the lane, even if it overlaps.
            return ClampToBounds(laneCenter + new Vector2(
                Random.Range(-_laneHalfWidth, _laneHalfWidth),
                Random.Range(-_laneHalfHeight, _laneHalfHeight)));
        }

        private Vector2 FindGroupSpawnPosition(bool forPlayer, FormationLine line)
        {
            FormationLineView lineView = FindLine(forPlayer, line);
            if (lineView == null) return Vector2.zero;
            return ClampToBounds(LaneAnchoredPosition(lineView));
        }

        private Vector2 PreviewSpawnPosition(Vector2 center, int index)
        {
            if (index <= 0) return center;

            int pair = (index + 1) / 2;
            float direction = index % 2 == 1 ? -1f : 1f;
            float x = direction * pair * _previewSpawnOffset;
            float y = (pair - 1) * _previewSpawnOffset * 0.45f;
            return ClampToBounds(center + new Vector2(x, y));
        }

        private void AssignPackedTargets(List<BattleUnit> units, bool forPlayer, FormationLine line)
        {
            if (units == null || units.Count == 0) return;

            List<Vector2> positions = BuildPackedPositions(forPlayer, line, units.Count);
            for (int i = 0; i < units.Count && i < positions.Count; i++)
            {
                if (units[i] != null) units[i].SetSettleTarget(positions[i]);
            }
        }

        private List<Vector2> BuildPackedPositions(bool forPlayer, FormationLine line, int count)
        {
            List<Vector2> positions = new(count);
            Vector2 center = FindGroupSpawnPosition(forPlayer, line);
            if (count <= 0) return positions;

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = PackedOffset(i);
                float x = offset.x * _packXSpacing;
                float y = offset.y * _packYSpacing;
                positions.Add(ClampToBounds(center + new Vector2(x, y)));
            }

            return positions;
        }

        private static Vector2 PackedOffset(int index)
        {
            // Vertical-first packing: stack along Y, then branch into adjacent X columns.
            switch (index)
            {
                case 0: return Vector2.zero;
                case 1: return new Vector2(0f, 1f);
                case 2: return new Vector2(0f, -1f);
                case 3: return new Vector2(0f, 2f);
                case 4: return new Vector2(0f, -2f);
                case 5: return new Vector2(-1f, 0f);
                case 6: return new Vector2(-1f, 1f);
                case 7: return new Vector2(-1f, -1f);
                case 8: return new Vector2(-1f, 2f);
                case 9: return new Vector2(-1f, -2f);
                case 10: return new Vector2(1f, 0f);
                case 11: return new Vector2(1f, 1f);
                case 12: return new Vector2(1f, -1f);
                case 13: return new Vector2(1f, 2f);
                case 14: return new Vector2(1f, -2f);
            }

            // For larger counts, keep adding columns to the side filled top-to-bottom.
            int adjusted = index - 15;
            const int rowsPerColumn = 5;
            int colIndex = adjusted / rowsPerColumn;
            int rowOffset = adjusted % rowsPerColumn - (rowsPerColumn - 1) / 2;
            int colSide = (colIndex % 2 == 0) ? -1 : 1;
            int colMagnitude = 2 + colIndex / 2;
            return new Vector2(colSide * colMagnitude, rowOffset);
        }

        private bool IsPositionFree(Vector2 candidate, float minDist)
        {
            float minSqr = minDist * minDist;
            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead) continue;
                if ((u.Rect.anchoredPosition - candidate).sqrMagnitude < minSqr) return false;
            }
            foreach (BattleUnit u in _enemyBattleUnits)
            {
                if (u == null || u.IsDead) continue;
                if ((u.Rect.anchoredPosition - candidate).sqrMagnitude < minSqr) return false;
            }
            foreach (BattleUnit u in _previewUnits)
            {
                if (u == null) continue;
                if ((u.Rect.anchoredPosition - candidate).sqrMagnitude < minSqr) return false;
            }
            return true;
        }

        // Reused scratch list so depth sorting allocates nothing per frame.
        private readonly List<BattleUnit> _depthSortScratch = new();

        // Painter's-algorithm depth sort: under a Canvas, render order follows sibling
        // order (later siblings draw on top). Units higher on screen (larger Y) are
        // further "back", so they should draw first; units lower on screen (smaller Y)
        // are closer to the viewer and draw last, in front. Runs after units have moved.
        private void LateUpdate()
        {
            _depthSortScratch.Clear();
            _depthSortScratch.AddRange(_playerBattleUnits);
            _depthSortScratch.AddRange(_enemyBattleUnits);
            _depthSortScratch.AddRange(_deadUnits);
            if (_depthSortScratch.Count < 2) return;

            // Larger Y first → ends up at a lower sibling index → drawn behind.
            _depthSortScratch.Sort(static (a, b) =>
            {
                if (a == null || a.Rect == null) return 1;
                if (b == null || b.Rect == null) return -1;
                return b.Rect.anchoredPosition.y.CompareTo(a.Rect.anchoredPosition.y);
            });

            for (int i = 0; i < _depthSortScratch.Count; i++)
            {
                BattleUnit u = _depthSortScratch[i];
                if (u != null && u.Rect != null && u.Rect.parent == _battleFieldRoot)
                {
                    u.Rect.SetSiblingIndex(i);
                }
            }
        }

        private BattleUnit SpawnUnitView(bool forPlayer, FormationLine line, Vector2 position, bool playDrop = true)
        {
            if (_unitViewPrefab == null || _battleFieldRoot == null) return null;

            UnitGroupView view = Instantiate(_unitViewPrefab, _battleFieldRoot);
            BattleUnit battleUnit = view.gameObject.AddComponent<BattleUnit>();

            RectTransform rt = (RectTransform)view.transform;
            rt.anchoredPosition = ClampToBounds(position);
            if (!forPlayer)
            {
                Vector3 scale = rt.localScale;
                scale.x = -Mathf.Abs(scale.x);
                rt.localScale = scale;
            }
            if (playDrop) battleUnit.StartDrop();
            return battleUnit;
        }

        private Vector2 LaneAnchoredPosition(FormationLineView lineView)
        {
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, lineView.transform.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_battleFieldRoot, screenPos, null, out Vector2 localPos);
            return localPos;
        }

        private FormationLineView FindLine(bool isPlayer, FormationLine line)
        {
            FormationLineView[] source = isPlayer ? _playerLines : _enemyLines;
            if (source == null) return null;
            foreach (FormationLineView lineView in source)
            {
                if (lineView != null && lineView.Line == line) return lineView;
            }
            return null;
        }
    }

    internal sealed class BattlefieldSlowFieldVfx : MonoBehaviour
    {
        private const int SnowflakeCount = 64;
        private static readonly Color SnowColor = new(0.86f, 0.96f, 1f, 1f);
        private static readonly Color FieldColor = new(0.34f, 0.78f, 1f, 0.32f);

        private readonly List<Snowflake> _snowflakes = new();
        private Image _fieldGlow;
        private SlowFieldBounds _bounds;
        private float _duration;

        public static void Play(RectTransform parent, IReadOnlyList<Vector2> unitPositions,
            Vector2 fallbackCenter, float duration)
        {
            if (parent == null) return;

            GameObject go = new("SlowFieldSnowVFX", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.SetAsLastSibling();

            BattlefieldSlowFieldVfx fx = go.AddComponent<BattlefieldSlowFieldVfx>();
            fx.PlayAt(unitPositions, fallbackCenter, duration);
        }

        private void PlayAt(IReadOnlyList<Vector2> unitPositions, Vector2 fallbackCenter, float duration)
        {
            _bounds = SlowFieldBounds.From(unitPositions, fallbackCenter);
            _duration = Mathf.Clamp(duration, 1.2f, 6f);

            RectTransform rect = (RectTransform)transform;
            rect.anchoredPosition = _bounds.Center;
            rect.sizeDelta = new Vector2(_bounds.Width + 150f, _bounds.Height + 150f);

            BuildVisuals(rect.sizeDelta);
            StartCoroutine(Animate());
        }

        private void BuildVisuals(Vector2 size)
        {
            _fieldGlow = SpawnImage("ColdMist", GetMistSprite(), Vector2.zero,
                new Vector2(size.x * 0.92f, size.y * 0.74f), FieldColor);

            for (int i = 0; i < SnowflakeCount; i++)
            {
                float n0 = Noise01(i, _bounds.Width);
                float n1 = Noise01(i + 71, _bounds.Height);
                float n2 = Noise01(i + 149, _bounds.Center.x + _bounds.Center.y);
                float sizePx = Mathf.Lerp(5f, 12f, n2);
                Vector2 start = new(
                    Mathf.Lerp(-size.x * 0.46f, size.x * 0.46f, n0),
                    Mathf.Lerp(size.y * 0.36f, size.y * 0.58f, n1));

                Image image = SpawnImage($"Snow_{i}", GetSnowflakeSprite(), start,
                    Vector2.one * sizePx, new Color(SnowColor.r, SnowColor.g, SnowColor.b, 0f));

                _snowflakes.Add(new Snowflake
                {
                    Rect = (RectTransform)image.transform,
                    Image = image,
                    Start = start,
                    Delay = Mathf.Lerp(0f, _duration * 0.72f, Noise01(i + 227, _bounds.Width)),
                    Fall = Mathf.Lerp(size.y * 0.46f, size.y * 0.72f, Noise01(i + 311, _bounds.Height)),
                    Drift = Mathf.Lerp(-42f, 42f, n2),
                    Scale = Mathf.Lerp(0.8f, 1.35f, n1)
                });
            }
        }

        private IEnumerator Animate()
        {
            float elapsed = 0f;
            while (elapsed < _duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / _duration);
                float inFade = Smooth01(Mathf.InverseLerp(0f, 0.12f, u));
                float outFade = 1f - Smooth01(Mathf.InverseLerp(0.78f, 1f, u));
                float fieldAlpha = inFade * outFade;

                if (_fieldGlow != null)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(elapsed * 2.4f);
                    _fieldGlow.transform.localScale = new Vector3(1f + pulse * 0.035f, 1f + pulse * 0.05f, 1f);
                    SetAlpha(_fieldGlow, FieldColor.a * fieldAlpha);
                }

                foreach (Snowflake flake in _snowflakes)
                {
                    if (flake.Rect == null || flake.Image == null) continue;

                    float cycle = Mathf.Repeat((elapsed - flake.Delay) / 1.55f, 1f);
                    float visible = elapsed >= flake.Delay ? 1f : 0f;
                    Vector2 pos = flake.Start + new Vector2(
                        flake.Drift * Mathf.Sin((cycle + flake.Scale) * Mathf.PI * 2f) * 0.35f,
                        -flake.Fall * cycle);
                    flake.Rect.anchoredPosition = pos;
                    flake.Rect.localScale = Vector3.one * flake.Scale;

                    float flakeFade = Mathf.Sin(cycle * Mathf.PI) * visible * fieldAlpha;
                    SetAlpha(flake.Image, 0.88f * flakeFade);
                }

                yield return null;
            }

            Destroy(gameObject);
        }

        private Image SpawnImage(string name, Sprite sprite, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform child = go.GetComponent<RectTransform>();
            child.SetParent(transform, false);
            child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
            child.pivot = new Vector2(0.5f, 0.5f);
            child.anchoredPosition = anchoredPosition;
            child.sizeDelta = size;

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = false;
            return image;
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image == null) return;
            Color c = image.color;
            c.a = Mathf.Clamp01(alpha);
            image.color = c;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Noise01(int index, float salt)
        {
            float v = Mathf.Sin(index * 12.9898f + salt * 0.073f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static Sprite _snowflakeSprite;
        private static Sprite GetSnowflakeSprite()
        {
            if (_snowflakeSprite != null) return _snowflakeSprite;

            const int res = 32;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[res * res];
            Vector2 center = new(res * 0.5f, res * 0.5f);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    Vector2 p = new(x + 0.5f, y + 0.5f);
                    float d = Vector2.Distance(p, center) / (res * 0.5f);
                    float core = Mathf.SmoothStep(1f, 0f, d);
                    float sparkle = Mathf.SmoothStep(1f, 0f, Mathf.Abs(p.x - center.x) / 1.6f)
                        + Mathf.SmoothStep(1f, 0f, Mathf.Abs(p.y - center.y) / 1.6f);
                    float alpha = Mathf.Clamp01(core * 0.86f + sparkle * 0.18f) * Mathf.SmoothStep(1f, 0f, d);
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _snowflakeSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _snowflakeSprite;
        }

        private static Sprite _mistSprite;
        private static Sprite GetMistSprite()
        {
            if (_mistSprite != null) return _mistSprite;

            const int width = 256;
            const int height = 192;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            Vector2 center = new(width * 0.5f, height * 0.48f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float nx = (x + 0.5f - center.x) / (width * 0.5f);
                    float ny = (y + 0.5f - center.y) / (height * 0.5f);
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float alpha = Mathf.SmoothStep(1f, 0f, d) * 0.72f;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _mistSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _mistSprite;
        }

        private struct Snowflake
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Start;
            public float Delay;
            public float Fall;
            public float Drift;
            public float Scale;
        }

        private readonly struct SlowFieldBounds
        {
            public readonly Vector2 Center;
            public readonly float Width;
            public readonly float Height;

            private SlowFieldBounds(Vector2 center, float width, float height)
            {
                Center = center;
                Width = width;
                Height = height;
            }

            public static SlowFieldBounds From(IReadOnlyList<Vector2> points, Vector2 fallbackCenter)
            {
                if (points == null || points.Count == 0)
                {
                    return new SlowFieldBounds(fallbackCenter, 250f, 190f);
                }

                Vector2 sum = Vector2.zero;
                Vector2 min = points[0];
                Vector2 max = points[0];
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 p = points[i];
                    sum += p;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }

                Vector2 center = sum / points.Count;
                float width = Mathf.Max(250f, max.x - min.x + 110f);
                float height = Mathf.Max(190f, max.y - min.y + 130f);
                return new SlowFieldBounds(center, width, height);
            }
        }
    }

    internal sealed class BattlefieldBarrierVfx : MonoBehaviour
    {
        private const float Duration = 0.9f;
        private const int BlinkCount = 3;
        private const float DomeScale = 1.5f;

        private Image _dome;
        private Image _baseRing;
        private Color _color;
        private DomeBounds _bounds;

        public static void Play(RectTransform parent, IReadOnlyList<Vector2> unitPositions,
            Vector2 fallbackCenter, Color color)
        {
            if (parent == null) return;

            GameObject go = new("BarrierDomeVFX", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.SetAsLastSibling();

            BattlefieldBarrierVfx fx = go.AddComponent<BattlefieldBarrierVfx>();
            fx.PlayAt(unitPositions, fallbackCenter, color);
        }

        private void PlayAt(IReadOnlyList<Vector2> unitPositions, Vector2 fallbackCenter, Color color)
        {
            _bounds = DomeBounds.From(unitPositions, fallbackCenter);
            _color = color;

            RectTransform rect = (RectTransform)transform;
            rect.anchoredPosition = _bounds.Center;
            rect.sizeDelta = new Vector2(_bounds.Diameter, _bounds.Diameter);

            BuildVisuals();
            StartCoroutine(Animate());
        }

        private void BuildVisuals()
        {
            Color domeColor = new(_color.r, _color.g, _color.b, 0.58f);
            Color ringColor = new(1f, 1f, 1f, 0.66f);
            float domeRadius = _bounds.Diameter * 0.5f;
            float baseY = -_bounds.Diameter * 0.28f;

            _baseRing = SpawnImage("BarrierBaseRing", GetBaseRingSprite(),
                new Vector2(0f, baseY),
                new Vector2(_bounds.Diameter * 0.88f, _bounds.Diameter * 0.18f),
                ringColor);

            _dome = SpawnImage("BarrierDome", GetDomeSprite(),
                new Vector2(0f, baseY + domeRadius * 0.5f),
                new Vector2(_bounds.Diameter, domeRadius),
                domeColor);
        }

        private IEnumerator Animate()
        {
            float elapsed = 0f;
            while (elapsed < Duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / Duration);
                float blink = 0.5f + 0.5f * Mathf.Sin(u * BlinkCount * Mathf.PI * 2f);
                float fadeOut = 1f - Smooth01(Mathf.InverseLerp(0.72f, 1f, u));
                float alphaPulse = Mathf.Lerp(0.32f, 1f, blink) * fadeOut;
                float scalePulse = 1f + 0.045f * blink;

                RectTransform rect = (RectTransform)transform;
                rect.localScale = new Vector3(scalePulse, scalePulse, 1f);

                SetAlpha(_dome, 0.58f * alphaPulse);
                SetAlpha(_baseRing, 0.66f * alphaPulse);

                yield return null;
            }

            Destroy(gameObject);
        }

        private Image SpawnImage(string name, Sprite sprite, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform child = go.GetComponent<RectTransform>();
            child.SetParent(transform, false);
            child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
            child.pivot = new Vector2(0.5f, 0.5f);
            child.anchoredPosition = anchoredPosition;
            child.sizeDelta = size;

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = false;
            return image;
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image == null) return;
            Color c = image.color;
            c.a = Mathf.Clamp01(alpha);
            image.color = c;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Sprite _domeSprite;
        private static Sprite GetDomeSprite()
        {
            if (_domeSprite != null) return _domeSprite;

            const int width = 256;
            const int height = 128;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            Vector2 center = new(width * 0.5f, 0f);
            float radius = width * 0.5f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2 p = new(x + 0.5f, y + 0.5f);
                    float dist = Vector2.Distance(p, center) / radius;
                    float alpha = 0f;
                    if (dist <= 1f)
                    {
                        float ny = (p.y - center.y) / radius;
                        float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 0.99f, dist));
                        float fill = Mathf.Lerp(0.1f, 0.26f, Mathf.Clamp01(ny));
                        float crown = Mathf.SmoothStep(1f, 0f, Mathf.Abs(ny - 0.72f) / 0.2f) * 0.16f;
                        alpha = Mathf.Clamp01(fill + rim + crown);
                        alpha *= Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.98f, 1f, dist));
                    }
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _domeSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 100f);
            return _domeSprite;
        }

        private static Sprite _baseRingSprite;
        private static Sprite GetBaseRingSprite()
        {
            if (_baseRingSprite != null) return _baseRingSprite;

            const int width = 256;
            const int height = 80;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            Vector2 center = new(width * 0.5f, height * 0.5f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float nx = (x + 0.5f - center.x) / (width * 0.5f);
                    float ny = (y + 0.5f - center.y) / (height * 0.5f);
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float ring = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.72f) / 0.065f);
                    float glow = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.72f) / 0.22f) * 0.34f;
                    float alpha = d <= 1f ? Mathf.Clamp01(ring + glow) : 0f;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _baseRingSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _baseRingSprite;
        }

        private readonly struct DomeBounds
        {
            public readonly Vector2 Center;
            public readonly float Diameter;

            private DomeBounds(Vector2 center, float diameter)
            {
                Center = center;
                Diameter = diameter;
            }

            public static DomeBounds From(IReadOnlyList<Vector2> points, Vector2 fallbackCenter)
            {
                if (points == null || points.Count == 0)
                {
                    return new DomeBounds(fallbackCenter, 280f * DomeScale);
                }

                Vector2 sum = Vector2.zero;
                Vector2 min = points[0];
                Vector2 max = points[0];
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 p = points[i];
                    sum += p;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }

                Vector2 center = sum / points.Count;
                float diameter = Mathf.Max(280f, max.x - min.x + 180f, max.y - min.y + 180f) * DomeScale;
                return new DomeBounds(center, diameter);
            }
        }
    }

    internal sealed class BattlefieldLevelUpVfx : MonoBehaviour
    {
        private const float Duration = 1f;
        private const int SparkCount = 26;

        private static readonly Color Gold = new(1f, 0.76f, 0.18f, 1f);
        private static readonly Color GlowColor = new(1f, 0.84f, 0.32f, 0.58f);

        private readonly List<Spark> _sparks = new();
        private Image _ring;
        private Image _glow;
        private Image _arrow;

        public static void Play(RectTransform parent, IReadOnlyList<Vector2> unitPositions, Sprite arrowSprite)
        {
            if (parent == null) return;

            GameObject go = new("LevelUpVFX", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            BattlefieldLevelUpVfx fx = go.AddComponent<BattlefieldLevelUpVfx>();
            fx.PlayAt(unitPositions, arrowSprite);
        }

        private void PlayAt(IReadOnlyList<Vector2> unitPositions, Sprite arrowSprite)
        {
            Bounds2D bounds = Bounds2D.From(unitPositions);
            RectTransform rect = (RectTransform)transform;
            rect.anchoredPosition = bounds.Center;
            rect.sizeDelta = new Vector2(bounds.Width + 190f, bounds.Height + 230f);

            BuildVisuals(bounds, arrowSprite);
            StartCoroutine(Animate(bounds));
        }

        private void BuildVisuals(Bounds2D bounds, Sprite arrowSprite)
        {
            _ring = SpawnImage("GroundRing", GetRingSprite(), Vector2.zero,
                new Vector2(bounds.Width + 150f, Mathf.Max(72f, bounds.Height * 0.42f + 58f)),
                new Color(Gold.r, Gold.g, Gold.b, 0.78f));

            _glow = SpawnImage("VerticalGlow", GetVerticalGlowSprite(), new Vector2(0f, 42f),
                new Vector2(Mathf.Max(120f, bounds.Width * 0.72f), Mathf.Max(190f, bounds.Height + 210f)),
                GlowColor);

            for (int i = 0; i < SparkCount; i++)
            {
                float n0 = Noise01(i, bounds.Width);
                float n1 = Noise01(i + 53, bounds.Height);
                float n2 = Noise01(i + 109, bounds.Width + bounds.Height);
                Vector2 start = new(
                    Mathf.Lerp(-bounds.Width * 0.5f, bounds.Width * 0.5f, n0),
                    Mathf.Lerp(-18f, Mathf.Max(20f, bounds.Height * 0.25f), n1));

                Image image = SpawnImage($"Spark_{i}", GetSparkSprite(), start,
                    Vector2.one * Mathf.Lerp(9f, 18f, n2),
                    new Color(1f, 0.86f, 0.34f, 0f));

                _sparks.Add(new Spark
                {
                    Rect = (RectTransform)image.transform,
                    Image = image,
                    Start = start,
                    Delay = Mathf.Lerp(0.02f, 0.22f, n1),
                    Rise = Mathf.Lerp(72f, 155f, n2),
                    Drift = Mathf.Lerp(-32f, 32f, Noise01(i + 211, bounds.Width)),
                    Scale = Mathf.Lerp(0.75f, 1.25f, n0)
                });
            }

            _arrow = SpawnImage("LevelUpArrow", arrowSprite != null ? arrowSprite : GetArrowSprite(),
                new Vector2(0f, bounds.Top + 74f), new Vector2(64f, 74f),
                new Color(Gold.r, Gold.g, Gold.b, 0f));
        }

        private IEnumerator Animate(Bounds2D bounds)
        {
            float elapsed = 0f;
            while (elapsed < Duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / Duration);
                float outFade = 1f - Smooth01(Mathf.InverseLerp(0.62f, 1f, u));

                if (_ring != null)
                {
                    float ringIn = Smooth01(Mathf.InverseLerp(0f, 0.45f, u));
                    _ring.transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 1.12f, ringIn);
                    SetAlpha(_ring, 0.78f * outFade);
                }

                if (_glow != null)
                {
                    float glowIn = Smooth01(Mathf.InverseLerp(0f, 0.2f, u));
                    _glow.transform.localScale = new Vector3(
                        Mathf.Lerp(0.75f, 1.05f, glowIn),
                        Mathf.Lerp(0.8f, 1.12f, u),
                        1f);
                    SetAlpha(_glow, GlowColor.a * glowIn * outFade);
                }

                foreach (Spark s in _sparks)
                {
                    float su = Mathf.Clamp01((elapsed - s.Delay) / 0.72f);
                    Vector2 pos = s.Start + new Vector2(s.Drift * su, s.Rise * Smooth01(su));
                    s.Rect.anchoredPosition = pos;
                    s.Rect.localScale = Vector3.one * Mathf.Lerp(0.35f, s.Scale, su);
                    SetAlpha(s.Image, Mathf.Sin(su * Mathf.PI) * 0.92f);
                }

                if (_arrow != null)
                {
                    float arrowIn = Smooth01(Mathf.InverseLerp(0.08f, 0.28f, u));
                    float arrowOut = 1f - Smooth01(Mathf.InverseLerp(0.72f, 1f, u));
                    RectTransform ar = (RectTransform)_arrow.transform;
                    ar.anchoredPosition = new Vector2(0f, bounds.Top + 74f + 18f * Smooth01(u));
                    float pulse = 1f + Mathf.Sin(Mathf.Clamp01(u * 2.6f) * Mathf.PI) * 0.18f;
                    ar.localScale = Vector3.one * Mathf.Lerp(0.72f, pulse, arrowIn);
                    SetAlpha(_arrow, arrowIn * arrowOut);
                }

                yield return null;
            }

            Destroy(gameObject);
        }

        private Image SpawnImage(string name, Sprite sprite, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform child = go.GetComponent<RectTransform>();
            child.SetParent(transform, false);
            child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
            child.pivot = new Vector2(0.5f, 0.5f);
            child.anchoredPosition = anchoredPosition;
            child.sizeDelta = size;

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = false;
            return image;
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image == null) return;
            Color c = image.color;
            c.a = Mathf.Clamp01(alpha);
            image.color = c;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Noise01(int index, float salt)
        {
            float v = Mathf.Sin(index * 12.9898f + salt * 0.073f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static Sprite _ringSprite;
        private static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            const int width = 256;
            const int height = 128;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            Vector2 center = new(width * 0.5f, height * 0.5f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float nx = (x + 0.5f - center.x) / (width * 0.5f);
                    float ny = (y + 0.5f - center.y) / (height * 0.5f);
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float ring = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.66f) / 0.055f);
                    float outerGlow = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.76f) / 0.16f) * 0.42f;
                    float innerGlow = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.42f) / 0.25f) * 0.22f;
                    float alpha = d <= 1f ? Mathf.Clamp01(ring + outerGlow + innerGlow) : 0f;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _ringSprite;
        }

        private static Sprite _verticalGlowSprite;
        private static Sprite GetVerticalGlowSprite()
        {
            if (_verticalGlowSprite != null) return _verticalGlowSprite;

            const int width = 96;
            const int height = 256;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            Vector2 center = new(width * 0.5f, height * 0.18f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float nx = Mathf.Abs(x + 0.5f - center.x) / (width * 0.5f);
                    float up = Mathf.Clamp01((y + 0.5f) / height);
                    float core = Mathf.SmoothStep(1f, 0f, nx);
                    float rise = Mathf.SmoothStep(1f, 0f, Mathf.Abs(up - 0.45f) / 0.55f);
                    float alpha = core * rise * Mathf.SmoothStep(0f, 1f, up) * 0.9f;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _verticalGlowSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 100f);
            return _verticalGlowSprite;
        }

        private static Sprite _sparkSprite;
        private static Sprite GetSparkSprite()
        {
            if (_sparkSprite != null) return _sparkSprite;

            const int res = 32;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[res * res];
            Vector2 center = new(res * 0.5f, res * 0.5f);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (res * 0.5f);
                    pixels[y * res + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(1f, 0f, d));
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _sparkSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _sparkSprite;
        }

        private static Sprite _arrowSprite;
        private static Sprite GetArrowSprite()
        {
            if (_arrowSprite != null) return _arrowSprite;

            const int width = 96;
            const int height = 112;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / width - 0.5f);
                    bool shaft = v > 0.06f && v < 0.58f && u < 0.13f;
                    float headHalf = Mathf.Lerp(0.44f, 0.03f, Mathf.InverseLerp(0.48f, 0.96f, v));
                    bool head = v >= 0.42f && v < 0.97f && u < headHalf;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, shaft || head ? 1f : 0f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _arrowSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _arrowSprite;
        }

        private struct Spark
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Start;
            public float Delay;
            public float Rise;
            public float Drift;
            public float Scale;
        }

        private readonly struct Bounds2D
        {
            public readonly Vector2 Center;
            public readonly float Width;
            public readonly float Height;
            public readonly float Top;

            private Bounds2D(Vector2 center, float width, float height, float top)
            {
                Center = center;
                Width = width;
                Height = height;
                Top = top;
            }

            public static Bounds2D From(IReadOnlyList<Vector2> points)
            {
                if (points == null || points.Count == 0)
                {
                    return new Bounds2D(Vector2.zero, 120f, 70f, 35f);
                }

                Vector2 sum = Vector2.zero;
                Vector2 min = points[0];
                Vector2 max = points[0];
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 p = points[i];
                    sum += p;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }

                Vector2 center = sum / points.Count;
                float width = Mathf.Max(120f, max.x - min.x);
                float height = Mathf.Max(70f, max.y - min.y);
                return new Bounds2D(center, width, height, max.y - center.y);
            }
        }
    }

    internal sealed class BattlefieldMarkVfx : MonoBehaviour
    {
        private const float Duration = 0.68f;

        private Image _outerAim;
        private Image _innerAim;
        private Image _focusGlow;
        private Image _verticalSight;
        private Image _horizontalSight;
        private Color _aimColor;

        public static void Play(RectTransform parent, IReadOnlyList<Vector2> unitPositions, Vector2 fallbackCenter, Color color)
        {
            if (parent == null) return;

            GameObject go = new("MarkAimVFX", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            BattlefieldMarkVfx fx = go.AddComponent<BattlefieldMarkVfx>();
            fx.PlayAt(unitPositions, fallbackCenter, color);
        }

        private void PlayAt(IReadOnlyList<Vector2> unitPositions, Vector2 fallbackCenter, Color color)
        {
            MarkBounds bounds = MarkBounds.From(unitPositions, fallbackCenter);
            RectTransform rect = (RectTransform)transform;
            rect.anchoredPosition = bounds.Center;
            rect.sizeDelta = new Vector2(bounds.Width + 250f, bounds.Height + 190f);

            _aimColor = color;
            BuildVisuals(rect.sizeDelta);
            StartCoroutine(Animate());
        }

        private void BuildVisuals(Vector2 size)
        {
            float aimSize = Mathf.Max(size.x, size.y);
            Color strong = new(_aimColor.r, _aimColor.g, _aimColor.b, 0.92f);
            Color soft = new(_aimColor.r, _aimColor.g, _aimColor.b, 0.45f);

            _focusGlow = SpawnImage("FocusGlow", GetFocusGlowSprite(), Vector2.zero,
                new Vector2(aimSize * 0.78f, aimSize * 0.78f), soft);
            _outerAim = SpawnImage("OuterAim", GetAimReticleSprite(), Vector2.zero,
                new Vector2(aimSize, aimSize), strong);
            _innerAim = SpawnImage("InnerAim", GetAimReticleSprite(), Vector2.zero,
                new Vector2(aimSize * 0.44f, aimSize * 0.44f), new Color(1f, 1f, 1f, 0.82f));
            _verticalSight = SpawnImage("VerticalSight", GetSightLineSprite(), Vector2.zero,
                new Vector2(6f, aimSize * 0.9f), soft);
            _horizontalSight = SpawnImage("HorizontalSight", GetSightLineSprite(), Vector2.zero,
                new Vector2(6f, aimSize * 0.9f), soft);
            if (_horizontalSight != null)
            {
                ((RectTransform)_horizontalSight.transform).localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        private IEnumerator Animate()
        {
            RectTransform rect = (RectTransform)transform;
            float elapsed = 0f;
            while (elapsed < Duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / Duration);
                float eased = Smooth01(u);
                float fade = 1f - Smooth01(Mathf.InverseLerp(0.42f, 1f, u));

                float scale = Mathf.Lerp(1.42f, 0.24f, eased);
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-10f, 7f, u));

                SetAlpha(_outerAim, 0.92f * fade);
                SetAlpha(_innerAim, Mathf.Lerp(0.95f, 0.25f, u) * fade);
                SetAlpha(_focusGlow, 0.45f * Mathf.Sin(u * Mathf.PI) * fade);
                SetAlpha(_verticalSight, 0.55f * fade);
                SetAlpha(_horizontalSight, 0.55f * fade);

                yield return null;
            }

            Destroy(gameObject);
        }

        private Image SpawnImage(string name, Sprite sprite, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform child = go.GetComponent<RectTransform>();
            child.SetParent(transform, false);
            child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
            child.pivot = new Vector2(0.5f, 0.5f);
            child.anchoredPosition = anchoredPosition;
            child.sizeDelta = size;

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = false;
            return image;
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image == null) return;
            Color c = image.color;
            c.a = Mathf.Clamp01(alpha);
            image.color = c;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Sprite _aimReticleSprite;
        private static Sprite GetAimReticleSprite()
        {
            if (_aimReticleSprite != null) return _aimReticleSprite;

            const int res = 256;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[res * res];
            Vector2 center = new(res * 0.5f, res * 0.5f);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx = (x + 0.5f - center.x) / (res * 0.5f);
                    float ny = (y + 0.5f - center.y) / (res * 0.5f);
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float angle = Mathf.Atan2(ny, nx) * Mathf.Rad2Deg;
                    if (angle < 0f) angle += 360f;

                    float outerRing = RingAlpha(d, 0.78f, 0.035f);
                    float innerRing = RingAlpha(d, 0.36f, 0.025f) * 0.9f;
                    float notch = IsNearCardinal(angle, 7f) && d > 0.48f && d < 0.96f ? 1f : 0f;
                    float centerDot = Mathf.SmoothStep(1f, 0f, d / 0.04f);
                    float alpha = Mathf.Clamp01(outerRing + innerRing + notch + centerDot);
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _aimReticleSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _aimReticleSprite;
        }

        private static Sprite _focusGlowSprite;
        private static Sprite GetFocusGlowSprite()
        {
            if (_focusGlowSprite != null) return _focusGlowSprite;

            const int res = 128;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[res * res];
            Vector2 center = new(res * 0.5f, res * 0.5f);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (res * 0.5f);
                    float alpha = Mathf.SmoothStep(1f, 0f, d) * 0.72f;
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _focusGlowSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _focusGlowSprite;
        }

        private static Sprite _sightLineSprite;
        private static Sprite GetSightLineSprite()
        {
            if (_sightLineSprite != null) return _sightLineSprite;

            const int width = 16;
            const int height = 256;
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[width * height];
            float center = (width - 1) * 0.5f;
            for (int y = 0; y < height; y++)
            {
                float v = Mathf.Abs((y + 0.5f) / height - 0.5f) * 2f;
                for (int x = 0; x < width; x++)
                {
                    float dist = Mathf.Abs(x - center);
                    float core = Mathf.SmoothStep(1f, 0f, dist / 2.4f);
                    float fade = Mathf.SmoothStep(1f, 0f, v);
                    pixels[y * width + x] = new Color(1f, 1f, 1f, core * fade);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _sightLineSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _sightLineSprite;
        }

        private static float RingAlpha(float distance, float radius, float thickness)
        {
            return Mathf.SmoothStep(1f, 0f, Mathf.Abs(distance - radius) / thickness);
        }

        private static bool IsNearCardinal(float angle, float tolerance)
        {
            float a = angle % 90f;
            return a <= tolerance || a >= 90f - tolerance;
        }

        private readonly struct MarkBounds
        {
            public readonly Vector2 Center;
            public readonly float Width;
            public readonly float Height;

            private MarkBounds(Vector2 center, float width, float height)
            {
                Center = center;
                Width = width;
                Height = height;
            }

            public static MarkBounds From(IReadOnlyList<Vector2> points, Vector2 fallbackCenter)
            {
                if (points == null || points.Count == 0)
                {
                    return new MarkBounds(fallbackCenter, 135f, 95f);
                }

                Vector2 sum = Vector2.zero;
                Vector2 min = points[0];
                Vector2 max = points[0];
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 p = points[i];
                    sum += p;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }

                Vector2 center = sum / points.Count;
                float width = Mathf.Max(135f, max.x - min.x);
                float height = Mathf.Max(95f, max.y - min.y);
                return new MarkBounds(center, width, height);
            }
        }
    }
}
