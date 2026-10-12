"""Generate docs/database-erd.drawio from the live schema metadata.

Usage (from the repo root, Git Bash):
  bash docs/schema-tests/reset-and-load.sh
  MSYS2_ARG_CONV_EXCL='*' sqlcmd -E -S "(localdb)\\MSSQLLocalDB" -d CompanionSchemaTest -I -y 0 -f 65001 \
      -i docs/erd/export-meta.sql -o docs/erd/meta.json
  python docs/erd/gen-drawio.py
  "/c/Program Files/draw.io/draw.io.exe" -x -f png -p 1 -o docs/database-erd.png docs/database-erd.drawio

Page 1 shows every table grouped by domain. Each following page is one domain: its tables in full,
plus the tables it references from other domains as grey stubs (name only).
"""
import json, os, re
from xml.sax.saxutils import escape

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
META = os.path.join(ROOT, "docs", "erd", "meta.json")
SCHEMA = os.path.join(ROOT, "docs", "database-schema.sql")
OUT = os.path.join(ROOT, "docs", "database-erd.drawio")

COL_W, ROW_H, HEAD_H, GAP_X, GAP_Y, PAD = 250, 20, 28, 40, 30, 30
PER_ROW = 4  # tables per row inside a domain group
COLORS = [("#dae8fc", "#6c8ebf"), ("#d5e8d4", "#82b366"), ("#fff2cc", "#d6b656"), ("#f8cecc", "#b85450"),
          ("#e1d5e7", "#9673a6"), ("#ffe6cc", "#d79b00"), ("#f5f5f5", "#666666"), ("#fad9d5", "#ae4132"),
          ("#b1ddf0", "#10739e"), ("#d0cee2", "#56517e")]


def domains_from_schema():
    """Map table -> domain title using the '-- ==== Domain N: Name ====' headers in the script."""
    owner, current = {}, "Other"
    for line in open(SCHEMA, encoding="utf-8"):
        m = re.match(r"--\s*=+\s*(Domain \d+: [^=]+?)\s*=+", line)
        if m:
            current = m.group(1)
        m = re.match(r"CREATE TABLE (\w+)", line)
        if m:
            owner[m.group(1)] = current
    return owner


def domain_order(name):
    m = re.match(r"Domain (\d+)", name)
    return int(m.group(1)) if m else 99


class Page:
    def __init__(self, pid, name):
        self.pid, self.name, self.cells, self.n = pid, name, [], 0

    def nid(self, prefix):
        self.n += 1
        return f"{self.pid}-{prefix}{self.n}"

    def add(self, xml):
        self.cells.append(xml)

    def xml(self):
        body = "\n".join(self.cells)
        return (f'  <diagram id="{self.pid}" name="{escape(self.name)}">\n'
                f'    <mxGraphModel grid="1" gridSize="10" guides="1" connect="1" arrows="1" page="0" math="0">\n'
                f'      <root>\n        <mxCell id="{self.pid}-0"/>\n'
                f'        <mxCell id="{self.pid}-1" parent="{self.pid}-0"/>\n{body}\n'
                f'      </root>\n    </mxGraphModel>\n  </diagram>')


def table_height(t, stub):
    return HEAD_H if stub else HEAD_H + ROW_H * len(t["columns"])


def draw_table(page, t, x, y, fill, stroke, stub=False):
    """A swimlane whose children are one text row per column. Returns the swimlane id."""
    tid = page.nid("t")
    h = table_height(t, stub)
    style = (f"swimlane;fontStyle=1;childLayout=stackLayout;horizontal=1;startSize={HEAD_H};"
             f"horizontalStack=0;resizeParent=1;collapsible=0;marginBottom=0;html=1;rounded=0;"
             f"fillColor={'#eeeeee' if stub else fill};strokeColor={'#999999' if stub else stroke};"
             f"fontColor={'#666666' if stub else '#000000'};")
    page.add(f'        <mxCell id="{tid}" value="{escape(t["table"])}" style="{style}" vertex="1" '
             f'parent="{page.pid}-1"><mxGeometry x="{x}" y="{y}" width="{COL_W}" height="{h}" as="geometry"/></mxCell>')
    if stub:
        return tid
    for i, c in enumerate(t["columns"]):
        key = "PK " if c["pk"] else ("FK " if c["fk"] else "")
        label = f'{key}{c["name"]}: {c["type"]}{"" if c["nullable"] else " NN"}'
        bold = "fontStyle=5;" if c["pk"] else ("fontStyle=2;" if c["fk"] else "")
        page.add(f'        <mxCell id="{page.nid("c")}" value="{escape(label)}" '
                 f'style="text;html=1;align=left;verticalAlign=middle;spacingLeft=6;fontSize=11;{bold}" '
                 f'vertex="1" parent="{tid}"><mxGeometry y="{HEAD_H + i * ROW_H}" width="{COL_W}" '
                 f'height="{ROW_H}" as="geometry"/></mxCell>')
    return tid


def draw_group(page, title, tables, x, y, fill, stroke, stubs=()):
    """Lay tables of one domain in rows of PER_ROW inside a labelled background box.
    Returns ({table name: cell id}, width, height)."""
    items = [(t, False) for t in tables] + [(t, True) for t in stubs]
    rows = [items[i:i + PER_ROW] for i in range(0, len(items), PER_ROW)]
    width = PAD * 2 + min(len(items), PER_ROW) * (COL_W + GAP_X) - GAP_X
    heights = [max(table_height(t, s) for t, s in row) for row in rows]
    height = PAD + 30 + sum(heights) + GAP_Y * (len(rows) - 1) + PAD
    page.add(f'        <mxCell id="{page.nid("g")}" value="{escape(title)}" '
             f'style="rounded=1;arcSize=2;html=1;fillColor={fill};strokeColor={stroke};opacity=35;'
             f'verticalAlign=top;align=left;spacingLeft=12;spacingTop=6;fontSize=16;fontStyle=1;" vertex="1" '
             f'parent="{page.pid}-1"><mxGeometry x="{x}" y="{y}" width="{width}" height="{height}" as="geometry"/></mxCell>')
    ids, cy = {}, y + PAD + 30
    for row, rh in zip(rows, heights):
        cx = x + PAD
        for t, stub in row:
            ids[t["table"]] = draw_table(page, t, cx, cy, fill, stroke, stub)
            cx += COL_W + GAP_X
        cy += rh + GAP_Y
    return ids, width, height


def draw_edges(page, fks, ids, owned=None, labels=True):
    """One crow's-foot edge per (child, parent, column). With `owned`, only edges whose child table is
    owned by the page are drawn, so stub-to-stub relationships from other domains stay off it.
    Without labels, edges are also collapsed to one per table pair (the overview)."""
    seen = set()
    for fk in fks:
        if owned is not None and fk["from"] not in owned:
            continue
        a, b = ids.get(fk["from"]), ids.get(fk["to"])
        key = (a, b, fk["col"]) if labels else (a, b)
        if not a or not b or a == b or key in seen:
            continue
        seen.add(key)
        value = escape(fk["col"]) if labels else ""
        page.add(f'        <mxCell id="{page.nid("e")}" value="{value}" style="edgeStyle=entityRelationEdgeStyle;'
                 f'html=1;endArrow=ERmandOne;startArrow=ERmany;endFill=0;startFill=0;'
                 f'strokeColor={"#7a7a7a" if labels else "#b0b0b0"};strokeWidth=1;fontSize=9;fontColor=#555555;'
                 f'labelBackgroundColor=#ffffff;" edge="1" parent="{page.pid}-1" source="{a}" target="{b}">'
                 f'<mxGeometry relative="1" as="geometry"/></mxCell>')


def main():
    meta = json.loads(open(META, encoding="utf-8-sig").read().strip())
    tables = {t["table"]: t for t in meta["tables"]}
    owner = domains_from_schema()
    by_domain = {}
    for name in sorted(tables):
        by_domain.setdefault(owner.get(name, "Other"), []).append(tables[name])
    order = sorted(by_domain, key=domain_order)
    color = {d: COLORS[i % len(COLORS)] for i, d in enumerate(order)}
    pages = []

    # Page 1: overview. Every table as a name-only box, grouped by domain, with every FK edge.
    ov = Page("overview", "Overview (all domains)")
    ids, x, y, row_h = {}, 40, 40, 0
    for d in order:
        fill, stroke = color[d]
        got, w, h = draw_group(ov, d, [], x, y, fill, stroke, stubs=by_domain[d])
        ids.update(got)
        row_h = max(row_h, h)
        x += w + 80
        if x > 3200:
            x, y, row_h = 40, y + row_h + 80, 0
    draw_edges(ov, meta["fks"], ids, labels=False)
    pages.append(ov)

    # One page per domain: its tables in full, plus referenced outside tables as stubs.
    for d in order:
        fill, stroke = color[d]
        own = by_domain[d]
        own_names = {t["table"] for t in own}
        outside = sorted({fk["to"] for fk in meta["fks"] if fk["from"] in own_names and fk["to"] not in own_names})
        p = Page(re.sub(r"\W+", "-", d.lower()).strip("-"), d)
        got, _, _ = draw_group(p, d, own, 40, 40, fill, stroke, stubs=[tables[n] for n in outside])
        draw_edges(p, meta["fks"], got, owned=own_names)
        pages.append(p)

    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write('<mxfile host="app.diagrams.net" type="device">\n')
        f.write("\n".join(pg.xml() for pg in pages))
        f.write("\n</mxfile>\n")
    print(f"{len(tables)} tables, {len(meta['fks'])} FKs, {len(pages)} pages -> {os.path.relpath(OUT, ROOT)}")


if __name__ == "__main__":
    main()
