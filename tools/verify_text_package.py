"""Independent binary-preservation and XML checks for a locally built text package."""
import argparse
import json
import re
import struct
import xml.etree.ElementTree as ET
import hashlib
from pathlib import Path
from inspect_bfhp import inspect

p=argparse.ArgumentParser();p.add_argument('game',type=Path);p.add_argument('package',type=Path);args=p.parse_args()
marker=re.compile(rb'\[\[([^\[\]]+)\]\]([\s\S]*?)\[\[\]\]')

def pak(path):
    b=path.read_bytes();count,size=struct.unpack_from('<II',b,32);at=40;previous=b'';result={}
    for _ in range(count):
        length,prefix=struct.unpack_from('<BB',b,at);at+=2
        name=previous[:prefix]+b[at:at+length-prefix];at+=length-prefix
        offset,length=struct.unpack_from('<II',b,at);at+=8
        result[name.decode('ascii')]=b[offset:offset+length];previous=name
    assert at==40+size
    return result

unchanged=changed=xml_checked=0
def compare(original,patched):
    global unchanged,changed,xml_checked
    assert original.keys()==patched.keys(),'Archive paths changed'
    for name,before in original.items():
        after=patched[name]
        if not name.lower().endswith(('.xml','.ini','.vs','.txt')) or not marker.search(before):
            assert before==after,f'Untranslated payload changed: {name}'
            unchanged+=1;continue
        assert len(marker.findall(before))==len(marker.findall(after)),f'Marker count changed: {name}'
        before_markers=marker.findall(before);after_markers=marker.findall(after)
        assert [m[0] for m in before_markers]==[m[0] for m in after_markers],f'IDs changed: {name}'
        # Replacing only the string payloads must leave every other byte unchanged.
        assert marker.sub(lambda m:b'[['+m[1]+b']][[]]',before)==marker.sub(lambda m:b'[['+m[1]+b']][[]]',after),f'Non-text bytes changed: {name}'
        for _,text in after_markers:
            text.decode('utf8')
        if name.lower().endswith('.xml'):
            try:ET.fromstring(before)
            except (ET.ParseError,ValueError):pass
            else:
                ET.fromstring(after);xml_checked+=1
        changed+=1

compare(pak(args.game/'Packs/data.pak'),pak(args.package/'files/Packs/data.pak'))
original_constants=pak(args.game/'Packs/data.pak')['DATA\\VXCONST.INI']
patched_constants=pak(args.package/'files/Packs/data.pak')['DATA\\VXCONST.INI']
def races(text):
    return re.search(rb'(?ms)^\[Races\].*?(?=^\[)',text)[0]
assert races(original_constants)==races(patched_constants),'Engine race identifiers changed'
for name in ['Darkling Campaign.bfhp','Forester Campaign.bfhp','Human Campaign.bfhp','Tutorial.BFHP']:
    a={};b={};inspect(args.game/'adventures'/name,payloads=a);inspect(args.package/'files/adventures'/name,payloads=b);compare(a,b)
for original in (args.game/'Local/Fonts').glob('*.apf'):
    patched=args.package/'files/Local/Fonts'/original.name
    def ranges(path):
        b=path.read_bytes();at=struct.unpack_from('<I',b,28)[0];info=struct.unpack_from('<I',b,at+12)[0]
        count=struct.unpack_from('<I',b,info+56)[0];result={}
        for i in range(count):
            offset,size,start,length=struct.unpack_from('<4I',b,info+60+i*16)
            result[start,length]=b[offset:offset+size]
        return result
    a=ranges(original);b=ranges(patched)
    for key,value in a.items():assert b[key]==value,f'Original glyph changed: {original.name}, {key}'
print(json.dumps({'changed_text_files':changed,'preserved_other_files':unchanged,'xml_documents_validated':xml_checked,'original_font_ranges_preserved':True}))
native=args.package/'files/Local/English/game.loc.xml'
if native.exists():
    rows=ET.parse(native).getroot().findall('translationtableentry')
    exe=(args.game/'RK.exe').read_bytes()
    dictionary=json.loads((Path(__file__).resolve().parents[1]/'translations/ko.json').read_text('utf-8'))
    assert {r.attrib['id'] for r in rows}==set(dictionary['EngineEntries'])
    for row in rows:
        id,source,translated=(row.attrib[k] for k in ['id','original_text','translated_text'])
        assert b'[['+id.encode()+b']]'+source.encode('cp1252')+b'[[]]' in exe
        assert hashlib.sha256(source.encode()).hexdigest()==dictionary['Entries'][id]['SourceSha256']
        assert translated==dictionary['Entries'][id]['Korean']
    print(json.dumps({'native_engine_strings_validated':len(rows),'native_xml_valid':True}))
