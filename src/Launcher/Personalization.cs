using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace ClawLauncher;
public sealed partial class MainWindow
{
 readonly MediaPlayer media=new();
 int trackIndex=-1;bool musicPaused;bool mediaInitialized;
 TextBlock? trackLabel;
 // 0.8.8：壁纸模糊烘焙缓存——只在图片或模糊值真正变化时才重新解码/烘焙，避免每次改外观都重做。
 string lastWallpaperPath="";double lastWallpaperBlur=-1;
 void ApplyAppearance() {
  var a=store.Settings.Appearance;Color accent;try{accent=(Color)ColorConverter.ConvertFromString(a.Accent);}catch{accent=Color.FromRgb(200,50,60);}
  Resources["AccentBrush"]=new SolidColorBrush(accent);Resources["AccentSoft"]=new SolidColorBrush(Color.FromArgb(25,accent.R,accent.G,accent.B));
  // The selected theme color also controls the title bar.
  titleBar.Background=new SolidColorBrush(Color.FromRgb(accent.R,accent.G,accent.B));
  ApplyTitleContrast(accent);
  if(titleBar.Child is Grid titleGrid&&titleGrid.Children[0] is TextBlock titleLabel)titleLabel.Text=a.TitleText;
  Resources["CardBrush"]=new SolidColorBrush(Color.FromArgb((byte)(255*Math.Clamp(a.CardOpacity,.2,1)),255,255,255));sidebarSurface.Background=new SolidColorBrush(Color.FromArgb((byte)(255*Math.Clamp(a.SidebarOpacity,.1,1)),255,255,255));
  FontSize=Math.Clamp(a.FontSize,11,17);wallpaper.Opacity=Math.Clamp(a.BackgroundOpacity,0,1);
  wallpaper.Stretch=Enum.TryParse<Stretch>(a.BackgroundFit,out var stretch)?stretch:Stretch.UniformToFill;
  RenderWallpaper();
  media.Volume=Math.Clamp(a.MusicVolume,0,1);
  if(!mediaInitialized){mediaInitialized=true;media.MediaEnded+=(_,_)=>{if(store.Settings.Appearance.MusicRepeat||trackIndex+1<store.Settings.Appearance.Playlist.Count)NextTrack();else{media.Stop();musicBadge.Text="♫  播放结束";}};media.MediaFailed+=(_,e)=>{media.Stop();musicBadge.Text="♫  播放失败";if(trackLabel!=null)trackLabel.Text="无法播放："+e.ErrorException.Message;AddLog("音乐播放失败："+e.ErrorException.Message);};}
 }
 void SaveAppearance(){store.Save();ApplyAppearance();}
 // 0.8.8：把模糊烘焙进一张冻结的位图，稳态下窗口不再挂实时 BlurEffect——
 // 否则每当界面有任何重绘（切页动画、状态灯更新、hover）都要对整张壁纸重做高斯模糊，
 // 全屏大图 + 大半径模糊会拖垮整条渲染管线，连带让桌面动态壁纸都卡成幻灯片。
 // 拖动模糊滑块时仍是实时预览（便宜），松手再烘焙成静态并卸掉实时特效。
 void RenderWallpaper() {
  var a=store.Settings.Appearance;var path=a.BackgroundImage;
  // 稳态绝不挂实时特效（实时 BlurEffect 是「整屏重绘就卡」的根因）。
  wallpaper.Effect=null;
  // 大图缩放按低质量合成，显著降低壁纸参与每帧合成的开销。
  RenderOptions.SetBitmapScalingMode(wallpaper,BitmapScalingMode.LowQuality);
  if(path.Length==0||!File.Exists(path)){wallpaper.Source=null;lastWallpaperPath="";lastWallpaperBlur=-1;return;}
  var blur=Math.Clamp(a.BackgroundBlur,0,40);
  if(path==lastWallpaperPath&&Math.Abs(blur-lastWallpaperBlur)<0.01&&wallpaper.Source!=null)return;
  lastWallpaperPath=path;lastWallpaperBlur=blur;
  BitmapSource? final=null;
  try {
   var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=1280;bitmap.UriSource=new Uri(Path.GetFullPath(path));bitmap.EndInit();bitmap.Freeze();
   if(bitmap.PixelWidth>0&&bitmap.PixelHeight>0)final=bitmap;
  } catch(Exception e){AddLog("背景图片无法读取："+e.Message);}
  // 关键：模糊烘焙失败也绝不能让壁纸消失——回退到未模糊的原图。
  // （上一版把烘焙异常直接 Source=null，结果壁纸整张不显示，这是本版修掉的回归。）
  if(final!=null&&blur>0.5) {
   try { var baked=BakeBlur(final,blur); if(baked!=null)final=baked; }
   catch(Exception e){AddLog("背景模糊烘焙失败，已改用原图显示："+e.Message);}
  }
  wallpaper.Source=final;
 }
 // 用 Image 控件（UIElement）烘焙，比 DrawingVisual+Effect 稳；失败返回 null，由调用方回退原图。
 static BitmapSource? BakeBlur(BitmapSource source,double radius) {
  var image=new Image{Source=source,Stretch=Stretch.None,Effect=new System.Windows.Media.Effects.BlurEffect{Radius=radius}};
  var size=new Size(source.PixelWidth,source.PixelHeight);
  image.Measure(size);image.Arrange(new Rect(size));
  var rtb=new RenderTargetBitmap(source.PixelWidth,source.PixelHeight,96,96,PixelFormats.Pbgra32);
  rtb.Render(image);rtb.Freeze();
  return rtb.PixelWidth>0&&rtb.PixelHeight>0?rtb:null;
 }
 // 0.8.8：模糊滑块拖动时实时预览（临时挂 BlurEffect），松手烘焙成静态并卸掉特效。
 UIElement BlurControl() {
  var a=store.Settings.Appearance;var label=Text("背景模糊  "+a.BackgroundBlur.ToString("0.##"),12);
  var slider=new Slider{Minimum=0,Maximum=40,Value=Math.Clamp(a.BackgroundBlur,0,40),SmallChange=0.4,LargeChange=4};
  slider.ValueChanged+=(_,e)=>{label.Text="背景模糊  "+e.NewValue.ToString("0.##");a.BackgroundBlur=e.NewValue;wallpaper.Effect=e.NewValue<=0.01?null:new System.Windows.Media.Effects.BlurEffect{Radius=e.NewValue};};
  slider.PreviewMouseLeftButtonUp+=(_,_)=>{RenderWallpaper();store.Save();};
  slider.LostKeyboardFocus+=(_,_)=>{RenderWallpaper();store.Save();};
  var stack=new StackPanel();stack.Children.Add(label);stack.Children.Add(slider);return stack;
 }
 UIElement SliderRow(string title,double value,double min,double max,Action<double> change,string suffix="") {
  var stack=new StackPanel();var label=Text(title+"  "+value.ToString("0.##")+suffix,12);var slider=new Slider{Minimum=min,Maximum=max,Value=Math.Clamp(value,min,max),SmallChange=(max-min)/100,LargeChange=(max-min)/10};slider.ValueChanged+=(_,e)=>{label.Text=title+"  "+e.NewValue.ToString("0.##")+suffix;change(e.NewValue);};slider.PreviewMouseLeftButtonUp+=(_,_)=>store.Save();slider.LostKeyboardFocus+=(_,_)=>store.Save();stack.Children.Add(label);stack.Children.Add(slider);return stack;
 }
 CheckBox Toggle(string title,bool value,Action<bool> changed){var check=new CheckBox{Content=title,IsChecked=value};check.Checked+=(_,_)=>changed(true);check.Unchecked+=(_,_)=>changed(false);return check;}
 void AppearancePage() {
  pageNote.Text="背景、透明度、强调色、文字和动画都可以自由调整，修改即时预览。";var a=store.Settings.Appearance;
  var picture=Text(a.BackgroundImage.Length>0?Path.GetFileName(a.BackgroundImage):"未设置背景图片",12,"#888888");
  var fitNames=new[]{"裁剪铺满","完整显示","拉伸铺满","原始尺寸"};var fitValues=new[]{"UniformToFill","Uniform","Fill","None"};var fit=new ComboBox{Width=170,ItemsSource=fitNames,SelectedIndex=Math.Max(0,Array.IndexOf(fitValues,a.BackgroundFit)),HorizontalAlignment=HorizontalAlignment.Left};fit.SelectionChanged+=(_,_)=>{a.BackgroundFit=fitValues[Math.Max(0,fit.SelectedIndex)];SaveAppearance();};
  body.Children.Add(Card("背景图片",picture,Row(Button("选择背景图片",()=>{
   var dialog=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"};if(dialog.ShowDialog(this)!=true)return;
   var probe=new BitmapImage();probe.BeginInit();probe.CacheOption=BitmapCacheOption.OnLoad;probe.UriSource=new Uri(dialog.FileName);probe.EndInit();
   var folder=Path.Combine(store.Root,"appearance");Directory.CreateDirectory(folder);var dest=Path.Combine(folder,Guid.NewGuid().ToString("N")+Path.GetExtension(dialog.FileName));File.Copy(dialog.FileName,dest);a.BackgroundImage=dest;SaveAppearance();picture.Text=Path.GetFileName(dialog.FileName);
  },true),Button("清除背景",()=>{a.BackgroundImage="";SaveAppearance();picture.Text="未设置背景图片";})),Text("填充方式：裁剪铺满 / 完整显示 / 拉伸 / 原尺寸",11,"#999999"),fit,
   SliderRow("图片可见度",a.BackgroundOpacity*100,0,100,v=>{a.BackgroundOpacity=v/100;wallpaper.Opacity=v/100;},"%"),BlurControl()));
  body.Children.Add(Card("透明与颜色",SliderRow("卡片不透明度",a.CardOpacity*100,20,100,v=>{a.CardOpacity=v/100;Resources["CardBrush"]=new SolidColorBrush(Color.FromArgb((byte)(255*v/100),255,255,255));},"%"),SliderRow("侧栏不透明度",a.SidebarOpacity*100,10,100,v=>{a.SidebarOpacity=v/100;sidebarSurface.Background=new SolidColorBrush(Color.FromArgb((byte)(255*v/100),255,255,255));},"%"),Palette()));
  var title=Input(a.TitleText,300);var welcome=Input(a.WelcomeText,500);
  body.Children.Add(Card("自定义文字",Text("左上角标题"),title,Text("主页欢迎文字"),welcome,Button("保存文字",()=>{a.TitleText=title.Text.Trim();a.WelcomeText=welcome.Text.Trim();store.Save();if(titleBar.Child is Grid g&&g.Children[0] is TextBlock t)t.Text=a.TitleText;AddLog("个性化文字已保存。");})));
  body.Children.Add(Card("动画与显示",Toggle("开启 OCL 六边形开场动画",a.Splash,v=>{a.Splash=v;store.Save();}),Toggle("开启页面切换及开场动效",a.Animations,v=>{a.Animations=v;store.Save();}),SliderRow("界面字号",a.FontSize,11,17,v=>{a.FontSize=v;FontSize=v;}),Row(AsyncButton("预览开场动画",PlayOpening),Button("恢复默认外观",()=>{var music=a.Playlist;var vol=a.MusicVolume;store.Settings.Appearance=new Appearance{Playlist=music,MusicVolume=vol};SaveAppearance();SelectPage("个性化");}))));
 }
 UIElement Palette() {
  var input=Input(store.Settings.Appearance.Accent,105);
  var row=new WrapPanel();foreach(var color in new[]{"#C8323C","#F06434","#C54C83","#6F58BA","#3476B8","#348572"}) {
   var button=Button("",()=>{store.Settings.Appearance.Accent=color;input.Text=color;SaveAppearance();UpdateNavigation();});button.Width=32;button.Height=30;button.Background=Brush(color);button.BorderThickness=new Thickness(0);button.ToolTip=color;row.Children.Add(button);
  }
  row.Children.Add(input);row.Children.Add(Button("应用色值",()=>{var color=(Color)ColorConverter.ConvertFromString(input.Text.Trim());color.A=255;store.Settings.Appearance.Accent=color.ToString();SaveAppearance();UpdateNavigation();}));return row;
 }
 void PlayTrack(int index) {
  var tracks=store.Settings.Appearance.Playlist;if(tracks.Count==0){musicBadge.Text="♫  尚未添加音乐";return;}
  index=((index%tracks.Count)+tracks.Count)%tracks.Count;var path=tracks[index];if(!File.Exists(path)){AddLog("音乐文件不存在："+path);musicBadge.Text="♫  文件不存在";return;}
  trackIndex=index;musicPaused=false;media.Open(new Uri(Path.GetFullPath(path)));media.Volume=store.Settings.Appearance.MusicVolume;media.Play();musicBadge.Text="♫  "+Path.GetFileNameWithoutExtension(path);if(trackLabel!=null)trackLabel.Text="正在播放："+Path.GetFileName(path);
 }
 void NextTrack(){var count=store.Settings.Appearance.Playlist.Count;if(count==0)return;PlayTrack(store.Settings.Appearance.MusicShuffle?Random.Shared.Next(count):trackIndex+1);}
 void MusicPage() {
  pageNote.Text="本地音乐播放列表，支持启动自动播放、循环、随机播放和音量调节。";var a=store.Settings.Appearance;
  trackLabel=Text(trackIndex>=0&&trackIndex<a.Playlist.Count?"当前曲目："+Path.GetFileName(a.Playlist[trackIndex]):"尚未播放音乐",13);
  var list=new ListBox{ItemsSource=a.Playlist.Select(Path.GetFileName).ToList(),Height=170,BorderThickness=new Thickness(0),Background=Brushes.Transparent};if(trackIndex>=0&&trackIndex<a.Playlist.Count)list.SelectedIndex=trackIndex;
  list.MouseDoubleClick+=(_,_)=>{if(list.SelectedIndex>=0)PlayTrack(list.SelectedIndex);};
  body.Children.Add(Card("背景音乐",trackLabel,Row(Button("上一首",()=>PlayTrack(trackIndex-1)),Button("播放",()=>PlayTrack(list.SelectedIndex>=0?list.SelectedIndex:0),true),Button("暂停 / 继续",()=>{if(musicPaused){media.Play();musicPaused=false;}else{media.Pause();musicPaused=true;}}),Button("下一首",NextTrack),Button("停止",()=>{media.Stop();musicPaused=false;musicBadge.Text="♫  已停止";trackLabel.Text="播放已停止";})),SliderRow("音量",a.MusicVolume*100,0,100,v=>{a.MusicVolume=v/100;media.Volume=v/100;},"%"),Toggle("启动时自动播放",a.MusicAutoPlay,v=>{a.MusicAutoPlay=v;store.Save();}),Toggle("循环播放列表",a.MusicRepeat,v=>{a.MusicRepeat=v;store.Save();}),Toggle("随机播放",a.MusicShuffle,v=>{a.MusicShuffle=v;store.Save();})));
  body.Children.Add(Card("播放列表",list,Row(Button("添加音乐",()=>{var dialog=new OpenFileDialog{Filter="音乐|*.mp3;*.wav;*.m4a;*.wma;*.aac|所有文件|*.*",Multiselect=true};if(dialog.ShowDialog(this)!=true)return;foreach(var path in dialog.FileNames)if(!a.Playlist.Contains(path,StringComparer.OrdinalIgnoreCase))a.Playlist.Add(path);store.Save();SelectPage(page);}),Button("移除所选",()=>{if(list.SelectedIndex<0)return;media.Stop();a.Playlist.RemoveAt(list.SelectedIndex);trackIndex=-1;musicBadge.Text="♫  已停止";store.Save();SelectPage(page);})),Text("使用 Windows 媒体解码器；不支持的编码会在日志中显示。音乐文件保持原位置。",11,"#999999")));
 }
 void ShortcutIconPage() {
  pageTitle.Text="快捷方式图标";
  pageNote.Text="选择桌面快捷方式（OCL.lnk）显示的图标；点「确定并应用」后立即更换，无需重启启动器。";
  var chosen=store.Settings.Appearance.ShortcutIcon;
  Border? cardOcl=null,cardOpen=null;
  var oclSrc=(ImageSource)Brand.Image();
  var openSrc=new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","openclaw.png")));
  Border Make(string key,ImageSource art,string caption) {
   var border=new Border{Width=132,Height=152,Margin=new Thickness(0,0,18,0),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(3),Background=Brushes.White,Padding=new Thickness(12,12,12,10),Cursor=Cursors.Hand};
   var stack=new StackPanel{Orientation=Orientation.Vertical};
   var holder=new Border{Width=86,Height=86,Margin=new Thickness(0,0,0,9),HorizontalAlignment=HorizontalAlignment.Center};holder.Child=new Image{Source=art,Width=86,Height=86,Stretch=Stretch.Uniform};stack.Children.Add(holder);
   stack.Children.Add(Text(caption,13,"#444444"));
   border.Child=stack;border.MouseLeftButtonDown+=(_,_)=>{chosen=key;Sync();};
   var tick=new TextBlock{Text="✓ 已选",FontSize=11,Foreground=Brush(store.Settings.Appearance.Accent),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,4,0,0)};stack.Children.Add(tick);
   return border;
  }
  void Sync() {
   if(cardOcl!=null)cardOcl.BorderBrush=Brush(chosen=="ocl"?store.Settings.Appearance.Accent:"#DDDDDD");
   if(cardOpen!=null)cardOpen.BorderBrush=Brush(chosen=="openclaw"?store.Settings.Appearance.Accent:"#DDDDDD");
  }
  cardOcl=Make("ocl",oclSrc,"OCL（六边形）");
  cardOpen=Make("openclaw",openSrc,"OpenClaw（角色）");
  var row=new WrapPanel{Children={cardOcl,cardOpen}};
  body.Children.Add(Card("选择图标",Text("OCL 启动器自带两种图标，挑一个作为桌面快捷方式。下一次启动也会自动按此设置重写。",12),row));
  body.Children.Add(Card("应用",Text("确定后立即把桌面 OCL.lnk 的图标换成所选样式；若要换回程序默认图标，选「恢复默认」再应用。",12),Row(AsyncButton("确定并应用",async()=>{store.Settings.Appearance.ShortcutIcon=chosen;store.Save();store.EnsureDesktopShortcut();AddLog("桌面快捷方式图标已切换为："+(chosen=="openclaw"?"OpenClaw 角色":chosen=="ocl"?"OCL 六边形":"程序默认"));status.Text="桌面图标已更换 · "+DateTime.Now.ToString("HH:mm:ss");},true),Button("恢复默认（跟随程序）",()=>{chosen="";Sync();}))));
  Sync();
 }
}
