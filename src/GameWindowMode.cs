using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RisingKingdomsKO {
public static class GameWindowMode {
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll",SetLastError=true)] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll",SetLastError=true)] static extern int SetWindowLong(IntPtr window,int index,int value);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool AdjustWindowRectEx(ref Rect rect,int style,bool menu,int extended);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    static bool FitsMonitor(Rect window,System.Drawing.Rectangle monitor) {
        return Math.Abs(window.Left-monitor.Left)<=2&&Math.Abs(window.Top-monitor.Top)<=2&&Math.Abs(window.Right-monitor.Right)<=2&&Math.Abs(window.Bottom-monitor.Bottom)<=2;
    }
    public static void SelfTest() {
        var monitor=new System.Drawing.Rectangle(0,0,2560,1440);
        if(FitsMonitor(new Rect {Left=150,Top=114,Right=1174,Bottom=882},monitor))throw new Exception("중앙의 작은 창을 전체 화면으로 오판했습니다.");
        if(!FitsMonitor(new Rect {Right=2560,Bottom=1440},monitor))throw new Exception("전체 화면 영역 판정 실패");
        if(FitsMonitor(new Rect {Right=3000,Bottom=1600},monitor))throw new Exception("잘린 화면을 전체 화면으로 오판했습니다.");
        if(!FitsMonitor(new Rect {Left=-1920,Top=431,Right=0,Bottom=1511},new System.Drawing.Rectangle(-1920,431,1920,1080)))throw new Exception("보조 모니터 영역 판정 실패");
    }
    public static async Task<string> Configure(Process process,string mode) {
        if(mode=="native")return "게임 기본 화면 설정";
        // The engine creates a popup even with Fullscreen=0. Give window mode a
        // normal frame, keeping the render client at its original dimensions.
        for(int attempt=0;attempt<60;attempt++) {
            await Task.Delay(250);if(process.HasExited)return "화면 확인 전에 게임이 종료됐습니다.";process.Refresh();
            IntPtr window=process.MainWindowHandle;
            if(window==IntPtr.Zero||process.MainWindowTitle!="Rising Kingdoms")continue;
            Rect client;if(!GetClientRect(window,out client)||client.Right<640||client.Bottom<480)continue;
            if(mode=="fullscreen") {
                var monitor=Screen.FromHandle(window).Bounds;
                Rect actual;if(!GetWindowRect(window,out actual))continue;
                bool covers=FitsMonitor(actual,monitor);
                if(!covers) {
                    // Some driver/window combinations keep the startup popup centered.
                    SetWindowLong(window,-16,(GetWindowLong(window,-16)&~0x00CF0000)|unchecked((int)0x80000000));
                    if(!SetWindowPos(window,IntPtr.Zero,monitor.Left,monitor.Top,monitor.Width,monitor.Height,0x0024))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"전체 화면 배치 실패");
                    await Task.Delay(500);
                    monitor=Screen.FromHandle(window).Bounds;
                    if(!GetWindowRect(window,out actual)||!FitsMonitor(actual,monitor))throw new InvalidOperationException("게임 창이 모니터 전체 영역과 일치하지 않습니다.");
                }
                return "전체 화면 확인 · 창 "+(actual.Right-actual.Left)+"×"+(actual.Bottom-actual.Top)+" / 모니터 "+monitor.Width+"×"+monitor.Height;
            }
            int style=(GetWindowLong(window,-16)&~unchecked((int)0x80CF0000))|0x00CA0000;
            Rect frame=new Rect {Right=client.Right,Bottom=client.Bottom};
            if(!AdjustWindowRectEx(ref frame,style,false,GetWindowLong(window,-20)))throw new InvalidOperationException("창 테두리 크기를 계산하지 못했습니다.");
            SetWindowLong(window,-16,style);
            var area=Screen.PrimaryScreen.WorkingArea;
            int width=frame.Right-frame.Left,height=frame.Bottom-frame.Top;
            if(!SetWindowPos(window,IntPtr.Zero,area.Left+Math.Max(0,(area.Width-width)/2),area.Top+Math.Max(0,(area.Height-height)/2),width,height,0x0034))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"창 모드 적용 실패");
            return "창 모드 확인 · 게임 영역 "+client.Right+"×"+client.Bottom;
        }
        throw new InvalidOperationException("게임 창을 찾지 못해 창 모드를 적용하지 못했습니다.");
    }
}
}
