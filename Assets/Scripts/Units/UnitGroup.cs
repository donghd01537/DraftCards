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
        public Sprite Artwork { get; private set; }
        public Sprite IdleSprite { get; private set; }
        public List<Sprite> AttackFrames { get; private set; }
        public Sprite ProjectileSprite { get; private set; }
        public string DisplayName { get; private set; }

        public float MoveSpeed { get; private set; }
        public float AttackRange { get; private set; }
        public float AttackCooldown { get; private set; }
        public float AttackSpeed { get; private set; }
        // Travel speed of this unit's thrown projectile (ranged units only).
        public float ProjectileSpeed { get; private set; }
        public UnitType UnitType { get; private set; }
        public float ShadowScale { get; private set; }

        // The active speed multiplier from a Rally buff (1 = no buff). Folded into the
        // move speed and attack rate while the rally window is open.
        private float RallyMultiplier => IsRallied ? 1f + _rallyBonus : 1f;
        public float EffectiveMoveSpeed => MoveSpeed * RallyMultiplier;
        public float EffectiveAttackCooldown => AttackCooldown / Mathf.Max(0.01f, AttackSpeed * RallyMultiplier);

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

        public UnitGroup(PendingUnitBuild build, bool isPlayerUnit)
        {
            Attack = build.attack;
            MaxHp = build.hp;
            Count = 1;
            Line = build.line;
            IsPlayerUnit = isPlayerUnit;
            Artwork = build.artwork;
            IdleSprite = build.idleSprite;
            AttackFrames = build.attackFrames;
            ProjectileSprite = build.projectileSprite;
            DisplayName = build.displayName;
            MoveSpeed = build.moveSpeed;
            AttackRange = build.attackRange;
            AttackCooldown = build.attackCooldown;
            AttackSpeed = build.attackSpeed;
            ProjectileSpeed = build.projectileSpeed;
            UnitType = build.unitType;
            ShadowScale = build.shadowScale > 0f ? build.shadowScale : 1f;
            CurrentHp = MaxHp;
            if (build.shieldDuration > 0f) ApplyShield(build.shieldDuration);
            if (build.rallyDuration > 0f) ApplyRally(build.rallyBonus, build.rallyDuration);
        }

        public void ApplyAttackMultiplier(float multiplier)
        {
            Attack = Mathf.Max(0, Mathf.RoundToInt(Attack * multiplier));
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

        public void TakeDamage(int damage)
        {
            // While the Fortify shield holds, the unit is fully immune.
            if (_shieldRemaining > 0f) return;

            CurrentHp -= damage;
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
