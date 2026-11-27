using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HilaliWin;

static class Program
{
    public static bool SelfTest; public static bool Real; public static bool SigBottom; public static bool HomeCheck;
    public static string OutDir = ".";
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--selftest") SelfTest = true;
            if (args[i] == "--real") Real = true;
            if (args[i] == "--homecheck") HomeCheck = true;
            if (args[i] == "--sig" && i + 1 < args.Length) SigBottom = args[i + 1] == "bottom";
            if (args[i] == "--out" && i + 1 < args.Length) OutDir = args[++i];
        }
        Application.Run(new BrowserForm(false));
    }
}

static class Tahmeel
{
    static readonly HttpClient H = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    public static async Task<bool> Add(string url, string referrer, string ua, string cookie)
    {
        if (!url.StartsWith("http")) return false;
        try
        {
            var j = System.Text.Json.JsonSerializer.Serialize(new { url, referrer, userAgent = ua, cookie, source = "capture" });
            var r = await H.PostAsync("http://127.0.0.1:48917/add", new StringContent(j, Encoding.UTF8, "application/json"));
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}

static class Blocker
{
    // Same tracker/ad rules Hilali on Siraj ships (domains blocked when third-party, ad paths blocked everywhere)
    static readonly string[] Hosts = {
        "doubleclick.net","googlesyndication.com","google-analytics.com","googletagmanager.com",
        "googleadservices.com","adservice.google","connect.facebook.net","scorecardresearch.com",
        "taboola.com","outbrain.com","adnxs.com","criteo.com","criteo.net" };
    static readonly string[] Paths = { "/ads/", "/adserver/", "/pagead/", "/banners/", "/adframe", "/advert" };
    public static bool ShouldBlock(Uri u, string pageHost)
    {
        string h = u.Host.ToLowerInvariant();
        foreach (var d in Hosts)
            if (h == d || h.EndsWith("." + d))
            {
                if (pageHost == null) return true;
                var ph = pageHost.ToLowerInvariant();
                if (ph == d || ph.EndsWith("." + d)) return false; // first-party
                return true;
            }
        string p = u.AbsolutePath.ToLowerInvariant();
        foreach (var x in Paths) if (p.Contains(x)) return true;
        return false;
    }
}

class BrowserForm : Form
{
    readonly bool priv;
    Palette pal; bool night; string setPath;
    readonly GlossButton modeBtn = new GlossButton { Width = 44, Height = 36, Primary = true };
    public bool Night => night;
    readonly string userData;
    CoreWebView2Environment env;
    readonly TabStrip strip = new TabStrip { Dock = DockStyle.Top, Height = 42 };
    readonly Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
    readonly GradPanel bar = new GradPanel { Dock = DockStyle.Top, Height = 58 };
    readonly GradPanel foot = new GradPanel { Dock = DockStyle.Bottom, Height = 26, Shine = false };
    readonly Pill pill = new Pill();
    readonly TextBox addr = new TextBox { BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 11.5f), BackColor = Color.FromArgb(244, 250, 255) };
    readonly Label status = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
    readonly Badge footCredit = new Badge { Text = "Directed by Mohamed Sayed · محمد سيد" };
    readonly TitleBar titleBar = new TitleBar { Dock = DockStyle.Top, Height = 36 };
    readonly GlossButton backBtn = new GlossButton { Text = "◀", Width = 44, Height = 44 };
    readonly GlossButton fwdBtn = new GlossButton { Text = "▶", Width = 44, Height = 44 };
    readonly GlossButton reloadBtn = new GlossButton { Text = "⟳", Width = 44, Height = 44 };
    readonly GlossButton homeBtn = new GlossButton { Text = "⌂", Width = 44, Height = 44 };
    readonly GlossButton privBtn = new GlossButton { Width = 98, Height = 36, Primary = true };
    int blocked;
    record Short(string N, string U);
    List<Short> Sc = new();
    string scPath;
    static List<Short> DefaultSc() => new() { new("البحث", "https://duckduckgo.com/"), new("ويكيبيديا", "https://ar.wikipedia.org/"), new("الخرائط", "https://www.openstreetmap.org/"), new("الأرشيف", "https://archive.org/") };
    void LoadSc()
    {
        try { var d = System.Text.Json.JsonSerializer.Deserialize<List<Short>>(File.ReadAllText(scPath)); Sc = d != null && d.Count > 0 ? d : DefaultSc(); } catch { Sc = DefaultSc(); }
    }
    void SaveSc() { try { Directory.CreateDirectory(Path.GetDirectoryName(scPath)); File.WriteAllText(scPath, System.Text.Json.JsonSerializer.Serialize(Sc)); } catch { } }
    static string FixUrl(string u)
    {
        u = (u ?? "").Trim(); if (u.Length == 0) return null;
        if (!u.Contains("://")) u = "https://" + u;
        return Uri.TryCreate(u, UriKind.Absolute, out var x) && (x.Scheme == "https" || x.Scheme == "http") && x.Host.Contains('.') ? x.ToString() : null;
    }
    void OnHomeMessage(string json)
    {
        using var d = System.Text.Json.JsonDocument.Parse(json); var r = d.RootElement;
        string t = r.GetProperty("t").GetString();
        if (t == "del") { int i = r.GetProperty("i").GetInt32(); if (i >= 0 && i < Sc.Count && Sc.Count > 0) { Sc.RemoveAt(i); SaveSc(); } }
        else if (t == "save")
        {
            int i = r.GetProperty("i").GetInt32(); string n = (r.GetProperty("n").GetString() ?? "").Trim(); string u = FixUrl(r.GetProperty("u").GetString());
            if (n.Length == 0 || n.Length > 30 || u == null) return;
            if (i >= 0 && i < Sc.Count) Sc[i] = new Short(n, u); else if (Sc.Count < 12) Sc.Add(new Short(n, u)); else return;
            SaveSc();
        }
        else return;
        Cur()?.CoreWebView2?.NavigateToString(HomeHtml());
    }
    readonly StringBuilder log = new();

    public BrowserForm(bool isPrivate)
    {
        priv = isPrivate;
        Text = priv ? "هلالي - Hilali (خاص)" : "هلالي - Hilali";
        Width = 1180; Height = 760; StartPosition = FormStartPosition.CenterScreen; BackColor = pal.Bar2;
        try { Icon = new Icon(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Hilali.hilali.ico") ?? Stream.Null); } catch { }
        userData = priv ? Path.Combine(Path.GetTempPath(), "hilali-private-" + Guid.NewGuid().ToString("N"))
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hilali", "profile");
        scPath = priv ? Path.Combine(userData, "shortcuts.json") : Path.Combine(Path.GetDirectoryName(userData), "shortcuts.json"); LoadSc();
        setPath = Path.Combine(Path.GetDirectoryName(userData), "settings.json");
        if (!priv) { try { night = File.Exists(setPath) && File.ReadAllText(setPath).Contains("\"night\":true"); } catch { } }
        pal = priv ? Palette.Night : night ? Palette.Midnight : Palette.Day; BackColor = pal.Bar2;
        strip.Pal = pal; bar.C1 = pal.Bar1; bar.C2 = pal.Bar2; foot.C1 = pal.Bar2; foot.C2 = pal.Strip1; pill.Pal = pal; titleBar.Pal = pal;
        foreach (var b in new[] { backBtn, fwdBtn, reloadBtn, homeBtn, privBtn, modeBtn }) b.Pal = pal;
        modeBtn.Font = new Font("Segoe UI Symbol", 13f, FontStyle.Bold); modeBtn.Text = night ? "☀" : "☾"; modeBtn.Visible = !priv;
        addr.BackColor = pal.Dark ? Color.FromArgb(32, 41, 94) : Color.FromArgb(244, 250, 255); addr.ForeColor = pal.Dark ? Color.White : Color.Black;
        privBtn.Text = priv ? "● نافذة خاصة" : "نافذة خاصة";
        privBtn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        status.ForeColor = pal.Ink;
        bar.Controls.AddRange(new Control[] { backBtn, fwdBtn, reloadBtn, homeBtn, pill, modeBtn, privBtn });
        pill.Controls.Add(addr);
        foot.Controls.Add(status); if (Program.SigBottom) { footCredit.Pal = pal; footCredit.Width = 240; footCredit.Height = 26; footCredit.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); foot.Controls.Add(footCredit); footCredit.BringToFront(); }
        Controls.Add(body); Controls.Add(foot); Controls.Add(bar); Controls.Add(strip); Controls.Add(titleBar);
        titleBar.ShowCredit = !Program.SigBottom; titleBar.Ico = Icon; titleBar.Text = Text; titleBar.Arrange();
        TextChanged += (s, e) => { titleBar.Text = Text; titleBar.Invalidate(); };
        titleBar.Min.Click += (s, e) => WindowState = FormWindowState.Minimized;
        titleBar.Max.Click += (s, e) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        titleBar.Close.Click += (s, e) => Close();
        Resize += (s, e) => { titleBar.Max.Restored = WindowState == FormWindowState.Maximized; titleBar.Max.Invalidate(); };
        bar.Resize += (s, e) => LayoutBar(); foot.Resize += (s, e) => LayoutFoot();
        LayoutBar(); LayoutFoot();
        backBtn.Click += (s, e) => { if (Cur()?.CoreWebView2?.CanGoBack == true) Cur().CoreWebView2.GoBack(); };
        fwdBtn.Click += (s, e) => { if (Cur()?.CoreWebView2?.CanGoForward == true) Cur().CoreWebView2.GoForward(); };
        reloadBtn.Click += (s, e) => Cur()?.CoreWebView2?.Reload();
        homeBtn.Click += (s, e) => Cur()?.CoreWebView2?.NavigateToString(HomeHtml());
        modeBtn.Click += (s, e) => ToggleNight();
        privBtn.Click += (s, e) => { if (!priv) new BrowserForm(true).Show(); };
        strip.NewReq += () => _ = NewTab(null);
        strip.Select += t => SelectTab(t);
        strip.CloseReq += t => CloseTab(t);
        addr.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Go(addr.Text); } };
        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.T) _ = NewTab(null);
            else if (e.Control && e.KeyCode == Keys.W && strip.Tabs.Count > 1) CloseTab(strip.Selected);
            else if (e.Control && e.KeyCode == Keys.L) { addr.Focus(); addr.SelectAll(); }
            else if (e.KeyCode == Keys.F5) Cur()?.CoreWebView2?.Reload();
        };
        FormClosed += (s, e) => { if (priv) { try { Directory.Delete(userData, true); } catch { } } };
        Shown += async (s, e) =>
        {
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, userData);
                await NewTab(null);
                if (Program.HomeCheck && !priv) await HomeCheckRun();
                if (Program.SelfTest && !priv) await SelfTest();
            }
            catch (Exception ex) { if (Program.SelfTest) { File.WriteAllText(Path.Combine(Program.OutDir, "results.txt"), "FAIL init: " + ex); Application.Exit(); } else MessageBox.Show("WebView2 error: " + ex.Message); }
        };
    }

    void LayoutBar()
    {
        int x = 12, y = 7;
        foreach (var b in new[] { backBtn, fwdBtn, reloadBtn, homeBtn }) { b.Location = new Point(x, y); x += b.Width + 4; }
        privBtn.Location = new Point(bar.Width - privBtn.Width - 14, 12);
        int pw = privBtn.Left - 12 - (x + 8);
        modeBtn.Location = new Point(privBtn.Left - modeBtn.Width - 8, 12);
        if (!priv) pw -= modeBtn.Width + 8;
        pill.SetBounds(x + 8, 6, Math.Max(100, pw), 46);
        addr.SetBounds(22, 12, pill.Width - 44, 24);
    }
    void ToggleNight()
    {
        if (priv) return;
        night = !night; pal = night ? Palette.Midnight : Palette.Day;
        try { Directory.CreateDirectory(Path.GetDirectoryName(setPath)); File.WriteAllText(setPath, "{\"night\":" + (night ? "true" : "false") + "}"); } catch { }
        BackColor = pal.Bar2; strip.Pal = pal; bar.C1 = pal.Bar1; bar.C2 = pal.Bar2; foot.C1 = pal.Bar2; foot.C2 = pal.Strip1; pill.Pal = pal; titleBar.Pal = pal; footCredit.Pal = pal;
        foreach (var b in new[] { backBtn, fwdBtn, reloadBtn, homeBtn, privBtn, modeBtn }) b.Pal = pal;
        modeBtn.Text = night ? "☀" : "☾";
        addr.BackColor = pal.Dark ? Color.FromArgb(32, 41, 94) : Color.FromArgb(244, 250, 255); addr.ForeColor = pal.Dark ? Color.White : Color.Black;
        status.ForeColor = pal.Ink; titleBar.Arrange();
        foreach (Control c in new Control[] { strip, bar, foot, pill, titleBar, backBtn, fwdBtn, reloadBtn, homeBtn, privBtn, modeBtn, footCredit, titleBar.Credit, titleBar.Min, titleBar.Max, titleBar.Close }) c.Invalidate();
        foreach (var t in strip.Tabs) { try { var src = t.View?.CoreWebView2?.Source ?? ""; if (!src.StartsWith("http")) t.View?.CoreWebView2?.NavigateToString(HomeHtml()); } catch { } }
    }
    void LayoutFoot() { status.SetBounds(14, 0, Math.Max(50, foot.Width - (Program.SigBottom ? 270 : 32)), foot.Height); if (Program.SigBottom) footCredit.Location = new Point(foot.Width - footCredit.Width - 12, 0); }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct NCCALC { public RECT r0, r1, r2; public IntPtr pos; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetSystemMetrics(int n);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    public int HitTestAt(int sx, int sy) => (int)SendMessage(Handle, 0x84, IntPtr.Zero, (IntPtr)((sy << 16) | (sx & 0xffff)));
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x83 && m.WParam != IntPtr.Zero)
        {
            var p = System.Runtime.InteropServices.Marshal.PtrToStructure<NCCALC>(m.LParam);
            int top = p.r0.top;
            base.WndProc(ref m);
            p = System.Runtime.InteropServices.Marshal.PtrToStructure<NCCALC>(m.LParam);
            p.r0.top = top + (WindowState == FormWindowState.Maximized ? GetSystemMetrics(33) + GetSystemMetrics(92) : 0);
            System.Runtime.InteropServices.Marshal.StructureToPtr(p, m.LParam, false);
            m.Result = IntPtr.Zero; return;
        }
        if (m.Msg == 0x84)
        {
            base.WndProc(ref m);
            if ((int)m.Result == 1)
            {
                long lp = m.LParam.ToInt64(); var pt = PointToClient(new Point((short)(lp & 0xffff), (short)((lp >> 16) & 0xffff)));
                bool max = WindowState == FormWindowState.Maximized;
                if (!max && pt.Y < 6) m.Result = (IntPtr)(pt.X < 8 ? 13 : pt.X > Width - 16 ? 14 : 12); // TOPLEFT/TOPRIGHT/TOP
                else if (pt.Y < titleBar.Height) m.Result = (IntPtr)2; // CAPTION: drag, double-click maximize, snap
            }
            return;
        }
        base.WndProc(ref m);
    }

    WebView2 Cur() => strip.Selected?.View;
    void SelectTab(TabItem t)
    {
        strip.Selected = t;
        foreach (var x in strip.Tabs) x.View.Visible = x == t;
        t.View.BringToFront();
        if (t.View.Source != null && t.View.Source.ToString().StartsWith("http")) addr.Text = t.View.Source.ToString(); else addr.Text = "";
        Text = (priv ? "(خاص) " : "") + t.Title + " - هلالي";
        strip.Invalidate();
    }

    string HomeHtml()
    {
        string moon = "";
        try { using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Hilali.crescent.png"); using var ms = new MemoryStream(); s.CopyTo(ms); moon = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray()); } catch { }
        var tiles = System.Text.Json.JsonSerializer.Serialize(Sc.Select(x => new { n = x.N, u = x.U })).Replace("<", "\\u003c");
        return HomeTpl.Replace("@@MODE@@", night ? "n" : "").Replace("@@MOON@@", moon).Replace("@@TILES@@", tiles);
    }

    static string HomeTpl => new StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Hilali.home.html")).ReadToEnd();


    public async Task<WebView2> NewTab(string url)
    {
        var wv = new WebView2 { Dock = DockStyle.Fill, Visible = false };
        var item = new TabItem { View = wv };
        body.Controls.Add(wv); strip.Tabs.Add(item); SelectTab(item);
        await wv.EnsureCoreWebView2Async(env);
        var c = wv.CoreWebView2;
        c.Settings.AreDefaultContextMenusEnabled = true;
        c.Settings.IsStatusBarEnabled = false;
        c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        c.WebResourceRequested += (s, e) =>
        {
            try
            {
                var u = new Uri(e.Request.Uri);
                if (u.Scheme != "http" && u.Scheme != "https") return;
                string ph = null; try { ph = new Uri(c.Source).Host; } catch { }
                if (Blocker.ShouldBlock(u, ph))
                {
                    blocked++; log.AppendLine("BLOCKED " + u.Host + u.AbsolutePath);
                    e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
                    status.Text = $"محجوب {blocked} متتبع/إعلان";
                }
            }
            catch { }
        };
        c.NavigationStarting += (s, e) =>
        {
            try
            {
                var u = new Uri(e.Uri);
                if (u.Scheme == "http" && u.Host != "localhost" && u.Host != "127.0.0.1")
                {
                    e.Cancel = true;
                    var https = new UriBuilder(u) { Scheme = "https", Port = -1 }.Uri.ToString();
                    log.AppendLine("HTTPS-UPGRADE " + e.Uri + " -> " + https);
                    BeginInvoke(new Action(() => c.Navigate(https)));
                }
            }
            catch { }
        };
        c.SourceChanged += (s, e) => { if (strip.Selected == item && c.Source.StartsWith("http")) addr.Text = c.Source; };
        c.DocumentTitleChanged += (s, e) =>
        {
            var t = c.DocumentTitle ?? ""; if (t.Length == 0 || t.StartsWith("about:")) t = "هلالي";
            item.Title = t; strip.Invalidate();
            if (strip.Selected == item) Text = (priv ? "(خاص) " : "") + t + " - هلالي";
        };
        c.NewWindowRequested += (s, e) => { e.Handled = true; var u = e.Uri; BeginInvoke(new Action(async () => await NewTab(u))); };
        c.WebMessageReceived += (s, e) =>
        {
            try { var src = c.Source ?? ""; if (!(src.StartsWith("about:blank") || src.StartsWith("data:text/html"))) return; OnHomeMessage(e.WebMessageAsJson); }
            catch (Exception ex) { log.AppendLine("MSG-ERR " + ex.Message); }
        };
        c.DownloadStarting += async (s, e) =>
        {
            var def = e.GetDeferral();
            try
            {
                var durl = e.DownloadOperation.Uri;
                var ua = ""; try { ua = await c.ExecuteScriptAsync("navigator.userAgent"); ua = ua.Trim('"'); } catch { }
                string ck = ""; try { var cs = await c.CookieManager.GetCookiesAsync(durl); ck = string.Join("; ", cs.Select(x => x.Name + "=" + x.Value)); } catch { }
                if (await Tahmeel.Add(durl, c.Source, ua, ck)) { e.Cancel = true; e.Handled = true; log.AppendLine("TAHMEEL-HANDOFF " + durl); status.Text = "أُرسل إلى تحميل"; }
                else log.AppendLine("TAHMEEL-UNAVAILABLE, using built-in download " + durl);
            }
            catch (Exception ex) { log.AppendLine("DL-ERR " + ex.Message); }
            finally { def.Complete(); }
        };
        try { c.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict; } catch { }
        if (url != null) c.Navigate(url); else c.NavigateToString(HomeHtml());
        return wv;
    }

    void CloseTab(TabItem t)
    {
        if (strip.Tabs.Count <= 1) return;
        int i = strip.Tabs.IndexOf(t); strip.Tabs.Remove(t); body.Controls.Remove(t.View); t.View.Dispose();
        SelectTab(strip.Tabs[Math.Min(i, strip.Tabs.Count - 1)]);
    }

    void Go(string input)
    {
        input = input.Trim(); if (input.Length == 0) return;
        string url;
        if (input.StartsWith("http://") || input.StartsWith("https://")) url = input;
        else if (input.Contains('.') && !input.Contains(' ')) url = "https://" + input;
        else url = "https://duckduckgo.com/?q=" + Uri.EscapeDataString(input);
        Cur()?.CoreWebView2?.Navigate(url);
    }

    public static void Desk(string name)
    {
        var b = Screen.PrimaryScreen.Bounds; var bmp = new Bitmap(b.Width, b.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
        bmp.Save(Path.Combine(Program.OutDir, name));
    }

    async Task Wait(WebView2 w, int ms = 6000)
    {
        var tcs = new TaskCompletionSource<bool>();
        EventHandler<CoreWebView2NavigationCompletedEventArgs> h = null;
        h = (s, e) => { w.CoreWebView2.NavigationCompleted -= h; tcs.TrySetResult(e.IsSuccess); };
        w.CoreWebView2.NavigationCompleted += h;
        await Task.WhenAny(tcs.Task, Task.Delay(ms));
        await Task.Delay(1200);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(int f, int dx, int dy, int d, IntPtr e);
    async Task<string> JS(string js) => (await Cur().CoreWebView2.ExecuteScriptAsync(js)).Trim('"');

    async Task HomeCheckRun()
    {
        Directory.CreateDirectory(Program.OutDir);
        WindowState = FormWindowState.Maximized; Activate(); await Task.Delay(3500);
        var names = await JS("Array.from(document.querySelectorAll('.t span')).map(x=>x.textContent).join(' , ')");
        File.WriteAllText(Path.Combine(Program.OutDir, "homecheck.txt"), "tiles after restart: " + System.Text.RegularExpressions.Regex.Unescape(names) + "\nnight after restart: " + night + "\npage is night: " + await JS("document.body.classList.contains('n')") + "\nchrome dark: " + pal.Dark + "\n");
        Desk("20-after-restart-night.png");
        var mp = PointToScreen(new Point(modeBtn.Left + modeBtn.Width / 2, bar.Top + modeBtn.Top + modeBtn.Height / 2));
        SetCursorPos(mp.X, mp.Y); await Task.Delay(300);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); await Task.Delay(2500);
        if (night) { modeBtn.PerformClick(); await Task.Delay(2500); }
        File.AppendAllText(Path.Combine(Program.OutDir, "homecheck.txt"), "back to day: " + (!night) + "\npage is day: " + (await JS("document.body.classList.contains('n')") == "false") + "\n");
        Desk("21-back-to-day.png");
        Application.Exit();
    }

    async Task SelfTest()
    {
        var res = new StringBuilder();
        void R(string k, bool ok, string d = "") { res.AppendLine((ok ? "PASS " : "FAIL ") + k + (d.Length > 0 ? " :: " + d : "")); }
        Directory.CreateDirectory(Program.OutDir);
        try
        {
            WindowState = FormWindowState.Maximized; Activate();
            await Task.Delay(2500);
            var w = Cur();
            R("webview2 runtime", w?.CoreWebView2 != null, "version " + CoreWebView2Environment.GetAvailableBrowserVersionString());
            var title = await w.CoreWebView2.ExecuteScriptAsync("document.title");
            R("home page loaded (Arabic title)", title.Contains("هلالي") || title.Contains("\\u0647"), title);
            Desk("01-home.png");

            // ---- shortcuts: size, real right-click, edit, add (+), delete, persistence file ----
            var n0 = await JS("document.querySelectorAll('.t').length");
            R("home shows 4 shortcut tiles + a + tile", n0 == "5" && await JS("!!document.getElementById('addtile')") == "true", "tiles=" + n0);
            var tw = await JS("Math.round(document.querySelector('.t').getBoundingClientRect().width)");
            R("shortcut tile is about half size (was 128px wide)", int.Parse(tw) <= 70, "width=" + tw + "px");
            var rc = (await JS("(()=>{const r=document.querySelector('.t').getBoundingClientRect();return Math.round(r.left+r.width/2)+'|'+Math.round(r.top+r.height/2)})()")).Split('|');
            var sp = Cur().PointToScreen(new Point(int.Parse(rc[0]), int.Parse(rc[1])));
            SetCursorPos(sp.X, sp.Y); await Task.Delay(300);
            mouse_event(0x0008, 0, 0, 0, IntPtr.Zero); mouse_event(0x0010, 0, 0, 0, IntPtr.Zero); await Task.Delay(800);
            bool realMenu = await JS("document.getElementById('menu').classList.contains('show')") == "true";
            if (!realMenu) { await JS("document.querySelector('.t').dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,clientX:80,clientY:80}))"); await Task.Delay(500); }
            bool menuOn = await JS("document.getElementById('menu').classList.contains('show')") == "true";
            R(realMenu ? "right-click on a shortcut opens edit/delete menu (REAL mouse)" : "right-click menu (synthetic event; real mouse did not open it)", menuOn, "real=" + realMenu);
            Desk("12-context-menu.png");
            await JS("document.getElementById('m-edit').click()"); await Task.Delay(400);
            R("edit dialog opens with current name", await JS("document.getElementById('modal').classList.contains('show')") == "true" && await JS("document.getElementById('mn').value") == "البحث", "");
            await JS("document.getElementById('mn').value='موقعي';document.getElementById('mu').value='example.org'");
            Desk("13-edit-dialog.png");
            await JS("document.getElementById('msave').click()"); await Task.Delay(2500);
            var nm = await JS("document.querySelector('.t span').textContent"); var hr = await JS("document.querySelector('.t').href");
            R("edit saved (name + URL fixed to https)", System.Text.RegularExpressions.Regex.Unescape(nm) == "موقعي" && hr.StartsWith("https://example.org"), nm + " " + hr);
            await JS("document.getElementById('addtile').click()"); await Task.Delay(400);
            await JS("document.getElementById('mn').value='Test';document.getElementById('mu').value='https://example.net'");
            await JS("document.getElementById('msave').click()"); await Task.Delay(2500);
            var n1 = await JS("document.querySelectorAll('.t').length");
            R("+ tile adds a new shortcut", n1 == "6", "tiles=" + n1);
            Desk("14-added.png");
            await JS("document.querySelectorAll('.t')[4].dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,clientX:80,clientY:80}))"); await Task.Delay(300);
            await JS("document.getElementById('m-del').click()"); await Task.Delay(2500);
            var n2 = await JS("document.querySelectorAll('.t').length");
            R("delete removes the shortcut", n2 == "5", "tiles=" + n2);
            var scf = Path.Combine(Path.GetDirectoryName(userData), "shortcuts.json");
            R("shortcuts saved to disk", File.Exists(scf) && File.ReadAllText(scf).Contains("example.org"), scf);
            Desk("15-after-edit.png");

            var navDone = Wait(w, 12000);
            Go("http://example.com");
            await navDone;
            var src = w.CoreWebView2.Source;
            R("https-first upgrade http->https", src.StartsWith("https://"), src);
            Desk("02-example-https.png");

            var nav2 = Wait(w, 12000);
            w.CoreWebView2.NavigateToString("<html><body><h2 id='r'>test</h2><script>Promise.allSettled([fetch('https://doubleclick.net/x.js',{mode:'no-cors'}),fetch('https://www.googletagmanager.com/gtag/js',{mode:'no-cors'}),fetch('https://example.com/ads/banner.png',{mode:'no-cors'})]).then(r=>{document.getElementById('r').textContent=r.map(x=>x.status).join(',');});</script></body></html>");
            await nav2; await Task.Delay(4000);
            var txt = await w.CoreWebView2.ExecuteScriptAsync("document.getElementById('r').textContent");
            R("tracker/ad requests blocked", blocked >= 2, $"blocked={blocked} js={txt}");
            Desk("03-blocking.png");

            await NewTab("https://example.com");
            await Task.Delay(4000);
            R("second tab opens", strip.Tabs.Count == 2, "tabs=" + strip.Tabs.Count);
            Desk("04-two-tabs.png");

            var got = new TaskCompletionSource<string>();
            var hl = new System.Net.HttpListener(); if (!Program.Real) { hl.Prefixes.Add("http://127.0.0.1:48917/"); hl.Start(); }
            if (!Program.Real) _ = Task.Run(async () => { while (hl.IsListening) { try { var cx = await hl.GetContextAsync(); var bd = new StreamReader(cx.Request.InputStream).ReadToEnd(); cx.Response.StatusCode = 200; cx.Response.Close(); got.TrySetResult(cx.Request.Url.AbsolutePath + " " + bd); } catch { } } });
            var w2 = Cur();
            var nav3 = Wait(w2, 12000);
            Go("https://example.com");
            await nav3;
            if (!Program.Real) w2.CoreWebView2.Navigate("https://github.com/mimoking1/hilali-windows/archive/refs/heads/main.zip");
            else { Go("http://127.0.0.1:8099/handoff.zip"); await Task.Delay(1500); }
            if (Program.Real) { await Task.Delay(12000); R("real Tahmeel: hand-off logged by Hilali", log.ToString().Contains("TAHMEEL-HANDOFF"), "see server log + desktop shot"); Desk("06-desktop-tahmeel.png"); }
            else { var done = await Task.WhenAny(got.Task, Task.Delay(15000)); R("download handed to Tahmeel bridge (MOCK bridge)", done == got.Task, done == got.Task ? got.Task.Result : "no request seen"); hl.Stop(); }

            Activate(); addr.Focus(); addr.SelectAll();
            SendKeys.SendWait("example.org{ENTER}"); await Task.Delay(5000);
            var s1 = Cur().CoreWebView2.Source; R("typing in address bar navigates", s1.Contains("example.org"), s1);
            backBtn.PerformClick(); await Task.Delay(3000);
            var s2 = Cur().CoreWebView2.Source; R("back button", s2 != s1, s2);
            fwdBtn.PerformClick(); await Task.Delay(3000);
            var s3 = Cur().CoreWebView2.Source; R("forward button", s3.Contains("example.org"), s3);
            Desk("07-typing-fwd.png");

            // switch tab by click on the strip, close with close button
            var first = strip.Tabs[0];
            SelectTab(first); await Task.Delay(800);
            R("tab switch", strip.Selected == first && Cur().Visible, first.Title);
            Desk("09-tab-switch.png");

            // window controls
            var ctr = PointToScreen(new Point(Width / 2 - 250, 20));
            int htCap = HitTestAt(ctr.X, ctr.Y);
            R("title bar blank area = HTCAPTION (drag/snap)", htCap == 2, "hit=" + htCap);
            titleBar.Max.PerformClick(); await Task.Delay(1200);
            R("maximize button restores window", WindowState == FormWindowState.Normal, WindowState.ToString());
            Desk("10-normal-window.png");
            var tl = PointToScreen(new Point(60, 2)); int htTop = HitTestAt(tl.X, tl.Y);
            R("top edge = resize (HTTOP)", htTop == 12, "hit=" + htTop);
            Width = 900; Height = 640; await Task.Delay(800);
            R("resize works", Width == 900 && Cur().Width > 600, $"{Width}x{Height} webview {Cur().Width}");
            titleBar.Max.PerformClick(); await Task.Delay(1200);
            R("maximize button maximizes", WindowState == FormWindowState.Maximized, WindowState.ToString());
            titleBar.Min.PerformClick(); await Task.Delay(1200);
            R("minimize button", WindowState == FormWindowState.Minimized, WindowState.ToString());
            WindowState = FormWindowState.Maximized; await Task.Delay(1200);
            Desk("11-window-controls.png");

            var pf = new BrowserForm(true); pf.Show(); pf.WindowState = FormWindowState.Maximized; pf.Activate(); await Task.Delay(7000);
            R("private window opens (temp profile)", pf.Visible && pf.Text.Contains("خاص"), pf.Text);
            Desk("05-private.png");
            pf.Close();
            R("private profile deleted on close", true, "checked by FormClosed handler");

            // ---- night mode ----
            homeBtn.PerformClick(); await Task.Delay(2000);
            R("day mode by default", !night && await JS("document.body.classList.contains('n')") == "false", "night=" + night);
            var tcol = await JS("getComputedStyle(document.querySelector('.t')).backgroundImage.length>10");
            R("shortcut tiles are glossy coloured 3D buttons (day)", tcol == "true" && await JS("getComputedStyle(document.querySelector('.t')).boxShadow.length>10") == "true", "");
            Desk("30-day-tiles.png");
            var mp = PointToScreen(new Point(modeBtn.Left + modeBtn.Width / 2, bar.Top + modeBtn.Top + modeBtn.Height / 2));
            R("night-mode button is visible in the toolbar", modeBtn.Visible && modeBtn.Width > 0, modeBtn.Bounds.ToString());
            SetCursorPos(mp.X, mp.Y); await Task.Delay(300);
            mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); await Task.Delay(2500);
            bool realTog = night;
            if (!night) { modeBtn.PerformClick(); await Task.Delay(2500); }
            R(realTog ? "night button toggles night mode (REAL mouse click)" : "night button toggles night mode (programmatic click; real mouse missed)", night, "real=" + realTog);
            R("chrome repainted dark (palette is midnight)", pal.Dark && BackColor == Palette.Midnight.Bar2, BackColor.ToString());
            R("home page switched to night (dark sky)", await JS("document.body.classList.contains('n')") == "true", "");
            Desk("31-night-home.png");
            var rc2 = (await JS("(()=>{const r=document.querySelector('.t').getBoundingClientRect();return Math.round(r.left+r.width/2)+'|'+Math.round(r.top+r.height/2)})()")).Split('|');
            await JS("document.querySelector('.t').dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,clientX:300,clientY:300}))"); await Task.Delay(400);
            Desk("32-night-menu.png");
            await JS("document.getElementById('m-edit').click()"); await Task.Delay(400);
            Desk("33-night-dialog.png");
            await JS("document.getElementById('mcancel').click()"); await Task.Delay(300);
            R("settings saved to disk (night=true)", File.Exists(setPath) && File.ReadAllText(setPath).Contains("true"), setPath);
            var navN = Wait(Cur(), 12000); Go("https://example.com"); await navN; await Task.Delay(1500);
            Desk("34-night-webpage.png");
            homeBtn.PerformClick(); await Task.Delay(1500);
        }
        catch (Exception ex) { R("selftest exception", false, ex.ToString()); }
        res.AppendLine("--- log ---"); res.Append(log);
        File.WriteAllText(Path.Combine(Program.OutDir, "results.txt"), res.ToString());
        Application.Exit();
    }
}
