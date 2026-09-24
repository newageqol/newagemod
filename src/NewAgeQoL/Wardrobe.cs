using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Transport.Messages.Responses.User.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Wardrobe
    {
        private const float PanelW = 1424f;
        private const float PanelH = 812f;
        private const float Head = 110f;
        private const float SideW = 440f;
        private const float StageW = 600f;
        private const float BodyH = 680f;
        private const float ResultW = 320f;
        private const float Cell = 76f;
        private const float Gap = 12f;
        private const float DollScale = 3f;

        private static readonly int[] LeftSlots = { 1, 3, 6, 25, 26, 7, 8 };
        private static readonly int[] RightSlots = { 2, 5, 4, 13, 14, 9, 10 };
        private static readonly int[] RelicSlots = { 15, 16, 17, 18 };
        private static readonly int[] HandSlots = { 12, 11 };
        private static readonly int[] VisualSlots = { 1, 3, 4, 5, 11, 12, 13, 14 };

        internal static readonly WardrobeState S = new WardrobeState();
        private static string _loadedKey;
        private static RectTransform _tabs;
        private static InputField _nameInput;
        private static readonly List<Outline> Blinks = new List<Outline>();
        private static bool _openWhenReady;
        private static bool _seeding;
        private static int _edits;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static GameObject _tipGo;
        private static float _paintAt;
        private static RectTransform _side;
        private static RectTransform _stage;
        private static RectTransform _dollBox;
        private static RawImage _doll;
        private static Texture2D _texture;
        private static Text _dollNote;
        private static readonly Dictionary<int, RectTransform> Cells = new Dictionary<int, RectTransform>();
        private static readonly Dictionary<int, Image> Plain = new Dictionary<int, Image>();
        private static string _dollSign;
        private static int _dollJob;
        private static float _dollDue;

        private static Text _race, _gender, _level, _class, _sub, _rank;
        private static readonly InputField[] BaseInput = new InputField[7];
        private static readonly Text[] TotalText = new Text[7];
        private static Text _free, _rating, _life, _mana, _energy, _warn, _stamp, _updateNote;
        private static Button _update;
        private static string _statusSeen;
        private static readonly Text[] ArmorText = new Text[5];
        private static readonly Text[] MagicText = new Text[3];

        private static GameObject _askGo;
        private static GameObject _menuGo;
        private static Button _add;
        private const float StripW = PanelW - 32f - 128f;

        internal static bool IsOpen => _canvasGo != null;
        internal static RectTransform Side => _side;
        internal static Transform Panel => _panelGo != null ? _panelGo.transform : null;

        internal static void Toggle()
        {
            if (_canvasGo != null) { Close(); return; }
            Open();
        }

        internal static void Open()
        {
            try
            {
                if (!WardrobeData.Ready())
                {
                    if (!WardrobeData.HasRules())
                    {
                        Notice.Show("Переодевалка: " + (WardrobeData.Error ?? "нет правил игры"), 6f);
                        return;
                    }
                    _openWhenReady = true;
                    WardrobeUpdate.Start();
                    Notice.Show("Переодевалка: загружаю вещи, окно откроется само", 6f);
                    return;
                }
                var active = WardrobeStore.Active;
                if (active == null)
                {
                    foreach (var m in WardrobeStore.All) { active = m; break; }
                    if (active == null)
                    {
                        Seed();
                        active = WardrobeStore.Add(WardrobeStore.FreeTitle(), S.Pack());
                        _loadedKey = active.Key;
                    }
                    WardrobeStore.ActiveKey = active.Key;
                    WardrobeStore.Touch();
                }
                if (_loadedKey != active.Key) Load(active);
                Build();
                Changed(true);
                WardrobeUpdate.Auto(S.Unknown.Count > 0);
                Plugin.Trace("[переодевалка] открыта: " + Describe());
            }
            catch (Exception e) { Plugin.Fault("[переодевалка] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            WardrobePicker.Close();
            WardrobeArt.Close();
            WardrobeCompare.Close();
            _menuGo = null;
            _add = null;
            WardrobeDoll.Forget();
            WardrobeStore.Flush();
            _tabs = null;
            _nameInput = null;
            Blinks.Clear();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
            _texture = null;
            _canvasGo = null;
            _panelGo = null;
            Untip();
            _paintAt = 0f;
            _side = null;
            _stage = null;
            _dollBox = null;
            _doll = null;
            _dollNote = null;
            _askGo = null;
            _stamp = null;
            _updateNote = null;
            _update = null;
            _statusSeen = null;
            _dollSign = null;
            _dollJob = 0;
            _dollDue = 0f;
            Cells.Clear();
            Plain.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            if (_askGo != null) { CloseAsk(); return true; }
            if (CloseMenu()) return true;
            if (!WardrobePicker.IsOpen && WardrobeArt.EscapeClose()) return true;
            if (WardrobeCompare.EscapeClose()) return true;
            if (WardrobePicker.EscapeClose()) return true;
            Close();
            return true;
        }

        internal static void Tick()
        {
            if (_canvasGo == null) return;
            if (_paintAt > 0f && Time.unscaledTime >= _paintAt) Paint();
            var picture = WardrobeDoll.Take();
            if (picture != null && picture.Job == _dollJob) Show(picture);
            if (_dollDue > 0f && Time.unscaledTime >= _dollDue) { _dollDue = 0f; Order(); }
            Status();
            WardrobePicker.Tick();
            WardrobeStore.Tick();
            float glow = Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);
            foreach (var blink in Blinks)
                if (blink != null) blink.effectColor = new Color(1f, 0.85f, 0.25f, 0.25f + 0.75f * glow);
        }

        private static string Describe()
        {
            var race = S.Race;
            var klass = S.Klass;
            var sub = S.Sub;
            return (race != null ? race.Name : "?") + ", ур. " + S.Level + ", " + (klass != null ? klass.Name : "?")
                   + (sub != null ? " / " + sub.Name : "") + ", вещей " + S.Worn.Count + ", рейтинг " + S.Rating;
        }

        internal static void Seed()
        {
            try { Blank(); }
            catch (Exception e) { Plugin.Trace("[переодевалка] свой персонаж не прочитан: " + e.Message); }
            S.Settle(false);
        }

        private static bool Blank()
        {
            S.Rank = 0;
            S.Undress();
            for (int i = 0; i < 7; i++) S.Dist[i] = 0;
            var info = Controllers.User?.UserInfo;
            if (info == null) return false;
            S.RaceId = (int)info.Race;
            S.Gender = (int)info.Gender;
            S.Level = info.Level;
            S.ClassId = (int)info.RpgClass;
            return true;
        }

        private static int Seed(Dictionary<int, InventoryWearResponseMessageItem> worn, Dictionary<int, IGeneralThingInfoDescription> told, int[] card)
        {
            int dressed = 0;
            try
            {
                if (Controllers.User?.UserInfo == null) { S.Settle(false); return 0; }
                Blank();
                S.Settle(false);
                var arts = new List<KeyValuePair<int, IGeneralThingInfoDescription>>();
                foreach (var pair in worn)
                {
                    var item = pair.Value;
                    if (item == null) continue;
                    if (item.Rarity == WardrobeArt.Rarity)
                    {
                        IGeneralThingInfoDescription about;
                        if (told != null && told.TryGetValue(item.ThingId, out about) && about != null && WardrobeData.Fits(pair.Key, (int)about.ThingSubType))
                            arts.Add(new KeyValuePair<int, IGeneralThingInfoDescription>(pair.Key, about));
                        continue;
                    }
                    var thing = WardrobeData.Thing(item.ThingId) ?? WardrobeData.Look(item.Image, item.Level, item.Rarity);
                    if (thing == null || !WardrobeData.Fits(pair.Key, thing.Sub) || thing.Level > S.Level) continue;
                    S.Wear(pair.Key, thing);
                    dressed++;
                }
                foreach (var pair in WardrobeArt.FromGame(S, arts, card))
                {
                    if (pair.Value.Level > S.Level) continue;
                    S.Wear(pair.Key, pair.Value);
                    dressed++;
                }
                foreach (var pair in new List<KeyValuePair<int, WardrobeThing>>(S.Worn)) S.Grant(pair.Value);
                Plugin.Trace("[переодевалка] как у меня: надето " + dressed + ", из них артефактов " + arts.Count
                             + (card != null ? ", броня по точкам из карточки" : ", карточки нет — броня артефактов поровну"));
            }
            catch (Exception e)
            {
                Plugin.Trace("[переодевалка] свой персонаж не прочитан: " + e.Message);
                S.Settle(false);
            }
            return dressed;
        }

        internal static void Mine()
        {
            if (_seeding || Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(MineRoutine());
        }

        private static IEnumerator MineRoutine()
        {
            _seeding = true;
            string key = _loadedKey;
            int edits = _edits;
            int me = 0;
            string login = null;
            float since = Time.unscaledTime;
            try
            {
                var info = Controllers.User?.UserInfo;
                if (info != null) { me = info.UserId; login = info.Login; }
                if (me > 0) Armor.AskNow(me, login);
            }
            catch (Exception e) { Plugin.Trace("[переодевалка] карточка: " + e.Message); }
            yield return Plugin.Instance.StartCoroutine(Artifacts.FetchWear());
            if (!Artifacts.WearArrived)
            {
                _seeding = false;
                if (_canvasGo != null) Notice.Show("Переодевалка: игра не ответила, что на тебе надето — манекен не тронут", 5f);
                yield break;
            }
            var worn = new Dictionary<int, InventoryWearResponseMessageItem>(Storage.WornSlots());
            var told = new Dictionary<int, IGeneralThingInfoDescription>();
            int asked = Learn(worn, told);
            float t = 0f;
            while (t < 5f && told.Count < asked) { yield return null; t += Time.unscaledDeltaTime; }
            t = 0f;
            while (t < 3f && me > 0 && !Armor.HeardSince(me, since)) { yield return null; t += Time.unscaledDeltaTime; }
            _seeding = false;
            if (_canvasGo == null || _loadedKey != key) yield break;
            if (_edits != edits)
            {
                Notice.Show("Переодевалка: манекен поменяли, пока я читал персонажа, — «Как у меня» не применено", 5f);
                yield break;
            }
            int[] card = null;
            if (me > 0 && Armor.HeardSince(me, since))
                card = new[]
                {
                    Armor.Zone(me, TargetBody.Head), Armor.Zone(me, TargetBody.Body), Armor.Zone(me, TargetBody.LeftHand),
                    Armor.Zone(me, TargetBody.RightHand), Armor.Zone(me, TargetBody.Legs)
                };
            int dressed = Seed(worn, told, card);
            Changed(true);
            if (told.Count < asked) Notice.Show("Переодевалка: игра не рассказала про " + (asked - told.Count) + " " + Plural(asked - told.Count, "артефакт", "артефакта", "артефактов"), 5f);
            else
            {
                int wanted = 0;
                foreach (var pair in worn) if (pair.Value != null && WardrobeData.IsSlot(pair.Key)) wanted++;
                if (dressed < wanted) Notice.Show("Переодевалка: надето " + dressed + " из " + wanted + ", остальных вещей нет в базе", 5f);
            }
        }

        private static int Learn(Dictionary<int, InventoryWearResponseMessageItem> worn, Dictionary<int, IGeneralThingInfoDescription> told)
        {
            int asked = 0;
            try
            {
                var cache = DependencyContainer.GetContainer()?.Resolve<GeneralThingInfoDescriptionManager>();
                if (cache == null) return 0;
                foreach (var pair in worn)
                {
                    var item = pair.Value;
                    if (item == null || item.Rarity != WardrobeArt.Rarity || item.ThingId <= 0) continue;
                    int id = item.ThingId;
                    asked++;
                    cache.Get(id, got => { if (got != null) told[id] = got; });
                }
            }
            catch (Exception e) { Plugin.Trace("[переодевалка] описание артефакта: " + e.Message); }
            return asked;
        }

        internal static void Changed(bool keepSub)
        {
            _edits++;
            S.Settle(keepSub);
            Refresh();
            Paint();
            _dollDue = Time.unscaledTime + 0.12f;
            CloseMenu();
            WardrobePicker.Refresh();
            if (!WardrobePicker.IsOpen) WardrobeArt.Refresh();
            WardrobeCompare.Refresh();
            Keep();
        }

        private static void Keep()
        {
            var active = WardrobeStore.Active;
            if (active == null || active.Key != _loadedKey) return;
            string body = S.Pack();
            if (body == active.Body) return;
            active.Body = body;
            WardrobeStore.Touch();
        }

        private static void Load(WardrobeManikin manikin)
        {
            CloseMenu();
            WardrobeArt.Close();
            int lost = S.Unpack(manikin.Body);
            _loadedKey = manikin.Key;
            WardrobeStore.ActiveKey = manikin.Key;
            if (manikin.Received && !manikin.Seen) manikin.Seen = true;
            WardrobeStore.Touch();
            if (S.Unknown.Count > 0)
            {
                Notice.Show("В манекене «" + manikin.Title + "» нет в базе вещей: " + S.Unknown.Count + ". Они сохранены, ищу их в свежей базе", 6f);
                WardrobeUpdate.Auto(true);
            }
            else if (lost > 0) Notice.Show("В манекене «" + manikin.Title + "» не нашлось вещей: " + lost, 5f);
        }

        private static void Switch(WardrobeManikin manikin)
        {
            if (manikin == null || manikin.Key == _loadedKey) return;
            Keep();
            WardrobePicker.Close();
            Load(manikin);
            Changed(true);
            Tabs();
            Named();
        }

        private static void NewOne()
        {
            if (WardrobeStore.OwnCount >= WardrobeStore.MaxOwn)
            {
                Notice.Show("Своих манекенов может быть не больше " + WardrobeStore.MaxOwn + ". Удали ненужный.", 5f);
                return;
            }
            Keep();
            Seed();
            var manikin = WardrobeStore.Add(WardrobeStore.FreeTitle(), S.Pack());
            if (manikin == null) return;
            WardrobePicker.Close();
            Load(manikin);
            Changed(true);
            Tabs();
            Named();
        }

        private static void DeleteActive()
        {
            var active = WardrobeStore.Active;
            if (active == null) return;
            Ask("Удалить манекен «" + active.Title + "»" + (active.Received ? " от " + active.From : "") + "?", () =>
            {
                WardrobeStore.Remove(active);
                WardrobeManikin next = null;
                foreach (var m in WardrobeStore.All) if (!m.Received) { next = m; break; }
                if (next == null && WardrobeStore.All.Count > 0) next = WardrobeStore.All[0];
                if (next == null)
                {
                    Seed();
                    next = WardrobeStore.Add(WardrobeStore.FreeTitle(), S.Pack());
                }
                _loadedKey = null;
                WardrobePicker.Close();
                Load(next);
                Changed(true);
                Tabs();
                Named();
            }, "Удалить");
        }

        private static void Rename(string text)
        {
            var active = WardrobeStore.Active;
            if (active == null) return;
            string title = WardrobeStore.Clean(text);
            if (title.Length == 0) { Named(); return; }
            if (title == active.Title) return;
            active.Title = title;
            WardrobeStore.Touch();
            Tabs();
        }

        private static void Named()
        {
            var active = WardrobeStore.Active;
            if (_nameInput != null && active != null) _nameInput.text = active.Title;
        }

        private static void Tabs()
        {
            if (_tabs == null) return;
            for (int i = _tabs.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_tabs.GetChild(i).gameObject);
            Blinks.Clear();
            int own = 0, got = 0;
            foreach (var m in WardrobeStore.All) if (m.Received) got++; else own++;
            if (_add != null) _add.gameObject.SetActive(own < WardrobeStore.MaxOwn);
            int count = own + got;
            float room = StripW - 8f - (got > 0 ? 96f : 0f);
            float wide = count > 0 ? Mathf.Clamp((room - 6f * (count - 1)) / count, 84f, 150f) : 150f;
            foreach (var m in WardrobeStore.All) if (!m.Received) Tab(m, wide);
            if (got == 0) return;
            var label = OnlineWindow.Label(_tabs, "Прислали:", 14, FontStyle.Bold, WardrobeLook.Faint);
            Size(label.gameObject, 90f);
            foreach (var m in WardrobeStore.All) if (m.Received) Tab(m, wide);
        }

        private static void Tab(WardrobeManikin manikin, float wide)
        {
            bool active = manikin.Key == _loadedKey;
            string text = manikin.Received ? manikin.Title + "\n<size=11>от " + manikin.From + "</size>" : manikin.Title;
            var tab = Arrow(_tabs, text, () => Switch(manikin));
            Size(tab.gameObject, wide);
            tab.GetComponent<Image>().color = active ? WardrobeLook.Accent : manikin.Received ? WardrobeLook.TabReceived : WardrobeLook.Tab;
            var label = tab.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = active ? WardrobeLook.OnAccent : manikin.Received ? WardrobeLook.ReceivedText : WardrobeLook.Label;
                label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                label.fontSize = 14;
                label.supportRichText = true;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 10;
                label.resizeTextMaxSize = 14;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
            }
            if (manikin.Received && !manikin.Seen)
            {
                var ring = tab.gameObject.AddComponent<Outline>();
                ring.effectDistance = new Vector2(3f, -3f);
                Blinks.Add(ring);
            }
        }

        private static void Size(GameObject go, float width)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            le.minHeight = le.preferredHeight = 40f;
        }

        internal static void Pick(int slot, WardrobeThing thing, bool all)
        {
            if (thing != null && slot == 11 && !WardrobeData.Fits(11, thing.Sub)) slot = 12;
            var group = all ? WardrobeData.Group(slot) : null;
            var slots = group ?? new[] { slot };
            if (thing == null)
            {
                foreach (int one in slots) S.Wear(one, null);
                WardrobePicker.Close();
                Changed(true);
                return;
            }
            if (S.Fits(thing))
            {
                Dress(slots, thing);
                return;
            }
            if (thing.Level > S.Level)
            {
                Notice.Show("«" + thing.Name + "» надевается с " + thing.Level + " уровня", 5f);
                return;
            }
            var add = new int[7];
            int need = S.Missing(thing, add);
            if (need > S.Free)
            {
                Notice.Show("Не хватает статов на «" + thing.Name + "»: нужно ещё " + need + " (" + Gaps(add) + "), свободно " + S.Free, 7f);
                return;
            }
            Ask("Распределить " + need + " " + Plural(need, "свободное очко", "свободных очка", "свободных очков") + " (" + Gaps(add)
                + "), чтобы надеть «" + thing.Name + "»?", () =>
            {
                if (!S.Grant(thing)) { Notice.Show("Не хватает статов на «" + thing.Name + "»", 5f); return; }
                Dress(slots, thing);
            });
        }

        private static void Dress(int[] slots, WardrobeThing thing)
        {
            foreach (int one in slots) S.Wear(one, thing);
            WardrobePicker.Close();
            Changed(true);
        }

        internal static void Reloaded()
        {
            foreach (int slot in new List<int>(S.Worn.Keys))
            {
                var old = S.Worn[slot];
                var same = WardrobeData.Same(old);
                if (same == null || !WardrobeData.Fits(slot, same.Sub)) S.Forget(slot, old);
                else S.Worn[slot] = same;
            }
            int back = S.Recall();
            if (back > 0 && _canvasGo != null) Notice.Show("Переодевалка: в базе нашлись вещи манекена: " + back, 5f);
            _statusSeen = null;
            if (_canvasGo != null) { Changed(true); return; }
            if (!_openWhenReady) return;
            _openWhenReady = false;
            if (WardrobeData.Ready()) Open();
            else Notice.Show("Переодевалка: " + WardrobeUpdate.Status, 8f);
        }

        private static void Status()
        {
            if (_stamp == null) return;
            string status = WardrobeUpdate.Status ?? "";
            string key = status + "|" + WardrobeUpdate.Busy + "|" + WardrobeData.Source + "|" + WardrobeData.Things.Count;
            if (key == _statusSeen) return;
            _statusSeen = key;
            _stamp.text = "вещей " + WardrobeData.Things.Count + ", " + WardrobeData.Source + " (" + WardrobeData.Stamp + "), рейтинг без заклинаний";
            if (_updateNote != null) _updateNote.text = status;
            if (_update != null) _update.interactable = !WardrobeUpdate.Busy;
        }

        internal static string Gaps(int[] add)
        {
            var text = new StringBuilder();
            foreach (int i in WardrobeData.StatOrder)
            {
                if (add[i] <= 0) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(WardrobeData.StatNames[i]).Append(" +").Append(add[i]);
            }
            return text.ToString();
        }

        internal static string Plural(int n, string one, string few, string many)
        {
            int mod100 = n % 100, mod10 = n % 10;
            if (mod100 >= 11 && mod100 <= 14) return many;
            if (mod10 == 1) return one;
            if (mod10 >= 2 && mod10 <= 4) return few;
            return many;
        }

        private static void Build()
        {
            Close();
            var go = new GameObject("QoLWardrobe", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 830;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            UiScale.Own(scaler);
            go.AddComponent<WardrobeTicker>();

            var backGo = new GameObject("backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backGo.transform.SetParent(go.transform, false);
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            backGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            backGo.GetComponent<Button>().onClick.AddListener(Close);

            _panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            var back = _panelGo.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            var outline = _panelGo.GetComponent<Outline>();
            outline.effectColor = WardrobeLook.Edge;
            outline.effectDistance = new Vector2(1f, -1f);

            var title = OnlineWindow.Label(_panelGo.transform, "Переодевалка", 22, FontStyle.Bold, WardrobeLook.Bright);
            At(title.rectTransform, 20f, 12f, 400f, 36f);
            title.alignment = TextAnchor.MiddleLeft;
            _stamp = OnlineWindow.Label(_panelGo.transform, "", 12, FontStyle.Normal, WardrobeLook.Faint);
            At(_stamp.rectTransform, 200f, 20f, 560f, 24f);
            _stamp.alignment = TextAnchor.MiddleLeft;
            _updateNote = OnlineWindow.Label(_panelGo.transform, "", 12, FontStyle.Normal, WardrobeLook.Label);
            At(_updateNote.rectTransform, 760f, 14f, PanelW - 760f - 250f, 36f);
            _updateNote.alignment = TextAnchor.MiddleRight;
            _updateNote.horizontalOverflow = HorizontalWrapMode.Wrap;
            _updateNote.resizeTextForBestFit = true;
            _updateNote.resizeTextMinSize = 9;
            _updateNote.resizeTextMaxSize = 12;
            _update = GameButton(_panelGo.transform, "Обновить вещи", () =>
            {
                WardrobeUpdate.Start();
                Status();
            }, false).GetComponent<Button>();
            At((RectTransform)_update.transform, PanelW - 236f, 12f, 170f, 38f);
            OnlineWindow.MakeCloseButton(_panelGo.transform, Close);
            _statusSeen = null;
            Status();

            var stripGo = new GameObject("manikins", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            stripGo.transform.SetParent(_panelGo.transform, false);
            var strip = (RectTransform)stripGo.transform;
            At(strip, 16f, 56f, StripW, 48f);
            _add = Arrow(_panelGo.transform, "+ Новый", NewOne);
            At((RectTransform)_add.transform, 16f + StripW + 8f, 56f, PanelW - 32f - StripW - 8f, 48f);
            _add.GetComponent<Image>().color = WardrobeLook.Button;
            var addLabel = _add.GetComponentInChildren<Text>();
            if (addLabel != null) { addLabel.color = WardrobeLook.Accent; addLabel.fontSize = 16; }
            var stripBack = stripGo.GetComponent<Image>();
            stripBack.color = new Color(0f, 0f, 0f, 0f);
            stripBack.sprite = OnlineWindow.Rounded(10);
            stripBack.type = Image.Type.Sliced;
            var tabsGo = new GameObject("tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            tabsGo.transform.SetParent(strip, false);
            _tabs = (RectTransform)tabsGo.transform;
            _tabs.anchorMin = new Vector2(0f, 0f);
            _tabs.anchorMax = new Vector2(0f, 1f);
            _tabs.pivot = new Vector2(0f, 0.5f);
            _tabs.offsetMin = Vector2.zero;
            _tabs.offsetMax = Vector2.zero;
            var row = tabsGo.GetComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(4, 4, 4, 4);
            row.spacing = 6f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            tabsGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = stripGo.GetComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.content = _tabs;
            scroll.viewport = strip;
            Tabs();

            _side = Box(_panelGo.transform, "side", 16f, Head, SideW, BodyH, WardrobeLook.Card, 10);
            _stage = Box(_panelGo.transform, "stage", 16f + SideW + 16f, Head, StageW, BodyH, WardrobeLook.Stage, 10);
            var result = Box(_panelGo.transform, "result", 16f + SideW + 16f + StageW + 16f, Head, ResultW, BodyH, WardrobeLook.Card, 10);

            BuildSide();
            BuildStage();
            BuildResult(result);
        }

        private static void BuildSide()
        {
            float y = 12f;
            _nameInput = OnlineWindow.MakeInput(_side, 322f, "название манекена");
            At((RectTransform)_nameInput.transform, 12f, y, 322f, 34f);
            _nameInput.characterLimit = WardrobeStore.MaxTitle;
            WardrobeLook.Style(_nameInput);
            _nameInput.onEndEdit.AddListener(Rename);
            var delete = GameButton(_side, "Удалить", DeleteActive, true);
            At(delete, 340f, y, 88f, 34f);
            Named();
            y += 44f;
            _race = Cycler(_side, "Раса", y, step => Turn(Race, step));
            y += 40f;
            _gender = Cycler(_side, "Пол", y, step => { if (S.RaceId != 9) S.Gender = S.Gender == 2 ? 1 : 2; Changed(true); });
            y += 40f;
            _level = Cycler(_side, "Уровень", y, step => { S.Level += step; Changed(false); });
            y += 40f;
            _class = Cycler(_side, "Класс", y, step => Turn(Klass, step));
            y += 40f;
            _sub = Cycler(_side, "Подкласс", y, step => { StepSub(step); Changed(true); });
            y += 40f;
            _rank = Cycler(_side, "Крепость", y, step => { StepRank(step); Changed(true); });
            y += 48f;

            float bw = (SideW - 24f - 16f) / 3f;
            Place(GameButton(_side, "Как у меня", Mine, false), 12f, y, bw, 38f);
            Place(GameButton(_side, "Сравнить", Compare, false), 12f + bw + 8f, y, bw, 38f);
            Place(GameButton(_side, "Обнулить", () => { S.Undress(); S.Minimum(); Changed(true); }, true), 12f + (bw + 8f) * 2f, y, bw, 38f);
            y += 50f;

            Header(_side, "Характеристики", y);
            _free = OnlineWindow.Label(_side, "", 15, FontStyle.Bold, WardrobeLook.Accent);
            At(_free.rectTransform, SideW - 220f, y, 208f, 30f);
            _free.alignment = TextAnchor.MiddleRight;
            y += 34f;

            var capBase = OnlineWindow.Label(_side, "база", 12, FontStyle.Normal, WardrobeLook.Faint);
            At(capBase.rectTransform, 130f, y, 110f, 18f);
            var capTotal = OnlineWindow.Label(_side, "итог", 12, FontStyle.Normal, WardrobeLook.Faint);
            At(capTotal.rectTransform, 244f, y, 80f, 18f);
            y += 20f;

            for (int n = 0; n < 7; n++)
            {
                int i = WardrobeData.StatOrder[n];
                int stat = i;
                var row = Box(_side, "stat" + i, 12f, y, SideW - 24f, 34f, WardrobeLook.Stripe(n), 6);
                var name = OnlineWindow.Label(row, WardrobeData.StatNames[i], 15, FontStyle.Normal, WardrobeLook.Label);
                At(name.rectTransform, 10f, 0f, 116f, 34f);
                name.alignment = TextAnchor.MiddleLeft;
                BaseInput[i] = WardrobeArt.Number(row, 90f, 5);
                At((RectTransform)BaseInput[i].transform, 128f, 3f, 90f, 28f);
                BaseInput[i].onEndEdit.AddListener(value =>
                {
                    int number;
                    if (int.TryParse(value, out number)) SetBase(stat, number);
                    else Refresh();
                });
                TotalText[i] = OnlineWindow.Label(row, "", 16, FontStyle.Bold, WardrobeLook.Bright);
                At(TotalText[i].rectTransform, 232f, 0f, 80f, 34f);
                var minus = Arrow(row, "−", () => Lower(stat));
                At((RectTransform)minus.transform, SideW - 24f - 84f, 3f, 38f, 28f);
                var plus = Arrow(row, "+", () => Raise(stat));
                At((RectTransform)plus.transform, SideW - 24f - 42f, 3f, 38f, 28f);
                y += 36f;
            }
        }

        private static void BuildStage()
        {
            float cols = StageW - 2f * 18f - Cell;
            for (int i = 0; i < LeftSlots.Length; i++) MakeCell(LeftSlots[i], 18f, 18f + i * (Cell + Gap));
            for (int i = 0; i < RightSlots.Length; i++) MakeCell(RightSlots[i], 18f + cols, 18f + i * (Cell + Gap));
            float relicW = RelicSlots.Length * Cell + (RelicSlots.Length - 1) * Gap;
            float relicX = (StageW - relicW) * 0.5f;
            for (int i = 0; i < RelicSlots.Length; i++) MakeCell(RelicSlots[i], relicX + i * (Cell + Gap), 18f);
            float handW = HandSlots.Length * Cell + (HandSlots.Length - 1) * Gap;
            float handX = (StageW - handW) * 0.5f;
            float handY = 18f + 6f * (Cell + Gap);
            for (int i = 0; i < HandSlots.Length; i++) MakeCell(HandSlots[i], handX + i * (Cell + Gap), handY);

            float boxX = 18f + Cell + Gap;
            float boxY = 18f + Cell + Gap;
            var boxGo = new GameObject("doll", typeof(RectTransform), typeof(RectMask2D));
            boxGo.transform.SetParent(_stage, false);
            _dollBox = (RectTransform)boxGo.transform;
            At(_dollBox, boxX, boxY, StageW - 2f * boxX, handY - boxY - Gap);

            var dollGo = new GameObject("picture", typeof(RectTransform), typeof(RawImage));
            dollGo.transform.SetParent(_dollBox, false);
            _doll = dollGo.GetComponent<RawImage>();
            _doll.raycastTarget = false;
            _doll.enabled = false;

            _dollNote = OnlineWindow.Label(_dollBox, "", 14, FontStyle.Normal, WardrobeLook.Label);
            OnlineWindow.Place(_dollNote.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 10f), new Vector2(-10f, -10f));
            _dollNote.horizontalOverflow = HorizontalWrapMode.Wrap;

            MakePlain();
        }

        private static void BuildResult(RectTransform result)
        {
            float y = 12f;
            Header(result, "Рейтинг", y);
            _rating = OnlineWindow.Label(result, "", 30, FontStyle.Bold, WardrobeLook.Accent);
            At(_rating.rectTransform, ResultW - 180f, y - 4f, 168f, 40f);
            _rating.alignment = TextAnchor.MiddleRight;
            y += 48f;
            _life = Line(result, "Жизнь", y); y += 28f;
            _mana = Line(result, "Мана", y); y += 28f;
            _energy = Line(result, "Энергия", y); y += 36f;
            Header(result, "Броня", y); y += 32f;
            for (int p = 0; p < 5; p++) { ArmorText[p] = Line(result, WardrobeData.ArmorNames[p], y); y += 26f; }
            y += 8f;
            Header(result, "Защита от магии", y); y += 32f;
            for (int m = 0; m < 3; m++) { MagicText[m] = Line(result, WardrobeData.MagicNames[m], y); y += 26f; }
            y += 10f;
            _warn = OnlineWindow.Label(result, "", 13, FontStyle.Normal, WardrobeLook.Bad);
            At(_warn.rectTransform, 12f, y, ResultW - 24f, BodyH - y - 10f);
            _warn.alignment = TextAnchor.UpperLeft;
            _warn.horizontalOverflow = HorizontalWrapMode.Wrap;
            _warn.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static void MakeCell(int slot, float x, float y)
        {
            var go = new GameObject("slot" + slot, typeof(RectTransform));
            go.transform.SetParent(_stage, false);
            var rt = (RectTransform)go.transform;
            At(rt, x, y, Cell, Cell);
            Cells[slot] = rt;
        }

        private static void MakePlain()
        {
            foreach (var pair in Cells) MakePlainCell(pair.Key, pair.Value);
        }

        private static void MakePlainCell(int slot, RectTransform cell)
        {
            if (Plain.ContainsKey(slot)) return;
            var frameGo = new GameObject("frame", typeof(RectTransform), typeof(Image), typeof(Button), typeof(EventTrigger));
            frameGo.transform.SetParent(cell, false);
            OnlineWindow.Place((RectTransform)frameGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var frame = frameGo.GetComponent<Image>();
            frame.sprite = OnlineWindow.Rounded(10);
            frame.type = Image.Type.Sliced;
            frame.color = WardrobeLook.FieldEdge;
            var button = frameGo.GetComponent<Button>();
            button.targetGraphic = frame;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => Choose(slot));
            var trigger = frameGo.GetComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(data => Tip(slot));
            trigger.triggers.Add(enter);
            var leave = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            leave.callback.AddListener(data => Untip());
            trigger.triggers.Add(leave);
            var innerGo = new GameObject("inner", typeof(RectTransform), typeof(Image));
            innerGo.transform.SetParent(frameGo.transform, false);
            OnlineWindow.Place((RectTransform)innerGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var inner = innerGo.GetComponent<Image>();
            inner.sprite = OnlineWindow.Rounded(9);
            inner.type = Image.Type.Sliced;
            inner.color = WardrobeLook.Field;
            inner.raycastTarget = false;
            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(innerGo.transform, false);
            OnlineWindow.Place((RectTransform)iconGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(5f, 5f), new Vector2(-5f, -5f));
            var icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Plain[slot] = icon;
        }

        private static void Tip(int slot)
        {
            Untip();
            RectTransform cell;
            if (_panelGo == null || !Cells.TryGetValue(slot, out cell) || cell == null) return;
            WardrobeThing thing;
            string raw;
            string title, about, gives = null;
            Color color;
            if (S.Worn.TryGetValue(slot, out thing) && thing != null)
            {
                title = thing.Name;
                color = WardrobeData.RarityColor(thing.Rarity);
                var head = new StringBuilder("ур. ").Append(thing.Level);
                string rarity = WardrobeData.RarityName(thing.Rarity);
                if (rarity.Length > 0) head.Append(" · ").Append(rarity);
                if (!S.Fits(thing))
                {
                    var add = new int[7];
                    S.Missing(thing, add);
                    head.Append(" · не действует: не хватает ").Append(Gaps(add));
                }
                about = head.ToString();
                gives = WardrobePicker.Gives(thing);
            }
            else if (S.Unknown.TryGetValue(slot, out raw))
            {
                title = WardrobeState.Title(raw);
                color = WardrobeLook.Label;
                about = "Этой вещи нет в базе, в расчёт она не идёт";
            }
            else
            {
                title = WardrobeData.SlotName(slot);
                color = WardrobeLook.Bright;
                about = "Пусто — щёлкни, чтобы выбрать вещь";
            }

            const float Width = 300f;
            _tipGo = new GameObject("QoLWardrobeTip", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _tipGo.transform.SetParent(_panelGo.transform, false);
            var rt = (RectTransform)_tipGo.transform;
            float stageX = 16f + SideW + 16f;
            float cx = stageX + cell.anchoredPosition.x;
            float cy = Head - cell.anchoredPosition.y;
            float x = cx + Cell + 10f;
            if (x + Width > stageX + StageW) x = cx - 10f - Width;
            At(rt, x, cy, Width, 40f);
            var back = _tipGo.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            back.color = WardrobeLook.Popup;
            back.raycastTarget = false;
            var edge = _tipGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var layout = _tipGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _tipGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            TipLine(title, 15, FontStyle.Bold, color);
            TipLine(about, 12, FontStyle.Normal, WardrobeLook.Faint);
            if (!string.IsNullOrEmpty(gives)) TipLine(gives, 13, FontStyle.Normal, WardrobeLook.Label);
        }

        private static void TipLine(string text, int size, FontStyle style, Color color)
        {
            var label = OnlineWindow.Label(_tipGo.transform, text, size, style, color);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
        }

        private static void Untip()
        {
            if (_tipGo != null) UnityEngine.Object.Destroy(_tipGo);
            _tipGo = null;
        }

        private static void Paint()
        {
            bool pending = false;
            foreach (var pair in Plain)
            {
                WardrobeThing thing;
                var icon = pair.Value;
                var holder = icon.transform.parent != null ? icon.transform.parent.parent : null;
                var frame = holder != null ? holder.GetComponent<Image>() : null;
                if (S.Worn.TryGetValue(pair.Key, out thing) && thing != null)
                {
                    icon.sprite = WardrobeIcons.Get(thing.Image);
                    icon.enabled = icon.sprite != null;
                    icon.color = Color.white;
                    if (icon.sprite == null) pending = true;
                    if (frame != null) frame.color = S.Fits(thing) ? (Color)WardrobeData.RarityColor(thing.Rarity) : WardrobeLook.Bad;
                }
                else
                {
                    icon.sprite = SlotSprite(pair.Key);
                    icon.enabled = icon.sprite != null;
                    icon.color = new Color(1f, 1f, 1f, 0.28f);
                    if (frame != null) frame.color = S.Unknown.ContainsKey(pair.Key) ? WardrobeLook.Bad : WardrobeLook.FieldEdge;
                }
            }
            _paintAt = pending ? Time.unscaledTime + 0.3f : 0f;
        }

        private static Sprite SlotSprite(int slot)
        {
            try { return AtlasUtils.getSlotIconSprite((ESlots.SlotType)slot); }
            catch { return null; }
        }

        private static void Choose(int slot)
        {
            if (_canvasGo == null || _side == null) return;
            Untip();
            CloseMenu();
            WardrobeArt.Close();
            WardrobeThing worn;
            string raw;
            if (S.Worn.TryGetValue(slot, out worn) && worn != null)
            {
                if (worn.Art && WardrobeData.ArtKinds(slot).Count > 0)
                {
                    WardrobePicker.Close();
                    WardrobeArt.Pop((RectTransform)_panelGo.transform, slot, 16f + SideW + 16f, Head, StageW, BodyH);
                    return;
                }
                Menu(slot, worn.Name, WardrobeData.RarityColor(worn.Rarity), "Снять");
                return;
            }
            if (S.Unknown.TryGetValue(slot, out raw))
            {
                Menu(slot, WardrobeState.Title(raw) + " — нет в базе", WardrobeLook.Label, "Убрать");
                return;
            }
            WardrobePicker.Open(_side, slot);
        }

        private static void Compare()
        {
            if (_panelGo == null) return;
            CloseMenu();
            WardrobeArt.Close();
            if (WardrobeCompare.IsOpen) { WardrobeCompare.Close(); return; }
            float x = 16f + SideW + 16f;
            WardrobeCompare.Open((RectTransform)_panelGo.transform, x, Head, PanelW - x - 16f, BodyH);
        }

        internal static void Replace(int slot)
        {
            CloseMenu();
            if (_canvasGo != null && _side != null) WardrobePicker.Open(_side, slot);
        }

        private static void Menu(int slot, string name, Color32 color, string offText)
        {
            RectTransform cell;
            if (_panelGo == null || !Cells.TryGetValue(slot, out cell) || cell == null) { WardrobePicker.Open(_side, slot); return; }
            const float Width = 260f;
            const float Height = 116f;
            float stageX = 16f + SideW + 16f;
            float cx = stageX + cell.anchoredPosition.x;
            float cy = Head - cell.anchoredPosition.y;
            float x = cx + Cell + 8f;
            if (x + Width > stageX + StageW) x = cx - 8f - Width;
            float y = Mathf.Clamp(cy, Head, Head + BodyH - Height);

            _menuGo = new GameObject("QoLWardrobeMenu", typeof(RectTransform), typeof(Image), typeof(Button));
            _menuGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)_menuGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _menuGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            _menuGo.GetComponent<Button>().onClick.AddListener(() => CloseMenu());

            var box = Box(_menuGo.transform, "menu", x, y, Width, Height, WardrobeLook.Popup, 10);
            box.GetComponent<Image>().raycastTarget = true;
            var edge = box.gameObject.AddComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var title = OnlineWindow.Label(box, name, 15, FontStyle.Bold, color);
            At(title.rectTransform, 12f, 8f, Width - 24f, 48f);
            title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 10;
            title.resizeTextMaxSize = 15;
            float bw = (Width - 24f - 8f) / 2f;
            Place(GameButton(box, offText, () => { CloseMenu(); Pick(slot, null, false); }, true), 12f, 62f, bw, 42f);
            Place(GameButton(box, "Заменить", () => Replace(slot), false), 12f + bw + 8f, 62f, bw, 42f);
        }

        private static bool CloseMenu()
        {
            if (_menuGo == null) return false;
            UnityEngine.Object.Destroy(_menuGo);
            _menuGo = null;
            return true;
        }

        private static void Refresh()
        {
            if (_canvasGo == null) return;
            var race = S.Race;
            var klass = S.Klass;
            var sub = S.Sub;
            var rank = S.RankInfo;
            if (_race != null) _race.text = race != null ? race.Name : "?";
            if (_gender != null) _gender.text = S.Gender == 2 ? "женский" : "мужской";
            if (_level != null) _level.text = S.Level.ToString();
            if (_class != null) _class.text = klass != null ? klass.Name : "?";
            if (_sub != null) _sub.text = sub != null ? sub.Name + " (" + sub.Level + ")" : (S.Level < 8 ? "с 8 уровня" : "нет");
            if (_rank != null) _rank.text = S.ClassId == WardrobeData.Ranger ? "рейнджеру нельзя" : (rank != null ? rank.Name : "нет");

            int free = S.Free;
            if (_free != null)
            {
                _free.text = "Свободно: " + free;
                _free.color = free < 0 ? WardrobeLook.Bad : WardrobeLook.Accent;
            }
            for (int i = 0; i < 7; i++)
            {
                string value = S.Base(i).ToString();
                if (BaseInput[i] != null && !BaseInput[i].isFocused && BaseInput[i].text != value) BaseInput[i].SetTextWithoutNotify(value);
                if (TotalText[i] != null) TotalText[i].text = S.Total(i).ToString();
            }

            if (_rating != null) _rating.text = S.Rating.ToString();
            if (_life != null) _life.text = S.Life.ToString();
            if (_mana != null) _mana.text = S.Mana.ToString();
            if (_energy != null) _energy.text = S.Energy.ToString();
            for (int p = 0; p < 5; p++) if (ArmorText[p] != null) ArmorText[p].text = S.Armor(p).ToString();
            for (int m = 0; m < 3; m++) if (MagicText[m] != null) MagicText[m].text = S.Magic(m).ToString();

            if (_warn != null)
            {
                var text = new StringBuilder();
                if (S.Short) text.Append("На требования подкласса не хватает очков уровня.").Append('\n');
                foreach (var pair in S.Worn)
                {
                    var thing = pair.Value;
                    if (S.Fits(thing)) continue;
                    var add = new int[7];
                    S.Missing(thing, add);
                    text.Append("Не действует «").Append(thing.Name).Append("»: не хватает ").Append(Gaps(add)).Append('\n');
                }
                if (S.Unknown.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var pair in S.Unknown) names.Add(WardrobeData.SlotName(pair.Key).ToLowerInvariant() + " «" + WardrobeState.Title(pair.Value) + "»");
                    text.Append("Нет в базе вещей, в расчёт не идут: ").Append(string.Join(", ", names.ToArray()))
                        .Append(WardrobeUpdate.Busy ? ". Обновляю базу…" : ". Вещи сохранены в манекене, «Обновить вещи» может их найти.").Append('\n');
                }
                _warn.text = text.ToString();
            }
        }

        private static void Order()
        {
            if (_doll == null) return;
            if (!WardrobeDoll.Available())
            {
                _doll.enabled = false;
                _dollNote.text = "Куклу рисует плагин NewAge2D, а он не установлен.";
                return;
            }
            var wear = new List<KeyValuePair<int, WardrobeThing>>();
            var sign = new StringBuilder();
            sign.Append(S.RaceId).Append('/').Append(S.Gender);
            foreach (int slot in VisualSlots)
            {
                WardrobeThing thing;
                if (!S.Worn.TryGetValue(slot, out thing) || thing == null) continue;
                wear.Add(new KeyValuePair<int, WardrobeThing>(slot, thing));
                sign.Append('|').Append(slot).Append(':').Append(thing.Image);
            }
            string key = sign.ToString();
            if (key == _dollSign) return;
            _dollSign = key;
            _dollJob = WardrobeDoll.Request(S.RaceId, S.Gender, DollScale, wear);
            if (_dollJob == 0) { _dollNote.text = "Кукла не рисуется, подробности в журнале мода."; return; }
            if (!_doll.enabled) _dollNote.text = "Рисую куклу…";
        }

        private static void Show(WardrobePicture picture)
        {
            if (_doll == null || _dollBox == null) return;
            if (picture.Rgba == null || picture.Width <= 0 || picture.Height <= 0)
            {
                _doll.enabled = false;
                _dollNote.text = "Кукла не нарисовалась: " + (picture.Error ?? "нет картинки");
                Plugin.Warn("[переодевалка] кукла: " + (picture.Error ?? "нет картинки"));
                return;
            }
            try
            {
                var texture = new Texture2D(picture.Width, picture.Height, TextureFormat.RGBA32, false);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.LoadRawTextureData(picture.Rgba);
                texture.Apply(false, true);
                if (_texture != null) UnityEngine.Object.Destroy(_texture);
                _texture = texture;
                _doll.texture = texture;
                _doll.enabled = true;
                _dollNote.text = picture.Error != null ? picture.Error : "";

                float boxH = _dollBox.rect.height > 1f ? _dollBox.rect.height : 400f;
                float body = picture.BodyHeight > 1f ? picture.BodyHeight : picture.Height * 0.7f;
                float k = boxH * 0.8f / body;
                var rt = _doll.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(picture.PivotX, picture.PivotY);
                rt.sizeDelta = new Vector2(picture.Width * k, picture.Height * k);
                rt.anchoredPosition = new Vector2(0f, boxH * 0.06f);
            }
            catch (Exception e)
            {
                _doll.enabled = false;
                _dollNote.text = "Кукла не нарисовалась: " + e.Message;
                Plugin.Warn("[переодевалка] кукла в окне: " + e.Message);
            }
        }

        private static void Race(int step)
        {
            var order = WardrobeData.RaceOrder;
            int at = Array.IndexOf(order, S.RaceId);
            at = ((at < 0 ? 0 : at) + step + order.Length) % order.Length;
            S.RaceId = order[at];
            S.Undress();
            S.Minimum();
            Changed(true);
        }

        private static void Klass(int step)
        {
            var race = S.Race;
            if (race == null || race.Classes.Count == 0) return;
            int at = race.Classes.IndexOf(S.ClassId);
            at = ((at < 0 ? 0 : at) + step + race.Classes.Count) % race.Classes.Count;
            S.ClassId = race.Classes[at];
            Changed(false);
        }

        private static void Turn(Action<int> action, int step) => action(step);

        private static void StepSub(int step)
        {
            int top = S.TopSub;
            if (top <= 0) { S.SubN = 0; return; }
            S.SubN = (S.SubN + step + top + 1) % (top + 1);
            S.Settle(true);
        }

        private static void StepRank(int step)
        {
            int count = WardrobeData.Ranks.Count;
            if (count == 0) return;
            for (int tries = 0; tries < count; tries++)
            {
                int next = (S.Rank + step * (tries + 1) + count * 4) % count;
                if (S.CanRank(next)) { S.Rank = next; return; }
            }
            Notice.Show(S.ClassId == WardrobeData.Ranger ? "Рейнджеру крепость недоступна" : "Для крепости не хватает сложения", 4f);
        }

        private static void Raise(int i)
        {
            if (S.Free <= 0) { Notice.Show("Свободных очков нет", 3f); return; }
            S.Dist[i]++;
            Changed(true);
        }

        private static void SetBase(int i, int want)
        {
            int now = S.Base(i);
            if (want == now) { Refresh(); return; }
            if (want > now)
            {
                int room = S.Free;
                if (room <= 0) { Notice.Show("Свободных очков нет", 3f); Refresh(); return; }
                int add = Math.Min(want - now, room);
                if (add < want - now) Notice.Show("Свободных очков хватило до " + (now + add), 4f);
                S.Dist[i] += add;
                Changed(true);
                return;
            }
            int target = Math.Max(want, S.Floor(i));
            string why = null;
            if (target > want)
            {
                var sub = S.Sub;
                why = sub != null && S.Need(i) >= target ? "требования «" + sub.Name + "»" : "база расы";
            }
            string blocker = S.Blocker(i, target);
            if (blocker != null)
            {
                why = blocker;
                while (target < now && S.Blocker(i, target) != null) target++;
            }
            if (why != null) Notice.Show("Ниже " + target + " не опустить: " + why, 4f);
            if (target >= now) { Refresh(); return; }
            S.Dist[i] -= now - target;
            Changed(true);
        }

        private static void Lower(int i)
        {
            int now = S.Base(i);
            int step = now > S.Floor(i) ? 1 : 0;
            if (step <= 0)
            {
                var sub = S.Sub;
                Notice.Show(sub != null && S.Need(i) >= now
                    ? "Ниже требований «" + sub.Name + "» не опустить: " + WardrobeData.StatNames[i].ToLowerInvariant() + " " + S.Need(i)
                    : "Ниже базы расы не опустить", 4f);
                return;
            }
            string blocker = S.Blocker(i, now - step);
            if (blocker != null) { Notice.Show("Не опустить: " + blocker, 4f); return; }
            S.Dist[i] -= step;
            Changed(true);
        }

        private static void Ask(string text, Action yes) => Ask(text, yes, "Распределить");

        private static void Ask(string text, Action yes, string yesText) => Dialog(text, yesText, null, value => yes());


        private static void Dialog(string text, string yesText, string placeholder, Action<string> ok)
        {
            CloseAsk();
            if (_panelGo == null) return;
            _askGo = new GameObject("ask", typeof(RectTransform), typeof(Image));
            _askGo.transform.SetParent(_panelGo.transform, false);
            var rt = (RectTransform)_askGo.transform;
            OnlineWindow.Place(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _askGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            bool typed = placeholder != null;
            var boxGo = new GameObject("box", typeof(RectTransform), typeof(Image), typeof(Outline));
            boxGo.transform.SetParent(_askGo.transform, false);
            var box = (RectTransform)boxGo.transform;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(560f, typed ? 250f : 210f);
            var img = boxGo.GetComponent<Image>();
            img.color = WardrobeLook.Popup;
            img.sprite = OnlineWindow.Rounded(16);
            img.type = Image.Type.Sliced;
            var ol = boxGo.GetComponent<Outline>();
            ol.effectColor = WardrobeLook.Edge;
            ol.effectDistance = new Vector2(1f, -1f);

            var label = OnlineWindow.Label(box, text, 17, FontStyle.Normal, WardrobeLook.Bright);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(24f, typed ? 118f : 70f), new Vector2(-24f, -20f));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;

            InputField input = null;
            if (typed)
            {
                input = OnlineWindow.MakeInput(box, 400f, placeholder);
                var irt = (RectTransform)input.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0f);
                irt.pivot = new Vector2(0.5f, 0f);
                irt.sizeDelta = new Vector2(400f, 36f);
                irt.anchoredPosition = new Vector2(0f, 74f);
                input.characterLimit = 40;
                WardrobeLook.Style(input);
                input.ActivateInputField();
            }

            Action confirm = () =>
            {
                string value = input != null ? (input.text ?? "").Trim() : "";
                if (input != null && value.Length == 0) return;
                CloseAsk();
                ok?.Invoke(value);
            };
            var yesBtn = OnlineWindow.MakeGameButton(box, yesText, 200f, 44f, confirm);
            var yrt = (RectTransform)yesBtn.transform;
            yrt.anchorMin = yrt.anchorMax = new Vector2(0.5f, 0f);
            yrt.pivot = new Vector2(0.5f, 0f);
            yrt.sizeDelta = new Vector2(200f, 44f);
            yrt.anchoredPosition = new Vector2(-110f, 18f);
            var noBtn = OnlineWindow.MakeGameButton(box, "Отмена", 200f, 44f, CloseAsk);
            var nrt = (RectTransform)noBtn.transform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0f);
            nrt.pivot = new Vector2(0.5f, 0f);
            nrt.sizeDelta = new Vector2(200f, 44f);
            nrt.anchoredPosition = new Vector2(110f, 18f);
        }

        private static void CloseAsk()
        {
            if (_askGo != null) UnityEngine.Object.Destroy(_askGo);
            _askGo = null;
        }

        internal static RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color color, int radius)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            At(rt, x, y, w, h);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.sprite = OnlineWindow.Rounded(radius);
            img.type = Image.Type.Sliced;
            return rt;
        }

        internal static void At(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            if (rt != null) At(rt, x, y, w, h);
        }

        private static void Header(RectTransform parent, string text, float y)
        {
            var label = OnlineWindow.Label(parent, text, 16, FontStyle.Bold, WardrobeLook.Accent);
            At(label.rectTransform, 14f, y, 260f, 30f);
            label.alignment = TextAnchor.MiddleLeft;
        }

        private static Text Line(RectTransform parent, string name, float y)
        {
            var label = OnlineWindow.Label(parent, name, 15, FontStyle.Normal, WardrobeLook.Label);
            At(label.rectTransform, 18f, y, 170f, 26f);
            label.alignment = TextAnchor.MiddleLeft;
            var value = OnlineWindow.Label(parent, "", 16, FontStyle.Bold, WardrobeLook.Bright);
            At(value.rectTransform, ResultW - 132f, y, 116f, 26f);
            value.alignment = TextAnchor.MiddleRight;
            return value;
        }

        private static Text Cycler(RectTransform parent, string name, float y, Action<int> step)
        {
            var label = OnlineWindow.Label(parent, name, 15, FontStyle.Normal, WardrobeLook.Label);
            At(label.rectTransform, 16f, y, 110f, 34f);
            label.alignment = TextAnchor.MiddleLeft;
            float x = 126f;
            float right = SideW - 12f;
            var back = Arrow(parent, "‹", () => step(-1));
            At((RectTransform)back.transform, x, y + 2f, 34f, 30f);
            var next = Arrow(parent, "›", () => step(1));
            At((RectTransform)next.transform, right - 34f, y + 2f, 34f, 30f);
            var field = Box(parent, name + "Value", x + 38f, y + 2f, right - 34f - 4f - (x + 38f), 30f, WardrobeLook.Field, 8);
            var value = OnlineWindow.Label(field, "", 15, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(value.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(4f, 0f), new Vector2(-4f, 0f));
            value.resizeTextForBestFit = true;
            value.resizeTextMinSize = 10;
            value.resizeTextMaxSize = 15;
            return value;
        }

        internal static RectTransform GameButton(Transform host, string text, Action click, bool red)
        {
            var go = new GameObject(red ? "red" : "button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(host, false);
            var image = go.GetComponent<Image>();
            image.sprite = OnlineWindow.Rounded(8);
            image.type = Image.Type.Sliced;
            image.color = red ? WardrobeLook.Danger : WardrobeLook.Button;
            var label = OnlineWindow.Label(go.transform, text, 15, FontStyle.Bold, red ? WardrobeLook.DangerText : WardrobeLook.Bright);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 4f), new Vector2(-10f, -4f));
            label.alignment = TextAnchor.MiddleCenter;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 15;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                try { click(); }
                catch (Exception e) { Plugin.Warn("[переодевалка] кнопка: " + e.Message); }
            });
            return (RectTransform)go.transform;
        }

        internal static Button Arrow(Transform parent, string text, Action click)
        {
            var go = new GameObject("arrow", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = OnlineWindow.Rounded(8);
            img.type = Image.Type.Sliced;
            img.color = WardrobeLook.Button;
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                try { click(); }
                catch (Exception e) { Plugin.Warn("[переодевалка] кнопка: " + e.Message); }
            });
            var label = OnlineWindow.Label(go.transform, text, 18, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return button;
        }
    }

    internal sealed class WardrobeTicker : MonoBehaviour
    {
        private void Update()
        {
            try { Wardrobe.Tick(); }
            catch (Exception e) { Plugin.Warn("[переодевалка] такт: " + e.Message); }
        }
    }

    internal static class WardrobeIcons
    {
        private static readonly Dictionary<string, Sprite> Sharp = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Loading = new HashSet<string>();

        internal static Sprite Get(string image)
        {
            if (string.IsNullOrEmpty(image)) return null;
            Sprite sprite;
            if (Sharp.TryGetValue(image, out sprite) && sprite != null) return sprite;
            try
            {
                sprite = AtlasUtils.GetThingSprite(image);
                if (sprite != null) return sprite;
            }
            catch { }
            Load(image);
            return null;
        }

        private static string Folder => System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.CachePath, "NewAgeQoL"), "things");

        private static void Load(string image)
        {
            if (!Loading.Add(image)) return;
            string file = null;
            try
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(image, "^[A-Za-z0-9_\\-]+$"))
                {
                    file = System.IO.Path.Combine(Folder, image + ".png");
                    if (System.IO.File.Exists(file) && Make(image, System.IO.File.ReadAllBytes(file))) return;
                }
            }
            catch (Exception e) { Plugin.Trace("[переодевалка] картинка «" + image + "» из кэша: " + e.Message); }
            if (Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(Fetch(image, file));
        }

        private static System.Collections.IEnumerator Fetch(string image, string file)
        {
            var req = UnityEngine.Networking.UnityWebRequest.Get("https://files.nura.biz/site/images/things100x100/" + image + ".png");
            req.timeout = 30;
            yield return req.SendWebRequest();
            byte[] data = req.responseCode == 200 && string.IsNullOrEmpty(req.error) && req.downloadHandler != null ? req.downloadHandler.data : null;
            string error = req.error;
            req.Dispose();
            if (data == null || !Make(image, data))
            {
                Plugin.Trace("[переодевалка] картинка «" + image + "» не загрузилась: " + error);
                yield break;
            }
            if (file == null) yield break;
            try
            {
                System.IO.Directory.CreateDirectory(Folder);
                System.IO.File.WriteAllBytes(file, data);
            }
            catch (Exception e) { Plugin.Trace("[переодевалка] картинка «" + image + "» не сохранилась: " + e.Message); }
        }

        private static bool Make(string image, byte[] data)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, data))
            {
                UnityEngine.Object.Destroy(texture);
                return false;
            }
            texture.name = "qol_thing_" + image;
            Sharp[image] = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            return true;
        }
    }

    [HarmonyPatch(typeof(SetupDialog), "Start")]
    public static class WardrobeButtonPatch
    {
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(SetupDialog __instance)
        {
            try
            {
                if (!SideButtons.InWorld()) return;
                var reset = AccessTools.Field(typeof(SetupDialog), "ResetChatButton")?.GetValue(__instance) as Button;
                if (reset == null) return;
                var src = (RectTransform)reset.transform;
                if (src.parent != null && src.parent.Find("QoLWardrobeButton") != null) return;
                Clone(reset, "QoLWardrobeButton", 1, "Переодевалка", Wardrobe.Open);
                Clone(reset, "QoLKuButton", 2, "Калькулятор КУ", KuCalc.Open);
                var last = Clone(reset, "QoLCraftButton", 3, "Калькулятор крафта", CraftCalc.Show);
                __instance.StartCoroutine(Fit(__instance, last, 3));
            }
            catch (Exception e) { Plugin.Fault("[переодевалка] кнопка в настройках: " + e.Message); }
        }

        private static RectTransform Clone(Button reset, string name, int step, string text, Action open)
        {
            var src = (RectTransform)reset.transform;
            var go = UnityEngine.Object.Instantiate(reset.gameObject, src.parent);
            Clones.StripHotkeys(go, reset.gameObject);
            go.name = name;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax; rt.pivot = src.pivot;
            rt.sizeDelta = src.sizeDelta;
            rt.localScale = src.localScale;
            rt.anchoredPosition = src.anchoredPosition - new Vector2(0f, step * (src.rect.height + 10f));
            foreach (var t in go.GetComponentsInChildren<Text>(true)) t.text = text;
            var btn = go.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                try { UnityEngine.Object.FindObjectOfType<SetupDialog>()?.Close(); }
                catch { }
                open();
            });
            return rt;
        }

        private static IEnumerator Fit(SetupDialog dialog, RectTransform button, int steps)
        {
            float grown = 0f;
            for (int step = 0; step < 12; step++)
            {
                yield return null;
                if (dialog == null || button == null) yield break;
                var column = button.parent as RectTransform;
                var root = dialog.transform as RectTransform;
                if (column == null || root == null) yield break;
                var home = root.parent as RectTransform;
                if (home != null && home.GetComponent<LayoutGroup>() != null) yield break;
                float h = button.rect.height;
                if (h < 5f || column.rect.height < 5f) continue;
                var mine = new Vector3[4];
                var box = new Vector3[4];
                button.GetWorldCorners(mine);
                column.GetWorldCorners(box);
                float scale = root.lossyScale.y > 0f ? root.lossyScale.y : 1f;
                float over = (box[0].y - mine[0].y) / scale + 12f;
                if (step == 0)
                    Plugin.Trace("[переодевалка] кнопка в настройках: колонка " + column.name + " высота " + column.rect.height.ToString("0")
                                 + ", якоря кнопки " + button.anchorMin + "-" + button.anchorMax + ", вылезает на " + over.ToString("0"));
                if (over <= 1f) yield break;
                if (grown >= steps * (h + 10f)) yield break;
                float by = Mathf.Min(over, steps * (h + 10f) - grown);
                root.sizeDelta = new Vector2(root.sizeDelta.x, root.sizeDelta.y + by);
                grown += by;
                Plugin.Trace("[переодевалка] окно настроек выросло на " + by.ToString("0") + ", всего " + grown.ToString("0"));
            }
        }
    }
}
