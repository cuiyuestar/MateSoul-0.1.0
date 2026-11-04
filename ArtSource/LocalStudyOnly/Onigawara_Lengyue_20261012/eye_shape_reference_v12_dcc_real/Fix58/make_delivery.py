from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import numpy as np,json,shutil
R=Path(__file__).resolve().parent;O=Path('/Users/bytedance/DoubaoWork/chats/2026-10-19/new-chat');bg=(15,18,27);font='/System/Library/Fonts/Hiragino Sans GB.ttc'
def f(n):return ImageFont.truetype(font,n)
# Pixel-identical crops: no generated imagery, retouching or nonuniform scaling.
box=(1360,1260,1960,2270);out=Image.new('RGB',(1240,1130),bg);d=ImageDraw.Draw(out);d.text((20,12),'Direct57 → Direct58 · 右后侧发缝真实对照',font=f(28),fill='white')
for i,version in enumerate(['before','after']):
 im=Image.open(R/'renders'/version/'hair_135.png').convert('RGB');out.paste(im.crop(box),(20+i*620,110));d.text((20+i*620,63),'修改前' if i==0 else '修改后 · 收窄空隙，保留分束',font=f(23),fill='white')
p1=O/'冷月Direct58_后侧发缝真实前后对照.png';out.save(p1)
p2=O/'冷月Direct58_右后侧头发高清原图.png';shutil.copyfile(R/'renders/after/hair_135.png',p2)
out=Image.new('RGB',(3000,1140),bg);d=ImageDraw.Draw(out);d.text((24,18),'冷月 Direct58 · 后侧发缝优化 · 真实Unity三视图',font=f(36),fill='white')
for i,(angle,label) in enumerate([(0,'正面'),(45,'45°'),(90,'右侧面')]):
 im=Image.open(R/'renders/after'/f'head_{angle}.png').convert('RGB').resize((1000,1000),Image.Resampling.LANCZOS);out.paste(im,(i*1000,140));d.text((i*1000+24,80),label,font=f(30),fill='white')
p3=O/'冷月Direct58_真实头部三视图.png';out.save(p3)
# Verify the width of this particular exposed skin opening on fixed pixel rows of the actual render.
result={'measurement':'render-pixel widths of the marked skin opening at matched rows; not physical units','rows':[], 'render_differences':{}}
for y in [1650,1750,1850,1950]:
 vals=[]
 for version in ['before','after']:
  a=np.asarray(Image.open(R/'renders'/version/'hair_135.png').convert('RGB')).astype(int);row=a[y,1500:1800];mask=(row[:,0]>180)&(row[:,1]>140)&(row[:,2]>110)&(row[:,0]>row[:,2]);vals.append(int(mask.sum()))
 result['rows'].append({'y':y,'before_width_px':vals[0],'after_width_px':vals[1]})
assert all(0<x['after_width_px']<x['before_width_px'] for x in result['rows'])
for name in ['head_0','head_45','head_90','hair_120','hair_135','hair_150','hair_180','hair_225']:
 a=np.asarray(Image.open(R/'renders/before'/f'{name}.png')).astype(int);b=np.asarray(Image.open(R/'renders/after'/f'{name}.png')).astype(int);mask=np.max(abs(a-b),2)>2;yy,xx=np.where(mask);result['render_differences'][name]={'changed_pixels_gt2':int(mask.sum()),'bbox':[int(xx.min()),int(yy.min()),int(xx.max()),int(yy.max())] if len(xx) else None}
(R/'render_gate.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2));print('DELIVERY',p1,p2,p3)
# Review contact sheet, for inspection only, all 8 angles before/after at the same scales.
review=Image.new('RGB',(2400,1280),bg)
for i,name in enumerate(['head_0','head_45','head_90','hair_120','hair_135','hair_150','hair_180','hair_225']):
 x=(i%4)*600;y=(i//4)*640;dd=ImageDraw.Draw(review);dd.text((x+10,y+4),name,font=f(20),fill='white')
 for j,version in enumerate(['before','after']):
  im=Image.open(R/'renders'/version/f'{name}.png').convert('RGB').resize((300,300),Image.Resampling.LANCZOS);review.paste(im,(x+j*300,y+40))
review.crop((0,0,2400,1000)).save(R/'multi_view_review.jpg')
