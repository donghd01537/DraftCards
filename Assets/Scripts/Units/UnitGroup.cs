using System.Collections.Generic;
using DraftCards.Cards;
using DraftCards.Core;
using UnityEngine;

namespace DraftCards.Units
{
    public class UnitGroup
    {
        public int Attack { get; private set; }
        public int MaxHp { get; private set; }
        public int Count { get; private set; }
        public int CurrentHp { get; private set; }
        public FormationLine Line { get; private set; }
        public bool IsPlayerUnit { get; private set; }
        public bool TemporaryBattleOnly { get; private set; }
        public Sprite Artwork { get; private set; }
        public Sprite IdleSprite { get; private set; }
        public List<Sprite> AttackFrames { get; private set; }
        public Sprite ProjectileSprite { get; private set; }
        public string DisplayName { get; private set; }
        // Upgrade family this unit belongs to (the base unit card's cardId), so the
        // Upgrade spell can match and evolve it. Empty for clones/enemies. See UpgradeManager.
        public string FamilyId { get; private set; }

        public float MoveSpeed { get; private set; }
        public float AttackRange { get; private set; }
        public float AttackCooldown { get; private set; }
        public float AttackSpeed { get; private set; }
        // Travel speed of this unit's thrown projectile (ranged units only).
        public float ProjectileSpeed { get; private set; }
        public float ProjectileAoeRadius { get; private set; }
        public UnitType UnitType { get; private set; }
        public float ShadowScale { get; private set; }

        // The active speed multiplier from a Rally buff (1 = no buff). Folded into the
        // move speed and attack rate while the rally window is open.
        private float RallyMultiplier => IsRallied ? 1f + _rallyBonus : 1f;
        private float SlowMultiplier => IsSlowed ? Mathf.Max(0.1f, 1f - _slowPercent) : 1f;
        private float SpeedMultiplier => RallyMultiplier * SlowMultiplier;
        private float DamageTakenMultiplier => Mathf.Max(0.05f, (1f - _damageReduction) * (1f + _markBonusDamageTaken));
        public float EffectiveMoveSpeed => MoveSpeed * SpeedMultiplier;
        public float EffectiveAttackCooldown => AttackCooldown / Mathf.Max(0.01f, AttackSpeed * SpeedMultiplier);

        public int TotalDamage => Attack * Count;
        public bool IsDead => CurrentHp <= 0;

        // Fortify shield: full damage immunity for a fixed window once the battle starts.
        // _shieldDuration is armed by the Fortify spell during planning; the countdown only
        // runs while the unit is active (battle phase), driven by BattleUnit each frame.
        private float _shieldDuration;
        private float _shieldRemaining;
        public bool IsShielded => _shieldRemaining > 0f;
        public bool HasShield => _shieldDuration > 0f;
        public float ShieldDuration => _shieldDuration;

        // Rally buff: a flat move/attack-speed bonus for the first X seconds of combat.
        // Like the shield, it's armed during planning and only counts down once the unit
        // is active (battle phase). It's a one-battle effect — see TickRally.
        private float _rallyDuration;
        private float _rallyRemaining;
        private float _rallyBonus;
        public bool IsRallied => _rallyRemaining > 0f;
        public bool HasRally => _rallyDuration > 0f;
        public float RallyDuration => _rallyDuration;
        public float RallyBonus => _rallyBonus;

        private float _slowDuration;
        private float _slowRemaining;
        private float _slowPercent;
        public bool IsSlowed => _slowRemaining > 0f;
        public bool HasSlow => _slowDuration > 0f;
        public float SlowDuration => _slowDuration;
        public float SlowPercent => _slowPercent;

        private float _damageReduction;
        public float DamageReduction => _damageReduction;
        private float _markBonusDamageTaken;
        public bool IsMarked => _markBonusDamageTaken > 0f;
        public float MarkBonusDamageTaken => _markBonusDamageTaken;

        public UnitGroup(PendingUnitBuild build, bool isPlayerUnit)
        {
            Attack = build.attack;
            MaxHp = build.hp;
            Count = 1;
            Line = build.line;
            IsPlayerUnit = isPlayerUnit;
            TemporaryBattleOnly = build.temporaryBattleOnly;
            Artwork = build.artwork;
            IdleSprite = build.idleSprite;
            AttackFrames = build.attackFrames;
            ProjectileSprite = build.projectileSprite;
            DisplayName = build.displayName;
            FamilyId = build.familyId;
            MoveSpeed = build.moveSpeed;
            AttackRange = build.attackRange;
            AttackCooldown = build.attackCooldown;
            AttackSpeed = build.attackSpeed;
            ProjectileSpeed = build.projectileSpeed;
            ProjectileAoeRadius = Mathf.Max(0f, build.projectileAoeRadius);
            UnitType = build.unitType;
            ShadowScale = build.shadowScale > 0f ? build.shadowScale : 1f;
            CurrentHp = MaxHp;
            if (build.shieldDuration > 0f) ApplyShield(build.shieldDuration);
            if (build.rallyDuration > 0f) ApplyRally(build.rallyBonus, build.rallyDuration);
            if (build.damageReduction > 0f) ApplyDamageReduction(build.damageReduction);
        }

        public void ApplyAttackMultiplier(float multiplier)
        {
            Attack = Mathf.Max(0, Mathf.RoundToInt(Attack * multiplier));
        }

        // Scales every combat stat by `multiplier` (1.10 = +10%). Used by the Unit Upgrade
        // spell's stat-only tier. Current HP scales with max HP so a damaged unit keeps its
        // damage proportion (it isn't silently healed to full by an upgrade).
        public void ApplyStatMultiplier(float multiplier)
        {
            if (multiplier <= 0f || Mathf.Approximately(multiplier, 1f)) return;

            float hpFraction = MaxHp > 0 ? (float)CurrentHp / MaxHp : 1f;
            Attack = Mathf.Max(0, Mathf.RoundToInt(Attack * multiplier));
            MaxHp = Mathf.Max(1, Mathf.RoundToInt(MaxHp * multiplier));
            CurrentHp = Mathf.Clamp(Mathf.RoundToInt(MaxHp * hpFraction), 1, MaxHp);
            MoveSpeed *= multiplier;
            AttackRange *= multiplier;
            AttackSpeed *= multiplier;
            if (ProjectileSpeed > 0f) ProjectileSpeed *= multiplier;
        }

        // Re-skins this group to an evolved form: swaps art, name, family, line, and the
        // unit's combat stats to the evolved card's. Used by the Unit Upgrade spell when a
        // family changes identity (e.g. Swordsman -> Spartan). Preserves the current HP
        // proportion against the new max HP, and clears any temporary battle buffs that
        // shouldn't survive a transformation.
        public void ReskinTo(PendingUnitBuild evolved)
        {
            if (evolved == null) return;

            float hpFraction = MaxHp > 0 ? (float)CurrentHp / MaxHp : 1f;

            Attack = evolved.attack;
            MaxHp = Mathf.Max(1, evolved.hp);
            CurrentHp = Mathf.Clamp(Mathf.RoundToInt(MaxHp * hpFraction), 1, MaxHp);
            Line = evolved.line;
            Artwork = evolved.artwork;
            IdleSprite = evolved.idleSprite;
            AttackFrames = evolved.attackFrames;
            ProjectileSprite = evolved.projectileSprite;
            DisplayName = evolved.displayName;
            FamilyId = evolved.familyId;
            MoveSpeed = evolved.moveSpeed;
            AttackRange = evolved.attackRange;
            AttackCooldown = evolved.attackCooldown;
            AttackSpeed = evolved.attackSpeed;
            ProjectileSpeed = evolved.projectileSpeed;
            ProjectileAoeRadius = Mathf.Max(0f, evolved.projectileAoeRadius);
            UnitType = evolved.unitType;
            ShadowScale = evolved.shadowScale > 0f ? evolved.shadowScale : 1f;
        }

        // Arms (or extends) the Fortify shield. The timer doesn't start ticking until the
        // battle activates the unit — see ActivateShield/TickShield, called by BattleUnit.
        public void ApplyShield(float duration)
        {
            if (duration <= 0f) return;
            _shieldDuration = Mathf.Max(_shieldDuration, duration);
        }

        // Begins the shield's countdown for the "first X seconds" of the battle. Called when
        // the unit is set active so the window measures combat time, not planning time.
        public void ActivateShield()
        {
            _shieldRemaining = _shieldDuration;
        }

        // Counts the shield window down during combat. Returns true while still shielded.
        // Fortify is a one-battle effect: once the window is spent, clear the armed
        // duration so a surviving unit isn't re-shielded for free next round.
        public bool TickShield(float deltaTime)
        {
            if (_shieldRemaining <= 0f) return false;
            _shieldRemaining = Mathf.Max(0f, _shieldRemaining - deltaTime);
            if (_shieldRemaining <= 0f)
            {
                _shieldDuration = 0f;
                return false;
            }
            return true;
        }

        public void ClearBattlefieldSpellEffects()
        {
            _shieldDuration = 0f;
            _shieldRemaining = 0f;
            _rallyDuration = 0f;
            _rallyRemaining = 0f;
            _rallyBonus = 0f;
            _slowDuration = 0f;
            _slowRemaining = 0f;
            _slowPercent = 0f;
            _damageReduction = 0f;
            _markBonusDamageTaken = 0f;
        }

        // Arms (or strengthens) the Rally buff. The timer doesn't start until the battle
        // activates the unit — see ActivateRally/TickRally, called by BattleUnit. When two
        // rallies stack, keep the stronger bonus and the longer window.
        public void ApplyRally(float bonus, float duration)
        {
            if (bonus <= 0f || duration <= 0f) return;
            _rallyBonus = Mathf.Max(_rallyBonus, bonus);
            _rallyDuration = Mathf.Max(_rallyDuration, duration);
        }

        // Begins the rally's countdown for the "first X seconds" of the battle. Called when
        // the unit is set active so the window measures combat time, not planning time.
        public void ActivateRally()
        {
            _rallyRemaining = _rallyDuration;
        }

        // Counts the rally window down during combat. Returns true while still rallied.
        // Rally is a one-battle effect: once the window is spent, clear the armed buff so
        // a surviving unit isn't re-rallied for free next round.
        public bool TickRally(float deltaTime)
        {
            if (_rallyRemaining <= 0f) return false;
            _rallyRemaining = Mathf.Max(0f, _rallyRemaining - deltaTime);
            if (_rallyRemaining <= 0f)
            {
                _rallyDuration = 0f;
                _rallyBonus = 0f;
                return false;
            }
            return true;
        }

        public void ApplySlow(float percent, float duration)
        {
            if (percent <= 0f || duration <= 0f) return;
            _slowPercent = Mathf.Max(_slowPercent, Mathf.Clamp01(percent));
            _slowDuration = Mathf.Max(_slowDuration, duration);
        }

        public void ActivateSlow()
        {
            _slowRemaining = _slowDuration;
        }

        public bool TickSlow(float deltaTime)
        {
            if (_slowRemaining <= 0f) return false;
            _slowRemaining = Mathf.Max(0f, _slowRemaining - deltaTime);
            if (_slowRemaining <= 0f)
            {
                _slowDuration = 0f;
                _slowPercent = 0f;
                return false;
            }
            return true;
        }

        public void ApplyDamageReduction(float percent)
        {
            if (percent <= 0f) return;
            _damageReduction = Mathf.Max(_damageReduction, Mathf.Clamp01(percent));
        }

        public void ApplyMark(float bonusDamageTaken)
        {
            if (bonusDamageTaken <= 0f) return;
            _markBonusDamageTaken = Mathf.Max(_markBonusDamageTaken, bonusDamageTaken);
        }

        public void TakeDamage(int damage)
        {
            // While the Fortify shield holds, the unit is fully immune.
            if (_shieldRemaining > 0f) return;

            int adjustedDamage = Mathf.Max(0, Mathf.RoundToInt(damage * DamageTakenMultiplier));
            if (damage > 0 && adjustedDamage <= 0)
            {
                adjustedDamage = 1;
            }

            CurrentHp -= adjustedDamage;
            if (CurrentHp <= 0)
            {
                CurrentHp = 0;
                Count = 0;
            }
        }

        public void Revive()
        {
            ReviveWithHpFraction(1f);
        }

        // Brings a dead group back with a fraction of its max HP (e.g. 0.5 = 50%).
        // Used by the Revive spell, which resurrects the first X player units to fall
        // in a battle at partial health. Always leaves at least 1 HP so the unit lives.
        public void ReviveWithHpFraction(float fraction)
        {
            float clamped = Mathf.Clamp01(fraction);
            CurrentHp = Mathf.Max(1, Mathf.RoundToInt(MaxHp * clamped));
            Count = 1;
        }
    }
}
