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
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace BlockCraft
{
    static class Program
    {
        public const string LauncherVersion = "2.1";

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
    //  Launcher window
    // ------------------------------------------------------------------
    class LauncherForm : Form
    {
        readonly GameServer server;
        readonly Label status, notes;
        readonly ComboBox versionBox;
        readonly CheckBox appWindow;
        readonly Button play, updates;
        readonly LinkLabel newLauncher;
        readonly Versions versions = new Versions();
        bool played, busy;

        static readonly Color Bg = Color.FromArgb(30, 33, 28);
        static readonly Color PanelCol = Color.FromArgb(44, 48, 41);
        static readonly Color TextCol = Color.FromArgb(232, 232, 224);
        static readonly Color Muted = Color.FromArgb(160, 164, 150);

        public LauncherForm(GameServer server)
        {
            this.server = server;
            Text = "BlockCraft Launcher";
            ClientSize = new Size(560, 470);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = TextCol;
            Font = new Font("Segoe UI", 9.5f);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var header = new HeaderPanel();
            header.Bounds = new Rectangle(0, 0, 560, 170);
            Controls.Add(header);

            versionBox = new ComboBox();
            versionBox.DropDownStyle = ComboBoxStyle.DropDownList;
            versionBox.FlatStyle = FlatStyle.Flat;
            versionBox.BackColor = PanelCol;
            versionBox.ForeColor = TextCol;
            versionBox.Font = new Font("Segoe UI", 10f);
            versionBox.Bounds = new Rectangle(110, 184, 340, 28);
            versionBox.SelectedIndexChanged += delegate { ShowNotes(); };
            Controls.Add(versionBox);

            play = MakeButton("PLAY", new Rectangle(140, 220, 280, 60), Color.FromArgb(78, 128, 44), Color.FromArgb(96, 156, 54), 20f);
            play.Click += delegate { Play(); };
            Controls.Add(play);

            var saves = MakeButton("Saves", new Rectangle(140, 288, 88, 34), Color.FromArgb(78, 80, 74), Color.FromArgb(98, 100, 92), 9.5f);
            saves.Click += delegate { OpenSaves(); };
            Controls.Add(saves);

            updates = MakeButton("Updates", new Rectangle(236, 288, 88, 34), Color.FromArgb(78, 80, 74), Color.FromArgb(98, 100, 92), 9.5f);
            updates.Click += delegate { CheckForUpdates(true); };
            Controls.Add(updates);

            var quit = MakeButton("Quit", new Rectangle(332, 288, 88, 34), Color.FromArgb(78, 80, 74), Color.FromArgb(98, 100, 92), 9.5f);
            quit.Click += delegate { Close(); };
            Controls.Add(quit);

            notes = new Label();
            notes.ForeColor = Muted;
            notes.TextAlign = ContentAlignment.TopCenter;
            notes.Bounds = new Rectangle(30, 330, 500, 52);
            Controls.Add(notes);

            appWindow = new CheckBox();
            appWindow.Text = "Open in its own game window (Microsoft Edge)";
            appWindow.Checked = FindEdge() != null;
            appWindow.Enabled = FindEdge() != null;
            appWindow.ForeColor = Muted;
            appWindow.AutoSize = true;
            appWindow.Location = new Point(142, 388);
            Controls.Add(appWindow);

            status = new Label();
            status.ForeColor = Muted;
            status.BackColor = PanelCol;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(12, 0, 12, 0);
            status.Bounds = new Rectangle(0, 424, 560, 46);
            Controls.Add(status);

            newLauncher = new LinkLabel();
            newLauncher.Text = "New launcher available";
            newLauncher.LinkColor = Color.FromArgb(255, 255, 85);
            newLauncher.ActiveLinkColor = Color.White;
            newLauncher.BackColor = PanelCol;
            newLauncher.AutoSize = true;
            newLauncher.Visible = false;
            newLauncher.Location = new Point(400, 438);
            newLauncher.LinkClicked += delegate { OpenLauncherPage(); };
            Controls.Add(newLauncher);
            newLauncher.BringToFront();

            try { versions.LoadLocal(); } catch (Exception e) { status.Text = "Could not read versions.json: " + e.Message; }
            FillVersions(null);
            UpdateStatus();

            if (server != null)
            {
                server.Saved += delegate { try { BeginInvoke((Action)UpdateStatus); } catch { } };
                Shown += delegate { CheckForUpdates(false); };
            }
            AcceptButton = play;
        }

        Button MakeButton(string text, Rectangle bounds, Color back, Color hover, float size)
        {
            var b = new Button();
            b.Text = text;
            b.Bounds = bounds;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Color.FromArgb(20, 20, 18);
            b.FlatAppearance.BorderSize = 2;
            b.FlatAppearance.MouseOverBackColor = hover;
            b.FlatAppearance.MouseDownBackColor = back;
            b.Font = new Font("Consolas", size, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            return b;
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
            ShowNotes();
        }

        VersionInfo Selected { get { return versionBox.SelectedItem as VersionInfo; } }

        void ShowNotes()
        {
            var v = Selected;
            if (v == null) { notes.Text = ""; return; }
            string t = v.Notes ?? "";
            if (!v.Latest) t += (t.Length > 0 ? "\n" : "") + "Old versions keep their own worlds, so your newest worlds stay safe.";
            notes.Text = t;
        }

        void UpdateStatus()
        {
            if (busy) return;
            if (server == null) { status.Text = "Ready"; return; }
            int n = server.WorldCount();
            status.Text = "Ready  ·  " + n + (n == 1 ? " world" : " worlds") + " saved in: " + server.Saves;
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
                        // remember the full remote list so older versions can be downloaded too
                        Versions.WriteSafely(versions.ManifestPath, new UTF8Encoding(false).GetBytes(json));
                        changed = true;
                    }
                    bool launcherNewer = remote.RemoteLauncher != "" && Versions.Compare(remote.RemoteLauncher, Program.LauncherVersion) > 0;
                    string page = remote.LauncherUrl != "" ? remote.LauncherUrl : "https://github.com/" + repo;
                    BeginInvoke((Action)delegate
                    {
                        newLauncher.Visible = launcherNewer;
                        newLauncher.Tag = page;
                    });
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

        void Play()
        {
            if (server == null || busy) return;
            var v = Selected;
            if (v != null && !v.Installed)
            {
                string repo = Versions.Repo();
                if (repo == null) { MessageBox.Show("This version is not downloaded and updates are not set up.", "BlockCraft"); return; }
                busy = true; play.Enabled = false;
                status.Text = "Downloading BlockCraft " + v.Id + "...";
                var t = new Thread(delegate()
                {
                    string err = null;
                    try { Versions.Download(repo, v, v.Latest); } catch (Exception e) { err = e.Message; }
                    BeginInvoke((Action)delegate
                    {
                        busy = false; play.Enabled = true;
                        if (err != null) { status.Text = "Download failed: " + err; return; }
                        FillVersions(v.Id);
                        Launch(v);
                    });
                });
                t.IsBackground = true;
                t.Start();
                return;
            }
            Launch(v);
        }

        void Launch(VersionInfo v)
        {
            // the newest version runs from index.html; older ones from versions\<id>\ with their own worlds
            string url = server.Url;
            if (v != null && !v.Latest && File.Exists(Versions.FileFor(v.Id))) url += "v/" + v.Id + "/";
            try
            {
                string edge = FindEdge();
                if (appWindow.Checked && edge != null)
                    Process.Start(edge, "--app=\"" + url + "\" --window-size=1280,800 --no-first-run");
                else
                    Process.Start(url);
                played = true;
                status.Text = "BlockCraft " + (v != null ? v.Id + " " : "") + "is running. Keep this launcher open (minimized is fine) so your worlds can save.";
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

    // Painted title art: sky, clouds, a strip of grass blocks and the BLOCKCRAFT logo
    class HeaderPanel : Control
    {
        public HeaderPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }

        static Color Vary(Color c, int amount, Random r)
        {
            int n = r.Next(-amount, amount + 1);
            return Color.FromArgb(Clamp(c.R + n), Clamp(c.G + n), Clamp(c.B + n));
        }
        static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = Width, h = Height;
            using (var sky = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(120, 178, 255), Color.FromArgb(178, 214, 255), 90f))
                g.FillRectangle(sky, 0, 0, w, h);

            // blocky clouds
            using (var cloud = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                int[,] clouds = { { 30, 22, 96, 14 }, { 54, 12, 48, 12 }, { 380, 30, 120, 14 }, { 410, 18, 60, 14 }, { 230, 8, 70, 10 } };
                for (int i = 0; i < clouds.GetLength(0); i++) g.FillRectangle(cloud, clouds[i, 0], clouds[i, 1], clouds[i, 2], clouds[i, 3]);
            }

            // ground: a row of grass blocks (5px pixels, 16x16 textures)
            const int px = 5, block = 16 * px;
            int groundY = h - 45;
            var rnd = new Random(1337);
            Color grass = Color.FromArgb(92, 156, 52), dirt = Color.FromArgb(134, 96, 67);
            for (int bx = 0; bx * block < w; bx++)
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                    {
                        int edge = 3 + ((x * 7 + bx * 3) % 3 == 0 ? 1 : 0);
                        Color c = y < edge ? Vary(grass, 18, rnd) : Vary(dirt, 14, rnd);
                        using (var b = new SolidBrush(c)) g.FillRectangle(b, bx * block + x * px, groundY + y * px, px, px);
                    }

            // logo
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var font = new Font("Consolas", 40f, FontStyle.Bold))
            using (var shadow = new SolidBrush(Color.FromArgb(60, 60, 60)))
            using (var fore = new SolidBrush(Color.FromArgb(240, 240, 240)))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
            {
                var rect = new RectangleF(0, 34, w, 70);
                g.DrawString("BLOCKCRAFT", font, shadow, new RectangleF(rect.X + 4, rect.Y + 4, rect.Width, rect.Height), fmt);
                g.DrawString("BLOCKCRAFT", font, fore, rect, fmt);
            }
            using (var font = new Font("Consolas", 11f, FontStyle.Bold))
            using (var yellow = new SolidBrush(Color.FromArgb(255, 255, 85)))
            using (var dark = new SolidBrush(Color.FromArgb(63, 63, 0)))
            {
                var st = g.Save();
                g.TranslateTransform(w / 2 + 140, 104);
                g.RotateTransform(-8);
                g.DrawString("Every version!", font, dark, 2, 2);
                g.DrawString("Every version!", font, yellow, 0, 0);
                g.Restore(st);
            }
            using (var font = new Font("Consolas", 8.5f))
            using (var b = new SolidBrush(Color.FromArgb(40, 40, 40)))
                g.DrawString("Launcher " + Program.LauncherVersion, font, b, 6, 4);
        }
    }
}
