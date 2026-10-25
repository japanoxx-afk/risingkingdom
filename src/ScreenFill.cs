using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RisingKingdomsKO {
// Only for Rising Kingdoms. Keep the original upstream archive in the cache.
public class ScreenReceipt {public PatchFile[] Files;public string ConfigOriginal;public string ConfigOriginalSha256;public string ConfigPatchedSha256;}
public static class ScreenFill {
    public const string Url="https://github.com/dege-diosg/dgVoodoo2/releases/download/v2.87.5/dgVoodoo2_87_5.zip";
    const string ZipHash="5ffde6927f7355ca3fdd5d785b581256a8e6539fa13e395a891ade6ba1040850";
    static readonly string[] LibraryHashes={"d03e2562178db1fcf3493fc0a0b23a465e35c55d73adb3ec095be866cd662704","612a24408a090a3c6f3886557fa18034ee742e94ad0a40ebdf854d2816176c2e","93c534f2d17419ea78f15551f7e0aac78b3c503733a840914fa063708a5afe8e"};
    public static readonly string[] Modes={"fullscreen","windowed","native"};
    public static bool ValidMode(string mode){return Modes.Contains(mode);}
    static string MakeConfig(int width,int height,string mode="fullscreen") {
        if(!ValidMode(mode)||mode=="native")throw new ArgumentException("지원하지 않는 화면 모드");
        if(width<640||height<480||width>16384||height>16384)throw new ArgumentOutOfRangeException("화면 해상도");
        string template;
        using(var input=typeof(ScreenFill).Assembly.GetManifestResourceStream("RisingKingdomsKO.ScreenFill.conf"))
        using(var reader=new StreamReader(input,Encoding.ASCII))template=reader.ReadToEnd();
        var values=new System.Collections.Generic.Dictionary<string,string> {
            {"OutputAPI","d3d11_fl11_0"},{"FullScreenMode",mode=="fullscreen"?"true":"false"},{"ScalingMode","stretched"},
            {"KeepWindowAspectRatio",mode=="windowed"?"true":"false"},{"CaptureMouse",mode=="windowed"?"false":"true"},{"DesktopResolution",width+"x"+height},
            {"DeframerSize","0"},{"AppControlledScreenMode","false"},{"dgVoodooWatermark","false"},
            {"Resolution","unforced"},{"PrimarySurfaceBatchedUpdate","true"},{"CenterAppWindow","true"},
            {"DisableAltEnterToToggleScreenMode","true"},{"WindowedAttributes",""}
        };
        foreach(var pair in values)template=System.Text.RegularExpressions.Regex.Replace(template,@"(?m)^"+pair.Key+@"[ \t]*=[^\r\n]*",pair.Key+" = "+pair.Value);
        return template;
    }
    static readonly string[] Names={"D3D8.dll","DDraw.dll","D3DImm.dll","dgVoodoo.conf"};
    static string Receipt(string root){return Engine.Child(root,".risingkingdom-screen.json");}
    public static string Archive {get{return Path.Combine(AppPaths.Root,"graphics","dgVoodoo2_87_5.zip");}}
    public static void Download() {
        Directory.CreateDirectory(Path.GetDirectoryName(Archive));
        if(File.Exists(Archive)&&new FileInfo(Archive).Length==9268078&&Engine.Hash(Archive)==ZipHash)return;
        string temp=Archive+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
            using(var client=new UpdateClient(60000))client.DownloadFile(Url,temp);
            if(new FileInfo(temp).Length!=9268078||Engine.Hash(temp)!=ZipHash)throw new InvalidDataException("화면 확대 구성요소 다운로드 검증 실패");
            if(File.Exists(Archive))File.Replace(temp,Archive,null);else File.Move(temp,Archive);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static byte[][] ReadLibraries(string archive) {
        if(new FileInfo(archive).Length!=9268078||Engine.Hash(archive)!=ZipHash)throw new InvalidDataException("공식 그래픽 구성요소 SHA-256 불일치");
        using(var zip=ZipFile.OpenRead(archive)) {
            var libraries=new byte[3][];
            int[] lengths={414208,255488,208384};
            for(int i=0;i<3;i++) {
                var entries=zip.Entries.Where(e=>e.FullName=="MS/x86/"+Names[i]).ToArray();
                if(entries.Length!=1||entries[0].Length!=lengths[i])throw new InvalidDataException("32비트 그래픽 구성요소가 없습니다.");
                using(var input=entries[0].Open())using(var output=new MemoryStream()){input.CopyTo(output);libraries[i]=output.ToArray();}
                using(var h=System.Security.Cryptography.SHA256.Create())if(BitConverter.ToString(h.ComputeHash(libraries[i])).Replace("-","").ToLowerInvariant()!=LibraryHashes[i])throw new InvalidDataException("그래픽 DLL 검증 실패");
            }
            return libraries;
        }
    }
    static void ValidateOwned(string root,PatchFile[] receipt) {
        if(receipt==null||receipt.Length!=Names.Length||!receipt.Select(f=>f.Path).SequenceEqual(Names))throw new InvalidDataException("화면 확대 복구 기록이 올바르지 않습니다.");
        foreach(var f in receipt) {
            string file=Engine.Child(root,f.Path);
            if(File.Exists(file)&&Engine.Hash(file)!=f.PatchedSha256)throw new IOException("외부에서 변경한 화면 구성요소를 보존합니다: "+f.Path);
        }
    }
    static string BytesHash(byte[] bytes) {using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static byte[] WindowedConfig(byte[] bytes,bool fullscreen=false) {
        if(bytes.Length>1024*1024||bytes.Contains((byte)0))throw new InvalidDataException("지원하지 않는 config.ini 형식");
        string text=Encoding.GetEncoding(1252).GetString(bytes);
        var section=System.Text.RegularExpressions.Regex.Match(text,@"(?ims)^[ \t]*\[system\][^\r\n]*(?:\r?\n)(.*?)(?=^[ \t]*\[|\z)");
        if(!section.Success)throw new InvalidDataException("config.ini에 system 섹션이 없습니다.");
        var key=new System.Text.RegularExpressions.Regex(@"(?im)^([ \t]*Fullscreen[ \t]*=[ \t]*)[01]([ \t]*(?:;[^\r\n]*)?\r?$)");
        if(key.Matches(section.Value).Count!=1)throw new InvalidDataException("config.ini의 Fullscreen 설정을 확인하세요.");
        string changed=key.Replace(section.Value,"${1}"+(fullscreen?"1":"0")+"${2}");
        // Intro video resets the native window after the wrapper has scaled it.
        var splash=new System.Text.RegularExpressions.Regex(@"(?im)^([ \t]*NoSplash[ \t]*=[ \t]*)[01]([ \t]*(?:;[^\r\n]*)?\r?$)");
        if(splash.Matches(changed).Count==1)changed=splash.Replace(changed,"${1}1${2}");
        else if(System.Text.RegularExpressions.Regex.IsMatch(changed,@"(?im)^[ \t]*NoSplash[ \t]*="))throw new InvalidDataException("config.ini의 NoSplash 설정을 확인하세요.");
        else changed=changed.TrimEnd('\r','\n')+"\r\nNoSplash=1\r\n";
        return Encoding.GetEncoding(1252).GetBytes(text.Substring(0,section.Index)+changed+text.Substring(section.Index+section.Length));
    }
    static void Install(string root,byte[][] libraries,string config,bool fullscreen=false) {
        Disable(root);
        foreach(string name in Names)if(File.Exists(Engine.Child(root,name)))throw new IOException("기존 그래픽 모드가 있어 덮어쓰지 않습니다: "+name+". 화면 꽉 채우기를 끄거나 기존 모드를 별도로 보관하세요.");
        string gameConfig=Engine.Child(root,"config.ini");byte[] original=File.ReadAllBytes(gameConfig),patched=WindowedConfig(original,fullscreen);
        byte[][] content=libraries.Concat(new[]{Encoding.ASCII.GetBytes(config)}).ToArray();
        var receipt=new ScreenReceipt {Files=new PatchFile[Names.Length],ConfigOriginal=Convert.ToBase64String(original),ConfigOriginalSha256=BytesHash(original),ConfigPatchedSha256=BytesHash(patched)};
        for(int i=0;i<Names.Length;i++)receipt.Files[i]=new PatchFile {Path=Names[i],PatchedSha256=BytesHash(content[i])};
        string[] staged=Names.Select(n=>Engine.Child(root,n+"."+Guid.NewGuid().ToString("N")+".tmp")).ToArray();
        string configTemp=Engine.Child(root,"config.ini."+Guid.NewGuid().ToString("N")+".tmp");
        try {
            for(int i=0;i<Names.Length;i++)using(var output=new FileStream(staged[i],FileMode.CreateNew,FileAccess.Write))output.Write(content[i],0,content[i].Length);
            File.WriteAllBytes(configTemp,patched);
            AppPaths.AtomicText(Receipt(root),Engine.Json.Serialize(receipt));
            for(int i=0;i<Names.Length;i++)File.Move(staged[i],Engine.Child(root,Names[i]));
            Engine.Replace(configTemp,gameConfig);ValidateOwned(root,receipt.Files);
        }catch {Disable(root);throw;}
        finally{foreach(string file in staged.Concat(new[]{configTemp}))if(File.Exists(file))File.Delete(file);}
    }
    public static void Enable(string root,string mode="fullscreen") {
        if(!ValidMode(mode))throw new ArgumentException("지원하지 않는 화면 모드");
        if(mode=="native"){Disable(root);return;}
        var screen=System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        Install(root,ReadLibraries(Archive),MakeConfig(screen.Width,screen.Height,mode),mode=="fullscreen");
    }
    public static void Disable(string root) {
        if(!File.Exists(Receipt(root)))return;
        var receipt=Engine.Json.Deserialize<ScreenReceipt>(File.ReadAllText(Receipt(root),Encoding.UTF8));
        if(receipt==null)throw new InvalidDataException("화면 확대 복구 기록이 없습니다.");
        ValidateOwned(root,receipt.Files);
        byte[] original=Convert.FromBase64String(receipt.ConfigOriginal);
        if(BytesHash(original)!=receipt.ConfigOriginalSha256||(BytesHash(WindowedConfig(original))!=receipt.ConfigPatchedSha256&&BytesHash(WindowedConfig(original,true))!=receipt.ConfigPatchedSha256))throw new InvalidDataException("화면 설정 백업 검증 실패");
        string config=Engine.Child(root,"config.ini"),current=Engine.Hash(config);
        if(current!=receipt.ConfigOriginalSha256&&current!=receipt.ConfigPatchedSha256)throw new IOException("외부에서 변경한 config.ini를 보존합니다. 화면 확대 기록을 확인하세요.");
        if(current!=receipt.ConfigOriginalSha256) {
            string temp=config+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllBytes(temp,original);Engine.Replace(temp,config);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        foreach(string name in Names){string file=Engine.Child(root,name);if(File.Exists(file))File.Delete(file);}
        File.Delete(Receipt(root));
    }

    public static void SelfTest() {
        string root=Path.Combine(Path.GetTempPath(),"rk-screen-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string initial="[system]\r\nFullscreen=1\r\nWindowX=1024\r\n[Other]\r\nKeep=unchanged\r\n";
            File.WriteAllText(Path.Combine(root,"config.ini"),initial,Encoding.ASCII);
            if(!Encoding.ASCII.GetString(WindowedConfig(Encoding.ASCII.GetBytes(initial),true)).Contains("Fullscreen=1")||!Encoding.ASCII.GetString(WindowedConfig(Encoding.ASCII.GetBytes(initial))).Contains("Fullscreen=0"))throw new Exception("엔진 전체 화면 설정 구분 실패");
            string config=MakeConfig(1920,1080);
            if(!Encoding.ASCII.GetString(WindowedConfig(Encoding.ASCII.GetBytes(initial))).Contains("NoSplash=1"))throw new Exception("시작 영상 충돌 방지 실패");
            if(!config.Contains("DesktopResolution = 1920x1080")||!config.Contains("ScalingMode = stretched"))throw new Exception("화면 설정 생성 실패");
            string windowed=MakeConfig(1920,1080,"windowed");
            if(!windowed.Contains("FullScreenMode = false")||!windowed.Contains("DisableAltEnterToToggleScreenMode = true"))throw new Exception("화면 모드 전환 설정 실패");
            byte[] fake={1,2,3};
            Install(root,new[]{fake,fake,fake},config,true);Disable(root);
            if(File.ReadAllText(Path.Combine(root,"config.ini"))!=initial)throw new Exception("전체 화면 설정 복원 실패");
            Install(root,new[]{fake,fake,fake},config);Install(root,new[]{fake,fake,fake},config);Disable(root);
            if(Names.Any(n=>File.Exists(Path.Combine(root,n)))||File.Exists(Receipt(root)))throw new Exception("화면 설정 복원 실패");
            if(File.ReadAllText(Path.Combine(root,"config.ini"))!=initial)throw new Exception("게임 설정 원본 복원 실패");
            File.WriteAllText(Path.Combine(root,Names[0]),"existing");
            bool refused=false;try{Install(root,new[]{fake,fake,fake},config);}catch(IOException){refused=true;}
            if(!refused||File.ReadAllText(Path.Combine(root,Names[0]))!="existing")throw new Exception("기존 그래픽 모드 보존 실패");
            File.Delete(Path.Combine(root,Names[0]));Install(root,new[]{fake,fake,fake},config);File.WriteAllText(Path.Combine(root,Names[3]),"external");
            refused=false;try{Disable(root);}catch(IOException){refused=true;}
            if(!refused||!File.Exists(Path.Combine(root,Names[0])))throw new Exception("외부 변경 보호 실패");
            File.WriteAllText(Path.Combine(root,Names[3]),config,Encoding.ASCII);File.Delete(Path.Combine(root,Names[0]));Disable(root);
            if(File.Exists(Receipt(root)))throw new Exception("중단된 적용 복구 실패");
            Install(root,new[]{fake,fake,fake},config);File.AppendAllText(Path.Combine(root,"config.ini"),";external");
            refused=false;try{Disable(root);}catch(IOException){refused=true;}
            if(!refused||!File.Exists(Path.Combine(root,Names[0])))throw new Exception("외부 게임 설정 변경 보호 실패");
            File.WriteAllBytes(Path.Combine(root,"config.ini"),WindowedConfig(Encoding.ASCII.GetBytes(initial)));Disable(root);
            if(File.ReadAllText(Path.Combine(root,"config.ini"))!=initial)throw new Exception("설정 재복원 실패");
        }finally{Directory.Delete(root,true);}
    }
}
}
