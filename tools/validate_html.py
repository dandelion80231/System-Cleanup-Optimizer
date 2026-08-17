import sys, os
from html.parser import HTMLParser

VOID = {"area","base","br","col","embed","hr","img","input","link","meta","param","source","track","wbr"}

class V(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.stack = []
        self.errors = []
    def handle_starttag(self, tag, attrs):
        if tag in VOID: return
        self.stack.append((tag, self.getpos()))
    def handle_endtag(self, tag):
        if tag in VOID: return
        if not self.stack:
            self.errors.append(f"extra </{tag}> at {self.getpos()}")
            return
        top, pos = self.stack.pop()
        if top != tag:
            self.errors.append(f"mismatch: <{top}> (opened {pos}) closed by </{tag}> at {self.getpos()}")

files = sys.argv[1:]
ok = True
for f in files:
    p = V()
    try:
        p.feed(open(f, encoding="utf-8").read())
    except Exception as e:
        print(f"[ERR] {f}: read/parse failed: {e}")
        ok = False
        continue
    leftover = [t for t,_ in p.stack]
    if p.errors or leftover:
        ok = False
        print(f"[FAIL] {f}")
        for e in p.errors: print("   ", e)
        if leftover: print("    unclosed:", leftover)
    else:
        print(f"[OK]   {f}  ({os.path.getsize(f)} bytes)")
sys.exit(0 if ok else 1)
