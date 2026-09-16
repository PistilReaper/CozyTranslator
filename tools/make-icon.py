from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1]
target = root / 'src/CozyTranslator.App/Assets'
target.mkdir(parents=True, exist_ok=True)
im = Image.new('RGBA', (256, 256))
draw = ImageDraw.Draw(im)
draw.rounded_rectangle((4, 4, 252, 252), 76, fill='#F8F6F1', outline='#E4DFD5', width=4)
font = ImageFont.truetype('C:/Windows/Fonts/georgiai.ttf', 220)
draw.text((54, -18), 'c', font=font, fill='#B9674D')
draw.ellipse((195, 190, 215, 210), fill='#B9674D')
im.save(target / 'cozy.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
