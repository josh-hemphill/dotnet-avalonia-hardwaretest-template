using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace HardwareTest.Widgets.PdfPageView;

/// Displays a single PDF page bitmap.
public class PdfPageView : UserControl
{
    private readonly Image _image = new() { Stretch = Avalonia.Media.Stretch.Uniform };

    public PdfPageView()
    {
        Content = _image;
    }

    public void SetPage(Bitmap? bitmap)
    {
        _image.Source = bitmap;
    }
}
