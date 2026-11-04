from pathlib import Path
import numpy as np,json
from PIL import Image,ImageDraw
R=Path(__file__).resolve().parent;v=np.fromfile(R/'body_world.bin','<f4').reshape(-1,3);uv=np.fromfile(R/'body_uv.bin','<f4').reshape(-1,2);tri=np.fromfile(R/'sub0.bin','<i4').reshape(-1,3);im=Image.open(R.parents[1]/'Unity6Study/Assets/Study/EyeShapeReferenceV12DCCReal/Fix34/Textures/Body_BaseColor_Direct34.png');a=np.array(im);mask=Image.new('L',im.size);d=ImageDraw.Draw(mask)
for t in tri:
 p=v[t]
 if np.all(abs(p[:,0])>.525):d.polygon([(float(uv[i,0]*4096),float((1-uv[i,1])*4096)) for i in t],fill=255)
m=np.array(mask)>0;dark=m&(a[:,:,:3].mean(2)<160)&(a[:,:,3]>200);yy,xx=np.where(dark);print('darkcount',len(xx));print('ranges',xx.min(),xx.max(),yy.min(),yy.max());coords=np.stack([xx,yy],axis=1);np.save(R/'hand_dark_pixels.npy',coords)
# grid occupied areas list
from collections import Counter
print(Counter((int(x//32),int(y//32)) for x,y in coords).most_common(30))
bg=Image.new('RGBA',im.size,(80,80,80,255));bg.alpha_composite(im);bg=bg.convert('RGB');draw=ImageDraw.Draw(bg)
for x,y in coords:draw.point((int(x),int(y)),fill='red')
bg.resize((2000,2000)).save(R/'dark_uv_locations.png');mask.save(R/'hand_mask.png')
