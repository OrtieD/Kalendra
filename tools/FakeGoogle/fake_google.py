"""Stand-in for Google during tests: OAuth token endpoint + CalDAV that only accepts Bearer tokens.
CalDAV requests are forwarded to the local Radicale as user gmail:y.
Calendar API (only what Google Meet needs): GET/PATCH /calendar/v3/calendars/{cal}/events…, writes X-GOOGLE-CONFERENCE into the .ics.
Control: POST /_expire (current access token becomes invalid), POST /_revoke (refresh token becomes invalid),
POST /_apioff | /_apion (Calendar API disabled like in a fresh Google Cloud project)."""
import base64, hashlib, http.server, json, urllib.parse, urllib.request, itertools

RADICALE = "http://127.0.0.1:5232"
CLIENT_ID, CLIENT_SECRET = "test-client.apps.googleusercontent.com", "GOCSPX-test"
state = {"api": True, "access": None, "refresh": "refresh-1", "codes": {}, "n": itertools.count(1), "refreshes": 0}

def b64(d): return base64.urlsafe_b64encode(json.dumps(d).encode()).decode().rstrip("=")
ID_TOKEN = b64({"alg": "none"}) + "." + b64({"email": "test@gmail.example"}) + ".sig"

class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def send(self, code, body=b"", ctype="application/json", headers=()):
        self.send_response(code)
        self.send_header("Content-Type", ctype); self.send_header("Content-Length", str(len(body)))
        for k, v in headers: self.send_header(k, v)
        self.end_headers(); self.wfile.write(body)

    def do_GET(self):
        u = urllib.parse.urlparse(self.path)
        if u.path == "/auth":  # browser sign-in: immediately "approve" and remember the PKCE challenge
            q = dict(urllib.parse.parse_qsl(u.query))
            code = "code-%d" % next(state["n"])
            state["codes"][code] = q["code_challenge"]
            loc = q["redirect_uri"] + "?" + urllib.parse.urlencode({"code": code, "state": q["state"]})
            return self.send(302, headers=[("Location", loc)])
        if u.path.startswith("/calendar/v3/"): return self.api()
        return self.proxy()

    def radicale(self, method, path, body=None, ctype=None):
        req = urllib.request.Request(RADICALE + path, data=body, method=method)
        if ctype: req.add_header("Content-Type", ctype)
        req.add_header("Authorization", "Basic " + base64.b64encode(b"gmail:y").decode())
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        try: r = opener.open(req)
        except urllib.error.HTTPError as e: r = e
        return (r.status if hasattr(r, "status") else r.code), r.read()

    def api(self):
        if state["access"] is None or self.headers.get("Authorization", "") != "Bearer " + state["access"]:
            return self.send(401, b'{"error":{"code":401,"status":"UNAUTHENTICATED"}}')
        if not state["api"]:
            return self.send(403, json.dumps({"error": {"code": 403, "message": "Google Calendar API has not been used in project 1 before or it is disabled.", "errors": [{"reason": "accessNotConfigured"}], "status": "PERMISSION_DENIED"}}).encode())
        u = urllib.parse.urlparse(self.path)
        parts = [urllib.parse.unquote(p) for p in u.path.split("/") if p]  # calendar v3 calendars {cal} events [id]
        cal = parts[3]
        if self.command == "GET" and len(parts) == 5:
            uid = dict(urllib.parse.parse_qsl(u.query)).get("iCalUID", "")
            code, data = self.radicale("GET", "/gmail/%s/%s.ics" % (cal, urllib.parse.quote(uid)))
            items = [] if code != 200 else [{"id": "ev-" + uid, "iCalUID": uid}]
            if code == 200 and b"X-GOOGLE-CONFERENCE" in data:
                items[0]["hangoutLink"] = data.decode().split("X-GOOGLE-CONFERENCE:")[1].split("\r\n")[0].split("\n")[0].strip()
            return self.send(200, json.dumps({"items": items}).encode())
        if self.command == "PATCH" and len(parts) == 6 and "conferenceDataVersion=1" in u.query:
            uid = parts[5][3:]
            n = int(self.headers.get("Content-Length", 0) or 0)
            req = json.loads(self.rfile.read(n))
            assert req["conferenceData"]["createRequest"]["conferenceSolutionKey"]["type"] == "hangoutsMeet"
            path = "/gmail/%s/%s.ics" % (cal, urllib.parse.quote(uid))
            code, data = self.radicale("GET", path)
            if code != 200: return self.send(404, b'{"error":{"code":404}}')
            link = "https://meet.google.com/abc-defg-%03d" % next(state["n"])
            ics = data.decode().replace("\r\nEND:VEVENT", "\r\nX-GOOGLE-CONFERENCE:%s\r\nEND:VEVENT" % link, 1)
            self.radicale("PUT", path, ics.encode(), "text/calendar")
            return self.send(200, json.dumps({"id": "ev-" + uid, "hangoutLink": link, "conferenceData": {"entryPoints": [{"entryPointType": "video", "uri": link}]}}).encode())
        return self.send(400, b'{"error":{"code":400}}')

    def do_PATCH(self):
        if self.path.startswith("/calendar/v3/"): return self.api()
        return self.send(405)

    def do_POST(self):
        if self.path == "/_expire": state["access"] = None; return self.send(200)
        if self.path == "/_apioff": state["api"] = False; return self.send(200)
        if self.path == "/_apion": state["api"] = True; return self.send(200)
        if self.path == "/_revoke": state["refresh"] = None; state["access"] = None; return self.send(200)
        if self.path == "/token":
            n = int(self.headers.get("Content-Length", 0))
            f = dict(urllib.parse.parse_qsl(self.rfile.read(n).decode()))
            if f.get("client_id") != CLIENT_ID or f.get("client_secret") != CLIENT_SECRET:
                return self.send(401, json.dumps({"error": "invalid_client"}).encode())
            if f.get("grant_type") == "authorization_code":
                ch = state["codes"].pop(f.get("code"), None)
                ver = base64.urlsafe_b64encode(hashlib.sha256(f.get("code_verifier", "").encode()).digest()).decode().rstrip("=")
                if ch is None or ch != ver: return self.send(400, json.dumps({"error": "invalid_grant"}).encode())
                state["refresh"] = "refresh-%d" % next(state["n"])
                state["access"] = "access-%d" % next(state["n"])
                return self.send(200, json.dumps({"access_token": state["access"], "expires_in": 3599, "refresh_token": state["refresh"], "id_token": ID_TOKEN, "token_type": "Bearer"}).encode())
            if f.get("grant_type") == "refresh_token":
                if f.get("refresh_token") != state["refresh"] or state["refresh"] is None:
                    return self.send(400, json.dumps({"error": "invalid_grant"}).encode())
                state["access"] = "access-%d" % next(state["n"]); state["refreshes"] += 1
                return self.send(200, json.dumps({"access_token": state["access"], "expires_in": 3599, "token_type": "Bearer"}).encode())
            return self.send(400, b'{"error":"unsupported_grant_type"}')
        return self.proxy()

    def proxy(self):
        auth = self.headers.get("Authorization", "")
        if state["access"] is None or auth != "Bearer " + state["access"]:
            return self.send(401, b"unauthorized", "text/plain", [("WWW-Authenticate", "Bearer")])
        n = int(self.headers.get("Content-Length", 0) or 0)
        body = self.rfile.read(n) if n else None
        req = urllib.request.Request(RADICALE + self.path, data=body, method=self.command)
        for k in ("Content-Type", "Depth", "If-Match", "If-None-Match"):
            if self.headers.get(k): req.add_header(k, self.headers[k])
        req.add_header("Authorization", "Basic " + base64.b64encode(b"gmail:y").decode())
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        try: r = opener.open(req)
        except urllib.error.HTTPError as e: r = e
        data = r.read()
        hdr = [(k, v) for k, v in r.headers.items() if k.lower() in ("etag", "location")]
        self.send(r.status if hasattr(r, "status") else r.code, data, r.headers.get("Content-Type", "text/plain"), hdr)
    do_PROPFIND = do_REPORT = do_PUT = do_DELETE = do_PROPPATCH = proxy

http.server.ThreadingHTTPServer(("127.0.0.1", 5240), H).serve_forever()
