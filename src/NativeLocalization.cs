using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace RisingKingdomsKO {
public static class NativeLocalization {
    static readonly Regex Marker=new Regex(@"\[\[(\d+)\]\]((?:(?!\[\[\]\])[^\0])*)\[\[\]\]",RegexOptions.Compiled);
    public static byte[] Build(byte[] executable,TranslationSet set) {
        if(set.EngineEntries==null||set.EngineEntries.Length==0)throw new InvalidDataException("실행 파일 표시 문구 목록이 없습니다.");
        var selected=new HashSet<string>(set.EngineEntries,StringComparer.Ordinal);
        if(selected.Count!=set.EngineEntries.Length)throw new InvalidDataException("실행 파일 표시 문구 ID 중복");
        var sources=new Dictionary<string,string>();
        foreach(Match match in Marker.Matches(Encoding.GetEncoding(1252).GetString(executable))) {
            string id=match.Groups[1].Value,text=match.Groups[2].Value;
            if(!selected.Contains(id))continue;
            string previous;if(sources.TryGetValue(id,out previous)&&previous!=text)throw new InvalidDataException("실행 파일 표시 문구 ID 충돌: "+id);
            sources[id]=text;
        }
        using(var stream=new MemoryStream()) {
            using(var xml=XmlWriter.Create(stream,new XmlWriterSettings {Encoding=new UTF8Encoding(false),Indent=true,CloseOutput=false,NewLineHandling=NewLineHandling.Entitize})) {
                xml.WriteStartDocument();xml.WriteStartElement("translationtable");
                foreach(string id in set.EngineEntries) {
                    string source;Translation translation;
                    if(!sources.TryGetValue(id,out source)||!set.Entries.TryGetValue(id,out translation)||translation.SourceSha256!=TextPatch.HashText(source)||String.IsNullOrEmpty(translation.Korean))throw new InvalidDataException("실행 파일 표시 문구가 번역 사전과 다릅니다: "+id);
                    TextPatch.CheckTokens(source,translation.Korean);
                    xml.WriteStartElement("translationtableentry");xml.WriteAttributeString("id",id);
                    xml.WriteAttributeString("original_text",source);xml.WriteAttributeString("translated_text",translation.Korean);xml.WriteEndElement();
                }
                xml.WriteEndElement();xml.WriteEndDocument();
            }
            return stream.ToArray();
        }
    }
    public static void SelfTest() {
        var set=new TranslationSet {EngineEntries=new[]{"1"},Entries=new Dictionary<string,Translation>{{"1",new Translation {SourceSha256=TextPatch.HashText("Gold & %d1\n"),Korean="금 & %d1\n"}}}};
        byte[] source=Encoding.ASCII.GetBytes("MZ\0[[1]]Gold & %d1\n[[]]\0");
        byte[] before=(byte[])source.Clone();var document=new XmlDocument {XmlResolver=null};
        using(var stream=new MemoryStream(Build(source,set)))document.Load(stream);
        if(document.DocumentElement.FirstChild.Attributes["translated_text"].Value!="금 & %d1\n"||!source.SequenceEqual(before))throw new Exception("실행 파일 번역 테이블 검증 실패");
        set.Entries["1"].SourceSha256=new string('0',64);
        bool rejected=false;try{Build(source,set);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("다른 실행 파일 문구를 허용했습니다.");
    }
}
}
