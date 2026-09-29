using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ClawLauncher;
public sealed record Release(string Version,string Published,string Node,string Tag,long Bytes=0,string Deprecated="",string Tarball="",string Integrity="")
{
 public bool Preview=>Regex.IsMatch(Version,@"[A-Za-z]");
 public string Channel=>Preview?"预览版":"正式版";
 public string Date=>DateTimeOffset.TryParse(Published,out var date)?date.ToLocalTime().ToString("yyyy/MM/dd HH:mm"):"日期未提供";
}
public sealed class ReleaseCatalog
{
 public DateTime Fetched {get;set;}
 public string Latest {get;set;}="";
 public string Beta {get;set;}="";
 public List<Release> Releases {get;set;}=[];
 public static bool ValidVersion(string version)=>Regex.IsMatch(version,@"^\d+\.\d+\.\d+(?:[-.][A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?$");
 public static async Task<ReleaseCatalog> Fetch(Runner runner,string cache,CancellationToken token) {
  // Node uses the same network stack as OpenClaw and npm, including its configured environment.
  const string script="const r=await fetch('https://registry.npmjs.org/openclaw',{signal:AbortSignal.timeout(45000)});if(!r.ok)throw Error('npm registry '+r.status);const j=await r.json();console.log(JSON.stringify({Fetched:new Date().toISOString(),Latest:j['dist-tags']?.latest||'',Beta:j['dist-tags']?.beta||'',Releases:Object.entries(j.versions||{}).map(([v,p])=>({Version:v,Published:j.time?.[v]||'',Node:p.engines?.node||'',Tag:Object.entries(j['dist-tags']||{}).filter(([k,x])=>x===v).map(([k])=>k).join(' / '),Bytes:p.dist?.unpackedSize||0,Deprecated:p.deprecated||'',Tarball:p.dist?.tarball||'',Integrity:p.dist?.integrity||''}))}));";
  var psi=new ProcessStartInfo(runner.Node){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true};psi.ArgumentList.Add("--input-type=module");psi.ArgumentList.Add("-e");psi.ArgumentList.Add(script);
  var result=await Runner.Execute(psi,55,token);if(!result.Ok)throw new Exception("无法读取官方版本列表："+result.Summary);
  var catalog=JsonSerializer.Deserialize<ReleaseCatalog>(result.Output)??throw new Exception("版本目录格式无效。");catalog.Releases=catalog.Releases.Where(r=>ValidVersion(r.Version)).OrderByDescending(r=>r.Published,StringComparer.Ordinal).ThenByDescending(r=>r.Version,StringComparer.Ordinal).ToList();
  if(catalog.Releases.Count==0)throw new Exception("仓库没有返回可安装版本。");Directory.CreateDirectory(Path.GetDirectoryName(cache)!);File.WriteAllText(cache,JsonSerializer.Serialize(catalog));return catalog;
 }
 public static ReleaseCatalog? ReadCache(string cache){try{return JsonSerializer.Deserialize<ReleaseCatalog>(File.ReadAllText(cache));}catch{return null;}}
}
