#!/usr/bin/env python3
"""Check current documentation links, image paths and Markdown heading anchors offline."""
import html
from html.parser import HTMLParser
from pathlib import Path
import re
import urllib.parse

ROOT = Path(__file__).resolve().parents[1]
ROOT_DOCS = ['README.md', 'README.ru.md', 'CONTRIBUTING.md', 'ROADMAP.md', 'CHANGELOG.md']


def prose(text):
    return re.sub(r'^\s*(```|~~~).*?^\s*\1\s*$', '', text, flags=re.M | re.S)


def anchors(text):
    found = set(re.findall(r'\b(?:id|name)=["\']([^"\']+)["\']', text))
    counts = {}
    for heading in re.findall(r'^#{1,6}\s+(.+?)\s*#*\s*$', prose(text), re.M):
        heading = re.sub(r'!?(\[([^\]]+)\])\([^)]*\)', r'\2', heading)
        heading = html.unescape(re.sub(r'<[^>]+>', '', heading)).lower()
        slug = re.sub(r'[^\w\- ]', '', heading).replace(' ', '-')
        n = counts.get(slug, 0)
        counts[slug] = n + 1
        found.add(slug + ('-' + str(n) if n else ''))
    return found


class HTMLLinks(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        for key, value in attrs:
            if key in ('href', 'src') and value:
                self.links.append(value)


def links(text):
    text = prose(text)
    parser = HTMLLinks()
    parser.feed(text)
    inline = re.findall(r'!?\[[^\]\n]*\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+["\'][^\n]*?["\'])?\s*\)', text)
    references = re.findall(r'^\s*\[[^\]]+\]:\s*(<[^>]+>|\S+)', text, re.M)
    return [html.unescape(url.strip('<>')) for url in inline + references + parser.links]


def check(root=ROOT):
    pages = [root / p for p in ROOT_DOCS] + sorted((root / 'docs').glob('*.md'))
    errors = []
    checked = 0
    for page in pages:
        if not page.is_file():
            errors.append(f'{page.relative_to(root)}: missing document')
            continue
        for url in links(page.read_text()):
            parsed = urllib.parse.urlsplit(url)
            if parsed.scheme or parsed.netloc:
                continue
            checked += 1
            name = urllib.parse.unquote(parsed.path)
            target = ((root / name.lstrip('/')) if name.startswith('/') else page.parent / name).resolve() if name else page
            if target != root and root not in target.parents:
                errors.append(f'{page.relative_to(root)}: link escapes repository: {url}')
            elif not target.exists():
                errors.append(f'{page.relative_to(root)}: missing target: {url}')
            elif parsed.fragment and target.is_file() and target.suffix == '.md':
                fragment = urllib.parse.unquote(parsed.fragment)
                if fragment not in anchors(target.read_text()):
                    errors.append(f'{page.relative_to(root)}: missing heading: {url}')
    return pages, checked, errors


def main():
    pages, count, errors = check()
    for error in errors:
        print(error)
    print(f'Checked {len(pages)} current documents and {count} local links; issues={len(errors)}')
    return int(bool(errors))


if __name__ == '__main__':
    raise SystemExit(main())
