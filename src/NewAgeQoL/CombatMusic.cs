using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class CombatMusic
    {
        private const int MaxSongs = 24;
        private const float Fade = 2.5f;
        private const float LossFade = 2.5f;
        private const float WinFade = 1.5f;
        private const float Lead = 20f;
        private const float LoadWait = 15f;
        private const float ButtonSide = CornerRow.Round;
        private const int IconSize = 128;

        private static RectTransform _buttonRect;

        internal static bool ButtonShown => _buttonRect != null && _buttonRect.gameObject.activeInHierarchy;

        private static readonly List<string> Songs = new List<string>();
        private static readonly System.Random Dice = new System.Random();

        private sealed class Deck
        {
            public AudioSource Source;
            public float Weight;
            public float Want;
            public double At;
        }

        private static readonly Deck[] Decks = new Deck[2];
        private static GameObject _root;
        private static int _deck;
        private static int _last = -1;
        private static AudioClip _next;
        private static int _nextSong = -1;
        private static bool _loading;
        private static float _retryAt;
        private static float _loadingAt;
        private static int _generation;
        private static bool _scanned;

        private static bool _playing;
        private static bool _finished;
        private static bool _muted;
        private static bool _ending;
        private static bool _over;
        private static float _endFade;
        private static float _endShare;
        private static bool _synced;
        private static bool _rescan;

        private static GameObject _buttonCanvas;
        private static Image _note;
        private static GameObject _slash;

        private static float Volume =>
            Plugin.CfgMusicVolume == null ? 0f : Mathf.Clamp01(Plugin.CfgMusicVolume.Value);

        private static float Audible => _muted ? 0f : Volume;

        internal static void Tick()
        {
            try
            {
                float volume = Volume;
                bool fight = SideButtons.InCombat();
                if (!fight)
                {
                    _finished = false;
                    _muted = false;
                }
                if (!_synced && volume > 0.001f && !fight && SideButtons.InWorld() && Plugin.Instance != null)
                {
                    _synced = true;
                    Plugin.Instance.StartCoroutine(AssetSync.Tunes(changed => { if (changed) _rescan = true; }));
                }
                var cd = fight ? FighterHint.Cd() : null;
                var me = Me(cd);
                try { Button(fight && volume > 0.001f && (_playing || _muted) && !_ending); }
                catch (Exception e)
                {
                    Plugin.Trace("[music] button: " + e.Message);
                    if (_buttonCanvas != null) UnityEngine.Object.Destroy(_buttonCanvas);
                    _buttonCanvas = null;
                    _note = null;
                    _slash = null;
                }
                if (!_playing)
                {
                    if (volume <= 0.001f) return;
                    Scan();
                    if (_next == null) Load();
                    if (me == null || _finished || !Rivals(cd, me) || _next == null) return;
                    Begin();
                    return;
                }
                if (volume <= 0.001f)
                {
                    Stop(false);
                    return;
                }
                float dt = Time.unscaledDeltaTime;
                Blend(dt);
                if (_ending)
                {
                    _endShare = Mathf.MoveTowards(_endShare, 0f, dt / _endFade);
                    Apply(Audible);
                    if (_endShare <= 0f) Stop(_over);
                    return;
                }
                if (me == null)
                {
                    End(LossFade, false);
                    return;
                }
                var lead = Decks[_deck];
                double left = lead.Source.clip != null ? lead.Source.clip.length - (AudioSettings.dspTime - lead.At) : 0d;
                if (left <= Lead && _next == null) Load();
                if (left <= Fade + 0.3f && _next != null) Swap();
                Apply(Audible);
            }
            catch (Exception e) { Plugin.Trace("[music] " + e.Message); }
        }

        internal static void Over(bool win)
        {
            try
            {
                if (!_playing || _ending) return;
                Plugin.Trace("[music] fight over: " + (win ? "win" : "loss"));
                End(win ? WinFade : LossFade, true);
            }
            catch (Exception e) { Plugin.Trace("[music] over: " + e.Message); }
        }

        private static PlayerCharacter Me(ICombatData cd)
        {
            var me = cd?.MyCharacter;
            if (me == null || cd.Characters == null) return null;
            return cd.Characters.TryGetValue(me.UserId, out var mine) && ReferenceEquals(mine, me) ? me : null;
        }

        private static bool Rivals(ICombatData cd, PlayerCharacter me)
        {
            foreach (var one in cd.Characters.Values)
                if (one != null && one.UserId > 0 && one.Team != me.Team && !one.IsFitment()) return true;
            return false;
        }

        private static void Begin()
        {
            if (!Build()) return;
            foreach (var deck in Decks) Silence(deck);
            _deck = 0;
            Play(Decks[0]);
            _playing = true;
            _ending = false;
            Plugin.Trace("[music] fight music started, track " + Path.GetFileNameWithoutExtension(Songs[_last]));
        }

        private static void Play(Deck deck)
        {
            deck.Source.clip = _next;
            deck.Source.volume = 0f;
            deck.At = AudioSettings.dspTime + 0.1;
            deck.Source.PlayScheduled(deck.At);
            deck.Weight = 0f;
            deck.Want = 1f;
            _last = _nextSong;
            _next = null;
            _nextSong = -1;
        }

        private static void Swap()
        {
            var old = Decks[_deck];
            _deck = 1 - _deck;
            var deck = Decks[_deck];
            Silence(deck);
            old.Want = 0f;
            Play(deck);
            Plugin.Trace("[music] switching to track " + Path.GetFileNameWithoutExtension(Songs[_last]));
        }

        private static void Blend(float dt)
        {
            foreach (var deck in Decks)
            {
                if (deck == null || deck.Source.clip == null) continue;
                deck.Weight = Mathf.MoveTowards(deck.Weight, deck.Want, dt / Fade);
                if (deck.Want <= 0f && deck.Weight <= 0f) Silence(deck);
            }
        }

        private static void Silence(Deck deck)
        {
            if (deck == null) return;
            var clip = deck.Source.clip;
            deck.Source.Stop();
            deck.Source.clip = null;
            deck.Weight = 0f;
            deck.Want = 0f;
            if (clip != null) UnityEngine.Object.Destroy(clip);
        }

        private static void End(float fade, bool over)
        {
            _ending = true;
            _over = over;
            _endFade = fade;
            _endShare = 1f;
        }

        private static void Stop(bool done)
        {
            foreach (var deck in Decks) Silence(deck);
            _playing = false;
            _ending = false;
            _finished = done && SideButtons.InCombat();
            Plugin.Trace("[music] fight music stopped");
        }

        private static void Apply(float volume)
        {
            float share = _ending ? _endShare : 1f;
            foreach (var deck in Decks)
                if (deck != null && deck.Source.clip != null) deck.Source.volume = volume * share * deck.Weight;
        }

        private static readonly Color Gold = new Color(0.83f, 0.65f, 0.29f, 1f);

        private static void Button(bool show)
        {
            if (!show)
            {
                if (_buttonRect != null && _buttonRect.gameObject.activeSelf) _buttonRect.gameObject.SetActive(false);
                if (_buttonCanvas != null && _buttonCanvas.activeSelf) _buttonCanvas.SetActive(false);
                return;
            }
            if (_buttonCanvas == null)
            {
                _buttonCanvas = new GameObject("QoLMusicButton", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(_buttonCanvas);
                var canvas = _buttonCanvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 500;
                var scaler = _buttonCanvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;

                var body = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
                body.transform.SetParent(_buttonCanvas.transform, false);
                var rect = body.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(1f, 0f);
                _buttonRect = rect;
                rect.sizeDelta = new Vector2(ButtonSide, ButtonSide);
                var face = body.GetComponent<Image>();
                face.color = new Color(0.13f, 0.10f, 0.07f, 0.95f);
                face.sprite = OnlineWindow.RoundedExact(28);
                face.type = Image.Type.Sliced;
                var edge = body.AddComponent<Outline>();
                edge.effectColor = new Color(0.79f, 0.64f, 0.36f);
                edge.effectDistance = new Vector2(1.5f, -1.5f);
                body.GetComponent<Button>().onClick.AddListener(Toggle);

                _note = Layer(body.transform, "Note", Paint(Note));
                _note.color = Gold;
                var slash = Layer(body.transform, "Slash", Paint(Forbid));
                slash.color = new Color(0.88f, 0.2f, 0.17f, 1f);
                _slash = slash.gameObject;
            }
            if (!_buttonCanvas.activeSelf) _buttonCanvas.SetActive(true);
            if (_buttonRect != null && !_buttonRect.gameObject.activeSelf) _buttonRect.gameObject.SetActive(true);
            if (_buttonRect != null)
            {
                var area = ChatDock.Area;
                if (area != null && _buttonRect.parent != area) { _buttonRect.SetParent(area, false); _buttonRect.localScale = Vector3.one; }
                if (area != null) _buttonRect.SetAsLastSibling();
                var spot = CornerRow.Spot(CornerRow.Before(2));
                if (_buttonRect.anchoredPosition != spot) _buttonRect.anchoredPosition = spot;
            }
            _note.color = _muted ? new Color(Gold.r, Gold.g, Gold.b, 0.45f) : Gold;
            if (_slash.activeSelf != _muted) _slash.SetActive(_muted);
        }

        private static Image Layer(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static float Cover(float distance) => Mathf.Clamp01(0.5f - distance * IconSize * 0.5f);

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float half)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude - half;
        }

        private static Color Note(Vector2 p)
        {
            var head = p - new Vector2(-0.13f, -0.30f);
            float angle = 20f * Mathf.Deg2Rad;
            var turned = new Vector2(head.x * Mathf.Cos(angle) + head.y * Mathf.Sin(angle), -head.x * Mathf.Sin(angle) + head.y * Mathf.Cos(angle));
            float oval = (new Vector2(turned.x / 0.21f, turned.y / 0.15f).magnitude - 1f) * 0.15f;
            float stem = Segment(p, new Vector2(0.07f, -0.26f), new Vector2(0.07f, 0.46f), 0.045f);
            float flag = Segment(p, new Vector2(0.07f, 0.44f), new Vector2(0.30f, 0.16f), 0.05f);
            float a = Mathf.Max(Cover(oval), Mathf.Max(Cover(stem), Cover(flag)));
            return new Color(1f, 1f, 1f, a);
        }

        private static Color Forbid(Vector2 p)
        {
            float r = p.magnitude;
            float ring = Mathf.Min(Cover(r - 0.74f), Cover(0.62f - r));
            float bar = Cover(Segment(p, new Vector2(-0.48f, 0.48f), new Vector2(0.48f, -0.48f), 0.06f));
            return new Color(1f, 1f, 1f, Mathf.Max(ring, bar));
        }

        private static Sprite Paint(Func<Vector2, Color> shade)
        {
            var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[IconSize * IconSize];
            for (int y = 0; y < IconSize; y++)
                for (int x = 0; x < IconSize; x++)
                    pixels[y * IconSize + x] = shade(new Vector2((x + 0.5f) / IconSize * 2f - 1f, (y + 0.5f) / IconSize * 2f - 1f));
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void Toggle()
        {
            _muted = !_muted;
            Plugin.Trace("[music] " + (_muted ? "muted" : "unmuted") + " for this fight");
        }

        private static bool Build()
        {
            if (_root != null) return true;
            _root = new GameObject("QoLCombatMusic");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            for (int d = 0; d < 2; d++)
            {
                var go = new GameObject("Deck" + d, typeof(AudioSource));
                go.transform.SetParent(_root.transform, false);
                var source = go.GetComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.bypassListenerEffects = true;
                source.loop = false;
                source.volume = 0f;
                Decks[d] = new Deck { Source = source };
            }
            return true;
        }

        private static void Scan()
        {
            if (_rescan && !_playing)
            {
                _rescan = false;
                _scanned = false;
                _generation++;
                _loading = false;
                if (_next != null) UnityEngine.Object.Destroy(_next);
                _next = null;
                _nextSong = -1;
                _last = -1;
                Plugin.Trace("[music] new music files, rereading the list");
            }
            if (_scanned) return;
            _scanned = true;
            Songs.Clear();
            try
            {
                string old = Path.Combine(Path.Combine(Paths.CachePath, "NewAgeQoL"), "music");
                if (Directory.Exists(old)) Directory.Delete(old, true);
            }
            catch (Exception e) { Plugin.Trace("[music] old cache: " + e.Message); }
            try
            {
                string shelf = Path.Combine(AssetSync.Root, AssetSync.MusicDir);
                if (!Directory.Exists(shelf)) return;
                var files = Directory.GetFiles(shelf, "*.ogg");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                    if (Songs.Count < MaxSongs) Songs.Add(file);
                Plugin.Trace("[music] tracks: " + Songs.Count);
            }
            catch (Exception e) { Plugin.Warn("[music] tracks: " + e.Message); }
        }

        private static void Load()
        {
            if (_loading && Time.unscaledTime - _loadingAt > LoadWait + 5f)
            {
                Plugin.Warn("[music] track load did not answer, trying again");
                _loading = false;
                _generation++;
            }
            if (_loading || _next != null || Songs.Count == 0 || Plugin.Instance == null || Time.unscaledTime < _retryAt) return;
            var free = new List<int>();
            for (int i = 0; i < Songs.Count; i++)
                if (i != _last || Songs.Count == 1) free.Add(i);
            int song = free[Dice.Next(free.Count)];
            _loading = true;
            _loadingAt = Time.unscaledTime;
            Plugin.Trace("[music] loading " + Path.GetFileName(Songs[song]));
            Plugin.Instance.StartCoroutine(Fetch(song, _generation));
        }

        private static IEnumerator Fetch(int song, int generation)
        {
            string path = Songs[song];
            AudioClip clip = null;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS))
            {
                if (request.downloadHandler is DownloadHandlerAudioClip handler)
                {
                    handler.streamAudio = false;
                    handler.compressed = true;
                }
                var sending = request.SendWebRequest();
                float until = Time.unscaledTime + LoadWait;
                while (!sending.isDone && Time.unscaledTime < until) yield return null;
                if (!sending.isDone)
                {
                    request.Abort();
                    Plugin.Warn("[music] " + Path.GetFileName(path) + " did not load in " + LoadWait + " s");
                }
                else if (request.result == UnityWebRequest.Result.Success)
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip == null || clip.length <= 0f)
                    {
                        if (clip != null) UnityEngine.Object.Destroy(clip);
                        clip = null;
                        Plugin.Warn("[music] " + Path.GetFileName(path) + " did not open as sound: the file must hold only an Ogg Vorbis track");
                    }
                }
                else Plugin.Warn("[music] " + Path.GetFileName(path) + " did not load: " + request.error);
            }
            if (generation != _generation)
            {
                if (clip != null) UnityEngine.Object.Destroy(clip);
                yield break;
            }
            _loading = false;
            if (clip == null)
            {
                _retryAt = Time.unscaledTime + 30f;
                yield break;
            }
            clip.name = "QoL_music_" + Path.GetFileNameWithoutExtension(path);
            _next = clip;
            _nextSong = song;
            Plugin.Trace("[music] track " + clip.name + " ready, length " + clip.length.ToString("0") + " s");
        }
    }

    [HarmonyPatch(typeof(GameSettings), nameof(GameSettings.MusicVolume), MethodType.Getter)]
    internal static class GameMusicOffPatch
    {
        private static void Postfix(ref float __result)
        {
            __result = 0f;
        }
    }

    [HarmonyPatch(typeof(SetupDialog), "InitializeDialog")]
    internal static class GameMusicSliderPatch
    {
        private static void Postfix(SetupDialog __instance)
        {
            try
            {
                var slider = Traverse.Create(__instance).Field("MusicSlider").GetValue<Slider>();
                if (slider == null) return;
                slider.SetValueWithoutNotify(0f);
                var top = __instance.transform;
                var box = Climb(slider.transform, top, t => t.GetComponentsInChildren<Slider>(true).Length == 1);
                box.gameObject.SetActive(false);
                var caption = Traverse.Create(__instance).Field("SoundPanelCaption").GetValue<Text>();
                Transform row = null;
                if (caption != null)
                {
                    row = Climb(caption.transform, top, t => !Live(t, caption.transform));
                    row.gameObject.SetActive(false);
                }
                var panel = box.parent;
                if (panel == null) return;
                int from = box.GetSiblingIndex() + 1;
                if (row != null && row.parent == panel) from = Math.Min(from, row.GetSiblingIndex() + 1);
                for (int i = from; i < panel.childCount; i++)
                {
                    var next = panel.GetChild(i);
                    if (!next.gameObject.activeSelf) continue;
                    if (Live(next, null)) break;
                    next.gameObject.SetActive(false);
                }
                while (panel != null && panel != top && !Live(panel, null))
                {
                    panel.gameObject.SetActive(false);
                    panel = panel.parent;
                }
                if (Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                {
                    var parts = new List<string>();
                    var shown = box.parent;
                    if (shown != null)
                        for (int i = 0; i < shown.childCount; i++)
                        {
                            var c = shown.GetChild(i);
                            parts.Add(c.name + (c.gameObject.activeSelf ? "" : " (hidden)"));
                        }
                    Plugin.Trace("[music] game music block hidden: " + box.name + ", caption row " + (row != null ? row.name : "-") + "; panel " + (box.parent != null ? box.parent.name : "-") + ": " + string.Join(", ", parts.ToArray()));
                }
            }
            catch (Exception e) { Plugin.Trace("[music] game music slider: " + e.Message); }
        }

        private static Transform Climb(Transform from, Transform top, Func<Transform, bool> fits)
        {
            var at = from;
            while (at.parent != null && at.parent != top && fits(at.parent)) at = at.parent;
            return at;
        }

        private static bool Live(Transform t, Transform except)
        {
            foreach (var selectable in t.GetComponentsInChildren<Selectable>(false))
                if (selectable.gameObject.activeInHierarchy) return true;
            foreach (var text in t.GetComponentsInChildren<Text>(false))
                if (text.gameObject.activeInHierarchy && text.transform != except && !string.IsNullOrEmpty(text.text)) return true;
            return false;
        }
    }
}
