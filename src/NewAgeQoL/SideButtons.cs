using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class SideButtons
    {
        private class Entry
        {
            internal string Name;
            internal System.Func<string> Hint;
            internal GameObject Go;
            internal Image Icon;
            internal Image Frame;
            internal Text Count;
            internal System.Func<bool> Enabled;
            internal System.Func<Sprite> Sprite;
            internal System.Func<string> Art;
            internal Image Back;
            internal System.Func<string> Badge;
            internal bool WhiteBadge;
            internal System.Action Click;
            internal System.Action<RectTransform> RightClick;
            internal System.Func<bool> Usable;
            internal float PressAt;
            internal int Col;
            internal bool Right;
            internal bool Fight;
            internal int Order;
            internal int Seat;
            internal int FightSeat = -1;
        }

        private static readonly List<Entry> Buttons = new List<Entry>
        {
            new Entry
            {
                Name = "QoLBagButton",
                Art = () => "btn_bag",
                Col = 3,
                Fight = true,
                Hint = () => "Инвентарь" + Hotkeys.Tail("win:inventory"),
                Sprite = () => LeftColumn.MenuSprite("InventoryMenuButton") ?? MenuIcon(UserMenuController.ETabs.Inventory),
                Click = () =>
                {
                    try { Spells.Menu(UserMenuController.ETabs.Inventory); }
                    catch (System.Exception e) { Plugin.Warn("[buttons] bag: " + e.Message); }
                },
            },
            new Entry
            {
                Name = "QoLLeaveWatch",
                Art = () => "btn_leave",
                Col = 3,
                Fight = true,
                Order = 120,
                Hint = () => Spectate.Peeking ? "Выйти из просмотра боя" : "Выйти из боя",
                Enabled = () => Spectate.Peeking || Spectate.Rotted,
                Sprite = () => Pick("backward", 4),
                Click = () =>
                {
                    try { Spectate.Exit(); }
                    catch (System.Exception e) { Plugin.Warn("[buttons] leave spectate: " + e.Message); }
                },
            },
            new Entry
            {
                Name = "QoLSpellButton",
                Art = () => "btn_spells",
                Col = 3,
                Fight = true,
                Hint = () => "Книга магии" + Hotkeys.Tail("win:spells"),
                Sprite = () => MenuIcon(UserMenuController.ETabs.SpellBook),
                Click = Spells.Open,
            },
            new Entry
            {
                Name = "QoLOnlineButton",
                Art = () => "btn_online",
                Col = 3,
                Fight = true,
                Order = 90,
                Hint = () => (OnlineList.Configured ? "Кто в игре" : "Кто в игре: укажи запасной аккаунт в настройках мода") + OnlineWindow.KeyHint(),
                Sprite = () => Pick(1, "assassin_list", "friends", "clan"),
                Badge = () => OnlineList.Busy ? "…" : "",
                Click = OnlineWindow.Toggle,
            },
            new Entry
            {
                Name = "QoLDailyButton",
                Art = () => "btn_daily",
                Col = 3,
                Fight = true,
                FightSeat = 1,
                Hint = () => "Ежедневные задания" + Hotkeys.Tail("win:daily"),
                Sprite = () => LeftColumn.MenuSprite("DailyQuestsButton"),
                Click = () =>
                {
                    try { DependencyContainer.GetContainer()?.Resolve<DailyTasksWindowController>()?.OpenByButton(); }
                    catch (System.Exception e) { Plugin.Warn("[buttons] dailies: " + e.Message); }
                },
            },
            new Entry
            {
                Name = "QoLQuestButton",
                Art = () => "btn_quests",
                Col = 3,
                Hint = () => "Задания" + Hotkeys.Tail("win:quests"),
                Sprite = QuestBoard.Icon,
                Badge = () => QuestBoard.All.Count > 0 ? QuestBoard.All.Count.ToString() : "",
                Click = QuestWindow.Toggle,
            },
            new Entry
            {
                Name = "QoLSetupButton",
                Art = () => "btn_setup",
                Col = 3,
                Fight = true,
                Order = 99,
                Hint = () => "Настройки игры",
                Sprite = () => LeftColumn.MenuSprite("SetupMenuButton"),
                Click = () =>
                {
                    try { DependencyContainer.GetContainer()?.Resolve<SetupDialogController>()?.ShowSetupDialog(); }
                    catch (System.Exception e) { Plugin.Warn("[buttons] game settings: " + e.Message); }
                },
            },
            new Entry
            {
                Name = "QoLTownButton",
                Art = () => "btn_town",
                Right = true,
                Col = 0,
                Hint = () => Artifacts.Busy ? "Идёт работа с хранилищем"
                           : TownWalk.Busy ? "Уже иду"
                           : Plugin.CfgTownTournament != null && Plugin.CfgTownTournament.Value
                             ? "В город, потом на арену и к турнирам"
                             : "Вернуться в Иллениум",
                Enabled = () => !ClaimLocked(),
                Usable = () => !Artifacts.Busy && !TownWalk.Busy,
                Sprite = () => Pick("toTheCity", 4),
                Click = GoHome,
            },
            new Entry
            {
                Name = "QoLArtifactButton",
                Art = () => Artifacts.HasStash ? "btn_stash_get" : "btn_stash_put",
                Right = true,
                Col = 0,
                Hint = () => Artifacts.HasStash
                    ? "Вернуть основной набор"
                    : "Надеть запасной набор",
                Enabled = () => (Plugin.CfgArtifactButtons == null || Plugin.CfgArtifactButtons.Value) && !ClaimLocked(),
                Sprite = () => Artifacts.HasStash ? Pick("storage_get", 13) : Pick("storage_put", 14),
                Click = () =>
                {
                    if (Artifacts.Busy) return;
                    Travel.Cancel("занялся вещами");
                    if (Artifacts.HasStash) Artifacts.Restore();
                    else Artifacts.Stash();
                },
            },
            new Entry
            {
                Name = Workshop.ButtonName,
                Art = () => "btn_workshop",
                Col = 3,
                Order = 4,
                Hint = () => "Мастерская артефактов",
                Enabled = () => !Workshop.Here(),
                Usable = () => !Workshop.Busy,
                Sprite = () => { var art = Pick("art_create", 13); return art != null ? art : Icons.Spark(); },
                Click = Workshop.Toggle,
            },
            new Entry
            {
                Name = "QoLTravelButton",
                Art = () => "btn_travel",
                Right = true,
                Order = 9,
                Col = 2,
                Hint = () => Travel.Busy ? "Идёт поход — можно выбрать другую точку" : "Куда идти: список точек",
                Enabled = () => Travel.Enabled && !ClaimLocked(),
                Sprite = () => Pick(1, "move5", "move3", "mines_attack", "assassinate"),
                Badge = () => Travel.Busy ? "▶" : "",
                Click = TravelMenu.Toggle,
            },
        };

        static SideButtons()
        {
            for (int i = 0; i < Flasks.Rows; i++)
            {
                int row = i;
                Buttons.Add(new Entry
                {
                    Name = "QoLFlaskButton" + row,
                    Right = true,
                    Col = 1,
                    Hint = () => Flasks.Hint(row),
                    Badge = () => Flasks.Badge(row),
                    WhiteBadge = true,
                    Usable = () => !Flasks.Busy(row) && !Artifacts.Busy,
                    Enabled = () => Flasks.Shown(row),
                    Sprite = () => Flasks.Icon(row),
                    Click = () => Flasks.Use(row),
                    RightClick = at => FlaskPicker.Near(row, at),
                });
            }
            for (int i = 0; i < Buttons.Count; i++) Buttons[i].Seat = i;
        }

        internal static float LeftWidth => _panel != null && _panel.gameObject.activeSelf ? _panel.sizeDelta.x : 0f;

        internal static RectTransform LeftPanel => _panel != null && _panel.gameObject.activeInHierarchy ? _panel : null;

        internal static RectTransform DockedPanel => _panelRight != null && _panelRight.gameObject.activeInHierarchy ? _panelRight : null;

        private static float Space()
        {
            var area = Area();
            if (area == null || area.rect.height < 100f) return 800f;
            return Mathf.Max(200f, area.rect.height - LeftColumn.TopHeight() - ChatDock.PanelHeight - Gap * 3f);
        }

        internal static RectTransform Area()
        {
            try
            {
                var view = BaseLocationView.GetInstance();
                var canvas = view != null ? view.MainCanvas : null;
                if (canvas == null)
                {
                    var go = GameObject.Find("Canvas");
                    canvas = go != null ? go.GetComponent<Canvas>() : null;
                }
                if (canvas == null) return null;
                var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
                return (RectTransform)root.transform;
            }
            catch { return null; }
        }

        private static Sprite MenuIcon(UserMenuController.ETabs tab)
        {
            try { return AtlasUtils.GetCharacterMenuLeftPanelTabs(tab); }
            catch { return null; }
        }

        private static float _next;

        private const float Flash = 0.13f;
        private static readonly Color Pressed = new Color(0.5f, 0.5f, 0.5f, 0.95f);

        private static void Poke(Entry entry)
        {
            if (entry == null) return;
            entry.PressAt = Time.unscaledTime;
            if (entry.Icon != null) entry.Icon.color = Pressed;
        }

        private static void Flashes()
        {
            float now = Time.unscaledTime;
            foreach (var b in Buttons)
            {
                if (b.PressAt <= 0f) continue;
                if (now - b.PressAt < Flash) continue;
                b.PressAt = 0f;
                if (b.Icon != null && b.Icon.color != Color.white) b.Icon.color = Color.white;
            }
        }

        internal static void Wake()
        {
            _next = 0f;
        }

        internal static void Tick()
        {
            Slide();
            TravelMenu.Hover();
            Workshop.Hover();
            Flashes();
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.2f;

            bool fight = InCombat();
            bool world = InWorld() && !fight;
            bool live = InWorld();
            var home = Host();
            if (home != _hostNow) { _hostNow = home; Rebuild(); }
            bool ready = live && home != null && home.rect.width > 2f;

            foreach (var b in Buttons)
            {
                bool want = ready && Shown(b);
                if (!want)
                {
                    if (b.Go != null) b.Go.SetActive(false);
                    continue;
                }
                if (b.Go == null) Create(b);
                if (b.Go == null) continue;
                var sprite = Tile(b) ?? b.Sprite();
                if (b.Icon != null)
                {
                    if (sprite != null && b.Icon.sprite != sprite) b.Icon.sprite = sprite;
                    bool drawn = b.Icon.sprite != null && b.Icon.sprite.texture != null;
                    if (b.Icon.enabled != drawn) b.Icon.enabled = drawn;
                }
                if (b.Frame != null && b.Frame.enabled) b.Frame.enabled = false;

                var btn = b.Go.GetComponent<Button>();
                if (btn != null && !btn.interactable) btn.interactable = true;
                var tint = b.PressAt > 0f ? Pressed : Color.white;
                if (b.Icon != null && b.Icon.color != tint) b.Icon.color = tint;
                if (b.Back != null) Plate(b.Back, b.PressAt > 0f);
                if (b.Count != null && b.Badge != null)
                {
                    string badge = b.Badge();
                    if (b.Count.text != badge) b.Count.text = badge;
                }
            }
            Layout();
            Layer();
            foreach (var b in Buttons)
            {
                if (b.Go == null) continue;
                bool show = ready && Shown(b);
                if (show && !b.Go.activeSelf) b.Go.SetActive(true);
            }
            if (!ready)
            {
                if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
                if (_panelRight != null && _panelRight.gameObject.activeSelf) _panelRight.gameObject.SetActive(false);
            }
            TravelMenu.Tick(ready);
            Workshop.Tick(ready);
            UpdateStatus(world);
            if (!InWorld() || TravelMenu.Open || Workshop.Open) HideHint();
        }

        private static RectTransform _panel;
        private static RectTransform _panelRight;
        private static RectTransform _hostNow;

        private static RectTransform Host()
        {
            var deck = Deck();
            if (deck != null) return deck;
            var own = ChatDock.Area;
            return own != null ? own : Area();
        }

        private const int DeckHigh = 251;
        private static Canvas _deck;

        internal static RectTransform Deck()
        {
            if (_deck != null) return (RectTransform)_deck.transform;
            if (ChatDock.Area == null) return null;
            var go = new GameObject("QoLButtons", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Curtain.Stage(go);
            Object.DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = DeckHigh;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);
            _deck = canvas;
            Plugin.Trace("[buttons] own button canvas, layer " + DeckHigh);
            return (RectTransform)go.transform;
        }

        private static readonly System.Type[] ShopTypes =
        {
            typeof(ShopPanelContent), typeof(MagicShopPanelContent), typeof(TradePanelContentWindow), typeof(MarketProposalListGrid),
        };

        private static readonly List<CanvasGroup> Groups = new List<CanvasGroup>();
        private static Component _shop;
        private static float _shopAt;
        private static float _shopHot;
        private static int _shopTurn;

        internal static void WindowsMoved()
        {
            _shopHot = Time.unscaledTime + 2f;
            _next = 0f;
        }

        internal static bool WindowsHot => Time.unscaledTime < _shopHot;

        private static Component Shopping()
        {
            try
            {
                if (_shop != null && Visible(_shop)) return _shop;
                _shop = null;
                if (InCombat()) return null;
                float now = Time.unscaledTime;
                if (now >= _shopHot)
                {
                    if (now < _shopAt) return null;
                    _shopAt = now + 1f;
                }
                if (now < _shopHot)
                {
                    foreach (var type in ShopTypes)
                    {
                        _shop = Seen(type);
                        if (_shop != null) break;
                    }
                    return _shop;
                }
                _shop = Seen(ShopTypes[_shopTurn]);
                _shopTurn = (_shopTurn + 1) % ShopTypes.Length;
                return _shop;
            }
            catch { return null; }
        }

        private static Component Seen(System.Type type)
        {
            foreach (var one in Object.FindObjectsOfType(type))
                if (one is Component found && Visible(found)) return found;
            return null;
        }

        private static bool Visible(Component one)
        {
            if (one == null || !one.gameObject.activeInHierarchy) return false;
            var rt = one.transform as RectTransform;
            if (rt == null || rt.rect.width < 4f || rt.rect.height < 4f) return false;
            one.GetComponentsInParent(false, Groups);
            foreach (var group in Groups)
                if (group != null && group.alpha < 0.05f) return false;
            var canvas = one.GetComponentInParent<Canvas>();
            return canvas != null && canvas.isActiveAndEnabled;
        }

        private const float SlideTime = 0.28f;
        private static RectTransform _slideLeft;
        private static RectTransform _slideRight;
        private static bool _wantAway;
        private static float _away;

        private static RectTransform Slider(RectTransform parent, int which)
        {
            if (_deck == null || parent != (RectTransform)_deck.transform) return parent;
            var had = which == 1 ? _slideRight : _slideLeft;
            if (had != null) return had;
            var rt = new GameObject(which == 1 ? "QoLSlideRight" : "QoLSlideLeft", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (which == 1) _slideRight = rt; else _slideLeft = rt;
            return rt;
        }

        private static bool _told;
        private static float _bareSince = -1f;
        private static int _bareFrame = -1;
        private static bool _bare;
        private static CanvasGroup _deckGroup;

        internal static bool Bare()
        {
            if (_bareFrame == Time.frameCount) return _bare;
            _bareFrame = Time.frameCount;
            _bare = true;
            try
            {
                foreach (var cam in Camera.allCameras)
                {
                    if (cam == Backdrop.Cam || cam.targetTexture != null || !cam.isActiveAndEnabled) continue;
                    if (cam.clearFlags != CameraClearFlags.Skybox && cam.clearFlags != CameraClearFlags.SolidColor) continue;
                    var r = cam.rect;
                    if (r.width < 0.99f || r.height < 0.99f) continue;
                    _bare = false;
                    return false;
                }
            }
            catch { _bare = false; }
            return _bare;
        }

        private static void Cameras()
        {
            try
            {
                var line = new System.Text.StringBuilder();
                foreach (var cam in Camera.allCameras)
                    line.Append(" | ").Append(cam.name).Append(" clear ").Append(cam.clearFlags).Append(" depth ").Append(cam.depth)
                        .Append(cam.targetTexture != null ? " to texture" : "").Append(" rect ").Append(cam.rect.width.ToString("0.00")).Append('x').Append(cam.rect.height.ToString("0.00"));
                Plugin.Trace("[buttons] cameras when the window opened:" + (line.Length > 0 ? line.ToString() : " none") + ", screen bare " + Bare());
            }
            catch (System.Exception e) { Plugin.Trace("[buttons] cameras: " + e.Message); }
        }

        private static readonly System.Reflection.FieldInfo SnapshotCamera = HarmonyLib.AccessTools.Field(typeof(BackgroundScreenshotMaker), "_renderCamera");
        private static Camera _borrowed;
        private static Component _snap;
        private static float _borrowUntil;

        private static void GiveBack()
        {
            if (_borrowed == null || (Time.unscaledTime < _borrowUntil && _away < 1f && _snap != null)) return;
            var cam = _borrowed;
            _borrowed = null;
            try
            {
                if (cam != null && SnapshotCamera?.GetValue(null) as Camera == cam && Object.FindObjectOfType<BackgroundScreenshotMaker>() != null)
                    cam.gameObject.SetActive(false);
            }
            catch (System.Exception e) { Plugin.Trace("[buttons] camera back: " + e.Message); }
        }

        internal static void ScreenshotTaken(Component maker)
        {
            try
            {
                if (maker == null || _deck == null) return;
                bool shop = false;
                foreach (var type in ShopTypes)
                    if (maker.GetComponentInChildren(type, true) != null || maker.GetComponentInParent(type) != null) { shop = true; break; }
                if (!shop) return;
                _snap = maker;
                _wantAway = true;
                _told = true;
                WindowsMoved();
                var cam = SnapshotCamera?.GetValue(null) as Camera;
                if (cam != null && !cam.gameObject.activeSelf)
                {
                    cam.gameObject.SetActive(true);
                    _borrowed = cam;
                    _borrowUntil = Time.unscaledTime + 1.5f;
                }
                Plugin.Trace("[buttons] shop window took the scene snapshot, side buttons slide away" + (_borrowed != null ? " with the game camera kept on for the slide" : ""));
            }
            catch (System.Exception e) { Plugin.Trace("[buttons] snapshot: " + e.Message); }
        }

        private static void Veil(bool hide)
        {
            if (_deck == null) return;
            if (_deckGroup == null) _deckGroup = _deck.GetComponent<CanvasGroup>() ?? _deck.gameObject.AddComponent<CanvasGroup>();
            float e = _away * _away * (3f - 2f * _away);
            float want = hide ? 0f : 1f - e;
            if (_deckGroup.alpha != want) _deckGroup.alpha = want;
        }

        private static void Slide()
        {
            GiveBack();
            float target = _wantAway ? 1f : 0f;
            if (Mathf.Approximately(_away, target)) { Veil(false); _bareSince = -1f; return; }
            if (_wantAway && Bare())
            {
                _away = 1f;
                Veil(true);
                if (!_told) { _told = true; Plugin.Trace("[buttons] game camera off under the window, side buttons hidden at once"); }
                return;
            }
            if (Bare() && (_bareSince < 0f || Time.unscaledTime - _bareSince < 1.5f))
            {
                if (_bareSince < 0f) _bareSince = Time.unscaledTime;
                Veil(true);
                if (!_told) { _told = true; Plugin.Trace("[buttons] game camera off and window background not drawn yet, side buttons held still"); }
                return;
            }
            Veil(false);
            _away = Mathf.MoveTowards(_away, target, Time.unscaledDeltaTime / SlideTime);
            float e = _away * _away * (3f - 2f * _away);
            float half = _deck != null ? ((RectTransform)_deck.transform).rect.width * 0.5f : 960f;
            if (_slideLeft != null && _panel != null) _slideLeft.anchoredPosition = new Vector2(e * Out(_panel, half), 0f);
            if (_slideRight != null && _panelRight != null) _slideRight.anchoredPosition = new Vector2(e * Out(_panelRight, half), 0f);
            if (_away <= 0f || _away >= 1f) Plugin.Trace("[buttons] side buttons " + (_away >= 1f ? "slid away under a window" : "slid back"));
        }

        private static float Out(RectTransform panel, float half)
        {
            float left = panel.anchoredPosition.x;
            float right = left + panel.rect.width;
            return left + right < 0f ? -(right + half + 20f) : half - left + 20f;
        }

        private static void Layer()
        {
            if (_deck == null) return;
            int order = DeckHigh;
            var shop = Shopping();
            if (shop == null && _snap != null && _snap.gameObject.activeInHierarchy) shop = _snap;
            if (shop != null && !_wantAway) { _told = false; Cameras(); }
            _wantAway = shop != null;
            if (shop != null && _away >= 1f)
            {
                var canvas = shop.GetComponentInParent<Canvas>();
                var root = canvas != null ? canvas.rootCanvas : null;
                order = (root != null ? root.sortingOrder : 0) - 1;
                if (order > DeckHigh) order = DeckHigh;
            }
            if (_deck.sortingOrder == order) return;
            _deck.sortingOrder = order;
            Plugin.Trace("[buttons] button layer: " + order);
        }

        private static int Side(Entry entry) => entry.Right ? 1 : 0;

        private static int SeatOf(Entry entry, bool fight) => fight && entry.FightSeat >= 0 ? entry.FightSeat : entry.Seat;

        private static bool Docked(RectTransform panel) => panel != null && panel == _panelRight;

        private static RectTransform Panel(RectTransform parent, int which)
        {
            if (which == 1 && _panelRight != null) return _panelRight;
            if (which == 0 && _panel != null) return _panel;
            if (parent == null) return null;

            var go = new GameObject(which == 1 ? "QoLPanelRight" : "QoLPanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(Slider(parent, which), false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(_cellSide, _cellSide);

            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            WardrobeLook.Frame(go, WardrobeLook.Edge);
            back.raycastTarget = false;

            if (which == 1) _panelRight = rt; else _panel = rt;
            return rt;
        }

        private static void Rebuild()
        {
            foreach (var b in Buttons)
            {
                if (b.Go != null) Object.Destroy(b.Go);
                b.Go = null;
                b.Icon = null;
                b.Frame = null;
                b.Back = null;
                b.Count = null;
            }
            if (_panel != null) Object.Destroy(_panel.gameObject);
            if (_panelRight != null) Object.Destroy(_panelRight.gameObject);
            _panel = null;
            _panelRight = null;
            if (_status != null) { Object.Destroy(_status.gameObject); _status = null; }
            if (_hint != null) { Object.Destroy(_hint.gameObject); _hint = null; }
        }

        private static void LayoutSide(int which, float side, float step)
        {
            var panel = which == 1 ? _panelRight : _panel;
            if (panel == null) return;
            bool fixedSeats = which >= 1;

            int shown = 0, visible = 0;
            foreach (var b in Buttons)
            {
                if (b.Go == null || Side(b) != which) continue;
                if (fixedSeats ? Alive(b) : Shown(b)) shown++;
                if (Shown(b)) visible++;
            }
            if (shown == 0 || visible == 0)
            {
                Size(panel, new Vector2(side, side));
                if (panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
                return;
            }
            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);

            int perColumn = which >= 1 ? Stack(shown, step)
                : InCombat() ? 1
                : Mathf.Max(1, Mathf.FloorToInt((Space() - Pad * 2f + Gap) / step));
            int columns = (shown + perColumn - 1) / perColumn;
            int rows = Mathf.Min(shown, perColumn);

            float high = rows * side + (rows - 1) * Gap + Pad * 2f;
            float top = high * 0.5f - Pad - side * 0.5f;
            Order.Clear();
            foreach (var b in Buttons)
            {
                if (b.Go == null || Side(b) != which) continue;
                if (fixedSeats ? !Alive(b) : !Shown(b)) continue;
                Order.Add(b);
            }
            _orderFight = InCombat();
            Order.Sort(ByOrder);
            columns = (Order.Count + perColumn - 1) / perColumn;
            float wide = columns * side + (columns - 1) * Gap + Pad * 2f;
            Size(panel, new Vector2(wide, high));
            float first = -wide * 0.5f + Pad + side * 0.5f;
            for (int seat = 0; seat < Order.Count; seat++)
            {
                var b = Order[seat];
                if (b == null) continue;
                int column = seat / perColumn;
                int row = seat % perColumn;
                var rt = (RectTransform)b.Go.transform;
                var spot = new Vector2(first + step * column, top - step * row);
                if (rt.anchoredPosition != spot) rt.anchoredPosition = spot;
            }
            Order.Clear();
            Stand(panel, which);
        }

        private static readonly List<Entry> Order = new List<Entry>();
        private static bool _orderFight;

        private static readonly System.Comparison<Entry> ByOrder = (a, b) =>
            a.Order != b.Order ? a.Order.CompareTo(b.Order) : SeatOf(a, _orderFight).CompareTo(SeatOf(b, _orderFight));

        private static void Size(RectTransform panel, Vector2 size)
        {
            if (panel.sizeDelta != size) panel.sizeDelta = size;
        }

        private static float Room()
        {
            var parent = Host();
            if (parent == null || parent.rect.width < 10f) return 0f;
            var canvas = parent.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            return Mathf.Max(0f, ChatDock.LeftPixels / scale - Gap * 2f);
        }

        private static int Stack(int shown, float step)
        {
            float room = Room();
            if (room < 1f || shown <= 0) return PerColumn;
            int fits = Mathf.Max(1, Mathf.FloorToInt((room - Pad * 2f + Gap) / step));
            return Mathf.Max(PerColumn, Mathf.CeilToInt(shown / (float)fits));
        }

        private static void Stand(RectTransform panel, int which)
        {
            var parent = Host();
            if (parent == null || panel == null) return;

            Vector2 size = panel.sizeDelta;
            Rect area = parent.rect;
            float x, y;
            if (which >= 1)
            {
                var canvas = parent.GetComponentInParent<Canvas>();
                float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
                float dockLeft = area.xMin + ChatDock.LeftPixels / scale;

                x = Mathf.Max(area.xMin + Gap, dockLeft - Gap - size.x);
                y = area.yMin + Gap;
            }
            else
            {
                x = area.xMin + Gap;
                y = Mathf.Clamp(TopFor(area) - size.y, area.yMin + Gap, area.yMax - size.y);
            }

            var corner = new Vector2(0f, 0f);
            if (panel.pivot != corner) panel.pivot = corner;
            var middle = new Vector2(0.5f, 0.5f);
            if (panel.anchorMin != middle) panel.anchorMin = middle;
            if (panel.anchorMax != middle) panel.anchorMax = middle;

            var want = new Vector2(x, y);
            if ((panel.anchoredPosition - want).sqrMagnitude <= 4f) return;
            panel.anchoredPosition = want;
        }

        private const float FightTop = 12f;

        private static float TopFor(Rect area)
        {
            if (InCombat()) return area.yMax - FightTop;
            return area.yMax - LeftColumn.TopHeight() - Gap;
        }



        internal static bool AtRightSide
        {
            get
            {
                var panel = PanelRect;
                if (panel == null) return false;
                var parent = panel.parent as RectTransform;
                if (parent == null || parent.rect.width < 1f) return false;
                Vector2 center = parent.InverseTransformPoint(panel.TransformPoint(panel.rect.center));
                return center.x > parent.rect.width * 0.25f;
            }
        }

        internal static RectTransform PanelRect => _panelRight != null ? _panelRight : _panel;

        internal static RectTransform PanelOf(string name)
        {
            var rect = ButtonRect(name);
            var panel = rect != null ? rect.parent as RectTransform : null;
            return panel != null ? panel : PanelRect;
        }

        private static RectTransform Root(RectTransform any)
        {
            var canvas = any.GetComponentInParent<Canvas>();
            if (canvas == null) return any;
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            return (RectTransform)root.transform;
        }





        private const int PerColumn = 2;
        private const float Gap = 8f;
        private const float Pad = 7f;

        private static void Layout()
        {
            if (_panel == null) return;
            float side = _cellSide;
            float step = side + Gap;
            LayoutSide(0, side, step);
            LayoutSide(1, side, step);
        }

        private static bool Alive(Entry entry)
        {
            return entry != null && (entry.Enabled == null || entry.Enabled());
        }

        private static bool Shown(Entry entry)
        {
            if (!Alive(entry)) return false;
            return entry.Fight || !InCombat();
        }

        private static Entry Anchor()
        {
            foreach (var b in Buttons) if (b.Go != null && Shown(b)) return b;
            return null;
        }

        internal static RectTransform ButtonRect(string name)
        {
            var entry = Named(name);
            return entry != null && entry.Go != null ? (RectTransform)entry.Go.transform : null;
        }

        internal static float RowOf(string name)
        {
            var entry = Named(name);
            if (entry == null || entry.Go == null) return 0f;
            return ((RectTransform)entry.Go.transform).anchoredPosition.y;
        }

        private static Entry Named(string name)
        {
            foreach (var b in Buttons) if (b.Name == name) return b;
            return null;
        }

        internal static Font GameFont()
        {
            var holder = VisualPrefabsHolder.Instance;
            if (holder != null && holder.BoldStandardFont != null) return holder.BoldStandardFont;
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>()) return f;
            return null;
        }

        private static Text _status;

        private static void UpdateStatus(bool world)
        {
            try
            {
                if (!Artifacts.Busy && !string.IsNullOrEmpty(Artifacts.Status)
                    && Time.unscaledTime - Artifacts.StatusAt > 4f) Artifacts.Status = "";
                if (!Flasks.AnyBusy && !string.IsNullOrEmpty(Flasks.Status)
                    && Time.unscaledTime - Flasks.StatusAt > 5f) Flasks.Status = "";

                if (!Travel.Busy && !string.IsNullOrEmpty(Travel.Status)
                    && Time.unscaledTime - Travel.StatusAt > 6f) Travel.Status = "";

                string line = null;
                Entry owner = null;
                if (!string.IsNullOrEmpty(Travel.Status))
                {
                    line = Travel.Status;
                    owner = Named("QoLTravelButton");
                    if (owner == null || owner.Go == null || !Shown(owner)) owner = Named("QoLTownButton");
                }
                else if (!string.IsNullOrEmpty(Artifacts.Status))
                {
                    line = "Вещи: " + Artifacts.Status;
                    owner = Named("QoLArtifactButton");
                }
                else if (!string.IsNullOrEmpty(Flasks.Status))
                {
                    line = Flasks.Status;
                    owner = Named("QoLFlaskButton" + Flasks.StatusRow);
                }
                if (owner == null || owner.Go == null || !Shown(owner)) owner = Anchor();
                var host = owner != null ? owner.Go : null;
                if (!world || line == null || host == null || TravelMenu.Open)
                {
                    if (_status != null) _status.gameObject.SetActive(false);
                    return;
                }
                var hostRt = (RectTransform)host.transform;
                if (_status == null)
                {
                    var go = new GameObject("QoLStatus", typeof(RectTransform), typeof(Text), typeof(Outline));
                    go.transform.SetParent(hostRt.parent, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0f, 0.5f);
                    rt.sizeDelta = new Vector2(460f, 32f);
                    var t = go.GetComponent<Text>();
                    t.font = GameFont();
                    t.fontSize = 20;
                    t.alignment = TextAnchor.MiddleLeft;
                    t.color = WardrobeLook.Accent;
                    t.raycastTarget = false;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    t.verticalOverflow = VerticalWrapMode.Overflow;
                    var o = go.GetComponent<Outline>();
                    o.effectColor = new Color(0f, 0f, 0f, 0.95f);
                    o.effectDistance = new Vector2(1.6f, -1.6f);
                    _status = t;
                }
                var srt = (RectTransform)_status.transform;
                var owned = hostRt.parent as RectTransform;
                if (owned != null && srt.parent != owned) srt.SetParent(owned, false);
                if (Docked(owned)) Above(_status, owned);
                else srt.anchoredPosition = new Vector2(SideOf(_status, owned, ToLeft(owned)), hostRt.anchoredPosition.y);
                _status.text = line;
                Fit(_status);
                if (Docked(owned)) Rise(_status, owned);
                srt.SetAsLastSibling();
                if (!_status.gameObject.activeSelf) _status.gameObject.SetActive(true);
                _status.text = line;
                HideHint();
            }
            catch { }
        }

        private static Text _hint;

        private static bool Busy()
        {
            return Artifacts.Busy || !string.IsNullOrEmpty(Artifacts.Status)
                || Flasks.AnyBusy || !string.IsNullOrEmpty(Flasks.Status)
                || Travel.Busy || !string.IsNullOrEmpty(Travel.Status);
        }

        private static float SideOf(Text label, RectTransform panel, bool toLeft)
        {
            var rt = (RectTransform)label.transform;
            rt.pivot = new Vector2(toLeft ? 1f : 0f, 0.5f);
            label.alignment = toLeft ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            float half = panel != null ? panel.sizeDelta.x * 0.5f : 0f;
            return toLeft ? -(half + 10f) : half + 10f;
        }

        private static void Above(Text label, RectTransform panel)
        {
            var rt = (RectTransform)label.transform;
            rt.pivot = new Vector2(0.5f, 0f);
            label.alignment = TextAnchor.MiddleCenter;
            rt.anchoredPosition = new Vector2(0f, panel.sizeDelta.y * 0.5f + 6f);
        }

        private static void Rise(Text label, RectTransform panel)
        {
            try
            {
                var dock = ChatDock.Root;
                if (label == null || panel == null || dock == null || !dock.gameObject.activeInHierarchy) return;
                var rt = (RectTransform)label.transform;
                var corners = new Vector3[4];
                dock.GetWorldCorners(corners);
                Vector2 dockLow = panel.InverseTransformPoint(corners[0]);
                Vector2 dockTop = panel.InverseTransformPoint(corners[1]);
                Vector2 at = panel.InverseTransformPoint(rt.position);
                float wide = Mathf.Max(label.preferredWidth, 1f);
                float right = at.x + (1f - rt.pivot.x) * wide;
                if (right <= dockLow.x) return;
                float bottom = at.y - rt.pivot.y * rt.rect.height;
                float need = dockTop.y + 4f - bottom;
                if (need <= 0.5f) return;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y + need);
            }
            catch { }
        }

        private static void Fit(Text label)
        {
            if (label == null) return;
            try
            {
                var rt = (RectTransform)label.transform;
                var area = Root(rt);
                if (area == null || area.rect.width < 50f) return;

                float wide = Mathf.Max(label.preferredWidth, rt.rect.width);
                if (wide < 1f) return;
                float middle = area.InverseTransformPoint(rt.position).x;
                float left = middle - rt.pivot.x * wide;
                float right = left + wide;

                float low = area.rect.xMin + Gap;
                float high = area.rect.xMax - Gap;
                float shift = 0f;
                if (right > high) shift = high - right;
                if (left + shift < low) shift = low - left;
                if (Mathf.Abs(shift) < 0.5f) return;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + shift, rt.anchoredPosition.y);
            }
            catch { }
        }

        private static bool ToLeft(RectTransform panel)
        {
            return panel != null && panel == _panelRight;
        }

        private static void ShowHint(RectTransform near, string text)
        {
            try
            {
                if (Busy() || TravelMenu.Open || Workshop.Open) return;
                if (_hint == null)
                {
                    var go = new GameObject("QoLHint", typeof(RectTransform), typeof(Text), typeof(Outline));
                    go.transform.SetParent(near.parent, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0f, 0.5f);
                    rt.sizeDelta = new Vector2(460f, 30f);
                    _hint = go.GetComponent<Text>();
                    _hint.font = GameFont();
                    _hint.fontSize = 19;
                    _hint.alignment = TextAnchor.MiddleLeft;
                    _hint.color = WardrobeLook.Bright;
                    _hint.raycastTarget = false;
                    _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
                    _hint.verticalOverflow = VerticalWrapMode.Overflow;
                    var o = go.GetComponent<Outline>();
                    o.effectColor = new Color(0f, 0f, 0f, 0.95f);
                    o.effectDistance = new Vector2(1.6f, -1.6f);
                }
                if (Busy()) { HideHint(); return; }
                var panel = near.parent as RectTransform;
                if (panel != null && _hint.transform.parent != panel) _hint.transform.SetParent(panel, false);
                _hint.text = text;
                var hrt = (RectTransform)_hint.transform;
                if (Docked(panel)) Above(_hint, panel);
                else hrt.anchoredPosition = new Vector2(SideOf(_hint, panel, ToLeft(panel)), near.anchoredPosition.y);
                Fit(_hint);
                if (Docked(panel)) Rise(_hint, panel);
                hrt.SetAsLastSibling();
                _hint.gameObject.SetActive(true);
            }
            catch { }
        }

        private static void HideHint()
        {
            if (_hint != null && _hint.gameObject.activeSelf) _hint.gameObject.SetActive(false);
        }

        private static Sprite _edge;
        private static float _edgeAt = -10f;

        private static Sprite Edge()
        {
            if (_edge != null && _edge.texture != null) return _edge;
            _edge = null;
            if (Time.unscaledTime - _edgeAt < 0.5f) return null;
            _edgeAt = Time.unscaledTime;
            try { _edge = AtlasUtils.GetStateHighlightingSprite(EHighlightingType.Positive); }
            catch (System.Exception e) { Plugin.Trace("[buttons] frame: " + e.Message); }
            if (_edge != null && _edge.texture == null) _edge = null;
            return _edge;
        }

        private static void Dress(Image frame)
        {
            var now = frame.sprite;
            bool alive = now != null && now.texture != null;
            if (!alive)
            {
                var edge = Edge();
                if (edge != null) { frame.sprite = edge; alive = true; }
            }
            if (frame.enabled != alive) frame.enabled = alive;
        }

        private static float _cellSide = 64f;


        private static Sprite Tile(Entry entry)
        {
            if (entry.Art == null) return null;
            var tile = Icons.Flash(entry.Art());
            return tile != null && tile.texture != null ? tile : null;
        }

        private static readonly Color Held = WardrobeLook.Mix(WardrobeLook.Button, Color.black, 0.35f);

        private static void Plate(Image back, bool pressed)
        {
            var want = pressed ? Held : WardrobeLook.Button;
            if (back.color != want) back.color = want;
        }

        private static void Flatten(Entry entry, RectTransform cell, float side)
        {
            var back = cell.Find("back") as RectTransform;
            if (back != null)
            {
                entry.Back = back.GetComponent<Image>();
                back.offsetMin = Vector2.zero;
                back.offsetMax = Vector2.zero;
                entry.Back.sprite = OnlineWindow.Rounded(8);
                entry.Back.type = Image.Type.Sliced;
                entry.Back.color = WardrobeLook.Button;
                WardrobeLook.Frame(back.gameObject, WardrobeLook.Edge);
            }
            var clip = cell.Find("clip") as RectTransform;
            if (clip == null) return;
            float pad = side * 0.08f;
            clip.offsetMin = new Vector2(pad, pad);
            clip.offsetMax = new Vector2(-pad, -pad);
            if (clip.childCount == 0) return;
            var icon = clip.GetChild(0) as RectTransform;
            if (icon == null) return;
            icon.offsetMin = Vector2.zero;
            icon.offsetMax = Vector2.zero;
        }


        internal static void Strip(GameObject go)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                if (mb is Graphic || mb is Button || mb is Mask || mb is Shadow
                    || mb is LayoutGroup || mb is ContentSizeFitter || mb is LayoutElement) continue;
                Object.DestroyImmediate(mb);
            }
            foreach (var an in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
            foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true))
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> call)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(call);
            trigger.triggers.Add(e);
        }

        internal static GameObject BuildCell(RectTransform parent, string name, float side,
                                             out Image icon, out Image frame, out Image background)
        {
            GameObject cell = null;
            GameObject go;
            if (cell != null)
            {
                go = Object.Instantiate(cell, parent);
                Clones.StripHotkeys(go, cell);
                go.SetActive(false);
                Strip(go);
                foreach (var tx in go.GetComponentsInChildren<Text>(true)) tx.gameObject.SetActive(false);
            }
            else
            {
                go = new GameObject("cell", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                go.SetActive(false);
                var edge = new GameObject("frame", typeof(RectTransform), typeof(Image));
                edge.transform.SetParent(go.transform, false);
                edge.GetComponent<Image>().sprite = Edge();
            }
            go.name = name;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta = new Vector2(side, side);
            rt.SetAsLastSibling();

            background = null;
            frame = null;
            icon = null;
            foreach (var im in go.GetComponentsInChildren<Image>(true))
            {
                if (im.transform == rt) { background = im; continue; }
                var crt = (RectTransform)im.transform;
                crt.anchorMin = Vector2.zero;
                crt.anchorMax = Vector2.one;
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.offsetMin = Vector2.zero;
                crt.offsetMax = Vector2.zero;
                crt.localScale = Vector3.one;
                crt.localRotation = Quaternion.identity;
                im.raycastTarget = false;

                bool looksFrame = im.name.IndexOf("frame", System.StringComparison.OrdinalIgnoreCase) >= 0
                           || im.name.IndexOf("border", System.StringComparison.OrdinalIgnoreCase) >= 0
                           || im.name.IndexOf("highlight", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (looksFrame) frame = im;
                else if (icon == null) icon = im;
            }

            if (icon == null)
            {
                var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(rt, false);
                icon = iconGo.GetComponent<Image>();
                icon.raycastTarget = false;
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                if (frame != null) ((RectTransform)frame.transform).SetAsLastSibling();
            }

            var clipGo = new GameObject("clip", typeof(RectTransform), typeof(RectMask2D));
            clipGo.transform.SetParent(rt, false);
            var clip = (RectTransform)clipGo.transform;
            clip.anchorMin = Vector2.zero;
            clip.anchorMax = Vector2.one;
            clip.pivot = new Vector2(0.5f, 0.5f);
            float edgePad = side * 0.1f;
            clip.offsetMin = new Vector2(edgePad, edgePad);
            clip.offsetMax = new Vector2(-edgePad, -edgePad);

            var iconRt = (RectTransform)icon.transform;
            iconRt.SetParent(clip, false);
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            float over = side * 0.06f;
            iconRt.offsetMin = new Vector2(-over, -over);
            iconRt.offsetMax = new Vector2(over, over);

            if (background != null)
            {
                background.enabled = true;
                background.raycastTarget = true;
                background.color = new Color(1f, 1f, 1f, 0f);
            }

            var backGo = new GameObject("back", typeof(RectTransform), typeof(Image));
            backGo.transform.SetParent(rt, false);
            backGo.transform.SetAsFirstSibling();
            var backRt = (RectTransform)backGo.transform;
            backRt.anchorMin = Vector2.zero;
            backRt.anchorMax = Vector2.one;
            float pad = side * 0.04f;
            backRt.offsetMin = new Vector2(pad, pad);
            backRt.offsetMax = new Vector2(-pad, -pad);
            var back = backGo.GetComponent<Image>();
            back.color = new Color(0.05f, 0.05f, 0.06f, 0.6f);
            back.raycastTarget = false;

            if (frame != null)
            {
                frame.gameObject.SetActive(true);
                frame.color = Color.white;
                frame.transform.SetAsLastSibling();
                Dress(frame);
            }
            return go;
        }

        private static void Create(Entry entry)
        {
            GameObject go = null;
            try
            {
                var host = Host();
                if (host == null) return;
                var parent = Panel(host, Side(entry));
                if (parent == null) return;

                go = BuildCell(parent, entry.Name, _cellSide, out var icon, out var frame, out var background);
                if (go == null) return;
                go.SetActive(false);
                float side = _cellSide;
                var rt = (RectTransform)go.transform;

                var sprite = Tile(entry) ?? entry.Sprite();
                icon.gameObject.SetActive(true);
                icon.enabled = true;
                icon.preserveAspect = true;
                icon.color = Color.white;
                if (sprite != null) icon.sprite = sprite;
                entry.Icon = icon;
                entry.Frame = frame;
                entry.Back = null;
                Flatten(entry, rt, side);
                if (frame != null) frame.enabled = false;

                if (entry.Badge != null) entry.Count = Badge(rt, side, entry.Badge(), entry.WhiteBadge);

                var button = go.GetComponent<Button>();
                if (button == null) button = go.AddComponent<Button>();
                button.onClick = new Button.ButtonClickedEvent();
                button.targetGraphic = background;
                button.interactable = true;
                var clickAction = entry.Click;
                var pressed = entry;
                button.onClick.AddListener(() =>
                {
                    Poke(pressed);
                    if (pressed.Usable != null && !pressed.Usable()) return;
                    Windows.Shut(pressed.Name);
                    clickAction();
                });

                var trigger = go.GetComponent<EventTrigger>();
                if (trigger == null) trigger = go.AddComponent<EventTrigger>();
                trigger.triggers = new List<EventTrigger.Entry>();
                var hint = entry.Hint;
                AddTrigger(trigger, EventTriggerType.PointerEnter, _ => ShowHint(rt, hint()));
                AddTrigger(trigger, EventTriggerType.PointerExit, _ => HideHint());
                var right = entry.RightClick;
                if (right != null)
                    AddTrigger(trigger, EventTriggerType.PointerClick, data =>
                    {
                        var click = data as PointerEventData;
                        if (click == null || click.button != PointerEventData.InputButton.Right) return;
                        HideHint();
                        right(rt);
                    });

                entry.Go = go;
                go = null;
                Layout();
                Plugin.Trace("[buttons] " + entry.Name + " side " + side
                                    + ", frame " + (frame != null && frame.sprite != null ? frame.sprite.name : "none")
                                    + ", icon " + (sprite != null ? sprite.name : "none"));
            }
            catch (System.Exception e)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                Plugin.Fault("[buttons] " + e);
            }
        }

        private static Text Badge(RectTransform host, float side, string text, bool white)
        {
            var go = new GameObject("count", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(side * 0.1f, side * 0.06f);
            rt.offsetMax = new Vector2(-side * 0.1f, side * 0.4f);
            rt.localScale = Vector3.one;
            rt.SetAsLastSibling();

            var label = go.GetComponent<Text>();
            label.font = GameFont();
            label.fontSize = Mathf.Max(11, Mathf.RoundToInt(side * 0.27f));
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.LowerRight;
            label.color = white ? Color.white : WardrobeLook.Accent;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = text ?? "";

            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(1.4f, -1.4f);
            return label;
        }

        private static Sprite Pick(int tabFallback, params string[] names)
        {
            foreach (var name in names)
            {
                try
                {
                    var s = InCombatAtlas(name) ? AtlasUtils.GetQuickButtonSprite(name) : null;
                    if (Ok(s)) return s;
                    s = AtlasUtils.GetBottomPanelSprite(EBottomPanelAtlas.BottomPanel, name);
                    if (Ok(s)) return s;
                    s = AtlasUtils.GetBottomPanelSpriteByName(name);
                    if (Ok(s)) return s;
                }
                catch { }
            }
            try
            {
                var s = AtlasUtils.GetThingTabImage((EThingTabType)tabFallback);
                if (Ok(s)) return s;
            }
            catch { }
            return null;
        }

        private static Sprite Pick(string bottomPanelName, int tabFallback)
        {
            try
            {
                var s = AtlasUtils.GetBottomPanelSprite(EBottomPanelAtlas.BottomPanel, bottomPanelName);
                if (Ok(s)) return s;
                s = AtlasUtils.GetBottomPanelSpriteByName(bottomPanelName);
                if (Ok(s)) return s;
                s = AtlasUtils.GetThingTabImage((EThingTabType)tabFallback);
                if (Ok(s)) return s;
            }
            catch { }
            return null;
        }

        private static bool Ok(Sprite s) => s != null && s.name != "unknown";

        private static bool InCombatAtlas(string name) =>
            AtlasesDatabase.Instance.GetData(new AtlasDatabaseKey(EAtlasType.COMBAT_ICONS_ATLAS, name)) != null;

        internal static void GoHome()
        {
            try
            {
                if (InCombat()) { Plugin.Trace("[buttons] return to town from combat is not allowed"); return; }
                if (ClaimLocked()) { Plugin.Trace("[buttons] return to town from a fight claim is not allowed"); return; }
                Travel.Cancel("ты вернулся в город");
                TownWalk.Go();
            }
            catch (System.Exception e) { Plugin.Fault("[buttons] " + e.Message); }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static bool ClaimLocked()
        {
            try { return IsScene(EUnityScene.ChaoticWait) || IsScene(EUnityScene.QuestCombatWait); }
            catch { return false; }
        }

        private static string _scene;
        private static bool _sceneHooked;
        private static float _sceneAt;

        internal static bool SceneFresh
        {
            get
            {
                Scene();
                return Time.unscaledTime - _sceneAt < 3f;
            }
        }
        private static readonly Dictionary<int, string> SceneNames = new Dictionary<int, string>();
        private static readonly MethodInfo SceneNameOf = HarmonyLib.AccessTools.Method(typeof(SceneWorkFlow), "GetSceneName");

        internal static string Scene()
        {
            if (!_sceneHooked)
            {
                _sceneHooked = true;
                UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (from, to) => { _scene = null; _sceneAt = Time.unscaledTime; };
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += (loaded, mode) => { _scene = null; _sceneAt = Time.unscaledTime; };
                UnityEngine.SceneManagement.SceneManager.sceneUnloaded += unloaded => _scene = null;
            }
            if (_scene != null) return _scene;
            try { _scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? ""; }
            catch { return ""; }
            return _scene;
        }

        private static bool IsScene(EUnityScene scene)
        {
            int id = (int)scene;
            if (!SceneNames.TryGetValue(id, out var name))
            {
                try { name = SceneNameOf?.Invoke(null, new object[] { scene }) as string; }
                catch { name = null; }
                SceneNames[id] = name;
            }
            return name != null ? Scene() == name : SceneWorkFlow.IsCurrentScene(scene);
        }

        internal static bool InCombat()
        {
            try { return IsScene(EUnityScene.CombatLocation); }
            catch { return false; }
        }

        internal static bool LoggedIn()
        {
            try
            {
                var ud = Controllers.User;
                return ud != null && ud.LoggedIn;
            }
            catch { return false; }
        }

        internal static bool InWorld()
        {
            try
            {
                var ud = Controllers.User;
                if (ud == null || !ud.LoggedIn) return false;
                return !IsScene(EUnityScene.Launch) && !IsScene(EUnityScene.CreateCharacter);
            }
            catch { return false; }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(BackgroundScreenshotMaker), "Start")]
    internal static class SideButtonsSnapshotPatch
    {
        private static void Postfix(BackgroundScreenshotMaker __instance)
        {
            SideButtons.ScreenshotTaken(__instance);
        }
    }
}
