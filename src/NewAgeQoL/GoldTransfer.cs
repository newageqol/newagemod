using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class GoldTransfer
    {
        private const string RootName = "QoLGoldTransfer";

        private static readonly object Gate = new object();
        private static bool _busy;
        private static string _status = "";
        private static bool _good;
        private static int _version;
        private static string _announce;

        internal static bool Busy { get { lock (Gate) return _busy; } }
        internal static int Version { get { lock (Gate) return _version; } }

        internal static void Tick()
        {
            string say;
            lock (Gate) { say = _announce; _announce = null; }
            if (say != null) Notice.Show(say, 8f);
        }

        private static string Spare() => (Plugin.CfgOnlineLogin?.Value ?? "").Trim();

        private static string Mine()
        {
            try
            {
                var info = Controllers.User?.UserInfo;
                return info != null && !string.IsNullOrEmpty(info.Login) ? info.Login.Trim() : "";
            }
            catch { return ""; }
        }

        private static void Set(string status, bool good)
        {
            lock (Gate) { _status = status; _good = good; _version++; }
        }

        internal static void Open(RectTransform card, string target, Font font)
        {
            if (card == null || string.IsNullOrEmpty(target)) return;
            var old = card.Find(RootName);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);

            var veil = new GameObject(RootName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            veil.transform.SetParent(card, false);
            veil.GetComponent<LayoutElement>().ignoreLayout = true;
            var vrt = (RectTransform)veil.transform;
            OnlineWindow.Place(vrt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            veil.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            const float w = 440f, h = 330f;
            var panel = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panel.transform.SetParent(veil.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(w, h);
            var back = panel.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            var edge = panel.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Accent;
            edge.effectDistance = new Vector2(1f, -1f);

            float y = 14f;
            var title = Say(prt, "Перевод золота игроку " + target, 17, FontStyle.Bold, WardrobeLook.Accent, font);
            Wardrobe.At(title.rectTransform, 16f, y, w - 32f, 28f);
            y += 32f;

            string spare = Spare();
            string me = Mine();
            string from = spare.Length == 0
                ? "Запасной аккаунт не указан в настройках мода (тот же, что для «Кто в игре»)"
                : "Перевод пойдёт с персонажа " + spare + " — запасного аккаунта из настроек мода";
            var source = Say(prt, from, 14, FontStyle.Normal, WardrobeLook.Body, font);
            source.horizontalOverflow = HorizontalWrapMode.Wrap;
            Wardrobe.At(source.rectTransform, 16f, y, w - 32f, 40f);
            y += 46f;

            var amountLabel = Say(prt, "Сумма", 15, FontStyle.Normal, WardrobeLook.Label, font);
            Wardrobe.At(amountLabel.rectTransform, 16f, y, 120f, 32f);
            var amount = OnlineWindow.MakeInput(prt, w - 152f, "сколько золота");
            WardrobeLook.Style(amount);
            amount.contentType = InputField.ContentType.Custom;
            amount.characterValidation = InputField.CharacterValidation.None;
            amount.onValidateInput = (text, index, c) => (c >= '0' && c <= '9') || c == '.' || c == ',' ? c : '\0';
            amount.characterLimit = 12;
            Wardrobe.At((RectTransform)amount.transform, 136f, y, w - 152f, 32f);
            y += 40f;

            var noteLabel = Say(prt, "Комментарий", 15, FontStyle.Normal, WardrobeLook.Label, font);
            Wardrobe.At(noteLabel.rectTransform, 16f, y, 120f, 32f);
            var note = OnlineWindow.MakeInput(prt, w - 152f, "необязательно");
            WardrobeLook.Style(note);
            note.characterLimit = 100;
            Wardrobe.At((RectTransform)note.transform, 136f, y, w - 152f, 32f);
            y += 42f;

            var status = Say(prt, "", 14, FontStyle.Normal, WardrobeLook.Body, font);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.alignment = TextAnchor.UpperLeft;
            Wardrobe.At(status.rectTransform, 16f, y, w - 32f, 64f);

            var keeper = veil.AddComponent<GoldPanel>();
            keeper.Status = status;
            keeper.Seen = Version;
            keeper.Panel = prt;
            keeper.Font = font;
            keeper.From = spare;

            float bw = (w - 32f - 12f) / 2f;
            var send = Wardrobe.GameButton(prt, "Перевести", () => keeper.Press(target, amount.text, note.text), false);
            Wardrobe.At(send, 16f, h - 48f, bw, 34f);
            keeper.Send = send.GetComponent<Button>();
            var close = Wardrobe.GameButton(prt, "Закрыть", () => UnityEngine.Object.Destroy(veil), false);
            Wardrobe.At(close, 16f + bw + 12f, h - 48f, bw, 34f);

            if (spare.Length == 0) keeper.Lock("Укажи логин и пароль запасного аккаунта в настройках мода.");
            else if (me.Length > 0 && string.Equals(me, spare, StringComparison.OrdinalIgnoreCase))
                keeper.Lock("В настройках указан тот же персонаж, которым ты играешь — нужен запасной.");
            else if (string.Equals(target, spare, StringComparison.OrdinalIgnoreCase))
                keeper.Lock("Это и есть запасной персонаж — переводить самому себе нельзя.");
            else if (Busy) keeper.Lock(null);
        }

        private static Text Say(RectTransform parent, string text, int size, FontStyle style, Color color, Font font)
        {
            var label = OnlineWindow.Label(parent, text, size, style, color);
            if (font != null) label.font = font;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }

        internal static bool Amount(string typed, out double value)
        {
            value = 0;
            string clean = (typed ?? "").Trim().Replace(',', '.');
            return clean.Length > 0
                   && double.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
                   && value > 0;
        }

        internal static string Shown(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        internal static bool Start(string target, double amount, string comment)
        {
            string why = SpareSession.Refuse();
            if (why != null)
            {
                Set(why + ".", false);
                return false;
            }
            lock (Gate)
            {
                if (_busy) return false;
                _busy = true;
                _status = SpareSession.Online ? "Отправляю перевод…" : "Вхожу запасным аккаунтом…";
                _good = true;
                _version++;
            }
            Plugin.Trace("[gold] transfer started");
            SpareSession.Run("gold", false, link => Work(link, target, amount, comment ?? ""), Failed);
            return true;
        }

        private static void Failed(string why)
        {
            Plugin.Trace("[gold] transfer not done");
            Done("Не вышло: " + why, false);
        }

        private static void Done(string result, bool ok)
        {
            lock (Gate)
            {
                _status = result;
                _good = ok;
                _busy = false;
                _announce = result;
                _version++;
            }
        }

        internal static bool Read(ref int seen, out string status, out bool good, out bool busy)
        {
            lock (Gate)
            {
                status = _status;
                good = _good;
                busy = _busy;
                if (seen == _version) return false;
                seen = _version;
                return true;
            }
        }

        private static string Attr(string xml, string key)
        {
            var m = Regex.Match(xml ?? "", "\\b" + key + "=\"([^\"]*)\"");
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : "";
        }

        private static void Work(SpareLink link, string target, double amount, string comment)
        {
            double? balance = link.Balance;
            if (balance == null) throw new Exception("не удалось узнать, есть ли золото на запасном аккаунте");
            if (balance.Value <= 0.0001) throw new Exception("на запасном аккаунте нет золота");
            if (balance.Value + 0.0001 < amount) throw new Exception("на запасном аккаунте не хватает золота на эту сумму");

            Plugin.Trace("[gold] spare has gold, sending request");
            Set("Отправляю перевод…", true);

            SpareSession.Send(link, "<Message type=\"79\"><CashTransfer amount=\"" + SpareSession.Esc(Shown(amount)) + "\" user=\"" + SpareSession.Esc(target) + "\" signature=\"" + SpareSession.Esc(comment) + "\" /></Message>");
            string answer = null;
            foreach (var m in SpareSession.Pump(link, 15, m => m.Contains("type=\"217\"")))
                if (m.Contains("type=\"217\"")) answer = m;
            if (answer == null) throw new SpareReset("сервер не ответил на перевод, соединение запасного аккаунта сброшено");
            string success = Attr(answer, "success");
            bool ok = success == "1" || success == "true";
            string msg = Attr(answer, "msg");
            Plugin.Trace("[gold] transfer " + (ok ? "accepted" : "refused") + " by server");
            Done((ok ? "Перевод принят" : "Перевод отклонён") + (msg.Length > 0 ? ": " + msg : "."), ok);
        }
    }

    internal sealed class GoldPanel : MonoBehaviour
    {
        internal Text Status;
        internal Button Send;
        internal RectTransform Panel;
        internal Font Font;
        internal string From;
        internal int Seen;
        private bool _locked;
        private GameObject _ask;

        internal void Lock(string why)
        {
            if (why != null)
            {
                _locked = true;
                Paint(why, false);
            }
            if (Send != null) Send.interactable = false;
        }

        internal void Press(string target, string typed, string comment)
        {
            if (_locked || GoldTransfer.Busy || _ask != null) return;
            double amount;
            if (!GoldTransfer.Amount(typed, out amount))
            {
                Paint("Впиши сумму больше нуля.", false);
                return;
            }
            Paint("", true);
            Ask(target, amount, comment ?? "");
        }

        private void Ask(string target, double amount, string comment)
        {
            if (Panel == null) return;
            _ask = new GameObject("ask", typeof(RectTransform), typeof(Image));
            _ask.transform.SetParent(Panel, false);
            OnlineWindow.Place((RectTransform)_ask.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var dim = _ask.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.sprite = OnlineWindow.Rounded(10);
            dim.type = Image.Type.Sliced;

            const float w = 380f, h = 180f;
            var box = new GameObject("box", typeof(RectTransform), typeof(Image), typeof(Outline));
            box.transform.SetParent(_ask.transform, false);
            var brt = (RectTransform)box.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(w, h);
            var back = box.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            var edge = box.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Accent;
            edge.effectDistance = new Vector2(1f, -1f);

            string question = "Перевести " + GoldTransfer.Shown(amount) + " золота игроку " + target
                              + (string.IsNullOrEmpty(From) ? "?" : " с персонажа " + From + "?");
            var text = OnlineWindow.Label(box.transform, question, 16, FontStyle.Bold, WardrobeLook.Bright);
            if (Font != null) text.font = Font;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.raycastTarget = false;
            Wardrobe.At(text.rectTransform, 16f, 14f, w - 32f, h - 80f);

            float bw = (w - 32f - 12f) / 2f;
            var yes = Wardrobe.GameButton(brt, "Перевести", () =>
            {
                Close();
                if (GoldTransfer.Start(target, amount, comment) && Send != null) Send.interactable = false;
            }, false);
            Wardrobe.At(yes, 16f, h - 50f, bw, 34f);
            var no = Wardrobe.GameButton(brt, "Отмена", Close, false);
            Wardrobe.At(no, 16f + bw + 12f, h - 50f, bw, 34f);
        }

        private void Close()
        {
            if (_ask != null) Destroy(_ask);
            _ask = null;
        }

        private void Paint(string text, bool good)
        {
            if (Status == null) return;
            Status.text = text;
            Status.color = good ? WardrobeLook.Body : WardrobeLook.Bad;
        }

        private void Update()
        {
            string status;
            bool good, busy;
            bool fresh = GoldTransfer.Read(ref Seen, out status, out good, out busy);
            if (Send != null && !_locked && Send.interactable == busy) Send.interactable = !busy;
            if (!fresh || _locked || string.IsNullOrEmpty(status)) return;
            if (!busy && good)
            {
                Destroy(gameObject);
                return;
            }
            if (Status == null) return;
            Status.text = status;
            Status.color = busy ? WardrobeLook.Body : good ? WardrobeLook.Good : WardrobeLook.Bad;
        }
    }
}
