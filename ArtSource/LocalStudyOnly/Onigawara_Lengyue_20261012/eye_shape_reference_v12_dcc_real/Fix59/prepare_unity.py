from pathlib import Path
R=Path(__file__).resolve().parent;B=R.parent;U=R.parents[1]/'Unity6Study/Assets/Study/EyeShapeReferenceV12DCCReal';D=U/'Fix59/Editor'
s=(U/'Fix58/Editor/Direct58Build.cs').read_text().replace('Direct58','Direct59').replace('Fix58','Fix59').replace('Fix57/Onigawara_Lengyue_Direct57.prefab','Fix58/Onigawara_Lengyue_Direct58.prefab').replace('后侧发缝优化','侧耳发缝闭合');(D/'Direct59Build.cs').write_text(s)
s=(U/'Fix58/Editor/Direct58Lines.cs').read_text().replace('Direct58','Direct59').replace('Fix58','Fix59');(D/'Direct59Lines.cs').write_text(s)
s=(B/'Fix58/verify_blend.py').read_text().replace('Fix55/Onigawara_Lengyue_Direct55.blend','Fix58/Onigawara_Lengyue_Direct58.blend').replace('DIRECT58_BLEND_OK','DIRECT59_BLEND_OK');(R/'verify_blend.py').write_text(s)
print('Direct59 build and validation scripts ready')
