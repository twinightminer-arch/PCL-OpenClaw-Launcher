using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace ClawLauncher;

/// <summary>
/// OpenClaw 网关是「单实例」的：启动时会在临时目录拿一把文件锁
/// （Windows：%TEMP%\openclaw；Linux/macOS：/tmp/openclaw[-&lt;uid&gt;]），
/// 锁里只写 pid / ownerId / createdAt / 端口，不写进程启动时间。
///
/// 官方在 Windows 上读不到别人的命令行时会保守地把锁当成「仍然存活」，
/// 于是当 pid 被系统回收给别的进程以后（实测被 SearchFilterHost.exe 复用），
/// 这把锁就永久卡住，表现为：
///   Gateway failed to start: gateway already running (pid 26852); lock timeout after 5000ms
/// 用户看到的就是「网页控制台打不开了」。
///
/// 这里做的是 OCL 侧的复核：锁里记的 pid 如果已经不存在，或者存在但明显不是
/// node/openclaw 进程（即 pid 被复用），就认定是失效锁并清掉。
/// 只在证据明确时清理；拿不准（读不到进程信息）时一律不动手。
/// </summary>
public static class GatewayLock
{
 public static string LockDirectory => Path.Combine(Path.GetTempPath(), "openclaw");

 /// <summary>可能真的是 OpenClaw 网关的进程名——这类 pid 一律不清理。</summary>
 static readonly string[] GatewayLike = ["node", "nodejs", "openclaw", "openclaw-gateway", "bun"];

 public static bool IsGatewayLike(string processName) =>
  processName.Length > 0 && GatewayLike.Any(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));

 /// <summary>纯逻辑：给定「pid 是否存活」和「该 pid 的进程名」，给出结论。</summary>
 public static string VerdictFor(bool pidAlive, string processName)
 {
  if (!pidAlive) return "失效：记录的进程已不存在";
  if (processName.Length == 0) return "未知：读取不到该进程信息，保守跳过";
  return IsGatewayLike(processName) ? "存活：确实是 node / OpenClaw 相关进程" : "失效：pid 已被 " + processName + " 复用";
 }

 public sealed record LockFile(string Path, int Pid, string CreatedAt, int Port, string ProcessName, string Verdict)
 {
  public string Name => System.IO.Path.GetFileName(Path);
  public bool IsStale => Verdict.StartsWith("失效", StringComparison.Ordinal);
  public string Describe()
  {
   var owner = ProcessName.Length > 0 ? "，该 pid 现在是 " + ProcessName + ".exe" : "";
   var port = Port > 0 ? " · 端口 " + Port : "";
   return Name + "（pid " + Pid + port + "）：" + Verdict + owner;
  }
 }

 /// <summary>进程是否存在。只有明确「查无此进程」才算不存在，其余一律当活着，避免误删。</summary>
 public static bool PidAlive(int pid)
 {
  if (pid <= 0) return false;
  try { using var process = Process.GetProcessById(pid); return true; }
  catch (ArgumentException) { return false; }
  catch { return true; }
 }

 public static string ProcessName(int pid)
 {
  if (pid <= 0) return "";
  try { using var process = Process.GetProcessById(pid); return process.ProcessName ?? ""; }
  catch (ArgumentException) { return ""; }
  catch { return TasklistName(pid); }
 }

 /// <summary>ProcessName 读不到时的兜底：tasklist 在 Windows 上始终可用。</summary>
 static string TasklistName(int pid)
 {
  try
  {
   var info = new ProcessStartInfo("tasklist") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
   info.ArgumentList.Add("/FI"); info.ArgumentList.Add("PID eq " + pid);
   info.ArgumentList.Add("/FO"); info.ArgumentList.Add("CSV"); info.ArgumentList.Add("/NH");
   using var process = Process.Start(info);
   if (process == null) return "";
   var text = process.StandardOutput.ReadToEnd();
   process.WaitForExit(8000);
   foreach (var line in text.Split('\n'))
   {
    var cells = line.Split("\",\"");
    if (cells.Length > 1) return cells[0].Trim('"', ' ', '\r', '\n');
   }
   return "";
  }
  catch { return ""; }
 }

 public static LockFile? Parse(string path, string json)
 {
  try
  {
   if (JsonNode.Parse(json) is not JsonObject node) return null;
   var pid = node["pid"]?.GetValue<int>() ?? 0;
   if (pid <= 0) return null;
   var createdAt = node["createdAt"]?.ToString() ?? "";
   int port;
   try { port = node["port"]?.GetValue<int>() ?? 0; } catch { port = 0; }
   var alive = PidAlive(pid);
   var name = alive ? ProcessName(pid) : "";
   return new LockFile(path, pid, createdAt, port, name, VerdictFor(alive, name));
  }
  catch { return null; }
 }

 /// <summary>扫描临时目录里的所有网关锁文件（不修改任何东西）。</summary>
 public static List<LockFile> Inspect()
 {
  var found = new List<LockFile>();
  try
  {
   if (!Directory.Exists(LockDirectory)) return found;
   foreach (var path in Directory.GetFiles(LockDirectory, "gateway*.lock"))
   {
    if (path.EndsWith(".lock.sqlite", StringComparison.OrdinalIgnoreCase)) continue;
    try { if (Parse(path, File.ReadAllText(path)) is LockFile item) found.Add(item); } catch { }
   }
  }
  catch { }
  return found;
 }

 /// <summary>已知的「网关已经在跑」报错片段，用来判断要不要复核锁。</summary>
 public const string AlreadyRunningMarker = "already running";

 public static bool MentionsLockConflict(string text) =>
  text.Contains(AlreadyRunningMarker, StringComparison.OrdinalIgnoreCase) &&
  (text.Contains("gateway", StringComparison.OrdinalIgnoreCase) || text.Contains("lock", StringComparison.OrdinalIgnoreCase));

 /// <summary>清理失效锁（连同 sqlite 协调库与 journal 残留）。返回（清理条数, 说明）。</summary>
 public static (int Cleared, string Report) ClearStale()
 {
  var inspected = Inspect();
  var stale = inspected.Where(item => item.IsStale).ToList();
  var cleared = 0;
  var details = new List<string>();
  foreach (var item in stale)
  {
   try
   {
    File.Delete(item.Path);
    foreach (var suffix in new[] { ".sqlite", ".sqlite-journal", ".sqlite-wal", ".sqlite-shm" })
     try { File.Delete(item.Path + suffix); } catch { }
    cleared++;
    details.Add(item.Describe());
   }
   catch (Exception error) { details.Add(item.Name + "：清理失败（" + error.Message + "）"); }
  }
  var report = (inspected.Count, cleared) switch
  {
   (0, _) => "临时目录里没有 OpenClaw 网关锁。",
   (_, 0) => "网关锁都正常：" + string.Join("；", inspected.Select(item => item.Describe())),
   _ => "已清理 " + cleared + " 个失效网关锁：" + string.Join("；", details)
  };
  return (cleared, report);
 }

 /// <summary>
 /// 网关进程刚起来就退出时，从它最后几行输出里读出一句人话。
 /// 这类失败在界面上只显示「网关启动后退出」的话，用户根本无从下手。
 /// </summary>
 public static string ExplainStartupFailure(string tail)
 {
  if (tail.Length == 0) return "";
  if (tail.Contains("startup migrations are already running", StringComparison.OrdinalIgnoreCase))
  {
   var retry = RetryHint(tail);
   return "另一个 OpenClaw 正在对同一个状态目录做启动迁移，租约会自己过期" +
    (retry.Length > 0 ? "（大约在 " + retry + " 之后）" : "") +
    "。常见原因是上一次启动被中途强杀，租约留在了状态库里。到「启动总览 → 运行环境」点「体检并修复」可以立刻清掉。";
  }
  if (tail.Contains("already running (pid", StringComparison.OrdinalIgnoreCase))
   return "网关单实例锁被占用。若确认没有别的 OpenClaw 在跑，到「启动总览 → 网关锁」点「检查并清理失效锁」。";
  if (tail.Contains("EADDRINUSE", StringComparison.OrdinalIgnoreCase) || tail.Contains("already listening on ws://", StringComparison.OrdinalIgnoreCase))
   return "端口已被别的程序占用。关闭占用端口的程序，或在配置里给这个实例换个端口。";
  if (tail.Contains("Could not start the CLI", StringComparison.OrdinalIgnoreCase))
   return "OpenClaw 没能启动 CLI 本体，通常是安装目录或 Node 版本不对；到「运行环境」页检查。";
  return "";
 }

 /// <summary>从官方输出里抠出 ISO 时间戳，转成本地时间。原文里 "retry after the other gateway finishes or after &lt;时间&gt;" 中间夹着别的词，只能靠格式找。</summary>
 static string RetryHint(string tail)
 {
  var match = System.Text.RegularExpressions.Regex.Match(tail, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z?");
  if (!match.Success) return "";
  return DateTimeOffset.TryParse(match.Value, out var parsed) ? parsed.ToLocalTime().ToString("HH:mm:ss") : "";
 }

 /// <summary>多行摘要，给界面显示。</summary>
 public static string Summary()
 {
  var inspected = Inspect();
  if (inspected.Count == 0) return "未发现网关锁（网关当前没有运行）。";
  return string.Join(Environment.NewLine, inspected.Select(item => "· " + item.Describe()));
 }
}
