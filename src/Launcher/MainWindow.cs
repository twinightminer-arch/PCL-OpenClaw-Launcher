using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
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
  Style=(Style)Application.Current.FindResource(typeof(Window));
  store=new Store();var resolvedNode=Runner.ResolveNode(store.Settings.Node);runner=new Runner(resolvedNode);
  // 默认（未显式配置）时把解析到的兼容 node 写回设置，保证界面与运行时一致、下次启动直接命中。
  if(string.IsNullOrWhiteSpace(store.Settings.Node)||store.Settings.Node=="node.exe"){store.Settings.Node=resolvedNode;store.Save();}
  current=store.Settings.Instances.FirstOrDefault(i=>i.Id==store.Settings.Selected)??store.Settings.Instances[0];
  Title="PCL · OpenClaw Launcher — OCL";Width=1040;Height=660;MinWidth=850;MinHeight=540;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  runner.Log+=AddLog;
  BuildShell();ApplyAppearance();SelectPage("启动总览");
  if(!screenshot)store.EnsureDesktopShortcut();
  Loaded+=async(_,_)=>{if(!screenshot){await PlayOpening();if(store.Settings.Appearance.MusicAutoPlay)PlayTrack(0);await Refresh();timer.Start();}};
  Closed+=(_,_)=>media.Close();
  timer.Tick+=async(_,_)=>{if(!busy&&refreshCancellation==null&&page is "启动总览" or "软件连接")await Refresh();};
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
 Button AsyncButton(string text,Func<Task> action,bool primary=false)=>Button(text,()=>_ = Operate(action),primary);
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
 async Task Operate(Func<Task> action) {
  if(closing)return;if(busy){status.Text="已有操作正在执行，请查看进度或点击取消。";return;}refreshCancellation?.Cancel();busy=true;instancePicker.IsEnabled=false;body.IsEnabled=false;cancellation=new();status.Text="正在处理…";
  try{await action();status.Text="操作完成 · "+DateTime.Now.ToString("HH:mm:ss");}catch(OperationCanceledException){if(!closing)status.Text="操作已取消";}catch(Exception e){if(!closing)Error(e);}finally{busy=false;instancePicker.IsEnabled=true;body.IsEnabled=true;cancellation.Dispose();cancellation=null;}
 }
 bool Confirm(string text)=>MessageBox.Show(this,text,"确认操作",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK;
 async Task<CommandResult> Run(params string[] args)=>await runner.Run(current,args,120,cancellation?.Token??default);
 internal async Task<JsonNode> Json(params string[] args) {var result=await Run(args);if(!result.Ok)throw new Exception(result.Summary);return result.Json()??throw new Exception("命令未返回可识别的数据："+result.Summary);}
 async Task Execute(params string[] args){var result=await runner.Run(current,args,600,cancellation?.Token??default);AddLog(result.Summary);if(!result.Ok)throw new Exception(result.Summary);}
 JsonNode? channelCatalog;
 readonly Dictionary<string,string> pageErrors=new();
 internal string ProbeState()=>"page="+page+"; plugins="+plugins.Count+"; skills="+skills.Count+"; status="+status.Text+"; errors=["+string.Join(" | ",pageErrors.Values)+"]; node="+store.Settings.Node+"; entry="+current.Entry;
 internal async Task Refresh() {  refreshCancellation?.Cancel();
  var source=new CancellationTokenSource();refreshCancellation=source;
  var token=source.Token;var generation=++refreshGeneration;var selected=current;var destination=page;
  bool Valid()=>!closing&&!token.IsCancellationRequested&&generation==refreshGeneration&&current==selected&&page==destination;
  async Task<JsonNode> Query(params string[] args){var result=await runner.Run(selected,args,150,token);token.ThrowIfCancellationRequested();if(!result.Ok)throw new Exception(result.Summary);return result.Json()??throw new Exception("命令没有返回有效 JSON。");}
  try {
   if(destination is not ("启动总览" or "软件连接" or "插件管理" or "Skills 管理" or "实例设置"))return;
   status.Text="正在检测；首次扫描可能需要 1–2 分钟，可切换页面或取消…";
   pageErrors.Remove(selected.Id+destination);
   if(destination=="启动总览") {var value=await Query("gateway","status","--json");if(!Valid())return;gateway=value;}
   // 实例设置页内联管理插件与 Skill（仿 PCL 每实例管理 mods / 资源包），因此同样需要这两份列表。
   if(destination is "插件管理" or "实例设置"){var value=ReadModel.Plugins(await Query("plugins","list","--json"));if(!Valid())return;plugins=value;}
   if(destination is "Skills 管理" or "实例设置"){var value=ReadModel.Skills(await Query("skills","list","--json"));if(!Valid())return;skills=value;}
   if(destination=="软件连接") {
    JsonNode? catalog=channelCatalog,live=null;var failures=new List<string>();
    try{catalog=await Query("channels","list","--all","--json");}catch(Exception e) when(e is not OperationCanceledException){failures.Add("应用列表："+SafeLog.Clean(e.Message));}
    if(!Valid())return;channelCatalog=catalog;
    // Channel RPC is its own reachability proof; an unrelated service-manager probe must not gate it.
    try{live=await Query("channels","status","--probe","--timeout","20000","--json");}catch(Exception e) when(e is not OperationCanceledException){failures.Add("连接探测："+SafeLog.Clean(e.Message));}
    if(!Valid())return;
    channels=ReadModel.Channels(catalog,live,live?["channelAccounts"] is JsonObject);
    if(failures.Count>0)pageErrors[selected.Id+destination]=string.Join("\n",failures);
   }
   if(Valid()){checkedAt=DateTime.Now;refreshTimes[selected.Id+destination]=DateTime.Now;SelectPage(destination);status.Text=pageErrors.ContainsKey(selected.Id+destination)?"部分检测失败；请查看页面提示并重试":"状态已更新 · "+checkedAt.Value.ToString("HH:mm:ss");}
  }catch(OperationCanceledException){}catch(Exception e){if(Valid()){pageErrors[selected.Id+destination]=SafeLog.Clean(e.Message);AddLog(e.Message);SelectPage(destination);status.Text="检测失败，页面已显示原因；上次列表不代表当前状态";}}
  finally{if(refreshCancellation==source)refreshCancellation=null;source.Dispose();}
 }
 string savedSelection="",savedFilter="",savedTablePage="";
 void SelectPage(string name) {
  if(activeGrid!=null){savedSelection=(activeGrid.SelectedItem as ItemRow)?.Id??"";savedFilter=filter?.Text??"";savedTablePage=page;}
  page=name;body.IsEnabled=!busy;if(name=="操作日志")log.Text=string.Join(Environment.NewLine,logLines);pageTitle.Text=name;body.Children.Clear();activeGrid=null;filter=null;
  UpdateNavigation();
  if(pageErrors.TryGetValue(current.Id+name,out var failure))body.Children.Add(Card("检测未完成",Text(failure,12,"#D9363E"),Text("下方列表如有内容，是上次检测结果。点击右上角刷新重试。")));
  pageNote.Text=checkedAt==null?"所选实例："+current.Name:"所选实例："+current.Name+"  ·  最近检查 "+checkedAt.Value.ToString("HH:mm:ss");
  switch(name){case "启动总览":Overview();break;case "版本与实例":Versions();break;case "插件管理":PluginPage();break;case "Skills 管理":SkillPage();break;case "软件连接":ChannelPage();break;case "整合包":PackPage();break;case "操作日志":LogPage();break;case "实例设置":InstanceSettingsPage();break;case "版本下载":DownloadPage();break;case "个性化":AppearancePage();break;case "背景音乐":MusicPage();break;case "快捷方式图标":ShortcutIconPage();break;case "关于":AboutPage();break;}
  ApplyOverviewVisibility();AnimateContent();
 }
 async Task StartGatewayCore() {
  LaunchProgress("正在检查已有网关…");
  Runner.EnsureGatewayConfiguration(current);
  var check=await runner.Run(current,["gateway","status","--json"],20,cancellation?.Token??default);var snapshot=check.Json();
  if(ReadModel.B(snapshot?["rpc"],"ok")==true){AddLog("网关已经运行，无需重复启动。");gateway=snapshot;SelectPage(page);return;}
  LaunchProgress("正在启动 OpenClaw，首次启动可能需要一两分钟…");
  if(owned.TryGetValue(current.Id,out var existing)&&!existing.HasExited){AddLog("等待已启动的网关就绪。");}
  else if(ReadModel.B(snapshot?["service"],"loaded")==true)await Execute("gateway","start");
  else {
   var instance=current;var process=runner.StartGateway(instance,line=>AddLog("["+instance.Name+"] "+line));owned[instance.Id]=process;
   process.Exited+=(_,_)=>AddLog("["+instance.Name+"] 网关进程已退出，请检查日志。");
   await Task.Delay(3000,cancellation?.Token??default);if(process.HasExited)throw new Exception("网关启动后退出。请查看操作日志，确认配置、模型和依赖是否完整。");
  }
  var deadline=DateTime.UtcNow.AddSeconds(90);bool ready=false;
  while(DateTime.UtcNow<deadline) {
   cancellation?.Token.ThrowIfCancellationRequested();
   if(owned.TryGetValue(current.Id,out var child)&&child.HasExited)throw new Exception("网关启动失败：\n"+string.Join(Environment.NewLine,logLines.TakeLast(8)));
   var probe=await runner.Run(current,["gateway","status","--json"],15,cancellation?.Token??default);
   var value=probe.Json();if(ReadModel.B(value?["rpc"],"ok")==true){gateway=value;ready=true;break;}
   LaunchProgress("网关正在初始化，正在等待连接检查通过…");
   await Task.Delay(1000,cancellation?.Token??default);
  }
  if(!ready)throw new Exception("网关进程尚未通过连接检查。请查看操作日志，或稍后刷新状态。");
  checkedAt=DateTime.Now;SelectPage(page);AddLog("网关已启动，连接检查通过。");
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
 void Versions() {
  body.Children.Add(Card("已安装的实例",InstanceList()));
  body.Children.Add(Card("当前版本",Text(current.Version,25,"#C8323C"),Text(current.Runtime),Text(current.Managed?"独立实例：配置和工作目录单独保存。":"现有安装：使用所选配置；切换版本前建议创建独立实例。")));
  var runtime=Input(current.Runtime,540);
  var known=store.Settings.Instances.Select(i=>i.Runtime).ToList();var versionsFolder=Path.Combine(store.Root,"versions");
  if(Directory.Exists(versionsFolder))known.AddRange(Directory.GetDirectories(versionsFolder).Where(d=>!Path.GetFileName(d).StartsWith(".")&&File.Exists(Path.Combine(d,"ocl-install.json"))).Select(d=>Path.Combine(d,"node_modules","openclaw")).Where(d=>File.Exists(Path.Combine(d,"openclaw.mjs"))));
  var library=new ComboBox{Width=540,HorizontalAlignment=HorizontalAlignment.Left,ItemsSource=known.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),SelectedItem=current.Runtime};
  library.SelectionChanged+=(_,_)=>{if(library.SelectedItem is string path)runtime.Text=path;};
  body.Children.Add(Card("本机版本库",library,Text("选择已登记的版本后，在下方应用；新下载的版本会保存在本机版本库。")));
  body.Children.Add(Card("选择已有版本目录",runtime,Row(Button("浏览目录",()=>{var dialog=new OpenFolderDialog();if(dialog.ShowDialog(this)==true)runtime.Text=dialog.FolderName;}),AsyncButton("应用到当前实例",async()=>{
   if(owned.TryGetValue(current.Id,out var process)&&!process.HasExited)throw new Exception("请先停止当前网关，再切换版本。");
   if(!File.Exists(Path.Combine(runtime.Text,"openclaw.mjs")))throw new Exception("该目录没有 openclaw.mjs。");
   var check=await Json("gateway","status","--json");if(ReadModel.B(check?["rpc"],"ok")==true)throw new Exception("请先停止当前网关，再切换版本。");
   if(!Confirm("将当前实例切换到：\n"+runtime.Text+"\n旧版本可能不兼容现有数据，建议用新实例测试。"))return;
   current.Runtime=Path.GetFullPath(runtime.Text);store.Save();SelectPage(page);
  }))));
  body.Children.Add(Card("下载版本",Text("从完整版本列表选择正式版、预览版或历史版本，自动下载并创建实例。"),Button("前往自动安装",()=>Navigate("版本下载"),true)));
  var name=Input("新的 OpenClaw 实例",280);var port=Input((store.Settings.Instances.Max(i=>i.Port)+1).ToString(),100);
  body.Children.Add(Card("创建独立实例",Text("为新实例创建独立配置、工作目录和随机网关令牌；不会复制聊天记录与账号密钥。"),Row(name,port),AsyncButton("创建实例",async()=>{
   if(!int.TryParse(port.Text,out var number))throw new Exception("端口必须是数字。");if(!File.Exists(Path.Combine(runtime.Text,"openclaw.mjs")))throw new Exception("请先选择有效的版本目录。");
   var added=store.Create(name.Text,runtime.Text,number,store.Settings.DefaultPlugins);current=added;channelCatalog=null;instancePicker.Items.Refresh();instancePicker.SelectedItem=added;gateway=null;plugins=[];skills=[];channels=[];checkedAt=null;SelectPage(page);AddLog(store.Settings.DefaultPlugins.Count>0?"已按默认插件集创建实例（"+store.Settings.DefaultPlugins.Count+" 项）。":"已创建实例；默认插件集为空，未携带任何插件。");await Task.CompletedTask;
  })));
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
 void PluginPage() {
  pageTitle.Text="插件市场";
  int bundled=plugins.Count(p=>p.Source=="bundled"),custom=plugins.Count(p=>p.Source is not "" and not "bundled");
  pageNote.Text=$"共 {plugins.Count} 个插件 · 官方内置 {bundled} · 自定义/外部 {custom}。启用、禁用、删除仅对当前实例「{current.Name}」生效；自定义插件装到实例 extensions 目录后即出现在此。";
  var filterBox=Input("",360);filterBox.ToolTip="按名称、状态或来源筛选";
  var scope=new ComboBox{Width=140,ItemsSource=new[]{"全部","已启用","已禁用","官方内置","自定义/外部"},SelectedIndex=0};
  body.Children.Add(Toolbar(Label("筛选"),filterBox,scope));
  var panel=new StackPanel();body.Children.Add(panel);
  List<ItemRow> Filtered() {
   var q=filterBox.Text.Trim();var s=scope.SelectedIndex;
   return plugins.Where(r=>(q.Length==0||(r.Name+" "+r.State+" "+r.Source+" "+r.Detail).Contains(q,StringComparison.OrdinalIgnoreCase)))
    .Where(r=>s==0||(s==1&&r.Enabled)||(s==2&&!r.Enabled)||(s==3&&r.Source=="bundled")||(s==4&&r.Source is not "" and not "bundled")).ToList();
  }
  void Render(){panel.Children.Clear();var rows=Filtered();foreach(var r in rows)panel.Children.Add(PluginRow(r));if(rows.Count==0)panel.Children.Add(Toolbar(Text(plugins.Count==0?(refreshCancellation!=null?"正在加载插件列表；首次扫描可能需要 1–2 分钟…":"尚未加载列表：请点击右上角 ↻ 刷新，或稍候自动刷新。"):"没有匹配的插件。",12)));}
  filterBox.TextChanged+=(_,_)=>Render();scope.SelectionChanged+=(_,_)=>Render();
  body.Children.Add(Row(Button("打开插件目录",OpenPluginFolder),AsyncButton("插件诊断",async()=>{await Execute("plugins","doctor");SelectPage("操作日志");})));
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
  pageTitle.Text="Skill 库";
  int bundled=skills.Count(s=>s.Source.Contains("bundled")),custom=skills.Count(s=>s.Source is not "" && !s.Source.Contains("bundled"));
  pageNote.Text=$"共 {skills.Count} 个 Skill · 官方内置 {bundled} · 自定义/插件附带 {custom}。启用、禁用、删除仅对当前实例「{current.Name}」生效。";
  var filterBox=Input("",360);filterBox.ToolTip="按名称、状态或来源筛选";
  var scope=new ComboBox{Width=140,ItemsSource=new[]{"全部","已启用","已禁用","官方内置","自定义/外部"},SelectedIndex=0};
  body.Children.Add(Toolbar(Label("筛选"),filterBox,scope));
  var panel=new StackPanel();body.Children.Add(panel);
  List<ItemRow> Filtered() {
   var q=filterBox.Text.Trim();var s=scope.SelectedIndex;
   return skills.Where(r=>(q.Length==0||(r.Name+" "+r.State+" "+r.Source+" "+r.Detail).Contains(q,StringComparison.OrdinalIgnoreCase)))
    .Where(r=>s==0||(s==1&&r.Enabled)||(s==2&&!r.Enabled)||(s==3&&r.Source.Contains("bundled"))||(s==4&&r.Source is not "" && !r.Source.Contains("bundled"))).ToList();
  }
  void Render(){panel.Children.Clear();var rows=Filtered();foreach(var r in rows)panel.Children.Add(SkillRow(r));if(rows.Count==0)panel.Children.Add(Toolbar(Text(skills.Count==0?(refreshCancellation!=null?"正在加载 Skill 列表；首次扫描可能需要 1–2 分钟…":"尚未加载列表：请点击右上角 ↻ 刷新，或稍候自动刷新。"):"没有匹配的 Skill。",12)));}
  filterBox.TextChanged+=(_,_)=>Render();scope.SelectionChanged+=(_,_)=>Render();
  body.Children.Add(Row(Button("打开 Skills 目录",OpenSkillsFolder),AsyncButton("依赖检查",async()=>{await Execute("skills","check");SelectPage("操作日志");}),AsyncButton("更新已跟踪 Skills",async()=>{if(Confirm("更新当前实例中由 ClawHub 跟踪的 Skills？")){await Execute("skills","update","--all");await Refresh();}})));
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
  pageNote.Text="两类连接：插件直连（如 Wallpaper Engine）与 API 密钥（Token）直连。绿：已连接/探测通过，黄：未验证，红：异常，灰：未配置。";
  // —— 插件链接 ——
  body.Children.Add(SectionTitle("插件链接"));
  body.Children.Add(WallpaperConnectionCard());
  // —— API 密钥链接（Token）——
  body.Children.Add(SectionTitle("API 密钥链接（Token）"));
  body.Children.Add(Row(Button("检测全部",()=>{if(!busy)_=Refresh();}),Button("配置账号",()=>OpenWizard(["channels","add"]))));
  var search=Input("",350);search.ToolTip="筛选应用名称、账号或状态";body.Children.Add(Toolbar(Label("查找应用"),search));
  var cards=new StackPanel();body.Children.Add(cards);
  void Render(){cards.Children.Clear();foreach(var item in channels.Where(r=>(r.Name+" "+r.Account+" "+r.State).Contains(search.Text,StringComparison.OrdinalIgnoreCase))) {
   var name=new StackPanel{Orientation=Orientation.Horizontal};name.Children.Add(Text(item.Name,16,"#444444"));
   name.Children.Add(new System.Windows.Shapes.Ellipse{Width=9,Height=9,Fill=Brush(item.Indicator),Margin=new Thickness(9,0,0,8),VerticalAlignment=VerticalAlignment.Center,ToolTip=item.State});
   var actions=Row(AsyncButton("检测连接",()=>ProbeChannel(item)),Button("配置",()=>OpenWizard(["channels","add","--channel",item.Id])),Button("登录",()=>OpenWizard(item.Account.Length>0?["channels","login","--channel",item.Id,"--account",item.Account]:["channels","login","--channel",item.Id])));
   actions.IsEnabled=item.Id.Length>0;
   cards.Children.Add(Card("",name,Text(item.State+(item.Account.Length>0?" · 账号 "+item.Account:""),13,item.Indicator),Text(item.Detail,11),actions));
  }
  if(channels.Count==0)cards.Children.Add(Toolbar(Text("正在等待检测结果；点击“检测全部”加载应用列表。",12)));}
  search.TextChanged+=(_,_)=>Render();Render();
 }
 async Task ProbeChannel(ItemRow row) {
  var selected=current;
  try {
   var value=await Json("channels","status","--channel",row.Id,"--probe","--timeout","20000","--json");
   if(current!=selected)return;
   var next=ReadModel.Channels(channelCatalog,value,value["channelAccounts"] is JsonObject).Where(r=>r.Id==row.Id).ToList();
   channels.RemoveAll(r=>r.Id==row.Id);channels.AddRange(next.Count>0?next:[row with {State="未验证",Detail="网关未返回该应用的运行数据。"}]);
  }catch(Exception e) when(e is not OperationCanceledException){channels=channels.Select(r=>r.Id==row.Id?r with {State="检测失败",Detail=SafeLog.Clean(e.Message)}:r).ToList();}
  if(page=="软件连接")SelectPage(page);
 }
 UIElement WallpaperConnectionCard() {
  var title=new StackPanel{Orientation=Orientation.Horizontal};title.Children.Add(Text("Wallpaper Engine",16,"#444444"));
  var label=Text("点击检测本地壁纸库与网页组件",12);var light=new System.Windows.Shapes.Ellipse{Width=9,Height=9,Fill=Brush("#999999"),Margin=new Thickness(9,0,0,8),VerticalAlignment=VerticalAlignment.Center};title.Children.Add(light);
  return Card("",title,label,Row(Button("检测连接",()=>{
   try{var configPath=current.Config.Length>0?current.Config:Path.Combine(current.State.Length>0?current.State:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw"),"openclaw.json");
    var cfg=JsonNode.Parse(File.ReadAllText(configPath));var plugin=cfg?["plugins"]?["entries"]?["wallpaper-engine"];
    var library=ReadModel.S(plugin?["config"],"libraryRoot");var ui=ReadModel.S(plugin?["config"],"controlUiRoot");
    if(ui.Length==0)ui=Store.ResolveControlUiRoot(current.Runtime); // 配置未写时按运行目录兜底
    var count=Directory.Exists(library)?Directory.EnumerateDirectories(library).Count(d=>File.Exists(Path.Combine(d,"project.json"))):0;
    var ready=ReadModel.B(plugin,"enabled")==true&&count>0&&File.Exists(Path.Combine(ui,"wallpaper","wallpaper.js"));
    light.Fill=Brush(ready?"#248B69":"#D9363E");
    label.Text=ready?$"本地库可用 · {count} 项 · 网页组件已安装；在控制台点击“壁纸”测试网关连接。":(plugin==null?"本实例未安装 Wallpaper Engine 插件，点击「修复插件」即可为本实例安装并启用。":"尚未就绪：请检查插件启用状态、壁纸库目录与网页组件；必要时点击「修复插件」。");
   }catch(Exception e){light.Fill=Brush("#D9363E");label.Text=SafeLog.Clean(e.Message);}
  }),Button("修复插件",()=>_ = RepairWallpaper()),AsyncButton("打开网页控制台",OpenDashboard)));
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
  pageNote.Text="以可审查的清单管理一组版本、插件与 Skills；账号凭据不随整合包导出。";
  body.Children.Add(Card("导出当前整合包",Text("保存 OpenClaw 版本、插件及 Skill 启停状态。能确定来源的扩展会记录安装引用；不能自动还原的扩展会列为待补充项。"),AsyncButton("导出 .clawpack.json",ExportPack,true)));
  body.Children.Add(Card("导入整合包",Text("先查看完整清单，再创建独立实例逐项安装。任何失败都会保留日志；不会覆盖当前实例。需要单独完成模型和软件账号配置。"),AsyncButton("选择并预览整合包",ImportPack)));
  body.Children.Add(Card("整合包的边界",Text("这是依赖与启停清单，不包含聊天记录、令牌、模型密钥或本地 Skill 文件。需要本地文件的项目会明确列出，不会显示为已恢复。")));
 }
 async Task ExportPack() {
  var pluginJson=await Json("plugins","list","--json");var skillJson=await Json("skills","list","--json");
  var installsResult=await Run("config","get","plugins.installs","--json");var installs=installsResult.Ok?installsResult.Json():null;
  var pack=new Pack{Name=current.Name,Version=current.Version};
  foreach(var p in (pluginJson["plugins"] as JsonArray??[]).Where(p=>p!=null)) {
   var id=ReadModel.S(p,"id");var bundled=ReadModel.S(p,"origin")=="bundled";var spec=ReadModel.S(installs?[id],"spec");
   if(!bundled&&!Pack.IsPortableSpec(spec)){pack.Unresolved.Add("plugin: "+id+"（缺少可移植安装来源）");continue;}
   pack.Entries.Add(new(id,bundled?null:spec,"plugin",ReadModel.B(p,"enabled")==true));
  }
  foreach(var s in (skillJson["skills"] as JsonArray??[]).Where(s=>s!=null)) {
   var id=ReadModel.S(s,"name");if(ReadModel.B(s,"bundled")!=true){pack.Unresolved.Add("skill: "+id+"（请在清单中补充 @owner/slug，或在导入后手动安装）");continue;}
   pack.Entries.Add(new(id,null,"skill",ReadModel.B(s,"disabled")!=true));
  }
  var dialog=new SaveFileDialog{Filter="OpenClaw 整合包|*.clawpack.json",FileName="OpenClaw.clawpack.json"};if(dialog.ShowDialog(this)!=true)return;
  File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(pack,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));AddLog("已导出整合包；待补充项 "+pack.Unresolved.Count);MessageBox.Show(this,"整合包已导出。\n待补充来源的项目："+pack.Unresolved.Count,"导出完成");
 }
 async Task ImportPack() {
  var dialog=new OpenFileDialog{Filter="OpenClaw 整合包|*.clawpack.json;*.json"};if(dialog.ShowDialog(this)!=true)return;var pack=Pack.Load(dialog.FileName);
  var preview=JsonSerializer.Serialize(pack,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
  var review=new Window{Owner=this,Title="整合包预览 · "+pack.Name,Width=760,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner};var dock=new DockPanel{Margin=new Thickness(20)};
  var approve=Button("创建独立实例并安装",()=>review.DialogResult=true,true);DockPanel.SetDock(approve,Dock.Bottom);dock.Children.Add(approve);dock.Children.Add(new TextBox{Text=preview,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});review.Content=dock;
  if(review.ShowDialog()!=true)return;
  var runtime=current.Version==pack.Version?current.Runtime:await RuntimeInstall.Install(store,runner,pack.Version,cancellation?.Token??default);
  var added=store.Create(pack.Name+" · 导入",runtime,store.Settings.Instances.Max(i=>i.Port)+1);current=added;channelCatalog=null;instancePicker.Items.Refresh();instancePicker.SelectedItem=added;
  var failures=new List<string>();
  foreach(var entry in pack.Entries) {
   cancellation?.Token.ThrowIfCancellationRequested();
   try {
    if(entry.Install!=null)await Execute(entry.Kind=="plugin"?"plugins":"skills","install",entry.Install);
    if(entry.Kind=="plugin")await Execute("plugins",entry.Enabled?"enable":"disable",entry.Id);else await SetSkill(entry.Id,entry.Enabled);
   }catch(Exception e){failures.Add(entry.Id+": "+SafeLog.Clean(e.Message));}
  }
  gateway=null;SelectPage(page);var message=$"清单处理结束。失败 {failures.Count} 项，待手动补充 {pack.Unresolved.Count} 项。\n请单独配置模型与软件账号。";AddLog(message+"\n"+string.Join("\n",failures));MessageBox.Show(this,message,"整合包结果");
 }
 void LogPage() {
  body.Children.Add(Row(Button("导出日志",()=>{var dialog=new SaveFileDialog{Filter="文本日志|*.txt",FileName="OpenClaw-launcher-log.txt"};if(dialog.ShowDialog(this)==true)File.WriteAllText(dialog.FileName,SafeLog.Clean(string.Join(Environment.NewLine,logLines)));}),Button("清空显示",()=>{logLines.Clear();log.Clear();})));
  body.Children.Add(Toolbar(Text("日志已过滤常见令牌字段；分享前仍请检查个人路径及第三方插件输出。",12)));body.Children.Add(log);
 }
 void InstanceSettingsPage() {
  var node=Input(store.Settings.Node,510);var state=Input(current.State,510);var config=Input(current.Config,510);
  var instanceName=Input(current.Name,280);
  body.Children.Add(Card("实例名称与版本",Text("每个实例对应一个 OpenClaw 版本；把实例改名后再创建，即可让同一版本并存多个实例（如同 PCL 的多个存档）。"),Row(instanceName,Button("重命名",()=>{store.Rename(current,instanceName.Text);instancePicker.Items.Refresh();AddLog("实例已重命名为 "+current.Name);SelectPage(page);}),Button("复制实例",()=>{var clone=store.Duplicate(current,current.Name+" 副本");current=clone;channelCatalog=null;instancePicker.Items.Refresh();instancePicker.SelectedItem=clone;gateway=null;plugins=[];skills=[];channels=[];checkedAt=null;SelectPage(page);AddLog("已复制实例 "+clone.Name);})),Text("当前版本："+current.Version+"  ·  "+(current.Managed?"独立实例（独立配置与工作目录）":"已有安装（使用所选配置）"),12)));
  body.Children.Add(Toolbar(Text("以下插件、Skill 与整合包都只作用于当前实例「"+current.Name+"」；切换实例即切换该实例自己的一套。",12)));
  // —— 插件（本实例）：仿 PCL「mods」管理，列表内联、逐项可启用/禁用/删除 ——
  // 进页时若还没数据就自动拉取（用 refreshCancellation 防重复/死循环），避免显示成误导性的「共 0 个」。
  if(plugins.Count==0&&!busy&&refreshCancellation==null)_ = Refresh();
  var loading=refreshCancellation!=null;
  body.Children.Add(Card("插件（本实例）",Text(loading?"正在加载插件列表；首次扫描可能需要 1–2 分钟…":"共 "+plugins.Count+" 个 · 官方内置 "+plugins.Count(p=>p.Source=="bundled")+" · 自定义/外部 "+plugins.Count(p=>p.Source is not "" and not "bundled")+"。启用、禁用、删除只作用于「"+current.Name+"」。",12),Row(Button("↻ 刷新",()=>{if(!busy)_ = Refresh();}),Button("打开插件目录",OpenPluginFolder),Button("插件市场",()=>Navigate("插件管理"),true))));
  var pluginFilter=Input("",340);pluginFilter.ToolTip="按名称、状态或来源筛选";body.Children.Add(Toolbar(Label("筛选"),pluginFilter));
  var pluginPanel=new StackPanel();body.Children.Add(pluginPanel);
  void RenderPlugins(){pluginPanel.Children.Clear();var q=pluginFilter.Text.Trim();var rows=plugins.Where(r=>q.Length==0||(r.Name+" "+r.State+" "+r.Source+" "+r.Detail).Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();foreach(var r in rows)pluginPanel.Children.Add(PluginRow(r));if(rows.Count==0)pluginPanel.Children.Add(Toolbar(Text(plugins.Count==0?(loading?"正在加载插件列表；首次扫描可能需要 1–2 分钟…":"尚未加载：点击「↻ 刷新」加载本实例的插件。"):"没有匹配的插件。",12)));}
  pluginFilter.TextChanged+=(_,_)=>RenderPlugins();RenderPlugins();
  // —— Skill（本实例）：仿 PCL「资源包」管理，与插件同一套横向条目 ——
  // 同样做进页自动加载，避免显示成误导性的「共 0 个」。
  if(skills.Count==0&&!busy&&refreshCancellation==null)_ = Refresh();
  var skillLoading=refreshCancellation!=null||skills.Count==0;
  body.Children.Add(Card("Skill（本实例）",Text(skillLoading?"正在加载 Skill 列表…":"共 "+skills.Count+" 个 · 内置 "+skills.Count(s=>s.Source.Contains("bundled"))+" · 外部/自定义 "+skills.Count(s=>s.Source is not "" && !s.Source.Contains("bundled"))+"。启用、禁用、删除只作用于「"+current.Name+"」。",12),Row(Button("打开 Skills 目录",OpenSkillsFolder),Button("Skill 库",()=>Navigate("Skills 管理"),true))));
  var skillFilter=Input("",340);skillFilter.ToolTip="按名称、状态或来源筛选";body.Children.Add(Toolbar(Label("筛选"),skillFilter));
  var skillPanel=new StackPanel();body.Children.Add(skillPanel);
  void RenderSkills(){skillPanel.Children.Clear();var q=skillFilter.Text.Trim();var rows=skills.Where(r=>q.Length==0||(r.Name+" "+r.State+" "+r.Source+" "+r.Detail).Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();foreach(var r in rows)skillPanel.Children.Add(SkillRow(r));if(rows.Count==0)skillPanel.Children.Add(Toolbar(Text(skills.Count==0?(refreshCancellation!=null?"正在加载 Skill 列表…":"尚未加载：点击上方「↻ 刷新」重新加载本实例的 Skill。"):"没有匹配的 Skill。",12)));}
  skillFilter.TextChanged+=(_,_)=>RenderSkills();RenderSkills();
  // —— 实例独立性：每个实例用自己的状态目录与配置，插件/Skill 不互相继承 ——
  body.Children.Add(Card("实例独立性",Text("本实例状态目录："+InstanceStateRoot()+"\n插件与 Skill 只从这里与实例配置加载，切换实例即换一套，不会继承其他实例的内容。",12),Row(AsyncButton("检查各实例是否独立",CheckInstanceIsolation),AsyncButton("从其他实例导入设置",ImportInstanceSettings,true))));
  // —— 整合包（本实例）——
  body.Children.Add(Card("整合包（本实例）",Text("导出本实例的版本、插件与 Skill 启停清单；导入时创建独立实例逐项安装。账号凭据不随整合包导出。",12),Row(AsyncButton("导出 .clawpack.json",ExportPack,true),AsyncButton("选择并预览整合包",ImportPack))));
  body.Children.Add(Card("新实例默认插件集",Text("下载/创建新实例时只携带这里的“必要插件”，其余由用户自行安装或制作。以当前实例为模板采集最省事。"),Row(AsyncButton("照搬当前实例",CaptureDefaultPlugins,true),Button("清空默认集",()=>{store.Settings.DefaultPlugins=[];store.Save();SelectPage(page);})),Text(store.Settings.DefaultPlugins.Count==0?"尚未采集默认插件集：新实例将不携带任何插件。":"已采集 "+store.Settings.DefaultPlugins.Count+" 个默认插件："+string.Join("、",store.Settings.DefaultPlugins.Take(8))+(store.Settings.DefaultPlugins.Count>8?" …":""),11,"#999999")));
  body.Children.Add(Card("运行环境与配置",Text("Node.js 可执行文件"),node,Text("OpenClaw 状态目录（留空使用默认目录）"),state,Text("配置文件路径（留空使用默认规则；不是程序目录里的任意 JSON）"),config,Row(Button("选择配置文件",()=>{var dialog=new OpenFileDialog{Filter="配置文件|*.json;*.json5|所有文件|*.*"};if(dialog.ShowDialog(this)==true)config.Text=dialog.FileName;}),Button("保存",()=>{
   if(owned.TryGetValue(current.Id,out var p)&&!p.HasExited)throw new Exception("请先停止该实例网关，再修改运行设置。");
   RuntimeInstall.ResolveExecutable(node.Text.Trim());if(config.Text.Length>0&&!File.Exists(config.Text))throw new Exception("配置文件不存在。");if(state.Text.Length>0&&!Directory.Exists(state.Text))throw new Exception("状态目录不存在。");
   store.Settings.Node=node.Text.Trim();runner.Node=store.Settings.Node;current.State=state.Text.Trim();current.Config=config.Text.Trim();store.Save();gateway=null;checkedAt=null;AddLog("设置已保存。");
  }),Button("打开配置向导",()=>OpenWizard(["configure"])),AsyncButton("验证配置",async()=>{await Execute("config","validate");MessageBox.Show(this,"配置验证通过。","检查完成");}))));
 }
 // 逐实例查询插件/Skill 数量并检查状态目录：共用同一状态目录的实例会互相影响，这里明确报出来。
 async Task CheckInstanceIsolation() {
  var lines=new List<string>();
  var byRoot=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
  foreach(var instance in store.Settings.Instances.ToList()) {
   var root=instance.State.Length>0?instance.State:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw");
   var note=instance==current?"（当前）":"";
   string pluginsText="查询失败",skillsText="查询失败";
   try {
    var p=await runner.Run(instance,["plugins","list","--json"],150,cancellation?.Token??default);
    pluginsText=p.Ok&&p.Json() is JsonNode pn?ReadModel.Plugins(pn).Count+" 个":"失败："+SafeLog.Clean(p.Summary);
   } catch(Exception e){pluginsText="失败："+SafeLog.Clean(e.Message);}
   try {
    var s=await runner.Run(instance,["skills","list","--json"],150,cancellation?.Token??default);
    skillsText=s.Ok&&s.Json() is JsonNode sn?ReadModel.Skills(sn).Count+" 个":"失败："+SafeLog.Clean(s.Summary);
   } catch(Exception e){skillsText="失败："+SafeLog.Clean(e.Message);}
   lines.Add(instance.Name+note+"：插件 "+pluginsText+" · Skill "+skillsText+"\n    状态目录 "+root);
   if(!byRoot.TryGetValue(root,out var names))byRoot[root]=names=new List<string>();
   names.Add(instance.Name);
  }
  var shared=byRoot.Where(p=>p.Value.Count>1).ToList();
  lines.Add(shared.Count==0?"\n结论：每个实例使用各自的状态目录，插件与 Skill 互不继承。":"\n⚠ 以下状态目录被多个实例共用，插件与 Skill 会互相影响：\n"+string.Join("\n",shared.Select(p=>"  "+p.Key+" → "+string.Join("、",p.Value))));
  var report=string.Join("\n",lines);
  AddLog("实例独立性检查：\n"+report);
  MessageBox.Show(this,report,"实例独立性检查");
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
