using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace RisingKingdomsKO {
// Resource descriptions are cached before the language table is loaded.
// Change only their verified data literals in our newly created, suspended game.
// Keep each literal's byte length, markers and terminator; never edit RK.exe.
public static class NativeGame {
    static readonly string[] EarlyIds={"1783994","1778642","1767414","1759051"};
    sealed class Edit {public uint Rva;public byte[] Before,After;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
        public int Size;public string Reserved,Desktop,Title;
        public uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags;
        public ushort Show,ReservedSize;public IntPtr ReservedBytes,Input,Output,Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr Process,Thread;public uint Id,ThreadId;}
    [StructLayout(LayoutKind.Sequential)] struct BasicInfo {public IntPtr Reserved,Peb,Reserved2,Reserved3,Id,Reserved4;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string application,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr process,IntPtr address,[Out] byte[] bytes,UIntPtr size,out UIntPtr read);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] bytes,UIntPtr size,out UIntPtr written);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtectEx(IntPtr process,IntPtr address,UIntPtr size,uint protection,out uint previous);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint exit);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll",EntryPoint="NtQueryInformationProcess")] static extern int QueryWow64(IntPtr process,int type,out IntPtr info,int size,out int length);
    [DllImport("ntdll.dll",EntryPoint="NtQueryInformationProcess")] static extern int QueryBasic(IntPtr process,int type,out BasicInfo info,int size,out int length);
    static byte[] Read(IntPtr process,long address,int count) {
        byte[] bytes=new byte[count];UIntPtr read;
        if(!ReadProcessMemory(process,new IntPtr(address),bytes,(UIntPtr)count,out read)||read.ToUInt64()!=(ulong)count)throw new Win32Exception(Marshal.GetLastWin32Error(),"게임 설명 메모리 확인 실패");
        return bytes;
    }
    static long ImageBase(IntPtr process) {
        IntPtr peb;int length;
        if(IntPtr.Size==8) {
            if(QueryWow64(process,26,out peb,IntPtr.Size,out length)!=0||peb==IntPtr.Zero)throw new InvalidDataException("32비트 게임 프로세스 확인 실패");
        }else {
            BasicInfo info;if(QueryBasic(process,0,out info,Marshal.SizeOf(typeof(BasicInfo)),out length)!=0)throw new InvalidDataException("게임 시작 정보 확인 실패");peb=info.Peb;
        }
        return BitConverter.ToUInt32(Read(process,peb.ToInt64()+8,4),0);
    }
    static byte[] Literal(string id,string source,string translated) {
        TextPatch.CheckTokens(source,translated);
        byte[] old=Encoding.GetEncoding(1252).GetBytes(source),text=Encoding.UTF8.GetBytes(translated);
        if(text.Length>old.Length)throw new InvalidDataException("자원 설명 번역을 갱신해야 합니다. 원본 복원 후 최신 한글패치를 적용하세요. ID: "+id);
        return Encoding.UTF8.GetBytes("[["+id+"]]"+translated+new string(' ',old.Length-text.Length)+"[[]]");
    }
    static List<Edit> Edits(byte[] executable,XmlDocument table) {
        if(executable.Length<64||executable[0]!='M'||executable[1]!='Z')throw new InvalidDataException("게임 실행 파일 형식 오류");
        int pe=BitConverter.ToInt32(executable,60);
        if(pe<64||pe>executable.Length-256||BitConverter.ToUInt32(executable,pe)!=0x4550||BitConverter.ToUInt16(executable,pe+4)!=0x14c||BitConverter.ToUInt16(executable,pe+24)!=0x10b)throw new InvalidDataException("지원하지 않는 게임 실행 파일 형식");
        int sections=BitConverter.ToUInt16(executable,pe+6),sectionStart=pe+24+BitConverter.ToUInt16(executable,pe+20);
        if(sections<1||sections>96||sectionStart+40L*sections>executable.Length)throw new InvalidDataException("게임 데이터 영역 확인 실패");
        var edits=new List<Edit>();
        foreach(string id in EarlyIds) {
            var node=table.SelectSingleNode("/translationtable/translationtableentry[@id='"+id+"']");
            if(node==null)throw new InvalidDataException("자원 설명 번역이 없습니다: "+id);
            string source=node.Attributes["original_text"].Value,translated=node.Attributes["translated_text"].Value;
            byte[] before=Encoding.GetEncoding(1252).GetBytes("[["+id+"]]"+source+"[[]]");
            var hits=new List<int>();
            for(int at=0;at<=executable.Length-before.Length;at++)if(executable[at]==before[0]&&executable[at+1]==before[1]) {
                bool match=true;for(int j=2;j<before.Length;j++)if(executable[at+j]!=before[j]){match=false;break;}if(match)hits.Add(at);
            }
            if(hits.Count!=1)throw new InvalidDataException("게임 원문 설명이 번역 테이블과 다릅니다: "+id);
            int offset=hits[0];bool mapped=false;
            for(int i=0;i<sections;i++) {
                int section=sectionStart+i*40;uint rva=BitConverter.ToUInt32(executable,section+12),size=BitConverter.ToUInt32(executable,section+16),raw=BitConverter.ToUInt32(executable,section+20),flags=BitConverter.ToUInt32(executable,section+36);
                if(offset<(long)raw||offset+before.Length>(long)raw+size)continue;
                if((flags&0x40000000)==0||(flags&0xA0000000)!=0)throw new InvalidDataException("설명 문구가 읽기 전용 데이터 영역에 없습니다.");
                edits.Add(new Edit {Rva=checked(rva+(uint)(offset-raw)),Before=before,After=Literal(id,source,translated)});mapped=true;break;
            }
            if(!mapped)throw new InvalidDataException("게임 설명 주소 확인 실패");
        }
        return edits;
    }
    public static Process Start(string root,Action<string> log) {
        string path=Path.Combine(root,"RK.exe"),state=Engine.State(root),tablePath=Engine.Child(root,Engine.NativeTable);
        if(!File.Exists(Path.Combine(state,"installed"))||!File.Exists(tablePath))return Process.Start(new ProcessStartInfo(path){WorkingDirectory=root,UseShellExecute=false});
        var backup=Engine.Json.Deserialize<Backup>(File.ReadAllText(Path.Combine(state,"backup.json"),Encoding.UTF8));
        if(backup==null||backup.Files==null||!String.Equals(backup.Game,Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("게임 번역 복구 기록의 경로가 다릅니다.");
        var record=backup.Files.SingleOrDefault(f=>f.Created&&String.Equals(f.Path.Replace('\\','/'),Engine.NativeTable,StringComparison.OrdinalIgnoreCase));
        if(record==null||Engine.Hash(tablePath)!=record.PatchedSha256)throw new InvalidDataException("자원 설명 번역 파일이 외부에서 변경되었습니다.");
        var table=new XmlDocument {XmlResolver=null};using(var reader=XmlReader.Create(tablePath,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))table.Load(reader);
        var edits=Edits(File.ReadAllBytes(path),table);
        var startup=new Startup {Size=Marshal.SizeOf(typeof(Startup))};ProcessInfo info;
        if(!CreateProcess(path,new StringBuilder("\""+path+"\""),IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,root,ref startup,out info))throw new Win32Exception(Marshal.GetLastWin32Error(),"게임 시작 실패");
        bool resumed=false;Process result=null;
        try {
            long image=ImageBase(info.Process);
            foreach(var edit in edits) {
                long address=image+edit.Rva;
                if(!Read(info.Process,address,edit.Before.Length).SequenceEqual(edit.Before))throw new InvalidDataException("시작된 게임의 설명 원문이 다릅니다.");
                uint old;if(!VirtualProtectEx(info.Process,new IntPtr(address),(UIntPtr)edit.After.Length,4,out old))throw new Win32Exception(Marshal.GetLastWin32Error(),"게임 설명 준비 실패");
                try {UIntPtr written;if(!WriteProcessMemory(info.Process,new IntPtr(address),edit.After,(UIntPtr)edit.After.Length,out written)||written.ToUInt64()!=(ulong)edit.After.Length)throw new Win32Exception(Marshal.GetLastWin32Error(),"게임 설명 적용 실패");}
                finally {uint ignored;if(!VirtualProtectEx(info.Process,new IntPtr(address),(UIntPtr)edit.After.Length,old,out ignored))throw new Win32Exception(Marshal.GetLastWin32Error(),"게임 데이터 보호 복원 실패");}
                if(!Read(info.Process,address,edit.After.Length).SequenceEqual(edit.After))throw new InvalidDataException("게임 설명 적용 검증 실패");
            }
            result=Process.GetProcessById((int)info.Id);
            if(ResumeThread(info.Thread)==UInt32.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error(),"게임 실행 재개 실패");
            resumed=true;log("자원 설명 4개 한글 적용 · 실행 파일 원본 유지");return result;
        }finally {
            if(!resumed){TerminateProcess(info.Process,1);if(result!=null)result.Dispose();}
            CloseHandle(info.Thread);CloseHandle(info.Process);
        }
    }
    public static void SelfTest() {
        byte[] literal=Literal("1","A long English description.","한글 설명");
        if(literal.Length!=Encoding.ASCII.GetByteCount("[[1]]A long English description.[[]]")||!Encoding.UTF8.GetString(literal).EndsWith("[[]]"))throw new Exception("게임 설명 길이 및 표식 보존 실패");
        bool rejected=false;try{Literal("1","A","매우 긴 설명");}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("초과 설명 길이를 허용했습니다.");
    }
}
}
