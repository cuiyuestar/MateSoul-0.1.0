from pathlib import Path
from PIL import Image
import numpy as np,json
R=Path(__file__).resolve().parent;U=R.parents[1]/'Unity6Study/Assets/Study/EyeShapeReferenceV12DCCReal';reports=[]
# Draw only inside the two known eyebrow UV islands. Everything else bit-identical.
boxes=[(2140,630,2990,899),(3110,630,3990,899)]
for kind in ['BaseColor','Shade']:
 source=U/'Fix41/Textures/Face_Expression_Direct41.png' if kind=='BaseColor' else R/'original_expression_shade.png';a=np.array(Image.open(source).convert('RGBA'));factor=a.shape[1]/4096;local_boxes=[tuple(round(v*factor) for v in box) for box in boxes];b=a.copy()
 # first brow (right side of model): outer -> inner
 for box in local_boxes:
  x0,y0,x1,y1=box;b[y0:y1,x0:x1]=0
  yp,xp=np.mgrid[y0:y1,x0:x1];yy=yp/factor;xx=xp/factor
  # mirrored counterpart around texture x=3072.5, matching the existing UV layout.
  x=xx if x0<3000*factor else 6145-xx
  t=(x-2220)/712;valid=(t>=0)&(t<=1);tc=np.clip(t,0,1)
  # Lower, smoother arch; tail gradually tapers; soft rounded inner head.
  center=818-26*np.sin(np.pi*tc)**.92-5*tc
  half=14*np.sin(np.pi*tc)**.6*(.70+.30*tc)
  alpha=np.clip((half-np.abs(yy-center)+.8)/1.6,0,1)*valid
  color=np.array([51,28,77] if kind=='BaseColor' else [40,20,63],dtype=np.uint8)
  block=b[y0:y1,x0:x1];block[:,:,:3]=color;block[:,:,3]=np.rint(alpha*255).astype(np.uint8)
  # Transparent pixels retain zero RGB as original convention.
  block[block[:,:,3]==0,:3]=0
 outside=np.ones(a.shape[:2],bool)
 for x0,y0,x1,y1 in local_boxes:outside[y0:y1,x0:x1]=False
 assert np.array_equal(a[outside],b[outside]);dst=U/'Fix57/Textures'/f'Face_{kind}_Direct57.png';Image.fromarray(b).save(dst);reports.append({'kind':kind,'outside_brows_exact':True,'changed_pixels':int(np.any(a!=b,axis=2).sum())})
(R/'texture_gate.json').write_text(json.dumps({'pass':True,'base_brow_rgb':[51,28,77],'shade_brow_rgb':[40,20,63],'textures':reports},indent=2))
# Composite preview of brows against neutral background only for inspection.
im=Image.open(U/'Fix57/Textures/Face_BaseColor_Direct57.png');factor=im.width/4096;q=im.crop(tuple(round(v*factor) for v in (2100,610,4050,915)));bg=Image.new('RGBA',q.size,(160,160,160,255));bg.alpha_composite(q);bg.convert('RGB').save(R/'brow_texture_after.png');print(reports)
