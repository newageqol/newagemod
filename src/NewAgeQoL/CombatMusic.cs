using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        private const float SongSpan = 240f;
        private const float SwapFade = 4f;
        private const float Fade = 3f;
        private const float StartFade = 2f;
        private const float LossFade = 2.5f;
        private const float WinFade = 0.9f;
        private const float Gap = 0.4f;
        private const float Repeat = 1.5f;
        private const float Hold = 0.3f;
        private const float FellWait = 0.6f;
        private const float LowOn = 0.25f;
        private const float LowOff = 0.3f;
        private const float Tense = 0.6f;
        private const float CrowdTense = 0.4f;
        private const float HurtShare = 0.15f;
        private const int Crowd = 25;
        private const int ManyFallen = 3;
        private const float Muffled = 700f;
        private const float Open = 22000f;
        private const float CutoffSpeed = 2.3f;
        private const float HeartSpeed = 0.7f;
        private const float ButtonSide = CornerRow.Round;

        private static RectTransform _buttonRect;

        internal static bool ButtonShown => _buttonRect != null && _buttonRect.gameObject.activeInHierarchy;
        private const int IconSize = 128;
        private const float LongFrame = 0.05f;
        private const float Watch = 5f;

        private static readonly string[] Shared = { "heart", "crit", "dodge", "hurt", "fell", "fell_many", "win" };
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly List<Song> Songs = new List<Song>();
        private static readonly Dictionary<int, int> Struck = new Dictionary<int, int>();
        private static readonly float[] Level = new float[3];
        private static readonly System.Random Dice = new System.Random();

        private sealed class Song
        {
            public string Name;
            public string[] Paths;
            public AudioClip[] Clips;
            public bool Loading;
            public bool Layered => Paths.Length > 1;
        }

        private sealed class Deck
        {
            public AudioSource[] Layers;
            public AudioLowPassFilter[] Filters;
            public int Song = -1;
            public float Weight;
            public float Want;
        }

        private static readonly Deck[] Decks = new Deck[2];
        private static string _folder;
        private static bool _preparing;
        private static bool _prepared;
        private static GameObject _root;
        private static AudioSource _heart;
        private static AudioSource _accent;
        private static int _deck;
        private static int _ready = -1;
        private static int _last = -1;
        private static float _songAt;

        private static bool _playing;
        private static bool _finished;
        private static bool _muted;
        private static bool _engaged;
        private static bool _low;
        private static bool _ending;
        private static float _endFade;
        private static float _endShare;
        private static float _heartLevel;
        private static float _cutoff = Open;
        private static int _mostTheirs;

        private static string _pending;
        private static int _pendingRank;
        private static float _pendingAt;
        private static float _lastAt = -10f;
        private static int _lastRank;
        private static string _lastName;
        private static int _fallen;
        private static float _fallenAt;
        private static float _sampleAt;
        private static bool _synced;
        private static bool _rescan;
        private static string _event = "nothing";
        private static float _eventAt = -100f;

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
                float now = Time.unscaledTime;
                float frame = Time.unscaledDeltaTime;
                if (frame > LongFrame && (_playing || now - _eventAt < Watch))
                    Plugin.Trace("[music] long frame " + Mathf.RoundToInt(frame * 1000f) + " ms, " + (now - _eventAt).ToString("0.0") + " s after " + _event);
                float volume = Volume;
                float audible = Audible;
                if (_accent != null)
                {
                    _accent.volume = audible;
                    if (audible <= 0.001f && _accent.isPlaying) _accent.Stop();
                }
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
                    Prepare();
                    if (me == null || _finished || !Rivals(cd, me)) return;
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
                    Apply(audible);
                    if (_endShare <= 0f) Stop(true);
                    return;
                }
                if (me == null)
                {
                    End(LossFade);
                    return;
                }
                if (Soon(now)) Want(true);
                if (Due(now)) Swap();
                Steer(cd, me, dt);
                Apply(audible);
                Flush(now, audible);
            }
            catch (Exception e) { Plugin.Trace("[music] " + e.Message); }
        }

        internal static void Shown(AnimationItem item, AbstractCharacter by)
        {
            try
            {
                if (!_playing || _ending || item == null) return;
                var cd = FighterHint.Cd();
                var me = Me(cd);
                if (me == null) return;
                string name = item.AnimationName ?? "";
                var source = item.Source;
                var target = item.Target;
                var fx = item.VisualEffect;
                bool toMe = Same(target, me);
                if (name == AnimationItem.WOUND)
                {
                    if (fx != EAnimationVisualEffect.CRITICAL_HIT) return;
                    if (toMe) Hit("hurt", 4);
                    else if (Same(source, me)) Hit("crit", 5);
                    return;
                }
                if (name == AnimationItem.EVADE)
                {
                    if (toMe && (fx == EAnimationVisualEffect.UVOROT || fx == EAnimationVisualEffect.EVADE || fx == EAnimationVisualEffect.DEFLECT_ARROWS)) Hit("dodge", 3);
                    return;
                }
                if (name == AnimationItem.CHANGE_LIFE_ANIMATION)
                {
                    int value = item is ChangeLifeAnimationItem change ? change.life : item.Value;
                    if (value >= 0) return;
                    _engaged = true;
                    if (toMe)
                    {
                        var indicators = me.Indicators;
                        if (indicators != null && indicators.MaxLife > 0 && -value >= indicators.MaxLife * HurtShare) Hit("hurt", 4);
                    }
                    else if (Same(by, me) && target != null && target.Team != me.Team)
                    {
                        Struck[target.UserId] = cd.RoundNum;
                    }
                    return;
                }
                if (name == "die" && source != null && source.Team != me.Team
                    && Struck.TryGetValue(source.UserId, out int round) && cd.RoundNum - round <= 2)
                {
                    Struck.Remove(source.UserId);
                    _fallen++;
                    _fallenAt = Time.unscaledTime;
                }
            }
            catch (Exception e) { Plugin.Trace("[music] event: " + e.Message); }
        }

        internal static void Over(bool win)
        {
            try
            {
                if (!_playing || _ending) return;
                Plugin.Trace("[music] fight over: " + (win ? "win" : "loss"));
                if (win) Fire("win", Audible);
                End(win ? WinFade : LossFade);
            }
            catch (Exception e) { Plugin.Trace("[music] over: " + e.Message); }
        }

        internal static void Sample()
        {
            float now = Time.unscaledTime;
            if (now < _sampleAt) return;
            _sampleAt = now + 0.25f;
            Prepare();
            if (_playing) return;
            Fire("crit", Volume);
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

        private static bool Same(AbstractCharacter a, AbstractCharacter b) =>
            a != null && b != null && a.UserId == b.UserId;

        private static bool Dead(AbstractCharacter one) =>
            one.Indicators != null && one.Indicators.IsDead;

        private static void Begin()
        {
            if (!_prepared || _ready < 0 || Songs[_ready].Clips == null || !Build()) return;
            foreach (var deck in Decks) Silence(deck);
            _deck = 0;
            for (int i = 0; i < 3; i++) Level[i] = 0f;
            Play(Decks[0], _ready, 1f);
            _heart.volume = 0f;
            _heart.clip = Clips["heart"];
            _heart.PlayScheduled(AudioSettings.dspTime + 0.2);
            _playing = true;
            _ending = false;
            _engaged = false;
            _low = false;
            _heartLevel = 0f;
            _cutoff = Open;
            _mostTheirs = 0;
            _pending = null;
            _fallen = 0;
            _lastRank = 0;
            _lastName = null;
            Struck.Clear();
            Plugin.Trace("[music] fight music started, track " + Songs[_last].Name);
            Mark("start " + Songs[_last].Name);
            Want(false);
        }

        private static void Play(Deck deck, int song, float weight)
        {
            double at = AudioSettings.dspTime + 0.2;
            var clips = Songs[song].Clips;
            for (int i = 0; i < 3; i++)
            {
                var layer = deck.Layers[i];
                layer.Stop();
                layer.clip = i < clips.Length ? clips[i] : null;
                layer.loop = true;
                layer.volume = 0f;
                deck.Filters[i].cutoffFrequency = _cutoff;
                if (layer.clip != null) layer.PlayScheduled(at);
            }
            deck.Song = song;
            deck.Weight = weight;
            deck.Want = 1f;
            _last = song;
            _ready = -1;
            _songAt = Time.unscaledTime;
        }

        private static bool Soon(float now)
        {
            if (Songs.Count < 2 || (_ready >= 0 && (Songs[_ready].Clips != null || Songs[_ready].Loading))) return false;
            var deck = Decks[_deck];
            if (deck == null || deck.Song < 0 || deck.Want <= 0f) return false;
            if (Songs[deck.Song].Layered) return now - _songAt >= SongSpan - 60f;
            var lead = deck.Layers[0];
            return lead.clip != null && lead.isPlaying && lead.clip.length - lead.time <= 60f;
        }

        private static bool Due(float now)
        {
            if (Songs.Count < 2) return false;
            var deck = Decks[_deck];
            if (deck == null || deck.Song < 0 || deck.Want <= 0f) return false;
            if (Songs[deck.Song].Layered) return now - _songAt >= SongSpan;
            var lead = deck.Layers[0];
            return lead.clip != null && lead.isPlaying && lead.clip.length - lead.time <= SwapFade + 0.3f;
        }

        private static void Swap()
        {
            if (_ready < 0 || Songs[_ready].Clips == null)
            {
                Want(true);
                return;
            }
            var old = Decks[_deck];
            var next = Decks[1 - _deck];
            Silence(next);
            old.Want = 0f;
            _deck = 1 - _deck;
            Play(next, _ready, 0f);
            Plugin.Trace("[music] switching to track " + Songs[_last].Name);
            Mark("switch to " + Songs[_last].Name);
            Want(false);
        }

        private static void Blend(float dt)
        {
            foreach (var deck in Decks)
            {
                if (deck == null || deck.Song < 0) continue;
                deck.Weight = Mathf.MoveTowards(deck.Weight, deck.Want, dt / SwapFade);
                if (deck.Want <= 0f && deck.Weight <= 0f) Silence(deck);
            }
        }

        private static void Silence(Deck deck)
        {
            if (deck == null || deck.Layers == null) return;
            int song = deck.Song;
            foreach (var layer in deck.Layers)
            {
                layer.Stop();
                layer.clip = null;
            }
            deck.Song = -1;
            deck.Weight = 0f;
            deck.Want = 0f;
            if (song >= 0 && song != _ready && !InUse(song)) Unload(song);
        }

        private static bool InUse(int song)
        {
            foreach (var deck in Decks)
                if (deck != null && deck.Song == song) return true;
            return false;
        }

        private static void End(float fade)
        {
            _ending = true;
            _endFade = fade;
            _endShare = 1f;
        }

        private static void Stop(bool done)
        {
            foreach (var deck in Decks) Silence(deck);
            if (_heart != null) _heart.Stop();
            _playing = false;
            _ending = false;
            _finished = done && SideButtons.InCombat();
            Plugin.Trace("[music] fight music stopped");
            Mark("stop");
        }

        private static void Mark(string what)
        {
            _event = what;
            _eventAt = Time.unscaledTime;
        }

        private static void Steer(ICombatData cd, PlayerCharacter me, float dt)
        {
            int mine = 0, theirs = 0, total = 0;
            foreach (var one in cd.Characters.Values)
            {
                if (one == null || one.IsFitment()) continue;
                total++;
                if (Dead(one)) continue;
                if (one.Team == me.Team) mine++;
                else theirs++;
            }
            if (theirs > _mostTheirs) _mostTheirs = theirs;
            var indicators = me.Indicators;
            float share = indicators != null && indicators.MaxLife > 0 ? Mathf.Clamp01(indicators.CurrentLife / (float)indicators.MaxLife) : 1f;
            bool dead = Dead(me);
            if (share < LowOn) _low = true;
            else if (share > LowOff) _low = false;
            bool tense = total >= Crowd
                ? share < CrowdTense
                : share < Tense || theirs > mine || (theirs == 1 && _mostTheirs > 1);
            float[] want = { 1f, _engaged && !dead ? 1f : 0f, tense && !dead ? 1f : 0f };
            for (int i = 0; i < 3; i++)
                Level[i] = Mathf.MoveTowards(Level[i], want[i], dt / (i == 0 ? StartFade : Fade));
            bool muffle = _low || dead;
            float cut = muffle ? Muffled : Open;
            _cutoff = Mathf.Exp(Mathf.MoveTowards(Mathf.Log(_cutoff), Mathf.Log(cut), dt * CutoffSpeed));
            _heartLevel = Mathf.MoveTowards(_heartLevel, _low && !dead ? 1f : 0f, dt * HeartSpeed);
        }

        private static void Apply(float volume)
        {
            float share = _ending ? _endShare : 1f;
            foreach (var deck in Decks)
            {
                if (deck == null || deck.Song < 0) continue;
                bool layered = Songs[deck.Song].Layered;
                for (int i = 0; i < 3; i++)
                {
                    float level = layered ? Level[i] : (i == 0 ? Mathf.Max(Level[0], 0f) : 0f);
                    deck.Layers[i].volume = level * volume * share * deck.Weight;
                    deck.Filters[i].cutoffFrequency = _cutoff;
                }
            }
            _heart.volume = _heartLevel * volume * share;
        }

        private static void Hit(string name, int rank)
        {
            float now = Time.unscaledTime;
            if (now - _lastAt < Gap && rank <= _lastRank) { Plugin.Trace("[music] accent " + name + " skipped: " + _lastName + " just played"); return; }
            if (name == _lastName && now - _lastAt < Repeat) { Plugin.Trace("[music] accent " + name + " skipped: same sound less than " + Repeat + " s ago"); return; }
            if (_pending != null && now - _pendingAt <= Hold && rank <= _pendingRank) { Plugin.Trace("[music] accent " + name + " skipped: " + _pending + " is waiting and matters more"); return; }
            _pending = name;
            _pendingRank = rank;
            _pendingAt = now;
            Flush(now, Audible);
        }

        private static void Flush(float now, float volume)
        {
            if (_fallen > 0 && now - _fallenAt >= FellWait)
            {
                string name = _fallen >= ManyFallen ? "fell_many" : "fell";
                _fallen = 0;
                Hit(name, 6);
                return;
            }
            if (_pending == null) return;
            if (now - _pendingAt > Hold)
            {
                Plugin.Trace("[music] accent " + _pending + " dropped: waited too long");
                _pending = null;
                return;
            }
            if (now - _lastAt < Gap) return;
            Fire(_pending, volume);
            _lastAt = now;
            _lastRank = _pendingRank;
            _lastName = _pending;
            _pending = null;
        }

        private static void Fire(string name, float volume)
        {
            if (volume <= 0.001f) { Plugin.Trace("[music] accent " + name + " silent: volume 0 or muted"); return; }
            if (!Clips.TryGetValue(name, out var clip) || clip == null) { Plugin.Trace("[music] accent " + name + " not played: sound not loaded"); return; }
            if (!Build()) { Plugin.Trace("[music] accent " + name + " not played: no audio source"); return; }
            _accent.volume = volume;
            _accent.PlayOneShot(clip, 1f);
            Plugin.Trace("[music] accent " + name + " played, volume " + volume.ToString("0.00"));
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

        private static Color Disc(Vector2 p)
        {
            float r = p.magnitude;
            float body = Cover(r - 0.94f);
            float ring = Mathf.Min(Cover(r - 0.94f), Cover(0.84f - r));
            var fill = new Color(0.08f, 0.07f, 0.06f, 0.82f);
            var color = Color.Lerp(fill, new Color(Gold.r, Gold.g, Gold.b, 0.9f), ring);
            color.a *= body;
            return color;
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
            if (!_prepared) return false;
            _root = new GameObject("QoLCombatMusic");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            for (int d = 0; d < 2; d++)
            {
                var deck = new Deck { Layers = new AudioSource[3], Filters = new AudioLowPassFilter[3] };
                for (int i = 0; i < 3; i++)
                {
                    var go = new GameObject("Deck" + d + "Layer" + (i + 1), typeof(AudioSource), typeof(AudioLowPassFilter));
                    go.transform.SetParent(_root.transform, false);
                    deck.Layers[i] = Source(go, true);
                    deck.Filters[i] = go.GetComponent<AudioLowPassFilter>();
                    deck.Filters[i].cutoffFrequency = Open;
                }
                Decks[d] = deck;
            }
            var heart = new GameObject("Heart", typeof(AudioSource));
            heart.transform.SetParent(_root.transform, false);
            _heart = Source(heart, true);
            var accent = new GameObject("Accent", typeof(AudioSource));
            accent.transform.SetParent(_root.transform, false);
            _accent = Source(accent, false);
            _accent.volume = 1f;
            return true;
        }

        private static AudioSource Source(GameObject go, bool loop)
        {
            var source = go.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.bypassListenerEffects = true;
            source.loop = loop;
            source.volume = 0f;
            return source;
        }

        private static void Prepare()
        {
            if (_rescan && _prepared && !_playing) Forget();
            if (_prepared)
            {
                Want(SideButtons.InCombat());
                return;
            }
            if (_preparing || Plugin.Instance == null) return;
            _preparing = true;
            Plugin.Instance.StartCoroutine(PrepareAll());
        }

        private static void Forget()
        {
            _rescan = false;
            foreach (var deck in Decks) Silence(deck);
            for (int i = 0; i < Songs.Count; i++) Unload(i);
            Songs.Clear();
            if (_heart != null) _heart.clip = null;
            foreach (var clip in Clips.Values) if (clip != null) UnityEngine.Object.Destroy(clip);
            Clips.Clear();
            _ready = -1;
            _last = -1;
            _prepared = false;
            _preparing = false;
            Plugin.Trace("[music] new music files, rereading the list");
        }

        private static IEnumerator PrepareAll()
        {
            _folder = Path.Combine(Path.Combine(Paths.CachePath, "NewAgeQoL"), "music");
            var asm = Assembly.GetExecutingAssembly();
            Songs.Clear();
            bool whole = true;
            var accents = new List<string>();
            try
            {
                foreach (var name in Shared)
                    if (Has(asm, name))
                    {
                        whole &= Unpack(asm, name);
                        accents.Add(name);
                    }
                for (int k = 1; Songs.Count < MaxSongs && Has(asm, "t" + k + "_l1"); k++)
                {
                    var paths = new string[3];
                    for (int l = 1; l <= 3; l++)
                    {
                        whole &= Unpack(asm, "t" + k + "_l" + l);
                        paths[l - 1] = Path.Combine(_folder, "t" + k + "_l" + l + ".ogg");
                    }
                    Songs.Add(new Song { Name = "t" + k, Paths = paths });
                }
                string shelf = Path.Combine(AssetSync.Root, AssetSync.MusicDir);
                if (Directory.Exists(shelf))
                {
                    var files = Directory.GetFiles(shelf, "*.ogg");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (var file in files)
                        if (Songs.Count < MaxSongs) Songs.Add(new Song { Name = Path.GetFileNameWithoutExtension(file), Paths = new[] { file } });
                }
            }
            catch (Exception e)
            {
                whole = false;
                Plugin.Warn("[music] unpack: " + e.Message);
            }
            if (!whole || Songs.Count == 0) yield break;
            foreach (var name in accents)
            {
                AudioClip clip = null;
                yield return Fetch(Path.Combine(_folder, name + ".ogg"), c => clip = c);
                if (clip != null) Clips[name] = clip;
            }
            if (!Clips.ContainsKey("heart")) yield break;
            _prepared = true;
            var titles = new List<string>();
            foreach (var song in Songs) titles.Add(song.Name);
            Plugin.Trace("[music] accents loaded: " + Clips.Count + ", tracks " + string.Join(", ", titles.ToArray()));
            Want(SideButtons.InCombat());
        }

        private static void Want(bool load)
        {
            if (!_prepared || Plugin.Instance == null || Songs.Count == 0) return;
            if (_ready < 0)
            {
                var free = new List<int>();
                for (int i = 0; i < Songs.Count; i++)
                    if (!InUse(i) && (i != _last || Songs.Count == 1)) free.Add(i);
                if (free.Count == 0) return;
                _ready = free[Dice.Next(free.Count)];
            }
            var ready = Songs[_ready];
            if (load && ready.Clips == null && !ready.Loading) Plugin.Instance.StartCoroutine(LoadSong(_ready));
        }

        private static IEnumerator LoadSong(int index)
        {
            var song = Songs[index];
            song.Loading = true;
            Mark("load " + song.Name);
            var clips = new AudioClip[song.Paths.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                int k = i;
                yield return Fetch(song.Paths[i], c => clips[k] = c, !song.Layered);
            }
            song.Loading = false;
            bool whole = true;
            foreach (var clip in clips) whole &= clip != null;
            if (!whole || (_ready != index && !InUse(index)))
            {
                foreach (var clip in clips) if (clip != null) UnityEngine.Object.Destroy(clip);
                if (!whole)
                {
                    Plugin.Warn("[music] track " + song.Name + " did not load");
                    if (_ready == index) _ready = -1;
                }
                yield break;
            }
            song.Clips = clips;
            Plugin.Trace("[music] track " + song.Name + " ready");
        }

        private static void Unload(int index)
        {
            var song = Songs[index];
            var clips = song.Clips;
            song.Clips = null;
            if (clips == null) return;
            foreach (var clip in clips) if (clip != null) UnityEngine.Object.Destroy(clip);
            Mark("unload " + song.Name);
        }

        private static IEnumerator Fetch(string path, Action<AudioClip> done, bool stream = false)
        {
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS))
            {
                var handler = request.downloadHandler as DownloadHandlerAudioClip;
                if (handler != null)
                {
                    handler.streamAudio = stream;
                    handler.compressed = false;
                }
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Warn("[music] " + Path.GetFileName(path) + " did not load: " + request.error);
                    yield break;
                }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip != null) clip.name = "QoL_music_" + Path.GetFileNameWithoutExtension(path);
                if (stream || watch.ElapsedMilliseconds > 20) Plugin.Trace("[music] " + Path.GetFileName(path) + " decoded in " + watch.ElapsedMilliseconds + " ms" + (stream && clip != null ? ", streamed, length " + clip.length.ToString("0") + " s" : ""));
                Mark("decode " + Path.GetFileName(path));
                done(clip);
            }
        }

        private static string Resource(Assembly asm, string name)
        {
            foreach (var r in asm.GetManifestResourceNames())
                if (r.EndsWith(".Music." + name + ".ogg", StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        private static bool Has(Assembly asm, string name) => Resource(asm, name) != null;

        private static bool Unpack(Assembly asm, string name)
        {
            string resource = Resource(asm, name);
            if (resource == null)
            {
                Plugin.Warn("[music] resource " + name + " not found in dll");
                return false;
            }
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, name + ".ogg");
            using (var stream = asm.GetManifestResourceStream(resource))
            {
                if (File.Exists(path) && new FileInfo(path).Length == stream.Length) return true;
                using (var file = File.Create(path)) stream.CopyTo(file);
            }
            return true;
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
