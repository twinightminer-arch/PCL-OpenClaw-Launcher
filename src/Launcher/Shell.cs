using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
namespace ClawLauncher;
public sealed partial class MainWindow
{
 readonly Grid frame=new(),workspace=new(),sidebarHost=new(),splash=new();
 readonly Border titleBar=new(),sidebarSurface=new();
 readonly Image wallpaper=new();
 readonly Dictionary<string,Button> topButtons=new();
 readonly TextBlock musicBadge=new();
 ColumnDefinition sidebarColumn=new();
 Border contentPane=new();
 string GroupFor(string value)=>value switch {"版本下载" or "版本与实例"=>"下载","软件连接"=>"连接","实例设置" or "插件管理" or "Skills 管理" or "整合包"=>"实例","个性化" or "背景音乐" or "快捷方式图标" or "关于" or "操作日志"=>"设置",_=>"启动"};
 void Navigate(string destination) {refreshCancellation?.Cancel();SelectPage(destination);if(!busy&&(!refreshTimes.TryGetValue(current.Id+destination,out var time)||DateTime.Now-time>TimeSpan.FromSeconds(60)))_ = Refresh();}
 void BuildShell() {
  WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.CanResize;
  WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(7),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(6)});
  Icon=Brand.Image();var outer=new Grid{Margin=new Thickness(9)};
  outer.Children.Add(new PclControls.MyDropShadow{ShadowRadius=8,CornerRadius=new CornerRadius(6),Color=Color.FromArgb(65,0,0,0),Margin=new Thickness(-7)});
  frame.RowDefinitions.Add(new(){Height=new GridLength(48)});frame.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
  var frameBorder=new Border{CornerRadius=new CornerRadius(6),Child=frame,Background=Brushes.White};outer.Children.Add(frameBorder);
  frame.SizeChanged+=(_,_)=>frame.Clip=new RectangleGeometry(new Rect(0,0,frame.ActualWidth,frame.ActualHeight),6,6);
  var titleContent=new Grid();titleContent.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});titleContent.ColumnDefinitions.Add(new(){Width=GridLength.Auto});titleContent.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  var logo=Text("OCL",19,"#FFFFFF");logo.FontFamily=new FontFamily("Arial");logo.FontWeight=FontWeights.SemiBold;logo.Margin=new Thickness(19,0,0,0);logo.VerticalAlignment=VerticalAlignment.Center;logo.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("TitleText"){Source=store.Settings.Appearance});titleContent.Children.Add(logo);
  var tabs=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(13,0,13,0)};Grid.SetColumn(tabs,1);titleContent.Children.Add(tabs);
  var groups=new[]{"启动","下载","连接","实例","设置"};var destinations=new[]{"启动总览","版本下载","软件连接","实例设置","个性化"};
  for(int i=0;i<groups.Length;i++) {
   var destination=destinations[i];var button=Button("",()=>Navigate(destination));button.Style=(Style)FindResource("TitleButton");
   var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(PclGlyphs.Title[i]),Fill=Brushes.White,Width=14,Height=14,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)});row.Children.Add(new TextBlock{Text=groups[i],Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center});button.Content=row;topButtons[groups[i]]=button;tabs.Children.Add(button);
  }
  var windowButtons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,9,0)};Grid.SetColumn(windowButtons,2);titleContent.Children.Add(windowButtons);
  foreach(var (glyph,action,hint) in new (string,Action,string)[]{("─",()=>WindowState=WindowState.Minimized,"最小化"),("□",()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized,"最大化 / 还原"),("×",Close,"关闭")}) {
   var b=Button(glyph,action);b.Style=(Style)FindResource("TitleButton");b.Width=27;b.Height=28;b.MinHeight=0;b.Margin=new Thickness(2,0,2,0);b.Padding=new Thickness(0);b.FontSize=20;b.ToolTip=hint;windowButtons.Children.Add(b);
  }
  titleBar.Child=titleContent;titleBar.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is Grid or Border or TextBlock){if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else DragMove();}};frame.Children.Add(titleBar);
  var backdrop=new Grid{Background=Brush("#F3F3F3")};Grid.SetRow(backdrop,1);frame.Children.Add(backdrop);wallpaper.IsHitTestVisible=false;backdrop.Children.Add(wallpaper);
  workspace.ColumnDefinitions.Add(sidebarColumn);workspace.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});backdrop.Children.Add(workspace);
  sidebarSurface.Child=sidebarHost;workspace.Children.Add(sidebarSurface);var separator=new Border{Width=3,HorizontalAlignment=HorizontalAlignment.Right,Background=new LinearGradientBrush(Color.FromArgb(15,0,0,0),Colors.Transparent,0)};workspace.Children.Add(separator);
  var right=new Grid{Margin=new Thickness(20,17,15,6)};right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});right.RowDefinitions.Add(new(){Height=GridLength.Auto});
  var head=new DockPanel();var headHost=new Border{Background=new SolidColorBrush(Color.FromArgb(216,255,255,255)),CornerRadius=new CornerRadius(6),Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,5,11),Child=head};var actions=Row(Button("↻",()=>{if(busy)return;if(page=="版本下载")_ = Operate(LoadCatalog);else _ = Refresh();}),Button("×",()=>{cancellation?.Cancel();refreshCancellation?.Cancel();}));DockPanel.SetDock(actions,Dock.Right);head.Children.Add(actions);pageTitle.FontSize=14;pageTitle.FontWeight=FontWeights.Bold;pageTitle.Margin=new Thickness(0,0,0,3);pageNote.FontSize=11;pageNote.Foreground=Brush("#6E6E6E");pageNote.TextTrimming=TextTrimming.CharacterEllipsis;var titleStack=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var heading=new StackPanel{Orientation=Orientation.Horizontal};heading.Children.Add(pageTitle);
  overviewToggle=Button("收起 ▴",()=>{overviewCollapsed=!overviewCollapsed;ApplyOverviewVisibility();});overviewToggle.Foreground=Brush("#D9363E");overviewToggle.BorderThickness=new Thickness(0);overviewToggle.Background=Brushes.Transparent;overviewToggle.Margin=new Thickness(10,-5,0,0);overviewToggle.Padding=new Thickness(5,2,5,2);heading.Children.Add(overviewToggle);titleStack.Children.Add(heading);titleStack.Children.Add(pageNote);head.Children.Add(titleStack);right.Children.Add(headHost);
  contentPane=new Border{Child=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(3,3,7,3)}};Grid.SetRow(contentPane,1);right.Children.Add(contentPane);
  var footer=new DockPanel();musicBadge.FontSize=11;musicBadge.MaxWidth=170;musicBadge.TextTrimming=TextTrimming.CharacterEllipsis;musicBadge.VerticalAlignment=VerticalAlignment.Center;musicBadge.Foreground=Brush("#777777");musicBadge.Cursor=Cursors.Hand;musicBadge.MouseLeftButtonDown+=(_,_)=>Navigate("背景音乐");DockPanel.SetDock(musicBadge,Dock.Right);footer.Children.Add(musicBadge);status.FontSize=10;status.Foreground=Brush("#999999");status.Text="OCL "+Brand.Version;status.Margin=new Thickness(0,4,5,0);status.TextTrimming=TextTrimming.CharacterEllipsis;footer.Children.Add(status);var footerHost=new Border{Background=new SolidColorBrush(Color.FromArgb(200,255,255,255)),CornerRadius=new CornerRadius(5),Padding=new Thickness(10,4,10,4),Margin=new Thickness(0,4,5,0),Child=footer};Grid.SetRow(footerHost,2);right.Children.Add(footerHost);Grid.SetColumn(right,1);workspace.Children.Add(right);
  splash.Visibility=Visibility.Collapsed;splash.Background=Brushes.White;Grid.SetRowSpan(splash,2);Panel.SetZIndex(splash,20);frame.Children.Add(splash);Content=outer;
  instancePicker.ItemsSource=store.Settings.Instances;instancePicker.SelectedItem=current;instancePicker.SelectionChanged+=async(_,_)=>{if(instancePicker.SelectedItem is Instance selected&&selected!=current){if(busy){instancePicker.SelectedItem=current;return;}current=selected;channelCatalog=null;store.Settings.Selected=current.Id;store.Save();gateway=null;plugins=[];skills=[];channels=[];checkedAt=null;SelectPage(page);await Refresh();}};
 }
 void UpdateNavigation() {
  foreach(var (group,b) in topButtons)b.Background=group==GroupFor(page)?Brush("#35FFFFFF"):Brushes.Transparent;
  sidebarColumn.Width=new GridLength(page=="启动总览"?300:176);
  if(instancePicker.Parent is Panel old)old.Children.Remove(instancePicker);
  if(gatewayBadge.Parent is Panel prior)prior.Children.Remove(gatewayBadge);
  sidebarHost.Children.Clear();
  if(page=="启动总览"){LaunchSidebar();return;}
  var dock=new DockPanel();sidebarHost.Children.Add(dock);
  var currentBox=new StackPanel{Margin=new Thickness(12)};currentBox.Children.Add(Text("当前实例",11,"#999999"));instancePicker.Width=152;instancePicker.Margin=new Thickness(0);currentBox.Children.Add(instancePicker);DockPanel.SetDock(currentBox,Dock.Bottom);dock.Children.Add(currentBox);
  var menu=new StackPanel{Margin=new Thickness(0,12,0,0)};dock.Children.Add(menu);
  var entries=GroupFor(page) switch {
   "下载"=>new[]{("自动安装","版本下载","⬡"),("本地实例","版本与实例","▣")},
   "连接"=>new[]{("软件连接","软件连接","◎")},
   "实例"=>new[]{("实例设置","实例设置","⚙"),("插件市场","插件管理","◇"),("Skill 库","Skills 管理","✧"),("整合包","整合包","▤")},
   "设置"=>new[]{("个性化","个性化","✦"),("背景音乐","背景音乐","♫"),("快捷方式图标","快捷方式图标","⬡"),("关于","关于","ⓘ"),("操作日志","操作日志","≡")},
   _=>new[]{("关于","关于","ⓘ"),("操作日志","操作日志","≡")}
  };
  foreach(var (label,destination,glyph) in entries) {
   if(label=="插件")menu.Children.Add(new TextBlock{Text="扩展资源",FontSize=11,Foreground=Brush("#999999"),Margin=new Thickness(13,22,0,4)});
   var b=Button("",()=>Navigate(destination));b.Height=37;b.Margin=new Thickness(0);b.BorderThickness=new Thickness(0);b.Padding=new Thickness(0);b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.Background=destination==page?(System.Windows.Media.Brush)FindResource("AccentSoft"):Brushes.Transparent;
   var g=new Grid();g.ColumnDefinitions.Add(new(){Width=new GridLength(4)});g.ColumnDefinitions.Add(new(){Width=new GridLength(35)});g.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
   g.Children.Add(new Border{Width=3,Height=20,CornerRadius=new CornerRadius(1),Background=destination==page?(System.Windows.Media.Brush)FindResource("AccentBrush"):Brushes.Transparent});var symbol=new TextBlock{Text=glyph,FontSize=17,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(symbol,1);g.Children.Add(symbol);var text=new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(text,2);g.Children.Add(text);if(destination==page){symbol.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");text.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");}b.Content=g;menu.Children.Add(b);
  }
 }
 void LaunchSidebar() {
  var grid=new Grid{Margin=new Thickness(20,24,20,20)};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});sidebarHost.Children.Add(grid);
  var badge=new Border{Background=(System.Windows.Media.Brush)FindResource("AccentSoft"),CornerRadius=new CornerRadius(13.5),Padding=new Thickness(15,5,15,5),HorizontalAlignment=HorizontalAlignment.Center};badge.Child=new TextBlock{Text="◎  本地网关",FontSize=13,Foreground=(System.Windows.Media.Brush)FindResource("AccentBrush")};grid.Children.Add(badge);
  var middle=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,12,0,12)};Grid.SetRow(middle,1);grid.Children.Add(middle);
  middle.Children.Add(BrandPair(68,20));var caption=Text(current.Name,15,"#555555");caption.TextAlignment=TextAlignment.Center;middle.Children.Add(caption);var sub=Text("OpenClaw "+current.Version,11,"#999999");sub.TextAlignment=TextAlignment.Center;middle.Children.Add(sub);
  instancePicker.Width=260;instancePicker.Margin=new Thickness(0,5,0,8);middle.Children.Add(instancePicker);gatewayBadge.Text=ReadModel.B(gateway?["rpc"],"ok")==true?"●  网关可达":"○  "+(gateway==null?"尚未检查":"网关未就绪");gatewayBadge.FontSize=11;gatewayBadge.Foreground=Brush(GatewayColor);gatewayBadge.HorizontalAlignment=HorizontalAlignment.Center;gatewayBadge.Margin=new Thickness(0,7,0,0);middle.Children.Add(gatewayBadge);
  var launch=AsyncButton("",StartGateway,true);launch.Height=54;launch.Margin=new Thickness(0);launch.Padding=new Thickness(0);var label=new StackPanel();label.Children.Add(new TextBlock{Text="启动 OpenClaw",FontSize=17,HorizontalAlignment=HorizontalAlignment.Center});label.Children.Add(new TextBlock{Text=current.Version+"  ·  "+(current.Managed?"独立实例":"已有安装"),FontSize=11,Foreground=Brush("#888888"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,3,0,0)});launch.Content=label;Grid.SetRow(launch,2);grid.Children.Add(launch);
  var buttons=new Grid{Margin=new Thickness(0,10,0,0)};buttons.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});buttons.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});var select=Button("版本选择",()=>Navigate("版本与实例"));select.Height=35;select.Margin=new Thickness(0,0,5,0);buttons.Children.Add(select);var settings=Button("实例设置",()=>Navigate("实例设置"));settings.Height=35;settings.Margin=new Thickness(5,0,0,0);Grid.SetColumn(settings,1);buttons.Children.Add(settings);Grid.SetRow(buttons,3);grid.Children.Add(buttons);
 }
 UIElement Card(string title,params UIElement[] contents) {
  // PCL MyCard: 5px corners, 3px shadow, 13px title, 15px inset; Minecraft virtualization removed.
  var grid=new Grid{Margin=new Thickness(0,0,0,15)};grid.Children.Add(new PclControls.MyDropShadow{Color=Color.FromArgb(18,0,0,0),ShadowRadius=3,CornerRadius=new CornerRadius(5),Margin=new Thickness(-3)});
  var stack=new StackPanel();if(title.Length>0){var heading=Text(title,13,"#4B4B4B");heading.FontWeight=FontWeights.Bold;heading.Margin=new Thickness(0,0,0,14);stack.Children.Add(heading);}foreach(var child in contents)stack.Children.Add(child);  var border=new Border{CornerRadius=new CornerRadius(5),Padding=new Thickness(16,13,16,12),Child=stack};border.SetResourceReference(Border.BackgroundProperty,"CardBrush");grid.Children.Add(border);  return grid;
 }
 UIElement SectionTitle(string text) {
  var t=Text(text,14,"#C8323C");t.FontWeight=FontWeights.Bold;t.Margin=new Thickness(0,6,0,4);
  return t;
 }
 // PCL 风格开关：开启=蓝色，关闭=灰色；点击切换并回调新状态。
 UIElement Switch(bool on,Action<bool> changed) {
  var track=new Border{Width=46,Height=24,CornerRadius=new CornerRadius(12),Background=Brush(on?"#1E6FE8":"#BBBBBB"),Cursor=Cursors.Hand,VerticalAlignment=VerticalAlignment.Center};
  var knob=new System.Windows.Shapes.Ellipse{Width=18,Height=18,Fill=Brushes.White,VerticalAlignment=VerticalAlignment.Center};
  var dock=new DockPanel{LastChildFill=false};DockPanel.SetDock(knob,Dock.Left);dock.Children.Add(knob);track.Child=dock;
  bool state=on;knob.Margin=new Thickness(on?23:3,0,0,0);
  track.MouseLeftButtonDown+=(_,_)=>{state=!state;track.Background=Brush(state?"#1E6FE8":"#BBBBBB");knob.Margin=new Thickness(state?23:3,0,0,0);changed(state);};
  return track;
 }
 UIElement SwitchWithLabel(bool on,Action<bool> changed) {
  var panel=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};var label=new TextBlock{Text=on?"已启用":"已禁用",FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0),Foreground=Brush(on?"#1E6FE8":"#888888")};
  var track=Switch(on,_=>{label.Text=_.ToString()=="True"?"已启用":"已禁用";label.Foreground=Brush(_?"#1E6FE8":"#888888");changed(_);});
  panel.Children.Add(track);panel.Children.Add(label);return panel;
 }
 void Overview() {
  var online=ReadModel.B(gateway?["rpc"],"ok")==true;
  body.Children.Add(Card(store.Settings.Appearance.WelcomeText,Text("这里是你的 OpenClaw 启动器。选择实例，管理扩展，然后开始工作。",13),Row(Button("下载 OpenClaw",()=>Navigate("版本下载"),true),Button("软件连接",()=>Navigate("软件连接")),Button("个性化你的启动器",()=>Navigate("个性化")))));
  body.Children.Add(Card("实例状态",GatewaySummary(),Text("安装目录："+current.Runtime,11,"#888888"),Text(SafeLog.Clean(ReadModel.S(gateway?["rpc"],"error",online?"网关 RPC 检查通过。":"点击刷新检查当前实例，或先完成运行配置。")),12),Row(Button("刷新",()=>{if(!busy)_ = Refresh();}),AsyncButton("停止",StopGateway),AsyncButton("重启",async()=>{await StopGateway();await StartGateway();}),AsyncButton("控制台",OpenDashboard))));
  body.Children.Add(Card("启动器",Text("OCL "+Brand.Version+"  ·  PCL 风格 OpenClaw 独立启动器",12),Text("PCL 原作者：龙腾猫跃。本程序为第三方独立二次创作。",11,"#999999")));
 }
 UIElement InstanceList() {
  var stack=new StackPanel();foreach(var item in store.Settings.Instances.ToList()) {var dock=new DockPanel{Margin=new Thickness(0,0,0,8)};
   // 每实例「设置」：选中该实例并进入其专属实例设置页（仿 PCL 每个存档独立的设置入口）。
   var settings=Button("设置",()=>{if(busy)return;instancePicker.SelectedItem=item;Navigate("实例设置");});DockPanel.SetDock(settings,Dock.Right);dock.Children.Add(settings);
   var choose=Button(item==current?"当前实例":"选择",()=>{instancePicker.SelectedItem=item;});DockPanel.SetDock(choose,Dock.Right);dock.Children.Add(choose);
   var labels=new StackPanel();labels.Children.Add(Text(item.Name,13,"#444444"));labels.Children.Add(Text(item.Version+"  ·  "+(item.Managed?"独立配置":"已有配置"),11,"#999999"));dock.Children.Add(labels);stack.Children.Add(dock);}return stack;
 }
 void AnimateContent() {if(screenshot||!store.Settings.Appearance.Animations)return;body.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(170)));var move=new TranslateTransform();body.RenderTransform=move;move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(8,0,TimeSpan.FromMilliseconds(210)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}});}
 UIElement BrandPair(double size,double gap) {
  var pair=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,14)};
  pair.Children.Add(new Image{Source=Brand.Image(),Width=size,Height=size,Stretch=Stretch.Fill,Margin=new Thickness(0,0,gap,0),ToolTip="OCL"});
  var source=new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","openclaw.png")));
  // Trim the supplied character's transparent padding so the visible marks share identical bounds.
  var character=new System.Windows.Media.Imaging.CroppedBitmap(source,new Int32Rect(55,115,1144,1038));
  pair.Children.Add(new Image{Source=character,Width=size,Height=size,Stretch=Stretch.Fill,ToolTip="OpenClaw"});return pair;
 }
 internal void ShowSplashFrame() {
  splash.Children.Clear();splash.Visibility=Visibility.Visible;splash.Opacity=1;var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};stack.Children.Add(BrandPair(156,32));var text=Text("OpenClaw Launcher",15,"#777777");text.HorizontalAlignment=HorizontalAlignment.Center;text.Margin=new Thickness(0,23,0,0);stack.Children.Add(text);splash.Children.Add(stack);
 }
 async Task PlayOpening() {
  if(!store.Settings.Appearance.Splash)return;ShowSplashFrame();var content=splash.Children[0];content.RenderTransformOrigin=new Point(.5,.5);var transform=new TransformGroup();var rotate=new RotateTransform(-5);var scale=new ScaleTransform(.78,.78);transform.Children.Add(rotate);transform.Children.Add(scale);content.RenderTransform=transform;
  if(store.Settings.Appearance.Animations){var duration=TimeSpan.FromMilliseconds(550);scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.78,1,duration){EasingFunction=new BackEase{Amplitude=.25,EasingMode=EasingMode.EaseOut}});scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.78,1,duration){EasingFunction=new BackEase{Amplitude=.25,EasingMode=EasingMode.EaseOut}});rotate.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(-5,0,duration));}else content.RenderTransform=Transform.Identity;
  await Task.Delay(850);if(store.Settings.Appearance.Animations){splash.BeginAnimation(OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(230)));await Task.Delay(240);}splash.Visibility=Visibility.Collapsed;splash.BeginAnimation(OpacityProperty,null);splash.Opacity=1;
 }
 void AboutPage() {
  var pcl=Button("打赏 PCL 原作者",()=>OpenLink("https://meloong.com/afd/a/LTCat"));pcl.Padding=new Thickness(14,6,14,6);
  var ocl=Button("打赏 OCL 作者",OpenDonate);ocl.Padding=new Thickness(14,6,14,6);ocl.FontWeight=FontWeights.SemiBold;
  ocl.Background=Brush("#1E6FE8");ocl.Foreground=Brushes.White;ocl.BorderBrush=Brush("#1E6FE8");
  body.Children.Add(Card("关于 OCL",new Image{Source=Brand.Image(),Width=80,Height=85,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12)},Text("PCL · OpenClaw Launcher",20,"#555555"),Text(Brand.Version+"  ·  Windows 桌面版"),Text("OCL 作者：Twinight_Miner",16,"#D9363E"),Text("PCL 原作者：龙腾猫跃",14),Row(pcl,ocl,Button("PCL 项目",()=>OpenLink("https://github.com/Meloong-Git/PCL"))),Text("如果 OCL 帮到你，欢迎打赏支持一下（点蓝色按钮查看收款码）。",12,"#1E6FE8"),Text("第三方基于 PCL 独立二次创作。界面结构与控件外观参照 PCL 源码，OpenClaw 管理逻辑独立实现。没有 Minecraft 启动、账号、模组、Java 或游戏资源功能。"),Text("完整源码和来源说明随程序提供。DSHL 源码未公开，未反编译其私有实现。",12)));
 }
}
