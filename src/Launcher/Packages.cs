using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace ClawLauncher;
public record PackEntry(string Id,string? Install,string Kind,bool Enabled);
public sealed class Pack
{
 public string Format {get;set;}="openclaw-launcher-pack/1";
 public string Name {get;set;}="我的 OpenClaw 整合包";
 public string Version {get;set;}="";
 public List<PackEntry> Entries {get;set;}=[];
 public List<string> Unresolved {get;set;}=[];
 public static Pack Load(string file) {
  if(new FileInfo(file).Length>2_000_000)throw new Exception("整合包清单过大。");
  var pack=JsonSerializer.Deserialize<Pack>(File.ReadAllText(file))??throw new Exception("无效的整合包清单。");
  if(pack.Format!="openclaw-launcher-pack/1"||pack.Entries.Count>500)throw new Exception("不支持的整合包格式或条目过多。");
  foreach(var entry in pack.Entries) {
   if(entry.Kind is not ("plugin" or "skill")||string.IsNullOrWhiteSpace(entry.Id)||entry.Id.Length>160)throw new Exception("清单包含无效条目。");
   if(entry.Install!=null&&!IsPortableSpec(entry.Install))throw new Exception("整合包仅接受 npm / ClawHub 包引用，不接受本地路径、任意 URL 或命令："+entry.Id);
  }
  return pack;
 }
 public static bool IsPortableSpec(string spec)=> Regex.IsMatch(spec,@"^(?:(?:npm|clawhub):)?(?:@[A-Za-z0-9_.-]+/)?[A-Za-z0-9][A-Za-z0-9_.-]*(?:@[A-Za-z0-9_.+-]+)?$");
}
