using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClawLauncher;

/// <summary>
/// OpenClaw 运行环境的体检与修复。
///
/// 这两件事都会让「网关起不来」或「启动特别慢」，而且都不会自己好：
///
/// 1) 缺少 dist/build-info.json
///    OpenClaw 用它来判断「这个状态目录的启动迁移是不是已经跑过了」
///    （src/infra/startup-migration-checkpoint.ts 的 needsStartupMigrationCheckpoint）。
///    文件不存在时它只能保守地每次启动都重跑一遍全量启动迁移。
///    本机实测：这一项让每次冷启动多花约 50 秒（55.1s → 1.9s）。
///
/// 2) 残留的启动迁移租约（state_leases 表，scope=startup-migrations，TTL 5 分钟）
///    网关启动时会去拿这条租约；如果上一次启动被强杀（正好停在迁移阶段），
///    租约就留在库里，之后每次启动都会直接失败：
///      Could not start the CLI.
///      Reason: OpenClaw startup migrations are already running for this state directory
///    官方自己用 Node 的 node:sqlite 读写这张表，所以这里也走同一个方式，不引入额外依赖。
///
/// 原则：只读优先、只做可逆的小事、拿不准就不动手。
/// </summary>
public static class RuntimeDoctor
{
 /// <summary>官方构建会写这个文件（scripts/write-build-info.ts），发布产物里也必须带它。</summary>
 public static string BuildInfoPath(Instance instance) => Path.Combine(instance.Runtime, "dist", "build-info.json");

 public static bool HasBuildInfo(Instance instance)
 {
  try { return File.Exists(BuildInfoPath(instance)); } catch { return false; }
 }

 /// <summary>读出现有的 builtAt（用于界面显示与自检）。文件不在或读不出都返回空串。</summary>
 public static string BuiltAt(Instance instance)
 {
  try
  {
   var path = BuildInfoPath(instance);
   if (!File.Exists(path)) return "";
   return JsonNode.Parse(File.ReadAllText(path))?["builtAt"]?.ToString() ?? "";
  }
  catch { return ""; }
 }

 /// <summary>给界面用的一句话结论。</summary>
 public static string BuildInfoVerdict(Instance instance)
 {
  if (!Directory.Exists(Path.Combine(instance.Runtime, "dist")))
   return "安装目录里没有 dist，不是一份可直接运行的 OpenClaw。";
  if (!HasBuildInfo(instance))
   return "缺少 dist/build-info.json：OpenClaw 无法确认「启动迁移已经跑过」，于是每次启动都重跑一遍，实测多花约 50 秒。";
  var built = BuiltAt(instance);
  return built.Length > 0
   ? "已就绪（构建标识 " + built + "）。"
   : "文件在，但读不到 builtAt，OpenClaw 仍会按「未迁移」处理。";
 }

 /// <summary>
 /// 补齐 dist/build-info.json。优先用官方的 scripts/write-build-info.ts（会带上真实 commit），
 /// 没有脚本时按官方同样的字段格式（version/commit/builtAt）写一份，效果一致：
 /// build-info 只被当作「构建标识」用，不影响任何行为。
 /// </summary>
 public static async Task<(bool Ok, string Report)> EnsureBuildInfo(Instance instance, string node, CancellationToken cancellation = default)
 {
  var path = BuildInfoPath(instance);
  if (HasBuildInfo(instance)) return (true, "dist/build-info.json 已存在，无需补齐。");
  if (!Directory.Exists(Path.Combine(instance.Runtime, "dist")))
   return (false, "安装目录里没有 dist，无法补齐；这份 OpenClaw 本身可能不完整。");

  var script = Path.Combine(instance.Runtime, "scripts", "write-build-info.ts");
  if (File.Exists(script))
  {
   var info = new ProcessStartInfo(node) { WorkingDirectory = instance.Runtime, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
   info.ArgumentList.Add("--import"); info.ArgumentList.Add("tsx"); info.ArgumentList.Add(script);
   info.Environment["NO_COLOR"] = "1";
   var result = await Runner.Execute(info, 90, cancellation);
   if (File.Exists(path)) return (true, "已用官方脚本生成 dist/build-info.json。");
   return (false, "官方脚本没能生成（" + result.Summary + "），将改用等价文件。");
  }

  // 官方字段格式见 scripts/write-build-info.ts：{ version, commit, builtAt }
  var version = "unknown";
  try
  {
   var packageJson = Path.Combine(instance.Runtime, "package.json");
   if (File.Exists(packageJson)) version = JsonNode.Parse(File.ReadAllText(packageJson))?["version"]?.ToString() ?? version;
  }
  catch { }
  try
  {
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   var body = new JsonObject
   {
    ["version"] = version,
    ["commit"] = null,
    ["builtAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
   };
   File.WriteAllText(path, body.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
   return (true, "已按官方字段格式补齐 dist/build-info.json（version=" + version + "）。下次启动起，OpenClaw 不会再重跑启动迁移。");
  }
  catch (Exception error)
  {
   return (false, "补齐 dist/build-info.json 失败：" + error.Message);
  }
 }

 /// <summary>OpenClaw 的状态目录：实例没单独指定就跟官方一样落到用户主目录下的 .openclaw。</summary>
 public static string StateDirectory(Instance instance) =>
  instance.State.Length > 0
   ? instance.State
   : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openclaw");

 public static string StateDatabasePath(Instance instance) =>
  Path.Combine(StateDirectory(instance), "state", "openclaw.sqlite");

 public const string LeaseScope = "startup-migrations";

 // 官方自己就是用 node:sqlite 的 DatabaseSync 读写这张表，这里沿用同一套，不额外引入依赖。
 const string LeaseQueryScript =
  "const{DatabaseSync}=require('node:sqlite');" +
  "let out={ok:false};" +
  "try{const db=new DatabaseSync(process.argv[1]);" +
  "const rows=db.prepare(\"select owner,expires_at from state_leases where scope=?\").all(process.argv[2]);" +
  "out={ok:true,rows:rows.map(r=>({owner:String(r.owner),expiresAt:Number(r.expires_at)}))};db.close();}" +
  "catch(e){out={ok:false,error:String(e&&e.message||e)};}" +
  "process.stdout.write(JSON.stringify(out));";

 const string LeaseClearScript =
  "const{DatabaseSync}=require('node:sqlite');" +
  "let out={ok:false};" +
  "try{const db=new DatabaseSync(process.argv[1]);" +
  "const r=db.prepare(\"delete from state_leases where scope=?\").run(process.argv[2]);" +
  "out={ok:true,removed:Number(r.changes)};db.close();}" +
  "catch(e){out={ok:false,error:String(e&&e.message||e)};}" +
  "process.stdout.write(JSON.stringify(out));";

 static ProcessStartInfo NodeInfo(string node, string script, string database) =>
  Build(node, script, database);

 static ProcessStartInfo Build(string node, string script, string database)
 {
  var info = new ProcessStartInfo(node) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
  info.ArgumentList.Add("--no-warnings");
  info.ArgumentList.Add("-e"); info.ArgumentList.Add(script);
  info.ArgumentList.Add(database);
  info.ArgumentList.Add(LeaseScope);
  return info;
 }

 public sealed record LeaseState(bool Known, bool Present, int Count, string ExpiresHint, string Report);

 /// <summary>只读查询启动迁移租约。查不到（没有状态库、Node 不支持 node:sqlite）时 Known=false，界面如实说不知道。</summary>
 public static async Task<LeaseState> InspectLease(Instance instance, string node, CancellationToken cancellation = default)
 {
  var database = StateDatabasePath(instance);
  if (!File.Exists(database))
   return new LeaseState(false, false, 0, "", "还没有状态库（" + Path.GetFileName(database) + "），说明这个实例没被启动过，不存在租约问题。");
  try
  {
   var result = await Runner.Execute(NodeInfo(node, LeaseQueryScript, database), 30, cancellation);
   if (result.Json() is not JsonNode parsed || ReadModel.B(parsed, "ok") != true)
    return new LeaseState(false, false, 0, "", "读取启动迁移租约失败：" + result.Summary);
   var rows = parsed["rows"] as JsonArray ?? new JsonArray();
   if (rows.Count == 0) return new LeaseState(true, false, 0, "", "没有启动迁移租约，网关可以正常启动。");
   var expires = "";
   if (rows[0] is JsonNode first && first["expiresAt"] is JsonNode stamp)
   {
    try
    {
     expires = DateTimeOffset.FromUnixTimeMilliseconds(stamp.GetValue<long>()).ToLocalTime().ToString("HH:mm:ss");
    }
    catch { }
   }
   return new LeaseState(true, true, rows.Count, expires,
    "存在 " + rows.Count + " 条启动迁移租约" + (expires.Length > 0 ? "（" + expires + " 自动过期）" : "") +
    "。上一次启动如果被强杀，租约就会留下来，之后每次启动都会报「startup migrations are already running」。");
  }
  catch (Exception error)
  {
   return new LeaseState(false, false, 0, "", "读取启动迁移租约失败：" + error.Message);
  }
 }

 /// <summary>清掉启动迁移租约。只删 state_leases 里 scope=startup-migrations 的行，别的行一律不动。</summary>
 public static async Task<(bool Ok, string Report)> ClearLease(Instance instance, string node, CancellationToken cancellation = default)
 {
  var database = StateDatabasePath(instance);
  if (!File.Exists(database)) return (true, "还没有状态库，无需清理。");
  var result = await Runner.Execute(NodeInfo(node, LeaseClearScript, database), 30, cancellation);
  if (result.Json() is not JsonNode parsed || ReadModel.B(parsed, "ok") != true)
   return (false, "清理启动迁移租约失败：" + result.Summary);
  var removed = parsed["removed"]?.GetValue<int>() ?? 0;
  return (true, removed > 0 ? "已清理 " + removed + " 条启动迁移租约。" : "没有需要清理的启动迁移租约。");
 }

 /// <summary>界面摘要：把两件事的结论拼一段话。</summary>
 public static string Summary(Instance instance) =>
  "构建标识：" + BuildInfoVerdict(instance) + Environment.NewLine +
  "启动迁移租约：" + (File.Exists(StateDatabasePath(instance)) ? "点「体检并修复」可实时查看与清理。" : "还没有状态库。");

 /// <summary>官方输出里的「已经有一个网关在跑」——这时不该报错，而应该直接复用那个网关。</summary>
 public static bool MentionsGatewayAlreadyRunning(string text) =>
  text.Length > 0 &&
  (text.Contains("already running (pid", StringComparison.OrdinalIgnoreCase) ||
   text.Contains("gateway already running", StringComparison.OrdinalIgnoreCase));

 /// <summary>从「already running (pid N)」里抠出 pid，用于判断占位者是不是还活着。</summary>
 public static int RunningPid(string text)
 {
  var match = Regex.Match(text, @"already running \(pid\s*(\d+)\)", RegexOptions.IgnoreCase);
  return match.Success && int.TryParse(match.Groups[1].Value, out var pid) ? pid : 0;
 }

 /// <summary>端口被占用的原文（官方会同时印出占用者命令行）。</summary>
 public static bool MentionsPortInUse(string text) =>
  text.Length > 0 && (text.Contains("is already in use", StringComparison.OrdinalIgnoreCase) ||
                      text.Contains("EADDRINUSE", StringComparison.OrdinalIgnoreCase));
}
