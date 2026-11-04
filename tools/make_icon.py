from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path

ASSETS = Path(__file__).resolve().parent.parent / "src" / "RightMenuManager" / "Assets"
ASSETS.mkdir(parents=True, exist_ok=True)

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)

# 背景渐变圆角方块
grad = Image.new("RGBA", (S, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / (S - 1)
    r = int(59 + (29 - 59) * t)
    g = int(130 + (78 - 130) * t)
    b = int(246 + (216 - 246) * t)
    gd.line([(0, y), (S, y)], fill=(r, g, b, 255))

mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle([0, 0, S - 1, S - 1], radius=218, fill=255)
img.paste(grad, (0, 0), mask)

# 顶部高光
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gg = ImageDraw.Draw(glow)
gg.ellipse([-260, -420, 760, 380], fill=(255, 255, 255, 26))
img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))

draw = ImageDraw.Draw(img)

# 菜单卡片阴影
shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(shadow)
sd.rounded_rectangle([536, 268, 872, 664], radius=46, fill=(8, 30, 80, 90))
shadow = shadow.filter(ImageFilter.GaussianBlur(26))
img = Image.alpha_composite(img, shadow)
draw = ImageDraw.Draw(img)

# 菜单卡片
draw.rounded_rectangle([520, 250, 856, 646], radius=46, fill=(255, 255, 255, 255))

# 菜单三条线
for i, width in enumerate([196, 236, 160]):
    y = 336 + i * 108
    draw.rounded_rectangle([588, y, 588 + width, y + 36], radius=18, fill=(37, 99, 235, 255))

# 光标阴影
cursor = [(238, 246), (238, 720), (368, 596), (466, 812), (570, 764), (474, 552), (650, 552)]
cshadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
cs = ImageDraw.Draw(cshadow)
cs.polygon([(x + 16, y + 20) for x, y in cursor], fill=(8, 30, 80, 100))
cshadow = cshadow.filter(ImageFilter.GaussianBlur(18))
img = Image.alpha_composite(img, cshadow)
draw = ImageDraw.Draw(img)

# 光标本体：白色填充 + 蓝色描边
draw.polygon(cursor, fill=(255, 255, 255, 255), outline=(29, 78, 216, 255), width=18)
# 内部细节
draw.line([(300, 380), (300, 640)], fill=(219, 228, 245, 255), width=10)

# 4x 降采样，获得平滑边缘
img = img.resize((512, 512), Image.LANCZOS)
img.save(ASSETS / "app-icon-512.png")

ico = img.resize((256, 256), Image.LANCZOS)
ico.save(
    ASSETS / "app.ico",
    format="ICO",
    sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
)
print("icon written to", ASSETS)
