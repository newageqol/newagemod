using BepInEx.Bootstrap;

namespace NewAgeQoL
{
    internal static class FlashLook
    {
        internal static bool Fight
        {
            get
            {
                try { return Chainloader.PluginInfos.TryGetValue("newage.2d", out var info) && info.Instance != null; }
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
