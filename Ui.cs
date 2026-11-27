using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Web.WebView2.WinForms;

namespace HilaliWin;

static class G
{
    public static GraphicsPath RR(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        if (d > r.Height) d = r.Height; if (d > r.Width) d = r.Width;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    public static GraphicsPath TopRR(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom); p.CloseFigure(); return p;
    }
    public static void Shadow(Graphics g, RectangleF r, float rad, int layers = 5, int maxA = 38)
    {
        for (int i = layers; i >= 1; i--)
        {
            var rr = new RectangleF(r.X - i + 1, r.Y + 2 + (layers - i) * 0.4f, r.Width + 2 * i - 2, r.Height + i);
            using var p = RR(rr, rad + i); using var b = new SolidBrush(Color.FromArgb(maxA / layers + 2, 20, 60, 110)); g.FillPath(b, p);
        }
    }
    public static void Gloss(Graphics g, RectangleF r, float rad, int a1 = 150, int a2 = 10)
    {
        var gr = new RectangleF(r.X + 2, r.Y + 1.5f, r.Width - 4, r.Height * 0.52f);
        if (gr.Height < 4) return;
        using var p = RR(gr, Math.Max(2, rad - 2));
        using var b = new LinearGradientBrush(gr, Color.FromArgb(a1, 255, 255, 255), Color.FromArgb(a2, 255, 255, 255), 90f);
        g.FillPath(b, p);
    }
    public static void Quality(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; g.PixelOffsetMode = PixelOffsetMode.HighQuality; }
}

record Palette(Color Strip1, Color Strip2, Color Bar1, Color Bar2, Color Hi, Color Lo, Color Border, Color Ink)
{
    public static Palette Day = new(
        ColorTranslator.FromHtml("#9ad9ff"), ColorTranslator.FromHtml("#4fb2f7"),
        ColorTranslator.FromHtml("#ffffff"), ColorTranslator.FromHtml("#cfeaff"),
        ColorTranslator.FromHtml("#7fd6ff"), ColorTranslator.FromHtml("#1d8fe6"),
        ColorTranslator.FromHtml("#1a78c8"), ColorTranslator.FromHtml("#0d3558"));
    public static Palette Night = new(
        ColorTranslator.FromHtml("#c9b3ff"), ColorTranslator.FromHtml("#8c68f0"),
        ColorTranslator.FromHtml("#fbf8ff"), ColorTranslator.FromHtml("#dccfff"),
        ColorTranslator.FromHtml("#b99cff"), ColorTranslator.FromHtml("#6a43d8"),
        ColorTranslator.FromHtml("#5530b8"), ColorTranslator.FromHtml("#2a1760"));
}

class GradPanel : Panel
{
    public Color C1, C2; public bool Shine = true;
    public GradPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.Quality(g);
        using (var b = new LinearGradientBrush(ClientRectangle, C1, C2, 90f)) g.FillRectangle(b, ClientRectangle);
        if (Shine) using (var p = new Pen(Color.FromArgb(200, 255, 255, 255))) g.DrawLine(p, 0, 0, Width, 0);
        using (var p = new Pen(Color.FromArgb(70, 20, 80, 140))) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
    }
}

class GlossButton : Control
{
    public bool Primary; public Palette Pal = Palette.Day; bool hover, down;
    public GlossButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, false);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; Cursor = Cursors.Hand; Font = new Font("Segoe UI Symbol", 13f, FontStyle.Bold);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    public void PerformClick() => OnClick(EventArgs.Empty);
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.Quality(g);
        var r = new RectangleF(3, 2, Width - 7, Height - 9);
        float rad = Math.Min(16, r.Height / 2.2f);
        if (!down) G.Shadow(g, r, rad);
        if (down) r.Offset(0, 2);
        Color top, bot, txt; 
        if (Primary) { top = Pal.Hi; bot = Pal.Lo; txt = Color.White; }
        else { top = Color.White; bot = Color.FromArgb(205, 232, 252); txt = Pal.Ink; }
        if (hover) { top = ControlPaint.Light(top, 0.25f); bot = ControlPaint.Light(bot, 0.15f); }
        using (var p = G.RR(r, rad))
        {
            using (var b = new LinearGradientBrush(r, top, bot, 90f)) g.FillPath(b, p);
            G.Gloss(g, r, rad, Primary ? 140 : 190, 10);
            using (var pen = new Pen(Primary ? Pal.Border : Color.FromArgb(150, 90, 160, 220), 1.4f)) g.DrawPath(pen, p);
        }
        // bottom inner light for depth
        using (var p = new Pen(Color.FromArgb(Primary ? 90 : 120, 255, 255, 255)))
            g.DrawArc(p, r.X + 3, r.Bottom - rad * 2 - 2, r.Width - 7, rad * 2, 20, 140);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        var tr = new RectangleF(r.X, r.Y, r.Width, r.Height);
        if (Primary) using (var sb = new SolidBrush(Color.FromArgb(110, 10, 50, 100))) g.DrawString(Text, Font, sb, new RectangleF(tr.X, tr.Y + 1.5f, tr.Width, tr.Height), sf);
        using (var tb = new SolidBrush(txt)) g.DrawString(Text, Font, tb, tr, sf);
    }
}

class Badge : Control
{
    public Palette Pal = Palette.Day;
    public Badge() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; Font = new Font("Segoe UI", 9.5f, FontStyle.Bold); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.Quality(g);
        var r = new RectangleF(2, 1, Width - 5, Height - 4);
        using var p = G.RR(r, r.Height / 2);
        using (var b = new LinearGradientBrush(r, Color.White, Color.FromArgb(200, 232, 255), 90f)) g.FillPath(b, p);
        G.Gloss(g, r, r.Height / 2, 200, 20);
        using (var pen = new Pen(Color.FromArgb(160, 80, 150, 215), 1.2f)) g.DrawPath(pen, p);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        using var tb = new SolidBrush(Color.Black); g.DrawString(Text, Font, tb, r, sf);
    }
}

class Pill : Panel
{
    public Palette Pal = Palette.Day;
    public Pill() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.Quality(g);
        var r = new RectangleF(2, 3, Width - 5, Height - 8);
        float rad = r.Height / 2;
        G.Shadow(g, r, rad, 4, 26);
        using var p = G.RR(r, rad);
        using (var b = new LinearGradientBrush(r, Color.FromArgb(232, 243, 252), Color.White, 90f)) g.FillPath(b, p);
        using (var pen = new Pen(Pal.Border, 1.8f)) g.DrawPath(pen, p);
        using (var pen = new Pen(Color.FromArgb(70, 20, 70, 130))) g.DrawArc(pen, r.X + 6, r.Y + 2, r.Width - 12, rad * 2 - 4, 195, 150);
    }
}

class TabItem { public string Title = "هلالي"; public WebView2 View; public Rectangle Rect, Close; }

class TabStrip : Control
{
    public List<TabItem> Tabs = new(); public TabItem Selected; public Palette Pal = Palette.Day;
    public event Action<TabItem> Select, CloseReq; public event Action NewReq;
    Rectangle plus; TabItem hot; bool hotPlus;
    public TabStrip() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Font = new Font("Segoe UI", 10f, FontStyle.Bold); }
    void LayoutTabs()
    {
        int n = Math.Max(1, Tabs.Count); int w = Math.Min(230, (Width - 90) / n); int x = 14;
        foreach (var t in Tabs) { t.Rect = new Rectangle(x, 8, w, Height - 8); t.Close = new Rectangle(t.Rect.Right - 28, t.Rect.Y + (t.Rect.Height - 20) / 2 + 1, 20, 20); x += w + 4; }
        plus = new Rectangle(x + 2, 11, 34, 28);
    }
    protected override void OnMouseMove(MouseEventArgs e) { var h = Tabs.FirstOrDefault(t => t.Rect.Contains(e.Location)); bool hp = plus.Contains(e.Location); if (h != hot || hp != hotPlus) { hot = h; hotPlus = hp; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = null; hotPlus = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        LayoutTabs();
        if (plus.Contains(e.Location)) { NewReq?.Invoke(); return; }
        foreach (var t in Tabs.ToArray())
            if (t.Rect.Contains(e.Location)) { if (t.Close.Contains(e.Location) && Tabs.Count > 1) CloseReq?.Invoke(t); else Select?.Invoke(t); return; }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.Quality(g); LayoutTabs();
        using (var b = new LinearGradientBrush(ClientRectangle, Pal.Strip1, Pal.Strip2, 90f)) g.FillRectangle(b, ClientRectangle);
        using (var p = new Pen(Color.FromArgb(170, 255, 255, 255))) g.DrawLine(p, 0, 0, Width, 0);
        foreach (var t in Tabs.Where(x => x != Selected).Concat(Tabs.Where(x => x == Selected)))
        {
            bool sel = t == Selected; var r = t.Rect;
            var rf = new RectangleF(r.X, r.Y + (sel ? 0 : 4), r.Width, r.Height - (sel ? 0 : 4) + 1);
            using var path = G.TopRR(rf, 13);
            if (sel)
            {
                using (var sh = G.TopRR(new RectangleF(rf.X - 2, rf.Y - 1, rf.Width + 4, rf.Height), 14)) using (var sb = new SolidBrush(Color.FromArgb(40, 10, 50, 110))) g.FillPath(sb, sh);
                using var b = new LinearGradientBrush(rf, Color.White, Pal.Bar1, 90f); g.FillPath(b, path);
                G.Gloss(g, rf, 12, 160, 10);
            }
            else
            {
                var c1 = t == hot ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255);
                using var b = new LinearGradientBrush(rf, c1, Color.FromArgb(60, 255, 255, 255), 90f); g.FillPath(b, path);
            }
            using (var pen = new Pen(Color.FromArgb(sel ? 190 : 110, Pal.Border), 1.2f)) g.DrawPath(pen, path);
            var tr = new RectangleF(r.X + 12, r.Y + 6, r.Width - 46, r.Height - 6);
            var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var tb = new SolidBrush(Pal.Ink)) g.DrawString(t.Title, Font, tb, tr, sf);
            if (Tabs.Count > 1)
            {
                var cr = new RectangleF(t.Close.X, t.Close.Y + (sel ? 0 : 3), 20, 20);
                using (var b = new SolidBrush(Color.FromArgb(sel ? 70 : 50, Pal.Lo))) g.FillEllipse(b, cr);
                using (var pen = new Pen(Pal.Ink, 1.8f)) { g.DrawLine(pen, cr.X + 6, cr.Y + 6, cr.Right - 6, cr.Bottom - 6); g.DrawLine(pen, cr.Right - 6, cr.Y + 6, cr.X + 6, cr.Bottom - 6); }
            }
        }
        var pr = new RectangleF(plus.X, plus.Y, plus.Width, plus.Height);
        G.Shadow(g, pr, 12, 3, 24);
        using (var p = G.RR(pr, 12)) { using var b = new LinearGradientBrush(pr, hotPlus ? Color.White : Color.FromArgb(240, 250, 255), Pal.Hi, 90f); g.FillPath(b, p); G.Gloss(g, pr, 12, 190, 10); using var pen = new Pen(Pal.Border, 1.3f); g.DrawPath(pen, p); }
        using (var pen = new Pen(Pal.Ink, 2.2f)) { g.DrawLine(pen, pr.X + 10, pr.Y + 14, pr.Right - 10, pr.Y + 14); g.DrawLine(pen, pr.X + 17, pr.Y + 7, pr.X + 17, pr.Bottom - 7); }
    }
}
