"""Draws the BlockCraft icon (an isometric grass block) and writes icon.ico.
Pure Python (no extra packages): renders pixels, encodes PNGs with zlib, packs an ICO."""
import random, struct, zlib, os

def texture(seed, kind):
    r = random.Random(seed)
    tex = []
    edge = [3 + (r.random() < 0.5) + (r.random() < 0.25) for _ in range(16)]
    for y in range(16):
        row = []
        for x in range(16):
            n = r.randint(-18, 18)
            if kind == 'top' or y < edge[x]:
                c = (92 + n, 156 + n, 52 + n)
            else:
                d = -25 if r.random() < 0.09 else 0
                c = (134 + n + d, 96 + n + d, 67 + n + d)
            row.append(tuple(max(0, min(255, v)) for v in c))
        tex.append(row)
    return tex

TOP, SIDE = texture(1, 'top'), texture(2, 'side')

def render(size):
    """Isometric cube filling the square; returns rows of RGBA tuples."""
    s = size
    A = (s / 2, s * 0.03); R = (s * 0.97, s * 0.27); C = (s / 2, s * 0.5); L = (s * 0.03, s * 0.27)
    Lb = (s * 0.03, s * 0.75); Cb = (s / 2, s * 0.98); Rb = (s * 0.97, s * 0.75)
    faces = [  # origin, u-axis end, v-axis end, texture, shade
        (A, R, L, TOP, 1.0),
        (L, C, Lb, SIDE, 0.78),
        (C, R, Cb, SIDE, 0.6),
    ]
    img = []
    for py in range(s):
        row = []
        for px in range(s):
            x, y = px + 0.5, py + 0.5
            col = (0, 0, 0, 0)
            for (o, u, v, tex, shade) in faces:
                ux, uy = u[0] - o[0], u[1] - o[1]
                vx, vy = v[0] - o[0], v[1] - o[1]
                det = ux * vy - uy * vx
                a = ((x - o[0]) * vy - (y - o[1]) * vx) / det
                b = (ux * (y - o[1]) - uy * (x - o[0])) / det
                if 0 <= a < 1 and 0 <= b < 1:
                    t = tex[int(b * 16)][int(a * 16)]
                    col = (int(t[0] * shade), int(t[1] * shade), int(t[2] * shade), 255)
                    break
            row.append(col)
        img.append(row)
    return img

def png(img):
    h, w = len(img), len(img[0])
    raw = b''.join(b'\x00' + bytes(v for p in row for v in p) for row in img)
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))

def ico(path, sizes):
    images = [png(render(s)) for s in sizes]
    out = struct.pack('<HHH', 0, 1, len(images))
    offset = 6 + 16 * len(images)
    for s, data in zip(sizes, images):
        out += struct.pack('<BBBBHHII', s % 256, s % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(path, 'wb') as f:
        f.write(out + b''.join(images))

if __name__ == '__main__':
    here = os.path.dirname(os.path.abspath(__file__))
    ico(os.path.join(here, 'icon.ico'), [16, 24, 32, 48, 64, 256])
    with open(os.path.join(here, 'icon_preview.png'), 'wb') as f:
        f.write(png(render(128)))
    print('wrote icon.ico')
