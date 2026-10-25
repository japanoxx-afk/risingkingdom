using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RisingKingdomsKO {
public static class GameMonitor {
    public static string Failure(string game,DateTime startedUtc,int exitCode) {
        string logs=Path.Combine(game,"Logs");
        if(Directory.Exists(logs)) {
            var latest=Directory.GetFiles(logs,"vx.log",SearchOption.AllDirectories).Select(p=>new FileInfo(p)).Where(f=>f.LastWriteTimeUtc>=startedUtc).OrderByDescending(f=>f.LastWriteTimeUtc).FirstOrDefault();
            if(latest!=null) {
                string text=File.ReadAllText(latest.FullName);
                if(text.Contains("Exception Information")&&Regex.IsMatch(text,@"(?m)^Error:[ \t]+\S")) {
                    var error=Regex.Match(text,@"(?m)^Error:[ \t]+([^\r\n]+)");
                    return "게임의 비정상 종료가 감지됐습니다.\n"+error.Groups[1].Value+"\n\n진단 로그: "+latest.FullName;
                }
            }
        }
        return exitCode!=0?"게임이 비정상 종료됐습니다. 종료 코드: "+exitCode+"\n게임 폴더의 Logs를 확인하세요.":null;
    }
    public static void SelfTest() {
        string root=Path.Combine(Path.GetTempPath(),"rkko-monitor-"+Guid.NewGuid().ToString("N"));string directory=Path.Combine(root,"Logs","test");Directory.CreateDirectory(directory);
        string log=Path.Combine(directory,"vx.log");File.WriteAllText(log,"-- Exception Information\r\nError: Access violation\r\n");DateTime started=DateTime.UtcNow;
        File.SetLastWriteTimeUtc(log,started.AddMinutes(-1));if(Failure(root,started,0)!=null)throw new Exception("오래된 크래시 기록을 새 오류로 처리했습니다.");
        File.SetLastWriteTimeUtc(log,started.AddSeconds(1));if(Failure(root,started,0)==null)throw new Exception("종료 코드 0인 크래시를 감지하지 못했습니다.");
        if(Failure(Path.Combine(root,"missing"),started,1)==null)throw new Exception("비정상 종료 코드를 감지하지 못했습니다.");
    }
}
}
