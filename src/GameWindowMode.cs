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
    [DllImport("user32.dll")] static extern bool AdjustWindowRectEx(ref Rect rect,int style,bool menu,int extended);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    public static async Task Configure(Process process,string mode) {
        if(mode!="windowed")return;
        // The engine creates a popup even with Fullscreen=0. Give window mode a
        // normal frame, keeping the render client at its original dimensions.
        for(int attempt=0;attempt<60;attempt++) {
            await Task.Delay(250);if(process.HasExited)return;process.Refresh();
            IntPtr window=process.MainWindowHandle;
            if(window==IntPtr.Zero||process.MainWindowTitle!="Rising Kingdoms")continue;
            Rect client;if(!GetClientRect(window,out client)||client.Right<640||client.Bottom<480)continue;
            int style=(GetWindowLong(window,-16)&~unchecked((int)0x80CF0000))|0x00CA0000;
            Rect frame=new Rect {Right=client.Right,Bottom=client.Bottom};
            if(!AdjustWindowRectEx(ref frame,style,false,GetWindowLong(window,-20)))throw new InvalidOperationException("창 테두리 크기를 계산하지 못했습니다.");
            SetWindowLong(window,-16,style);
            var area=Screen.PrimaryScreen.WorkingArea;
            int width=frame.Right-frame.Left,height=frame.Bottom-frame.Top;
            if(!SetWindowPos(window,IntPtr.Zero,area.Left+Math.Max(0,(area.Width-width)/2),area.Top+Math.Max(0,(area.Height-height)/2),width,height,0x0034))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"창 모드 적용 실패");
            return;
        }
        throw new InvalidOperationException("게임 창을 찾지 못해 창 모드를 적용하지 못했습니다.");
    }
}
}
