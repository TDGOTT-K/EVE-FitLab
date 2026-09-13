"""Build the desktop ICO and PNG brand assets from the editable SVG master."""
from pathlib import Path
import io
import cairosvg
from PIL import Image
root=Path(__file__).resolve().parent.parent
svg=(root/'assets/app-icon.svg').read_bytes()
master=Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg,output_width=1024,output_height=1024))).convert('RGBA')
master.save(root/'assets/app-icon.png')
master.resize((64,64),Image.Resampling.LANCZOS).save(root/'assets/app-favicon.png')
master.resize((256,256),Image.Resampling.LANCZOS).save(root/'desktop/icon.ico',sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
master.resize((256,256),Image.Resampling.LANCZOS).save(root/'desktop/icon.png')
preview=Image.new('RGB',(850,350),'#0c1721')
for size,x in [(256,30),(128,330),(64,510),(32,630),(16,730)]:
 icon=master.resize((size,size),Image.Resampling.LANCZOS);preview.paste(icon,(x,(350-size)//2),icon)
(root/'output').mkdir(exist_ok=True)
preview.save(root/'output/app-icon-preview.png')
print('Built 16–256 px ICO, favicon and 1024 px master PNG')
