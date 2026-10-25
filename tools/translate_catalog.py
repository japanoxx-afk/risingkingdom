"""Create a cached Korean machine-translation draft; game files remain untouched.

Only the public English game strings in the extracted catalog are sent to Google
Translate's public endpoint. Never pass user documents, saves or credentials.
The endpoint is not a supported Cloud API and can stop working; errors are fatal.
"""
import hashlib
import json
import re
import time
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / 'reports' / 'translation-catalog.json'
OUTPUT = ROOT / 'translations' / 'ko.json'
TOKEN = re.compile(r"#[^#\r\n]+#|%[sdfiuoxX]\d*|%[A-Za-z_][A-Za-z_0-9]*%|</?[^>\r\n]+>|&(?:[A-Za-z]+|#\d+);|\\[nrt]")
GLOSSARY = {
    'Rising Kingdoms': '라이징 킹덤즈', 'Darklings': '다클링', 'Darkling': '다클링',
    'Foresters': '포레스터', 'Forester': '포레스터', 'Humans': '인간', 'Human': '인간',
    'Axemaster': '도끼의 달인', 'Soulmonger': '영혼 수집가', 'Soulwatchers': '영혼 감시자',
    'Soulwatcher': '영혼 감시자', 'Night Assassin': '밤의 암살자', 'Seedkeeper': '씨앗 수호자',
    'Clodhopper': '흙발 전사', 'Champion': '영웅', 'Champions': '영웅', 'champion': '영웅',
    'Gold': '금', 'Gems': '보석', 'gold': '금', 'gems': '보석', 'GameSpy': 'GameSpy',
}
MANUAL = {
    'Main menu':'주 메뉴', 'Load game':'게임 불러오기', 'Load Game':'게임 불러오기',
    'Save game':'게임 저장', 'Save Game':'게임 저장', 'Options':'설정', 'Restart':'다시 시작',
    'Quit':'종료', 'Continue':'계속하기', 'Resign':'항복', 'Restart Mission':'임무 다시 시작',
    'Single player':'싱글 플레이', 'Multiplayer':'멀티 플레이', 'Editor':'편집기', 'Credits':'제작진',
    'Tutorial':'튜토리얼', 'Start New Campaign':'새 캠페인', 'Continue Campaign':'캠페인 계속하기',
    'Custom Game':'사용자 지정 게임', 'Start Campaign':'캠페인 시작', 'Quit Campaign':'캠페인 종료',
    'Change Player':'플레이어 변경', 'Change player':'플레이어 변경', 'Online Battle':'온라인 대전',
    'Start New Online Battle':'새 온라인 대전', 'Exit Online Battle':'온라인 대전 종료',
    'Yes':'예', 'No':'아니요', 'Cancel':'취소', 'Close':'닫기', 'Back':'뒤로', 'Start':'시작',
    'Ok':'확인', 'OK':'확인', 'Apply':'적용', 'New':'새로 만들기', 'Save':'저장', 'Load':'불러오기',
    'Delete':'삭제', 'Search':'검색', 'Search:':'검색:', 'Select':'선택', 'Send':'보내기', 'Chat':'채팅',
    'Description':'설명', 'Statistics':'통계', 'View Statistics':'통계 보기', 'Save Replay':'리플레이 저장',
    'Quests':'퀘스트', 'Conversations':'대화', 'No active quests':'진행 중인 퀘스트 없음',
    'You lose':'패배', 'You Win!':'승리!', 'Paused ...':'일시 정지...', 'Please wait':'잠시 기다려 주세요',
    'Gameplay':'게임 설정', 'Graphic Details':'그래픽 세부 설정', 'Music and sound':'음악 및 소리',
    'Scroll speed':'화면 이동 속도', 'Game speed':'게임 속도', 'Editor Resolution':'편집기 해상도',
    'Particle system details':'입자 효과 품질', 'Reverse speakers':'스피커 좌우 바꾸기',
    'Unit speech':'유닛 음성', 'Nature sounds':'자연 환경음', 'Sound effects':'효과음', 'Play music':'음악 재생',
    'Turn off environment animations':'환경 애니메이션 끄기', 'Turn off drawing transparent units':'유닛 투명 효과 끄기',
    'Turn off view fog effect':'시야 안개 효과 끄기', 'Champion healthbars':'영웅 체력 표시',
    'Severely wounded units only':'심하게 다친 유닛만', 'Friendly':'아군', 'Enemy':'적군',
    'Healthbars for Alt key':'Alt 키 체력 표시', 'Healthbars for Ctrl key':'Ctrl 키 체력 표시',
    'All':'전체', 'Name:':'이름:', 'Player name':'플레이어 이름', 'Player':'플레이어', 'Players':'플레이어',
    'Player 1':'플레이어 1', 'Team':'팀', 'Race':'종족', 'Level':'레벨', 'Wins':'승리', 'Losses':'패배',
    'Rating':'평점', 'Favourite Race':'선호 종족', 'Hall of Fame':'명예의 전당', 'Place':'순위',
    'Game type:':'게임 유형:', 'Map:':'지도:', 'Join':'참가', 'Refresh':'새로고침', 'Spectate':'관전',
    'Host in LAN':'LAN 게임 만들기', 'Host in Gamespy':'GameSpy 게임 만들기', 'Host IP:':'호스트 IP:',
    'Game description':'게임 설명', 'Game list':'게임 목록', 'Internet host:':'인터넷 호스트:',
    'Independent Colony Strength':'독립 거점 전력', 'Uploading Maps':'지도 업로드 중',
    'Send to all':'모두에게 보내기', 'Send to allies':'동맹에게 보내기', 'Send to player':'플레이어에게 보내기',
    'Send location':'위치 보내기', 'View location':'위치 보기', 'Send Global':'전체 채팅 보내기',
    'Select race:':'종족 선택:', 'Select game type:':'게임 유형 선택:', 'Select map preference:':'선호 지도 선택:',
    'Play Game':'게임 시작', 'Searching for other players':'다른 플레이어 검색 중',
    'Login':'로그인', 'Register':'가입', 'Profile':'프로필', 'Player Profile':'플레이어 프로필',
    'View Profile':'프로필 보기', 'Rename':'이름 변경', 'Percentage':'백분율', 'Total':'합계', 'Message':'메시지',
    'Do you really want to restart?':'정말 다시 시작하시겠습니까?',
    'Please wait while other players are aware of your disconnection...':'다른 플레이어에게 접속 종료를 알리는 중입니다...',
    'Exit now':'지금 종료', 'Rising Kingdoms Editor':'라이징 킹덤즈 편집기',
    'Move':'이동', 'Attack':'공격', 'Hold Position':'위치 사수', 'Patrol':'순찰',
    'Gather Gold':'금 채집', 'Gather Gems':'보석 채집', 'Repair':'수리', 'Set Rally Point':'집결 지점 설정',
    'Call a Champion':'영웅 소환', 'Resurrect':'부활', 'Learn New Skill':'새 기술 배우기', 'Show Inventory':'소지품 보기',
    'Ferocity':'흉포함', 'Bash':'강타', 'Blood Lust':'피의 갈증', 'Leap Attack':'도약 공격',
    'Death Blow':'죽음의 일격', "Death's Embrace":'죽음의 포옹', 'Invincibility':'무적',
    'Possession':'빙의', 'Mind Blaze':'정신의 불꽃', 'Divine Journey':'신성한 여정', 'Dichotomize':'분열',
    'Reality Ripple':'현실의 파동', 'Fervent Disintegration':'격렬한 붕괴', 'Shadows Of The Night':'밤의 그림자',
    'Confuse':'혼란', 'Shadewalk':'그림자 걸음', 'Pain Link':'고통 연결', 'Angel of Death':'죽음의 천사',
    'Backstab':'기습', 'Cloud of Darkness':'어둠의 구름', 'Soul Burn':'영혼 연소', 'Absorb Soul':'영혼 흡수',
    'Soul Link':'영혼 연결', 'Soul Nova':'영혼 폭발', 'Reincarnate':'환생', 'Replenish':'회복', 'Energy Tide':'에너지의 물결',
    'Heal':'치유', 'Life Steal':'생명력 흡수', 'Charge':'돌격', 'Flanking':'측면 공격', 'Looting':'약탈',
    'Caltrops':'마름쇠', 'Gallop':'질주', 'Windwalking':'바람 걸음', 'Promotion':'승급', 'Levitation':'공중 부양',
    'Land':'착륙', 'Consume Power':'힘 흡수', 'Withering Breeze':'쇠약의 바람', 'Voice of Damnation':'저주의 목소리',
    'Blight':'역병', 'Haunt Building':'건물 빙의', 'Polymorph':'변이', 'Reequip':'장비 교체',
    'Flare':'섬광', 'Hailstorm':'우박 폭풍', 'Frost Arrows':'서리 화살', 'Crystal Tower':'수정 탑',
    'Shelter':'대피소', 'Frost Shield':'서리 방패', 'Invisibility':'투명화', 'Spiritual Fire':'영혼의 불꽃',
    'Exorcism':'퇴마', 'Holy Armour':'신성한 갑옷', 'Leadership':'지도력', 'Stealth':'은신',
    'Weakness':'약화', 'Restoration':'복구', 'Plunder':'약탈', 'Bazaar':'시장', 'Shock':'충격', 'Second Sight':'천리안',
    'Immortality':'불멸', 'Soul Collector':'영혼 수집자', 'Earthshaker':'대지진', 'Troll Cannon':'트롤 대포',
}

def digest(text):
    return hashlib.sha256(text.encode('utf-8')).hexdigest()

def protect(source):
    saved=[]
    def token(value):
        saved.append(value)
        return f'ZXQ{len(saved)-1:04d}QXZ'
    hotkeys=re.findall(r'@([A-Za-z])',source)
    text=TOKEN.sub(lambda m:token(m[0]),source.replace('@','') if hotkeys else source)
    terms=re.compile(r'\b('+'|'.join(map(re.escape,sorted(GLOSSARY,key=len,reverse=True)))+r')\b')
    text=terms.sub(lambda m:token(GLOSSARY[m[0]]),text)
    if hotkeys:text+=' '+token('('+' '.join('@'+key for key in hotkeys)+')')
    return text,saved

def unprotect(text,saved):
    for index,value in enumerate(saved):
        pattern=re.compile(r'ZXQ\s*'+f'{index:04d}'+r'\s*QXZ',re.I)
        if len(pattern.findall(text))!=1:
            raise ValueError('Translation changed a protected placeholder')
        text=pattern.sub(lambda _:value,text)
    if 'ZXQ' in text or 'QXZ' in text:
        raise ValueError('Unrestored translation placeholder')
    return text.strip()

def request(text):
    query=urllib.parse.urlencode({'client':'gtx','sl':'en','tl':'ko','dt':'t','q':text})
    url='https://translate.googleapis.com/translate_a/single?'+query
    for attempt in range(3):
        try:
            with urllib.request.urlopen(url,timeout=30) as response:
                result=json.load(response)
            return ''.join(part[0] for part in result[0] if part[0])
        except Exception:
            if attempt==2: raise
            time.sleep(2*(attempt+1))

def translate_batch(batch):
    prepared=[protect(source) for source in batch]
    text='\n'.join(f'[RK{i:05d}]\n{item[0]}' for i,item in enumerate(prepared))+'\n[RK99999]'
    translated=request(text)
    markers=list(re.finditer(r'\[\s*RK\s*(\d{5})\s*\]',translated,re.I))
    if [int(m[1]) for m in markers]!=list(range(len(batch)))+[99999]:
        raise ValueError('Translation changed segment boundaries')
    return [unprotect(translated[markers[i].end():markers[i+1].start()],prepared[i][1]) for i in range(len(batch))]

def main():
    rows=json.loads(CATALOG.read_text('utf-8-sig'))
    campaign=ROOT/'reports'/'campaign-catalog.json'
    if campaign.exists():rows+=json.loads(campaign.read_text('utf-8-sig'))
    by_id={}
    for row in rows:
        key=row.get('Key',row['Id'])
        if key in by_id and by_id[key]!=row['Source']:
            raise ValueError('Conflicting translation IDs')
        by_id[key]=row['Source']
    OUTPUT.parent.mkdir(exist_ok=True)
    state=json.loads(OUTPUT.read_text('utf-8')) if OUTPUT.exists() else {'Schema':1,'Method':'Google machine draft + manual UI and skill terminology','Entries':{}}
    state['Method']='Google machine draft + manual UI and skill terminology'
    entries=state['Entries']
    cache={}
    for id,source in by_id.items():
        if id in entries and entries[id]['SourceSha256']==digest(source) and ('@' not in source or state.get('HotkeyPolicy')==1):cache[source]=entries[id]['Korean']
    def save():
        temp=OUTPUT.with_suffix('.tmp')
        temp.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf-8')
        temp.replace(OUTPUT)
    sources=list(dict.fromkeys(by_id.values()))
    pending=[]
    for source in sources:
        clean=source.replace('@','').strip()
        keys=re.findall(r'@([A-Za-z])',source)
        suffix=' ('+' '.join('@'+key for key in keys)+')' if keys else ''
        if clean in MANUAL:cache[source]=MANUAL[clean]+suffix
        elif clean.startswith('Learn ') and clean[6:] in MANUAL:cache[source]=MANUAL[clean[6:]]+' 배우기'+suffix
        elif not re.search('[A-Za-z]',source):cache[source]=source
        elif source.strip() in GLOSSARY:cache[source]=GLOSSARY[source.strip()]
        elif source not in cache:pending.append(source)
    batch=[]; length=0
    batches=[]
    for source in pending:
        estimate=len(source)+len(TOKEN.findall(source))*15+45
        if batch and length+estimate>2800:batches.append(batch);batch=[];length=0
        batch.append(source);length+=estimate
    if batch:batches.append(batch)
    for index,batch in enumerate(batches):
        try: result=translate_batch(batch)
        except ValueError:
            result=[]
            for source in batch:
                text,saved=protect(source)
                try: result.append(unprotect(request(text),saved))
                except ValueError:
                    # Keep literal game expressions even when MT drops them in a sentence.
                    parts=re.split(r'(ZXQ\d{4}QXZ)',text)
                    rebuilt=[]
                    for part in parts:
                        if re.fullmatch(r'ZXQ\d{4}QXZ',part):rebuilt.append(saved[int(part[3:7])])
                        elif re.search('[A-Za-z]',part):rebuilt.append(request(part));time.sleep(.2)
                        else:rebuilt.append(part)
                    result.append(''.join(rebuilt))
                time.sleep(.2)
        for source,translation in zip(batch,result):cache[source]=translation
        for id,source in by_id.items():
            if source in cache:entries[id]={'SourceSha256':digest(source),'Korean':cache[source]}
        save()
        print(f'Batch {index+1}/{len(batches)}; translated IDs {len(entries)}/{len(by_id)}',flush=True)
        time.sleep(.35)
    for id,source in by_id.items():
        entries[id]={'SourceSha256':digest(source),'Korean':cache[source]}
    state['HotkeyPolicy']=1
    save()
    print(f'Completed {len(entries)} IDs. This is a machine-translation draft, not a reviewed translation.',flush=True)

if __name__=='__main__':main()
