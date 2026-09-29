using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
namespace ClawLauncher;
public sealed record InstallProgress(string Stage,double Percent,string Line="");
public static class RuntimeInstall
{
 public static async Task<string> Install(Store store,Runner runner,string version,CancellationToken cancellation=default,IProgress<InstallProgress>? progress=null,Release? release=null) {
  if(!ReleaseCatalog.ValidVersion(version))throw new Exception("无效的 OpenClaw 版本号。");
  var versions=Path.Combine(store.Root,"versions");var target=Path.Combine(versions,version);var runtime=Path.Combine(target,"node_modules","openclaw");
  progress?.Report(new("正在检查运行环境",5));
  var node=ResolveExecutable(runner.Node);var npm=Path.Combine(Path.GetDirectoryName(node)!,"node_modules","npm","bin","npm-cli.js");
  if(!File.Exists(npm))throw new Exception("Node.js 目录中未找到 npm，请在运行设置中选择完整的 Node.js 安装。");
  if(await IsReady(node,runtime,version,cancellation)){progress?.Report(new("已安装此版本",100));return runtime;}
  cancellation.ThrowIfCancellationRequested();
  if(release==null){var catalog=await ReleaseCatalog.Fetch(runner,Path.Combine(store.Root,"cache","openclaw-versions.json"),cancellation);release=catalog.Releases.FirstOrDefault(r=>r.Version==version)??throw new Exception("官方仓库没有此版本，请从版本列表选择。");}
  progress?.Report(new("正在下载官方安装包并校验",15));var archive=await ArchiveDownload.Download(store,runner,release,cancellation);
  Directory.CreateDirectory(versions);var staging=Path.Combine(versions,".installing-"+version+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
  var info=new ProcessStartInfo(node){WorkingDirectory=staging,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  foreach(var arg in new[]{npm,"install","--prefix",staging,"--no-audit","--no-fund","--registry=https://registry.npmjs.org","--loglevel=info",archive})info.ArgumentList.Add(arg);
  info.Environment["npm_config_engine_strict"]="true";
  info.Environment["npm_config_cache"]=Path.Combine(store.Root,"cache","npm");
  info.Environment["npm_config_update_notifier"]="false";
  // Older OpenClaw releases depend on public libsignal repositories via GitHub SSH URLs.
  // Use HTTPS only for this installer process; never mutate the user's global Git config.
  var gitCount=info.Environment.TryGetValue("GIT_CONFIG_COUNT",out var countText)&&int.TryParse(countText,out var inherited)?inherited:0;
  void GitConfig(string key,string value){info.Environment["GIT_CONFIG_KEY_"+gitCount]=key;info.Environment["GIT_CONFIG_VALUE_"+gitCount]=value;gitCount++;}
  GitConfig("url.https://github.com/.insteadOf","ssh://git@github.com/");GitConfig("url.https://github.com/.insteadOf","git@github.com:");GitConfig("http.sslBackend","openssl");
  info.Environment["GIT_CONFIG_COUNT"]=gitCount.ToString();info.Environment["GIT_TERMINAL_PROMPT"]="0";
  progress?.Report(new("正在下载 OpenClaw 与依赖",15,"安装 openclaw@"+version));
  try {
   var result=await ExecuteInstall(info,progress,cancellation);if(!result.Ok)throw new Exception(result.Summary);
   cancellation.ThrowIfCancellationRequested();progress?.Report(new("正在验证程序入口",88));var stagedRuntime=Path.Combine(staging,"node_modules","openclaw");
   if(!await IsReady(node,stagedRuntime,version,cancellation))throw new Exception("软件包下载完成，但版本入口检查未通过。没有登记为已安装。临时目录："+staging);
   File.WriteAllText(Path.Combine(staging,"ocl-install.json"),System.Text.Json.JsonSerializer.Serialize(new{version,installedAt=DateTimeOffset.Now,source="https://registry.npmjs.org/openclaw"}));
   cancellation.ThrowIfCancellationRequested();
   // Preserve any previous incomplete directory; only publish after successful CLI verification.
   if(Directory.Exists(target))Directory.Move(target,Path.Combine(versions,".previous-"+version+"-"+Guid.NewGuid().ToString("N")));
   Directory.Move(staging,target);progress?.Report(new("安装完成",100));return runtime;
  }catch{progress?.Report(new("安装未完成",0,"临时文件保留于："+staging));throw;}
 }
 public static async Task<bool> IsReady(string node,string runtime,string version,CancellationToken token=default) {
  var entry=Path.Combine(runtime,"openclaw.mjs");if(!File.Exists(entry))return false;
  try {
   var package=JsonNode.Parse(File.ReadAllText(Path.Combine(runtime,"package.json")));if(package?["name"]?.ToString()!="openclaw"||package?["version"]?.ToString()!=version)return false;
   var psi=new ProcessStartInfo(node){WorkingDirectory=runtime,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true};psi.ArgumentList.Add(entry);psi.ArgumentList.Add("--version");
   var result=await Runner.Execute(psi,60,token);return result.Ok&&result.Output.Contains(version,StringComparison.Ordinal);
  }catch(OperationCanceledException){throw;}catch{return false;}
 }
 static async Task<CommandResult> ExecuteInstall(ProcessStartInfo info,IProgress<InstallProgress>? progress,CancellationToken cancellation) {
  using var process=new Process{StartInfo=info};var text=new StringBuilder();var sync=new object();
  async Task Read(StreamReader stream){while(await stream.ReadLineAsync() is { } raw){var line=SafeLog.Clean(raw);lock(sync){text.AppendLine(line);if(text.Length>40000)text.Remove(0,text.Length-30000);}progress?.Report(new("正在安装程序与依赖",45,line));}}
  process.Start();process.StandardInput.Close();var output=Read(process.StandardOutput);var error=Read(process.StandardError);
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(20));
  try{await process.WaitForExitAsync(timeout.Token);await Task.WhenAll(output,error);return new(process.ExitCode,text.ToString(),process.ExitCode==0?"":text.ToString());}
  catch(OperationCanceledException){try{process.Kill(true);}catch(InvalidOperationException){}await process.WaitForExitAsync();await Task.WhenAll(output,error);return new(-1,"","安装已取消或超时。未创建实例；重试会使用新的临时目录。",true);}
 }
 public static string ResolveExecutable(string name) {
  if(Path.IsPathFullyQualified(name)&&File.Exists(name))return name;
  foreach(var folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)){var candidate=Path.Combine(folder,name);if(File.Exists(candidate))return candidate;}
  throw new Exception("找不到 Node.js，请在运行设置中选择 node.exe。");
 }
}
