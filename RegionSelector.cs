namespace WallpaperPeek;

internal sealed class RegionSelector : Form
{
    private Point _start;
    private Rectangle _selection;
    public event Action<Rectangle>? RegionSelected;

    public RegionSelector()
    {
        var area = Screen.PrimaryScreen!.Bounds;
        Bounds = area;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = .28;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }
    protected override void OnMouseDown(MouseEventArgs e) { _start = e.Location; _selection = new Rectangle(e.Location, Size.Empty); Capture = true; base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (Capture) { _selection = Rectangle.FromLTRB(Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y), Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y)); Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        Capture = false;
        // Hiding first also makes any fallback capture free of the dark selector layer.
        if (_selection.Width > 8 && _selection.Height > 8) { Hide(); RegionSelected?.Invoke(new Rectangle(_selection.Location + (Size)Location, _selection.Size)); }
        Close(); base.OnMouseUp(e);
    }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!_selection.IsEmpty) { using var pen = new Pen(Color.DeepSkyBlue, 2); e.Graphics.DrawRectangle(pen, _selection); using var brush = new SolidBrush(Color.FromArgb(90, Color.DeepSkyBlue)); e.Graphics.FillRectangle(brush, _selection); }
        base.OnPaint(e);
    }
}
