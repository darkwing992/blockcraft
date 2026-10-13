// BlockCraft Launcher for Windows.
// - Runs a tiny local web server (localhost only) that serves the game and stores
//   each world as a file in the "saves" folder next to the .exe.
// - Lets you pick any game version from the "versions" folder (old versions keep their own worlds).
// - Checks GitHub for new game versions (repo named in update-source.txt), downloads them,
//   checks their SHA-256 against versions.json and installs them. The .exe never replaces itself.
// - Opens the game in its own Microsoft Edge app window (or the default browser).
// Build with build.bat (uses the C# compiler that comes with Windows).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("BlockCraft Launcher")]
[assembly: AssemblyProduct("BlockCraft")]
[assembly: AssemblyDescription("Launcher and save server for BlockCraft")]
[assembly: AssemblyVersion("2.6.0.0")]
[assembly: AssemblyFileVersion("2.6.0.0")]

namespace BlockCraft
{
    static class Program
    {
        public const string LauncherVersion = "2.6";

        [STAThread]
        static int Main(string[] args)
        {
            // helpers: --server-only <port>, --render-preview <file.png>, --make-manifest
            if (args.Length >= 1 && args[0] == "--server-only")
            {
                var s = new GameServer();
                int port = args.Length >= 2 ? int.Parse(args[1]) : 8777;
                if (!s.Start(port)) return 1;
                Console.WriteLine("BlockCraft server on http://localhost:" + s.Port + "/");
                Thread.Sleep(Timeout.Infinite);
                return 0;
            }
            if (args.Length >= 1 && args[0] == "--make-manifest")
            {
                try { new Versions().MakeManifest(); return 0; }
                catch (Exception e) { File.WriteAllText(Path.Combine(Versions.RootDir, "manifest-error.txt"), e.ToString()); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && args[0] == "--render-preview")
            {
                using (var f = new LauncherForm(null))
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(-4000, -4000);
                    f.ShowInTaskbar = false;
                    f.Show();
                    if (args.Length >= 3) f.ShowPage(int.Parse(args[2]));
                    Application.DoEvents();
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, "BlockCraftLauncher_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("The BlockCraft Launcher is already open.", "BlockCraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                var server = new GameServer();
                if (!server.Start(8777))
                {
                    MessageBox.Show("BlockCraft could not start its save server.\nTry restarting your computer.", "BlockCraft", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                Application.Run(new LauncherForm(server));
                server.Stop();
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------
    //  Game versions: versions\<id>\index.html, described by versions.json
    // ------------------------------------------------------------------
    class VersionInfo
    {
        public string Id, Title, Date, Notes, Sha256;
        public bool Installed;
        public bool Latest;
        public override string ToString()
        {
            string s = Id;
            if (!string.IsNullOrEmpty(Title)) s += "  -  " + Title;
            if (Latest) s += "  (latest)";
            if (!Installed) s += "  [download]";
            return s;
        }
    }

    class Versions
    {
        public static readonly string RootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        public string ManifestPath { get { return Path.Combine(RootDir, "versions.json"); } }
        public string LatestId = "";
        public string RemoteLauncher = "", LauncherUrl = "";
        public List<VersionInfo> List = new List<VersionInfo>();

        public static string FileFor(string id) { return Path.Combine(RootDir, "versions", id, "index.html"); }
        public static bool ValidId(string id) { return id != null && Regex.IsMatch(id, "^[0-9]{1,3}(\\.[0-9]{1,3}){1,3}$"); }
        public static int Compare(string a, string b)
        {
            try { return new Version(a).CompareTo(new Version(b)); } catch { return string.CompareOrdinal(a, b); }
        }

        // GitHub repository ("owner/name") that updates come from, set by the publish script
        public static string Repo()
        {
            try
            {
                string f = Path.Combine(RootDir, "update-source.txt");
                if (!File.Exists(f)) return null;
                string r = File.ReadAllText(f).Trim();
                if (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) r = r.Substring(0, r.Length - 4);
                return Regex.IsMatch(r, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") ? r : null;
            }
            catch { return null; }
        }
        static string Raw(string repo, string path) { return "https://raw.githubusercontent.com/" + repo + "/main/" + path; }

        public void LoadLocal()
        {
            Parse(File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath, Utf8) : null);
            // the game next to the exe always counts as a version, even without a manifest
            string cur = CurrentGameVersion();
            if (cur != null && Find(cur) == null)
            {
                var v = new VersionInfo(); v.Id = cur; v.Title = ""; v.Notes = ""; List.Insert(0, v);
                if (Compare(cur, LatestId == "" ? "0.0" : LatestId) > 0) LatestId = cur;
            }
            Refresh();
        }

        public void Parse(string json)
        {
            List.Clear(); LatestId = ""; RemoteLauncher = ""; LauncherUrl = "";
            if (string.IsNullOrEmpty(json)) return;
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            LatestId = Str(d, "latest"); RemoteLauncher = Str(d, "launcher"); LauncherUrl = Str(d, "launcherUrl");
            object arr;
            if (d.TryGetValue("versions", out arr) && arr is ArrayList)
                foreach (var o in (ArrayList)arr)
                {
                    var e = o as Dictionary<string, object>;
                    if (e == null || !ValidId(Str(e, "id"))) continue;
                    var v = new VersionInfo();
                    v.Id = Str(e, "id"); v.Title = Str(e, "title"); v.Date = Str(e, "date"); v.Notes = Str(e, "notes"); v.Sha256 = Str(e, "sha256");
                    List.Add(v);
                }
            List.Sort(delegate(VersionInfo a, VersionInfo b) { return Compare(b.Id, a.Id); });
        }
        static string Str(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? o.ToString() : ""; }

        public VersionInfo Find(string id) { foreach (var v in List) if (v.Id == id) return v; return null; }

        public void Refresh()
        {
            foreach (var v in List)
            {
                v.Latest = v.Id == LatestId;
                v.Installed = File.Exists(FileFor(v.Id)) || (v.Latest && v.Id == CurrentGameVersion());
            }
        }

        // reads const GAME_VERSION='x.y' from index.html next to the exe
        public static string CurrentGameVersion() { return VersionOfFile(Path.Combine(RootDir, "index.html")); }
        public static string VersionOfFile(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                var m = Regex.Match(File.ReadAllText(file, Utf8), "const GAME_VERSION\\s*=\\s*['\"]([0-9.]+)['\"]");
                return m.Success && ValidId(m.Groups[1].Value) ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(data)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2 for GitHub
            var wc = new WebClient();
            wc.Headers.Add("User-Agent", "BlockCraft-Launcher/" + Program.LauncherVersion);
            wc.Encoding = Utf8;
            return wc;
        }

        // Downloads versions.json from GitHub. Returns the raw text (null if updates are not set up).
        public static string FetchRemote(string repo)
        {
            using (var wc = Client()) return wc.DownloadString(Raw(repo, "versions.json") + "?t=" + DateTime.UtcNow.Ticks);
        }

        // Downloads one version, checks its hash and installs it. Throws on any problem.
        public static void Download(string repo, VersionInfo v, bool makeCurrent)
        {
            byte[] data;
            using (var wc = Client()) data = wc.DownloadData(Raw(repo, "versions/" + v.Id + "/index.html") + "?t=" + DateTime.UtcNow.Ticks);
            if (string.IsNullOrEmpty(v.Sha256) || !string.Equals(Sha256Hex(data), v.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new Exception("the download for version " + v.Id + " did not match its checksum, so it was not installed");
            string file = FileFor(v.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            WriteSafely(file, data);
            if (makeCurrent)
            {
                // keep the version being replaced, then make the new one the main game
                string main = Path.Combine(RootDir, "index.html");
                string old = VersionOfFile(main);
                if (old != null && !File.Exists(FileFor(old)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FileFor(old)));
                    File.Copy(main, FileFor(old));
                }
                WriteSafely(main, data);
            }
        }

        public static void WriteSafely(string file, byte[] data)
        {
            string tmp = file + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }

        // Used by the publish script: copies index.html into versions\<id>\ and fills in every checksum.
        public void MakeManifest()
        {
            LoadLocal();
            string cur = CurrentGameVersion();
            if (cur == null) throw new Exception("index.html has no GAME_VERSION");
            Directory.CreateDirectory(Path.GetDirectoryName(FileFor(cur)));
            File.Copy(Path.Combine(RootDir, "index.html"), FileFor(cur), true);
            if (Compare(cur, LatestId == "" ? "0.0" : LatestId) >= 0) LatestId = cur;
            var outList = new ArrayList();
            foreach (var v in List)
            {
                if (!File.Exists(FileFor(v.Id))) continue;
                var e = new Dictionary<string, object>();
                e["id"] = v.Id; e["title"] = v.Title ?? ""; e["date"] = v.Date ?? ""; e["notes"] = v.Notes ?? "";
                e["sha256"] = Sha256Hex(File.ReadAllBytes(FileFor(v.Id)));
                outList.Add(e);
            }
            var d = new Dictionary<string, object>();
            d["latest"] = LatestId;
            d["launcher"] = Program.LauncherVersion;
            if (!string.IsNullOrEmpty(LauncherUrl)) d["launcherUrl"] = LauncherUrl;
            d["versions"] = outList;
            string json = new JavaScriptSerializer().Serialize(d);
            json = json.Replace("{\"id\"", "\n    {\"id\"").Replace("]}", "\n  ]\n}").Replace(",\"versions\":[", ",\n  \"versions\": [");
            WriteSafely(ManifestPath, Utf8.GetBytes(json));
        }
    }

    // ------------------------------------------------------------------
    //  Local game + save server (same API the game uses with server.ps1)
    //  /            -> the newest game (index.html), worlds in saves\
    //  /v/<id>/     -> an old version, worlds in saves\old-versions\<id>\
    // ------------------------------------------------------------------
    // Your BlockCraft account (cloud saves), shared by the launcher and the game: saves\account.json = {"name":..,"token":..}
    // or {} when logged out. The password is never kept, only the login key (token) the server remembers.
    static class AccountStore
    {
        static readonly object Lock = new object();
        public static string FileIn(string saves) { return Path.Combine(saves, "account.json"); }
        public static bool Valid(string name, string token)
        {
            return name != null && token != null && Regex.IsMatch(name, "^[A-Za-z0-9_ \\-]{1,16}$") && token.Length >= 8 && token.Length <= 64 && Regex.IsMatch(token, "^[A-Za-z0-9_.\\-]+$");
        }
        // returns null if there is no file yet, otherwise { name, token } (both "" when logged out)
        public static string[] Read(string saves)
        {
            string f = FileIn(saves);
            lock (Lock)
            {
                if (!File.Exists(f)) return null;
                try
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    object n, t; d.TryGetValue("name", out n); d.TryGetValue("token", out t);
                    string name = n as string ?? "", token = t as string ?? "";
                    return Valid(name, token) ? new[] { name, token } : new[] { "", "" };
                }
                catch { return new[] { "", "" }; }
            }
        }
        public static void Write(string saves, string name, string token)
        {
            var d = new Dictionary<string, object>();
            if (Valid(name, token)) { d["name"] = name; d["token"] = token; }
            lock (Lock) { Directory.CreateDirectory(saves); File.WriteAllText(FileIn(saves), new JavaScriptSerializer().Serialize(d)); }
        }
        public static string NewToken()
        {
            var b = new byte[18]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            var sb = new StringBuilder(); foreach (byte x in b) sb.Append("abcdefghijklmnopqrstuvwxyz0123456789"[x % 36]); return sb.ToString();
        }
        // the big server's web address, read from the game (DEFAULT_SERVER, written by "Deploy Big Server.bat")
        public static string CloudBase()
        {
            try
            {
                string html = File.ReadAllText(Path.Combine(Versions.RootDir, "index.html"));
                var m = Regex.Match(html, "const DEFAULT_SERVER='([^']*)'");
                if (!m.Success || m.Groups[1].Value == "") return "";
                string u = m.Groups[1].Value;
                if (u.StartsWith("wss://")) u = "https://" + u.Substring(6); else if (u.StartsWith("ws://")) u = "http://" + u.Substring(5);
                if (u.EndsWith("/ws")) u = u.Substring(0, u.Length - 3);
                return u.TrimEnd('/');
            }
            catch { return ""; }
        }
        // one request to the cloud; returns the answer as JSON (throws with the server's message if it says no)
        public static Dictionary<string, object> Call(string path, string method, string body, string name, string token)
        {
            string b = CloudBase();
            if (b == "") throw new Exception("No big server is set up yet (run \"Deploy Big Server.bat\").");
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            var req = (HttpWebRequest)WebRequest.Create(b + path);
            req.Method = method; req.UserAgent = "BlockCraft-Launcher/" + Program.LauncherVersion; req.Timeout = 15000;
            if (name != null) { req.Headers["x-bc-name"] = name; req.Headers["x-bc-token"] = token; }
            if (body != null)
            {
                var bytes = new UTF8Encoding(false).GetBytes(body); req.ContentType = "application/json"; req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            }
            string text; int code;
            try { using (var r = (HttpWebResponse)req.GetResponse()) { code = (int)r.StatusCode; using (var sr = new StreamReader(r.GetResponseStream())) text = sr.ReadToEnd(); } }
            catch (WebException e)
            {
                var r = e.Response as HttpWebResponse;
                if (r == null) throw new Exception("Could not reach the cloud. Check your internet connection.");
                code = (int)r.StatusCode; using (var sr = new StreamReader(r.GetResponseStream())) text = sr.ReadToEnd();
            }
            Dictionary<string, object> d;
            try { d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text); } catch { d = new Dictionary<string, object>(); }
            if (code < 200 || code >= 300)
            {
                object why; d.TryGetValue("error", out why);
                throw new Exception(why as string ?? ("The cloud said no (" + code + "). If you just updated, run \"Deploy Big Server.bat\"."));
            }
            return d;
        }
    }

    class GameServer
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        HttpListener listener;
        Thread thread;
        public int Port;
        public string Root;
        public string Saves;
        public event Action Saved, SkinChanged, AccountChanged;
        readonly object fileLock = new object();

        public bool Start(int firstPort)
        {
            Root = Versions.RootDir;
            Saves = Path.Combine(Root, "saves");
            Directory.CreateDirectory(Saves);
            for (int p = firstPort; p < firstPort + 30; p++)
            {
                var l = new HttpListener();
                l.Prefixes.Add("http://localhost:" + p + "/");
                try { l.Start(); listener = l; Port = p; break; }
                catch { try { l.Close(); } catch { } }
            }
            if (listener == null) return false;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Start();
            return true;
        }

        public void Stop()
        {
            try { listener.Stop(); listener.Close(); } catch { }
        }

        public string Url { get { return "http://localhost:" + Port + "/"; } }

        public int WorldCount()
        {
            try { return Directory.GetFiles(Saves, "*.info.json").Length; } catch { return 0; }
        }

        void Loop()
        {
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); } catch { return; }
                ThreadPool.QueueUserWorkItem(delegate { Handle(ctx); });
            }
        }

        static void Send(HttpListenerContext ctx, int code, string type, byte[] bytes)
        {
            var r = ctx.Response;
            r.StatusCode = code;
            r.ContentType = type;
            r.Headers.Add("Cache-Control", "no-store");
            r.ContentLength64 = bytes.Length;
            r.OutputStream.Write(bytes, 0, bytes.Length);
            r.OutputStream.Close();
        }
        static void SendText(HttpListenerContext ctx, int code, string type, string text) { Send(ctx, code, type, Utf8.GetBytes(text)); }

        static string ContentType(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript";
                case ".css": return "text/css";
                case ".json": return "application/json";
                case ".png": return "image/png";
                case ".ico": return "image/x-icon";
                case ".txt": return "text/plain; charset=utf-8";
                default: return "application/octet-stream";
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                string path = Uri.UnescapeDataString(req.Url.AbsolutePath);
                string saves = Saves, webRoot = Root;
                var vm = Regex.Match(path, "^/v/([0-9.]{3,15})(/.*)?$");
                if (vm.Success)
                {
                    string id = vm.Groups[1].Value;
                    if (!Versions.ValidId(id) || !File.Exists(Versions.FileFor(id))) { SendText(ctx, 404, "text/plain", "Version not installed"); return; }
                    if (!vm.Groups[2].Success || vm.Groups[2].Value == "")
                    {
                        // relative links need the trailing slash
                        ctx.Response.Redirect("/v/" + id + "/");
                        ctx.Response.Close();
                        return;
                    }
                    path = vm.Groups[2].Value;
                    webRoot = Path.GetDirectoryName(Versions.FileFor(id));
                    saves = Path.Combine(Saves, "old-versions", id);
                    Directory.CreateDirectory(saves);
                }
                if (path == "/api/ping")
                {
                    SendText(ctx, 200, "application/json", "{\"blockcraft\":true,\"launcher\":true}");
                }
                else if (path == "/api/saves")
                {
                    var sb = new StringBuilder("[");
                    bool first = true;
                    lock (fileLock)
                    {
                        foreach (var f in Directory.GetFiles(saves, "*.info.json"))
                        {
                            string txt = File.ReadAllText(f, Utf8).Trim();
                            if (txt.Length == 0) continue;
                            if (!first) sb.Append(',');
                            sb.Append(txt);
                            first = false;
                        }
                    }
                    sb.Append(']');
                    SendText(ctx, 200, "application/json", sb.ToString());
                }
                else if (path == "/api/skin")
                {
                    // your skin: "preset:<name>" or a Minecraft skin PNG as a data: URL; shared by the launcher and every game version
                    string file = Path.Combine(Saves, "skin.txt");
                    if (req.HttpMethod == "POST")
                    {
                        string body;
                        using (var reader = new StreamReader(req.InputStream, Utf8)) body = reader.ReadToEnd().Trim();
                        if (body != "" && !SkinArt.Valid(body)) { SendText(ctx, 400, "text/plain", "not a skin"); return; }
                        lock (fileLock) { if (body == "") { if (File.Exists(file)) File.Delete(file); } else File.WriteAllText(file, body, Utf8); }
                        SendText(ctx, 200, "application/json", "{\"ok\":true}");
                        var h = SkinChanged; if (h != null) h();
                    }
                    else
                    {
                        string s = "";
                        lock (fileLock) { if (File.Exists(file)) s = File.ReadAllText(file, Utf8).Trim(); }
                        SendText(ctx, 200, "text/plain; charset=utf-8", s);
                    }
                }
                else if (path == "/api/account")
                {
                    // the cloud-save login, shared with the launcher's Account page
                    if (req.HttpMethod == "POST")
                    {
                        string body;
                        using (var reader = new StreamReader(req.InputStream, Utf8)) body = reader.ReadToEnd();
                        string name = "", token = "";
                        try
                        {
                            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
                            object n, t; if (d != null) { d.TryGetValue("name", out n); d.TryGetValue("token", out t); name = n as string ?? ""; token = t as string ?? ""; }
                        }
                        catch { SendText(ctx, 400, "text/plain", "bad account"); return; }
                        if (name != "" && !AccountStore.Valid(name, token)) { SendText(ctx, 400, "text/plain", "bad account"); return; }
                        AccountStore.Write(Saves, name, token);
                        SendText(ctx, 200, "application/json", "{\"ok\":true}");
                        var h = AccountChanged; if (h != null) h();
                    }
                    else
                    {
                        var a = AccountStore.Read(Saves);
                        var d = new Dictionary<string, object>();
                        d["exists"] = a != null;
                        if (a != null && a[0] != "") { d["name"] = a[0]; d["token"] = a[1]; }
                        SendText(ctx, 200, "application/json", new JavaScriptSerializer().Serialize(d));
                    }
                }
                else if (path == "/api/save")
                {
                    string slot = req.QueryString["slot"];
                    if (slot == null || !Regex.IsMatch(slot, "^[A-Za-z0-9_-]{1,64}$")) { SendText(ctx, 400, "text/plain", "bad save slot name"); return; }
                    string data = Path.Combine(saves, slot + ".json");
                    string info = Path.Combine(saves, slot + ".info.json");
                    if (req.HttpMethod == "GET")
                    {
                        byte[] bytes = null;
                        lock (fileLock) { if (File.Exists(data)) bytes = File.ReadAllBytes(data); }
                        if (bytes != null) Send(ctx, 200, "application/json", bytes);
                        else SendText(ctx, 404, "text/plain", "not found");
                    }
                    else if (req.HttpMethod == "POST")
                    {
                        string body;
                        using (var reader = new StreamReader(req.InputStream, Utf8)) body = reader.ReadToEnd();
                        string target = req.QueryString["kind"] == "info" ? info : data;
                        lock (fileLock)
                        {
                            // write a temp file first so a crash can never leave a half-written save
                            string tmp = target + ".tmp";
                            File.WriteAllText(tmp, body, Utf8);
                            if (File.Exists(target)) File.Delete(target);
                            File.Move(tmp, target);
                        }
                        SendText(ctx, 200, "application/json", "{\"ok\":true}");
                        var h = Saved; if (h != null) h();
                    }
                    else if (req.HttpMethod == "DELETE")
                    {
                        // deleted worlds go to saves\deleted instead of being destroyed
                        string trash = Path.Combine(saves, "deleted");
                        lock (fileLock)
                        {
                            Directory.CreateDirectory(trash);
                            foreach (var f in new[] { data, info })
                            {
                                if (!File.Exists(f)) continue;
                                string dest = Path.Combine(trash, Path.GetFileName(f));
                                if (File.Exists(dest)) File.Delete(dest);
                                File.Move(f, dest);
                            }
                        }
                        SendText(ctx, 200, "application/json", "{\"ok\":true}");
                        var h = Saved; if (h != null) h();
                    }
                    else SendText(ctx, 405, "text/plain", "method not allowed");
                }
                else
                {
                    if (path == "/") path = "/index.html";
                    string file = Path.GetFullPath(Path.Combine(webRoot, path.TrimStart('/').Replace('/', '\\')));
                    bool inside = file.StartsWith(webRoot + "\\", StringComparison.OrdinalIgnoreCase) &&
                                  !file.StartsWith(Saves, StringComparison.OrdinalIgnoreCase);
                    if (inside && File.Exists(file))
                    {
                        Send(ctx, 200, ContentType(Path.GetExtension(file)), File.ReadAllBytes(file));
                    }
                    else SendText(ctx, 404, "text/plain", "Not found");
                }
            }
            catch (Exception e)
            {
                try { SendText(ctx, 500, "text/plain", e.Message); } catch { }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Launcher window (dark, with a side bar, a hero banner, version cards, news and minigames)
    // ------------------------------------------------------------------
    static class UI
    {
        public static readonly Color Bg = Color.FromArgb(14, 14, 17);
        public static readonly Color Side = Color.FromArgb(10, 10, 12);
        public static readonly Color Card = Color.FromArgb(24, 24, 29);
        public static readonly Color CardBorder = Color.FromArgb(44, 44, 52);
        public static readonly Color Text = Color.FromArgb(240, 240, 244);
        public static readonly Color Muted = Color.FromArgb(150, 150, 162);
        public static readonly Color Green = Color.FromArgb(46, 170, 70);
        public static readonly Color Orange = Color.FromArgb(240, 120, 32);
        public static readonly Color Blue = Color.FromArgb(46, 130, 230);
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static Color Shade(Color c, float k)
        {
            Func<int, int> f = v => Math.Max(0, Math.Min(255, (int)(v * k)));
            return Color.FromArgb(c.A, f(c.R), f(c.G), f(c.B));
        }
        // blocky "pixel" texture over a rounded area, like the buttons in the picture
        public static void Pixels(Graphics g, Rectangle r, Color c, int seed)
        {
            var rnd = new Random(seed); int s = 10;
            for (int y = r.Top; y < r.Bottom; y += s)
                for (int x = r.Left; x < r.Right; x += s)
                {
                    int n = rnd.Next(5); if (n > 1) continue;
                    using (var b = new SolidBrush(Color.FromArgb(55, n == 0 ? Color.White : Color.Black))) g.FillRectangle(b, x, y, s, s);
                }
        }
        public static Font F(float size, bool bold) { return new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }
        public static string IconFont() { return "Segoe MDL2 Assets"; }
    }

    // a rounded button: solid or "pixel" texture, an optional icon (Segoe MDL2 Assets glyph) and text
    class FancyButton : Control
    {
        public Color Base = UI.Green; public bool PixelTex; public string Glyph = ""; public float Radius = 10; public float TextSize = 12;
        bool hover, down;
        public FancyButton(string text) { Text = text; DoubleBuffered = true; Cursor = Cursors.Hand; ForeColor = Color.White; SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var pb = new SolidBrush(Parent != null ? Parent.BackColor : UI.Bg)) g.FillRectangle(pb, ClientRectangle);
            var r = new RectangleF(1, 1, Width - 3, Height - 3 - (down ? 0 : 2));
            if (down) r.Y += 2;
            Color c = !Enabled ? UI.Shade(Base, 0.55f) : hover ? UI.Shade(Base, 1.15f) : Base;
            using (var path = UI.Round(new RectangleF(r.X, r.Y + 3, r.Width, r.Height), Radius))
            using (var lip = new SolidBrush(UI.Shade(Base, 0.6f))) if (!down) g.FillPath(lip, path);
            using (var path = UI.Round(r, Radius))
            {
                using (var b = new LinearGradientBrush(r, UI.Shade(c, 1.12f), UI.Shade(c, 0.9f), 90f)) g.FillPath(b, path);
                if (PixelTex) { var st = g.Save(); g.SetClip(path); UI.Pixels(g, Rectangle.Round(r), c, Text.Length * 31 + Width); g.Restore(st); }
                using (var pen = new Pen(UI.Shade(c, 1.35f), 1.5f)) g.DrawPath(pen, path);
            }
            using (var tf = UI.F(TextSize, true))
            using (var gf = new Font(UI.IconFont(), TextSize * 0.95f))
            using (var tb = new SolidBrush(Enabled ? ForeColor : Color.FromArgb(170, ForeColor)))
            {
                SizeF ts = g.MeasureString(Text, tf), gs = Glyph != "" ? g.MeasureString(Glyph, gf) : SizeF.Empty;
                float gap = Glyph != "" && Text != "" ? 6 : 0, total = ts.Width + gs.Width + gap, x = r.X + (r.Width - total) / 2, cy = r.Y + r.Height / 2;
                if (Glyph != "") g.DrawString(Glyph, gf, tb, x, cy - gs.Height / 2 + 1);
                g.DrawString(Text, tf, tb, x + gs.Width + gap, cy - ts.Height / 2);
            }
        }
    }

    // a rounded dark card that other controls sit on
    class CardPanel : Panel
    {
        public Color Fill = UI.Card, Border = UI.CardBorder; public float Radius = 14;
        public CardPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); BackColor = UI.Card; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pb = new SolidBrush(Parent != null ? Parent.BackColor : UI.Bg)) g.FillRectangle(pb, ClientRectangle);
            using (var path = UI.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), Radius))
            using (var b = new SolidBrush(Fill)) using (var pen = new Pen(Border, 1.5f)) { g.FillPath(b, path); g.DrawPath(pen, path); }
        }
    }

    static class Art
    {
        static Color V(Color c, int amt, Random r) { int n = r.Next(-amt, amt + 1); return Color.FromArgb(Cl(c.R + n), Cl(c.G + n), Cl(c.B + n)); }
        static int Cl(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }
        public static void Px(Graphics g, Color c, float x, float y, float s) { using (var b = new SolidBrush(c)) g.FillRectangle(b, x, y, s, s); }
        // a grass block icon
        public static void GrassBlock(Graphics g, float x, float y, float s)
        {
            var r = new Random(7); float p = s / 16;
            for (int j = 0; j < 16; j++) for (int i = 0; i < 16; i++)
                    Px(g, j < 4 + (i % 3 == 0 ? 1 : 0) ? V(Color.FromArgb(92, 156, 52), 18, r) : V(Color.FromArgb(134, 96, 67), 14, r), x + i * p, y + j * p, p + 0.5f);
        }
        // the player, drawn from the front (a 16x32 pixel character)
        public static void Player(Graphics g, float x, float y, float p)
        {
            var r = new Random(3);
            Color skin = Color.FromArgb(198, 140, 104), hair = Color.FromArgb(60, 38, 22), shirt = Color.FromArgb(40, 160, 170), pants = Color.FromArgb(50, 60, 150), shoes = Color.FromArgb(70, 70, 74);
            for (int j = 0; j < 32; j++)
                for (int i = 0; i < 16; i++)
                {
                    Color c = Color.Empty;
                    if (j < 8) { if (i >= 4 && i < 12) { int hx = i - 4; c = j < 2 || (j == 2 && (hx == 0 || hx == 7)) ? hair : skin;
                            if (j == 4 && (hx == 1 || hx == 6)) c = Color.White; if (j == 4 && (hx == 2 || hx == 5)) c = Color.FromArgb(48, 80, 192);
                            if (j == 6 && (hx == 3 || hx == 4)) c = Color.FromArgb(122, 74, 58); } }
                    else if (j < 20) { if (i >= 4 && i < 12) c = shirt; else c = j < 12 ? shirt : skin; }
                    else { if (i >= 4 && i < 12) c = j >= 30 ? shoes : pants; }
                    if (c == Color.Empty) continue;
                    float shade = i < 4 ? 0.82f : i >= 12 ? 0.9f : 1f;
                    Px(g, V(UI.Shade(c, shade), 8, r), x + i * p, y + j * p, p + 0.5f);
                }
        }
        // a sunset over blocky hills, trees and grass
        public static void Sunset(Graphics g, Rectangle b)
        {
            using (var sky = new LinearGradientBrush(b, Color.FromArgb(246, 166, 120), Color.FromArgb(250, 224, 170), 90f)) g.FillRectangle(sky, b);
            var r = new Random(11); int px = 8;
            using (var sun = new SolidBrush(Color.FromArgb(255, 246, 200))) g.FillRectangle(sun, b.Left + b.Width * 0.22f, b.Top + b.Height * 0.38f, 56, 56);
            using (var glow = new SolidBrush(Color.FromArgb(70, 255, 240, 200))) g.FillRectangle(glow, b.Left + b.Width * 0.22f - 16, b.Top + b.Height * 0.38f - 16, 88, 88);
            using (var cloud = new SolidBrush(Color.FromArgb(150, 255, 236, 220)))
            { g.FillRectangle(cloud, b.Left + 60, b.Top + 40, 140, 16); g.FillRectangle(cloud, b.Left + 90, b.Top + 28, 70, 14); g.FillRectangle(cloud, b.Right - 260, b.Top + 60, 160, 16); }
            // far hills
            for (int x = b.Left; x < b.Right; x += px)
            {
                int hgt = 60 + (int)(Math.Sin(x * 0.012) * 26 + Math.Sin(x * 0.031) * 12);
                for (int y = b.Bottom - 70 - hgt; y < b.Bottom - 70; y += px) Px(g, V(Color.FromArgb(150, 170, 110), 10, r), x, y, px);
            }
            // trees
            int[] tx = { 70, 170, 520, 640, 760 };
            foreach (int t in tx)
            {
                if (t > b.Width - 40) continue;
                int baseY = b.Bottom - 70, trunkH = 60 + (t % 3) * 14;
                for (int y = baseY - trunkH; y < baseY; y += px) Px(g, V(Color.FromArgb(104, 82, 50), 10, r), b.Left + t, y, px);
                for (int dy = -3; dy <= 2; dy++) for (int dx = -4; dx <= 4; dx++)
                    {
                        if (Math.Abs(dx) == 4 && (dy == -3 || dy == 2)) continue;
                        Px(g, V(Color.FromArgb(108, 150, 50), 18, r), b.Left + t + dx * px, baseY - trunkH + dy * px, px);
                    }
            }
            // grass ground
            for (int x = b.Left; x < b.Right; x += px)
                for (int y = b.Bottom - 70; y < b.Bottom; y += px)
                    Px(g, y < b.Bottom - 54 + ((x / px) % 3 == 0 ? px : 0) ? V(Color.FromArgb(104, 160, 52), 16, r) : V(Color.FromArgb(124, 88, 60), 14, r), x, y, px);
        }
    }

    // the big picture at the top: a sunset scene with your player
    class HeroPanel : Control
    {
        public string Title = "BlockCraft", Sub = "Choose a version and play"; public Bitmap Skin; public bool SkinSlim;
        public HeroPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pb = new SolidBrush(UI.Bg)) g.FillRectangle(pb, ClientRectangle);
            using (var clip = UI.Round(new RectangleF(0, 0, Width - 1, Height - 1), 18))
            {
                var st = g.Save(); g.SetClip(clip);
                g.SmoothingMode = SmoothingMode.None; Art.Sunset(g, ClientRectangle);
                if (Skin != null) SkinArt.DrawFront(g, Skin, SkinSlim, Width / 2 - 48, Height - 70 - 192 + 6, 6); else Art.Player(g, Width / 2 - 48, Height - 70 - 192 + 6, 6);
                g.Restore(st); g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(UI.CardBorder, 2)) g.DrawPath(pen, clip);
            }
            // name tag at the top
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var tf = UI.F(17, true)) using (var sf = UI.F(9.5f, false))
            {
                SizeF a = g.MeasureString(Title, tf), b = g.MeasureString("→ " + Sub, sf);
                float w = Math.Max(a.Width, b.Width) + 30, x = (Width - w) / 2;
                using (var path = UI.Round(new RectangleF(x, 18, w, 56), 10)) using (var bg = new SolidBrush(Color.FromArgb(170, 40, 40, 48))) g.FillPath(bg, path);
                using (var wb = new SolidBrush(Color.White)) g.DrawString(Title, tf, wb, (Width - a.Width) / 2, 20);
                using (var mb = new SolidBrush(Color.FromArgb(225, 225, 230))) g.DrawString("→ " + Sub, sf, mb, (Width - b.Width) / 2, 50);
            }
        }
    }

    // one version in the list: an icon, its name and a green Play button
    class VersionCard : CardPanel
    {
        public readonly FancyButton PlayBtn;
        readonly string title, sub;
        public VersionCard(string title, string sub)
        {
            this.title = title; this.sub = sub; Height = 82;
            PlayBtn = new FancyButton("Play"); PlayBtn.Glyph = ""; PlayBtn.Base = UI.Green; PlayBtn.PixelTex = true; PlayBtn.TextSize = 13;
            Controls.Add(PlayBtn);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (PlayBtn != null) PlayBtn.Bounds = new Rectangle(Width - 150, 18, 128, 46); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.None;
            Art.GrassBlock(g, 20, 17, 48);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var tf = UI.F(13.5f, true)) using (var tb = new SolidBrush(UI.Text)) g.DrawString(title, tf, tb, 84, 14);
            using (var sf = UI.F(9.5f, false)) using (var sb = new SolidBrush(UI.Muted)) g.DrawString(sub, sf, sb, 86, 44);
        }
    }

    // a news card (from the version notes): an orange tag, a title and how long ago
    class NewsCard : CardPanel
    {
        readonly string tag, title, when;
        public NewsCard(string tag, string title, string when) { this.tag = tag; this.title = title; this.when = when; Height = 110; Fill = Color.FromArgb(40, 26, 18); Border = Color.FromArgb(90, 56, 30); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var path = UI.Round(new RectangleF(1, 1, Width - 3, Height - 3), 14))
            {
                var st = g.Save(); g.SetClip(path);
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Color.FromArgb(150, 90, 40), Color.FromArgb(36, 24, 18), 0f)) g.FillRectangle(b, 0, 0, Width, Height);
                UI.Pixels(g, new Rectangle(Width / 2, 0, Width / 2, Height), Color.Black, tag.Length + title.Length);
                g.Restore(st);
            }
            using (var tf = UI.F(8.5f, true))
            {
                SizeF s = g.MeasureString(tag, tf);
                using (var path = UI.Round(new RectangleF(18, 16, s.Width + 14, 24), 6)) using (var ob = new SolidBrush(UI.Orange)) g.FillPath(ob, path);
                using (var wb = new SolidBrush(Color.FromArgb(40, 16, 0))) g.DrawString(tag, tf, wb, 25, 20);
            }
            using (var f = UI.F(13.5f, true)) using (var wb = new SolidBrush(Color.White)) g.DrawString(title, f, wb, new RectangleF(18, 46, Width - 30, 30));
            using (var f = UI.F(9.5f, false)) using (var mb = new SolidBrush(Color.FromArgb(220, 210, 200))) g.DrawString(when, f, mb, 19, 78);
        }
    }

    // the blue banner at the top of the minigames panel
    class BannerPanel : Control
    {
        public string Caption = "MINIGAMES"; public Color Top1 = Color.FromArgb(60, 150, 240), Bottom1 = Color.FromArgb(30, 100, 210);
        public BannerPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var pb = new SolidBrush(UI.Card)) g.FillRectangle(pb, ClientRectangle);
            using (var b = new LinearGradientBrush(ClientRectangle, Top1, Bottom1, 90f)) g.FillRectangle(b, ClientRectangle);
            UI.Pixels(g, ClientRectangle, Color.White, 5);
            using (var f = UI.F(18, true)) using (var wb = new SolidBrush(Color.White))
            { SizeF s = g.MeasureString(Caption, f); g.DrawString(Caption, f, wb, (Width - s.Width) / 2, (Height - s.Height) / 2); }
        }
    }

    // ------------------------------------------------------------------
    //  Skins (Minecraft Java layout): the same built-in skins as the game, importing PNG skins, front-view pictures
    // ------------------------------------------------------------------
    class SkinLook
    {
        public string Name; public Color Skin, Hair, Shirt, Pants, Shoes = Color.FromArgb(60, 50, 40);
        public Color? Eyes, Helmet, Mask, Belt, Stripe, Gloves; public string Mouth = "#7a4a3a"; public int Sleeve = 4; public bool Long, Slim;
    }
    static class SkinArt
    {
        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        public static readonly string[] Order = { "hero", "heroine", "knight", "ninja", "pirate", "robot", "hunter", "zombie", "astronaut", "wizard" };
        public static SkinLook Preset(string k)
        {
            var o = new SkinLook();
            switch (k)
            {
                case "hero": o.Name = "Hero"; o.Skin = C(198, 140, 104); o.Hair = C(60, 38, 22); o.Shirt = C(40, 160, 170); o.Pants = C(50, 60, 150); o.Shoes = C(70, 70, 74); break;
                case "heroine": o.Name = "Heroine"; o.Skin = C(226, 176, 140); o.Hair = C(200, 120, 40); o.Shirt = C(80, 150, 70); o.Pants = C(110, 80, 50); o.Shoes = C(90, 60, 40); o.Long = true; o.Eyes = C(60, 140, 60); o.Slim = true; break;
                case "knight": o.Name = "Knight"; o.Skin = C(200, 150, 110); o.Hair = C(40, 30, 20); o.Shirt = C(170, 170, 180); o.Pants = C(120, 120, 130); o.Shoes = C(80, 80, 90); o.Helmet = C(190, 190, 200); o.Belt = C(120, 80, 40); o.Gloves = C(140, 140, 150); break;
                case "ninja": o.Name = "Ninja"; o.Skin = C(190, 140, 100); o.Hair = C(20, 20, 24); o.Shirt = C(30, 30, 36); o.Pants = C(26, 26, 30); o.Shoes = C(20, 20, 20); o.Mask = C(30, 30, 36); o.Belt = C(170, 30, 30); o.Eyes = C(200, 40, 40); break;
                case "pirate": o.Name = "Pirate"; o.Skin = C(180, 124, 90); o.Hair = C(30, 20, 14); o.Shirt = C(230, 230, 220); o.Pants = C(60, 40, 30); o.Shoes = C(40, 26, 18); o.Belt = C(200, 40, 40); o.Stripe = C(200, 40, 40); o.Sleeve = 12; break;
                case "robot": o.Name = "Robot"; o.Skin = C(150, 156, 166); o.Hair = C(110, 116, 126); o.Shirt = C(120, 126, 136); o.Pants = C(90, 96, 106); o.Shoes = C(60, 64, 70); o.Eyes = C(255, 60, 40); o.Mouth = "#333333"; o.Sleeve = 0; break;
                case "hunter": o.Name = "Zombie Hunter"; o.Skin = C(170, 120, 90); o.Hair = C(90, 60, 30); o.Shirt = C(70, 90, 50); o.Pants = C(80, 70, 50); o.Shoes = C(50, 40, 30); o.Belt = C(40, 30, 20); o.Gloves = C(50, 40, 30); break;
                case "zombie": o.Name = "Zombie"; o.Skin = C(96, 130, 80); o.Hair = C(50, 70, 40); o.Shirt = C(50, 140, 150); o.Pants = C(60, 60, 140); o.Shoes = C(60, 60, 140); o.Eyes = C(20, 20, 20); o.Mouth = "#2d4c26"; break;
                case "astronaut": o.Name = "Astronaut"; o.Skin = C(220, 224, 230); o.Hair = C(220, 224, 230); o.Shirt = C(230, 232, 236); o.Pants = C(220, 224, 230); o.Shoes = C(150, 150, 160); o.Helmet = C(230, 232, 236); o.Stripe = C(220, 90, 40); o.Gloves = C(150, 150, 160); break;
                case "wizard": o.Name = "Wizard"; o.Skin = C(214, 170, 130); o.Hair = C(230, 230, 236); o.Shirt = C(80, 50, 150); o.Pants = C(70, 40, 130); o.Shoes = C(40, 30, 60); o.Long = true; o.Belt = C(230, 200, 60); o.Sleeve = 12; break;
                default: return null;
            }
            return o;
        }
        // paints one box of the Minecraft layout: fn(face, i, j) gives the colour of each pixel (or null)
        static void Part(Bitmap b, int u, int v, int w, int h, int d, Func<string, int, int, Color?> fn)
        {
            var R = new Dictionary<string, int[]>();
            R["right"] = new[] { u, v + d, d, h }; R["front"] = new[] { u + d, v + d, w, h }; R["left"] = new[] { u + d + w, v + d, d, h };
            R["back"] = new[] { u + 2 * d + w, v + d, w, h }; R["top"] = new[] { u + d, v, w, d }; R["bottom"] = new[] { u + d + w, v, w, d };
            foreach (var kv in R)
            {
                int[] r = kv.Value;
                for (int j = 0; j < r[3]; j++) for (int i = 0; i < r[2]; i++) { Color? c = fn(kv.Key, i, j); if (c.HasValue) b.SetPixel(r[0] + i, r[1] + j, c.Value); }
            }
        }
        public static Bitmap Human(SkinLook o)
        {
            var b = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rnd = new Random(7);
            Func<Color, double, Color> sh = (c, k) => { double n = 1 + (rnd.NextDouble() - 0.5) * k; Func<double, int> f = x => Math.Max(0, Math.Min(255, (int)(x * n))); return Color.FromArgb(f(c.R), f(c.G), f(c.B)); };
            Color mouth = ColorTranslator.FromHtml(o.Mouth);
            Part(b, 0, 0, 8, 8, 8, (f, i, j) =>
            {
                if (o.Helmet.HasValue) return j < (f == "front" ? 3 : 8) || (f == "front" && (i == 0 || i == 7)) ? sh(o.Helmet.Value, 0.15) : f == "front" && j == 3 ? Color.FromArgb(17, 17, 17) : sh(o.Skin, 0.06);
                if (f == "top") return sh(o.Hair, 0.12); if (f == "bottom") return sh(o.Skin, 0.05);
                if (f == "front")
                {
                    if (j < 2 || (j == 2 && (i == 0 || i == 7)) || (o.Long && (i == 0 || i == 7) && j < 6)) return sh(o.Hair, 0.12);
                    if (o.Mask.HasValue && j >= 5) return sh(o.Mask.Value, 0.1);
                    if (j == 4 && (i == 1 || i == 6)) return Color.White;
                    if (j == 4 && (i == 2 || i == 5)) return o.Eyes.HasValue ? o.Eyes.Value : Color.FromArgb(48, 80, 192);
                    if (j == 6 && i >= 3 && i <= 4) return mouth;
                    return sh(o.Skin, 0.05);
                }
                if (f == "back") return j < (o.Long ? 8 : 6) ? sh(o.Hair, 0.12) : sh(o.Skin, 0.05);
                return j < (o.Long ? 7 : 3) ? sh(o.Hair, 0.12) : sh(o.Skin, 0.05);
            });
            if (o.Mask.HasValue) Part(b, 32, 0, 8, 8, 8, (f, i, j) => f == "front" && j >= 5 && j <= 6 ? (Color?)sh(o.Mask.Value, 0.1) : null);
            Func<string, int, int, Color?> body = (f, i, j) => o.Belt.HasValue && j == 9 ? sh(o.Belt.Value, 0.1) : o.Stripe.HasValue && f == "front" && (i == 3 || i == 4) ? sh(o.Stripe.Value, 0.1) : sh(o.Shirt, 0.12);
            Func<string, int, int, Color?> arm = (f, i, j) => j < o.Sleeve ? sh(o.Shirt, 0.12) : o.Gloves.HasValue && j >= 10 ? sh(o.Gloves.Value, 0.1) : sh(o.Skin, 0.05);
            Func<string, int, int, Color?> leg = (f, i, j) => j >= 10 ? sh(o.Shoes, 0.12) : sh(o.Pants, 0.12);
            Part(b, 16, 16, 8, 12, 4, body); Part(b, 40, 16, 4, 12, 4, arm); Part(b, 0, 16, 4, 12, 4, leg);
            Part(b, 32, 48, 4, 12, 4, arm); Part(b, 16, 48, 4, 12, 4, leg);
            return b;
        }
        public static bool Valid(string s)
        {
            return s != null && s.Length < 65000 && Regex.IsMatch(s, "^(preset:[a-z]{1,16}|data:image/png;base64,[A-Za-z0-9+/=]+)$");
        }
        // a skin string -> a 64x64 (or 64x32) bitmap, or null if it can't be used
        public static Bitmap Load(string s, out bool slim)
        {
            slim = false;
            try
            {
                if (string.IsNullOrEmpty(s)) return null;
                if (s.StartsWith("preset:")) { var o = Preset(s.Substring(7)); if (o == null) return null; slim = o.Slim; return Human(o); }
                if (!Valid(s)) return null;
                byte[] bytes = Convert.FromBase64String(s.Substring(s.IndexOf(',') + 1));
                using (var ms = new MemoryStream(bytes)) using (var img = Image.FromStream(ms))
                {
                    int W = img.Width, H = img.Height;
                    if (W < 64 || W % 64 != 0 || (H != W && H != W / 2)) return null;
                    var b = new Bitmap(64, H == W ? 64 : 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(img, 0, 0, 64, b.Height); }
                    if (b.Height == 64) slim = b.GetPixel(54, 20).A == 0 && b.GetPixel(55, 31).A == 0;
                    return b;
                }
            }
            catch { return null; }
        }
        // the player seen from the front: a 16x32-pixel picture drawn at (x, y), p screen pixels per skin pixel
        public static void DrawFront(Graphics g, Bitmap sk, bool slim, float x, float y, float p)
        {
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.SmoothingMode = SmoothingMode.None;
            int aw = slim ? 3 : 4; bool legacy = sk.Height == 32;
            Action<int, int, int, int, int, int> b = (sx, sy, w, h, dx, dy) => g.DrawImage(sk, new RectangleF(x + dx * p, y + dy * p, w * p, h * p), new RectangleF(sx, sy, w, h), GraphicsUnit.Pixel);
            b(8, 8, 8, 8, 4, 0); b(20, 20, 8, 12, 4, 8); b(44, 20, aw, 12, 4 - aw, 8); b(4, 20, 4, 12, 4, 20);
            if (legacy) { b(44, 20, aw, 12, 12, 8); b(4, 20, 4, 12, 8, 20); }
            else { b(36, 52, aw, 12, 12, 8); b(20, 52, 4, 12, 8, 20); b(40, 8, 8, 8, 4, 0); b(20, 36, 8, 12, 4, 8); b(44, 36, aw, 12, 4 - aw, 8); b(52, 52, aw, 12, 12, 8); b(4, 36, 4, 12, 4, 20); b(4, 52, 4, 12, 8, 20); }
            g.Restore(st);
        }
    }

    // a skin choice: a little picture of the skin and its name
    class SkinTile : Control
    {
        public string Value; public string Label; public bool Selected; Bitmap bmp; bool slim, hover;
        public SkinTile(string value, string label) { Value = value; Label = label; DoubleBuffered = true; Cursor = Cursors.Hand; bmp = SkinArt.Load(value == "" ? "preset:hero" : value, out slim); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pb = new SolidBrush(Parent != null ? Parent.BackColor : UI.Bg)) g.FillRectangle(pb, ClientRectangle);
            using (var path = UI.Round(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            using (var b = new SolidBrush(Selected ? Color.FromArgb(30, 60, 44) : hover ? Color.FromArgb(36, 36, 44) : Color.FromArgb(28, 28, 34)))
            using (var pen = new Pen(Selected ? Color.FromArgb(120, 255, 170) : hover ? Color.FromArgb(120, 255, 170) : UI.CardBorder, 2)) { g.FillPath(b, path); g.DrawPath(pen, path); }
            if (bmp != null) SkinArt.DrawFront(g, bmp, slim, (Width - 48) / 2f, 8, 3);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var f = UI.F(8.5f, true)) using (var tb = new SolidBrush(UI.Text)) using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(Label, f, tb, new RectangleF(2, Height - 22, Width - 4, 20), sf);
        }
    }

    // the big picture of your skin on the Skins page
    class SkinPreview : Control
    {
        public Bitmap Bmp; public bool Slim; public string Caption = "";
        public SkinPreview() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pb = new SolidBrush(UI.Bg)) g.FillRectangle(pb, ClientRectangle);
            using (var path = UI.Round(new RectangleF(1, 1, Width - 3, Height - 3), 16))
            {
                using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(44, 58, 74), Color.FromArgb(18, 20, 26), 90f)) g.FillPath(b, path);
                using (var pen = new Pen(UI.CardBorder, 2)) g.DrawPath(pen, path);
            }
            if (Bmp != null) SkinArt.DrawFront(g, Bmp, Slim, (Width - 160) / 2f, 50, 10);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var f = UI.F(14, true)) using (var tb = new SolidBrush(Color.White)) using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(Caption, f, tb, new RectangleF(0, 14, Width, 30), sf);
        }
    }

    class LauncherForm : Form
    {
        readonly GameServer server;
        readonly Label status;
        readonly ComboBox versionBox;
        readonly CheckBox appWindow;
        readonly FancyButton play, updates;
        readonly LinkLabel newLauncher;
        readonly HeroPanel hero;
        readonly Panel versionList, newsList, mgPage, verPage, skinPage, skinGrid, acctPage;
        FancyButton acctTop, acLogin, acSignup, acLogout, acRefresh;
        TextBox acName, acPass, acPass2;
        Label acInfo, acMsg, acNameLabel, acPassLabel, acPass2Label;
        readonly SkinPreview skinPreview;
        readonly Label skinMsg;
        readonly FancyButton skinTab;
        readonly Control[] homeCtrls;
        readonly FancyButton logoTab, mgTab;
        readonly List<FancyButton> sideNav = new List<FancyButton>();
        readonly Versions versions = new Versions();
        bool played, busy;
        readonly List<FancyButton> playButtons = new List<FancyButton>();

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public LauncherForm(GameServer server)
        {
            this.server = server;
            Text = "BlockCraft Launcher " + Program.LauncherVersion;
            ClientSize = new Size(1180, 720);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UI.Bg;
            ForeColor = UI.Text;
            Font = UI.F(9.5f, false);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // ---- side bar ----
            var side = new Panel(); side.BackColor = UI.Side; side.Bounds = new Rectangle(0, 0, 76, 720); Controls.Add(side);
            Func<string, string, int, Color, Action, FancyButton> sideBtn = (glyph, tip, y, col, act) =>
            {
                var b = new FancyButton(""); b.Glyph = glyph; b.Base = col; b.TextSize = 15; b.Radius = 12; b.Bounds = new Rectangle(12, y, 52, 50);
                b.Click += delegate { act(); }; new ToolTip().SetToolTip(b, tip); side.Controls.Add(b); return b;
            };
            sideNav.Add(sideBtn("", "Home", 18, Color.FromArgb(40, 40, 48), delegate { ShowPage(0); }));
            sideNav.Add(sideBtn("", "Minigames", 84, Color.FromArgb(26, 26, 32), delegate { ShowPage(1); }));
            sideNav.Add(sideBtn("", "All versions", 146, Color.FromArgb(26, 26, 32), delegate { ShowPage(2); }));
            sideNav.Add(sideBtn("\uE77B", "Skins", 208, Color.FromArgb(26, 26, 32), delegate { ShowPage(3); }));
            sideNav.Add(sideBtn("", "Account (cloud saves)", 394, Color.FromArgb(26, 26, 32), delegate { ShowPage(4); }));
            sideBtn("", "Open the saves folder", 270, Color.FromArgb(26, 26, 32), delegate { OpenSaves(); });
            sideBtn("", "Check for updates", 332, Color.FromArgb(26, 26, 32), delegate { CheckForUpdates(true); });
            sideBtn("", "Quit", 580, Color.FromArgb(26, 26, 32), delegate { Close(); });
            var sidePlay = sideBtn("", "Play the newest version", 644, UI.Green, delegate { LaunchLatest(""); });
            sidePlay.PixelTex = true; playButtons.Add(sidePlay);

            // ---- top: the BlockCraft tab ----
            var tabs = new CardPanel(); tabs.Bounds = new Rectangle(100, 18, 590, 64); tabs.Fill = Color.FromArgb(20, 20, 24); tabs.Radius = 16; Controls.Add(tabs);
            logoTab = new FancyButton("BLOCKCRAFT"); logoTab.Click += delegate { ShowPage(0); }; logoTab.Base = UI.Orange; logoTab.PixelTex = true; logoTab.TextSize = 17; logoTab.Radius = 12; logoTab.Bounds = new Rectangle(6, 6, 220, 52);
            tabs.Controls.Add(logoTab);
            mgTab = new FancyButton("MINIGAMES"); mgTab.Base = Color.FromArgb(20, 20, 24); mgTab.ForeColor = Color.FromArgb(150, 150, 160); mgTab.TextSize = 15; mgTab.Bounds = new Rectangle(232, 6, 180, 52);
            mgTab.Click += delegate { ShowPage(1); }; tabs.Controls.Add(mgTab);
            skinTab = new FancyButton("SKINS"); skinTab.Base = Color.FromArgb(20, 20, 24); skinTab.ForeColor = Color.FromArgb(150, 150, 160); skinTab.TextSize = 15; skinTab.Bounds = new Rectangle(418, 6, 166, 52);
            skinTab.Click += delegate { ShowPage(3); }; tabs.Controls.Add(skinTab);

            updates = new FancyButton("Updates"); updates.Glyph = ""; updates.Base = Color.FromArgb(32, 32, 38); updates.TextSize = 13; updates.Bounds = new Rectangle(850, 22, 150, 56);
            updates.Click += delegate { CheckForUpdates(true); }; Controls.Add(updates);
            var savesTop = new FancyButton("Saves"); savesTop.Glyph = ""; savesTop.Base = Color.FromArgb(232, 180, 40); savesTop.ForeColor = Color.FromArgb(40, 30, 0); savesTop.TextSize = 13;
            savesTop.Bounds = new Rectangle(1010, 22, 150, 56); savesTop.Click += delegate { OpenSaves(); }; Controls.Add(savesTop);
            acctTop = new FancyButton("Log In"); acctTop.Glyph = ""; acctTop.Base = UI.Blue; acctTop.TextSize = 12; acctTop.Bounds = new Rectangle(700, 22, 140, 56);
            acctTop.Click += delegate { ShowPage(4); }; Controls.Add(acctTop); new ToolTip().SetToolTip(acctTop, "Your account: cloud saves for achievements and worlds");

            // ---- hero banner with the version picker ----
            hero = new HeroPanel(); hero.Bounds = new Rectangle(100, 98, 700, 330); Controls.Add(hero);
            var dock = new CardPanel(); dock.Fill = Color.FromArgb(28, 28, 34); dock.Radius = 12; dock.Bounds = new Rectangle(160, 268, 380, 52); hero.Controls.Add(dock);
            versionBox = new ComboBox();
            versionBox.DropDownStyle = ComboBoxStyle.DropDownList; versionBox.FlatStyle = FlatStyle.Flat;
            versionBox.BackColor = Color.FromArgb(28, 28, 34); versionBox.ForeColor = UI.Text; versionBox.Font = UI.F(10.5f, true);
            versionBox.Bounds = new Rectangle(12, 12, 236, 28);
            versionBox.SelectedIndexChanged += delegate { ShowSelected(); };
            versionBox.DrawMode = DrawMode.OwnerDrawFixed; versionBox.ItemHeight = 22;
            versionBox.DrawItem += (o, e) =>
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (var bg = new SolidBrush(sel ? Color.FromArgb(46, 130, 230) : Color.FromArgb(28, 28, 34))) e.Graphics.FillRectangle(bg, e.Bounds);
                TextRenderer.DrawText(e.Graphics, versionBox.Items[e.Index].ToString(), versionBox.Font, e.Bounds, UI.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            dock.Controls.Add(versionBox);
            play = new FancyButton("PLAY"); play.Glyph = ""; play.Base = UI.Green; play.PixelTex = true; play.TextSize = 13; play.Bounds = new Rectangle(256, 6, 116, 40);
            play.Click += delegate { Play(Selected, ""); }; dock.Controls.Add(play); playButtons.Add(play);

            // ---- versions ----
            var vh = new Label(); vh.Text = "Recent Versions"; vh.Font = UI.F(12.5f, true); vh.ForeColor = UI.Text; vh.AutoSize = true; vh.Location = new Point(104, 442); Controls.Add(vh);
            versionList = new Panel(); versionList.BackColor = UI.Bg; versionList.Bounds = new Rectangle(100, 474, 700, 180); Controls.Add(versionList);
            // ---- the other pages, shown in place of the home page ----
            homeCtrls = new Control[] { hero, vh, versionList };
            mgPage = new Panel(); mgPage.BackColor = UI.Bg; mgPage.Bounds = new Rectangle(100, 98, 700, 556); mgPage.Visible = false; Controls.Add(mgPage);
            Action<string, string, Color, Color, string, int> mgCard = (caption, desc, top, bottom, hash, y) =>
            {
                var card = new CardPanel(); card.Bounds = new Rectangle(0, y, 700, 262); card.Radius = 16; mgPage.Controls.Add(card);
                var ban = new BannerPanel(); ban.Caption = caption; ban.Top1 = top; ban.Bottom1 = bottom; ban.Bounds = new Rectangle(2, 2, 696, 84); card.Controls.Add(ban);
                var d = new Label(); d.Text = desc; d.Font = UI.F(11f, false); d.ForeColor = UI.Text; d.BackColor = card.Fill; d.Bounds = new Rectangle(26, 104, 470, 140); card.Controls.Add(d);
                var b = new FancyButton("Play"); b.Glyph = ""; b.Base = top; b.PixelTex = true; b.TextSize = 15; b.Bounds = new Rectangle(522, 150, 150, 60);
                b.Click += delegate { LaunchLatest(hash); }; card.Controls.Add(b); playButtons.Add(b);
            };
            mgCard("ZOMBIE APOCALYPSE", "Survive the undead alone or with up to 4 friends.\n\n4 maps: Dark Forest, Desert, Plains and Ruined City.\nSurvival or Waves - 11 kinds of zombies.\nCrossbows, flame arrows, firecrackers and ammo crates.",
                Color.FromArgb(200, 50, 46), Color.FromArgb(110, 20, 20), "#zombies", 0);
            mgCard("PVP DUELS", "Fight other players 1v1 in an arena.\n\n4 maps and 6 kits - first to 3 round wins.\nFind a random match, or duel a friend by both typing the same secret code.",
                UI.Blue, Color.FromArgb(24, 80, 170), "#pvp", 288);
            verPage = new Panel(); verPage.BackColor = UI.Bg; verPage.Bounds = new Rectangle(100, 98, 700, 556); verPage.AutoScroll = true; verPage.Visible = false; Controls.Add(verPage);
            // ---- the Skins page ----
            skinPage = new Panel(); skinPage.BackColor = UI.Bg; skinPage.Bounds = new Rectangle(100, 98, 700, 556); skinPage.Visible = false; Controls.Add(skinPage);
            skinPreview = new SkinPreview(); skinPreview.Bounds = new Rectangle(0, 0, 250, 420); skinPage.Controls.Add(skinPreview);
            skinGrid = new Panel(); skinGrid.BackColor = UI.Bg; skinGrid.Bounds = new Rectangle(266, 0, 434, 420); skinGrid.AutoScroll = true; skinPage.Controls.Add(skinGrid);
            var importBtn = new FancyButton("Import Minecraft Skin (.png)"); importBtn.Glyph = ""; importBtn.Base = UI.Green; importBtn.PixelTex = true; importBtn.TextSize = 12;
            importBtn.Bounds = new Rectangle(0, 434, 340, 50); importBtn.Click += delegate { ImportSkin(); }; skinPage.Controls.Add(importBtn);
            var defBtn = new FancyButton("Use Default Skin"); defBtn.Base = Color.FromArgb(40, 40, 48); defBtn.TextSize = 12;
            defBtn.Bounds = new Rectangle(352, 434, 220, 50); defBtn.Click += delegate { SetSkin(""); }; skinPage.Controls.Add(defBtn);
            skinMsg = new Label(); skinMsg.ForeColor = UI.Muted; skinMsg.BackColor = UI.Bg; skinMsg.Font = UI.F(9.5f, false);
            skinMsg.Bounds = new Rectangle(2, 494, 690, 60);
            skinMsg.Text = "Works with Minecraft Java skins (64x64, old 64x32, or HD). Your skin shows in the game, to friends, on the big server and in PvP.";
            skinPage.Controls.Add(skinMsg);
            if (server != null) server.SkinChanged += delegate { try { BeginInvoke((Action)delegate { RefreshSkin(); }); } catch { } };
            // ---- the Account page (log in for cloud saves; the game uses the same login) ----
            acctPage = new Panel(); acctPage.BackColor = UI.Bg; acctPage.Bounds = new Rectangle(100, 98, 700, 556); acctPage.Visible = false; Controls.Add(acctPage);
            var acCard = new CardPanel(); acCard.Fill = UI.Card; acCard.Radius = 16; acCard.Bounds = new Rectangle(0, 0, 700, 540); acctPage.Controls.Add(acCard);
            var acTitle = new Label(); acTitle.Text = "YOUR ACCOUNT"; acTitle.Font = UI.F(22f, true); acTitle.ForeColor = Color.FromArgb(143, 208, 255); acTitle.BackColor = UI.Card;
            acTitle.Bounds = new Rectangle(30, 24, 640, 46); acCard.Controls.Add(acTitle);
            acInfo = new Label(); acInfo.Font = UI.F(10.5f, false); acInfo.ForeColor = UI.Muted; acInfo.BackColor = UI.Card; acInfo.Bounds = new Rectangle(32, 74, 636, 70);
            acCard.Controls.Add(acInfo);
            Func<string, int, Label> acLabel = (txt, y) => { var l = new Label(); l.Text = txt; l.Font = UI.F(11f, true); l.ForeColor = UI.Text; l.BackColor = UI.Card; l.Bounds = new Rectangle(32, y + 4, 170, 26); acCard.Controls.Add(l); return l; };
            Func<int, bool, TextBox> acBox = (y, pw) => { var t = new TextBox(); t.Font = UI.F(12f, false); t.BackColor = Color.FromArgb(8, 8, 10); t.ForeColor = UI.Text; t.BorderStyle = BorderStyle.FixedSingle;
                t.Bounds = new Rectangle(210, y, 300, 30); t.MaxLength = pw ? 64 : 16; if (pw) t.UseSystemPasswordChar = true; acCard.Controls.Add(t); return t; };
            acNameLabel = acLabel("Name", 156); acName = acBox(156, false);
            acPassLabel = acLabel("Password", 198); acPass = acBox(198, true);
            acPass2Label = acLabel("Password again", 240); acPass2 = acBox(240, true);
            acLogin = new FancyButton("Log In"); acLogin.Base = UI.Green; acLogin.PixelTex = true; acLogin.TextSize = 13; acLogin.Bounds = new Rectangle(32, 296, 230, 52);
            acLogin.Click += delegate { AccountGo(false); }; acCard.Controls.Add(acLogin);
            acSignup = new FancyButton("Create Account"); acSignup.Base = UI.Blue; acSignup.TextSize = 13; acSignup.Bounds = new Rectangle(274, 296, 236, 52);
            acSignup.Click += delegate { AccountGo(true); }; acCard.Controls.Add(acSignup);
            acLogout = new FancyButton("Log Out"); acLogout.Base = Color.FromArgb(150, 50, 50); acLogout.TextSize = 13; acLogout.Bounds = new Rectangle(32, 296, 230, 52);
            acLogout.Click += delegate { AccountLogout(); }; acCard.Controls.Add(acLogout);
            acRefresh = new FancyButton("Refresh"); acRefresh.Base = Color.FromArgb(40, 40, 48); acRefresh.TextSize = 13; acRefresh.Bounds = new Rectangle(274, 296, 236, 52);
            acRefresh.Click += delegate { RefreshAccount(true); }; acCard.Controls.Add(acRefresh);
            acMsg = new Label(); acMsg.Font = UI.F(10.5f, false); acMsg.ForeColor = Color.FromArgb(255, 215, 94); acMsg.BackColor = UI.Card; acMsg.Bounds = new Rectangle(32, 360, 636, 60);
            acCard.Controls.Add(acMsg);
            var acHint = new Label(); acHint.Font = UI.F(9.5f, false); acHint.ForeColor = UI.Muted; acHint.BackColor = UI.Card; acHint.Bounds = new Rectangle(32, 428, 636, 100);
            acHint.Text = "Logged in, your achievements and worlds are saved in the cloud, so they are on every computer you log in on. " +
                "It is the same account as the big server.\nNames: letters, numbers, spaces, _ and - (up to 16). Passwords: at least 6 characters - " +
                "don't use a password from another website, and remember it (it can't be reset).";
            acCard.Controls.Add(acHint);
            acPass.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AccountGo(acPass2.Visible); } };
            acPass2.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AccountGo(true); } };
            if (server != null) server.AccountChanged += delegate { try { BeginInvoke((Action)delegate { RefreshAccount(false); }); } catch { } };
            RefreshAccount(false);

            // ---- bottom buttons ----
            var savesBtn = new FancyButton("Open Saves Folder"); savesBtn.Glyph = ""; savesBtn.Base = UI.Orange; savesBtn.PixelTex = true; savesBtn.TextSize = 12;
            savesBtn.Bounds = new Rectangle(100, 662, 340, 46); savesBtn.Click += delegate { OpenSaves(); }; Controls.Add(savesBtn);
            appWindow = new CheckBox();
            appWindow.Text = "Open in its own game window (Microsoft Edge)";
            appWindow.Checked = FindEdge() != null; appWindow.Enabled = FindEdge() != null;
            appWindow.ForeColor = UI.Muted; appWindow.AutoSize = true; appWindow.Location = new Point(458, 676); Controls.Add(appWindow);

            // ---- news ----
            var nh = new Label(); nh.Text = "●  Latest News"; nh.Font = UI.F(12.5f, true); nh.ForeColor = UI.Text; nh.AutoSize = true; nh.Location = new Point(828, 100); Controls.Add(nh);
            newsList = new Panel(); newsList.BackColor = UI.Bg; newsList.Bounds = new Rectangle(824, 132, 336, 236); Controls.Add(newsList);

            // ---- minigames (blue, like a server list) ----
            var mg = new CardPanel(); mg.Bounds = new Rectangle(824, 380, 336, 270); mg.Radius = 14; Controls.Add(mg);
            var banner = new BannerPanel(); banner.Bounds = new Rectangle(2, 2, 332, 64); mg.Controls.Add(banner);
            Action<string, string, string, string, int> mgRow = (name, desc, hash, glyph, y) =>
            {
                var row = new CardPanel(); row.Fill = Color.FromArgb(30, 30, 37); row.Radius = 10; row.Bounds = new Rectangle(12, y, 312, 84); mg.Controls.Add(row);
                var n = new Label(); n.Text = name; n.Font = UI.F(11.5f, true); n.ForeColor = UI.Text; n.BackColor = row.Fill; n.AutoSize = true; n.Location = new Point(14, 14); row.Controls.Add(n);
                var d = new Label(); d.Text = desc; d.Font = UI.F(8.5f, false); d.ForeColor = UI.Muted; d.BackColor = row.Fill; d.AutoSize = true; d.Location = new Point(15, 44); row.Controls.Add(d);
                var b = new FancyButton("Play"); b.Glyph = glyph; b.Base = UI.Blue; b.PixelTex = true; b.TextSize = 11; b.Bounds = new Rectangle(196, 20, 104, 42);
                b.Click += delegate { LaunchLatest(hash); }; row.Controls.Add(b); playButtons.Add(b);
            };
            mgRow("Zombie Apocalypse", "Survive alone or with friends", "#zombies", "", 78);
            mgRow("PvP Duels", "1v1 other players online", "#pvp", "", 172);

            // ---- status line ----
            status = new Label(); status.ForeColor = UI.Muted; status.BackColor = UI.Bg; status.AutoEllipsis = true;
            status.Bounds = new Rectangle(824, 662, 336, 46); status.TextAlign = ContentAlignment.MiddleLeft; Controls.Add(status);
            newLauncher = new LinkLabel(); newLauncher.Text = "New launcher available"; newLauncher.LinkColor = Color.FromArgb(255, 210, 80); newLauncher.ActiveLinkColor = Color.White;
            newLauncher.BackColor = UI.Bg; newLauncher.AutoSize = true; newLauncher.Visible = false; newLauncher.Location = new Point(828, 652);
            newLauncher.LinkClicked += delegate { OpenLauncherPage(); }; Controls.Add(newLauncher); newLauncher.BringToFront();

            try { versions.LoadLocal(); } catch (Exception e) { status.Text = "Could not read versions.json: " + e.Message; }
            FillVersions(null);
            RefreshSkin();
            UpdateStatus();

            HandleCreated += delegate { try { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { } }; // dark title bar
            if (server != null)
            {
                server.Saved += delegate { try { BeginInvoke((Action)UpdateStatus); } catch { } };
                Shown += delegate { CheckForUpdates(false); };
            }
        }

        void FillVersions(string keep)
        {
            versions.Refresh();
            versionBox.Items.Clear();
            int sel = 0;
            foreach (var v in versions.List)
            {
                int i = versionBox.Items.Add(v);
                if (keep != null ? v.Id == keep : v.Latest) sel = i;
            }
            if (versionBox.Items.Count > 0) versionBox.SelectedIndex = sel;
            // the three newest versions as cards
            versionList.Controls.Clear();
            int y = 0, n = 0;
            foreach (var v in versions.List)
            {
                if (n++ >= 2) break;
                var vv = v;
                var card = new VersionCard("BlockCraft " + v.Id + (v.Latest ? "   (newest)" : ""), (string.IsNullOrEmpty(v.Title) ? "" : v.Title + "  ·  ") + (v.Installed ? "Ready to play" : "Downloads when you press Play"));
                card.Bounds = new Rectangle(0, y, 700, 82); card.PlayBtn.Click += delegate { Play(vv, ""); };
                versionList.Controls.Add(card); y += 92;
            }
            verPage.Controls.Clear();
            int vy = 0;
            foreach (var v in versions.List)
            {
                var vv = v;
                var card = new VersionCard("BlockCraft " + v.Id + (v.Latest ? "   (newest)" : ""), (string.IsNullOrEmpty(v.Title) ? "" : v.Title + "  ·  ") + (v.Installed ? "Ready to play" : "Downloads when you press Play"));
                card.Bounds = new Rectangle(0, vy, 676, 82); card.PlayBtn.Click += delegate { Play(vv, ""); };
                new ToolTip().SetToolTip(card, v.Notes ?? ""); verPage.Controls.Add(card); vy += 92;
            }
            // news: the notes of the newest versions
            newsList.Controls.Clear();
            int ny = 0, k = 0;
            foreach (var v in versions.List)
            {
                if (k++ >= 2) break;
                var card = new NewsCard(v.Latest ? "NEW UPDATE" : "UPDATE", "BlockCraft " + v.Id + (string.IsNullOrEmpty(v.Title) ? "" : ": " + v.Title), Ago(v.Date));
                card.Bounds = new Rectangle(0, ny, 336, 110); new ToolTip().SetToolTip(card, v.Notes ?? "");
                newsList.Controls.Add(card); ny += 122;
            }
            ShowSelected();
        }

        string SkinFile { get { return Path.Combine(server != null ? server.Saves : Path.Combine(Versions.RootDir, "saves"), "skin.txt"); } }
        string CurrentSkin() { try { return File.Exists(SkinFile) ? File.ReadAllText(SkinFile).Trim() : ""; } catch { return ""; } }
        string CustomSkinFile { get { return Path.Combine(Path.GetDirectoryName(SkinFile), "skin-imported.txt"); } }

        // fills the Skins page and the player on the home page with your current skin
        void RefreshSkin()
        {
            string cur = CurrentSkin(); bool slim;
            Bitmap bmp = SkinArt.Load(cur == "" ? "preset:hero" : cur, out slim);
            if (bmp == null) { cur = ""; bmp = SkinArt.Load("preset:hero", out slim); }
            skinPreview.Bmp = bmp; skinPreview.Slim = slim;
            skinPreview.Caption = cur == "" ? "Default skin" : cur.StartsWith("preset:") ? SkinArt.Preset(cur.Substring(7)).Name : "Your imported skin";
            skinPreview.Invalidate();
            hero.Skin = bmp; hero.SkinSlim = slim; hero.Invalidate();
            skinGrid.Controls.Clear();
            var opts = new List<string[]>(); opts.Add(new[] { "", "Default" });
            foreach (var k in SkinArt.Order) opts.Add(new[] { "preset:" + k, SkinArt.Preset(k).Name });
            string custom = ""; try { if (File.Exists(CustomSkinFile)) custom = File.ReadAllText(CustomSkinFile).Trim(); } catch { }
            if (SkinArt.Valid(custom) && custom.StartsWith("data:")) opts.Add(new[] { custom, "My Skin" });
            int i = 0;
            foreach (var o in opts)
            {
                var t = new SkinTile(o[0], o[1]); t.Selected = o[0] == cur;
                t.Bounds = new Rectangle((i % 4) * 104, (i / 4) * 128, 98, 122);
                string val = o[0]; t.Click += delegate { SetSkin(val); };
                skinGrid.Controls.Add(t); i++;
            }
        }

        void SetSkin(string val)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SkinFile));
                if (val == "") { if (File.Exists(SkinFile)) File.Delete(SkinFile); } else File.WriteAllText(SkinFile, val);
                skinMsg.Text = "Skin saved! The game uses it the next time it starts (or when you go back to its title screen).";
            }
            catch (Exception e) { skinMsg.Text = "Could not save the skin: " + e.Message; }
            RefreshSkin();
        }

        void ImportSkin()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Choose a Minecraft skin"; dlg.Filter = "Minecraft skin (*.png)|*.png";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var info = new FileInfo(dlg.FileName);
                    if (info.Length > 45000) { skinMsg.Text = "That picture is too big for a skin (a skin is a small 64x64 PNG)."; return; }
                    string data = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(dlg.FileName));
                    bool slim;
                    if (SkinArt.Load(data, out slim) == null) { skinMsg.Text = "That is not a Minecraft skin: it must be a 64x64 or 64x32 PNG."; return; }
                    File.WriteAllText(CustomSkinFile, data);
                    SetSkin(data);
                }
                catch (Exception e) { skinMsg.Text = "Could not read that file: " + e.Message; }
            }
        }

        string SavesDir { get { return server != null ? server.Saves : Path.Combine(Versions.RootDir, "saves"); } }
        string[] Account() { var a = AccountStore.Read(SavesDir); return a != null && a[0] != "" ? a : null; }

        // shows the logged-in / logged-out version of the Account page, and the name on the top button
        void RefreshAccount(bool fetch)
        {
            var a = Account();
            acctTop.Text = a != null ? a[0] : "Log In"; acctTop.Invalidate();
            foreach (Control c in new Control[] { acNameLabel, acName, acPassLabel, acPass, acLogin, acSignup }) c.Visible = a == null;
            acPass2.Visible = acPass2Label.Visible = a == null && signingUp;
            acLogout.Visible = acRefresh.Visible = a != null;
            if (a == null)
            {
                acInfo.Text = "Log in to keep your achievements and worlds in the cloud. You can log in here or in the game - it is the same login.";
                if (acName.Text == "") { try { acName.Text = File.Exists(Path.Combine(SavesDir, "name.txt")) ? File.ReadAllText(Path.Combine(SavesDir, "name.txt")).Trim() : ""; } catch { } }
                return;
            }
            acInfo.Text = "You are logged in as " + a[0] + ". Your achievements and worlds are saved in the cloud.";
            if (!fetch) return;
            acInfo.Text += "\nChecking the cloud...";
            string name = a[0], token = a[1];
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text; bool err = false;
                try
                {
                    var d = AccountStore.Call("/api/cloud", "GET", null, name, token);
                    int worlds = 0, ach = 0; object w, s;
                    if (d.TryGetValue("worlds", out w) && w is System.Collections.ArrayList) worlds = ((System.Collections.ArrayList)w).Count;
                    if (d.TryGetValue("save", out s) && s is Dictionary<string, object>) { object x; if (((Dictionary<string, object>)s).TryGetValue("ach", out x) && x is Dictionary<string, object>) ach = ((Dictionary<string, object>)x).Count; }
                    text = "In the cloud: " + ach + " achievement" + (ach == 1 ? "" : "s") + " and " + worlds + " world" + (worlds == 1 ? "" : "s") + ".";
                }
                catch (Exception e)
                {
                    text = e.Message; err = true;
                    if (e.Message.StartsWith("Please log in")) { AccountStore.Write(SavesDir, "", ""); }
                }
                try { BeginInvoke((Action)delegate { RefreshAccount(false); if (err) acMsg.Text = text; else if (Account() != null) acInfo.Text += "\n" + text; }); } catch { }
            });
        }

        bool signingUp;
        void AccountGo(bool signup)
        {
            string name = acName.Text.Trim(), pass = acPass.Text;
            if (!Regex.IsMatch(name, "^[A-Za-z0-9_ \\-]{1,16}$")) { acMsg.Text = "Pick a name with only letters, numbers, spaces, _ or - (up to 16)."; return; }
            if (pass.Length < 6) { acMsg.Text = "Passwords have at least 6 characters."; return; }
            if (signup && !signingUp) { signingUp = true; RefreshAccount(false); acMsg.Text = "Type your new password again to make the account."; acPass2.Focus(); return; }
            if (signup && acPass2.Text != pass) { acMsg.Text = "The two passwords are different. Type them again."; return; }
            acMsg.Text = signup ? "Creating your account..." : "Logging in...";
            acLogin.Enabled = acSignup.Enabled = false;
            var old = AccountStore.Read(SavesDir);
            string token = old != null && old[1] != "" ? old[1] : AccountStore.NewToken();
            string body = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "name", name }, { "pass", pass }, { "token", token }, { "signup", signup } });
            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg; bool ok = false;
                try
                {
                    var d = AccountStore.Call("/api/login", "POST", body, null, null);
                    object n; string real = d.TryGetValue("name", out n) && n is string ? (string)n : name;
                    AccountStore.Write(SavesDir, real, token);
                    msg = signup ? "Welcome, " + real + "! Your account is made." : "Welcome back, " + real + "!";
                    ok = true;
                }
                catch (Exception e) { msg = e.Message; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        acLogin.Enabled = acSignup.Enabled = true;
                        if (ok) { acPass.Text = acPass2.Text = ""; signingUp = false; RefreshAccount(true); msg += " The game logs in by itself the next time it starts (or goes back to its title screen)."; }
                        acMsg.Text = msg;
                    });
                }
                catch { }
            });
        }

        void AccountLogout()
        {
            AccountStore.Write(SavesDir, "", "");
            acMsg.Text = "Logged out. Your worlds stay on this computer too.";
            RefreshAccount(false);
        }

        public void ShowPage(int n)
        {
            foreach (var c in homeCtrls) c.Visible = n == 0;
            mgPage.Visible = n == 1; verPage.Visible = n == 2; skinPage.Visible = n == 3; acctPage.Visible = n == 4;
            if (n == 4) { acMsg.Text = ""; signingUp = false; RefreshAccount(true); }
            logoTab.Base = n == 1 ? Color.FromArgb(20, 20, 24) : UI.Orange; logoTab.PixelTex = n != 1; logoTab.ForeColor = n == 1 ? Color.FromArgb(150, 150, 160) : Color.White;
            mgTab.Base = n == 1 ? UI.Orange : Color.FromArgb(20, 20, 24); mgTab.PixelTex = n == 1; mgTab.ForeColor = n == 1 ? Color.White : Color.FromArgb(150, 150, 160);
            bool lt = n == 0 || n == 2;
            logoTab.Base = lt ? UI.Orange : Color.FromArgb(20, 20, 24); logoTab.PixelTex = lt; logoTab.ForeColor = lt ? Color.White : Color.FromArgb(150, 150, 160);
            skinTab.Base = n == 3 ? UI.Orange : Color.FromArgb(20, 20, 24); skinTab.PixelTex = n == 3; skinTab.ForeColor = n == 3 ? Color.White : Color.FromArgb(150, 150, 160);
            logoTab.Invalidate(); mgTab.Invalidate(); skinTab.Invalidate();
            for (int i = 0; i < sideNav.Count; i++) { sideNav[i].Base = i == n ? Color.FromArgb(40, 40, 48) : Color.FromArgb(26, 26, 32); sideNav[i].Invalidate(); }
        }

        static string Ago(string date)
        {
            DateTime d; if (!DateTime.TryParse(date, out d)) return "";
            int days = (int)(DateTime.Today - d.Date).TotalDays;
            return days <= 0 ? "Today" : days == 1 ? "Yesterday" : days + " days ago";
        }

        VersionInfo Selected { get { return versionBox.SelectedItem as VersionInfo; } }

        void ShowSelected()
        {
            var v = Selected;
            hero.Title = "BlockCraft" + (v != null ? " " + v.Id : "");
            hero.Sub = v == null ? "Choose a version and play" : !v.Installed ? "Press Play to download it" : v.Latest ? "The newest version - press Play!" : "Old versions keep their own worlds";
            hero.Invalidate();
        }

        void UpdateStatus()
        {
            if (busy) return;
            if (server == null) { status.Text = "Ready"; return; }
            int n = server.WorldCount();
            status.Text = "Ready  ·  " + n + (n == 1 ? " world" : " worlds") + " saved";
        }

        void SetStatus(string text) { try { BeginInvoke((Action)delegate { status.Text = text; }); } catch { } }

        // Asks GitHub for versions.json. A newer game version is downloaded and installed automatically.
        void CheckForUpdates(bool manual)
        {
            if (busy) return;
            string repo = Versions.Repo();
            if (repo == null)
            {
                if (manual) MessageBox.Show("Updates are not set up yet.\n\nRun \"Publish to GitHub.bat\" once (see README.txt) and the launcher will check that GitHub repository for new versions.",
                    "BlockCraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            busy = true; updates.Enabled = false;
            status.Text = "Checking GitHub for updates...";
            string keep = Selected != null ? Selected.Id : null;
            var t = new Thread(delegate()
            {
                string msg; bool changed = false;
                try
                {
                    string json = Versions.FetchRemote(repo);
                    var remote = new Versions(); remote.Parse(json);
                    if (remote.List.Count == 0) throw new Exception("the version list on GitHub is empty");
                    string localLatest = versions.LatestId;
                    var newest = remote.Find(remote.LatestId);
                    bool newer = newest != null && Versions.Compare(remote.LatestId, localLatest == "" ? "0.0" : localLatest) > 0;
                    if (newer)
                    {
                        SetStatus("Downloading BlockCraft " + newest.Id + "...");
                        Versions.Download(repo, newest, true);
                        msg = "Updated to BlockCraft " + newest.Id + "!";
                    }
                    else msg = "BlockCraft is up to date (version " + (localLatest == "" ? "?" : localLatest) + ").";
                    if (Versions.Compare(remote.LatestId, localLatest == "" ? "0.0" : localLatest) >= 0)
                    {
                        Versions.WriteSafely(versions.ManifestPath, new UTF8Encoding(false).GetBytes(json));
                        changed = true;
                    }
                    bool launcherNewer = remote.RemoteLauncher != "" && Versions.Compare(remote.RemoteLauncher, Program.LauncherVersion) > 0;
                    string page = remote.LauncherUrl != "" ? remote.LauncherUrl : "https://github.com/" + repo;
                    BeginInvoke((Action)delegate { newLauncher.Visible = launcherNewer; newLauncher.Tag = page; });
                }
                catch (Exception e) { msg = manual ? "Could not check for updates: " + e.Message : "Offline - playing the versions already installed."; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        busy = false; updates.Enabled = true;
                        if (changed) { try { versions.LoadLocal(); } catch { } FillVersions(keep == versions.LatestId ? null : keep); }
                        status.Text = msg;
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void OpenLauncherPage()
        {
            string page = newLauncher.Tag as string;
            if (page != null && page.StartsWith("https://")) try { Process.Start(page); } catch { }
        }

        static string FindEdge()
        {
            string[] candidates = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe"),
            };
            foreach (var c in candidates) if (File.Exists(c)) return c;
            return null;
        }

        void LaunchLatest(string hash)
        {
            VersionInfo latest = versions.Find(versions.LatestId);
            Play(latest, hash);
        }

        void SetPlayEnabled(bool on) { foreach (var b in playButtons) b.Enabled = on; }

        void Play(VersionInfo v, string hash)
        {
            if (server == null || busy) return;
            if (v != null && !v.Installed)
            {
                string repo = Versions.Repo();
                if (repo == null) { MessageBox.Show("This version is not downloaded and updates are not set up.", "BlockCraft"); return; }
                busy = true; SetPlayEnabled(false);
                status.Text = "Downloading BlockCraft " + v.Id + "...";
                var t = new Thread(delegate()
                {
                    string err = null;
                    try { Versions.Download(repo, v, v.Latest); } catch (Exception e) { err = e.Message; }
                    BeginInvoke((Action)delegate
                    {
                        busy = false; SetPlayEnabled(true);
                        if (err != null) { status.Text = "Download failed: " + err; return; }
                        FillVersions(v.Id);
                        Launch(v, hash);
                    });
                });
                t.IsBackground = true;
                t.Start();
                return;
            }
            Launch(v, hash);
        }

        void Launch(VersionInfo v, string hash)
        {
            // the newest version runs from index.html; older ones from versions\<id>\ with their own worlds
            string url = server.Url;
            if (v != null && !v.Latest && File.Exists(Versions.FileFor(v.Id))) url += "v/" + v.Id + "/";
            url += hash ?? "";
            try
            {
                string edge = FindEdge();
                if (appWindow.Checked && edge != null)
                    Process.Start(edge, "--app=\"" + url + "\" --window-size=1280,800 --no-first-run");
                else
                    Process.Start(url);
                played = true;
                status.Text = "BlockCraft " + (v != null ? v.Id + " " : "") + "is running. Keep this launcher open (minimized is fine) so your worlds save.";
                WindowState = FormWindowState.Minimized;
            }
            catch (Exception e)
            {
                MessageBox.Show("Could not open the game: " + e.Message + "\n\nYou can open this address in any browser:\n" + url, "BlockCraft", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void OpenSaves()
        {
            if (server == null) return;
            try { Process.Start("explorer.exe", "\"" + server.Saves + "\""); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (played && e.CloseReason == CloseReason.UserClosing)
            {
                var r = MessageBox.Show("If BlockCraft is still open, quit to the title screen first so your world is saved.\n\nClose the launcher now?",
                    "BlockCraft", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
        }
    }
}
