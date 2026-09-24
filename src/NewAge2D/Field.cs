using System.Globalization;
using System.Xml;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NewAge2D;

[HarmonyPatch]
internal static class Field
{
    internal static readonly float PxPerUnit = 104f / (MathConsts.Sqrt3 * MathConsts.HEX_SIZE);
    internal static readonly float RowPx = 84f / (2f * MathConsts.HEX_SIZE);
    internal static readonly float Lean = RowPx / PxPerUnit;
    internal static readonly float Pitch = Mathf.Asin(Lean) * Mathf.Rad2Deg;

    private const float Mirror = 0.3f;
    private static int Keep => Beauty.Weak ? 2 : 3;

    internal sealed class Ground
    {
        public string File;
        public bool DayNight;
        public bool Combat;
        public string Id;
        public bool Placed;
        public float X;
        public float Y;
    }

    internal sealed class Backdrop
    {
        public string Key;
        public string File;
        public string Error;
        public Texture2D Texture;
        public Sprite Sprite;
        public int Width;
        public int Height;
        public float PivotX;
        public float PivotY;
        public float Scale = 1f;
    }

    internal static readonly List<GameObject> Scenery = new();

    private static readonly List<Backdrop> Backdrops = new();
    private static readonly Dictionary<string, List<Action<Backdrop>>> Loading = new();

    private static Ground _parsed;
    private static Ground _ground;
    private static FieldView _view;
    private static int _battle;

    internal static bool Active => _view != null && _view.Applied;

    internal static float DollSize => Mathf.Clamp(Plugin.CfgDollSize.Value, 0.3f, 3f);


    internal static Vector3 SceneToGrid(float x, float y) => new(x / PxPerUnit, 0f, (21f - y) / RowPx);

    private static Vector2 HexCorner(int x, int y) => new(104f * (x - 1) - ((y & 1) != 0 ? 52f : 0f), 63f * (y - 1) - 21f);

    [HarmonyPrefix, HarmonyPatch(typeof(LocationMapBuilder), nameof(LocationMapBuilder.Build))]
    private static void BeforeBuild(string locationXml)
    {
        try
        {
            _parsed = Parse(locationXml);
            if (_parsed != null && _parsed.Combat && Plugin.FlashField) Fetch(_parsed, null);
        }
        catch (Exception ex)
        {
            _parsed = null;
            Plugin.Log.LogWarning("[поле] XML карты не разобрался: " + ex.Message);
        }
    }

    private static int Number(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    private static Ground Parse(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        var settings = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = true, IgnoreComments = true };
        Ground ground = null;
        bool combat = false;
        using (var reader = XmlReader.Create(new StringReader(xml), settings))
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.Name == "Map")
                {
                    combat = reader.GetAttribute("mapType") == "1";
                }
                else if (reader.Name == "element")
                {
                    if (ground != null || reader.GetAttribute("ground") != "1") continue;
                    string path = reader.GetAttribute("path");
                    if (path == null || path.IndexOf(".swf", StringComparison.OrdinalIgnoreCase) <= 0) continue;
                    ground = new Ground { File = path, DayNight = reader.GetAttribute("dn") != null, Id = reader.GetAttribute("id") };
                }
                else if (reader.Name == "p" && ground != null && !ground.Placed && reader.GetAttribute("id") == ground.Id)
                {
                    int x = Number(reader.GetAttribute("X"));
                    int y = Number(reader.GetAttribute("Y"));
                    ground.Placed = true;
                    if (reader.GetAttribute("c") == "1")
                    {
                        ground.X = x;
                        ground.Y = y;
                    }
                    else
                    {
                        var corner = HexCorner(x, y);
                        ground.X = corner.x;
                        ground.Y = corner.y;
                    }
                }
            }
        }
        if (ground == null) return null;
        ground.Combat = combat;
        if (!ground.Placed)
        {
            var corner = HexCorner(1, 1);
            ground.X = corner.x;
            ground.Y = corner.y;
        }
        return ground;
    }

    internal static readonly Color Morning = new(1f, 0.96f, 0.90f);
    internal static readonly Color Night = new(0.76f, 0.82f, 0.97f);

    internal static Color DayTint
    {
        get
        {
            if (!Active) return Color.white;
            string file = _view != null && _view.Backdrop != null ? _view.Backdrop.File : null;
            if (string.IsNullOrEmpty(file)) return Color.white;
            if (file.IndexOf("_night", StringComparison.OrdinalIgnoreCase) >= 0) return Night;
            if (file.IndexOf("_morning", StringComparison.OrdinalIgnoreCase) >= 0) return Morning;
            return Color.white;
        }
    }

    internal static string DayPart()
    {
        int hour = DateTime.UtcNow.AddHours(Plugin.CfgFieldZone.Value).Hour;
        if ((hour >= 6 && hour <= 8) || (hour >= 18 && hour <= 20)) return "_morning";
        return hour > 8 && hour < 18 ? "_day" : "_night";
    }

    private static List<string> FilesFor(Ground ground)
    {
        var files = new List<string>();
        int cut = ground.File.IndexOf(".swf", StringComparison.OrdinalIgnoreCase);
        if (ground.DayNight && cut > 0) files.Add(ground.File.Substring(0, cut) + DayPart() + ".swf");
        files.Add(ground.File);
        return files;
    }

    internal static void Fetch(Ground ground, Action<Backdrop> done)
    {
        var files = FilesFor(ground);
        string key = string.Join("|", files);
        var ready = Backdrops.FirstOrDefault(b => b.Key == key && b.Texture != null);
        if (ready != null)
        {
            done?.Invoke(ready);
            return;
        }
        if (Loading.TryGetValue(key, out var waiters))
        {
            if (done != null) waiters.Add(done);
            return;
        }
        waiters = new List<Action<Backdrop>>();
        if (done != null) waiters.Add(done);
        Loading[key] = waiters;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var pick = Sharpness();
        DollWorker.Run(() => Draw(files, pick), result => MainThread.Post(() => Finish(key, result, clock)), true);
    }

    private static Func<int, int, float> Sharpness()
    {
        float screenW = Mathf.Max(1, Screen.width);
        float screenH = Mathf.Max(1, Screen.height);
        float wanted = Plugin.CfgFieldView.Value;
        float view = wanted > 0f ? Mathf.Clamp(wanted, 300f, 4000f) : 0f;
        int side = Beauty.MaxSide;
        long bytes = Beauty.MaxBytes;
        return (width, height) =>
        {
            float shown = view > 0f ? view : Math.Min(width * screenH / screenW, height);
            float scale = screenH / Math.Max(1f, shown) * 1.15f;
            scale = Math.Min(scale, side / (float)Math.Max(width, height));
            scale = Math.Min(scale, (float)Math.Sqrt(bytes / (4.0 * width * height)));
            return Math.Max(1f, (float)Math.Floor(scale * 20f) / 20f);
        };
    }

    private static object Draw(List<string> files, Func<int, int, float> pick)
    {
        var reasons = new List<string>();
        foreach (string file in files)
        {
            var movie = Plugin.Store.Get(file, out string reason);
            if (movie == null)
            {
                reasons.Add($"{file}: {reason}");
                continue;
            }
            var picture = Doll.RenderStage(movie, Mirror, pick);
            picture.Notes.Add(file);
            return picture;
        }
        return new DollPicture { Error = string.Join("; ", reasons) };
    }

    private static void Finish(string key, object result, System.Diagnostics.Stopwatch clock)
    {
        var picture = result as DollPicture ?? new DollPicture { Error = result is Exception failure ? failure.Message : "пустой результат" };
        var backdrop = new Backdrop { Key = key };
        if (picture.Error != null || picture.Rgba == null || picture.Width <= 0 || picture.Height <= 0)
            backdrop.Error = picture.Error ?? "пусто";
        else
        {
            try { Build(backdrop, picture); }
            catch (Exception ex) { backdrop.Error = ex.Message; }
        }
        if (backdrop.Error == null)
        {
            Remember(backdrop);
            Plugin.Log.LogInfo($"[поле] {backdrop.File} готов: {backdrop.Width}x{backdrop.Height} с запасом снизу, растр ×{backdrop.Scale:0.##} ({backdrop.Texture.width}x{backdrop.Texture.height}), {clock.ElapsedMilliseconds} мс");
        }
        if (!Loading.TryGetValue(key, out var waiters)) return;
        Loading.Remove(key);
        foreach (var waiter in waiters)
        {
            try { waiter(backdrop); }
            catch (Exception ex) { Plugin.Log.LogError("[поле] " + ex); }
        }
    }

    private static void Build(Backdrop backdrop, DollPicture picture)
    {
        float scale = picture.Scale > 0f ? picture.Scale : 1f;
        var texture = new Texture2D(picture.Width, picture.Height, TextureFormat.RGBA32, true)
        {
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = Beauty.Aniso,
            name = "NewAge2D.Field",
        };
        texture.SetPixelData(picture.Rgba, 0);
        texture.Apply(true, true);
        backdrop.Texture = texture;
        backdrop.Scale = scale;
        backdrop.Sprite = Sprite.Create(texture, new Rect(0, 0, picture.Width, picture.Height),
            new Vector2(picture.PivotX, picture.PivotY), PxPerUnit * scale, 0, SpriteMeshType.FullRect);
        backdrop.File = picture.Notes.Count > 0 ? picture.Notes[picture.Notes.Count - 1] : "?";
        backdrop.Width = Mathf.RoundToInt(picture.Width / scale);
        backdrop.Height = Mathf.RoundToInt(picture.Height / scale);
        backdrop.PivotX = picture.PivotX;
        backdrop.PivotY = picture.PivotY;
    }

    private static void Remember(Backdrop backdrop)
    {
        Backdrops.Add(backdrop);
        while (Backdrops.Count > Keep)
        {
            var old = Backdrops.FirstOrDefault(b => _view == null || !ReferenceEquals(_view.Backdrop, b));
            if (old == null) break;
            Backdrops.Remove(old);
            Drop(old);
        }
    }

    private static void Drop(Backdrop backdrop)
    {
        if (backdrop.Sprite != null) UnityEngine.Object.Destroy(backdrop.Sprite);
        if (backdrop.Texture != null) UnityEngine.Object.Destroy(backdrop.Texture);
        backdrop.Sprite = null;
        backdrop.Texture = null;
    }

    private static IEnumerable<GameObject> Roots()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects()) yield return root;
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CombatLocationLoadController), nameof(CombatLocationLoadController.AssetDataLoaded))]
    private static void BeforeLoaded(out HashSet<int> __state)
    {
        __state = new HashSet<int>();
        try
        {
            foreach (var root in Roots()) __state.Add(root.GetInstanceID());
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[поле] список объектов сцены: " + ex.Message); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CombatLocationLoadController), nameof(CombatLocationLoadController.AssetDataLoaded))]
    private static void AfterLoaded(HashSet<int> __state)
    {
        try
        {
            var fresh = new List<GameObject>();
            foreach (var root in Roots())
                if (__state == null || !__state.Contains(root.GetInstanceID())) fresh.Add(root);
            Begin(fresh);
        }
        catch (Exception ex) { Plugin.Log.LogError("[поле] " + ex); }
    }

    private static void Begin(List<GameObject> fresh)
    {
        Stop();
        _battle++;
        Scenery.Clear();
        Scenery.AddRange(fresh);
        _ground = _parsed;
        Trace.Battle(_ground?.File ?? "карта без картинки Flash");
        if (Plugin.CfgVerbose.Value)
            Plugin.Log.LogInfo(_ground == null
                ? $"[поле] у карты боя нет картинки Flash, объектов 3D-карты {fresh.Count}"
                : $"[поле] карта боя: {_ground.File}{(_ground.DayNight ? " (утро/день/ночь)" : "")}, угол картинки во Flash ({_ground.X:0}, {_ground.Y:0}), объектов 3D-карты {fresh.Count}");
        if (Plugin.FlashField) Set(true);
    }

    internal static void Set(bool on)
    {
        if (!on)
        {
            Stop();
            return;
        }
        if (_view != null) return;
        var location = CombatView.Get();
        if (_ground == null || location == null || location.HexGrid == null) return;
        var go = new GameObject("NewAge2D.Field");
        var view = go.AddComponent<FieldView>();
        _view = view;
        int battle = _battle;
        view.Init(location.HexGrid.transform, _ground);
        Fetch(_ground, backdrop =>
        {
            if (battle != _battle || view == null || !ReferenceEquals(view, _view)) return;
            if (backdrop.Error != null)
            {
                Plugin.Log.LogWarning($"[поле] картинка поля не получилась ({backdrop.Error}), бой остаётся в 3D");
                Stop();
                return;
            }
            view.Show(backdrop);
        });
    }

    internal static void Stop()
    {
        var view = _view;
        _view = null;
        if (view == null) return;
        view.Restore();
        UnityEngine.Object.Destroy(view.gameObject);
    }

    internal static void Dispose()
    {
        Stop();
        foreach (var backdrop in Backdrops) Drop(backdrop);
        Backdrops.Clear();
        Loading.Clear();
    }

    internal static void Forget()
    {
        foreach (var backdrop in Backdrops.ToList())
        {
            if (_view != null && ReferenceEquals(_view.Backdrop, backdrop)) continue;
            Backdrops.Remove(backdrop);
            Drop(backdrop);
        }
    }

    internal static void Forget(FieldView view)
    {
        if (ReferenceEquals(_view, view)) _view = null;
    }
}

internal sealed class FieldView : MonoBehaviour
{
    private const float Distance = 60f;
    private const float MinView = 300f;
    private const float MaxView = 4000f;

    private static readonly System.Reflection.FieldInfo ConstraintField = AccessTools.Field(typeof(CameraControl), "constraint");

    private Transform _grid;
    private Field.Ground _ground;
    private GameObject _pictureObject;
    private Material _skin;
    private bool _hasBounds;
    private float _xMin;
    private float _xMax;
    private float _zMin;
    private float _zMax;

    private Camera _eye;
    private CameraControl _control;
    private bool _wasOrtho;
    private float _wasSize;
    private CameraClearFlags _wasClear;
    private Color _wasColor;
    private float _wasNear;
    private float _wasFar;
    private bool _wasControl;

    private readonly List<Renderer> _hidden = new();
    private readonly List<Terrain> _terrains = new();
    private float _rescanAt;

    private bool _warmLogged;
    private Vector2 _focus;
    private bool _centered;
    private float _appliedAt;
    private float _view;
    private float _shownView;
    private bool _zoomed;
    private bool _pressed;
    private bool _dragging;
    private int _button;
    private Vector3 _pressAt;
    private Vector3 _lastMouse;
    private bool _failed;

    internal bool Applied { get; private set; }

    internal Field.Backdrop Backdrop { get; private set; }

    internal void Init(Transform grid, Field.Ground ground)
    {
        _grid = grid;
        _ground = ground;
        Camera.onPreCull += BeforeCull;
        Apply();
    }

    internal void Show(Field.Backdrop backdrop)
    {
        if (_grid == null || backdrop?.Sprite == null) return;
        Backdrop = backdrop;
        if (_pictureObject != null) Destroy(_pictureObject);

        _pictureObject = new GameObject("NewAge2D.FieldPicture");
        _pictureObject.transform.SetParent(_grid, false);
        _pictureObject.layer = _grid.gameObject.layer;
        float x = _ground.X + Plugin.CfgFieldOffsetX.Value;
        float y = _ground.Y + Plugin.CfgFieldOffsetY.Value;
        var origin = Field.SceneToGrid(x, y);
        origin.y = -0.03f;
        _pictureObject.transform.localPosition = origin;
        _pictureObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _pictureObject.transform.localScale = new Vector3(1f, Field.PxPerUnit / Field.RowPx, 1f);

        var view = _pictureObject.AddComponent<SpriteRenderer>();
        if (_skin == null)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader != null) _skin = new Material(shader);
        }
        if (_skin != null) view.sharedMaterial = _skin;
        view.sprite = backdrop.Sprite;
        var layers = SortingLayer.layers;
        if (layers.Length > 0) view.sortingLayerID = layers[0].id;
        view.sortingOrder = short.MinValue;

        float width = backdrop.Width / Field.PxPerUnit;
        float height = backdrop.Height / Field.RowPx;
        _xMin = origin.x - backdrop.PivotX * width;
        _xMax = _xMin + width;
        _zMin = origin.z - backdrop.PivotY * height;
        _zMax = _zMin + height;
        _hasBounds = true;
        _pictureObject.SetActive(Applied);

        Plugin.Log.LogInfo($"[поле] {backdrop.File} на поле: угол ({x:0}, {y:0}) → сетка ({origin.x:0.00}, {origin.z:0.00}), камера под {Field.Pitch:0.0}°");
    }

    private TransparencySortMode _wasSort;

    private void Apply()
    {
        var location = CombatView.Get();
        _eye = location != null && location.CombatCamera != null ? location.CombatCamera : Camera.main;
        if (_eye == null)
        {
            Plugin.Log.LogWarning("[поле] камеры боя нет, бой остаётся в 3D");
            return;
        }
        _control = FindObjectOfType<CameraControl>();
        _wasSort = _eye.transparencySortMode;
        _eye.transparencySortMode = TransparencySortMode.Orthographic;
        _wasOrtho = _eye.orthographic;
        _wasSize = _eye.orthographicSize;
        _wasClear = _eye.clearFlags;
        _wasColor = _eye.backgroundColor;
        _wasNear = _eye.nearClipPlane;
        _wasFar = _eye.farClipPlane;
        _eye.clearFlags = CameraClearFlags.SolidColor;
        _eye.backgroundColor = Color.black;
        if (_control != null)
        {
            _wasControl = _control.enabled;
            _control.Active = false;
            _control.enabled = false;
        }
        float wanted = Plugin.CfgFieldView.Value;
        _view = wanted <= 0f ? 0f : Mathf.Clamp(wanted, MinView, MaxView);
        var middle = Field.SceneToGrid(_ground.X + 962f, _ground.Y + 585f);
        _focus = new Vector2(middle.x, middle.z);
        _centered = false;
        _appliedAt = Time.unscaledTime;
        Applied = true;
        if (_pictureObject != null) _pictureObject.SetActive(true);
        HideScenery();
        _rescanAt = Time.unscaledTime + 3f;
        Center();
        Aim();
    }

    internal void Restore()
    {
        if (!Applied) return;
        Applied = false;
        foreach (var renderer in _hidden)
            if (renderer != null) renderer.enabled = true;
        _hidden.Clear();
        foreach (var terrain in _terrains)
            if (terrain != null) terrain.enabled = true;
        _terrains.Clear();
        if (_zoomed)
        {
            _zoomed = false;
            Plugin.CfgFieldView.Value = _view;
        }
        bool alive = CombatView.Get() != null;
        if (_eye != null)
        {
            _eye.orthographic = _wasOrtho;
            _eye.transparencySortMode = _wasSort;
            _eye.orthographicSize = _wasSize;
            _eye.clearFlags = _wasClear;
            _eye.backgroundColor = _wasColor;
            _eye.nearClipPlane = _wasNear;
            _eye.farClipPlane = _wasFar;
        }
        if (_control != null)
        {
            _control.enabled = _wasControl;
            _control.Active = true;
            if (alive && _eye != null && ConstraintField?.GetValue(_control) is CameraConstraint rig && rig != null)
            {
                try { _control.SetConstraint(rig); }
                catch (Exception ex) { Plugin.Log.LogWarning("[поле] камера Unity не вернулась на место: " + ex.GetType().Name + " " + ex.Message); }
            }
        }
        if (_pictureObject != null) _pictureObject.SetActive(false);
    }

    private void OnDestroy()
    {
        Camera.onPreCull -= BeforeCull;
        try { Restore(); }
        catch (Exception ex) { Plugin.Log.LogWarning("[поле] возврат вида: " + ex.Message); }
        if (_pictureObject != null) Destroy(_pictureObject);
        if (_skin != null) Destroy(_skin);
        Field.Forget(this);
    }

    private void BeforeCull(Camera camera)
    {
        if (!Applied || _failed || camera == null || camera != _eye || _grid == null) return;
        try { Aim(); }
        catch (Exception ex)
        {
            _failed = true;
            Plugin.Log.LogError("[поле] камера: " + ex);
        }
    }

    private void LateUpdate()
    {
        try { Tick(); }
        catch (Exception ex)
        {
            if (_failed) return;
            _failed = true;
            Plugin.Log.LogError("[поле] " + ex);
        }
    }

    private void Tick()
    {
        if (_grid == null || CombatView.Get() == null)
        {
            Destroy(gameObject);
            return;
        }
        if (!Applied) return;
        if (_eye == null)
        {
            Restore();
            return;
        }
        if (_control != null && _control.enabled) _control.enabled = false;
        if (Time.unscaledTime >= _rescanAt)
        {
            _rescanAt = Time.unscaledTime + 3f;
            HideScenery();
        }
        if (!_warmLogged && Plugin.CfgVerbose.Value && Fighters.AllReady())
        {
            _warmLogged = true;
            Plugin.Log.LogInfo($"[поле] все куклы и их анимации готовы через {Time.unscaledTime - _appliedAt:0.00} с после загрузки карты");
        }
        Center();
        Steer();
        Aim();
    }

    private void HideScenery()
    {
        int renderers = _hidden.Count;
        int terrains = _terrains.Count;
        foreach (var root in Field.Scenery)
        {
            if (root == null) continue;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || renderer.transform.IsChildOf(_grid)) continue;
                renderer.enabled = false;
                _hidden.Add(renderer);
            }
            foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                if (terrain == null || !terrain.enabled) continue;
                terrain.enabled = false;
                _terrains.Add(terrain);
            }
        }
        if (Plugin.CfgVerbose.Value && (_hidden.Count != renderers || _terrains.Count != terrains))
            Plugin.Log.LogInfo($"[поле] спрятано у 3D-карты: объектов {_hidden.Count}, ландшафтов {_terrains.Count}");
    }

    private void Center()
    {
        if (_centered) return;
        var me = Fighters.Combat()?.MyCharacter;
        if (me != null && me.Initialized)
        {
            var local = _grid.InverseTransformPoint(me.position);
            _focus = new Vector2(local.x, local.z);
            _centered = true;
            return;
        }
        if (Time.unscaledTime - _appliedAt > 10f) _centered = true;
    }

    private static float DragPixels => Screen.dpi > 1f ? Screen.dpi / 2.54f * 0.5f : 12f;

    private static System.Reflection.MethodInfo _modOver;
    private static bool _modLooked;

    private static bool OverMod()
    {
        if (!_modLooked)
        {
            _modLooked = true;
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType("NewAgeQoL.WheelGuard", false);
                    if (type == null) continue;
                    _modOver = type.GetMethod("Over", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    break;
                }
            }
            catch { _modOver = null; }
        }
        if (_modOver == null) return false;
        try { return (bool)_modOver.Invoke(null, null); }
        catch { return false; }
    }

    private void Steer()
    {
        bool overUi = Unity3DHelper.IsOverInterface() || OverMod();
        float wheel = Input.mouseScrollDelta.y;
        if (!overUi && Mathf.Abs(wheel) > 0.01f)
        {
            _view = Mathf.Clamp((_view > 0f ? _view : _shownView) * Mathf.Pow(0.88f, wheel), MinView, MaxView);
            _zoomed = true;
        }
        if (!_pressed && !overUi)
        {
            for (int button = 0; button < 3; button++)
            {
                if (!Input.GetMouseButtonDown(button)) continue;
                _pressed = true;
                _dragging = false;
                _button = button;
                _pressAt = Input.mousePosition;
                _lastMouse = _pressAt;
                break;
            }
        }
        if (!_pressed) return;
        if (!Input.GetMouseButton(_button))
        {
            _pressed = false;
            _dragging = false;
            return;
        }
        var mouse = Input.mousePosition;
        if (!_dragging && (mouse - _pressAt).magnitude > DragPixels) _dragging = true;
        if (_dragging)
        {
            var delta = mouse - _lastMouse;
            float unit = _eye.orthographicSize * 2f / Mathf.Max(1, Screen.height);
            _focus.x -= delta.x * unit;
            _focus.y -= delta.y * unit / Field.Lean;
        }
        _lastMouse = mouse;
    }

    private static float Fit(float value, float low, float high) =>
        low > high ? (low + high) * 0.5f : Mathf.Clamp(value, low, high);

    private void Aim()
    {
        float aspect = Mathf.Max(0.1f, _eye.aspect);
        float view = _view > 0f ? _view : MaxView;
        if (_hasBounds)
        {
            float wide = (_xMax - _xMin) * Field.PxPerUnit / aspect;
            float tall = (_zMax - _zMin) * Field.RowPx;
            float fill = Mathf.Max(MinView, Mathf.Min(wide, tall) - 2f);
            float whole = Mathf.Max(fill, Mathf.Max(wide, tall) * 1.15f);
            view = _view > 0f ? Mathf.Clamp(_view, MinView, whole) : fill;
            if (_view > 0f) _view = view;
        }
        _shownView = view;
        float size = view / Field.PxPerUnit * 0.5f;
        _eye.orthographic = true;
        _eye.orthographicSize = size;
        if (_hasBounds)
        {
            float halfX = size * aspect;
            float halfZ = size / Field.Lean;
            _focus.x = Fit(_focus.x, _xMin + halfX, _xMax - halfX);
            _focus.y = Fit(_focus.y, _zMin + halfZ, _zMax - halfZ);
        }
        var body = _eye.transform;
        body.rotation = _grid.rotation * Quaternion.Euler(Field.Pitch, 0f, 0f);
        body.position = _grid.TransformPoint(new Vector3(_focus.x, 0f, _focus.y)) - body.forward * Distance;
        _eye.nearClipPlane = 0.3f;
        _eye.farClipPlane = Distance * 4f;
    }
}
