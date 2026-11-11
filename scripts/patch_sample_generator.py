"""Add a pool itemType to every question the sample-package generator emits.

The pool requires a semantic type per modality (CK_Items_Modality_Type) and refuses an item that does
not state one; a generated sample that omits it is a sample the product cannot import. The type is
derived from the section the question sits in, which is the fact the generator already knows.

Run once, after editing the generator:  python scripts/patch_sample_generator.py
"""

import io

PATH = "scripts/build_sample_packages.py"
NEEDLE = '              "questionType": "mcq",\n'

HELPER = '''
def item_type_for(section_code, has_passage):
    """The pool's semantic item type for a section.

    CK_Items_Modality_Type accepts a closed set per modality, so a question has to name one. It is
    derived from the section code, which is the only place the modality is stated.
    """
    prefix = (section_code or "")[:1].upper()
    if prefix == "R":
        return "passage_comprehension" if has_passage else "sentence_cloze"
    if prefix == "P":
        digits = "".join(ch for ch in section_code if ch.isdigit())
        if digits and int(digits) <= 4:
            return "short_exchange"
        return "passage_comprehension" if has_passage else "sentence_cloze"
    return {"L": "short_exchange", "S": "descriptive_monologue",
            "W": "transactional_correspondence"}.get(prefix, "sentence_cloze")

'''

source = io.open(PATH, encoding="utf-8").read()

if "def item_type_for(" not in source:
    lines = source.split("\n")
    last_import = max(i for i, ln in enumerate(lines) if ln.startswith(("import ", "from ")))
    lines.insert(last_import + 1, HELPER)
    source = "\n".join(lines)

replacement = (
    '              "questionType": "mcq",\n'
    '              "itemType": ITEM_TYPE_PLACEHOLDER,\n'
)

count = source.count(NEEDLE)
source = source.replace(NEEDLE, replacement)
source = source.replace(
    "ITEM_TYPE_PLACEHOLDER",
    'item_type_for(section["sectionCode"], bool(group.get("passage")))',
)

io.open(PATH, "w", encoding="utf-8", newline="\n").write(source)
print(f"added itemType to {count} question literal(s)")
