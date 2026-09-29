using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
namespace ClawLauncher;
public static class ArchiveDownload
{
 public static bool Trusted(Release release)=>ReleaseCatalog.ValidVersion(release.Version)&&Uri.TryCreate(release.Tarball,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="registry.npmjs.org"&&uri.AbsolutePath.StartsWith("/openclaw/-/",StringComparison.Ordinal)&&release.Integrity.StartsWith("sha512-",StringComparison.Ordinal);
 public static bool Verify(string file,string integrity) {
  if(!File.Exists(file)||!integrity.StartsWith("sha512-",StringComparison.Ordinal))return false;
  using var stream=File.OpenRead(file);return "sha512-"+Convert.ToBase64String(SHA512.HashData(stream))==integrity;
 }
 public static async Task<string> Download(Store store,Runner runner,Release release,CancellationToken token=default) {
  if(!Trusted(release))throw new Exception("此版本缺少官方包地址或 SHA-512 校验，请刷新版本列表。");
  var folder=Path.Combine(store.Root,"downloads");Directory.CreateDirectory(folder);var dest=Path.Combine(folder,"openclaw-"+release.Version+".tgz");
  if(await Task.Run(()=>Verify(dest,release.Integrity),token))return dest;
  var partial=dest+"."+Guid.NewGuid().ToString("N")+".part";
  const string script="import{createWriteStream}from'node:fs';import{rename,unlink}from'node:fs/promises';import{Readable,Transform}from'node:stream';import{pipeline}from'node:stream/promises';import{createHash}from'node:crypto';const[url,tmp,dest,expected]=process.argv.slice(1);const idle=new AbortController();let timer;function touch(){clearTimeout(timer);timer=setTimeout(()=>idle.abort(Error('Download stalled for 30 seconds')),30000)}touch();try{const r=await fetch(url,{signal:AbortSignal.any([AbortSignal.timeout(600000),idle.signal])});if(!r.ok)throw Error('HTTP '+r.status);const hash=createHash('sha512');await pipeline(Readable.fromWeb(r.body),new Transform({transform(c,e,done){touch();hash.update(c);done(null,c)}}),createWriteStream(tmp));if('sha512-'+hash.digest('base64')!==expected){await unlink(tmp);throw Error('SHA-512 mismatch')}await rename(tmp,dest);console.log('SHA-512 verified')}finally{clearTimeout(timer)}";
  var info=new ProcessStartInfo(runner.Node){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true};foreach(var arg in new[]{"--input-type=module","-e",script,release.Tarball,partial,dest,release.Integrity})info.ArgumentList.Add(arg);
  var result=await Runner.Execute(info,630,token);if(!result.Ok)throw new Exception("下载未完成："+result.Summary);
  if(!await Task.Run(()=>Verify(dest,release.Integrity),token))throw new Exception("安装包完整性校验失败。");return dest;
 }
}
