using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Blink
    {
        private const uint Tray = 2;
        private const uint UntilFront = 12;
        private const uint Add = 0;
        private const uint Modify = 1;
        private const uint Delete = 2;
        private const uint HasIcon = 2;
        private const uint HasTip = 4;
        private const uint HasInfo = 16;
        private const uint OwnIcon = 4;
        private const uint BigIcon = 32;
        private const int Longest = 255;

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public IntPtr Window;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrayData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id;
            public uint Flags;
            public uint CallbackMessage;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State;
            public uint StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public uint InfoFlags;
            public Guid Item;
            public IntPtr BalloonIcon;
        }

        private delegate bool EachWindow(IntPtr window, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FlashInfo info);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EachWindow each, IntPtr param);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int room);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint message, ref TrayData data);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractIcon(IntPtr instance, string file, int index);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex Levels = new Regex(@"(\d{1,3})\s*[-–—]\s*(\d{1,3})", RegexOptions.Compiled);

        private static IntPtr _window;
        private static IntPtr _icon;
        private static bool _trayOn;
        private static bool _trayBroken;
        private static bool _wasCombat;
        private static float _at;
        private static float _blinkAt;
        private static float _fightAt;
        private static object _chatOn;
        private static bool _broken;

        private static bool Enabled => Plugin.CfgTaskbarBlink == null || Plugin.CfgTaskbarBlink.Value;

        private static bool Toasts => Plugin.CfgDesktopToasts == null || Plugin.CfgDesktopToasts.Value;

        private static bool Windows =>
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        internal static void Tick()
        {
            if (_broken || !Windows) return;
            try
            {
                if (Application.isFocused && _window == IntPtr.Zero)
                {
                    var active = GetActiveWindow();
                    if (active != IntPtr.Zero) _window = active;
                }
                Listen();
                if (Time.unscaledTime < _at) return;
                _at = Time.unscaledTime + 0.2f;
                bool combat = SideButtons.InCombat();
                if (combat && !_wasCombat) _fightAt = Time.unscaledTime;
                _wasCombat = combat;
                if (!combat) _fightAt = 0f;
                if (_fightAt > 0f) Mine();
            }
            catch (Exception e) { _broken = true; Plugin.Warn("[blink] off: " + e.Message); }
        }

        internal static void Shutdown()
        {
            if (_trayOn)
            {
                try
                {
                    var data = Data(0);
                    Shell_NotifyIcon(Delete, ref data);
                }
                catch { }
                _trayOn = false;
            }
            if (_post != IntPtr.Zero)
            {
                try { DestroyWindow(_post); } catch { }
                _post = IntPtr.Zero;
            }
        }

        private const uint Callback = 0x8041;
        private const uint HasMessage = 1;
        private const uint BalloonClick = 0x405;
        private const uint LeftUp = 0x202;

        private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wide, IntPtr wide2);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Size;
            public uint Style;
            public IntPtr Proc;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            public string Menu;
            public string Name;
            public IntPtr SmallIcon;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WindowClass type);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string type, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wide, IntPtr wide2);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int how);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        private static readonly WindowProc Receiver = Receive;
        private static IntPtr _post;

        private static IntPtr Post()
        {
            if (_post != IntPtr.Zero) return _post;
            var instance = GetModuleHandle(null);
            const string name = "NewAgeQoLTray";
            var type = new WindowClass
            {
                Size = (uint)Marshal.SizeOf(typeof(WindowClass)),
                Proc = Marshal.GetFunctionPointerForDelegate(Receiver),
                Instance = instance,
                Name = name
            };
            RegisterClassEx(ref type);
            _post = CreateWindowEx(0, name, "New Age QoL", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (_post == IntPtr.Zero) Plugin.Warn("[blink] click receiver window was not created, error " + Marshal.GetLastWin32Error());
            return _post;
        }

        private static IntPtr Receive(IntPtr window, uint message, IntPtr wide, IntPtr wide2)
        {
            try
            {
                if (message == Callback)
                {
                    uint what = (uint)(wide2.ToInt64() & 0xFFFF);
                    if (what == BalloonClick || what == LeftUp) Raise();
                    return IntPtr.Zero;
                }
            }
            catch { }
            return DefWindowProc(window, message, wide, wide2);
        }

        private static void Raise()
        {
            var game = Find();
            if (game == IntPtr.Zero) return;
            if (IsIconic(game)) ShowWindow(game, 9);
            else ShowWindow(game, 5);
            SetForegroundWindow(game);
            Plugin.Trace("[blink] game window raised from the notification");
        }

        private static void Mine()
        {
            var cd = FighterHint.Cd();
            bool known = cd != null && cd.Characters != null && cd.Characters.Count > 0;
            if (known && cd.MyCharacter != null)
            {
                _fightAt = 0f;
                Ping("own fight", Lang.T("Начался бой"), Lang.T("Ты в бою, пора ходить"));
                return;
            }
            if (known || Time.unscaledTime - _fightAt > 10f) _fightAt = 0f;
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _chatOn = null; return; }
            if (ReferenceEquals(_chatOn, nc)) return;
            nc.RemoveMessageListener(ResponseConsts.CHAT_RESPONCE, OnChat);
            nc.AddMessageListener(ResponseConsts.CHAT_RESPONCE, OnChat);
            _chatOn = nc;
        }

        private static void OnChat(object m)
        {
            try
            {
                var msg = m as Transport.Messages.Responses.Chat.ChatResponseMessage;
                if (msg == null) return;
                if (Application.isFocused) return;
                if (Stale(msg.Time)) return;
                var kind = (EChatMessageType)msg.Type;
                string text = Plain(msg.Text);
                if (kind == EChatMessageType.MSG_ADMIN || kind == EChatMessageType.MSG_INFO)
                {
                    if (Event(kind, text)) Ping("event", kind == EChatMessageType.MSG_ADMIN ? Lang.T("Объявление") : Lang.T("Приглашение"), text);
                    return;
                }
                if (kind != EChatMessageType.MSG_PRIVATE && kind != EChatMessageType.MSG_COMMON && kind != EChatMessageType.MSG_CLAN
                    && kind != EChatMessageType.MSG_ALLIANCE && kind != EChatMessageType.MSG_TEAM && kind != EChatMessageType.MSG_TROOP) return;
                try { if (GameSettings.Instance != null && GameSettings.Instance.IsIgnored(msg.Sender)) return; } catch { }
                int me = 0;
                try { me = Controllers.User.UserInfo.UserId; } catch { }
                if (me > 0 && msg.SenderId == me) return;
                string who = string.IsNullOrEmpty(msg.Sender) ? "?" : msg.Sender;
                if (kind == EChatMessageType.MSG_PRIVATE)
                {
                    if (msg.ReceiverId.HasValue && me > 0 && msg.ReceiverId.Value != me) return;
                    Ping("private", Lang.F("Личное от {0}", who), text);
                    return;
                }
                bool toMe = me > 0 && msg.ReceiverId.HasValue && msg.ReceiverId.Value == me;
                if (!toMe) toMe = Named(text);
                if (toMe) Ping("addressed in " + kind, Lang.F("{0} — {1}", who, Room(kind)), text);
            }
            catch (Exception e) { Plugin.Caught("[blink] chat: " + e.Message); }
        }

        private static string Room(EChatMessageType kind)
        {
            switch (kind)
            {
                case EChatMessageType.MSG_CLAN: return Lang.T("клан-чат");
                case EChatMessageType.MSG_ALLIANCE: return Lang.T("альянс-чат");
                case EChatMessageType.MSG_TEAM: return Lang.T("чат команды");
                case EChatMessageType.MSG_TROOP: return Lang.T("чат отряда");
                default: return Lang.T("общий чат");
            }
        }

        private static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return Tags.Replace(text, "").Trim();
        }

        private static bool Stale(string time)
        {
            if (string.IsNullOrEmpty(time)) return false;
            var parts = time.Trim().Split(':');
            int h, mi;
            if (parts.Length < 2 || !int.TryParse(parts[0], out h) || !int.TryParse(parts[1], out mi)) return false;
            int then = h * 60 + mi;
            var utc = DateTime.UtcNow;
            return Gap(utc.AddHours(2), then) > 3 && Gap(utc.AddHours(3), then) > 3;
        }

        private static int Gap(DateTime clock, int then)
        {
            int gap = Math.Abs(clock.Hour * 60 + clock.Minute - then);
            return Math.Min(gap, 1440 - gap);
        }

        private static bool Event(EChatMessageType kind, string text)
        {
            if (text.Length == 0) return false;
            if (text.IndexOf("турнир", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (text.IndexOf("Ваш друг", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (text.IndexOf("хаотический бой", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (text.IndexOf("получил Контракт", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (text.IndexOf("получил Договор", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            bool invite = text.StartsWith("Приглашаем", StringComparison.OrdinalIgnoreCase);
            if (invite) return Fits(text);
            return kind == EChatMessageType.MSG_ADMIN;
        }

        private static bool Fits(string text)
        {
            int level = 0;
            try { level = Controllers.User.UserInfo.Level; } catch { }
            if (level <= 0) return true;
            var range = Levels.Match(text);
            if (!range.Success) return true;
            int low = int.Parse(range.Groups[1].Value);
            int high = int.Parse(range.Groups[2].Value);
            if (low > high) { int t = low; low = high; high = t; }
            return level >= low && level <= high;
        }

        private static bool Named(string text)
        {
            string login = ChatHighlight.Login();
            if (string.IsNullOrEmpty(login) || login.Length < 3 || text.Length == 0) return false;
            int at = text.IndexOf(login, StringComparison.OrdinalIgnoreCase);
            while (at >= 0)
            {
                int end = at + login.Length;
                bool left = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                bool right = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (left && right) return true;
                at = text.IndexOf(login, end, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static void Ping(string why, string title, string body)
        {
            if (_broken || !Windows) return;
            if (Application.isFocused) return;
            var window = Find();
            if (window == IntPtr.Zero) { Plugin.Trace("[blink] game window not found"); return; }
            if (Enabled && Time.unscaledTime >= _blinkAt)
            {
                _blinkAt = Time.unscaledTime + 1f;
                try
                {
                    var info = new FlashInfo
                    {
                        Size = (uint)Marshal.SizeOf(typeof(FlashInfo)),
                        Window = window,
                        Flags = Tray | UntilFront,
                        Count = 0,
                        Timeout = 0
                    };
                    FlashWindowEx(ref info);
                    Plugin.Trace("[blink] taskbar icon blinks: " + why);
                }
                catch (Exception e) { _broken = true; Plugin.Warn("[blink] off: " + e.Message); return; }
            }
            if (Toasts) Toast(title, body);
        }

        internal static void Notify(string title, string body)
        {
            if (_broken || _trayBroken || !Windows || !Toasts) return;
            Toast(title, body);
        }

        private static TrayData Data(uint flags)
        {
            return new TrayData
            {
                Size = (uint)Marshal.SizeOf(typeof(TrayData)),
                Window = _post != IntPtr.Zero ? _post : Find(),
                Id = 0x4E41,
                CallbackMessage = Callback,
                Flags = flags,
                Icon = _icon,
                Tip = "New Age",
                Info = "",
                InfoTitle = "",
                BalloonIcon = _icon
            };
        }

        private static void Toast(string title, string body)
        {
            if (_trayBroken) return;
            try
            {
                if (!_trayOn)
                {
                    if (_icon == IntPtr.Zero)
                    {
                        string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        _icon = ExtractIcon(IntPtr.Zero, exe, 0);
                        if (_icon == IntPtr.Zero || _icon == (IntPtr)1) _icon = LoadIcon(IntPtr.Zero, (IntPtr)32512);
                    }
                    Post();
                    var add = Data(HasIcon | HasTip | (_post != IntPtr.Zero ? HasMessage : 0));
                    if (!Shell_NotifyIcon(Add, ref add)) { _trayBroken = true; Plugin.Warn("[blink] tray icon was not added, no desktop notifications"); return; }
                    _trayOn = true;
                }
                var data = Data(HasInfo);
                data.InfoTitle = Cut(string.IsNullOrEmpty(title) ? "New Age" : title, 63);
                data.Info = Cut(string.IsNullOrEmpty(body) ? " " : body, Longest);
                data.InfoFlags = OwnIcon | BigIcon;
                Shell_NotifyIcon(Modify, ref data);
            }
            catch (Exception e) { _trayBroken = true; Plugin.Warn("[blink] desktop notification: " + e.Message); }
        }

        private static string Cut(string text, int room)
        {
            if (text.Length <= room) return text;
            return text.Substring(0, room - 1) + "…";
        }

        private static IntPtr Find()
        {
            if (_window != IntPtr.Zero) return _window;
            uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            var name = new StringBuilder(64);
            EnumWindows((w, p) =>
            {
                uint owner;
                GetWindowThreadProcessId(w, out owner);
                if (owner != me || !IsWindowVisible(w)) return true;
                name.Length = 0;
                GetClassName(w, name, name.Capacity);
                if (name.ToString() != "UnityWndClass") return true;
                found = w;
                return false;
            }, IntPtr.Zero);
            _window = found;
            return found;
        }
    }
}
