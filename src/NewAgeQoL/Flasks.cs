using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Transport.Messages.Responses.Things.Actions;
using Transport.Messages.Responses.Things.Thinginfo.Generalinfo;
using Transport.Messages.Responses.Things.Thingtabs;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Flasks
    {
        internal const int Rows = 4;

        private const int WinInventory = -104;
        private const int AllTab = 18;
        private const int BtnUse = 0x20;
        private const float CacheAge = 20f;
        private const float RescanEvery = 180f;
        private const float TickEvery = 0.2f;
        private const int FillLimit = 50;
        private const int NameLimit = 60;
        private const int Batch = 20;
        private const float Pause = 0.15f;

        private static readonly string[] Titles = { "Жизнь", "Мана", "Энергия", "Грибы" };

        internal const int Shrooms = 3;

        private static readonly int[][] Kin =
        {
            new[] { 426, 427, 428 },
            new[] { 429, 430, 431 },
            new[] { 16501, 16502, 16503 },
            new[] { 16852, 16853, 16854, 16856, 16857, 16858 },
        };

        private struct Stack
        {
            internal int Inv, Tab, ThingId, Qty, SubType;
        }

        private static readonly List<Stack> Scanned = new List<Stack>();
        private static readonly HashSet<int> ScannedInv = new HashSet<int>();
        private static readonly HashSet<int> GotTabs = new HashSet<int>();
        private static readonly Dictionary<int, (bool Ok, string Err, int Left)> Ctx = new Dictionary<int, (bool, string, int)>();
        private static readonly Dictionary<int, string> Images = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> Sharp = new Dictionary<string, Sprite>();
        private static readonly Dictionary<int, int[]> Facts = new Dictionary<int, int[]>();
        private static readonly Dictionary<int, int> Uses = new Dictionary<int, int>();
        private static readonly HashSet<int> AskedInfo = new HashSet<int>();
        private static readonly HashSet<string> Loading = new HashSet<string>();

        private static readonly int[] Left = { -1, -1, -1, -1 };
        private static readonly int[] Resolved = new int[Rows];
        private static readonly bool[] Checked = new bool[Rows];
        private static readonly int[] WatchedId = new int[Rows];
        private static readonly string[] WatchedWish = new string[Rows];
        private static readonly bool[] RowBusy = new bool[Rows];
        private static readonly float[] RowSince = new float[Rows];
        private static readonly int[] RowThing = new int[Rows];
        private static readonly string[] RowMsg = new string[Rows];

        private static string _scene = "";
        private static float _nextTick;
        private static List<int> _tabs;
        private static int _seen;
        private static bool _listeners, _scanBusy, _scanOk, _scanFull, _wasCombat, _urgent;
        private static float _scanAt = -999f, _nextScan;
        private static float _busySince, _whyAt;

        internal static string Status = "";
        internal static float StatusAt;
        internal static int StatusRow = -1;


        internal static bool AnyBusy
        {
            get
            {
                for (int row = 0; row < Rows; row++) if (RowBusy[row]) return true;
                return false;
            }
        }

        private static ConfigEntry<int> Cfg(int row)
        {
            switch (row)
            {
                case 0: return Plugin.CfgFlaskHpId;
                case 1: return Plugin.CfgFlaskManaId;
                case 2: return Plugin.CfgFlaskEnergyId;
                case 3: return Plugin.CfgFlaskMushroomId;
            }
            return null;
        }

        internal static int Id(int row)
        {
            var cfg = row >= 0 && row < Rows ? Cfg(row) : null;
            return cfg != null ? cfg.Value : 0;
        }

        internal static int RowFor(int thingId)
        {
            if (thingId <= 0) return -1;
            for (int row = 0; row < Kin.Length; row++)
                if (System.Array.IndexOf(Kin[row], thingId) >= 0) return row;
            return -1;
        }

        internal static int RowOf(ConfigEntry<int> entry)
        {
            if (entry == null) return -1;
            for (int row = 0; row < Rows; row++)
            {
                var mine = Cfg(row);
                if (mine == null) continue;
                if (ReferenceEquals(mine, entry)) return row;
                if (mine.Definition.Section == entry.Definition.Section && mine.Definition.Key == entry.Definition.Key) return row;
            }
            return -1;
        }

        private static ConfigEntry<string> CfgName(int row)
        {
            switch (row)
            {
                case 0: return Plugin.CfgFlaskHpName;
                case 1: return Plugin.CfgFlaskManaName;
                case 2: return Plugin.CfgFlaskEnergyName;
                case 3: return Plugin.CfgFlaskMushroomName;
            }
            return null;
        }

        private static string Wish(int row)
        {
            var cfg = row >= 0 && row < Rows ? CfgName(row) : null;
            return cfg != null ? (cfg.Value ?? "").Trim() : "";
        }

        internal static int Thing(int row)
        {
            if (row < 0 || row >= Rows) return 0;
            int id = Id(row);
            return id > 0 ? id : Resolved[row];
        }

        internal static bool Shown(int row) => Id(row) > 0 || Wish(row).Length > 0;

        internal static bool Absent(int row) => !Shown(row) || (Thing(row) > 0 ? Left[row] == 0 : Checked[row]);

        internal static bool Busy(int row) => row >= 0 && row < Rows && RowBusy[row];

        private static readonly int[] BadgeOf = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        private static readonly string[] BadgeText = new string[Rows];

        internal static string Badge(int row)
        {
            if (!Shown(row)) return "";
            int left = Thing(row) <= 0 ? -1 : Left[row];
            if (BadgeOf[row] != left)
            {
                BadgeOf[row] = left;
                BadgeText[row] = left < 0 ? "?" : left.ToString();
            }
            return BadgeText[row];
        }

        internal static string Hint(int row)
        {
            if (!Shown(row)) return "";
            if (RowBusy[row]) return Titles[row] + ": " + (RowMsg[row] ?? "…");

            int id = Thing(row);
            if (id <= 0) return Titles[row] + ": «" + Wish(row) + "» в сумке не найдено";

            string name = NameOf(id);
            string head = !string.IsNullOrEmpty(name) ? name : Titles[row];
            bool wantFill = Plugin.CfgFlaskFillToMax == null || Plugin.CfgFlaskFillToMax.Value;
            return head + " — " + (wantFill ? "до полного" : "не до полного") + Hotkeys.Tail("flask:" + row);
        }

        internal static Sprite Icon(int row) => IconFor(Thing(row));

        internal static Sprite Real(int row)
        {
            int id = Thing(row);
            if (id <= 0) return null;
            string image;
            lock (Images) image = Images.TryGetValue(id, out var img) ? img : null;
            if (string.IsNullOrEmpty(image)) return null;
            if (Sharp.TryGetValue(image, out var crisp) && crisp != null && crisp.texture != null) return crisp;
            if (Icons.TryGetValue(image, out var known) && known != null && known.texture != null) return known;
            return null;
        }

        internal static Sprite IconFor(int id)
        {
            var exact = Exact(id);
            return exact != null ? exact : Fallback();
        }

        internal static Sprite Exact(int id)
        {
            if (id <= 0) return null;

            string image;
            lock (Images) image = Images.TryGetValue(id, out var img) ? img : null;
            if (string.IsNullOrEmpty(image)) { AskInfo(id); return null; }

            if (Sharp.TryGetValue(image, out var crisp))
            {
                if (crisp != null && crisp.texture != null) return crisp;
                Sharp.Remove(image);
                Loading.Remove(image);
            }
            LoadRemote(image);
            if (Icons.TryGetValue(image, out var known))
            {
                if (known != null && known.texture != null) return known;
                Icons.Remove(image);
            }
            try
            {
                var s = AtlasUtils.GetThingSprite(image);
                if (s != null && s.name != "unknown") { Icons[image] = s; return s; }
            }
            catch { }
            return null;
        }

        internal static string ImageOf(int thingId)
        {
            lock (Images) return Images.TryGetValue(thingId, out var img) && !string.IsNullOrEmpty(img) ? img : null;
        }

        internal static int RarityOf(int thingId)
        {
            lock (Facts) return Facts.TryGetValue(thingId, out var f) ? f[1] : 0;
        }

        internal static int LevelOf(int thingId)
        {
            lock (Facts) return Facts.TryGetValue(thingId, out var f) ? f[2] : 0;
        }

        internal static bool CanWear(int thingId)
        {
            int mask;
            lock (Uses) return Uses.TryGetValue(thingId, out mask) && mask == 0;
        }

        internal static void NoteUse(int thingId, int canUse)
        {
            if (thingId <= 0) return;
            lock (Uses) Uses[thingId] = canUse;
        }

        internal static bool UseKnown(int thingId)
        {
            lock (Uses) return Uses.ContainsKey(thingId);
        }

        internal static void Note(int thingId, string image, int subType, int rarity, int level)
        {
            if (thingId <= 0) return;
            if (!string.IsNullOrEmpty(image)) lock (Images) Images[thingId] = image;
            lock (Facts)
            {
                if (!Facts.TryGetValue(thingId, out var f) || f == null) f = new int[4];
                if (subType > 0) f[0] = subType;
                if (rarity > 0) f[1] = rarity;
                if (level > 0) f[2] = level;
                Facts[thingId] = f;
            }
        }

        internal static void AskName(int thingId)
        {
            if (thingId <= 0 || NameOf(thingId) != null) return;
            AskInfo(thingId);
        }

        internal static List<int> BagThings()
        {
            var seen = new HashSet<int>();
            var list = new List<int>();
            lock (Scanned)
                foreach (var one in Scanned)
                    if (one.Qty > 0 && seen.Add(one.ThingId))
                        list.Add(one.ThingId);
            return list;
        }

        internal static bool Scanning => _scanBusy;

        internal static bool Picking;

        private static bool Fresh
        {
            get { lock (Scanned) return Scanned.Count > 0; }
        }

        internal static string State()
        {
            int stacks;
            lock (Scanned) stacks = Scanned.Count;
            return "опрос идёт " + _scanBusy + ", ряд занят " + AnyBusy
                 + ", до опроса " + Mathf.Max(0f, _nextScan - RealTime.Now).ToString("0.0")
                 + " с, стопок в памяти " + stacks;
        }

        internal static int Consumables(List<int> ids, Dictionary<int, int> subtypes, Dictionary<int, int> quantities)
        {
            if (ids == null || subtypes == null || quantities == null) return 0;
            ids.Clear();
            subtypes.Clear();
            quantities.Clear();
            int left = 0;
            lock (Scanned)
                for (int i = 0; i < Scanned.Count; i++)
                {
                    var s = Scanned[i];
                    if (!Consumable(s.SubType) || s.Qty <= 0) continue;
                    int had;
                    if (quantities.TryGetValue(s.ThingId, out had)) { quantities[s.ThingId] = had + s.Qty; continue; }
                    ids.Add(s.ThingId);
                    quantities[s.ThingId] = s.Qty;
                    subtypes[s.ThingId] = s.SubType;
                    if (NameOf(s.ThingId) == null) left++;
                }
            return left;
        }

        internal static int SubTypeOf(int thingId)
        {
            lock (Scanned) foreach (var s in Scanned) if (s.ThingId == thingId) return s.SubType;
            lock (Facts) if (Facts.TryGetValue(thingId, out var f) && f[0] > 0) return f[0];
            return 0;
        }

        internal static bool Known(int thingId)
        {
            lock (Facts) return Facts.ContainsKey(thingId);
        }

        internal static int QtyOf(int thingId)
        {
            int q = 0;
            lock (Scanned) foreach (var s in Scanned) if (s.ThingId == thingId) q += s.Qty;
            return q;
        }

        internal static string DisplayName(int thingId)
        {
            string n = NameOf(thingId);
            return string.IsNullOrEmpty(n) ? null : n;
        }

        internal static void RequestScan()
        {
            _nextScan = 0f;
        }

        internal static void RequestScanNow()
        {
            _urgent = true;
            _nextScan = 0f;
        }

        internal static void RetryNames(int cap)
        {
            var ids = new List<int>();
            var seen = new HashSet<int>();
            lock (Scanned)
                foreach (var s in Scanned)
                    if (Consumable(s.SubType) && s.Qty > 0 && seen.Add(s.ThingId) && NameOf(s.ThingId) == null)
                        ids.Add(s.ThingId);
            if (ids.Count > cap) ids.RemoveRange(cap, ids.Count - cap);
            lock (AskedInfo) foreach (int id in ids) AskedInfo.Remove(id);
            foreach (int id in ids) AskInfo(id);
        }

        internal static void RequestNames()
        {
            List<int> ids;
            lock (Scanned)
            {
                ids = new List<int>();
                foreach (var s in Scanned)
                    if (Consumable(s.SubType) && s.Qty > 0 && NameOf(s.ThingId) == null)
                        ids.Add(s.ThingId);
            }
            foreach (int id in ids) AskInfo(id);
        }

        private static Sprite Fallback()
        {
            try { return AtlasUtils.GetThingTabImage(EThingTabType.NON_COMBAT_USED_TAB); }
            catch { return null; }
        }

        private static void LoadRemote(string image)
        {
            if (string.IsNullOrEmpty(image) || !Loading.Add(image)) return;
            try
            {
                RemoteImageLoader.Instance.Load(
                    "https://files.nura.biz/site/images/things100x100/" + image + ".png",
                    sprite => { if (sprite != null) Sharp[image] = sprite; },
                    error => Plugin.Trace("[flasks] картинка «" + image + "» не загрузилась: " + error));
            }
            catch { }
        }

        private static void AskInfo(int thingId)
        {
            if (thingId <= 0) return;
            lock (AskedInfo) if (AskedInfo.Contains(thingId)) return;
            if (!EnsureListeners()) return;
            if (!Send(new GeneralThingHintRequest(thingId))) return;
            lock (AskedInfo) AskedInfo.Add(thingId);
        }

        private static void Learn(int thingId, string image, string name)
        {
            if (thingId <= 0) return;
            if (!string.IsNullOrEmpty(image)) lock (Images) Images[thingId] = image;
            if (!string.IsNullOrEmpty(name)) lock (Names) Names[thingId] = name;
        }

        private static string NameOf(int thingId)
        {
            lock (Names) return Names.TryGetValue(thingId, out var n) && !string.IsNullOrEmpty(n) ? n : null;
        }

        private static bool Consumable(int subType) => subType == 16 || subType == 18 || subType == 54;

        private static void ResolveWishes()
        {
            for (int row = 0; row < Rows; row++)
            {
                Checked[row] = _scanOk;
                int id = Id(row);
                if (id > 0) { Resolved[row] = id; continue; }
                string want = Wish(row);
                Resolved[row] = want.Length > 0 ? Kinned(row) : 0;
                if (Resolved[row] > 0) Settle(row, want);
            }
        }

        private static int Kinned(int row)
        {
            if (row < 0 || row >= Kin.Length) return 0;
            lock (Scanned)
                foreach (var s in Scanned)
                    if (System.Array.IndexOf(Kin[row], s.ThingId) >= 0) return s.ThingId;
            return 0;
        }

        private static void Settle(int row, string want)
        {
            var cfg = Cfg(row);
            var name = CfgName(row);
            int id = Resolved[row];
            if (cfg == null || name == null || id <= 0) return;
            try
            {
                cfg.Value = id;
                name.Value = "";
                WatchedId[row] = id;
                WatchedWish[row] = "";
                Plugin.Trace("[банки] " + Titles[row] + ": «" + want + "» теперь хранится как id " + id);
            }
            catch (System.Exception e) { Plugin.Trace("[банки] перевод названия в id: " + e.Message); }
        }

        private static int Find(string want)
        {
            int loose = 0;
            lock (Scanned)
                foreach (var s in Scanned)
                {
                    string name = NameOf(s.ThingId);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(name, want, System.StringComparison.OrdinalIgnoreCase)) return s.ThingId;
                    if (loose == 0 && name.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0) loose = s.ThingId;
                }
            return loose;
        }

        private static bool NeedNames()
        {
            for (int row = 0; row < Rows; row++)
                if (Id(row) <= 0 && Wish(row).Length > 0) return true;
            return false;
        }

        private static IEnumerator NameConsumables()
        {
            if (!NeedNames()) yield break;

            var need = new List<int>();
            lock (Scanned)
                foreach (var s in Scanned)
                    if (Consumable(s.SubType) && NameOf(s.ThingId) == null && !need.Contains(s.ThingId))
                        need.Add(s.ThingId);
            if (need.Count == 0) yield break;
            if (!EnsureListeners()) yield break;

            int asked = 0;
            foreach (int id in need)
            {
                if (asked++ >= NameLimit) break;
                if (!Send(new GeneralThingHintRequest(id))) yield break;
                yield return Wait(0.06f);
            }
            yield return Wait(0.4f);
        }

        internal static void DropIcons()
        {
            Icons.Clear();
            Loading.Clear();
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickEvery;

            string scene = SideButtons.Scene();
            if (scene != _scene)
            {
                _scene = scene;
                DropIcons();
            }

            bool combat = SideButtons.InCombat();
            if (_wasCombat && !combat) Forget();
            _wasCombat = combat;

            for (int row = 0; row < Rows; row++)
            {
                int id = Id(row);
                string wish = Wish(row);
                if (WatchedId[row] == id && WatchedWish[row] == wish) continue;
                WatchedId[row] = id;
                WatchedWish[row] = wish;
                Left[row] = -1;
                Resolved[row] = 0;
                Checked[row] = false;
                Forget();
            }

            if (Plugin.Instance == null || combat || !SideButtons.InWorld())
            {
                if (Picking && RealTime.Now >= _whyAt)
                {
                    _whyAt = RealTime.Now + 3f;
                    Plugin.Trace("[банки] опрос не начат: " + (Plugin.Instance == null ? "мод не готов"
                        : combat ? "идёт бой" : "игра считает, что я не в мире"));
                }
                return;
            }
            Purse();
            if (_chestAgain > 0f && RealTime.Now >= _chestAgain)
            {
                _chestAgain = 0f;
                RequestScanNow();
                Plugin.Trace("[банки] награда: повторный пересчёт остатков");
            }
            if (_scanBusy && RealTime.Now - _busySince > 15f)
            {
                _scanBusy = false;
                Plugin.Trace("[банки] прошлый опрос завис дольше 15 с, снимаю замок");
            }
            if (_scanBusy || AnyBusy || RealTime.Now < _nextScan)
            {
                if (Picking && RealTime.Now >= _whyAt)
                {
                    _whyAt = RealTime.Now + 3f;
                    Plugin.Trace("[банки] опрос отложен: " + (_scanBusy ? "предыдущий ещё идёт"
                        : AnyBusy ? "ряд занят" : "жду ещё " + (_nextScan - RealTime.Now).ToString("0.0") + " с"));
                }
                return;
            }

            bool any = Picking && !Fresh;
            for (int row = 0; row < Rows; row++) if (Shown(row)) any = true;
            if (!any)
            {
                if (Picking && !Fresh) Plugin.Trace("[банки] опрос пропущен: ни один ряд не настроен, а окно выбора не в счёт");
                return;
            }

            if (BagOpen() && !_urgent)
            {
                _nextScan = RealTime.Now + 5f;
                if (RealTime.Now >= _waited)
                {
                    _waited = RealTime.Now + 10f;
                    Plugin.Trace("[банки] сумка открыта: пересчёт остатков подождёт");
                }
                return;
            }

            _urgent = false;
            _nextScan = RealTime.Now + (_scanOk ? RescanEvery : 3f);
            Plugin.Trace("[банки] запускаю опрос сумки");
            Plugin.Instance.StartCoroutine(RefreshRoutine());
        }

        private static bool BagOpen()
        {
            try
            {
                foreach (var panel in Object.FindObjectsOfType<InventoryPanelContent>())
                {
                    if (panel == null || !panel.gameObject.activeInHierarchy) continue;
                    var rt = panel.transform as RectTransform;
                    if (rt == null || rt.rect.width < 4f || rt.rect.height < 4f) continue;
                    bool faded = false;
                    foreach (var group in panel.GetComponentsInParent<CanvasGroup>(false))
                        if (group != null && group.alpha < 0.05f) { faded = true; break; }
                    if (faded) continue;
                    var canvas = panel.GetComponentInParent<Canvas>();
                    if (canvas == null || !canvas.isActiveAndEnabled) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static float _purse = -1f;
        private static float _purseAt;
        private static float _waited;
        private static float _chestAgain;

        private static void Purse()
        {
            try
            {
                var ud = Controllers.User;
                if (ud == null) return;
                var cash = ud.Cash;
                float now = cash.Gold + cash.Talls;
                if (_purse < 0f) { _purse = now; return; }
                bool spent = now < _purse - 0.001f;
                _purse = now;
                if (!spent || RealTime.Now - _purseAt < 3f) return;
                _purseAt = RealTime.Now;
                RequestScanNow();
                Plugin.Trace("[банки] денег стало меньше: пересчитываю остатки");
            }
            catch { }
        }

        private static void Forget()
        {
            _scanOk = false;
            _scanAt = -999f;
            _nextScan = 0f;
            System.Array.Clear(Checked, 0, Rows);
        }

        private static IEnumerator RefreshRoutine()
        {
            if (!Connected())
            {
                Plugin.Trace("[банки] опрос: связи с сервером нет");
                yield break;
            }
            yield return Plugin.Instance.StartCoroutine(EnsureScan(0f));
            yield return Plugin.Instance.StartCoroutine(NameConsumables());
            ResolveWishes();
            UpdateCounts();
        }

        internal static void Use(int row)
        {
            if (Plugin.Instance == null || row < 0 || row >= Rows) return;
            if (RowBusy[row])
            {
                Tell(Titles[row] + ": нажатие пропущено — прошлое питьё ещё идёт " + (RealTime.Now - RowSince[row]).ToString("0.0") + " с");
                return;
            }
            if (!Shown(row))
            {
                Tell(Titles[row] + ": нажатие пропущено — банка для этой кнопки не выбрана в настройках");
                return;
            }
            Plugin.Instance.StartCoroutine(UseRoutine(row, -1, null));
        }

        private static void Tell(string text)
        {
            Plugin.Log?.LogInfo("[банки] " + text);
            DiskJournal.Flush();
        }

        internal static bool Drink(int row, int count, System.Action<int, bool> done)
        {
            if (Plugin.Instance == null || row < 0 || row >= Rows) return false;
            if (RowBusy[row] || !Shown(row)) return false;
            Plugin.Instance.StartCoroutine(UseRoutine(row, Mathf.Max(0, count), done));
            return true;
        }

        internal static int Remaining(int row) => row >= 0 && row < Rows && Thing(row) > 0 ? Left[row] : -1;

        internal static int Current(int row)
        {
            if (!Gauge(row, out int cur, out _)) return -1;
            return Mathf.Max(0, cur);
        }

        private static IEnumerator UseRoutine(int row, int want, System.Action<int, bool> done)
        {
            int thingId = Thing(row);
            string title = Titles[row];
            int used = 0;
            bool ranOut = false;
            if (thingId <= 0 && Wish(row).Length == 0) { done?.Invoke(0, false); yield break; }
            for (int i = 0; i < Rows; i++)
                if (i != row && RowBusy[i] && thingId > 0 && RowThing[i] == thingId)
                { Say(row, title + ": этот предмет уже использует соседняя кнопка"); done?.Invoke(0, false); yield break; }

            RowBusy[row] = true;
            RowSince[row] = RealTime.Now;
            RowThing[row] = thingId;
            var clock = new Clock { Began = RealTime.Now };
            bool told = false;
            bool counted = false;
            try
            {
                if (SideButtons.InCombat()) { Say(row, title + ": в бою эти банки не пьются"); yield break; }
                if (!Connected()) { Say(row, title + ": нет соединения"); yield break; }

                Step(row, "…");
                bool cached = RealTime.Now - _scanAt <= CacheAge;
                float scannedAt = _scanAt;
                yield return Plugin.Instance.StartCoroutine(EnsureScan(CacheAge));
                if (_scanAt != scannedAt) UpdateCounts();
                if (thingId <= 0)
                {
                    yield return Plugin.Instance.StartCoroutine(NameConsumables());
                    ResolveWishes();
                    thingId = Thing(row);
                    RowThing[row] = thingId;
                    if (thingId <= 0)
                    { Say(row, title + ": «" + Wish(row) + "» в сумке не найдено"); ranOut = true; yield break; }
                }
                var stacks = Stacks(thingId);

                if (stacks.Count == 0 && cached)
                {
                    cached = false;
                    Step(row, "смотрю сумку");
                    yield return Plugin.Instance.StartCoroutine(EnsureScan(0f));
                    stacks = Stacks(thingId);
                    UpdateCounts();
                }
                if (stacks.Count == 0)
                {
                    Say(row, _seen == 0 && !_scanFull
                        ? title + ": сумка ещё не пришла, попробуй через пару секунд"
                        : title + ": предмета id " + thingId + " в сумке нет");
                    ranOut = _seen != 0 || _scanFull;
                    yield break;
                }

                float pause = Pause;
                bool wantFill = want < 0 ? (Plugin.CfgFlaskFillToMax == null || Plugin.CfgFlaskFillToMax.Value) : want == 0;

                int cur = 0, max = 0;
                bool known = Gauge(row, out cur, out max);
                bool fill = wantFill && known;
                int count = !wantFill ? Mathf.Max(1, want) : fill ? FillLimit : Batch;
                if (known && cur >= max) { Say(row, title + ": уже полное — " + cur + " / " + max); yield break; }

                int si = 0, q = 0;
                bool stopped = false, rescanned = false;
                int startCur = cur;
                clock.From = RealTime.Now;
                counted = true;

                while (used < count && !stopped)
                {
                    if (si >= stacks.Count)
                    {
                        if (rescanned || (fill && cur >= max)) break;
                        rescanned = true;
                        cached = false;
                        Step(row, "смотрю сумку");
                        yield return Plugin.Instance.StartCoroutine(EnsureScan(0f));
                        stacks = Stacks(thingId);
                        UpdateCounts();
                        si = 0; q = 0;
                        if (stacks.Count == 0) break;
                        continue;
                    }
                    var st = stacks[si];
                    if (q >= st.Qty) { si++; q = 0; continue; }
                    q++;

                    Await(st.Inv, thingId);
                    if (!Send(new ContextActionRequest(st.Inv, BtnUse, WinInventory, st.Tab, 1)))
                    { Say(row, title + ": нет соединения"); yield break; }

                    bool ok = false, got = false;
                    string err = null;
                    int left = -1;
                    float wait = fill && used > 0 ? Quick : Answer;
                    float budget = wait, spent = 0f, last = RealTime.Now;
                    while (!(got = Take(st.Inv, out ok, out err, out left)) && budget > 0f && spent < Answer * 5f)
                    {
                        yield return null;
                        float now = RealTime.Now;
                        if (Flowing()) budget -= now - last;
                        spent += now - last;
                        last = now;
                    }
                    Unawait(st.Inv);
                    if (got) clock.Reply(spent);

                    if (!got && fill && used > 0)
                    {
                        float until = RealTime.Now + 1f;
                        while (RealTime.Now < until && !(Gauge(row, out cur, out max) && cur >= max))
                            yield return null;
                        if (Gauge(row, out cur, out max) && cur >= max)
                        {
                            clock.Silent();
                            Say(row, title + ": до полного — " + used + " шт. (" + cur + " / " + max + ")");
                            yield break;
                        }
                    }

                    if (!got || !ok)
                    {
                        if (!rescanned && (!got || (used == 0 && cached)))
                        {
                            rescanned = true;
                            cached = false;
                            Step(row, "смотрю сумку");
                            yield return Plugin.Instance.StartCoroutine(EnsureScan(0f));
                            stacks = Stacks(thingId);
                            UpdateCounts();
                            si = 0; q = 0;
                            if (fill && Gauge(row, out cur, out max) && cur >= max)
                            {
                                Say(row, title + ": до полного — " + used + " шт. (" + cur + " / " + max + ")");
                                yield break;
                            }
                            continue;
                        }
                        stopped = true;
                        string why = !got ? "сервер не ответил"
                                   : string.IsNullOrEmpty(err) ? "дальше сервер не даёт, похоже уже полное" : err;
                        Say(row, title + ": использовано " + used + ", " + why);
                        Plugin.Log?.LogInfo("[банки] " + title + ": выпито " + used + ", стоп — " + why
                            + (got ? "" : " (ждал " + spent.ToString("0.0") + " с, из них с открытой очередью " + (wait - budget).ToString("0.0") + ")")
                            + ", запись " + st.Inv + ", вещь " + thingId + ", было " + startCur + " / " + max);
                        DiskJournal.Flush();
                        told = true;
                        break;
                    }

                    used++;
                    if (left >= 0 && stacks.Count == 1) Left[row] = left;
                    else if (Left[row] > 0) Left[row]--;
                    Step(row, fill ? used.ToString() : used + " / " + count);

                    if (fill)
                    {
                        float barFrom = RealTime.Now;
                        float w = barFrom + 1.5f;
                        int c2 = cur, m2 = max;
                        bool moved = false;
                        while (RealTime.Now < w)
                        {
                            if (Gauge(row, out c2, out m2) && c2 != cur) { moved = true; break; }
                            yield return null;
                        }
                        clock.Bar(moved, RealTime.Now - barFrom);
                        cur = c2; max = m2;
                        if (cur >= max)
                        {
                            Say(row, title + ": до полного — " + used + " шт. (" + cur + " / " + max + ")");
                            yield break;
                        }
                    }

                    if (used < count) yield return Wait(Random.Range(pause * 0.8f, pause * 1.2f));
                }

                if (!stopped)
                {
                    ranOut = used < count;
                    Say(row, fill
                        ? title + ": использовано " + used + " шт., предметы кончились (" + cur + " / " + max + ")"
                        : title + ": использовано " + used + " шт." + (used < count ? ", предметы кончились" : ""));
                    if (fill && cur < max)
                        Plugin.Log?.LogInfo("[банки] " + title + ": выпито " + used + ", до полного не хватило — в сумке больше нет, было "
                            + startCur + ", стало " + cur + " / " + max + ", вещь " + thingId);
                    DiskJournal.Flush();
                }
            }
            finally
            {
                Unawaited(thingId);
                RowBusy[row] = false;
                RowMsg[row] = null;
                _nextScan = RealTime.Now + 2f;
                if (counted && used > 0) clock.Say(title, used, row);
                else if (!told && StatusRow == row && !string.IsNullOrEmpty(Status)) Tell("не выпито — " + Status);
                done?.Invoke(used, ranOut);
            }
        }

        private sealed class Clock
        {
            internal float Began, From;
            private int _replies, _bars, _missed;
            private float _replySum, _replyWorst, _barSum, _barWorst;

            internal void Reply(float seconds)
            {
                _replies++;
                _replySum += seconds;
                if (seconds > _replyWorst) _replyWorst = seconds;
            }

            private int _silent;

            internal void Silent() => _silent++;

            internal void Bar(bool moved, float seconds)
            {
                if (!moved) { _missed++; return; }
                _bars++;
                _barSum += seconds;
                if (seconds > _barWorst) _barWorst = seconds;
            }

            internal void Say(string title, int used, int row)
            {
                float now = RealTime.Now;
                var line = new System.Text.StringBuilder();
                line.Append("[банки] замер: ").Append(title).Append(" — выпито ").Append(used)
                    .Append(" за ").Append((now - From).ToString("0.00")).Append(" с");
                if (From - Began > 0.05f) line.Append(" (+ ").Append((From - Began).ToString("0.00")).Append(" с на сумку до первой)");
                if (_replies > 0)
                    line.Append(", ответ сервера ").Append((_replySum / _replies).ToString("0.00"))
                        .Append(" с, худший ").Append(_replyWorst.ToString("0.00"));
                if (_bars > 0)
                    line.Append(", полоска ").Append((_barSum / _bars).ToString("0.00"))
                        .Append(" с, худшая ").Append(_barWorst.ToString("0.00"));
                if (_missed > 0) line.Append(", полоска не сдвинулась за 1,5 с: ").Append(_missed).Append(" раз");
                if (_silent > 0) line.Append(", на лишнюю банку сервер промолчал — уже полное");
                if (Gauge(row, out int cur, out int max)) line.Append(", итог ").Append(cur).Append(" / ").Append(max);
                Plugin.Log?.LogInfo(line.ToString());
                DiskJournal.Flush();
            }
        }

        private static bool Gauge(int row, out int cur, out int max)
        {
            cur = 0; max = 0;
            try
            {
                var indicators = Controllers.User?.Indicators;
                if (indicators == null) return false;
                switch (row)
                {
                    case 0: cur = indicators.CurrentLife; max = indicators.MaxLife; break;
                    case 1: cur = indicators.CurrentMana; max = indicators.MaxMana; break;
                    case 2: cur = indicators.CurrentStamina; max = indicators.MaxStamina; break;
                    default: return false;
                }
                return max > 0;
            }
            catch { return false; }
        }

        private static List<Stack> Stacks(int thingId)
        {
            lock (Scanned) return Scanned.Where(s => s.ThingId == thingId && s.Qty > 0).ToList();
        }

        private static void UpdateCounts()
        {
            if (_seen == 0 && !_scanFull) return;
            for (int row = 0; row < Rows; row++)
            {
                int id = Thing(row);
                Left[row] = id > 0 ? Stacks(id).Sum(s => s.Qty) : -1;
            }
        }

        private static IEnumerator EnsureScan(float maxAge)
        {
            while (_scanBusy) yield return null;
            if (RealTime.Now - _scanAt <= maxAge) yield break;
            _scanBusy = true;
            _busySince = RealTime.Now;
            System.Array.Clear(Checked, 0, Rows);
            try
            {
                yield return Plugin.Instance.StartCoroutine(ScanRoutine());
                _scanAt = RealTime.Now;
                _scanOk = _seen > 0 || _scanFull;
            }
            finally { _scanBusy = false; }
        }

        private static IEnumerator ScanRoutine()
        {
            if (!EnsureListeners())
            {
                Plugin.Trace("[банки] опрос сумки: нет связи с игрой, слушатели не встали");
                yield break;
            }
            Plugin.Trace("[банки] опрос сумки начат" + (BagOpen() ? ", сумка открыта" : ""));
            _tabs = null;
            _seen = 0;
            _scanFull = false;
            lock (Scanned) { Scanned.Clear(); ScannedInv.Clear(); GotTabs.Clear(); }

            List<int> tabs;
            if (BagOpen())
            {
                tabs = new List<int> { AllTab };
            }
            else
            {
                if (!Send(new GetThingsTabsRequest(WinInventory)))
                {
                    Plugin.Trace("[банки] опрос сумки: запрос вкладок не ушёл");
                    yield break;
                }

                float wait = RealTime.Now + 3f;
                while (_tabs == null && RealTime.Now < wait) yield return null;
                if (_tabs == null)
                {
                    Plugin.Trace("[банки] опрос сумки: вкладки не пришли за 3 с");
                    yield break;
                }
                if (_tabs.Count == 0)
                {
                    _scanFull = true;
                    Plugin.Trace("[банки] опрос сумки: игра прислала пустой список вкладок");
                    yield break;
                }
                tabs = new List<int>(_tabs);
            }

            Plugin.Trace("[банки] опрос сумки: вкладок " + tabs.Count + " (" + string.Join(",", tabs) + ")");
            foreach (int tab in tabs)
            {
                if (!Send(new GetTabContentRequest(tab, WinInventory)))
                {
                    Plugin.Trace("[банки] опрос сумки: запрос вкладки " + tab + " не ушёл");
                    yield break;
                }
                yield return null;
            }

            float t = RealTime.Now + 3f;
            while (RealTime.Now < t)
            {
                int got;
                lock (Scanned) got = GotTabs.Count;
                if (got >= tabs.Count)
                {
                    _scanFull = tabs.Count > 0;
                    break;
                }
                yield return null;
            }
            int stacks, kinds;
            lock (Scanned)
            {
                stacks = Scanned.Count;
                var seen = new HashSet<int>();
                kinds = 0;
                foreach (var s in Scanned)
                    if (Consumable(s.SubType) && s.Qty > 0 && seen.Add(s.ThingId)) kinds++;
            }
            Plugin.Trace("[банки] опрос сумки закончен: вкладок пришло " + GotTabs.Count + " из " + tabs.Count
                         + ", вещей " + _seen + ", стопок " + stacks + ", из них банок " + kinds);
            yield return null;
        }

        private static object _boundTo;

        private static bool EnsureListeners()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null) return false;
                if (_listeners && ReferenceEquals(_boundTo, nc)) return true;
                nc.AddMessageListener(359, OnTabs);
                nc.AddMessageListener(380, OnTabContent);
                nc.AddMessageListener(385, OnThingInfo);
                nc.AddMessageListener(416, OnAction);
                nc.AddMessageListener(431, OnGift);
                nc.AddMessageListener(444, OnChest);
                _boundTo = nc;
                _listeners = true;
                return true;
            }
            catch { return false; }
        }

        private static void OnTabs(object m)
        {
            if (!_scanBusy) return;
            if (m is ThingTypeTabsResponseMessage t && t.TabIds != null && t.WindowId == WinInventory)
                _tabs = t.TabIds.Distinct().ToList();
        }

        private static void OnTabContent(object m)
        {
            if (!(m is ThingTabInventoryResponseMessage inv) || inv.WindowId != WinInventory || inv.Things == null) return;

            foreach (var th in inv.Things)
                if (th != null) Learn(th.ThingId, th.Image, th.Name);

            if (!_scanBusy) { Overhear(inv); return; }
            lock (Scanned)
            {
                GotTabs.Add(inv.TabNumber);
                _seen += inv.Things.Count;
                foreach (var th in inv.Things)
                {
                    if (th == null || th.Inventories == null) continue;
                    foreach (var ii in th.Inventories)
                    {
                        if (ii == null || ii.Quantity <= 0 || !ScannedInv.Add(ii.InventoryId)) continue;
                        Scanned.Add(new Stack
                        {
                            Inv = ii.InventoryId,
                            Tab = inv.TabNumber,
                            ThingId = th.ThingId,
                            Qty = ii.Quantity,
                            SubType = th.SubType,
                        });
                        NoteUse(th.ThingId, ii.CanUse ?? 0);
                        Note(th.ThingId, th.Image, th.SubType, th.Rarity, th.Level);
                    }
                }
            }
        }

        private static void Overhear(ThingTabInventoryResponseMessage inv)
        {
            try
            {
                if (AnyBusy) return;
                for (int row = 0; row < Rows; row++)
                {
                    int id = Thing(row);
                    if (id <= 0) continue;
                    int sum = 0;
                    bool seen = false;
                    foreach (var th in inv.Things)
                    {
                        if (th == null || th.ThingId != id || th.Inventories == null) continue;
                        seen = true;
                        foreach (var ii in th.Inventories)
                            if (ii != null && ii.Quantity > 0) sum += ii.Quantity;
                    }
                    if (seen) Left[row] = sum;
                }
            }
            catch { }
        }

        private static void OnChest(object m)
        {
            Gift("сундук открыт");
        }

        private static void OnGift(object m)
        {
            Gift("награда за задание");
        }

        private static void Gift(string why)
        {
            try
            {
                _chestAgain = RealTime.Now + 6f;
                RequestScanNow();
                Plugin.Trace("[банки] " + why + ": пересчитываю остатки");
            }
            catch { }
        }

        private static void OnThingInfo(object m)
        {
            if (!(m is GeneralThingInfoMessage info)) return;
            Learn(info.ThingId, info.Image, info.Name);
            lock (Facts)
                Facts[info.ThingId] = new[] { info.SubType, info.Rarity, info.Level ?? 0, info.PreferableClassMask ?? 0 };
        }

        private static void OnAction(object m)
        {
            if (!(m is ThingContextActionResponseMessage r)) return;
            int left = -1;
            if (r.ChangesInTab != null)
                foreach (var ch in r.ChangesInTab)
                    if (ch != null && ch.Id == r.Id) left = ch.Quantity;
            lock (Ctx)
            {
                if (Awaited.ContainsKey(r.Id)) Ctx[r.Id] = (r.Success, r.Success ? null : r.ErrorMessage, left);
                else if (r.WindowId == WinInventory && r.ButtonId == BtnUse && r.ChangesInTab != null)
                {
                    int owner = int.MinValue;
                    foreach (var pair in Awaited)
                    {
                        if (pair.Value <= 0 || Ctx.ContainsKey(pair.Key)) continue;
                        foreach (var ch in r.ChangesInTab)
                            if (ch != null && ch.ThingId == pair.Value) { owner = pair.Key; break; }
                        if (owner != int.MinValue) break;
                    }
                    if (owner != int.MinValue) Ctx[owner] = (r.Success, r.Success ? null : r.ErrorMessage, -1);
                }
            }
            if (r.Success) Bought(r);
        }

        private static void Bought(ThingContextActionResponseMessage r)
        {
            try
            {
                bool buy = r.ButtonId == (int)EThingActionButton.BUY;
                bool got = !AnyBusy && (r.ButtonId == (int)EThingActionButton.USE || r.ButtonId == (int)EThingActionButton.GET_FROM_BOX);
                string why = buy ? "покупка" : "вещь использована не нашей кнопкой";
                if (r.ChangesInTab == null || r.ChangesInTab.Count == 0)
                {
                    if (!buy && !got) return;
                    RequestScanNow();
                    Plugin.Trace("[банки] " + why + " без списка изменений: пересчитываю остатки");
                    return;
                }

                bool ours = false;
                for (int row = 0; row < Rows && !ours; row++)
                {
                    int id = Thing(row);
                    if (id <= 0) continue;
                    foreach (var ch in r.ChangesInTab)
                        if (ch != null && ch.ThingId == id) { ours = true; break; }
                }
                if (!ours && !buy && !got) return;

                if (ours && r.WindowId == WinInventory)
                {
                    lock (Scanned)
                    {
                        foreach (var ch in r.ChangesInTab)
                        {
                            if (ch == null) continue;
                            Note(ch.ThingId, ch.Image, ch.SubType, ch.Rarity, ch.Level);
                            NoteUse(ch.ThingId, ch.CanUseMask);
                            int at = -1;
                            for (int i = 0; i < Scanned.Count; i++) if (Scanned[i].Inv == ch.Id) { at = i; break; }
                            if (ch.Quantity <= 0)
                            {
                                if (at >= 0) Scanned.RemoveAt(at);
                                ScannedInv.Remove(ch.Id);
                                continue;
                            }
                            var stack = new Stack
                            {
                                Inv = ch.Id,
                                Tab = at >= 0 ? Scanned[at].Tab : r.TabId,
                                ThingId = ch.ThingId,
                                Qty = ch.Quantity,
                                SubType = ch.SubType,
                            };
                            if (at >= 0) Scanned[at] = stack; else Scanned.Add(stack);
                            ScannedInv.Add(ch.Id);
                        }
                    }
                    UpdateCounts();
                }

                RequestScanNow();
                Plugin.Trace("[банки] " + (ours ? "изменились наши остатки" : why) + ": пересчитываю остатки");
            }
            catch (System.Exception e) { Plugin.Trace("[банки] покупка: " + e.Message); }
        }

        private const float Answer = 6f;
        private const float Quick = 1f;

        private static readonly Dictionary<int, int> Awaited = new Dictionary<int, int>();

        private static void Await(int inv, int thing)
        {
            lock (Ctx)
            {
                Ctx.Remove(inv);
                Awaited[inv] = thing;
            }
        }

        private static void Unawait(int inv)
        {
            lock (Ctx)
            {
                Awaited.Remove(inv);
                Ctx.Remove(inv);
            }
        }

        private static void Unawaited(int thing)
        {
            if (thing <= 0) return;
            lock (Ctx)
            {
                var gone = new List<int>();
                foreach (var pair in Awaited)
                    if (pair.Value == thing) gone.Add(pair.Key);
                foreach (int inv in gone)
                {
                    Awaited.Remove(inv);
                    Ctx.Remove(inv);
                }
            }
        }

        private static bool Flowing()
        {
            var nc = NetworkConnection.Instance;
            return nc != null && nc.DispatchingEnabled;
        }

        private static bool Take(int inv, out bool ok, out string err, out int left)
        {
            lock (Ctx)
            {
                if (Ctx.TryGetValue(inv, out var v))
                {
                    Ctx.Remove(inv);
                    ok = v.Ok; err = v.Err; left = v.Left;
                    return true;
                }
            }
            ok = false; err = null; left = -1;
            return false;
        }

        private static bool Connected()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                return nc != null && nc.IsConnected();
            }
            catch { return false; }
        }

        private static bool Send(BaseRequest request)
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return false;
                nc.SendRequest(request);
                return true;
            }
            catch (System.Exception e) { Plugin.Fault("[flasks] " + e.Message); return false; }
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = RealTime.Now + seconds;
            while (RealTime.Now < t) yield return null;
        }

        private static void Say(int row, string text)
        {
            Status = text;
            StatusAt = Time.unscaledTime;
            StatusRow = row;
            Plugin.Trace("[flasks] " + text);
        }

        private static void Step(int row, string text)
        {
            RowMsg[row] = text;
            Status = Titles[row] + ": " + text;
            StatusAt = Time.unscaledTime;
            StatusRow = row;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(AtlasUtils), "ClearUnusedAtlases")]
    public static class AtlasDropPatch
    {
        private static void Postfix() => Flasks.DropIcons();
    }
}
