using System.Collections;
using System.Collections.Generic;
using DraftCards.Battle;
using DraftCards.Cards;
using DraftCards.Core;
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
            foreach (BattleUnit u in _playerBattleUnits) u.SetActive(true);
            foreach (BattleUnit u in _enemyBattleUnits) u.SetActive(true);
        }

        public void PauseBattle()
        {
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

        private void DestroyEnemyUnit(BattleUnit unit)
        {
            if (unit == null) return;
            if (unit.Group != null) _byGroup.Remove(unit.Group);
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

        private readonly List<GameObject> _laneTargetOverlays = new();
        private readonly Dictionary<FormationLine, GameObject> _overlayByLane = new();
        private FormationLine? _hoveredLaneOverlay;

        private bool _laneTargetsActive;

        public void ShowPlayerLaneTargets()
        {
            HidePlayerLaneTargets();
            _laneTargetsActive = true;
            Debug.Log("[BattlefieldView] ShowPlayerLaneTargets: drag mode active (unit highlight)");
        }

        public void HighlightHoveredLane(FormationLine? lane)
        {
            if (!_laneTargetsActive) return;
            if (_hoveredLaneOverlay == lane) return;
            _hoveredLaneOverlay = lane;
            foreach (BattleUnit u in _playerBattleUnits)
            {
                if (u == null || u.IsDead || u.Group == null || u.View == null) continue;
                bool isInLane = lane.HasValue && u.Group.Line == lane.Value;
                u.View.SetHighlight(isInLane);
            }
            // Preview units (pending summons) participate in highlight too.
            foreach (BattleUnit u in _previewUnits)
            {
                if (u == null || u.Group == null || u.View == null) continue;
                bool isInLane = lane.HasValue && u.Group.Line == lane.Value;
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

        public void DuplicatePlayerUnits(PendingUnitBuild pendingBuild, FormationLine? targetLane = null)
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

            BattleUnit[] realSnapshot = _playerBattleUnits.ToArray();
            foreach (BattleUnit src in realSnapshot)
            {
                if (src == null || src.IsDead || src.Group == null) continue;
                if (src.Group.Line != lane)
                {
                    Debug.Log($"  skip {src.Group.DisplayName} (line={src.Group.Line})");
                    continue;
                }
                Debug.Log($"  CLONE {src.Group.DisplayName} (line={src.Group.Line})");
                UnitGroup clone = CloneUnitGroup(src.Group);
                _battlefieldManager?.AddSilent(clone);
                SpawnCloneView(clone);
            }

            bool previewsMatch = pendingBuild != null
                && _previewUnits.Count > 0
                && pendingBuild.line == lane;

            if (previewsMatch)
            {
                BattleUnit[] previewSnapshot = _previewUnits.ToArray();
                foreach (BattleUnit src in previewSnapshot)
                {
                    if (src == null || src.Group == null) continue;
                    FormationLine line = src.Group.Line;
                    Vector2 pos = FindGroupSpawnPosition(forPlayer: true, line);
                    BattleUnit pu = SpawnUnitView(forPlayer: true, line: line, position: pos);
                    if (pu == null) continue;
                    UnitGroup previewGroup = new(pendingBuild, isPlayerUnit: true);
                    pu.Init(previewGroup, this);
                    pu.SetActive(false);
                    UnitGroupView view = pu.View;
                    if (view != null) view.BindPreview(pendingBuild, isPlayerSide: true);
                    _previewUnits.Add(pu);
                }
                pendingBuild.count = _previewUnits.Count;
                AssignPackedTargets(_previewUnits, forPlayer: true, pendingBuild.line);
            }

            Vector2 fxPos = LaneAnchoredPosition(FindLine(true, lane));
            SpawnEffect.Play(_battleFieldRoot, fxPos, "x2", _smokeSprite, _spellTextColor);
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

        // Clears one-turn battlefield effects (currently the Revive budget). Called at the
        // start of each draft turn so a spell's effect never bleeds into a later round.
        public void ResetTurnEffects()
        {
            _reviveBudget = 0;
            _reviveHpFraction = 0f;
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

        private static UnitGroup CloneUnitGroup(UnitGroup source)
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
                unitType = source.UnitType,
                shadowScale = source.ShadowScale,
                shieldDuration = source.ShieldDuration,
                rallyBonus = source.RallyBonus,
                rallyDuration = source.RallyDuration
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
}
