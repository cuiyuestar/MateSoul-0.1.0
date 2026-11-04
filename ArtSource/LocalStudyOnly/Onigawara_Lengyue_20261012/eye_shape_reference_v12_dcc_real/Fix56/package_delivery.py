from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import shutil,json,zipfile,tarfile,hashlib
R=Path(__file__).resolve().parent;O=Path('/Users/bytedance/DoubaoWork/chats/2026-10-19/new-chat');U=R.parents[1]/'Unity6Study';font='/System/Library/Fonts/Hiragino Sans GB.ttc';bg=(15,18,27)
files=[]
def f(n):return ImageFont.truetype(font,n)
# Original individual renders remain full resolution and pixel-identical.
for n,label in [('full_0','全身正面'),('full_90','全身右侧面'),('full_180','全身背面'),('hair_135','右后侧头发近景'),('hair_180','后脑头发近景'),('hair_225','左后侧头发近景')]:
 src=R/'renders'/(n+'.png');dst=O/('冷月Direct56_'+label+'_高清原图.png');shutil.copyfile(src,dst);assert src.read_bytes()==dst.read_bytes();files.append(dst)
# Three-view overview: no scaling, no markings over any source pixels.
out=Image.new('RGB',(4800,2540),bg);d=ImageDraw.Draw(out);d.text((30,20),'冷月 Direct56 · 高清全身正侧背三视图 · 真实Unity渲染',font=f(44),fill='white')
for i,(n,label) in enumerate(zip(['full_0','full_90','full_180'],['正面','右侧面','背面'])):
 d.text((i*1600+30,88),label,font=f(34),fill='white');im=Image.open(R/'renders'/(n+'.png')).convert('RGB');out.paste(im,(i*1600,140));assert out.crop((i*1600,140,(i+1)*1600,2540)).tobytes()==im.tobytes()
p=O/'冷月Direct56_高清全身三视图.png';out.save(p);files.insert(0,p)
# Thumb close-up before/after, exact same camera crop. Keep normal light.
out=Image.new('RGB',(1600,650),bg);d=ImageDraw.Draw(out);d.text((24,14),'拇指固定暗纹修复 · 同条件真实前后对照',font=f(31),fill='white')
for i,v in enumerate(['before','after']):
 im=Image.open(R/'renders'/f'hand_1_{v}_lit.png').crop((120,400,600,730)).resize((800,440),Image.Resampling.LANCZOS);out.paste(im,(i*800,100));d.text((i*800+24,60),'修改前' if i==0 else '修改后：保留正常明暗',font=f(26),fill='white')
p=O/'冷月Direct56_拇指暗纹真实前后对照.png';out.save(p);files.append(p)
package=O/'冷月Direct56_拇指暗纹修复.unitypackage';verified=[]
with tarfile.open(package,'r:gz') as t:
 ms={m.name:m for m in t.getmembers()}
 for n,m in ms.items():
  if not n.endswith('/pathname'):continue
  path=t.extractfile(m).read().decode().strip();asset=n.rsplit('/',1)[0]+'/asset'
  if asset not in ms:continue
  real=U/path
  if not real.is_file() and path.startswith('Packages/'):
   _,pkg,rest=path.split('/',2);dep=json.loads((U/'Packages/manifest.json').read_text())['dependencies'].get(pkg,'')
   if dep.startswith('file:'):real=(U/'Packages'/dep[5:]/rest).resolve()
   else:
    candidates=list((U/'Library/PackageCache').glob(pkg+'@*/'+rest));assert len(candidates)==1;real=candidates[0]
  assert real.is_file(),path;assert hashlib.sha256(t.extractfile(ms[asset]).read()).digest()==hashlib.sha256(real.read_bytes()).digest(),path;verified.append(path)
(R/'package_gate.json').write_text(json.dumps({'pass':True,'verified_payloads':len(verified),'all_payloads_byte_match':True},indent=2))
notes='''# 冷月 Direct56

本轮只去掉左右拇指根部固定暗纹，修改两张皮肤贴图的两处小块。纹理透明通道与区域外像素保持完全一致，所有网格、骨骼、表情、发束分布、头发纹理及其他材质保持不变。

全身正侧背单张1600×2400，三视总览4800×2540，拼图保留原图像素、不缩放。后发正后、右后45度、左后45度近景均2400×2400。全部为Unity真实渲染，临时放松手臂与中性补光仅用于展示，截图后清理，未写入Prefab。
拇指前后对照使用相同相机和光照。固定黑斑去除，但自然卡通明暗继续保留。

Unity导入“冷月Direct56_拇指暗纹修复.unitypackage”，使用 Assets/Study/EyeShapeReferenceV12DCCReal/Fix56/Onigawara_Lengyue_Direct56.prefab。旧Direct55不覆盖。

验证：贴图区域外像素和Alpha完全一致；原依赖字节、所有网格引用与骨骼不变；原始高清图片复制与总览拼接像素核验；Unity包逐项字节核验；ZIP CRC。
限制：只验收静态零表情与展示站姿，未逐项测试全部动作和物理。服装原有局部空隙与褶皱本轮未修。头发近景暴露的原有头顶分缝/叠片和发尾层次不代表已经修复，不用补光掩盖。
原素材继续受LocalStudyOnly限制，产物不新增商业使用或再分发授权。
'''
(R/'交付说明.md').write_text(notes);(R/'delivery_images.json').write_text(json.dumps([str(p) for p in files],ensure_ascii=False,indent=2))
z=O/'冷月Direct56_高清视图与拇指修复资产.zip'
entries=[(p,'高清视图/'+p.name) for p in files]+[(package,package.name),(R/'交付说明.md','交付说明.md')]
for n in ['texture_gate.json','unity_gate.json','package_gate.json']:entries.append((R/n,'验证/'+n))
for n in ['Body_BaseColor_Direct56.png','Body_Shade_Direct56.png']:entries.append((U/'Assets/Study/EyeShapeReferenceV12DCCReal/Fix56/Textures'/n,'修改贴图/'+n))
entries.append((R/'fix_thumb.py','复现/fix_thumb.py'));entries.append((U/'Assets/Study/EyeShapeReferenceV12DCCReal/Fix56/Editor/Direct56Build.cs','复现/Direct56Build.cs'))
with zipfile.ZipFile(z,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as q:
 for p,n in entries:q.write(p,n)
with zipfile.ZipFile(z) as q:assert q.testzip() is None;print('ZIP',len(q.namelist()),z.stat().st_size,'PAYLOAD',len(verified))
