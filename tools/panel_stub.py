# -*- coding: utf-8 -*-
"""Stub of TechPanelServer, so the panel can be screenshotted without the game.

Serves the real TechPanel.html, a /techs JSON parsed straight out of
TechRegistry.cs, and /icon?name= resolved with the same prefix/slash rules the
C# TechIconLoader uses.
"""
import io, json, os, re, sys, urllib.parse
from http.server import BaseHTTPRequestHandler, HTTPServer

SRC = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)), ".."))
ICONS = os.path.join(SRC, "Graphics", "Atlases", "Gui", "techpopups", "icons")
HTML = io.open(os.path.join(SRC, "Source", "Panel", "TechPanel.html"), encoding="utf-8").read()

reg = io.open(os.path.join(SRC, "Source", "TechRegistry.cs"), encoding="utf-8").read()
rows = re.findall(
    r'E\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]*)"\s*,\s*TechCategory\.(\w+)\s*,'
    r'\s*(\w+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*"([^"]*)"', reg)
techs = [{"id": a, "name": b, "alias": c, "cat": d, "sec": e,
          "x": int(f), "y": int(g), "key": h,
          "on": (i % 5 != 0)}          # 大部分开、少数关，好看清两种状态
         for i, (a, b, c, d, e, f, g, h) in enumerate(rows)]
print("stub: %d techs" % len(techs))

PREF = ["extended ", "reverse ", "mid-air ", "chained ", "delayed ", "grounded "]
files = os.listdir(ICONS)
def collapse(s):
    while "  " in s:
        s = s.replace("  ", " ")
    return s
def resolve(name):
    cur = name
    for _ in range(3):
        flat = collapse(cur.replace("/", " ")).strip()
        for cand in (flat, flat.lower(), flat.replace(" ", "_"), flat.lower().replace(" ", "_")):
            for f in files:
                if f.lower() == cand.lower() + ".png":
                    return f
        nxt = None
        for p in PREF:
            if cur.lower().startswith(p):
                nxt = cur[len(p):].strip()
                break
        if not nxt or nxt == cur:
            return None
        cur = nxt
    return None

class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass
    def _send(self, body, ctype, code=200):
        if isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)
    def do_GET(self):
        u = urllib.parse.urlparse(self.path)
        q = urllib.parse.parse_qs(u.query)
        if u.path == "/techs":
            return self._send(json.dumps({"techs": techs}), "application/json")
        if u.path == "/visual":
            return self._send(json.dumps({"visual": []}), "application/json")
        if u.path == "/icon":
            name = (q.get("name") or [""])[0]
            f = resolve(name)
            if not f:
                return self._send(b"", "text/plain", 404)
            return self._send(io.open(os.path.join(ICONS, f), "rb").read(), "image/png")
        return self._send(HTML, "text/html; charset=utf-8")

print("stub: listening on 32299")
HTTPServer(("127.0.0.1", 32299), H).serve_forever()
