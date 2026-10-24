using System;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Pointer
    {
        private static readonly Vector2 Tip = new Vector2(1f, 1f);

        private static int _scale;

        internal static bool Drawn => Icons.Flash("cursor32") != null;

        internal static void Tick()
        {
            int scale = Screen.height > 1500 ? 2 : 1;
            if (scale == _scale) return;
            bool first = _scale == 0;
            _scale = scale;
            if (first || (!SkillList.Armed && !BodyClick.Shown)) Plain();
        }

        internal static void Plain()
        {
            try
            {
                if (_scale == 0) _scale = Screen.height > 1500 ? 2 : 1;
                var art = Icons.Flash(_scale == 2 ? "cursor64" : "cursor32");
                if (art == null) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
                Cursor.SetCursor(art.texture, Tip * _scale, CursorMode.Auto);
            }
            catch (Exception e) { Plugin.Trace("[pointer] " + e.Message); }
        }
    }
}
