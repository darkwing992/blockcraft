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
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]

namespace BlockCraft
{
    static class Program
    {
        public const string LauncherVersion = "2.2";

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
    class GameServer
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        HttpListener listener;
        Thread thread;
        public int Port;
        public string Root;
        public string Saves;
        public event Action Saved;
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
        public string Title = "BlockCraft", Sub = "Choose a version and play";
        public HeroPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pb = new SolidBrush(UI.Bg)) g.FillRectangle(pb, ClientRectangle);
            using (var clip = UI.Round(new RectangleF(0, 0, Width - 1, Height - 1), 18))
            {
                var st = g.Save(); g.SetClip(clip);
                g.SmoothingMode = SmoothingMode.None; Art.Sunset(g, ClientRectangle);
                Art.Player(g, Width / 2 - 48, Height - 70 - 192 + 6, 6);
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
        public string Caption = "MINIGAMES";
        public BannerPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var pb = new SolidBrush(UI.Card)) g.FillRectangle(pb, ClientRectangle);
            using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(60, 150, 240), Color.FromArgb(30, 100, 210), 90f)) g.FillRectangle(b, ClientRectangle);
            UI.Pixels(g, ClientRectangle, Color.White, 5);
            using (var f = UI.F(18, true)) using (var wb = new SolidBrush(Color.White))
            { SizeF s = g.MeasureString(Caption, f); g.DrawString(Caption, f, wb, (Width - s.Width) / 2, (Height - s.Height) / 2); }
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
        readonly Panel versionList, newsList;
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
            sideBtn("", "Home", 18, Color.FromArgb(40, 40, 48), delegate { });
            sideBtn("", "Minigames", 84, Color.FromArgb(26, 26, 32), delegate { LaunchLatest("#minigames"); });
            sideBtn("", "Skins", 146, Color.FromArgb(26, 26, 32), delegate { LaunchLatest("#skins"); });
            sideBtn("", "Open the saves folder", 208, Color.FromArgb(26, 26, 32), delegate { OpenSaves(); });
            sideBtn("", "Check for updates", 270, Color.FromArgb(26, 26, 32), delegate { CheckForUpdates(true); });
            sideBtn("", "Quit", 580, Color.FromArgb(26, 26, 32), delegate { Close(); });
            var sidePlay = sideBtn("", "Play the newest version", 644, UI.Green, delegate { LaunchLatest(""); });
            sidePlay.PixelTex = true; playButtons.Add(sidePlay);

            // ---- top: the BlockCraft tab ----
            var tabs = new CardPanel(); tabs.Bounds = new Rectangle(100, 18, 420, 64); tabs.Fill = Color.FromArgb(20, 20, 24); tabs.Radius = 16; Controls.Add(tabs);
            var logoTab = new FancyButton("BLOCKCRAFT"); logoTab.Base = UI.Orange; logoTab.PixelTex = true; logoTab.TextSize = 17; logoTab.Radius = 12; logoTab.Bounds = new Rectangle(6, 6, 220, 52);
            tabs.Controls.Add(logoTab);
            var mgTab = new FancyButton("MINIGAMES"); mgTab.Base = Color.FromArgb(20, 20, 24); mgTab.ForeColor = Color.FromArgb(150, 150, 160); mgTab.TextSize = 15; mgTab.Bounds = new Rectangle(232, 6, 180, 52);
            mgTab.Click += delegate { LaunchLatest("#minigames"); }; tabs.Controls.Add(mgTab);

            updates = new FancyButton("Updates"); updates.Glyph = ""; updates.Base = Color.FromArgb(32, 32, 38); updates.TextSize = 13; updates.Bounds = new Rectangle(850, 22, 150, 56);
            updates.Click += delegate { CheckForUpdates(true); }; Controls.Add(updates);
            var savesTop = new FancyButton("Saves"); savesTop.Glyph = ""; savesTop.Base = Color.FromArgb(232, 180, 40); savesTop.ForeColor = Color.FromArgb(40, 30, 0); savesTop.TextSize = 13;
            savesTop.Bounds = new Rectangle(1010, 22, 150, 56); savesTop.Click += delegate { OpenSaves(); }; Controls.Add(savesTop);

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
