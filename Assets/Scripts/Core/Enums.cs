namespace DraftCards.Core
{
    public enum CardType
    {
        Unit,
        Support
    }

    public enum FormationLine
    {
        Back,
        Middle,
        Front
    }

    public enum SupportEffectType
    {
        MultiplyUnitCount,
        AddAttackPercent,
        AddAttackFlat,
        AddHpPercent,
        AddHpFlat,
        ChangeLine,
        DuplicateAllPlayerUnits,
        StrengthenAllPlayerUnits,
        ShieldFrontLine,
        RallyAllPlayerUnits,
        ReviveFirstDead,
        LightningStrikePriorityEnemy,
        MarkEnemyLine,
        ShieldPlayerLine,
        DuplicatePlayerLineLimited,
        DrawTemporarySpellCards,
        HoldSpellForNextTurn,
        IncreaseMaxMpNextTurn,
        DamageEnemyLine,
        SlowEnemyOpeningLines,
        ReduceDamageFrontLine,
        RallyPlayerLine,
        LightningStrikePriorityEnemies,
        EmergencyDraftUnits,
        UpgradeUnit,
        MeteorEnemyLine
    }

    public enum UnitType
    {
        Ground,
        Flying,
        // Mounted skirmisher: deployed behind the front line but still pushes forward, and
        // its targeting ignores enemy Front-line units, diving for the Middle/Back instead
        // (see BattleUnit.FindClosestOpponent target filtering / BattlefieldView).
        Cavalry
    }

    public enum GameState
    {
        DrawPhase,
        SelectCardPhase,
        PreviewPhase,
        ResolvePhase,
        EnemyPhase,
        Victory,
        Defeat
    }
}
