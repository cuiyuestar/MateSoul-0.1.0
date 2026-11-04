from pathlib import Path
from PIL import Image,ImageFilter
import numpy as np,json
R=Path(__file__).resolve().parent;T=R.parents[1]/'Unity6Study/Assets/Study/EyeShapeReferenceV12DCCReal/Fix56/Textures';S=R.parents[1]/'Unity6Study/Assets/Study/EyeShapeReferenceV12DCCReal/Fix34/Textures';report=[]
for kind in ['BaseColor','Shade']:
 im=Image.open(S/f'Body_{kind}_Direct34.png').convert('RGBA');a=np.array(im);b=a.copy()
 for x in [249,775]:
  box=(x-15,3833,x+15,3870);x0,y0,x1,y1=box;patch=a[y0:y1,x0:x1,:3].astype(float);sample_x=x+26 if x<500 else x-26;sample=a[y0:y1,sample_x-3:sample_x+4,:3].mean(1)
  yy,xx=np.mgrid[y0:y1,x0:x1];weight=np.clip((15-abs(xx-x))/4,0,1)*np.clip((yy-y0)/4,0,1)*np.clip((y1-1-yy)/4,0,1);b[y0:y1,x0:x1,:3]=np.rint(patch*(1-weight[...,None])+sample[:,None,:]*weight[...,None]).astype('uint8')
 assert np.array_equal(a[:,:,3],b[:,:,3]);changed=np.any(a!=b,axis=2);outside=np.ones(changed.shape,bool)
 for x in [249,775]:outside[3833:3870,x-15:x+15]=False
 assert not changed[outside].any();Image.fromarray(b).save(T/f'Body_{kind}_Direct56.png');report.append({'kind':kind,'changed_pixels':int(changed.sum()),'alpha_exact':True,'outside_two_thumb_regions_exact':True})
(R/'texture_gate.json').write_text(json.dumps({'pass':True,'textures':report},indent=2));print(report)
