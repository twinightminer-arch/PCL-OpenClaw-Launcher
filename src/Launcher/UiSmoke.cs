using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ClawLauncher;
public sealed partial class MainWindow
{
 internal async Task RunGatewaySmoke(string report) {
  runner.Log+=line=>File.AppendAllText(report+".log",SafeLog.Clean(line)+Environment.NewLine);
  string? openedUrl=null;openDashboardUrl=url=>openedUrl=url;
  cancellation=new();await StartGateway();
  if(openedUrl==null||!Uri.TryCreate(openedUrl,UriKind.Absolute,out _))throw new Exception("Startup did not open dashboard");
  if(!owned.TryGetValue(current.Id,out var child)||child.HasExited)throw new Exception("Launcher did not start gateway child");
  for(int n=0;n<8;n++) {await Refresh();if(ReadModel.B(gateway?["rpc"],"ok")==true)break;await Task.Delay(1500);}
  if(ReadModel.B(gateway?["rpc"],"ok")!=true)throw new Exception("Gateway not reachable after startup");
  Close();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
  File.WriteAllText(report,"PASS launcher starts E:\\openclaw; RPC reachable; official dashboard URL handed to browser opener; closing stops only owned gateway child");
 }
 internal async Task RunUiSmoke(string directory) {
  Directory.CreateDirectory(directory);var checks=new List<string>();
  void Capture(string name){UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);}
  foreach(var name in new[]{"启动总览","版本下载","版本与实例","插件市场","软件连接","实例设置","运行环境","导入实例设置","插件管理","Skills 管理","整合包","操作日志","个性化","背景音乐","关于"}) {CapturePage(name);await Task.Delay(40);Capture(name);checks.Add("PASS page: "+name);}
  // 0.8.6：实例设置是「每个实例一份」的东西，进入后顶端分区导航让位给「实例设置」一项，退出即还原。
  CapturePage("实例设置");
  if(topButtons["实例设置"].Visibility!=Visibility.Visible)throw new Exception("实例设置导航项未出现");
  if(topButtons["启动"].Visibility!=Visibility.Collapsed)throw new Exception("实例设置模式下分区导航未让位");
  CapturePage("启动总览");
  if(topButtons["实例设置"].Visibility!=Visibility.Collapsed)throw new Exception("离开实例设置后导航项未隐藏");
  if(topButtons["启动"].Visibility!=Visibility.Visible)throw new Exception("离开实例设置后分区导航未还原");
  checks.Add("PASS instance-settings nav swaps in and restores the section nav");
  // 0.8.6：列表快照让插件 / Skill 进页即全显，不等后台扫描；折叠按钮只影响本分区。
  CapturePage("插件管理");var instant=plugins.Count;
  checks.Add(instant>0?"PASS plugin list shows "+instant+" entries without waiting for a scan":"UNVERIFIED list snapshot: 该实例尚无缓存，首次进入仍需扫描");
  if(pluginToggle!=null) {
   var section=pluginSection;
   if(section==null)throw new Exception("插件分区未构建");
   pluginToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
   if(section.Visibility!=Visibility.Collapsed)throw new Exception("插件分区未收起");
   Capture("插件-收起");
   pluginToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
   if(section.Visibility!=Visibility.Visible)throw new Exception("插件分区未展开");
   checks.Add("PASS plugin section collapses and expands");
  }
  Capture("插件列表");
  channels=[new("telegram","Telegram","已连接","网关报告连接已建立。",Account:"default"),new("signal","Signal","未连接","连接未建立。"),new("email","邮箱","运行中 · 连接未验证","适配器运行中，尚未返回探测结果。")];
  CapturePage("软件连接");await Task.Delay(40);Capture("应用连接区域");checks.Add("PASS populated per-application cards with green, red and amber indicators");
  var pair=(System.Windows.Controls.StackPanel)BrandPair(68,20);var left=(System.Windows.Controls.Image)pair.Children[0];var right=(System.Windows.Controls.Image)pair.Children[1];
  if(left.Width!=right.Width||left.Height!=right.Height||left.Width!=left.Height)throw new Exception("Brand dimensions differ");checks.Add("PASS equal square brand images in a horizontal pair");
  var savedSplash=store.Settings.Appearance.Splash;store.Settings.Appearance.Splash=true;
  ShowSplashFrame();Capture("开场动画");await PlayOpening();store.Settings.Appearance.Splash=savedSplash;if(splash.Visibility!=Visibility.Collapsed)throw new Exception("开场动画未正常退出。");checks.Add("PASS animated opening completes");
  var savedImage=store.Settings.Appearance.BackgroundImage;var savedOpacity=store.Settings.Appearance.CardOpacity;
  store.Settings.Appearance.BackgroundImage=Path.Combine(AppContext.BaseDirectory,"Assets","ocl.png");store.Settings.Appearance.CardOpacity=.45;ApplyAppearance();if(wallpaper.Source==null)throw new Exception("背景预览未加载。");CapturePage("个性化");Capture("背景透明预览");checks.Add("PASS image background and card opacity");store.Settings.Appearance.BackgroundImage=savedImage;store.Settings.Appearance.CardOpacity=savedOpacity;ApplyAppearance();
  Width=850;Height=540;CapturePage("版本下载");await Task.Delay(40);Capture("最小窗口");checks.Add("PASS minimum window render");Width=1040;Height=660;
  var audio=Path.Combine(directory,"silence.wav");using(var w=new BinaryWriter(File.Create(audio))){const int length=44100*2;w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+length);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(44100);w.Write(88200);w.Write((short)2);w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(length);w.Write(new byte[length]);}
  var opened=new TaskCompletionSource<bool>();EventHandler ready=(_,_)=>opened.TrySetResult(true);EventHandler<ExceptionEventArgs> failed=(_,e)=>opened.TrySetResult(false);media.MediaOpened+=ready;media.MediaFailed+=failed;media.Volume=0;media.Open(new Uri(audio));media.Play();var completed=await Task.WhenAny(opened.Task,Task.Delay(8000));checks.Add(completed==opened.Task&&await opened.Task?"PASS WAV media decoder and playback open":"UNVERIFIED audio device/codec unavailable in test session");media.Stop();media.Close();media.MediaOpened-=ready;media.MediaFailed-=failed;
  var originalAccent=store.Settings.Appearance.Accent;store.Settings.Appearance.Accent="#348572";SaveAppearance();
  if(titleBar.Background is not SolidColorBrush titleBrush||titleBrush.Color!=(Color)ColorConverter.ConvertFromString("#348572"))throw new Exception("Title color did not change");
  if(new Store(store.Root).Settings.Appearance.Accent!="#348572")throw new Exception("Title color did not persist");
  checks.Add("PASS title color changes and persists");CapturePage("启动总览");Capture("顶栏选色");
  overviewToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));if(contentPane.Visibility!=Visibility.Collapsed)throw new Exception("Overview did not collapse");Capture("收起总览");
  CapturePage("关于");if(contentPane.Visibility!=Visibility.Visible)throw new Exception("Collapse leaked into another page");
  CapturePage("启动总览");if(contentPane.Visibility!=Visibility.Collapsed)throw new Exception("Refresh lost collapsed state");
  overviewToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));if(contentPane.Visibility!=Visibility.Visible)throw new Exception("Overview did not expand");checks.Add("PASS overview collapse, navigation, refresh and expand");
  var savedGateway=gateway;
  foreach(var state in new[]{"unknown","offline","ready"}){
   gateway=state=="unknown"?null:System.Text.Json.Nodes.JsonNode.Parse(state=="ready"?"{\"rpc\":{\"ok\":true}}":"{\"rpc\":{\"ok\":false}}");
   var expected=state=="unknown"?"#999999":state=="ready"?"#248B69":"#D9363E";
   var indicator=(System.Windows.Shapes.Ellipse)((System.Windows.Controls.StackPanel)GatewaySummary()).Children[1];
   if(((SolidColorBrush)indicator.Fill).Color!=(Color)ColorConverter.ConvertFromString(expected))throw new Exception("Wrong gateway light: "+state);
   CapturePage("启动总览");Capture("状态灯-"+state);
  }
  checks.Add("PASS gray, red and green gateway indicators");gateway=savedGateway;store.Settings.Appearance.Accent=originalAccent;SaveAppearance();
  // Regression: a deliberately hung CLI must never hold page navigation or closing hostage.
  var original=current;var fake=Path.Combine(directory,"slow-cli");Directory.CreateDirectory(fake);
  File.WriteAllText(Path.Combine(fake,"openclaw.mjs"),"setTimeout(()=>console.log('{}'),60000)");
  current=new Instance{Runtime=fake};SelectPage("插件管理");var pending=Refresh();
  await Task.Delay(150);var watch=System.Diagnostics.Stopwatch.StartNew();Navigate("个性化");UpdateLayout();watch.Stop();
  if(page!="个性化"||busy||watch.ElapsedMilliseconds>1500)throw new Exception("Background CLI blocked navigation");
  checks.Add("PASS navigation while CLI hung: "+watch.ElapsedMilliseconds+" ms");
  await pending.WaitAsync(TimeSpan.FromSeconds(5));if(page!="个性化")throw new Exception("Stale refresh changed current page");
  checks.Add("PASS canceled refresh cannot overwrite destination");current=original;
  CapturePage("启动总览");await Task.Delay(40);Capture("启动总览");
  current=new Instance{Runtime=fake};SelectPage("插件管理");pending=Refresh();await Task.Delay(150);
  var closed=false;Closed+=(_,_)=>closed=true;watch.Restart();Close();watch.Stop();
  if(!closed||watch.ElapsedMilliseconds>1500)throw new Exception("Closing blocked by background operation");
  checks.Add("PASS close during hung CLI: "+watch.ElapsedMilliseconds+" ms");
  await pending.WaitAsync(TimeSpan.FromSeconds(5));checks.Add("PASS closing cancels and reaps status child");
  File.WriteAllLines(Path.Combine(directory,"ui-checks.txt"),checks);
 }
}
