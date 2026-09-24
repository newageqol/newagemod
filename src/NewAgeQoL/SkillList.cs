using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Transport.Messages.Responses.Combat.Buttons;
using Transport.Messages.Responses.Hints;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal sealed class SkillList
    {
        private const float Side = 46f;
        internal const float Gap = 4f;
        private const int Columns = 6;
        private const float TopGap = 104f;
        private const float SideGap = 14f;

        internal enum Kind { Abilities, Tricks, Things, Spells }

        internal static readonly SkillList Abilities = new SkillList(Kind.Abilities);
        internal static readonly SkillList Tricks = new SkillList(Kind.Tricks);
        internal static readonly SkillList Things = new SkillList(Kind.Things);
        internal static readonly SkillList Spells = new SkillList(Kind.Spells);

        private static readonly SkillList[] Lists = { Abilities, Tricks, Things, Spells };
        private static readonly SkillList[] Every = { Abilities, Tricks, Things, Spells };

        private static float _ateAt;

        internal static void Ate()
        {
            _ateAt = Time.unscaledTime;
        }

        internal static bool Eaten()
        {
            if (_ateAt <= 0f) return false;
            bool fresh = Time.unscaledTime - _ateAt <= 1f;
            _ateAt = 0f;
            return fresh;
        }

        internal static void Aims(HashSet<int> into)
        {
            foreach (var one in Every)
            {
                if (one._cellPick != 0) into.Add(one._cellPick);
                if (one._hexPick != 0) into.Add(one._hexPick);
            }
        }

        private const float ThingTop = 110f;
        private const int Cap = 17;

        private readonly bool _tricks;
        private readonly bool _things;
        private readonly bool _spells;
        private readonly string _tag;

        private SkillList(Kind kind)
        {
            _tricks = kind == Kind.Tricks;
            _things = kind == Kind.Things;
            _spells = kind == Kind.Spells;
            _tag = _tricks ? "[приёмы]" : _things ? "[предметы]" : _spells ? "[магия]" : "[умения]";
        }

        private bool Native => _tricks || _things || _spells;

        internal static bool Armed => Abilities._armed != null || Tricks._armed != null || Things._armed != null || Spells._armed != null;

        internal static List<IQuickButton> Of(Kind kind)
        {
            var one = kind == Kind.Tricks ? Tricks : kind == Kind.Things ? Things : kind == Kind.Spells ? Spells : Abilities;
            try { return one.All(); }
            catch { return new List<IQuickButton>(); }
        }

        internal static bool UseSpell(int id)
        {
            var book = Spells;
            if (!SideButtons.InCombat() || Spectate.Peeking) return false;
            if (book.Native && !CombatBar.On) return false;
            var skill = book.Skill(id);
            if (skill == null) return false;
            book.Tap(skill);
            return true;
        }

        internal static bool Use(int id)
        {
            foreach (var one in new[] { Abilities, Tricks, Spells, Things })
            {
                var skill = one.Skill(id);
                if (skill == null) continue;
                one.Tap(skill);
                return true;
            }
            Plugin.Trace("[умения] по клавише не нашёл кнопку " + id);
            return false;
        }

        internal static bool Use(string kind, int id)
        {
            var one = kind == "trick" ? Tricks : kind == "spell" ? Spells : kind == "skill" ? Abilities : null;
            if (one == null) return Use(id);
            var skill = one.Skill(id);
            if (skill == null) { Plugin.Trace("[умения] по клавише нет кнопки " + kind + ":" + id); return false; }
            one.Tap(skill);
            return true;
        }

        internal static int TrickCount => Tricks._panelGo != null && Tricks._panelGo.activeInHierarchy ? Tricks.Live.Count : 0;

        internal static float TrickWidth
        {
            get
            {
                var go = Tricks._panelGo;
                if (go == null || !go.activeInHierarchy) return 0f;
                var rt = go.transform as RectTransform;
                return rt != null ? Mathf.Max(0f, rt.rect.width) : 0f;
            }
        }

        internal static void Tick()
        {
            Abilities.Step();
            Tricks.Step();
            Things.Step();
            Spells.Step();
        }

        internal static void Aim()
        {
            if (Input.GetMouseButtonDown(0)) _ateAt = 0f;
            Abilities.Target();
            Tricks.Target();
            Things.Target();
            Spells.Target();
        }

        internal static bool EscapeClose()
        {
            bool done = Abilities.Cancel();
            if (Tricks.Cancel()) done = true;
            if (Things.Cancel()) done = true;
            return Spells.Cancel() || done;
        }

        private GameObject _canvasGo;
        private GameObject _panelGo;
        private Transform _cells;
        private float _pollAt;
        private float _sourceAt;
        private GameObject _hidden;
        private bool _found;
        private CombatButtonsController _ctrl;
        private IQuickButton _armed;
        private sealed class Cell
        {
            internal Image Icon;
            internal Image Dim;
            internal Outline Edge;
            internal Button Press;
            internal Image Mark;
            internal Image Lit;
            internal Image Cool;
            internal Text Left;
            internal Text Count;
            internal string Block;
        }

        private readonly Dictionary<int, Cell> Live = new Dictionary<int, Cell>();
        private readonly Dictionary<int, float> Used = new Dictionary<int, float>();
        private readonly Dictionary<int, string> Told = new Dictionary<int, string>();
        private readonly HashSet<int> Asked = new HashSet<int>();
        private GameObject _tipGo;
        private Text _tipText;
        private int _tipFor;
        private bool _listening;
        private readonly Vector2 Spot = new Vector2(-600f, -30f);
        private static Texture2D _cursor;
        private int _hexPick;
        private int _hexFor;
        private OffsetCoord _hexAt;
        private bool _hexOk = true;
        private AbstractCharacter _lit;
        private MaterialPropertyBlock _paint;
        private Renderer _painted;
        private GameObject _stopGo;
        private GameObject _upGo, _downGo;
        private Image _upPic, _downPic;
        private int _page;
        private int _total;

        private void Step()
        {
            try
            {
                if (Native && !CombatBar.On) { Off(); return; }
                if (!SideButtons.InCombat() || Spectate.Peeking) { Off(); return; }
                if (_things) Roll();
                if (Time.unscaledTime < _pollAt) return;
                _pollAt = Time.unscaledTime + 0.06f;
                Source();
                if (!_found) { Close(); return; }
                Veil(_hidden, true);
                if (_canvasGo == null) Build();
                Refresh();
                Stop();
                Place();
            }
            catch (Exception e) { Plugin.Trace(_tag + " " + e.Message); }
        }

        private static int _whoFrame = -1;
        private static int _whoId;
        private static ICombatData _whoFight;

        private static int Whom(ICombatData cd)
        {
            if (_whoFrame == Time.frameCount && ReferenceEquals(cd, _whoFight)) return _whoId;
            _whoFrame = Time.frameCount;
            _whoFight = cd;
            _whoId = FighterHint.Under(cd, false);
            return _whoId;
        }

        private void Target()
        {
            try
            {
                if (_armed == null || (Native && !CombatBar.On) || !SideButtons.InCombat() || Spectate.Peeking) { if (_armed != null) Disarm(); return; }
                if (Input.GetMouseButtonDown(1)) { Disarm(); return; }
                if (!Soft(Why(_armed))) { Disarm(); return; }

                var cd = FighterHint.Cd();
                if (cd == null) return;
                bool over = Unity3DHelper.IsOverInterface();
                if (TargetTypeExtension.IsActionHasCellTarget(_armed)) { Ground(cd, over); return; }
                int id = over ? 0 : Whom(cd);
                var target = id != 0 ? cd.GetCharacter(id) : null;
                Frame(target);
                Pulse();

                if (!Input.GetMouseButtonDown(0) || target == null) return;
                Ate();

                var skill = _armed;
                cd.SelectedCharacter = target;
                if (!Chase(skill))
                {
                    var verdict = Check(skill);
                    if (Broke(verdict)) { Disarm(); if (!Topup(skill, verdict)) Hand(skill); return; }
                    if (verdict != EQuickButtonValidationResult.Success)
                    {
                        Refuse(skill, verdict);
                        return;
                    }
                }
                Disarm();
                Fire(skill);
            }
            catch (Exception e) { Plugin.Trace(_tag + " цель: " + e.Message); }
        }

        private static readonly Color Fits = new Color(0.2f, 1.3f, 0.25f, 1f);
        private static readonly Color Wrong = new Color(1.3f, 0.15f, 0.1f, 1f);

        private int _cellPick;
        private bool _cellOk;
        private bool _cellSet;
        private OffsetCoord _cell;
        private AbstractCharacter _first;
        private static CameraControl _rig;

        private static bool Wants(IQuickButton skill)
        {
            var kind = skill.Target;
            return kind == ETargetType.TARGET_ANY_PLAYER_CELL || kind == ETargetType.TARGET_ENEMY_CELL
                || kind == ETargetType.TARGET_ALLY_CELL;
        }

        private static HexGridControl _pad;
        private static int _padFrame = -1;
        private static bool _padOk;
        private static OffsetCoord _padHex;
        private static bool _padSaid;
        private static float _padAt;
        private static float _rigAt;
        private static System.Reflection.PropertyInfo _gridProp;
        private static bool _gridAsked;
        private static readonly RaycastHit[] Beam = new RaycastHit[64];

        private static HexGridControl Pad()
        {
            if (_pad != null) return _pad;
            if (Time.unscaledTime < _padAt) return null;
            _padAt = Time.unscaledTime + 0.25f;
            try { _pad = UnityEngine.Object.FindObjectOfType<HexGridControl>(); }
            catch (Exception e) { Plugin.Trace("[прицел] поле боя: " + e.Message); }
            return _pad;
        }

        private static Transform Rig()
        {
            if (_rig == null)
            {
                if (Time.unscaledTime < _rigAt) return null;
                _rigAt = Time.unscaledTime + 0.25f;
                try { _rig = UnityEngine.Object.FindObjectOfType<CameraControl>(); }
                catch (Exception e) { Plugin.Trace("[прицел] камера: " + e.Message); }
                if (_rig == null) return null;
            }
            if (!_gridAsked)
            {
                _gridAsked = true;
                _gridProp = AccessTools.Property(typeof(BaseUserInput), "gridTransform");
                if (_gridProp == null) Plugin.Trace("[прицел] у игры нет gridTransform, клетку считаю только по полю боя");
            }
            if (_gridProp == null) return null;
            try { return _gridProp.GetValue(_rig) as Transform; }
            catch (Exception e) { Plugin.Trace("[прицел] сетка боя: " + e.Message); return null; }
        }

        internal static bool HexUnder(out OffsetCoord hex)
        {
            if (_padFrame == Time.frameCount) { hex = _padHex; return _padOk; }
            _padFrame = Time.frameCount;
            _padOk = Look(out _padHex);
            hex = _padHex;
            return _padOk;
        }

        private static bool Look(out OffsetCoord hex)
        {
            hex = default(OffsetCoord);
            try
            {
                Transform grid = null;
                Camera cam = null;
                var pad = Pad();
                if (pad != null) { grid = pad.transform; cam = pad.gridCamera; }
                if (grid == null) grid = Rig();
                if (cam == null) cam = Camera.main;
                if (grid == null || cam == null) return false;

                var ray = cam.ScreenPointToRay(Input.mousePosition);
                if (pad != null)
                {
                    int found = Physics.RaycastNonAlloc(ray, Beam, 1000f);
                    float near = float.MaxValue;
                    var spot = Vector3.zero;
                    bool got = false;
                    for (int i = 0; i < found; i++)
                    {
                        var hit = Beam[i];
                        if (hit.collider == null || hit.collider.gameObject != pad.gameObject || hit.distance >= near) continue;
                        near = hit.distance;
                        spot = hit.point;
                        got = true;
                    }
                    if (got)
                    {
                        hex = HexUtils.pixelToOffset(grid.InverseTransformPoint(spot));
                        return true;
                    }
                    if (!_padSaid)
                    {
                        _padSaid = true;
                        Plugin.Trace("[прицел] луч не задел поле боя, считаю клетку по плоскости");
                    }
                }

                var floor = new Plane(grid.up, grid.position);
                float along;
                if (!floor.Raycast(ray, out along)) return false;
                hex = HexUtils.pixelToOffset(grid.InverseTransformPoint(ray.GetPoint(along)));
                return true;
            }
            catch (Exception e) { Plugin.Trace("[прицел] клетка под мышью: " + e.Message); return false; }
        }

        private void ClearCell()
        {
            if (_cellPick != 0)
            {
                try { var cd = FighterHint.Cd(); if (cd != null) cd.ClearSelection(_cellPick); }
                catch (Exception e) { Plugin.Trace(_tag + " снять подсветку клетки: " + e.Message); }
                _cellPick = 0;
            }
            _cellSet = false;
        }

        private void Ground(ICombatData cd, bool over)
        {
            var skill = _armed;
            bool needChar = Wants(skill) && _first == null;
            if (needChar)
            {
                ClearCell();
                int id = over ? 0 : Whom(cd);
                var target = id != 0 ? cd.GetCharacter(id) : null;
                Frame(target);
                Pulse();
                if (!Input.GetMouseButtonDown(0) || target == null) return;
                Ate();
                cd.SelectedCharacter = target;
                _first = target;
                Plugin.Trace(_tag + " выбран боец " + target.Login + ", теперь клетка");
                return;
            }

            OffsetCoord hex;
            if (over || !HexUnder(out hex)) { ClearCell(); FlashLook.Hint(0); return; }
            bool fits = Check(skill, hex) == EQuickButtonValidationResult.Success;
            FlashLook.Hint(fits ? 1 : 2);
            if (!_cellSet || !hex.Equals(_cell) || fits != _cellOk)
            {
                ClearCell();
                _cell = hex;
                _cellOk = fits;
                _cellSet = true;
                var paint = fits ? Fits : Wrong;
                try { _cellPick = cd.SetSelection(hex, 0, paint, true, true); }
                catch (Exception e) { Plugin.Trace("[прицел] подсветка клетки: " + e.Message); }
            }
            if (!Input.GetMouseButtonDown(0)) return;
            Ate();
            if (!fits) { Refuse(skill, Check(skill, hex)); return; }
            Disarm();
            FireAt(skill, hex);
        }

        private void FireAt(IQuickButton skill, OffsetCoord hex)
        {
            try
            {
                Used[skill.Id] = Time.unscaledTime;
                _pollAt = 0f;
                Repaint();
                var ctrl = _ctrl ?? Controllers.Get<CombatButtonsController>();
                var confirm = AccessTools.Method(typeof(CombatButtonsController), "OnActionConfirmed");
                if (ctrl == null || confirm == null) return;
                confirm.Invoke(ctrl, new object[] { skill, hex });
                Plugin.Trace(_tag + " применяю " + skill.Id + " на клетку");
            }
            catch (Exception e) { Plugin.Warn(_tag + " применение на клетку " + skill.Id + ": " + e.Message); }
        }

        private void Frame(AbstractCharacter target)
        {
            var cd = FighterHint.Cd();
            if (cd == null || target == null) { ClearHex(); FlashLook.Hint(0); return; }
            bool fits = Suits(_armed, target) && Why(_armed) == null;
            FlashLook.Hint(fits ? 1 : 2);
            var spot = target.HexGridPosition;
            if (_hexFor == target.UserId && _hexPick != 0 && _hexOk == fits && _hexAt.Equals(spot)) return;

            ClearHex();
            _hexFor = target.UserId;
            _hexAt = spot;
            _hexOk = fits;
            var paint = _hexOk ? Fits : Wrong;
            try { _hexPick = cd.SetSelection(spot, 0, paint, true, true); }
            catch (Exception e) { Plugin.Trace(_tag + " клетка цели: " + e.Message); }
            Glow(target);
        }

        private bool Suits(IQuickButton skill, AbstractCharacter target)
        {
            if (skill == null || target == null) return false;
            var cd = FighterHint.Cd();
            var me = cd != null ? cd.MyCharacter : null;
            if (me == null) return false;
            var need = skill.TargetCondition;
            if (need == ETargetCondition.CONDITION_ALIVE && target.Dead) return false;
            if (need == ETargetCondition.CONDITION_DEAD && !target.Dead) return false;
            bool mate = target.Team == me.Team;
            switch (skill.Target)
            {
                case ETargetType.TARGET_ENEMY:
                case ETargetType.TARGET_ENEMY_CELL:
                    return !mate;
                case ETargetType.TARGET_ALLY:
                case ETargetType.TARGET_ALLY_CELL:
                    return mate;
                case ETargetType.TARGET_ALLY_EXCEPT_SOURCE:
                    return mate && target.UserId != me.UserId;
                default:
                    return true;
            }
        }

        private void Glow(AbstractCharacter target)
        {
            if (ReferenceEquals(_lit, target)) return;
            Unglow();
            _lit = target;
        }

        private void Pulse()
        {
            if (_lit == null) return;
            try
            {
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                Tint(wave);
            }
            catch (Exception e) { Plugin.Trace(_tag + " подсветка модели: " + e.Message); }
        }

        private void Tint(float wave)
        {
            try
            {
                var holder = _lit.CharacterMeshRendererHolder;
                var skin = holder != null ? holder.MainRenderer : null;
                if (skin == null) return;
                if (_paint == null) _paint = new MaterialPropertyBlock();
                skin.GetPropertyBlock(_paint);
                var glow = new Color(0.25f + 0.2f * wave, 1f, 0.3f + 0.2f * wave, 1f);
                _paint.SetColor("_Color", glow);
                _paint.SetColor("_BaseColor", glow);
                _paint.SetColor("_TintColor", glow);
                _paint.SetColor("_EmissionColor", new Color(0.04f, 0.16f + 0.18f * wave, 0.06f, 1f));
                _paint.SetColor("_RimColor", glow);
                _paint.SetColor("_OutlineColor", glow);
                skin.SetPropertyBlock(_paint);
                _painted = skin;
            }
            catch (Exception e) { Plugin.Trace(_tag + " окраска модели: " + e.Message); }
        }

        private void Unglow()
        {
            if (_painted != null)
            {
                try { _painted.SetPropertyBlock(null); }
                catch (Exception e) { Plugin.Trace(_tag + " вернуть материал бойца: " + e.Message); }
                _painted = null;
            }
            _lit = null;
        }

        private static System.Reflection.MethodInfo _judgeCall;
        private static bool _judgeAsked;

        private EQuickButtonValidationResult Check(IQuickButton skill)
        {
            return Check(skill, null);
        }

        private EQuickButtonValidationResult Check(IQuickButton skill, OffsetCoord hex)
        {
            if (skill == null) return EQuickButtonValidationResult.Success;
            try
            {
                var data = Controllers.User;
                if (data == null) return EQuickButtonValidationResult.Success;
                if (_spells)
                {
                    var book = Book();
                    if (book == null) return EQuickButtonValidationResult.Success;
                    if (!_judgeAsked)
                    {
                        _judgeAsked = true;
                        _judgeCall = AccessTools.Method(typeof(Spellbook), "GetValidator");
                        if (_judgeCall == null) Plugin.Trace(_tag + " у книги нет GetValidator, проверку цели делает игра");
                    }
                    var spellJudge = _judgeCall != null ? _judgeCall.Invoke(book, null) as QuickButtonUsageValidator : null;
                    return spellJudge == null ? EQuickButtonValidationResult.Success : spellJudge.Validate(data, skill, hex);
                }
                var holder = Holder();
                var judge = holder != null ? holder.Validator : null;
                if (judge == null) return EQuickButtonValidationResult.Success;
                return judge.Validate(data, skill, hex);
            }
            catch (Exception e)
            {
                Plugin.Trace(_tag + " проверка цели: " + e.Message);
                return EQuickButtonValidationResult.Success;
            }
        }

        private void Refuse(IQuickButton skill, EQuickButtonValidationResult verdict)
        {
            try
            {
                string key = QuickButtonValidationResultExtension.GetValidationMessage(skill, verdict);
                if (string.IsNullOrEmpty(key)) key = "combat.gui.combatbutton.disablecause.target_not_selected";
                AirMessageScript.ShowErrorNotification(key);
                Plugin.Trace(_tag + " цель не годится: " + verdict);
            }
            catch (Exception e) { Plugin.Trace(_tag + " отказ по цели: " + e.Message); }
        }

        private void ClearHex()
        {
            Unglow();
            if (_hexPick == 0) { _hexFor = 0; return; }
            try
            {
                var cd = FighterHint.Cd();
                if (cd != null) cd.ClearSelection(_hexPick);
            }
            catch (Exception e) { Plugin.Trace(_tag + " снять клетку: " + e.Message); }
            _hexPick = 0;
            _hexFor = 0;
        }

        private void Point(bool on)
        {
            try
            {
                if (!on) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
                if (_cursor == null) _cursor = Crosshair();
                if (_cursor != null) Cursor.SetCursor(_cursor, new Vector2(15f, 15f), CursorMode.Auto);
            }
            catch (Exception e) { Plugin.Trace(_tag + " курсор: " + e.Message); }
        }

        private Texture2D Crosshair()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            var gold = WardrobeLook.Accent;
            var dark = new Color(WardrobeLook.Field.r, WardrobeLook.Field.g, WardrobeLook.Field.b, 0.95f);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = x - 15.5f, dy = y - 15.5f;
                    float far = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = far > 8.5f && far < 11.5f;
                    bool edge = far > 7.5f && far <= 8.5f || far >= 11.5f && far < 12.5f;
                    bool cross = far < 6f && (Mathf.Abs(dx) < 1.2f || Mathf.Abs(dy) < 1.2f);
                    tex.SetPixel(x, y, ring || cross ? gold : edge ? dark : clear);
                }
            tex.Apply();
            return tex;
        }

        private void Disarm()
        {
            _armed = null;
            _first = null;
            Point(false);
            ClearHex();
            ClearCell();
            if (_stopGo != null) _stopGo.SetActive(false);
            foreach (var pair in Live)
                if (pair.Value.Mark != null) pair.Value.Mark.enabled = false;
        }

        private void Arm(IQuickButton skill)
        {
            foreach (var other in Lists)
                if (!ReferenceEquals(other, this) && other._armed != null) other.Disarm();
            _armed = skill;
            Point(true);
            foreach (var pair in Live)
                if (pair.Value.Mark != null) pair.Value.Mark.enabled = pair.Key == skill.Id;
        }

        private void Fire(IQuickButton skill)
        {
            try
            {
                Used[skill.Id] = Time.unscaledTime;
                _pollAt = 0f;
                Repaint();
                var ctrl = _ctrl ?? Controllers.Get<CombatButtonsController>();
                if (ctrl == null) return;
                var confirm = AccessTools.Method(typeof(CombatButtonsController), "OnActionConfirmed");
                if (confirm == null) return;
                confirm.Invoke(ctrl, new object[] { skill, null });
                Plugin.Trace(_tag + " применяю " + skill.Id);
            }
            catch (Exception e) { Plugin.Warn(_tag + " применение " + skill.Id + ": " + e.Message); }
        }

        private IQuickButton Skill(int id)
        {
            try
            {
                if (_spells) { var book = Book(); return book == null ? null : book.GetButton(id) as IQuickButton; }
                var holder = Holder();
                return holder == null ? null : holder.GetButton(id);
            }
            catch (Exception e) { Plugin.Trace(_tag + " умение " + id + ": " + e.Message); return null; }
        }

        private const float Narrowest = 22f;

        private float Squeeze()
        {
            int count = Mathf.Max(1, Live.Count);
            float room = (CombatBar.SkillsWide + Gap) / count - Gap;
            return Mathf.Clamp(Mathf.Floor(room * 4f) / 4f, Narrowest, CombatBar.SmallSide);
        }

        private void Row()
        {
            var grid = _panelGo != null ? _panelGo.GetComponent<GridLayoutGroup>() : null;
            if (grid == null) return;
            bool strip = _tricks;
            float side = strip ? Squeeze() : Side;
            var cell = new Vector2(side, side);
            if (grid.cellSize != cell) grid.cellSize = cell;
            int fit = strip ? Mathf.Max(1, Mathf.FloorToInt((CombatBar.SkillsWide + Gap) / (side + Gap) + 0.01f)) : _things ? 1 : _spells ? Mathf.Clamp(Live.Count, 1, 8) : Columns;
            var corner = strip ? GridLayoutGroup.Corner.LowerLeft : GridLayoutGroup.Corner.UpperLeft;
            if (grid.constraintCount != fit) grid.constraintCount = fit;
            if (grid.startCorner != corner) grid.startCorner = corner;
        }

        private void Place()
        {
            if (_panelGo == null) return;
            Row();
            var prt = (RectTransform)_panelGo.transform;
            if (_tricks) { Strip(prt); return; }
            if (_things) { Column(prt); return; }
            if (_spells) { Crown(prt); return; }
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            var spot = new Vector2(280f, -8f);
            if (prt.anchoredPosition != spot) prt.anchoredPosition = spot;

            if (_stopGo == null) return;
            var srt = (RectTransform)_stopGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(spot.x, spot.y - prt.rect.height - 4f);
        }

        private Spellbook Book()
        {
            try { return Controllers.User?.Spellbook; }
            catch { return null; }
        }

        private static readonly Dictionary<Type, System.Reflection.FieldInfo> _schoolField = new Dictionary<Type, System.Reflection.FieldInfo>();
        private static readonly Dictionary<Type, System.Reflection.PropertyInfo> _schoolProp = new Dictionary<Type, System.Reflection.PropertyInfo>();
        private static readonly HashSet<Type> _schoolSaid = new HashSet<Type>();

        private static int School(IQuickButton spell)
        {
            if (spell == null) return 0;
            var type = spell.GetType();
            try
            {
                System.Reflection.FieldInfo field;
                System.Reflection.PropertyInfo prop;
                if (!_schoolField.TryGetValue(type, out field))
                {
                    field = AccessTools.Field(type, "schoolMagic") ?? AccessTools.Field(type, "_schoolMagic");
                    _schoolField[type] = field;
                    _schoolProp[type] = field == null ? (AccessTools.Property(type, "SchoolMagic") ?? AccessTools.Property(type, "School")) : null;
                }
                if (field != null) return Convert.ToInt32(field.GetValue(spell));
                if (_schoolProp.TryGetValue(type, out prop) && prop != null) return Convert.ToInt32(prop.GetValue(spell, null));
                if (_schoolSaid.Add(type)) Plugin.Trace("[магия] у " + type.Name + " школы не видно, книга ляжет по номерам");
            }
            catch (Exception e)
            {
                if (_schoolSaid.Add(type)) Plugin.Trace("[магия] школа у " + type.Name + " не читается: " + e.Message);
            }
            return 0;
        }

        private readonly Dictionary<int, int> Schooled = new Dictionary<int, int>();
        private Comparison<IQuickButton> _order;

        private int Grade(IQuickButton spell)
        {
            if (spell == null) return 0;
            int id = spell.Id;
            int school;
            if (Schooled.TryGetValue(id, out school)) return school;
            school = School(spell);
            Schooled[id] = school;
            return school;
        }

        private int Order(IQuickButton a, IQuickButton b)
        {
            int bySchool = Grade(a).CompareTo(Grade(b));
            return bySchool != 0 ? bySchool : a.Id.CompareTo(b.Id);
        }

        private void Crown(RectTransform prt)
        {
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            var spot = new Vector2(0f, -8f);
            if (prt.anchoredPosition != spot) prt.anchoredPosition = spot;
            if (_stopGo != null && _stopGo.activeSelf) _stopGo.SetActive(false);
        }

        private void Column(RectTransform prt)
        {
            prt.anchorMin = prt.anchorMax = new Vector2(1f, 0.5f);
            prt.pivot = new Vector2(1f, 0.5f);
            var spot = new Vector2(-SideGap, 0f);
            if (prt.anchoredPosition != spot) prt.anchoredPosition = spot;
            Wheel(prt);

            if (_stopGo == null) return;
            var srt = (RectTransform)_stopGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(1f, 0.5f);
            srt.anchoredPosition = new Vector2(spot.x - prt.rect.width - 6f, spot.y);
        }

        private void Strip(RectTransform prt)
        {
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0f);
            prt.pivot = new Vector2(0f, 0f);
            var grid = _panelGo.GetComponent<GridLayoutGroup>();
            float lift = grid != null ? Mathf.Max(0f, (CombatBar.SmallSide - grid.cellSize.y) * 0.5f) : 0f;
            var spot = new Vector2(CombatBar.SkillsX, CombatBar.SkillsY + lift);
            if (prt.anchoredPosition != spot) prt.anchoredPosition = spot;

            if (_stopGo == null) return;
            var srt = (RectTransform)_stopGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 0f);
            srt.pivot = new Vector2(0f, 0f);
            srt.anchoredPosition = new Vector2(spot.x, spot.y + prt.rect.height + 6f);
        }

        private void Wheel(RectTransform prt)
        {
            bool need = _total > Cap;
            if (!need)
            {
                if (_upGo != null && _upGo.activeSelf) _upGo.SetActive(false);
                if (_downGo != null && _downGo.activeSelf) _downGo.SetActive(false);
                return;
            }
            if (_upGo == null) _upGo = Arrow(true);
            if (_downGo == null) _downGo = Arrow(false);
            if (_upGo == null || _downGo == null) return;
            if (!_upGo.activeSelf) _upGo.SetActive(true);
            if (!_downGo.activeSelf) _downGo.SetActive(true);

            float half = prt.rect.height * 0.5f;
            var up = (RectTransform)_upGo.transform;
            up.anchorMin = up.anchorMax = new Vector2(1f, 0.5f);
            up.pivot = new Vector2(1f, 0f);
            up.anchoredPosition = new Vector2(-SideGap, half + 4f);
            var down = (RectTransform)_downGo.transform;
            down.anchorMin = down.anchorMax = new Vector2(1f, 0.5f);
            down.pivot = new Vector2(1f, 1f);
            down.anchoredPosition = new Vector2(-SideGap, -half - 4f);

            var dim = new Color(WardrobeLook.Bright.r, WardrobeLook.Bright.g, WardrobeLook.Bright.b, 0.3f);
            if (_upPic != null) _upPic.color = _page > 0 ? WardrobeLook.Bright : dim;
            if (_downPic != null) _downPic.color = _page < _total - Cap ? WardrobeLook.Bright : dim;
        }

        private GameObject Arrow(bool up)
        {
            if (_canvasGo == null) return null;
            var go = new GameObject(up ? "up" : "down", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(_canvasGo.transform, false);
            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Button;
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;
            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            ((RectTransform)go.transform).sizeDelta = new Vector2(Side, 20f);

            var tip = new GameObject("mark", typeof(RectTransform), typeof(Image));
            tip.transform.SetParent(go.transform, false);
            var pic = tip.GetComponent<Image>();
            pic.sprite = Icons.Downward();
            pic.color = WardrobeLook.Bright;
            pic.raycastTarget = false;
            pic.preserveAspect = true;
            var trt = (RectTransform)tip.transform;
            OnlineWindow.Place(trt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(6f, 3f), new Vector2(-6f, -3f));
            if (up) trt.localRotation = Quaternion.Euler(0f, 0f, 180f);
            if (up) _upPic = pic; else _downPic = pic;

            var press = go.GetComponent<Button>();
            press.targetGraphic = back;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            press.colors = colors;
            press.onClick.AddListener(() => Scroll(up ? -1 : 1));
            return go;
        }

        private void Roll()
        {
            if (_panelGo == null || _total <= Cap) return;
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!Under()) return;
            Scroll(wheel > 0f ? -1 : 1);
        }

        internal bool Under()
        {
            if (!_things || _panelGo == null || !_panelGo.activeInHierarchy) return false;
            return Hit(_panelGo) || Hit(_upGo) || Hit(_downGo);
        }

        private static bool Hit(GameObject go)
        {
            return go != null && go.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)go.transform, Input.mousePosition, null);
        }

        private void Scroll(int by)
        {
            int was = _page;
            _page = Mathf.Clamp(_page + by, 0, Mathf.Max(0, _total - Cap));
            if (_page == was) return;
            Sig.Clear();
            _pollAt = 0f;
            Plugin.Trace(_tag + " прокрутка: с " + (_page + 1) + " из " + _total);
        }

        private void Stop()
        {
            if (_canvasGo == null) return;
            if (_stopGo == null)
            {
                _stopGo = new GameObject("cancel", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
                _stopGo.transform.SetParent(_canvasGo.transform, false);
                var back = _stopGo.GetComponent<Image>();
                back.color = WardrobeLook.Danger;
                back.sprite = OnlineWindow.Rounded(6);
                back.type = Image.Type.Sliced;
                var edge = _stopGo.GetComponent<Outline>();
                edge.effectColor = WardrobeLook.Edge;
                edge.effectDistance = new Vector2(2f, -2f);
                var srt = (RectTransform)_stopGo.transform;
                srt.sizeDelta = new Vector2(Side, Side);
                var text = OnlineWindow.Label(_stopGo.transform, "✕", 30, FontStyle.Bold, WardrobeLook.DangerText);
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;
                OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                var stop = _stopGo.GetComponent<Button>();
                stop.targetGraphic = back;
                var colors = stop.colors;
                colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
                stop.colors = colors;
                stop.onClick.AddListener(Disarm);
            }
            bool want = _armed != null;
            if (_stopGo.activeSelf != want) _stopGo.SetActive(want);
        }

        private bool Cancel()
        {
            if (_armed == null) return false;
            Disarm();
            return true;
        }

        internal void Off()
        {
            if (_armed != null) Disarm();
            Veil(_hidden, false);
            _hidden = null;
            _found = false;
            _ctrl = null;
            _armed = null;
            _sourceAt = 0f;
            Close();
        }

        private void Source()
        {
            if (_found && Time.unscaledTime < _sourceAt) return;
            _sourceAt = Time.unscaledTime + 2f;
            try
            {
                if (_spells ? Book() == null : Holder() == null) { _found = false; return; }
                _found = true;
                if (_hidden != null) return;
                _ctrl = null;
                var button = FromController() ?? FromContainer();
                if (button == null) { Plugin.Trace(_tag + " кнопка умений не найдена"); return; }
                _hidden = button.gameObject;
                Plugin.Trace(_tag + " кнопка спрятана: " + button.name);
            }
            catch (Exception e) { Plugin.Trace(_tag + " источник: " + e.Message); }
        }

        private QuickButtonStateHolder Holder()
        {
            try
            {
                var data = Controllers.User;
                var manager = data?.CombatData?.ButtonManager;
                if (manager == null) return null;
                return _tricks ? manager.Dodges : _things ? manager.QuickSlots : manager.Skills;
            }
            catch { return null; }
        }

        private static readonly RoundType[] Rounds = { RoundType.COMBAT_ROUND, RoundType.WALK_ROUND };

        private readonly List<IQuickButton> Bag = new List<IQuickButton>();
        private readonly List<IQuickButton> Sheet = new List<IQuickButton>();
        private readonly HashSet<int> Seen = new HashSet<int>();
        private readonly List<int> Sig = new List<int>();

        private List<IQuickButton> All()
        {
            return All(new List<IQuickButton>());
        }

        private List<IQuickButton> All(List<IQuickButton> into)
        {
            into.Clear();
            Seen.Clear();
            if (_spells)
            {
                var book = Book();
                if (book == null) return into;
                foreach (var round in Rounds)
                {
                    var part = book.GetButtonsByRoundType(round);
                    if (part == null) continue;
                    foreach (var one in part)
                        if (one != null && Seen.Add(one.Id)) into.Add(one);
                }
                if (_order == null) _order = Order;
                into.Sort(_order);
                return into;
            }
            var holder = Holder();
            if (holder == null) return into;
            foreach (var round in Rounds)
            {
                var part = holder.GetButtonsByRoundType(round);
                if (part == null) continue;
                foreach (var one in part)
                    if (one != null && Seen.Add(one.Id)) into.Add(one);
            }
            return into;
        }

        private string Key => _tricks ? "DodgesButton" : _things ? "UsedThingsButton" : _spells ? "SpellsButton" : "MasteriesButton";

        private Component FromController()
        {
            try
            {
                var ctrl = Controllers.Get<CombatButtonsController>();
                if (ctrl == null) return null;
                _ctrl = ctrl;
                return AccessTools.Property(typeof(CombatButtonsController), Key)?.GetValue(ctrl) as Component;
            }
            catch (Exception e) { Plugin.Trace(_tag + " контроллер боя: " + e.Message); return null; }
        }

        private Component FromContainer()
        {
            try
            {
                var box = DependencyContainer.GetContainer();
                if (box == null) return null;
                if (_spells) return box.Resolve<ComplexSectorButtonSelector>(Key);
                return box.Resolve<SimpleSectorButtonSelector>(Key);
            }
            catch (Exception e) { Plugin.Trace(_tag + " кнопка " + Key + " из контейнера: " + e.Message); return null; }
        }

        internal static void Veil(GameObject go, bool hide)
        {
            if (go == null) return;
            var veil = go.GetComponent<CanvasGroup>();
            if (veil != null) { veil.alpha = 1f; veil.blocksRaycasts = true; veil.interactable = true; }
            if (go.activeSelf == !hide) return;
            go.SetActive(!hide);
            var parent = go.transform.parent as RectTransform;
            if (parent != null) LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        private void Close()
        {
            _tipGo = null;
            _tipText = null;
            _tipFor = 0;
            _stopGo = null;
            _upGo = null;
            _downGo = null;
            _upPic = null;
            _downPic = null;
            Live.Clear();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _panelGo = null;
            _cells = null;
            Sig.Clear();
            Schooled.Clear();
        }

        private void Build()
        {
            Close();
            var go = new GameObject(_tricks ? "QoLTrickRow" : _things ? "QoLThingColumn" : _spells ? "QoLSpellRow" : "QoLSkillList", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 280;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);

            _panelGo = new GameObject("skills", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            _panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-SideGap, -TopGap);

            var grid = _panelGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(Side, Side);
            grid.spacing = new Vector2(Gap, Gap);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;

            var fit = _panelGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _cells = _panelGo.transform;
        }

        private void Repaint()
        {
            try { if (_cells != null) Paint(Shown()); }
            catch (Exception e) { Plugin.Trace(_tag + " перерисовка: " + e.Message); }
        }

        private List<IQuickButton> Shown()
        {
            var all = All(Bag);
            if (_things)
            {
                _total = all.Count;
                if (_total <= Cap) { _page = 0; return all; }
                _page = Mathf.Clamp(_page, 0, _total - Cap);
                Sheet.Clear();
                for (int i = 0; i < Cap; i++) Sheet.Add(all[_page + i]);
                return Sheet;
            }
            if (!_tricks) return all;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var one = all[i];
                if (one == null) continue;
                if (one.Id == DodgesButtonStateHolder.HantingOnButtonId || one.Id == DodgesButtonStateHolder.HantingOffButtonId)
                    all.RemoveAt(i);
            }
            for (int i = 0; i < all.Count; i++)
            {
                if (!Summon(all[i])) continue;
                var call = all[i];
                all.RemoveAt(i);
                all.Insert(0, call);
                break;
            }
            return all;
        }

        private void Refresh()
        {
            if (_cells == null) return;
            var all = Shown();
            if (!Same(all))
            {
                Sig.Clear();
                for (int i = 0; i < all.Count; i++) Sig.Add(all[i].Id);
                Rebuild(all);
            }
            Paint(all);
        }

        private bool Same(List<IQuickButton> all)
        {
            if (Sig.Count != all.Count) return false;
            for (int i = 0; i < Sig.Count; i++)
                if (Sig[i] != all[i].Id) return false;
            return true;
        }

        private void Rebuild(List<IQuickButton> all)
        {
            HideTip();
            for (int i = _cells.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_cells.GetChild(i).gameObject);
            Live.Clear();
            foreach (var one in all) Slot(one);
        }

        private void Paint(List<IQuickButton> all)
        {
            foreach (var skill in all)
            {
                Cell cell;
                if (!Live.TryGetValue(skill.Id, out cell)) continue;
                string why = Why(skill);
                bool ready = why == null;
                bool poor = Native && why == Poor;
                bool bright = Soft(why);
                bool cooling = Cooling(skill);
                int rounds = skill.Recharge - skill.Turn;
                cell.Block = why;
                if (cell.Icon != null) cell.Icon.color = bright ? Color.white : cooling ? new Color(1f, 1f, 1f, 0.75f) : new Color(0.5f, 0.5f, 0.5f, 0.4f);
                if (cell.Dim != null) cell.Dim.enabled = !bright && !cooling;
                if (cell.Cool != null)
                {
                    cell.Cool.enabled = cooling;
                    if (cooling) cell.Cool.fillAmount = Mathf.Clamp01(1f - (float)skill.Turn / skill.Recharge);
                }
                if (cell.Left != null)
                {
                    bool show = cooling && rounds > 0 && rounds < 100 && !Summon(skill);
                    cell.Left.enabled = show;
                    if (show) cell.Left.text = Num(rounds);
                }
                if (cell.Count != null)
                {
                    bool show = skill.Count > 0 && (skill.Count < 100 || _things);
                    cell.Count.enabled = show;
                    if (show) cell.Count.text = Num(skill.Count);
                }
                if (cell.Edge != null) cell.Edge.effectColor = bright ? WardrobeLook.FieldEdge : new Color(WardrobeLook.Edge.r, WardrobeLook.Edge.g, WardrobeLook.Edge.b, 0.7f);
                if (cell.Lit != null)
                {
                    bool lit = Focus.Lit(skill);
                    if (cell.Lit.gameObject.activeSelf != lit) cell.Lit.gameObject.SetActive(lit);
                }
                if (cell.Press != null) cell.Press.interactable = ready || poor;
                if (!bright && _armed != null && _armed.Id == skill.Id) Disarm();
            }
        }

        private static readonly string[] Digits = Numbers();

        private static string[] Numbers()
        {
            var made = new string[100];
            for (int i = 0; i < made.Length; i++) made[i] = i.ToString();
            return made;
        }

        private static string Num(int value)
        {
            return value >= 0 && value < Digits.Length ? Digits[value] : value.ToString();
        }

        private bool Cooling(IQuickButton skill)
        {
            return !skill.CanActivate && skill.Recharge > 0 && skill.Turn < skill.Recharge;
        }

        private static int _overRound = -1;
        private static ICombatData _overFight;

        internal static void PhaseOver()
        {
            try
            {
                var cd = FighterHint.Cd();
                _overFight = cd;
                _overRound = cd != null ? cd.RoundNum : -1;
                Plugin.Trace("[умения] фаза сдана в раунде " + _overRound + ", кнопки остаются живыми");
            }
            catch (Exception e) { Plugin.Trace("[умения] конец фазы: " + e.Message); }
        }

        private static bool Over()
        {
            if (_overRound < 0) return false;
            var cd = FighterHint.Cd();
            if (cd != null && ReferenceEquals(cd, _overFight) && cd.RoundNum == _overRound) return true;
            if (cd == null || !ReferenceEquals(cd, _overFight)) { _overFight = null; _overRound = -1; }
            return false;
        }

        internal static bool PhaseDone
        {
            get { return Over(); }
        }

        private static bool Late
        {
            get { return Over(); }
        }

        internal string Why(IQuickButton skill)
        {
            if (skill == null) return null;
            if (Chase(skill)) return null;
            float used;
            if (Used.TryGetValue(skill.Id, out used) && Time.unscaledTime - used < (_tricks ? 0f : _things ? 0.08f : 0.15f)) return Sending;
            var cd = FighterHint.Cd();
            bool late = Late;
            if (!late && cd != null && cd.RoundType != RoundType.WALK_ROUND && cd.RoundType != RoundType.COMBAT_ROUND) return "идёт расчёт раунда";
            if (cd != null && !QuickButtonHelper.CheckRoundType(skill, cd.RoundType)) return OffPhase;
            if (!skill.Enabled && !late) return string.IsNullOrEmpty(skill.DisableCause) ? "недоступно" : skill.DisableCause;
            if (!_things)
            {
                if (Cooling(skill)) return "перезарядка";
                if (!skill.CanActivate && !late) return "перезарядка";
            }
            if (_spells && !late && cd != null)
            {
                string spent = Spent(skill, cd);
                if (spent != null) return spent;
            }
            var me = cd != null ? cd.MyCharacter : null;
            var ind = me != null ? me.Indicators : null;
            if (ind == null) return null;
            if (skill.StaminaCost > ind.CurrentStamina) return "не хватает энергии";
            if (skill.ManaCost > ind.CurrentMana) return "не хватает маны";
            if (skill.ExpowerCost > ind.CurrentExpower) return Poor;
            return null;
        }

        private static string Spent(IQuickButton skill, ICombatData cd)
        {
            try
            {
                var spell = skill as Spell;
                var units = cd.TimeUnitsManager;
                if (spell == null || units == null) return null;
                switch (units.CanCastSpell(spell, cd.RoundType))
                {
                    case EQuickButtonValidationResult.AlreadyCastSpell: return "в этой фазе заклинание уже было";
                    case EQuickButtonValidationResult.NotEnoughActionPoints: return "не хватает очков действия";
                    default: return null;
                }
            }
            catch { return null; }
        }

        internal static bool SpellReady(int id)
        {
            try
            {
                if (!SideButtons.InCombat() || Spectate.Peeking) return true;
                var skill = Spells.Skill(id);
                return skill == null || Spells.Soft(Spells.Why(skill));
            }
            catch { return true; }
        }

        private void Listen()
        {
            if (_listening) return;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                nc.RemoveMessageListener(419, OnHint);
                nc.AddMessageListener(419, OnHint);
                nc.RemoveMessageListener(409, OnEffect);
                nc.AddMessageListener(409, OnEffect);
                _listening = true;
            }
            catch (Exception e) { Plugin.Trace(_tag + " слушатель подсказок: " + e.Message); }
        }

        private void OnHint(object message)
        {
            try
            {
                var answer = message as DynamicHintResponseMessage;
                if (answer == null || answer.Request == null || answer.Request.HintType != (int)HintKind) return;
                int id = answer.Request.Id;
                var skill = Skill(id);
                string raw = skill != null ? skill.Description : null;
                if (string.IsNullOrEmpty(raw)) return;
                var effect = answer.ActionEffectMessage;
                string made = DynamicHintHelper.PrepareActionDescription(raw, effect);
                if (effect != null)
                {
                    int items = effect.ActionEffectItems != null ? effect.ActionEffectItems.Count : 0;
                    int texts = effect.TextItems != null ? effect.TextItems.Count : 0;
                    if (Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                        Plugin.Trace(_tag + " подсказка " + id + ": эффектов " + items + ", строк " + texts
                            + (texts > 0 ? " [" + string.Join(" | ", effect.TextItems.ToArray()) + "]" : "")
                            + ", итог: " + made);
                    if (items == 0 && texts > 0 && made.IndexOf(effect.TextItems[0], StringComparison.Ordinal) < 0)
                        made = made + "\n" + string.Join(", ", effect.TextItems.ToArray());
                }
                else Plugin.Trace(_tag + " подсказка " + id + ": эффекта нет, итог: " + made);
                Told[id] = made;
                if (_tipFor == id) Fill(id);
            }
            catch (Exception e) { Plugin.Trace(_tag + " описание: " + e.Message); }
        }

        private EHintType HintKind => _things ? EHintType.THING : _tricks ? EHintType.DODGE : _spells ? EHintType.SPELL : EHintType.SKILL;

        private readonly Dictionary<int, string> Firm = new Dictionary<int, string>();
        private readonly Dictionary<string, float> Sent = new Dictionary<string, float>();

        private int Aimed(IQuickButton skill)
        {
            var cd = FighterHint.Cd();
            if (cd == null || skill == null) return 0;
            var me = cd.MyCharacter;
            var picked = cd.SelectedCharacter;
            int mine = me != null ? me.UserId : 0;
            switch (skill.Target)
            {
                case ETargetType.TARGET_SOURCE:
                case ETargetType.TARGET_CELL:
                case ETargetType.TARGET_SOURCE_CELL:
                    return mine;
                case ETargetType.TARGET_ANY_PLAYER:
                case ETargetType.TARGET_ENEMY:
                case ETargetType.TARGET_ALLY:
                case ETargetType.TARGET_ALLY_EXCEPT_SOURCE:
                case ETargetType.TARGET_ANY_PLAYER_CELL:
                case ETargetType.TARGET_ENEMY_CELL:
                case ETargetType.TARGET_ALLY_CELL:
                    return picked != null ? picked.UserId : mine;
                default:
                    return mine;
            }
        }

        private void AskFirm(int id)
        {
            var skill = Skill(id);
            if (skill == null) return;
            int who = Aimed(skill);
            string key = id + ":" + who;
            float last;
            if (Sent.TryGetValue(key, out last) && Time.unscaledTime - last < 2f) return;
            Sent[key] = Time.unscaledTime;
            try
            {
                Listen();
                var nc = NetworkConnection.Instance;
                if (nc != null && nc.IsConnected()) nc.SendRequest(new GetActionEffectDescriptionRequest(skill.QuickButtonType, id, who));
            }
            catch (Exception e) { Plugin.Trace(_tag + " запрос эффекта " + id + ": " + e.Message); }
        }

        private void OnEffect(object message)
        {
            try
            {
                var answer = message as GetActionEffectDescriptionResponseMessage;
                if (answer == null) return;
                var skill = Skill(answer.ButtonId);
                if (skill == null || (int)skill.QuickButtonType != answer.ButtonType) return;
                string raw = skill.Description;
                if (string.IsNullOrEmpty(raw)) return;
                string made = DynamicHintHelper.PrepareActionDescription(raw, answer.ActionEffectMessage);
                if (string.IsNullOrEmpty(made)) return;
                Firm[answer.ButtonId] = made;
                Plugin.Trace(_tag + " эффект " + answer.ButtonId + " по цели " + answer.TargetId + ": " + made);
                if (_tipFor == answer.ButtonId) Fill(answer.ButtonId);
            }
            catch (Exception e) { Plugin.Trace(_tag + " эффект: " + e.Message); }
        }

        private void AskTold(int id)
        {
            if (Told.ContainsKey(id) || !Asked.Add(id)) return;
            try
            {
                Listen();
                var nc = NetworkConnection.Instance;
                if (nc != null && nc.IsConnected()) nc.SendRequest(new DynamicHintRequest(HintKind, id, 0));
            }
            catch (Exception e) { Plugin.Trace(_tag + " запрос описания " + id + ": " + e.Message); }
        }

        private string Phase(int phase)
        {
            if (phase == 1) return "фаза перемещения";
            if (phase == 2) return "фаза боя";
            if (phase == 3) return "любая фаза";
            return "";
        }

        private string Costs(IQuickButton skill)
        {
            var parts = new List<string>();
            if (skill.ManaCost > 0) parts.Add("мана " + skill.ManaCost);
            if (skill.StaminaCost > 0) parts.Add("энергия " + skill.StaminaCost);
            if (skill.ExpowerCost > 0) parts.Add("стоимость " + skill.ExpowerCost);
            if (skill.Count > 0) parts.Add("зарядов " + skill.Count);
            if (skill.Recharge > 0 && skill.Recharge < 100 && !Summon(skill)) parts.Add("перезарядка " + skill.Recharge);
            return string.Join(", ", parts.ToArray());
        }

        private void ShowTip(int id, RectTransform near)
        {
            try
            {
                _tipFor = id;
                AskTold(id);
                AskFirm(id);
                if (_tipGo == null) BuildTip();
                if (_tipGo == null) return;
                Fill(id);
                _tipGo.SetActive(true);

                var corners = new Vector3[4];
                near.GetWorldCorners(corners);
                var point = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                var root = (RectTransform)_canvasGo.transform;
                Vector2 spot;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, point, null, out spot))
                {
                    var trt = (RectTransform)_tipGo.transform;
                    if (_tricks)
                    {
                        trt.pivot = new Vector2(0f, 0f);
                        trt.anchoredPosition = new Vector2(spot.x, spot.y + near.rect.height + 8f);
                    }
                    else if (_things)
                    {
                        trt.pivot = new Vector2(1f, 1f);
                        trt.anchoredPosition = new Vector2(spot.x - 8f, spot.y + near.rect.height);
                    }
                    else
                    {
                        trt.pivot = new Vector2(1f, 1f);
                        trt.anchoredPosition = new Vector2(spot.x + near.rect.width, spot.y - 6f);
                    }
                    Inside(trt, root);
                }
            }
            catch (Exception e) { Plugin.Trace(_tag + " подсказка " + id + ": " + e.Message); }
        }

        private static void Inside(RectTransform tip, RectTransform root)
        {
            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tip);
                var size = tip.rect.size;
                if (size.x < 1f || size.y < 1f) return;
                float halfWide = root.rect.width * 0.5f;
                float halfHigh = root.rect.height * 0.5f;
                float floor = -halfHigh + 6f;
                var spot = tip.anchoredPosition;
                float left = spot.x - size.x * tip.pivot.x;
                float low = spot.y - size.y * tip.pivot.y;
                if (left + size.x > halfWide - 6f) left = halfWide - 6f - size.x;
                if (left < -halfWide + 6f) left = -halfWide + 6f;
                if (low + size.y > halfHigh - 6f) low = halfHigh - 6f - size.y;
                if (low < floor) low = floor;
                tip.anchoredPosition = new Vector2(left + size.x * tip.pivot.x, low + size.y * tip.pivot.y);
            }
            catch (Exception e) { Plugin.Trace("[подсказка] место: " + e.Message); }
        }

        internal static string Spoken(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(' ') >= 0 || text.IndexOf('.') <= 0) return text;
            try
            {
                string said = ResourceStrings.GetString(text);
                return string.IsNullOrEmpty(said) || said == text ? text : said;
            }
            catch { return text; }
        }

        private void Fill(int id)
        {
            if (_tipText == null) return;
            var skill = Skill(id);
            var text = new StringBuilder();
            string name = skill != null && !string.IsNullOrEmpty(skill.Name) ? Spoken(skill.Name) : "";
            text.Append("<b><color=#e2b85c>").Append(name).Append("</color></b>");
            string mark = _things ? null : _tricks ? "trick" : _spells ? "spell" : "skill";
            string bind = mark == null ? "" : Hotkeys.Tail(mark + ":" + id);
            if (bind.Length > 0) text.Append("<color=#a8c4ea>").Append(bind).Append("</color>");
            if (_tricks && Focus.Is(skill))
                text.Append("\n<color=#e2b85c>").Append(Focus.Hint(Hotkeys.Of(mark + ":" + id))).Append("</color>\n");
            string block = Reason(id);
            if (block != null && block != OffPhase) text.Append("\n<color=#f07a6e>").Append(block).Append("</color>");
            if (skill != null)
            {
                string phase = _things || _spells ? "" : Phase(skill.Phase);
                if (phase.Length > 0) text.Append("\n<color=#acb3bd>").Append(phase).Append("</color>");
                string cost = Costs(skill);
                if (cost.Length > 0) text.Append("\n<color=#d6dae0>").Append(cost).Append("</color>");
                string told, firm;
                string plain = Spoken(skill.Description);
                string body = Firm.TryGetValue(id, out firm) ? firm
                    : Told.TryGetValue(id, out told) ? told : plain;
                if (!string.IsNullOrEmpty(body)) text.Append("\n\n").Append(body.Trim());
            }
            _tipText.text = text.ToString();
        }

        private string Reason(int id)
        {
            Cell cell;
            return Live.TryGetValue(id, out cell) ? cell.Block : null;
        }

        private void HideTip()
        {
            _tipFor = 0;
            if (_tipGo != null) _tipGo.SetActive(false);
        }

        private void BuildTip()
        {
            if (_canvasGo == null) return;
            _tipGo = new GameObject("tip", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup), typeof(CanvasGroup));
            _tipGo.transform.SetParent(_canvasGo.transform, false);
            var trt = (RectTransform)_tipGo.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(1f, 1f);

            var back = _tipGo.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            var edge = _tipGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var veil = _tipGo.GetComponent<CanvasGroup>();
            veil.blocksRaycasts = false;
            veil.interactable = false;

            var box = _tipGo.GetComponent<VerticalLayoutGroup>();
            box.padding = new RectOffset(10, 10, 8, 8);
            box.childControlWidth = true;
            box.childControlHeight = true;
            box.childForceExpandWidth = true;
            box.childForceExpandHeight = false;

            var fit = _tipGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            trt.sizeDelta = new Vector2(320f, 60f);

            _tipText = OnlineWindow.Label(_tipGo.transform, "", 14, FontStyle.Normal, WardrobeLook.Bright);
            _tipText.alignment = TextAnchor.UpperLeft;
            _tipText.supportRichText = true;
            _tipText.raycastTarget = false;
            _tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tipText.verticalOverflow = VerticalWrapMode.Overflow;
            var le = _tipText.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 300f;
            _tipGo.SetActive(false);
        }

        private const float TapGap = 0.45f;
        private int _tapId;
        private float _tapAt = -10f;

        private bool Twice(int id)
        {
            float now = Time.unscaledTime;
            bool twice = id == _tapId && now - _tapAt <= TapGap;
            _tapId = id;
            _tapAt = twice ? -10f : now;
            return twice;
        }

        private bool Single(IQuickButton skill)
        {
            if (skill == null || (!_tricks && !_spells)) return false;
            if (Chase(skill) || Summon(skill)) return false;
            var at = skill.Target;
            return at == ETargetType.TARGET_ANY_PLAYER || at == ETargetType.TARGET_ENEMY
                || at == ETargetType.TARGET_ALLY || at == ETargetType.TARGET_ALLY_EXCEPT_SOURCE
                || at == ETargetType.TARGET_ANY_PLAYER_CELL || at == ETargetType.TARGET_ENEMY_CELL
                || at == ETargetType.TARGET_ALLY_CELL || at == ETargetType.TARGET_CELL;
        }

        private void AtChosen(IQuickButton skill)
        {
            try
            {
                if (skill == null) return;
                string why = Why(skill);
                if (!Soft(why)) { Plugin.Trace(_tag + " по выделенному нельзя: " + why); return; }
                var cd = FighterHint.Cd();
                var chosen = cd != null ? cd.SelectedCharacter : null;
                if (chosen == null || chosen.UserId == 0)
                {
                    try { AirMessageScript.ShowErrorNotification("combat.gui.combatbutton.disablecause.target_not_selected"); }
                    catch (Exception e) { Plugin.Trace(_tag + " сообщение о цели: " + e.Message); }
                    Plugin.Trace(_tag + " двойной клик: выделенного бойца нет");
                    return;
                }
                bool cell = TargetTypeExtension.IsActionHasCellTarget(skill);
                var hex = chosen.HexGridPosition;
                var verdict = cell ? Check(skill, hex) : Check(skill);
                if (Broke(verdict)) { Disarm(); if (!Topup(skill, verdict)) Hand(skill); return; }
                if (verdict != EQuickButtonValidationResult.Success) { Refuse(skill, verdict); return; }
                Disarm();
                Plugin.Trace(_tag + " двойной клик по выделенному " + chosen.UserId + (cell ? ", по его клетке" : ""));
                if (cell) FireAt(skill, hex);
                else Fire(skill);
            }
            catch (Exception e) { Plugin.Warn(_tag + " двойной клик: " + e.Message); }
        }

        private bool Focused(IQuickButton skill)
        {
            if (!_tricks || !Focus.Is(skill)) return false;
            bool took = Focus.Click(skill);
            _pollAt = 0f;
            if (_tipFor == skill.Id) Fill(skill.Id);
            return took;
        }

        private void Tap(IQuickButton skill)
        {
            try
            {
                if (skill == null) return;
                if (Focused(skill)) return;
                bool twice = Twice(skill.Id);
                bool single = Single(skill);
                if (Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                    Plugin.Trace(_tag + " клавиша " + skill.Id + (twice ? ", второе нажатие" : ", первое нажатие")
                        + ", цель " + skill.Target + ", по выделенному " + single
                        + ", наведено " + (_armed != null ? _armed.Id.ToString() : "нет"));
                if (twice && single) { AtChosen(skill); return; }
                Pick(skill);
            }
            catch (Exception e) { Plugin.Warn(_tag + " нажатие " + skill.Id + ": " + e.Message); }
        }

        private void Pick(IQuickButton skill)
        {
            try
            {
                if (skill == null) return;
                if (_armed != null && _armed.Id == skill.Id) { Disarm(); return; }
                if (TargetTypeExtension.IsActionHasCellTarget(skill)) { Arm(skill); return; }
                if (Chase(skill))
                {
                    Disarm();
                    bool off = skill.Id == DodgesButtonStateHolder.HantingOffButtonId;
                    if (off || skill.Target == ETargetType.TARGET_SOURCE) { Fire(skill); return; }
                    var cd = FighterHint.Cd();
                    var chosen = cd != null ? cd.SelectedCharacter : null;
                    if (chosen == null || chosen.UserId == 0)
                    {
                        try { AirMessageScript.ShowErrorNotification("combat.gui.combatbutton.disablecause.target_not_selected"); }
                        catch (Exception e) { Plugin.Trace(_tag + " сообщение о цели: " + e.Message); }
                        Plugin.Trace(_tag + " преследование: выделенного бойца нет");
                        return;
                    }
                    Plugin.Trace(_tag + " преследование по выделению: " + chosen.UserId);
                    Fire(skill);
                    return;
                }
                if (_things && skill.Id < 0) { Hand(skill); return; }
                if (_things && skill.Target == ETargetType.TARGET_SOURCE) { Fire(skill); return; }
                if (ByHand(skill)) { Arm(skill); return; }
                if (_spells && skill.Target != ETargetType.TARGET_SOURCE) { Arm(skill); return; }
                if (Native)
                {
                    var verdict = Check(skill);
                    if (verdict != EQuickButtonValidationResult.Success && !Broke(verdict)) { Refuse(skill, verdict); return; }
                    if (Broke(verdict) && Topup(skill, verdict)) return;
                    Hand(skill);
                    return;
                }
                if (skill.Target == ETargetType.TARGET_SOURCE) { Disarm(); Fire(skill); return; }
                if (_armed != null && _armed.Id == skill.Id) { Disarm(); return; }
                Arm(skill);
            }
            catch (Exception e) { Plugin.Warn(_tag + " выбор " + skill.Id + ": " + e.Message); }
        }

        private static bool Counter(IQuickButton skill)
        {
            return skill != null && skill.Id == DodgeConsts.DODGE_COUNTER_DODGE;
        }

        private const string OffPhase = "не в этой фазе";
        private const string Poor = "не хватает зарядов";
        private const string Sending = "применяю…";

        private bool Soft(string why)
        {
            return why == null || why == Sending || (Native && why == Poor);
        }

        private static IQuickButton _paid;
        private static bool _onPaid;

        private bool Topup(IQuickButton skill, EQuickButtonValidationResult verdict)
        {
            if (verdict != EQuickButtonValidationResult.NotEnoughExpowerCanBuy || skill == null) return false;
            try
            {
                var mine = this;
                DialogFactory.ShowPriceConfirmMessageBox(
                    "messages.confirmsprice.buyexpowerincombat.caption", null,
                    delegate (EMessageBoxResult answer)
                    {
                        if (answer != EMessageBoxResult.MB_OK) return;
                        _paid = skill;
                        mine.Paid();
                        NetworkConnection.Instance.SendRequest(new BuyExpowerInCombatRequest());
                    },
                    EPriceKey.ExpowerMax, "messages.confirmsprice.buyexpowerincombat.buyexpower");
                Plugin.Trace(_tag + " зарядов мало, открыл пополнение по " + skill.Id);
                return true;
            }
            catch (Exception e) { Plugin.Trace(_tag + " пополнение: " + e.Message); return false; }
        }

        private void Paid()
        {
            if (_onPaid) return;
            var nc = NetworkConnection.Instance;
            if (nc == null) return;
            _onPaid = true;
            nc.AddAfterMessageListener(318, Bought);
        }

        private void Bought(object message)
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc != null) nc.RemoveAfterMessageListener(318, Bought);
                _onPaid = false;
                var skill = _paid;
                _paid = null;
                if (skill == null) return;
                Plugin.Trace(_tag + " заряды куплены, применяю " + skill.Id);
                Hand(skill);
            }
            catch (Exception e) { Plugin.Trace(_tag + " после пополнения: " + e.Message); }
        }

        private static bool Broke(EQuickButtonValidationResult verdict)
        {
            return verdict == EQuickButtonValidationResult.NotEnoughExpower
                || verdict == EQuickButtonValidationResult.NotEnoughExpowerCanBuy;
        }

        private bool ByHand(IQuickButton skill)
        {
            if (!_tricks || skill == null) return false;
            var at = skill.Target;
            return at == ETargetType.TARGET_ENEMY || at == ETargetType.TARGET_ALLY
                || at == ETargetType.TARGET_ANY_PLAYER || at == ETargetType.TARGET_ALLY_EXCEPT_SOURCE;
        }

        private bool Chase(IQuickButton skill)
        {
            if (!_tricks || skill == null) return false;
            return skill.Id == DodgesButtonStateHolder.HantingOnButtonId
                || skill.Id == DodgesButtonStateHolder.HantingOffButtonId;
        }

        private bool Summon(IQuickButton skill)
        {
            if (!_tricks || skill == null) return false;
            int id = skill.Id;
            return id == DodgeConsts.DODGE_SUMMON_1 || id == DodgeConsts.DODGE_SUMMON_2
                || id == DodgeConsts.DODGE_SUMMON_3 || id == DodgeConsts.DODGE_GENERAL_SUMMON;
        }

        private void Hand(IQuickButton skill)
        {
            try
            {
                var ctrl = _ctrl ?? Controllers.Get<CombatButtonsController>();
                if (ctrl == null) return;
                Used[skill.Id] = Time.unscaledTime;
                _pollAt = 0f;
                string name = _things ? "ActivateUsedThingsButtonHandler" : _spells ? "ActivateSpellButtonHandler" : "ActivateDodgeButtonHandler";
                var call = AccessTools.Method(typeof(CombatButtonsController), name);
                if (call == null) { Plugin.Trace(_tag + " у игры нет " + name); return; }
                call.Invoke(ctrl, new object[] { skill.Id });
                Plugin.Trace(_tag + " отдал игре " + skill.Id);
            }
            catch (Exception e) { Plugin.Warn(_tag + " применение " + skill.Id + ": " + e.Message); }
        }
        private void Dialog(int id)
        {
            try
            {
                var holder = Holder();
                var dialog = Controllers.Get<ConfirmActionDialogController>();
                if (holder == null || dialog == null) return;
                dialog.OpenDialog(holder, id);
            }
            catch (Exception e) { Plugin.Warn(_tag + " окно выбора " + id + ": " + e.Message); }
        }

        private void Slot(IQuickButton skill)
        {
            var go = new GameObject("skill", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(_cells, false);

            var frame = go.GetComponent<Image>();
            frame.color = WardrobeLook.Card;
            frame.sprite = _tricks ? OnlineWindow.Disc() : OnlineWindow.Rounded(6);
            frame.type = _tricks ? Image.Type.Simple : Image.Type.Sliced;

            var edge = go.GetComponent<Outline>();
            edge.enabled = !_tricks;
            edge.effectColor = WardrobeLook.FieldEdge;
            edge.effectDistance = new Vector2(2f, -2f);

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)iconGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(4f, 4f), new Vector2(-4f, -4f));
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = AtlasUtils.GetQuickButtonSprite(skill);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var dimGo = new GameObject("dim", typeof(RectTransform), typeof(Image));
            dimGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)dimGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var dim = dimGo.GetComponent<Image>();
            dim.color = new Color(WardrobeLook.Field.r, WardrobeLook.Field.g, WardrobeLook.Field.b, 0.55f);
            dim.sprite = OnlineWindow.Rounded(6);
            dim.type = Image.Type.Sliced;
            dim.raycastTarget = false;

            var coolGo = new GameObject("cool", typeof(RectTransform), typeof(Image));
            coolGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)coolGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var cool = coolGo.GetComponent<Image>();
            cool.sprite = OnlineWindow.Rounded(6);
            cool.color = new Color(0f, 0f, 0f, 0.62f);
            cool.type = Image.Type.Filled;
            cool.fillMethod = Image.FillMethod.Radial360;
            cool.fillOrigin = (int)Image.Origin360.Top;
            cool.fillClockwise = false;
            cool.fillAmount = 0f;
            cool.raycastTarget = false;
            cool.enabled = false;

            var count = OnlineWindow.Label(go.transform, "", 13, FontStyle.Bold, WardrobeLook.Bright);
            count.alignment = TextAnchor.LowerRight;
            count.raycastTarget = false;
            var shadow = count.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(1f, -1f);
            OnlineWindow.Place(count.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-3f, -2f));
            count.enabled = false;

            var watch = go.AddComponent<HoverWatch>();
            int who = skill.Id;
            var where = (RectTransform)go.transform;
            watch.OnEnter = () => ShowTip(who, where);
            watch.OnExit = HideTip;

            var markGo = new GameObject("armed", typeof(RectTransform), typeof(Image), typeof(Outline));
            markGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)markGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-2f, -2f), new Vector2(2f, 2f));
            var mark = markGo.GetComponent<Image>();
            mark.color = new Color(WardrobeLook.Accent.r, WardrobeLook.Accent.g, WardrobeLook.Accent.b, _tricks ? 0.20f : 0.22f);
            mark.sprite = _tricks ? OnlineWindow.Disc() : OnlineWindow.Rounded(6);
            mark.type = _tricks ? Image.Type.Simple : Image.Type.Sliced;
            mark.raycastTarget = false;
            var markEdge = markGo.GetComponent<Outline>();
            markEdge.enabled = !_tricks;
            markEdge.effectColor = WardrobeLook.Accent;
            markEdge.effectDistance = new Vector2(2f, -2f);
            mark.enabled = false;

            var button = go.GetComponent<Button>();
            button.targetGraphic = frame;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(2.6f, 2.2f, 1.6f, 1f);
            colors.fadeDuration = 0f;
            button.colors = colors;
            var shot = skill;
            var quick = go.AddComponent<EventTrigger>();
            var tap = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            tap.callback.AddListener(data =>
            {
                var point = data as PointerEventData;
                if (point != null && point.button != PointerEventData.InputButton.Left) return;
                if (Cooldown.Catch(shot)) return;
                if (Focused(shot)) return;
                if (!button.interactable) return;
                Pick(shot);
            });
            quick.triggers.Add(tap);

            var left = OnlineWindow.Label(go.transform, "", 16, FontStyle.Bold, WardrobeLook.Bright);
            left.alignment = TextAnchor.MiddleCenter;
            left.raycastTarget = false;
            var leftEdge = left.gameObject.AddComponent<Outline>();
            leftEdge.effectColor = new Color(0f, 0f, 0f, 0.95f);
            leftEdge.effectDistance = new Vector2(1.5f, -1.5f);
            OnlineWindow.Place(left.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            left.enabled = false;

            Image lit = null;
            if (_tricks)
            {
                var ringGo = new GameObject("ring", typeof(RectTransform), typeof(Image));
                ringGo.transform.SetParent(go.transform, false);
                OnlineWindow.Place((RectTransform)ringGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                var ring = ringGo.GetComponent<Image>();
                ring.sprite = OnlineWindow.Ring();
                ring.color = new Color32(150, 170, 190, 90);
                ring.raycastTarget = false;
                ringGo.transform.SetAsLastSibling();

                if (Focus.Is(skill))
                {
                    var litGo = new GameObject("focus", typeof(RectTransform), typeof(Image));
                    litGo.transform.SetParent(go.transform, false);
                    OnlineWindow.Place((RectTransform)litGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(1f, 1f), new Vector2(-1f, -1f));
                    lit = litGo.GetComponent<Image>();
                    lit.sprite = Focus.Glow();
                    lit.color = new Color(WardrobeLook.Accent.r, WardrobeLook.Accent.g, WardrobeLook.Accent.b, 0.3f);
                    lit.raycastTarget = false;
                    litGo.AddComponent<FocusPulse>();
                    litGo.transform.SetSiblingIndex(coolGo.transform.GetSiblingIndex() + 1);
                    litGo.SetActive(false);
                }
            }

            Live[skill.Id] = new Cell { Icon = icon, Dim = dim, Cool = cool, Left = left, Count = count, Edge = edge, Press = button, Mark = mark, Lit = lit };
        }
    }
}

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(CombatController), "OnHexClick")]
    internal static class SkillListHexClickPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HexClickInfo target)
        {
            try
            {
                if (!SkillList.Eaten()) return true;
                if (target != null) target.Handled = true;
                Plugin.Trace("[прицел] клик по бойцу не проваливается на клетку под ним");
                return false;
            }
            catch (Exception e) { Plugin.Trace("[прицел] клик по клетке: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(CombatButtonsController), "OnEndPhase")]
    internal static class SkillListPhasePatch
    {
        private static void Postfix() => SkillList.PhaseOver();
    }

    internal sealed class HoverWatch : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal Action OnEnter;
        internal Action OnExit;

        public void OnPointerEnter(PointerEventData e)
        {
            if (OnEnter != null) OnEnter();
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (OnExit != null) OnExit();
        }

        private void OnDisable()
        {
            if (OnExit != null) OnExit();
        }
    }
}
