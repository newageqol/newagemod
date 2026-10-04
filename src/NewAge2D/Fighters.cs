using System.Reflection;
using HarmonyLib;
using Model.Combat.Animation;
using UnityEngine;

namespace NewAge2D;

[HarmonyPatch]
internal static class Fighters
{
    internal const string DollName = "NewAge2D.Fighter";
    internal const string Living = "~";
    internal const float Caption = 30f;
    internal const float CaptionRaise = 14f;
    private const float Lift = 20f;

    private static readonly FieldInfo ContainerField = AccessTools.Field(typeof(AbstractCharacter), "_containerGameObject");
    private static readonly FieldInfo ModelField = AccessTools.Field(typeof(AbstractCharacter), "_characterGameObject");
    private static readonly FieldInfo DistanceField = AccessTools.Field(typeof(CharacterMover), "_distance");
    private static readonly FieldInfo TargetAngleField = AccessTools.Field(typeof(CharacterMover), "_targetAngle");
    internal static readonly FieldInfo CapsuleHeightField = AccessTools.Field(typeof(AbstractCharacter), "_originalCapsuleHeight");
    private static readonly MethodInfo StartMovingMethod = AccessTools.Method(typeof(CharacterMover), "StartMoving");
    private static readonly HashSet<string> SeenClips = new();
    private static readonly List<FighterDoll> Alive = new();

    internal static List<FighterDoll> Dolls => Alive;

    internal static int Count => Alive.Count;

    [HarmonyPostfix, HarmonyPatch(typeof(AbstractCharacter), "SetCharacterGameObject")]
    private static void AfterGameObject(AbstractCharacter __instance)
    {
        if (!Plugin.FlashFight || !Dollable(__instance)) return;
        var character = __instance;
        try { Veil(character); }
        catch (Exception ex) { Plugin.Log.LogError("[combat] hide 3D: " + ex); }
        MainThread.Post(() =>
        {
            try { Attach(character); }
            catch (Exception ex) { Plugin.Log.LogError("[combat] " + ex); }
        });
    }

    internal static string HandOf(AnimationItem item)
    {
        var group = item?.Group;
        if (group == null) return null;
        switch (group.actionType)
        {
            case ActionType.LEFTHANDKICK:
                return "_left";
            case ActionType.RIGHTHANDKICK:
            case ActionType.RIPOSTE:
                return "_right";
            default:
                return null;
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(AbstractCharacter), "PlayDeathAnimation")]
    private static void AfterDeath(AbstractCharacter __instance)
    {
        if (Plugin.FlashFight && !FlashQueue.DeathCall) return;
        var doll = DollOf(__instance);
        if (doll != null) doll.Die();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(AbstractCharacter), "PlayRiseAnimation")]
    private static void AfterRise(AbstractCharacter __instance)
    {
        var doll = DollOf(__instance);
        if (doll != null) doll.Rise();
    }

    private static readonly HashSet<int> Summoned = new();
    private static readonly HashSet<int> SummonRaces = new() { 100, 227, 234, 300, 301, 302, 310, 311, 312 };
    private static bool _summonRacesRead;

    private static string SummonRacesFile => Path.Combine(BepInEx.Paths.CachePath, "NewAge2D", "player-summons.txt");

    private static bool SummonRace(int race)
    {
        if (!_summonRacesRead)
        {
            _summonRacesRead = true;
            try
            {
                if (File.Exists(SummonRacesFile))
                    foreach (string line in File.ReadAllLines(SummonRacesFile))
                        if (int.TryParse(line.Trim(), out int known)) SummonRaces.Add(known);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[summon] summon races: " + ex.Message); }
        }
        return SummonRaces.Contains(race);
    }

    private static void LearnSummonRace(int race)
    {
        if (SummonRace(race) || ClipFor(race) == null) return;
        SummonRaces.Add(race);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SummonRacesFile));
            File.WriteAllLines(SummonRacesFile, SummonRaces.OrderBy(one => one).Select(one => one.ToString()));
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[summon] summon races: " + ex.Message); }
        if (Trace.On) Trace.Write($"summon: race {race} remembered as summoned, such creatures are drawn in Flash also when joining a running fight");
    }
    private static Dictionary<int, string> _clips;

    [HarmonyPrefix, HarmonyPatch(typeof(CombatController), "OnSummonCreatureResponse")]
    private static void BeforeSummon(object msg)
    {
        if (!(msg is Transport.Messages.Responses.Combat.SummonCreatureResponseMessage message) || !message.Bot) return;
        var combat = Combat();
        bool byPlayer = combat != null && combat.Characters.Values.Any(character => character is PlayerCharacter && character.Team == message.Team);
        if (!byPlayer)
        {
            if (Trace.On) Trace.Write($"summon: creature {message.UserId}, race {message.Race}, model {message.AssetBundle}, team {message.Team} - no players in this team, so it is a monster, not a summon: stays 3D");
            return;
        }
        Summoned.Add(message.UserId);
        LearnSummonRace(message.Race);
        if (combat?.MyCharacter != null && combat.MyCharacter.Team == message.Team) SummonWarm.Summoned(message.Race);
        if (Trace.On) Trace.Write($"summon: creature {message.UserId}, race {message.Race}, model {message.AssetBundle}, team {message.Team}, summoned by a player, Flash clip {ClipFor(message.Race) ?? "none, stays 3D"}");
    }

    internal static string ClipFor(int race)
    {
        if (_clips == null)
        {
            _clips = new Dictionary<int, string>();
            try
            {
                string path = Path.Combine(Plugin.Store?.BundleDir ?? "", "clips.txt");
                if (File.Exists(path))
                    foreach (string line in File.ReadAllLines(path))
                    {
                        int space = line.IndexOf(' ');
                        if (space > 0 && int.TryParse(line.Substring(0, space), out int id)) _clips[id] = line.Substring(space + 1).Trim();
                    }
                if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[summon] Flash clips by race: {_clips.Count}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[summon] clip table: " + ex.Message); }
        }
        return _clips.TryGetValue(race, out string clip) ? clip : null;
    }

    private static readonly HashSet<int> FlashRaces = new() { 59 };

    internal static string ClipOf(AbstractCharacter character) =>
        character is BotCharacter && (Summoned.Contains(character.UserId) || SummonRace(character.Race) || FlashRaces.Contains(character.Race))
            ? ClipFor(character.Race) : null;

    internal static bool IsSummoned(AbstractCharacter character) => character != null && Summoned.Contains(character.UserId);

    private static bool Dollable(AbstractCharacter character) =>
        character is PlayerCharacter player ? !string.IsNullOrEmpty(player.Login) : ClipOf(character) != null;

    internal static FighterDoll DollOf(AbstractCharacter character)
    {
        if (character == null) return null;
        foreach (var doll in Alive)
            if (doll != null && ReferenceEquals(doll.Owner, character)) return doll;
        return null;
    }

    internal static string WhoShows(UnityEngine.Object item)
    {
        foreach (var doll in Alive)
            if (doll != null && doll.Shows(item)) return doll.Owner?.Login ?? "?";
        return null;
    }

    internal static void Swap(Sprite old, Sprite fresh)
    {
        foreach (var doll in Alive)
            if (doll != null) doll.Swap(old, fresh);
    }

    private static bool Dolled(CharacterMover mover) =>
        Plugin.FlashFight && mover != null && mover.Owner != null && DollOf(mover.Owner) != null;

    private static readonly FieldInfo MoverField = AccessTools.Field(typeof(AbstractCharacter), "_characterMover");
    private static readonly FieldInfo PathField = AccessTools.Field(typeof(CharacterMover), "_path");
    private static readonly FieldInfo SelectionField = AccessTools.Field(typeof(AbstractCharacter), "_selectionObject");

    [HarmonyPostfix, HarmonyPatch(typeof(AbstractCharacter), "AttachSelection")]
    private static void GroundSelection(AbstractCharacter __instance)
    {
        if (!Plugin.FlashFight || SelectionField == null) return;
        try
        {
            if (!(SelectionField.GetValue(__instance) is GameObject ring) || ring == null) return;
            var layers = SortingLayer.layers;
            int layerId = layers.Length > 0 ? layers[0].id : 0;
            var parts = new List<string>();
            RingMaterials keeper = null;
            foreach (var renderer in ring.GetComponentsInChildren<Renderer>(true))
            {
                var material = renderer.sharedMaterial;
                int queue = material != null ? material.renderQueue : 0;
                parts.Add($"{renderer.GetType().Name} '{renderer.name}' queue {queue} order {renderer.sortingOrder}");
                renderer.sortingLayerID = layerId;
                renderer.sortingOrder = short.MinValue + 2;
                if (queue <= 3000) continue;
                var copy = renderer.material;
                copy.renderQueue = 3000;
                if (keeper == null) keeper = ring.AddComponent<RingMaterials>();
                keeper.Owned.Add(copy);
            }
            var doll = DollOf(__instance);
            int ground = __instance.parent != null ? __instance.parent.gameObject.layer : -1;
            foreach (var projector in ring.GetComponentsInChildren<Projector>(true))
            {
                parts.Add($"Projector '{projector.name}' ignored layers {projector.ignoreLayers}");
                if (doll != null && doll.gameObject.layer != ground) projector.ignoreLayers |= 1 << doll.gameObject.layer;
            }
            if (Trace.On) Trace.Write($"'{NameOf(__instance)}' Unity: selection circle lowered below dolls: {string.Join("; ", parts)}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[combat] selection circle: " + ex.Message); }
    }

    internal static float StepSeconds(AbstractCharacter owner)
    {
        if (owner == null || MoverField == null || DistanceField == null) return 0f;
        if (!(MoverField.GetValue(owner) is CharacterMover mover) || mover == null) return 0f;
        return DistanceField.GetValue(mover) is float distance && distance > 0.05f ? distance / 3f : 0f;
    }

    private static bool Paced(CharacterMover mover) => Plugin.FlashSpeed && Dolled(mover);

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMover), "StartMoving")]
    private static void FlashPace(CharacterMover __instance)
    {
        if (DistanceField == null || __instance.State != MoverState.Walk || !Paced(__instance)) return;
        float seconds = Mathf.Clamp(Plugin.CfgHexSeconds.Value, 0.2f, 5f);
        var owner = __instance.Owner;
        float scale = owner != null && owner.parent != null ? Mathf.Abs(owner.parent.lossyScale.x) : 1f;
        float hex = MathConsts.Sqrt3 * MathConsts.HEX_SIZE * (scale > 0.0001f ? scale : 1f);
        float length = DistanceField.GetValue(__instance) is float measured ? measured : hex;
        float share = Mathf.Clamp(length / hex, 0.05f, 1f);
        DistanceField.SetValue(__instance, 3f * seconds * share);
        if (Trace.On && owner is PlayerCharacter player)
            Trace.Write($"'{player.Login}' Unity: step slowed to {seconds * share:0.00} s per segment ({share:0.00} cell), phase {Combat()?.RoundType}");
    }

    private static string NameOf(AbstractCharacter character) =>
        !string.IsNullOrEmpty(character?.Login) ? character.Login : character?.UserId.ToString() ?? "?";

    private static readonly FieldInfo PathIndexField = AccessTools.Field(typeof(CharacterMover), "_pathIndex");
    private static readonly FieldInfo CurrentTargetField = AccessTools.Field(typeof(CharacterMover), "_currentTarget");
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharacterMover, Queue<OffsetCoord[]>> PendingPaths = new();

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMover), "SetPath")]
    private static bool DeferPath(CharacterMover __instance, OffsetCoord[] path)
    {
        if (!Paced(__instance) || PathField == null || PathIndexField == null || CurrentTargetField == null) return true;
        if (__instance.State == MoverState.Idle || PathField.GetValue(__instance) == null) return true;
        PendingPaths.GetValue(__instance, _ => new Queue<OffsetCoord[]>()).Enqueue(path);
        if (Trace.On) Trace.Write($"'{NameOf(__instance.Owner)}' Unity: new path ({(path?.Length ?? 1) - 1} cells) waits for current move to end");
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMover), "MakeTarget")]
    private static void TakePendingPath(CharacterMover __instance)
    {
        if (PathField == null || PathIndexField == null || CurrentTargetField == null) return;
        if (!PendingPaths.TryGetValue(__instance, out var pending) || pending.Count == 0) return;
        var current = PathField.GetValue(__instance) as OffsetCoord[];
        int index = PathIndexField.GetValue(__instance) is int value ? value : 0;
        if (current != null && index < current.Length) return;
        var next = pending.Dequeue();
        if (next == null || next.Length < 2) return;
        if (current != null && CurrentTargetField.GetValue(__instance) is Vector3 reached && __instance.Owner != null)
            __instance.Owner.position = reached;
        PathField.SetValue(__instance, next);
        PathIndexField.SetValue(__instance, 1);
        if (Trace.On) Trace.Write($"'{NameOf(__instance.Owner)}' Unity: move reached cell, deferred path started ({next.Length - 1} cells)");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMover), "Reset")]
    private static void ForgetPath(CharacterMover __instance)
    {
        if (PendingPaths.TryGetValue(__instance, out var pending)) pending.Clear();
        if (!Plugin.FlashFight || PathField == null || PathField.GetValue(__instance) == null) return;
        PathField.SetValue(__instance, null);
        if (Trace.On) Trace.Write($"'{NameOf(__instance.Owner)}' Unity: old path cleared on move to cell");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AbstractCharacter), "MoveTo")]
    private static bool ShortPath(AbstractCharacter __instance, List<OffsetCoord> path)
    {
        if (!Plugin.FlashFight || (path != null && path.Count >= 2)) return true;
        try
        {
            if (Trace.On) Trace.Write($"'{NameOf(__instance)}' Unity: route not found (points {path?.Count ?? 0}), moved to cell at once");
            __instance.ValidateGameObjectPosition(false);
            DollOf(__instance)?.Jump("route not found");
        }
        catch (Exception ex) { Plugin.Log.LogError("[combat] move without route: " + ex); }
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CombatData), "CharacterTeleported")]
    private static void AfterTeleport(CombatData __instance, int characterId)
    {
        if (!Plugin.FlashFight || !__instance.Characters.TryGetValue(characterId, out var character)) return;
        if (Trace.On) Trace.Write($"'{NameOf(character)}' Unity: teleport to cell {character.HexGridPosition?.clientX},{character.HexGridPosition?.clientY}");
        var doll = DollOf(character);
        if (doll != null && !doll.Hold.HasValue) doll.Jump("teleport");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CombatData), "CharacterMoved")]
    private static void AfterMove(CombatData __instance, int characterId, bool decreaseAp)
    {
        if (!Trace.On || !Plugin.FlashFight || !__instance.Characters.TryGetValue(characterId, out var character)) return;
        Trace.Write($"'{NameOf(character)}' Unity: move to cell {character.HexGridPosition?.clientX},{character.HexGridPosition?.clientY}, phase {__instance.RoundType}, {(decreaseAp ? "for move points" : "without move points")}");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMover), "State", MethodType.Setter)]
    private static void TraceState(CharacterMover __instance, MoverState value)
    {
        if (!Trace.On || !Dolled(__instance) || __instance.State == value) return;
        Trace.Write($"'{NameOf(__instance.Owner)}' Unity: step {__instance.State} → {value}");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(AbstractCharacter), "RotateTo")]
    private static void AfterRotateTo(AbstractCharacter __instance)
    {
        if (!Plugin.FlashFight) return;
        DollOf(__instance)?.GameTurned();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMover), "StartRotating")]
    private static void SnapTurn(CharacterMover __instance)
    {
        if (!Dolled(__instance)) return;
        var state = __instance.State;
        if (state != MoverState.RotateLeft && state != MoverState.RotateRight) return;
        var owner = __instance.Owner;
        if (owner == null || TargetAngleField == null || StartMovingMethod == null) return;
        try
        {
            int angle = (int)TargetAngleField.GetValue(__instance);
            var euler = owner.rotation.eulerAngles;
            euler.y = angle;
            owner.rotation = Quaternion.Euler(euler);
            var animator = owner.CharacterAnimator;
            if (animator != null)
            {
                animator.SetBool("turn_left", false);
                animator.SetBool("turn_right", false);
            }
            StartMovingMethod.Invoke(__instance, null);
        }
        catch (Exception ex) { Plugin.Log.LogError("[combat] turn: " + ex); }
    }

    internal static ICombatData Combat()
    {
        try { return Controllers.User?.CombatData; }
        catch { return null; }
    }

    private static void Attach(AbstractCharacter player)
    {
        if (!player.Initialized) return;
        var combat = Combat();
        if (combat == null || !combat.Characters.TryGetValue(player.UserId, out var known) || !ReferenceEquals(known, player)) return;
        var container = ContainerField.GetValue(player) as GameObject;
        if (container == null) return;

        var child = container.transform.Find(DollName);
        var doll = child != null ? child.GetComponent<FighterDoll>() : null;
        if (doll != null && !ReferenceEquals(doll.Owner, player))
        {
            if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[combat] container '{player.Login}' taken by doll '{doll.Owner?.Login}', removing it");
            if (Trace.On) Trace.Write($"'{player.Login}' container taken by doll '{doll.Owner?.Login}', foreign doll removed");
            child.name = DollName + ".old";
            UnityEngine.Object.Destroy(child.gameObject);
            doll = null;
        }
        if (doll == null)
        {
            var go = new GameObject(DollName);
            go.transform.SetParent(container.transform, false);
            go.layer = container.layer;
            doll = go.AddComponent<FighterDoll>();
            doll.Init(player, container, ClipOf(player));
            Alive.Add(doll);
            if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[combat] doll for '{player.Login}' (id {player.UserId})");
        }
        else if (Trace.On) Trace.Write($"'{player.Login}' model recreated by game, same doll");
        doll.Refresh();
        SpellFx.Prewarm();
        FlashNumbers.Prewarm();
    }

    private static float _emptySince;
    private static bool _crowd;
    internal const int CrowdSize = 25;

    internal static bool Crowd
    {
        get
        {
            if (_crowd) return true;
            var combat = Combat();
            if (combat == null || combat.Characters == null || combat.Characters.Count < CrowdSize) return false;
            _crowd = true;
            if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[combat] big fight: {combat.Characters.Count} fighters, lighter frames and calmer drawing order");
            if (Trace.On) Trace.Write($"big fight: {combat.Characters.Count} fighters, frame scale {Plugin.CombatScale:0.##}, live stance and mirrored looks drawn last, 3D model kept hidden up to 10 s");
            return true;
        }
    }

    internal static void Calm() => _crowd = false;

    internal static void Forget(FighterDoll doll)
    {
        Alive.Remove(doll);
        if (Alive.Count == 0) { _emptySince = Time.unscaledTime; _crowd = false; }
        else Prune();
    }

    private static readonly Dictionary<AbstractCharacter, (List<Renderer> Renderers, float At)> Veils = new();

    private static void Veil(AbstractCharacter player)
    {
        if (Combat() == null || CombatView.Get() == null) return;
        var model = Model(player);
        if (model == null) return;
        var list = Veils.TryGetValue(player, out var known) ? known.Renderers : new List<Renderer>();
        int before = list.Count;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled) continue;
            renderer.enabled = false;
            list.Add(renderer);
        }
        Veils[player] = (list, Time.unscaledTime);
        if (Trace.On) Trace.Write($"'{player.Login}' game created 3D model, renderers hidden {list.Count - before}");
    }

    private static float _sortAt;
    private static readonly Dictionary<AbstractCharacter, (UnityEngine.Object Model, Renderer[] Renderers)> Sorted = new();
    private static readonly HashSet<AbstractCharacter> Traced = new();
    private static readonly Dictionary<Renderer, int> Original = new();

    private static void Unsort()
    {
        foreach (var pair in Original)
            if (pair.Key != null) pair.Key.sortingOrder = pair.Value;
        Original.Clear();
        Sorted.Clear();
        Traced.Clear();
    }

    private static void SortModels()
    {
        if (!Plugin.FlashFight)
        {
            if (Original.Count > 0 || Sorted.Count > 0) Unsort();
            return;
        }
        if (Time.unscaledTime < _sortAt) return;
        _sortAt = Time.unscaledTime + 0.25f;
        var combat = Combat();
        var location = CombatView.Get();
        var eye = location != null ? location.CombatCamera : null;
        if (combat == null || eye == null)
        {
            Unsort();
            return;
        }
        foreach (var character in Sorted.Keys.ToList())
            if (character == null || !combat.Characters.ContainsValue(character)) Sorted.Remove(character);
        foreach (var character in combat.Characters.Values)
        {
            if (character == null || !character.Initialized || DollOf(character) != null) continue;
            var model = Model(character);
            if (model == null) continue;
            if (!Sorted.TryGetValue(character, out var known) || !ReferenceEquals(known.Model, model))
            {
                known = (model, model.GetComponentsInChildren<Renderer>(true));
                Sorted[character] = known;
            }
            var ring = SelectionField?.GetValue(character) as GameObject;
            int order = Layer(eye, character.position, character.HexGridPosition);
            int changed = 0;
            foreach (var renderer in known.Renderers)
            {
                if (renderer == null || renderer.sortingOrder == order) continue;
                if (ring != null && renderer.transform.IsChildOf(ring.transform)) continue;
                if (!Original.ContainsKey(renderer)) Original[renderer] = renderer.sortingOrder;
                renderer.sortingOrder = order;
                changed++;
            }
            if (changed > 0 && Trace.On && Traced.Add(character))
                Trace.Write($"'{NameOf(character)}' 3D model without doll: {changed} renderers get depth order {order}, as dolls");
        }
    }

    internal static List<Renderer> TakeVeil(AbstractCharacter character)
    {
        if (character == null || !Veils.TryGetValue(character, out var veil)) return null;
        Veils.Remove(character);
        return veil.Renderers;
    }

    private static void Unveil(AbstractCharacter player)
    {
        var list = TakeVeil(player);
        if (list == null) return;
        foreach (var renderer in list)
            if (renderer != null) renderer.enabled = true;
    }

    internal static bool AllReady()
    {
        var combat = Combat();
        if (combat == null) return false;
        int seen = 0;
        foreach (var character in combat.Characters.Values)
        {
            if (character == null || !character.Initialized) continue;
            seen++;
            if (!Plugin.FlashFight || !(character is PlayerCharacter player) || string.IsNullOrEmpty(player.Login)) continue;
            var doll = DollOf(player);
            if (doll == null || !doll.Warm) return false;
        }
        return seen > 0;
    }

    internal static void Tick()
    {
        TopBars.Tick();
        FlashQueue.Tick();
        FlashTeleport.Tick();
        FlashNumbers.Tick();
        SpellMarks.Tick();
        SummonWarm.Tick();
        SortModels();
        WatchClash();
        if (Veils.Count > 0)
        {
            foreach (var player in Veils.Keys.ToList())
            {
                if (player == null)
                {
                    Veils.Remove(player);
                    continue;
                }
                if (Plugin.FlashFight && (DollOf(player) != null || Time.unscaledTime - Veils[player].At < (Crowd ? 10f : 3f))) continue;
                Unveil(player);
                if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[combat] '{player.Login}': no doll, 3D model restored");
                if (Trace.On) Trace.Write($"'{player.Login}' 3D model RESTORED: doll did not appear within {(Crowd ? 10 : 3)} s");
            }
        }
        if (Alive.Count > 0 || _emptySince <= 0f || Time.unscaledTime - _emptySince < 180f) return;
        _emptySince = 0f;
        FrameCache.Clear();
        SpellFx.Reset();
        Summoned.Clear();
        if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo("[frames] no fights for a long time, doll frames unloaded");
    }

    internal static int Layer(Camera eye, Vector3 point, OffsetCoord spot)
    {
        if (eye == null) return 0;
        float depth = -Vector3.Dot(point - eye.transform.position, eye.transform.forward);
        int step = Mathf.Clamp(Mathf.RoundToInt(depth * 20f), -1800, 1800);
        return step * 16 + Side(spot) * 4;
    }

    private static int Side(OffsetCoord spot)
    {
        if (spot == null) return 0;
        int side = spot.clientX % 4;
        return side < 0 ? side + 4 : side;
    }

    private static float _clashAt;

    private static void WatchClash()
    {
        if (!Trace.On || Alive.Count < 2 || Time.unscaledTime < _clashAt) return;
        _clashAt = Time.unscaledTime + 5f;
        for (int i = 0; i < Alive.Count; i++)
            for (int j = i + 1; j < Alive.Count; j++)
            {
                var one = Alive[i];
                var two = Alive[j];
                if (one == null || two == null) continue;
                int first = one.SortingOrder;
                int second = two.SortingOrder;
                if (Mathf.Abs(first - second) >= 16) continue;
                if (first == second)
                {
                    Trace.Write($"'{one.Owner?.Login}' and '{two.Owner?.Login}' share draw order {first} - Unity decides which is on top");
                    continue;
                }
                var top = first > second ? one : two;
                var under = first > second ? two : one;
                Trace.Write($"'{top.Owner?.Login}' over '{under.Owner?.Login}': same depth, order {first} and {second}");
            }
    }

    internal static void TowardEye(Camera eye, Vector3 point, float weight, out Vector3 shift, out float near)
    {
        shift = Vector3.zero;
        near = 1f;
        float bias = Mathf.Clamp(Plugin.CfgDepthBias.Value, 0f, 3f) * Mathf.Clamp01(weight);
        if (bias <= 0f) return;
        if (eye.orthographic)
        {
            float depth = Vector3.Dot(point - eye.transform.position, eye.transform.forward);
            float lift = Field.Active ? Mathf.Clamp(depth - eye.nearClipPlane - 1f, bias, Lift) * Mathf.Clamp01(weight) : bias;
            shift = -eye.transform.forward * Mathf.Max(bias, lift);
            return;
        }
        var ray = point - eye.transform.position;
        float distance = ray.magnitude;
        if (distance < bias + 0.5f) return;
        shift = ray * (-bias / distance);
        near = (distance - bias) / distance;
    }

    internal static void HotSequences(HashSet<string> hot)
    {
        foreach (var doll in Alive)
            if (doll != null) doll.AddHot(hot);
        foreach (string look in SummonWarm.Kept)
        {
            hot.Add(FrameCache.SequenceKey(look, "stop"));
            hot.Add(FrameCache.SequenceKey(look, "move"));
        }
    }

    internal static void Prune()
    {
        var used = new HashSet<string>();
        foreach (var doll in Alive)
        {
            if (doll == null) continue;
            if (doll.Look != null) used.Add(doll.Look);
            if (doll.ViewLook != null) used.Add(doll.ViewLook);
            if (doll.OtherLook != null) used.Add(doll.OtherLook);
            if (!doll.Warm)
                foreach (string look in doll.OlderLooks) used.Add(look);
        }
        foreach (string look in SummonWarm.Kept) used.Add(look);
        FrameCache.DropUnused(used);
    }

    internal static void ClearAll()
    {
        foreach (var doll in Alive.ToArray())
            if (doll != null) UnityEngine.Object.Destroy(doll.gameObject);
        Alive.Clear();
        foreach (var player in Veils.Keys.ToList()) Unveil(player);
        Unsort();
        FrameCache.Clear();
    }

    internal static void Set(bool on)
    {
        if (!on)
        {
            ClearAll();
            return;
        }
        var combat = Combat();
        if (combat == null) return;
        foreach (var character in combat.Characters.Values.ToArray())
        {
            if (character == null || !character.Initialized || !Dollable(character)) continue;
            try { Attach(character); }
            catch (Exception ex) { Plugin.Log.LogError("[combat] " + ex); }
        }
    }

    internal static GameObject Model(AbstractCharacter character) => ModelField.GetValue(character) as GameObject;

    internal static string LabelFor(string clip, AbstractCharacter owner, string hand, out bool loop, out int rank)
    {
        string name = (clip ?? "").ToLowerInvariant();
        loop = false;
        string label;
        if (name.StartsWith("death") || name.StartsWith("die") || name.StartsWith("dead")) { label = "die"; rank = 5; }
        else if (name.StartsWith("heal")) { label = "healing"; rank = 4; }
        else if (name.StartsWith("cast") || name.StartsWith("magic")) { label = "cast"; rank = 4; }
        else if (name.StartsWith("attack")) { label = owner is PlayerCharacter player ? WeaponLabel(player, hand, name) : "fight" + (hand ?? (name == "attack8" ? "_left" : "_right")); rank = 4; }
        else if (name.StartsWith("run") || name.StartsWith("walk") || name.StartsWith("move")) { loop = true; label = "move"; rank = 2; }
        else { loop = true; label = "stop"; rank = name.Length == 0 || name.StartsWith("idle") ? 0 : 1; }

        if (Plugin.CfgVerbose.Value && name.Length > 0 && SeenClips.Add(name))
            Plugin.Log.LogInfo($"[combat] animator clip '{clip}' → label {label}");
        return label;
    }

    internal static bool BothHands(PlayerCharacter owner)
    {
        var right = owner.RightHandWeapon;
        var left = owner.LeftHandWeapon;
        return right != null && right.ThingSubType != EThingSubType.SHIELD
            && left != null && left.ThingSubType != EThingSubType.SHIELD;
    }

    internal static string WeaponLabel(PlayerCharacter owner, string hand) => WeaponLabel(owner, hand, null);

    internal static string WeaponLabel(PlayerCharacter owner, string hand, string clip)
    {
        var weapon = WeaponOf(owner, hand, out string side);
        return Family(weapon, clip) + side;
    }

    internal static Weapon WeaponOf(PlayerCharacter owner, string hand, out string side)
    {
        side = hand;
        if (side == "_left") return owner.LeftHandWeapon;
        if (side == "_right") return owner.RightHandWeapon;
        side = "_right";
        var weapon = owner.RightHandWeapon;
        if (weapon == null || weapon.ThingSubType == EThingSubType.SHIELD)
        {
            var left = owner.LeftHandWeapon;
            if (left != null && left.ThingSubType != EThingSubType.SHIELD)
            {
                weapon = left;
                side = "_left";
            }
        }
        return weapon;
    }

    private static string Family(Weapon weapon, string clip)
    {
        if (clip == "attack7") return "bow";
        if (clip == "attack10") return "spear";
        if (weapon == null) return "fight";
        switch (weapon.ThingSubType)
        {
            case EThingSubType.BOW:
                return "bow";
            case EThingSubType.SPEAR_TWO_HAND_SPLIT:
            case EThingSubType.SPEAR_TWO_HAND_CUT:
                return "spear";
            case EThingSubType.DAGGER:
            case EThingSubType.KNUCKLEDUSTER:
                return "kinjal";
            case EThingSubType.AXE_TWO_HAND:
            case EThingSubType.SWORD_TWO_HAND:
            case EThingSubType.HAMMER_TWO_HAND:
            case EThingSubType.STAFF:
                return "axe";
            case EThingSubType.SHIELD:
                return "kinjal";
            default:
                return "sword";
        }
    }

    internal static string Mirror(string label)
    {
        if (label == null) return null;
        if (label.EndsWith("_left", StringComparison.Ordinal)) return label.Substring(0, label.Length - 5) + "_right";
        if (label.EndsWith("_right", StringComparison.Ordinal)) return label.Substring(0, label.Length - 6) + "_left";
        return label;
    }

    internal static bool FacesLeft(string look) => look != null && look.Contains("/L|");

    internal static string Describe(Weapon weapon) => weapon == null ? "-" : weapon.ThingSubType.ToString();

    private static Sprite _shade;

    internal static Sprite ShadeSprite
    {
        get
        {
            if (_shade != null) return _shade;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(1f - d);
                    float alpha = t * t * (3f - 2f * t);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _shade = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
            return _shade;
        }
    }
}

internal static class FrameCache
{
    private const int Cap = 30000;

    private static readonly Dictionary<Sprite, Silhouette> Shapes = new();

    internal static Silhouette ShapeOf(Sprite sprite) => sprite != null && Shapes.TryGetValue(sprite, out var shape) ? shape : null;

    private sealed class Batch
    {
        public string Look;
        public string Label;
        public string Sequence;
        public List<DollPicture> Frames;
        public float Ppu;
        public Action Done;
        public int Next;
        public int Generation;
        public int[] Map;
        public long Queued;
        public long Began;
    }

    private static readonly Queue<Batch> Batches = new();
    private static readonly Queue<Batch> Later = new();

    private static readonly Dictionary<string, Sprite> Sprites = new();
    private static readonly Dictionary<string, string> Owner = new();
    private static readonly LinkedList<string> Order = new();
    private static readonly Dictionary<string, LinkedListNode<string>> Nodes = new();
    private static readonly HashSet<string> Ready = new();
    private static readonly HashSet<string> Pending = new();
    private static readonly Dictionary<string, float> Stops = new();
    private static readonly Dictionary<string, float> Heads = new();
    private static readonly Dictionary<string, float> Hits = new();
    private static readonly Dictionary<string, Dictionary<string, LabelRange>> Labels = new();
    private static readonly Dictionary<string, double> Rates = new();
    private static readonly Dictionary<string, int> Counts = new();
    private static readonly Dictionary<string, int[]> Maps = new();
    private static readonly Dictionary<string, List<string>> Keys = new();
    private static readonly Dictionary<string, long> Used = new();
    private static long _use;
    private static readonly List<UnityEngine.Object> Doomed = new();

    internal static int Generation { get; private set; }

    internal static bool Busy => Batches.Count > 0 || Later.Count > 0;

    private const int KeyCap = 32768;
    private static readonly Dictionary<(string, string), string> SequenceKeys = new();
    private static readonly Dictionary<(string, string, int), string> FrameKeys = new();

    internal static string SequenceKey(string look, string label)
    {
        if (look == null || label == null) return look + "|" + label;
        lock (SequenceKeys)
        {
            if (SequenceKeys.TryGetValue((look, label), out string key)) return key;
            if (SequenceKeys.Count >= KeyCap) SequenceKeys.Clear();
            key = look + "|" + label;
            SequenceKeys[(look, label)] = key;
            return key;
        }
    }

    internal static string FrameKey(string look, string label, int frame)
    {
        if (look == null || label == null) return look + "#" + label + ":" + frame;
        lock (FrameKeys)
        {
            if (FrameKeys.TryGetValue((look, label, frame), out string key)) return key;
            if (FrameKeys.Count >= KeyCap) FrameKeys.Clear();
            key = look + "#" + label + ":" + frame;
            FrameKeys[(look, label, frame)] = key;
            return key;
        }
    }

    internal static bool HasSequence(string sequence) => Ready.Contains(sequence);

    internal static bool IsPending(string sequence) => Pending.Contains(sequence);

    internal static string StateOf(string sequence) => Ready.Contains(sequence) ? "ready" : Pending.Contains(sequence) ? "drawing" : "not ordered";

    internal static bool BeginSequence(string sequence) => !Ready.Contains(sequence) && Pending.Add(sequence);

    internal static void EndSequence(string sequence, bool ready)
    {
        Pending.Remove(sequence);
        if (ready) Ready.Add(sequence);
    }

    internal static void SetCount(string sequence, int count) => Counts[sequence] = count;

    internal static int CountOf(string sequence) => Counts.TryGetValue(sequence, out int count) ? count : 0;

    internal static int Source(string sequence, int frame) =>
        Maps.TryGetValue(sequence, out var map) && frame >= 0 && frame < map.Length ? map[frame] : frame;

    private static int[] BuildMap(List<DollPicture> frames)
    {
        var map = new int[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            map[i] = i;
            for (int j = 0; j < i; j++)
                if (ReferenceEquals(frames[i], frames[j])) { map[i] = map[j]; break; }
        }
        return map;
    }

    internal static Dictionary<string, LabelRange> LabelsFor(string look) =>
        look != null && Labels.TryGetValue(look, out var known) ? known : null;

    internal static double RateFor(string look) => Rates.TryGetValue(look, out double rate) && rate > 1 ? rate : 20.0;

    internal static void Remember(string look, Dictionary<string, LabelRange> labels, double rate)
    {
        if (labels != null && !Labels.ContainsKey(look)) Labels[look] = labels;
        if (rate > 1 && !Rates.ContainsKey(look)) Rates[look] = rate;
    }

    internal static void SetStop(string look, float pixels)
    {
        if (look != null && pixels > 0f) Stops[look] = pixels;
    }

    internal static float StopPixels(string look) => look != null && Stops.TryGetValue(look, out float pixels) ? pixels : 0f;

    internal static void SetHead(string look, float pixels)
    {
        if (look != null && !float.IsNaN(pixels)) Heads[look] = pixels;
    }

    internal static float HeadPixels(string look) => look != null && Heads.TryGetValue(look, out float pixels) ? pixels : 0f;

    internal static void SetHit(string sequence, float share)
    {
        if (sequence != null && !float.IsNaN(share)) Hits[sequence] = Mathf.Clamp(share, 0.3f, 0.6f);
    }

    internal static float HitShare(string sequence) => sequence != null && Hits.TryGetValue(sequence, out float share) ? share : 0.55f;

    internal static void Store(string look, string label, string sequence, List<DollPicture> frames, float ppu, Action done, bool urgent = true)
    {
        (urgent ? Batches : Later).Enqueue(new Batch
        {
            Look = look, Label = label, Sequence = sequence, Frames = frames, Ppu = ppu, Done = done,
            Generation = Generation, Queued = System.Diagnostics.Stopwatch.GetTimestamp(),
        });
    }

    internal static bool Hurry(string sequence)
    {
        if (Later.Count == 0) return false;
        Batch found = null;
        foreach (var batch in Later)
        {
            if (batch.Sequence != sequence) continue;
            found = batch;
            break;
        }
        if (found == null) return false;
        var rest = Later.Where(batch => !ReferenceEquals(batch, found)).ToList();
        Later.Clear();
        foreach (var batch in rest) Later.Enqueue(batch);
        Batches.Enqueue(found);
        return true;
    }

    private static int Remaining()
    {
        int left = 0;
        foreach (var batch in Batches) left += batch.Frames.Count - batch.Next;
        foreach (var batch in Later) left += batch.Frames.Count - batch.Next;
        return left;
    }

    private static readonly Dictionary<string, long> Sizes = new();
    private const float MipBias = -0.5f;

    private static long _bytes;

    private static float _spilledAt;

    internal static string Stats() => $"urgent {Batches.Count}, later {Later.Count}, frames left {Remaining()}, sprites {Sprites.Count}, video memory {_bytes / 1048576.0:0} MB";

    private static long Ms(long from, long to) => (to - from) * 1000 / System.Diagnostics.Stopwatch.Frequency;

    private static void Bury()
    {
        if (Doomed.Count == 0) return;
        foreach (var item in Doomed)
        {
            if (item == null) continue;
            if (Trace.On && item is Sprite)
            {
                string who = Fighters.WhoShows(item);
                if (who != null) Trace.Write($"'{who}' destroying a sprite that is on screen now");
            }
            UnityEngine.Object.Destroy(item);
        }
        Doomed.Clear();
    }

    internal static void Tick()
    {
        Bury();
        if (Batches.Count == 0 && Later.Count == 0) return;
        long budget = Remaining() > 300 ? 24 : 12;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < budget)
        {
            var queue = Batches.Count > 0 ? Batches : Later.Count > 0 ? Later : null;
            if (queue == null) return;
            var batch = queue.Peek();
            if (batch.Generation != Generation)
            {
                queue.Dequeue();
                continue;
            }
            if (batch.Map == null)
            {
                batch.Map = BuildMap(batch.Frames);
                Maps[batch.Sequence] = batch.Map;
            }
            if (batch.Began == 0) batch.Began = System.Diagnostics.Stopwatch.GetTimestamp();
            while (batch.Next < batch.Frames.Count && clock.ElapsedMilliseconds < budget)
            {
                if (batch.Map[batch.Next] == batch.Next)
                    Put(FrameKey(batch.Look, batch.Label, batch.Next), batch.Sequence, batch.Frames[batch.Next], batch.Ppu);
                batch.Next++;
            }
            if (batch.Next < batch.Frames.Count) return;
            queue.Dequeue();
            SetCount(batch.Sequence, batch.Frames.Count);
            EndSequence(batch.Sequence, true);
            if (Trace.On)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                Trace.Write($"uploaded {batch.Label} look {Trace.Look(batch.Look)}: frames {batch.Frames.Count}, waited for upload {Ms(batch.Queued, batch.Began)} ms, upload {Ms(batch.Began, now)} ms, queue {(queue == Batches ? "urgent" : "late")}");
            }
            try { batch.Done?.Invoke(); }
            catch (Exception ex) { Plugin.Log.LogError("[frames] " + ex); }
        }
    }

    internal static void DropUnused(HashSet<string> used)
    {
        var dead = new HashSet<string>();
        foreach (string look in Labels.Keys)
            if (!look.StartsWith("fx/", StringComparison.Ordinal) && !used.Contains(look)) dead.Add(look);
        foreach (string sequence in Owner.Values)
        {
            int cut = sequence.LastIndexOf('|');
            if (cut <= 0) continue;
            string look = sequence.Substring(0, cut);
            if (!look.StartsWith("fx/", StringComparison.Ordinal) && !used.Contains(look)) dead.Add(look);
        }
        if (dead.Count == 0) return;
        foreach (string key in Sprites.Keys.ToList())
        {
            int cut = key.IndexOf('#');
            if (cut > 0 && dead.Contains(key.Substring(0, cut))) Drop(key);
        }
        foreach (string look in dead)
        {
            Labels.Remove(look);
            Rates.Remove(look);
            Stops.Remove(look);
            Heads.Remove(look);
            string prefix = look + "|";
            Ready.RemoveWhere(s => s.StartsWith(prefix, StringComparison.Ordinal));
            foreach (string sequence in Counts.Keys.Where(s => s.StartsWith(prefix, StringComparison.Ordinal)).ToList()) Counts.Remove(sequence);
            foreach (string sequence in Hits.Keys.Where(s => s.StartsWith(prefix, StringComparison.Ordinal)).ToList()) Hits.Remove(sequence);
            foreach (string sequence in Maps.Keys.Where(s => s.StartsWith(prefix, StringComparison.Ordinal)).ToList()) Maps.Remove(sequence);
        }
        if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[frames] looks freed: {string.Join(", ", dead)}; sprites left {Sprites.Count}");
        if (Trace.On) Trace.Write($"looks unloaded from memory {string.Join(", ", dead.Select(Trace.Look))}; sprites left {Sprites.Count}");
    }

    internal static bool TryGet(string key, out Sprite sprite)
    {
        if (!Sprites.TryGetValue(key, out sprite)) return false;
        if (sprite == null)
        {
            Drop(key);
            return false;
        }
        var node = Nodes[key];
        Order.Remove(node);
        Order.AddLast(node);
        if (Owner.TryGetValue(key, out string sequence)) Used[sequence] = ++_use;
        return true;
    }

    internal static Sprite Put(string key, string sequence, DollPicture picture, float pixelsPerUnit)
    {
        bool packed = picture.Dxt != null;
        bool raw = !packed && picture.Raw;
        bool mips = packed ? picture.Mips > 1 : raw;
        var texture = new Texture2D(picture.Width, picture.Height, packed ? TextureFormat.DXT5 : TextureFormat.RGBA32, mips)
        {
            filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        if (mips) texture.mipMapBias = MipBias;
        if (packed) Upload(texture, picture);
        else texture.SetPixelData(picture.Rgba, 0);
        if (!packed && !raw && Plugin.CfgCompress.Value && picture.Width % 4 == 0 && picture.Height % 4 == 0) texture.Compress(false);
        texture.Apply(raw, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, picture.Width, picture.Height),
            new Vector2(picture.PivotX, picture.PivotY), pixelsPerUnit, 0, SpriteMeshType.FullRect);
        var shape = Silhouette.From(picture);
        if (shape != null) Shapes[sprite] = shape;
        Sprites.TryGetValue(key, out var previous);
        Drop(key, Owner.TryGetValue(key, out string was) && was == sequence);
        Sprites[key] = sprite;
        long size = packed ? (picture.DxtSize > 0 ? picture.DxtSize : picture.Dxt.Length) : picture.Rgba.Length + (raw ? picture.Rgba.Length / 3 : 0);
        if (packed && picture.Pooled)
        {
            Pixels.Return(picture.Dxt);
            picture.Dxt = null;
        }
        Sizes[key] = size;
        _bytes += size;
        Owner[key] = sequence;
        Nodes[key] = Order.AddLast(key);
        if (!Keys.TryGetValue(sequence, out var kin)) Keys[sequence] = kin = new List<string>();
        kin.Add(key);
        Used[sequence] = ++_use;
        if (previous != null) Fighters.Swap(previous, sprite);
        int evicted = 0;
        while (Order.Count > Cap)
        {
            Drop(Order.First.Value);
            evicted++;
        }
        long budget = Plugin.FrameMemory;
        if (_bytes > budget) Spill(budget);
        if (evicted > 0 && Trace.On) Trace.Write($"cache limit {Cap}: frames evicted {evicted}");
        return sprite;
    }

    private static readonly HashSet<string> Hot = new();

    private static void Spill(long budget)
    {
        long room = budget - budget / 10;
        Hot.Clear();
        Fighters.HotSequences(Hot);
        foreach (var batch in Batches) Hot.Add(batch.Sequence);
        foreach (var batch in Later) Hot.Add(batch.Sequence);
        int spilled = 0;
        int series = 0;
        int forced = 0;
        while (_bytes > room)
        {
            string victim = Victim(skipHot: true) ?? Victim(skipHot: false);
            if (victim == null) break;
            bool needed = Hot.Contains(victim);
            int dropped = DropSequence(victim);
            if (dropped == 0) break;
            if (needed) forced++;
            spilled += dropped;
            series++;
        }
        while (_bytes > room && Order.Count > 1)
        {
            Drop(Order.First.Value);
            spilled++;
        }
        if (spilled > 0 && Trace.On && Time.unscaledTime - _spilledAt > 1f)
        {
            _spilledAt = Time.unscaledTime;
            Trace.Write($"video memory limit {budget / 1048576L} MB: sets evicted {series} (of them needed now {forced}), frames {spilled}, left {_bytes / 1048576.0:0} MB");
        }
    }

    private static string Victim(bool skipHot)
    {
        string victim = null;
        long oldest = long.MaxValue;
        foreach (var pair in Used)
        {
            if (pair.Value >= oldest) continue;
            if (skipHot && (Hot.Contains(pair.Key) || pair.Key.StartsWith("fx/", StringComparison.Ordinal))) continue;
            if (!Keys.TryGetValue(pair.Key, out var kin) || kin.Count == 0) continue;
            oldest = pair.Value;
            victim = pair.Key;
        }
        return victim;
    }

    private static int DropSequence(string sequence)
    {
        if (!Keys.TryGetValue(sequence, out var kin)) return 0;
        int dropped = kin.Count;
        foreach (string key in kin.ToArray()) Drop(key);
        Keys.Remove(sequence);
        Used.Remove(sequence);
        Ready.Remove(sequence);
        Maps.Remove(sequence);
        Counts.Remove(sequence);
        return dropped;
    }

    private static void Upload(Texture2D texture, DollPicture picture)
    {
        if (!picture.Pooled || picture.DxtSize <= 0)
        {
            texture.LoadRawTextureData(picture.Dxt);
            return;
        }
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(picture.Dxt, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { texture.LoadRawTextureData(pin.AddrOfPinnedObject(), picture.DxtSize); }
        finally { pin.Free(); }
    }

    private static void Drop(string key, bool keepSequence = false)
    {
        if (Sizes.TryGetValue(key, out long size))
        {
            _bytes -= size;
            Sizes.Remove(key);
        }
        if (Sprites.TryGetValue(key, out var old))
        {
            if (old != null)
            {
                if (old.texture != null) Doomed.Add(old.texture);
                Doomed.Add(old);
                Shapes.Remove(old);
            }
            Sprites.Remove(key);
        }
        if (Owner.TryGetValue(key, out string sequence))
        {
            if (!keepSequence)
            {
                Ready.Remove(sequence);
                Maps.Remove(sequence);
            }
            if (Keys.TryGetValue(sequence, out var kin))
            {
                kin.Remove(key);
                if (kin.Count == 0)
                {
                    Keys.Remove(sequence);
                    Used.Remove(sequence);
                }
            }
            Owner.Remove(key);
        }
        if (Nodes.TryGetValue(key, out var node))
        {
            Order.Remove(node);
            Nodes.Remove(key);
        }
    }

    internal static void Clear()
    {
        Batches.Clear();
        Later.Clear();
        foreach (string key in Sprites.Keys.ToList()) Drop(key);
        Owner.Clear();
        Ready.Clear();
        Pending.Clear();
        Stops.Clear();
        Heads.Clear();
        Hits.Clear();
        Labels.Clear();
        Rates.Clear();
        Counts.Clear();
        Maps.Clear();
        Keys.Clear();
        Used.Clear();
        Hot.Clear();
        Generation++;
        Pixels.Clear();
        if (Trace.On) Trace.Write("frame cache fully cleared");
    }
}

internal sealed class FighterDoll : MonoBehaviour
{
    private AbstractCharacter _owner;
    private PlayerCharacter _player;
    private string _clip;
    private bool _appear;
    private bool _fade;
    private float _fadeFrom = -1f;

    internal float TeleportAlpha = 1f;

    internal Vector3? Hold;

    internal bool Appearing => _appear || _playing == "prizuv";
    private GameObject _container;
    private SpriteRenderer _view;
    private Material _skin;
    private readonly List<Renderer> _hidden = new();
    private readonly HashSet<int> _needs = new();
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private Action<string> _arrived;
    private readonly List<string> _older = new();
    private bool _warmedOnce;
    private float _lastRequested = -1f;
    private float _lastBegan = -1f;
    private float _queuedAt = -1f;

    private string _look;
    private float _worldHeight = 1.8f;
    private double _rate = 20.0;
    private Dictionary<string, LabelRange> _labels;
    private string _shownKey;
    private bool _broken;
    private bool _shown;
    private string _warmedWeapon;
    private float _retryAt;
    private float _askAt;

    private string _playing;
    private float _playStart;
    private float _playRequested;
    private float _ownerCheckAt;
    private float _orphanedAt;
    private int _playCount;
    private bool _hold;
    private bool _deathShown;
    private bool _sawAlive;

    internal bool CameDead => !_sawAlive;
    private bool _moving;
    private float _moveStart;
    private string _pending;
    private int _smooth = 1;
    private SpriteRenderer _shade;
    private bool _capsuleTouched;
    private float _crown;

    private bool _wasVisible;
    private string _lastLabel;
    private MoverState _lastMover = MoverState.Idle;
    private bool _lastGliding;
    private float _missSince;
    private bool _missLogged;
    private string _missWhat;

    internal AbstractCharacter Owner => _owner;

    private string Who => _owner != null ? _owner.Login : "?";

    internal bool Acting => (_playing != null && _playing != "die") || _queue.Count > 0;

    internal Vector3 Feet => _feet;

    internal int SortingOrder => _view != null ? _view.sortingOrder : 0;

    internal Vector3 HeadShift(Camera eye)
    {
        if (eye == null || _view == null || _view.sprite == null || _viewLook == null) return Vector3.zero;
        float units = FrameCache.HeadPixels(_viewLook) / _view.sprite.pixelsPerUnit * transform.localScale.x;
        return eye.transform.right * (_view.flipX ? -units : units);
    }

    internal void Swap(Sprite old, Sprite fresh)
    {
        if (_view != null && _view.sprite == old) _view.sprite = fresh;
    }

    internal void Jump(string why)
    {
        _placed = false;
        _gliding = false;
        if (Trace.On) Trace.Write($"'{Who}' {why}: doll moved at once, without glide");
    }

    internal bool Placed => _placed;

    internal bool Gliding => _gliding;


    internal bool Shows(UnityEngine.Object item) => _view != null && ReferenceEquals(_view.sprite, item);

    public void Init(AbstractCharacter owner, GameObject container, string clip)
    {
        _owner = owner;
        _player = owner as PlayerCharacter;
        _clip = clip;
        _appear = clip != null && Fighters.IsSummoned(owner);
        _container = container;
        _view = gameObject.AddComponent<SpriteRenderer>();
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            _skin = new Material(shader);
            _view.sharedMaterial = _skin;
        }
        {
            var shade = new GameObject("NewAge2D.Shade");
            shade.transform.SetParent(container.transform, false);
            shade.layer = container.layer;
            shade.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shade.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            _shade = shade.AddComponent<SpriteRenderer>();
            if (_skin != null) _shade.sharedMaterial = _skin;
            _shade.sprite = Fighters.ShadeSprite;
            _shade.color = new Color(0f, 0f, 0f, 0.55f);
            if (Trace.On) Trace.Write($"'{Who}' shadow under feet placed");
        }
        ThingImages.Learned += OnLearned;
        _arrived = file => MainThread.Post(() => OnArrived(file));
        if (Plugin.Store != null) Plugin.Store.Arrived += _arrived;
        Camera.onPreCull += BeforeCull;
        string hands = _player != null ? $"right {Fighters.Describe(_player.RightHandWeapon)}, left {Fighters.Describe(_player.LeftHandWeapon)}" : "Flash clip " + clip;
        if (Trace.On) Trace.Write($"'{Who}' doll created (id {owner.UserId}, race {owner.Race}, gender {(int)owner.Gender}, {hands})");
    }

    private void OnDestroy()
    {
        if (Trace.On) Trace.Write($"'{Who}' doll REMOVED");
        ThingImages.Learned -= OnLearned;
        if (Plugin.Store != null && _arrived != null) Plugin.Store.Arrived -= _arrived;
        Camera.onPreCull -= BeforeCull;
        try { RestoreModel(); } catch { }
        try { if (_owner != null && _capsuleTouched) _owner.RecalculateCapsuleHeight(); } catch { }
        if (_skin != null) Destroy(_skin);
        if (_shade != null) Destroy(_shade.gameObject);
        DropHalo();
        Fighters.Forget(this);
    }

    private void OnLearned(int thingId)
    {
        if (!_needs.Contains(thingId)) return;
        if (Trace.On) Trace.Write($"'{Who}' item image arrived {thingId}");
        Refresh();
    }

    private void OnArrived(string file)
    {
        if (this == null || !_files.Contains(file)) return;
        string failure = Plugin.Store?.Failure(file);
        if (Trace.On) Trace.Write(failure == null ? $"'{Who}' item clip {file} ready" : $"'{Who}' item clip {file} NOT RECEIVED, item not drawn: {failure}");
        Refresh();
    }

    private float _bornAt;
    private bool _modelHidden;

    private bool Waited()
    {
        if (_view != null && _view.sprite != null) return true;
        if (FrameCache.LabelsFor(_look) != null) return true;
        if (_bornAt <= 0f) _bornAt = Time.unscaledTime;
        if (Time.unscaledTime - _bornAt >= 0.25f) return true;
        if (_owner == null || _needs.Count > 0 || _files.Count > 0) return false;
        if (_player == null) return true;
        foreach (var pair in _player.DressedThings)
            if (pair.Value != null && Doll.IsVisualSlot((int)pair.Key)) return true;
        return false;
    }

    private static bool SameBody(string a, string b)
    {
        if (a == null || b == null) return false;
        int cutA = a.IndexOf('/', a.IndexOf('/') + 1);
        int cutB = b.IndexOf('/', b.IndexOf('/') + 1);
        return cutA > 0 && cutA == cutB && string.CompareOrdinal(a, 0, b, 0, cutA) == 0;
    }

    private const float LookSettle = 0.3f;
    private string _nextLook;
    private float _nextLookAt;

    public void Refresh()
    {
        if (_owner == null || _container == null) return;
        HideModel(_view != null && _view.sprite != null);
        var plan = Plan();
        string look = plan.Look;
        if (look != _look && _look != null && _labels != null)
        {
            float now = Time.unscaledTime;
            if (look != _nextLook)
            {
                if (Trace.On) Trace.Write($"'{Who}' look {Trace.Look(_look)} → {Trace.Look(look)} waits {LookSettle * 1000f:0} ms for the rest of the change");
                _nextLook = look;
                _nextLookAt = now + LookSettle;
                return;
            }
            if (now < _nextLookAt) return;
        }
        _nextLook = null;
        if (look != _look)
        {
            if (Trace.On) Trace.Write($"'{Who}' look {Trace.Look(_look)} → {Trace.Look(look)}: items {plan.Wear.Count}, images pending {_needs.Count}, clips pending {_files.Count}, stop frames {FrameCache.StateOf(FrameCache.SequenceKey(look, "stop"))}");
            string was = _look;
            string wasOther = _otherLook;
            _look = look;
            _otherLook = Plan(!_left).Look;
            _lookSince = Time.unscaledTime;
            if (!SameBody(was, look))
            {
                _labels = null;
                _shown = false;
                _older.Clear();
            }
            else
            {
                Older(wasOther);
                Older(was);
            }
            _broken = false;
            _warmedWeapon = null;
            _shownKey = null;
        }
        Need("stop", true);
        if (Settled) Need("move", false, false, true);
    }

    internal int LastStartSerial { get; private set; }

    private int _requestCounter;
    private int _finishedRequest;
    private int _playingRequest;
    private int _pendingRequest;

    internal int LastRequest { get; private set; }

    internal bool Finished(int request) => request <= 0 || _finishedRequest >= request;

    internal bool CastWaiting(float since, out float began)
    {
        began = -1f;
        if (_playing != null && _playRequested >= since - 0.1f)
        {
            if (!Has(_playing)) return true;
            began = _playStart;
            return false;
        }
        if (_lastRequested >= since - 0.1f)
        {
            began = _lastBegan;
            return false;
        }
        return _queue.Count > 0 && _queuedAt >= since - 0.1f;
    }

    public void Play(string animation, string hand)
    {
        LastStartSerial = 0;
        LastRequest = 0;
        string wanted = Fighters.LabelFor(animation, _owner, hand, out bool loop, out int rank);
        if (Trace.On)
        {
            string side = hand ?? "-";
            Weapon weapon = null;
            if (_player != null) weapon = Fighters.WeaponOf(_player, hand, out side);
            Trace.Write($"'{Who}' Unity: action '{animation}', hand {hand ?? "-"} ({side}), weapon {Fighters.Describe(weapon)} → {wanted}");
        }
        if (loop || rank < 4)
        {
            if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[combat] '{_owner.Login}': action '{animation}' has no Flash animation");
            return;
        }
        Launch(wanted, animation);
    }

    public void Die()
    {
        if (_deathShown) return;
        _deathShown = true;
        if (!_sawAlive)
        {
            _queue.Clear();
            _playing = null;
            if (Trace.On) Trace.Write($"'{Who}' was dead before first seen: lies down at once, death clip not played");
            return;
        }
        Launch("die", "death");
    }

    public void Rise()
    {
        _deathShown = false;
        _queue.Clear();
        _finishedRequest = _requestCounter;
        if (_playing == "die") _playing = null;
        if (Trace.On) Trace.Write($"'{Who}' raised");
    }

    private readonly Queue<(string Wanted, string Reason, int Serial, int Request)> _queue = new();
    private int _serialRequested;
    private int _playingSerial;
    private int _landedSerial;
    private int _pendingSerial;

    private void Appear()
    {
        if (!_appear || _labels == null) return;
        _appear = false;
        if (_labels.ContainsKey("prizuv")) Launch("prizuv", "appearance");
        else _fade = true;
    }

    private void Launch(string wanted, string reason, int serial = -1, int request = 0)
    {
        bool fresh = serial < 0;
        if (fresh)
        {
            serial = wanted == "die" || wanted == "cast" || wanted == "healing" || wanted == "prizuv" ? 0 : ++_serialRequested;
            LastStartSerial = serial;
            request = ++_requestCounter;
            LastRequest = request;
        }
        if (wanted != "die" && wanted != "prizuv") Appear();
        if (wanted == "die")
        {
            _queue.Clear();
            _finishedRequest = Math.Max(_finishedRequest, request);
        }
        else if ((fresh || reason == "deferred") && Acting)
        {
            _queue.Enqueue((wanted, reason, serial, request));
            _queuedAt = Time.time;
            if (Trace.On) Trace.Write($"'{Who}' action '{reason}' → {wanted} queued behind {_playing ?? "queue"} (strike #{serial})");
            return;
        }
        if (_labels == null)
        {
            if (Trace.On) Trace.Write($"'{Who}' action '{reason}' → {wanted} DEFERRED: look {Trace.Look(_look)} has no labels yet (first frames not ready)");
            _pending = wanted;
            _pendingSerial = serial;
            _pendingRequest = request;
            return;
        }
        string label = Keyed(wanted, out var range);
        if (label == null)
        {
            if (Trace.On) Trace.Write($"'{Who}' action '{reason}' → {wanted}: body has no such label");
            _landedSerial = Math.Max(_landedSerial, serial);
            _finishedRequest = Math.Max(_finishedRequest, request);
            return;
        }
        _playing = label;
        _playingSerial = serial;
        _playingRequest = request;
        _playStart = Time.time;
        _playRequested = Time.time;
        _playCount = range.Count * _smooth;
        _hold = label == "die";
        _playScale = 1f;
        _measuring = !Plugin.FlashSpeed;
        _measureUntil = Time.time + 1f;
        _idleHash = StateHash();
        if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[combat] '{_owner.Login}': '{reason}' → {label}{(_left ? "" : " (body " + Fighters.Mirror(label) + ")")}, frames {range.Count}");
        if (Trace.On) Trace.Write($"'{Who}' action '{reason}' → {label}{(_left ? "" : " (body " + Fighters.Mirror(label) + ")")}, frames {range.Count}, look {Trace.Look(_look)}, frames {FrameCache.StateOf(FrameCache.SequenceKey(_look, label))}");
        Need(label, true);
    }

    private float _playScale = 1f;
    private bool _measuring;
    private float _measureUntil;
    private int _idleHash;

    private Animator LiveAnimator()
    {
        var animator = _owner != null && _owner.Initialized ? _owner.CharacterAnimator : null;
        return animator != null && animator.isActiveAndEnabled && animator.layerCount > 0 ? animator : null;
    }

    private int StateHash()
    {
        var animator = LiveAnimator();
        return animator != null ? animator.GetCurrentAnimatorStateInfo(0).fullPathHash : 0;
    }

    private void Measure(float now)
    {
        if (!_measuring || _playing == null) return;
        if (now > _measureUntil)
        {
            _measuring = false;
            return;
        }
        var animator = LiveAnimator();
        if (animator == null) return;
        var state = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        if (state.fullPathHash == _idleHash || state.loop || state.length < 0.2f) return;
        float unity = state.length / Mathf.Max(0.05f, Mathf.Abs(state.speed * state.speedMultiplier));
        float flash = _playCount / (float)_rate;
        float scale = Mathf.Clamp(flash / unity, 0.4f, 3f);
        _playStart = now - (now - _playStart) * _playScale / scale;
        _playScale = scale;
        _measuring = false;
        if (Plugin.CfgVerbose.Value)
            Plugin.Log.LogInfo($"[combat] '{_owner.Login}': {_playing} Flash {flash:0.00} s, Unity {unity:0.00} s → speed ×{scale:0.00}");
    }

    private void FreeCapsule()
    {
        if (!_capsuleTouched || _owner == null) return;
        _capsuleTouched = false;
        try { _owner.RecalculateCapsuleHeight(); }
        catch (Exception ex) { Plugin.Log.LogError("[combat] capsule: " + ex); }
    }

    internal float FitHeight { get; private set; }

    private void FitCapsule(Camera eye)
    {
        if (!Plugin.CfgFitCapsule.Value || _owner == null || !_owner.Initialized || _owner.Dead) return;
        var collider = _owner.CharacterCollider;
        if (collider == null || collider.direction != 1) return;
        var sprite = _view.sprite;
        if (sprite == null || sprite.pixelsPerUnit <= 0f) return;
        float stop = FrameCache.StopPixels(_viewLook);
        if (stop > 0f) _crown = stop / sprite.pixelsPerUnit;
        if (_crown <= 0f) return;
        float tall = _crown * transform.localScale.y;
        var feet = transform.position;
        float feetY = eye.WorldToScreenPoint(feet).y;
        float unit = eye.WorldToScreenPoint(feet + Vector3.up).y - feetY;
        if (Mathf.Abs(unit) < 0.01f) return;
        float crown = eye.WorldToScreenPoint(feet + transform.up * tall).y;
        float lift = Mathf.Clamp(Plugin.CfgBarsLift.Value, 0.5f, 2f);
        float over = (crown - Fighters.Caption - feetY) / unit * lift;
        float height = Mathf.Clamp(over, 0.1f, _worldHeight * 4f);
        FitHeight = height;
        if (Mathf.Abs(collider.height - height) < 0.001f) return;
        collider.height = height;
        var center = collider.center;
        center.y = height / 2f;
        collider.center = center;
        _capsuleTouched = true;
    }

    private void HideModel(bool hide = true)
    {
        var model = Fighters.Model(_owner);
        if (model == null) return;
        bool any = false;
        var bounds = new Bounds();
        int own = 0;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.enabled)
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
                if (hide)
                {
                    _hidden.Add(renderer);
                    own++;
                }
            }
            if (hide) renderer.enabled = false;
        }
        if (hide)
        {
            var veiled = Fighters.TakeVeil(_owner);
            if (veiled != null) _hidden.AddRange(veiled);
            if (Trace.On && (!_modelHidden || own > 0)) Trace.Write($"'{Who}' 3D model hidden by doll: visible were {own}, from veil {veiled?.Count ?? 0}");
            _modelHidden = true;
        }
        float box = any ? bounds.size.y : 0f;
        float body = 0f;
        var holder = _owner.CharacterMeshRendererHolder;
        if (holder != null && holder.MainRenderer != null) body = holder.MainRenderer.bounds.size.y;
        float capsule = 0f;
        var collider = _owner.CharacterCollider;
        if (collider != null && Fighters.CapsuleHeightField != null && Fighters.CapsuleHeightField.GetValue(_owner) is float original && original > 0.05f)
            capsule = original * collider.transform.lossyScale.y;
        float height = body > 0.05f ? body : capsule > 0.05f ? capsule : box;
        if (height > 0.05f) _worldHeight = height * Mathf.Clamp(Plugin.CfgHeight.Value, 0.3f, 3f);
        _hidden.RemoveAll(r => r == null);
        if (_shade != null)
        {
            float width = _worldHeight * 0.55f;
            _shade.transform.localScale = new Vector3(width, width * 0.24f, 1f);
        }
    }

    private void RestoreModel()
    {
        int shown = 0;
        foreach (var renderer in _hidden)
            if (renderer != null)
            {
                renderer.enabled = true;
                shown++;
            }
        _hidden.Clear();
        var veiled = Fighters.TakeVeil(_owner);
        if (veiled != null)
            foreach (var renderer in veiled)
                if (renderer != null)
                {
                    renderer.enabled = true;
                    shown++;
                }
        if (Trace.On && shown > 0) Trace.Write($"'{Who}' 3D model RESTORED by doll: renderers {shown}{(_broken ? ", doll broken" : "")}");
        _modelHidden = false;
        if (_view != null) _view.sprite = null;
    }

    private static readonly Dictionary<ESlots.SlotType, Transport.Messages.Common.User.Wear.InventorySlotMessage> NoWear = new();

    private DollRequest Plan(bool? left = null)
    {
        var request = new DollRequest
        {
            Race = _owner.Race,
            Gender = (int)_owner.Gender,
            Clip = _clip,
            Scale = Plugin.CombatScale,
            Smooth = Plugin.Smooth,
            Left = left ?? _left,
        };
        _smooth = request.Smooth;
        _needs.Clear();
        _files.Clear();
        if (_clip != null && Plugin.Store != null && !Plugin.Store.Ready(_clip)) _files.Add(_clip);
        foreach (var pair in _player != null ? _player.DressedThings : NoWear)
        {
            int slot = (int)pair.Key;
            var item = pair.Value;
            if (item == null || !Doll.IsVisualSlot(slot)) continue;
            string image = ThingImages.Get(item.ThingId);
            if (image == null)
            {
                _needs.Add(item.ThingId);
                ThingImages.Ask(item.ThingId);
                continue;
            }
            string file = Doll.WearFile(image);
            if (file != null && Plugin.Store != null && !Plugin.Store.Ready(file))
            {
                _files.Add(file);
                continue;
            }
            request.Wear.Add(new DollWear { Slot = slot, ThingId = item.ThingId, Image = image });
        }
        if (_files.Count > 0) Plugin.Store.Prefetch(_files.ToArray());
        return request;
    }

    private bool _failed;
    private float _failedAt = -1f;

    private void LateUpdate()
    {
        if (_failedAt >= 0f && Time.unscaledTime - _failedAt < 1f) return;
        try
        {
            Tick();
            Watch();
            _failedAt = -1f;
        }
        catch (Exception ex)
        {
            _failedAt = Time.unscaledTime;
            if (_failed) return;
            _failed = true;
            Plugin.Log.LogError($"[combat] '{_owner?.Login}': doll broke: {ex}");
            if (Trace.On) Trace.Write($"'{Who}' doll BROKE: {ex.GetType().Name} {ex.Message}");
        }
    }

    private void Watch()
    {
        if (!Trace.On) return;
        bool visible = _view != null && _view.sprite != null && _view.enabled && gameObject.activeInHierarchy;
        if (visible != _wasVisible)
        {
            _wasVisible = visible;
            Trace.Write($"'{Who}' doll {(visible ? "appeared" : "VANISHED")}: label {_lastLabel ?? "-"}, look on screen {Trace.Look(_viewLook)}, wanted look {Trace.Look(_look)}, 3D {(_modelHidden ? "hidden" : "not hidden")}");
        }
        if (_owner == null || !_owner.Initialized) return;
        var mover = _owner.MoverState;
        if (mover != _lastMover || _gliding != _lastGliding)
        {
            Trace.Write($"'{Who}' movement {_lastMover}{(_lastGliding ? "+glide" : "")} → {mover}{(_gliding ? "+glide" : "")}, move frames {FrameCache.StateOf(FrameCache.SequenceKey(_look, "move"))}");
            _lastMover = mover;
            _lastGliding = _gliding;
        }
    }

    private static readonly (float X, float Y, float A)[] Ring = BuildRing();
    private SpriteRenderer[] _halo;
    private Material _haloSkin;

    private static (float X, float Y, float A)[] BuildRing()
    {
        float[] radii = { 2f, 4f, 6f };
        float[] alphas = { 0.5f, 0.3f, 0.15f };
        const int around = 8;
        var ring = new (float X, float Y, float A)[radii.Length * around];
        for (int r = 0; r < radii.Length; r++)
            for (int i = 0; i < around; i++)
            {
                float angle = (i + r * 0.5f) * Mathf.PI * 2f / around;
                ring[r * around + i] = (Mathf.Cos(angle) * radii[r], Mathf.Sin(angle) * radii[r], alphas[r]);
            }
        return ring;
    }

    private bool _hover;
    private bool _threat;
    private bool _chosen;
    private Color _chosenPaint;

    internal void Glow(bool on)
    {
        _hover = on;
        Halo();
    }

    internal void Threat(bool on)
    {
        if (_threat == on) return;
        _threat = on;
        Halo();
    }

    internal void Chosen(bool on, Color paint)
    {
        _chosenPaint = paint;
        if (_chosen == on) return;
        _chosen = on;
        Halo();
    }

    private void Halo()
    {
        if (!_hover && !_threat && !_chosen)
        {
            DropHalo();
            return;
        }
        if (_halo != null) return;
        var shader = Shader.Find("UI/Default");
        if (shader == null) return;
        _haloSkin = new Material(shader);
        _haloSkin.SetColor("_TextureSampleAdd", new Color(1f, 1f, 1f, 0f));
        _halo = new SpriteRenderer[Ring.Length];
        for (int i = 0; i < Ring.Length; i++)
        {
            var go = new GameObject("NewAge2D.Glow");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = _haloSkin;
            _halo[i] = renderer;
        }
        SyncHalo();
    }

    private void SyncHalo()
    {
        if (_halo == null || _view == null) return;
        var sprite = _view.sprite;
        bool shown = sprite != null && _view.enabled;
        float unit = sprite != null && sprite.pixelsPerUnit > 0f ? 1f / sprite.pixelsPerUnit : 0.01f;
        float alpha = _view.color.a;
        for (int i = 0; i < _halo.Length; i++)
        {
            var renderer = _halo[i];
            if (renderer == null) continue;
            if (renderer.enabled != shown) renderer.enabled = shown;
            if (!shown) continue;
            if (renderer.sprite != sprite) renderer.sprite = sprite;
            if (renderer.flipX != _view.flipX) renderer.flipX = _view.flipX;
            if (renderer.sortingLayerID != _view.sortingLayerID) renderer.sortingLayerID = _view.sortingLayerID;
            int order = _view.sortingOrder - 1;
            if (renderer.sortingOrder != order) renderer.sortingOrder = order;
            var at = new Vector3(Ring[i].X * unit, Ring[i].Y * unit, 0f);
            if (renderer.transform.localPosition != at) renderer.transform.localPosition = at;
            var paint = _hover ? NewAge2D.Glow.Paint : _chosen ? _chosenPaint : NewAge2D.Glow.Reach;
            var tint = new Color(paint.r, paint.g, paint.b, Ring[i].A * alpha * (_hover || _chosen ? 1f : NewAge2D.Glow.Pulse));
            if (renderer.color != tint) renderer.color = tint;
        }
    }

    private void DropHalo()
    {
        if (_halo != null)
            foreach (var renderer in _halo)
                if (renderer != null) Destroy(renderer.gameObject);
        _halo = null;
        if (_haloSkin != null) Destroy(_haloSkin);
        _haloSkin = null;
    }

    internal bool HasPicture => _view != null && _view.sprite != null;

    internal bool Covers(Camera eye, Vector2 mouse)
    {
        if (eye == null || _view == null || !_view.enabled || _view.sprite == null) return false;
        var sprite = _view.sprite;
        var place = _view.transform;
        var ray = eye.ScreenPointToRay(mouse);
        var sheet = new Plane(place.forward, place.position);
        if (!sheet.Raycast(ray, out float along)) return false;
        var local = place.InverseTransformPoint(ray.GetPoint(along));
        if (_view.flipX) local.x = -local.x;
        var box = sprite.bounds;
        if (box.size.x <= 0f || box.size.y <= 0f) return false;
        float u = (local.x - box.min.x) / box.size.x;
        float v = (local.y - box.min.y) / box.size.y;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
        var shape = FrameCache.ShapeOf(sprite);
        return shape == null || shape.At(u, v);
    }

    internal bool Broken => _broken;

    internal void AddHot(HashSet<string> hot)
    {
        Keep(hot, _look);
        Keep(hot, _viewLook);
        if (_playing == null) return;
        if (_look != null) hot.Add(FrameCache.SequenceKey(_look, _playing));
        if (_viewLook != null) hot.Add(FrameCache.SequenceKey(_viewLook, _playing));
    }

    private static void Keep(HashSet<string> hot, string look)
    {
        if (look == null) return;
        hot.Add(FrameCache.SequenceKey(look, "stop"));
        hot.Add(FrameCache.SequenceKey(look, "move"));
    }

    internal string Look => _look;

    internal string ViewLook => _viewLook;

    internal string OtherLook => _otherLook;

    private float _lookSince;

    private bool Settled => (_needs.Count == 0 && _files.Count == 0) || Time.unscaledTime - _lookSince > 2f;

    private bool Mine
    {
        get
        {
            var me = Fighters.Combat()?.MyCharacter;
            return me != null && _owner != null && me.UserId == _owner.UserId;
        }
    }
    private string _viewLook;
    private string _otherLook;
    private bool _left = true;

    private void Face(bool left)
    {
        if (left == _left) return;
        _left = left;
        if (_look == null) return;
        string was = _look;
        _look = _otherLook ?? Plan().Look;
        _otherLook = was;
        _warmedWeapon = null;
        if (Trace.On) Trace.Write($"'{Who}' turn {(left ? "left" : "right")}: look {Trace.Look(was)} → {Trace.Look(_look)}, stop frames {FrameCache.StateOf(FrameCache.SequenceKey(_look, "stop"))}, move {FrameCache.StateOf(FrameCache.SequenceKey(_look, "move"))}");
    }

    private void Tick()
    {
        if (_owner == null || _container == null)
        {
            Destroy(gameObject);
            return;
        }
        if (Time.unscaledTime >= _ownerCheckAt)
        {
            _ownerCheckAt = Time.unscaledTime + 0.5f;
            var combat = Fighters.Combat();
            if (combat != null && (!combat.Characters.TryGetValue(_owner.UserId, out var current) || !ReferenceEquals(current, _owner)))
            {
                if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[combat] '{_owner.Login}': character replaced or removed, doll taken off");
                if (Trace.On) Trace.Write($"'{Who}' character replaced or removed from fight, doll taken off");
                Destroy(gameObject);
                return;
            }
            if (!_owner.Initialized)
            {
                if (_orphanedAt <= 0f) _orphanedAt = Time.unscaledTime;
                else if (Time.unscaledTime - _orphanedAt > 1.5f)
                {
                    if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[combat] '{_owner.Login}': model removed by game, doll taken off");
                    if (Trace.On) Trace.Write($"'{Who}' model removed by game 1.5 s ago, doll taken off");
                    Destroy(gameObject);
                    return;
                }
            }
            else _orphanedAt = 0f;
        }
        if (_owner.Initialized && !_owner.Dead) _sawAlive = true;
        if (_broken)
        {
            FreeCapsule();
            return;
        }
        var view = CombatView.Get();
        var eye = view != null ? view.CombatCamera : Camera.main;
        if (eye != null) Place(eye);
        if (_nextLook != null && Time.unscaledTime >= _nextLookAt) Refresh();
        if (_needs.Count > 0 && Time.unscaledTime >= _askAt)
        {
            _askAt = Time.unscaledTime + 10f;
            foreach (int thingId in _needs) ThingImages.Ask(thingId);
        }
        if (_labels == null)
        {
            _labels = FrameCache.LabelsFor(_look);
            if (_labels == null)
            {
                if (Time.unscaledTime >= _retryAt)
                {
                    _retryAt = Time.unscaledTime + 0.5f;
                    Need("stop", true);
                }
                return;
            }
            _rate = FrameCache.RateFor(_look);
            Appear();
            if (_pending != null)
            {
                string pending = _pending;
                _pending = null;
                Launch(pending, "deferred", _pendingSerial, _pendingRequest);
            }
        }
        Appear();
        if (_shown && (Settled || !_warmedOnce) && Time.unscaledTime - _lookSince >= 0.2f && !(_playing == "prizuv" && !Has("prizuv"))) Prewarm();
        Decide(out string label, out int frame);
        if (label != _lastLabel)
        {
            if (Trace.On) Trace.Write($"'{Who}' label {_lastLabel ?? "-"} → {label}, frames {FrameCache.StateOf(FrameCache.SequenceKey(_look, label))}, look {Trace.Look(_look)}");
            _lastLabel = label;
        }
        Need(label, true);
        if (label != "stop" && Settled) Need("stop", true);
        if (label == "stop") Need("stop" + Fighters.Living, !Fighters.Crowd, false, false, Fighters.Crowd);
        Show(label, frame);
        float alpha = _appear || (_playing == "prizuv" && !Has("prizuv")) ? 0f : 1f;
        if (_fade && HasPicture)
        {
            if (_fadeFrom < 0f) _fadeFrom = Time.time;
            alpha = Mathf.Clamp01((Time.time - _fadeFrom) / 0.5f);
            if (alpha >= 1f) _fade = false;
        }
        alpha *= TeleportAlpha;
        var tint = Field.DayTint;
        var paint = new Color(tint.r, tint.g, tint.b, alpha);
        if (_view.color != paint) _view.color = paint;
        SyncHalo();
        Diagnose(label, frame);
    }

    private bool _diagWalking;
    private float _diagWalkAt;
    private bool _diagReady;
    private bool _diagFlip;
    private int _diagOrder;
    private Vector3 _diagFeet;
    private bool _diagTurned;
    private bool _diagSprite;
    private float _diagRendererAt;
    private bool _lastTurned;

    private void Diagnose(string label, int frame)
    {
        if (!Trace.On || _view == null || !Mine) return;
        bool flip = _view.flipX;
        int order = _view.sortingOrder;
        bool sprite = _view.sprite != null;
        bool moving = _owner.Initialized && (_owner.MoverState != MoverState.Idle || _gliding);
        if (_diagReady && (moving || _moving))
        {
            string at = $"{label}:{frame}, look {Trace.Look(_look)}, move {_owner.MoverState}{(_gliding ? "+glide" : "")}";
            if (flip != _diagFlip) Trace.Write($"'{Who}' DIAG doll mirroring changed to {(flip ? "flipX" : "no flipX")} ({at})");
            if (Mathf.Abs(order - _diagOrder) > 128) Trace.Write($"'{Who}' DIAG draw order {_diagOrder} → {order} ({at})");
            float jump = Vector3.Distance(_feet, _diagFeet);
            if (jump > 0.15f) Trace.Write($"'{Who}' DIAG feet jumped by {jump:0.00} ({at})");
            if (_lastTurned != _diagTurned) Trace.Write($"'{Who}' DIAG Unity model turn while moving → {(_lastTurned ? "right" : "left")}, doll faces {(_facingRight ? "right" : "left")} ({at})");
            if (sprite != _diagSprite) Trace.Write($"'{Who}' DIAG sprite {(sprite ? "appeared" : "VANISHED")} ({at})");
            if (Time.unscaledTime >= _diagRendererAt)
            {
                foreach (var renderer in _hidden)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    _diagRendererAt = Time.unscaledTime + 1f;
                    Trace.Write($"'{Who}' DIAG renderer of hidden 3D model '{renderer.name}' enabled again ({at})");
                    break;
                }
            }
        }
        if (moving || _moving)
        {
            if (!_diagWalking)
            {
                _diagWalking = true;
                _diagWalkAt = Time.time;
            }
            string shown = "-";
            if (_shownKey != null)
            {
                int cut = _shownKey.IndexOf('#');
                shown = cut > 0 ? Trace.Look(_shownKey.Substring(0, cut)) + " " + _shownKey.Substring(cut + 1) : _shownKey;
            }
            int visible3d = 0;
            var model = Fighters.Model(_owner);
            if (model != null)
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled) visible3d++;
            var place = transform.position;
            Trace.Write($"'{Who}' FRAME +{Time.time - _diagWalkAt:0.000} {label}:{frame} shown {shown} flip {(flip ? 1 : 0)} order {order} feet {_feet.x:0.00},{_feet.z:0.00} doll {place.x:0.00},{place.y:0.00},{place.z:0.00} scale {transform.localScale.x:0.000} visible {(_view.enabled && gameObject.activeInHierarchy ? 1 : 0)} 3D {visible3d} turn {(_lastTurned ? "R" : "L")} move {_owner.MoverState}{(_gliding ? "+glide" : "")}");
        }
        else _diagWalking = false;
        _diagReady = true;
        _diagFlip = flip;
        _diagOrder = order;
        _diagFeet = _feet;
        _diagTurned = _lastTurned;
        _diagSprite = sprite;
    }

    private bool _gliding;
    private bool _placed;
    private Vector3 _feet;
    private const float StepGrace = 1f;
    private float _stepWait = -1f;

    private bool Late(string label) => !_broken && label != null && _look != null && _labels != null && _labels.ContainsKey(label) && !Has(label);

    private Vector3 Glide(Vector3 target)
    {
        if (Hold.HasValue && _placed)
        {
            _feet = Hold.Value;
            _gliding = false;
            return _feet;
        }
        if (!_placed || !Plugin.FlashSpeed || _owner.Dead)
        {
            _placed = true;
            _feet = target;
            _gliding = false;
            return target;
        }
        float gap = Vector3.Distance(_feet, target);
        if (gap > 6f || gap < 0.01f)
        {
            if (gap > 6f && Trace.On) Trace.Write($"'{Who}' moved by {gap:0.0} without glide");
            _feet = target;
            _gliding = false;
            return target;
        }
        float speed = MathConsts.Sqrt3 * MathConsts.HEX_SIZE / Mathf.Clamp(Plugin.CfgHexSeconds.Value, 0.2f, 5f);
        bool walking = _owner.MoverState != MoverState.Idle;
        if (Late("move"))
        {
            if (_stepWait < 0f)
            {
                _stepWait = Time.time;
                if (Trace.On) Trace.Write($"'{Who}' walk of look {Trace.Look(_look)} still drawing, feet wait up to {StepGrace:0.0} s");
            }
            if (Time.time - _stepWait < StepGrace)
            {
                _gliding = false;
                return _feet;
            }
        }
        else _stepWait = -1f;
        _feet = Vector3.MoveTowards(_feet, target, (walking ? speed * 1.5f : speed) * Time.deltaTime);
        _gliding = !walking && Vector3.Distance(_feet, target) > 0.01f;
        return _feet;
    }

    private bool _facingRight;
    private bool _facingKnown;
    private bool _walked;
    private bool _turnPending;

    internal void GameTurned() => _turnPending = true;

    private bool Facing(Camera eye)
    {
        bool turned = Vector3.Dot(_container.transform.forward, eye.transform.right) > 0f;
        _lastTurned = turned;
        if (_turnPending || !_facingKnown)
        {
            _turnPending = false;
            _walked = false;
            _facingKnown = true;
            _facingRight = turned;
            return turned;
        }
        bool walking = _owner.Initialized && _owner.MoverState != MoverState.Idle;
        if (!walking)
        {
            if (_gliding || _walked) return _facingRight;
            _facingRight = turned;
            return turned;
        }
        _walked = true;
        float across = Vector3.Dot(_container.transform.forward, eye.transform.right);
        if (turned == _facingRight || Mathf.Abs(across) < 0.05f) return _facingRight;
        if (Trace.On) Trace.Write($"'{Who}' step {(turned ? "right" : "left")}: turn by step, as in Flash");
        _facingRight = turned;
        return _facingRight;
    }

    private void Ground(Vector3 feet, Vector3 shift)
    {
        if (_shade == null) return;
        _shadeAt = feet + shift;
        _shade.transform.SetPositionAndRotation(_shadeAt, Flat);
    }

    private static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f);
    private Vector3 _shadeAt;

    private Vector3 Steady(Camera eye, Vector3 at)
    {
        if (eye == null || !eye.orthographic || Screen.height <= 0) return at;
        if (_gliding || (_owner != null && _owner.Initialized && _owner.MoverState != MoverState.Idle)) return at;
        float unit = eye.orthographicSize * 2f / Screen.height;
        if (unit <= 0.0000001f) return at;
        var eyeAt = eye.transform.position;
        var right = eye.transform.right;
        var up = eye.transform.up;
        var forward = eye.transform.forward;
        var local = at - eyeAt;
        float x = Mathf.Round(Vector3.Dot(local, right) / unit) * unit;
        float y = Mathf.Round(Vector3.Dot(local, up) / unit) * unit;
        float z = Vector3.Dot(local, forward);
        return eyeAt + right * x + up * y + forward * z;
    }

    private void Place(Camera eye)
    {
        bool right = Facing(eye);
        _view.flipX = right != Plugin.CfgCombatFlip.Value;
        Face(!_view.flipX);
        var feet = Glide(_container.transform.position);
        Fighters.TowardEye(eye, feet, 1f, out var shift, out float near);
        var facing = eye.transform.rotation;
        float fit = Field.DollSize;
        _view.sortingOrder = Fighters.Layer(eye, feet, _owner != null ? _owner.HexGridPosition : null);
        if (_shade != null) _shade.sortingOrder = _view.sortingOrder - 2;
        transform.rotation = facing;
        transform.localScale = new Vector3(near, near, near) * fit;
        transform.position = Steady(eye, feet + shift);
        Ground(feet, shift);
        Remember();
        FitCapsule(eye);
    }

    private Vector3 _worldPosition;
    private Quaternion _worldRotation;
    private bool _worldKnown;
    private float _skewLoggedAt;

    private void Remember()
    {
        _worldPosition = transform.position;
        _worldRotation = transform.rotation;
        _worldKnown = true;
    }

    private void BeforeCull(Camera camera)
    {
        if (!_worldKnown || this == null || camera == null) return;
        float angle = Quaternion.Angle(transform.rotation, _worldRotation);
        float shift = Vector3.Distance(transform.position, _worldPosition);
        if (angle < 0.1f && shift < 0.001f)
        {
            if (_shade != null && Vector3.Distance(_shade.transform.position, _shadeAt) > 0.001f)
                _shade.transform.SetPositionAndRotation(_shadeAt, Flat);
            return;
        }
        if (Trace.On && Time.unscaledTime >= _skewLoggedAt && Mine)
        {
            _skewLoggedAt = Time.unscaledTime + 0.2f;
            Trace.Write($"'{Who}' DIAG before frame the doll was knocked by Unity model turn: {angle:0} deg, shift {shift:0.00}, put back");
        }
        transform.SetPositionAndRotation(_worldPosition, _worldRotation);
        if (_shade != null) _shade.transform.SetPositionAndRotation(_shadeAt, Flat);
    }

    private void Prewarm()
    {
        string main = _player != null ? Fighters.WeaponLabel(_player, null) : "fight_right";
        string spare = _player != null ? (Fighters.BothHands(_player) ? Fighters.WeaponLabel(_player, "_left") : null) : "fight_left";
        string weapon = main + "|" + spare;
        if (weapon == _warmedWeapon) return;
        _warmedWeapon = weapon;
        _warmedOnce = true;
        _warm.Clear();
        if (Trace.On) Trace.Write($"'{Who}' warming look {Trace.Look(_look)} and {Trace.Look(_otherLook)}: strike {main}{(spare != null ? ", second hand " + spare : "")}");
        string strike = Keyed(main, out _);
        string second = spare != null ? Keyed(spare, out _) : null;
        if (_labels != null && _labels.ContainsKey("move")) Need("move", false, false, true);
        if (strike != null) Need(strike, false);
        if (second != null) Need(second, false);
        foreach (string label in new[] { "cast", "healing" })
            if (_labels.ContainsKey(label)) Need(label, false);
        Need("stop" + Fighters.Living, false);
        if (_labels.ContainsKey("die")) Need("die", false, false, false, true);
        Need("stop", false, true);
        if (_labels.ContainsKey("move")) Need("move", false, true, !Fighters.Crowd, Fighters.Crowd);
        if (strike != null) Need(strike, false, true, Mine, !Mine);
        if (second != null) Need(second, false, true, Mine, !Mine);
        foreach (string label in new[] { "cast", "healing" })
            if (_labels.ContainsKey(label)) Need(label, false, true, !Fighters.Crowd, Fighters.Crowd);
        Need("stop" + Fighters.Living, false, true, false, true);
    }

    private readonly List<string> _warm = new();

    internal bool Warm
    {
        get
        {
            if (_broken) return true;
            if (!HasPicture || _warmedWeapon == null) return false;
            foreach (string sequence in _warm)
                if (!FrameCache.HasSequence(sequence) && FrameCache.IsPending(sequence)) return false;
            return true;
        }
    }

    private string Keyed(string wanted, out LabelRange range)
    {
        range = default;
        string label = Resolve(wanted);
        if (label == "stop" || !_labels.TryGetValue(_left ? label : Fighters.Mirror(label), out range)) return null;
        int kick = Doll.SlotOf(wanted);
        if (_player == null) kick = 0;
        return kick == 0 ? label : label + "/" + kick;
    }

    private static void Split(string label, out string real, out int kick, out bool alive)
    {
        real = label;
        kick = 0;
        alive = false;
        if (real.EndsWith(Fighters.Living, StringComparison.Ordinal))
        {
            real = real.Substring(0, real.Length - Fighters.Living.Length);
            alive = true;
        }
        int cut = real.IndexOf('/');
        if (cut < 0) return;
        kick = int.TryParse(real.Substring(cut + 1), out int slot) ? slot : 0;
        real = real.Substring(0, cut);
    }

    private string Resolve(string wanted)
    {
        if (_labels.ContainsKey(wanted)) return wanted;
        int cut = wanted.LastIndexOf('_');
        if (cut > 0)
        {
            string spare = "fight" + wanted.Substring(cut);
            if (_labels.ContainsKey(spare)) return spare;
        }
        return "stop";
    }


    private void Decide(out string label, out int frame)
    {
        float now = Time.time;
        if (_playing == null && _queue.Count > 0)
        {
            var queued = _queue.Dequeue();
            Launch(queued.Wanted, queued.Reason, queued.Serial, queued.Request);
        }
        if (_playing != null && !Has(_playing))
        {
            string sequence = FrameCache.SequenceKey(_look, _playing);
            Need(_playing, true);
            float patience = FrameCache.IsPending(sequence) ? 3f : 1.5f;
            if (now - _playRequested <= patience) _playStart = now;
            else
            {
                if (Trace.On) Trace.Write($"'{Who}' action {_playing} DROPPED: frames of look {Trace.Look(_look)} {FrameCache.StateOf(sequence)} for {now - _playRequested:0.0} s already");
                _landedSerial = Math.Max(_landedSerial, _playingSerial);
                _finishedRequest = Math.Max(_finishedRequest, _playingRequest);
                _playing = null;
            }
        }
        if (_playing != null && Has(_playing))
        {
            Measure(now);
            _lastRequested = _playRequested;
            _lastBegan = _playStart;
            int total = FrameCache.CountOf(FrameCache.SequenceKey(PlayLook(_playing), _playing));
            if (total <= 0) total = _playCount;
            int index = (int)((now - _playStart) * _rate * _playScale);
            if (index < total)
            {
                label = _playing;
                frame = index;
                return;
            }
            if (_hold)
            {
                label = _playing;
                frame = total - 1;
                return;
            }
            _landedSerial = Math.Max(_landedSerial, _playingSerial);
            _finishedRequest = Math.Max(_finishedRequest, _playingRequest);
            _playing = null;
            if (_queue.Count > 0)
            {
                var queued = _queue.Dequeue();
                Launch(queued.Wanted, queued.Reason, queued.Serial, queued.Request);
                if (_playing != null)
                {
                    label = _playing;
                    frame = 0;
                    return;
                }
            }
        }

        if (_owner.Dead && _labels.TryGetValue("die", out var dead))
        {
            label = "die";
            frame = dead.Count * _smooth - 1;
            return;
        }

        bool moving = (_owner.Initialized && _owner.MoverState != MoverState.Idle) || _gliding;
        if (moving && _labels.TryGetValue("move", out var move))
        {
            if (!Has("move") && Has("stop"))
            {
                if (_moving || !_glideNoted)
                {
                    _glideNoted = true;
                    if (Trace.On) Trace.Write($"'{Who}' walk frames not ready, gliding in stance");
                }
                _moving = false;
                Need("move", !Fighters.Crowd, false, false, Fighters.Crowd);
                label = "stop";
                frame = 0;
                return;
            }
            _glideNoted = false;
            if (!_moving)
            {
                _moving = true;
                _moveStart = now;
            }
            float step = Fighters.StepSeconds(_owner);
            float boost = !Plugin.FlashSpeed && step > 0.05f ? Mathf.Clamp(27f / 20f / step, 0.25f, 4f) : 1f;
            int cycle = FrameCache.CountOf(FrameCache.SequenceKey(PlayLook("move"), "move"));
            if (cycle <= 0) cycle = move.Count * _smooth;
            label = "move";
            frame = (int)((now - _moveStart) * _rate * boost) % cycle;
            return;
        }
        _moving = false;
        string alive = "stop" + Fighters.Living;
        string living = PlayLook(alive);
        if (living == null || !FrameCache.HasSequence(FrameCache.SequenceKey(living, alive)))
            living = _viewLook != null && _viewLook != _look && FrameCache.HasSequence(FrameCache.SequenceKey(_viewLook, alive)) ? _viewLook : null;
        int count = living != null ? FrameCache.CountOf(FrameCache.SequenceKey(living, alive)) : 0;
        if (count > 1)
        {
            label = alive;
            frame = ((int)(now * _rate) + Mathf.Abs(_owner.UserId)) % count;
            return;
        }
        label = "stop";
        frame = 0;
    }

    private void Need(string label, bool urgent, bool other = false, bool soon = false, bool late = false)
    {
        if (!Waited()) return;
        if (_clip != null && Plugin.Store != null && !Plugin.Store.Ready(_clip))
        {
            if (_files.Add(_clip)) Plugin.Store.Prefetch(new[] { _clip });
            return;
        }
        string look = other ? _otherLook : _look;
        if (look == null) return;
        string sequence = FrameCache.SequenceKey(look, label);
        if (FrameCache.HasSequence(sequence)) return;
        if (!other && Spare(label) != null)
        {
            if (!Settled) return;
            if (label != "stop" && !label.EndsWith(Fighters.Living, StringComparison.Ordinal))
            {
                urgent = false;
                soon = false;
            }
        }
        if (!urgent && !_warm.Contains(sequence)) _warm.Add(sequence);
        if (!FrameCache.BeginSequence(sequence))
        {
            if (urgent && (DollWorker.Promote(sequence) || FrameCache.Hurry(sequence)) && Trace.On) Trace.Write($"'{Who}' order {label} look {Trace.Look(look)} raised to urgent queue");
            return;
        }

        var request = Plan(other ? !_left : _left);
        if (request.Look != look)
        {
            FrameCache.EndSequence(sequence, false);
            if (Trace.On) Trace.Write($"'{Who}' order {label} of look {Trace.Look(look)} recalculated: look is now {Trace.Look(request.Look)}");
            if (other)
            {
                _otherLook = request.Look;
                Need(label, urgent, true, soon, late);
            }
            else Refresh();
            return;
        }
        float ppu = Field.PxPerUnit * request.Scale;
        int generation = FrameCache.Generation;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Split(label, out string real, out int kick, out bool alive);
        if (!request.Left) real = Fighters.Mirror(real);
        int priority = urgent ? 0 : soon ? (Mine ? 0 : 1) : late ? 3 : 2;
        string who = Who;
        if (Trace.On) Trace.Write($"'{who}' order {label}{(real != label.Split('/')[0].TrimEnd('~') ? " (body " + real + ")" : "")} look {Trace.Look(look)}, queue {(priority == 0 ? "urgent" : priority == 1 ? "soon" : priority == 2 ? "background" : "last")}");
        DollWorker.EnqueueSequence(request, real, kick, alive, result => MainThread.Post(() =>
        {
            if (FrameCache.Generation != generation) return;
            bool ok = result.Error == null && result.Frames.Count > 0;
            if (!ok)
            {
                FrameCache.EndSequence(sequence, false);
                if (Trace.On) Trace.Write($"'{who}' {label} look {Trace.Look(look)} NOT DRAWN: {result.Error ?? "no frames"}");
                if (this == null) return;
                if (label == "stop" && look == _look)
                {
                    _broken = true;
                    RestoreModel();
                    Plugin.Log.LogWarning($"[combat] '{_owner?.Login}': {result.Error ?? "no frames"} - keeping 3D");
                }
                else if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[combat] '{_owner?.Login}': {label}: {result.Error ?? "no frames"}");
                return;
            }
            FrameCache.Remember(look, result.Labels, result.FrameRate);
            FrameCache.SetHit(sequence, result.HitShare);
            if (Trace.On && !float.IsNaN(result.HitShare)) Trace.Write($"'{who}' weapon contact in {label} of look {Trace.Look(look)}: {result.HitShare * 100f:0}% of animation");
            if (label == "stop" || label == "stop" + Fighters.Living)
            {
                var idle = result.Frames[0];
                FrameCache.SetStop(look, float.IsNaN(result.BarY) ? idle.Height * (1f - idle.PivotY) : result.BarY);
                FrameCache.SetHead(look, result.HeadX);
            }
            FrameCache.Store(look, label, sequence, result.Frames, ppu, () =>
            {
                if (this == null || look != _look) return;
                if (_labels == null)
                {
                    _labels = result.Labels;
                    _rate = FrameCache.RateFor(look);
                    if (Plugin.CfgVerbose.Value)
                        foreach (string note in result.Notes) Plugin.Log.LogInfo($"[combat] '{_owner?.Login}': {note}");
                }
                if (!_shown)
                {
                    _shown = true;
                    var first = result.Frames[0];
                    Plugin.Log.LogInfo($"[combat] '{_owner?.Login}': doll shown, {first.Width}x{first.Height}, {ppu:0} px per unit");
                    if (_view == null || _view.sprite == null) Show("stop", 0);
                    Fighters.Prune();
                }
                else if (Plugin.CfgVerbose.Value)
                    Plugin.Log.LogInfo($"[combat] '{_owner?.Login}': {label} ready, frames {result.Frames.Count}, {clock.ElapsedMilliseconds} ms");
            }, urgent || soon || (this != null && look == _look && _playing == label));
        }), priority, who, sequence);
    }

    private void Show(string label, int frame)
    {
        string key = FrameCache.FrameKey(_look, label, FrameCache.Source(FrameCache.SequenceKey(_look, label), frame));
        if (key == _shownKey && _view.sprite != null)
        {
            Settle();
            return;
        }
        if (!Waited()) return;
        if (FrameCache.TryGet(key, out var sprite))
        {
            Settle();
            Jumped(label, frame);
            _view.sprite = sprite;
            _viewLook = _look;
            _shownKey = key;
            if (!_modelHidden) HideModel();
            return;
        }
        Miss(label, frame);
        string spare = Spare(label) ?? _viewLook;
        if (spare == null || spare == _look) return;
        string stale = FrameCache.FrameKey(spare, label, FrameCache.Source(FrameCache.SequenceKey(spare, label), frame));
        if (stale != _shownKey && FrameCache.TryGet(stale, out var old))
        {
            if (!_fallbackLogged && Trace.On)
            {
                _fallbackLogged = true;
                Trace.Write($"'{Who}' frame {label}:{frame} of look {Trace.Look(_look)} not ready, showing frame of look {Trace.Look(spare)}");
            }
            _view.sprite = old;
            _shownKey = stale;
        }
    }

    private bool _fallbackLogged;
    private string _shownLabel;

    private void Older(string look)
    {
        if (look == null || look == _look || look == _otherLook) return;
        _older.Remove(look);
        _older.Insert(0, look);
        if (_older.Count > 4) _older.RemoveAt(_older.Count - 1);
    }

    internal IReadOnlyList<string> OlderLooks => _older;

    private string Spare(string label)
    {
        if (_look == null || label == null) return null;
        bool left = Fighters.FacesLeft(_look);
        if (_viewLook != null && _viewLook != _look && Fighters.FacesLeft(_viewLook) == left && FrameCache.HasSequence(FrameCache.SequenceKey(_viewLook, label))) return _viewLook;
        foreach (string look in _older)
            if (Fighters.FacesLeft(look) == left && FrameCache.HasSequence(FrameCache.SequenceKey(look, label))) return look;
        return null;
    }

    private string PlayLook(string label)
    {
        if (label == null || _look == null || FrameCache.HasSequence(FrameCache.SequenceKey(_look, label))) return _look;
        return Spare(label) ?? _look;
    }

    private bool Has(string label) => label != null && _look != null && FrameCache.HasSequence(FrameCache.SequenceKey(PlayLook(label), label));
    private int _shownFrame = -1;
    private bool _glideNoted;
    private string _shownLook;

    private void Jumped(string label, int frame)
    {
        _fallbackLogged = false;
        if (Trace.On && _shownLabel == label)
        {
            if (_shownLook != _look)
                Trace.Write($"'{Who}' frame {label}:{frame} changed look {Trace.Look(_shownLook)} → {Trace.Look(_look)}");
            else if (label == "move")
            {
                int cycle = Math.Max(1, FrameCache.CountOf(FrameCache.SequenceKey(_look, label)));
                int step = ((frame - _shownFrame) % cycle + cycle) % cycle;
                if (step > 3) Trace.Write($"'{Who}' walk skipped {_shownFrame} → {frame} ({step} frames)");
            }
        }
        _shownLabel = label;
        _shownFrame = frame;
        _shownLook = _look;
    }

    private void Miss(string label, int frame)
    {
        float now = Time.unscaledTime;
        if (_missSince <= 0f)
        {
            _missSince = now;
            _missLogged = false;
            _missWhat = $"{label}:{frame} look {Trace.Look(_look)}";
            return;
        }
        if (_missLogged || now - _missSince < 1f || !Trace.On) return;
        _missLogged = true;
        Trace.Write($"'{Who}' frame {_missWhat} NOT READY for 1 s, on screen {(_view.sprite != null ? "old frame of look " + Trace.Look(_viewLook) : "nothing")}, sequence {FrameCache.StateOf(FrameCache.SequenceKey(_look, label))}");
    }

    private void Settle()
    {
        if (_missSince <= 0f) return;
        float waited = Time.unscaledTime - _missSince;
        _missSince = 0f;
        if (waited >= 0.15f && Trace.On) Trace.Write($"'{Who}' frame {_missWhat} arrived after {waited * 1000f:0} ms");
    }
}

internal sealed class RingMaterials : MonoBehaviour
{
    internal readonly List<Material> Owned = new();

    private void OnDestroy()
    {
        foreach (var material in Owned)
            if (material != null) Destroy(material);
        Owned.Clear();
    }
}
