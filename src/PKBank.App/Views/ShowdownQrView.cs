using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using PKBank.App.Theme;
using SKBitmapAlias = SkiaSharp.SKBitmap;
using SKColors = SkiaSharp.SKColors;
using ZXing.QrCode;

namespace PKBank.App.Views;

/// <summary>A Showdown text rendered as a scannable QR code (PKForge's QrPopup equivalent).</summary>
public sealed class ShowdownQrView : Border
{
    public ShowdownQrView(string setTitle, string setText)
    {
        Width = 380;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Background = PkChrome.PanelGradient();
        BorderBrush = PkChrome.Brush(PKBank.App.Theme.UiTokens.Rim);
        BorderThickness = new Thickness(1.2);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(20, 16);

        var column = new StackPanel { Spacing = 10 };
        column.Children.Add(PkChrome.PixelLabel("SHOWDOWN QR", 16, PKBank.App.Theme.UiTokens.Bright));
        column.Children.Add(PkChrome.BodyLabel(setTitle, 12, PKBank.App.Theme.UiTokens.Ink0, wrap: true));
        var qrImage = new Image
        {
            Source = new Avalonia.Media.Imaging.Bitmap(EncodeToStream(setText)),
            Width = 280,
            Height = 280,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        column.Children.Add(qrImage);
        column.Children.Add(PkChrome.BodyLabel("Scan with a phone running Pokémon Showdown to import the set.", 11, PKBank.App.Theme.UiTokens.InkSoft, wrap: true));

        var close = new Button { Content = "Close" };
        SaveEditorView.StyleButton(close);
        close.Click += (_, _) => this.IsVisible = false;
        column.Children.Add(close);
        Child = column;
    }

    private static Stream EncodeToStream(string text)
    {
        var data = new ZXing.BarcodeWriterPixelData().Write(text);
        using var bmp = new SKBitmapAlias(data.Width, data.Height);
        for (var y = 0; y < data.Height; y++)
        for (var x = 0; x < data.Width; x++)
        {
            var p = data.Pixels[(y * data.Width) + x];
            bmp.SetPixel(x, y, p > 127 ? SKColors.White : SKColors.Black);
        }
        using var image = SkiaSharp.SKImage.FromBitmap(bmp);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return new MemoryStream(encoded.ToArray());
    }
}
