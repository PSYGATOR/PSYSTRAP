using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap phone remote: a tiny web page on your home network (http://your-pc:8787) to check on your Roblox windows
    /// from your phone and rejoin, close or screenshot them. Off by default. Behind a 6 digit PIN; five wrong tries
    /// locks that address out for 5 minutes. Only one watcher runs it at a time (whoever holds the lock), and it stops
    /// when the last Roblox closes.
    ///
    /// It's plain HTTP inside your own network, so don't forward the port on your router.
    /// </summary>
    public sealed class PhoneRemote : IDisposable
    {
        private const string LOG_IDENT = "PhoneRemote";
        private const string CookieName = "psy_session";

        private readonly CancellationTokenSource _cancel = new();
        private readonly Thread _thread;
        private readonly Dictionary<string, DateTime> _sessions = new();
        private readonly Dictionary<string, (int Fails, DateTime LockedUntil)> _failures = new();

        public PhoneRemote()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "PSYstrap phone remote" };
            _thread.Start();
        }

        #region Settings helpers
        /// <summary>A new random 6 digit PIN.</summary>
        public static string NewPin() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        /// <summary>The addresses your phone can use, like http://192.168.1.20:8787.</summary>
        public static List<string> Addresses(int port)
        {
            var addresses = new List<string>();

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                {
                    foreach (var address in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                        addresses.Add($"http://{address.Address}:{port}");
                }
            }
            catch { }

            return addresses.Distinct().ToList();
        }
        #endregion

        private void Loop()
        {
            InterProcessLock? owner = null;
            TcpListener? listener = null;
            int listeningPort = 0;
            var token = _cancel.Token;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var remote = App.Settings.Prop.Afk.Remote;
                    bool wanted = remote.Enabled && remote.Pin.Length >= 6;

                    if (wanted && owner is null)
                    {
                        var candidate = new InterProcessLock("PhoneRemote");

                        if (candidate.IsAcquired)
                            owner = candidate;
                        else
                            candidate.Mutex.Dispose();
                    }

                    if (owner is not null && wanted && (listener is null || listeningPort != remote.Port))
                    {
                        listener?.Stop();

                        try
                        {
                            listener = new TcpListener(IPAddress.Any, remote.Port);
                            listener.Start();
                            listeningPort = remote.Port;
                            App.Logger.WriteLine(LOG_IDENT, $"Listening on port {remote.Port}");
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Couldn't listen on port {remote.Port}: {ex.Message}");
                            listener = null;
                        }
                    }
                    else if (!wanted && listener is not null)
                    {
                        listener.Stop();
                        listener = null;
                        App.Logger.WriteLine(LOG_IDENT, "Stopped");
                    }

                    if (listener is not null && listener.Pending())
                    {
                        var client = listener.AcceptTcpClient();
                        Task.Run(() => Handle(client));
                        continue;
                    }

                    token.WaitHandle.WaitOne(listener is null ? 3000 : 100);
                }
            }
            finally
            {
                listener?.Stop();
                owner?.Dispose();
            }
        }

        #region HTTP
        private record Request(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers, string Body, string Ip);

        private void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 10000;
                    using var stream = client.GetStream();
                    string ip = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";

                    var request = Read(stream, ip);

                    if (request is null)
                        return;

                    Respond(stream, request);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Request failed: {ex.Message}");
                }
            }
        }

        private static Request? Read(NetworkStream stream, string ip)
        {
            var buffer = new byte[8192];
            int total = 0;
            int headerEnd = -1;

            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);

                if (read <= 0)
                    break;

                total += read;
                headerEnd = IndexOf(buffer, total, HeaderEnd);

                if (headerEnd >= 0)
                    break;
            }

            if (headerEnd < 0)
                return null;

            string head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
            var lines = head.Split("\r\n");
            var first = lines[0].Split(' ');

            if (first.Length < 2)
                return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon > 0)
                    headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            // small bodies only (the login form)
            int bodyStart = headerEnd + 4;
            int length = headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out int l) ? Math.Clamp(l, 0, 1024) : 0;

            while (total - bodyStart < length && total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read <= 0)
                    break;
                total += read;
            }

            string body = length > 0 ? Encoding.UTF8.GetString(buffer, bodyStart, Math.Min(length, total - bodyStart)) : "";

            string target = first[1];
            string path = target.Split('?')[0];
            var query = ParseForm(target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "");

            return new Request(first[0].ToUpperInvariant(), path, query, headers, body, ip);
        }

        private static readonly byte[] HeaderEnd = { 13, 10, 13, 10 };

        private static int IndexOf(byte[] buffer, int length, byte[] pattern) =>
            buffer.AsSpan(0, length).IndexOf(pattern);

        private static Dictionary<string, string> ParseForm(string text)
        {
            var values = new Dictionary<string, string>();

            foreach (string pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
                string value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                values[key] = value;
            }

            return values;
        }

        private static void Send(NetworkStream stream, int status, string contentType, byte[] body, string? extraHeaders = null)
        {
            string reason = status switch { 200 => "OK", 303 => "See Other", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 429 => "Too Many Requests", _ => "Error" };
            string head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n" +
                          "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n" +
                          (extraHeaders ?? "") + "\r\n";

            stream.Write(Encoding.ASCII.GetBytes(head));
            stream.Write(body);
        }

        private static void SendText(NetworkStream stream, int status, string contentType, string text, string? extraHeaders = null) =>
            Send(stream, status, contentType, Encoding.UTF8.GetBytes(text), extraHeaders);
        #endregion

        #region Pages and actions
        private bool IsSignedIn(Request request)
        {
            if (!request.Headers.TryGetValue("Cookie", out var cookies))
                return false;

            foreach (string part in cookies.Split(';'))
            {
                var kv = part.Trim().Split('=', 2);

                if (kv.Length == 2 && kv[0] == CookieName)
                {
                    lock (_sessions)
                    {
                        if (_sessions.TryGetValue(kv[1], out var expires) && expires > DateTime.Now)
                            return true;
                    }
                }
            }

            return false;
        }

        private void Respond(NetworkStream stream, Request request)
        {
            if (request.Method == "POST" && request.Path == "/login")
            {
                Login(stream, request);
                return;
            }

            if (!IsSignedIn(request))
            {
                if (request.Path == "/")
                    SendText(stream, 200, "text/html; charset=utf-8", LoginPage(""));
                else
                    SendText(stream, 401, "text/plain", "Sign in first.");
                return;
            }

            switch (request.Method, request.Path)
            {
                case ("GET", "/"):
                    SendText(stream, 200, "text/html; charset=utf-8", DashboardPage());
                    break;

                case ("GET", "/api/status"):
                    SendText(stream, 200, "application/json", StatusJson());
                    break;

                case ("GET", "/api/shot"):
                    if (RobloxPid(request) is int shotPid && RobloxWindows.Capture(shotPid) is byte[] jpeg)
                        Send(stream, 200, "image/jpeg", jpeg);
                    else
                        SendText(stream, 404, "text/plain", "No picture (the window might be minimized).");
                    break;

                case ("POST", "/api/rejoin"):
                    if (RobloxPid(request) is int rejoinPid)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Rejoin requested for {rejoinPid}");
                        Task.Run(() => Accounts.ClientRejoin.Rejoin(rejoinPid));
                        SendText(stream, 200, "application/json", "{\"ok\":true}");
                    }
                    else SendText(stream, 404, "application/json", "{\"ok\":false}");
                    break;

                case ("POST", "/api/close"):
                    if (RobloxPid(request) is int closePid)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Close requested for {closePid}");
                        try { using var process = Process.GetProcessById(closePid); process.CloseMainWindow(); } catch { }
                        SendText(stream, 200, "application/json", "{\"ok\":true}");
                    }
                    else SendText(stream, 404, "application/json", "{\"ok\":false}");
                    break;

                case ("POST", "/api/screenoff"):
                    RobloxWindows.ScreenOff();
                    SendText(stream, 200, "application/json", "{\"ok\":true}");
                    break;

                case ("POST", "/api/afksetup"):
                    Process.Start(Paths.Process, "-afksetup");
                    SendText(stream, 200, "application/json", "{\"ok\":true}");
                    break;

                case ("POST", "/logout"):
                    SendText(stream, 303, "text/plain", "", $"Location: /\r\nSet-Cookie: {CookieName}=; Max-Age=0; Path=/; HttpOnly; SameSite=Strict\r\n");
                    break;

                default:
                    SendText(stream, 404, "text/plain", "Not here.");
                    break;
            }
        }

        /// <summary>The pid in the query, only if it really is a Roblox window.</summary>
        private static int? RobloxPid(Request request) =>
            request.Query.TryGetValue("pid", out var text) && int.TryParse(text, out int pid) && MultiInstance.GetRobloxProcesses().Any(p => p.Id == pid) ? pid : null;

        private void Login(NetworkStream stream, Request request)
        {
            lock (_failures)
            {
                if (_failures.TryGetValue(request.Ip, out var record) && record.LockedUntil > DateTime.Now)
                {
                    SendText(stream, 429, "text/html; charset=utf-8", LoginPage("Too many wrong PINs. Try again in a few minutes."));
                    return;
                }
            }

            string pin = ParseForm(request.Body).GetValueOrDefault("pin", "").Trim();
            string expected = App.Settings.Prop.Afk.Remote.Pin;
            bool ok = expected.Length >= 6 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pin), Encoding.UTF8.GetBytes(expected));

            if (!ok)
            {
                lock (_failures)
                {
                    var record = _failures.GetValueOrDefault(request.Ip);
                    int fails = record.Fails + 1;
                    _failures[request.Ip] = fails >= 5 ? (0, DateTime.Now.AddMinutes(5)) : (fails, DateTime.MinValue);
                }

                App.Logger.WriteLine(LOG_IDENT, $"Wrong PIN from {request.Ip}");
                SendText(stream, 401, "text/html; charset=utf-8", LoginPage("Wrong PIN."));
                return;
            }

            string session = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

            lock (_sessions)
                _sessions[session] = DateTime.Now.AddHours(12);

            lock (_failures)
                _failures.Remove(request.Ip);

            App.Logger.WriteLine(LOG_IDENT, $"Signed in from {request.Ip}");
            SendText(stream, 303, "text/plain", "", $"Location: /\r\nSet-Cookie: {CookieName}={session}; Max-Age=43200; Path=/; HttpOnly; SameSite=Strict\r\n");
        }

        private static string StatusJson()
        {
            var clients = MultiInstance.GetRobloxProcesses();
            var sessions = Accounts.LiveSessions.List();
            var accounts = Accounts.AccountStore.Load();
            var log = AfkLog.Load(60);

            var windows = clients.Select(client =>
            {
                var session = sessions.FirstOrDefault(s => s.ProcessId == client.Id);
                long ramMB = 0;
                try { client.Refresh(); ramMB = client.WorkingSet64 / (1024 * 1024); } catch { }

                return new
                {
                    pid = client.Id,
                    account = accounts.FirstOrDefault(a => a.UserId == session?.UserId)?.Username ?? "",
                    game = session is null || session.PlaceId == 0 ? "" : log.FirstOrDefault(e => e.PlaceId == session.PlaceId && e.Game.Length > 0)?.Game ?? $"place {session.PlaceId}",
                    minutes = session is null || session.PlaceId == 0 ? -1 : (int)(DateTime.Now - session.Joined).TotalMinutes,
                    ramMB,
                    minimized = RobloxWindows.IsMinimized(client.MainWindowHandle)
                };
            }).ToList();

            var events = log.Take(15).Select(e => new
            {
                at = e.At.ToString("HH:mm"),
                what = AfkLog.Describe(e.Kind),
                account = accounts.FirstOrDefault(a => a.UserId == e.UserId)?.Username ?? "",
                game = e.Game,
                detail = e.Detail
            });

            return JsonSerializer.Serialize(new { windows, events, status = AfkCoordinator.BuildStatus(DateTime.Today) });
        }

        private static string Page(string body) => @"<!doctype html><html lang=""en""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>PSYstrap remote</title>
<link href=""https://fonts.googleapis.com/css2?family=Permanent+Marker&family=Patrick+Hand&display=swap"" rel=""stylesheet"">
<style>
body{margin:0;background:#07120a;color:#eef8ec;font:19px/1.4 ""Patrick Hand"",system-ui,sans-serif;padding:16px}
h1{font:30px ""Permanent Marker"",cursive;color:#3cf02a;margin:4px 0 14px;text-shadow:2px 2px 0 #17761d}
.card{border:2.5px solid #27b52b;border-radius:14px 20px 12px 22px;background:#0f2214;padding:12px 14px;margin:0 0 14px}
.card b{font:20px ""Permanent Marker"",cursive;color:#3cf02a;font-weight:400}
.muted{color:#9fbaa0;font-size:16px}
button,input{font:18px ""Permanent Marker"",cursive;border-radius:10px;padding:8px 14px;margin:6px 6px 0 0;border:2.5px solid #17761d;background:#14291c;color:#eef8ec}
button.go{background:repeating-linear-gradient(-58deg,#3cf02a 0 5px,#34d726 5px 7px,#3cf02a 7px 12px);color:#062a08}
input{font-family:""Patrick Hand"",sans-serif;width:9em;letter-spacing:.2em}
img{max-width:100%;border-radius:8px;margin-top:8px;display:block}
pre{white-space:pre-wrap;font:inherit;margin:0}
.err{color:#ff7a8a}
</style></head><body>%BODY%</body></html>".Replace("%BODY%", body);

        private static string LoginPage(string error) => Page(@"<h1>PSYstrap</h1>
<div class=""card""><form method=""post"" action=""/login"">
<label for=""pin"">PIN (in PSYstrap Settings &gt; AFK &amp; launching)</label><br>
<input id=""pin"" name=""pin"" inputmode=""numeric"" autocomplete=""one-time-code"" maxlength=""6"" autofocus>
<button class=""go"" type=""submit"">Open</button>
<p class=""err"">%ERROR%</p></form></div>".Replace("%ERROR%", WebUtility.HtmlEncode(error)));

        private static string DashboardPage() => Page(@"<h1>PSYstrap</h1>
<div class=""card""><pre id=""status"" class=""muted"">Loading...</pre>
<button onclick=""act('/api/afksetup','Opening your AFK setup')"">Open AFK setup</button>
<button onclick=""act('/api/screenoff','Screen off')"">Screen off</button>
<form method=""post"" action=""/logout"" style=""display:inline""><button type=""submit"">Sign out</button></form></div>
<div id=""windows""></div>
<div class=""card""><b>Recently</b><div id=""events"" class=""muted""></div></div>
<script>
function esc(s){return String(s).replace(/[&<>""']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;',""'"":'&#39;'}[c]))}
async function act(url,msg){const r=await fetch(url,{method:'POST'});if(msg)alert(r.ok?msg:'That didn\'t work');load()}
function shot(pid){const box=document.getElementById('shot'+pid);box.innerHTML='<img alt=""Screenshot"" src=""/api/shot?pid='+pid+'&t='+Date.now()+'"">'}
async function load(){
 const r=await fetch('/api/status');if(r.status==401){location.reload();return}
 const d=await r.json();
 document.getElementById('status').textContent=d.status;
 document.getElementById('windows').innerHTML=d.windows.map(w=>'<div class=""card""><b>'+esc(w.account||'Roblox')+'</b><div>'+(w.minutes<0?'not in a game':esc(w.game)+', '+w.minutes+' min')+'</div><div class=""muted"">'+w.ramMB+' MB'+(w.minimized?', minimized':'')+'</div>'+
  '<button onclick=""act(\'/api/rejoin?pid='+w.pid+'\',\'Rejoining\')"">Rejoin</button><button onclick=""shot('+w.pid+')"">Screenshot</button><button onclick=""if(confirm(\'Close this window?\'))act(\'/api/close?pid='+w.pid+'\',\'Closing\')"">Close</button><div id=""shot'+w.pid+'""></div></div>').join('')||'<div class=""card muted"">No Roblox windows open.</div>';
 document.getElementById('events').innerHTML=d.events.map(e=>'<div>'+esc(e.at)+' '+esc(e.account)+' '+esc(e.what)+(e.game?' ('+esc(e.game)+')':'')+'</div>').join('')||'Nothing yet.';
}
load();setInterval(load,15000);
</script>");
        #endregion

        public void Dispose()
        {
            _cancel.Cancel();
            _thread.Join(3000);
        }
    }
}
