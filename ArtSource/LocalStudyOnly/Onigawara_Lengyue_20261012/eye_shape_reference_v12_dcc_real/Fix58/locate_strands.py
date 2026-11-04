from pathlib import Path
import numpy as np,json
from PIL import Image,ImageDraw,ImageFont
R=Path(__file__).resolve().parent;B=R.parent
v=np.fromfile(B/'Fix55/Hair_new.bin','<f4').reshape(-1,3).astype(float)
t=np.load(B/'Fix39/hair_triangles.npy');groups=json.loads((B/'Fix43/strand_groups.json').read_text());owner=np.full(len(v),-1)
for g in groups:owner[g['ids']]=g['id']
target=np.array([0,1.455,-.025]);d=np.array([np.sin(np.pi*.75),.05,np.cos(np.pi*.75)]);d/=np.linalg.norm(d);right=np.cross([0,1,0],-d);right/=np.linalg.norm(right);up=np.cross(-d,right)
xy=np.column_stack([1200+(v-target)@right*7500,1200-(v-target)@up*7500]);depth=(v-target)@d
# Sort and draw actual projected triangles to identify both bordering strands.
im=Image.new('I',(2400,2400),-1);draw=ImageDraw.Draw(im)
for k in np.argsort(depth[t].mean(1)):
 ids=t[k];draw.polygon([tuple(p) for p in xy[ids]],fill=int(owner[ids[0]]))
im.save(R/'hair_group_projection.tiff')
raw=Image.open(B/'Fix56/renders/hair_135.png').convert('RGB');dr=ImageDraw.Draw(raw);font=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',28)
for y in [1450,1600,1750,1900,2050]:
 a=np.asarray(im);r=a[y,1450:1900];runs=[]
 for x in range(1,len(r)):
  if r[x]!=r[x-1]:runs.append((1450+x,int(r[x-1]),int(r[x])))
 print(y,runs)
 for x,old,new in runs:
  dr.ellipse((x-4,y-4,x+4,y+4),fill='yellow');dr.text((x+5,y),str(new),font=font,fill='yellow')
raw.crop((1300,1300,2000,2250)).save(R/'strand_location.png')
for gid in [g['id'] for g in groups]:
 ids=np.where(owner==gid)[0];q=v[ids];p=xy[ids];mask=(p[:,0]>1450)&(p[:,0]<1900)&(p[:,1]>1400)&(p[:,1]<2200)
 if mask.any():print('candidate',gid,'bounds',q.min(0).round(5),q.max(0).round(5),'projection',p.min(0).round(),p.max(0).round())
np.save(R/'hair_projected.npy',xy)
