namespace YikaiLocal;

internal static class Branding
{
    public static Icon LoadIcon()
    {
        using var stream = typeof(Branding).Assembly.GetManifestResourceStream("YikaiLocal.Assets.app.ico")!;
        using var icon = new Icon(stream, 32, 32);
        return (Icon)icon.Clone();
    }

    public static Control CreateHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, AccessibleName = "易开面板" };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        using var stream = typeof(Branding).Assembly.GetManifestResourceStream("YikaiLocal.Assets.brand.png")!;
        using var image = Image.FromStream(stream);
        var picture = new PictureBox { Image = new Bitmap(image), SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "易开 Logo" };
        picture.Disposed += (_, _) => picture.Image?.Dispose();
        header.Controls.Add(picture, 0, 0);
        var words = new Panel { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), AccessibleName = "易开面板" };
        words.Paint += (_, e) =>
        {
            var h = words.ClientSize.Height;
            using var title = new Font("Microsoft YaHei UI", h * 0.43f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var subtitle = new Font("Microsoft YaHei UI", h * 0.22f, FontStyle.Regular, GraphicsUnit.Pixel);
            TextRenderer.DrawText(e.Graphics, "易开面板", title, new Rectangle(0, 0, words.Width, (int)(h * 0.63)), Palette.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, "Yikai Panel", subtitle, new Rectangle(0, (int)(h * 0.63), words.Width, (int)(h * 0.37)), Palette.Secondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
        header.Controls.Add(words, 1, 0);
        return header;
    }
}
