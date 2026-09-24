using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.Combat.Animation;
using UnityEngine;

namespace NewAge2D;

[HarmonyPatch]
internal static class FlashQueue
{
    private const float Frame = 0.05f;
    private const float ChildStep = 0.4f;
    private const float EmptySeconds = 1f;
    private const float SoundDelay = 0.5f;
    private const float NumberSeconds = 0.95f;
    private const float EffectSeconds = 1.45f;
    private const float DieSeconds = 1.2f;
    private const float Patience = 15f;

    private static readonly FieldInfo WaitingField = AccessTools.Field(typeof(AnimationProcessor), "groups");
    private static readonly FieldInfo PlayingField = AccessTools.Field(typeof(AnimationProcessor), "activeGroups");
    private static readonly FieldInfo ActiveField = AccessTools.Field(typeof(AnimationProcessor), "_active");
    private static readonly MethodInfo FinishedMethod = AccessTools.Method(typeof(AnimationProcessor), "fireAnimationFinishedEvent");
    private static readonly FieldInfo ItemsField = AccessTools.Field(typeof(AnimationGroup), "_items");
    private static readonly FieldInfo WoundsField = AccessTools.Field(typeof(AnimationGroup), "_wounds");
    private static readonly FieldInfo BlocksField = AccessTools.Field(typeof(AnimationGroup), "_blocks");
    private static readonly FieldInfo StartTimeField = AccessTools.Field(typeof(ChangeLifeAnimationItem), "_startTime");
    private static readonly int IdleHash = Animator.StringToHash("idle");
    private static readonly MethodInfo KickTextMethod = AccessTools.Method(typeof(KickAnimationItem), "ShowAnimatedText");

    private static bool On => Plugin.FlashFight && WaitingField != null && PlayingField != null && ActiveField != null && FinishedMethod != null
        && ItemsField != null && WoundsField != null && BlocksField != null;

    private sealed class Play
    {
        public AnimationItem Item;
        public int Kind;
        public float At;
        public float Until;
        public float Began = -1f;
        public bool NeedStart;
        public bool Body;
        public int Request;
    }

    private sealed class Source
    {
        public Group Group;
        public AbstractCharacter Who;
        public readonly List<int> Types = new();
        public readonly Dictionary<int, Queue<AnimationItem>> Queues = new();
        public readonly List<Play> Current = new();
        public readonly Queue<AnimationItem> Children = new();
        public bool Timer;
        public float TimerAt;
    }

    private sealed class Group
    {
        public readonly List<Source> Sources = new();
        public readonly HashSet<int> Locks = new();
        public readonly Dictionary<CharacterIndicators, int> Versions = new();
        public int Number;
        public int RoundNum;
        public RoundType RoundType;
        public bool EndBattlePhase;
        public bool Started;
        public float StartedAt;

        public bool Fresh(CharacterIndicators indicators) =>
            !Versions.TryGetValue(indicators, out int version) || version == FlashNumbers.VersionOf(indicators);
    }

    private static readonly ConditionalWeakTable<AnimationGroup, List<AnimationItem>> Original = new();
    private static readonly ConditionalWeakTable<AnimationGroup, Group> Models = new();
    private static readonly HashSet<int> Locked = new();
    private static readonly List<Source> Timers = new();
    private static readonly List<(float At, AbstractCharacter Who, string Sound)> Sounds = new();
    private static readonly HashSet<AbstractCharacter> Dying = new();
    private static AnimationProcessor _processor;
    private static int _numbered;

    internal static bool DeathCall { get; private set; }

    internal static void Clear()
    {
        Locked.Clear();
        Timers.Clear();
        Sounds.Clear();
        Dying.Clear();
        _processor = null;
    }

    private static void Bind(AnimationProcessor processor)
    {
        if (ReferenceEquals(_processor, processor)) return;
        Clear();
        _processor = processor;
    }

    private static List<AnimationGroup> Waiting(AnimationProcessor processor) => (List<AnimationGroup>)WaitingField.GetValue(processor);

    private static List<AnimationGroup> Playing(AnimationProcessor processor) => (List<AnimationGroup>)PlayingField.GetValue(processor);

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationGroup), "Items", MethodType.Setter)]
    private static bool KeepOrder(AnimationGroup __instance, List<AnimationItem> value)
    {
        if (!On || value == null) return true;
        var items = new List<AnimationItem>();
        List<AnimationItem> wounds = null;
        List<AnimationItem> blocks = null;
        foreach (var item in value)
        {
            if (item == null) continue;
            if (item.AnimationType != AnimationItemType.TARGET_USER_CHANGE_STANCE) items.Add(item);
            else if (item.AnimationName == "wound") (wounds ??= new List<AnimationItem>()).Add(item);
            else if (item.AnimationName == "evade" || item.AnimationName == "block") (blocks ??= new List<AnimationItem>()).Add(item);
        }
        if (items.Count == 0 && wounds != null)
        {
            items = wounds;
            wounds = null;
        }
        ItemsField.SetValue(__instance, items);
        WoundsField.SetValue(__instance, wounds);
        BlocksField.SetValue(__instance, blocks);
        Original.Remove(__instance);
        Original.Add(__instance, new List<AnimationItem>(value));
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationProcessor), "SortGroups")]
    private static bool KeepArrival() => !On;

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationProcessor), "FindGroupForGlue")]
    private static bool NoGlue(ref AnimationGroup __result)
    {
        if (!On) return true;
        __result = null;
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(AnimationProcessor), "AddGroup")]
    private static void Arrived(AnimationProcessor __instance, AnimationGroup group)
    {
        if (!On || group == null) return;
        try
        {
            Bind(__instance);
            var model = ModelOf(group);
            if (Trace.On) Trace.Write($"очередь Flash: группа №{model.Number} пришла ({group.actionType}{(model.EndBattlePhase ? ", в фазе хода" : "")}): {Describe(model)}");
            Promote(__instance);
        }
        catch (Exception ex) { Plugin.Log.LogError("[очередь] приход группы: " + ex); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationProcessor), "StartGroupProcessing")]
    private static bool RunFlash(AnimationProcessor __instance, ref IEnumerator __result)
    {
        if (!On) return true;
        __result = Run(__instance);
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationProcessor), "Clear")]
    private static bool StopAnimations(AnimationProcessor __instance)
    {
        if (!On) return true;
        try { Stop(__instance); }
        catch (Exception ex) { Plugin.Log.LogError("[очередь] новая фаза: " + ex); }
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AbstractCharacter), "PlayDeathAnimation")]
    private static bool QueueDeath(AbstractCharacter __instance)
    {
        if (!On || DeathCall) return true;
        try
        {
            var combat = Fighters.Combat();
            var processor = combat?.AnimationProcessor;
            if (processor == null || !combat.Characters.ContainsKey(__instance.UserId)) return true;
            var doll = Fighters.DollOf(__instance);
            if (doll != null && doll.CameDead)
            {
                if (Trace.On) Trace.Write($"«{NameOf(__instance)}» был мёртв ещё до входа в бой: ролик смерти в очередь не ставлю");
                return true;
            }
            if (Dying.Add(__instance)) Die(__instance, combat, processor);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("[очередь] смерть: " + ex);
            return true;
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationEventHandler), "startWound")]
    private static bool NoWound() => !On;

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationEventHandler), "startBlock")]
    private static bool NoBlock() => !On;

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationEventHandler), "InternalArrowHide")]
    private static bool NoArrow() => !On;

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationEventHandler), "startMove")]
    private static bool NoThrow() => !On;

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationEventHandler), "startShoot")]
    private static bool NoShot() => !On;

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationEventHandler), "startParabShoot")]
    private static bool NoParabola() => !On;

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationEventHandler), "startReverseShot")]
    private static bool NoReverse() => !On;

    [HarmonyPrefix, HarmonyPatch(typeof(DistanceAttack), "StartAttack")]
    private static bool NoMissile() => !On;

    private static void Die(AbstractCharacter who, ICombatData combat, AnimationProcessor processor)
    {
        var group = new AnimationGroup(ActionType.WITHOUTHANDLE, processor, combat.RoundNum);
        group.Items = new List<AnimationItem> { new AnimationItem(group, who, who, "die", 0, AnimationItemType.SOURCE_USER_CHANGE_STANCE) };
        group.characters = new List<int> { who.UserId };
        var model = ModelOf(group);
        model.EndBattlePhase = true;
        if (Trace.On) Trace.Write($"«{NameOf(who)}» сервер прислал смерть: группа смерти №{model.Number} встаёт в конец очереди");
        processor.AddGroup(group);
        if (!processor.Active) BaseLocationView.ExecuteCoroutine(processor.StartGroupProcessing());
    }

    private static IEnumerator Run(AnimationProcessor processor)
    {
        Bind(processor);
        ActiveField.SetValue(processor, true);
        float next = Time.time;
        while (Playing(processor).Count > 0 || Waiting(processor).Count > 0)
        {
            if (Time.time >= next)
            {
                next += Frame;
                if (next < Time.time) next = Time.time + Frame;
                Step(processor, Time.time);
            }
            yield return null;
        }
        ActiveField.SetValue(processor, false);
        if (Trace.On) Trace.Write("очередь Flash: все группы доиграли, конец анимаций");
        FinishedMethod.Invoke(processor, new object[] { 0 });
    }

    private static void Step(AnimationProcessor processor, float now)
    {
        var playing = Playing(processor);
        bool removed = false;
        for (int i = playing.Count - 1; i >= 0; i--)
        {
            var model = ModelOf(playing[i]);
            bool over;
            try
            {
                if (model.Started)
                    foreach (var source in model.Sources)
                        Check(source, now);
                over = model.Started && (model.Sources.All(source => source.Current.Count == 0) || now - model.StartedAt > Patience);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[очередь] группа №{model.Number}: {ex}");
                over = true;
            }
            if (!over) continue;
            Unlock(model);
            playing.RemoveAt(i);
            removed = true;
            if (Trace.On) Trace.Write($"очередь Flash: группа №{model.Number} доиграла через {(now - model.StartedAt) * 1000f:0} мс{(now - model.StartedAt > Patience ? ", ОБОРВАНА по времени" : "")}");
        }
        if (removed && Waiting(processor).Count > 0) Promote(processor);
        foreach (var group in playing.ToArray())
        {
            var model = ModelOf(group);
            if (model.Started || !Ready(model)) continue;
            try { Start(model, now); }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[очередь] старт группы №{model.Number}: {ex}");
                model.Started = true;
                model.StartedAt = now;
                foreach (var source in model.Sources) source.Current.Clear();
            }
        }
    }

    private static void Stop(AnimationProcessor processor)
    {
        Bind(processor);
        var playing = Playing(processor);
        var waiting = Waiting(processor);
        int dropped = 0;
        for (int i = playing.Count - 1; i >= 0; i--)
        {
            var model = ModelOf(playing[i]);
            if (model.EndBattlePhase) continue;
            Unlock(model);
            playing.RemoveAt(i);
            dropped++;
        }
        for (int i = waiting.Count - 1; i >= 0; i--)
        {
            if (ModelOf(waiting[i]).EndBattlePhase) continue;
            waiting.RemoveAt(i);
            dropped++;
        }
        if (Trace.On) Trace.Write($"очередь Flash: началась фаза хода, недоигранных групп снято {dropped}, осталось {playing.Count + waiting.Count}");
        if (waiting.Count > 0) Promote(processor);
    }

    private static Group ModelOf(AnimationGroup group)
    {
        if (Models.TryGetValue(group, out var model)) return model;
        model = new Group { Number = ++_numbered };
        var combat = Fighters.Combat();
        if (combat != null)
        {
            model.RoundNum = combat.RoundNum;
            model.RoundType = combat.RoundType;
        }
        model.EndBattlePhase = model.RoundType == RoundType.WALK_ROUND;
        foreach (var item in TopOf(group))
        {
            if (item == null) continue;
            var source = model.Sources.FirstOrDefault(known => ReferenceEquals(known.Who, item.Source));
            if (source == null)
            {
                source = new Source { Group = model, Who = item.Source };
                model.Sources.Add(source);
            }
            int type = (int)item.AnimationType;
            if (!source.Queues.TryGetValue(type, out var queue))
            {
                queue = new Queue<AnimationItem>();
                source.Queues[type] = queue;
                source.Types.Add(type);
            }
            queue.Enqueue(item);
            Remember(model, item, 0);
        }
        Models.Add(group, model);
        return model;
    }

    private static List<AnimationItem> TopOf(AnimationGroup group)
    {
        if (Original.TryGetValue(group, out var list)) return list;
        var items = new List<AnimationItem>();
        foreach (var field in new[] { ItemsField, WoundsField, BlocksField })
            if (field.GetValue(group) is List<AnimationItem> part)
                items.AddRange(part);
        return items;
    }

    private static void Remember(Group model, AnimationItem item, int depth)
    {
        if (item == null || depth > 8) return;
        if (item.Source != null) model.Locks.Add(item.Source.UserId);
        Snapshot(model, item.Source);
        Snapshot(model, item.Target);
        if (item.Items != null)
            foreach (var child in item.Items)
                Remember(model, child, depth + 1);
    }

    private static void Snapshot(Group model, AbstractCharacter who)
    {
        var indicators = who?.Indicators;
        if (indicators != null && !model.Versions.ContainsKey(indicators)) model.Versions[indicators] = FlashNumbers.VersionOf(indicators);
    }

    private static void Promote(AnimationProcessor processor)
    {
        var waiting = Waiting(processor);
        var playing = Playing(processor);
        for (int i = 0; i < waiting.Count;)
        {
            var model = ModelOf(waiting[i]);
            if (model.Locks.Any(Locked.Contains))
            {
                i++;
                continue;
            }
            Locked.UnionWith(model.Locks);
            playing.Add(waiting[i]);
            waiting.RemoveAt(i);
            if (Trace.On) Trace.Write($"очередь Flash: группа №{model.Number} заняла бойцов: {string.Join(", ", model.Locks.Select(NameOf))}");
        }
    }

    private static void Unlock(Group model) => Locked.ExceptWith(model.Locks);

    private static bool Ready(Group model)
    {
        foreach (var source in model.Sources)
            if (source.Who != null && !ClipReady(source.Who))
                return false;
        return true;
    }

    internal static bool ClipReady(AbstractCharacter who)
    {
        if (!who.Initialized) return true;
        var doll = Fighters.DollOf(who);
        if (doll != null) return !doll.Acting && !doll.Gliding && who.MoverState == MoverState.Idle;
        if (who.Dead) return true;
        return who.MoverState == MoverState.Idle && Idle(who);
    }

    private static bool Idle(AbstractCharacter who)
    {
        var animator = who.CharacterAnimator;
        if (animator == null || !animator.isActiveAndEnabled) return true;
        return !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).tagHash == IdleHash;
    }

    private static void Start(Group model, float now)
    {
        model.Started = true;
        model.StartedAt = now;
        if (Trace.On) Trace.Write($"очередь Flash: группа №{model.Number} стартует");
        foreach (var source in model.Sources)
            foreach (int type in source.Types)
                if (source.Queues[type].Count > 0)
                    source.Current.Add(Begin(source, source.Queues[type].Dequeue(), now, false));
    }

    private static void Check(Source source, float now)
    {
        for (int i = source.Current.Count - 1; i >= 0; i--)
        {
            var play = source.Current[i];
            if (play.NeedStart)
            {
                play.NeedStart = false;
                StartBody(play, now);
            }
            if (!Stopped(play, now)) continue;
            if (Trace.On) Trace.Write($"«{NameOf(play.Item.Source)}» {Describe(play.Item)} закончилось через {(now - play.At) * 1000f:0} мс");
            Children(source, play.Item, now);
            int type = (int)play.Item.AnimationType;
            if (source.Queues.TryGetValue(type, out var queue) && queue.Count > 0) source.Current[i] = Begin(source, queue.Dequeue(), now, false);
            else source.Current.RemoveAt(i);
        }
    }

    private static void Children(Source source, AnimationItem item, float now)
    {
        if (item.Items != null)
            foreach (var child in item.Items)
                if (child != null)
                    source.Children.Enqueue(child);
        if (!source.Timer && source.Children.Count > 0) NextChild(source, now);
    }

    private static void NextChild(Source source, float now)
    {
        if (source.Children.Count == 0) return;
        Begin(source, source.Children.Dequeue(), now, true);
        source.Timer = true;
        source.TimerAt = now + ChildStep;
        if (!Timers.Contains(source)) Timers.Add(source);
    }

    internal static void Tick()
    {
        if (Timers.Count == 0 && Sounds.Count == 0) return;
        float now = Time.time;
        for (int i = Timers.Count - 1; i >= 0; i--)
        {
            var source = Timers[i];
            if (source.Timer && now < source.TimerAt) continue;
            source.Timer = false;
            try { NextChild(source, now); }
            catch (Exception ex) { Plugin.Log.LogError("[очередь] следующее действие ребёнка: " + ex); }
            if (!source.Timer) Timers.RemoveAt(i);
        }
        for (int i = Sounds.Count - 1; i >= 0; i--)
        {
            var (at, who, sound) = Sounds[i];
            if (now < at) continue;
            Sounds.RemoveAt(i);
            try
            {
                if (who != null && who.Initialized) who.PlayCombatSound(sound);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[очередь] звук: " + ex.Message); }
        }
    }

    private static Play Begin(Source source, AnimationItem item, float now, bool child)
    {
        var model = source.Group;
        var play = new Play { Item = item, At = now, Kind = KindOf(item) };
        string name = item.AnimationName ?? "";
        var who = item.Source;
        if (Trace.On) Trace.Write($"«{NameOf(who)}» {(child ? "ребёнок " : "")}{Describe(item)} → {NameOf(item.Target)}, группа №{model.Number}");
        ShowTexts(item);
        switch (play.Kind)
        {
            case 1:
                Face(who, item.Target);
                if (name.Contains("cast") && who != null)
                {
                    Aura(who, name);
                    SpellMarks.Show(item, who, name);
                    if (Counts(model)) AddMana(model, who, item.Value);
                }
                string sound = SoundOf(item);
                if (sound != null && who != null) Sounds.Add((now + SoundDelay, who, sound));
                play.NeedStart = !child;
                break;
            case 2:
                Effect(model, play, now);
                break;
        }
        return play;
    }

    private static void ShowTexts(AnimationItem item)
    {
        try
        {
            if (item is KickAnimationItem kick && kick.Target != null && KickTextMethod != null) KickTextMethod.Invoke(kick, new object[] { kick.Target });
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[очередь] надпись над головой: " + (ex.InnerException ?? ex).Message); }
    }

    private static int KindOf(AnimationItem item) => item.AnimationType switch
    {
        AnimationItemType.SOURCE_USER_CHANGE_STANCE => 1,
        AnimationItemType.CHANGE_INDICATOR => 2,
        AnimationItemType.EFFECT => 2,
        _ => 0,
    };

    private static void StartBody(Play play, float now)
    {
        var item = play.Item;
        var who = item.Source;
        play.Began = now;
        if (who == null || !who.Initialized) return;
        if (item.AnimationName == "die")
        {
            Dying.Remove(who);
            if (!who.Dead)
            {
                if (Trace.On) Trace.Write($"«{NameOf(who)}» к очереди смерти уже поднят, смерть не играется");
                return;
            }
            DeathCall = true;
            try { who.PlayDeathAnimation(); }
            finally { DeathCall = false; }
            play.Body = true;
            return;
        }
        string animation = item is KickAnimationItem kick ? kick.GetAnimationName() : item.AnimationName;
        var doll = Fighters.DollOf(who);
        if (doll != null)
        {
            doll.Play(animation ?? "", Fighters.HandOf(item));
            play.Request = doll.LastRequest;
            play.Body = true;
            return;
        }
        var animator = who.CharacterAnimator;
        if (string.IsNullOrEmpty(animation) || animator == null || !animator.parameters.Any(parameter => parameter.name == animation)) return;
        if (who.CharacterAnimationEventHandler != null) who.CharacterAnimationEventHandler.currentItem = item;
        animator.SetBool(animation, true);
        play.Body = true;
    }

    private static bool Stopped(Play play, float now)
    {
        switch (play.Kind)
        {
            case 1:
                if (play.NeedStart) return false;
                var who = play.Item.Source;
                if (who == null || !who.Initialized || !play.Body) return true;
                if (play.Item.AnimationName == "die") return now >= play.Began + DieSeconds;
                var doll = Fighters.DollOf(who);
                if (doll != null) return doll.Finished(play.Request);
                return now >= play.Began + Frame * 2f && Idle(who);
            case 2:
                return now >= play.Until;
            default:
                return now >= play.At + EmptySeconds;
        }
    }

    private static void Face(AbstractCharacter who, AbstractCharacter target)
    {
        if (who == null || target == null || !who.Initialized || !target.Initialized || AbstractCharacter.IsCharacterGameObjectsIdentical(who, target)) return;
        var location = CombatView.Get();
        var eye = location != null ? location.CombatCamera : null;
        float from = eye != null ? eye.WorldToScreenPoint(who.position).x : who.position.x;
        float to = eye != null ? eye.WorldToScreenPoint(target.position).x : target.position.x;
        if (Mathf.Abs(from - to) < 0.5f) return;
        var direction = target.position - who.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        who.RotateTo((int)Quaternion.LookRotation(direction).eulerAngles.y);
    }

    private static void Aura(AbstractCharacter who, string name)
    {
        int at = name.IndexOf('_');
        if (at < 0) return;
        string prefix = name.Substring(at + 1) switch
        {
            "1" => "WM",
            "2" => "BM",
            "3" => "NM",
            _ => null,
        };
        if (prefix != null) SpellFx.Play(who, prefix);
    }

    private static bool Counts(Group model)
    {
        var combat = Fighters.Combat();
        return combat == null || combat.RoundType == RoundType.COMBAT_ROUND || (combat.RoundNum == model.RoundNum && combat.RoundType == model.RoundType);
    }

    private static void AddMana(Group model, AbstractCharacter who, int value)
    {
        var indicators = who.Indicators;
        if (indicators == null || value == 0) return;
        if (!model.Fresh(indicators))
        {
            if (Trace.On) Trace.Write($"«{NameOf(who)}» мана каста {value}: сервер уже прислал итог, не прибавлена");
            return;
        }
        indicators.CurrentMana += value;
    }

    private static void Effect(Group model, Play play, float now)
    {
        var item = play.Item;
        string name = item.AnimationName ?? "";
        var who = item.Target ?? item.Source;
        play.Until = now;
        if (who == null) return;
        if (name.Contains("effect"))
        {
            if (name != "effect" || !who.Initialized) return;
            SpellFx.Play(who, "EF");
            play.Until = now + EffectSeconds;
            return;
        }
        if (name != "change_life" && name != "change_mana" && name != "change_stamina" && name != "change_expower") return;
        int value = item is ChangeLifeAnimationItem change ? change.life : item.Value;
        if (Counts(model)) Apply(model, item, who, name, value);
        else if (Trace.On) Trace.Write($"«{NameOf(who)}» {name} {value}: действие прошлой фазы, значение не прибавлено");
        if (item.IgnoreByPlayAnimation || !who.Initialized) return;
        FlashNumbers.Show(who, name, value);
        play.Until = now + NumberSeconds;
    }

    private static void Apply(Group model, AnimationItem item, AbstractCharacter who, string name, int value)
    {
        if (item is ChangeLifeAnimationItem change && StartTimeField != null)
        {
            if (StartTimeField.GetValue(change) != null)
            {
                if (Trace.On) Trace.Write($"«{NameOf(who)}» {name} {value}: уже применено сразу");
                return;
            }
            StartTimeField.SetValue(change, (float?)0f);
        }
        var indicators = who.Indicators;
        if (indicators == null) return;
        if (!model.Fresh(indicators))
        {
            if (Trace.On) Trace.Write($"«{NameOf(who)}» {name} {value}: сервер уже прислал итог, не прибавлено");
            return;
        }
        switch (name)
        {
            case "change_life":
                indicators.CurrentLife += value;
                break;
            case "change_mana":
                indicators.CurrentMana += value;
                break;
            case "change_expower":
                indicators.CurrentExpower += value;
                break;
            default:
                indicators.CurrentStamina += value;
                break;
        }
    }

    private static string SoundOf(AnimationItem item)
    {
        string name = item.AnimationName ?? "";
        if (!name.StartsWith("attack") && !name.Contains("_left") && !name.Contains("_right")) return null;
        var who = item.Source;
        if (who is PlayerCharacter player)
        {
            var weapon = Fighters.HandOf(item) == "_left" ? player.LeftHandWeapon : player.RightHandWeapon;
            if (weapon == null) return "weapon_strike_sword";
            switch (weapon.ThingSubType)
            {
                case EThingSubType.SHIELD:
                case EThingSubType.STAFF:
                case EThingSubType.HAMMER_ONE_HAND:
                case EThingSubType.HAMMER_TWO_HAND:
                    return "weapon_strike_club";
                case EThingSubType.BOW:
                    return "weapon_strike_arrow";
                default:
                    return "weapon_strike_sword";
            }
        }
        if (who == null || RaceDatabase.Instance == null) return null;
        var data = RaceDatabase.Instance.GetData(who.Race);
        return data != null && !string.IsNullOrEmpty(data.StrikeSound) ? data.StrikeSound : "weapon_strike_default";
    }

    private static string NameOf(AbstractCharacter character) =>
        character == null ? "-" : !string.IsNullOrEmpty(character.Login) ? character.Login : character.UserId.ToString();

    private static string NameOf(int userId)
    {
        var combat = Fighters.Combat();
        return combat != null && combat.Characters.TryGetValue(userId, out var character) ? NameOf(character) : userId.ToString();
    }

    private static string Describe(AnimationItem item) => $"{item.AnimationName} (тип {(int)item.AnimationType}, {item.Value})";

    private static string Describe(Group model) =>
        string.Join("; ", model.Sources.Select(source => $"«{NameOf(source.Who)}»: " + string.Join(", ", source.Types.SelectMany(type => source.Queues[type]).Select(item => Describe(item) + (item.Items != null && item.Items.Count > 0 ? $" + детей {item.Items.Count}" : "")))));
}
