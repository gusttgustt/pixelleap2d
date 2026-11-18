"""Adds translations to Assets/Resources/Lang/en.txt (the dictionary the game reads, Lang.cs).

    python Tools/lang_merge.py Tools/lang_en/*.txt

Each input line is "<id> TAB english", where id is the line number in Tools/lang_strings.tsv as
lang_extract.py last wrote it. Existing entries in en.txt are kept (a new translation of the same
Thai replaces the old one). Templates are checked: the English must use the same {0}, {1}... holes.
"""
import os, re, sys, glob

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EN = os.path.join(ROOT, 'Assets', 'Resources', 'Lang', 'en.txt')

def main():
    rows = [ln.rstrip('\n').split('\t') for ln in open(os.path.join(ROOT, 'Tools', 'lang_strings.tsv'), encoding='utf-8')]
    entries = {}
    order = []
    if os.path.exists(EN):
        for ln in open(EN, encoding='utf-8'):
            ln = ln.rstrip('\n')
            if not ln or ln.startswith('#'): continue
            k, th, en = ln.split('\t', 2)
            entries[(k, th)] = en; order.append((k, th))
    bad = 0
    files = [f for a in sys.argv[1:] for f in sorted(glob.glob(a))]
    for f in files:
        for n, ln in enumerate(open(f, encoding='utf-8'), 1):
            ln = ln.rstrip('\n')
            if not ln.strip(): continue
            i, en = ln.split('\t', 1)
            kind, th = rows[int(i) - 1][0], rows[int(i) - 1][1]
            if kind == 'T':
                want = sorted(set(re.findall(r'(?<!\{)\{(\d+)\}', th)))
                got = sorted(set(re.findall(r'(?<!\{)\{(\d+)\}', en)))
                if want != got:
                    print('%s:%d holes %s != %s  | %s | %s' % (f, n, want, got, th, en)); bad += 1
            elif re.search(r'(?<!\{)\{\d+\}', en):
                print('%s:%d plain string with a hole: %s' % (f, n, en)); bad += 1
            key = (kind, th)
            if key not in entries: order.append(key)
            entries[key] = en
    os.makedirs(os.path.dirname(EN), exist_ok=True)
    with open(EN, 'w', encoding='utf-8', newline='\n') as w:
        w.write('# Thai -> English for Lang.cs. kind (E exact / T template) TAB thai TAB english. \\n = new line.\n')
        for key in order: w.write('%s\t%s\t%s\n' % (key[0], key[1], entries[key]))
    print('%d entries in %s, %d problems' % (len(order), EN, bad))

if __name__ == '__main__':
    main()
