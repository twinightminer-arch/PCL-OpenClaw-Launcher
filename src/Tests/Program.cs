using System.Text.Json.Nodes;
using ClawLauncher;
if(args.Contains("--only-install")){
 try{var rootArg=Array.IndexOf(args,"--install-root");var vi=Array.IndexOf(args,"--install-version");var version=vi<0?"2026.9.6":args[vi+1];var targetStore=new Store(args[rootArg+1]);var path=await RuntimeInstall.Install(targetStore,new Runner(args[0]),version,default,new Progress<InstallProgress>(p=>Console.WriteLine(p.Stage+" "+p.Line)));var old=targetStore.Settings.Selected;var port=18789;while(targetStore.Settings.Instances.Any(i=>i.Port==port))port++;if(!targetStore.Settings.Instances.Any(i=>i.Runtime==path)){targetStore.Create("OpenClaw "+version,path,port);targetStore.Settings.Selected=old;targetStore.Save();}Console.WriteLine("VERIFIED AND REGISTERED: "+path);}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}return;
}
var passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
var catalog=JsonNode.Parse("""{"chat":{"telegram":{"installed":true,"origin":"configured"}}}""");
ItemRow Channel(string account,bool online=true,string extra="")=>ReadModel.Channels(catalog,JsonNode.Parse("{\"channelAccounts\":{\"telegram\":["+account+"]}"+extra+"}"),online).Single(r=>r.Id=="telegram");
Check(Channel("""{"accountId":"main","enabled":true,"configured":true,"connected":true}""").State=="已连接","explicit connected is connected");
Check(Channel("""{"connected":true}""",false).State.Contains("未验证"),"gateway offline never shows cached online");
Check(Channel("""{"connected":true}""",true,",\"configOnly\":true").State.Contains("未验证"),"config-only fallback never shows online");
Check(Channel("""{"running":true}""").State.Contains("连接未验证"),"running alone is not connected");
Check(Channel("""{"connected":false,"probe":{"ok":true}}""").State=="未连接","explicit disconnect wins over successful API probe");
Check(Channel("""{"probe":{"ok":true}}""").State=="探测通过","successful probe is distinguished from connected");
Check(Channel("""{"connected":true,"probe":{"ok":false}}""").State=="连接异常","failed probe wins over transport connected");
Check(Channel("""{"enabled":false,"connected":true}""").State=="已禁用","disabled wins over stale connection");
Check(ReadModel.Channels(catalog,null,false).Any(r=>r.Name=="邮箱"&&r.State=="未发现渠道适配器"),"missing email adapter is not reported offline");
Check(JsonOutput.Parse("[warning] plugin noisy\n{\"ok\":true}\n")?["ok"]?.GetValue<bool>()==true,"parse JSON following diagnostic output");
Check(JsonOutput.Parse("{\"ok\":true}\nnot-json")==null,"reject contaminated trailing output");
Check(!SafeLog.Clean("token=very-secret apiKey: abcdef password='hello world'").Contains("very-secret"),"redact token values");
Check(!SafeLog.Clean("{\"apiKey\":\"abcdef\",\"password\":\"hello\"}").Contains("abcdef"),"redact JSON credentials");
Check(!SafeLog.Clean("Bearer abcXYZ123").Contains("abcXYZ123"),"redact authorization bearer");
var skill=ReadModel.Skills(JsonNode.Parse("""{"skills":[{"name":"mail","eligible":false,"disabled":false,"missing":{"bins":["mail-cli"]}}]}""")).Single();
Check(skill.State=="缺少依赖"&&skill.Detail.Contains("mail-cli"),"skill dependency failure surfaced");
Check(ReadModel.Plugins(JsonNode.Parse("""{"plugins":[{"id":"broken","status":"error","error":"load failed"}]}""")).Single().State=="加载失败","plugin error surfaced");
Check(Pack.IsPortableSpec("@openclaw/telegram@2026.7.2"),"allow version-pinned package reference");
Check(!Pack.IsPortableSpec("../../escape")&&!Pack.IsPortableSpec("https://evil.invalid/plugin")&&!Pack.IsPortableSpec("a;calc.exe"),"reject paths URLs and shell input in pack");
var root=Path.Combine(Path.GetTempPath(),"claw-launcher-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var store=new Store(root);var instance=store.Create("测试",root,19881);File.WriteAllText(Path.Combine(root,"openclaw.mjs"),"");
var separateApp=Path.Combine(root,"app-next");Directory.CreateDirectory(separateApp);File.WriteAllText(Path.Combine(separateApp,"data-location.txt"),"../shared-data");Check(Store.ResolveRoot(separateApp)==Path.Combine(root,"shared-data"),"side-by-side update preserves shared data location");
var config=JsonNode.Parse(File.ReadAllText(instance.Config));
Check(config?["gateway"]?["auth"]?["token"]?.ToString().Length==64,"isolated instance has random gateway credential");
Check(config?["gateway"]?["bind"]?.ToString()=="loopback","isolated instance binds loopback");
var info=new Runner("node.exe").StartInfo(instance,["plugins","install","hello; calc.exe"]);
Check(info.ArgumentList.Last()=="hello; calc.exe"&&!info.UseShellExecute,"arguments passed directly without shell expansion");
Check(info.Environment["OPENCLAW_CONFIG_PATH"]==instance.Config&&info.Environment["OPENCLAW_STATE_DIR"]==instance.State,"instance config routed explicitly");
var duplicate=false;try{store.Create("重复端口",root,19881);}catch{duplicate=true;}Check(duplicate,"duplicate instance port rejected");
Check(ReadModel.Channels(JsonNode.Parse("""{"chat":{"telegram":{"accounts":["one","two"],"installed":true,"origin":"configured"}}}"""),null,false).Count(r=>r.Id=="telegram")==2,"offline catalog preserves every configured account");
Check(ReadModel.Channels(null,JsonNode.Parse("""{"configOnly":true,"configuredChannels":["custom"]}"""),false).Any(r=>r.Id=="custom"),"config-only channel IDs survive missing catalog");
Check(Channel("""{"connected":true}""").Indicator=="#248B69"&&Channel("""{"connected":false}""").Indicator=="#D9363E","application lights reflect verified transport state");
var node=args.FirstOrDefault()??"node.exe";
var script=Path.Combine(root,"echo.mjs");File.WriteAllText(script,"console.log(JSON.stringify(process.argv.slice(2)));console.error('diagnostic');");
var psi=new System.Diagnostics.ProcessStartInfo(node){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true};psi.ArgumentList.Add(script);psi.ArgumentList.Add("空 格 ' ; & 中文");
var result=await Runner.Execute(psi,10);Check(result.Ok&&result.Json()?[0]?.ToString()=="空 格 ' ; & 中文"&&result.Error.Contains("diagnostic"),"real process Unicode arguments and separated stderr");
psi.ArgumentList.Clear();psi.ArgumentList.Add("-e");psi.ArgumentList.Add("setTimeout(()=>{},30000)");var timed=await Runner.Execute(psi,1);Check(timed.TimedOut&&!timed.Ok,"timeout kills owned command and reports incomplete");
Console.WriteLine($"{passed} checks passed. Test artifacts: {root}");
if(args.Length>1&&!args[1].StartsWith("--")) {
 instance.Runtime=args[1];var runner=new Runner(node);
 foreach(var command in new[]{new[]{"gateway","status","--json"},new[]{"plugins","list","--json"},new[]{"skills","list","--json"},new[]{"channels","list","--all","--json"},new[]{"channels","status","--timeout","2000","--json"}}) {
  var actual=await runner.Run(instance,command,120);var json=actual.Json();Check(actual.Ok&&json!=null,"real OpenClaw "+string.Join(" ",command.Take(2)));
  if(command[0]=="plugins")Check(ReadModel.Plugins(json).Count>0,"real plugin schema adapter");
  if(command[0]=="skills")Check(ReadModel.Skills(json).Count>0,"real skill schema adapter");
  if(command[0]=="channels"&&command[1]=="status")Check(ReadModel.B(json,"configOnly")==true||json?["channelAccounts"]!=null,"real channel offline/live schema");
 }
 Console.WriteLine($"Integration complete: {passed} total checks passed. No gateway started; isolated configuration only.");
}
Check(ReleaseCatalog.ValidVersion("2026.9.6-beta.1")&&ReleaseCatalog.ValidVersion("0.1.0"),"official prerelease and historical versions accepted");
Check(!ReleaseCatalog.ValidVersion("../../x")&&!ReleaseCatalog.ValidVersion("latest;calc")&&!ReleaseCatalog.ValidVersion("2026.9.6/../../x"),"installer version cannot escape staging root");
Check(!ReleaseCatalog.ValidVersion("2026.9.6...")&&!ReleaseCatalog.ValidVersion("2026.9.6-"),"reject Windows trailing-dot version aliases");
var hashFile=Path.Combine(root,"hash.txt");File.WriteAllText(hashFile,"verified");using(var stream=File.OpenRead(hashFile)){var hash="sha512-"+Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(stream));Check(ArchiveDownload.Verify(hashFile,hash)&&!ArchiveDownload.Verify(hashFile,"sha512-incorrect"),"archive integrity rejects modified payload");}
Check(!ArchiveDownload.Trusted(new Release("2026.9.6","","","",Tarball:"https://evil.invalid/openclaw.tgz",Integrity:"sha512-xxx")),"archive source constrained to official registry");
// 0.8.7「软件连接」：软件清单来自官方频道清单 × 插件适配器（channelIds），三个按钮的逻辑要可验证。
var connChannels=new List<ItemRow> {
 new("openclaw-weixin","微信","已配置 · 连接未验证","",Account:"default"),
 new("telegram","Telegram","已连接","",Account:"default"),
 new("line","LINE","未安装适配器",""),
 new("signal","Signal","未连接","",Account:"main") };
var connPlugins=new List<ItemRow> {
 new("telegram","@openclaw/telegram","已加载","",Enabled:true,ChannelIds:new[]{"telegram"}),
 new("line","LINE","已禁用","",Enabled:false,ChannelIds:new[]{"line"}),
 new("openclaw-weixin","@tencent-weixin/openclaw-weixin","已加载","",Enabled:true,ChannelIds:new[]{"openclaw-weixin"}) };
var apps=ConnectionCatalog.Apps(connPlugins,connChannels);
Check(apps.Count==connChannels.Count,"every official channel becomes one connection card");
Check(apps.First(a=>a.Id=="telegram").AdapterReady&&apps.First(a=>a.Id=="telegram").Indicator=="#248B69","connected app shows green with a ready adapter");
Check(apps.First(a=>a.Id=="line").PluginId=="line"&&!apps.First(a=>a.Id=="line").AdapterReady,"disabled bundled adapter is matched and flagged as not ready");
Check(apps.First(a=>a.Id=="signal").PluginId.Length==0&&!apps.First(a=>a.Id=="signal").AdapterInstalled,"channel without adapter is flagged as missing");
Check(apps[0].Id=="telegram","usable apps sort before unconfigured ones");
Check(apps.All(a=>a.ConsoleUrl.StartsWith("https://")),"every card has an https console target");
Check(ConnectionCatalog.ConsoleUrl("telegram")=="https://web.telegram.org/"&&ConnectionCatalog.ConsoleUrl("line").Contains("line.biz")&&ConnectionCatalog.ConsoleUrl("qqbot").Contains("q.qq.com")&&ConnectionCatalog.ConsoleUrl("openclaw-weixin").Contains("mp.weixin.qq.com"),"console targets map to each software's official console");
Check(ConnectionCatalog.ConsoleUrl("no-such-channel")=="https://docs.openclaw.ai/cli/channels","unknown software still gets a working console target");
Check(ConnectionCatalog.Display("qqbot")=="QQ Bot"&&ConnectionCatalog.Display("openclaw-weixin")=="微信"&&ConnectionCatalog.Display("telegram","Telegram")=="Telegram","software naming follows the official ids");
var market=JsonNode.Parse("""{"results":[{"package":{"name":"obsidian-media-claim","displayName":"Obsidian Media Claim","isOfficial":false}},{"package":{"name":"line-adapter","displayName":"某人的 LINE","isOfficial":false}},{"package":{"name":"openclaw-line","displayName":"LINE (official)","isOfficial":true}}]}""");
Check(ConnectionCatalog.BestAdapter("line",market)?.Id=="openclaw-line","adapter search prefers the exact then official package");
Check(ConnectionCatalog.BestAdapter("line",JsonNode.Parse("""{"results":[]}"""))==null,"no adapter results means no match instead of installing something random");
// 0.8.7 修复「网页控制台打不开」：网关单实例锁的复核逻辑（pid 被系统回收后旧锁会永久卡住）。
Check(GatewayLock.VerdictFor(false,"node").StartsWith("失效")&&GatewayLock.VerdictFor(false,"node").Contains("不存在"),"a lock whose pid is gone is stale");
Check(GatewayLock.VerdictFor(true,"SearchFilterHost").StartsWith("失效")&&GatewayLock.VerdictFor(true,"SearchFilterHost").Contains("复用"),"a recycled pid owned by an unrelated process is stale");
Check(GatewayLock.VerdictFor(true,"node").StartsWith("存活")&&GatewayLock.VerdictFor(true,"openclaw").StartsWith("存活"),"a lock held by a real node/openclaw process is left alone");
Check(GatewayLock.VerdictFor(true,"").StartsWith("未知"),"unreadable process info is never treated as stale");
Check(GatewayLock.IsGatewayLike("NODE")&&GatewayLock.IsGatewayLike("openclaw-gateway")&&!GatewayLock.IsGatewayLike("explorer")&&!GatewayLock.IsGatewayLike(""),"gateway process names matched case-insensitively, others rejected");
Check(GatewayLock.PidAlive(0)==false&&GatewayLock.PidAlive(-1)==false,"invalid pids are never called alive");
var lockPath=Path.Combine(root,"gateway.deadbeef.lock");
var parsed=GatewayLock.Parse(lockPath,"""{"pid":26852,"ownerId":"37ba12bd","createdAt":"2026-10-03T15:03:41.525Z","configPath":"C:/c.json","stateDir":"C:/s","port":3000}""");
Check(parsed!=null&&parsed!.Pid==26852&&parsed.Port==3000&&parsed.CreatedAt.StartsWith("2026-10-03")&&parsed.Describe().Contains("3000"),"a real-world stale lock payload parses into a readable diagnosis");
Check(GatewayLock.Parse(lockPath,"not json")==null&&GatewayLock.Parse(lockPath,"""{"pid":0}""")==null,"malformed or pid-less lock payloads are ignored");
Check(GatewayLock.MentionsLockConflict("Gateway failed to start: gateway already running (pid 26852); lock timeout after 5000ms")&&!GatewayLock.MentionsLockConflict("gateway listening on port 3000"),"the lock-conflict message is recognised");
Check(GatewayLock.Inspect().All(item=>item.Pid>0&&item.Name.EndsWith(".lock")),"every inspected lock comes from a parseable gateway lock file");
var migrationTail="[openclaw] Reason: OpenClaw startup migrations are already running for this state directory; retry after the other gateway finishes or after 2026-10-03T19:03:04.335Z.";
Check(GatewayLock.ExplainStartupFailure(migrationTail).Contains("启动迁移")&&GatewayLock.ExplainStartupFailure(migrationTail).Contains("03:03:04"),"a startup-migration conflict is explained with the local retry time");
Check(GatewayLock.ExplainStartupFailure("Gateway failed to start: gateway already running (pid 26852); lock timeout after 5000ms").Contains("清理失效锁"),"a lock conflict points the user at the lock repair button");
Check(GatewayLock.ExplainStartupFailure("Error: listen EADDRINUSE: address already in use 127.0.0.1:3000").Contains("端口"),"a port conflict names the port");
Check(GatewayLock.ExplainStartupFailure("[openclaw] Could not start the CLI.").Contains("运行环境"),"a broken CLI install points at the runtime page");
Check(GatewayLock.ExplainStartupFailure("").Length==0&&GatewayLock.ExplainStartupFailure("nothing familiar here").Length==0,"unknown failures get no invented advice");
Check(new Release("2026.9.6-beta.1","","","beta").Preview&&!new Release("2026.9.6","","","latest").Preview,"catalog channel classification");
store.Settings.Appearance.BackgroundImage="C:/test/image.png";store.Settings.Appearance.MusicVolume=.22;store.Settings.Appearance.Playlist.Add("C:/test/music.mp3");store.Save();var reloaded=new Store(root);Check(reloaded.Settings.Appearance.MusicVolume==.22&&reloaded.Settings.Appearance.Playlist.Count==1,"personalization survives settings reload");
var broken=Path.Combine(root,"broken");Directory.CreateDirectory(broken);File.WriteAllText(Path.Combine(broken,"package.json"),"{\"name\":\"openclaw\",\"version\":\"2026.9.6\"}");File.WriteAllText(Path.Combine(broken,"openclaw.mjs"),"process.exit(1)");Check(!await RuntimeInstall.IsReady(node,broken,"2026.9.6"),"incomplete npm install cannot appear installed");
// 0.8.7 修订：运行环境体检（缺 build-info.json 会让每次启动重跑约 50 秒的启动迁移；
// 残留的启动迁移租约会让网关每次都起不来）。
var doctorRuntime=Path.Combine(root,"doctor-runtime");Directory.CreateDirectory(Path.Combine(doctorRuntime,"dist"));
File.WriteAllText(Path.Combine(doctorRuntime,"package.json"),"{\"name\":\"openclaw\",\"version\":\"2026.7.2\"}");
var doctorInstance=new Instance{Id="doctor",Name="体检用例",Runtime=doctorRuntime,State="",Config="",Port=3000};
// 租约那两条断言要一份「从没被启动过」的实例：状态目录是空的，没有状态库。
var leaseInstance=new Instance{Id="lease",Name="租约用例",Runtime=doctorRuntime,State=Path.Combine(root,"doctor-state"),Config=Path.Combine(root,"doctor-state","openclaw.json"),Port=3001};
Check(RuntimeDoctor.BuildInfoPath(doctorInstance)==Path.Combine(doctorRuntime,"dist","build-info.json"),"build info is read from dist/build-info.json");
Check(!RuntimeDoctor.HasBuildInfo(doctorInstance)&&RuntimeDoctor.BuildInfoVerdict(doctorInstance).Contains("50 秒"),"a missing build-info.json is reported with the real cost");
var buildFix=await RuntimeDoctor.EnsureBuildInfo(doctorInstance,node,default);
Check(buildFix.Ok&&RuntimeDoctor.HasBuildInfo(doctorInstance),"a missing build-info.json can be repaired in place");
Check(RuntimeDoctor.BuiltAt(doctorInstance).EndsWith("Z")&&RuntimeDoctor.BuiltAt(doctorInstance).Length>=20,"the repaired build info carries a usable builtAt stamp");
Check(File.ReadAllText(RuntimeDoctor.BuildInfoPath(doctorInstance)).Contains("\"version\": \"2026.7.2\""),"the repaired build info keeps the official field shape");
var again=await RuntimeDoctor.EnsureBuildInfo(doctorInstance,node,default);
Check(again.Ok&&again.Report.Contains("无需补齐"),"an existing build info is left untouched");
Check(RuntimeDoctor.StateDirectory(new Instance{State=""}).EndsWith(Path.Combine("",".openclaw").TrimStart(Path.DirectorySeparatorChar))||RuntimeDoctor.StateDirectory(new Instance{State=""}).EndsWith(".openclaw"),"an instance without its own state falls back to ~/.openclaw");
Check(RuntimeDoctor.StateDirectory(new Instance{State=@"E:\state\one"})==@"E:\state\one","an instance with its own state keeps it");
Check(RuntimeDoctor.StateDatabasePath(new Instance{State=@"E:\state\one"}).EndsWith(Path.Combine("state","openclaw.sqlite")),"the startup-migration lease lives in <state>/state/openclaw.sqlite");
Check(RuntimeDoctor.MentionsGatewayAlreadyRunning("Gateway failed to start: gateway already running (pid 29096); lock timeout after 5000ms"),"the official already-running line is recognised");
Check(RuntimeDoctor.RunningPid("Gateway failed to start: gateway already running (pid 29096); lock timeout after 5000ms")==29096,"the pid of the already-running gateway is extracted");
Check(RuntimeDoctor.RunningPid("Port 3000 is already in use.")==0&&!RuntimeDoctor.MentionsGatewayAlreadyRunning("Port 3000 is already in use."),"a bare port conflict is not mistaken for a running gateway");
Check(RuntimeDoctor.MentionsPortInUse("Port 3000 is already in use.")&&RuntimeDoctor.MentionsPortInUse("Error: listen EADDRINUSE: address already in use 127.0.0.1:3000"),"port-in-use output is recognised from both official wordings");
var leaseVerdict=await RuntimeDoctor.InspectLease(leaseInstance,node,default);
Check(leaseVerdict.Known==false&&leaseVerdict.Report.Contains("没有状态库"),"a never-started instance reports no lease instead of guessing");
var clearedLease=await RuntimeDoctor.ClearLease(leaseInstance,node,default);
Check(clearedLease.Ok&&clearedLease.Report.Contains("无需清理"),"clearing a lease on a never-started instance is a no-op, not an error");
Check(GatewayLock.ExplainStartupFailure(migrationTail).Contains("运行环境"),"a stale startup-migration lease points the user at the runtime repair button");
if(args.Contains("--catalog")) {
 var fetched=await ReleaseCatalog.Fetch(new Runner(node),Path.Combine(root,"catalog.json"),default);Check(fetched.Releases.Count>0&&fetched.Releases.Any(v=>v.Version==fetched.Latest),"live official release catalog and latest tag");Check(ReleaseCatalog.ReadCache(Path.Combine(root,"catalog.json"))?.Releases.Count==fetched.Releases.Count,"offline catalog cache roundtrip");Console.WriteLine($"Catalog: {fetched.Releases.Count} versions; latest {fetched.Latest}");
}
var installIndex=Array.IndexOf(args,"--install");
if(installIndex>=0) {
 var version=args[installIndex+1];var rootIndex=Array.IndexOf(args,"--install-root");var installRoot=rootIndex>=0?args[rootIndex+1]:Path.Combine(root,"download");var downloads=new Store(installRoot);
 var progress=new Progress<InstallProgress>(p=>{if(p.Line.Length>0)Console.WriteLine(p.Line);else Console.WriteLine(p.Stage);});
 var installed=await RuntimeInstall.Install(downloads,new Runner(node),version,default,progress);Check(await RuntimeInstall.IsReady(node,installed,version),"downloaded real OpenClaw version starts with --version");
 var original=downloads.Settings.Selected;var candidate=18789;while(downloads.Settings.Instances.Any(i=>i.Port==candidate))candidate++;var created=downloads.Create("OpenClaw "+version,installed,candidate);downloads.Settings.Selected=original;downloads.Save();Check(File.Exists(created.Config)&&created.Runtime==installed,"successful download registers independent instance");
 Console.WriteLine("Installed and verified: "+installed);
}
Console.WriteLine($"FINAL: {passed} checks passed.");
if(args.Contains("--archive")) {
 var rootIndex=Array.IndexOf(args,"--install-root");var downloads=new Store(args[rootIndex+1]);var fetched=await ReleaseCatalog.Fetch(new Runner(node),Path.Combine(downloads.Root,"cache","openclaw-versions.json"),default);var vi=Array.IndexOf(args,"--archive-version");var release=fetched.Releases.Single(v=>v.Version==(vi>=0?args[vi+1]:fetched.Latest));
 var file=await ArchiveDownload.Download(downloads,new Runner(node),release);Check(ArchiveDownload.Verify(file,release.Integrity),"real official tarball download and SHA-512 integrity");Console.WriteLine("Archive: "+file+" ("+new FileInfo(file).Length+" bytes)");
}
