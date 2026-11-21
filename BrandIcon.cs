using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeChatCleaner {
 internal static class BrandIcon {
  // Both the executable's Win32 icon and this managed resource are built from assets/WeChatCleaner.ico.
  internal static readonly BitmapFrame Source = Load();

  static BitmapFrame Load() {
   using (var stream = typeof(BrandIcon).Assembly.GetManifestResourceStream("WeChatCleaner.Brand.ico")) {
    if (stream == null) throw new InvalidOperationException("程序缺少内嵌图标。");
    var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    BitmapFrame largest = decoder.Frames[0];
    foreach (BitmapFrame frame in decoder.Frames) {
     if (frame.PixelWidth > largest.PixelWidth) largest = frame;
     if (frame.CanFreeze) frame.Freeze();
    }
    // Keep a frame from the ICO decoder: WPF can choose the small images for the native window.
    return largest;
   }
  }

  internal static Image CreateImage(double size) {
   var image = new Image { Source = Source, Width = size, Height = size, Stretch = Stretch.Uniform,
    SnapsToDevicePixels = true, IsHitTestVisible = false };
   RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
   return image;
  }
 }
}
