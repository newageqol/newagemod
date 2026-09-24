using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;

namespace NewAgeQoL
{
    internal sealed class WardrobePicture
    {
        internal int Job;
        internal int Width;
        internal int Height;
        internal float PivotX;
        internal float PivotY;
        internal float BodyHeight;
        internal byte[] Rgba;
        internal string Error;
    }

    internal static class WardrobeDoll
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static bool _looked;
        private static Type _request;
        private static Type _wear;
        private static FieldInfo _store;
        private static MethodInfo _render;
        private static MethodInfo _run;
        private static FieldInfo _race, _gender, _scale, _list, _slot, _thing, _image;
        private static FieldInfo _width, _height, _pivotX, _pivotY, _body, _rgba, _error;

        private static readonly object Gate = new object();
        private static WardrobePicture _ready;
        private static int _job;

        internal static bool Available()
        {
            if (!_looked) Look();
            return _run != null && _render != null && _request != null;
        }

        private static void Look()
        {
            _looked = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue("newage.2d", out var info) || info.Instance == null)
                {
                    Plugin.Trace("[переодевалка] NewAge2D не установлен, куклы не будет");
                    return;
                }
                var asm = info.Instance.GetType().Assembly;
                _request = asm.GetType("NewAge2D.DollRequest");
                _wear = asm.GetType("NewAge2D.DollWear");
                var picture = asm.GetType("NewAge2D.DollPicture");
                var doll = asm.GetType("NewAge2D.Doll");
                var worker = asm.GetType("NewAge2D.DollWorker");
                var storeType = asm.GetType("NewAge2D.SwfStore");
                if (_request == null || _wear == null || picture == null || doll == null || worker == null || storeType == null)
                {
                    Plugin.Warn("[переодевалка] в NewAge2D нет нужных типов куклы");
                    _request = null;
                    return;
                }
                _store = worker.GetField("Store", Any);
                _render = doll.GetMethod("Render", Any, null, new[] { _request, storeType }, null);
                _run = worker.GetMethod("Run", Any, null, new[] { typeof(Func<object>), typeof(Action<object>), typeof(int) }, null);
                _race = _request.GetField("Race", Any);
                _gender = _request.GetField("Gender", Any);
                _scale = _request.GetField("Scale", Any);
                _list = _request.GetField("Wear", Any);
                _slot = _wear.GetField("Slot", Any);
                _thing = _wear.GetField("ThingId", Any);
                _image = _wear.GetField("Image", Any);
                _width = picture.GetField("Width", Any);
                _height = picture.GetField("Height", Any);
                _pivotX = picture.GetField("PivotX", Any);
                _pivotY = picture.GetField("PivotY", Any);
                _body = picture.GetField("BodyHeight", Any);
                _rgba = picture.GetField("Rgba", Any);
                _error = picture.GetField("Error", Any);
                if (_store == null || _render == null || _run == null || _list == null || _rgba == null)
                {
                    Plugin.Warn("[переодевалка] у куклы NewAge2D другой вид, рисовать не берусь");
                    _run = null;
                    return;
                }
                Plugin.Trace("[переодевалка] кукла NewAge2D подключена");
            }
            catch (Exception e)
            {
                Plugin.Warn("[переодевалка] кукла NewAge2D: " + e.Message);
                _run = null;
            }
        }

        internal static int Request(int race, int gender, float scale, IEnumerable<KeyValuePair<int, WardrobeThing>> wear)
        {
            if (!Available()) return 0;
            int job;
            lock (Gate) job = ++_job;
            try
            {
                var request = Activator.CreateInstance(_request);
                _race.SetValue(request, race);
                _gender.SetValue(request, gender);
                _scale.SetValue(request, scale);
                var list = _list.GetValue(request) as IList;
                foreach (var pair in wear)
                {
                    if (pair.Value == null || string.IsNullOrEmpty(pair.Value.Image)) continue;
                    var item = Activator.CreateInstance(_wear);
                    _slot.SetValue(item, pair.Key);
                    _thing.SetValue(item, pair.Value.Id);
                    _image.SetValue(item, pair.Value.Image);
                    list?.Add(item);
                }
                var store = _store.GetValue(null);
                Func<object> work = () => _render.Invoke(null, new[] { request, store });
                Action<object> done = result => Finish(job, result);
                _run.Invoke(null, new object[] { work, done, 0 });
            }
            catch (Exception e)
            {
                Plugin.Warn("[переодевалка] кукла не заказана: " + e.Message);
                return 0;
            }
            return job;
        }

        private static void Finish(int job, object result)
        {
            var picture = new WardrobePicture { Job = job };
            try
            {
                if (result == null) picture.Error = "пустой ответ";
                else if (result is Exception ex) picture.Error = (ex.InnerException ?? ex).Message;
                else
                {
                    picture.Width = (int)_width.GetValue(result);
                    picture.Height = (int)_height.GetValue(result);
                    picture.PivotX = (float)_pivotX.GetValue(result);
                    picture.PivotY = (float)_pivotY.GetValue(result);
                    picture.BodyHeight = (float)_body.GetValue(result);
                    picture.Rgba = _rgba.GetValue(result) as byte[];
                    picture.Error = _error.GetValue(result) as string;
                }
            }
            catch (Exception e) { picture.Error = e.Message; }
            lock (Gate)
            {
                if (job == _job) _ready = picture;
            }
        }

        internal static WardrobePicture Take()
        {
            lock (Gate)
            {
                var picture = _ready;
                _ready = null;
                return picture;
            }
        }

        internal static void Forget()
        {
            lock (Gate)
            {
                _job++;
                _ready = null;
            }
        }
    }
}
