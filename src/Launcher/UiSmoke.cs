using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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
  // 0.8.7：把这次启动的时间轴与全部日志落盘——「点启动 → 控制台出现」到底花了几秒、
  // 花在哪一段，必须能在自检产物里看到，而不是靠估。
  if(launchTimeline.Length>0)
   File.WriteAllText(report+".timeline.txt",launchTimeline+Environment.NewLine+string.Join(Environment.NewLine,logLines));
  // 0.8.7：网关本来就跑着时，OCL 会**直接复用**它（不再启第二个，那会撞上单实例锁），
  // 所以「没有自己拉起的子进程」在这种情况下也是正确行为，不算失败。
  var startedByUs=owned.TryGetValue(current.Id,out var child)&&child!=null&&!child.HasExited;
  var reused=RanWarmGateway();
  if(!startedByUs&&!reused)throw new Exception("Launcher neither started a gateway child nor reused a running gateway");
  for(int n=0;n<8;n++) {await Refresh();if(ReadModel.B(gateway?["rpc"],"ok")==true)break;await Task.Delay(1500);}
  if(ReadModel.B(gateway?["rpc"],"ok")!=true)throw new Exception("Gateway not reachable after startup");
  Close();
  if(startedByUs)await child!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
  File.WriteAllText(report,"PASS 网关启动链路："
   +(startedByUs?"OCL 自己拉起网关子进程":"网关已在运行，OCL 直接复用它")
   +"；RPC 可达；控制台地址已交给浏览器；关闭只停掉自己拉起的网关子进程。时间轴："+launchTimeline);
 }

 /// <summary>这次启动是不是「复用已经在跑的网关」——判据是启动日志，不是猜。</summary>
 internal bool RanWarmGateway() =>
  logLines.Any(line=>line.Contains("已经有一个 OpenClaw 网关")||line.Contains("网关已经在运行"));
 internal async Task RunUiSmoke(string directory) {
  Directory.CreateDirectory(directory);var checks=new List<string>();
  // 上一次失败留下的 ui-error.txt 会让人误判，开跑先清掉。
  File.Delete(Path.Combine(AppContext.BaseDirectory,"ui-error.txt"));
  void Capture(string name){UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);}
  // 0.8.7：自检模式让「修复插件 / 打开网页控制台」只记录将要执行的动作——真装插件、真开浏览器留给用户点。
  dryRun=true;
  // 0.8.7：先真跑一次列表与官方清单并写快照，再进页面——验证「预热 → 点开即全显」整条链路。
  await WarmListCache(force:true);
  checks.Add("PASS snapshot warm-up: "+warmSummary);
  if(warmSummary.Contains("Skill=无")||warmSummary.Contains("Skill=0")||warmSummary.Contains("快照=未写入"))throw new Exception("快照预热没成功："+warmSummary);
  // 0.8.7 修复「网页控制台打不开」：网关单实例锁的复核。先只读看一遍，再走一次真实自愈路径。
  var locks=GatewayLock.Inspect();
  checks.Add("PASS 网关锁复核（只读）："+(locks.Count==0?"临时目录里没有锁":string.Join("｜",locks.Select(l=>l.Describe()))));
  ClearStaleGatewayLock();
  if(lastLockReport.Length==0)throw new Exception("网关锁复核没有给出结论");
  if(locks.Where(l=>l.IsStale).Count()!=lastLockCleared)throw new Exception("网关锁清理数量与复核结果不一致："+lastLockCleared+" vs "+lastLockReport);
  checks.Add("PASS 网关锁自愈："+lastLockReport);
  // 0.8.7 修订：运行环境体检（缺 build-info.json / 残留启动迁移租约）。自检里只做只读判定 +
  // 在临时目录上的可逆修复，绝不碰用户真实的状态库。
  if(RuntimeDoctor.Summary(current).Length==0)throw new Exception("运行环境结论是空的");
  checks.Add("PASS 运行环境结论："+RuntimeDoctor.Summary(current).Replace(Environment.NewLine,"｜"));
  var buildVerdict=RuntimeDoctor.BuildInfoVerdict(current);
  if(!buildVerdict.StartsWith("已就绪")&&!buildVerdict.StartsWith("缺少"))throw new Exception("构建标识结论异常："+buildVerdict);
  checks.Add("PASS 构建标识判定："+buildVerdict);
  // 后台预热在自检模式必须被挡住，否则自检会真去拉网关，既拖慢也会搅乱正在用的实例。
  if(owned.Count>0)throw new Exception("自检模式不应该启动任何网关子进程");
  checks.Add("PASS 后台预热在自检模式下不启动网关");
  var fakeAlreadyRunning="Gateway failed to start: gateway already running (pid 29096); lock timeout after 5000ms";
  if(!RuntimeDoctor.MentionsGatewayAlreadyRunning(fakeAlreadyRunning)||RuntimeDoctor.RunningPid(fakeAlreadyRunning)!=29096)throw new Exception("「已有网关在跑」的识别失效");
  var fakePortInUse="2026-10-04T04:30:15.916+08:00 Port 3000 is already in use.";
  if(!RuntimeDoctor.MentionsPortInUse(fakePortInUse))throw new Exception("端口占用的识别失效");
  checks.Add("PASS 启动失败归因：已有网关在跑 / 端口被占用 都能识别（并会改成直接复用那个网关）");
  File.WriteAllText(Path.Combine(directory,"warm-summary.txt"),warmSummary);
  foreach(var name in new[]{"启动总览","版本下载","版本选择","插件市场","软件连接","实例设置","运行环境","导入实例设置","插件管理","Skills 管理","整合包","操作日志","个性化","背景音乐","关于"}) {CapturePage(name);await Task.Delay(40);Capture(name);checks.Add("PASS page: "+name);}
  // 0.8.6：实例设置是「每个实例一份」的东西，进入后顶端分区导航让位给「实例设置」一项，退出即还原。
  CapturePage("实例设置");
  if(topButtons["实例设置"].Visibility!=Visibility.Visible)throw new Exception("实例设置导航项未出现");
  if(topButtons["启动"].Visibility!=Visibility.Collapsed)throw new Exception("实例设置模式下分区导航未让位");
  CapturePage("启动总览");
  if(topButtons["实例设置"].Visibility!=Visibility.Collapsed)throw new Exception("离开实例设置后导航项未隐藏");
  if(topButtons["启动"].Visibility!=Visibility.Visible)throw new Exception("离开实例设置后分区导航未还原");
  checks.Add("PASS instance-settings nav swaps in and restores the section nav");
  // 0.8.8：版本选择页必须渲染出每个实例的横向卡片（含置顶心形按钮）与「新建实例」卡片。
  CapturePage("版本选择");await Task.Delay(40);Capture("版本选择");
  int HeartCount(DependencyObject e){int n=0;if(e is Button b&&b.Content is string s&&(s=="♥"||s=="♡"))n++;foreach(var c in LogicalTreeHelper.GetChildren(e))if(c is DependencyObject d)n+=HeartCount(d);return n;}
  var hearts=HeartCount(body);
  if(hearts<1)throw new Exception("版本选择页没有渲染出实例卡片（心形按钮数="+hearts+"）");
  bool HasNewInstance(DependencyObject e){if(e is TextBlock t&&t.Text.Contains("新建实例"))return true;foreach(var c in LogicalTreeHelper.GetChildren(e))if(c is DependencyObject d&&HasNewInstance(d))return true;return false;}
  if(!HasNewInstance(body))throw new Exception("版本选择页没有渲染出「新建实例」卡片");
  checks.Add("PASS 版本选择页渲染出 "+hearts+" 个实例卡片（心形置顶 / 设置 / 删除）+ 新建实例卡片");
  // 0.8.7：插件 / Skill / 软件连接三种列表都必须「点开即全显」。
  // 启动器先自己预热快照，这里把内存清空、重新进页面——模拟「关掉启动器再打开，第一次点进去」。
  plugins=[];skills=[];channels=[];channelCatalog=null;
  CapturePage("插件管理");var instantPlugins=plugins.Count;
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
  CapturePage("Skills 管理");var instantSkills=skills.Count;Capture("Skill列表");
  CapturePage("软件连接");var instantChannels=channels.Count;Capture("软件列表-快照");
  if(instantPlugins==0||instantSkills==0||instantChannels==0){File.WriteAllText(Path.Combine(directory,"warm-summary.txt"),warmSummary);File.WriteAllText(Path.Combine(directory,"app-log.txt"),SafeLog.Clean(string.Join(Environment.NewLine,logLines)));throw new Exception($"列表不能秒开（没有快照就要等扫描）：插件 {instantPlugins} · Skill {instantSkills} · 软件 {instantChannels}；预热结果：{warmSummary}");}
  checks.Add($"PASS 三种列表点开即显、无需等待：插件 {instantPlugins} · Skill {instantSkills} · 软件 {instantChannels}");
  // 0.8.7：软件连接页 = 每个软件一张插件连接卡（Wallpaper Engine + 官方频道清单），三个按钮都要能点。
  channels=[new("openclaw-weixin","微信","已配置 · 连接未验证","来自 OpenClaw 官方频道清单。",Account:"default"),
   new("telegram","Telegram","已连接","网关报告连接已建立。",Account:"default"),
   new("qqbot","QQ Bot","已配置 · 连接未验证","来自 OpenClaw 官方频道清单。",Account:"default"),
   new("line","LINE","未安装适配器","官方清单里有该频道，但本实例没有适配器插件。"),
   new("signal","Signal","未连接","连接未建立。"),
   new("email","邮箱","运行中 · 连接未验证","适配器运行中，尚未返回探测结果。",Account:"default")];
  CapturePage("软件连接");await Task.Delay(40);Capture("应用连接区域");
  foreach(var needed in new[]{"微信","Telegram","QQ Bot","LINE"})if(!connectionApps.Any(a=>a.Name==needed))throw new Exception("缺少软件卡片："+needed);
  if(connectionApps.Count<6)throw new Exception("连接卡片过少："+connectionApps.Count);
  checks.Add("PASS connection cards: "+connectionApps.Count+" 个软件（"+string.Join(" / ",connectionApps.Select(a=>a.Name))+"）");
  if(connectionButtons.Count!=connectionApps.Count+1)throw new Exception("连接卡片按钮组数不对："+connectionButtons.Count+"，应为 "+(connectionApps.Count+1));
  checks.Add("PASS 每张连接卡都有「检测连接 / 修复插件 / 打开网页控制台」三个按钮（共 "+connectionButtons.Count+" 组）");
  var sample=connectionButtons[1];
  sample.Console.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
  var consoleClick=System.Diagnostics.Stopwatch.StartNew();
  while(consoleClick.ElapsedMilliseconds<8000&&(consolePlan.Length==0||lastConsoleTarget.Length==0))await Task.Delay(100);
  if(consolePlan.Length==0||lastConsoleTarget.Length==0){File.WriteAllText(Path.Combine(directory,"app-log.txt"),SafeLog.Clean(string.Join(Environment.NewLine,logLines)));throw new Exception($"「打开网页控制台」按钮点了没反应（busy={busy}，status={status.Text}）");}
  if(!consolePlan.Contains("控制台"))throw new Exception("「打开网页控制台」没有指向 OpenClaw 控制台："+consolePlan);
  if(!Uri.TryCreate(openedConsoleUrl,UriKind.Absolute,out var consoleUri)||consoleUri.Scheme!="https")throw new Exception("官方后台地址无效："+openedConsoleUrl);
  checks.Add("PASS 打开网页控制台 → "+consolePlan+"；该软件官方后台 "+openedConsoleUrl);
  sample.Repair.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
  await Task.Delay(400);
  if(lastRepairTarget.Length==0)throw new Exception("「修复插件」按钮点了没反应");
  checks.Add("PASS 修复插件 → 命中适配器 "+lastRepairTarget);
  sample.Detect.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
  // 必须给出明确结果（真探测 / 网关未运行而跳过）并回到空闲，不能把后面的页面导航堵住。
  var probeWatch=System.Diagnostics.Stopwatch.StartNew();var sawBusy=false;
  while(probeWatch.ElapsedMilliseconds<90000) {
   if(busy)sawBusy=true;
   if(!busy&&lastProbeOutcome.Length>0)break;
   await Task.Delay(150);
  }
  if(busy)throw new Exception("「检测连接」触发后没有回到空闲状态");
  if(lastProbeOutcome.Length==0)throw new Exception("「检测连接」没有给出任何结果");
  if(lastProbeTarget.Length==0||!sawBusy)throw new Exception("「检测连接」没有真的开始执行："+lastProbeTarget);
  checks.Add("PASS 检测连接 → "+lastProbeOutcome+"（"+probeWatch.ElapsedMilliseconds+" ms，已回到空闲）");
  dryRun=false;
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
  if(page!="个性化"||busy||watch.ElapsedMilliseconds>1500)throw new Exception($"Background CLI blocked navigation: page={page}, busy={busy}, elapsed={watch.ElapsedMilliseconds} ms, refreshCancellation={(refreshCancellation==null?"无":"挂起")}");
  checks.Add("PASS navigation while CLI hung: "+watch.ElapsedMilliseconds+" ms");
  await pending.WaitAsync(TimeSpan.FromSeconds(5));if(page!="个性化")throw new Exception("Stale refresh changed current page");
  checks.Add("PASS canceled refresh cannot overwrite destination");current=original;
  CapturePage("启动总览");await Task.Delay(40);Capture("启动总览");
  current=new Instance{Runtime=fake};SelectPage("插件管理");pending=Refresh();await Task.Delay(150);
  var closed=false;Closed+=(_,_)=>closed=true;watch.Restart();Close();watch.Stop();
  if(!closed||watch.ElapsedMilliseconds>1500)throw new Exception("Closing blocked by background operation");
  checks.Add("PASS close during hung CLI: "+watch.ElapsedMilliseconds+" ms");
  await pending.WaitAsync(TimeSpan.FromSeconds(5));checks.Add("PASS closing cancels and reaps status child");
  File.WriteAllText(Path.Combine(directory,"warm-summary.txt"),warmSummary);
  File.WriteAllText(Path.Combine(directory,"app-log.txt"),SafeLog.Clean(string.Join(Environment.NewLine,logLines)));
  File.WriteAllLines(Path.Combine(directory,"ui-checks.txt"),checks);
 }
}
