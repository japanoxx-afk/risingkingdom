using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RisingKingdomsKO {
public static class RuntimeContracts {
    static readonly Regex Section=new Regex(@"(?m)^[ \t]*\[([^\]\r\n]+)\][ \t]*\r*$");
    public static bool IsIdentifier(string path,string source,int index) {
        if(!String.Equals(path.Replace('/','\\'),"DATA\\VXCONST.INI",StringComparison.OrdinalIgnoreCase))return false;
        string before=source.Substring(0,index);var sections=Section.Matches(before);
        if(sections.Count==0||!String.Equals(sections[sections.Count-1].Groups[1].Value,"Races",StringComparison.OrdinalIgnoreCase))return false;
        int line=before.LastIndexOf('\n')+1;
        return Regex.IsMatch(before.Substring(line),@"^[ \t]*Race\d+[ \t]*=[ \t]*$",RegexOptions.IgnoreCase);
    }
    static string GetSection(string text,string section) {
        var sections=Section.Matches(text);
        for(int i=0;i<sections.Count;i++)if(String.Equals(sections[i].Groups[1].Value,section,StringComparison.OrdinalIgnoreCase)) {
            int start=sections[i].Index,end=i+1<sections.Count?sections[i+1].Index:text.Length;return text.Substring(start,end-start);
        }
        throw new InvalidDataException("게임 필수 섹션이 없습니다: "+section);
    }
    public static void ValidateConstants(string original,string patched) {
        string races=GetSection(original,"Races");
        if(races!=GetSection(patched,"Races"))throw new InvalidDataException("종족 내부 식별자가 변경됐습니다. Race_* 스크립트 및 Frame UI 참조가 깨질 수 있어 적용을 중단합니다.");
        foreach(Match m in Regex.Matches(races,@"(?m)^[ \t]*Race(\d+)[ \t]*=[ \t]*\[\[[^\]]+\]\]([^\r\n]*?)\[\[\]\]")) {
            string typePattern=@"(?m)^[ \t]*Type"+m.Groups[1].Value+@"[ \t]*=[ \t]*1[ \t]*\r*$";
            if(!Regex.IsMatch(races,typePattern))continue;
            string frame=GetSection(patched,"Frame"+m.Groups[2].Value);
            if(!Regex.IsMatch(frame,@"(?m)^[ \t]*FrameTestY[ \t]*=[ \t]*\d+"))throw new InvalidDataException("종족 UI FrameTestY 참조가 유효하지 않습니다.");
        }
    }
    static string ReadConstants(string path) {
        var entry=Pak.Read(path).Single(e=>String.Equals(e.Path,"DATA\\VXCONST.INI",StringComparison.OrdinalIgnoreCase));
        using(var r=new BinaryReader(File.OpenRead(path))){r.BaseStream.Position=entry.Offset;return Encoding.GetEncoding(1252).GetString(r.ReadBytes(checked((int)entry.Size)));}
    }
    public static void ValidatePak(string original,string patched){ValidateConstants(ReadConstants(original),ReadConstants(patched));}
    public static void SelfTest() {
        string source="[Races]\r\nRace0=[[1]]Darklings[[]]\r\nType0=1\r\n[FrameDarklings]\r\nFrameTestY=435\r\n[Other]\r\nText=[[1]]Darklings[[]]\r\n";
        int first=source.IndexOf("[[1]]"),last=source.LastIndexOf("[[1]]");
        if(!IsIdentifier("DATA\\VXCONST.INI",source,first)||IsIdentifier("DATA\\VXCONST.INI",source,last)||IsIdentifier("DATA\\INTERFACE\\MENU.INI",source,first))throw new Exception("내부 식별자 분류 실패");
        ValidateConstants(source,source);
        bool rejected=false;try{ValidateConstants(source,source.Replace("Race0=[[1]]Darklings","Race0=[[1]]다클링"));}catch(InvalidDataException){rejected=true;}
        if(!rejected)throw new Exception("번역된 내부 종족 ID를 허용했습니다.");
    }
}
}
