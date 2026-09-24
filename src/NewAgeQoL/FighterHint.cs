using System;
using System.Collections.Generic;
using System.Text;
using Transport.Messages.Common.List;
using Transport.Messages.Responses.Combat.States;
using Transport.Messages.Responses.User.Info;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class FighterHint
    {
        private const float Look = 0.08f;
        private const float Wait = 0.3f;
        private const float Fresh = 3f;
        private const float BoardW = 300f;
        private const float NameMax = 146f;
        private const float SrcMax = 92f;
        private const float Gap = 7f;
        private const int CellFont = 9;
        private static int _rowIndex;
        private static float _tableW;
        private static Text _measure;
        private static LayoutElement _ble;

        private static float _lookAt, _hoverAt;
        private static int _hoverId, _shownId;
        private static object _on;
        private static Canvas _canvas;
        private static RectTransform _host;
        private static RectTransform _board;
        private static CanvasGroup _veil;
        private static Text _title, _range, _meta, _branch;
        private static Text _hp, _mp, _sp;
        private static GameObject _barsGo;
        private static Transform _rows;
        private static string _rowsSig = "";
        private static bool _reveal;
        private static int _watchId = -1, _watchLife, _watchMana, _watchStamina, _watchPower, _watchQueue;
        private static bool _watchFriend;
        internal static readonly Dictionary<int, List<UserEnchantmentsResponseItem>> States = new Dictionary<int, List<UserEnchantmentsResponseItem>>();
        internal static readonly Dictionary<int, List<UserEnchantmentsResponseItem>> Rough = new Dictionary<int, List<UserEnchantmentsResponseItem>>();
        internal static readonly Dictionary<int, float> AskedAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static readonly HashSet<int> AskedNames = new HashSet<int>();
        private static readonly Dictionary<int, float> FirstAsk = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> NudgedAt = new Dictionary<int, float>();
        private static readonly HashSet<int> Swept = new HashSet<int>();
        internal static int NamesVersion;
        private static bool _wasFight;
        private static float _sweepAt;
        private static readonly StringBuilder Sig = new StringBuilder();
        private static readonly StringBuilder Meta = new StringBuilder();


        internal static void Tick()
        {
            try
            {
                Listen();
                bool fight = SideButtons.InCombat();
                if (fight != _wasFight) { _wasFight = fight; Forget(); Branches.NewFight(); }
                if (!fight) { Hide(); return; }
                Sweep(Cd());
                if (SkillList.Armed) { Hide(); return; }
                if (Time.unscaledTime < _lookAt) return;
                _lookAt = Time.unscaledTime + Look;

                var cd = Cd();
                int id = cd == null ? 0 : Under(cd, false, Slack);
                if (id == 0 || Unity3DHelper.IsOverInterface()) { Hide(); return; }
                var who = cd.GetCharacter(id);
                if (who == null) { Hide(); return; }
                if (id != _hoverId)
                {
                    Hide();
                    _hoverId = id;
                    _hoverAt = Time.unscaledTime + Wait;
                    Ask(id);
                    return;
                }
                if (Time.unscaledTime < _hoverAt) return;
                var ch = cd.GetCharacter(id);
                if (ch == null) { Hide(); return; }

                Build();
                bool fresh = _shownId != id || !_board.gameObject.activeSelf;
                Fill(ch, cd);
                _shownId = id;
                if (fresh)
                {
                    _veil.alpha = 0f;
                    _reveal = true;
                    _board.gameObject.SetActive(true);
                    return;
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(_board);
                if (_reveal) { _reveal = false; Place(); _veil.alpha = 1f; }
                else Fit();
                float at;
                if (!AskedAt.TryGetValue(id, out at) || Time.unscaledTime - at > Fresh) Ask(id);
            }
            catch (Exception e) { Plugin.Trace("[боец] подсказка: " + e.Message); }
        }

        private static void Hide()
        {
            _hoverId = 0;
            _shownId = 0;
            if (_board != null && _board.gameObject.activeSelf) _board.gameObject.SetActive(false);
        }

        internal static ICombatData Cd()
        {
            try { return Controllers.User?.CombatData; }
            catch { return null; }
        }

        internal static int Under(ICombatData cd)
        {
            return Under(cd, true);
        }

        internal static int Under(ICombatData cd, bool playersOnly)
        {
            return Under(cd, playersOnly, Slack);
        }

        private const int Memo = 4;
        private static readonly int[] MemoId = new int[Memo];
        private static readonly float[] MemoSlack = new float[Memo];
        private static readonly bool[] MemoOnly = new bool[Memo];
        private static int _memoFrame = -1;
        private static int _memoCount;

        internal static int Under(ICombatData cd, bool playersOnly, float slack)
        {
            int frame = Time.frameCount;
            if (_memoFrame != frame) { _memoFrame = frame; _memoCount = 0; }
            for (int i = 0; i < _memoCount; i++)
                if (MemoOnly[i] == playersOnly && MemoSlack[i] == slack) return MemoId[i];

            int found = Pick(cd, playersOnly);
            if (_memoCount < Memo)
            {
                MemoOnly[_memoCount] = playersOnly;
                MemoSlack[_memoCount] = slack;
                MemoId[_memoCount] = found;
                _memoCount++;
            }
            return found;
        }

        private static int Pick(ICombatData cd, bool playersOnly)
        {
            AbstractCharacter pick;
            if (!FlashLook.Under(out pick))
            {
                var camera = Eye();
                if (camera == null) return 0;
                pick = Ray(cd, camera, false) ?? OnHex(cd, false);
            }
            if (pick == null) return 0;
            return playersOnly && pick.IsBot ? 0 : pick.UserId;
        }

        internal static AbstractCharacter OnHex(ICombatData cd, bool aliveOnly)
        {
            AbstractCharacter corpse = null;
            try
            {
                OffsetCoord hex;
                if (!SkillList.HexUnder(out hex) || hex == null) return null;
                foreach (var pair in cd.Characters)
                {
                    var ch = pair.Value;
                    if (ch == null || ch.UserId == 0) continue;
                    var spot = ch.HexGridPosition;
                    if (spot == null || !spot.Equals(hex)) continue;
                    if (!ch.Dead && ch.Initialized) return ch;
                    if (!aliveOnly && corpse == null) corpse = ch;
                }
            }
            catch (Exception e) { Plugin.Trace("[боец] клетка под мышью: " + e.Message); }
            return corpse;
        }

        private static readonly RaycastHit[] Beam = new RaycastHit[64];

        internal static AbstractCharacter Ray(ICombatData cd, Camera camera, bool aliveOnly)
        {
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            int found = Physics.RaycastNonAlloc(ray, Beam, 1000f);
            AbstractCharacter best = null, corpse = null;
            float near = float.MaxValue, low = float.MaxValue;
            for (int i = 0; i < found; i++)
            {
                var hit = Beam[i];
                if (hit.collider == null) continue;
                var who = cd.FindCharacterByGameObject(hit.collider.gameObject);
                if (who == null || who.UserId == 0) continue;
                if (who.Dead || !who.Initialized)
                {
                    if (aliveOnly || hit.distance >= low) continue;
                    low = hit.distance;
                    corpse = who;
                    continue;
                }
                if (hit.distance >= near) continue;
                near = hit.distance;
                best = who;
            }
            return best ?? corpse;
        }

        internal const float Slack = 0.012f;

        internal static float Limit(float slack)
        {
            return Mathf.Max(12f, Screen.height * slack);
        }

        internal static Camera Eye()
        {
            var view = CombatView();
            return view != null ? view.CombatCamera : null;
        }

        internal static CombatLocationView CombatView()
        {
            var view = BaseLocationView.GetInstance() as CombatLocationView;
            return view != null ? view : null;
        }

        internal static bool Zone(Camera camera, AbstractCharacter ch, out Rect zone)
        {
            bool capsule;
            return Zone(camera, ch, out zone, out capsule);
        }

        internal static bool Zone(Camera camera, AbstractCharacter ch, out Rect zone, out bool capsule)
        {
            zone = new Rect();
            capsule = true;
            if (camera == null || ch == null || ch.HexGridPosition == null) return false;
            var view = CombatView();
            var grid = view != null && view.HexGrid != null ? view.HexGrid.transform : null;
            if (grid == null) return false;
            var center = HexUtils.offsetToPixelInWordSpace(grid, ch.HexGridPosition);
            float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            for (int k = 0; k < 6; k++)
            {
                float angle = -Mathf.PI / 3f * (k + 0.5f);
                var corner = center + grid.TransformVector(new Vector3(MathConsts.HEX_SIZE * Mathf.Cos(angle), 0f, MathConsts.HEX_SIZE * Mathf.Sin(angle)));
                var spot = camera.WorldToScreenPoint(corner);
                if (spot.z <= 0f) return false;
                left = Mathf.Min(left, spot.x);
                right = Mathf.Max(right, spot.x);
                bottom = Mathf.Min(bottom, spot.y);
                top = Mathf.Max(top, spot.y);
            }
            zone = Rect.MinMaxRect(left, bottom, right, top);
            return true;
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(109, OnStates);
            nc.AddMessageListener(109, OnStates);
            nc.RemoveMessageListener(408, OnGroups);
            nc.AddMessageListener(408, OnGroups);
            nc.RemoveMessageListener(407, OnChanging);
            nc.AddMessageListener(407, OnChanging);
            nc.RemoveMessageListener(24, OnRound);
            nc.AddMessageListener(24, OnRound);
            nc.RemoveMessageListener(433, OnWho);
            nc.AddMessageListener(433, OnWho);
            _on = nc;
        }

        private static void OnStates(object m)
        {
            var msg = m as UserEnchantmentsResponseMessage;
            if (msg == null) return;
            var fresh = msg.Items != null ? new List<UserEnchantmentsResponseItem>(msg.Items) : new List<UserEnchantmentsResponseItem>();
            List<UserEnchantmentsResponseItem> was;
            bool same = States.TryGetValue(msg.UserId, out was) && was != null && was.Count == fresh.Count;
            States[msg.UserId] = fresh;
            FirstAsk.Remove(msg.UserId);
            if (!same) Plugin.Trace("[боец] состояния " + msg.UserId + ": " + fresh.Count);
        }

        private static void Forget()
        {
            States.Clear();
            Rough.Clear();
            AskedNames.Clear();
            AskedAt.Clear();
            FirstAsk.Clear();
            NudgedAt.Clear();
            Swept.Clear();
            _sweepAt = 0f;
            _rowsSig = "";
        }

        private static void Sweep(ICombatData cd)
        {
            if (cd == null || cd.Characters == null) return;
            if (Time.unscaledTime < _sweepAt) return;
            _sweepAt = Time.unscaledTime + 1f;
            int myId = cd.MyCharacter != null ? cd.MyCharacter.UserId : 0;
            bool foes = false;
            foreach (var kv in cd.Characters)
            {
                var one = kv.Value;
                if (one == null || one.UserId <= 0 || one.UserId == myId) continue;
                if (cd.MyCharacter != null && one.Team == cd.MyCharacter.Team) continue;
                foes = true;
                break;
            }
            int fresh = 0;
            foreach (var kv in cd.Characters)
            {
                if (kv.Value == null || kv.Key == 0) continue;
                if (foes && kv.Key != myId) Branches.Want(kv.Key, kv.Value.Login);
                if (!Swept.Add(kv.Key)) continue;
                Ask(kv.Key);
                fresh++;
            }
            if (fresh > 0) Plugin.Trace("[боец] спрошены состояния у " + fresh + " бойцов");
        }

        private static void OnRound(object m)
        {
            Swept.Clear();
            _sweepAt = 0f;
        }

        private static void OnChanging(object m)
        {
            var msg = m as ListIntMessage;
            if (msg == null || msg.Value == null) return;
            foreach (int id in msg.Value) Nudge(id);
        }

        private static void Nudge(int userId)
        {
            if (userId == 0) return;
            float at;
            if (NudgedAt.TryGetValue(userId, out at) && Time.unscaledTime - at < 0.8f) return;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                NudgedAt[userId] = Time.unscaledTime;
                nc.SendRequest(new GetStateGroupsOnUserRequest(userId));
                nc.SendRequest(new UserEnchantmentsRequest(userId));
            }
            catch (Exception e) { Plugin.Trace("[боец] толчок " + userId + ": " + e.Message); }
        }

        private static void OnGroups(object m)
        {
            var msg = m as StateGroupsOnUserResponseMessage;
            if (msg == null) return;
            var list = new List<UserEnchantmentsResponseItem>();
            if (msg.Groups != null)
                foreach (var g in msg.Groups)
                    if (g != null)
                        list.Add(new UserEnchantmentsResponseItem(g.StateId, g.StateType, g.Highlighting, 0));
            List<UserEnchantmentsResponseItem> had;
            bool same = Rough.TryGetValue(msg.UserId, out had) && had != null && had.Count == list.Count;
            Rough[msg.UserId] = list;
            FirstAsk.Remove(msg.UserId);
            if (!same) Plugin.Trace("[боец] группы состояний " + msg.UserId + ": " + list.Count);
        }

        private static void OnWho(object m)
        {
            var info = m as Unity3DUserInfoResponseMessage;
            if (info == null || info.UserId <= 0 || string.IsNullOrEmpty(info.Login)) return;
            string had;
            if (Names.TryGetValue(info.UserId, out had) && had == info.Login) return;
            Names[info.UserId] = info.Login;
            NamesVersion++;
        }

        private static string NameOf(int userId)
        {
            if (userId <= 0) return "";
            string name;
            if (Names.TryGetValue(userId, out name) && !string.IsNullOrEmpty(name)) return name;
            AskName(userId);
            return "#" + userId;
        }

        private static void AskName(int userId)
        {
            if (userId <= 0 || AskedNames.Contains(userId)) return;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                nc.SendRequest(new UserInfoRequest(userId, null));
                AskedNames.Add(userId);
            }
            catch (Exception e) { Plugin.Trace("[боец] имя " + userId + ": " + e.Message); }
        }

        internal static bool Silent(int userId)
        {
            float at;
            return FirstAsk.TryGetValue(userId, out at) && Time.unscaledTime - at > 1.5f;
        }

        internal static void Ask(int userId)
        {
            if (userId == 0) return;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                AskedAt[userId] = Time.unscaledTime;
                if (!FirstAsk.ContainsKey(userId)) FirstAsk[userId] = Time.unscaledTime;
                nc.SendRequest(new UserEnchantmentsRequest(userId));
                NudgedAt[userId] = Time.unscaledTime;
                nc.SendRequest(new GetStateGroupsOnUserRequest(userId));
            }
            catch (Exception e) { Plugin.Trace("[боец] запрос состояний: " + e.Message); }
        }

        private static void Fill(AbstractCharacter ch, ICombatData cd)
        {
            bool me = cd.MyCharacter != null && cd.MyCharacter.UserId == ch.UserId;
            bool friend = me || (cd.MyCharacter != null && ch.Team == cd.MyCharacter.Team);
            _title.text = (ch.Login ?? "?") + (me ? "  (ты)" : "");
            _range.text = Steps(ch, cd, me);
            _range.gameObject.SetActive(_range.text.Length > 0);
            var meta = Meta;
            meta.Length = 0;
            if (ch.Level > 0) meta.Append("Уровень: ").Append(ch.Level);
            if (ch.Rank > 0) meta.Append(meta.Length > 0 ? "      " : "").Append("Рейтинг: ").Append(ch.Rank);
            _meta.text = meta.ToString();
            _meta.gameObject.SetActive(meta.Length > 0);
            _branch.text = ch.UserId > 0 && !me ? Branches.Text(ch.UserId) : "";
            _branch.gameObject.SetActive(_branch.text.Length > 0);

            var ind = ch.Indicators;
            if (ind != null)
            {
                var later = Outcome.Of(cd, ch.UserId);
                int life = ind.CurrentLife + later.Life;
                int mana = ind.CurrentMana + later.Mana;
                int expower = ind.CurrentExpower + later.Expower;
                _hp.text = life + " / " + ind.MaxLife;
                _mp.text = mana + " / " + ind.MaxMana;
                _sp.text = friend ? ind.CurrentStamina + later.Stamina + " / " + ind.MaxStamina : "";
                _sp.transform.parent.gameObject.SetActive(friend);
                _barsGo.SetActive(true);
                if (Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                {
                    int stamina = friend ? ind.CurrentStamina + later.Stamina : 0;
                    if (_watchId != ch.UserId || _watchLife != life || _watchMana != mana
                        || _watchStamina != stamina || _watchPower != expower || _watchQueue != later.Life
                        || _watchFriend != friend)
                    {
                        _watchId = ch.UserId;
                        _watchLife = life;
                        _watchMana = mana;
                        _watchStamina = stamina;
                        _watchPower = expower;
                        _watchQueue = later.Life;
                        _watchFriend = friend;
                        Plugin.Trace("[боец] показатели " + ch.UserId + ": жизнь " + life + "/" + ind.MaxLife
                            + ", мана " + mana + "/" + ind.MaxMana
                            + (friend ? ", энергия " + stamina + "/" + ind.MaxStamina : "")
                            + ", заряды " + expower + "/" + ind.MaxExpower
                            + (later.Life != 0 ? ", из них жизнь " + later.Life + " ещё в очереди анимаций" : ""));
                    }
                }
            }
            else _barsGo.SetActive(false);

            List<UserEnchantmentsResponseItem> items;
            bool known = Effects(ch.UserId, out items);
            var sig = Sig;
            sig.Length = 0;
            sig.Append(ch.UserId).Append('|').Append(NamesVersion).Append('|');
            if (known)
                foreach (var it in items)
                {
                    sig.Append(it.StateType).Append(':').Append(it.StateId).Append(':').Append(it.Duration).Append(':').Append(it.Highlighting).Append(':').Append(Power(it)).Append(':');
                    if (it.Sources != null) foreach (var s in it.Sources) sig.Append(s.SourceUserId).Append('/');
                    sig.Append(';');
                }
            else sig.Append(Silent(ch.UserId) ? "?!" : "?");
            sig.Append('|').Append(Aura(ch));
            string s2 = sig.ToString();
            if (s2 != _rowsSig)
            {
                _rowsSig = s2;
                Table(ch, cd, known, items);
            }
            Width();
        }

        internal static string Aura(AbstractCharacter ch)
        {
            try
            {
                var bot = ch as BotCharacter;
                var fit = bot != null ? bot.FitmentAura : null;
                if (fit == null) return "";
                return (fit.Good ? "добрая аура" : "злая аура") + ", радиус " + fit.Range + " " + Cells(fit.Range);
            }
            catch { return ""; }
        }

        internal static bool AuraGood(AbstractCharacter ch)
        {
            var bot = ch as BotCharacter;
            return bot != null && bot.FitmentAura != null && bot.FitmentAura.Good;
        }

        internal static int Far(AbstractCharacter ch, ICombatData cd)
        {
            var mine = cd != null ? cd.MyCharacter : null;
            if (mine == null) return -1;
            try { return HexUtils.range(mine.HexGridPosition, ch.HexGridPosition); }
            catch (Exception e) { Plugin.Trace("[боец] расстояние: " + e.Message); return -1; }
        }

        internal static string Steps(AbstractCharacter ch, ICombatData cd, bool me)
        {
            if (me) return "это ты";
            int far = Far(ch, cd);
            if (far < 0) return "";
            return "До цели: " + far + " " + Cells(far);
        }

        private static string Cells(int count)
        {
            int tail = count % 100;
            if (tail >= 11 && tail <= 14) return "клеток";
            switch (count % 10)
            {
                case 1: return "клетка";
                case 2:
                case 3:
                case 4: return "клетки";
                default: return "клеток";
            }
        }

        private static void Table(AbstractCharacter ch, ICombatData cd, bool known, List<UserEnchantmentsResponseItem> items)
        {
            for (int i = _rows.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_rows.GetChild(i).gameObject);
            _rowIndex = 0;

            var list = new List<string[]>();
            var tint = new List<Color32>();
            bool head = known && items.Count > 0;
            if (head)
            {
                var body = new List<string[]>();
                var paint = new List<Color32>();
                foreach (var it in items)
                {
                    string key = "states.state_" + it.StateType + "_" + it.StateId;
                    string name = ResourceStrings.GetString(key + ".name");
                    if (name == key + ".name") name = "состояние " + it.StateType + "/" + it.StateId;
                    int power = Power(it);
                    string dur = it.Duration > 1000 ? "до конца боя" : it.Duration > 0 ? it.Duration + " " + Turns(it.Duration) : "";
                    body.Add(new[] { name, Sources(it, cd, ch.UserId), power != 0 ? power.ToString() : "", dur });
                    paint.Add(it.Highlighting == (int)EHighlightingType.Positive ? WardrobeLook.Good
                            : it.Highlighting == (int)EHighlightingType.Negative ? WardrobeLook.Bad
                            : WardrobeLook.Body);
                }
                var cap = new[] { "Название", "Источник", "Эффект", "Длительность" };
                bool extra = false;
                for (int c = 1; c < 4; c++)
                {
                    bool any = false;
                    foreach (var row in body) if (row[c].Length > 0) { any = true; break; }
                    if (any) extra = true; else cap[c] = "";
                }
                head = extra;
                if (head)
                {
                    list.Add(cap);
                    tint.Add(WardrobeLook.Label);
                }
                list.AddRange(body);
                tint.AddRange(paint);
            }
            else
            {
                list.Add(new[] { Status(ch, cd, known), "", "", "" });
                tint.Add(WardrobeLook.Body);
            }

            string aura = Aura(ch);
            if (aura.Length > 0)
            {
                list.Add(new[] { aura, "", "", "" });
                tint.Add(AuraGood(ch) ? WardrobeLook.Good : WardrobeLook.Bad);
            }

            var wide = new float[4];
            for (int i = 0; i < list.Count; i++)
            {
                var style = head && i == 0 ? FontStyle.Bold : FontStyle.Normal;
                list[i][0] = Clip(list[i][0], NameMax, style);
                list[i][1] = Clip(list[i][1], SrcMax, style);
                for (int c = 0; c < 4; c++) wide[c] = Mathf.Max(wide[c], Measure(list[i][c], CellFont, style));
            }
            float total = 0f;
            int shown = 0;
            for (int c = 0; c < 4; c++)
            {
                if (wide[c] <= 0f) continue;
                wide[c] = Mathf.Ceil(wide[c]) + 2f;
                total += wide[c];
                shown++;
            }
            if (shown > 1) total += Gap * (shown - 1);
            _tableW = total + 8f;

            for (int i = 0; i < list.Count; i++)
            {
                AddRow(list[i], wide, tint[i], head && i == 0);
                if (head && i == 0) Rule(_rows);
            }
        }

        internal static string Status(AbstractCharacter ch, ICombatData cd, bool known)
        {
            if (known) return "нет эффектов";
            return Silent(ch.UserId) ? "нет эффектов" : "загружаю…";
        }

        private static void AddRow(string[] cells, float[] wide, Color32 color, bool head)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(_rows, false);
            var bg = go.GetComponent<Image>();
            bg.raycastTarget = false;
            bool stripe = !head && (_rowIndex++ & 1) == 0;
            bg.color = stripe ? new Color(1f, 1f, 1f, 0.05f) : new Color(1f, 1f, 1f, 0f);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(3, 3, 0, 0);
            h.spacing = Gap;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            var style = head ? FontStyle.Bold : FontStyle.Normal;
            for (int c = 0; c < 4; c++)
            {
                if (wide[c] <= 0f) continue;
                Cell(go.transform, cells[c], wide[c], style, color);
            }
        }

        private static void Cell(Transform row, string text, float width, FontStyle style, Color32 color)
        {
            var t = Line(row, CellFont, style, color, TextAnchor.MiddleLeft);
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 13f; le.minHeight = 13f;
        }

        private static float Measure(string text, int size, FontStyle style, bool rich = false)
        {
            if (_measure == null || string.IsNullOrEmpty(text)) return 0f;
            _measure.fontSize = size;
            _measure.fontStyle = style;
            _measure.supportRichText = rich;
            _measure.text = text;
            return _measure.preferredWidth;
        }

        private static string Clip(string text, float max, FontStyle style)
        {
            if (string.IsNullOrEmpty(text) || Measure(text, CellFont, style) <= max) return text;
            string best = "…";
            int low = 1, high = text.Length - 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                string cut = text.Substring(0, mid).TrimEnd() + "…";
                if (Measure(cut, CellFont, style) <= max) { best = cut; low = mid + 1; }
                else high = mid - 1;
            }
            return best;
        }

        private static void Width()
        {
            if (_board == null || _ble == null) return;
            float w = _tableW;
            w = Mathf.Max(w, Measure(_title.text, 12, FontStyle.Bold));
            if (_range.gameObject.activeSelf) w = Mathf.Max(w, Measure(_range.text, 12, FontStyle.Bold));
            if (_meta.gameObject.activeSelf) w = Mathf.Max(w, Measure(_meta.text, 9, FontStyle.Normal));
            if (_branch.gameObject.activeSelf) w = Mathf.Max(w, Measure(_branch.text, 9, FontStyle.Normal));
            if (_barsGo != null && _barsGo.activeSelf)
            {
                float bars = Measure(_hp.text, 9, FontStyle.Bold) + Measure(_mp.text, 9, FontStyle.Bold) + Measure(_sp.text, 9, FontStyle.Bold);
                w = Mathf.Max(w, bars + 3f * 13f + 2f * 8f);
            }
            w = Mathf.Clamp(Mathf.Ceil(w) + 14f, 150f, 520f);
            if (Mathf.Abs(_board.sizeDelta.x - w) < 0.5f) return;
            _ble.preferredWidth = w; _ble.minWidth = w;
            _board.sizeDelta = new Vector2(w, _board.sizeDelta.y);
        }

        internal static string Turns(int n)
        {
            int a = n % 10, b = n % 100;
            if (b >= 11 && b <= 14) return "ходов";
            if (a == 1) return "ход";
            if (a >= 2 && a <= 4) return "хода";
            return "ходов";
        }

        internal static bool Effects(int userId, out List<UserEnchantmentsResponseItem> items)
        {
            bool told = States.TryGetValue(userId, out items);
            if (told && items != null && items.Count > 0) return true;
            List<UserEnchantmentsResponseItem> rough;
            if (Rough.TryGetValue(userId, out rough) && rough != null && rough.Count > 0)
            {
                items = rough;
                return true;
            }
            if (told) return true;
            if (rough != null) { items = rough; return true; }
            return false;
        }

        internal static int Power(UserEnchantmentsResponseItem it)
        {
            int sum = 0;
            if (it.Sources != null) foreach (var s in it.Sources) sum += s.Power;
            return sum;
        }

        internal static string Sources(UserEnchantmentsResponseItem it, ICombatData cd, int self)
        {
            if (it.Sources == null || it.Sources.Count == 0) return "";
            var names = new List<string>();
            int myId = cd.MyCharacter != null ? cd.MyCharacter.UserId : 0;
            foreach (var s in it.Sources)
            {
                string n;
                if (myId > 0 && s.SourceUserId == myId) n = "я";
                else
                {
                    var c = cd.GetCharacter(s.SourceUserId);
                    n = c != null && !string.IsNullOrEmpty(c.Login) ? c.Login : NameOf(s.SourceUserId);
                }
                if (n.Length > 0 && !names.Contains(n)) names.Add(n);
            }
            return string.Join(", ", names.ToArray());
        }

        private static void Build()
        {
            if (_board != null) return;
            var canvasGo = new GameObject("QoLFighterHint", typeof(Canvas), typeof(CanvasScaler));
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 690;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            CanvasScaler sample = null;
            foreach (var one in UnityEngine.Object.FindObjectsOfType<CanvasScaler>())
            {
                if (one == null || one == scaler) continue;
                var owner = one.GetComponent<Canvas>();
                if (owner == null || owner.renderMode == RenderMode.WorldSpace) continue;
                if (sample == null || one.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) sample = one;
                if (one.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) break;
            }
            if (sample != null)
            {
                scaler.uiScaleMode = sample.uiScaleMode;
                scaler.referenceResolution = sample.referenceResolution;
                scaler.screenMatchMode = sample.screenMatchMode;
                scaler.matchWidthOrHeight = sample.matchWidthOrHeight;
                scaler.scaleFactor = sample.scaleFactor;
                UiScale.Own(scaler);
            }
            _host = (RectTransform)canvasGo.transform;

            var boardGo = new GameObject("board", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
            boardGo.transform.SetParent(canvasGo.transform, false);
            _board = (RectTransform)boardGo.transform;
            _board.anchorMin = _board.anchorMax = new Vector2(0f, 0f);
            _board.pivot = new Vector2(0f, 1f);
            _veil = boardGo.GetComponent<CanvasGroup>();
            _veil.blocksRaycasts = false;
            _veil.interactable = false;
            var back = boardGo.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = WardrobeLook.Popup;
            back.raycastTarget = false;
            var ol = boardGo.GetComponent<Outline>();
            ol.effectColor = WardrobeLook.Edge;
            ol.effectDistance = new Vector2(1f, -1f);
            var vlg = boardGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(7, 7, 4, 5);
            vlg.spacing = 1f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = boardGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _ble = boardGo.AddComponent<LayoutElement>();
            _ble.preferredWidth = BoardW; _ble.minWidth = BoardW;
            _board.sizeDelta = new Vector2(BoardW, 100f);

            _title = Line(_board, 12, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleCenter);
            _range = Line(_board, 12, FontStyle.Bold, new Color32(130, 225, 255, 255), TextAnchor.MiddleCenter);
            _meta = Line(_board, 9, FontStyle.Normal, WardrobeLook.Body, TextAnchor.MiddleCenter);
            _branch = Line(_board, 9, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleCenter);
            _branch.text = "";
            _branch.gameObject.SetActive(false);
            Bars(_board);
            Rule(_board);
            var rowsGo = new GameObject("rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            rowsGo.transform.SetParent(_board, false);
            var rl = rowsGo.GetComponent<VerticalLayoutGroup>();
            rl.spacing = 1f;
            rl.childControlWidth = true;
            rl.childControlHeight = true;
            rl.childForceExpandWidth = true;
            rl.childForceExpandHeight = false;
            var rf = rowsGo.GetComponent<ContentSizeFitter>();
            rf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _rows = rowsGo.transform;

            _measure = Line(_board, CellFont, FontStyle.Normal, new Color32(255, 255, 255, 0), TextAnchor.MiddleLeft);
            _measure.horizontalOverflow = HorizontalWrapMode.Overflow;
            _measure.enabled = false;
            var mle = _measure.gameObject.AddComponent<LayoutElement>();
            mle.ignoreLayout = true;

            _tableW = 0f;
            _rowsSig = "";
            _board.gameObject.SetActive(false);
        }

        private static void Bars(Transform host)
        {
            _barsGo = new GameObject("bars", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            _barsGo.transform.SetParent(host, false);
            var row = _barsGo.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            var le = _barsGo.GetComponent<LayoutElement>();
            le.preferredHeight = 14f; le.minHeight = 14f;

            _hp = Pair(_barsGo.transform, Heart(), new Color32(255, 106, 90, 255));
            _mp = Pair(_barsGo.transform, Icon(EActionParamIconType.MANA_COST), new Color32(109, 179, 255, 255));
            _sp = Pair(_barsGo.transform, Icon(EActionParamIconType.STAMINA_COST), new Color32(255, 210, 87, 255));
        }

        private static Text Pair(Transform host, Sprite mark, Color32 tint)
        {
            var pairGo = new GameObject("pair", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            pairGo.transform.SetParent(host, false);
            var row = pairGo.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 2f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var markGo = new GameObject("mark", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            markGo.transform.SetParent(pairGo.transform, false);
            var image = markGo.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.sprite = mark != null ? mark : OnlineWindow.Rounded(8);
            if (mark == null || ReferenceEquals(mark, _heart)) image.color = tint;
            var mle = markGo.GetComponent<LayoutElement>();
            mle.preferredWidth = 11f; mle.minWidth = 11f; mle.preferredHeight = 11f; mle.minHeight = 11f;

            var text = Line(pairGo.transform, 9, FontStyle.Bold, tint, TextAnchor.MiddleLeft);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tle = text.gameObject.AddComponent<LayoutElement>();
            tle.preferredHeight = 12f; tle.minHeight = 12f;
            return text;
        }

        private static Sprite _heart;

        internal static Sprite Heart()
        {
            if (_heart != null && _heart.texture != null) return _heart;
            try
            {
                const int side = 48;
                var tex = new Texture2D(side, side, TextureFormat.RGBA32, false);
                var clear = new Color(1f, 1f, 1f, 0f);
                for (int py = 0; py < side; py++)
                    for (int px = 0; px < side; px++)
                    {
                        float x = (px - side * 0.5f + 0.5f) / (side * 0.42f);
                        float y = (py - side * 0.5f + 0.5f) / (side * 0.42f) + 0.13f;
                        float sum = x * x + y * y - 1f;
                        bool solid = sum * sum * sum - x * x * y * y * y <= 0f;
                        tex.SetPixel(px, py, solid ? Color.white : clear);
                    }
                tex.Apply();
                tex.filterMode = FilterMode.Bilinear;
                _heart = Sprite.Create(tex, new Rect(0f, 0f, side, side), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Plugin.Trace("[боец] сердце: " + e.Message); }
            return _heart;
        }

        private static Sprite Icon(EActionParamIconType type)
        {
            try { return AtlasUtils.GetActionParamIcon(type); }
            catch (Exception e) { Plugin.Trace("[боец] значок: " + e.Message); return null; }
        }

        private static void Rule(Transform host)
        {
            var go = new GameObject("rule", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var img = go.GetComponent<Image>();
            img.color = WardrobeLook.Edge;
            img.raycastTarget = false;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 1f; le.minHeight = 1f; le.flexibleWidth = 1f;
        }

        private static Text Line(Transform host, int size, FontStyle style, Color32 color, TextAnchor align)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(host, false);
            var t = go.GetComponent<Text>();
            t.font = FlaskPicker.Font();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        private static void Fit()
        {
            if (_board == null || _host == null) return;
            float width = _board.rect.width > 0f ? _board.rect.width : BoardW;
            float height = _board.rect.height;
            float hw = _host.rect.width, hh = _host.rect.height;
            var at = _board.anchoredPosition;
            float x = Mathf.Clamp(at.x, 4f, Mathf.Max(4f, hw - width - 4f));
            float y = Mathf.Clamp(at.y, height + 4f, Mathf.Max(height + 4f, hh - 4f));
            if (Mathf.Abs(x - at.x) > 0.5f || Mathf.Abs(y - at.y) > 0.5f) _board.anchoredPosition = new Vector2(x, y);
        }

        private static void Place()
        {
            if (_board == null || _host == null) return;
            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float width = _board.rect.width > 0f ? _board.rect.width : BoardW;
            float height = _board.rect.height;
            var mouse = (Vector2)Input.mousePosition;
            float mx = mouse.x / scale, my = mouse.y / scale;
            float hw = _host.rect.width, hh = _host.rect.height;
            float x = mx - width - 22f;
            float y = my + height * 0.5f;
            if (x < 4f)
            {
                x = Mathf.Clamp(mx - width * 0.5f, 4f, hw - width - 4f);
                y = my + 26f + height;
                if (y > hh - 4f) y = my - 26f;
            }
            if (y > hh - 4f) y = hh - 4f;
            if (y - height < 4f) y = height + 4f;
            _board.anchoredPosition = new Vector2(x, y);
        }
    }
}
