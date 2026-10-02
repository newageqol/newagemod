using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace NewAgeQoL
{
    internal static class FlashLook
    {
        internal static ConfigEntry<bool> Entry(string section, string key)
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue("newage.2d", out var info) || info.Instance == null) return null;
                return info.Instance.Config.TryGetEntry<bool>(new ConfigDefinition(section, key), out var entry) ? entry : null;
            }
            catch { return null; }
        }

        private static ConfigEntry<bool> _look;
        private static ConfigEntry<bool> _combat;
        private static bool _entriesLooked;

        internal static bool Fight
        {
            get
            {
                try
                {
                    if (!Chainloader.PluginInfos.TryGetValue("newage.2d", out var info) || info.Instance == null)
                    {
                        _entriesLooked = false;
                        _look = null;
                        _combat = null;
                        return false;
                    }
                    if (!_entriesLooked)
                    {
                        _entriesLooked = true;
                        _look = Entry("General", "Enabled");
                        _combat = Entry("Combat", "Enabled");
                    }
                    return _look != null && _look.Value && (_combat == null || _combat.Value);
                }
                catch { return false; }
            }
        }

        private static System.Action<int> _hint;
        private static bool _hintLooked;
        private static int _hinted;

        internal static void Hint(int mode)
        {
            if (mode == _hinted) return;
            try
            {
                if (!_hintLooked)
                {
                    _hintLooked = true;
                    if (Chainloader.PluginInfos.TryGetValue("newage.2d", out var info) && info.Instance != null)
                    {
                        var method = info.Instance.GetType().Assembly.GetType("NewAge2D.Glow")?.GetMethod("Hint",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                            null, new[] { typeof(int) }, null);
                        if (method != null) _hint = (System.Action<int>)System.Delegate.CreateDelegate(typeof(System.Action<int>), method);
                    }
                }
                _hinted = mode;
                _hint?.Invoke(mode);
            }
            catch { }
        }

        private static System.Func<AbstractCharacter> _under;
        private static bool _underLooked;

        internal static bool Under(out AbstractCharacter body)
        {
            body = null;
            try
            {
                if (!Fight) return false;
                if (!_underLooked)
                {
                    _underLooked = true;
                    if (Chainloader.PluginInfos.TryGetValue("newage.2d", out var info) && info.Instance != null)
                    {
                        var method = info.Instance.GetType().Assembly.GetType("NewAge2D.BodyClick")?.GetMethod("Body",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                            null, System.Type.EmptyTypes, null);
                        if (method != null && method.ReturnType == typeof(AbstractCharacter))
                            _under = (System.Func<AbstractCharacter>)System.Delegate.CreateDelegate(typeof(System.Func<AbstractCharacter>), method);
                    }
                    Plugin.Trace("[fighter] Flash view hover " + (_under != null ? "taken from the doll, dead ones too" : "not found on the doll, using capsule"));
                }
                if (_under == null) return false;
                body = _under();
                return true;
            }
            catch { return false; }
        }

        internal static void AskSwitch()
        {
            var look = Entry("General", "Enabled");
            if (look == null) return;
            bool toFlash = !look.Value;
            string text = (toFlash ? "Включить вид как во Flash?" : "Вернуть 3D-вид?")
                + " Вид меняется только после перезапуска игры: клиент сейчас закроется, запусти его снова.";
            try
            {
                DialogFactory.ShowConfirmMessageBox("dialogs.artworkshop.confirm.caption", result =>
                {
                    if (result != EMessageBoxResult.MB_OK) return;
                    look.Value = toFlash;
                    Plugin.Log?.LogInfo("[look] " + (toFlash ? "Flash" : "3D") + " view from the next game start, closing the client");
                    Quit();
                }, text);
            }
            catch (System.Exception e) { Plugin.Log?.LogWarning("[look] switch dialog: " + e.Message); }
        }

        internal static void Quit()
        {
            try
            {
                var session = DependencyContainer.GetContainer()?.Resolve<SessionController>();
                if (session != null) { session.ApplicationExit(); return; }
            }
            catch (System.Exception e) { Plugin.Log?.LogWarning("[look] leaving the game: " + e.Message); }
            UnityEngine.Application.Quit();
        }

        internal static string ResetCache()
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue("newage.2d", out var info) || info.Instance == null) return null;
                var type = info.Instance.GetType().Assembly.GetType("NewAge2D.CacheReset");
                var run = type?.GetMethod("Run", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return run?.Invoke(null, null) as string;
            }
            catch { return null; }
        }
    }
}
