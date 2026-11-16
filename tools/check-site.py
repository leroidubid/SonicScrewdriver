#!/usr/bin/env python3
"""Checks the static site in site/ (standard library only, no network access).

For every site/*.html page:
  - relative href/src targets exist, #anchors exist in the target page, images exist;
  - exactly one <h1>, a <title>, a meta description, a lang attribute on <html>, every <img> has an alt;
  - no duplicate ids;
  - no third-party host in src, in the href of stylesheets/icons/preloads, in <style> or inline url(...).
Also: style.css and site.js (no third-party host, relative url() targets exist) and site/sitemap.xml (every <loc>
points to an existing page).

External links (<a href="https://...">) are listed, never fail the run (a link to a host other than github.com
only gets a note). The only external hosts allowed to be contacted by the page itself are github.com links and the
api.github.com call in site.js.

Usage: python tools/check-site.py [site-dir]      Exit code 1 if anything failed, 0 otherwise.
"""
import os
import re
import sys
from html.parser import HTMLParser
from urllib.parse import unquote, urlsplit

# Hosts a link (<a href>) may point to without a note.
LINK_HOSTS = {"github.com", "www.github.com"}
# Hosts site.js may reference.
SCRIPT_HOSTS = {"api.github.com"}
# <link rel=...> values that load a resource (their href must be local); others (canonical, ...) are just links.
RESOURCE_RELS = {"stylesheet", "icon", "shortcut", "apple-touch-icon", "preload", "prefetch", "modulepreload",
                 "manifest", "mask-icon"}
SKIP_SCHEMES = ("mailto:", "tel:", "javascript:", "data:", "sms:")
URL_RE = re.compile(r"""url\(\s*(['"]?)(.*?)\1\s*\)""", re.I)
ABS_URL_RE = re.compile(r"""(?:https?:)?//[^\s'")>]+""", re.I)


class PageParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.lang = None
        self.title = None
        self._in_title = False
        self._title_buf = []
        self.description = None
        self.h1 = 0
        self.ids = []
        self.base = None
        self.links = []      # (kind, url) kind: "a" or "resource" or "img" ...
        self.alt_missing = 0
        self.styles = []     # text of <style> and style="" attributes
        self._in_style = False

    def handle_starttag(self, tag, attrs):
        a = {k: (v if v is not None else "") for k, v in attrs}
        if "id" in a:
            self.ids.append(a["id"])
        if tag == "a" and "name" in a:
            self.ids.append(a["name"])
        if "style" in a:
            self.styles.append(a["style"])
        if tag == "html":
            self.lang = a.get("lang")
        elif tag == "title":
            self._in_title = True
        elif tag == "style":
            self._in_style = True
        elif tag == "h1":
            self.h1 += 1
        elif tag == "base":
            self.base = a.get("href")
        elif tag == "meta":
            if a.get("name", "").lower() == "description":
                self.description = a.get("content", "")
        elif tag == "a" or tag == "area":
            if "href" in a:
                self.links.append(("a", a["href"]))
        elif tag == "link":
            rels = set(a.get("rel", "").lower().split())
            if "href" in a:
                self.links.append(("resource" if rels & RESOURCE_RELS else "link", a["href"]))
        elif tag == "img":
            if "alt" not in a:
                self.alt_missing += 1
            if "src" in a:
                self.links.append(("src", a["src"]))
            if "srcset" in a:
                for part in a["srcset"].split(","):
                    if part.strip():
                        self.links.append(("src", part.strip().split()[0]))
        elif tag in ("script", "source", "audio", "video", "iframe", "embed", "track", "input"):
            if "src" in a:
                self.links.append(("src", a["src"]))
            if "srcset" in a:
                for part in a["srcset"].split(","):
                    if part.strip():
                        self.links.append(("src", part.strip().split()[0]))
        elif tag == "form" and "action" in a:
            self.links.append(("a", a["action"]))

    def handle_endtag(self, tag):
        if tag == "title":
            self._in_title = False
            self.title = "".join(self._title_buf).strip()
        elif tag == "style":
            self._in_style = False

    def handle_data(self, data):
        if self._in_title:
            self._title_buf.append(data)
        if self._in_style:
            self.styles.append(data)


def host_of(url):
    return (urlsplit(url).hostname or "").lower()


def is_external(url):
    return url.startswith(("http://", "https://", "//"))


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "site"))
    if not os.path.isdir(root):
        print("site folder not found: " + root)
        return 1
    pages = sorted(f for f in os.listdir(root) if f.endswith(".html"))
    errors = []
    externals = {}  # url -> set(pages)
    notes = []

    def err(page, msg):
        errors.append("%s: %s" % (page, msg))

    parsed = {}
    for name in pages:
        with open(os.path.join(root, name), encoding="utf-8") as fh:
            p = PageParser()
            p.feed(fh.read())
            p.close()
        parsed[name] = p

    def check_local(page, p, kind, url):
        """Check a relative url found in `page`; returns nothing, records errors."""
        if url.startswith("#"):
            target, frag = page, url[1:]
        else:
            parts = urlsplit(url)
            path = unquote(parts.path)
            frag = unquote(parts.fragment)
            if path.startswith("/"):
                prefix = "/SonicScrewdriver/"
                if p.base == prefix and path.startswith(prefix):
                    path = path[len(prefix):]
                else:
                    err(page, "root-relative link breaks under the project URL: " + url)
                    return
            # A page with <base href="/SonicScrewdriver/"> resolves everything from the site root; pages
            # without it are all in the root folder too, so the same resolution applies.
            full = os.path.normpath(os.path.join(root, path))
            if os.path.commonpath([root, full]) != root:
                err(page, "link leaves the site folder: " + url)
                return
            if os.path.isdir(full) or path == "" or path.endswith("/"):
                index = os.path.join(full, "index.html")
                if not os.path.isfile(index):
                    err(page, "link target has no index.html: " + url)
                    return
                full = index
            if not os.path.isfile(full):
                err(page, "%s target not found: %s" % ("image" if kind == "src" and re.search(r"\.(png|jpe?g|gif|svg|webp|ico|avif)$", path, re.I) else kind, url))
                return
            target = os.path.relpath(full, root).replace(os.sep, "/")
        if frag and frag != "top":
            if target not in parsed:
                if target.endswith(".html"):
                    err(page, "anchor target page is not parsed: " + url)
                return
            if frag not in parsed[target].ids:
                err(page, "anchor #%s not found in %s (link: %s)" % (frag, target, url))

    for name in pages:
        p = parsed[name]
        if p.h1 != 1:
            err(name, "expected exactly one <h1>, found %d" % p.h1)
        if not p.title:
            err(name, "missing or empty <title>")
        if not p.description or not p.description.strip():
            err(name, "missing or empty meta description")
        if not p.lang:
            err(name, "missing lang attribute on <html>")
        if p.alt_missing:
            err(name, "%d <img> without an alt attribute" % p.alt_missing)
        dups = sorted({i for i in p.ids if p.ids.count(i) > 1})
        if dups:
            err(name, "duplicate id(s): " + ", ".join(dups))
        for css in p.styles:
            for m in URL_RE.finditer(css):
                u = m.group(2).strip()
                if is_external(u):
                    err(name, "third-party url() in inline style: " + u)
                elif u and not u.startswith(SKIP_SCHEMES) and not u.startswith("#"):
                    check_local(name, p, "url()", u)
        for kind, url in p.links:
            url = url.strip()
            if not url or url.startswith(SKIP_SCHEMES):
                continue
            if is_external(url):
                if kind in ("src", "resource"):
                    err(name, "third-party host in %s: %s" % ("src" if kind == "src" else "stylesheet/icon link", url))
                else:
                    externals.setdefault(url, set()).add(name)
                continue
            if kind == "link":
                continue  # canonical etc. on the site itself are not files to check
            check_local(name, p, "link" if kind == "a" else kind, url)

    # Stylesheet and script: no third-party host, local url() targets exist.
    for asset in ("style.css", "site.js"):
        path = os.path.join(root, asset)
        if not os.path.isfile(path):
            continue
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
        allowed = SCRIPT_HOSTS if asset.endswith(".js") else set()
        for m in ABS_URL_RE.finditer(text):
            h = host_of(m.group(0) if not m.group(0).startswith("//") else "https:" + m.group(0))
            if h not in allowed:
                err(asset, "third-party host: " + m.group(0))
        if asset.endswith(".css"):
            for m in URL_RE.finditer(text):
                u = m.group(2).strip()
                if u and not is_external(u) and not u.startswith(SKIP_SCHEMES) and not u.startswith("#"):
                    full = os.path.normpath(os.path.join(root, unquote(urlsplit(u).path)))
                    if not os.path.isfile(full):
                        err(asset, "url() target not found: " + u)
            if re.search(r"@import\s", text, re.I):
                err(asset, "@import is not allowed")

    # Sitemap: every <loc> is a page of the site.
    sm = os.path.join(root, "sitemap.xml")
    if os.path.isfile(sm):
        with open(sm, encoding="utf-8") as fh:
            locs = re.findall(r"<loc>\s*(.*?)\s*</loc>", fh.read())
        base = "https://leroidubid.github.io/SonicScrewdriver/"
        listed = set()
        for loc in locs:
            if not loc.startswith(base):
                err("sitemap.xml", "unexpected URL: " + loc)
                continue
            rel = loc[len(base):] or "index.html"
            listed.add(rel)
            if not os.path.isfile(os.path.join(root, rel)):
                err("sitemap.xml", "page not found: " + loc)
        for name in pages:
            if name != "404.html" and name not in listed and not (name == "index.html" and "index.html" in listed):
                err("sitemap.xml", "page missing from the sitemap: " + name)

    # Report.
    print("Checked %d pages in %s" % (len(pages), root))
    if externals:
        print("\nExternal links (not checked, never fail the run):")
        for url in sorted(externals):
            note = ""
            if host_of(url) not in LINK_HOSTS:
                note = "  [note: not github.com]"
            print("  %s  (%s)%s" % (url, ", ".join(sorted(externals[url])), note))
    if errors:
        print("\n%d problem(s):" % len(errors))
        for e in errors:
            print("  FAIL " + e)
        return 1
    print("\nOK: no problem found.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
