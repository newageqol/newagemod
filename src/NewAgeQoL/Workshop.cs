using System;
using System.Collections.Generic;
using Transport.Messages.Common;
using Transport.Messages.Responses.Artworkshop;
using Transport.Messages.Responses.Things.Thingtabs;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Workshop
    {
        private const float RowHeight = 30f;
        private const float Width = 210f;
        private const float Pad = 6f;
        internal const string ButtonName = "QoLWorkshopButton";

        private sealed class Row
        {
            internal int Op;
            internal string Key;
            internal string Plain;
        }

        private static readonly Row[] Rows =
        {
            new Row { Op = 1, Key = "bottompanel.artworkshop.create", Plain = "Создать артефакт" },
            new Row { Op = 3, Key = "bottompanel.artworkshop.update", Plain = "Улучшить артефакт" },
            new Row { Op = 2, Key = "bottompanel.artworkshop.reshoe", Plain = "Изменить артефакт" },
        };

        private static RectTransform _root;
        private static bool _open;
        private static float _leftAt;

        private static object _on;
        private static float _listenAt;
        private static CreateArtDialog _dialog;
        private static bool _mine;
        private static float _askAt;

        internal static bool Here()
        {
            try { return Controllers.Get<ArtWorkshopController>() != null; }
            catch { return false; }
        }

        internal static bool Busy => _dialog != null;

        internal static bool Open => _open && _root != null && _root.gameObject.activeSelf;

        internal static void Toggle()
        {
            _open = !_open;
            _leftAt = 0f;
            if (!_open) Hide();
        }

        internal static void Hide()
        {
            _open = false;
            _leftAt = 0f;
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        internal static void Shut()
        {
            Hide();
            if (_dialog == null) return;
            try { _dialog.Close(); }
            catch (Exception e) { Plugin.Trace("[мастерская] закрытие окна: " + e.Message); }
            _dialog = null;
            _mine = false;
        }

        internal static void Net()
        {
            if (Time.unscaledTime < _listenAt) return;
            _listenAt = Time.unscaledTime + 1f;
            Listen();
        }

        internal static void Hover()
        {
            if (!_open || _root == null || !_root.gameObject.activeSelf) return;
            var eye = Eye();
            if (Inside(_root, eye) || Inside(SideButtons.ButtonRect(ButtonName), eye)) { _leftAt = 0f; return; }
            if (_leftAt <= 0f) { _leftAt = Time.unscaledTime; return; }
            if (Time.unscaledTime - _leftAt > 0.4f) Hide();
        }

        internal static void Tick(bool ready)
        {
            try
            {
                if (_mine && _dialog == null && Here()) _mine = false;
                if (_mine && _dialog == null && _askAt > 0f && Time.unscaledTime > _askAt)
                {
                    _mine = false;
                    _askAt = 0f;
                    Plugin.Trace("[мастерская] сервер не прислал список, жду дальше не буду");
                }
                if (!ready || !_open || Here()) { Hide(); return; }
                if (_root == null) Build();
                if (_root == null) { _open = false; return; }
                Place();
                if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                Plugin.Fault("[мастерская] меню: " + e.Message);
                _open = false;
            }
        }

        private static bool Inside(RectTransform rect, Camera eye)
        {
            return rect != null && rect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, eye);
        }

        private static Canvas _eye;

        private static Camera Eye()
        {
            if (_root == null) return null;
            if (_eye == null) _eye = _root.GetComponentInParent<Canvas>();
            if (_eye == null) return null;
            var root = _eye.rootCanvas != null ? _eye.rootCanvas : _eye;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        private static string Title(Row row)
        {
            try
            {
                string got = ResourceStrings.GetString(row.Key);
                return string.IsNullOrEmpty(got) ? row.Plain : got;
            }
            catch { return row.Plain; }
        }

        private static void Place()
        {
            var panel = SideButtons.PanelOf(ButtonName);
            if (panel == null || _root == null) return;
            if (!ReferenceEquals(_root.parent, panel)) { _root.SetParent(panel, false); _eye = null; }

            float wantY = SideButtons.RowOf(ButtonName);
            bool left = false;
            var canvas = panel.parent as RectTransform;
            if (canvas != null && canvas.rect.width > 1f && canvas.rect.height > 1f)
            {
                Vector2 center = canvas.InverseTransformPoint(panel.TransformPoint(panel.rect.center));
                left = center.x > canvas.rect.width * 0.25f;
                float limit = Mathf.Max(0f, canvas.rect.height * 0.5f - _root.sizeDelta.y * 0.5f);
                wantY = Mathf.Clamp(center.y + wantY, -limit, limit) - center.y;
            }
            _root.pivot = new Vector2(left ? 1f : 0f, 0.5f);
            float half = panel.sizeDelta.x * 0.5f;
            _root.anchoredPosition = new Vector2(left ? -(half + 8f) : half + 8f, wantY);
        }

        private static void Build()
        {
            var panel = SideButtons.PanelOf(ButtonName);
            if (panel == null) return;

            var go = new GameObject("QoLWorkshopMenu", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(panel, false);
            _root = (RectTransform)go.transform;
            _eye = null;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.localScale = Vector3.one;
            _root.sizeDelta = new Vector2(Width, Rows.Length * RowHeight + Pad * 2f);

            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = true;
            WardrobeLook.Frame(go, WardrobeLook.Edge);

            var font = SideButtons.GameFont();
            for (int i = 0; i < Rows.Length; i++)
            {
                var row = Rows[i];
                var line = new GameObject("row" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                line.transform.SetParent(_root, false);
                var rt = (RectTransform)line.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(Pad, 0f);
                rt.offsetMax = new Vector2(-Pad, 0f);
                rt.sizeDelta = new Vector2(-Pad * 2f, RowHeight);
                rt.anchoredPosition = new Vector2(0f, -Pad - i * RowHeight);

                var fill = line.GetComponent<Image>();
                fill.color = Color.white;
                fill.raycastTarget = true;

                var button = line.GetComponent<Button>();
                button.targetGraphic = fill;
                var colors = button.colors;
                colors.normalColor = new Color(1f, 1f, 1f, 0.05f);
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
                colors.pressedColor = new Color(1f, 1f, 1f, 0.18f);
                colors.selectedColor = new Color(1f, 1f, 1f, 0.05f);
                colors.fadeDuration = 0.05f;
                button.colors = colors;
                button.onClick.AddListener(() => { Hide(); Start(row.Op); });

                var textGo = new GameObject("text", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(rt, false);
                var trt = (RectTransform)textGo.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(8f, 0f);
                trt.offsetMax = new Vector2(-8f, 0f);

                var label = textGo.GetComponent<Text>();
                label.font = font;
                label.fontSize = 19;
                label.alignment = TextAnchor.MiddleLeft;
                label.color = WardrobeLook.Bright;
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.text = Title(row);
            }
        }

        private static void Start(int op)
        {
            try
            {
                if (Here()) { Plugin.Trace("[мастерская] я в мастерской, окно откроет сама игра"); return; }
                if (_dialog != null) { Plugin.Trace("[мастерская] окно уже открыто"); return; }
                Listen();
                _mine = true;
                if (op == 1) { Show(1); return; }
                _askAt = Time.unscaledTime + 8f;
                NetworkConnection.Instance.SendRequest(new ArtefactListForOperationRequest(op));
                Plugin.Trace("[мастерская] спросил список вещей для операции " + op);
            }
            catch (Exception e)
            {
                _mine = false;
                Plugin.Warn("[мастерская] операция " + op + ": " + e.Message);
            }
        }

        private static void Show(int op)
        {
            _dialog = DialogFactory.ShowCreateArtDialog(op);
            if (_dialog == null) { _mine = false; Plugin.Warn("[мастерская] окно не создалось"); return; }
            _dialog.RequestArtInfoEvent += Ask;
            _dialog.WizardCompletedEvent += Ready;
            _dialog.OnDialogDestroy += Gone;
            _dialog.ShowCurrentPanel();
            Plugin.Trace("[мастерская] окно операции " + op + " открыто");
        }

        private static void Gone()
        {
            _dialog = null;
            _mine = false;
        }

        private static void Ask()
        {
            try
            {
                if (_dialog == null) return;
                NetworkConnection.Instance.SendRequest(
                    new ArtefactInfoRequest(_dialog.Operation, _dialog.ThingSubtypeInventory, _dialog.SelectedLevel));
            }
            catch (Exception e) { Plugin.Warn("[мастерская] запрос параметров: " + e.Message); }
        }

        private static void Ready()
        {
            try
            {
                if (_dialog == null || _dialog.ArtefactInfo == null) return;
                var price = new Cash(0f, 0f, _dialog.ArtefactInfo.ArtCrystalCost, 0f, 0, 0);
                const string caption = "dialogs.artworkshop.confirm.caption";
                switch (_dialog.Operation)
                {
                    case 1:
                        DialogFactory.ShowPriceConfirmMessageBox(caption, null, Pay, price,
                            "dialogs.artworkshop.create.confirm.text");
                        break;
                    case 3:
                        DialogFactory.ShowPriceConfirmMessageBox(caption, null, Pay, price,
                            "dialogs.artworkshop.upgrade.confirm.text", _dialog.ArtName, _dialog.SelectedLevel);
                        break;
                    case 2:
                        DialogFactory.ShowPriceConfirmMessageBox(caption, null, Pay, price,
                            "dialogs.artworkshop.update.confirm.text", _dialog.ArtName);
                        break;
                }
            }
            catch (Exception e) { Plugin.Warn("[мастерская] подтверждение: " + e.Message); }
        }

        private static void Pay(EMessageBoxResult result)
        {
            try
            {
                if (_dialog == null) return;
                if (result != EMessageBoxResult.MB_OK) { _dialog.EnableButtons(); return; }
                short id = _dialog.Operation == 1 ? (short)362 : _dialog.Operation == 2 ? (short)363 : (short)364;
                NetworkConnection.Instance.SendRequest(
                    new CreateArtRequest(id, _dialog.ArtName, _dialog.ArtefactInfo, _dialog.ThingSubtypeInventory));
                Plugin.Trace("[мастерская] отправил запрос " + id);
            }
            catch (Exception e) { Plugin.Warn("[мастерская] отправка: " + e.Message); }
        }

        private static void Listen()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) { _on = null; return; }
                if (ReferenceEquals(_on, nc)) return;
                nc.RemoveMessageListener(394, OnInfo);
                nc.RemoveMessageListener(395, OnDone);
                nc.RemoveMessageListener(396, OnDone);
                nc.RemoveMessageListener(397, OnDone);
                nc.RemoveMessageListener(400, OnList);
                nc.RemoveMessageListener(429, OnPoor);
                nc.AddMessageListener(394, OnInfo);
                nc.AddMessageListener(395, OnDone);
                nc.AddMessageListener(396, OnDone);
                nc.AddMessageListener(397, OnDone);
                nc.AddMessageListener(400, OnList);
                nc.AddMessageListener(429, OnPoor);
                _on = nc;
            }
            catch { _on = null; }
        }

        private static void OnList(object msg)
        {
            try
            {
                if (!_mine || _dialog != null) return;
                var tab = msg as ThingTabInventoryResponseMessage;
                if (tab == null || tab.Things == null) return;
                var list = new List<InventoryThingTabContentDto>();
                foreach (var thing in tab.Things)
                {
                    if (thing == null || thing.Inventories == null) continue;
                    foreach (var one in thing.Inventories)
                        if (one != null) list.Add(new InventoryThingTabContentDto(thing, one));
                }
                _askAt = 0f;
                Show(tab.TabNumber);
                if (_dialog != null) _dialog.SetInventory(list);
                Plugin.Trace("[мастерская] пришёл список, вещей " + list.Count);
            }
            catch (Exception e) { Plugin.Warn("[мастерская] список вещей: " + e.Message); }
        }

        private static void OnInfo(object msg)
        {
            try
            {
                if (!_mine || _dialog == null) return;
                var info = msg as ArtefactInfoResponseMessage;
                if (info == null) return;
                _dialog.ArtefactInfo = info;
                _dialog.ShowCurrentPanel();
            }
            catch (Exception e) { Plugin.Warn("[мастерская] параметры: " + e.Message); }
        }

        private static void OnDone(object msg)
        {
            try
            {
                if (!_mine || _dialog == null) return;
                var done = msg as BooleanStringMessage;
                if (done == null) return;
                if (done.Success)
                {
                    try { DependencyContainer.GetContainer()?.Resolve<GeneralThingInfoDescriptionManager>()?.Clear(); }
                    catch (Exception e) { Plugin.Trace("[мастерская] сброс описаний: " + e.Message); }
                    _dialog.Close();
                }
                else _dialog.EnableButtons();
                AirMessageScript.ShowErrorNotification(done.Message);
                Plugin.Trace("[мастерская] ответ сервера: " + (done.Success ? "готово" : "отказ"));
            }
            catch (Exception e) { Plugin.Warn("[мастерская] ответ: " + e.Message); }
        }

        private static void OnPoor(object msg)
        {
            try
            {
                if (!_mine || _dialog == null) return;
                _dialog.EnableButtons();
                Plugin.Trace("[мастерская] сервер сказал, что оплаты не хватает");
            }
            catch (Exception e) { Plugin.Trace("[мастерская] нехватка: " + e.Message); }
        }
    }
}
