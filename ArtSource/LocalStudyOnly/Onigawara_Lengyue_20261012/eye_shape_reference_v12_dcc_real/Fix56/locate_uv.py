import numpy as np,json
from pathlib import Path
from PIL import Image,ImageDraw
R=Path(__file__).resolve().parent;v=np.fromfile(R/'body_world.bin','<f4').reshape(-1,3);uv=np.fromfile(R/'body_uv.bin','<f4').reshape(-1,2);im=Image.open(R.parents[1]/'Unity6Study/Assets/Study/EyeShapeReferenceV12DCCReal/Fix34/Textures/Body_BaseColor_Direct34.png').convert('RGBA');d=ImageDraw.Draw(im)
ix=np.where((abs(v[:,0])>.52)&(abs(v[:,0])<.63))[0];print('hand UV ranges',uv[ix].min(0),uv[ix].max(0))
for i in ix:
 x,y=uv[i]*[im.width,im.height];y=im.height-y;d.ellipse((x-3,y-3,x+3,y+3),fill=(255,0,0,255))
# skin region of atlas includes hands; retain alpha visible over gray
bg=Image.new('RGBA',im.size,(90,90,90,255));bg.alpha_composite(im);bg.convert('RGB').resize((1800,1800)).save(R/'hand_uv_locate.jpg')
(R/'hand_uv_info.json').write_text(json.dumps([{'i':int(i),'p':v[i].tolist(),'uv':uv[i].tolist()} for i in ix],indent=2))
