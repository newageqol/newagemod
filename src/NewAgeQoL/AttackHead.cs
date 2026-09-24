using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class AttackHead
    {
        private const float Room = 205f;

        private static int _who;
        private static int _race;
        private static bool _beast;
        private static int _me;
        private static string _meLogin = "";
        private static string _login = "";
        private static string _head;
        private static bool _painted;
        private static bool _paintedOn;
        private static int _paintedArmor = -1;
        private static readonly HashSet<int> Laid = new HashSet<int>();

        internal static void Aim(int userId, string login, bool beast, int race, int myId, string myLogin)
        {
            _who = userId;
            _race = race;
            _beast = beast;
            _login = login ?? "";
            _me = myId;
            _meLogin = myLogin ?? "";
            _head = null;
            _painted = false;
            _paintedArmor = -1;
            if (beast) Armor.AskBeast(race);
            else Armor.Ask(userId, login);
            Armor.Ask(myId, myLogin);
        }

        private static int Guard(TargetBody part, bool mine)
        {
            if (mine) return Armor.Zone(_me, part);
            return _beast ? Armor.Beast(_race) : Armor.Zone(_who, part);
        }

        internal static void Tick()
        {
            try
            {
                if (_who == 0) return;
                var ctrl = Controllers.Get<ConfirmActionDialogController>();
                if (ctrl == null || !ctrl.Opened) { _who = 0; _login = ""; _head = null; _painted = false; _paintedArmor = -1; return; }
                var view = Dialog(ctrl);
                if (view == null || !view.IsAttackDialog) return;
                Paint(view);
            }
            catch (Exception e) { Plugin.Trace("[удар] заголовок: " + e.Message); }
        }

        private static PropertyInfo _dialogProp;
        private static FieldInfo _nameField, _bodyField, _blocksField, _kicksField, _captionField;
        private static bool _wired;

        private static void Wire()
        {
            if (_wired) return;
            _wired = true;
            try
            {
                _dialogProp = AccessTools.Property(typeof(ConfirmActionDialogController), "ConfirmActionDialog");
                _nameField = AccessTools.Field(typeof(ConfirmActionDialog), "ActionNameText");
                _bodyField = AccessTools.Field(typeof(ConfirmActionDialog), "AttackDescription");
                _blocksField = AccessTools.Field(typeof(AttackDescription), "Blocks");
                _kicksField = AccessTools.Field(typeof(AttackDescription), "Kicks");
                _captionField = AccessTools.Field(typeof(AttackDialogRow), "CaptionText");
            }
            catch (Exception e) { Plugin.Trace("[удар] поля окна: " + e.Message); }
        }

        private static ConfirmActionDialog Dialog(ConfirmActionDialogController ctrl)
        {
            Wire();
            try { return _dialogProp?.GetValue(ctrl) as ConfirmActionDialog; }
            catch { return null; }
        }

        private static void Paint(ConfirmActionDialog view)
        {
            bool on = Armor.On;
            if (_painted && _paintedOn == on && _paintedArmor == Armor.Version) return;
            _painted = true;
            _paintedOn = on;
            _paintedArmor = Armor.Version;
            if (_head == null) _head = Head();

            var name = _nameField?.GetValue(view) as Text;
            if (name != null) name.text = _head;
            if (!on) return;

            var body = _bodyField?.GetValue(view) as AttackDescription;
            if (body == null) return;
            Side(_blocksField?.GetValue(body) as AttackDialogRow[], true);
            Side(_kicksField?.GetValue(body) as AttackDialogRow[], false);
        }

        private static void Side(AttackDialogRow[] rows, bool mine)
        {
            if (rows == null) return;
            foreach (var row in rows)
            {
                if (row == null) continue;
                var caption = _captionField?.GetValue(row) as Text;
                if (caption == null) continue;
                var digits = Column(caption);
                if (Laid.Add(caption.GetInstanceID()))
                {
                    caption.horizontalOverflow = HorizontalWrapMode.Overflow;
                    caption.alignment = TextAnchor.MiddleLeft;
                    caption.fontSize = Mathf.Max(9, Mathf.RoundToInt(caption.fontSize * 0.85f));
                    var box = caption.rectTransform;
                    if (box.sizeDelta.x < Room) box.sizeDelta = new Vector2(Room, box.sizeDelta.y);
                    var wide = caption.GetComponent<LayoutElement>();
                    if (wide == null) wide = caption.gameObject.AddComponent<LayoutElement>();
                    wide.preferredWidth = Room;
                    wide.minWidth = Room;
                }
                caption.text = Spot(row.BodyPart);
                if (digits == null) continue;
                int guard = Guard(row.BodyPart, mine);
                digits.text = guard < 0 ? "" : guard.ToString();
            }
        }

        private static Text Column(Text caption)
        {
            try
            {
                var had = caption.transform.Find("QoLArmorNum");
                if (had != null) return had.GetComponent<Text>();

                var stale = caption.transform.parent != null ? caption.transform.parent.Find("QoLArmorNum") : null;
                if (stale != null) UnityEngine.Object.Destroy(stale.gameObject);

                var go = UnityEngine.Object.Instantiate(caption.gameObject, caption.transform.parent);
                go.name = "QoLArmorNum";
                foreach (var nested in go.GetComponentsInChildren<Transform>(true))
                    if (nested != null && nested != go.transform) UnityEngine.Object.Destroy(nested.gameObject);
                var extra = go.GetComponent<LayoutElement>();
                if (extra != null) UnityEngine.Object.Destroy(extra);
                go.transform.SetParent(caption.transform, false);

                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;

                var text = go.GetComponent<Text>();
                text.alignment = TextAnchor.MiddleRight;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.color = new Color32(138, 90, 30, 255);
                text.raycastTarget = false;
                return text;
            }
            catch (Exception e) { Plugin.Trace("[удар] колонка: " + e.Message); return null; }
        }

        private static string Spot(TargetBody part)
        {
            string name;
            try { name = ResourceStrings.GetString("combat.gui.body." + (int)part); }
            catch { name = ""; }
            return string.IsNullOrEmpty(name) ? Plain(part) : name;
        }

        private static string Plain(TargetBody part)
        {
            switch (part)
            {
                case TargetBody.Head: return "Голова";
                case TargetBody.Body: return "Корпус";
                case TargetBody.LeftHand: return "Левая рука";
                case TargetBody.RightHand: return "Правая рука";
                default: return "Ноги";
            }
        }

        private static string Head()
        {
            string caption;
            try { caption = ResourceStrings.GetString("combat.gui.attack.dialog.caption"); }
            catch { caption = "Атака"; }
            if (string.IsNullOrEmpty(caption)) caption = "Атака";
            return _login.Length > 0 ? caption + " — " + _login : caption;
        }
    }
}
