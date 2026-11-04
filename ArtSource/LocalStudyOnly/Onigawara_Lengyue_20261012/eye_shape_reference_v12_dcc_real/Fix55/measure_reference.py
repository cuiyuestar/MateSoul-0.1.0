from PIL import Image
from pathlib import Path
import numpy as np,json
R=Path(__file__).resolve().parent
im=Image.open(R.parent/'Fix54/用户刘海预期.png').convert('RGB')
im.crop((180,810,510,1000)).resize((990,570)).save(R/'参考刘海局部.png')
# Reference scale from crown/head silhouette, approximate 0.774 of 1200px Unity source; report scanlines separately.
def intervals(mask):
 z=np.flatnonzero(mask);return [(int(a[0]),int(a[-1]),len(a)) for a in np.split(z,np.where(np.diff(z)>1)[0]+1) if len(a)]
report={}
for name,p,rows,x0,x1 in [('ref',R.parent/'Fix54/用户刘海预期.png',[910,930,950,970,985],205,495),('before',R.parent/'Fix54/renders/after/head_0.png',[565,590,616,642,661],410,785)]:
 a=np.array(Image.open(p));out=[]
 for y in rows:
  q=a[y,x0:x1,:3].astype(float);skin=(q[:,0]>170)&(q[:,1]>135)&(q[:,0]-q[:,2]>5)&(q[:,1]/np.maximum(q[:,0],1)>.69)
  out.append({'y':y,'gaps':[(x0+lo,x0+hi,w) for lo,hi,w in intervals(skin)]})
 report[name]=out
(R/'gap_scan_initial.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
