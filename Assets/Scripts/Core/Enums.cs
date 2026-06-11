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
        ReviveFirstDead
    }

    public enum UnitType
    {
        Ground,
        Flying
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
