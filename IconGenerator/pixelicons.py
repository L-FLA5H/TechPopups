# yes, this is vibe coded, stfu

from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Iterable, Optional, Sequence, Tuple, List

import numpy as np
from PIL import Image


# ---------------------------------------------------------------------------
# Color helpers
# ---------------------------------------------------------------------------

RGBA = Tuple[int, int, int, int]
ColorLike = "str | tuple"


def parse_color(c) -> RGBA:
    if c is None:
        return (0, 0, 0, 0)
    if isinstance(c, (tuple, list)):
        if len(c) == 3:
            return (int(c[0]), int(c[1]), int(c[2]), 255)
        if len(c) == 4:
            return (int(c[0]), int(c[1]), int(c[2]), int(c[3]))
        raise ValueError(f"bad color tuple {c}")
    s = c.strip().lstrip("#")
    if len(s) == 6:
        return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), 255)
    if len(s) == 8:
        return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), int(s[6:8], 16))
    if len(s) == 3:
        return (int(s[0] * 2, 16), int(s[1] * 2, 16), int(s[2] * 2, 16), 255)
    raise ValueError(f"bad color string {c!r}")


def shade(color, amount: float) -> RGBA:
    r, g, b, a = parse_color(color)
    if amount >= 0:
        r = r + (255 - r) * amount
        g = g + (255 - g) * amount
        b = b + (255 - b) * amount
    else:
        f = 1 + amount
        r, g, b = r * f, g * f, b * f
    return (int(round(r)), int(round(g)), int(round(b)), a)


def mix(c1, c2, t: float) -> RGBA:
    a = parse_color(c1)
    b = parse_color(c2)
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(4))

class PixelGrid:
    """Low-resolution drawing surface.

    Coordinates are expressed in art pixels. Shapes are rasterized by
    testing whether each pixel center falls inside the shape.
    """
    
    @staticmethod
    def dilate_mask(mask: np.ndarray, radius: int = 1) -> np.ndarray:
        out = mask.copy()

        for dy in range(-radius, radius + 1):
            for dx in range(-radius, radius + 1):
                if dx == 0 and dy == 0:
                    continue

                shifted = np.zeros_like(mask)

                sy0 = max(0, -dy)
                sy1 = mask.shape[0] - max(0, dy)

                sx0 = max(0, -dx)
                sx1 = mask.shape[1] - max(0, dx)

                shifted[
                    sy0 + dy:sy1 + dy,
                    sx0 + dx:sx1 + dx
                ] = mask[sy0:sy1, sx0:sx1]

                out |= shifted

        return out

    def __init__(self, size: int):
        self.size = size
        # RGBA float buffer for alpha compositing
        self.buf = np.zeros((size, size, 4), dtype=np.float32)

    # ---- low level ---- #
    def _blend(self, mask: np.ndarray, color: RGBA):
        """Alpha-composite `color` onto buf wherever mask is True."""
        r, g, b, a = color
        if a == 0:
            return
        ca = a / 255.0
        src = np.array([r, g, b], dtype=np.float32)
        idx = mask
        dst_rgb = self.buf[..., :3]
        dst_a = self.buf[..., 3:4]
        # standard "over" compositing
        out_a = ca + (dst_a[..., 0] / 255.0) * (1 - ca)
        # avoid div by zero
        safe = np.where(out_a > 0, out_a, 1.0)
        for ch in range(3):
            dst_rgb[..., ch] = np.where(
                idx,
                (src[ch] * ca + dst_rgb[..., ch] * (dst_a[..., 0] / 255.0) * (1 - ca)) / safe,
                dst_rgb[..., ch],
            )
        self.buf[..., 3] = np.where(idx, out_a * 255.0, self.buf[..., 3])

    def _coords(self):
        ys, xs = np.mgrid[0:self.size, 0:self.size]
        # sample at cell centers
        return xs + 0.5, ys + 0.5

    # primitives
    def fill_disc(self, cx, cy, r, color):
        X, Y = self._coords()
        mask = (X - cx) ** 2 + (Y - cy) ** 2 <= r * r
        self._blend(mask, parse_color(color))

    def fill_ring(self, cx, cy, r_outer, r_inner, color):
        X, Y = self._coords()
        d2 = (X - cx) ** 2 + (Y - cy) ** 2
        mask = (d2 <= r_outer * r_outer) & (d2 >= r_inner * r_inner)
        self._blend(mask, parse_color(color))

    def fill_arc_ring(self, cx, cy, r_outer, r_inner, a0, a1, color):
        """Ring restricted to angles [a0, a1] (radians, math convention)."""
        X, Y = self._coords()
        dx = X - cx
        dy = Y - cy
        d2 = dx * dx + dy * dy
        ang = np.arctan2(-dy, dx)  # screen y is down; flip so angles are math-like
        ang = np.mod(ang - a0, 2 * math.pi)
        span = (a1 - a0) % (2 * math.pi)
        mask = (d2 <= r_outer * r_outer) & (d2 >= r_inner * r_inner) & (ang <= span)
        self._blend(mask, parse_color(color))

    def fill_poly(self, points: Sequence[Tuple[float, float]], color):
        X, Y = self._coords()
        mask = _point_in_poly(X, Y, points)
        self._blend(mask, parse_color(color))

    def fill_rect(self, x0, y0, x1, y1, color):
        X, Y = self._coords()
        mask = (X >= min(x0, x1)) & (X <= max(x0, x1)) & (Y >= min(y0, y1)) & (Y <= max(y0, y1))
        self._blend(mask, parse_color(color))

    def fill_capsule(self, p0, p1, radius, color):
        """A thick line segment with rounded ends (distance to segment)."""
        X, Y = self._coords()
        mask = _dist_to_segment(X, Y, p0, p1) <= radius
        self._blend(mask, parse_color(color))

    def fill_mask(self, mask: np.ndarray, color):
        self._blend(mask, parse_color(color))

    def edge_of(self, color_mask_fn, base_color, edge_color, thickness=1):
        """Generic helper unused publicly; kept for extension."""
        raise NotImplementedError


def _point_in_poly(X, Y, poly) -> np.ndarray:
    """Vectorized even-odd point-in-polygon test."""
    n = len(poly)
    inside = np.zeros(X.shape, dtype=bool)
    j = n - 1
    for i in range(n):
        xi, yi = poly[i]
        xj, yj = poly[j]
        cond = ((yi > Y) != (yj > Y)) & (
            X < (xj - xi) * (Y - yi) / ((yj - yi) + 1e-12) + xi
        )
        inside ^= cond
        j = i
    return inside


def _dist_to_segment(X, Y, p0, p1) -> np.ndarray:
    x0, y0 = p0
    x1, y1 = p1
    dx, dy = x1 - x0, y1 - y0
    L2 = dx * dx + dy * dy
    if L2 == 0:
        return np.sqrt((X - x0) ** 2 + (Y - y0) ** 2)
    t = ((X - x0) * dx + (Y - y0) * dy) / L2
    t = np.clip(t, 0, 1)
    px = x0 + t * dx
    py = y0 + t * dy
    return np.sqrt((X - px) ** 2 + (Y - py) ** 2)


def rotate_pt(px, py, cx, cy, deg):
    a = math.radians(deg)
    s, c = math.sin(a), math.cos(a)
    dx, dy = px - cx, py - cy
    return (cx + dx * c - dy * s, cy + dx * s + dy * c)


# --------------------------------------------------------------------------- #
# Shapes
# --------------------------------------------------------------------------- #

@dataclass
class Shape:
    """Base shape. offset is a fraction of the image size.
    for example, (0,0) = centered. (0.1, -0.2) = 10% right, 20% up."""
    offset: Tuple[float, float] = (0.0, 0.0)

    def origin(self, grid: PixelGrid) -> Tuple[float, float]:
        cx = grid.size * (0.5 + self.offset[0])
        cy = grid.size * (0.5 - self.offset[1])
        return cx, cy

    def draw(self, grid: PixelGrid):
        raise NotImplementedError


@dataclass
class Circle(Shape):
    """The round badge background with outline + bevel shading."""
    radius_frac: float = 0.46          # radius as fraction of image size
    fill: ColorLike = "#3fe4f0"
    outline: ColorLike = "#3a4250"
    outline_width: float = 1.6         # in art pixels
    bevel: float = 0.22                # 0 = flat, higher = stronger top-left highlight
    inner_shadow: float = 0.18         # darken bottom-right inner edge

    def draw(self, grid: PixelGrid):
        cx, cy = self.origin(grid)
        R = grid.size * self.radius_frac
        ow = self.outline_width
        # outline ring (slightly darker than itself at bottom for depth handled by fill)
        grid.fill_disc(cx, cy, R, self.outline)
        # inner fill
        rin = R - ow
        grid.fill_disc(cx, cy, rin, self.fill)
        if self.bevel > 0 or self.inner_shadow > 0:
            self._apply_bevel(grid, cx, cy, rin)

    def _apply_bevel(self, grid, cx, cy, rin):
        X, Y = grid._coords()
        d = np.sqrt((X - cx) ** 2 + (Y - cy) ** 2)
        inside = d <= rin
        # direction vector from center, normalized
        nx = (X - cx) / (d + 1e-6)
        ny = (Y - cy) / (d + 1e-6)
        # light from top-left => (-0.707, -0.707)
        light = -(nx * -0.707 + ny * -0.707)  # positive toward bottom-right
        edge = np.clip((d - rin * 0.55) / (rin * 0.45), 0, 1)  # only near edge
        # highlight top-left
        hl = inside & (light < 0) & (edge > 0)
        if self.bevel > 0:
            amt = (-light) * edge * self.bevel
            self._tint(grid, hl, amt)
        # shadow bottom-right
        sh = inside & (light > 0) & (edge > 0)
        if self.inner_shadow > 0:
            amt = (light) * edge * self.inner_shadow
            self._tint(grid, sh, -amt)

    @staticmethod
    def _tint(grid, mask, amount_arr):
        """Brighten(+)/darken(-) existing buffer pixels by per-pixel amount."""
        rgb = grid.buf[..., :3]
        amt = np.where(mask, amount_arr, 0.0)[..., None]
        pos = amt > 0
        rgb_new = np.where(pos, rgb + (255 - rgb) * amt, rgb * (1 + amt))
        grid.buf[..., :3] = np.clip(rgb_new, 0, 255)


def _arrow_polygon(length, shaft_w, head_w, head_len):
    """Return arrow polygon points pointing +x (right), centered at origin.
    length = total length, shaft_w = shaft thickness, head_w = full head width,
    head_len = length of the triangular head."""
    half_s = shaft_w / 2
    half_h = head_w / 2
    x0 = -length / 2
    x_head = length / 2 - head_len
    x_tip = length / 2
    return [
        (x0, -half_s),
        (x_head, -half_s),
        (x_head, -half_h),
        (x_tip, 0),
        (x_head, half_h),
        (x_head, half_s),
        (x0, half_s),
    ]


@dataclass
class Arrow(Shape):
    """A chunky straight arrow. Rotation in degrees (0 = pointing right,
    angles increase clockwise on screen so 45 = down-right like the source)."""
    angle: float = 0.0
    length_frac: float = 0.62
    width_frac: float = 0.20           # shaft thickness as fraction of image
    head_width_frac: float = 0.44      # full arrowhead width
    head_len_frac: float = 0.26
    fill: ColorLike = "#ffcf1f"
    edge: Optional[ColorLike] = None   # outline color; None => auto-darkened fill
    edge_width: float = 1.0            # art pixels
    shading: float = 0.22              # darken bottom-right half for 3D feel

    def _points(self, grid):
        cx, cy = self.origin(grid)
        S = grid.size
        poly = _arrow_polygon(
            S * self.length_frac, S * self.width_frac,
            S * self.head_width_frac, S * self.head_len_frac,
        )
        # rotate (screen: clockwise positive) and translate
        out = []
        a = math.radians(self.angle)
        s, c = math.sin(a), math.cos(a)
        for px, py in poly:
            rx = px * c - py * s
            ry = px * s + py * c
            out.append((cx + rx, cy + ry))
        return out, (cx, cy)

    def draw(self, grid: PixelGrid):
        pts, (cx, cy) = self._points(grid)
        edge_col = self.edge if self.edge is not None else shade(self.fill, -0.45)
        # 1) draw edge by drawing a slightly enlarged poly first
        if self.edge_width > 0:
            X, Y = grid._coords()
            body = _point_in_poly(X, Y, pts)
            outline = PixelGrid.dilate_mask(body, max(1, int(round(self.edge_width))))
            grid.fill_mask(outline, edge_col)
        # 2) fill body
        grid.fill_poly(pts, self.fill)
        # 3) shading: darken the lower-right half along arrow's perpendicular
        if self.shading > 0:
            self._shade(grid, pts, cx, cy)

    def _shade(self, grid, pts, cx, cy):
        X, Y = grid._coords()
        mask = _point_in_poly(X, Y, pts)
        # perpendicular axis = arrow direction rotated 90deg
        a = math.radians(self.angle + 90)
        ux, uy = math.cos(a), math.sin(a)
        proj = (X - cx) * ux + (Y - cy) * uy
        lower = mask & (proj > 0)
        rgb = grid.buf[..., :3]
        m = lower[..., None]
        grid.buf[..., :3] = np.where(m, np.clip(rgb * (1 - self.shading), 0, 255), rgb)
        # subtle highlight on the upper edge
        upper = mask & (proj < -grid.size * 0.04)
        m2 = upper[..., None]
        grid.buf[..., :3] = np.where(m2, np.clip(rgb + (255 - rgb) * (self.shading * 0.5), 0, 255), grid.buf[..., :3])


def _expand_poly(pts, center, amount):
    """Push each vertex outward from the polygon centroid by `amount`."""
    cx, cy = center
    out = []
    for x, y in pts:
        dx, dy = x - cx, y - cy
        d = math.hypot(dx, dy) + 1e-6
        out.append((x + dx / d * amount, y + dy / d * amount))
    return out


@dataclass
class CurvedArrow(Shape):
    """An arrow that bends — used for wallbounce / bunnyhop style icons where
    the arrow visibly curves. Defined by a poly-line of waypoints (in relative
    -1..1 space, where (0,0) is the shape origin and +y is up). A chunky body is
    drawn along the path, with an arrowhead at the final segment.
    """

    points: Sequence[Tuple[float, float]] = field(
        default_factory=lambda: [(-0.4, -0.3), (0.0, -0.3), (0.0, 0.35)]
    )
    width_frac: float = 0.20
    head_width_frac: float = 0.42
    head_len_frac: float = 0.26
    fill: ColorLike = "#ffcf1f"
    edge: Optional[ColorLike] = None
    edge_width: float = 1.0
    shading: float = 0.22
    smooth: int = 8
    final_angle: Optional[float] = None

    def _world_points(self, grid):
        cx, cy = self.origin(grid)
        S = grid.size

        wp = []
        for rx, ry in self.points:
            wp.append(
                (
                    cx + rx * S * 0.5,
                    cy - ry * S * 0.5,
                )
            )

        return wp, (cx, cy)

    @staticmethod
    def _smooth_path(pts, subdiv):
        if len(pts) < 2:
            return pts

        path = list(pts)

        if len(path) >= 3 and subdiv > 0:
            for _ in range(3):
                new = [path[0]]

                for i in range(len(path) - 1):
                    p, q = path[i], path[i + 1]

                    new.append((
                        p[0] * 0.75 + q[0] * 0.25,
                        p[1] * 0.75 + q[1] * 0.25,
                    ))

                    new.append((
                        p[0] * 0.25 + q[0] * 0.75,
                        p[1] * 0.25 + q[1] * 0.75,
                    ))

                new.append(path[-1])
                path = new

        dense = []

        for i in range(len(path) - 1):
            p, q = path[i], path[i + 1]

            for k in range(6):
                t = k / 6.0
                dense.append((
                    p[0] + (q[0] - p[0]) * t,
                    p[1] + (q[1] - p[1]) * t,
                ))

        dense.append(path[-1])

        return dense

    def draw(self, grid: PixelGrid):
        wp, (cx, cy) = self._world_points(grid)

        S = grid.size
        radius = S * self.width_frac / 2

        path = self._smooth_path(wp, self.smooth)

        edge_col = (
            self.edge
            if self.edge is not None
            else shade(self.fill, -0.45)
        )

        p_last = wp[-1]
        p_prev = wp[-2]

        if self.final_angle is not None:
            a = math.radians(self.final_angle)
            dirx, diry = math.cos(a), math.sin(a)
        else:
            dirx = p_last[0] - p_prev[0]
            diry = p_last[1] - p_prev[1]

            dl = math.hypot(dirx, diry) + 1e-6
            dirx /= dl
            diry /= dl

        head_len = S * self.head_len_frac
        head_w = S * self.head_width_frac

        tip = p_last

        base = (
            tip[0] - dirx * head_len,
            tip[1] - diry * head_len,
        )

        perpx, perpy = -diry, dirx

        head_poly = [
            (
                base[0] + perpx * head_w / 2,
                base[1] + perpy * head_w / 2,
            ),
            tip,
            (
                base[0] - perpx * head_w / 2,
                base[1] - perpy * head_w / 2,
            ),
        ]

        trimmed = _trim_path_to(path, base)

        X, Y = grid._coords()

        # Build exact fill mask

        body_mask = np.zeros(X.shape, dtype=bool)

        for i in range(len(trimmed) - 1):
            body_mask |= (
                _dist_to_segment(
                    X,
                    Y,
                    trimmed[i],
                    trimmed[i + 1],
                )
                <= radius
            )

        head_mask = _point_in_poly(X, Y, head_poly)

        fill_mask = body_mask | head_mask

        # 1-pixel outline

        if self.edge_width > 0:
            outline_mask = (
                PixelGrid.dilate_mask(fill_mask, 1)
                & ~fill_mask
            )

            grid.fill_mask(outline_mask, edge_col)

        # Fill

        grid.fill_mask(fill_mask, self.fill)

        # Shading

        if self.shading > 0:
            self._shade(grid, fill_mask, cx, cy)

    def _shade(self, grid, mask, cx, cy):
        X, Y = grid._coords()

        proj = (
            (X - cx) * 0.707 +
            (Y - cy) * 0.707
        )

        rgb = grid.buf[..., :3]

        lower = (
            (mask & (proj > 0))
        )[..., None]

        grid.buf[..., :3] = np.where(
            lower,
            np.clip(rgb * (1 - self.shading), 0, 255),
            rgb,
        )


def _centroid(pts):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return (sum(xs) / len(xs), sum(ys) / len(ys))


def _trim_path_to(path, end_point):
    """Return path but ending at end_point (cut off the tail beyond it)."""
    # find closest path index to end_point, keep up to there, append end_point
    best_i, best_d = 0, 1e18
    for i, p in enumerate(path):
        d = (p[0] - end_point[0]) ** 2 + (p[1] - end_point[1]) ** 2
        if d < best_d:
            best_d, best_i = d, i
    kept = path[: best_i + 1]
    kept.append(end_point)
    return kept


@dataclass
class Star(Shape):
    """A 5-point star (dream double jump / neutral jump / ultra)."""
    points: int = 5
    radius_frac: float = 0.30
    inner_ratio: float = 0.45
    rotation: float = -90.0            # point up
    fill: ColorLike = "#ffd21f"
    edge: Optional[ColorLike] = None
    edge_width: float = 1.0
    shading: float = 0.18

    def _poly(self, grid):
        cx, cy = self.origin(grid)
        R = grid.size * self.radius_frac
        rin = R * self.inner_ratio
        pts = []
        n = self.points * 2
        for i in range(n):
            ang = math.radians(self.rotation) + i * math.pi / self.points
            r = R if i % 2 == 0 else rin
            pts.append((cx + math.cos(ang) * r, cy + math.sin(ang) * r))
        return pts, (cx, cy)

    def draw(self, grid: PixelGrid):
        pts, (cx, cy) = self._poly(grid)
        edge_col = self.edge if self.edge is not None else shade(self.fill, -0.45)
        if self.edge_width > 0:
            big = _expand_poly(pts, (cx, cy), self.edge_width)
            grid.fill_poly(big, edge_col)
        grid.fill_poly(pts, self.fill)
        if self.shading > 0:
            X, Y = grid._coords()
            mask = _point_in_poly(X, Y, pts)
            proj = (X - cx) * 0.707 + (Y - cy) * 0.707
            rgb = grid.buf[..., :3]
            lower = (mask & (proj > 0))[..., None]
            grid.buf[..., :3] = np.where(lower, np.clip(rgb * (1 - self.shading), 0, 255), rgb)


@dataclass
class Cross(Shape):
    """A thick X (grounded ultra)."""
    size_frac: float = 0.34
    thickness_frac: float = 0.14
    fill: ColorLike = "#e23b2e"
    edge: Optional[ColorLike] = None
    edge_width: float = 1.0
    shading: float = 0.18

    def draw(self, grid: PixelGrid):
        cx, cy = self.origin(grid)
        S = grid.size
        r = S * self.size_frac
        t = S * self.thickness_frac / 2
        a = (cx - r, cy - r)
        b = (cx + r, cy + r)
        c = (cx - r, cy + r)
        d = (cx + r, cy - r)
        edge_col = self.edge if self.edge is not None else shade(self.fill, -0.45)
        if self.edge_width > 0:
            grid.fill_capsule(a, b, t + self.edge_width, edge_col)
            grid.fill_capsule(c, d, t + self.edge_width, edge_col)
        grid.fill_capsule(a, b, t, self.fill)
        grid.fill_capsule(c, d, t, self.fill)
        if self.shading > 0:
            X, Y = grid._coords()
            m1 = _dist_to_segment(X, Y, a, b) <= t
            m2 = _dist_to_segment(X, Y, c, d) <= t
            mask = m1 | m2
            proj = (X - cx) * 0.707 + (Y - cy) * 0.707
            rgb = grid.buf[..., :3]
            lower = (mask & (proj > 0))[..., None]
            grid.buf[..., :3] = np.where(lower, np.clip(rgb * (1 - self.shading), 0, 255), rgb)


@dataclass
class WallOverlay(Shape):
    """A vertical (or horizontal) 'wall' segment — like the wallbounce icon's
    bar that the arrow bounces off. side: 'left','right','top','bottom'."""
    side: str = "left"
    thickness_frac: float = 0.16
    length_frac: float = 0.92          # how much of the diameter it spans
    fill: ColorLike = "#ff8a1e"
    edge: Optional[ColorLike] = None
    edge_width: float = 1.0
    clip_radius_frac: Optional[float] = 0.46  # clip to circle if set
    shading: float = 0.2

    def draw(self, grid: PixelGrid):
        cx, cy = self.origin(grid)
        S = grid.size
        th = S * self.thickness_frac
        ln = S * self.length_frac
        if self.side in ("left", "right"):
            x = cx - (S * 0.5 - th / 2) * self.length_frac if self.side == "left" else cx + (S * 0.5 - th / 2) * self.length_frac
            # position the bar a bit inside the circle edge
            barx = cx - (S * 0.30) if self.side == "left" else cx + (S * 0.30)
            x0, x1 = barx - th / 2, barx + th / 2
            y0, y1 = cy - ln / 2, cy + ln / 2
        else:
            bary = cy - (S * 0.30) if self.side == "top" else cy + (S * 0.30)
            y0, y1 = bary - th / 2, bary + th / 2
            x0, x1 = cx - ln / 2, cx + ln / 2

        edge_col = self.edge if self.edge is not None else shade(self.fill, -0.4)
        # build masks then optionally clip to circle
        X, Y = grid._coords()
        rect = (X >= x0) & (X <= x1) & (Y >= y0) & (Y <= y1)
        big = (X >= x0 - self.edge_width) & (X <= x1 + self.edge_width) & (Y >= y0 - self.edge_width) & (Y <= y1 + self.edge_width)
        if self.clip_radius_frac:
            R = S * self.clip_radius_frac
            d2 = (X - cx) ** 2 + (Y - cy) ** 2
            inside = d2 <= (R - 1) ** 2
            rect &= inside
            big &= inside
        if self.edge_width > 0:
            grid.fill_mask(big & ~rect, edge_col)
        grid.fill_mask(rect, self.fill)
        if self.shading > 0:
            proj = (X - cx) * 0.707 + (Y - cy) * 0.707
            rgb = grid.buf[..., :3]
            lower = (rect & (proj > 0))[..., None]
            grid.buf[..., :3] = np.where(lower, np.clip(rgb * (1 - self.shading), 0, 255), rgb)


@dataclass
class CornerOverlay(Shape):
    """A square 'corner' block in one corner of the circle (corner boost icon).
    corner: 'br','bl','tr','tl'."""
    corner: str = "br"
    size_frac: float = 0.30
    fill: ColorLike = "#555a63"
    edge: Optional[ColorLike] = None
    edge_width: float = 1.0
    clip_radius_frac: Optional[float] = 0.46
    shading: float = 0.0

    def draw(self, grid: PixelGrid):
        cx, cy = self.origin(grid)
        S = grid.size
        sz = S * self.size_frac
        off = S * 0.30
        if self.corner == "br":
            ccx, ccy = cx + off, cy + off
        elif self.corner == "bl":
            ccx, ccy = cx - off, cy + off
        elif self.corner == "tr":
            ccx, ccy = cx + off, cy - off
        else:
            ccx, ccy = cx - off, cy - off
        x0, y0 = ccx - sz / 2, ccy - sz / 2
        x1, y1 = ccx + sz / 2, ccy + sz / 2
        edge_col = self.edge if self.edge is not None else shade(self.fill, -0.4)
        X, Y = grid._coords()
        rect = (X >= x0) & (X <= x1) & (Y >= y0) & (Y <= y1)
        big = (X >= x0 - self.edge_width) & (X <= x1 + self.edge_width) & (Y >= y0 - self.edge_width) & (Y <= y1 + self.edge_width)
        if self.clip_radius_frac:
            R = S * self.clip_radius_frac
            d2 = (X - cx) ** 2 + (Y - cy) ** 2
            inside = d2 <= (R - 1) ** 2
            rect &= inside
            big &= inside
        if self.edge_width > 0:
            grid.fill_mask(big & ~rect, edge_col)
        grid.fill_mask(rect, self.fill)


# --------------------------------------------------------------------------- #
# Canvas
# --------------------------------------------------------------------------- #

@dataclass
class Canvas:
    """Holds shapes and renders them. `grid_size` is the logical art resolution;
    `scale` is how many output pixels per art pixel (NEAREST upscale)."""
    grid_size: int = 32
    scale: int = 16
    background: ColorLike = (0, 0, 0, 0)
    shapes: List[Shape] = field(default_factory=list)

    def add(self, *shapes: Shape) -> "Canvas":
        self.shapes.extend(shapes)
        return self

    def render(self) -> Image.Image:
        grid = PixelGrid(self.grid_size)
        bg = parse_color(self.background)
        if bg[3] > 0:
            grid.buf[:] = np.array(bg, dtype=np.float32)
        for shp in self.shapes:
            shp.draw(grid)
        arr = np.clip(grid.buf, 0, 255).astype(np.uint8)
        img = Image.fromarray(arr, "RGBA")
        if self.scale != 1:
            img = img.resize(
                (self.grid_size * self.scale, self.grid_size * self.scale),
                Image.NEAREST,
            )
        return img

    def save(self, path: str):
        self.render().save(path)
        return path
