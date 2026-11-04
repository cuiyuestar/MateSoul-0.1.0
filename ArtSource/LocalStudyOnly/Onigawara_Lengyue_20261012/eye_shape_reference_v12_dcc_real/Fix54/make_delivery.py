from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import numpy as np,json
R=Path(__file__).resolve().parent;O=Path('/Users/bytedance/DoubaoWork/chats/2026-10-19/new-chat');bg=(15,18,27);font='/System/Library/Fonts/Hiragino Sans GB.ttc'
def f(n):return ImageFont.truetype(font,n)
def read(v,n):return Image.open(R/'renders'/v/(n+'.png')).convert('RGB')
def montage(name,title,panels,labels,width=800):
 scaled=[]
 for im in panels:
  h=round(im.height*width/im.width);scaled.append(im.resize((width,h),Image.Resampling.LANCZOS))
 top=115;h=max(im.height for im in scaled);out=Image.new('RGB',(width*len(panels),top+h+20),bg);d=ImageDraw.Draw(out);d.text((24,18),title,font=f(32),fill='white')
 for i,(im,label) in enumerate(zip(scaled,labels)):
  d.text((i*width+24,74),label,font=f(24),fill='#dddddd');out.paste(im,(i*width,top))
 p=O/name;out.save(p);return p
paths=[]
paths.append(montage('冷月Direct54_真实头部三视图.png','Direct54 · 刘海与耳廓调整 · 真实Unity三视图',[read('after','head_'+str(a)) for a in [0,45,90]],['正面','45°','右侧面']))
# exact crop and equal scaling only; no painting over the render.
paths.append(montage('冷月Direct54_刘海真实前后对照.png','刘海前后对照 · 中央两束及发缝保持原样',[read(v,'head_0').crop((330,365,858,790)) for v in ['before','after']],['Direct53 修改前','Direct54 修改后'],900))
paths.append(montage('冷月Direct54_体态真实前后对照.png','脖子、手臂与腿小幅收细 · 同光照同姿态',[read(v,'body_0') for v in ['before','after']],['Direct53 修改前','Direct54 修改后'],1000))
paths.append(montage('冷月Direct54_耳廓灰模结构对照.png','耳朵结构检查 · 临时灰模与隐藏头发（未写入资产）',[read(v,'ear_clay') for v in ['before','after']],['修改前：有真实曲面，但耳根有缝','修改后：耳窝起伏与耳根衔接'],850))
# More legible local comparison: neck, arm, calf.
im=Image.new('RGB',(1600,1150),bg);d=ImageDraw.Draw(im);d.text((24,20),'Direct53 → Direct54 · 局部等比例对照',font=f(30),fill='white')
boxes=[('脖子', (490,960,710,1105),'head_0'),('手臂',(1000,335,1320,415),'body_0'),('小腿',(675,1035,775,1390),'body_0')]
for row,(label,box,src) in enumerate(boxes):
 for col,v in enumerate(['before','after']):
  p=read(v,src).crop(box);p.thumbnail((700,290),Image.Resampling.LANCZOS);x=col*800+30;y=80+row*350;d.text((x,y),label+(' · 前' if col==0 else ' · 后'),font=f(25),fill='white');im.paste(p,(x,y+42))
p=O/'冷月Direct54_体态局部对照.png';im.save(p);paths.append(p)
(R/'delivery_images.json').write_text(json.dumps([str(p) for p in paths],ensure_ascii=False,indent=2));print('\n'.join(map(str,paths)))
