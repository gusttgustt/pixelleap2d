"""Pulls every Thai string out of the game's C# scripts (and the server's JS) so they can be
translated. Plain literals become exact entries; interpolated strings ($"...{x}...") become
templates with {0}, {1}... where the holes were. Writes Tools/lang_strings.tsv:
    kind <TAB> thai (escaped) <TAB> first place it was seen
and, with --check Assets/Resources/Lang/en.txt, lists the strings that have no translation yet.

    python Tools/lang_extract.py            (refresh the list)
    python Tools/lang_extract.py --check    (what's still missing from en.txt)
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
THAI = re.compile(r'[\u0E00-\u0E7F]')

def esc(s):  # one line per entry in the tsv
    return s.replace('\\', '\\\\').replace('\t', '\\t').replace('\n', '\\n').replace('\r', '')

def unesc_cs(s):
    out, i = [], 0
    while i < len(s):
        c = s[i]
        if c == '\\' and i + 1 < len(s):
            n = s[i + 1]
            out.append({'n': '\n', 't': '\t', '"': '"', "'": "'", '\\': '\\', '0': '\0', 'r': ''}.get(n, n))
            i += 2
            continue
        out.append(c); i += 1
    return ''.join(out)

class Lexer:
    """Just enough of C# to find string literals, with interpolation holes followed into."""
    def __init__(self, src, found, where):
        self.s, self.i, self.found, self.where = src, 0, found, where

    def line(self, at): return self.s.count('\n', 0, at) + 1

    def run(self, stop_at_brace=False):
        s = self.s; depth = 0
        while self.i < len(s):
            c = s[self.i]
            if s.startswith('//', self.i):
                j = s.find('\n', self.i); self.i = len(s) if j < 0 else j; continue
            if s.startswith('/*', self.i):
                j = s.find('*/', self.i + 2); self.i = len(s) if j < 0 else j + 2; continue
            if c == "'":
                j = self.i + 1
                while j < len(s) and s[j] != "'":
                    j += 2 if s[j] == '\\' else 1
                self.i = j + 1; continue
            if c in '$@"':
                m = re.match(r'(\$@|@\$|\$|@)?"', s[self.i:])
                if m and (c == '"' or m.group(1)):
                    pre = m.group(1) or ''
                    start = self.i
                    self.i += len(m.group(0))
                    self.string('$' in pre, '@' in pre, start)
                    continue
            if stop_at_brace:
                if c == '{': depth += 1
                elif c == '}':
                    if depth == 0: return
                    depth -= 1
            self.i += 1

    def string(self, interp, verbatim, start):
        s = self.s; parts, holes = [], 0
        buf = []
        while self.i < len(s):
            c = s[self.i]
            if verbatim and c == '"' and s.startswith('""', self.i):
                buf.append('"'); self.i += 2; continue
            if c == '"':
                self.i += 1; break
            if not verbatim and c == '\\':
                buf.append(s[self.i:self.i + 2]); self.i += 2; continue
            if interp and c == '{':
                if s.startswith('{{', self.i): buf.append('{'); self.i += 2; continue
                self.i += 1
                self.run(stop_at_brace=True)  # the hole's code (its own strings get found too)
                self.i += 1  # the closing }
                buf.append('\x01%d\x02' % holes); holes += 1
                continue
            if interp and c == '}' and s.startswith('}}', self.i):
                buf.append('}'); self.i += 2; continue
            buf.append(c); self.i += 1
        text = ''.join(buf)
        if not verbatim: text = unesc_cs(text)
        text = text.replace('{', '{{').replace('}', '}}') if holes else text
        text = re.sub('\x01(\\d+)\x02', r'{\1}', text)
        if THAI.search(text):
            kind = 'T' if holes else 'E'
            key = (kind, text)
            if key not in self.found: self.found[key] = '%s:%d' % (self.where, self.line(start))

def js_strings(src, found, where):
    for m in re.finditer(r'"((?:[^"\\\n]|\\.)*)"|`((?:[^`\\]|\\.)*)`', src):
        if m.group(1) is not None:
            text, kind = unesc_cs(m.group(1)), 'E'
        else:
            raw, n = m.group(2), [0]
            def hole(_):
                n[0] += 1; return '{%d}' % (n[0] - 1)
            text = re.sub(r'\$\{[^}]*\}', hole, raw)
            kind = 'T' if '${' in raw else 'E'
        if THAI.search(text):
            key = (kind, text)
            if key not in found: found[key] = '%s:%d' % (where, src.count('\n', 0, m.start()) + 1)

def main():
    sys.stdout.reconfigure(encoding="utf-8")
    found = {}
    sdir = os.path.join(ROOT, 'Assets', 'Scripts')
    for f in sorted(os.listdir(sdir)):
        if f.endswith('.cs'):
            src = open(os.path.join(sdir, f), encoding='utf-8-sig').read()
            Lexer(src, found, f).run()
    js = os.path.join(ROOT, 'Server', 'server.js')
    js_strings(open(js, encoding='utf-8').read(), found, 'server.js')
    out = os.path.join(ROOT, 'Tools', 'lang_strings.tsv')
    with open(out, 'w', encoding='utf-8', newline='\n') as w:
        for (kind, text), where in found.items():
            w.write('%s\t%s\t%s\n' % (kind, esc(text), where))
    print('%d strings (%d templates) -> %s' % (len(found), sum(1 for k in found if k[0] == 'T'), out))
    if '--check' in sys.argv:
        en = os.path.join(ROOT, 'Assets', 'Resources', 'Lang', 'en.txt')
        have = set()
        if os.path.exists(en):
            for ln in open(en, encoding='utf-8'):
                if '\t' in ln and not ln.startswith('#'): have.add(ln.split('\t')[1])
        missing = [(k, w) for k, w in found.items() if esc(k[1]) not in have]
        for (kind, text), where in missing: print('%s\t%s\t%s' % (kind, esc(text), where))
        print('missing: %d' % len(missing), file=sys.stderr)

if __name__ == '__main__':
    main()
