from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import numpy as np,json,shutil
R=Path(__file__).resolve().parent;O=Path('/Users/bytedance/DoubaoWork/chats/2026-10-19/new-chat');bg=(15,18,27);font='/System/Library/Fonts/Hiragino Sans GB.ttc'
def f(n):return ImageFont.truetype(font,n)
# Matched side-view crops at native resolution, no retouching or scaling.
out=Image.new('RGB',(1120,800),bg);d=ImageDraw.Draw(out);d.text((20,16),'Direct58 → Direct59 · 侧耳发缝闭合 · 真实Unity对照',font=f(26),fill='white')
for i,version in enumerate(['before','after']):
 im=Image.open(R/'renders'/version/'head_90.png').convert('RGB').crop((460,680,990,1340));out.paste(im,(20+i*550,120));d.text((20+i*550,75),'修改前' if i==0 else '修改后 · 原有发束自然遮挡',font=f(23),fill='white')
p1=O/'冷月Direct59_侧耳闭缝真实前后对照.png';out.save(p1)
out=Image.new('RGB',(3000,1140),bg);d=ImageDraw.Draw(out);d.text((24,18),'冷月 Direct59 · 侧耳发缝自然闭合 · 真实Unity三视图',font=f(36),fill='white')
for i,(angle,label) in enumerate([(0,'正面'),(45,'45°'),(90,'右侧面')]):
 im=Image.open(R/'renders/after'/f'head_{angle}.png').convert('RGB').resize((1000,1000),Image.Resampling.LANCZOS);out.paste(im,(i*1000,140));d.text((i*1000+24,80),label,font=f(30),fill='white')
p2=O/'冷月Direct59_真实头部三视图.png';out.save(p2)
p3=O/'冷月Direct59_右后侧头发高清原图.png';shutil.copyfile(R/'renders/after/hair_135.png',p3)
result={'roi_tests':[],'render_differences':{}}
for name,box in [('head_90',(545,850,710,1105)),('hair_135',(1470,1330,1760,1950))]:
 counts=[]
 for version in ['before','after']:
  a=np.asarray(Image.open(R/'renders'/version/(name+'.png')).convert('RGB').crop(box)).astype(int);skin=(a[:,:,0]>180)&(a[:,:,1]>140)&(a[:,:,2]>110)&(a[:,:,0]>a[:,:,2]);counts.append(int(skin.sum()))
 result['roi_tests'].append({'view':name,'bbox':box,'before_skin_pixels':counts[0],'after_skin_pixels':counts[1],'closed':counts[1]==0})
for name in ['head_0','head_45','head_90','hair_120','hair_135','hair_150','hair_180','hair_225']:
 a=np.asarray(Image.open(R/'renders/before'/f'{name}.png')).astype(int);b=np.asarray(Image.open(R/'renders/after'/f'{name}.png')).astype(int);mask=np.max(abs(a-b),2)>2;yy,xx=np.where(mask);result['render_differences'][name]={'changed_pixels_gt2':int(mask.sum()),'bbox':[int(xx.min()),int(yy.min()),int(xx.max()),int(yy.max())] if len(xx) else None}
(R/'render_gate.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2));assert all(t['closed'] for t in result['roi_tests']),'Closure incomplete'
review=Image.new('RGB',(2400,760),bg)
for i,name in enumerate(['head_0','head_45','head_90','hair_120','hair_135','hair_150','hair_180','hair_225']):
 x=(i%4)*600;y=(i//4)*380;dd=ImageDraw.Draw(review);dd.text((x+10,y+4),name,font=f(20),fill='white')
 for j,version in enumerate(['before','after']):
  im=Image.open(R/'renders'/version/f'{name}.png').convert('RGB').resize((300,300),Image.Resampling.LANCZOS);review.paste(im,(x+j*300,y+40))
review.save(R/'multi_view_review.jpg')
