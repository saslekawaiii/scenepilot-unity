#!/usr/bin/env python3
"""Build the standalone WorldWeaver 1.3 academic supplement, never the main thesis.

Use the bundled Python runtime returned by the Codex workspace dependency loader.
Default execution validates source/proof links and prints the intended design only.
Pass --create only after the agreed factual cutoff and artifact-operation marker.
PDF conversion and individual page review use the documents skill's render_docx.py.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import sys

from docx import Document
from docx.enum.style import WD_STYLE_TYPE
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT, WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Mm, Pt, RGBColor

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'docs/thesis-1.3-supplement.md'
OUTPUT = ROOT / 'deliverables/Дополнение_1.3_WorldWeaver.docx'
TITLE = 'Развитие инструментария WorldWeaver в версии 1.3'
FONT = 'Times New Roman'
TOKEN = re.compile(r'(\[[^\]]+\]\([^)]+\)|`[^`]+`|\*\*[^*]+\*\*)')
LINK = re.compile(r'\[([^\]]+)\]\(([^)]+)\)')
IMAGE = re.compile(r'!\[([^\]]*)\]\(([^)]+)\)')
DIGEST = re.compile(r'^[0-9a-f]{64}$')
DESIGN = {
    'paper': 'A4', 'font': FONT, 'body_size_pt': 14,
    'line_spacing': 1.5, 'first_line_indent_mm': 15,
    'margins_mm': {'left': 30, 'right': 10, 'top': 15, 'bottom': 20},
    'table_size_pt': 12, 'table_line_spacing': 1.0,
    'registry_size_pt': 12, 'registry_layout': 'numbered records; exact SHA-256 in two 32-character lines',
    'title': TITLE, 'official_administrative_fields': False,
    'main_thesis_changed': False,
    'dash_typography': 'ASCII hyphen in rendered prose; original evidence files and hashes remain unchanged',
    'figure_crop_scope': 'Two NPC figures use native Word crop of unchanged original PNG; captions disclose a fragment, full evidence PNG remains linked',
}


# Keep evidence images byte-identical; native Word crop shows the useful Game View
# region at readable print size. Values are exact inspected desktop pixel bounds.
FIGURE_CROPS = {
    'npc-fullhd-response-fit-r11.png': (1050, 470, 2180, 1330),
    'npc-640x480-response-visible-r11.png': (785, 360, 2070, 1325),
}


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def parse_rows(lines: list[str]) -> list[list[str]]:
    rows = []
    for line in lines:
        values = [x.strip() for x in line.strip().strip('|').split('|')]
        if all(re.fullmatch(r':?-{3,}:?', x) for x in values):
            continue
        rows.append(values)
    if not rows or len({len(row) for row in rows}) != 1:
        raise ValueError('Malformed Markdown table')
    return rows


def source_audit(source: Path, text: str) -> dict:
    if not source.is_file() or not text.strip():
        raise ValueError('Supplement Markdown is empty or absent')
    if any(line.rstrip() != line for line in text.splitlines()):
        raise ValueError('Trailing whitespace in supplement Markdown')
    references = LINK.findall(text)
    for label, target in references:
        if target.startswith(('https://', 'http://')):
            continue
        path = (source.parent / target.split('#', 1)[0]).resolve()
        if not path.is_file():
            raise ValueError(f'Unresolved local source link: {target}')
    proof_rows = re.findall(r'\]\(\.\./results/([^)]*)\) \| `([0-9a-f]{64})`', text)
    for name, digest in proof_rows:
        path = ROOT / 'results' / name
        if sha(path) != digest:
            raise ValueError(f'Proof digest differs from the Markdown registry: {name}')
    return {'markdown': str(source.relative_to(ROOT)), 'sha256': sha(source),
            'words': len(text.split()), 'links_checked': len(references),
            'registry_digests_checked': len(proof_rows)}


def set_font(style, size: float = 14, bold: bool = False) -> None:
    style.font.name = FONT
    style.font.size = Pt(size)
    style.font.bold = bold
    style.font.color.rgb = RGBColor(0, 0, 0)
    props = style.element.get_or_add_rPr()
    fonts = props.find(qn('w:rFonts'))
    if fonts is None:
        fonts = OxmlElement('w:rFonts'); props.insert(0, fonts)
    for attr in ('ascii', 'hAnsi', 'eastAsia', 'cs'):
        fonts.set(qn('w:' + attr), FONT)
    for attr in ('asciiTheme', 'hAnsiTheme', 'eastAsiaTheme', 'cstheme'):
        fonts.attrib.pop(qn('w:' + attr), None)
    lang = props.find(qn('w:lang'))
    if lang is None:
        lang = OxmlElement('w:lang'); props.append(lang)
    lang.set(qn('w:val'), 'ru-RU')


def style(doc: Document, name: str, size: float = 14, bold: bool = False):
    styles = doc.styles
    result = styles[name] if name in styles else styles.add_style(name, WD_STYLE_TYPE.PARAGRAPH)
    result.base_style = styles['Normal'] if name != 'Normal' else None
    set_font(result, size, bold)
    return result


def design(doc: Document) -> None:
    # The bundled starter template carries a theme-blue Title border. Remove
    # paragraph borders explicitly; table borders below are intentional.
    for paragraph_border in list(doc.styles.element.iter(qn('w:pBdr'))):
        paragraph_border.getparent().remove(paragraph_border)
    section = doc.sections[0]
    section.page_width, section.page_height = Mm(210), Mm(297)
    section.left_margin, section.right_margin = Mm(30), Mm(10)
    section.top_margin, section.bottom_margin = Mm(15), Mm(20)
    section.header_distance, section.footer_distance = Mm(5), Mm(10)
    normal = style(doc, 'Normal')
    pf = normal.paragraph_format
    pf.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
    pf.first_line_indent = Mm(15); pf.line_spacing = 1.5
    pf.space_before = Pt(0); pf.space_after = Pt(0); pf.widow_control = True
    title = style(doc, 'Title', 16, True)
    title.paragraph_format.alignment = WD_ALIGN_PARAGRAPH.CENTER
    title.paragraph_format.first_line_indent = Mm(0)
    title.paragraph_format.space_after = Pt(18)
    title.paragraph_format.line_spacing = 1.25
    title.paragraph_format.keep_with_next = True
    for name, before, after in [('Heading 1', 14, 8), ('Heading 2', 10, 6), ('Heading 3', 8, 4)]:
        s = style(doc, name, 14, True)
        pf = s.paragraph_format
        pf.alignment = WD_ALIGN_PARAGRAPH.LEFT; pf.first_line_indent = Mm(0)
        pf.space_before = Pt(before); pf.space_after = Pt(after)
        pf.keep_with_next = True; pf.keep_together = True; pf.line_spacing = 1.5
    for name, size in [('Supplement Table', 12), ('Supplement Reference', 12), ('Supplement Reference Title', 14)]:
        s = style(doc, name, size, name.endswith('Title'))
        pf = s.paragraph_format
        pf.first_line_indent = Mm(0); pf.alignment = WD_ALIGN_PARAGRAPH.LEFT
        pf.line_spacing = 1.0 if not name.endswith('Title') else 1.25
        pf.space_after = Pt(3)
        pf.widow_control = True
    doc.styles['Supplement Reference Title'].paragraph_format.keep_with_next = True
    caption = style(doc, 'Supplement Caption', 12)
    caption.paragraph_format.first_line_indent = Mm(0)
    caption.paragraph_format.alignment = WD_ALIGN_PARAGRAPH.CENTER
    caption.paragraph_format.line_spacing = 1.15
    caption.paragraph_format.space_before = Pt(4)
    caption.paragraph_format.space_after = Pt(8)
    caption.paragraph_format.keep_together = True
    table_caption = style(doc, 'Supplement Table Caption', 12)
    table_caption.paragraph_format.first_line_indent = Mm(0)
    table_caption.paragraph_format.alignment = WD_ALIGN_PARAGRAPH.LEFT
    table_caption.paragraph_format.line_spacing = 1.15
    table_caption.paragraph_format.space_before = Pt(6)
    table_caption.paragraph_format.space_after = Pt(5)
    table_caption.paragraph_format.keep_with_next = True
    # A plain centered page number is the only running element.
    p = section.footer.paragraphs[0]
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.paragraph_format.first_line_indent = Mm(0); p.paragraph_format.line_spacing = 1
    p.paragraph_format.space_after = Pt(0)
    r = p.add_run(); r.font.name = FONT; r.font.size = Pt(12)
    field = OxmlElement('w:fldSimple'); field.set(qn('w:instr'), 'PAGE')
    number = OxmlElement('w:r'); props = OxmlElement('w:rPr')
    fonts = OxmlElement('w:rFonts'); fonts.set(qn('w:ascii'), FONT); fonts.set(qn('w:hAnsi'), FONT)
    size = OxmlElement('w:sz'); size.set(qn('w:val'), '24')
    props.append(fonts); props.append(size); number.append(props)
    text = OxmlElement('w:t'); text.text = '1'
    number.append(text); field.append(number); r._r.addnext(field)
    settings = doc.settings.element
    update = settings.find(qn('w:updateFields'))
    if update is None:
        update = OxmlElement('w:updateFields'); settings.append(update)
    update.set(qn('w:val'), 'true')


def safe_plain(text: str) -> str:
    return LINK.sub(lambda m: m.group(1), text).replace('**', '').replace('`', '')


def link_target(target: str, source: Path, output: Path) -> str:
    if target.startswith(('https://', 'http://')):
        return target
    resolved = (source.parent / target).resolve()
    # Rendering uses a private staging folder, but the exact reviewed bytes are
    # delivered from OUTPUT.parent. Keep local evidence links portable there.
    return os.path.relpath(resolved, OUTPUT.parent).replace(os.sep, '/')


def hyperlink(paragraph, label: str, target: str) -> None:
    rel_id = paragraph.part.relate_to(
        target, 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink',
        is_external=True)
    h = OxmlElement('w:hyperlink'); h.set(qn('r:id'), rel_id)
    r = OxmlElement('w:r'); props = OxmlElement('w:rPr')
    color = OxmlElement('w:color'); color.set(qn('w:val'), '000000'); props.append(color)
    r.append(props); t = OxmlElement('w:t'); t.text = label; r.append(t); h.append(r)
    paragraph._p.append(h)


def add_text(paragraph, text: str, *, bold=False, code=False) -> None:
    # A native line break makes the full digest legible and copyable after joining lines.
    pieces = [text[:32], text[32:]] if DIGEST.fullmatch(text) else [text]
    for index, value in enumerate(pieces):
        if index: paragraph.add_run().add_break()
        r = paragraph.add_run(value)
        r.bold = bold
        if code:
            r.font.name = FONT  # Keep the academic font, not a theme monospace fallback.


def inline(paragraph, text: str, source: Path, output: Path) -> None:
    for token in TOKEN.split(text):
        if not token: continue
        m = LINK.fullmatch(token)
        if m:
            hyperlink(paragraph, m.group(1), link_target(m.group(2), source, output))
        elif token.startswith('**') and token.endswith('**'):
            add_text(paragraph, token[2:-2], bold=True)
        elif token.startswith('`') and token.endswith('`'):
            add_text(paragraph, token[1:-1], code=True)
        else:
            add_text(paragraph, token)


def heading(doc, text: str, depth: int = 1):
    # Markdown numbering is preserved; trailing punctuation/label colons are unnecessary in headings.
    label = text.rstrip('.').rstrip(':')
    p = doc.add_paragraph(label, style=f'Heading {min(depth, 3)}')
    return p


def border_table(table) -> None:
    props = table._tbl.tblPr
    borders = OxmlElement('w:tblBorders')
    for edge in ('top', 'left', 'bottom', 'right', 'insideH', 'insideV'):
        e = OxmlElement('w:' + edge)
        for key, value in [('val', 'single'), ('sz', '5'), ('color', 'D9D9D9')]:
            e.set(qn('w:' + key), value)
        borders.append(e)
    props.append(borders)
    margins = OxmlElement('w:tblCellMar')
    for name, width in [('top', 85), ('bottom', 85), ('left', 100), ('right', 100)]:
        element = OxmlElement('w:' + name); element.set(qn('w:w'), str(width)); element.set(qn('w:type'), 'dxa')
        margins.append(element)
    props.append(margins)


def add_table(doc, rows, source: Path, output: Path) -> None:
    columns = len(rows[0]); table = doc.add_table(rows=0, cols=columns)
    table.alignment = WD_TABLE_ALIGNMENT.CENTER; table.autofit = False
    widths = [40, 61, 69] if columns == 3 else [170 / columns] * columns
    for col, width in zip(table.columns, widths): col.width = Mm(width)
    border_table(table)
    for ri, values in enumerate(rows):
        row = table.add_row()
        pr = row._tr.get_or_add_trPr(); keep = OxmlElement('w:cantSplit'); pr.append(keep)
        if ri == 0:
            repeat = OxmlElement('w:tblHeader'); repeat.set(qn('w:val'), 'true'); pr.append(repeat)
        for ci, value in enumerate(values):
            cell = row.cells[ci]; cell.width = Mm(widths[ci]); cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
            p = cell.paragraphs[0]; p.style = doc.styles['Supplement Table']
            if ri == 0:
                fill = OxmlElement('w:shd'); fill.set(qn('w:fill'), 'F2F2F2'); cell._tc.get_or_add_tcPr().append(fill)
                inline(p, '**' + value + '**', source, output)
            else:
                inline(p, value, source, output)
    spacer = doc.add_paragraph(); spacer.paragraph_format.line_spacing = 1
    spacer.paragraph_format.space_after = Pt(0); spacer.paragraph_format.first_line_indent = Mm(0)
    spacer.add_run().font.size = Pt(4)


def registry(doc, rows, source: Path, output: Path, start_index: int = 0) -> int:
    data_rows = rows if LINK.fullmatch(rows[0][0]) else rows[1:]
    for index, row in enumerate(data_rows, start_index + 1):
        if len(row) != 3: raise ValueError('Expected three proof-registry columns')
        label, digest, scope = row
        m = LINK.fullmatch(label)
        if not m: raise ValueError('Registry entry requires a named file link')
        digest = digest.strip('`')
        if not DIGEST.fullmatch(digest): raise ValueError('Registry SHA-256 must be complete')
        p = doc.add_paragraph(style='Supplement Reference Title')
        hyperlink(p, f'{index}. {m.group(1)}', link_target(m.group(2), source, output))
        p = doc.add_paragraph(safe_plain(scope), style='Supplement Reference')
        p.paragraph_format.keep_with_next = True
        relative = m.group(2).removeprefix('../')
        p = doc.add_paragraph(style='Supplement Reference'); p.paragraph_format.keep_with_next = True
        p.add_run('Файл: ')
        # Natural break opportunities after delimiters, without changing the saved path string.
        for i, component in enumerate(re.split(r'(?<=/)|(?<=-)|(?<=\.)', relative)):
            if i and i % 7 == 0: p.add_run().add_break()
            p.add_run(component)
        p = doc.add_paragraph(style='Supplement Reference')
        p.add_run('SHA-256: ' + digest[:32]); p.add_run().add_break(); p.add_run(digest[32:])
        p.paragraph_format.left_indent = Mm(18); p.paragraph_format.first_line_indent = Mm(-18)
        p.paragraph_format.keep_together = True
        p.paragraph_format.space_after = Pt(9)
    return len(data_rows)


def native_figure_crop(picture, path: Path) -> dict | None:
    box = FIGURE_CROPS.get(path.name)
    if box is None:
        return None
    from docx.image.image import Image
    source_image = Image.from_file(str(path))
    width, height = source_image.px_width, source_image.px_height
    left, top, right, bottom = box
    if not (0 <= left < right <= width and 0 <= top < bottom <= height):
        raise ValueError(f'Invalid native crop: {path.name}')
    rect = OxmlElement('a:srcRect')
    for key, value in [('l', left / width), ('t', top / height),
                       ('r', (width-right) / width), ('b', (height-bottom) / height)]:
        rect.set(key, str(round(value * 100000)))
    fill = picture._inline.graphic.graphicData.pic.blipFill
    fill.insert(1, rect)
    # Shape dimensions follow the retained crop; all original PNG bytes remain
    # embedded in the DOCX and can be recovered through Reset Picture.
    picture.height = round(picture.width * (bottom-top) / (right-left))
    picture._inline.graphic.graphicData.pic.spPr.xfrm.ext.cy = picture.height
    return {'source_image': path.name, 'source_sha256': sha(path),
            'source_pixels': [width, height], 'retained_box_pixels': list(box),
            'native_word_crop_only': True, 'source_png_changed': False}


def build(source: Path, output: Path, text: str) -> tuple[Document, dict]:
    # Keep punctuation predictable across the DOCX/PDF font/rendering pipeline.
    # The source audit and linked raw evidence retain their original bytes.
    text = text.translate(str.maketrans({char: '-' for char in '\u2010\u2011\u2012\u2013\u2014\u2212'}))
    doc = Document(); design(doc)
    properties = doc.core_properties
    properties.title = TITLE; properties.subject = 'Дополнение к ВКР по неизменной теме разработки интеллектуального инструментария динамических игровых миров'
    properties.author = 'Коваленко Кирилл Дмитриевич'; properties.keywords = 'WorldWeaver; 1.3; ВКР; Unity; NPC'
    properties.created = datetime(2026, 11, 1, tzinfo=timezone.utc)
    properties.modified = properties.created
    doc.add_paragraph(TITLE, style='Title')
    lines = text.splitlines(); i = 0; table_count = 0; registry_count = 0; image_count = 0; in_registry = False
    figure_crops = []
    while i < len(lines):
        line = lines[i].strip()
        if not line or line.startswith('# '): i += 1; continue
        image_match = IMAGE.fullmatch(line)
        if image_match:
            label, target = image_match.groups()
            path = (source.parent / target).resolve()
            p = doc.add_paragraph()
            p.paragraph_format.first_line_indent = Mm(0)
            p.paragraph_format.alignment = WD_ALIGN_PARAGRAPH.CENTER
            p.paragraph_format.line_spacing = 1
            p.paragraph_format.keep_with_next = True
            picture = p.add_run().add_picture(str(path), width=Mm(170))
            picture._inline.docPr.set('descr', label)
            crop = native_figure_crop(picture, path)
            if crop: figure_crops.append(crop)
            doc.add_paragraph(label, style='Supplement Caption')
            image_count += 1; i += 1; continue
        if re.match(r'^Таблица \d+\s', line):
            p = doc.add_paragraph(style='Supplement Table Caption')
            inline(p, line, source, output)
            i += 1; continue
        if line.startswith('## '):
            value = line[3:]
            in_registry = bool(re.match(r'^6\.\s+Реестр', value))
            if in_registry:
                p = heading(doc, 'Приложение А Реестр свидетельств')
                p.paragraph_format.page_break_before = True
            else:
                p = heading(doc, value)
                if value == 'Abstract' or value.startswith('1. '):
                    p.paragraph_format.page_break_before = True
            i += 1; continue
        if line.startswith('### '): heading(doc, line[4:], 2); i += 1; continue
        if line.startswith('|'):
            block = []
            while i < len(lines) and lines[i].lstrip().startswith('|'):
                block.append(lines[i]); i += 1
            rows = parse_rows(block)
            if in_registry: registry_count += registry(doc, rows, source, output, registry_count)
            else: add_table(doc, rows, source, output); table_count += 1
            continue
        paragraph_lines = [line]; i += 1
        while i < len(lines) and lines[i].strip() and not lines[i].lstrip().startswith(('#', '|')):
            paragraph_lines.append(lines[i].strip()); i += 1
        p = doc.add_paragraph()
        inline(p, ' '.join(paragraph_lines), source, output)
    return doc, {'tables': table_count, 'images': image_count, 'registry_records': registry_count,
                 'figure_crops': figure_crops, 'headings': [p.text for p in doc.paragraphs if p.style.name.startswith('Heading')]}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=SOURCE)
    parser.add_argument('--output', type=Path, default=OUTPUT)
    parser.add_argument('--receipt', type=Path)
    parser.add_argument('--create', action='store_true', help='Actually write DOCX after the factual cutoff and operation marker')
    args = parser.parse_args()
    source, output = args.source.resolve(), args.output.resolve()
    if output.suffix.lower() != '.docx': parser.error('Output must be a .docx')
    if output.name != OUTPUT.name: parser.error('Use the standalone supplement filename; the main thesis must never be overwritten')
    if output == source: parser.error('Output cannot replace its Markdown source')
    text = source.read_text(encoding='utf-8'); audit = source_audit(source, text)
    summary = {'design': DESIGN, 'source_audit': audit, 'created': False, 'output': str(output)}
    if args.create:
        document, structure = build(source, output, text)
        output.parent.mkdir(parents=True, exist_ok=True); document.save(output)
        summary.update(created=True, structure=structure, docx_sha256=sha(output),
                       builder_sha256=sha(Path(__file__)), actual_build_utc=datetime.now(timezone.utc).isoformat())
        if args.receipt:
            args.receipt.parent.mkdir(parents=True, exist_ok=True)
            args.receipt.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
