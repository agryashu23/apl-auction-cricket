"""
Draws the crowd sprite sheet the way StadiumBuilder.Crowd's PaintFan does
(a straight port, for review without Unity): build/gear/preview_fans.png,
shirts in sample colours, 4x scale.  python tools/gear/preview_fans.py
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zlib, struct
COLS, ROWS, CW, CH, VARIANTS = 8, 4, 32, 64, 15
SKIN = [(0.93,0.76,0.62),(0.8,0.6,0.45),(0.63,0.44,0.31),(0.43,0.29,0.2)]
HAIR = [(0.07,0.06,0.05),(0.24,0.15,0.09),(0.07,0.06,0.05),(0.5,0.5,0.52)]
LEGS = [(0.14,0.17,0.28),(0.1,0.1,0.12),(0.45,0.4,0.3),(0.2,0.22,0.3)]
SHIRTS = [(0.9,0.1,0.15),(0.95,0.8,0.1),(0.1,0.3,0.8),(0.95,0.4,0.7),(0.2,0.7,0.3)]
W, H = COLS*CW, ROWS*CH
px = [[None]*W for _ in range(H)]  # (rgb, shirt)
def mul(c,k): return tuple(x*k for x in c)
def paint(ox, oy, v, cheer):
    skin=SKIN[(v*7+1)%4]; hair=HAIR[(v*5+2)%4]; legs=LEGS[(v*3)%4]
    tw=7+v%3; ho=-(v*11%5)-(7 if v in (6,13) else 0); cap=v%5==3; longh=v%7==4; pose=v%4; cx=16
    def put(x,y,c,shirt):
        x=int(x); y=int(y)
        if 0<=x<CW and 0<=y<CH: px[oy+y][ox+x]=(c,shirt)
    def shade(x): k=0.72+0.28*(1-((x-cx)/(tw+3))**2); return (k,k,k)
    def disc(x0,y0,r,c,shirt,shaded=False):
        for y in range(int(y0-r-1),int(y0+r+1)+1):
            for x in range(int(x0-r-1),int(x0+r+1)+1):
                if (x-x0)**2+(y-y0)**2<=r*r: put(x,y,shade(x) if shaded else c,shirt)
    def limb(a,b,r,sleeve):
        s=0.0
        while s<=1.0:
            p=(a[0]+(b[0]-a[0])*s,a[1]+(b[1]-a[1])*s)
            disc(p[0],p[1],r,(0.8,0.8,0.8) if s<sleeve else skin,s<sleeve,s<sleeve); s+=0.04
    for y in range(0,21+int(ho/2)):
        for x in range(cx-tw+1,cx+tw):
            put(x,y,mul(legs,0.7 if x==cx else 1),False)
    top=44+ho; shL=(cx-tw+1,top-3); shR=(cx+tw-1,top-3)
    if cheer:
        if pose==0: limb(shL,(cx-tw-7,top+15),2.1,0.3); limb(shR,(cx+tw+7,top+15),2.1,0.3)
        elif pose==1: limb(shL,(cx-tw-3,top+18),2.1,0.3); limb(shR,(cx+tw+3,top+18),2.1,0.3)
        elif pose==2: limb(shL,(cx-tw-2,top-20),2.1,0.4); limb(shR,(cx+tw+5,top+17),2.1,0.3)
        else:
            limb(shL,(cx-9,top+16),2,0.3); limb(shR,(cx+9,top+16),2,0.3)
            for y in range(min(top+16,CH-7),CH):
                for x in range(2,30):
                    border=y<min(top+18,CH-5) or y>=CH-2 or x<4 or x>=28
                    put(x,y,(1,1,1) if border else (0.96,0.96,0.94),border)
    for y in range(20+int(ho/2),top+1):
        for x in range(cx-tw,cx+tw+1): put(x,y,shade(x),True)
    disc(cx-tw+2,top-1,3,(1,1,1),True,True); disc(cx+tw-2,top-1,3,(1,1,1),True,True)
    if not cheer:
        if pose==1: limb(shL,(cx-tw-1,top-12),2,0.6); limb(shR,(cx+tw+1,top-12),2,0.6); limb((cx-tw,top-12),(cx+tw-2,top-10),2,0)
        elif pose==2:
            limb(shL,(cx-tw-1,24+int(ho/2)),2,0.35); limb(shR,(cx+5,top+6),2,0.35)
            for y in range(top+5,top+11):
                for x in range(cx+3,cx+8): put(x,y,(0.08,0.08,0.1),False)
        elif pose==3: limb(shL,(cx-1,top-8),2,0.35); limb(shR,(cx+1,top-8),2,0.35)
        else: limb(shL,(cx-tw-1,24+int(ho/2)),2,0.35); limb(shR,(cx+tw+1,24+int(ho/2)),2,0.35)
    for y in range(top,top+4):
        for x in range(cx-2,cx+3): put(x,y,mul(skin,0.85),False)
    hy=top+9
    if longh:
        for y in range(hy-4,hy+8):
            for x in range(cx-7,cx+8):
                if (x-cx)**2+(y-hy-1)**2<=49: put(x,y,hair,False)
    disc(cx,hy,6,skin,False)
    for y in range(hy+1,hy+8):
        for x in range(cx-7,cx+8):
            if (x-cx)**2+(y-hy)**2>6.6**2: continue
            if cap: put(x,y,(0.9,0.9,0.9),True)
            elif y>=hy+3: put(x,y,hair,False)
    if cap:
        for x in range(cx-2,cx+9): put(x,hy+1,(0.75,0.75,0.75),True)
for v in range(VARIANTS):
    paint((v%COLS)*CW,(v//COLS)*CH,v,False); paint((v%COLS)*CW,(v//COLS+2)*CH,v,True)
S=4; rows=[]
for y in range(H-1,-1,-1):
    row=b"\x00"
    for x in range(W):
        p=px[y][x]
        if p is None: c=(40,90,40)
        else:
            col,shirt=p
            if shirt: sh=SHIRTS[(x//CW + y//CH)%5]; col=tuple(sh[i]*col[0] for i in range(3))
            c=tuple(int(min(1,max(0,k))*255) for k in col)
        row+=bytes(c)*S
    for _ in range(S): rows.append(row[:1]+row[1:])
raw=b"".join(rows)
def chunk(t,d): return struct.pack(">I",len(d))+t+d+struct.pack(">I",zlib.crc32(t+d)&0xffffffff)
png=b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">IIBBBBB",W*S,H*S,8,2,0,0,0))+chunk(b"IDAT",zlib.compress(raw))+chunk(b"IEND",b"")
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
open(_APL_REPO + "/build/gear/preview_fans.png","wb").write(png)
