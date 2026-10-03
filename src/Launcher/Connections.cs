using System.Text.Json.Nodes;
namespace ClawLauncher;

// 「软件连接」页的数据模型：一个软件 = 一个 OpenClaw 频道 + 提供它的插件适配器。
// 数据全部读 OpenClaw 官方设置（channels list --all 与 plugins list），启动器不内置任何账号与凭据。
public sealed record ConnectionApp(
 string Id,string Name,string State,string Detail,string Accounts,
 string PluginId,string PluginPackage,bool PluginEnabled,string PluginStatus)
{
 public string Indicator=>State switch {
  "已连接" or "探测通过"=>"#248B69",
  "连接异常" or "检测失败"=>"#D9363E",
  "未安装适配器"=>"#999999",
  _=>"#C58B20" };
 // 已装适配器但没启用 / 加载失败，都算「点了修复能变好」的情形。
 public bool AdapterInstalled=>PluginId.Length>0;
 public bool AdapterReady=>PluginId.Length>0&&PluginEnabled&&PluginStatus!="加载失败";
 public string Summary {
  get {
   var parts=new List<string>{State};
   if(Accounts.Length>0)parts.Add("账号 "+Accounts);
   parts.Add(AdapterInstalled?"适配器 "+PluginPackage+(PluginEnabled?(PluginStatus=="加载失败"?" · 加载失败":""):" · 未启用"):"未装适配器插件");
   return string.Join("  ·  ",parts);
  }
 }
 public string ConsoleUrl=>ConnectionCatalog.ConsoleUrl(Id);
}

public static class ConnectionCatalog
{
 // 官方频道 id → 软件名。优先用这张表（CLI 给的 channelLabels 会把 QQ Bot 简写成 QQ），表里没有的才用 CLI 的名字。
 public static string Display(string id,string fallback="") {
  var curated=id switch {
   "telegram"=>"Telegram","qqbot"=>"QQ Bot","openclaw-weixin" or "weixin" or "wechat"=>"微信","wecom"=>"企业微信",
   "line"=>"LINE","signal"=>"Signal","email"=>"邮箱","discord"=>"Discord","slack"=>"Slack","whatsapp"=>"WhatsApp",
   "feishu"=>"飞书","msteams"=>"Microsoft Teams","matrix"=>"Matrix","irc"=>"IRC","sms"=>"短信","twitch"=>"Twitch",
   "nostr"=>"Nostr","mattermost"=>"Mattermost","nextcloud-talk"=>"Nextcloud Talk","synology-chat"=>"Synology Chat",
   "googlechat"=>"Google Chat","imessage"=>"iMessage","zalo"=>"Zalo","yuanbao"=>"腾讯元宝","zalouser"=>"Zalo 个人号",
   "clickclack"=>"ClickClack","raft"=>"Raft","reef"=>"Reef","tlon"=>"Tlon","qa-channel"=>"QA 测试频道",
   _=>"" };
  if(curated.Length>0)return curated;
  return fallback.Length>0?fallback:id;
 }
 // 该软件的官方网页控制台 / 后台。没收录的一律给 OpenClaw 频道文档，保证按钮永远有去处、不会点了没反应。
 public static string ConsoleUrl(string id)=>id switch {
  "telegram"=>"https://web.telegram.org/",
  "qqbot"=>"https://q.qq.com/",
  "openclaw-weixin" or "weixin" or "wechat" or "wecom"=>"https://mp.weixin.qq.com/",
  "line"=>"https://developers.line.biz/console/",
  "signal"=>"https://signal.org/download/",
  "discord"=>"https://discord.com/developers/applications",
  "slack"=>"https://api.slack.com/apps",
  "whatsapp"=>"https://web.whatsapp.com/",
  "feishu"=>"https://open.feishu.cn/app",
  "msteams"=>"https://portal.azure.com/",
  "matrix"=>"https://app.element.io/",
  "wallpaper-engine"=>"https://www.wallpaperengine.io/",
  _=>"https://docs.openclaw.ai/cli/channels" };
 // 排序：能用的排前面（已连接 → 待验证 → 未配置 → 未装适配器），同类按名字。
 static int Rank(ConnectionApp app)=>app.State switch {
  "已连接"=>0,"探测通过"=>1,"运行中 · 连接未验证"=>2,"已配置 · 连接未验证"=>3,
  "未运行 / 未验证"=>4,"未配置 / 无运行数据"=>5,"未配置"=>6,"已禁用"=>7,"未安装适配器"=>9,_=>8 };
 // 由官方数据拼出软件列表。channels 是 ReadModel.Channels 的结果（每行一个 频道×账号）。
 public static List<ConnectionApp> Apps(IEnumerable<ItemRow> plugins,IEnumerable<ItemRow> channels) {
  var result=new List<ConnectionApp>();
  foreach(var group in channels.Where(c=>c.Id.Length>0).GroupBy(c=>c.Id)) {
   var id=group.Key;
   var provider=plugins.FirstOrDefault(p=>p.ProvidesChannel(id));
   var primary=group.FirstOrDefault(r=>r.State=="已连接")??group.FirstOrDefault(r=>r.Account.Length>0)??group.First();
   var accounts=string.Join("、",group.Select(r=>r.Account).Where(a=>a.Length>0).Distinct());
   result.Add(new ConnectionApp(id,Display(id,primary.Name),primary.State,primary.Detail,accounts,
    provider?.Id??"",provider?.Name??"",provider?.Enabled??false,provider?.State??"未安装"));
  }
  return result.OrderBy(Rank).ThenBy(a=>a.Name,StringComparer.CurrentCulture).ToList();
 }
 // 从 ClawHub 搜索结果里挑一个最像该软件适配器的插件：名字精确 > 含频道名 > 官方，最后才退第一条。
 public static MarketItem? BestAdapter(string channelId,JsonNode? searchJson) {
  var items=(searchJson?["results"] as JsonArray??[]).Where(n=>n!=null).Select(n=> {
   var pkg=n?["package"]??n;
   return new MarketItem(ReadModel.S(pkg,"name"),ReadModel.S(pkg,"displayName",ReadModel.S(pkg,"name")),
    ReadModel.S(pkg,"ownerHandle"),ReadModel.S(pkg,"summary"),ReadModel.S(pkg,"latestVersion"),
    ReadModel.S(ReadModel.Node(pkg,"stats"),"downloads"),ReadModel.B(pkg,"isOfficial")==true);
  }).Where(i=>i.Id.Length>0).ToList();
  if(items.Count==0)return null;
  var wanted=channelId.ToLowerInvariant();
  int Score(MarketItem i) {
   var name=i.Id.ToLowerInvariant();
   if(name==wanted||name=="openclaw-"+wanted||name=="@openclaw/"+wanted)return 4;
   if(name.Contains(wanted))return 3;
   if((i.Display+" "+i.Summary).ToLowerInvariant().Contains(wanted))return 2;
   return 1;
  }
  return items.OrderByDescending(i=>Score(i)).ThenByDescending(i=>i.Official).ThenByDescending(i=>Score(i)>1).First();
 }
}
