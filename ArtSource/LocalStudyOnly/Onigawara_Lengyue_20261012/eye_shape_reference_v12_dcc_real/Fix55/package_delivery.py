from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,tarfile,hashlib,zipfile
R=Path(__file__).resolve().parent;O=Path('/Users/bytedance/DoubaoWork/chats/2026-10-19/new-chat');U=R.parents[1]/'Unity6Study';font='/System/Library/Fonts/Hiragino Sans GB.ttc';bg=(14,17,27)
def f(n):return ImageFont.truetype(font,n)
def img(v,a):return Image.open(R/'renders'/v/f'head_{a}.png').convert('RGB')
def montage(path,title,arr,labels,crop=None):
 ims=[]
 for im in arr:
  if crop:im=im.crop(crop)
  im.thumbnail((900,900),Image.Resampling.LANCZOS);ims.append(im)
 out=Image.new('RGB',(900*len(ims),110+max(x.height for x in ims)),bg);d=ImageDraw.Draw(out);d.text((22,14),title,font=f(31),fill='white')
 for i,(im,l) in enumerate(zip(ims,labels)):d.text((i*900+22,65),l,font=f(23),fill='#dddddd');out.paste(im,(i*900,110))
 out.save(path)
p1=O/'冷月Direct55_刘海间隙真实前后对照.png';montage(p1,'Direct54 → Direct55 · 非中央发束间隙微调',[img('before',0),img('after',0)],['修改前','修改后'],(365,315,835,745))
p2=O/'冷月Direct55_刘海真实三视图.png';montage(p2,'Direct55 · 刘海间隙精修 · 真实Unity三视图',[img('after',a) for a in [0,45,90]],['正面','45°','右侧面'])
# Verify exported package payloads against project and package cache/local file dependencies.
package=O/'冷月Direct55_刘海间隙精修.unitypackage';verified=[]
with tarfile.open(package,'r:gz') as t:
 members={x.name:x for x in t.getmembers()}
 for n,m in members.items():
  if not n.endswith('/pathname'):continue
  q=t.extractfile(m).read().decode().strip();asset=n.rsplit('/',1)[0]+'/asset'
  if asset not in members:continue
  real=U/q
  if not real.is_file() and q.startswith('Packages/'):
   _,pkg,rest=q.split('/',2);dep=json.loads((U/'Packages/manifest.json').read_text())['dependencies'].get(pkg,'')
   if dep.startswith('file:'):real=(U/'Packages'/dep[5:]/rest).resolve()
   else:
    c=list((U/'Library/PackageCache').glob(pkg+'@*/'+rest));assert len(c)==1;(real:=c[0])
  assert real.is_file();data=t.extractfile(members[asset]).read();assert hashlib.sha256(data).digest()==hashlib.sha256(real.read_bytes()).digest();verified.append(q)
(R/'package_gate.json').write_text(json.dumps({'pass':True,'verified_assets':len(verified),'payload_bytes_match_project':True},indent=2))
notes='''# 冷月 Direct55 刘海间隙精修
- 从Direct54继续，仅调整非中央五组刘海的局部横向侧缘，共206点，最大位移约1.48毫米。
- 中央四组发束及现有中央发缝逐点保持不变；刘海高度和前后深度逐点保持不变。
- 上半部圆弧、整体厚度、自然末端、高光材质、身体、耳朵、脸型、妆容与其他发型均未改动。
- 同步重建Direct55发束描边，沿新边界并沿用原骨骼权重。
- 像素扫描：正面下缘五处开口在相同1200×1200渲染中，代表性扫描行总露肤宽度由25/34px增至40/52px；上部扫描线与中央缝保持原结果。
- 独立重开Blend以及Unity门禁通过。几何无三角翻转或新增退化面。
- 静态零表情验收；未逐项测试所有动态、表情与LOD。
- 原素材继续受LocalStudyOnly限制。
''';(R/'交付说明.md').write_text(notes)
files=[(R/'Onigawara_Lengyue_Direct55.blend','Onigawara_Lengyue_Direct55.blend'),(package,package.name),(p1,'预览/'+p1.name),(p2,'预览/'+p2.name),(R/'交付说明.md','交付说明.md')]
for n in ['geometry_gate.json','unity_gate.json','independent_blend_gate.json','package_gate.json','gap_measurement.json']:files.append((R/n,'验证/'+n))
for n in ['build_hair.py','verify_blend.py','Hair_new.bin','Hair_new_normals.bin','Hair_target.bin','central_hair_locked.bin']:files.append((R/n,'复现/'+n))
for p in (U/'Assets/Study/EyeShapeReferenceV12DCCReal/Fix55/Editor').glob('*.cs'):files.append((p,'复现/Unity/'+p.name))
manifest=[{'path':n,'size':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p,n in files];(R/'delivery_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2));files.append((R/'delivery_manifest.json','delivery_manifest.json'))
z=O/'冷月Direct55_刘海间隙精修真实资产.zip'
with zipfile.ZipFile(z,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as q:
 for p,n in files:q.write(p,n)
with zipfile.ZipFile(z) as q:assert q.testzip() is None;print('ZIP',len(q.namelist()),z.stat().st_size,'UnityPayloads',len(verified))
