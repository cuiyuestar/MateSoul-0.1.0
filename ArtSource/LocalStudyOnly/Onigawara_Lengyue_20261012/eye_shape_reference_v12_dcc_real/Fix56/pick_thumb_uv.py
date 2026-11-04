import numpy as np
from pathlib import Path
from PIL import Image,ImageDraw
R=Path(__file__).resolve().parent;v=np.fromfile(R/'body_world.bin','<f4').reshape(-1,3);uv=np.fromfile(R/'body_uv.bin','<f4').reshape(-1,2);t=np.fromfile(R/'sub0.bin','<i4').reshape(-1,3)
f=np.array([0,-1.5,-.3]);f/=np.linalg.norm(f);right=np.cross([1,0,0],f);right/=np.linalg.norm(right);up=np.cross(f,right);target=np.array([.57,1.244,-.027])
def hit(x,y):
 o=target+right*((x/1000-.5)*.162)+up*((.5-y/1000)*.162)-f*2;a=v[t[:,0]];e1=v[t[:,1]]-a;e2=v[t[:,2]]-a;h=np.cross(np.broadcast_to(f,e2.shape),e2);det=(e1*h).sum(1);inv=np.divide(1,det,out=np.zeros_like(det),where=abs(det)>1e-9);s=o-a;u=inv*(s*h).sum(1);q=np.cross(s,e1);w=inv*(q*f).sum(1);d=inv*(e2*q).sum(1);valid=(u>=0)&(w>=0)&(u+w<=1)&(d>0);d[~valid]=np.inf;j=d.argmin();return t[j],uv[t[j]].T@np.array([1-u[j]-w[j],u[j],w[j]])
for x,y in [(220,459),(205,450),(235,454)]:
 ids,coord=hit(x,y);print(ids,coord,'pixels',coord*[4096,-4096]+[0,4096]);p=coord*[4096,-4096]+[0,4096];im=Image.open(R.parents[1]/'Unity6Study/Assets/Study/EyeShapeReferenceV12DCCReal/Fix34/Textures/Body_BaseColor_Direct34.png').convert('RGB');im.crop((int(p[0])-65,int(p[1])-65,int(p[0])+65,int(p[1])+65)).resize((780,780)).save(R/f'true_thumb_{x}.png')
