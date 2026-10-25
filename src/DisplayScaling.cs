using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RisingKingdomsKO {
// CCD scaling is temporary: never use SDC_SAVE_TO_DATABASE.
public sealed class DisplayScaling : IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct Luid {public uint Low;public int High;}
    [StructLayout(LayoutKind.Sequential)] struct Rational {public uint Numerator,Denominator;}
    [StructLayout(LayoutKind.Sequential)] struct Source {public Luid Adapter;public uint Id,Mode,Status;}
    [StructLayout(LayoutKind.Sequential)] struct Target {
        public Luid Adapter;public uint Id,Mode,Technology,Rotation,Scaling;
        public Rational Refresh;public uint ScanLine;public int Available;public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] struct DisplayPath {public Source Source;public Target Target;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] struct Signal {
        public ulong PixelRate;public Rational HSync,VSync;
        public uint ActiveWidth,ActiveHeight,TotalWidth,TotalHeight,Standard,ScanLine;
    }
    [StructLayout(LayoutKind.Sequential)] struct SourceMode {public uint Width,Height,Format;public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct Rectangle {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct DesktopImage {public Point Size;public Rectangle Region,Clip;}
    [StructLayout(LayoutKind.Explicit,Size=48)] struct ModeData {
        [FieldOffset(0)] public Signal Target;
        [FieldOffset(0)] public SourceMode Source;
        [FieldOffset(0)] public DesktopImage Image;
    }
    [StructLayout(LayoutKind.Sequential)] struct DisplayMode {public uint Type,Id;public Luid Adapter;public ModeData Data;}
    [StructLayout(LayoutKind.Sequential)] struct Header {public uint Type,Size;public Luid Adapter;public uint Id;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct SourceName {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)] struct Preferred {public Header Header;public uint Width,Height;public Signal Signal;}
    sealed class Snapshot {public DisplayPath[] Paths;public DisplayMode[] Modes;}
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags,ref uint count,[Out] DisplayPath[] paths,ref uint modeCount,[Out] DisplayMode[] modes,IntPtr topology);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint count,[In] DisplayPath[] paths,uint modeCount,[In] DisplayMode[] modes,uint flags);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo",CharSet=CharSet.Unicode)] static extern int GetName(ref SourceName name);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")] static extern int GetPreferred(ref Preferred preferred);
    const uint QueryActive=2,UseSupplied=0x20,Validate=0x40,Apply=0x80,AllowChanges=0x400;
    readonly Snapshot original;
    readonly object gate=new object();
    bool changed,disposed,warned;
    uint nativeWidth,nativeHeight,reportedScaling;
    System.Threading.Timer watcher;
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    DisplayScaling(Snapshot snapshot){original=snapshot;}
    static Snapshot Read(uint queryFlags=QueryActive) {
        for(int attempt=0;attempt<3;attempt++) {
            uint count,modes;int error=GetDisplayConfigBufferSizes(queryFlags,out count,out modes);
            if(error!=0)throw new Win32Exception(error,"화면 출력 정보 조회 실패");
            if(count==0||count>64||modes>256)throw new InvalidOperationException("화면 출력 정보 크기 오류");
            var result=new Snapshot {Paths=new DisplayPath[count],Modes=new DisplayMode[modes]};
            error=QueryDisplayConfig(queryFlags,ref count,result.Paths,ref modes,result.Modes,IntPtr.Zero);
            if(error==122)continue;
            if(error!=0)throw new Win32Exception(error,"화면 출력 구성 조회 실패");
            Array.Resize(ref result.Paths,(int)count);Array.Resize(ref result.Modes,(int)modes);return result;
        }
        throw new InvalidOperationException("화면 출력 구성이 변경 중입니다. 다시 실행하세요.");
    }
    static int Primary(Snapshot state) {
        string primary=Screen.PrimaryScreen.DeviceName;
        for(int i=0;i<state.Paths.Length;i++) {
            var source=state.Paths[i].Source;
            var name=new SourceName {Header=new Header {Type=1,Size=(uint)Marshal.SizeOf(typeof(SourceName)),Adapter=source.Adapter,Id=source.Id}};
            if(GetName(ref name)==0&&String.Equals(name.Name,primary,StringComparison.OrdinalIgnoreCase))return i;
        }
        throw new InvalidOperationException("기본 모니터 출력 경로를 찾지 못했습니다.");
    }
    public static DisplayScaling Capture(string mode){return new DisplayScaling(mode=="fullscreen"?Read():null);}
    public static string Inspect() {
        var state=Read();var path=state.Paths[Primary(state)];
        var source=state.Modes[path.Source.Mode].Data.Source;var target=state.Modes[path.Target.Mode].Data.Target;
        return "원본 "+source.Width+"×"+source.Height+" / 출력 "+target.ActiveWidth+"×"+target.ActiveHeight+" / 배율 "+path.Target.Scaling+" / "+InspectImage();
    }
    public static string InspectImage() {
        var state=Read(QueryActive|0x10);var path=state.Paths[Primary(state)];
        uint index=path.Target.Mode&0xFFFF;
        if((path.Flags&8)==0||index>=state.Modes.Length||state.Modes[index].Type!=3)return "가상 출력 이미지 없음";
        var image=state.Modes[index].Data.Image;
        return "출력 이미지 "+image.Size.X+"×"+image.Size.Y+" / 내용 영역 "+image.Region.Left+","+image.Region.Top+"~"+image.Region.Right+","+image.Region.Bottom+" / 원본 영역 "+image.Clip.Left+","+image.Clip.Top+"~"+image.Clip.Right+","+image.Clip.Bottom;
    }
    public string Stretch() {
        lock(gate){return StretchCore();}
    }
    string StretchCore() {
        if(original==null)return null;
        var state=Read();int index=Primary(state);var path=state.Paths[index];
        if(path.Target.Mode>=state.Modes.Length||path.Source.Mode>=state.Modes.Length)throw new InvalidOperationException("화면 출력 모드 참조 오류");
        var preferred=new Preferred {Header=new Header {Type=3,Size=(uint)Marshal.SizeOf(typeof(Preferred)),Adapter=path.Target.Adapter,Id=path.Target.Id}};
        int error=GetPreferred(ref preferred);
        if(error!=0)throw new Win32Exception(error,"모니터 기본 출력 해상도 조회 실패");
        var sourceBefore=state.Modes[path.Source.Mode].Data.Source;
        var initialPath=original.Paths[Primary(original)];
        var initialSignal=original.Modes[initialPath.Target.Mode].Data.Target;
        // Retain the user's native refresh rate when that signal is available.
        if(initialSignal.ActiveWidth==preferred.Signal.ActiveWidth&&initialSignal.ActiveHeight==preferred.Signal.ActiveHeight)preferred.Signal=initialSignal;
        // Keep the game's logical 4:3 surface; stretch it to the native signal.
        var target=state.Modes[path.Target.Mode];target.Data.Target=preferred.Signal;state.Modes[path.Target.Mode]=target;
        path.Target.Scaling=3; // DISPLAYCONFIG_SCALING_STRETCHED
        path.Target.Refresh=preferred.Signal.VSync;path.Target.ScanLine=preferred.Signal.ScanLine;state.Paths[index]=path;
        error=SetDisplayConfig((uint)state.Paths.Length,state.Paths,(uint)state.Modes.Length,state.Modes,UseSupplied|Validate|AllowChanges);
        if(error!=0)throw new Win32Exception(error,"여백 없는 화면 배율 검증 실패");
        error=SetDisplayConfig((uint)state.Paths.Length,state.Paths,(uint)state.Modes.Length,state.Modes,UseSupplied|Apply|AllowChanges);
        if(error!=0)throw new Win32Exception(error,"여백 없는 화면 배율 적용 실패");
        changed=true;
        var applied=Read();var actual=applied.Paths[Primary(applied)];var signal=applied.Modes[actual.Target.Mode].Data.Target;
        var sourceAfter=applied.Modes[actual.Source.Mode].Data.Source;
        if((actual.Target.Scaling!=3&&actual.Target.Scaling!=1)||signal.ActiveWidth!=preferred.Signal.ActiveWidth||signal.ActiveHeight!=preferred.Signal.ActiveHeight||sourceAfter.Width!=sourceBefore.Width||sourceAfter.Height!=sourceBefore.Height)throw new InvalidOperationException("그래픽 드라이버가 화면 늘리기 설정을 적용하지 않았습니다: "+Inspect()+" / 요청 출력 "+preferred.Signal.ActiveWidth+"×"+preferred.Signal.ActiveHeight+" / 원본 유지 "+sourceBefore.Width+"×"+sourceBefore.Height);
        nativeWidth=signal.ActiveWidth;nativeHeight=signal.ActiveHeight;reportedScaling=actual.Target.Scaling;
        return "가로·세로 늘리기 적용 · "+Inspect();
    }
    public void Watch(int processId,Action<string> log) {
        if(original==null)return;
        watcher=new System.Threading.Timer(s=>{
            lock(gate) {
                if(disposed)return;
                uint foreground;GetWindowThreadProcessId(GetForegroundWindow(),out foreground);
                if(foreground!=(uint)processId)return;
                try {
                    var state=Read();var path=state.Paths[Primary(state)];var signal=state.Modes[path.Target.Mode].Data.Target;
                    if(path.Target.Scaling!=reportedScaling||signal.ActiveWidth!=nativeWidth||signal.ActiveHeight!=nativeHeight)log(StretchCore());
                    warned=false;
                }catch(Exception ex){if(!warned){log("화면 배율 재적용 오류: "+ex.Message);warned=true;}}
            }
        },null,1000,1000);
    }
    public void Dispose() {
        lock(gate) {
            disposed=true;if(watcher!=null)watcher.Dispose();
            if(!changed||original==null)return;
            int error=SetDisplayConfig((uint)original.Paths.Length,original.Paths,(uint)original.Modes.Length,original.Modes,UseSupplied|Apply|AllowChanges);
            if(error!=0)throw new Win32Exception(error,"기존 화면 배율 복원 실패");
            changed=false;
        }
    }
    public static void SelfTest() {
        if(Marshal.SizeOf(typeof(DisplayPath))!=72||Marshal.SizeOf(typeof(DisplayMode))!=64||Marshal.SizeOf(typeof(SourceName))!=84||Marshal.SizeOf(typeof(Preferred))!=80)throw new Exception("Windows 화면 배율 API 구조체 크기 불일치");
    }
}
}
