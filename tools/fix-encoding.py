"""Repair source files double-encoded by a PowerShell round-trip.

Windows PowerShell 5.1 reads with the ANSI codepage (cp1252 here), so
`Get-Content -Raw ... | Set-Content -Encoding utf8` re-encodes every non-ASCII
character's UTF-8 bytes as UTF-8 again. cp1252 is not latin-1: byte 0x98 maps
to U+02DC and 0x80 to U+20AC, which is why a naive latin-1 round-trip cannot
undo it.

Each run of non-ASCII characters is decoded back through cp1252 when that
yields valid UTF-8; genuine glyphs that never went through the round-trip fail
that test and are left exactly as they are.
"""

import glob
import os
import re
import sys

RUN = re.compile(u"[^\x00-\x7f]+")


def repair_run(m):
    run = m.group(0)
    try:
        decoded = run.encode("cp1252").decode("utf-8")
    except (UnicodeEncodeError, UnicodeDecodeError):
        return run
    # Only accept the rewrite when it actually shortens the run: a genuine
    # glyph never decodes to something longer or equal.
    return decoded if len(decoded) < len(run) else run


def repair_file(path):
    raw = open(path, "rb").read()
    has_bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    fixed = RUN.sub(repair_run, text)
    if fixed == text:
        return False
    out = (b"\xef\xbb\xbf" if has_bom else b"") + fixed.encode("utf-8")
    open(path, "wb").write(out)
    return True


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    targets = sys.argv[1:] or glob.glob(os.path.join(root, "src", "*.cs"))
    for path in targets:
        changed = repair_file(path)
        print(("repaired " if changed else "clean    ") + os.path.basename(path))


if __name__ == "__main__":
    main()
