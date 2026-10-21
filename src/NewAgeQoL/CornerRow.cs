using UnityEngine;

namespace NewAgeQoL
{
    internal static class CornerRow
    {
        internal const float Round = 56f;
        internal const float Margin = 8f;

        internal static Vector2 Spot(int index) => new Vector2(-(Margin + index * (Round + Margin)), Margin);

        internal static int Before(int slot)
        {
            int n = 0;
            if (slot > 0 && Relog.Shown) n++;
            if (slot > 2 && CombatMusic.ButtonShown) n++;
            return n;
        }

        public static int Taken => Before(3);
    }
}
