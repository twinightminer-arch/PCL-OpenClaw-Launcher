using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ClawLauncher;
public partial class App : Application
{
 protected override void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  if(e.Args.Contains("--gateway-smoke")||e.Args.Contains("--ui-smoke"))ShutdownMode=ShutdownMode.OnExplicitShutdown;
  if(e.Args.Contains("--write-brand")){var dir=e.Args.SkipWhile(a=>a!="--write-brand").Skip(1).FirstOrDefault()??Path.Combine(AppContext.BaseDirectory,"Assets");Brand.WriteAssets(dir);Shutdown();return;}
  // --shortcut <path>：把「指向当前 exe 的快捷方式」写到指定位置（发布时更新桌面/仓库内的 OCL.lnk）。
  if(e.Args.Contains("--shortcut")){var link=e.Args.SkipWhile(a=>a!="--shortcut").Skip(1).FirstOrDefault();if(!string.IsNullOrWhiteSpace(link))new Store().WriteShortcut(Path.GetFullPath(link));Shutdown();return;}
  DispatcherUnhandledException += (_, a) => {if(e.Args.Contains("--ui-smoke")||e.Args.Contains("--gateway-smoke")||e.Args.Contains("--screenshot")){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-error.txt"),a.Exception.ToString());a.Handled=true;Shutdown(1);return;}MessageBox.Show(SafeLog.Clean(a.Exception.Message), "启动器错误"); a.Handled = true; };
  var window = new MainWindow(e.Args.Contains("--screenshot")||e.Args.Contains("--gateway-smoke")||e.Args.Contains("--ui-smoke"));
  MainWindow = window;
  if(e.Args.Contains("--gateway-smoke")){window.ShowInTaskbar=false;window.ShowActivated=false;window.Loaded+=async(_,_)=>{await window.RunGatewaySmoke(e.Args.Last());Shutdown();};}
  if(e.Args.Contains("--ui-smoke")){window.ShowInTaskbar=false;window.ShowActivated=false;window.Left=-20000;window.Loaded+=async(_,_)=>{var dir=e.Args.SkipWhile(a=>a!="--ui-smoke").Skip(1).First();await window.RunUiSmoke(dir);Shutdown();};}
  if (e.Args.Contains("--screenshot")) {
   var page=e.Args.SkipWhile(a=>a!="--screenshot").Skip(1).FirstOrDefault();if(page!=null)window.CapturePage(page);
   window.ShowInTaskbar = false; window.ShowActivated = false; window.Left = -20000;
   window.Loaded += async (_, _) => { await Task.Delay(600); window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(AppContext.BaseDirectory,"preview.png")); png.Save(file); Shutdown(); };
  }
  window.Show();
 }
}
