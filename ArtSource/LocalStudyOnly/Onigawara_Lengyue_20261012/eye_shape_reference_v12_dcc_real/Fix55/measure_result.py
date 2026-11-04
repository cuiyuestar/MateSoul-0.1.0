from PIL import Image
from pathlib import Path
import numpy as np,json
R=Path(__file__).resolve().parent
def ints(mask):
 z=np.flatnonzero(mask);return [(int(a[0]),int(a[-1]),len(a)) for a in np.split(z,np.where(np.diff(z)>1)[0]+1) if len(a)]
def scan(path,rows,x0,x1):
 a=np.array(Image.open(path));o=[]
 for y in rows:
  q=a[y,x0:x1,:3].astype(float);skin=(q[:,0]>170)&(q[:,1]>135)&(q[:,0]-q[:,2]>5)&(q[:,1]/np.maximum(q[:,0],1)>.69)
  o.append({'y':y,'gaps':[(x0+x,x0+y,w) for x,y,w in ints(skin)]})
 return o
rows=[565,590,616,642,661];before=scan(R/'renders/before/head_0.png',rows,410,785);after=scan(R/'renders/after/head_0.png',rows,410,785)
def total(o):return [sum(x[2] for x in r['gaps']) for r in o]
report={'before':before,'after':after,'total_skin_opening_px_before':total(before),'total_skin_opening_px_after':total(after),'delta_px':[b-a for a,b in zip(total(before),total(after))]}
(R/'gap_measurement.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
