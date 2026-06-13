using System;
using DraftCards.Core;

namespace DraftCards.Data
{
    [Serializable]
    public class SupportEffectData
    {
        public SupportEffectType effectType;
        public float value;
        // Optional second parameter for effects that need two numbers. For
        // RallyAllPlayerUnits, value = speed bonus (0.4 = +40%) and value2 =
        // how many seconds of combat the rally lasts. Unused by other effects.
        public float value2;
        public float value3;
    }
}
