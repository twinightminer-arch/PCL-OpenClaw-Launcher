using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace ClawLauncher;

/// <summary>
/// 0.8.7 修订：让「点启动 → 控制台出现」尽量短。
///
/// 以前的启动路径上串了三次官方 CLI（每次 8 秒起）：先 <c>gateway status</c> 探活、
/// 再每轮 <c>gateway status</c> 等就绪、最后 <c>dashboard --json --no-open</c> 取地址。
/// 光这些开销就 20 秒以上，而它们要等的其实只是「网关的 HTTP 端口有没有开始应答」。
///
/// 这里换成两件零成本的事：
///   1) 直接 TCP 探测 <c>/healthz</c>（毫秒级）判断就绪，就绪立刻开浏览器；
///   2) 按官方同一套公式本地拼出控制台地址（见 dist/control-ui-links 与 dist/dashboard），
///      不再为了拿一个网址再起一个 Node 进程。
/// 官方 CLI 只作为回退：配置里控制台被禁用、或绑定在 tailnet / 密钥引用这类本地拼不出来的情形。
/// </summary>
public static class ConsoleLauncher
{
 static readonly HttpClient probes = new(new SocketsHttpHandler { UseProxy=false, PooledConnectionLifetime=TimeSpan.FromMinutes(2) }) { Timeout=Timeout.InfiniteTimeSpan };
 /// <summary>探测网关端口；HTTP 忙于插件初始化时，以 TCP 已接收连接作为启动状态。</summary>
 public static async Task<bool> ReadyAsync(string host,int port,int timeoutMs,CancellationToken cancellation=default) =>
  await ProbeAsync(host,port,timeoutMs,cancellation).ConfigureAwait(false)==true;

 // null means a refused connection. A TCP accept with a delayed HTTP response means warming.
 static async Task<bool?> ProbeAsync(string host,int port,int timeoutMs,CancellationToken cancellation)
 {
  if (port <= 0 || port > 65535 || host.Length == 0) return false;
  using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
  budget.CancelAfter(Math.Max(80, timeoutMs));
  try
  {
   // Windows can complete the TCP handshake while Node's event loop is busy importing
   // optional plugins. Probe HTTP briefly, but don't wait 110 seconds if it cannot run yet.
   using var socket = new TcpClient();
   await socket.ConnectAsync(host, port, budget.Token).ConfigureAwait(false);
   using var httpBudget = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
   httpBudget.CancelAfter(Math.Min(180, Math.Max(80, timeoutMs)));
   try
   {
    using var response = await probes.GetAsync(new UriBuilder("http",host,port,"/healthz").Uri,httpBudget.Token).ConfigureAwait(false);
    return response.IsSuccessStatusCode;
   }
   catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
   {
    return true; // TCP is listening; HTTP handling is delayed by startup work.
   }
   catch (HttpRequestException)
   {
    return true; // TCP accepted the connection before the HTTP handler stalled.
   }
  }
  catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
  catch (SocketException error) when (error.SocketErrorCode == SocketError.ConnectionRefused) { return null; }
  catch (Exception) { return false; }
 }
 /// <summary>轮询到就绪为止。返回（是否就绪, 实际等待毫秒）。</summary>
 public static async Task<(bool Ready,long ElapsedMs)> WaitReadyAsync(string host,int port,int timeoutMs,int pollMs=150,CancellationToken cancellation=default)
 {
  var watch = Stopwatch.StartNew();
  while (watch.ElapsedMilliseconds < timeoutMs)
  {
   cancellation.ThrowIfCancellationRequested();
   // 单次预算 800ms 而不是 400ms：网关忙的时候（插件正在加载）几百毫秒答不上来是常态，
   // 预算太紧会让「等就绪」这一圈一圈地空转，最后误判成「网关没起来」。
   var remaining=(int)Math.Max(1,timeoutMs-watch.ElapsedMilliseconds);
   if (await ReadyAsync(host, port, Math.Min(800,remaining), cancellation).ConfigureAwait(false)) return (true, watch.ElapsedMilliseconds);
   remaining=(int)Math.Max(0,timeoutMs-watch.ElapsedMilliseconds);
   if(remaining>0)await Task.Delay(Math.Min(Math.Max(40,pollMs),remaining),cancellation).ConfigureAwait(false);
  }
  return (false, watch.ElapsedMilliseconds);
 }

 /// <summary>
 /// 「网关是不是已经在跑」——必须先等一下就绪再下结论。
 /// 单个 ReadyAsync 在这个环境里是不可靠的：进程里**第一次**连这个地址可能要 2 秒以上
 /// （本地实测被安全软件/沙盒的首次连接检查拖住），于是永远第一次判「没在跑」，
 /// 结果 OCL 会去启第二个网关、撞上单实例锁——用户看到的就是「启动失败」。
 /// 所以决策点一律走这个：给一个短窗口、内部重试。
 /// </summary>
 public static async Task<(bool Ready,long ElapsedMs)> AlreadyRunningAsync(string host,int port,CancellationToken cancellation=default) {
  var watch=Stopwatch.StartNew();
  var first=await ProbeAsync(host,port,800,cancellation).ConfigureAwait(false);
  if(first!=false)return (first==true,watch.ElapsedMilliseconds);
  var waited=await WaitReadyAsync(host,port,Math.Max(1,3000-(int)watch.ElapsedMilliseconds),150,cancellation).ConfigureAwait(false);
  return (waited.Ready,watch.ElapsedMilliseconds);
 }

 /// <summary>本地拼出来的控制台地址与探测地址。</summary>
 public sealed record ConsoleEndpoint(string Url,string Host,int Port,bool LocalUrl,bool Ready,Uri? Official)
 {
  /// <summary>拼不出本地地址（控制台被禁用 / tailnet / 密钥引用）时，用官方页面地址兜底，至少不是空按钮。</summary>
  public string EffectiveUrl => LocalUrl ? Url : (Official?.ToString() ?? Url);
 }

 /// <summary>
 /// 按官方公式从配置里直接算出控制台地址——<c>&lt;scheme&gt;://&lt;host&gt;:&lt;port&gt;&lt;basePath&gt;/</c>，
 /// 带 token 时再拼 <c>#token=</c>（官方 dashboard 就是这个写法）。
 /// 返回 null 表示本地拼不出来，需要回退官方 CLI。
 /// </summary>
 public static ConsoleEndpoint? FromConfig(string configJson,int fallbackPort)
 {
  try
  {
   if (JsonNode.Parse(configJson) is not JsonObject root) return null;
   var gateway = root["gateway"] as JsonObject;
   int port = ParseInt(gateway?["port"]) ?? fallbackPort;
   if (port <= 0 || port > 65535) return null;
   if (gateway?["controlUi"] is JsonObject ui && ui["enabled"] is JsonValue disabled && disabled.TryGetValue<bool>(out var enabled) && !enabled) return null;

   var bind = Str(gateway?["bind"]).Trim().ToLowerInvariant();
   var customHost = Str(gateway?["customBindHost"]).Trim();
   var basePath = NormalizeBasePath(Str(gateway?["controlUi"] is JsonObject u ? Str(u["basePath"]) : ""));
   var tls = gateway?["tls"] is JsonObject tlsNode && tlsNode["enabled"] is JsonValue tlsFlag && tlsFlag.TryGetValue<bool>(out var tlsOn) && tlsOn;
   var scheme = tls ? "https" : "http";

   // tailnet 绑定只在 Tailscale 网卡上监听，本机 127.0.0.1 可能连不上——这种交给官方 CLI 处理。
   if (bind == "tailnet") return null;
   // custom 指向具体地址时就用它；其余（loopback / lan / custom 通配）本机走回环即可。
   var host = "127.0.0.1";
   if (bind == "custom" && customHost.Length > 0 && customHost != "0.0.0.0") host = customHost;

   var uiPath = basePath.Length > 0 ? basePath + "/" : "/";
   var url = scheme + "://" + host + ":" + port + uiPath;

   var authMode = Str(gateway?["auth"] is JsonObject auth ? Str(auth["mode"]) : "").Trim().ToLowerInvariant();
   if (authMode.Length == 0 || authMode == "token")
   {
    var token = gateway?["auth"] is JsonObject authNode ? StringOrNull(authNode["token"]) : null;
    // 密钥引用（secret ref / ${...} / secret://）不是明文 token，拼进 URL 会无效，留给官方 CLI。
    if (token != null && token.Length > 0 && !LooksLikeSecretRef(token)) url += "#token=" + Uri.EscapeDataString(token);
   }
   return new ConsoleEndpoint(url, host, port, true, false, null);
  }
  catch (Exception) { return null; }
 }

 /// <summary>探测地址：控制台地址去掉 token 片段，只留 scheme://host:port/。</summary>
 public static ConsoleEndpoint ProbeEndpoint(string configJson,int fallbackPort) =>
  FromConfig(configJson, fallbackPort) ?? new ConsoleEndpoint("", "127.0.0.1", fallbackPort, false, false, null);

 static string NormalizeBasePath(string? value)
 {
  var text = (value ?? "").Trim();
  if (text.Length == 0 || text == "/") return "";
  if (!text.StartsWith('/')) text = "/" + text;
  return text.TrimEnd('/');
 }

 /// <summary>密钥引用（而不是明文）的几种常见写法。</summary>
 public static bool LooksLikeSecretRef(string value) =>
  value.StartsWith("${", StringComparison.Ordinal) || value.Contains("://", StringComparison.Ordinal) || value.StartsWith("secret", StringComparison.OrdinalIgnoreCase) || value.StartsWith("vault", StringComparison.OrdinalIgnoreCase);

 static int? ParseInt(JsonNode? node)
 {
  if (node is JsonValue value)
  {
   if (value.TryGetValue<int>(out var number)) return number;
   if (value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed)) return parsed;
  }
  return null;
 }

 static string Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

 static string? StringOrNull(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
