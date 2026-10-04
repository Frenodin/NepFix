"""Иллюстрации для меню NepFix: небольшая сцена, отрисованная трассировкой лучей.
Все буферы считаются один раз, варианты параметров только смешивают их."""
import numpy as np, struct, zlib, sys, os

W, H, SS = 200, 125, 2
RW, RH = W * SS, H * SS
rng = np.random.default_rng(7)
EPS = 1e-4

def norm(v):
    return v / np.linalg.norm(v, axis=-1, keepdims=True)

# ---------- геометрия ----------
# прямоугольники: (ось нормали, координата, знак нормали, границы по двум другим осям, альбедо)
RECTS = [
    (1, 0.0, +1, ((-2.2, 2.2), (-1.5, 3.2)), (0.55, 0.52, 0.48)),   # пол (x, z)
    (2, 3.2, -1, ((-2.2, 2.2), (0.0, 2.6)), (0.62, 0.62, 0.66)),    # задняя стена (x, y)
    (0, -2.2, +1, ((0.0, 2.6), (-1.5, 3.2)), (0.82, 0.14, 0.10)),   # левая стена красная (y, z)
]
SPHERES = [((-0.9, 0.55, 1.9), 0.55, (0.8, 0.8, 0.8))]
BOXES = [
    ((0.35, 0.0, 1.25), (1.25, 0.95, 2.15), (0.22, 0.42, 0.85)),
    ((-0.2, 0.0, 0.55), (0.15, 0.32, 0.9), (0.92, 0.74, 0.18)),
]
SUN = norm(np.array([0.55, 1.0, -0.55]))
SUN_COL = np.array([1.0, 0.96, 0.9]) * 1.6
SKY_COL = np.array([0.55, 0.68, 0.9])
LAMP = np.array([1.7, 1.55, 0.35]); LAMP_COL = np.array([1.0, 0.72, 0.4]) * 5.0


def intersect(o, d, tmax=1e9):
    n = o.shape[0]
    T = np.full(n, tmax); Nn = np.zeros((n, 3)); A = np.zeros((n, 3)); ID = np.full(n, -1)
    oid = 0
    for ax, c, sg, bounds, alb in RECTS:
        dd = d[:, ax]
        with np.errstate(divide='ignore', invalid='ignore'):
            t = (c - o[:, ax]) / dd
        others = [i for i in range(3) if i != ax]
        p = o + d * t[:, None]
        ok = (t > EPS) & (t < T) & (dd * sg < 0)
        for k, (lo, hi) in zip(others, bounds):
            ok &= (p[:, k] >= lo) & (p[:, k] <= hi)
        T[ok] = t[ok]; nv = np.zeros(3); nv[ax] = sg; Nn[ok] = nv; A[ok] = alb; ID[ok] = oid
        oid += 1
    for cen, r, alb in SPHERES:
        cen = np.array(cen); oc = o - cen
        b = np.sum(oc * d, 1); cc = np.sum(oc * oc, 1) - r * r
        disc = b * b - cc
        ok = disc > 0
        sq = np.sqrt(np.maximum(disc, 0))
        t = -b - sq
        t = np.where(t > EPS, t, -b + sq)
        ok &= (t > EPS) & (t < T)
        T[ok] = t[ok]; p = o[ok] + d[ok] * t[ok, None]
        Nn[ok] = (p - cen) / r; A[ok] = alb; ID[ok] = oid
        oid += 1
    for lo, hi, alb in BOXES:
        lo = np.array(lo); hi = np.array(hi)
        with np.errstate(divide='ignore', invalid='ignore'):
            inv = 1.0 / d
            t1 = (lo - o) * inv; t2 = (hi - o) * inv
        tmin = np.nanmax(np.minimum(t1, t2), 1); tmx = np.nanmin(np.maximum(t1, t2), 1)
        ok = (tmx >= tmin) & (tmin > EPS) & (tmin < T)
        T[ok] = tmin[ok]
        p = o[ok] + d[ok] * tmin[ok, None]
        c = (lo + hi) / 2; hs = (hi - lo) / 2
        q = (p - c) / hs
        ax = np.argmax(np.abs(q), 1)
        nv = np.zeros((ok.sum(), 3)); nv[np.arange(len(ax)), ax] = np.sign(q[np.arange(len(ax)), ax])
        Nn[ok] = nv; A[ok] = alb; ID[ok] = oid
        oid += 1
    hit = T < tmax
    return T, Nn, A, hit, ID


def occluded(p, dirs, maxd):
    T, _, _, hit, _ = intersect(p, dirs, maxd)
    return hit


# ---------- камера ----------
cam = np.array([0.15, 1.55, -2.3]); look = np.array([-0.1, 0.55, 1.7])
fw = norm(look - cam); rt = norm(np.cross(np.array([0, 1, 0]), fw)); up = np.cross(fw, rt)
fov = np.tan(np.radians(48) / 2)
ys, xs = np.mgrid[0:RH, 0:RW]
u = ((xs + 0.5) / RW * 2 - 1) * fov * RW / RH
v = (1 - (ys + 0.5) / RH * 2) * fov
D = norm(u[..., None] * rt + v[..., None] * up + fw).reshape(-1, 3)
O = np.repeat(cam[None], D.shape[0], 0)
T, Nn, A, HIT, ID = intersect(O, D)
P = O + D * T[:, None]
NPX = D.shape[0]
print("primary", HIT.mean())

sky = np.clip(SKY_COL * (0.7 + 0.5 * D[:, 1:2]), 0, 1)

def sun_vis(bias):
    vis = np.zeros(NPX)
    m = HIT & (Nn @ SUN > 0)
    vis[m] = ~occluded(P[m] + Nn[m] * 0.002 + SUN * bias, np.repeat(SUN[None], m.sum(), 0), 50)
    return vis

vis_sun = sun_vis(0.0)
vis_sun_far = sun_vis(0.45)       # как в карте теней без контактных: пропускает тень у самого основания
ndl = np.clip(Nn @ SUN, 0, 1)

ld = LAMP - P; ldist = np.linalg.norm(ld, axis=1); ldn = ld / ldist[:, None]
lndl = np.clip(np.sum(Nn * ldn, 1), 0, 1)
lamp_att = 1.0 / (1 + ldist ** 2 * 0.9)
vis_lamp = np.zeros(NPX); m = HIT & (lndl > 0)
vis_lamp[m] = ~occluded(P[m] + Nn[m] * 0.002, ldn[m], 1) if False else 0
# отдельный тест с ограничением по расстоянию до лампы
Tl, _, _, hl, _ = intersect(P[m] + Nn[m] * 0.002, ldn[m], 1e9)
vis_lamp[m] = ~(hl & (Tl < ldist[m] - 0.01))

# ---------- лучи для AO и GI ----------
K = 24
def cos_dirs(n, k):
    u1 = rng.random((n.shape[0], k)); u2 = rng.random((n.shape[0], k))
    r = np.sqrt(u1); phi = 2 * np.pi * u2
    a = np.where(np.abs(n[:, 1:2]) < 0.99, np.array([[0, 1, 0]]), np.array([[1, 0, 0]]))
    t = norm(np.cross(n, a)); b = np.cross(n, t)
    return (t[:, None] * (r * np.cos(phi))[..., None] + b[:, None] * (r * np.sin(phi))[..., None]
            + n[:, None] * np.sqrt(1 - u1)[..., None])

idx = np.where(HIT)[0]
rt_t = np.full((NPX, K), 1e9); rt_rad = np.zeros((NPX, K, 3))
CH = 20000
for s in range(0, len(idx), CH):
    ii = idx[s:s + CH]
    dirs = cos_dirs(Nn[ii], K).reshape(-1, 3)
    orig = np.repeat(P[ii] + Nn[ii] * 0.003, K, 0)
    t, n2, a2, h2, _ = intersect(orig, dirs, 6)
    ph = orig + dirs * t[:, None]
    rad = np.zeros((len(t), 3))
    hm = h2 & (n2 @ SUN > 0)
    vv = ~occluded(ph[hm] + n2[hm] * 0.002, np.repeat(SUN[None], hm.sum(), 0), 50)
    rad[hm] = a2[hm] * SUN_COL * (n2[hm] @ SUN)[:, None] * vv[:, None]
    rad[h2] += a2[h2] * SKY_COL * 0.25
    rad[~h2] = 0
    rt_t[ii] = np.where(h2, t, 1e9).reshape(-1, K)
    rt_rad[ii] = rad.reshape(-1, K, 3)
    print("rays", s, len(idx))

# отражение пола
refl = np.zeros((NPX, 3))
fm = HIT & (ID == 0)
rd = D[fm] - 2 * np.sum(D[fm] * Nn[fm], 1)[:, None] * Nn[fm]
t, n2, a2, h2, _ = intersect(P[fm] + Nn[fm] * 0.002, rd, 50)
ph = P[fm] + rd * t[:, None]
rc = np.repeat(np.clip(SKY_COL * 1.1, 0, 1)[None], len(t), 0)
hm = h2.copy()
sv = np.zeros(len(t)); sm = h2 & (n2 @ SUN > 0)
sv[sm] = ~occluded(ph[sm] + n2[sm] * 0.002, np.repeat(SUN[None], sm.sum(), 0), 50)
rc[h2] = a2[h2] * (SUN_COL * np.clip(n2[h2] @ SUN, 0, 1)[:, None] * sv[h2, None] + SKY_COL * 0.45)
refl[fm] = rc


def ao_term(radius, k=K):
    t = rt_t[:, :k]
    occ = np.where(t < radius, 1 - t / radius, 0).mean(1)
    return 1 - occ


def gi_term(radius, k=K):
    t = rt_t[:, :k, None]
    return np.where(t < radius, rt_rad[:, :k], 0).mean(1)


def blur(img, r=1):
    im = img.reshape(RH, RW, -1)
    out = np.zeros_like(im); c = 0
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            out += np.roll(np.roll(im, dy, 0), dx, 1); c += 1
    return (out / c).reshape(img.shape)


def compose(amb=0.45, ssao=0.0, ssao_r=0.5, ssao_lq=False, lamp=True, lamp_sh=False, sun_sh_contact=True,
            refl_k=0.0, fx=False, gi_i=1.0, gi_r=2.0, ao_i=0.6, cl=0.35, ci=0.7, rays=24, noisy=False,
            amb_add=0.0, dbg=0, split=False, sun_k=1.0, refl_blur=False):
    alb = A
    sun_v = vis_sun if (fx and cl > 0.02 and ci > 0) else vis_sun_far
    if fx and cl > 0.02:
        # контактная тень частично: чем сильнее, тем ближе к точной
        sun_v = vis_sun_far + (vis_sun - vis_sun_far) * np.clip(ci, 0, 1) * np.clip(cl / 0.45, 0, 1)
    direct = alb * SUN_COL * sun_k * (ndl * sun_v)[:, None]
    if lamp:
        lv = vis_lamp if lamp_sh else 1.0
        direct += alb * LAMP_COL * (lndl * lamp_att * lv)[:, None]
    ambient = alb * SKY_COL * amb + alb * amb_add * 0.8
    if ssao > 0:
        a = ao_term(ssao_r, 6 if ssao_lq else K)
        if ssao_lq: a = blur(a[:, None], 2)[:, 0]
        ambient *= (1 - ssao * (1 - a))[:, None]
    col = direct + ambient
    gi = np.zeros((NPX, 3)); ao = np.ones(NPX)
    if fx:
        k = rays
        gi = gi_term(gi_r, k); ao = ao_term(min(gi_r, 1.2), k)
        if not noisy:
            gi = blur(gi, 1); ao = blur(ao[:, None], 1)[:, 0]
        ao_f = 1 - ao_i * (1 - ao)
        col = col * np.clip(ao_f, 0, 1)[:, None] + gi * alb * gi_i * 1.4
    if refl_k > 0:
        fr = 0.12 + 0.5 * (1 - np.abs(np.sum(D * Nn, 1))) ** 3
        m = ID == 0
        rs = blur(refl, 4) if refl_blur else refl
        col[m] = col[m] * (1 - refl_k * fr[m, None] * 0.6) + rs[m] * refl_k * fr[m, None]
    col = np.where(HIT[:, None], col, sky)
    if dbg == 1: col = np.where(HIT[:, None], gi * gi_i, [0, 0, 0.35])
    if dbg == 2: col = np.where(HIT[:, None], np.repeat(ao[:, None], 3, 1), [0, 0, 0.35])
    if dbg == 3: col = np.where(HIT[:, None], np.repeat((1 - (vis_sun_far - vis_sun) * ci)[:, None], 3, 1), [0, 0, 0.35])
    if dbg == 4: col = np.where(HIT[:, None], Nn * 0.5 + 0.5, [0, 0, 0.35])
    if dbg == 5: col = col * 0.5 + np.array([1, 0, 0]) * 0.5
    if dbg == 6: col = np.where(HIT[:, None], np.repeat((1 - np.clip(T / 8, 0, 1))[:, None], 3, 1), [0, 0, 0.35])
    return col


def tonemap(c, raw=False):
    if not raw:
        c = c * 1.0
        c = c / (1 + c * 0.6) * 1.15
        c = np.clip(c, 0, 1) ** (1 / 2.2)
    else:
        c = np.clip(c, 0, 1)
    im = c.reshape(RH, RW, 3)
    im = im.reshape(H, SS, W, SS, 3).mean((1, 3))
    return (np.clip(im, 0, 1) * 255 + 0.5).astype(np.uint8)


def pair(a, b, raw_b=False):
    ia = tonemap(a); ib = tonemap(b, raw_b)
    gap = np.full((H, 4, 3), 20, np.uint8)
    return np.concatenate([ia, gap, ib], 1)


def split_img(a, b):
    ia = tonemap(a); ib = tonemap(b)
    im = ib.copy(); im[:, :W // 2] = ia[:, :W // 2]; im[:, W // 2 - 1:W // 2 + 1] = 255
    return im

NOFX = dict()
FX = dict(fx=True)
P_ = {}
P_["ForceLightShadows"] = pair(compose(lamp_sh=False, sun_k=0.12, amb=0.2), compose(lamp_sh=True, sun_k=0.12, amb=0.2))
P_["MaxShadowedLights"] = P_["ForceLightShadows"]
P_["AmbientMul"] = pair(compose(amb=0.2), compose(amb=0.8))
P_["CharAmbient"] = pair(compose(amb=0.05, sun_k=0.8), compose(amb=0.7, sun_k=0.8))
P_["AmbientAdd"] = pair(compose(amb=0.0), compose(amb=0.0, amb_add=0.45))
P_["ReflectionMul"] = pair(compose(refl_k=0.0), compose(refl_k=1.6))
P_["SsaoIntensityMul"] = pair(compose(amb=0.9, sun_k=0.35, ssao=0.0), compose(amb=0.9, sun_k=0.35, ssao=1.0, ssao_r=0.6))
P_["Ssao"] = P_["SsaoIntensityMul"]
P_["SsaoRadiusMul"] = pair(compose(amb=0.9, sun_k=0.35, ssao=1.0, ssao_r=0.15), compose(amb=0.9, sun_k=0.35, ssao=1.0, ssao_r=0.9))
P_["SsaoHighQuality"] = pair(compose(amb=0.9, sun_k=0.35, ssao=1.0, ssao_r=0.6, ssao_lq=True), compose(amb=0.9, sun_k=0.35, ssao=1.0, ssao_r=0.6))
P_["FxEnabled"] = pair(compose(), compose(**FX))
P_["FxGiIntensity"] = pair(compose(fx=True, gi_i=0.0), compose(fx=True, gi_i=2.2))
P_["FxGiRadius"] = pair(compose(fx=True, gi_r=0.4), compose(fx=True, gi_r=3.0))
P_["FxAoIntensity"] = pair(compose(fx=True, ao_i=0.0, sun_k=0.4, amb=0.9), compose(fx=True, ao_i=1.2, sun_k=0.4, amb=0.9))
P_["FxContactLength"] = pair(compose(fx=True, cl=0.0), compose(fx=True, cl=0.5))
P_["FxIndoorShadows"] = pair(compose(sun_k=0.0, amb=1.0), compose(sun_k=0.0, amb=1.0, fx=True, ao_i=1.5, gi_i=0.3))
P_["FxContactIntensity"] = pair(compose(fx=True, ci=0.15), compose(fx=True, ci=1.0))
P_["FxTemporal"] = pair(compose(fx=True, rays=2, noisy=True), compose(fx=True, rays=16))
P_["FxSsr"] = pair(compose(), compose(refl_k=1.3))
P_["FxSsrIntensity"] = pair(compose(refl_k=0.4), compose(refl_k=2.2))
P_["FxSsrDistance"] = P_["FxSsr"]
P_["FxSsrFloorsOnly"] = P_["FxSsr"]
P_["FxSsrBlur"] = pair(compose(refl_k=1.3), compose(refl_k=1.3, refl_blur=True))
P_["FxRays"] = pair(compose(fx=True, rays=1), compose(fx=True, rays=8))
for d in range(1, 7):
    P_[f"FxDebug{d}"] = pair(compose(fx=True), compose(fx=True, dbg=d), raw_b=d in (1, 2, 3, 4, 6))
P_["FxSplit"] = np.concatenate([split_img(compose(), compose(fx=True)), np.full((H, 4, 3), 20, np.uint8),
                                tonemap(compose(fx=True))], 1)

out = sys.argv[1] if len(sys.argv) > 1 else "previews.bin"
blob = bytearray(struct.pack("<i", len(P_)))
for k, im in P_.items():
    h, w, _ = im.shape
    rgba = np.concatenate([im, np.full((h, w, 1), 255, np.uint8)], 2)[::-1].tobytes()  # Unity: снизу вверх
    z = zlib.compress(rgba, 9)
    kb = k.encode()
    blob += struct.pack("<i", len(kb)) + kb + struct.pack("<iii", w, h, len(z)) + z
open(out, "wb").write(blob)
print("written", out, len(blob))

# контрольный лист для просмотра
from PIL import Image, ImageDraw
rows = list(P_.items())
sheet = Image.new("RGB", (408, 140 * len(rows)), (30, 30, 30))
dr = ImageDraw.Draw(sheet)
for i, (k, im) in enumerate(rows):
    sheet.paste(Image.fromarray(im), (2, 140 * i + 13)); dr.text((4, 140 * i), k, fill=(255, 255, 0))
sheet.save(os.path.splitext(out)[0] + "_sheet.png")
