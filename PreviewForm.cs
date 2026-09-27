namespace WallpaperPeek;

internal sealed class PreviewForm : Form
{
    private readonly Bitmap _image;
    private Point _dragStart;
    private bool _dragging;
    private bool _clickThrough;
    private const int ResizeBorder = 8;
    public bool IsClickThrough => _clickThrough;

    public PreviewForm(Bitmap image, Size? initialSize = null)
    {
        _image = image;
        Text = "WallpaperPeek";
        // A fresh capture appears at the exact pixel dimensions the user selected.
        ClientSize = initialSize is { Width: >= 80, Height: >= 80 } saved ? saved : image.Size;
        MinimumSize = new Size(80, 80);
        FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false;
        BackColor = Color.Black;
        DoubleBuffered = true; ResizeRedraw = true; KeyPreview = true;
    }

    public void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.DrawImage(_image, ClientRectangle);
    }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _dragStart = e.Location; _dragging = true; } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_dragging && e.Button == MouseButtons.Left) Location += (Size)e.Location - (Size)_dragStart; base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Opacity = Math.Clamp(Opacity + Math.Sign(e.Delta) * .08, .2, 1); base.OnMouseWheel(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); if (e.KeyCode == Keys.L) ToggleClickThrough(); base.OnKeyDown(e); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_NCHITTEST && _clickThrough) { m.Result = (IntPtr)NativeMethods.HTTRANSPARENT; return; }
        if (m.Msg == NativeMethods.WM_NCHITTEST && !_clickThrough)
        {
            base.WndProc(ref m);
            if ((int)m.Result == NativeMethods.HTCLIENT)
            {
                var p = PointToClient(Cursor.Position);
                var left = p.X <= ResizeBorder; var right = p.X >= ClientSize.Width - ResizeBorder;
                var top = p.Y <= ResizeBorder; var bottom = p.Y >= ClientSize.Height - ResizeBorder;
                m.Result = (IntPtr)(top && left ? NativeMethods.HTTOPLEFT : top && right ? NativeMethods.HTTOPRIGHT : bottom && left ? NativeMethods.HTBOTTOMLEFT : bottom && right ? NativeMethods.HTBOTTOMRIGHT : left ? NativeMethods.HTLEFT : right ? NativeMethods.HTRIGHT : top ? NativeMethods.HTTOP : bottom ? NativeMethods.HTBOTTOM : NativeMethods.HTCLIENT);
                return;
            }
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing) { if (disposing) _image.Dispose(); base.Dispose(disposing); }
}
