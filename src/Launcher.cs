using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RisingKingdomsKO {
public class Entry { public string Path; public uint Offset; public uint Size; }
public class Pak {
    public static List<Entry> Read(string path) {
        using (var r = new BinaryReader(File.OpenRead(path))) {
            if (Encoding.ASCII.GetString(r.ReadBytes(16)) != "HMMSYS PackFile\n") throw new InvalidDataException("지원하지 않는 PAK: " + path);
            r.BaseStream.Position = 32;
            int count = r.ReadInt32(), dir = r.ReadInt32();
            if(count < 0 || count > 100000 || dir < 0 || 40L+dir > r.BaseStream.Length) throw new InvalidDataException("잘못된 PAK 헤더");
            string previous = ""; var result = new List<Entry>();
            for(int i=0; i<count; i++) {
                int length=r.ReadByte(), prefix=r.ReadByte();
                if(prefix > previous.Length || prefix > length) throw new InvalidDataException("잘못된 PAK 경로 압축");
                byte[] tail=r.ReadBytes(length-prefix);
                if(tail.Length != length-prefix) throw new EndOfStreamException();
                string name=previous.Substring(0,prefix)+Encoding.ASCII.GetString(tail);
                uint offset=r.ReadUInt32(), size=r.ReadUInt32();
                if(offset < 40L+dir || (long)offset+size > r.BaseStream.Length) throw new InvalidDataException("잘못된 PAK 범위");
                result.Add(new Entry {Path=name,Offset=offset,Size=size}); previous=name;
            }
            if(r.BaseStream.Position != 40L+dir) throw new InvalidDataException("PAK 디렉터리 길이 불일치");
            return result;
        }
    }
}
public class PatchFile { public string Path; public string OriginalSha256; public string PatchedSha256; }
public class Manifest {
    public int Schema; public string Version; public bool Complete; public bool KoreanFontVerified;
    public string Scope; public string TranslationMethod; public bool FontFormatVerified; public bool RuntimeVerified;
    public int TranslatedStrings; public int TotalStrings; public List<PatchFile> Files;
}
public class Backup { public string Game; public List<PatchFile> Files; }
public static class Engine {
    public static JavaScriptSerializer Json = new JavaScriptSerializer {MaxJsonLength=int.MaxValue};
    public static string Hash(string path) { using(var s=File.OpenRead(path)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
    public static string Child(string root, string relative) {
        if(String.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) || relative.Contains(":") || relative.Split('\\','/').Any(x=>x==".." || x=="." || x.Length==0)) throw new InvalidDataException("허용되지 않는 상대 경로: "+relative);
        string full=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,relative));
        string canonical=System.IO.Path.GetFullPath(root).TrimEnd('\\')+"\\";
        if(!full.StartsWith(canonical,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("폴더 밖 경로");
        string cursor=full;
        while(cursor!=null && cursor.Length>=canonical.TrimEnd('\\').Length) {
            if((File.Exists(cursor)||Directory.Exists(cursor)) && (File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("링크 경로는 지원하지 않습니다: "+cursor);
            cursor=System.IO.Path.GetDirectoryName(cursor);
        }
        return full;
    }
    public static void ValidateGame(string game) {
        if(!File.Exists(System.IO.Path.Combine(game,"RK.exe")) || !File.Exists(System.IO.Path.Combine(game,"Packs","data.pak"))) throw new InvalidDataException("RK.exe와 Packs\\data.pak가 있는 게임 폴더를 선택하세요.");
        if(Process.GetProcessesByName("RK").Length>0) throw new IOException("게임을 종료한 뒤 다시 시도하세요.");
    }
    public static string State(string game) { return Child(game,".risingkingdom-ko"); }
    public static Manifest LoadPackage(string package) {
        if(!File.Exists(System.IO.Path.Combine(package,"manifest.json"))) throw new InvalidDataException("한글 번역 패키지가 없습니다. 텍스트 패치를 먼저 생성하세요.");
        var m=Json.Deserialize<Manifest>(File.ReadAllText(System.IO.Path.Combine(package,"manifest.json"),Encoding.UTF8));
        if(m==null || m.Schema!=1 || String.IsNullOrWhiteSpace(m.Version) || (!(m.Complete&&m.KoreanFontVerified) && !(m.Scope=="game-text"&&m.FontFormatVerified)) || m.TotalStrings<=0 || m.TranslatedStrings!=m.TotalStrings || m.Files==null || m.Files.Count==0) throw new InvalidDataException("번역 또는 한글 글꼴 형식 검증이 완료된 패치 패키지가 아닙니다.");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in m.Files) {
            if(f==null || String.IsNullOrEmpty(f.Path)) throw new InvalidDataException("잘못된 파일 항목");
            string normalized=f.Path.Replace('/','\\');
            if(!seen.Add(normalized) || !(normalized.StartsWith("Local\\",StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Packs\\",StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("adventures\\",StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Scenarios\\",StringComparison.OrdinalIgnoreCase)) || Regex.IsMatch(normalized,@"\.(exe|dll|bat|cmd|ps1)$",RegexOptions.IgnoreCase)) throw new InvalidDataException("허용되지 않는 패치 대상: "+normalized);
            if(!Regex.IsMatch(f.OriginalSha256??"", "^[0-9a-f]{64}$") || !Regex.IsMatch(f.PatchedSha256??"", "^[0-9a-f]{64}$")) throw new InvalidDataException("잘못된 SHA-256");
            if(Hash(Child(System.IO.Path.Combine(package,"files"),normalized))!=f.PatchedSha256) throw new InvalidDataException("패치 파일 손상: "+normalized);
        }
        return m;
    }
    public static void Install(string game,string package,Action<string> log) {
        ValidateGame(game); var m=LoadPackage(package); string state=State(game);
        if(Directory.Exists(state)) throw new IOException("기존 패치 또는 복구 기록이 있습니다. 먼저 원본 복원을 실행하세요.");
        foreach(var f in m.Files) if(Hash(Child(game,f.Path))!=f.OriginalSha256) throw new InvalidDataException("원본 버전 불일치: "+f.Path);
        if(m.Scope=="game-text")RuntimeContracts.ValidatePak(Child(game,"Packs/data.pak"),Child(package,"files/Packs/data.pak"));
        // Finish every backup before changing any game file. A persistent journal enables retry after interruption.
        Directory.CreateDirectory(state);
        File.WriteAllText(System.IO.Path.Combine(state,"backup.json"),Json.Serialize(new Backup {Game=System.IO.Path.GetFullPath(game),Files=m.Files}),Encoding.UTF8);
        foreach(var f in m.Files) {
            string target=Child(state,"original\\"+f.Path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
            File.Copy(Child(game,f.Path),target,false);
            if(Hash(target)!=f.OriginalSha256) throw new IOException("백업 검증 실패: "+f.Path);
        }
        File.WriteAllText(System.IO.Path.Combine(state,"ready"),"backup complete");
        try {
            foreach(var f in m.Files) {
                string staged=Child(state,"staged\\"+f.Path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(staged));
                File.Copy(Child(System.IO.Path.Combine(package,"files"),f.Path),staged,true);
                if(Hash(staged)!=f.PatchedSha256) throw new IOException("패치 검증 실패");
                Replace(staged,Child(game,f.Path)); log("적용: "+f.Path);
            }
            File.WriteAllText(System.IO.Path.Combine(state,"installed"),m.Version,Encoding.UTF8);
        } catch { Restore(game,log); throw; }
    }
    public static void Replace(string source,string target) {
        string temp=target+".rkko.tmp";
        if(File.Exists(temp)) throw new IOException("이전 임시 파일을 확인하세요: "+temp);
        try { File.Copy(source,temp,false); File.Replace(temp,target,null); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    public static void Restore(string game,Action<string> log) {
        ValidateGame(game); string state=State(game);
        var b=Json.Deserialize<Backup>(File.ReadAllText(Child(state,"backup.json"),Encoding.UTF8));
        if(b==null || b.Files==null || !String.Equals(b.Game,System.IO.Path.GetFullPath(game),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("백업 경로 불일치");
        bool ready=File.Exists(Child(state,"ready"));
        foreach(var f in b.Files) {
            string original=Child(state,"original\\"+f.Path);
            if(ready && (!File.Exists(original)||Hash(original)!=f.OriginalSha256)) throw new IOException("백업 손상: "+f.Path);
            string current=Hash(Child(game,f.Path));
            if(current!=f.OriginalSha256 && (!ready || current!=f.PatchedSha256)) throw new IOException("외부에서 변경된 파일입니다. 복원을 중단했습니다: "+f.Path);
        }
        foreach(var f in b.Files) if(Hash(Child(game,f.Path))!=f.OriginalSha256) { Replace(Child(state,"original\\"+f.Path),Child(game,f.Path)); log("복원: "+f.Path); }
        // Retain originals for manual recovery; move only the verified state directory within this game folder.
        string archived=Child(game,".risingkingdom-ko-backup-"+Guid.NewGuid().ToString("N"));
        Directory.Move(state,archived); log("원본 복원 완료. 백업 보관: "+archived);
    }
    public static void Inspect(string game,string output,Action<string> log) {
        ValidateGame(game); Directory.CreateDirectory(output);
        var inventory=new List<object>(); var catalog=new List<object>();
        var marker=new Regex(@"\[\[(\d+)\]\]([\s\S]*?)\[\[\]\]",RegexOptions.Compiled);
        foreach(string rel in new[]{"Packs\\data.pak","Packs\\assets.pak","Local\\english.pak"}) {
            string path=Child(game,rel); var entries=Pak.Read(path); int strings=0;
            using(var r=new BinaryReader(File.OpenRead(path))) foreach(var e in entries) {
                inventory.Add(new {Archive=rel,Path=e.Path,Offset=e.Offset,Size=e.Size});
                if(!Regex.IsMatch(e.Path,@"\.(ini|xml|txt|vs)$",RegexOptions.IgnoreCase)) continue;
                r.BaseStream.Position=e.Offset; byte[] bytes=r.ReadBytes(checked((int)e.Size));
                string text=Encoding.GetEncoding(1252).GetString(bytes);
                foreach(Match match in marker.Matches(text)) {
                    catalog.Add(new {Archive=rel,Path=e.Path,Id=match.Groups[1].Value,Source=match.Groups[2].Value,Korean=""}); strings++;
                }
            }
            log(rel+": "+entries.Count+"개 파일 / 번역 표식 "+strings+"개");
        }
        File.WriteAllText(System.IO.Path.Combine(output,"inventory.json"),Json.Serialize(inventory),Encoding.UTF8);
        File.WriteAllText(System.IO.Path.Combine(output,"translation-catalog.json"),Json.Serialize(catalog),Encoding.UTF8);
        File.WriteAllText(System.IO.Path.Combine(output,"campaign-catalog.json"),Json.Serialize(Campaign.Inspect(game,log)),Encoding.UTF8);
        log("번역 조사 자료 저장: "+output); log("이미지 속 영문·EXE 내장 문구·사용자 맵은 조사 범위에서 제외됩니다.");
    }
}
public class Launcher : Form {
    TextBox game=new TextBox(), package=new TextBox(), log=new TextBox(); Label status=new Label();
    FlowLayoutPanel buttons;
    CheckBox fillScreen=new CheckBox();
    bool gameRunning;
    bool checkingUpdate;
    public Launcher(string startupToken=null,bool automaticUpdates=true) {
        Text="라이징 킹덤즈 · 한글패치 런처 v"+BuildInfo.Version; Size=new Size(800,650); MinimumSize=new Size(760,620); Font=new Font("맑은 고딕",10); BackColor=Color.FromArgb(24,29,38); ForeColor=Color.Gainsboro;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(26),ColumnCount=1,RowCount=11}; Controls.Add(layout);
        layout.Controls.Add(new Label {Text="RISING KINGDOMS",Font=new Font("Segoe UI",24,FontStyle.Bold),AutoSize=true});
        layout.Controls.Add(new Label {Text="런처 v"+BuildInfo.Version+" · 번역 패치 v"+BuildInfo.PatchVersion,AutoSize=true});
        layout.Controls.Add(new Label {Text="게임 설치 폴더",AutoSize=true});
        var settings=AppPaths.LoadSettings();
        game.Text=String.IsNullOrWhiteSpace(settings.GamePath)?@"C:\Program Files (x86)\Steam\steamapps\common\Rising Kingdoms":settings.GamePath; layout.Controls.Add(PathRow(game));
        layout.Controls.Add(new Label {Text="텍스트 패키지 폴더 (첫 적용 시 설치본에서 자동 생성)",AutoSize=true});
        package.Text=AppPaths.DefaultPackage; layout.Controls.Add(PathRow(package));
        status.AutoSize=true; status.MaximumSize=new Size(700,0); status.Text="텍스트 한글패치 · 기계번역 초안 + 메뉴·기술명 보정\n메뉴·설명·캠페인 대사·튜토리얼 텍스트 / 전체 문맥 검수 전"; status.ForeColor=Color.FromArgb(240,193,106); layout.Controls.Add(status);
        fillScreen.Text="화면 꽉 채우기 (가로·세로 확대 · 첫 사용 시 구성요소 다운로드)"; fillScreen.AutoSize=true; fillScreen.Checked=settings.FillScreen??true; layout.Controls.Add(fillScreen);
        buttons=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill}; layout.Controls.Add(buttons);
        AddButton(buttons,"설치본 조사",()=>Engine.Inspect(game.Text,System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reports"),Log));
        AddButton(buttons,"한글패치 적용",()=>{
            AppPaths.SaveSettings(game.Text,fillScreen.Checked);
            if(!File.Exists(System.IO.Path.Combine(package.Text,"manifest.json")))TextPatch.Build(game.Text,AppPaths.Dictionary,package.Text,Log);
            var manifest=Engine.LoadPackage(package.Text);Engine.Install(game.Text,package.Text,Log);status.Text="텍스트 패치 적용 완료 · "+manifest.TranslatedStrings+"개 키\n기계번역 초안 / 전체 문맥 검수 전";
        });
        AddButton(buttons,"원본 복원",()=>{Engine.ValidateGame(game.Text);ScreenFill.Disable(game.Text);Engine.Restore(game.Text,Log); status.Text="원본 복원 완료";});
        AddButton(buttons,"게임 실행",StartGame);
        var updateButton=new Button {Text="업데이트 확인",AutoSize=true,Height=36,BackColor=Color.FromArgb(51,65,83),FlatStyle=FlatStyle.Flat};updateButton.Click+=async(s,e)=>await CheckUpdate();buttons.Controls.Add(updateButton);
        log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill; log.BackColor=Color.FromArgb(16,20,28); log.ForeColor=Color.Gainsboro; layout.Controls.Add(log);
        layout.RowStyles.Clear(); for(int i=0;i<9;i++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.Controls.Add(new Label {Text="쓰기 권한 오류가 나면 런처를 관리자 권한으로 실행하세요.",AutoSize=true});
        if(automaticUpdates) {
            Shown+=async(s,e)=>{
                try {
                    AutoUpdater.MarkStarted(startupToken);
                    string installed=System.IO.Path.Combine(game.Text,".risingkingdom-ko","installed");
                    Log(File.Exists(installed)?"게임에 적용된 패치: "+File.ReadAllText(installed):"게임에 적용된 패치 기록 없음");
                    if(startupToken==null)await CheckUpdate();else Log("자동 업데이트로 시작됨 · v"+BuildInfo.Version);
                }catch(Exception ex){Log("시작 확인 오류: "+ex.Message);}
            };
            FormClosing+=(s,e)=>{try{AppPaths.SaveSettings(game.Text,fillScreen.Checked);}catch{}};
        }
    }
    async System.Threading.Tasks.Task CheckUpdate() {
        if(checkingUpdate||gameRunning)return;checkingUpdate=true;buttons.Enabled=false;
        try {AppPaths.SaveSettings(game.Text,fillScreen.Checked);string directory=await AutoUpdater.CheckAndStage(Log);if(!IsDisposed&&directory!=null&&await AutoUpdater.LaunchUpdated(directory,Log))Close();}
        catch(Exception ex){Log("업데이트 오류 · 기존 버전으로 계속합니다: "+ex.Message);}
        finally{if(!IsDisposed)buttons.Enabled=true;checkingUpdate=false;}
    }
    async void StartGame() {
        if(gameRunning||checkingUpdate)return;gameRunning=true;buttons.Enabled=false;fillScreen.Enabled=false;game.Enabled=false;
        try {
            string root=game.Text;Engine.ValidateGame(root);string state=Engine.State(root);
            if(Directory.Exists(state)) {
                if(!File.Exists(System.IO.Path.Combine(state,"installed")))throw new IOException("중단된 패치 기록이 있습니다. 먼저 원본 복원을 실행하세요.");
                try{RuntimeContracts.ValidatePak(Engine.Child(state,"original/Packs/data.pak"),Engine.Child(root,"Packs/data.pak"));}
                catch(InvalidDataException){throw new InvalidDataException("기존 패치에서 종족 내부 ID 변경을 감지했습니다. 원본 복원 후 최신 한글패치를 적용하세요.");}
            }
            AppPaths.SaveSettings(root,fillScreen.Checked);
            if(fillScreen.Checked) {
                Log("화면 꽉 채우기 준비 · 공식 dgVoodoo 2.87.5 확인 (첫 사용 시 약 9 MB)");
                await System.Threading.Tasks.Task.Run(()=>ScreenFill.Download());
                if(IsDisposed)return;Engine.ValidateGame(root);ScreenFill.Enable(root);
                Log("화면 꽉 채우기 적용 · 메뉴와 전투 화면을 모니터 크기로 확대합니다.");
            }else {ScreenFill.Disable(root);Log("화면 확대 해제 · 기본 게임 화면으로 실행합니다.");}
            DateTime started=DateTime.UtcNow;
            using(var process=Process.Start(new ProcessStartInfo(System.IO.Path.Combine(root,"RK.exe")){WorkingDirectory=root,UseShellExecute=false})) {
                Log("게임 실행 · 종료 상태와 크래시 로그를 확인합니다.");
                await System.Threading.Tasks.Task.Run(()=>process.WaitForExit());if(IsDisposed)return;
                string error=GameMonitor.Failure(root,started,process.ExitCode);
                if(error!=null){Log(error);MessageBox.Show(this,error,"게임 비정상 종료 감지",MessageBoxButtons.OK,MessageBoxIcon.Warning);}else Log("게임 종료 · 종료 코드 "+process.ExitCode);
            }
        }catch(Exception ex){if(!IsDisposed){Log("게임 실행 오류: "+ex.Message);MessageBox.Show(this,ex.Message,"게임 실행",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
        finally{gameRunning=false;if(!IsDisposed){buttons.Enabled=true;fillScreen.Enabled=true;game.Enabled=true;}}
    }
    Control PathRow(TextBox box) { var panel=new TableLayoutPanel {ColumnCount=2,Dock=DockStyle.Top,AutoSize=true}; panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80)); box.Dock=DockStyle.Fill; panel.Controls.Add(box); var browse=new Button {Text="찾아보기",AutoSize=true}; browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog()){d.SelectedPath=box.Text; if(d.ShowDialog()==DialogResult.OK)box.Text=d.SelectedPath;}}; panel.Controls.Add(browse); return panel; }
    void AddButton(FlowLayoutPanel p,string text,Action action) { var b=new Button {Text=text,AutoSize=true,Height=36,BackColor=Color.FromArgb(51,65,83),FlatStyle=FlatStyle.Flat}; b.Click+=(s,e)=>{UseWaitCursor=true; b.Enabled=false; try{action();}catch(Exception ex){Log("오류: "+ex.Message); MessageBox.Show(this,ex.Message,"작업을 완료하지 못했습니다",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{UseWaitCursor=false;b.Enabled=true;}}; p.Controls.Add(b); }
    void Log(string line) { if(IsDisposed||log.IsDisposed)return;log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+line+Environment.NewLine); log.Refresh(); }
    [STAThread] public static int Main(string[] args) {
        try {
            if(args.Length==3 && args[0]=="--inspect") {Engine.Inspect(args[1],args[2],Console.WriteLine);return 0;}
            if(args.Length==4 && args[0]=="--build-text") {TextPatch.Build(args[1],args[2],args[3],Console.WriteLine);return 0;}
            if(args.Length==3 && args[0]=="--apply") {Engine.Install(args[1],args[2],Console.WriteLine);return 0;}
            if(args.Length==2 && args[0]=="--restore") {Engine.ValidateGame(args[1]);ScreenFill.Disable(args[1]);Engine.Restore(args[1],Console.WriteLine);return 0;}
            if(args.Length==3 && args[0]=="--screen-fill") {Engine.ValidateGame(args[1]);if(args[2]=="on"){ScreenFill.Download();ScreenFill.Enable(args[1]);}else if(args[2]=="off")ScreenFill.Disable(args[1]);else throw new ArgumentException("on/off 필요");return 0;}
            if(args.Length==1 && args[0]=="--self-test") {SelfTest.Run();return 0;}
            if(args.Length==3 && args[0]=="--updater-self-test") {AutoUpdater.SelfTest(args[1],args[2]);return 0;}
            if(args.Length==4 && args[0]=="--test-handoff") {AutoUpdater.TestHandoff(args[1],args[2],args[3]);return 0;}
            if(args.Length==1 && args[0]=="--check-updates") {string update=AutoUpdater.CheckAndStage(Console.WriteLine).GetAwaiter().GetResult();Console.WriteLine(update==null?"UPDATE_CHECK_COMPLETE":"STAGED: "+update);return 0;}
            if(args.Length==2 && args[0]=="--preview") {Application.EnableVisualStyles(); using(var form=new Launcher(null,false)) {form.Show(); Application.DoEvents(); using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(args[1]);} form.Close();}return 0;}
            string startupToken=args.Length==2&&args[0]=="--updated-start"?args[1]:null;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new Launcher(startupToken));return 0;
        } catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
public static class SelfTest {
    static void Check(bool value,string name){if(!value)throw new Exception("실패: "+name);}
    static void Reject(Action action) {bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,"거부되어야 하는 입력");}
    public static void Run() {
        RuntimeContracts.SelfTest();
        GameMonitor.SelfTest();
        ScreenFill.SelfTest();
        string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"rkko-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string game=System.IO.Path.Combine(root,"game"), pack=System.IO.Path.Combine(root,"pack"); Directory.CreateDirectory(System.IO.Path.Combine(game,"Packs")); Directory.CreateDirectory(System.IO.Path.Combine(pack,"files","Packs"));
        File.WriteAllText(System.IO.Path.Combine(game,"RK.exe"),"fixture"); string original=System.IO.Path.Combine(game,"Packs","data.pak"), changed=System.IO.Path.Combine(pack,"files","Packs","data.pak"); File.WriteAllText(original,"original"); File.WriteAllText(changed,"translated");
        var m=new Manifest {Schema=1,Version="test",Complete=true,KoreanFontVerified=true,TotalStrings=1,TranslatedStrings=1,Files=new List<PatchFile>{new PatchFile {Path="Packs/data.pak",OriginalSha256=Engine.Hash(original),PatchedSha256=Engine.Hash(changed)}}};
        string manifest=System.IO.Path.Combine(pack,"manifest.json"); Action save=()=>File.WriteAllText(manifest,Engine.Json.Serialize(m)); save();
        Reject(()=>Engine.Child(root,"../escape")); Reject(()=>Engine.Child(root,"C:\\escape")); Reject(()=>Engine.Child(root,"a:stream"));
        m.Complete=false;save();Reject(()=>Engine.LoadPackage(pack));m.Complete=true;save();
        File.WriteAllText(changed,"corrupted");Reject(()=>Engine.Install(game,pack,s=>{}));Check(File.ReadAllText(original)=="original","손상 패키지 원본 보존");File.WriteAllText(changed,"translated");
        Engine.Install(game,pack,s=>{});Check(File.ReadAllText(original)=="translated","설치");Reject(()=>Engine.Install(game,pack,s=>{}));
        File.WriteAllText(original,"external update");Reject(()=>Engine.Restore(game,s=>{}));Check(File.ReadAllText(original)=="external update","외부 변경 보존");File.WriteAllText(original,"translated");
        Engine.Restore(game,s=>{});Check(File.ReadAllText(original)=="original","복원");
        Engine.Install(game,pack,s=>{});File.Delete(System.IO.Path.Combine(Engine.State(game),"installed"));Engine.Restore(game,s=>{});Check(File.ReadAllText(original)=="original","중단 복구");
        TextPatch.CheckTokens("Attack %s1 #GetClassInt('A','B')# @A","공격 %s1 #GetClassInt('A','B')# (@A)");
        Reject(()=>TextPatch.CheckTokens("Damage %s1","피해 %s2"));Reject(()=>TextPatch.CheckTokens("@Attack","공격"));Reject(()=>TextPatch.CheckTokens("Damage #GetClassInt('A','B')#","피해 100"));
        File.WriteAllText(System.IO.Path.Combine(root,"PASS.txt"),"PASS: install, restore, interrupted recovery, traversal, incomplete package, corruption, external changes");
        Console.WriteLine("PASS: "+root);
    }
}
}
