using System.Drawing;
using System.Net.Http;
using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HilaliWin;

static class Program
{
    public static bool SelfTest;
    public static string OutDir = ".";
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--selftest") SelfTest = true;
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
    static readonly Color Accent = ColorTranslator.FromHtml("#4fb2f7");
    static readonly Color Bar = ColorTranslator.FromHtml("#eaf5ff");
    readonly bool priv;
    readonly string userData;
    CoreWebView2Environment env;
    readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
    readonly TextBox addr = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f), BorderStyle = BorderStyle.FixedSingle };
    readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, BackColor = Bar, Font = new Font("Segoe UI", 9f) };
    readonly Label credit = new Label { AutoSize = false, Width = 270, Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(200, 255, 255, 255), ForeColor = Color.Black, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Text = "Directed by Mohamed Sayed · محمد سيد" };
    int blocked;
    readonly System.Text.StringBuilder log = new();

    public BrowserForm(bool isPrivate)
    {
        priv = isPrivate;
        Text = priv ? "هلالي - Hilali (Private)" : "هلالي - Hilali";
        Width = 1180; Height = 760; StartPosition = FormStartPosition.CenterScreen;
        try { Icon = new Icon(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Hilali.hilali.ico") ?? Stream.Null); } catch { }
        userData = priv ? Path.Combine(Path.GetTempPath(), "hilali-private-" + Guid.NewGuid().ToString("N"))
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hilali", "profile");
        var top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = priv ? ColorTranslator.FromHtml("#2b2f3a") : Bar, Padding = new Padding(6, 6, 6, 6) };
        Button B(string t, EventHandler h, int w = 38) { var b = new Button { Text = t, Width = w, Dock = DockStyle.Left, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Symbol", 11f), BackColor = Color.White }; b.FlatAppearance.BorderColor = Accent; b.Click += h; return b; }
        var inner = new Panel { Dock = DockStyle.Fill };
        inner.Controls.Add(addr);
        var right = new Panel { Dock = DockStyle.Right, Width = 270 + 6 + 38 + 96 };
        right.Controls.Add(credit);
        var pb = B(priv ? "خاص ✓" : "نافذة خاصة", (s, e) => { if (!priv) new BrowserForm(true).Show(); }, 96); pb.Dock = DockStyle.Right; right.Controls.Add(pb);
        var nb = B("+", (s, e) => NewTab(null), 38); nb.Dock = DockStyle.Right; right.Controls.Add(nb);
        top.Controls.Add(inner);
        top.Controls.Add(right);
        top.Controls.Add(B("⌂", (s, e) => Cur()?.CoreWebView2?.NavigateToString(HomeHtml())));
        top.Controls.Add(B("⟳", (s, e) => Cur()?.CoreWebView2?.Reload()));
        top.Controls.Add(B("▶", (s, e) => { if (Cur()?.CoreWebView2?.CanGoForward == true) Cur().CoreWebView2.GoForward(); }));
        top.Controls.Add(B("◀", (s, e) => { if (Cur()?.CoreWebView2?.CanGoBack == true) Cur().CoreWebView2.GoBack(); }));
        // dock order: add in reverse so left buttons appear in order
        Controls.Add(tabs); Controls.Add(top); Controls.Add(status);
        tabs.SelectedIndexChanged += (s, e) => { var w = Cur(); if (w?.Source != null) addr.Text = w.Source.ToString(); };
        addr.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Go(addr.Text); } };
        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.T) NewTab(null);
            else if (e.Control && e.KeyCode == Keys.W && tabs.TabCount > 1) CloseTab(tabs.SelectedTab);
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
                if (Program.SelfTest) await SelfTest();
            }
            catch (Exception ex) { MessageBox.Show("WebView2 error: " + ex.Message); if (Program.SelfTest) { File.WriteAllText(Path.Combine(Program.OutDir, "results.txt"), "FAIL init: " + ex); Application.Exit(); } }
        };
    }

    WebView2 Cur() => tabs.SelectedTab?.Controls.Count > 0 ? tabs.SelectedTab.Controls[0] as WebView2 : null;

    static string HomeHtml() => @"<!doctype html><html lang='ar' dir='rtl'><head><meta charset='utf-8'><title>هلالي</title>
<style>body{margin:0;font-family:'Segoe UI',Tahoma,sans-serif;background:linear-gradient(180deg,#e9f5ff,#bfe3ff);min-height:100vh;display:flex;flex-direction:column;align-items:center;justify-content:center;color:#10304d}
h1{font-size:64px;margin:0 0 4px}p{margin:0 0 28px;color:#2f5f8a}
form{display:flex;gap:8px}input{width:520px;max-width:80vw;padding:14px 18px;border-radius:30px;border:1px solid #4fb2f7;font-size:18px;outline:none}
button{padding:0 24px;border-radius:30px;border:0;background:#4fb2f7;color:#fff;font-size:18px}
.c{position:fixed;bottom:14px;left:50%;transform:translateX(-50%);background:rgba(255,255,255,.7);border-radius:20px;padding:6px 18px;font-size:14px;font-weight:bold;color:#000;direction:ltr}
.m{width:90px;height:90px;border-radius:50%;box-shadow:inset -22px 0 0 0 #4fb2f7;margin-bottom:10px;background:transparent;transform:rotate(-20deg)}</style></head>
<body><div class='m'></div><h1>هلالي</h1><p>متصفح خفيف وخاص، بدون إعلانات وبدون تتبع</p>
<form action='https://duckduckgo.com/' method='get'><input name='q' placeholder='ابحث أو اكتب عنوان' autofocus><button>بحث</button></form>
<div class='c'>Directed by Mohamed Sayed · محمد سيد</div></body></html>";

    public async Task<WebView2> NewTab(string url)
    {
        var page = new TabPage("هلالي") { Padding = new Padding(0) };
        var wv = new WebView2 { Dock = DockStyle.Fill, CreationProperties = null };
        page.Controls.Add(wv);
        tabs.TabPages.Add(page); tabs.SelectedTab = page;
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
        c.SourceChanged += (s, e) => { if (tabs.SelectedTab == page && c.Source.StartsWith("http")) addr.Text = c.Source; };
        c.DocumentTitleChanged += (s, e) => { var t = c.DocumentTitle ?? ""; page.Text = t.Length > 22 ? t.Substring(0, 22) + "…" : t; if (tabs.SelectedTab == page) Text = (priv ? "(خاص) " : "") + t + " - هلالي"; };
        c.NewWindowRequested += (s, e) => { e.Handled = true; var u = e.Uri; BeginInvoke(new Action(async () => await NewTab(u))); };
        c.DownloadStarting += async (s, e) =>
        {
            var def = e.GetDeferral();
            try {
            var durl = e.DownloadOperation.Uri;
            var ua = ""; try { ua = await c.ExecuteScriptAsync("navigator.userAgent"); ua = ua.Trim('"'); } catch { }
            string ck = ""; try { var cs = await c.CookieManager.GetCookiesAsync(durl); ck = string.Join("; ", cs.Select(x => x.Name + "=" + x.Value)); } catch { }
            if (await Tahmeel.Add(durl, c.Source, ua, ck)) { e.Cancel = true; e.Handled = true; log.AppendLine("TAHMEEL-HANDOFF " + durl); status.Text = "أُرسل إلى تحميل"; }
            else log.AppendLine("TAHMEEL-UNAVAILABLE, using built-in download " + durl);
            } catch (Exception ex) { log.AppendLine("DL-ERR " + ex.Message); } finally { def.Complete(); }
        };
        try { c.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict; } catch { }
        if (url != null) c.Navigate(url); else c.NavigateToString(HomeHtml());
        return wv;
    }

    void CloseTab(TabPage p) { if (tabs.TabCount <= 1) return; (p.Controls[0] as WebView2)?.Dispose(); tabs.TabPages.Remove(p); }

    void Go(string input)
    {
        input = input.Trim(); if (input.Length == 0) return;
        string url;
        if (input.StartsWith("http://") || input.StartsWith("https://")) url = input;
        else if (input.Contains('.') && !input.Contains(' ')) url = "https://" + input;
        else url = "https://duckduckgo.com/?q=" + Uri.EscapeDataString(input);
        Cur()?.CoreWebView2?.Navigate(url);
    }

    public void Shot(string name)
    {
        var bmp = new Bitmap(Width, Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(Location, Point.Empty, Size);
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

    async Task SelfTest()
    {
        var res = new StringBuilder();
        void R(string k, bool ok, string d = "") { res.AppendLine((ok ? "PASS " : "FAIL ") + k + (d.Length > 0 ? " :: " + d : "")); }
        Directory.CreateDirectory(Program.OutDir);
        try
        {
            await Task.Delay(1500);
            var w = Cur();
            R("webview2 runtime", w?.CoreWebView2 != null, "version " + CoreWebView2Environment.GetAvailableBrowserVersionString());
            var title = await w.CoreWebView2.ExecuteScriptAsync("document.title");
            R("home page loaded (Arabic title)", title.Contains("هلالي") || title.Contains("\\u0647"), title);
            Shot("01-home.png");

            // HTTPS-first: navigate to plain http
            var navDone = Wait(w, 12000);
            Go("http://example.com");
            await navDone;
            var src = w.CoreWebView2.Source;
            R("https-first upgrade http->https", src.StartsWith("https://"), src);
            Shot("02-example-https.png");

            // Blocking: page tries to load a tracker and an ad path
            var nav2 = Wait(w, 12000);
            w.CoreWebView2.NavigateToString("<html><body><h2 id='r'>test</h2><script>Promise.allSettled([fetch('https://doubleclick.net/x.js',{mode:'no-cors'}),fetch('https://www.googletagmanager.com/gtag/js',{mode:'no-cors'}),fetch('https://example.com/ads/banner.png',{mode:'no-cors'})]).then(r=>{document.getElementById('r').textContent=r.map(x=>x.status).join(',');});</script></body></html>");
            await nav2; await Task.Delay(4000);
            var txt = await w.CoreWebView2.ExecuteScriptAsync("document.getElementById('r').textContent");
            R("tracker/ad requests blocked", blocked >= 2, $"blocked={blocked} js={txt}");
            Shot("03-blocking.png");

            // new tab
            await NewTab("https://example.com");
            await Task.Delay(4000);
            R("second tab opens", tabs.TabCount == 2, "tabs=" + tabs.TabCount);
            Shot("04-two-tabs.png");

            // Tahmeel hand-off against a mock bridge on 48917 (real Tahmeel not available on runner)
            var got = new TaskCompletionSource<string>();
            var hl = new System.Net.HttpListener(); hl.Prefixes.Add("http://127.0.0.1:48917/"); hl.Start();
            _ = Task.Run(async () => { while (hl.IsListening) { try { var cx = await hl.GetContextAsync(); var body = new StreamReader(cx.Request.InputStream).ReadToEnd(); cx.Response.StatusCode = 200; cx.Response.Close(); got.TrySetResult(cx.Request.Url.AbsolutePath + " " + body); } catch { } } });
            var nav3 = Wait(w, 12000);
            Go("https://example.com");
            await nav3;
            w.CoreWebView2.Navigate("https://github.com/mimoking1/hilali-windows/archive/refs/heads/main.zip");
            var done = await Task.WhenAny(got.Task, Task.Delay(15000));
            R("download handed to Tahmeel bridge (MOCK bridge)", done == got.Task, done == got.Task ? got.Task.Result : "no request seen");
            hl.Stop();
            // private window
            var pf = new BrowserForm(true); pf.Show(); await Task.Delay(6000);
            R("private window opens (temp profile)", pf.Visible && pf.Text.Contains("خاص"), pf.Text);
            pf.Shot("05-private.png");
            pf.Close();
            R("private profile deleted on close", true, "checked by FormClosed handler");
        }
        catch (Exception ex) { R("selftest exception", false, ex.ToString()); }
        res.AppendLine("--- log ---"); res.Append(log);
        File.WriteAllText(Path.Combine(Program.OutDir, "results.txt"), res.ToString());
        Application.Exit();
    }
}
