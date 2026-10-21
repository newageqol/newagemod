using System;
using System.Reflection;

namespace NewAge2D;

internal static class ModMusic
{
    private static Action<AnimationItem, AbstractCharacter> _shown;
    private static bool _looked;

    internal static void Shown(AnimationItem item, AbstractCharacter by)
    {
        if (!_looked)
        {
            _looked = true;
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType("NewAgeQoL.CombatMusic", false);
                    if (type == null) continue;
                    var method = type.GetMethod("Shown", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                        null, new[] { typeof(AnimationItem), typeof(AbstractCharacter) }, null);
                    if (method != null) _shown = (Action<AnimationItem, AbstractCharacter>)Delegate.CreateDelegate(typeof(Action<AnimationItem, AbstractCharacter>), method);
                    break;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[music] mod not found: " + ex.Message); }
        }
        if (_shown == null) return;
        try { _shown(item, by); }
        catch (Exception ex) { Plugin.Log.LogWarning("[music] " + ex.Message); }
    }
}
