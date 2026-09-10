import struct, zlib, sys, io, os

M = 0xFFFFFFFF

class SwzRandom:
    """WELL512-style PRNG, port of BrawlhallaSwz/src/SwzRandom.cs"""
    __slots__ = ("s", "i")
    def __init__(self, seed):
        s = [0]*16
        s[0] = seed & M
        for i in range(1, 16):
            p = s[i-1]
            s[i] = (i + 0x6C078965 * (p ^ (p >> 30))) & M
        self.s = s; self.i = 0
    def next(self):
        s = self.s; idx = self.i
        a = s[idx]
        b = s[(idx+13) % 16]
        c = (a ^ (a << 16) ^ b ^ (b << 15)) & M
        b = s[(idx+9) % 16]
        b ^= b >> 11
        s[idx] = (b ^ c) & M
        a = s[idx]
        d = (a ^ ((a << 5) & 0xDA442D24)) & M
        idx = (idx+15) % 16
        a = s[idx]
        s[idx] = (a ^ (a << 2) ^ (b << 28) ^ c ^ (c << 18) ^ d) & M
        self.i = idx
        return s[idx]

def rotr(x, n):
    n &= 31
    return ((x >> n) | (x << (32-n))) & M

def check_key(data, key):
    exp = struct.unpack_from(">I", data, 0)[0]
    seed = struct.unpack_from(">I", data, 4)[0] ^ (key & M)
    r = SwzRandom(seed)
    cs = 0x2DF4A1CD
    for _ in range((key & M) % 31 + 5):
        cs ^= r.next()
    return cs == exp, r

def read_swz(path, key):
    data = open(path, "rb").read()
    ok, r = check_key(data, key)
    if not ok:
        raise ValueError("key checksum mismatch")
    off = 8
    out = []
    while len(data) - off > 12:
        csize = struct.unpack_from(">I", data, off)[0] ^ r.next(); off += 4
        usize = struct.unpack_from(">I", data, off)[0] ^ r.next(); off += 4
        exp   = struct.unpack_from(">I", data, off)[0];             off += 4
        buf = bytearray(csize)
        cs = r.next()
        for i in range(csize):
            rnd = r.next()
            bi = i & 0xF
            nb = (data[off+i] ^ (((0xFF << bi) & rnd) >> bi)) & 0xFF
            buf[i] = nb
            cs = (nb ^ rotr(cs, (i % 7) + 1)) & M
        off += csize
        if cs != exp:
            raise ValueError("data checksum mismatch at entry %d" % len(out))
        raw = zlib.decompress(bytes(buf))
        assert len(raw) == usize, (len(raw), usize)
        out.append(raw)
    return out

# ---- SWF -> ABC uint constant pool ----
def swf_body(path):
    d = open(path, "rb").read()
    sig = d[:3]
    if sig == b"FWS": return d[8:]
    if sig == b"CWS": return zlib.decompress(d[8:])
    if sig == b"ZWS":
        import lzma
        return lzma.decompress(d[12:] if False else d[8:])
    raise ValueError(sig)

def swf_tags(body):
    # skip RECT
    nbits = body[0] >> 3
    total = 5 + 4*nbits
    off = (total + 7)//8
    off += 4  # framerate(2) + framecount(2)
    while off < len(body):
        (th,) = struct.unpack_from("<H", body, off); off += 2
        code = th >> 6; ln = th & 0x3F
        if ln == 0x3F:
            (ln,) = struct.unpack_from("<I", body, off); off += 4
        yield code, body[off:off+ln]
        off += ln

def u30(b, o):
    r = 0; sh = 0
    while True:
        c = b[o]; o += 1
        r |= (c & 0x7F) << sh
        if not (c & 0x80) or sh >= 28: break
        sh += 7
    return r & 0xFFFFFFFF, o

def abc_uints(abc):
    o = 4  # minor u16, major u16
    n, o = u30(abc, o)          # int_count
    for _ in range(max(0, n-1)): _, o = u30(abc, o)
    n, o = u30(abc, o)          # uint_count
    uints = []
    for _ in range(max(0, n-1)):
        v, o = u30(abc, o); uints.append(v)
    return uints

def find_key(swf_path, swz_path):
    body = swf_body(swf_path)
    cands = []
    for code, tag in swf_tags(body):
        if code == 72:
            cands.append(tag)
        elif code == 82:
            z = tag.index(b"\x00", 4)
            cands.append(tag[z+1:])
    data = open(swz_path, "rb").read()
    for abc in cands:
        try: uints = abc_uints(abc)
        except Exception: continue
        for u in uints:
            if check_key(data, u)[0]:
                return u, len(uints)
    return None, sum(len(abc_uints(a)) for a in cands)

if __name__ == "__main__":
    G = r"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla"
    key, ncand = find_key(os.path.join(G, "BrawlhallaAir.swf"), os.path.join(G, "Dynamic.swz"))
    print("candidates scanned:", ncand)
    print("KEY =", key)
