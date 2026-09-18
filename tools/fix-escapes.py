"""Repair C# string escapes mangled by shell heredoc round-trips.

Writing C# through a shell heredoc eats backslashes, so what should have been
`"\\r"` landed as a real carriage return inside a string constant. Works on
lines rather than regex, because the file is CRLF and the broken constants
contain raw newlines of their own.
"""

import io

BS = chr(92)
Q = '"'


def block(header, body_lines):
    return [header] + body_lines


def replace_member(lines, start_text, new_lines):
    """Swap the member starting at start_text, up to its closing brace."""
    for i, line in enumerate(lines):
        if line.strip() != start_text:
            continue
        indent = line[:len(line) - len(line.lstrip())]
        # find the closing brace at the same indent
        j = i + 1
        while j < len(lines) and lines[j].rstrip("\r\n") != indent + "}":
            j += 1
        if j >= len(lines):
            return False
        lines[i:j + 1] = new_lines
        return True
    return False


def main():
    path = "src/Mapping.cs"
    raw = io.open(path, encoding="utf-8-sig", newline="").read()
    lines = raw.splitlines()

    preview = [
        '            public string Preview',
        '            {',
        '                get { return Text.Replace(' + Q + BS + 'r' + Q + ', ' + Q + Q +
        ').Replace(' + Q + BS + 'n' + Q + ', ' + Q + '  ' + Q + '); }',
        '            }',
    ]

    escape = [
        '        static string Escape(string text)',
        '        {',
        '            return text.Replace(' + Q + BS + BS + Q + ', ' + Q + BS + BS + BS + BS + Q + ')',
        '                       .Replace(' + Q + BS + 'r' + Q + ', ' + Q + Q + ')',
        '                       .Replace(' + Q + BS + 'n' + Q + ', ' + Q + BS + BS + 'n' + Q + ');',
        '        }',
    ]

    unescape = [
        '        static string Unescape(string text)',
        '        {',
        '            return text.Replace(' + Q + BS + BS + 'n' + Q + ', ' + Q + BS + 'n' + Q + ')',
        '                       .Replace(' + Q + BS + BS + BS + BS + Q + ', ' + Q + BS + BS + Q + ');',
        '        }',
    ]

    ok = []
    ok.append(replace_member(lines, "public string Preview", preview))
    ok.append(replace_member(lines, "static string Escape(string text)", escape))
    ok.append(replace_member(lines, "static string Unescape(string text)", unescape))

    io.open(path, "w", encoding="utf-8-sig", newline="\r\n").write("\n".join(lines) + "\n")
    print("replaced: Preview=%s Escape=%s Unescape=%s" % tuple(ok))


if __name__ == "__main__":
    main()
