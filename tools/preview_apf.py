"""Render generated APF glyphs without launching or modifying the game."""
import argparse
import struct
from pathlib import Path
from PIL import Image

p=argparse.ArgumentParser()
p.add_argument('font',type=Path)
p.add_argument('output',type=Path)
p.add_argument('--text',default='메뉴 게임 불러오기 설정 계속하기 종료')
args=p.parse_args()
b=args.font.read_bytes()
def integer(at):return struct.unpack_from('<i',b,at)[0]
info=integer(integer(28)+12)
height=integer(info)
canvas=Image.new('L',(1200,height+6))
x=0
for ch in args.text:
    for index in range(integer(info+56)):
        at=info+60+index*16
        offset,size,start,count=struct.unpack_from('<4i',b,at)
        if not start<=ord(ch)<start+count:continue
        glyph=offset+16+(ord(ch)-start)*32
        a,width,c,left,top,right,bottom,begin=struct.unpack_from('<8i',b,glyph)
        end=integer(glyph+60) if ord(ch)-start+1<count else integer(offset+12)
        pixels=[]
        for token in b[offset+integer(offset+8)+begin:offset+integer(offset+8)+end]:
            pixels.extend([((token>>5)*255)//7]*((token&31)+1))
        if right>=left and bottom>=top:
            image=Image.new('L',(right-left+1,bottom-top+1))
            if len(pixels)!=image.width*image.height:raise ValueError('Glyph RLE length mismatch')
            image.putdata(pixels)
            canvas.paste(image,(x+left,top+2))
        x+=a+width+c
        break
    else:raise ValueError(f'Glyph missing: {ch}')
canvas.crop((0,0,x+2,height+6)).resize(((x+2)*3,(height+6)*3),Image.Resampling.NEAREST).save(args.output)
