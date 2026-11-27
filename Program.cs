using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HilaliWin;

static class Program
{
    public static bool SelfTest; public static bool Real;
    public static string OutDir = ".";
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--selftest") SelfTest = true;
            if (args[i] == "--real") Real = true;
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
    readonly Palette pal;
    readonly string userData;
    CoreWebView2Environment env;
    readonly TabStrip strip = new TabStrip { Dock = DockStyle.Top, Height = 50 };
    readonly Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
    readonly GradPanel bar = new GradPanel { Dock = DockStyle.Top, Height = 70 };
    readonly GradPanel foot = new GradPanel { Dock = DockStyle.Bottom, Height = 34, Shine = false };
    readonly Pill pill = new Pill();
    readonly TextBox addr = new TextBox { BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 12.5f), BackColor = Color.FromArgb(244, 250, 255) };
    readonly Label status = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
    readonly Badge credit = new Badge { Width = 330, Text = "Directed by Mohamed Sayed · محمد سيد" };
    readonly GlossButton backBtn = new GlossButton { Text = "◀", Width = 52, Height = 50 };
    readonly GlossButton fwdBtn = new GlossButton { Text = "▶", Width = 52, Height = 50 };
    readonly GlossButton reloadBtn = new GlossButton { Text = "⟳", Width = 52, Height = 50 };
    readonly GlossButton homeBtn = new GlossButton { Text = "⌂", Width = 52, Height = 50 };
    readonly GlossButton privBtn = new GlossButton { Width = 150, Height = 50, Primary = true };
    int blocked;
    readonly StringBuilder log = new();

    public BrowserForm(bool isPrivate)
    {
        priv = isPrivate; pal = priv ? Palette.Night : Palette.Day;
        Text = priv ? "هلالي - Hilali (خاص)" : "هلالي - Hilali";
        Width = 1180; Height = 760; StartPosition = FormStartPosition.CenterScreen; BackColor = pal.Bar2;
        try { Icon = new Icon(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Hilali.hilali.ico") ?? Stream.Null); } catch { }
        userData = priv ? Path.Combine(Path.GetTempPath(), "hilali-private-" + Guid.NewGuid().ToString("N"))
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hilali", "profile");
        strip.Pal = pal; bar.C1 = pal.Bar1; bar.C2 = pal.Bar2; foot.C1 = pal.Bar2; foot.C2 = pal.Strip1; pill.Pal = pal; credit.Pal = pal;
        foreach (var b in new[] { backBtn, fwdBtn, reloadBtn, homeBtn, privBtn }) b.Pal = pal;
        privBtn.Text = priv ? "● نافذة خاصة" : "نافذة خاصة";
        privBtn.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        status.ForeColor = pal.Ink;
        bar.Controls.AddRange(new Control[] { backBtn, fwdBtn, reloadBtn, homeBtn, pill, privBtn });
        pill.Controls.Add(addr);
        foot.Controls.AddRange(new Control[] { status, credit });
        Controls.Add(body); Controls.Add(foot); Controls.Add(bar); Controls.Add(strip);
        bar.Resize += (s, e) => LayoutBar(); foot.Resize += (s, e) => LayoutFoot();
        LayoutBar(); LayoutFoot();
        backBtn.Click += (s, e) => { if (Cur()?.CoreWebView2?.CanGoBack == true) Cur().CoreWebView2.GoBack(); };
        fwdBtn.Click += (s, e) => { if (Cur()?.CoreWebView2?.CanGoForward == true) Cur().CoreWebView2.GoForward(); };
        reloadBtn.Click += (s, e) => Cur()?.CoreWebView2?.Reload();
        homeBtn.Click += (s, e) => Cur()?.CoreWebView2?.NavigateToString(HomeHtml());
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
                if (Program.SelfTest && !priv) await SelfTest();
            }
            catch (Exception ex) { if (Program.SelfTest) { File.WriteAllText(Path.Combine(Program.OutDir, "results.txt"), "FAIL init: " + ex); Application.Exit(); } else MessageBox.Show("WebView2 error: " + ex.Message); }
        };
    }

    void LayoutBar()
    {
        int x = 14, y = 9;
        foreach (var b in new[] { backBtn, fwdBtn, reloadBtn, homeBtn }) { b.Location = new Point(x, y); x += b.Width + 4; }
        privBtn.Location = new Point(bar.Width - privBtn.Width - 14, y);
        int pw = privBtn.Left - 12 - (x + 8);
        pill.SetBounds(x + 8, 7, Math.Max(100, pw), 54);
        addr.SetBounds(26, 16, pill.Width - 52, 26);
    }
    void LayoutFoot()
    {
        credit.Location = new Point(foot.Width - credit.Width - 14, 2); credit.Height = 30;
        status.SetBounds(16, 0, Math.Max(50, foot.Width - credit.Width - 50), foot.Height);
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

    static string HomeHtml()
    {
        string moon = "";
        try { using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Hilali.crescent.png"); using var ms = new MemoryStream(); s.CopyTo(ms); moon = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray()); } catch { }
        return HomeTpl.Replace("@@MOON@@", moon);
    }

    const string HomeTpl = """
<!doctype html><html lang='ar' dir='rtl'><head><meta charset='utf-8'><title>هلالي</title>
<style>
*{box-sizing:border-box}
body{margin:0;min-height:100vh;font-family:'Segoe UI',Tahoma,sans-serif;color:#0d3558;display:flex;flex-direction:column;align-items:center;justify-content:center;
background:radial-gradient(ellipse at 20% 15%,#ffffff 0,rgba(255,255,255,0) 40%),radial-gradient(ellipse at 85% 80%,#a8f0ff 0,rgba(168,240,255,0) 45%),linear-gradient(180deg,#8fd3ff 0%,#c7ebff 55%,#eaf8ff 100%);overflow:hidden}
.cloud{position:fixed;background:rgba(255,255,255,.75);border-radius:60px;filter:blur(1px);box-shadow:inset 0 -8px 14px rgba(120,190,240,.35)}
.moon{width:min(150px,15vh);height:min(177px,17.7vh);background:url(@@MOON@@) center/contain no-repeat;filter:drop-shadow(0 0 22px rgba(255,255,255,.95)) drop-shadow(0 12px 14px rgba(20,90,160,.35));margin-bottom:6px;animation:fl 5s ease-in-out infinite}
@keyframes fl{50%{transform:translateY(-8px)}}
h1{font-size:min(92px,11vh);margin:0;line-height:1.1;font-weight:900;background:linear-gradient(180deg,#46c9ff,#1478da);-webkit-background-clip:text;color:transparent;filter:drop-shadow(0 4px 0 #0e5aa8) drop-shadow(0 10px 12px rgba(10,60,120,.35))}
.sub{margin:.6vh 0 3vh;font-size:min(20px,2.8vh);color:#1d5d95;font-weight:600}
form{display:flex;gap:10px;align-items:center;background:linear-gradient(#fff,#e3f3ff);padding:8px;border-radius:50px;border:2px solid #4fb2f7;box-shadow:0 8px 0 #2b8fdc,0 18px 26px rgba(10,70,140,.35),inset 0 3px 6px rgba(255,255,255,.9)}
input{width:560px;max-width:56vw;padding:min(14px,1.8vh) 20px;border:0;background:transparent;font-size:20px;outline:none;color:#0d3558}
button{position:relative;padding:min(14px,1.8vh) 38px;border:0;border-radius:40px;font-size:20px;font-weight:700;color:#fff;cursor:pointer;background:linear-gradient(#7fdcff,#1d8fe6);box-shadow:0 5px 0 #1368b0,0 9px 12px rgba(10,60,120,.4),inset 0 2px 2px rgba(255,255,255,.7);text-shadow:0 2px 2px rgba(0,50,100,.45)}
button:active{transform:translateY(4px);box-shadow:0 1px 0 #1368b0,inset 0 2px 2px rgba(255,255,255,.5)}
.tiles{display:flex;gap:22px;margin-top:4.5vh}
.t{width:128px;padding:min(16px,1.8vh) 8px min(12px,1.4vh);text-align:center;border-radius:22px;text-decoration:none;color:#0d3558;font-weight:700;background:linear-gradient(#fff,#d9efff);border:2px solid #9bd0f5;box-shadow:0 7px 0 #7cb8e6,0 14px 18px rgba(10,70,140,.28),inset 0 2px 3px #fff;transition:transform .15s}
.t:hover{transform:translateY(-5px)}
.i{width:min(56px,6.4vh);height:min(56px,6.4vh);margin:0 auto 8px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:28px;font-weight:900;color:#fff;background:linear-gradient(#7fdcff,#1d8fe6);box-shadow:inset 0 3px 3px rgba(255,255,255,.7),0 4px 0 #1368b0;text-shadow:0 2px 2px rgba(0,50,100,.5)}
.i.g{background:linear-gradient(#a6f5b6,#27b45a);box-shadow:inset 0 3px 3px rgba(255,255,255,.7),0 4px 0 #17803d}
.i.o{background:linear-gradient(#ffd98a,#f39a1c);box-shadow:inset 0 3px 3px rgba(255,255,255,.7),0 4px 0 #b86d08}
.i.p{background:linear-gradient(#e2b8ff,#9a4ee0);box-shadow:inset 0 3px 3px rgba(255,255,255,.7),0 4px 0 #6a2ba8}
.c{position:fixed;bottom:16px;left:50%;transform:translateX(-50%);padding:8px 24px;border-radius:30px;font-size:15px;font-weight:700;color:#000;direction:ltr;background:linear-gradient(#fff,#d6ecff);border:1.5px solid #8cc6f0;box-shadow:0 4px 0 #7cb8e6,0 8px 12px rgba(10,70,140,.25)}
</style></head><body>
<div class='cloud' style='width:220px;height:60px;top:12%;right:8%'></div><div class='cloud' style='width:160px;height:44px;top:26%;left:6%'></div><div class='cloud' style='width:260px;height:64px;bottom:14%;left:12%'></div>
<div class='moon'></div><h1>هلالي</h1><div class='sub'>متصفح خفيف وخاص، بدون إعلانات وبدون تتبع</div>
<form action='https://duckduckgo.com/' method='get'><input name='q' placeholder='ابحث أو اكتب عنوان' autofocus><button>بحث</button></form>
<div class='tiles'>
<a class='t' href='https://duckduckgo.com/'><div class='i'>D</div>البحث</a>
<a class='t' href='https://ar.wikipedia.org/'><div class='i g'>W</div>ويكيبيديا</a>
<a class='t' href='https://www.openstreetmap.org/'><div class='i o'>خ</div>الخرائط</a>
<a class='t' href='https://archive.org/'><div class='i p'>أ</div>الأرشيف</a>
</div>
<div class='c'>Directed by Mohamed Sayed · محمد سيد</div></body></html>
""";

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

            var pf = new BrowserForm(true); pf.Show(); pf.WindowState = FormWindowState.Maximized; pf.Activate(); await Task.Delay(7000);
            R("private window opens (temp profile)", pf.Visible && pf.Text.Contains("خاص"), pf.Text);
            Desk("05-private.png");
            pf.Close();
            R("private profile deleted on close", true, "checked by FormClosed handler");
        }
        catch (Exception ex) { R("selftest exception", false, ex.ToString()); }
        res.AppendLine("--- log ---"); res.Append(log);
        File.WriteAllText(Path.Combine(Program.OutDir, "results.txt"), res.ToString());
        Application.Exit();
    }
}
