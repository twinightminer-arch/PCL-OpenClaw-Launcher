using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace ClawLauncher;
public sealed partial class MainWindow
{
 ReleaseCatalog? catalog;
 string catalogNotice="";
 string CatalogPath=>Path.Combine(store.Root,"cache","openclaw-versions.json");
 async Task LoadCatalog() {
  status.Text="正在获取 OpenClaw 官方版本列表…";
  try{catalog=await ReleaseCatalog.Fetch(runner,CatalogPath,cancellation?.Token??default);catalogNotice="官方 npm · "+catalog.Releases.Count+" 个版本 · "+catalog.Fetched.ToLocalTime().ToString("yyyy/MM/dd HH:mm");}
  catch(OperationCanceledException){throw;}
  catch(Exception e){if(cancellation?.IsCancellationRequested==true)throw new OperationCanceledException();catalog=ReleaseCatalog.ReadCache(CatalogPath)??ReleaseCatalog.ReadCache(Path.Combine(AppContext.BaseDirectory,"Assets","catalog-snapshot.json"));if(catalog==null)throw;catalogNotice="网络不可用，显示 "+catalog.Fetched.ToLocalTime().ToString("yyyy/MM/dd HH:mm")+" 的缓存";AddLog(e.Message);}
  SelectPage("版本下载");
 }
 void DownloadPage() {
  catalog??=ReleaseCatalog.ReadCache(CatalogPath)??ReleaseCatalog.ReadCache(Path.Combine(AppContext.BaseDirectory,"Assets","catalog-snapshot.json"));pageNote.Text=catalogNotice.Length>0?catalogNotice:catalog!=null?"官方目录快照 · "+catalog.Fetched.ToLocalTime().ToString("yyyy/MM/dd HH:mm")+" · 可刷新获取最新版本":"选择版本 → 设置实例 → 自动下载安装";
  if(catalog==null){body.Children.Add(Card("OpenClaw 自动安装",Text("从官方发布目录获取正式版、预览版和历史版本。安装完成后会自动加入本地实例。"),AsyncButton("获取版本列表",LoadCatalog,true)));return;}
  var latest=catalog.Releases.FirstOrDefault(r=>r.Version==catalog.Latest);
  if(latest!=null)body.Children.Add(Card("最新版本",ReleaseItem(latest,"最新正式版"),AsyncButton("仅下载官方安装包",async()=>{status.Text="正在下载并校验安装包…";var file=await ArchiveDownload.Download(store,runner,latest,cancellation?.Token??default);AddLog("已下载并通过 SHA-512 校验："+file);MessageBox.Show(this,"下载完成，SHA-512 校验通过：\n"+file,"官方安装包");})));
  var beta=catalog.Releases.FirstOrDefault(r=>r.Version==catalog.Beta);if(beta!=null&&beta.Version!=latest?.Version)body.Children.Add(Card("预览版本",ReleaseItem(beta,"最新预览版")));
  var query=Input("",250);query.ToolTip="输入版本号筛选，例如 2026.7";var channel=new ComboBox{Width=125,ItemsSource=new[]{"全部版本","正式版","预览版"},SelectedIndex=0};
  body.Children.Add(Row(query,channel,AsyncButton("刷新列表",LoadCatalog)));
  var results=new StackPanel();body.Children.Add(results);int limit=30;
  void Filter(){results.Children.Clear();var list=catalog.Releases.Where(r=>r.Version.Contains(query.Text.Trim(),StringComparison.OrdinalIgnoreCase)&&(channel.SelectedIndex==0||r.Channel==(string?)channel.SelectedItem)).ToList();
   foreach(var group in list.Take(limit).GroupBy(r=>r.Channel)) {
    var rows=new StackPanel();foreach(var release in group)rows.Children.Add(ReleaseItem(release));
    var expander=new Expander{Header=group.Key+"（"+group.Count()+"）",IsExpanded=group.Key=="正式版"||query.Text.Length>0,Content=rows,FontSize=13,Padding=new Thickness(0,2,0,0)};results.Children.Add(Card("",expander));
   }if(list.Count>limit)results.Children.Add(Button("加载更多版本（剩余 "+(list.Count-limit)+"）",()=>{limit+=30;Filter();}));if(list.Count==0)results.Children.Add(Card("没有匹配的版本",Text("换一个关键词或切换版本类型。")));
  }
  query.TextChanged+=(_,_)=>Filter();channel.SelectionChanged+=(_,_)=>Filter();Filter();
 }
 UIElement ReleaseItem(Release release,string? note=null) {
  var button=Button("",()=>_ = Operate(()=>InstallRelease(release)));button.Margin=new Thickness(0,0,0,4);button.Padding=new Thickness(8,7,8,7);button.BorderThickness=new Thickness(0);button.Background=Brushes.Transparent;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
  var row=new DockPanel();var right=new TextBlock{Text="↓  安装",FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,5,0)};right.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");DockPanel.SetDock(right,Dock.Right);row.Children.Add(right);
  var logo=new Image{Source=Brand.Image(),Width=29,Height=33,Margin=new Thickness(0,0,12,0)};DockPanel.SetDock(logo,Dock.Left);row.Children.Add(logo);var labels=new StackPanel();labels.Children.Add(new TextBlock{Text=release.Version+(note!=null?"   "+note:release.Tag.Length>0?"   "+release.Tag:""),Foreground=Brush("#444444"),FontSize=13});labels.Children.Add(new TextBlock{Text=release.Date+"   ·   Node.js "+release.Node+(release.Deprecated.Length>0?"   ·   已弃用":""),FontSize=11,Foreground=Brush("#999999"),TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,4,0,0)});row.Children.Add(labels);button.Content=row;return button;
 }
 async Task InstallRelease(Release release) {
  var dialog=new Window{Owner=this,Title="安装 OpenClaw "+release.Version,Width=500,Height=365,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var name=Input("OpenClaw "+release.Version,400);var port=Input(NextPort().ToString(),140);var stack=new StackPanel{Margin=new Thickness(25)};
  stack.Children.Add(Text("安装 OpenClaw "+release.Version,20,"#555555"));stack.Children.Add(Text(release.Channel+"  ·  Node.js "+release.Node,12));if(release.Deprecated.Length>0)stack.Children.Add(Text("已弃用："+release.Deprecated,11,"#B54545"));stack.Children.Add(Text("实例名称",12));stack.Children.Add(name);stack.Children.Add(Text("网关端口",12));stack.Children.Add(port);stack.Children.Add(Text("自动下载程序与依赖，并创建独立配置。不会启动网关。",11));stack.Children.Add(Row(Button("开始安装",()=>dialog.DialogResult=true,true),Button("取消",()=>dialog.DialogResult=false)));dialog.Content=stack;
  if(dialog.ShowDialog()!=true)return;if(!int.TryParse(port.Text,out var number)||number<1024||number>65535||store.Settings.Instances.Any(i=>i.Port==number))throw new Exception("请选择有效且未被其他实例登记的端口。");if(string.IsNullOrWhiteSpace(name.Text))throw new Exception("实例名称不能为空。");
  var progressWindow=new Window{Owner=this,Title="正在安装 OpenClaw",Width=560,Height=340,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var stage=Text("正在准备安装…",17,"#555555");var bar=new ProgressBar{Minimum=0,Maximum=100,Height=4,Margin=new Thickness(0,12,0,14)};bar.SetResourceReference(Control.ForegroundProperty,"AccentBrush");var lines=new TextBox{IsReadOnly=true,Height=150,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=11};var panel=new StackPanel{Margin=new Thickness(25)};panel.Children.Add(stage);panel.Children.Add(bar);panel.Children.Add(lines);panel.Children.Add(Button("取消安装",()=>cancellation?.Cancel()));progressWindow.Content=panel;bool finished=false;progressWindow.Closing+=(_,e)=>{if(!finished){cancellation?.Cancel();e.Cancel=!closing;stage.Text="正在取消并回收下载进程…";}};progressWindow.Show();
  var progress=new Progress<InstallProgress>(p=>{stage.Text=p.Stage;bar.IsIndeterminate=p.Percent>=15&&p.Percent<88;bar.Value=p.Percent;if(p.Line.Length>0){lines.AppendText(p.Line+Environment.NewLine);if(lines.Text.Length>20000)lines.Text=lines.Text[^16000..];lines.ScrollToEnd();AddLog(p.Line);}});
  try{
   var runtime=await RuntimeInstall.Install(store,runner,release.Version,cancellation?.Token??default,progress,release);
   cancellation?.Token.ThrowIfCancellationRequested();var added=store.Create(name.Text.Trim(),runtime,number);current=added;instancePicker.Items.Refresh();instancePicker.SelectedItem=added;gateway=null;plugins=[];skills=[];channels=[];checkedAt=null;AddLog("安装完成："+release.Version+"；已创建独立实例 "+added.Name);SelectPage("版本与实例");
  }finally{finished=true;progressWindow.Close();}
 }
 int NextPort(){var used=store.Settings.Instances.Select(i=>i.Port).ToHashSet();for(int port=18789;port<65535;port++)if(!used.Contains(port))return port;throw new Exception("没有可用的实例端口。");}
}
