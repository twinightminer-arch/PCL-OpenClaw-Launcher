using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClawLauncher;

public sealed class Instance
{
 public string Id { get; set; } = Guid.NewGuid().ToString("N");
 public string Name { get; set; } = "现有 OpenClaw";
 public string Runtime { get; set; } = @"E:\openclaw";
 public string State { get; set; } = "";
 public string Config { get; set; } = "";
 public int Port { get; set; } = 18789;
 public bool Managed { get; set; }
 public override string ToString() => Name;
 public string Entry => Path.Combine(Runtime,"openclaw.mjs");
 public string Version { get { try { return JsonNode.Parse(File.ReadAllText(Path.Combine(Runtime,"package.json")))?["version"]?.ToString() ?? "未知"; } catch { return "未找到"; } } }
}
public sealed class Settings
{
 public string Node { get; set; } = "node.exe";
 public string Selected { get; set; } = "";
 public List<Instance> Instances { get; set; } = [];
 public Appearance Appearance { get; set; } = new();
 // 新实例默认携带的“必要插件/Skill”清单（照搬当前实例采集而来）。
 // 其余插件需要用户在实例内自行安装或制作。
 public List<string> DefaultPlugins { get; set; } = [];
 public List<string> DefaultSkills { get; set; } = [];
}
public sealed class Appearance
{
 public string BackgroundImage {get;set;}="";
 public double BackgroundOpacity {get;set;}=0.65;
 public double BackgroundBlur {get;set;}=0;
 public double CardOpacity {get;set;}=0.96;
 public double SidebarOpacity {get;set;}=0.97;
 public string BackgroundFit {get;set;}="UniformToFill";
 public string Accent {get;set;}="#D9363E";
 public string TitleText {get;set;}="OCL";
 public string WelcomeText {get;set;}="欢迎使用 OpenClaw Launcher！";
 public double FontSize {get;set;}=13;
 public bool Animations {get;set;}=true;
 public bool Splash {get;set;}=true;
 public bool MusicAutoPlay {get;set;}=false;
 public bool MusicShuffle {get;set;}=false;
 public bool MusicRepeat {get;set;}=true;
 public double MusicVolume {get;set;}=0.35;
 public List<string> Playlist {get;set;}=[];
 // 桌面快捷方式图标："ocl" / "openclaw"，空串表示跟随 exe 默认图标。
 public string ShortcutIcon {get;set;}="";
}
public sealed class Store
{
 public string Root { get; }
 public Settings Settings { get; }
 public Store(string? root = null) {
  var isDefault = root == null;
  Root = root ?? ResolveRoot(AppContext.BaseDirectory);
  Directory.CreateDirectory(Root);
  var file = Path.Combine(Root,"launcher.json");
  // 更新到新版本时自动保留上一版本的设置数据（背景媒体随 appearance 目录一并带过来）。
  // 仅对默认（真实）数据目录做迁移，显式传入的临时目录不受历史数据影响。
  if(isDefault && !File.Exists(file)) AdoptPreviousData();
  Settings = File.Exists(file) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) ?? new() : new();
  if (Settings.Instances.Count == 0) {
   Settings.Instances.Add(new Instance { State = Environment.GetEnvironmentVariable("OPENCLAW_STATE_DIR") ?? "", Config = Environment.GetEnvironmentVariable("OPENCLAW_CONFIG_PATH") ?? "" });
   Settings.Selected = Settings.Instances[0].Id;
  }
  // 背景图片与音乐文件复制进当前数据目录，保证换版本后素材仍在。
  if(isDefault) RelocateAssets();
 }
 // 稳定共享数据目录（%LOCALAPPDATA%\OCL）：跨版本保留设置、背景媒体与音乐；
 // 仍支持 data-location.txt 覆盖（开发布局）。
 public static string ResolveRoot(string directory) {
  var pointer=Path.Combine(directory,"data-location.txt");
  if(File.Exists(pointer)) {var path=File.ReadAllText(pointer).Trim();if(path.Length>0)return Path.GetFullPath(path,directory);}
  return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OCL");
 }
 // 候选旧数据目录：开发布局、同目录 Data、以及 %LOCALAPPDATA%\Programs\OCL-* 各版本。
 static IEnumerable<string> LegacyCandidates() {
  var baseDir=AppContext.BaseDirectory;
  yield return Path.Combine(baseDir,"..","app","Data");
  yield return Path.Combine(baseDir,"Data");
  var programs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs");
  if(!Directory.Exists(programs))yield break;
  foreach(var dir in Directory.GetDirectories(programs,"OCL*")) {
   yield return Path.Combine(dir,"Data");
   foreach(var sub in Directory.GetDirectories(dir,"app*"))yield return Path.Combine(sub,"Data");
  }
 }
 // 取最近一次写入的 launcher.json 作为“上一版本设置”，只迁移设置与 appearance（不搬 versions/instances 大目录）。
 void AdoptPreviousData() {
  var source=LegacyCandidates().Where(c=>File.Exists(Path.Combine(c,"launcher.json")))
   .OrderByDescending(c=>File.GetLastWriteTimeUtc(Path.Combine(c,"launcher.json"))).FirstOrDefault();
  if(source==null)return;
  try { File.Copy(Path.Combine(source,"launcher.json"),Path.Combine(Root,"launcher.json"),true); } catch(IOException) {}
  var appearance=Path.Combine(source,"appearance");
  if(Directory.Exists(appearance))CopyDir(appearance,Path.Combine(Root,"appearance"));
  try { File.WriteAllText(Path.Combine(Root,"migrated-from.txt"),source); } catch(IOException) {}
 }
 static void CopyDir(string source,string target) {
  Directory.CreateDirectory(target);
  foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) {
   var dest=Path.Combine(target,Path.GetRelativePath(source,file));
   try { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); if(!File.Exists(dest))File.Copy(file,dest,true); } catch(IOException) {}
  }
 }
 // 把背景图片与音乐文件复制进当前数据目录（一次），并改写设置里的路径。
 void RelocateAssets() {
  var appearance=Settings.Appearance;var changed=false;
  if(appearance.BackgroundImage.Length>0&&File.Exists(appearance.BackgroundImage)&&!IsUnder(Root,appearance.BackgroundImage)) {
   try { var dir=Path.Combine(Root,"appearance");Directory.CreateDirectory(dir);var dest=Path.Combine(dir,Path.GetFileName(appearance.BackgroundImage));if(!File.Exists(dest))File.Copy(appearance.BackgroundImage,dest,true);appearance.BackgroundImage=dest;changed=true; } catch(IOException) {}
  }
  if(appearance.Playlist.Count>0) {
   for(int i=0;i<appearance.Playlist.Count;i++) {
    var path=appearance.Playlist[i];
    if(path.Length==0||!File.Exists(path)||IsUnder(Root,path))continue;
    try { var dir=Path.Combine(Root,"music");Directory.CreateDirectory(dir);var dest=Path.Combine(dir,Path.GetFileName(path));if(!File.Exists(dest))File.Copy(path,dest,true);if(File.Exists(dest)){appearance.Playlist[i]=dest;changed=true;} } catch(IOException) {}
   }
  }
  if(changed)Save();
 }
 static bool IsUnder(string root,string path) {
  try { var r=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;return Path.GetFullPath(path).StartsWith(r,StringComparison.OrdinalIgnoreCase); } catch { return false; }
 }
 // 桌面快捷方式始终指向当前（最新）版本：固定名称，每次启动重写，避免旧版本残留链接。
 public void EnsureDesktopShortcut() {
  var desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
  if(desktop.Length==0)return;
  WriteShortcut(Path.Combine(desktop,"OCL.lnk"));
 }
 // 把快捷方式写到指定路径；--shortcut <path> 也走这里（发布脚本用它更新仓库内的 OCL.lnk）。
 public void WriteShortcut(string link) {
  try {
   var exe=Environment.ProcessPath??Path.Combine(AppContext.BaseDirectory,"PCL-OpenClaw-Launcher.exe");
   if(!File.Exists(exe))return;
   if(Path.GetDirectoryName(link) is { Length: >0 } dir)Directory.CreateDirectory(dir);
   // 已存在的 .lnk 可能因残留的 LinkInfo 导致 Save 失败，先移除再写，保证「始终指向最新版本」。
   if(File.Exists(link)) { try { File.SetAttributes(link,FileAttributes.Normal); File.Delete(link); } catch(Exception) {} }
   var shellType=Type.GetTypeFromProgID("WScript.Shell");if(shellType==null)return;
   dynamic shell=Activator.CreateInstance(shellType)!;
   dynamic shortcut=shell.CreateShortcut(link);
   shortcut.TargetPath=exe;shortcut.WorkingDirectory=Path.GetDirectoryName(exe)??AppContext.BaseDirectory;
   // 桌面图标可在「快捷方式图标」页切换：openclaw=角色图标，ocl=OCL 六边形，空=跟随 exe 内置图标。
   var icon=Settings.Appearance.ShortcutIcon switch {
    "openclaw"=>Path.Combine(AppContext.BaseDirectory,"Assets","openclaw.ico"),
    "ocl"=>Path.Combine(AppContext.BaseDirectory,"Assets","ocl.ico"),
    _=>exe
   };
   if(icon!=exe&&!File.Exists(icon))icon=exe;
   shortcut.IconLocation=icon;shortcut.Description="PCL · OpenClaw Launcher";shortcut.Save();
  } catch(Exception e) { try { File.AppendAllText(Path.Combine(Root,"shortcut-error.log"),link+"："+e.Message+Environment.NewLine); } catch(IOException) {} }
 }
 public void Save() {
  var file = Path.Combine(Root,"launcher.json"); var temp = file + ".tmp";
  File.WriteAllText(temp,JsonSerializer.Serialize(Settings,new JsonSerializerOptions { WriteIndented = true })); File.Move(temp,file,true);
 }
 public Instance Create(string name, string runtime, int port, IReadOnlyList<string>? defaultPlugins=null) {
  if(string.IsNullOrWhiteSpace(name)) throw new Exception("请填写实例名称。");
  if(port < 1024 || port > 65535 || Settings.Instances.Any(i=>i.Port==port)) throw new Exception("请选择 1024–65535 之间未被其他实例使用的端口。");
  var instance = new Instance { Name = name.Trim(),Runtime=runtime,Port=port,Managed=true };
  instance.State=Path.Combine(Root,"instances",instance.Id); instance.Config=Path.Combine(instance.State,"openclaw.json");
  Directory.CreateDirectory(instance.State);
  var config=new JsonObject {
   ["gateway"]=new JsonObject { ["mode"]="local",["port"]=port,["bind"]="loopback",["auth"]=new JsonObject { ["mode"]="token",["token"]=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) } },
   ["agents"]=new JsonObject { ["defaults"]=new JsonObject { ["workspace"]=Path.Combine(instance.State,"workspace") } }
  };
  // 新实例只携带“必要插件”（默认集）；其余由用户自行安装或制作。
  var plugins=(defaultPlugins??Settings.DefaultPlugins).Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  if(plugins.Count>0) {
   var entries=new JsonObject();foreach(var id in plugins)entries[id]=new JsonObject { ["enabled"]=true };
   config["plugins"]=new JsonObject { ["entries"]=entries };
  }
  File.WriteAllText(instance.Config,config.ToJsonString(new JsonSerializerOptions {WriteIndented=true}));
  Settings.Instances.Add(instance); Settings.Selected=instance.Id; Save(); LinkWallpaperPlugin(instance); return instance;
 }
 public void Rename(Instance instance,string name) {
  if(string.IsNullOrWhiteSpace(name)) throw new Exception("实例名称不能为空。");
  instance.Name=name.Trim(); Save();
 }
 public Instance Duplicate(Instance source,string name) {
  // 复制出的实例始终是独立受管实例：拥有自己的状态目录与配置，端口/令牌重写以免冲突。
  var clone=new Instance { Name=name.Trim(),Runtime=source.Runtime,Port=Math.Max(1024,Settings.Instances.Max(i=>i.Port)+1),Managed=true };
  clone.State=Path.Combine(Root,"instances",clone.Id); clone.Config=Path.Combine(clone.State,"openclaw.json");
  Directory.CreateDirectory(clone.State);
  var sourceConfig=source.Config.Length>0?source.Config:Path.Combine(source.State.Length>0?source.State:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw"),"openclaw.json");
  try {
   if(File.Exists(sourceConfig)) {
    var node=JsonNode.Parse(File.ReadAllText(sourceConfig)) as JsonObject;
    if(node?["gateway"] is JsonObject gateway) {
     gateway["port"]=clone.Port;
     gateway["auth"]=new JsonObject { ["mode"]="token",["token"]=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) };
    }
    File.WriteAllText(clone.Config,node?.ToJsonString(new JsonSerializerOptions {WriteIndented=true})??"{}");
   }
  } catch(Exception) { try{File.Copy(sourceConfig,clone.Config,true);}catch{} }
  Settings.Instances.Add(clone); Settings.Selected=clone.Id; Save(); LinkWallpaperPlugin(clone); return clone;
 }
 // —— Wallpaper Engine 插件桥接 ——
 // OpenClaw 的“受管实例”使用独立状态目录，不会自动合并全局 ~/.openclaw/extensions，
 // 导致我们自带的 wallpaper-engine 在受管实例的插件列表里看不到。这里把该插件以目录连接
 // （junction，无需管理员）挂入受管实例的扩展目录，并补全启用项与 controlUiRoot，使其可见可用。
 public void LinkWallpaperPlugin(Instance instance) {
  var runtime=instance.Runtime;
  var configPath=instance.Config.Length>0?instance.Config:Path.Combine(instance.State.Length>0?instance.State:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw"),"openclaw.json");
  EnsureWallpaperConfig(configPath,runtime);
  if(instance.State.Length==0)return; // 默认实例本就用 ~/.openclaw，全局扩展天然可见
  var source=ResolveWallpaperSource(); if(source==null)return;
  var target=Path.Combine(instance.State,"extensions","wallpaper-engine");
  if(!Directory.Exists(target)) { try { LinkOrCopy(source,target); } catch(IOException){} catch(UnauthorizedAccessException){} }
 }
 void EnsureWallpaperConfig(string configPath,string runtime) {
  var root=File.Exists(configPath)?(JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject??new JsonObject()):new JsonObject();
  var changed=false;
  var plugins=root["plugins"] as JsonObject??new JsonObject();
  var entries=plugins["entries"] as JsonObject??new JsonObject();
  var entry=entries["wallpaper-engine"] as JsonObject??new JsonObject();
  if(ReadModel.B(entry,"enabled")!=true){entry["enabled"]=true;changed=true;}
  var config=entry["config"] as JsonObject??new JsonObject();
  var uiRoot=ResolveControlUiRoot(runtime);
  if(config["controlUiRoot"] is not JsonValue){config["controlUiRoot"]=uiRoot;changed=true;}
  if(config["libraryRoot"] is not JsonValue){config["libraryRoot"]=@"D:\STEAM\steamapps\workshop\content\431960";changed=true;}
  entry["config"]=config; entries["wallpaper-engine"]=entry; plugins["entries"]=entries; root["plugins"]=plugins;
  if(changed){Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);File.WriteAllText(configPath,root.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));}
 }
 static string? ResolveWallpaperSource() {
  foreach(var c in new[]{Path.Combine(AppContext.BaseDirectory,"extensions","wallpaper-engine"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw","extensions","wallpaper-engine")})
   if(Directory.Exists(c))return c;
  return null;
 }
 internal static string ResolveControlUiRoot(string runtime) {
  foreach(var c in new[]{Path.Combine(AppContext.BaseDirectory,"dist","control-ui"),Path.Combine(runtime,"dist","control-ui"),@"E:\openclaw\dist\control-ui"})
   if(Directory.Exists(c))return c;
  return Path.Combine(runtime,"dist","control-ui");
 }
 // 目录连接（junction）无需管理员；失败（如跨卷）则退化为复制（排除 node_modules/.git 以减重）。
 static void LinkOrCopy(string source,string target) {
  Directory.CreateDirectory(Path.GetDirectoryName(target)!);
  var psi=new ProcessStartInfo("cmd.exe",$"/c mklink /J \"{target}\" \"{source}\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  try { using var p=new Process{StartInfo=psi}; p.Start(); p.WaitForExit(); if(p.ExitCode==0&&Directory.Exists(target))return; } catch {}
  CopyPlugin(source,target);
 }
 static void CopyPlugin(string source,string target) {
  Directory.CreateDirectory(target);
  foreach(var entry in Directory.GetFileSystemEntries(source)) {
   var name=Path.GetFileName(entry); if(name=="node_modules"||name==".git")continue;
   var dest=Path.Combine(target,name);
   if(Directory.Exists(entry))CopyPlugin(entry,dest); else File.Copy(entry,dest,true);
  }
 }
}
public static class SafeLog
{
 public static string Clean(string value) {
  value=Regex.Replace(value,@"\x1B\[[0-?]*[ -/]*[@-~]","");
  value=Regex.Replace(value,"(?i)([\"']?(?:token|api[_-]?key|password|secret|authorization|access[_-]?token|refresh[_-]?token|client[_-]?secret)[\"']?\\s*[:=]\\s*)(?:\"[^\"]*\"|'[^']*'|[^\\s,}]+)","$1[已隐藏]");
  value=Regex.Replace(value,@"(?i)(Bearer\s+)[A-Za-z0-9._~+/-]+=*","$1[已隐藏]");
  value=Regex.Replace(value,@"\bsk-[A-Za-z0-9_-]{12,}","[已隐藏]");
  value=Regex.Replace(value,@"\b\d{6,12}:[A-Za-z0-9_-]{20,}","[已隐藏]");
  return value;
 }
}
public record CommandResult(int ExitCode,string Output,string Error,bool TimedOut=false) {
 public bool Ok=>ExitCode==0&&!TimedOut;
 public string Summary=>SafeLog.Clean(string.IsNullOrWhiteSpace(Error)?Output:Error);
 public JsonNode? Json() => JsonOutput.Parse(Output);
}
public static class JsonOutput
{
 public static JsonNode? Parse(string text) {
  // Some plugin versions print warnings before JSON. Only accept a complete trailing JSON document.
  for(int i=0;i<text.Length;i++) if(text[i]=='{'||text[i]=='[') {
   try {return JsonNode.Parse(text[i..]);} catch(JsonException) {}
  }
  return null;
 }
}
public sealed class Runner
{
 public string Node { get; set; }
 public event Action<string>? Log;
 public Runner(string node) {Node=node;}
 public ProcessStartInfo StartInfo(Instance instance,IEnumerable<string> args) {
  if(!File.Exists(instance.Entry))throw new Exception("所选目录没有 openclaw.mjs，请在版本与实例中选择 OpenClaw 安装目录。");
  var psi = new ProcessStartInfo(Node) {WorkingDirectory=instance.Runtime,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  psi.ArgumentList.Add(instance.Entry); foreach(var arg in args)psi.ArgumentList.Add(arg);
  // Explicit selection must not inherit another instance's routing.
  foreach(var key in new[]{"OPENCLAW_PROFILE","OPENCLAW_STATE_DIR","OPENCLAW_CONFIG_PATH"})psi.Environment.Remove(key);
  if(instance.State.Length>0)psi.Environment["OPENCLAW_STATE_DIR"]=instance.State;
  if(instance.Config.Length>0)psi.Environment["OPENCLAW_CONFIG_PATH"]=instance.Config;
  psi.Environment["NO_COLOR"]="1"; psi.Environment["FORCE_COLOR"]="0";
  return psi;
 }
 public async Task<CommandResult> Run(Instance instance,string[] args,int seconds=90,CancellationToken cancellation=default) {
  Log?.Invoke("› "+string.Join(" ",args.Take(2)));
  var result=await Execute(StartInfo(instance,args),seconds,cancellation);
  if(!result.Ok)Log?.Invoke("失败："+result.Summary);
  return result;
 }
 public static async Task<CommandResult> Execute(ProcessStartInfo info,int seconds,CancellationToken cancellation=default) {
  // 不依赖外部调用方与控制台代码页：子进程输出一律按 UTF-8 解码，避免中文路径/参数乱码。
  info.StandardOutputEncoding??=Encoding.UTF8; info.StandardErrorEncoding??=Encoding.UTF8;
  using var p = new Process {StartInfo=info}; p.Start(); p.StandardInput.Close();
  var stdout=p.StandardOutput.ReadToEndAsync(); var stderr=p.StandardError.ReadToEndAsync();
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
  using var cleanup=timeout.Token.Register(()=>{try{if(!p.HasExited)p.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
  try {await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);} catch(OperationCanceledException) {
   try {p.Kill(true);} catch(InvalidOperationException) {}
   try {await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}catch(TimeoutException){}
   cancellation.ThrowIfCancellationRequested();
   return new(-1,"","状态检查超时，请查看网关日志后重试。",true);
  }
  return new(p.ExitCode,await stdout,await stderr);
 }
 public static void EnsureGatewayConfiguration(Instance instance) {
  var state=instance.State.Length>0?instance.State:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".openclaw");
  var path=instance.Config.Length>0?instance.Config:Path.Combine(state,"openclaw.json");
  if(File.Exists(path))return;
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  var config=new {gateway=new {mode="local",port=instance.Port,bind="loopback",auth=new {mode="token",token=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))}}};
  using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
  JsonSerializer.Serialize(file,config,new JsonSerializerOptions{WriteIndented=true});
 }
 public Process StartGateway(Instance instance,Action<string> output) {
  var info=StartInfo(instance,["gateway","run"]);
  var p=new Process {StartInfo=info,EnableRaisingEvents=true};
  p.OutputDataReceived+=(_,e)=> {if(e.Data!=null)output(SafeLog.Clean(e.Data));};
  p.ErrorDataReceived+=(_,e)=> {if(e.Data!=null)output(SafeLog.Clean(e.Data));};
  p.Start();p.StandardInput.Close();p.BeginOutputReadLine();p.BeginErrorReadLine();return p;
 }
 // OpenClaw 对 Node 的硬性要求：>=22.22.3 且 <23，或 >=24.15.0 且 <25，或 >=25.9.0。
 // 默认 "node.exe" 在部分机器上会解析到不满足要求的版本（如 22.22.2），导致所有查询失败、列表为空。
 // 这里优先用已配置路径；不可用或不兼容时，依次探测常见安装位置与 PATH，挑第一个兼容的。
 public static string ResolveNode(string preferred) {
  var candidates=new List<string>();
  if(!string.IsNullOrWhiteSpace(preferred))candidates.Add(preferred);
  candidates.Add(@"C:\Program Files\nodejs\node.exe");
  candidates.Add(@"C:\Program Files (x86)\nodejs\node.exe");
  foreach(var p in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
   try { var full=Path.Combine(p,"node.exe"); if(File.Exists(full))candidates.Add(full); } catch {}
  string? fallback=null;
  foreach(var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
   if(!File.Exists(c))continue;
   if(fallback==null)fallback=c;
   if(NodeCompatible(c))return c;
  }
  return fallback??preferred??"node.exe";
 }
 static bool NodeCompatible(string node) {
  try {
   var psi=new ProcessStartInfo(node){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   psi.ArgumentList.Add("--version");
   using var p=new Process{StartInfo=psi};p.Start();
   var v=p.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();p.WaitForExit(10000);
   var m=Regex.Match(v??"",@"v?(\d+)\.(\d+)\.(\d+)");
   if(!m.Success)return false;
   var maj=int.Parse(m.Groups[1].Value);var min=int.Parse(m.Groups[2].Value);var pat=int.Parse(m.Groups[3].Value);
   bool Ge(int a,int b,int c)=>(maj>a)||(maj==a&&min>b)||(maj==a&&min==b&&pat>=c);
   bool Lt(int a,int b,int c)=>(maj<a)||(maj==a&&min<b)||(maj==a&&min==b&&pat<c);
   return (Ge(22,22,3)&&Lt(23,0,0))||(Ge(24,15,0)&&Lt(25,0,0))||Ge(25,9,0);
  } catch { return false; }
 }
}
public sealed record ItemRow(string Id,string Name,string State,string Detail,string Source="",string Account="",bool Enabled=false,string RootDir="") {
 public string Indicator => State switch { "已连接" or "探测通过" or "已加载" or "壁纸库可用" => "#248B69", "连接异常" or "未连接" or "检测失败" or "加载失败" => "#D9363E", _ when State.Contains("未验证") => "#C58B20", _ => "#999999" };
}
public static class ReadModel
{
 public static string S(JsonNode? node,string key,string fallback="")=>node?[key]?.ToString()??fallback;
 public static bool? B(JsonNode? node,string key) => node?[key] is JsonValue value && value.TryGetValue<bool>(out var b)?b:null;
 public static List<ItemRow> Plugins(JsonNode? json) {
  if(json?["plugins"] is not JsonArray list)throw new Exception("插件列表返回了不支持的数据格式。");
  // rootDir = OpenClaw 报告的插件真实目录（全局插件会指向 ~/.openclaw/extensions），删除时按它精准定位。
  return list.Where(n=>n!=null).Select(n=>new ItemRow(S(n,"id"),S(n,"name",S(n,"id")),S(n,"status") switch {"loaded"=>"已加载","disabled"=>"已禁用","error"=>"加载失败",var v=>v.Length>0?v:"未确认"},SafeLog.Clean(S(n,"error",S(n,"description"))),S(n,"origin"),Enabled:B(n,"enabled")==true,RootDir:S(n,"rootDir"))).ToList();
 }
 public static List<ItemRow> Skills(JsonNode? json) {
  if(json?["skills"] is not JsonArray list)throw new Exception("Skills 返回了不支持的数据格式。");
  return list.Where(n=>n!=null).Select(n=> {
   var missing=n?["missing"] as JsonObject;var detail=new List<string>();
   if(missing!=null)foreach(var pair in missing)if(pair.Value is JsonArray a&&a.Count>0)detail.Add(pair.Key+": "+string.Join(", ",a.Select(v=>v?.ToString())));
   var status=B(n,"disabled")==true?"已禁用":B(n,"blockedByAllowlist")==true?"白名单阻止":B(n,"blockedByAgentFilter")==true?"Agent 未采用":B(n,"eligible")==true?"依赖就绪":"缺少依赖";
   return new ItemRow(S(n,"name"),S(n,"name"),status,detail.Count>0?string.Join("；",detail):S(n,"description"),S(n,"source"),Enabled:B(n,"disabled")!=true);
  }).ToList();
 }
 public static List<ItemRow> Channels(JsonNode? catalog,JsonNode? live,bool gatewayOnline) {
  var result=new List<ItemRow>(); var chat=catalog?["chat"] as JsonObject;
  var accounts=live?["channelAccounts"] as JsonObject;
  var actualLive=gatewayOnline&&B(live,"configOnly")!=true&&B(live,"gatewayReachable")!=false&&accounts!=null;
  var ids=new HashSet<string>(chat?.Select(p=>p.Key)??[]);
  if(live?["configuredChannels"] is JsonArray configuredIds)foreach(var id in configuredIds)if(id!=null)ids.Add(id.ToString());
  if(accounts!=null)foreach(var p in accounts)ids.Add(p.Key);
  foreach(var id in ids.Order()) {
   var meta=chat?[id];var entries=accounts?[id] as JsonArray;
   var accountEntries=entries?.Where(x=>x!=null).ToList()??[];
   if(accountEntries.Count==0 && meta is JsonObject && meta["accounts"] is JsonArray configured)
    foreach(var account in configured)accountEntries.Add(new JsonObject{["accountId"]=account?.ToString()});
   if(accountEntries.Count==0)accountEntries.Add(null);
   foreach(var entry in accountEntries) {
    var installed=B(meta,"installed"); var state="未检测";
    if(installed==false)state="未安装适配器";
    else if(!actualLive||entry==null)state=S(meta,"origin")=="configured"?"已配置 · 连接未验证":"未配置 / 无运行数据";
    else if(B(entry,"enabled")==false)state="已禁用";
    else if(B(entry,"configured")==false)state="未配置";
    else if(S(entry,"lastError").Length>0||B(entry?["probe"],"ok")==false)state="连接异常";
    else if(B(entry,"connected")==true)state="已连接";
    else if(B(entry,"connected")==false)state="未连接";
    else if(B(entry?["probe"],"ok")==true)state="探测通过";
    else if(B(entry,"running")==true)state="运行中 · 连接未验证";
    else state="未运行 / 未验证";
    result.Add(new(id,S(live?["channelLabels"],id,ChannelName(id)),state,SafeLog.Clean(S(entry,"lastError",actualLive?"状态来自网关；探测通过不代表消息已端到端送达。":"网关不可用或未返回运行数据，不能判断在线。")),S(meta,"origin"),S(entry,"accountId"),B(entry,"enabled")==true));
   }
  }
  foreach(var (name,aliases) in new[]{("微信",new[]{"wechat","weixin","wecom"}),("Telegram",new[]{"telegram"}),("QQ",new[]{"qq","qqbot"}),("Signal",new[]{"signal"}),("Line",new[]{"line"}),("Discord",new[]{"discord"}),("邮箱",new[]{"email","mail","gmail","imap","smtp"})})
   if(!result.Any(r=>aliases.Any(a=>r.Id.Contains(a,StringComparison.OrdinalIgnoreCase))))result.Add(new("",name,"未发现渠道适配器","可在插件页安装对应适配器。邮箱也可能通过 Skill / Hook 接入，不能据此判断邮箱本身离线。"));
  return result;
 }
 public static string ChannelName(string id)=>id switch {"telegram"=>"Telegram","signal"=>"Signal","qqbot"=>"QQ", "wechat" or "weixin" or "openclaw-weixin"=>"微信","wecom"=>"企业微信","email"=>"邮箱",_=>id};
}
