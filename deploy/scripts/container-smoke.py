"""HTTPS smoke tests against the real published image; Python standard library only."""
import hashlib
import html
import http.cookiejar
import json
import os
from pathlib import Path
import re
import ssl
import struct
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zlib


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


base = os.environ.get("SMOKE_URL", "https://localhost:8443")
state = Path(os.environ["SMOKE_STATE_DIR"])
state.mkdir(parents=True, exist_ok=True)
cookies = http.cookiejar.LWPCookieJar(str(state / "cookies.txt"))
context = ssl.create_default_context(cafile=os.environ["SMOKE_CA"])


def client(jar):
    return urllib.request.build_opener(NoRedirect(), urllib.request.HTTPSHandler(context=context),
                                      urllib.request.HTTPCookieProcessor(jar))


browser = client(cookies)


def request(path, values=None, *, body=None, headers=None, expected=200, opener=None):
    headers = dict(headers or {})
    if values is not None:
        body = urllib.parse.urlencode(values).encode()
        headers["Content-Type"] = "application/x-www-form-urlencoded"
    try:
        response = (opener or browser).open(urllib.request.Request(base + path, body, headers), timeout=30)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        payload = response.read()
        assert response.code == expected, f"{path}: expected {expected}, got {response.code}"
        return payload, response.headers


def token(path):
    markup = request(path)[0].decode()
    return html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', markup)[1])


def png():
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    pixels = (b"\0" + b"\x30\xa0\x70" * 64) * 32
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 64, 32, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b""))


def verify(saved):
    report = json.loads(request(f'/history/export/{saved["audit"]}')[0])
    assert report["mode"] == "DEMO"
    assert hashlib.sha256(request(f'/photoaudit/image/{saved["draft"]}')[0]).hexdigest() == saved["image_sha256"]
    assert "DEMO" in request(f'/history/print/{saved["audit"]}')[0].decode()
    assert request("/health")[0] == b"Healthy"
    assert request("/history", opener=client(http.cookiejar.CookieJar()), expected=302)[1]["Location"]


if sys.argv[1] == "prepare":
    assert request("/health")[0] == b"Healthy"
    home, headers = request("/", headers={"X-Forwarded-Proto": "http", "X-Forwarded-For": "198.51.100.23"})
    assert b"RetailVision" in home
    assert headers["X-Content-Type-Options"] == "nosniff"
    assert "frame-ancestors 'none'" in headers["Content-Security-Policy"]
    request("/audit/demo", {"scenario": "correct"}, expected=400)
    for scenario, percent in [("correct", "100"), ("missing", "87,5"), ("sequence", "75")]:
        markup = html.unescape(request("/audit/demo", {"scenario": scenario, "__RequestVerificationToken": token("/audit")})[0].decode())
        assert f'data-testid="compliance">{percent}' in markup
        assert "нейросеть не использовалась" in markup
    password = Path(os.environ["ADMIN_PASSWORD_FILE"]).read_text().rstrip("\r\n")
    _, headers = request("/account/login", {"Email": os.environ["ADMIN_EMAIL"], "Password": password,
                                            "__RequestVerificationToken": token("/account/login")}, expected=302)
    assert headers["Location"] == "/home/settings"
    assert any(cookie.name == "RetailVision.Auth" and cookie.secure for cookie in cookies)
    key = uuid.uuid4().hex
    _, headers = request("/history/savedemo", {"scenario": "missing", "key": key,
                                             "__RequestVerificationToken": token("/audit")}, expected=302)
    audit = headers["Location"].split("/")[-1]
    fields = {"PlanogramId": "1", "__RequestVerificationToken": token("/photoaudit")}
    boundary = "rv-" + uuid.uuid4().hex
    parts = [f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode()
             for name, value in fields.items()]
    parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="Photo"; filename="probe.png"\r\nContent-Type: image/png\r\n\r\n'.encode()
                 + png() + f'\r\n--{boundary}--\r\n'.encode())
    _, headers = request("/photoaudit/upload", body=b"".join(parts),
                         headers={"Content-Type": f"multipart/form-data; boundary={boundary}"}, expected=302)
    draft = headers["Location"].split("/")[-1]
    image, _ = request(f"/photoaudit/image/{draft}")
    assert image.startswith(b"\x89PNG")
    saved = {"audit": audit, "draft": draft, "image_sha256": hashlib.sha256(image).hexdigest()}
    (state / "saved.json").write_text(json.dumps(saved))
    cookies.save(ignore_discard=True, ignore_expires=True)
    verify(saved)
    print("HTTPS smoke passed: DEMO, CSRF, secure login, saved report, native image upload and private routes.")
elif sys.argv[1] == "verify":
    cookies.load(ignore_discard=True, ignore_expires=True)
    verify(json.loads((state / "saved.json").read_text()))
    print("Persistence verified: existing login cookie, report and image survived recreation/restoration.")
else:
    raise SystemExit("Expected prepare or verify.")
