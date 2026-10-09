using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace NewAgeQoL
{
    internal sealed class SpareLink
    {
        internal NetworkStream Stream;
        internal Socket Socket;
        internal readonly List<byte> Acc = new List<byte>();
        internal readonly Queue<string> Early = new Queue<string>();
        internal double? Balance;
    }

    internal sealed class SpareReset : IOException
    {
        internal SpareReset(string message) : base(message) { }
    }

    internal static class SpareSession
    {
        private const string Host = "nura.biz";
        private const int Port = 2000;
        private const double KeepAlive = 40.0;
        private const int Idle = 120;
        private static readonly Regex Money = new Regex("realcash=\"([0-9.]+)\"");

        private sealed class Job
        {
            internal string Name;
            internal bool Background;
            internal Action<SpareLink> Work;
            internal Action<string> Fail;
        }

        private sealed class Session
        {
            internal string Login;
            internal string Key;
            internal int Who;
            internal TcpClient Client;
            internal SpareLink Link;
            internal volatile bool Closing;
            internal bool Ready;
            internal readonly Queue<Job> Jobs = new Queue<Job>();
        }

        private static readonly object Gate = new object();
        private static Session _now;
        private static bool _kicked;

        internal static bool Online { get { lock (Gate) return _now != null && _now.Ready && !_now.Closing; } }

        internal static string Login() => (Plugin.CfgOnlineLogin?.Value ?? "").Trim();

        internal static string Mine()
        {
            try
            {
                var info = Controllers.User?.UserInfo;
                return info != null && !string.IsNullOrEmpty(info.Login) ? info.Login.Trim() : "";
            }
            catch { return ""; }
        }

        private static int Who()
        {
            try
            {
                var info = Controllers.User?.UserInfo;
                return info != null ? info.UserId : 0;
            }
            catch { return 0; }
        }

        private static string Key()
        {
            string ver = (Plugin.CfgOnlineVersion?.Value ?? "").Trim();
            return Login() + "\u0001" + (Plugin.CfgOnlinePassword?.Value ?? "") + "\u0001" + ver;
        }

        internal static string Refuse()
        {
            string login = Login();
            if (login.Length == 0 || string.IsNullOrEmpty(Plugin.CfgOnlinePassword?.Value))
                return "Укажи логин и пароль запасного аккаунта в настройках мода";
            string me = Mine();
            if (me.Length > 0 && string.Equals(me, login, StringComparison.OrdinalIgnoreCase))
                return "В настройках указан тот же персонаж, которым ты играешь — нужен запасной";
            return null;
        }

        internal static bool Run(string name, bool background, Action<SpareLink> work, Action<string> fail)
        {
            string why = Refuse();
            if (why != null) { fail?.Invoke(why); return false; }
            string key = Key();
            int who = Who();
            Session start = null;
            Session old = null;
            lock (Gate)
            {
                if (_now != null && (_now.Closing || _now.Key != key || _now.Who != who))
                {
                    old = _now;
                    _now = null;
                }
                if (_now == null)
                {
                    if (background && _kicked)
                    {
                        fail?.Invoke("spare session failed or was taken over elsewhere, waiting for a manual request");
                        return false;
                    }
                    _kicked = false;
                    start = new Session { Login = Login(), Key = key, Who = who };
                    _now = start;
                }
                _now.Jobs.Enqueue(new Job { Name = name, Background = background, Work = work, Fail = fail });
            }
            if (old != null) Shut(old, "settings or character changed");
            if (start != null)
            {
                var t = new Thread(() => Loop(start)) { IsBackground = true, Name = "QoLSpareSession" };
                t.Start();
            }
            return true;
        }

        internal static void Tick()
        {
            Session s;
            lock (Gate) s = _now;
            if (s == null) return;
            string why = null;
            if (!SideButtons.InWorld()) why = "left the game world";
            else if (Who() != s.Who) why = "character changed";
            else if (Key() != s.Key) why = "spare account settings changed";
            else if (Refuse() != null) why = "playing the spare character itself";
            if (why == null) return;
            lock (Gate) if (ReferenceEquals(_now, s)) _now = null;
            Shut(s, why);
        }

        internal static void Shutdown()
        {
            Session s;
            lock (Gate) { s = _now; _now = null; }
            if (s != null) Shut(s, "game closing");
        }

        private static void Shut(Session s, string why)
        {
            if (s.Closing) return;
            s.Closing = true;
            Plugin.Trace("[spare] closing session: " + why);
            try { s.Client?.Close(); } catch { }
        }

        private static void Loop(Session s)
        {
            string lost = null;
            try
            {
                Open(s);
                lock (Gate) s.Ready = true;
                Plugin.Trace("[spare] logged in");
                var quiet = DateTime.UtcNow;
                var used = DateTime.UtcNow;
                while (!s.Closing)
                {
                    Job job = null;
                    lock (Gate) if (s.Jobs.Count > 0) job = s.Jobs.Dequeue();
                    if (job != null)
                    {
                        try { job.Work(s.Link); }
                        catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
                        {
                            Fail(job, e is SpareReset ? e.Message : s.Closing ? "соединение запасного аккаунта закрыто" : "соединение запасного аккаунта оборвалось");
                            throw;
                        }
                        catch (Exception e) { Fail(job, e.Message); }
                        quiet = DateTime.UtcNow;
                        used = DateTime.UtcNow;
                        continue;
                    }
                    if ((DateTime.UtcNow - used).TotalSeconds >= Idle)
                    {
                        bool idle;
                        lock (Gate)
                        {
                            idle = s.Jobs.Count == 0;
                            if (idle)
                            {
                                s.Closing = true;
                                if (ReferenceEquals(_now, s)) _now = null;
                            }
                        }
                        if (idle)
                        {
                            Plugin.Trace("[spare] unused for " + Idle + " s, leaving the game");
                            break;
                        }
                    }
                    if (!Drain(s.Link)) Thread.Sleep(50);
                    if ((DateTime.UtcNow - quiet).TotalSeconds >= KeepAlive)
                    {
                        Send(s.Link, "<Message type=\"378\" />");
                        quiet = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception e)
            {
                if (!s.Closing && !(e is SpareReset)) lost = e.Message;
            }
            finally
            {
                s.Closing = true;
                try { s.Client?.Close(); } catch { }
                List<Job> left;
                lock (Gate)
                {
                    left = new List<Job>(s.Jobs);
                    s.Jobs.Clear();
                    if (ReferenceEquals(_now, s)) _now = null;
                    if (lost != null) _kicked = true;
                }
                string say = lost != null && !s.Ready ? lost : "соединение запасного аккаунта закрыто";
                foreach (var job in left) Fail(job, say);
                if (lost != null) Plugin.Trace("[spare] session lost: " + lost);
            }
        }

        private static void Fail(Job job, string why)
        {
            try { job.Fail?.Invoke(why); }
            catch (Exception e) { Plugin.Trace("[spare] " + job.Name + ": " + e.Message); }
        }

        private static void Open(Session s)
        {
            string pass = Plugin.CfgOnlinePassword?.Value ?? "";
            string ver = (Plugin.CfgOnlineVersion?.Value ?? "").Trim();
            if (ver.Length == 0) ver = "11073";
            var client = new TcpClient { NoDelay = true };
            s.Client = client;
            if (s.Closing) throw new IOException("closed");
            var ar = client.BeginConnect(Host, Port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(8000)) throw new Exception("нет соединения с " + Host);
            client.EndConnect(ar);
            s.Link = new SpareLink { Stream = client.GetStream(), Socket = client.Client };
            Pump(s.Link, 1.5, null);
            Send(s.Link, "<Message type=\"315\"><auth account=\"" + Esc(s.Login) + "\" password=\"" + Esc(pass) + "\" ver=\"" + Esc(ver) + "\" site=\"1\" /></Message>");
            string lr = null;
            foreach (var m in Pump(s.Link, 8, m => m.Contains("LoginResponce")))
                if (m.Contains("LoginResponce")) lr = m;
            if (lr == null) throw new Exception("сервер не ответил на вход запасным аккаунтом");
            if (!lr.Contains("LoggedIn=\"1\""))
            {
                var mm = Regex.Match(lr, "Msg=\"([^\"]*)\"");
                throw new Exception("вход отклонён" + (mm.Success && mm.Groups[1].Value.Length > 0 ? ": " + System.Net.WebUtility.HtmlDecode(mm.Groups[1].Value) : ""));
            }
            if (s.Link.Balance == null) Pump(s.Link, 4, m => Money.IsMatch(m));
        }

        private static bool Drain(SpareLink link)
        {
            link.Early.Clear();
            return Take(link, new List<string>());
        }

        internal static string Esc(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        internal static void Send(SpareLink link, string xml)
        {
            var b = Encoding.UTF8.GetBytes(xml + "\0");
            link.Stream.Write(b, 0, b.Length);
            link.Stream.Flush();
        }

        private static bool Take(SpareLink link, List<string> into)
        {
            bool any = false;
            var tmp = new byte[65536];
            while (link.Stream.DataAvailable)
            {
                int n = link.Stream.Read(tmp, 0, tmp.Length);
                if (n <= 0) throw new IOException("server closed the connection");
                any = true;
                for (int i = 0; i < n; i++)
                {
                    if (tmp[i] != 0) { link.Acc.Add(tmp[i]); continue; }
                    if (link.Acc.Count == 0) continue;
                    string m = Encoding.UTF8.GetString(link.Acc.ToArray());
                    link.Acc.Clear();
                    if (m.Trim().Length == 0) continue;
                    Note(link, m);
                    into.Add(m);
                }
            }
            if (!any) Alive(link);
            return any;
        }

        private static void Alive(SpareLink link)
        {
            var socket = link.Socket;
            if (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
                throw new IOException("server closed the connection");
        }

        private static void Note(SpareLink link, string m)
        {
            var money = Money.Match(m);
            double value;
            if (money.Success && double.TryParse(money.Groups[1].Value, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out value))
                link.Balance = value;
        }

        internal static List<string> Pump(SpareLink link, double seconds, Func<string, bool> stopWhen)
        {
            var got = new List<string>();
            while (link.Early.Count > 0)
            {
                string m = link.Early.Dequeue();
                got.Add(m);
                if (stopWhen != null && stopWhen(m)) return got;
            }
            var end = DateTime.UtcNow.AddSeconds(seconds);
            var chunk = new List<string>();
            while (DateTime.UtcNow < end)
            {
                chunk.Clear();
                if (!Take(link, chunk)) { Thread.Sleep(40); continue; }
                for (int k = 0; k < chunk.Count; k++)
                {
                    got.Add(chunk[k]);
                    if (stopWhen == null || !stopWhen(chunk[k])) continue;
                    for (int rest = k + 1; rest < chunk.Count; rest++) link.Early.Enqueue(chunk[rest]);
                    return got;
                }
            }
            return got;
        }
    }
}
