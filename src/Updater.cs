using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RisingKingdomsKO {
public class ReleaseAsset {public string name;public string browser_download_url;public long size;}
public class GitHubRelease {public string tag_name;public bool draft;public bool prerelease;public ReleaseAsset[] assets;}
public class UpdateFile {public string Path;public string Sha256;public long Size;}
public class UpdateManifest {public int Schema;public string Version;public string AssetName;public string Sha256;public long Size;public List<UpdateFile> Files;}
public class UserSettings {public string GamePath;public bool? FillScreen;public string ScreenMode;}
public static class AppPaths {
    public static string Root {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RisingKingdomsKO");}}
    public static string Dictionary {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"translations","ko.json");}}
    public static string DefaultPackage {get{return Path.Combine(Root,"packages",BuildInfo.PatchVersion+"-"+BuildInfo.DictionarySha256.Substring(0,12));}}
    public static UserSettings LoadSettings() {try{return Engine.Json.Deserialize<UserSettings>(File.ReadAllText(Path.Combine(Root,"settings.json"),Encoding.UTF8))??new UserSettings();}catch{return new UserSettings();}}
    public static void SaveSettings(string game,bool? fillScreen=null,string screenMode=null) {var settings=LoadSettings();settings.GamePath=game;if(fillScreen.HasValue)settings.FillScreen=fillScreen;if(screenMode!=null){if(!ScreenFill.ValidMode(screenMode))throw new ArgumentException("잘못된 화면 모드");settings.ScreenMode=screenMode;}Directory.CreateDirectory(Root);AtomicText(Path.Combine(Root,"settings.json"),Engine.Json.Serialize(settings));}
    public static void AtomicText(string path,string text) {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temp,text,Encoding.UTF8);
        try{if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
class UpdateClient : WebClient {
    readonly int timeout;
    public UpdateClient(int timeoutMs){timeout=timeoutMs;Headers[HttpRequestHeader.UserAgent]="RisingKingdomsKO/"+BuildInfo.Version;Encoding=Encoding.UTF8;}
    protected override WebRequest GetWebRequest(Uri address) {var r=base.GetWebRequest(address);r.Timeout=timeout;var http=r as HttpWebRequest;if(http!=null){http.ReadWriteTimeout=timeout;http.AllowAutoRedirect=true;}return r;}
}
public static class AutoUpdater {
    public const string Executable="RisingKingdomsLauncher.exe";
    public const string Repository="japanoxx-afk/risingkingdom";
    const long MaxZip=20*1024*1024,MaxExpanded=30*1024*1024;
    static readonly string[] Required={Executable,"translations/ko.json","version.json"};
    public static Version ParseVersion(string value) {
        if(!Regex.IsMatch(value??"",@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$"))throw new InvalidDataException("잘못된 버전 번호");
        return new Version(value);
    }
    public static bool IsNewer(string candidate,string current){return ParseVersion(candidate)>ParseVersion(current);}
    public static void ValidateManifest(UpdateManifest m) {
        if(m==null||m.Schema!=1)throw new InvalidDataException("지원하지 않는 업데이트 형식");ParseVersion(m.Version);
        if(m.AssetName!="RisingKingdomsKO-"+m.Version+"-win.zip"||m.Size<=0||m.Size>MaxZip||!Regex.IsMatch(m.Sha256??"","^[0-9a-f]{64}$")||m.Files==null||m.Files.Count!=Required.Length)throw new InvalidDataException("잘못된 업데이트 매니페스트");
        var paths=new HashSet<string>(StringComparer.Ordinal);
        long total=0;
        foreach(var f in m.Files) {
            if(f==null||!Required.Contains(f.Path)||!paths.Add(f.Path)||f.Size<=0||f.Size>MaxExpanded||!Regex.IsMatch(f.Sha256??"","^[0-9a-f]{64}$"))throw new InvalidDataException("허용되지 않는 업데이트 파일");total+=f.Size;
        }
        if(total>MaxExpanded)throw new InvalidDataException("업데이트 파일 크기 초과");
    }
    public static string AssetUrl(GitHubRelease release,string assetName) {
        if(release==null||release.draft||release.prerelease||release.assets==null||String.IsNullOrEmpty(release.tag_name)||!release.tag_name.StartsWith("v",StringComparison.Ordinal))throw new InvalidDataException("정식 릴리스가 아닙니다.");
        ParseVersion(release.tag_name.Substring(1));
        var assets=release.assets.Where(a=>a.name==assetName).ToArray();if(assets.Length!=1)throw new InvalidDataException("배포 파일을 찾을 수 없습니다: "+assetName);
        string expected="https://github.com/"+Repository+"/releases/download/"+release.tag_name+"/"+assetName;
        if(assets[0].browser_download_url!=expected)throw new InvalidDataException("허용되지 않는 업데이트 서버");return expected;
    }
    static void ValidatePayload(string root,UpdateManifest m) {
        ValidateManifest(m);
        foreach(var f in m.Files) {
            string path=Engine.Child(root,f.Path);
            if(!File.Exists(path)||new FileInfo(path).Length!=f.Size||Engine.Hash(path)!=f.Sha256)throw new InvalidDataException("업데이트 파일 검증 실패: "+f.Path);
        }
        var version=Engine.Json.Deserialize<Dictionary<string,string>>(File.ReadAllText(Engine.Child(root,"version.json"),Encoding.UTF8));
        if(version==null||!version.ContainsKey("Version")||version["Version"]!=m.Version)throw new InvalidDataException("업데이트 버전 불일치");
        string exeVersion=FileVersionInfo.GetVersionInfo(Engine.Child(root,Executable)).FileVersion;
        if(exeVersion!=m.Version+".0")throw new InvalidDataException("실행 파일 버전 불일치");
        var dictionary=Engine.Json.Deserialize<TranslationSet>(File.ReadAllText(Engine.Child(root,"translations/ko.json"),Encoding.UTF8));
        if(dictionary==null||dictionary.Schema!=1||dictionary.Entries==null||dictionary.Entries.Count==0)throw new InvalidDataException("잘못된 번역 사전");
    }
    public static void Stage(string zip,string destination,UpdateManifest m) {
        ValidateManifest(m);
        if(new FileInfo(zip).Length!=m.Size||Engine.Hash(zip)!=m.Sha256)throw new InvalidDataException("다운로드 SHA-256 또는 크기가 일치하지 않습니다.");
        if(Directory.Exists(destination)||File.Exists(destination))throw new IOException("업데이트 대상 폴더가 이미 있습니다.");
        using(var archive=ZipFile.OpenRead(zip)) {
            var paths=new HashSet<string>(StringComparer.Ordinal);long size=0;
            foreach(var entry in archive.Entries) {
                if(!Required.Contains(entry.FullName)||!paths.Add(entry.FullName)||entry.Length<=0||entry.Length>MaxExpanded||(entry.ExternalAttributes&0xF0000000)==unchecked((int)0xA0000000))throw new InvalidDataException("허용되지 않는 ZIP 경로 또는 파일");
                var f=m.Files.Single(x=>x.Path==entry.FullName);if(entry.Length!=f.Size)throw new InvalidDataException("ZIP 파일 크기 불일치");size+=entry.Length;
            }
            if(paths.Count!=Required.Length||size>MaxExpanded)throw new InvalidDataException("업데이트 ZIP 구성 오류");
            Directory.CreateDirectory(destination);
            foreach(var entry in archive.Entries) {
                string target=Engine.Child(destination,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(target));
                using(var input=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew))input.CopyTo(output);
            }
        }
        ValidatePayload(destination,m);
        File.WriteAllText(Engine.Child(destination,"update-receipt.json"),Engine.Json.Serialize(m),Encoding.UTF8);
    }
    public static string FindCachedNewer(string root,string current) {
        string versions=Path.Combine(root,"versions");if(!Directory.Exists(versions))return null;
        string best=null;Version highest=ParseVersion(current);
        foreach(string directory in Directory.GetDirectories(versions))try {
            if(File.Exists(Engine.Child(directory,"failed.json"))||!File.Exists(Engine.Child(directory,"startup-ok")))continue;
            var m=Engine.Json.Deserialize<UpdateManifest>(File.ReadAllText(Engine.Child(directory,"update-receipt.json"),Encoding.UTF8));
            if(ParseVersion(m.Version)<=highest)continue;
            ValidatePayload(directory,m);highest=ParseVersion(m.Version);best=directory;
        }catch(Exception){}
        return best;
    }
    static async Task<string> GetText(string url,int timeout) {
        using(var client=new UpdateClient(timeout)) {
            var task=client.DownloadStringTaskAsync(new Uri(url));
            if(await Task.WhenAny(task,Task.Delay(timeout))!=task){client.CancelAsync();throw new TimeoutException("업데이트 서버 응답 시간 초과");}
            return await task;
        }
    }
    public static async Task<string> CheckAndStage(Action<string> log) {
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        string root=AppPaths.Root;Directory.CreateDirectory(root);
        string cached=FindCachedNewer(root,BuildInfo.Version);
        try {
            log("업데이트 확인 중 · 현재 v"+BuildInfo.Version);
            string json=await GetText("https://api.github.com/repos/"+Repository+"/releases/latest",10000);
            var release=Engine.Json.Deserialize<GitHubRelease>(json);
            string manifestUrl=AssetUrl(release,"update.json");string remoteVersion=release.tag_name.Substring(1);
            if(!IsNewer(remoteVersion,BuildInfo.Version)){log("최신 버전입니다 · v"+BuildInfo.Version);return cached;}
            var m=Engine.Json.Deserialize<UpdateManifest>(await GetText(manifestUrl,10000));ValidateManifest(m);
            if(m.Version!=remoteVersion)throw new InvalidDataException("릴리스와 업데이트 버전이 다릅니다.");
            if(cached!=null) {
                var cachedManifest=Engine.Json.Deserialize<UpdateManifest>(File.ReadAllText(Path.Combine(cached,"update-receipt.json"),Encoding.UTF8));
                if(ParseVersion(cachedManifest.Version)>=ParseVersion(remoteVersion))return cached;
            }
            string url=AssetUrl(release,m.AssetName);
            var asset=release.assets.Single(a=>a.name==m.AssetName);if(asset.size!=m.Size)throw new InvalidDataException("릴리스 파일 크기 불일치");
            string target=Engine.Child(root,"versions/"+m.Version);
            if(File.Exists(Engine.Child(target,"failed.json"))){log("v"+m.Version+"은 이전 시작 실패로 건너뜁니다. 기존 버전을 실행합니다.");return cached;}
            if(Directory.Exists(target)){ValidatePayload(target,m);return target;}
            string downloads=Engine.Child(root,"downloads");Directory.CreateDirectory(downloads);
            string zip=Engine.Child(downloads,Guid.NewGuid().ToString("N")+".zip");
            log("새 버전 v"+m.Version+" 다운로드 중");
            try {
                using(var client=new UpdateClient(30000)) {
                    bool overflow=false;
                    client.DownloadProgressChanged+=(s,e)=>{if(e.BytesReceived>m.Size||e.TotalBytesToReceive>MaxZip){overflow=true;client.CancelAsync();}};
                    var transfer=client.DownloadFileTaskAsync(new Uri(url),zip);
                    if(await Task.WhenAny(transfer,Task.Delay(60000))!=transfer){client.CancelAsync();throw new TimeoutException("업데이트 다운로드 시간 초과");}
                    await transfer;
                    if(overflow)throw new InvalidDataException("다운로드 크기 초과");
                }
                string stage=Engine.Child(root,"versions/.staging-"+Guid.NewGuid().ToString("N"));
                log("업데이트 무결성 검사 중");await Task.Run(()=>Stage(zip,stage,m));
                // Promote only a completely verified payload; failed staging folders are never launched.
                Directory.Move(stage,target);return target;
            }finally{if(File.Exists(zip))File.Delete(zip);}
        } catch(Exception ex) {
            log("자동 업데이트를 완료하지 못했습니다. 기존 버전으로 계속합니다: "+ex.Message);return cached;
        }
    }
    public static void MarkStarted(string token) {
        if(String.IsNullOrEmpty(token))return;
        if(!Regex.IsMatch(token,"^[0-9a-f]{32}$"))throw new InvalidDataException("잘못된 시작 토큰");
        string own=AppDomain.CurrentDomain.BaseDirectory;
        if(File.Exists(Path.Combine(own,"update-receipt.json")))File.WriteAllText(Engine.Child(own,"startup-ok"),BuildInfo.Version);
        string handoffs=Engine.Child(AppPaths.Root,"handoffs");Directory.CreateDirectory(handoffs);File.WriteAllText(Engine.Child(handoffs,token+".ready"),BuildInfo.Version);
    }
    public static async Task<bool> LaunchUpdated(string directory,Action<string> log,string handoffRoot=null) {
        string token=Guid.NewGuid().ToString("N"),ready=Engine.Child(handoffRoot??AppPaths.Root,"handoffs/"+token+".ready");
        Process child=null;
        try {
            log("새 버전을 시작합니다.");
            child=Process.Start(new ProcessStartInfo(Engine.Child(directory,Executable),"--updated-start "+token){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true});
            for(int i=0;i<100;i++) {
                if(File.Exists(ready)) {File.Delete(ready);log("업데이트 완료 · 새 버전으로 전환합니다.");return true;}
                if(child==null||child.HasExited)throw new IOException("새 버전 프로세스가 시작 중 종료됐습니다.");
                await Task.Delay(100);
            }
            throw new TimeoutException("새 버전의 시작 확인 시간이 초과됐습니다.");
        } catch(Exception ex) {
            if(child!=null)try{if(!child.HasExited)child.Kill();}catch{}
            try{File.WriteAllText(Engine.Child(directory,"failed.json"),Engine.Json.Serialize(new {Message=ex.Message,Time=DateTime.UtcNow.ToString("o")}));}catch{}
            log("새 버전 시작 실패 · 기존 버전을 유지합니다: "+ex.Message);return false;
        }finally{if(child!=null)child.Dispose();}
    }
    public static void SelfTest(string validZip,string manifestFile) {
        var m=Engine.Json.Deserialize<UpdateManifest>(File.ReadAllText(manifestFile));ValidateManifest(m);
        string root=Path.Combine(Path.GetTempPath(),"rkko-update-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        Action<Action> reject=action=>{bool rejected=false;try{action();}catch{rejected=true;}if(!rejected)throw new Exception("잘못된 업데이트를 허용했습니다.");};
        if(!IsNewer("0.10.0","0.9.0")||IsNewer("0.4.0","0.4.0")||IsNewer("0.3.0","0.4.0"))throw new Exception("버전 비교 실패");
        reject(()=>ParseVersion("0.4.0-beta"));reject(()=>ParseVersion("../1.0.0"));
        string target=Path.Combine(root,"versions",m.Version);Stage(validZip,target,m);
        File.WriteAllText(Path.Combine(target,"startup-ok"),m.Version);
        if(FindCachedNewer(root,"0.0.0")!=target||FindCachedNewer(root,m.Version)!=null)throw new Exception("캐시 버전 선택 실패");
        File.AppendAllText(Path.Combine(target,"translations","ko.json"),"corrupt");if(FindCachedNewer(root,"0.0.0")!=null)throw new Exception("손상 캐시 허용");
        string wrong=Path.Combine(root,"wrong.zip");File.WriteAllText(wrong,"invalid");reject(()=>Stage(wrong,Path.Combine(root,"wrong"),m));
        string evil=Path.Combine(root,"evil.zip");
        using(var zip=ZipFile.Open(evil,ZipArchiveMode.Create))using(var w=new StreamWriter(zip.CreateEntry("../escape.exe").Open()))w.Write("x");
        var bad=new UpdateManifest {Schema=m.Schema,Version=m.Version,AssetName=m.AssetName,Sha256=Engine.Hash(evil),Size=new FileInfo(evil).Length,Files=m.Files};
        reject(()=>Stage(evil,Path.Combine(root,"evil"),bad));if(File.Exists(Path.Combine(root,"escape.exe")))throw new Exception("ZIP 경로 이탈");
        var r=new GitHubRelease {tag_name="v"+m.Version,assets=new[]{new ReleaseAsset {name="update.json",browser_download_url="https://example.com/update.json"}}};reject(()=>AssetUrl(r,"update.json"));r.prerelease=true;reject(()=>AssetUrl(r,"update.json"));
        Console.WriteLine("PASS: version ordering, ZIP/hash validation, traversal, trusted URLs, cached fallback, corrupted cache rejection");
    }
    public static void TestHandoff(string root,string success,string failure) {
        string before=Engine.Hash(Path.Combine(failure,Executable));
        if(!LaunchUpdated(success,Console.WriteLine,root).GetAwaiter().GetResult())throw new Exception("새 버전 시작 확인 실패");
        if(LaunchUpdated(failure,Console.WriteLine,root).GetAwaiter().GetResult())throw new Exception("실패한 새 버전을 성공으로 처리했습니다.");
        if(!File.Exists(Path.Combine(failure,"failed.json"))||Engine.Hash(Path.Combine(failure,Executable))!=before)throw new Exception("시작 실패 복구 상태 오류");
        Console.WriteLine("PASS: real child-process handoff and startup-failure fallback; executables preserved");
    }
}
}
