using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ClawLauncher;
// Vector reinterpretation of PCL's layered hexagonal application mark, with OCL letters and red palette.
public static class Brand
{
 public const string Hex="M128,7 L233,67 L233,189 L128,249 L23,189 L23,67 Z";
 // 单一版本来源：改这里即可，UI 各处统一引用，避免像 0.7.0 那样漏改。
 public const string Version="0.8.8";
 static DrawingImage? cached;
 public static DrawingImage Image() {
  if(cached!=null)return cached;
  var group=new DrawingGroup();using(var d=group.Open()) {
   d.DrawGeometry(new SolidColorBrush(Color.FromRgb(112,14,29)),null,Geometry.Parse(Hex));
   d.DrawGeometry(new LinearGradientBrush(Color.FromRgb(255,82,91),Color.FromRgb(169,21,45),35),new Pen(new SolidColorBrush(Color.FromRgb(221,39,59)),5),Geometry.Parse("M128,18 L222,73 L222,183 L128,237 L34,183 L34,73 Z"));
   d.DrawGeometry(new LinearGradientBrush(Color.FromRgb(164,20,42),Color.FromRgb(252,64,75),90),null,Geometry.Parse("M128,34 L209,81 L209,175 L128,223 L47,175 L47,81 Z"));
   d.DrawGeometry(new LinearGradientBrush(Color.FromRgb(237,46,62),Color.FromRgb(164,20,43),100),new Pen(new SolidColorBrush(Color.FromRgb(124,14,32)),4),Geometry.Parse("M128,43 L201,85 L201,171 L128,213 L55,171 L55,85 Z"));
   d.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(255,89,95)),8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},Geometry.Parse("M127,9 L25,68 L25,184"));
   d.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(216,33,58)),8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},Geometry.Parse("M131,246 L234,186 L234,83"));
   var text=new FormattedText("OCL",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Arial"),FontStyles.Normal,FontWeights.Bold,FontStretches.Normal),68,Brushes.White,1);d.DrawText(text,new Point(128-text.Width/2,126-text.Height/2));
  }group.Freeze();cached=new DrawingImage(group);cached.Freeze();return cached;
 }
 public static void WriteAssets(string directory) {
  Directory.CreateDirectory(directory);var frames=new List<byte[]>();var sizes=new[]{16,24,32,48,64,128,256};
  foreach(var size in sizes){var visual=new DrawingVisual();using(var d=visual.RenderOpen())d.DrawImage(Image(),new Rect(0,0,size,size));var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();png.Save(stream);frames.Add(stream.ToArray());if(size==256)File.WriteAllBytes(Path.Combine(directory,"ocl.png"),stream.ToArray());}
  using var writer=new BinaryWriter(File.Create(Path.Combine(directory,"ocl.ico")));writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
  for(int n=0;n<sizes.Length;n++){writer.Write((byte)(sizes[n]==256?0:sizes[n]));writer.Write((byte)(sizes[n]==256?0:sizes[n]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[n].Length);writer.Write(offset);offset+=frames[n].Length;}foreach(var data in frames)writer.Write(data);
 }
}
