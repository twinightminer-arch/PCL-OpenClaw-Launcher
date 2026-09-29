using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
namespace ClawLauncher;

public sealed partial class MainWindow
{
 TextBlock? launchStage;
 Action<string> openDashboardUrl=url=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
 void LaunchProgress(string message) {status.Text=message;if(launchStage!=null)launchStage.Text=message;AddLog(message);}
 async Task StartGateway() {
  var progress=new Window{Owner=this,Title="启动 OpenClaw",Width=550,Height=290,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
  launchStage=Text("正在准备启动…",17,"#D9363E");
  var details=Text("正在启动所选实例："+current.Name+"\n"+current.Runtime,12);
  var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(launchStage);panel.Children.Add(details);
  panel.Children.Add(new ProgressBar{IsIndeterminate=true,Height=5,Margin=new Thickness(0,15,0,15)});
  panel.Children.Add(Text("就绪后自动打开浏览器中的 OpenClaw 控制台。",12));
  panel.Children.Add(Button("取消启动",()=>cancellation?.Cancel()));progress.Content=panel;
  bool completed=false;progress.Closing+=(_,_)=>{if(!completed)cancellation?.Cancel();};
  progress.Show();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
  try {await StartGatewayCore();LaunchProgress("网关已就绪，正在打开 OpenClaw 控制台…");await OpenDashboard();LaunchProgress("OpenClaw 已启动，控制台已打开。");}
  catch(OperationCanceledException){
   if(owned.TryGetValue(current.Id,out var child)&&!child.HasExited){child.Kill(true);await child.WaitForExitAsync();owned.Remove(current.Id);child.Dispose();}
   throw;
  }
  finally{completed=true;launchStage=null;progress.Close();}
 }
 async Task OpenDashboard() {
  var result=await runner.Run(current,["dashboard","--json","--no-open"],45,cancellation?.Token??default);
  var data=result.Json();
  if(!result.Ok||ReadModel.B(data,"ok")!=true)throw new Exception("网关已启动，但控制台打开失败："+SafeLog.Clean(ReadModel.S(data,"reason",result.Summary)));
  var url=ReadModel.S(data,"url");
  if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))throw new Exception("OpenClaw 返回了无效的控制台地址。");
  // The official CLI resolves gateway configuration and authentication. Never log its credential-bearing URL.
  openDashboardUrl(url);
 }
}
