using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace RisingKingdomsKO {
public class Translation {public string SourceSha256;public string Korean;}
public class TranslationSet {public int Schema;public string Method;public Dictionary<string,Translation> Entries;}
public static class TextPatch {
    static readonly Regex Marker=new Regex(@"\[\[(\d+)\]\]([\s\S]*?)\[\[\]\]",RegexOptions.Compiled);
    static readonly Regex Protected=new Regex(@"#[^#\r\n]+#|%[sdfiuoxX]\d*|%[A-Za-z_][A-Za-z_0-9]*%|</?[^>\r\n]+>|&(?:[A-Za-z]+|#\d+);|\\[nrt]|@[A-Za-z]");
    public static string HashText(string text) {using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();}
    public static void CheckTokens(string source,string translated) {
        var a=Protected.Matches(source).Cast<Match>().Select(m=>m.Value).OrderBy(x=>x).ToArray();
        var b=Protected.Matches(translated).Cast<Match>().Select(m=>m.Value).OrderBy(x=>x).ToArray();
        if(!a.SequenceEqual(b))throw new InvalidDataException("번역의 게임 변수 또는 서식 코드가 원문과 다릅니다.");
        if(translated.Contains("[[")||translated.Contains("]]"))throw new InvalidDataException("번역문에 잘못된 번역 구분자가 있습니다.");
    }
    public static void Build(string game,string dictionary,string package,Action<string> log) {
        Engine.ValidateGame(game);
        if(Directory.Exists(Engine.State(game)))throw new IOException("패치를 생성하기 전에 원본 복원을 실행하세요.");
        var set=Engine.Json.Deserialize<TranslationSet>(File.ReadAllText(dictionary,Encoding.UTF8));
        if(set==null||set.Schema!=1||set.Entries==null)throw new InvalidDataException("지원하지 않는 번역 사전입니다.");
        var entries=Pak.Read(Engine.Child(game,"Packs\\data.pak"));
        var changed=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);var used=new HashSet<string>();
        int occurrences=0;
        using(var reader=new BinaryReader(File.OpenRead(Engine.Child(game,"Packs\\data.pak"))))foreach(var entry in entries) {
            if(!Regex.IsMatch(entry.Path,@"\.(ini|xml|txt|vs)$",RegexOptions.IgnoreCase))continue;
            reader.BaseStream.Position=entry.Offset;byte[] bytes=reader.ReadBytes(checked((int)entry.Size));
            string source=Encoding.GetEncoding(1252).GetString(bytes);
            if(!Marker.IsMatch(source))continue;
            Marker.Replace(source,match=>{
                string id=match.Groups[1].Value,text=match.Groups[2].Value;Translation item;
                if(!set.Entries.TryGetValue(id,out item)||item.SourceSha256!=HashText(text)||item.Korean==null)throw new InvalidDataException("번역이 없거나 원본 버전이 다릅니다. ID: "+id);
                if(text.Length>0 && item.Korean.Length==0)throw new InvalidDataException("비어 있는 번역: "+id);
                CheckTokens(text,item.Korean);used.Add(id);occurrences++;
                return "[["+id+"]]"+item.Korean+"[[]]";
            });
            // Retain every non-translated source byte verbatim, even in non-UTF8 legacy files.
            using(var output=new MemoryStream()) {
                int position=0;
                foreach(Match m in Marker.Matches(source)) {
                    output.Write(bytes,position,m.Index-position);
                    byte[] replacement=Encoding.UTF8.GetBytes("[["+m.Groups[1].Value+"]]"+set.Entries[m.Groups[1].Value].Korean+"[[]]");
                    output.Write(replacement,0,replacement.Length);position=m.Index+m.Length;
                }
                output.Write(bytes,position,bytes.Length-position);changed.Add(entry.Path,output.ToArray());
            }
        }
        if(used.Count==0)throw new InvalidDataException("번역 대상이 없습니다.");
        if(Directory.Exists(package))throw new IOException("생성 대상 폴더가 이미 있습니다. 새 패키지 폴더를 선택하세요.");
        string requested=Path.GetFullPath(package);
        package=requested+".building-"+Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(package);
        var manifest=new Manifest {Schema=1,Version="0.3.0-text-draft",Scope="game-text",TranslationMethod=set.Method,Complete=false,KoreanFontVerified=false,FontFormatVerified=true,RuntimeVerified=false,Files=new List<PatchFile>()};
        log("텍스트 변환: "+used.Count+"개 ID / "+occurrences+"개 표식");
        string destination=Engine.Child(package,"files\\Packs\\data.pak");Directory.CreateDirectory(Path.GetDirectoryName(destination));
        RewritePak(Engine.Child(game,"Packs\\data.pak"),destination,entries,changed);
        AddFile(manifest,game,package,"Packs\\data.pak");
        foreach(string campaign in new[]{"Darkling Campaign.bfhp","Forester Campaign.bfhp","Human Campaign.bfhp","Tutorial.BFHP"}) {
            string relative="adventures/"+campaign;
            Campaign.Build(game,relative,Engine.Child(package,"files/"+relative),set,used,log);AddFile(manifest,game,package,relative);
        }
        manifest.TotalStrings=used.Count;manifest.TranslatedStrings=used.Count;
        var characters=set.Entries.Values.SelectMany(v=>v.Korean).Where(c=>c>=0xAC00&&c<=0xD7A3).Distinct().OrderBy(c=>c).ToArray();
        if(characters.Length==0)throw new InvalidDataException("번역 사전에 한글 문자가 없습니다.");
        foreach(string font in Directory.GetFiles(Engine.Child(game,"Local\\Fonts"),"*.apf")) {
            string rel="Local\\Fonts\\"+Path.GetFileName(font), target=Engine.Child(package,"files\\"+rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            KoreanFont.Build(font,target,characters);AddFile(manifest,game,package,rel);log("한글 글꼴 생성: "+Path.GetFileName(font)+" / "+characters.Length+"자");
        }
        File.WriteAllText(Path.Combine(package,"manifest.json"),Engine.Json.Serialize(manifest),Encoding.UTF8);
        Engine.LoadPackage(package);Directory.Move(package,requested);log("텍스트 패키지 생성 완료 (기계번역 초안 / 실제 게임 화면 미검수)");
    }
    static void AddFile(Manifest m,string game,string package,string relative) {m.Files.Add(new PatchFile {Path=relative,OriginalSha256=Engine.Hash(Engine.Child(game,relative)),PatchedSha256=Engine.Hash(Engine.Child(package,"files\\"+relative))});}
    public static void RewritePak(string source,string target,List<Entry> entries,Dictionary<string,byte[]> replacements) {
        byte[] directory;
        using(var memory=new MemoryStream())using(var w=new BinaryWriter(memory)) {
            string previous="";
            foreach(var e in entries) {
                int prefix=0;while(prefix<previous.Length&&prefix<e.Path.Length&&previous[prefix]==e.Path[prefix])prefix++;
                if(e.Path.Length>255)throw new InvalidDataException("PAK 경로가 너무 깁니다.");
                w.Write((byte)e.Path.Length);w.Write((byte)prefix);w.Write(Encoding.ASCII.GetBytes(e.Path.Substring(prefix)));w.Write((uint)0);w.Write((uint)0);previous=e.Path;
            }
            directory=memory.ToArray();
        }
        using(var reader=new BinaryReader(File.OpenRead(source)))using(var writer=new BinaryWriter(File.Create(target))) {
            byte[] header=reader.ReadBytes(40);Array.Copy(BitConverter.GetBytes(directory.Length),0,header,36,4);writer.Write(header);writer.Write(directory);
            int record=40;string previous="";
            foreach(var e in entries) {
                byte[] replacement;long offset=writer.BaseStream.Position;uint length;
                if(replacements.TryGetValue(e.Path,out replacement)){writer.Write(replacement);length=(uint)replacement.Length;}
                else {reader.BaseStream.Position=e.Offset;byte[] original=reader.ReadBytes(checked((int)e.Size));writer.Write(original);length=e.Size;}
                if(offset>UInt32.MaxValue)throw new InvalidDataException("PAK 파일이 너무 큽니다.");
                int prefix=0;while(prefix<previous.Length&&prefix<e.Path.Length&&previous[prefix]==e.Path[prefix])prefix++;
                record+=2+e.Path.Length-prefix;
                long end=writer.BaseStream.Position;writer.BaseStream.Position=record;writer.Write((uint)offset);writer.Write(length);writer.BaseStream.Position=end;record+=8;previous=e.Path;
            }
        }
        var check=Pak.Read(target);if(check.Count!=entries.Count)throw new InvalidDataException("PAK 재구성 검증 실패");
        using(var reader=new BinaryReader(File.OpenRead(target)))foreach(var e in check) {
            byte[] expected;if(!replacements.TryGetValue(e.Path,out expected))continue;
            reader.BaseStream.Position=e.Offset;if(!reader.ReadBytes(checked((int)e.Size)).SequenceEqual(expected))throw new InvalidDataException("PAK 텍스트 검증 실패");
        }
    }
}
public static class KoreanFont {
    static int Int(byte[] bytes,int offset){return BitConverter.ToInt32(bytes,offset);}
    class Range {public int Start,Count;public byte[] Bytes;}
    public static void Build(string source,string target,char[] characters) {
        byte[] original=File.ReadAllBytes(source);
        if(Encoding.ASCII.GetString(original,0,4)!="ABCF" || Int(original,24)!=1)throw new InvalidDataException("지원하지 않는 APF 글꼴");
        int formatOffset=Int(original,28), infoOffset=Int(original,formatOffset+12), oldCount=Int(original,infoOffset+56);
        if(formatOffset<32 || infoOffset<formatOffset+20 || oldCount<1 || oldCount>10000 || infoOffset+60L+oldCount*16>original.Length)throw new InvalidDataException("잘못된 APF 헤더");
        var ranges=new List<Range>();
        for(int i=0;i<oldCount;i++) {
            int at=infoOffset+60+i*16, offset=Int(original,at), size=Int(original,at+4), start=Int(original,at+8), count=Int(original,at+12);
            if(offset<0||size<0||offset+(long)size>original.Length)throw new InvalidDataException("잘못된 APF 문자 범위");
            if(characters.Any(c=>c>=start&&c<start+count))throw new InvalidDataException("원본 APF에 이미 한글이 포함되어 있습니다.");
            var block=new byte[size];Array.Copy(original,offset,block,0,size);ranges.Add(new Range {Start=start,Count=count,Bytes=block});
        }
        int pixelSize=Int(original,formatOffset), lineHeight=Int(original,infoOffset);bool bold=Int(original,formatOffset+8)!=0;
        if(pixelSize<6||pixelSize>64||lineHeight<6||lineHeight>100)throw new InvalidDataException("지원하지 않는 APF 크기");
        using(var font=new Font("맑은 고딕",pixelSize,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()) {
            if(font.Name!="맑은 고딕"&&font.Name!="Malgun Gothic")throw new InvalidDataException("맑은 고딕 글꼴을 찾을 수 없습니다.");
            format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces|StringFormatFlags.NoClip;
            int index=0;
            while(index<characters.Length) {
                int start=characters[index], end=index+1;while(end<characters.Length&&characters[end]==characters[end-1]+1)end++;
                ranges.Add(new Range {Start=start,Count=end-index,Bytes=GlyphBlock(characters.Skip(index).Take(end-index).ToArray(),font,format,lineHeight)});index=end;
            }
        }
        ranges=ranges.OrderBy(r=>r.Start).ToList();
        int prefixSize=infoOffset+60, infoSize=60+ranges.Count*16, position=infoOffset+infoSize;
        byte[] prefix=new byte[prefixSize];Array.Copy(original,prefix,prefixSize);
        Array.Copy(BitConverter.GetBytes(infoSize),0,prefix,formatOffset+16,4);Array.Copy(BitConverter.GetBytes(ranges.Count),0,prefix,infoOffset+56,4);
        using(var writer=new BinaryWriter(File.Create(target))) {
            writer.Write(prefix);
            foreach(var range in ranges){writer.Write(position);writer.Write(range.Bytes.Length);writer.Write(range.Start);writer.Write(range.Count);position+=range.Bytes.Length;}
            foreach(var range in ranges)writer.Write(range.Bytes);
        }
        Validate(target,characters);
    }
    static byte[] GlyphBlock(char[] characters,Font font,StringFormat format,int height) {
        var glyphs=new List<int[]>();var pixels=new List<byte>();int canvas=height*3;
        using(var bitmap=new Bitmap(canvas,height,PixelFormat.Format32bppArgb))using(var graphics=Graphics.FromImage(bitmap)) {
            graphics.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            foreach(char ch in characters) {
                graphics.Clear(Color.Black);graphics.DrawString(ch.ToString(),font,Brushes.White,new PointF(0,-font.Size*.18f),format);
                int stride;byte[] bytes;
                var bits=bitmap.LockBits(new Rectangle(0,0,canvas,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                try{stride=bits.Stride;bytes=new byte[stride*height];Marshal.Copy(bits.Scan0,bytes,0,bytes.Length);}finally{bitmap.UnlockBits(bits);}
                int left=canvas,top=height,right=-1,bottom=-1;
                for(int y=0;y<height;y++)for(int x=0;x<canvas;x++)if(bytes[y*stride+x*4]>12){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
                if(right<0)throw new InvalidDataException("한글 글리프가 비어 있습니다: "+ch);
                int advance=(int)Math.Ceiling(graphics.MeasureString(ch.ToString(),font,canvas,format).Width);
                int offset=pixels.Count,previous=-1,run=0;
                Action flush=()=>{if(run>0)pixels.Add((byte)((previous<<5)|(run-1)));};
                for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++) {
                    int value=(bytes[y*stride+x*4]*7+127)/255;
                    if(value==previous&&run<32)run++;else{flush();previous=value;run=1;}
                }
                flush();glyphs.Add(new[]{0,advance,0,left,top,right,bottom,offset});
            }
        }
        using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)) {
            int data=16+glyphs.Count*32;writer.Write(data);writer.Write(0);writer.Write(data);writer.Write(pixels.Count);
            foreach(var glyph in glyphs)foreach(int n in glyph)writer.Write(n);writer.Write(pixels.ToArray());return memory.ToArray();
        }
    }
    public static void Validate(string path,char[] required) {
        byte[] bytes=File.ReadAllBytes(path);int formatOffset=Int(bytes,28),info=Int(bytes,formatOffset+12),ranges=Int(bytes,info+56);var found=new HashSet<char>();
        for(int i=0;i<ranges;i++) {
            int at=info+60+i*16,offset=Int(bytes,at),size=Int(bytes,at+4),start=Int(bytes,at+8),count=Int(bytes,at+12);
            if(offset<0||size<16||offset+(long)size>bytes.Length||16L+count*32>size)throw new InvalidDataException("APF 검증 실패");
            int data=Int(bytes,offset+8),length=Int(bytes,offset+12);
            if(data<16+count*32||data+(long)length>size)throw new InvalidDataException("APF 비트맵 범위 오류");
            foreach(char ch in required.Where(c=>c>=start&&c<start+count)) {
                int g=offset+16+(ch-start)*32,width=Int(bytes,g+20)-Int(bytes,g+12)+1,height=Int(bytes,g+24)-Int(bytes,g+16)+1;
                int begin=Int(bytes,g+28),end=ch-start+1<count?Int(bytes,g+60):length;
                if(begin<0||end<begin||end>length)throw new InvalidDataException("APF 글리프 범위 오류");
                int pixels=0;for(int p=begin;p<end;p++)pixels+=(bytes[offset+data+p]&31)+1;
                if(pixels!=width*height)throw new InvalidDataException("APF RLE 검증 실패");found.Add(ch);
            }
        }
        if(found.Count!=required.Distinct().Count())throw new InvalidDataException("한글 글꼴 문자 누락");
    }
}
}
