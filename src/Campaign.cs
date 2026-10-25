using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RisingKingdomsKO {
public class CampaignFile {public string Path;public int Node;public byte[] Bytes;}
public static class Campaign {
    static int Int(byte[] b,int at){return BitConverter.ToInt32(b,at);}
    static readonly Regex Marker=new Regex(@"\[\[([^\[\]]+)\]\]([\s\S]*?)\[\[\]\]",RegexOptions.Compiled);
    public static List<CampaignFile> Read(string path) {
        byte[] bytes=File.ReadAllBytes(path);
        if(bytes.Length<512||Encoding.ASCII.GetString(bytes,0,4)!="HPFS"||Int(bytes,4)!=512||Int(bytes,8)!=(bytes.Length+511)/512)throw new InvalidDataException("지원하지 않는 BFHP 헤더: "+path);
        var result=new List<CampaignFile>();var visited=new HashSet<int>();
        Walk(bytes,1,"",visited,result);return result;
    }
    static void Walk(byte[] bytes,int node,string prefix,HashSet<int> visited,List<CampaignFile> result) {
        if(!visited.Add(node))throw new InvalidDataException("BFHP 디렉터리 순환");
        using(var r=new BinaryReader(new MemoryStream(ReadNode(bytes,node))))while(r.BaseStream.Position<r.BaseStream.Length) {
            int child=r.ReadInt32();int flags=r.ReadUInt16(),length=r.ReadUInt16();byte[] nameBytes=r.ReadBytes(length);
            if(length==0||nameBytes.Length!=length||flags>1)throw new InvalidDataException("잘못된 BFHP 디렉터리");
            string name=Encoding.GetEncoding(1252).GetString(nameBytes);
            if(name.Contains("/")||name.Contains("\\")||name==".."||name==".")throw new InvalidDataException("잘못된 BFHP 이름");
            string path=prefix+name;
            if(flags==1)Walk(bytes,child,path+"/",visited,result);else result.Add(new CampaignFile {Path=path,Node=child,Bytes=ReadNode(bytes,child)});
        }
    }
    static byte[] ReadNode(byte[] bytes,int node) {
        if(node<=0||(node+1L)*512>bytes.Length)throw new InvalidDataException("BFHP 노드 범위 오류");
        int size=Int(bytes,node*512),level=Int(bytes,node*512+4);
        if(size<0||size>64*1024*1024||level<0||level>3)throw new InvalidDataException("잘못된 BFHP 파일 크기");
        var blocks=new List<int>();Pointers(bytes,node*512+8,level,(size+511)/512,126,blocks);
        var result=new byte[size];int position=0;
        foreach(int block in blocks){int count=Math.Min(512,size-position);if(block*512L+count>bytes.Length)throw new InvalidDataException("BFHP 파일 잘림");Buffer.BlockCopy(bytes,block*512,result,position,count);position+=count;}
        return result;
    }
    static void Pointers(byte[] bytes,int at,int level,int count,int capacity,List<int> result) {
        int coverage=1;for(int i=0;i<level;i++)coverage=checked(coverage*128);
        int number=(count+coverage-1)/coverage;if(number>capacity||at+(long)number*4>bytes.Length)throw new InvalidDataException("BFHP 포인터 개수 오류");
        for(int i=0;i<number;i++) {
            int block=Int(bytes,at+i*4);if(block<=0||block*512L>=bytes.Length)throw new InvalidDataException("BFHP 블록 범위 오류");
            int take=Math.Min(count,coverage);if(level==0)result.Add(block);else Pointers(bytes,block*512,level-1,take,128,result);count-=take;
        }
    }
    static string Decode(byte[] bytes) {try{return new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){return Encoding.GetEncoding(1252).GetString(bytes);}}
    public static List<object> Inspect(string game,Action<string> log) {
        var catalog=new List<object>();
        foreach(string name in new[]{"Darkling Campaign.bfhp","Forester Campaign.bfhp","Human Campaign.bfhp","Tutorial.BFHP"}) {
            string relative="adventures/"+name;int count=0;
            foreach(var file in Read(Engine.Child(game,relative))) {
                if(!Regex.IsMatch(file.Path,@"\.(xml|vs|ini|txt)$",RegexOptions.IgnoreCase))continue;
                string raw=Encoding.GetEncoding(1252).GetString(file.Bytes);
                foreach(Match m in Marker.Matches(raw)) {
                    string id=m.Groups[1].Value,text=Decode(Encoding.GetEncoding(1252).GetBytes(m.Groups[2].Value));
                    catalog.Add(new {Archive=relative,Path=file.Path,Id=id,Key=relative+"|"+file.Path+"|"+id,Source=text,Korean=""});count++;
                }
            }
            log(name+": 텍스트 표식 "+count+"개");
        }
        return catalog;
    }
    static string EscapeXml(string text) {
        // Keep existing entities; encode newly introduced XML metacharacters.
        return Regex.Replace(text,@"&(?![A-Za-z]+;|#\d+;|#x[0-9A-Fa-f]+;)","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;").Replace("'","&apos;");
    }
    public static int Build(string game,string relative,string target,TranslationSet set,HashSet<string> used,Action<string> log) {
        string source=Engine.Child(game,relative);var files=Read(source);var changes=new Dictionary<int,byte[]>();int count=0;
        foreach(var file in files) {
            if(!Regex.IsMatch(file.Path,@"\.(xml|vs|ini|txt)$",RegexOptions.IgnoreCase))continue;
            string raw=Encoding.GetEncoding(1252).GetString(file.Bytes);
            var matches=Marker.Matches(raw);if(matches.Count==0)continue;
            using(var output=new MemoryStream()) {
                int position=0;
                foreach(Match match in matches) {
                    string id=match.Groups[1].Value,key=relative.Replace('\\','/')+"|"+file.Path+"|"+id;
                    string original=Decode(Encoding.GetEncoding(1252).GetBytes(match.Groups[2].Value));Translation item;
                    if(!set.Entries.TryGetValue(key,out item)||item.SourceSha256!=TextPatch.HashText(original)||item.Korean==null)throw new InvalidDataException("캠페인 번역이 없거나 원본이 다릅니다: "+key);
                    if(original.Length>0&&item.Korean.Length==0)throw new InvalidDataException("빈 캠페인 번역: "+key);
                    TextPatch.CheckTokens(original,item.Korean);
                    output.Write(file.Bytes,position,match.Index-position);
                    string text=file.Path.EndsWith(".xml",StringComparison.OrdinalIgnoreCase)?EscapeXml(item.Korean):item.Korean;
                    byte[] replacement=Encoding.UTF8.GetBytes("[["+id+"]]"+text+"[[]]");output.Write(replacement,0,replacement.Length);
                    position=match.Index+match.Length;used.Add(key);count++;
                }
                output.Write(file.Bytes,position,file.Bytes.Length-position);changes.Add(file.Node,output.ToArray());
            }
        }
        if(changes.Count==0)throw new InvalidDataException("캠페인 텍스트를 찾지 못했습니다.");
        Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target,false);
        using(var stream=new FileStream(target,FileMode.Open,FileAccess.ReadWrite))using(var writer=new BinaryWriter(stream)) {
            stream.SetLength(((stream.Length+511)/512)*512);stream.Position=stream.Length;
            foreach(var change in changes) {
                var blocks=new List<int>();byte[] contents=change.Value;
                for(int position=0;position<contents.Length;position+=512)blocks.Add(Append(writer,contents.Skip(position).Take(512).ToArray()));
                int level=0;
                while(blocks.Count>126) {
                    var parents=new List<int>();
                    for(int i=0;i<blocks.Count;i+=128)using(var memory=new MemoryStream())using(var pointers=new BinaryWriter(memory)){foreach(int block in blocks.Skip(i).Take(128))pointers.Write(block);parents.Add(Append(writer,memory.ToArray()));}
                    blocks=parents;level++;
                }
                byte[] metadata=new byte[512];Array.Copy(BitConverter.GetBytes(contents.Length),0,metadata,0,4);Array.Copy(BitConverter.GetBytes(level),0,metadata,4,4);
                for(int i=0;i<blocks.Count;i++)Array.Copy(BitConverter.GetBytes(blocks[i]),0,metadata,8+i*4,4);
                long end=stream.Length;stream.Position=change.Key*512L;writer.Write(metadata);stream.Position=end;
            }
            int total=checked((int)(stream.Length/512));stream.Position=8;writer.Write(total);
        }
        var rebuilt=Read(target);if(rebuilt.Count!=files.Count)throw new InvalidDataException("캠페인 파일 개수 검증 실패");
        var byPath=rebuilt.ToDictionary(f=>f.Path,StringComparer.Ordinal);
        foreach(var file in files){byte[] expected;if(!changes.TryGetValue(file.Node,out expected))expected=file.Bytes;CampaignFile actual;if(!byPath.TryGetValue(file.Path,out actual)||!expected.SequenceEqual(actual.Bytes))throw new InvalidDataException("캠페인 데이터 보존 검증 실패: "+file.Path);}
        log(Path.GetFileName(relative)+": 텍스트 표식 "+count+"개 / 비텍스트 데이터 보존 검증 완료");return count;
    }
    static int Append(BinaryWriter writer,byte[] bytes) {if(bytes.Length>512)throw new InvalidDataException("잘못된 블록 크기");int block=checked((int)(writer.BaseStream.Length/512));writer.BaseStream.Position=writer.BaseStream.Length;writer.Write(bytes);writer.Write(new byte[512-bytes.Length]);return block;}
}
}
