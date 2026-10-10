using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Common;
using Transport.Messages.Common.Descriptions.Dialogs;
using Transport.Messages.Responses.Combat;
using Transport.Messages.Responses.Locations;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Loot
    {
        private const float Life = 10f;
        private const float Fade = 0.6f;
        private const float Leave = 0f;
        private const float Wide = 300f;
        private const float Folded = 230f;
        private const float Pad = 12f;
        private const float Pic = 30f;
        private const int Most = 4;
        private const int RepairDone = 361;

        private static readonly Color Exp = new Color32(132, 190, 255, 255);

        private sealed class Plate
        {
            internal GameObject Go;
            internal RectTransform Rt;
            internal CanvasGroup Veil;
            internal float Until;
            internal bool Shut;
            internal Text Title;
            internal string Head;
            internal LayoutElement Size;
            internal readonly List<GameObject> Body = new List<GameObject>();
            internal GameObject Wear;
            internal readonly HashSet<long> Worn = new HashSet<long>();
            internal readonly List<KeyValuePair<Image, string>> Pics = new List<KeyValuePair<Image, string>>();
        }

        private sealed class Result
        {
            internal bool Win;
            internal int Exp;
            internal double Cash;
            internal readonly List<DialogDescriptionItemMessage> Items = new List<DialogDescriptionItemMessage>();
            internal readonly List<DialogDescriptionItemMessage> Worn = new List<DialogDescriptionItemMessage>();
        }

        private static GameObject _canvasGo;
        private static RectTransform _stack;
        private static readonly List<Plate> Plates = new List<Plate>();
        private static readonly List<Result> Waiting = new List<Result>();
        private static float _leaveAt;
        private static readonly HashSet<long> Repairing = new HashSet<long>();
        private static AccessTools.FieldRef<SceneLoader, LocationResponseMessage> _delayed;
        private static bool _looked;
        private static object _on;

        internal static bool Take(CombatResultResponseMessage message)
        {
            if (message == null || message.Participants == null) return false;
            var me = CombatResultDialog.FindMyResult(message.Participants);
            if (me == null) return false;
            var result = new Result { Win = me.Team == message.WinnerTeam, Exp = me.Exp, Cash = me.Cash };
            if (message.Items != null)
                foreach (var one in message.Items) if (one != null) result.Items.Add(one);
            if (message.WearWarningInventory != null)
                foreach (var one in message.WearWarningInventory) if (one != null) result.Worn.Add(one);
            Waiting.Add(result);
            _leaveAt = Time.unscaledTime + Leave;
            Plugin.Trace("[loot] fight over: " + (result.Win ? "win" : "loss") + ", exp " + result.Exp + ", cash " + result.Cash
                + ", items " + result.Items.Count + ", worn " + result.Worn.Count + "; result window skipped");
            return true;
        }

        internal static void Tick()
        {
            try
            {
                Listen();
                if (!SideButtons.InWorld()) { _leaveAt = 0f; Waiting.Clear(); Clear(); return; }
                bool fighting = SideButtons.InCombat();
                if (_leaveAt > 0f && Time.unscaledTime >= _leaveAt) Go();
                if (fighting)
                {
                    if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
                    return;
                }
                if (Waiting.Count > 0)
                {
                    Build();
                    foreach (var one in Waiting) Add(one);
                    Waiting.Clear();
                }
                if (_canvasGo == null) return;
                if (Plates.Count == 0) { if (_canvasGo.activeSelf) _canvasGo.SetActive(false); return; }
                if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
                Place();
                Age();
            }
            catch (Exception e) { Plugin.Trace("[loot] " + e.Message); }
        }

        private static void Go()
        {
            try
            {
                if (!SideButtons.InCombat()) { _leaveAt = 0f; return; }
                var loader = DependencyContainer.GetContainer()?.Resolve<ISceneLoader>();
                if (loader == null) { _leaveAt = 0f; Plugin.Fault("[loot] no scene loader, cannot leave the fight"); return; }
                if (!Ready(loader)) return;
                _leaveAt = 0f;
                loader.LoadDelayedLocation();
                Plugin.Trace("[loot] leaving the fight location");
            }
            catch (Exception e) { _leaveAt = 0f; Plugin.Fault("[loot] leaving the fight: " + e.Message); }
        }

        private static long Key(DialogDescriptionItemMessage one) =>
            ((long)one.InventoryId.GetValueOrDefault() << 32) | (uint)one.SlotId.GetValueOrDefault();

        private static bool Ready(ISceneLoader loader)
        {
            var real = loader as SceneLoader;
            if (real == null) return true;
            if (!_looked)
            {
                _looked = true;
                try { _delayed = AccessTools.FieldRefAccess<SceneLoader, LocationResponseMessage>("_delayedMessage"); }
                catch (Exception e) { Plugin.Trace("[loot] delayed map field: " + e.Message); }
            }
            return _delayed == null || _delayed(real) != null;
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(RepairDone, OnRepaired);
            nc.AddMessageListener(RepairDone, OnRepaired);
            _on = nc;
        }

        private static void OnRepaired(object m)
        {
            if (Repairing.Count == 0) return;
            var done = new HashSet<long>(Repairing);
            Repairing.Clear();
            try
            {
                var answer = m as BooleanStringMessage;
                if (answer == null) return;
                if (!answer.Success)
                {
                    AirMessageScript.ShowErrorNotification(answer.Message);
                    Plugin.Trace("[loot] repair refused: " + answer.Message);
                    return;
                }
                AirMessageScript.ShowSuccessNotification(ResourceStrings.GetString("messages.blacksmith.repairsuccess"));
                foreach (var plate in Plates)
                    if (plate.Wear != null && plate.Worn.IsSubsetOf(done)) { UnityEngine.Object.Destroy(plate.Wear); plate.Wear = null; }
                Plugin.Trace("[loot] worn things repaired");
            }
            catch (Exception e) { Plugin.Trace("[loot] repair answer: " + e.Message); }
        }

        private static void Repair(List<DialogDescriptionItemMessage> worn)
        {
            try
            {
                var ids = new List<int>();
                foreach (var one in worn)
                {
                    int inv = one.InventoryId.GetValueOrDefault();
                    if (inv > 0) ids.Add(inv);
                }
                DialogFactory.ShowPriceConfirmMessageBox("messages.confirmsprice.repair.end_combat_repair.caption", null, answer =>
                {
                    if (answer != EMessageBoxResult.MB_OK) return;
                    var request = new RepairSomeInventoryWithDressOnRequest();
                    foreach (var one in worn) request.AddItem(one.InventoryId, one.SlotId);
                    Repairing.Clear();
                    foreach (var one in worn) Repairing.Add(Key(one));
                    NetworkConnection.Instance.SendRequest(request);
                    Plugin.Trace("[loot] repair of " + worn.Count + " worn things sent");
                }, EPriceKey.RepairSomeThings, ids, "messages.confirmsprice.repair.end_combat_repair.message");
            }
            catch (Exception e) { Plugin.Fault("[loot] repair: " + e.Message); }
        }

        private static void Age()
        {
            float now = Time.unscaledTime;
            for (int i = Plates.Count - 1; i >= 0; i--)
            {
                var plate = Plates[i];
                if (plate.Go == null) { Plates.RemoveAt(i); continue; }
                Show(plate);
                if (plate.Shut) { plate.Veil.alpha = 1f; continue; }
                if (RectTransformUtility.RectangleContainsScreenPoint(plate.Rt, Input.mousePosition, null))
                {
                    plate.Until = Mathf.Max(plate.Until, now + 2f);
                    plate.Veil.alpha = 1f;
                    continue;
                }
                float left = plate.Until - now;
                bool last = i == 0;
                if (left <= 0f) { if (last) Fold(plate, true); else Drop(plate); continue; }
                plate.Veil.alpha = last || left >= Fade ? 1f : left / Fade;
            }
        }

        private static void Show(Plate plate)
        {
            foreach (var pic in plate.Pics)
            {
                var image = pic.Key;
                if (image == null || (image.enabled && !Quickslots.Faded(image.sprite))) continue;
                var sprite = WardrobeIcons.Get(pic.Value);
                if (Quickslots.Faded(sprite)) { image.enabled = false; continue; }
                image.sprite = sprite;
                image.enabled = true;
            }
        }

        private static void Place()
        {
            float wide = HelpColumn.Wide;
            float x = wide > 0f ? -(wide + 8f) : -HelpColumn.SideGap;
            float below = Mathf.Max(Roster.Bottom, FightBoard.Bottom);
            float y = below > 0f ? -(below + 10f) : -HelpColumn.Head;
            var want = new Vector2(x, y);
            if ((_stack.anchoredPosition - want).sqrMagnitude > 0.25f) _stack.anchoredPosition = want;
        }

        private static void Fold(Plate plate, bool shut)
        {
            if (plate.Go == null || plate.Shut == shut) return;
            plate.Shut = shut;
            foreach (var part in plate.Body) if (part != null) part.SetActive(!shut);
            plate.Size.preferredWidth = shut ? Folded : Wide;
            plate.Title.fontSize = shut ? 14 : 16;
            plate.Title.text = shut ? "Последняя добыча" : plate.Head;
            plate.Title.GetComponent<LayoutElement>().preferredWidth = (shut ? Folded : Wide) - Pad * 2f - 26f;
            if (!shut) plate.Until = Time.unscaledTime + Life;
            plate.Veil.alpha = 1f;
            Plugin.Trace("[loot] last loot plate " + (shut ? "folded" : "unfolded"));
        }

        private static void Add(Result result)
        {
            for (int i = Plates.Count - 1; i >= 0; i--) if (Plates[i].Shut) Drop(Plates[i]);
            while (Plates.Count >= Most) Drop(Plates[Plates.Count - 1]);
            var plate = Make(result);
            plate.Until = Time.unscaledTime + Life;
            plate.Go.transform.SetAsFirstSibling();
            Plates.Insert(0, plate);
        }

        private static void Drop(Plate plate)
        {
            Plates.Remove(plate);
            plate.Pics.Clear();
            if (plate.Go != null) UnityEngine.Object.Destroy(plate.Go);
        }

        private static void Clear()
        {
            for (int i = Plates.Count - 1; i >= 0; i--) Drop(Plates[i]);
            if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
        }

        private static Plate Make(Result result)
        {
            var go = new GameObject("loot", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(CanvasGroup));
            go.transform.SetParent(_stack, false);
            var plate = new Plate { Go = go, Rt = (RectTransform)go.transform, Veil = go.GetComponent<CanvasGroup>() };

            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(12);
            back.type = Image.Type.Sliced;

            var edge = go.GetComponent<Outline>();
            edge.effectColor = result.Win ? WardrobeLook.Accent : WardrobeLook.Edge;
            edge.effectDistance = new Vector2(2f, -2f);

            plate.Size = go.GetComponent<LayoutElement>();
            plate.Size.preferredWidth = Wide;

            var column = go.GetComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset((int)Pad, (int)Pad, 8, 10);
            column.spacing = 4f;
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            plate.Head = result.Win ? "Победа" : "Поражение";
            var title = Line(go.transform, plate.Head, 16, FontStyle.Bold, result.Win ? WardrobeLook.Good : WardrobeLook.Bad);
            title.GetComponent<LayoutElement>().preferredWidth = Wide - Pad * 2f - 26f;
            plate.Title = title;
            var close = Shut(go.transform, plate);

            var gains = new List<string>();
            if (result.Exp != 0) gains.Add(Paint("Опыт", WardrobeLook.Label) + " " + Paint(Signed(result.Exp), Exp));
            if (Math.Abs(result.Cash) > 0.0001) gains.Add(Paint("Таллы", WardrobeLook.Label) + " " + Paint(Signed(result.Cash), WardrobeLook.Accent));
            if (gains.Count > 0) Line(go.transform, string.Join("   ", gains.ToArray()), 14, FontStyle.Bold, WardrobeLook.Body);

            if (result.Items.Count > 0)
            {
                Line(go.transform, "Добыча", 12, FontStyle.Normal, WardrobeLook.Faint);
                foreach (var item in result.Items) Row(go.transform, plate, item);
            }
            else if (gains.Count == 0)
            {
                Line(go.transform, "Без добычи", 13, FontStyle.Normal, WardrobeLook.Faint);
            }

            if (result.Worn.Count > 0)
            {
                foreach (var one in result.Worn) plate.Worn.Add(Key(one));
                plate.Wear = Worn(go.transform, result.Worn);
            }
            foreach (Transform part in go.transform)
                if (part.gameObject != title.gameObject && part.gameObject != close) plate.Body.Add(part.gameObject);
            var open = go.AddComponent<Button>();
            open.transition = Selectable.Transition.None;
            open.onClick.AddListener(() => { if (plate.Shut) Fold(plate, false); });
            return plate;
        }

        private static GameObject Shut(Transform host, Plate plate)
        {
            var go = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(24f, 24f);
            rt.anchoredPosition = new Vector2(-6f, -6f);
            var image = go.GetComponent<Image>();
            image.sprite = OnlineWindow.Rounded(8);
            image.type = Image.Type.Sliced;
            image.color = WardrobeLook.Button;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.6f, 0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => Drop(plate));
            var x = OnlineWindow.Label(go.transform, "×", 20, FontStyle.Bold, WardrobeLook.Bright);
            x.raycastTarget = false;
            OnlineWindow.Place(x.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 2f));
            return go;
        }

        private static void Row(Transform host, Plate plate, DialogDescriptionItemMessage item)
        {
            var go = new GameObject("item", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(host, false);
            var row = go.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var holder = new GameObject("pic", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            holder.transform.SetParent(go.transform, false);
            var le = holder.GetComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = Pic;
            le.preferredHeight = le.minHeight = Pic;
            var frame = holder.GetComponent<Image>();
            frame.sprite = OnlineWindow.Rounded(8);
            frame.type = Image.Type.Sliced;
            frame.color = WardrobeLook.Field;
            frame.raycastTarget = false;

            var kind = (EDialogDescriptionItemType)item.ItemType;
            Color tint = item.Frame.HasValue && kind == EDialogDescriptionItemType.ITEM_TYPE_THING
                ? (Color)WardrobeData.RarityColor(item.Frame.Value) : WardrobeLook.Body;
            var label = OnlineWindow.Label(go.transform, Name(item), 13, FontStyle.Bold, tint);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.raycastTarget = false;
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = Wide - Pad * 2f - Pic - 8f;

            Picture(holder.transform, plate, item, kind);

            if (kind == EDialogDescriptionItemType.ITEM_TYPE_THING)
            {
                int thing = item.Id;
                var hit = go.AddComponent<Image>();
                hit.sprite = OnlineWindow.Rounded(8);
                hit.type = Image.Type.Sliced;
                hit.color = Color.white;
                var button = go.AddComponent<Button>();
                button.targetGraphic = hit;
                var colors = button.colors;
                colors.normalColor = new Color(1f, 1f, 1f, 0f);
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
                colors.pressedColor = new Color(1f, 1f, 1f, 0.14f);
                colors.selectedColor = colors.normalColor;
                colors.fadeDuration = 0.08f;
                button.colors = colors;
                button.onClick.AddListener(() => Hint(thing));
                Ask(thing, label, Count(item));
            }
        }

        private static void Picture(Transform holder, Plate plate, DialogDescriptionItemMessage item, EDialogDescriptionItemType kind)
        {
            var go = new GameObject("icon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(holder, false);
            var rt = (RectTransform)go.transform;
            OnlineWindow.Place(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            try
            {
                Sprite sprite = null;
                switch (kind)
                {
                    case EDialogDescriptionItemType.ITEM_TYPE_THING:
                        if (string.IsNullOrEmpty(item.Image)) return;
                        plate.Pics.Add(new KeyValuePair<Image, string>(image, item.Image));
                        Show(plate);
                        return;
                    case EDialogDescriptionItemType.ITEM_TYPE_ENCHANTMENT: sprite = AtlasUtils.GetStateSprite(EStateType.GlobalEnchantment, item.Id); break;
                    case EDialogDescriptionItemType.ITEM_TYPE_PROFESSION: sprite = AtlasUtils.GetProfessionIcon(item.Id); break;
                    case EDialogDescriptionItemType.ITEM_TYPE_CLAN_RATING: sprite = AtlasUtils.GetClanRatingIcon(); break;
                    case EDialogDescriptionItemType.ITEM_TYPE_KARMA: sprite = AtlasUtils.GetKarmaIcon(); break;
                }
                if (sprite == null) return;
                image.sprite = sprite;
                image.enabled = true;
            }
            catch (Exception e) { Plugin.Trace("[loot] icon " + item.Id + ": " + e.Message); }
        }

        private static GameObject Worn(Transform host, List<DialogDescriptionItemMessage> worn)
        {
            var go = new GameObject("worn", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(host, false);
            var column = go.GetComponent<VerticalLayoutGroup>();
            column.spacing = 4f;
            column.padding = new RectOffset(0, 0, 4, 0);
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            var names = new List<string>();
            foreach (var one in worn) names.Add(Known(one.Id));
            var text = Line(go.transform, "Вещи изношены: " + string.Join(", ", names.ToArray()), 13, FontStyle.Bold, WardrobeLook.Bad);
            for (int i = 0; i < worn.Count; i++)
            {
                int at = i;
                Named(worn[i].Id, name =>
                {
                    if (text == null) return;
                    names[at] = name;
                    text.text = "Вещи изношены: " + string.Join(", ", names.ToArray());
                });
            }

            var copy = new List<DialogDescriptionItemMessage>(worn);
            var fix = OnlineWindow.MakeGameButton(go.transform, "Починить", 130f, 28f, () => Repair(copy));
            fix.GetComponent<Image>().color = WardrobeLook.Danger;
            return go;
        }

        private static Text Line(Transform host, string text, int size, FontStyle style, Color color)
        {
            var label = OnlineWindow.Label(host, text, size, style, color);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.raycastTarget = false;
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = Wide - Pad * 2f;
            return label;
        }

        private static string Name(DialogDescriptionItemMessage item)
        {
            switch ((EDialogDescriptionItemType)item.ItemType)
            {
                case EDialogDescriptionItemType.ITEM_TYPE_THING: return Known(item.Id) + Count(item);
                case EDialogDescriptionItemType.ITEM_TYPE_CLAN_RATING: return "Рейтинг клана " + Signed2(item.Value);
                case EDialogDescriptionItemType.ITEM_TYPE_KARMA: return "Карма " + Signed2(item.Value);
                case EDialogDescriptionItemType.ITEM_TYPE_PROFESSION:
                    return (Lang("professions.id" + item.Id + ".name") ?? "Профессия") + " " + Signed2(item.Value);
                case EDialogDescriptionItemType.ITEM_TYPE_ENCHANTMENT: return "Эффект";
                default: return "Награда " + item.Id;
            }
        }

        private static string Count(DialogDescriptionItemMessage item)
        {
            int n = (int)item.Value;
            return n > 1 ? "  ×" + n : "";
        }

        private static string Known(int thing)
        {
            try
            {
                var local = RecipeData.Thing(thing);
                if (local != null && !string.IsNullOrEmpty(local.Name)) return local.Name;
            }
            catch { }
            return "Предмет " + thing;
        }

        private static void Ask(int thing, Text label, string count)
        {
            Named(thing, name =>
            {
                if (label != null) label.text = name + count;
            });
        }

        private static void Named(int thing, Action<string> done)
        {
            try
            {
                var cache = DependencyContainer.GetContainer()?.Resolve<GeneralThingInfoDescriptionManager>();
                if (cache == null) return;
                cache.Get(thing, about =>
                {
                    try
                    {
                        if (about == null || string.IsNullOrEmpty(about.Name)) return;
                        done(about.Name);
                    }
                    catch (Exception e) { Plugin.Trace("[loot] name " + thing + ": " + e.Message); }
                });
            }
            catch (Exception e) { Plugin.Trace("[loot] name " + thing + ": " + e.Message); }
        }

        private static void Hint(int thing)
        {
            try
            {
                DependencyContainer.GetContainer().Resolve<ThingHintController>()
                    .ShowThingHint(thing, EThingContextWindow.WINDOW_WITHOUT_ACTION, null, null);
            }
            catch (Exception e) { Plugin.Trace("[loot] hint " + thing + ": " + e.Message); }
        }

        private static string Signed(double value)
        {
            string text = Math.Abs(value - Math.Round(value)) < 0.005 ? Math.Round(value).ToString("N0") : value.ToString("N2");
            return (value > 0 ? "+" : "") + text;
        }

        private static string Signed2(double value) => value.ToString("+0.00;-0.00");

        private static string Paint(string text, Color color) => "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";

        private static string Lang(string key)
        {
            try
            {
                string text = ResourceStrings.GetString(key);
                return string.IsNullOrEmpty(text) || text == key ? null : text;
            }
            catch { return null; }
        }

        private static void Build()
        {
            if (_canvasGo != null) return;

            _canvasGo = new GameObject("QoLLoot", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 240;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);

            var stackGo = new GameObject("stack", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            stackGo.transform.SetParent(_canvasGo.transform, false);
            _stack = (RectTransform)stackGo.transform;
            _stack.anchorMin = _stack.anchorMax = new Vector2(1f, 1f);
            _stack.pivot = new Vector2(1f, 1f);

            var column = stackGo.GetComponent<VerticalLayoutGroup>();
            column.spacing = 8f;
            column.childAlignment = TextAnchor.UpperRight;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            var fit = stackGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _canvasGo.SetActive(false);
            Plugin.Trace("[loot] result plates built at top right");
        }
    }

    [HarmonyPatch(typeof(CombatController), "OnCombatResultResponse")]
    internal static class LootResultPatch
    {
        private static bool Prefix(object msg)
        {
            try { return !Loot.Take(msg as CombatResultResponseMessage); }
            catch (Exception e) { Plugin.Fault("[loot] result: " + e.Message); return true; }
        }
    }
}
