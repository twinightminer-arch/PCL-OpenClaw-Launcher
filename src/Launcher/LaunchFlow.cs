using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
namespace ClawLauncher;

public sealed partial class MainWindow
{
 TextBlock? launchStage;
 Action<string> openDashboardUrl=url=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
 // 本次启动的时间轴：主人要求「点启动 → 控制台出现」尽快，这里把每一段都记下来，日志里能看到真实秒数。
 internal string launchTimeline="";
 void LaunchProgress(string message) {status.Text=message;if(launchStage!=null)launchStage.Text=message;AddLog(message);}
 async Task StartGateway() {
  var sinceClick=Stopwatch.StartNew();
  // 0.8.7 修订：每次启动都把「网关就绪耗时」清零，否则复用上次的旧值会把时间轴算错。
  prepareMs=0;
  AddLog("启动网关：调用来自 "+string.Join(" ← ",new System.Diagnostics.StackTrace(1,false).GetFrames().Take(3).Select(f=>f.GetMethod()?.Name??"?")));
  var progress=new Window{Owner=this,Title="启动 OpenClaw",Width=550,Height=290,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
  launchStage=Text("正在准备启动…",17,"#D9363E");
  var details=Text("正在启动所选实例："+current.Name+"\n"+current.Runtime,12);
  var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(launchStage);panel.Children.Add(details);
  panel.Children.Add(new ProgressBar{IsIndeterminate=true,Height=5,Margin=new Thickness(0,15,0,15)});
  panel.Children.Add(Text("网关的 HTTP 端口一开始应答，控制台就会自动打开。",12));
  panel.Children.Add(Button("取消启动",()=>cancellation?.Cancel()));progress.Content=panel;
  bool completed=false;progress.Closing+=(_,_)=>{if(!completed)cancellation?.Cancel();};
  progress.Show();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
  try {
   await StartGatewayCore();
   LaunchProgress("网关端口已开始接收连接；正在打开控制台，插件仍可能在后台初始化…");
   await OpenDashboard();
   launchTimeline=$"点按→网关就绪 {prepareMs/1000.0:0.0}s + 控制台打开 {sinceClick.Elapsed.TotalSeconds-prepareMs/1000.0:0.0}s = {sinceClick.Elapsed.TotalSeconds:0.0}s";
   LaunchProgress("OpenClaw 已启动，控制台已打开（"+sinceClick.Elapsed.TotalSeconds.ToString("0.0")+" 秒）。");
  }
  catch(OperationCanceledException){
   if(owned.TryGetValue(current.Id,out var child)&&!child.HasExited){child.Kill(true);await child.WaitForExitAsync();owned.Remove(current.Id);child.Dispose();}
   throw;
  }
  finally{completed=true;launchStage=null;progress.Close();}
 }
 // 网关就绪花的毫秒数（从开始启动到 /healthz 应答），OpenDashboard 之后据此拆分耗时。
 long prepareMs;

 // 0.8.7 修订：后台预热。
 // 主人要求「点启动 → 控制台出现」的总耗时压到 10 秒内。OCL 这一侧已经压到接近 0
 // （不再为了拿网址串三次官方 CLI，每次 8 秒；也不再等 60 秒的 dashboard 命令），
 // 剩下的大头是 OpenClaw 网关自己的冷启动：CLI 引导 + 153 个插件索引 + 21 个启动插件，
 // 本机实测到 HTTP 端口应答就要 20 秒上下，插件全部就位还要再加三十多秒。
 // 所以这里换个思路：启动器一打开就在后台把所选实例的网关拉起来。用户点「启动实例」时
 // 网关多半已经就绪，点按到控制台出现就只剩「打开浏览器」那一下。
 internal bool warmup;
 internal string warmupSummary = "";

 /// <summary>启动后的后台预热总入口：先列表快照，再网关。顺序不能颠倒（同一个状态目录）。</summary>
 async Task StartBackgroundWarmup() {
  // 截图模式与界面自检模式绝不能真去拉网关：自检要的是「界面能走完」，真启动会把自检拖住，
  // 也会把用户正在用的实例搅乱（历史上就是这样把自检搞成假失败的）。
  if(screenshot||dryRun)return;
  // List discovery is demand-driven; it must not delay or compete with gateway startup.
  await WarmGateway();
 }

 async Task WarmGateway() {
  if(warmup||closing||busy||launchStage!=null||owned.Count>0)return;
  var instance=current;
  var target=ConsoleEndpoint();
  warmup=true;
  void Splash(string message){AddLog(message);Dispatcher.BeginInvoke(()=>UpdateWarmupStatus(message));}
  try {
   // 已经有网关在跑（可能是用户自己开的、或别的程序托管的），绝不插手。
   Splash("后台预热：正在检查网关是否已在运行…");
   if((await ConsoleLauncher.AlreadyRunningAsync(target.Host,target.Port,cancellation?.Token??default)).Ready) {
    warmupSummary="网关已在运行，无需预热。";
    Splash("后台预热：检测到网关已经在 "+target.Host+":"+target.Port+" 上运行，跳过。");
    return;
   }
   ClearStaleGatewayLock();
   if(closing||current.Id!=instance.Id){warmupSummary="实例已切换，预热取消。";return;}
   Splash("后台预热：正在启动「"+instance.Name+"」的 OpenClaw 网关，请稍候…");
   StartGatewayProcess();
   var wait=await ConsoleLauncher.WaitReadyAsync(target.Host,target.Port,180000,250,CancellationToken.None);
   warmupSummary=wait.Ready
    ?"已就绪（"+(wait.ElapsedMs/1000.0).ToString("0.0")+" 秒）"
    :"未在 180 秒内就绪";
   Splash(wait.Ready
    ?"后台预热完成：网关已就绪（"+warmupSummary+"）。"
    :"后台预热未完成（"+warmupSummary+"）；点「启动实例」时会继续等它。");
   if(!closing&&current.Id==instance.Id) {checkedAt=DateTime.Now;if(page=="启动总览")SelectPage(page);}
   _ = RefreshGatewayState();
  }catch(Exception error){warmupSummary="失败："+SafeLog.Clean(error.Message);Splash("后台预热失败："+SafeLog.Clean(error.Message));}
  finally{warmup=false;}
 }

 async Task OpenDashboard() {
  // 0.8.7 修订：控制台地址按官方同一套公式在本地算出来（scheme://host:port/[basePath]#token=…），
  // 不再为了拿一个网址再起一个要 8 秒的 Node 进程。只有本地拼不出来（控制台被禁用 / tailnet 绑定 /
  // 密钥引用）才回退官方 CLI——那时给用户的说明也更明确。
  var endpoint=ConsoleEndpoint();
  if(endpoint.LocalUrl) {
   // 0.8.7 修订二：探测改成「带重试的等待」并给 4 秒窗口。
   // 单次短超时探测在忙的时候会误判成「端口没应答」，用户看到的就是「控制台打不开」——
   // 而这跟网关到底有没有起来其实没关系。实测本机第一次连接就可能被拖到 2 秒以上。
   if(!(await ConsoleLauncher.WaitReadyAsync(endpoint.Host,endpoint.Port,4000,250,cancellation?.Token??default)).Ready)
    throw new Exception("网关的 HTTP 端口还没有应答，控制台暂不可用。稍等几秒再点一次「控制台」。");
   openDashboardUrl(endpoint.Url);
   return;
  }
  // 回退：官方 CLI 解析配置与认证（它返回的地址带凭据，绝不写进日志）。
  var result=await runner.Run(current,["dashboard","--json","--no-open"],45,cancellation?.Token??default);
  var data=result.Json();
  if(!result.Ok||ReadModel.B(data,"ok")!=true)throw new Exception("网关已启动，但控制台打开失败："+SafeLog.Clean(ReadModel.S(data,"reason",result.Summary)));
  var url=ReadModel.S(data,"url");
  if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))throw new Exception("OpenClaw 返回了无效的控制台地址。");
  openDashboardUrl(url);
 }

 /// <summary>从当前实例的配置文件直接算出控制台地址（零 CLI 成本）。</summary>
 internal ConsoleLauncher.ConsoleEndpoint ConsoleEndpoint() {
  try {
   var path=Runner.ConfigPath(current);
   if(File.Exists(path)&&ConsoleLauncher.FromConfig(File.ReadAllText(path),current.Port) is ConsoleLauncher.ConsoleEndpoint endpoint)return endpoint;
  } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(System.Text.Json.JsonException) {}
  return ConsoleLauncher.ProbeEndpoint("",current.Port);
 }
}
