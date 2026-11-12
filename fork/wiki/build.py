"""
Builds the wiki (docs/wiki/*.md) into site/wiki/*.html for the website.

    pip install markdown
    python fork/wiki/build.py

The markdown in docs/wiki stays the source, so the wiki still reads fine on GitHub too.
The sidebar order comes from the list on Home.md.
"""

import html
import re
import shutil
from pathlib import Path

import markdown

ROOT = Path(__file__).resolve().parents[2]
WIKI = ROOT / "docs" / "wiki"
IMAGES = ROOT / "docs" / "images"
OUT = ROOT / "site" / "wiki"
TEMPLATE = Path(__file__).with_name("template.html")

REPO = "https://github.com/PSYGATOR/PSYSTRAP"


def page_name(md_file: str) -> str:
    """Home.md -> index.html, Overlay.md -> Overlay.html"""
    stem = Path(md_file).stem
    return "index.html" if stem == "Home" else f"{stem}.html"


def read_order() -> list[tuple[str, str]]:
    """(file, title) in the order Home.md lists them."""
    home = (WIKI / "Home.md").read_text(encoding="utf-8")
    order = [("Home.md", "Home")]

    for title, target in re.findall(r"^- \[([^\]]+)\]\(([A-Za-z0-9-]+\.md)\)", home, re.M):
        if (WIKI / target).exists():
            order.append((target, title))

    # anything not listed on Home still gets built, at the end
    listed = {f for f, _ in order}
    for path in sorted(WIKI.glob("*.md")):
        if path.name not in listed:
            order.append((path.name, path.stem.replace("-", " ")))

    return order


def fix_links(body: str) -> str:
    def href(match: re.Match) -> str:
        url = match.group(2)

        if re.match(r"^(https?:|mailto:|#)", url):
            return match.group(0)

        path, _, anchor = url.partition("#")
        anchor = f"#{anchor}" if anchor else ""

        if path.endswith(".md") and "/" not in path:
            return f'{match.group(1)}="{page_name(path)}{anchor}"'

        # anything else in the repo (scripts, the license...) opens on GitHub
        target = (WIKI / path).resolve().relative_to(ROOT).as_posix()
        return f'{match.group(1)}="{REPO}/blob/main/{target}{anchor}"'

    body = re.sub(r'(href)="([^"]+)"', href, body)
    body = body.replace('src="../images/', 'src="images/')
    return body


def main() -> None:
    template = TEMPLATE.read_text(encoding="utf-8")
    order = read_order()

    if OUT.exists():
        shutil.rmtree(OUT)
    OUT.mkdir(parents=True)

    if IMAGES.exists():
        shutil.copytree(IMAGES, OUT / "images")

    for index, (md_file, title) in enumerate(order):
        source = (WIKI / md_file).read_text(encoding="utf-8")
        converter = markdown.Markdown(extensions=["tables", "fenced_code", "toc", "sane_lists"])
        body = fix_links(converter.convert(source))

        heading = re.search(r"^# (.+)$", source, re.M)
        page_title = heading.group(1).strip() if heading else title

        current = ' class="here" aria-current="page"'
        nav = "\n".join(
            f'<li><a href="{page_name(f)}"{current if f == md_file else ""}>{html.escape(t)}</a></li>'
            for f, t in order
        )

        prev_link = next_link = ""
        if index > 0:
            f, t = order[index - 1]
            prev_link = f'<a class="pn prev sketch" href="{page_name(f)}"><small>back</small>{html.escape(t)}</a>'
        if index < len(order) - 1:
            f, t = order[index + 1]
            next_link = f'<a class="pn next sketch alt" href="{page_name(f)}"><small>next</small>{html.escape(t)}</a>'

        page = (template
                .replace("{{title}}", html.escape(page_title if md_file != "Home.md" else "Wiki"))
                .replace("{{nav}}", nav)
                .replace("{{content}}", body)
                .replace("{{prev}}", prev_link)
                .replace("{{next}}", next_link)
                .replace("{{edit}}", f"{REPO}/edit/main/docs/wiki/{md_file}"))

        (OUT / page_name(md_file)).write_text(page, encoding="utf-8")

    print(f"Built {len(order)} wiki pages into {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
