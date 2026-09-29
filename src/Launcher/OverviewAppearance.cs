using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace ClawLauncher;
public sealed partial class MainWindow
{
 Button overviewToggle=null!;
 bool overviewCollapsed;
 string GatewayColor=>gateway==null?"#999999":ReadModel.B(gateway["rpc"],"ok")==true?"#248B69":"#D9363E";
 string GatewayLabel=>gateway==null?"状态尚未检查":ReadModel.B(gateway["rpc"],"ok")==true?"网关已就绪":"网关未就绪";
 void ApplyOverviewVisibility(){
  overviewToggle.Visibility=page=="启动总览"?Visibility.Visible:Visibility.Collapsed;
  overviewToggle.Content=overviewCollapsed?"展开 ▾":"收起 ▴";
  overviewToggle.ToolTip=overviewCollapsed?"展开启动总览功能区域":"收起功能区域，查看背景图片";
  contentPane.Visibility=page=="启动总览"&&overviewCollapsed?Visibility.Collapsed:Visibility.Visible;
 }
 UIElement GatewaySummary(){
  var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,9)};
  row.Children.Add(new TextBlock{Text="版本："+current.Version+"    ·    "+GatewayLabel,FontSize=13,Foreground=Brush("#555555"),VerticalAlignment=VerticalAlignment.Center});
  row.Children.Add(new Ellipse{Width=10,Height=10,Fill=Brush(GatewayColor),Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center,ToolTip=GatewayLabel});
  return row;
 }
 void ApplyTitleContrast(Color color){
  var foreground=(.2126*color.R+.7152*color.G+.0722*color.B)>160?Brush("#202020"):Brushes.White;
  void Visit(DependencyObject node){
   if(node is TextBlock text)text.Foreground=foreground;
   if(node is Button button)button.Foreground=foreground;
   if(node is System.Windows.Shapes.Path path)path.Fill=foreground;
   foreach(var child in LogicalTreeHelper.GetChildren(node))if(child is DependencyObject element)Visit(element);
  }
  if(titleBar.Child!=null)Visit(titleBar.Child);
 }
}
