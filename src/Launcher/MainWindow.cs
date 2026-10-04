using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
namespace ClawLauncher;

public sealed partial class MainWindow : Window
{
 readonly Store store;
 readonly Runner runner;
 readonly StackPanel body=new();
 readonly TextBlock pageTitle=new(),pageNote=new(),status=new(),gatewayBadge=new();
 readonly ComboBox instancePicker=new();
 readonly TextBox log=new() {IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),MinHeight=440};
 readonly Dictionary<string,Process> owned=new();
 readonly List<string> logLines=[];
 readonly Dictionary<string,Button> nav=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(60)};
 readonly bool screenshot;
 Instance current;
 string page="启动总览";
 bool busy, closing;
 // 分区折叠状态（红色三角按钮），跨次渲染保持，避免来回切页就重置。
 bool pluginCollapsed,skillCollapsed;
 // 插件页的分区与折叠按钮引用：供界面自检确认收起 / 展开真的生效。
 Button? pluginToggle;StackPanel? pluginSection;
 Button? skillToggle;StackPanel? skillSection;
 // 0.8.7「软件连接」页：软件卡片清单 + 每张卡的三个按钮引用（自检要点它们），自检模式下只记录动作不真执行。
 internal List<ConnectionApp> connectionApps=[];
 readonly List<(Button Detect,Button Repair,Button Console)> connectionButtons=[];
 internal bool dryRun;
 internal string openedConsoleUrl="",lastConsoleTarget="",lastRepairTarget="",lastProbeTarget="",lastProbeChannel="",lastProbeOutcome="";
 // 自检用它确认「打开网页控制台」给出的动作计划（是直接开控制台，还是先启动网关）。
 internal string consolePlan="";
 // 0.8.7 修复：网关单实例锁的复核结果。pid 被系统回收后锁会卡住，表现为「网页控制台打不开」。
 internal string lastLockReport="";
 internal int lastLockCleared;
 // 「软件连接」快照时间：非空表示当前卡片是按快照秒开的，后台正在刷新。
 DateTime? channelSnapshotAt;
 // 0.8.8：周期自动检测专用——记住上次 TCP 探测到的网关在线状态。
 // 官方 CLI 单次调用实测「8 秒起」（CLI 引导 + 153 个插件索引），定时器每 60 秒跑一次就等于
 // 每分钟抢 8 秒 CPU/IO，浏览器里正在用的 OpenClaw 控制台会跟着发涩——
 // 典型症状就是「切换对话 / 文字显示卡顿」。现在周期性只做毫秒级 TCP 探测，
 // 在线状态没变就直接收工，只有真正发生变化才去跑那次昂贵的 CLI。
 bool? lastGatewayReady;
 // 0.8.6：进页先按本地快照铺满列表（见 LoadListCache），再后台刷新校正。
 CancellationTokenSource? refreshCancellation;
 int refreshGeneration;
 readonly Dictionary<string,DateTime> refreshTimes=new();
 CancellationTokenSource? cancellation;
 JsonNode? gateway;
 List<ItemRow> plugins=[],skills=[],channels=[];
 DateTime? checkedAt;
 DataGrid? activeGrid;
 TextBox? filter;
 List<ItemRow> activeRows=[];
 public MainWindow(bool screenshot=false) {
  this.screenshot=screenshot;
  // 截图 / 界面自检 / 网关自检都是「无头验证」模式：后台预热绝不能真去拉网关
  // （会把自检拖成几分钟，也会搅乱用户正在用的实例）。这里在构造时就定下来，
  // 因为 Loaded 可能在 --ui-smoke 分支设置 dryRun 之前就跑到。
  dryRun=screenshot;
  Style=(Style)Application.Current.FindResource(typeof(Window));
  store=new Store();var resolvedNode=Runner.ResolveNode(store.Settings.Node);runner=new Runner(resolvedNode);
  // 默认（未显式配置）时把解析到的兼容 node 写回设置，保证界面与运行时一致、下次启动直接命中。
  if(string.IsNullOrWhiteSpace(store.Settings.Node)||store.Settings.Node=="node.exe"){store.Settings.Node=resolvedNode;store.Save();}
  current=store.Settings.Instances.FirstOrDefault(i=>i.Id==store.Settings.Selected)??store.Settings.Instances[0];
  Title="PCL · OpenClaw Launcher — OCL";Width=1040;Height=660;MinWidth=850;MinHeight=540;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  runner.Log+=AddLog;
  BuildShell();ApplyAppearance();SelectPage("启动总览");
  if(!screenshot)store.EnsureDesktopShortcut();
  Loaded+=async(_,_)=>{if(!screenshot){await PlayOpening();if(store.Settings.Appearance.MusicAutoPlay)PlayTrack(0);
   // 实例独立性由底层自检：同一状态目录被多个实例共用会让插件与 Skill 串台，启动时就说明白。
   foreach(var problem in store.EnsureInstanceIsolation())AddLog("实例隔离检查："+problem);
   await Refresh();
   // 0.8.7：先把插件 / Skill 快照预热好，用户第一次点开这两页就是瞬间全显；
   // 快照预热完再把网关在后台拉起来——这是「点启动 → 控制台出现」进入 10 秒的关键。
   // 两者都要读写同一个 OpenClaw 状态目录，必须串行，不能抢（抢的结果就是网关起不来）。
   _ = StartBackgroundWarmup();
   timer.Start();}};
  Closed+=(_,_)=>media.Close();
  timer.Tick+=async(_,_)=>{if(!busy&&refreshCancellation==null&&page is "启动总览" or "软件连接")await Refresh(auto:true);};
  Closing+=(_,e)=>{
   closing=true;timer.Stop();refreshCancellation?.Cancel();cancellation?.Cancel();
   foreach(var process in owned.Values)try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
  };
 }
 static SolidColorBrush Brush(string color)=>new((Color)ColorConverter.ConvertFromString(color));
 internal void CapturePage(string name){SelectPage(name);if(name=="开场动画")ShowSplashFrame();}
 static TextBlock Text(string text,double size=13,string color="#666666")=>new(){Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,9)};
 static TextBox Input(string text="",double width=300)=>new(){Text=text,Width=width,HorizontalAlignment=HorizontalAlignment.Left};
 Button Button(string text,Action action,bool primary=false) {
  var b=new Button{Content=text};if(primary){b.SetResourceReference(Control.BorderBrushProperty,"AccentBrush");b.SetResourceReference(Control.ForegroundProperty,"AccentBrush");b.FontWeight=FontWeights.SemiBold;}
  b.Click+=(_,_)=>{try{action();}catch(Exception e){Error(e);}};return b;
 }
 Button AsyncButton(string text,Func<Task> action,bool primary=false)=>Button(text,()=>_ = Operate(action,text),primary);
 static WrapPanel Row(params UIElement[] controls){var row=new WrapPanel();foreach(var c in controls)row.Children.Add(c);return row;}
 // 与输入框并排的短标签：去掉落地下边距并垂直居中，避免和输入框错位。
 static TextBlock Label(string text,double size=13){var t=Text(text,size);t.Margin=new Thickness(0,0,8,0);t.VerticalAlignment=VerticalAlignment.Center;return t;}
 // 直接浮在壁纸上的工具条/说明：半透明白底圆角，保证任何背景图下文字都可读。
 static Border Toolbar(params UIElement[] controls){var row=new WrapPanel();foreach(var c in controls)row.Children.Add(c);return new Border{Background=new SolidColorBrush(Color.FromArgb(235,255,255,255)),CornerRadius=new CornerRadius(6),Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,13),HorizontalAlignment=HorizontalAlignment.Left,Child=row};}
 void AddLog(string message) {
  if(closing)return;
  if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(()=>AddLog(message));return;}
  var text=$"[{DateTime.Now:HH:mm:ss}] {SafeLog.Clean(message)}";logLines.Add(text);if(logLines.Count>1500)logLines.RemoveRange(0,logLines.Count-1500);
  if(page=="操作日志"){log.AppendText(text+Environment.NewLine);if(log.Text.Length>200000)log.Text=string.Join(Environment.NewLine,logLines);log.ScrollToEnd();}
 }
 void Error(Exception e) {AddLog(e.Message);status.Text="操作失败，详见日志";MessageBox.Show(this,SafeLog.Clean(e.Message),"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning);}
 async Task Operate(Func<Task> action,string? label=null) {
  // 操作名与耗时都进日志：出问题时能一眼看出「卡住的是哪一步、已经跑了多久」。
  var name=label??(action.Method.DeclaringType?.Name+"."+action.Method.Name);
  if(closing)return;
  // 0.8.7 修订：以前 busy 时直接 return，用户的点击会被「静默吞掉」——
  // 按钮看起来点了没反应（「打开网页控制台」尤其致命，症状就是「控制台打不开」）。
  // 现在改成：先掐掉后台刷新，再等上一项操作结束（最多 30 秒）后照常执行。
  if(busy) {
   status.Text="上一项操作仍在进行，正在等待它结束后执行…";
   var wait=System.Diagnostics.Stopwatch.StartNew();
   while(busy&&!closing&&wait.ElapsedMilliseconds<30000)await Task.Delay(120);
   if(closing)return;
   if(busy) {status.Text="上一项操作仍未结束，请稍后再试或点击取消。";return;}
  }
  refreshCancellation?.Cancel();busy=true;instancePicker.IsEnabled=false;body.IsEnabled=false;cancellation=new();status.Text="正在处理…";
  var elapsed=System.Diagnostics.Stopwatch.StartNew();AddLog("开始操作："+name);
  try{await action();status.Text="操作完成 · "+DateTime.Now.ToString("HH:mm:ss");}catch(OperationCanceledException){if(!closing)status.Text="操作已取消";}catch(Exception e){if(!closing)Error(e);}finally{busy=false;instancePicker.IsEnabled=true;body.IsEnabled=true;cancellation.Dispose();cancellation=null;AddLog("操作结束："+name+"（"+elapsed.Elapsed.TotalSeconds.ToString("0.0")+"s）");}
 }
 bool Confirm(string text)=>MessageBox.Show(this,text,"确认操作",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK;
 async Task<CommandResult> Run(params string[] args)=>await runner.Run(current,args,120,cancellation?.Token??default);
 internal async Task<JsonNode> Json(params string[] args) {var result=await Run(args);if(!result.Ok)throw new Exception(result.Summary);return result.Json()??throw new Exception("命令未返回可识别的数据："+result.Summary);}
 async Task Execute(params string[] args){var result=await runner.Run(current,args,600,cancellation?.Token??default);AddLog(result.Summary);if(!result.Ok)throw new Exception(result.Summary);}
 JsonNode? channelCatalog;
 readonly Dictionary<string,string> pageErrors=new();
 internal string ProbeState()=>"page="+page+"; plugins="+plugins.Count+"; skills="+skills.Count+"; status="+status.Text+"; errors=["+string.Join(" | ",pageErrors.Values)+"]; node="+store.Settings.Node+"; entry="+current.Entry;
 internal async Task Refresh(bool auto=false) {  refreshCancellation?.Cancel();
  var source=new CancellationTokenSource();refreshCancellation=source;
  var token=source.Token;var generation=++refreshGeneration;var selected=current;var destination=page;
  bool Valid()=>!closing&&!token.IsCancellationRequested&&generation==refreshGeneration&&current==selected&&page==destination;
  async Task<JsonNode> Query(params string[] args){var result=await runner.Run(selected,args,150,token);token.ThrowIfCancellationRequested();if(!result.Ok)throw new Exception(result.Summary);return result.Json()??throw new Exception("命令没有返回有效 JSON。");}
  try {
   if(destination is not ("启动总览" or "软件连接" or "插件管理" or "Skills 管理" or "整合包"))return;
   status.Text="正在检测；首次扫描可能需要 1–2 分钟，可切换页面或取消…";
   pageErrors.Remove(selected.Id+destination);
   if(destination=="启动总览") {
    // 0.8.8：周期自动刷新先做毫秒级 TCP 探测；网关在线状态没变就不去跑昂贵的官方 CLI。
    if(auto) {
     var endpoint=EndpointOf(selected);
     var probe=await ConsoleLauncher.AlreadyRunningAsync(endpoint.Host,endpoint.Port,token);
     if(lastGatewayReady==probe.Ready)return;   // 状态没变 —— 不抢 CPU，也不重绘
     lastGatewayReady=probe.Ready;
    }
    var value=await Query("gateway","status","--json");if(!Valid())return;gateway=value;
   }
   // 插件 / Skill 列表由「实例设置」下的插件页、Skill 页与整合包页共用，取一次后写入本地快照。
   JsonNode? rawPlugins=null,rawSkills=null;
   if(destination is "插件管理" or "整合包"){rawPlugins=await Query("plugins","list","--json");if(!Valid())return;plugins=ReadModel.Plugins(rawPlugins);}
   if(destination is "Skills 管理" or "整合包"){rawSkills=await Query("skills","list","--json");if(!Valid())return;skills=ReadModel.Skills(rawSkills);}
   SaveListCache(selected.Id,rawPlugins,rawSkills);
   if(destination=="软件连接") {
    JsonNode? catalog=channelCatalog,live=null;var failures=new List<string>();
    try{catalog=await Query("channels","list","--all","--json");}catch(Exception e) when(e is not OperationCanceledException){failures.Add("应用列表："+SafeLog.Clean(e.Message));}
    if(!Valid())return;channelCatalog=catalog;
    // Channel RPC is its own reachability proof; an unrelated service-manager probe must not gate it.
    // 0.8.8：--probe 要真去连每一个软件（单次超时 20 秒），是这一页最贵的动作。
    // 周期自动刷新不再跑它，只在用户手动点「检测」时才跑——避免每分钟一次的长耗时抢占。
    if(!auto){try{live=await Query("channels","status","--probe","--timeout","20000","--json");}catch(Exception e) when(e is not OperationCanceledException){failures.Add("连接探测："+SafeLog.Clean(e.Message));}
    if(!Valid())return;}
    channels=ReadModel.Channels(catalog,live,live?["channelAccounts"] is JsonObject);
    SaveConnectionCache(selected.Id,catalog,live);
    if(live!=null)channelSnapshotAt=null;   // 确实拿到实时结果，才取消「快照」标记
    if(failures.Count>0)pageErrors[selected.Id+destination]=string.Join("\n",failures);
   }
   if(Valid()){checkedAt=DateTime.Now;refreshTimes[selected.Id+destination]=DateTime.Now;SelectPage(destination);status.Text=pageErrors.ContainsKey(selected.Id+destination)?"部分检测失败；请查看页面提示并重试":"状态已更新 · "+checkedAt.Value.ToString("HH:mm:ss");}
  }catch(OperationCanceledException){}catch(Exception e){if(Valid()){pageErrors[selected.Id+destination]=SafeLog.Clean(e.Message);AddLog(e.Message);SelectPage(destination);status.Text="检测失败，页面已显示原因；上次列表不代表当前状态";}}
  finally{if(refreshCancellation==source)refreshCancellation=null;source.Dispose();}
 }
 // 0.8.8：周期探测用——从实例配置里算出网关地址；算不出来就退回「回环 + 实例端口」。
 static ConsoleLauncher.ConsoleEndpoint EndpointOf(Instance instance) {
  try {
   var path=Runner.ConfigPath(instance);
   if(File.Exists(path)&&ConsoleLauncher.FromConfig(File.ReadAllText(path),instance.Port) is ConsoleLauncher.ConsoleEndpoint endpoint)return endpoint;
  } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(System.Text.Json.JsonException) {}
  return ConsoleLauncher.ProbeEndpoint("",instance.Port);
 }
 string savedSelection="",savedFilter="",savedTablePage="";
 // 列表快照落在数据目录，下次进页（甚至下次启动）先用它把列表瞬间铺满，再由后台刷新校正。
 string ListCachePath(string instanceId)=>Path.Combine(store.Root,"cache","lists-"+instanceId+".json");
 // 0.8.7：快照必须「合并」写入。插件页只查插件、Skill 页只查 Skill，若各自覆盖整份文件，
 // 就会出现「逛完插件页再进 Skill 页要等扫描、反之亦然」——这正是 0.8.6 遗留的等待来源。
 void SaveListCache(string instanceId,JsonNode? pluginJson,JsonNode? skillJson) {
  try {
   if(pluginJson?["plugins"] is not JsonNode&&skillJson?["skills"] is not JsonNode)return;
   var path=ListCachePath(instanceId);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   var root=File.Exists(path)&&JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing?existing:new JsonObject();
   if(pluginJson?["plugins"] is JsonNode p)root["plugins"]=p.DeepClone();
   if(skillJson?["skills"] is JsonNode s)root["skills"]=s.DeepClone();
   File.WriteAllText(path,root.ToJsonString());
  } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(System.Text.Json.JsonException) {}
 }
 bool LoadListCache(string instanceId) {
  try {
   var path=ListCachePath(instanceId);if(!File.Exists(path))return false;
   if(JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)return false;
   // 只补空的那一份：内存里已有（或刚刷新的）列表永不被旧快照覆盖。
   var loaded=false;
   if(root["plugins"] is JsonNode p&&plugins.Count==0){plugins=ReadModel.Plugins(new JsonObject{["plugins"]=p.DeepClone()});loaded=true;}
   if(root["skills"] is JsonNode s&&skills.Count==0){skills=ReadModel.Skills(new JsonObject{["skills"]=s.DeepClone()});loaded=true;}
   return loaded;
  } catch(IOException) {return false;} catch(UnauthorizedAccessException) {return false;} catch(InvalidOperationException) {return false;} catch(System.Text.Json.JsonException) {return false;}
 }
 // 0.8.7：软件连接页同样要先吃快照。频道清单（channels list --all）与状态（channels status）都落盘，
 // 打开页面立刻按上次结果铺满软件卡片，再由后台刷新校正——绝不让用户盯着「正在读取官方清单」。
 string ConnectionCachePath(string instanceId)=>Path.Combine(store.Root,"cache","connections-"+instanceId+".json");
 void SaveConnectionCache(string instanceId,JsonNode? catalog,JsonNode? live) {
  try {
   if(catalog==null&&live==null)return;
   var path=ConnectionCachePath(instanceId);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   var root=File.Exists(path)&&JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing?existing:new JsonObject();
   if(catalog!=null)root["catalog"]=catalog.DeepClone();
   if(live!=null)root["live"]=live.DeepClone();
   root["savedAt"]=DateTime.Now.ToString("o");
   File.WriteAllText(path,root.ToJsonString());
  } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(System.Text.Json.JsonException) {}
 }
 bool LoadConnectionCache(string instanceId) {
  try {
   var path=ConnectionCachePath(instanceId);if(!File.Exists(path))return false;
   if(JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)return false;
   if(root["catalog"] is JsonNode catalog)channelCatalog=catalog.DeepClone();
   var live=root["live"] as JsonNode;
   if(channelCatalog==null&&live==null)return false;
   // 只有快照里真的带 channelAccounts（网关当时的实时结果）才允许显示在线状态，避免旧数据冒充「已连接」。
   channels=ReadModel.Channels(channelCatalog,live,live?["channelAccounts"] is JsonObject);
   channelSnapshotAt=DateTime.TryParse(ReadModel.S(root,"savedAt"),out var at)?at:null;
   return channels.Count>0;
  } catch(IOException) {return false;} catch(UnauthorizedAccessException) {return false;} catch(InvalidOperationException) {return false;} catch(System.Text.Json.JsonException) {return false;}
 }
 // 0.8.7：启动后台预热快照 —— 让「第一次点开插件 / Skill / 软件连接页」也是瞬间全显，而不是第一次必须等扫描。
 // warmSummary 记录每一步的结果，界面自检与排障都读它（不靠猜）。
 internal string warmSummary="";
 async Task WarmListCache(bool force=false) {
  var report=new List<string>();
  try {
   // 绝不在用户正在启动 / 停止网关时抢跑：那会多出四个 CLI 子进程抢同一个状态目录，
   // 网关起不来就直接表现为「网页控制台打不开」。
   if(busy||launchStage!=null||owned.Count>0){warmSummary="网关启动中，本次跳过预热";return;}
   var instance=current;var path=ListCachePath(instance.Id);
   if(force||!File.Exists(path)||DateTime.Now-File.GetLastWriteTime(path)>=TimeSpan.FromMinutes(10)) {
    var pluginTask=runner.Run(instance,["plugins","list","--json"],300,CancellationToken.None);
    var skillTask=runner.Run(instance,["skills","list","--json"],300,CancellationToken.None);
    var results=await Task.WhenAll(pluginTask,skillTask);
    var pluginJson=results[0].Ok?results[0].Json():null;var skillJson=results[1].Ok?results[1].Json():null;
    report.Add("插件="+(pluginJson==null?"无("+SafeLog.Clean(results[0].Summary)+")":ReadModel.Plugins(pluginJson).Count.ToString()));
    report.Add("Skill="+(skillJson==null?"无("+SafeLog.Clean(results[1].Summary)+")":ReadModel.Skills(skillJson).Count.ToString()));
    if(!closing&&current.Id==instance.Id) {
     if(pluginJson!=null||skillJson!=null)SaveListCache(instance.Id,pluginJson,skillJson);
     // 用户此刻若正停在需要这两份列表的页面上，顺手把空列表补上（不动已有数据，也不打断输入）。
     if(page is "插件管理" or "Skills 管理" or "实例设置" or "整合包") {
      var filled=false;
      if(pluginJson!=null&&plugins.Count==0&&page is "插件管理" or "实例设置"){plugins=ReadModel.Plugins(pluginJson);filled=true;}
      if(skillJson!=null&&skills.Count==0&&page is "Skills 管理" or "实例设置"){skills=ReadModel.Skills(skillJson);filled=true;}
      if(filled)SelectPage(page);
     }
     report.Add("列表快照="+(File.Exists(path)?"已写入 "+new FileInfo(path).Length+" 字节":"未写入"));
    }
   } else report.Add("插件/Skill 快照仍新鲜，跳过预热");
   if(busy||launchStage!=null||owned.Count>0){report.Add("网关启动中，跳过频道预热");warmSummary=string.Join("；",report);return;}
   // 频道清单与状态也一起预热（比插件扫描快，且「软件连接」页完全依赖它）。
   var connectionPath=ConnectionCachePath(instance.Id);
   if(force||!File.Exists(connectionPath)||DateTime.Now-File.GetLastWriteTime(connectionPath)>=TimeSpan.FromMinutes(10)) {
    var catalogResult=await runner.Run(instance,["channels","list","--all","--json"],180,CancellationToken.None);
    var statusResult=await runner.Run(instance,["channels","status","--json"],120,CancellationToken.None);
    if(closing||current.Id!=instance.Id){report.Add("软件连接=实例已切换，丢弃（"+instance.Name+"→"+current.Name+"）");warmSummary=string.Join("；",report);return;}
    var catalog=catalogResult.Ok?catalogResult.Json():null;var live=statusResult.Ok?statusResult.Json():null;
    report.Add("官方频道清单="+(catalog==null?"无("+SafeLog.Clean(catalogResult.Summary)+")":"就绪"));
    if(catalog!=null||live!=null)SaveConnectionCache(instance.Id,catalog,live);
    report.Add("软件连接快照="+(File.Exists(connectionPath)?"已写入 "+new FileInfo(connectionPath).Length+" 字节":"未写入"));
    if(channels.Count==0&&LoadConnectionCache(instance.Id)&&page=="软件连接")SelectPage(page);
   } else report.Add("软件连接快照仍新鲜，跳过预热");
   AddLog("已预热列表快照（"+string.Join("；",report)+"）。");
  }catch(OperationCanceledException){report.Add("预热被取消");}catch(Exception e){report.Add("预热失败："+SafeLog.Clean(e.Message));AddLog("列表预热未完成："+SafeLog.Clean(e.Message));}
  warmSummary=string.Join("；",report);
 }
 void SelectPage(string name) {
  if(activeGrid!=null){savedSelection=(activeGrid.SelectedItem as ItemRow)?.Id??"";savedFilter=filter?.Text??"";savedTablePage=page;}
  // 0.8.7：插件 / Skill / 软件连接三种列表都先吃本地快照，做到「点开即全显」，不等后台扫描。
  if(plugins.Count==0||skills.Count==0)LoadListCache(current.Id);
  if(name=="软件连接"&&channels.Count==0)LoadConnectionCache(current.Id);
  page=name;body.IsEnabled=!busy;if(name=="操作日志")log.Text=string.Join(Environment.NewLine,logLines);pageTitle.Text=name;body.Children.Clear();activeGrid=null;filter=null;
  UpdateNavigation();
  if(pageErrors.TryGetValue(current.Id+name,out var failure))body.Children.Add(Card("检测未完成",Text(failure,12,"#D9363E"),Text("下方列表如有内容，是上次检测结果。点击右上角刷新重试。")));
  pageNote.Text=checkedAt==null?"所选实例："+current.Name:"所选实例："+current.Name+"  ·  最近检查 "+checkedAt.Value.ToString("HH:mm:ss");
  switch(name){case "启动总览":Overview();break;case "版本选择":VersionSelectionPage();break;case "插件市场":PluginMarketPage();break;case "插件管理":PluginPage();break;case "Skills 管理":SkillPage();break;case "软件连接":ChannelPage();break;case "整合包":PackPage();break;case "操作日志":LogPage();break;case "实例设置":InstanceSettingsPage();break;case "运行环境":RuntimePage();break;case "导入实例设置":ImportSettingsPage();break;case "版本下载":DownloadPage();break;case "个性化":AppearancePage();break;case "背景音乐":MusicPage();break;case "快捷方式图标":ShortcutIconPage();break;case "关于":AboutPage();break;}
  ApplyOverviewVisibility();AnimateContent();
 }
 async Task StartGatewayCore() {
  // 0.8.7 修订：主人要求「点启动 → 控制台出现」尽量短。以前这条路上串了三次官方 CLI
  // （探活一次、每轮等就绪一次、最后取地址一次），每次 8 秒起，光开销就 20 秒以上。
  // 现在只做两件事：探测 TCP 端口是否已应答、就绪就返回；CLI 只在需要诊断时兜底。
  var boot=Stopwatch.StartNew();
  Runner.EnsureGatewayConfiguration(current);
  var target=ConsoleEndpoint();
  LaunchProgress("正在检查已有网关…");
  // 端口已经在应答 = 网关本来就跑着，省掉一次 8 秒的状态命令。
  // 这里必须用「带重试的等待探测」而不是单次探测：本机实测进程里第一次连这个地址可能被
  // 安全软件/沙盒的首次连接检查拖到 2 秒以上，单次探测会误判成「没在跑」，
  // 于是去启第二个网关、撞上单实例锁——用户看到的就是「启动失败」。
  if((await ConsoleLauncher.AlreadyRunningAsync(target.Host,target.Port,cancellation?.Token??default)).Ready) {
   prepareMs=boot.ElapsedMilliseconds;
   AddLog("网关已经在运行（"+target.Host+":"+target.Port+" 已应答），用时 "+boot.Elapsed.TotalSeconds.ToString("0.0")+" 秒。");
   _ = RefreshGatewayState();
   return;
  }
  // 网关是单实例锁，pid 被系统回收后会留下永久卡死的锁（gateway run 直接报 already running 退出）。
  ClearStaleGatewayLock();
  LaunchProgress("正在启动 OpenClaw…");
  var process=StartGatewayProcess();
  // 关键：不再「等 3 秒再问一次 status」，而是每 150 毫秒戳一次 /healthz——
  // 网关一开始应答就立刻返回去开控制台，一秒都不浪费。
  // 先给 2.5 秒的快速窗口：如果是锁冲突或「已注册成系统服务」这类秒退，就地补救一次。
  var quick=await ConsoleLauncher.WaitReadyAsync(target.Host,target.Port,2500,150,cancellation?.Token??default);
  if(!quick.Ready&&process!=null&&process.HasExited) {
   // 情况一（最常见）：已经有另一个 OpenClaw 网关在同一个端口上跑着。
   // 官方这时会拒绝启动并打印「already running (pid N)」「Port N is already in use」，
   // 但那个网关本身是好的——正确做法是直接复用它，而不是把「启动失败」摔给用户看。
   var tail=string.Join("\n",logLines.TakeLast(14));
   if((await ConsoleLauncher.AlreadyRunningAsync(target.Host,target.Port,cancellation?.Token??default)).Ready) {
    var pid=RuntimeDoctor.RunningPid(tail);
    AddLog("已经有一个 OpenClaw 网关在 "+target.Host+":"+target.Port+" 上运行"+(pid>0?"（pid "+pid+"）":"")+"，直接使用它，这不是启动失败。");
    prepareMs=boot.ElapsedMilliseconds;
    checkedAt=DateTime.Now;SelectPage(page);AddLog("网关已就绪，控制台地址已就绪。");
    _ = RefreshGatewayState();
    return;
   }
   if(GatewayLock.MentionsLockConflict(tail)) {
    ClearStaleGatewayLock();
    if(lastLockCleared>0) {AddLog("检测到网关锁冲突，已清理失效锁，正在重试启动…");process=StartGatewayProcess();}
   }
   if(process!=null&&process.HasExited) {
    // 兜底：有些环境把网关注册成系统服务，此时要用服务方式启动。
    AddLog("直接启动网关没有起来，改用系统服务方式启动一次。");
    try{await Execute("gateway","start");}catch(Exception error){AddLog("服务方式启动也未成功："+SafeLog.Clean(error.Message));}
   }
  }
  var wait=await ConsoleLauncher.WaitReadyAsync(target.Host,target.Port,110000,150,cancellation?.Token??default);
  if(!wait.Ready) {
   var doctor=RuntimeDoctor.HasBuildInfo(current)||!Directory.Exists(Path.Combine(current.Runtime,"dist"))
    ?""
    :"\n运行环境：这份 OpenClaw 缺少 dist/build-info.json，每次启动都会重跑一遍启动迁移（实测多花约 50 秒）。到「运行环境」页点「体检并修复」即可补上。";
   if(process!=null&&process.HasExited) {
    var hint=GatewayLock.ExplainStartupFailure(string.Join("\n",logLines.TakeLast(14)));
    throw new Exception("网关启动后退出。请查看操作日志，确认配置、模型和依赖是否完整。"+(hint.Length>0?"\n"+hint:"")+doctor+(lastLockReport.Length>0?"\n网关锁检查："+lastLockReport:""));
   }
   var late=GatewayLock.ExplainStartupFailure(string.Join("\n",logLines.TakeLast(14)));
   throw new Exception("网关的 HTTP 端口在 110 秒内没有应答。"+(late.Length>0?late:"请查看操作日志，或稍后刷新状态。")+doctor);
  }
  prepareMs=boot.ElapsedMilliseconds;
  AddLog("网关 HTTP 端口已应答，用时 "+boot.Elapsed.TotalSeconds.ToString("0.0")+" 秒。");
  checkedAt=DateTime.Now;SelectPage(page);AddLog("网关已启动，控制台地址已就绪。");
  _ = RefreshGatewayState();
 }

 /// <summary>拉起网关子进程（带锁冲突的自动重试）。返回可能已退出的进程，供调用方判断失败原因。</summary>
 Process? StartGatewayProcess() {
  if(owned.TryGetValue(current.Id,out var existing)&&!existing.HasExited){AddLog("等待已启动的网关就绪。");return existing;}
  var instance=current;
  Process? process=runner.StartGateway(instance,line=>AddLog("["+instance.Name+"] "+line));
  owned[instance.Id]=process;
  process.Exited+=(_,_)=>AddLog("["+instance.Name+"] 网关进程已退出，请检查日志。");
  return process;
 }

 /// <summary>控制台已打开后再慢慢补一份官方状态给界面（不挡启动路径）。</summary>
 async Task RefreshGatewayState() {
  try {
   var result=await runner.Run(current,["gateway","status","--json"],60,CancellationToken.None);
   if(result.Json() is JsonNode value&&ReadModel.B(value?["rpc"],"ok")==true&&!closing) {gateway=value;SelectPage(page);}
  } catch(Exception) {}
 }
 void ClearStaleGatewayLock() {
  try {
   var (cleared,report)=GatewayLock.ClearStale();
   lastLockCleared=cleared;lastLockReport=report;
   AddLog(cleared>0?"已清理失效的网关锁："+report:"网关锁检查："+report);
  } catch(Exception error){lastLockCleared=0;lastLockReport="检查网关锁失败："+error.Message;AddLog(lastLockReport);}
 }
 async Task RepairGatewayLock() {
  ClearStaleGatewayLock();
  MessageBox.Show(this,lastLockReport,"网关锁检查",MessageBoxButton.OK,MessageBoxImage.Information);
  await Refresh();
 }

 // 0.8.7：运行环境体检——两件「不会自己好」的事：缺 build-info.json（每次启动多花约 50 秒）、
 // 残留的启动迁移租约（之后每次启动都直接失败）。自检用例读 lastDoctorReport 判断这条链路真的跑通了。
 internal string lastDoctorReport="";
 async Task RepairRuntime() {
  var parts=new List<string>();
  try {
   var (buildOk,buildReport)=await RuntimeDoctor.EnsureBuildInfo(current,runner.Node,CancellationToken.None);
   parts.Add((buildOk?"✔ ":"✘ ")+buildReport);
  } catch(Exception error){parts.Add("✘ 补齐构建标识失败："+SafeLog.Clean(error.Message));}
  try {
   var lease=await RuntimeDoctor.InspectLease(current,runner.Node,CancellationToken.None);
   parts.Add("· "+lease.Report);
   if(lease.Present) {
    var (clearOk,clearReport)=await RuntimeDoctor.ClearLease(current,runner.Node,CancellationToken.None);
    parts.Add((clearOk?"✔ ":"✘ ")+clearReport);
   }
  } catch(Exception error){parts.Add("✘ 处理启动迁移租约失败："+SafeLog.Clean(error.Message));}
  lastDoctorReport=string.Join(Environment.NewLine,parts);
  AddLog("运行环境体检："+lastDoctorReport.Replace(Environment.NewLine,"；"));
  MessageBox.Show(this,lastDoctorReport,"运行环境体检",MessageBoxButton.OK,MessageBoxImage.Information);
  await Refresh();
 }
 async Task StopGateway() {
  if(owned.TryGetValue(current.Id,out var p)&&!p.HasExited) {
   if(!Confirm("停止本实例的网关进程？正在处理的任务会中断。"))return;
   // Only terminate the exact child process held by this launcher; never kill by port/name.
   p.Kill(true);await p.WaitForExitAsync();p.Dispose();owned.Remove(current.Id);AddLog("已停止本启动器创建的网关进程。");
  }else {
   var s=await Json("gateway","status","--json");
   if(ReadModel.B(s?["service"],"loaded")==true)await Execute("gateway","stop");
   else if(ReadModel.B(s?["rpc"],"ok")==true)throw new Exception("网关由其他程序启动，且未注册系统服务。请在原启动程序中停止它。");
   else AddLog("当前未检测到可停止的网关。");
  }
  await Refresh();
 }
 // 0.8.8：版本选择独立成页（参照 PCL 版本选择页）——每个实例一张横向卡片，含置顶心形、设置、删除。
 // 设置按钮直接跳到该实例的「实例设置」页；删除会移除实例（独立实例同时删除状态目录）。
 void VersionSelectionPage() {
  pageTitle.Text="版本选择";
  pageNote.Text="选择要使用的 OpenClaw 实例；点击心形置顶，设置进入该实例的独立设置，删除会移除该实例（独立实例同时删除状态目录）。";
  var ordered=store.Settings.Instances.OrderByDescending(i=>i.Pinned).ThenBy(i=>store.Settings.Instances.IndexOf(i)).ToList();
  var wrap=new WrapPanel{Margin=new Thickness(0,0,0,4)};
  foreach(var item in ordered)wrap.Children.Add(InstanceCard(item));
  wrap.Children.Add(NewInstanceCard());
  body.Children.Add(Card("实例",wrap,Text("每个实例对应一份独立的 OpenClaw 版本与配置；心形置顶的实例排在最前。",11,"#999999")));
  // 保留「切换当前实例版本目录」能力（不丢功能）。
  var runtime=Input(current.Runtime,540);
  var known=store.Settings.Instances.Select(i=>i.Runtime).ToList();var versionsFolder=Path.Combine(store.Root,"versions");
  if(Directory.Exists(versionsFolder))known.AddRange(Directory.GetDirectories(versionsFolder).Where(d=>!Path.GetFileName(d).StartsWith(".")&&File.Exists(Path.Combine(d,"ocl-install.json"))).Select(d=>Path.Combine(d,"node_modules","openclaw")).Where(d=>File.Exists(Path.Combine(d,"openclaw.mjs"))));
  var library=new ComboBox{Width=540,HorizontalAlignment=HorizontalAlignment.Left,ItemsSource=known.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),SelectedItem=current.Runtime};
  library.SelectionChanged+=(_,_)=>{if(library.SelectedItem is string path)runtime.Text=path;};
  body.Children.Add(Card("当前实例版本目录",library,Text("选择已登记的版本后，在下方应用；新下载的版本会保存在本机版本库。")));
  body.Children.Add(Card("切换当前实例到所选目录",runtime,Row(Button("浏览目录",()=>{var dialog=new OpenFolderDialog();if(dialog.ShowDialog(this)==true)runtime.Text=dialog.FolderName;}),AsyncButton("应用到当前实例",async()=>{
   if(owned.TryGetValue(current.Id,out var process)&&!process.HasExited)throw new Exception("请先停止当前网关，再切换版本。");
   if(!File.Exists(Path.Combine(runtime.Text,"openclaw.mjs")))throw new Exception("该目录没有 openclaw.mjs。");
   var check=await Json("gateway","status","--json");if(ReadModel.B(check?["rpc"],"ok")==true)throw new Exception("请先停止当前网关，再切换版本。");
   if(!Confirm("将当前实例切换到：\n"+runtime.Text+"\n旧版本可能不兼容现有数据，建议用新实例测试。"))return;
   current.Runtime=Path.GetFullPath(runtime.Text);store.Save();SelectPage(page);
  }))));
  body.Children.Add(Card("下载版本",Text("从完整版本列表选择正式版、预览版或历史版本，自动下载并创建实例。"),Button("前往自动安装",()=>Navigate("版本下载"),true)));
 }
 // 0.8.8：单实例横向卡片——心形置顶 + 设置（跳该实例实例设置）+ 删除。
 UIElement InstanceCard(Instance item) {
  var dock=new DockPanel{LastChildFill=true};
  var heart=Button(item.Pinned?"♥":"♡",()=>{item.Pinned=!item.Pinned;store.Save();SelectPage("版本选择");});
  heart.Width=34;heart.Height=34;heart.FontSize=20;heart.Margin=new Thickness(0,0,10,0);heart.BorderThickness=new Thickness(0);heart.Background=Brushes.Transparent;heart.Foreground=Brush(item.Pinned?"#E0314B":"#BBBBBB");heart.ToolTip="置顶实例";DockPanel.SetDock(heart,Dock.Left);dock.Children.Add(heart);
  var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
  var settings=Button("⚙",()=>{current=item;channelCatalog=null;store.Settings.Selected=item.Id;store.Save();gateway=null;plugins=[];skills=[];channels=[];checkedAt=null;instancePicker.Items.Refresh();instancePicker.SelectedItem=item;SelectPage("实例设置");});settings.Width=34;settings.Height=34;settings.FontSize=16;settings.Margin=new Thickness(0,0,6,0);settings.ToolTip="设置（进入该实例的独立设置）";
  var del=Button("✕",()=>{
   if(owned.TryGetValue(item.Id,out var p)&&!p.HasExited){Error(new Exception("请先停止该实例的网关，再删除。"));return;}
   if(!Confirm("确定删除实例「"+item.Name+"」？\n"+(item.Managed?"其独立状态目录也会被一并删除。":"该实例为已有安装，仅从列表移除。")))return;
   var wasCurrent=current.Id==item.Id;store.Delete(item);
   if(wasCurrent&&store.Settings.Instances.Count>0)current=store.Settings.Instances[0];
   instancePicker.Items.Refresh();instancePicker.SelectedItem=current;gateway=null;plugins=[];skills=[];channels=[];checkedAt=null;SelectPage("版本选择");AddLog("已删除实例 "+item.Name);
  });del.Width=34;del.Height=34;del.FontSize=15;del.ToolTip="删除实例";del.Foreground=Brush("#D9363E");
  actions.Children.Add(settings);actions.Children.Add(del);DockPanel.SetDock(actions,Dock.Right);dock.Children.Add(actions);
  var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,8,0)};
  info.Children.Add(Text(item.Name,14,item.Pinned?"#C8323C":"#333333"));info.Children.Add(Text(item.Version+"  ·  "+(item.Managed?"独立实例":"已有安装"),11,"#999999"));
  dock.Children.Add(info);
  return Card("",dock);
 }
 UIElement NewInstanceCard() {
  var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.Hand};stack.Children.Add(Text("＋ 新建实例",14,"#1E6FE8"));stack.Children.Add(Text("创建独立配置与工作目录",11,"#999999"));
  stack.MouseLeftButtonDown+=(_,_)=>OpenCreateInstanceDialog();
  return Card("",stack);
 }
 void OpenCreateInstanceDialog() {
  var dialog=new Window{Owner=this,Title="新建 OpenClaw 实例",Width=560,Height=320,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
  var dock=new DockPanel{Margin=new Thickness(20)};
  var ok=Button("创建实例",()=>dialog.DialogResult=true,true);DockPanel.SetDock(ok,Dock.Bottom);dock.Children.Add(ok);
  var stack=new StackPanel();
  var name=Input("新的 OpenClaw 实例",280);var port=Input((store.Settings.Instances.Max(i=>i.Port)+1).ToString(),100);
  var runtime=Input(current.Runtime,540);
  var known=store.Settings.Instances.Select(i=>i.Runtime).ToList();var versionsFolder=Path.Combine(store.Root,"versions");
  if(Directory.Exists(versionsFolder))known.AddRange(Directory.GetDirectories(versionsFolder).Where(d=>!Path.GetFileName(d).StartsWith(".")&&File.Exists(Path.Combine(d,"ocl-install.json"))).Select(d=>Path.Combine(d,"node_modules","openclaw")).Where(d=>File.Exists(Path.Combine(d,"openclaw.mjs"))));
  var library=new ComboBox{Width=540,HorizontalAlignment=HorizontalAlignment.Left,ItemsSource=known.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),SelectedItem=current.Runtime};
  library.SelectionChanged+=(_,_)=>{if(library.SelectedItem is string path)runtime.Text=path;};
  stack.Children.Add(Text("实例名称",12));stack.Children.Add(name);
  stack.Children.Add(Text("网关端口（1024–65535，自动避开已用端口）",12));stack.Children.Add(port);
  stack.Children.Add(Text("OpenClaw 版本目录",12));stack.Children.Add(library);stack.Children.Add(runtime);
  dock.Children.Add(stack);dialog.Content=dock;
  if(dialog.ShowDialog()!=true)return;
  if(!int.TryParse(port.Text,out var number))throw new Exception("端口必须是数字。");
  if(!File.Exists(Path.Combine(runtime.Text,"openclaw.mjs")))throw new Exception("请先选择有效的版本目录。");
  var added=store.Create(name.Text,runtime.Text,number,store.Settings.DefaultPlugins);current=added;channelCatalog=null;instancePicker.Items.Refresh();instancePicker.SelectedItem=added;gateway=null;plugins=[];skills=[];channels=[];checkedAt=null;SelectPage("版本选择");AddLog(store.Settings.DefaultPlugins.Count>0?"已按默认插件集创建实例（"+store.Settings.DefaultPlugins.Count+" 项）。":"已创建实例；默认插件集为空，未携带任何插件。");
 }
 DataGrid Table(List<ItemRow> rows,bool account=false) {
  activeRows=rows;filter=Input("",360);filter.ToolTip="按名称、状态或说明筛选";filter.TextChanged+=(_,_)=>{if(activeGrid!=null)activeGrid.ItemsSource=activeRows.Where(r=>(r.Name+" "+r.State+" "+r.Detail).Contains(filter.Text,StringComparison.OrdinalIgnoreCase)).ToList();};
  body.Children.Add(Toolbar(Label("筛选"),filter));
  var table=new DataGrid{Height=280,ItemsSource=rows};
  table.Columns.Add(new DataGridTextColumn{Header="名称",Binding=new Binding("Name"),Width=new DataGridLength(150)});
  table.Columns.Add(new DataGridTextColumn{Header="状态",Binding=new Binding("State"),Width=new DataGridLength(160)});
  if(account)table.Columns.Add(new DataGridTextColumn{Header="账号",Binding=new Binding("Account"),Width=new DataGridLength(100)});
  table.Columns.Add(new DataGridTextColumn{Header=account?"说明":"来源",Binding=new Binding(account?"Detail":"Source"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
  var detail=Text(rows.Count==0?"尚未加载列表，请点击刷新状态。":"选择条目查看详情。",12);
  table.SelectionChanged+=(_,_)=>{if(table.SelectedItem is ItemRow r)detail.Text=SafeLog.Clean(r.Name+"\n"+r.Detail);};
  body.Children.Add(Card("条目列表 · "+rows.Count,table,detail));activeGrid=table;
  if(savedTablePage==page){filter.Text=savedFilter;table.SelectedItem=rows.FirstOrDefault(r=>r.Id==savedSelection);}
  return table;
 }
 ItemRow Selected()=>activeGrid?.SelectedItem as ItemRow??throw new Exception("请先在列表中选择一个条目。");
 // 0.8.6：分区收起 / 展开，图标是红色上 / 下三角（仿 PCL 折叠分区）。
 Button CollapseToggle(string label,Func<bool> get,Action<bool> set,UIElement target) {
  Button? toggle=null;
  toggle=Button(get()?"展开 ▼":"收起 ▲",()=>{var next=!get();set(next);if(toggle!=null){toggle.Content=next?"展开 ▼":"收起 ▲";}target.Visibility=next?Visibility.Collapsed:Visibility.Visible;});
  toggle.Foreground=Brush("#D9363E");toggle.BorderThickness=new Thickness(0);toggle.Background=Brushes.Transparent;toggle.Padding=new Thickness(5,2,5,2);toggle.Margin=new Thickness(0,0,10,0);toggle.ToolTip="收起 / 展开「"+label+"」列表";
  target.Visibility=get()?Visibility.Collapsed:Visibility.Visible;
  return toggle;
 }
 // 0.8.6：插件市场（下载页，面向全部实例）——每行只给一个红色加号，点了选实例再装。
 void PluginMarketPage() {
  pageTitle.Text="插件市场";
  pageNote.Text="在这里搜索并安装插件；插件市场为所有实例服务，装上哪个实例由你选择。已装插件请到「实例设置 → 插件」管理。";
  var query=Input("",360);query.ToolTip="关键词，例如 obsidian、telegram、wallpaper";
  var limit=new ComboBox{Width=90,ItemsSource=new[]{"20","40","60"},SelectedIndex=0};
  var panel=new StackPanel();
  List<MarketItem> results=[];
  body.Children.Add(Card("搜索插件",Text("结果来自 OpenClaw 的插件仓库（ClawHub）。点每行右侧的红色「＋」选择要装到哪个实例。",12),Toolbar(Label("关键词"),query,limit,AsyncButton("搜索",async()=>{await MarketSearch(query.Text.Trim(),limit.Text);RenderMarket();}))));
  body.Children.Add(panel);
  void RenderMarket() {
   panel.Children.Clear();
   if(results.Count==0){panel.Children.Add(Toolbar(Text("还没有搜索结果：输入关键词后点「搜索」。",12)));return;}
   foreach(var item in results)panel.Children.Add(MarketRow(item));
  }
  RenderMarket();
  async Task MarketSearch(string text,string count) {
   if(text.Length==0)throw new Exception("请输入搜索关键词。");
   var json=await Json("plugins","search",text,"--json","--limit",count);
   results=(json["results"] as JsonArray??[]).Where(n=>n!=null).Select(n=> {
    var pkg=n?["package"]??n;
    return new MarketItem(ReadModel.S(pkg,"name"),ReadModel.S(pkg,"displayName",ReadModel.S(pkg,"name")),
     ReadModel.S(pkg,"ownerHandle"),ReadModel.S(pkg,"summary"),ReadModel.S(pkg,"latestVersion"),
     ReadModel.S(ReadModel.Node(pkg,"stats"),"downloads"),ReadModel.B(pkg,"isOfficial")==true);
   }).Where(i=>i.Id.Length>0).ToList();
   if(results.Count==0)AddLog("没有搜到插件："+text);
  }
 }
 UIElement MarketRow(MarketItem item) {
  var grid=new Grid();
  grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
  var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
  info.Children.Add(new TextBlock{Text=item.Display+(item.Official?"  ·  官方":""),FontSize=13,Foreground=Brush("#333333")});
  info.Children.Add(new TextBlock{Text=(item.Owner.Length>0?item.Owner+"  ·  ":"")+"v"+item.Version+(item.Downloads.Length>0?"  ·  下载 "+item.Downloads:""),FontSize=11,Foreground=Brush("#999999"),Margin=new Thickness(0,2,0,0)});
  if(item.Summary.Length>0)info.Children.Add(new TextBlock{Text=SafeLog.Clean(item.Summary),FontSize=11,Foreground=Brush("#888888"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,0)});
  Grid.SetColumn(info,0);grid.Children.Add(info);
  // 红色加号：市场里的条目不给开关，只负责「添加到某个实例」。
  var add=Button("＋",()=>AddPluginToInstance(item));
  add.Foreground=Brush("#D9363E");add.FontWeight=FontWeights.Bold;add.FontSize=15;add.Padding=new Thickness(9,1,9,3);add.Margin=new Thickness(12,0,0,0);add.ToolTip="添加到所选实例";
  Grid.SetColumn(add,1);grid.Children.Add(add);
  return new Border{CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1),BorderBrush=Brush("#ECECEC"),Background=Brush("#FBFBFB"),Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,0,0,5),Child=grid};
 }
 void AddPluginToInstance(MarketItem item) {
  var targets=store.Settings.Instances.ToList();
  if(targets.Count==0)throw new Exception("还没有实例。");
  var dialog=new Window{Owner=this,Title="添加插件："+item.Display,Width=520,Height=360,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var dock=new DockPanel{Margin=new Thickness(20)};
  var ok=Button("添加到所选实例",()=>dialog.DialogResult=true,true);DockPanel.SetDock(ok,Dock.Bottom);dock.Children.Add(ok);
  var stack=new StackPanel();
  stack.Children.Add(Text("选择要安装到的实例（可多选）：",12));
  var list=new ListBox{SelectionMode=SelectionMode.Extended,Height=180,Margin=new Thickness(0,0,0,10)};
  foreach(var instance in targets)list.Items.Add(new ListBoxItem{Content=instance.Name+(instance==current?"（当前）":""),Tag=instance.Id});
  if(current!=null)foreach(ListBoxItem it in list.Items)if((string?)it.Tag==current.Id)it.IsSelected=true;
  stack.Children.Add(list);
  stack.Children.Add(Text("外部来源需要 OpenClaw 确认；安装结果以命令输出为准。",11,"#999999"));
  dock.Children.Add(stack);dialog.Content=dock;
  if(dialog.ShowDialog()!=true)return;
  var chosen=list.SelectedItems.Cast<ListBoxItem>().Select(i=>(string?)i.Tag??"").Where(id=>id.Length>0).ToList();
  if(chosen.Count==0)throw new Exception("请至少选择一个实例。");
  _ = Operate(async()=> {
   foreach(var id in chosen) {
    var instance=store.Settings.Instances.FirstOrDefault(i=>i.Id==id);if(instance==null)continue;
    // ClawHub 来源需要显式确认信任警告，非 ClawHub 来源用 --force 覆盖同名安装。
    var result=await runner.Run(instance,["plugins","install",item.Id,"--acknowledge-clawhub-risk","--force"],600,cancellation?.Token??default);
    AddLog((result.Ok?"已安装到 ":"安装失败（")+instance.Name+(result.Ok?"":"）："+SafeLog.Clean(result.Summary)));
   }
   await Refresh();
  });
 }
 void PluginPage() {
  pageTitle.Text="插件";
  int bundled=plugins.Count(p=>p.Source=="bundled"),custom=plugins.Count(p=>p.Source is not "" and not "bundled");
  pageNote.Text=$"共 {plugins.Count} 个插件 · 官方内置 {bundled} · 自定义/外部 {custom}。启用、禁用、删除仅对当前实例「{current.Name}」生效；自定义插件装到实例 extensions 目录后即出现在此。";
  var filterBox=Input("",360);filterBox.ToolTip="按名称、状态或来源筛选";
  var scope=new ComboBox{Width=140,ItemsSource=new[]{"全部","已启用","已禁用","官方内置","自定义/外部"},SelectedIndex=0};
  var section=new StackPanel();section.Children.Add(Toolbar(Label("筛选"),filterBox,scope));
  var panel=new StackPanel();section.Children.Add(panel);
  if(plugins.Count==0&&!busy&&refreshCancellation==null)_ = Refresh();
  pluginSection=section;pluginToggle=CollapseToggle("插件",()=>pluginCollapsed,v=>pluginCollapsed=v,section);
  body.Children.Add(Card("插件（本实例）",Row(pluginToggle,Button("打开插件目录",OpenPluginFolder),AsyncButton("插件诊断",async()=>{await Execute("plugins","doctor");SelectPage("操作日志");}))));
  body.Children.Add(section);
  List<ItemRow> Filtered() {
   var q=filterBox.Text.Trim();var s=scope.SelectedIndex;
   return plugins.Where(r=>(q.Length==0||(r.Name+" "+r.State+" "+r.Source+" "+r.Detail).Contains(q,StringComparison.OrdinalIgnoreCase)))
    .Where(r=>s==0||(s==1&&r.Enabled)||(s==2&&!r.Enabled)||(s==3&&r.Source=="bundled")||(s==4&&r.Source is not "" and not "bundled")).ToList();
  }
  void Render(){panel.Children.Clear();var rows=Filtered();foreach(var r in rows)panel.Children.Add(PluginRow(r));if(rows.Count==0)panel.Children.Add(Toolbar(Text(plugins.Count==0?(refreshCancellation!=null?"正在加载插件列表；首次扫描可能需要 1–2 分钟…":"尚未加载列表：请点击右上角 ↻ 刷新，或稍候自动刷新。"):"没有匹配的插件。",12)));}
  filterBox.TextChanged+=(_,_)=>Render();scope.SelectionChanged+=(_,_)=>Render();
  Render();
  var spec=Input("",450);spec.ToolTip="例如 @openclaw/telegram 或本地插件目录";
  var trust=new CheckBox{Content="信任此外部来源，并允许替换同名安装",Margin=new Thickness(0,0,0,12)};
  body.Children.Add(Card("安装插件（官方 / 外部）",Text("输入 OpenClaw 官方包名、npm 引用或本地目录，安装到当前实例；结果以 OpenClaw 实际输出为准。",12),trust,Row(spec,AsyncButton("安装",async()=>{
   if(string.IsNullOrWhiteSpace(spec.Text))throw new Exception("请填写插件来源。");if(!Confirm("在 "+current.Name+" 安装插件：\n"+spec.Text+(trust.IsChecked==true?"\n已允许执行此外部来源并替换同名安装。":"")))return;
   var args=new List<string>{"plugins","install",spec.Text.Trim()};if(trust.IsChecked==true)args.Add("--force");await Execute(args.ToArray());await Refresh();
  },true),Button("本地目录",()=>{var dialog=new OpenFolderDialog();if(dialog.ShowDialog(this)==true)spec.Text=dialog.FolderName;}))));
 }
 // PCL「mods 管理」风格横向行：指示灯 + 名称/状态/来源 ‖ 开关 + 删除（0.8.4）
 UIElement PluginRow(ItemRow row) {
  var grid=new Grid();
  grid.ColumnDefinitions.Add(new(){Width=new GridLength(20)});
  grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
  var light=new System.Windows.Shapes.Ellipse{Width=9,Height=9,Fill=Brush(row.Indicator),VerticalAlignment=VerticalAlignment.Center,ToolTip=row.State};
  Grid.SetColumn(light,0);grid.Children.Add(light);
  var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
  var name=new TextBlock{Text=row.Name,FontSize=13,Foreground=Brush("#333333"),VerticalAlignment=VerticalAlignment.Center};
  var meta=new TextBlock{Text=row.State+(row.Source.Length>0?"  ·  "+row.Source:""),FontSize=11,Foreground=Brush(row.Indicator),Margin=new Thickness(0,2,0,0)};
  info.Children.Add(name);info.Children.Add(meta);
  if(row.Detail.Length>0)info.ToolTip=SafeLog.Clean(row.Detail);
  Grid.SetColumn(info,1);grid.Children.Add(info);
  var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
  // 开关：直接调 OpenClaw CLI 写配置（已实测写入 plugins.entries.<id>.enabled）。
  actions.Children.Add(SwitchWithLabel(row.Enabled,enabled=>_ = Operate(async()=>{
   await Execute("plugins",enabled?"enable":"disable",row.Id);
   AddLog("插件 "+row.Name+" 已"+(enabled?"启用":"禁用")+"并写入配置；如网关正在运行，需重启网关后生效。");
   await Refresh();
  })));
  var del=Button("删除",()=>_ = Operate(()=>UninstallPlugin(row)));del.Margin=new Thickness(12,0,0,0);
  actions.Children.Add(del);
  Grid.SetColumn(actions,2);grid.Children.Add(actions);
  return new Border{CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1),BorderBrush=Brush("#ECECEC"),Background=Brush("#FBFBFB"),Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,0,0,5),Child=grid};
 }
 // PCL「资源包管理」风格横向行（0.8.5）：与插件行同一套版式——指示灯 + 名称/状态/来源 ‖ 开关 + 删除。
 UIElement SkillRow(ItemRow row) {
  var grid=new Grid();
  grid.ColumnDefinitions.Add(new(){Width=new GridLength(20)});
  grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
  var light=new System.Windows.Shapes.Ellipse{Width=9,Height=9,Fill=Brush(row.Indicator),VerticalAlignment=VerticalAlignment.Center,ToolTip=row.State};
  Grid.SetColumn(light,0);grid.Children.Add(light);
  var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
  var name=new TextBlock{Text=row.Name,FontSize=13,Foreground=Brush("#333333"),VerticalAlignment=VerticalAlignment.Center};
  var meta=new TextBlock{Text=row.State+(row.Source.Length>0?"  ·  "+row.Source:""),FontSize=11,Foreground=Brush(row.Indicator),Margin=new Thickness(0,2,0,0)};
  info.Children.Add(name);info.Children.Add(meta);
  if(row.Detail.Length>0)info.ToolTip=SafeLog.Clean(row.Detail);
  Grid.SetColumn(info,1);grid.Children.Add(info);
  var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
  // 开关：写实例配置 skills.entries.<key>.enabled（已实测写入生效，网关需重启后应用）。
  actions.Children.Add(SwitchWithLabel(row.Enabled,enabled=>_ = Operate(async()=>{
   await SetSkill(row.Id,enabled);
   AddLog("Skill "+row.Name+" 已"+(enabled?"启用":"禁用")+"并写入配置；如网关正在运行，需重启网关后生效。");
   await Refresh();
  })));
  var del=Button("删除",()=>_ = Operate(()=>UninstallSkill(row)));del.Margin=new Thickness(12,0,0,0);
  actions.Children.Add(del);
  Grid.SetColumn(actions,2);grid.Children.Add(actions);
  return new Border{CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1),BorderBrush=Brush("#ECECEC"),Background=Brush("#FBFBFB"),Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,0,0,5),Child=grid};
 }
 // 卸载插件：先让 OpenClaw 卸载（对 plugins install 装进来的有效）；
 // 失败则兜底——手动放入/全局扩展没有安装记录，OpenClaw 会拒绝，这里直接移除目录并清理配置。
 async Task UninstallPlugin(ItemRow row) {
  if(row.Source=="bundled")throw new Exception("内置插件随 OpenClaw 更新，无法删除；如不需要请使用「禁用」。");
  var path=FindPluginDirectory(row.Id,row.RootDir);
  var where=path==null?"（未定位到插件目录，将只清理配置）":"\n将移除目录："+path+"\n普通目录会移到启动器回收目录，可手动移回恢复；连接点只删链接。";
  if(!Confirm("从当前实例卸载插件 "+row.Name+"？"+where+"\n\n先由 OpenClaw 卸载；若它仍在列表中（手动放入的插件没有安装记录、或全局扩展仍被发现），会补一步移除目录并清理配置。"))return;
  var result=await Run("plugins","uninstall",row.Id,"--force");
  AddLog(result.Ok?("OpenClaw 已卸载插件 "+row.Name):("OpenClaw 拒绝卸载「"+row.Name+"」（"+SafeLog.Clean(result.Summary)+"）"));
  await Refresh();
  // 关键复查：无论 CLI 是否报成功，只要该插件仍出现在本实例列表里
  // （常见原因：手动放入扩展目录没有安装记录 → 拒绝卸载；或目录仍在 extensions 被自动发现），
  // 就兜底移除插件目录并清理配置，保证「删除」一定生效。
  if(!plugins.Any(p=>p.Id==row.Id))return;
  AddLog("插件仍在列表中，兜底移除插件目录并清理配置。");
  RemovePluginDirectory(row.Id,row.RootDir);
  RemovePluginEntry(row.Id);
  await Refresh();
 }
 // 移除插件目录：优先用 OpenClaw 报告的 rootDir（全局插件会指向 ~/.openclaw/extensions），否则按顺序猜。
 // 连接点/junction（0.8.2 起的自动挂载）只删链接本身，不动源目录；普通目录移入回收目录。
 string? FindPluginDirectory(string id,string? rootDir) {
  var candidates=new List<string>();
  if(!string.IsNullOrWhiteSpace(rootDir))candidates.Add(rootDir);
  candidates.Add(Path.Combine(InstanceStateRoot(),"extensions",id));
  candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw","extensions",id));
  foreach(var dir in candidates)if(!string.IsNullOrWhiteSpace(dir)&&Directory.Exists(dir))return dir;
  return null;
 }
 void RemovePluginDirectory(string id,string? rootDir) {
  var dir=FindPluginDirectory(id,rootDir);
  if(dir==null) { AddLog("未找到插件目录，已跳过文件移除："+id); return; }
  if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0){Directory.Delete(dir,false);AddLog("已移除插件链接 "+dir);}
  else {
   var trash=Path.Combine(store.Root,"removed-plugins",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+id);
   Directory.CreateDirectory(Path.GetDirectoryName(trash)!);Directory.Move(dir,trash);AddLog("插件已移到 "+trash+"（可手动移回恢复）；原位置 "+dir);
  }
 }
 // 从实例配置里删掉该插件的 entries 项（找不到就跳过，不影响其他配置）。
 void RemovePluginEntry(string id) {
  var path=current.Config.Length>0?current.Config:Path.Combine(InstanceStateRoot(),"openclaw.json");
  if(!File.Exists(path))return;
  if(JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)return;
  if(root["plugins"]?["entries"] is JsonObject entries&&entries.ContainsKey(id)) {
   entries.Remove(id);File.WriteAllText(path,root.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));AddLog("已从配置移除插件项 "+id);
  }
 }
 void SkillPage() {
  pageTitle.Text="Skill";
  int bundled=skills.Count(s=>s.Source.Contains("bundled")),custom=skills.Count(s=>s.Source is not "" && !s.Source.Contains("bundled"));
  pageNote.Text=$"共 {skills.Count} 个 Skill · 官方内置 {bundled} · 自定义/插件附带 {custom}。启用、禁用、删除仅对当前实例「{current.Name}」生效。";
  var filterBox=Input("",360);filterBox.ToolTip="按名称、状态或来源筛选";
  var scope=new ComboBox{Width=140,ItemsSource=new[]{"全部","已启用","已禁用","官方内置","自定义/外部"},SelectedIndex=0};
  var section=new StackPanel();section.Children.Add(Toolbar(Label("筛选"),filterBox,scope));
  var panel=new StackPanel();section.Children.Add(panel);
  if(skills.Count==0&&!busy&&refreshCancellation==null)_ = Refresh();
  skillSection=section;skillToggle=CollapseToggle("Skill",()=>skillCollapsed,v=>skillCollapsed=v,section);
  body.Children.Add(Card("Skill（本实例）",Row(skillToggle,Button("打开 Skills 目录",OpenSkillsFolder),AsyncButton("依赖检查",async()=>{await Execute("skills","check");SelectPage("操作日志");}))));
  body.Children.Add(section);
  List<ItemRow> Filtered() {
   var q=filterBox.Text.Trim();var s=scope.SelectedIndex;
   return skills.Where(r=>(q.Length==0||(r.Name+" "+r.State+" "+r.Source+" "+r.Detail).Contains(q,StringComparison.OrdinalIgnoreCase)))
    .Where(r=>s==0||(s==1&&r.Enabled)||(s==2&&!r.Enabled)||(s==3&&r.Source.Contains("bundled"))||(s==4&&r.Source is not "" && !r.Source.Contains("bundled"))).ToList();
  }
  void Render(){panel.Children.Clear();var rows=Filtered();foreach(var r in rows)panel.Children.Add(SkillRow(r));if(rows.Count==0)panel.Children.Add(Toolbar(Text(skills.Count==0?(refreshCancellation!=null?"正在加载 Skill 列表；首次扫描可能需要 1–2 分钟…":"尚未加载列表：请点击右上角 ↻ 刷新，或稍候自动刷新。"):"没有匹配的 Skill。",12)));}
  filterBox.TextChanged+=(_,_)=>Render();scope.SelectionChanged+=(_,_)=>Render();
  Render();
  var spec=Input("",460);spec.ToolTip="@owner/slug 或含 SKILL.md 的本地目录";
  body.Children.Add(Card("安装 Skill",Text("可填写 ClawHub 的 @owner/slug，或选择包含 SKILL.md 的本地文件夹，安装到当前实例。",12),Row(spec,AsyncButton("安装",async()=>{
   if(string.IsNullOrWhiteSpace(spec.Text))throw new Exception("请填写 Skill 来源。");if(!Confirm("安装 Skill：\n"+spec.Text))return;await Execute("skills","install",spec.Text.Trim());await Refresh();
  },true),Button("本地目录",()=>{var dialog=new OpenFolderDialog();if(dialog.ShowDialog(this)==true)spec.Text=dialog.FolderName;}))));
 }
 async Task SetSkill(string name,bool enabled) {
  var info=await Json("skills","info",name,"--json");if(info["error"]!=null)throw new Exception(info["error"]!.ToString());
  var key=ReadModel.S(info,"skillKey",name);
  await Execute("config","set","skills.entries["+JsonSerializer.Serialize(key)+"].enabled",enabled?"true":"false","--strict-json");
 }
 // 删除 Skill：只允许删除「独立安装」的 Skill（工作区 skills 目录或共享 managed skills 目录）；
 // 内置、插件附带、个人 agent 目录或链接目录都会明确拒绝——这类请用开关禁用，或去卸载所属插件。
 async Task UninstallSkill(ItemRow row) {
  var info=await Json("skills","info",row.Id,"--json");
  if(info["error"]!=null)throw new Exception(info["error"]!.ToString());
  var source=ReadModel.S(info,"source",row.Source);
  var path=ReadModel.S(info,"filePath");
  var dir=row.RootDir.Length>0?row.RootDir:(path.Length>0?Path.GetDirectoryName(Path.GetFullPath(path))??"":"");
  if(ReadModel.B(info,"bundled")==true||source=="openclaw-bundled")throw new Exception("内置 Skill 随 OpenClaw 更新，不能删除；不需要就点开关「禁用」。");
  if(source=="openclaw-extra")throw new Exception("「"+row.Name+"」由插件提供，请在上面的「插件（本实例）」里禁用或删除所属插件。");
  if(dir.Length==0||!Directory.Exists(dir))throw new Exception("没有定位到 Skill 目录，无法删除；可改用开关禁用。");
  var list=await Json("skills","list","--json");
  var full=Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
  var roots=new[]{ReadModel.S(list,"managedSkillsDir"),Path.Combine(ReadModel.S(list,"workspaceDir"),"skills")}
   .Where(r=>!string.IsNullOrWhiteSpace(r)).Select(r=>Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar)).ToList();
  if(!roots.Any(r=>string.Equals(full,r,StringComparison.OrdinalIgnoreCase)))throw new Exception("只允许删除工作区或共享 Skills 目录下的独立 Skill（当前来源："+source+"）；其余请用开关禁用。");
  if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new Exception("该 Skill 是链接目录，请在来源处管理或用开关禁用。");
  if(!Confirm("将 Skill「"+row.Name+"」移到启动器回收目录：\n"+dir+"\n可手动移回原目录恢复。"))return;
  var trash=Path.Combine(store.Root,"removed-skills",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+row.Id);
  Directory.CreateDirectory(Path.GetDirectoryName(trash)!);Directory.Move(dir,trash);
  AddLog("Skill 已移到 "+trash+"（可手动移回恢复）；原位置 "+dir);
  await Refresh();
  if(skills.Any(s=>s.Id==row.Id))AddLog("Skill 仍在列表中：若它同时被插件或内置目录提供，请在插件里禁用。");
 }
 void ChannelPage() {
  pageTitle.Text="软件连接";
  pageNote.Text="所有软件都通过 OpenClaw 插件接入：这里直接读 OpenClaw 官方频道清单，把能连接与已连接的软件逐张列出来。绿：已连接，黄：待验证/未配置，红：异常，灰：未装适配器插件。";
  pageNote.TextTrimming=TextTrimming.CharacterEllipsis;
  // 0.8.7：卡片先按上次读取的官方清单秒开，后台刷新校正——需要等待的只有「检测连接」这种主动探测。
  if(channelSnapshotAt!=null)pageNote.Text="显示上次读取的官方清单（"+channelSnapshotAt.Value.ToString("HH:mm")+"），后台正在刷新；"+pageNote.Text;
  // 0.8.7：组装软件列表——官方频道清单（channels list --all）× 适配器插件（plugins list 的 channelIds）。
  connectionApps=ConnectionCatalog.Apps(plugins,channels);
  var search=Input("",350);search.ToolTip="按软件名、账号或状态筛选";
  body.Children.Add(SectionTitle("插件链接"));
  body.Children.Add(Toolbar(Label("查找软件"),search,Button("检测全部",()=>{if(!busy)_ = Refresh();}),Button("配置账号（向导）",()=>OpenWizard(["channels","add"]))));
  var cards=new StackPanel();body.Children.Add(cards);
  void Render() {
   cards.Children.Clear();connectionButtons.Clear();
   // Wallpaper Engine 是纯插件型连接（没有聊天频道），保留在最前面当模板。
   cards.Children.Add(WallpaperConnectionCard());
   var query=search.Text.Trim();
   var list=connectionApps.Where(a=>query.Length==0||(a.Name+" "+a.Accounts+" "+a.State+" "+a.Id+" "+a.PluginPackage).Contains(query,StringComparison.OrdinalIgnoreCase)).ToList();
   foreach(var app in list)cards.Children.Add(ConnectionCard(app));
   if(list.Count==0)cards.Children.Add(Toolbar(Text(connectionApps.Count==0?(refreshCancellation!=null?"正在读取 OpenClaw 官方频道清单…":"尚未加载：点右上角 ↻ 或「检测全部」读取官方清单。"):"没有匹配的软件。",12)));
  }
  search.TextChanged+=(_,_)=>Render();Render();
 }
 // 0.8.7：每个软件一张卡（与 Wallpaper Engine 同构）：状态灯 + 三个按钮——检测连接 / 修复插件 / 打开网页控制台。
 UIElement ConnectionCard(ConnectionApp app) {
  var title=new StackPanel{Orientation=Orientation.Horizontal};title.Children.Add(Text(app.Name,16,"#444444"));
  title.Children.Add(new System.Windows.Shapes.Ellipse{Width=9,Height=9,Fill=Brush(app.Indicator),Margin=new Thickness(9,0,0,8),VerticalAlignment=VerticalAlignment.Center,ToolTip=app.State});
  var detect=AsyncButton("检测连接",()=>ProbeConnection(app));
  var repair=AsyncButton("修复插件",()=>RepairAdapter(app));
  var console=AsyncButton("打开网页控制台",()=>OpenConsole(app));
  console.ToolTip="打开 OpenClaw 控制台（网关未运行会先启动）；"+app.Name+" 官方后台："+app.ConsoleUrl;
  connectionButtons.Add((detect,repair,console));
  return Card("",title,Text(app.Summary,13,app.Indicator),Text(app.Detail.Length>0?app.Detail:"状态来自 OpenClaw 官方设置；账号与凭据都在 OpenClaw 里，启动器不保存任何密钥。",11),Row(detect,repair,console));
 }
 // 检测连接：跑官方 channels status --probe，拿真实探测结果刷新这张卡。
 async Task ProbeConnection(ConnectionApp app) {
  lastProbeTarget=app.Id;   // 自检据此确认按钮真的点到了这个软件
  var target=channels.FirstOrDefault(r=>r.Id==app.Id&&r.Account.Length>0)??channels.FirstOrDefault(r=>r.Id==app.Id);
  if(target==null) {await Json("channels","status","--channel",app.Id,"--probe","--timeout","20000","--json");lastProbeOutcome="官方探测完成："+app.Id;return;}
  // 网关没起来时，官方探测会先重试连接约 40 秒才报「网关不可达」；先确认一次网关，给用户一句明白话，别让他干等。
  if(ReadModel.B(gateway?["rpc"],"ok")!=true) {
   var check=await runner.Run(current,["gateway","status","--json"],60,cancellation?.Token??default);
   if(check.Json() is JsonNode fresh)gateway=fresh;
  }
  if(ReadModel.B(gateway?["rpc"],"ok")!=true) {
   lastProbeOutcome="网关未运行，已跳过探测（"+app.Name+"）";
   AddLog("「"+app.Name+"」连接检测已跳过：网关未运行。先启动网关，再点检测连接。");
   if(!dryRun)throw new Exception("网关未运行：先到「启动总览」启动 OpenClaw 网关，然后再检测「"+app.Name+"」的连接。");
   return;
  }
  await ProbeChannel(target);
 }
 // 修复插件：把该软件的适配器插件修成本实例可用——没启用就启用；装都没装就从 ClawHub 找官方适配器装。
 async Task RepairAdapter(ConnectionApp app) {
  if(dryRun) {AddLog("（自检）修复插件将执行："+(app.AdapterInstalled?(app.PluginEnabled?"重新启用 ":"启用 ")+app.PluginId:"从 ClawHub 安装 "+app.Id+" 适配器"));lastRepairTarget=app.Id;return;}
  await Operate(async()=>{
   if(app.AdapterInstalled) {
    if(app.AdapterReady){AddLog("适配器 "+app.PluginPackage+" 已安装并启用，无需修复。");}
    else {await Execute("plugins","enable",app.PluginId);AddLog("已启用适配器 "+app.PluginPackage+"；如网关正在运行，重启网关后生效。");}
   } else {
    var search=await Json("plugins","search",app.Id,"--json","--limit","10");
    var match=ConnectionCatalog.BestAdapter(app.Id,search);
    if(match==null){AddLog("ClawHub 没有搜到「"+app.Name+"」的适配器插件；请在插件市场里按关键词挑选安装。");Navigate("插件市场");return;}
    var result=await runner.Run(current,["plugins","install",match.Id,"--acknowledge-clawhub-risk","--force"],600,cancellation?.Token??default);
    AddLog((result.Ok?"已安装适配器 ":"安装适配器失败（")+match.Id+(result.Ok?"）":"）："+SafeLog.Clean(result.Summary)));
    if(!result.Ok)throw new Exception(result.Summary);
   }
   plugins=[];skills=[];await Refresh();
  });
 }
 // 打开网页控制台 = 打开 OpenClaw 本地控制台（与 Wallpaper Engine 那张卡一致）。
 // 网关没起来时**先启动网关再打开**：旧写法直接调 dashboard，网关没跑就要等满 60 秒才报
 // 「Gateway is not running」——用户看到的就是「网页控制台打不开」。
 // 该软件自己的官方后台地址放在按钮提示与日志里，不抢这个按钮的位置。
 async Task OpenConsole(ConnectionApp app)=>await OpenConsoleFor(app.Name,app.ConsoleUrl,app.Id);
 async Task OpenConsoleFor(string label,string officialUrl,string target) {
  consolePlan=(ReadModel.B(gateway?["rpc"],"ok")==true?"打开 OpenClaw 控制台":"先启动网关，再打开 OpenClaw 控制台")+"（"+label+"）";
  lastConsoleTarget=target;openedConsoleUrl=officialUrl;
  if(dryRun){AddLog("（自检）打开网页控制台："+consolePlan+(officialUrl.Length>0?"；官方后台 "+officialUrl:""));return;}
  await Operate(async()=>{
   if(ReadModel.B(gateway?["rpc"],"ok")!=true) {
    AddLog("网关未运行：先启动网关，随后自动打开控制台。");
    await StartGateway();   // 内含启动进度窗口，结束时自己会调用 OpenDashboard
   } else await OpenDashboard();
   AddLog(label+"：控制台已打开"+(officialUrl.Length>0?"；该软件的官方后台是 "+officialUrl:"")+"。");
  });
 }
 async Task ProbeChannel(ItemRow row) {
  var selected=current;
  lastProbeTarget=row.Id;   // 自检用它确认「检测连接」真的点到了官方探测命令
  try {
   var value=await Json("channels","status","--channel",row.Id,"--probe","--timeout","20000","--json");
   lastProbeChannel=row.Id;   // 官方探测真的跑完了（自检据此判定「检测连接」不是空按钮）
   if(current!=selected)return;
   var next=ReadModel.Channels(channelCatalog,value,value["channelAccounts"] is JsonObject).Where(r=>r.Id==row.Id).ToList();
   channels.RemoveAll(r=>r.Id==row.Id);channels.AddRange(next.Count>0?next:[row with {State="未验证",Detail="网关未返回该应用的运行数据。"}]);
  }catch(Exception e) when(e is not OperationCanceledException){lastProbeChannel=row.Id;channels=channels.Select(r=>r.Id==row.Id?r with {State="检测失败",Detail=SafeLog.Clean(e.Message)}:r).ToList();}
  if(page=="软件连接")SelectPage(page);
  lastProbeOutcome="官方探测完成："+row.Id+" → "+(channels.FirstOrDefault(r=>r.Id==row.Id)?.State??"未知");
 }
 UIElement WallpaperConnectionCard() {
  var title=new StackPanel{Orientation=Orientation.Horizontal};title.Children.Add(Text("Wallpaper Engine",16,"#444444"));
  var label=Text("点击检测本地壁纸库与网页组件",12);var light=new System.Windows.Shapes.Ellipse{Width=9,Height=9,Fill=Brush("#999999"),Margin=new Thickness(9,0,0,8),VerticalAlignment=VerticalAlignment.Center};title.Children.Add(light);
  var console=AsyncButton("打开网页控制台",()=>OpenConsoleFor("Wallpaper Engine","https://www.wallpaperengine.io/","wallpaper-engine"));
  console.ToolTip="打开 OpenClaw 控制台（网关未运行会先启动）；Wallpaper Engine 官网：https://www.wallpaperengine.io/";
  var detect=Button("检测连接",()=>{
   try{var configPath=current.Config.Length>0?current.Config:Path.Combine(current.State.Length>0?current.State:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw"),"openclaw.json");
    var cfg=JsonNode.Parse(File.ReadAllText(configPath));var plugin=cfg?["plugins"]?["entries"]?["wallpaper-engine"];
    var library=ReadModel.S(plugin?["config"],"libraryRoot");var ui=ReadModel.S(plugin?["config"],"controlUiRoot");
    if(ui.Length==0)ui=Store.ResolveControlUiRoot(current.Runtime); // 配置未写时按运行目录兜底
    var count=Directory.Exists(library)?Directory.EnumerateDirectories(library).Count(d=>File.Exists(Path.Combine(d,"project.json"))):0;
    var ready=ReadModel.B(plugin,"enabled")==true&&count>0&&File.Exists(Path.Combine(ui,"wallpaper","wallpaper.js"));
    light.Fill=Brush(ready?"#248B69":"#D9363E");
    label.Text=ready?$"本地库可用 · {count} 项 · 网页组件已安装；在控制台点击“壁纸”测试网关连接。":(plugin==null?"本实例未安装 Wallpaper Engine 插件，点击「修复插件」即可为本实例安装并启用。":"尚未就绪：请检查插件启用状态、壁纸库目录与网页组件；必要时点击「修复插件」。");
   }catch(Exception e){light.Fill=Brush("#D9363E");label.Text=SafeLog.Clean(e.Message);}
  });
  connectionButtons.Add((detect,Button("修复插件",()=>_ = RepairWallpaper()),console));
  return Card("",title,label,Row(detect,Button("修复插件",()=>_ = RepairWallpaper()),console));
 }
 // 为当前实例安装/修复 Wallpaper Engine 插件（连接或复制到受管实例扩展目录并写配置），随后跳到插件管理刷新。
 async Task RepairWallpaper() {
  await Operate(async()=>{
   store.LinkWallpaperPlugin(current);
   AddLog("已为实例「"+current.Name+"」修复 Wallpaper Engine 插件（连接/复制并写入配置）。");
   Navigate("插件管理");
  });
 }
 void OpenWizard(string[] args) {
  if(!Confirm("将打开 OpenClaw 的交互配置窗口。完成后回到启动器刷新状态。\n实例："+current.Name))return;
  // Encoded PowerShell avoids shell interpolation of user paths, and supports interactive CLI prompts.
  static string Quote(string value)=>"'"+value.Replace("'","''")+"'";
  var lines=new List<string>{"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8"};
  foreach(var key in new[]{"OPENCLAW_PROFILE","OPENCLAW_STATE_DIR","OPENCLAW_CONFIG_PATH"})lines.Add("Remove-Item Env:"+key+" -ErrorAction SilentlyContinue");
  if(current.State.Length>0)lines.Add("$env:OPENCLAW_STATE_DIR="+Quote(current.State));if(current.Config.Length>0)lines.Add("$env:OPENCLAW_CONFIG_PATH="+Quote(current.Config));
  lines.Add("Set-Location -LiteralPath "+Quote(current.Runtime));lines.Add("& "+Quote(runner.Node)+" "+Quote(current.Entry)+" "+string.Join(" ",args.Select(Quote)));
  var info=new ProcessStartInfo("powershell.exe"){UseShellExecute=true};info.ArgumentList.Add("-NoExit");info.ArgumentList.Add("-EncodedCommand");info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(string.Join("\n",lines))));Process.Start(info);
 }
 void PackPage() {
  pageTitle.Text="整合包";
  pageNote.Text="把本实例的版本、插件与 Skill 打包成一份可审查的清单；账号凭据不随整合包导出。";
  body.Children.Add(Card("导出当前整合包",Text("保存当前实例「"+current.Name+"」的 OpenClaw 版本、插件及 Skill 启停状态。能确定来源的扩展会记录安装引用；不能自动还原的扩展会列为待补充项。"),Row(AsyncButton("导出 .clawpack.json",ExportPack,true),AsyncButton("预览可导出的整合包",PreviewExportPack))));
  body.Children.Add(Card("整合包的边界",Text("这是依赖与启停清单，不包含聊天记录、令牌、模型密钥或本地 Skill 文件。需要本地文件的项目会明确列出，不会显示为已恢复。")));
 }
 async Task ExportPack() {
  var pluginJson=await Json("plugins","list","--json");var skillJson=await Json("skills","list","--json");
  var pluginRows=ReadModel.Plugins(pluginJson);var skillRows=ReadModel.Skills(skillJson);
  var pack=await BuildPack(pluginRows,skillRows);
  var dialog=new SaveFileDialog{Filter="OpenClaw 整合包|*.clawpack.json",FileName="OpenClaw.clawpack.json"};if(dialog.ShowDialog(this)!=true)return;
  File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(pack,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));AddLog("已导出整合包；待补充项 "+pack.Unresolved.Count);MessageBox.Show(this,"整合包已导出。\n待补充来源的项目："+pack.Unresolved.Count,"导出完成");
 }
 // 0.8.6：先勾选要带走的插件 / Skill，再导出——让人一眼看清「装了哪些、要带走哪些」。
 async Task PreviewExportPack() {
  if(plugins.Count==0||skills.Count==0) {if(LoadListCache(current.Id)&&(plugins.Count==0||skills.Count==0))await Refresh();}
  var dialog=new Window{Owner=this,Title="预览可导出的整合包 · "+current.Name,Width=720,Height=620,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var dock=new DockPanel{Margin=new Thickness(20)};
  var actions=new StackPanel{Orientation=Orientation.Horizontal};
  var export=Button("导出选中项",()=>dialog.DialogResult=true,true);var all=Button("全选",()=>{});var none=Button("全不选",()=>{});
  actions.Children.Add(new Border{Margin=new Thickness(0,0,10,0),Child=export});actions.Children.Add(new Border{Margin=new Thickness(0,0,10,0),Child=all});actions.Children.Add(none);
  DockPanel.SetDock(actions,Dock.Bottom);dock.Children.Add(actions);
  var listStack=new StackPanel();
  var stack=new ScrollViewer{Content=listStack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  listStack.Children.Add(Text("勾选要写进整合包的条目（默认只带已启用的）。待补充来源的条目仍会列入清单的「待补充项」。",12));
  var pluginBoxes=new List<(CheckBox Box,ItemRow Row)>();
  listStack.Children.Add(new TextBlock{Text="插件 · "+plugins.Count+" 个",FontSize=13,Margin=new Thickness(0,10,0,4),Foreground=Brush("#555555")});
  foreach(var row in plugins){var box=new CheckBox{Content=row.Name+(row.Source=="bundled"?"（内置）":"（"+row.Source+"）")+(row.Enabled?"":" · 已禁用"),IsChecked=row.Enabled,Margin=new Thickness(0,0,0,3)};pluginBoxes.Add((box,row));listStack.Children.Add(box);}
  var skillBoxes=new List<(CheckBox Box,ItemRow Row)>();
  listStack.Children.Add(new TextBlock{Text="Skill · "+skills.Count+" 个",FontSize=13,Margin=new Thickness(0,12,0,4),Foreground=Brush("#555555")});
  foreach(var row in skills){var box=new CheckBox{Content=row.Name+(row.Source.Length>0?"（"+row.Source+"）":"")+(row.Enabled?"":" · 已禁用"),IsChecked=row.Enabled,Margin=new Thickness(0,0,0,3)};skillBoxes.Add((box,row));listStack.Children.Add(box);}
  all.Click+=(_,_)=>{foreach(var (box,_) in pluginBoxes)box.IsChecked=true;foreach(var (box,_) in skillBoxes)box.IsChecked=true;};
  none.Click+=(_,_)=>{foreach(var (box,_) in pluginBoxes)box.IsChecked=false;foreach(var (box,_) in skillBoxes)box.IsChecked=false;};
  dock.Children.Add(stack);dialog.Content=dock;
  if(dialog.ShowDialog()!=true)return;
  var chosenPlugins=pluginBoxes.Where(t=>t.Box.IsChecked==true).Select(t=>t.Row).ToList();
  var chosenSkills=skillBoxes.Where(t=>t.Box.IsChecked==true).Select(t=>t.Row).ToList();
  if(chosenPlugins.Count==0&&chosenSkills.Count==0)throw new Exception("没有勾选任何条目。");
  var pack=await BuildPack(chosenPlugins,chosenSkills);
  var save=new SaveFileDialog{Filter="OpenClaw 整合包|*.clawpack.json",FileName=current.Name+".clawpack.json"};if(save.ShowDialog(this)!=true)return;
  File.WriteAllText(save.FileName,JsonSerializer.Serialize(pack,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  AddLog("已导出整合包（插件 "+chosenPlugins.Count+" · Skill "+chosenSkills.Count+"）；待补充项 "+pack.Unresolved.Count);
  MessageBox.Show(this,"整合包已导出。\n条目：插件 "+chosenPlugins.Count+" 个 · Skill "+chosenSkills.Count+" 个\n待补充来源："+pack.Unresolved.Count+" 项","导出完成");
 }
 // 把（勾选过的）插件与 Skill 行整理成整合包：能确定来源的记安装引用，其余列入待补充项。
 async Task<Pack> BuildPack(IEnumerable<ItemRow> pluginRows,IEnumerable<ItemRow> skillRows) {
  var installsResult=await Run("config","get","plugins.installs","--json");var installs=installsResult.Ok?installsResult.Json():null;
  var pack=new Pack{Name=current.Name,Version=current.Version};
  foreach(var p in pluginRows) {
   var id=p.Id;var bundled=p.Source=="bundled";var spec=ReadModel.S(installs?[id],"spec");
   if(!bundled&&!Pack.IsPortableSpec(spec)){pack.Unresolved.Add("plugin: "+id+"（缺少可移植安装来源）");continue;}
   pack.Entries.Add(new(id,bundled?null:spec,"plugin",p.Enabled));
  }
  foreach(var s in skillRows) {
   var id=s.Id;if(!s.Source.Contains("bundled")){pack.Unresolved.Add("skill: "+id+"（请在清单中补充 @owner/slug，或在导入后手动安装）");continue;}
   pack.Entries.Add(new(id,null,"skill",s.Enabled));
  }
  return pack;
 }
 void LogPage() {
  body.Children.Add(Row(Button("导出日志",()=>{var dialog=new SaveFileDialog{Filter="文本日志|*.txt",FileName="OpenClaw-launcher-log.txt"};if(dialog.ShowDialog(this)==true)File.WriteAllText(dialog.FileName,SafeLog.Clean(string.Join(Environment.NewLine,logLines)));}),Button("清空显示",()=>{logLines.Clear();log.Clear();})));
  body.Children.Add(Toolbar(Text("日志已过滤常见令牌字段；分享前仍请检查个人路径及第三方插件输出。",12)));body.Children.Add(log);
 }
 void InstanceSettingsPage() {
  var instanceName=Input(current.Name,280);
  body.Children.Add(Card("实例名称与版本",Text("每个实例对应一个 OpenClaw 版本；把实例改名后再创建，即可让同一版本并存多个实例（如同 PCL 的多个存档）。"),Row(instanceName,Button("重命名",()=>{store.Rename(current,instanceName.Text);instancePicker.Items.Refresh();AddLog("实例已重命名为 "+current.Name);SelectPage(page);}),Button("复制实例",()=>{var clone=store.Duplicate(current,current.Name+" 副本");current=clone;channelCatalog=null;instancePicker.Items.Refresh();instancePicker.SelectedItem=clone;gateway=null;plugins=[];skills=[];channels=[];checkedAt=null;SelectPage(page);AddLog("已复制实例 "+clone.Name);})),Text("当前版本："+current.Version+"  ·  "+(current.Managed?"独立实例（独立配置与工作目录）":"已有安装（使用所选配置）"),12)));
  body.Children.Add(Toolbar(Text("左侧「实例」栏里的插件、Skill、整合包与运行环境都只作用于当前实例「"+current.Name+"」；切换实例即切换该实例自己的一套。",12)));
  body.Children.Add(Card("新实例默认插件集",Text("下载/创建新实例时只携带这里的“必要插件”，其余由用户自行安装或制作。以当前实例为模板采集最省事。"),Row(AsyncButton("照搬当前实例",CaptureDefaultPlugins,true),Button("清空默认集",()=>{store.Settings.DefaultPlugins=[];store.Save();SelectPage(page);})),Text(store.Settings.DefaultPlugins.Count==0?"尚未采集默认插件集：新实例将不携带任何插件。":"已采集 "+store.Settings.DefaultPlugins.Count+" 个默认插件："+string.Join("、",store.Settings.DefaultPlugins.Take(8))+(store.Settings.DefaultPlugins.Count>8?" …":""),11,"#999999")));
  body.Children.Add(Card("实例状态目录",Text("本实例状态目录："+InstanceStateRoot()+"\n插件与 Skill 只从这里与实例配置加载；启动器在底层保证每个实例各用一套，不需要手动检查。",12),Row(Button("打开状态目录",()=>OpenFolder(InstanceStateRoot())),Button("运行环境与配置",()=>Navigate("运行环境")))));
 }
 // 0.8.6：运行环境与配置独立成页（左侧导航「运行环境」）。
 void RuntimePage() {
  var node=Input(store.Settings.Node,510);var state=Input(current.State,510);var config=Input(current.Config,510);
  pageTitle.Text="运行环境";
  body.Children.Add(Card("运行环境与配置",Text("Node.js 可执行文件"),node,Text("OpenClaw 状态目录（留空使用默认目录）"),state,Text("配置文件路径（留空使用默认规则；不是程序目录里的任意 JSON）"),config,Row(Button("选择配置文件",()=>{var dialog=new OpenFileDialog{Filter="配置文件|*.json;*.json5|所有文件|*.*"};if(dialog.ShowDialog(this)==true)config.Text=dialog.FileName;}),Button("保存",()=>{
   if(owned.TryGetValue(current.Id,out var p)&&!p.HasExited)throw new Exception("请先停止该实例网关，再修改运行设置。");
   RuntimeInstall.ResolveExecutable(node.Text.Trim());if(config.Text.Length>0&&!File.Exists(config.Text))throw new Exception("配置文件不存在。");if(state.Text.Length>0&&!Directory.Exists(state.Text))throw new Exception("状态目录不存在。");
   store.Settings.Node=node.Text.Trim();runner.Node=store.Settings.Node;current.State=state.Text.Trim();current.Config=config.Text.Trim();store.Save();gateway=null;plugins=[];skills=[];checkedAt=null;
   foreach(var problem in store.EnsureInstanceIsolation())AddLog("实例隔离检查："+problem);
   AddLog("设置已保存。");
  }),Button("打开配置向导",()=>OpenWizard(["configure"])),AsyncButton("验证配置",async()=>{await Execute("config","validate");MessageBox.Show(this,"配置验证通过。","检查完成");}))));
 }
 // 0.8.6：导入实例设置独立成页（左侧导航「导入实例设置」）。
 void ImportSettingsPage() {
  pageTitle.Text="导入实例设置";
  var others=store.Settings.Instances.Where(i=>i!=current).ToList();
  var summary=others.Count==0?"当前只有一个实例，没有其他实例可导入。"
   :string.Join("\n",others.Select(i=>"·  "+i.Name+"（"+(i.Managed?"独立实例":"已有安装")+"）"));
  body.Children.Add(Card("从其他实例导入设置",Text("把来源实例的插件启停与插件配置、Skill 启停搬到当前实例；端口、令牌与账号凭据不会被复制。\n\n可导入的实例：\n"+summary,12),
   Row(AsyncButton("从其他实例导入设置",ImportInstanceSettings,true))));
 }
// 一键把其他实例的插件/Skill 启停与配置导入当前实例；端口、令牌与账号凭据不复制。
 async Task ImportInstanceSettings() {
  var others=store.Settings.Instances.Where(i=>i!=current).ToList();
  if(others.Count==0)throw new Exception("当前只有一个实例，没有其他实例可导入。");
  string ConfigPathOf(Instance i)=>i.Config.Length>0?i.Config:Path.Combine(i.State.Length>0?i.State:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw"),"openclaw.json");
  var dialog=new Window{Owner=this,Title="从其他实例导入设置",Width=580,Height=330,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var dock=new DockPanel{Margin=new Thickness(20)};
  var ok=Button("导入到「"+current.Name+"」",()=>dialog.DialogResult=true,true);DockPanel.SetDock(ok,Dock.Bottom);dock.Children.Add(ok);
  var stack=new StackPanel();
  var combo=new ComboBox{ItemsSource=others.Select(i=>i.Name).ToList(),SelectedIndex=0,Margin=new Thickness(0,0,0,10)};
  var copyPlugins=new CheckBox{Content="插件启停与插件配置",IsChecked=true,Margin=new Thickness(0,0,0,6)};
  var copySkills=new CheckBox{Content="Skill 启停",IsChecked=true,Margin=new Thickness(0,0,0,6)};
  var installMissing=new CheckBox{Content="按来源记录补装本实例缺少的插件",IsChecked=false,Margin=new Thickness(0,0,0,6)};
  stack.Children.Add(Text("选择来源实例：",12));stack.Children.Add(combo);
  stack.Children.Add(Text("导入内容（端口、令牌与账号凭据不会被复制）：",12));
  stack.Children.Add(copyPlugins);stack.Children.Add(copySkills);stack.Children.Add(installMissing);
  dock.Children.Add(stack);dialog.Content=dock;
  if(dialog.ShowDialog()!=true)return;
  var source=others[Math.Max(0,combo.SelectedIndex)];
  var srcPath=ConfigPathOf(source);
  if(!File.Exists(srcPath))throw new Exception("来源实例的配置文件不存在："+srcPath);
  var dstPath=ConfigPathOf(current);
  var src=JsonNode.Parse(File.ReadAllText(srcPath)) as JsonObject??throw new Exception("来源配置无法解析。");
  var dst=File.Exists(dstPath)?(JsonNode.Parse(File.ReadAllText(dstPath)) as JsonObject??new JsonObject()):new JsonObject();
  Directory.CreateDirectory(Path.GetDirectoryName(dstPath)!);
  int pluginCount=0,skillCount=0;
  if(copyPlugins.IsChecked==true&&(src["plugins"] as JsonObject)?["entries"] is JsonObject srcEntries) {
   var dstPlugins=dst["plugins"] as JsonObject??new JsonObject();
   var dstEntries=dstPlugins["entries"] as JsonObject??new JsonObject();
   foreach(var pair in srcEntries)if(pair.Value is JsonNode node){dstEntries[pair.Key]=node.DeepClone();pluginCount++;}
   dstPlugins["entries"]=dstEntries;dst["plugins"]=dstPlugins;
  }
  if(copySkills.IsChecked==true&&(src["skills"] as JsonObject)?["entries"] is JsonObject srcSkills) {
   var dstSkills=dst["skills"] as JsonObject??new JsonObject();
   var dstSkillEntries=dstSkills["entries"] as JsonObject??new JsonObject();
   foreach(var pair in srcSkills)if(pair.Value is JsonNode node){dstSkillEntries[pair.Key]=node.DeepClone();skillCount++;}
   dstSkills["entries"]=dstSkillEntries;dst["skills"]=dstSkills;
  }
  File.WriteAllText(dstPath,dst.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
  AddLog("已从实例「"+source.Name+"」导入设置：插件项 "+pluginCount+" · Skill 项 "+skillCount+" → "+dstPath);
  var failures=new List<string>();
  if(installMissing.IsChecked==true&&(src["plugins"] as JsonObject)?["installs"] is JsonObject installs) {
   var have=(await Json("plugins","list","--json"))["plugins"] as JsonArray??new JsonArray();
   var ids=have.Select(p=>ReadModel.S(p,"id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
   foreach(var pair in installs) {
    var spec=ReadModel.S(pair.Value,"spec");if(spec.Length==0||ids.Contains(pair.Key))continue;
    try {await Execute("plugins","install",spec,"--force");} catch(Exception e){failures.Add(pair.Key+"："+SafeLog.Clean(e.Message));}
   }
  }
  await Refresh();
  MessageBox.Show(this,"已导入插件项 "+pluginCount+" 个、Skill 项 "+skillCount+" 个。"+(failures.Count>0?"\n补装失败："+string.Join("；",failures):"")+"\n如网关正在运行，需重启网关后生效。","导入完成");
 }
 static void OpenLink(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
 void OpenDonate() {
  var page=Path.Combine(AppContext.BaseDirectory,"Assets","donate","index.html");
  if(File.Exists(page))OpenLink(new Uri(page).AbsoluteUri);
  else OpenLink("https://github.com/twinightminer-arch/PCL-OpenClaw-Launcher");
 }
 string InstanceStateRoot()=>current.State.Length>0?current.State:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw");
 void OpenFolder(string path){try{Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo("explorer.exe",path){UseShellExecute=true});}catch(Exception e){Error(e);}}
 void OpenPluginFolder()=>OpenFolder(Path.Combine(InstanceStateRoot(),"extensions"));
 void OpenSkillsFolder(){var root=InstanceStateRoot();var managed=Path.Combine(root,"skills");var workspace=Path.Combine(root,"workspace","skills");OpenFolder(Directory.Exists(managed)?managed:workspace);}
 async Task CaptureDefaultPlugins() {
  var json=await Json("plugins","list","--json");
  var ids=(json["plugins"] as JsonArray??[]).Where(p=>p!=null&&ReadModel.B(p,"enabled")==true).Select(p=>ReadModel.S(p,"id")).Where(id=>id.Length>0).Distinct().ToList();
  store.Settings.DefaultPlugins=ids;store.Save();
  MessageBox.Show(this,"已将当前实例启用的 "+ids.Count+" 个插件采集为新实例默认集。","采集完成");
  SelectPage(page);
 }
}
