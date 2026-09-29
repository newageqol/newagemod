namespace NewAge.Swf.Display;

public sealed class DisplayObject
{
    public int Depth { get; set; }
    public int CharacterId { get; set; }
    public SwfMatrix Matrix { get; set; } = SwfMatrix.Identity;
    public SwfColorTransform ColorTransform { get; set; } = SwfColorTransform.Identity;
    public double Ratio { get; set; }
    public string Name { get; set; }
    public int ClipDepth { get; set; }
    public bool Visible { get; set; } = true;
    public SwfBlendMode BlendMode { get; set; } = SwfBlendMode.Normal;
    public IReadOnlyList<SwfFilter> Filters { get; set; }
    public int ButtonState { get; set; }

    public MovieClip Clip { get; set; }

    public bool IsMask => ClipDepth > 0;

    public override string ToString() =>
        $"depth={Depth} char={CharacterId}{(Name is null ? "" : $" '{Name}'")}";
}

public sealed class MovieClip(SwfMovie movie, SwfTimeline timeline, int characterId)
{
    public static Action<MovieClip, SwfFrame> FrameScript;
    public static Action<MovieClip, DisplayObject> ChildCreated;

    private readonly SortedDictionary<int, DisplayObject> _displayList = new();
    private readonly SortedDictionary<int, DisplayObject> _attached = new();

    public SwfMovie Movie { get; } = movie;
    public SwfTimeline Timeline { get; } = timeline;
    public int CharacterId { get; } = characterId;

    public int CurrentFrame { get; private set; } = -1;
    public bool Playing { get; set; } = true;
    public string Name { get; set; }

    public int FrameCount => Timeline.FrameCount;

    private List<DisplayObject> _layersCache;

    public IReadOnlyList<DisplayObject> Layers
    {
        get
        {
            if (_layersCache is null)
            {
                var merged = new List<DisplayObject>(_displayList.Count + _attached.Count);
                merged.AddRange(_displayList.Values);
                merged.AddRange(_attached.Values);
                merged.Sort(static (a, b) => a.Depth.CompareTo(b.Depth));
                _layersCache = merged;
            }
            return _layersCache;
        }
    }

    public void Play() => Playing = true;
    public void Stop() => Playing = false;

    public bool GotoAndPlay(string label)
    {
        int frame = Timeline.FrameOf(label);
        if (frame < 0) return false;
        GotoFrame(frame);
        Playing = true;
        return true;
    }

    public bool GotoAndStop(string label)
    {
        int frame = Timeline.FrameOf(label);
        if (frame < 0) return false;
        GotoFrame(frame);
        Playing = false;
        return true;
    }

    public void GotoAndStop(int frame)
    {
        GotoFrame(frame);
        Playing = false;
    }

    public void GotoFrame(int frame)
    {
        frame = Math.Clamp(frame, 0, FrameCount - 1);

        if (frame < CurrentFrame || CurrentFrame < 0)
        {
            _displayList.Clear();
            _layersCache = null;
            CurrentFrame = -1;
        }

        while (CurrentFrame < frame)
        {
            CurrentFrame++;
            ApplyFrame(CurrentFrame, honorStop: CurrentFrame == frame);
        }
    }

    public void Advance()
    {
        if (Playing)
        {
            if (CurrentFrame + 1 >= FrameCount)
            {
                if (FrameCount > 1) GotoFrame(0);
            }
            else
            {
                ApplyFrame(++CurrentFrame, honorStop: true);
            }
        }
        else if (CurrentFrame < 0)
        {
            GotoFrame(0);
        }

        foreach (var layer in _displayList.Values) layer.Clip?.Advance();
        foreach (var layer in _attached.Values) layer.Clip?.Advance();
    }

    private void ApplyFrame(int frame, bool honorStop = false)
    {
        if (frame < 0 || frame >= FrameCount) return;

        foreach (object command in Timeline.Frames[frame].Commands)
        {
            switch (command)
            {
                case SwfRemoveCommand remove:
                    if (_displayList.Remove(remove.Depth)) _layersCache = null;
                    break;

                case SwfPlaceCommand place:
                    ApplyPlace(place);
                    break;
            }
        }

        if (honorStop && Timeline.Frames[frame].HasStop) Playing = false;
        if (honorStop && Timeline.Frames[frame].Actions.Count > 0) FrameScript?.Invoke(this, Timeline.Frames[frame]);
    }

    private void ApplyPlace(SwfPlaceCommand place)
    {
        _displayList.TryGetValue(place.Depth, out var target);

        if (target is null || !place.IsMove || (place.CharacterId is { } newId && newId != target.CharacterId))
        {
            target = new DisplayObject { Depth = place.Depth };
            _displayList[place.Depth] = target;
            _layersCache = null;
        }

        if (place.CharacterId is { } characterId && characterId != target.CharacterId)
        {
            target.CharacterId = characterId;
            target.Clip = null;

            if (Movie.KindOf(characterId) == SwfCharacterKind.Sprite)
            {
                var childTimeline = Movie.GetTimeline(characterId);
                if (childTimeline is not null)
                {
                    target.Clip = new MovieClip(Movie, childTimeline, characterId) { Name = place.Name };
                    if (place.Name is not null) target.Name = place.Name;
                    ChildCreated?.Invoke(this, target);
                    target.Clip.GotoFrame(0);
                }
            }
        }

        if (place.Matrix is { } m) target.Matrix = m;
        if (place.ColorTransform is { } cx) target.ColorTransform = cx;
        if (place.Ratio is { } ratio) target.Ratio = ratio;
        if (place.Name is not null) { target.Name = place.Name; if (target.Clip is not null) target.Clip.Name = place.Name; }
        if (place.ClipDepth is { } clip) target.ClipDepth = clip;
        if (place.BlendMode is { } blend) target.BlendMode = blend;
        if (place.Visible is { } visible) target.Visible = visible;
        if (place.Filters is not null) target.Filters = place.Filters;
    }

    public MovieClip FindChild(string name)
    {
        foreach (var layer in Layers)
            if (layer.Clip is not null && string.Equals(layer.Name, name, StringComparison.Ordinal))
                return layer.Clip;
        return null;
    }

    public MovieClip FindDescendant(string name)
    {
        var direct = FindChild(name);
        if (direct is not null) return direct;

        foreach (var layer in Layers)
        {
            var found = layer.Clip?.FindDescendant(name);
            if (found is not null) return found;
        }
        return null;
    }

    public MovieClip AttachMovie(string exportName, string instanceName = null, int? depth = null)
    {
        var clip = Movie.CreateInstance(exportName);
        if (clip is null) return null;

        clip.Name = instanceName ?? exportName;
        clip.GotoFrame(0);

        int at = depth ?? NextHighestDepth();
        _attached[at] = new DisplayObject
        {
            Depth = at,
            CharacterId = clip.CharacterId,
            Name = clip.Name,
            Clip = clip,
        };
        _layersCache = null;
        return clip;
    }

    public MovieClip AttachExternal(MovieClip clip, string instanceName = null, int? depth = null)
    {
        if (clip is null) return null;

        clip.Name = instanceName ?? clip.Name;
        if (clip.CurrentFrame < 0) clip.GotoFrame(0);

        int at = depth ?? NextHighestDepth();
        _attached[at] = new DisplayObject
        {
            Depth = at,
            CharacterId = clip.CharacterId,
            Name = clip.Name,
            Clip = clip,
        };
        _layersCache = null;
        return clip;
    }

    public bool RemoveAttached(string instanceName)
    {
        foreach (var (depth, layer) in _attached)
        {
            if (string.Equals(layer.Name, instanceName, StringComparison.Ordinal))
            {
                _attached.Remove(depth);
                _layersCache = null;
                return true;
            }
        }
        return false;
    }

    public void ClearAttached()
    {
        _attached.Clear();
        _layersCache = null;
    }

    public void ClearContent()
    {
        _displayList.Clear();
        _attached.Clear();
        _layersCache = null;
    }

    public void SwapDepths(int first, int second)
    {
        if (first == second) return;
        DisplayObject Take(int depth, out bool attached)
        {
            if (_attached.Remove(depth, out var layer)) { attached = true; return layer; }
            if (_displayList.Remove(depth, out layer)) { attached = false; return layer; }
            attached = false;
            return null;
        }
        var a = Take(first, out bool aAttached);
        var b = Take(second, out bool bAttached);
        if (a is not null)
        {
            a.Depth = second;
            (aAttached ? _attached : _displayList)[second] = a;
        }
        if (b is not null)
        {
            b.Depth = first;
            (bAttached ? _attached : _displayList)[first] = b;
        }
        _layersCache = null;
    }

    public static MovieClip CreateEmpty(SwfMovie movie, string name)
    {
        var timeline = new SwfTimeline
        {
            CharacterId = -1,
            Frames = new List<SwfFrame> { new() { Index = 0 } },
            Labels = new Dictionary<string, int>(StringComparer.Ordinal),
        };
        return new MovieClip(movie, timeline, -1) { Name = name };
    }

    public bool RemoveAt(int depth)
    {
        bool removed = _attached.Remove(depth) || _displayList.Remove(depth);
        if (removed) _layersCache = null;
        return removed;
    }

    public DisplayObject LayerOf(MovieClip child)
    {
        foreach (var layer in Layers)
            if (ReferenceEquals(layer.Clip, child))
                return layer;
        return null;
    }

    public DisplayObject AttachChild(MovieClip clip, string name, int depth)
    {
        clip.Name = name;
        var layer = new DisplayObject { Depth = depth, CharacterId = clip.CharacterId, Name = name, Clip = clip };
        _attached[depth] = layer;
        _layersCache = null;
        ChildCreated?.Invoke(this, layer);
        if (clip.CurrentFrame < 0 && clip.FrameCount > 0) clip.GotoFrame(0);
        return layer;
    }

    public int NextHighestDepth()
    {
        int max = 0;
        foreach (int depth in _displayList.Keys) if (depth > max) max = depth;
        foreach (int depth in _attached.Keys) if (depth > max) max = depth;
        return max + 1;
    }

    public override string ToString() =>
        $"MovieClip #{CharacterId} '{Name}' кадр {CurrentFrame + 1}/{FrameCount}";
}
