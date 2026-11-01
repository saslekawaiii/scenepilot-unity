"""Build editable academic documents from Markdown and a retained official cover."""
import copy
import hashlib
import json
import re
import zipfile
import os
import argparse
from pathlib import Path
from docx import Document
from docx.enum.section import WD_SECTION_START
from docx.enum.style import WD_STYLE_TYPE
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_BREAK, WD_TAB_ALIGNMENT, WD_TAB_LEADER
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'deliverables'
BUILD = ROOT/'.private/build'
REFERENCE = ROOT/'thesis/templates/official-title.docx'
if not REFERENCE.exists():REFERENCE=ROOT/'.private/reference/official-title.docx'
TOPIC = 'Разработка интеллектуального инструментария для автоматизированного создания динамических игровых миров'
ASSIGNMENT = ROOT/'thesis/templates/official-assignment.docx'
TEXT_FILES = ['00-introduction.md','01-analysis.md','02-design.md','03-implementation.md','04-evaluation.md','05-conclusion.md']


def font(style, size, bold=False):
    style.font.name='Times New Roman'; style.font.size=Pt(size);style.font.bold=bold;style.font.color.rgb=RGBColor(0,0,0)
    style.element.get_or_add_rPr().get_or_add_rFonts().set(qn('w:eastAsia'),'Times New Roman')


def styles(doc):
    compat=doc.settings.element.find(qn('w:compat'))
    if compat is None:compat=OxmlElement('w:compat');doc.settings.element.append(compat)
    if compat.find(qn('w:doNotExpandShiftReturn')) is None:
        compat.append(OxmlElement('w:doNotExpandShiftReturn'))
    for name,size,bold in [('WWBody',14,False),('WWHeading1',14,True),('WWHeading2',14,True),('WWHeading3',14,True),('WWCaption',14,False),('WWTable',14,False),('WWTOC',14,False),('WWCode',14,False)]:
        if name not in doc.styles:doc.styles.add_style(name,WD_STYLE_TYPE.PARAGRAPH)
        s=doc.styles[name];font(s,size,bold);f=s.paragraph_format;f.space_after=Pt(0);f.space_before=Pt(0);f.line_spacing=1.5;f.widow_control=True
        if name=='WWBody':f.first_line_indent=Cm(1.5);s.paragraph_format.alignment=WD_ALIGN_PARAGRAPH.JUSTIFY
        if name.startswith('WWHeading'):
            f.keep_with_next=True;f.space_before=Pt(12);f.space_after=Pt(8);f.line_spacing=1.5;f.first_line_indent=Cm(1.5)
            outline=OxmlElement('w:outlineLvl');outline.set(qn('w:val'),str(int(name[-1])-1));s.element.get_or_add_pPr().append(outline)
        if name in ['WWTable','WWCaption','WWCode','WWTOC']:f.line_spacing=1.5;f.first_line_indent=Cm(0)
    doc.styles['WWCaption'].paragraph_format.alignment=WD_ALIGN_PARAGRAPH.CENTER
    doc.styles['WWCaption'].paragraph_format.space_after=Pt(8)
    doc.styles['WWCaption'].paragraph_format.keep_with_next=False
    doc.styles['WWTable'].paragraph_format.space_after=Pt(3)
    for name in ['WWBody','WWHeading1','WWHeading2','WWHeading3','WWCaption','WWTable','WWTOC']:
        lang=OxmlElement('w:lang');lang.set(qn('w:val'),'ru-RU');doc.styles[name].element.get_or_add_rPr().append(lang)


def body_section(doc):
    s=doc.add_section(WD_SECTION_START.NEW_PAGE);s.page_width=Cm(21);s.page_height=Cm(29.7);s.left_margin=Cm(3);s.right_margin=Cm(1);s.top_margin=Cm(1.5);s.bottom_margin=Cm(2)
    s.header_distance=Cm(.8);s.footer_distance=Cm(1);s.footer.is_linked_to_previous=False
    p=s.footer.paragraphs[0];p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.style=doc.styles['WWCaption']
    field=OxmlElement('w:fldSimple');field.set(qn('w:instr'),'PAGE');p._p.append(field)
    return s


def replace(p,text):
    if p.runs:
        p.runs[0].text=text
        for r in p.runs[1:]:r.text=''
    else:p.add_run(text)


def cover():
    assert hashlib.sha256(REFERENCE.read_bytes()).hexdigest()=='7bdb804346650358e5d7afb53018ae3d11abdbeb1ffc72d87a487f803a271afd'
    d=Document(REFERENCE);p=d.paragraphs
    slots={4:'Факультет электроники и вычислительной техники',5:'Кафедра САПР и ПК',10:'к выпускной квалификационной работе бакалавра на тему',12:'Разработка интеллектуального инструментария для',13:'автоматизированного создания динамических игровых миров',14:'Автор  __________________  Коваленко Кирилл Дмитриевич',18:'Группа           ИВТ-465',20:'Направление 09.03.01 Информатика и вычислительная техника',22:'Профиль «Разработка игровых и XR приложений»',38:'Волгоград 2027 г.'}
    for i,text in slots.items():replace(p[i],text)
    d.core_properties.author='Коваленко Кирилл Дмитриевич';d.core_properties.title=TOPIC
    return d


def append_assignment(doc):
    """Append the retained official form; only explicitly documented slots are filled."""
    manifest=json.loads((ROOT/'thesis/templates/manifest.json').read_text())['assignment']
    assert hashlib.sha256(ASSIGNMENT.read_bytes()).hexdigest()==manifest['normalized_sha256']
    source=Document(ASSIGNMENT)
    data=json.loads((ROOT/'thesis/assignment-content.json').read_text())
    p=source.paragraphs
    slots={4:'Кафедра '+data['department'],8:'Задание на выпускную квалификационную работу бакалавра',10:'Студент '+data['student'],12:'Код кафедры _____________________                Группа '+data['group'],13:'Тема Разработка интеллектуального инструментария для',14:'автоматизированного создания динамических игровых миров',15:'',22:data['input']}
    for i,title in zip(range(25,29),data['sections']):slots[i]=title
    for i,title in zip(range(43,67,2),data['graphics']):slots[i]=str((i-43)//2+1)+') '+title
    for i,text in slots.items():replace(p[i],text)
    # Namespace source styles so Word never resolves an assignment style to the cover's Normal.
    mapping={s.style_id:'WWAssignment_'+s.style_id for s in source.styles}
    for style in source.styles:
        node=copy.deepcopy(style.element);node.set(qn('w:styleId'),mapping[style.style_id])
        node.attrib.pop(qn('w:default'),None)
        for tag in ['basedOn','next','link']:
            for item in node.findall(qn('w:'+tag)):
                old=item.get(qn('w:val'))
                if old in mapping:item.set(qn('w:val'),mapping[old])
        name=node.find(qn('w:name'))
        if name is not None:name.set(qn('w:val'),'WWAssignment '+style.name)
        doc.styles.element.append(node)
    section=doc.add_section(WD_SECTION_START.NEW_PAGE)
    for field in ['page_width','page_height','left_margin','right_margin','top_margin','bottom_margin','header_distance','footer_distance']:
        setattr(section,field,getattr(source.sections[0],field))
    section.header.is_linked_to_previous=False;section.footer.is_linked_to_previous=False
    section.different_first_page_header_footer=False
    for element in source.element.body:
        if element.tag==qn('w:sectPr'):continue
        node=copy.deepcopy(element)
        for item in node.iter(qn('w:p')):
            props=item.find(qn('w:pPr'))
            if props is None:props=OxmlElement('w:pPr');item.insert(0,props)
            if props.find(qn('w:pStyle')) is None:
                default=OxmlElement('w:pStyle');default.set(qn('w:val'),'Normal');props.insert(0,default)
        for tag in ['pStyle','rStyle','tblStyle']:
            for item in node.iter(qn('w:'+tag)):
                old=item.get(qn('w:val'))
                if old in mapping:item.set(qn('w:val'),mapping[old])
        doc.element.body.insert(len(doc.element.body)-1,node)


def paragraph(doc,text,style='WWBody'):
    text=re.sub(r'`([^`]+)`',r'\1',text);text=re.sub(r'\*\*([^*]+)\*\*',r'\1',text)
    if style=='WWBody' and re.match(r'^\d+\. ',text) and ' — URL: ' in text:
        description,address=text.split(' — URL: ',1)
        p=doc.add_paragraph(style=style);p.paragraph_format.keep_together=True
        p.add_run(description.replace('[б. г.]','[б.\u00a0г.]'))
        p.add_run().add_break(WD_BREAK.LINE)
        p.add_run('—\u00a0URL:\u00a0'+address)
        return p
    p=doc.add_paragraph(text,style);return p

def anchor_name(title):
    return 'ww_'+hashlib.sha256(title.encode()).hexdigest()[:16]

def bookmark(p,title,identifier):
    start=OxmlElement('w:bookmarkStart');start.set(qn('w:id'),str(identifier));start.set(qn('w:name'),anchor_name(title))
    end=OxmlElement('w:bookmarkEnd');end.set(qn('w:id'),str(identifier))
    p._p.insert(1,start);p._p.append(end)

def math_node(name,*children):
    e=OxmlElement('m:'+name)
    for child in children:e.append(child)
    return e

def math_text(value):
    t=OxmlElement('m:t');t.text=value;return math_node('r',t)

def math_sub(base,index):
    return math_node('sSub',math_node('e',math_text(base)),math_node('sub',math_text(index)))

def math_sup(base,power):
    return math_node('sSup',math_node('e',base if hasattr(base,'tag') else math_text(base)),math_node('sup',math_text(power)))

def math_sum(index,upper,*terms):
    props=math_node('naryPr');chr=OxmlElement('m:chr');chr.set(qn('m:val'),'∑');props.append(chr)
    lim=OxmlElement('m:limLoc');lim.set(qn('m:val'),'undOvr');props.append(lim)
    return math_node('nary',props,math_node('sub',math_text(index)),math_node('sup',math_text(upper)),math_node('e',*terms))

def math_root(term):
    hide=OxmlElement('m:degHide');hide.set(qn('m:val'),'1')
    return math_node('rad',math_node('radPr',hide),math_node('deg'),math_node('e',term))

def equation(doc,kind):
    p=doc.add_paragraph(style='WWBody');p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.first_line_indent=Cm(0);p.paragraph_format.space_before=Pt(6);p.paragraph_format.space_after=Pt(6)
    expression=math_node('oMath')
    if kind=='cosine':
        expression.append(math_text('c(q,d) = '))
        numerator=math_sum('i = 1','D',math_sub('q','i'),math_sub('d','i'))
        denominator=[math_root(math_sum('i = 1','D',math_sup(math_sub('q','i'),'2'))),math_text(' · '),math_root(math_sum('i = 1','D',math_sup(math_sub('d','i'),'2')))]
        expression.append(math_node('f',math_node('num',numerator),math_node('den',*denominator)))
    elif kind=='fbm':
        expression.append(math_text('F(u,v) = '))
        terms=[math_sub('a','o'),math_text('N('),math_sub('f','o'),math_text('u,'),math_sub('f','o'),math_text('v,'),math_sub('s','o'),math_text(')')]
        expression.append(math_node('f',math_node('num',math_sum('o = 0','O − 1',*terms)),math_node('den',math_sum('o = 0','O − 1',math_sub('a','o')))))
    else:
        expression.extend([math_sup('h','p + 1'),math_text(' = 0,65'),math_sup('h','p'),math_text(' + 0,35 '),math_sub('avg','4'),math_text('('),math_sup('h','p'),math_text(')')])
    p._p.append(math_node('oMathPara',expression))
    return p


def table(doc,lines,title,number):
    cells=[ [s.strip() for s in l.strip().strip('|').split('|')] for l in lines]
    cells=[r for r in cells if not all(re.fullmatch(r':?-+:?',c or '-') for c in r)]
    p=paragraph(doc,f'Таблица {number} — {title}','WWCaption');p.alignment=WD_ALIGN_PARAGRAPH.LEFT;p.paragraph_format.keep_with_next=True
    t=doc.add_table(rows=1,cols=len(cells[0]));t.alignment=WD_TABLE_ALIGNMENT.CENTER;t.autofit=False
    total=16.5
    if cells[0][:2]==['Код','Требование']:widths=[2,14.5]
    elif len(cells[0])==3 and cells[0][0]=='Код':widths=[2,4.7,9.8]
    elif len(cells[0])==3 and cells[0][0]=='№':widths=[1,6,9.5]
    elif cells[0][:2]==['ID','Вопрос']:widths=[1.8,8,6.7]
    elif len(cells[0])==3 and cells[0][0]=='ID':widths=[1.8,5.2,9.5]
    elif len(cells[0])==4 and cells[0][0]=='№':widths=[.9,4.4,8.8,2.4]
    elif len(cells[0])==4 and cells[0][0]=='Код':widths=[1.7,4.9,4.8,5.1]
    elif len(cells[0])==5 and cells[0][0]=='Маршрут':widths=[5.2,1.8,3.5,3.4,2.6]
    elif len(cells[0])==4 and cells[0][0]=='Показатель':widths=[5.5,3.667,3.667,3.666]
    elif len(cells[0])==4 and cells[0][0]=='Набор':widths=[6,2.5,2.5,5.5]
    elif len(cells[0])==3:widths=[4.2,6.4,5.9]
    elif len(cells[0])==2:widths=[4.5,12]
    else:widths=[total/len(cells[0])]*len(cells[0])
    for c,w in zip(t.columns,widths):c.width=Cm(w)
    props=t._tbl.tblPr
    borders=OxmlElement('w:tblBorders')
    for side in ['top','left','bottom','right','insideH','insideV']:
        border=OxmlElement('w:'+side);border.set(qn('w:val'),'single');border.set(qn('w:sz'),'4');border.set(qn('w:color'),'D9D9D9');borders.append(border)
    props.append(borders)
    margins=OxmlElement('w:tblCellMar')
    for side in ['top','left','bottom','right']:
        el=OxmlElement('w:'+side);el.set(qn('w:w'),'85');el.set(qn('w:type'),'dxa');margins.append(el)
    props.append(margins)
    for i,row in enumerate(cells):
        cs=t.rows[0].cells if i==0 else t.add_row().cells
        for j,text in enumerate(row):
            cs[j].width=Cm(widths[j]);cs[j].vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER;p=cs[j].paragraphs[0];p.style=doc.styles['WWTable'];p.text=text
            if i>0 and (re.fullmatch(r'[\d\s.,/%мс]+',text) or text in ['Пройден','Не пройден']):p.alignment=WD_ALIGN_PARAGRAPH.CENTER
            if len(cells)<=6:p.paragraph_format.keep_with_next=i<len(cells)-1
            if i==0:
                fill=OxmlElement('w:shd');fill.set(qn('w:fill'),'E9ECEF');cs[j]._tc.get_or_add_tcPr().append(fill)
            for run in p.runs:run.bold=i==0
        trPr=t.rows[i]._tr.get_or_add_trPr();cant=OxmlElement('w:cantSplit');trPr.append(cant)
        if i==0:
            repeat=OxmlElement('w:tblHeader');trPr.append(repeat)
    doc.add_paragraph('',style='WWCaption')


def render_markdown(doc,text,counters,headings,break_chapters=True):
    lines=text.splitlines();i=0;section='Сведения';code=False;pending_caption=None
    while i<len(lines):
        line=lines[i].strip()
        if not line:i+=1;continue
        if line.startswith('```'):code=not code;i+=1;continue
        if code:paragraph(doc,line,'WWCode');i+=1;continue
        caption=re.fullmatch(r'Таблица[ \u00a0](\d+) — (.+)',line)
        if caption:
            if pending_caption is not None:raise ValueError('Table caption without table')
            pending_caption=(int(caption.group(1)),caption.group(2));i+=1;continue
        if line in ['[[MATH_COSINE]]','[[MATH_FBM]]','[[MATH_SMOOTH]]']:
            equation(doc,{'[[MATH_COSINE]]':'cosine','[[MATH_FBM]]':'fbm','[[MATH_SMOOTH]]':'smooth'}[line]);i+=1;continue
        if line.startswith('# '):
            title=line[2:];p=paragraph(doc,title,'WWHeading1');p.paragraph_format.page_break_before=break_chapters
            bookmark(p,title,len(headings)+1000)
            headings.append((title,1));section=title;i+=1;continue
        if line.startswith('## '):
            title=line[3:];p=paragraph(doc,title,'WWHeading2');bookmark(p,title,len(headings)+1000);headings.append((title,2));section=title;i+=1;continue
        if line.startswith('### '):
            paragraph(doc,line[4:],'WWHeading3');i+=1;continue
        if line.startswith('|'):
            rows=[]
            while i<len(lines) and lines[i].strip().startswith('|'):rows.append(lines[i]);i+=1
            counters['tables']+=1
            if pending_caption is None:raise ValueError('Rendered table without explicit caption')
            number,title=pending_caption;pending_caption=None
            if number!=counters['tables']:raise ValueError('Table caption numbering differs from table order')
            table(doc,rows,title,counters['tables']);continue
        image=re.fullmatch(r'!\[(.*?)\]\((.*?)\)',line)
        if image:
            title,path=image.groups();p=ROOT/path
            if not p.exists():raise FileNotFoundError(p)
            from PIL import Image
            with Image.open(p) as im:w,h=im.size
            # Native Word cropping shows genuine UI details; source image bytes remain unchanged.
            crops={'play-dialogue.png':(.174,.145,.435,.14),'toolkit-world.png':(0,.06,.40,.02),
                   'chat-npc-play-reply.png':(.504,.149,.192,.143),
                   'chat-scene-result.png':(.1653,.1269,.1810,.1269),
                   'chat-420.jpg':(0,110/1400,0,60/1400),
                   'village-letter-received.jpg':(500/2900,230/1676,1180/2900,396/1676),
                   'village-quest-completed.jpg':(500/2900,230/1676,1180/2900,396/1676)}
            crop=crops.get(p.name)
            ratio=(w*(1-crop[0]-crop[2]))/(h*(1-crop[1]-crop[3])) if crop else w/h
            width=min(17.0,19.5*ratio);pr=doc.add_paragraph();pr.alignment=WD_ALIGN_PARAGRAPH.CENTER;pr.paragraph_format.keep_with_next=True
            shape=pr.add_run().add_picture(str(p),width=Cm(width),height=Cm(width/ratio))
            if crop:
                rect=OxmlElement('a:srcRect')
                for key,value in zip(['l','t','r','b'],crop):rect.set(key,str(round(value*100000)))
                shape._inline.graphic.graphicData.pic.blipFill.insert(1,rect)
            shape._inline.docPr.set('descr',title)
            counters['figures']+=1;paragraph(doc,f"Рисунок {counters['figures']} — {title}",'WWCaption');i+=1;continue
        if line.startswith('- '):paragraph(doc,'— '+line[2:]);i+=1;continue
        parts=[line];i+=1
        while i<len(lines) and lines[i].strip() and not lines[i].strip().startswith(('#','|','![','```')):parts.append(lines[i].strip());i+=1
        paragraph(doc,' '.join(parts))
    if pending_caption is not None:raise ValueError('Table caption without table')


def catalog():
    data=json.loads((ROOT/'docs/research/sources.json').read_text())
    extra=ROOT/'thesis/supplemental-sources.json'
    if extra.exists():data+=json.loads(extra.read_text())
    result={str(r['id']):r for r in data}
    if len(result)!=len(data):raise ValueError('Duplicate source identifier')
    overrides=ROOT/'thesis/source-overrides.json'
    if overrides.exists():
        for key,fields in json.loads(overrides.read_text()).items():
            if key not in result:raise ValueError('Override without source '+key)
            result[key]={**result[key],**fields}
    return result


def citation_ids(content):
    """Read stable catalog IDs and an optional page locator from a square citation."""
    parts=re.split(r',\s*с\.\s*',content,maxsplit=1)
    tokens=[p.strip() for p in parts[0].split(',')]
    ids=[]
    for token in tokens:
        if re.fullmatch(r'\d+',token) or re.fullmatch(r'@[a-z0-9-]+',token):ids.append(token)
        elif re.fullmatch(r'\d+\s*[–-]\s*\d+',token):
            first,last=map(int,re.split(r'\s*[–-]\s*',token))
            if last<first or last-first>100:raise ValueError('Invalid source range')
            ids.extend(str(i) for i in range(first,last+1))
        else:return None
    return ids,parts[1] if len(parts)>1 else None


def renumber_citations(texts):
    known=catalog();order=[]
    def convert(match):
        parsed=citation_ids(match.group(1))
        if parsed is None:return match.group(0)
        ids,page=parsed
        for key in ids:
            if key not in known:raise ValueError('Unknown source ID '+key)
            if key not in order:order.append(key)
        return '['+', '.join(str(order.index(key)+1) for key in ids)+((', с. '+page) if page else '')+']'
    updated=[re.sub(r'\[([^\[\]\n]+)\]',convert,text) for text in texts]
    return updated,order


def bibliography(order):
    data=catalog();text='# СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ\n\n'
    for number,key in enumerate(order,1):
        r=data[key]
        accessed=r.get('accessed','2026-10-30');parts=accessed.split('-');accessed='.'.join(reversed(parts)) if len(parts)==3 else accessed
        author=r['author'].rstrip('.')
        description=r.get('description',f"{author}. {r['title']}. — {r['year']}.")
        text+=f"{number}. {description} — URL: {r['url']} (дата обращения: {accessed}). — Текст : электронный.\n\n"
    return text


def expanded_appendices():
    text=(ROOT/'thesis/06-appendices.md').read_text()
    tz=(ROOT/'thesis/technical-specification.md').read_text()
    def heading(match):
        depth,title=match.groups()
        return ('## ' if len(depth)==1 else '### ')+'А.'+title
    tz=re.sub(r'^(#{1,2}) (\d[^\n]*)$',heading,tz,flags=re.M)
    return text.replace('[[TECHNICAL_SPECIFICATION]]',tz)


def resolve_layout_references(texts):
    figures={};tables={};captions={}
    for text in texts:
        for path in re.findall(r'^!\[.*?\]\((.*?)\)$',text,flags=re.M):
            key=Path(path).stem
            if key in figures:raise ValueError('Duplicate figure reference key '+key)
            figures[key]=len(figures)+1
        pending=None;in_table=False
        for line in text.splitlines():
            tag=re.fullmatch(r'\[\[TABLE:([a-z0-9-]+)\|([^\]\n]+)\]\]',line.strip())
            if tag:
                if pending is not None:raise ValueError('Table key without table '+pending)
                captions[tag.group(1)]=tag.group(2).strip()
                pending=tag.group(1);in_table=False;continue
            if line.strip().startswith('|'):
                if not in_table:
                    if pending is None:raise ValueError('Table without a stable reference key')
                    if pending in tables:raise ValueError('Duplicate table key '+pending)
                    tables[pending]=len(tables)+1;pending=None
                in_table=True
            else:
                in_table=False
                if pending and line.strip():raise ValueError('Table key not immediately followed by table '+pending)
        if pending is not None:raise ValueError('Table key without table '+pending)
    def figure_reference(match):
        key=match.group(1)
        if key not in figures:raise ValueError('Unknown figure reference '+key)
        return 'рисунке\u00a0'+str(figures[key])
    def table_reference(match):
        key=match.group(1)
        if key not in tables:raise ValueError('Unknown table reference '+key)
        return 'таблице\u00a0'+str(tables[key])
    def table_caption(match):
        key=match.group(1)
        return 'Таблица\u00a0'+str(tables[key])+' — '+captions[key]+'\n'
    updated=[]
    for text in texts:
        text=re.sub(r'\[\[FIG:([a-z0-9-]+)\]\]',figure_reference,text)
        text=re.sub(r'\[\[TAB:([a-z0-9-]+)\]\]',table_reference,text)
        text=re.sub(r'^\[\[TABLE:([a-z0-9-]+)\|[^\]\n]+\]\]\s*\n',table_caption,text,flags=re.M)
        updated.append(text)
    return updated


def document_sources():
    raw=[(ROOT/'thesis'/name).read_text() for name in TEXT_FILES]
    raw.append(expanded_appendices())
    raw=resolve_layout_references(raw)
    if any('{{' in text for text in raw):raise ValueError('Unresolved content placeholder')
    texts,order=renumber_citations(raw)
    return texts[:-1],texts[-1],order


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--draft',action='store_true',help='Create review DOCX only; do not replace final deliverables')
    parser.add_argument('--sources-only',action='store_true',help='Refresh combined Markdown and source manifest without DOCX or PDF')
    parser.add_argument('--output-dir',type=Path,help='Explicit DOCX output directory')
    args=parser.parse_args()
    state=json.loads((ROOT/'thesis/build-status.json').read_text())
    if not args.draft and not args.sources_only and not (state.get('content_final') and state.get('metrics_final')):
        raise SystemExit('Final build blocked: content and actual 1.2 metrics are not confirmed. Use --sources-only or --draft for internal review.')
    output=args.output_dir or (ROOT/'.private/draft-vkr2026' if args.draft else OUT)
    BUILD.mkdir(parents=True,exist_ok=True)
    texts,appendices,order=document_sources()
    references=bibliography(order)
    complete='\n\n'.join(texts+[references,appendices])
    for marker,expression in {'[[MATH_COSINE]]':r'$$c(q,d)=\frac{\sum_{i=1}^{D}q_i d_i}{\sqrt{\sum_{i=1}^{D}q_i^2}\sqrt{\sum_{i=1}^{D}d_i^2}}$$','[[MATH_FBM]]':r'$$F(u,v)=\frac{\sum_{o=0}^{O-1}a_oN(f_ou,f_ov,s_o)}{\sum_{o=0}^{O-1}a_o}$$','[[MATH_SMOOTH]]':r'$$h^{p+1}=0.65h^p+0.35\operatorname{avg}_4(h^p)$$'}.items():
        complete=complete.replace(marker,expression)
    (ROOT/'thesis/ВКР_полный_текст.md').write_text(complete)
    source_manifest={'edition':state['edition'],'content_final':state['content_final'],'metrics_final':state['metrics_final'],'sources':[{'printed_number':i+1,'catalog_id':key,'title':catalog()[key]['title']} for i,key in enumerate(order)],'combined_markdown_sha256':hashlib.sha256(complete.encode()).hexdigest()}
    (BUILD/'source-manifest.json').write_text(json.dumps(source_manifest,ensure_ascii=False,indent=2))
    if args.sources_only:
        print('SOURCES_UPDATED',len(order),'cited references');return
    output.mkdir(parents=True,exist_ok=True)
    doc=cover();styles(doc);append_assignment(doc);body_section(doc)
    front,after=texts[0].split('# ПЕРЕЧЕНЬ СОКРАЩЕНИЙ',1)
    texts[0]='# ПЕРЕЧЕНЬ СОКРАЩЕНИЙ'+after
    titles=[]
    for text in texts+[references,appendices]:
        for line in text.splitlines():
            if line.startswith('# ') or line.startswith('## '):
                title=line.lstrip('#').strip()
                if title not in ['АННОТАЦИЯ','ABSTRACT','ПЕРЕЧЕНЬ СОКРАЩЕНИЙ']:
                    titles.append((title,2 if line.startswith('##') else 1))
    if len({title for title,_ in titles})!=len(titles):raise ValueError('Duplicate heading text')
    counters={'figures':0,'tables':0};headings=[]
    # Both short language versions share a page; the official program recommends half a page each.
    render_markdown(doc,front,counters,headings,break_chapters=False)
    paragraph(doc,'СОДЕРЖАНИЕ','WWHeading1').paragraph_format.page_break_before=True
    map_path=ROOT/'thesis/document-page-map.json'
    private_map=BUILD/'page-map.json'
    if private_map.exists():map_path=private_map
    pages=json.loads(map_path.read_text()) if map_path.exists() else {}
    missing=[title for title,_ in titles if title not in pages]
    if missing and not args.draft:raise ValueError('Final TOC page map is incomplete. Render draft and run map_document_pages.py first: '+str(missing))
    for title,level in titles:
        p=paragraph(doc,title+'\t'+str(pages.get(title,'—')),'WWTOC')
        p.paragraph_format.left_indent=Cm(.75 if level==2 else 0)
        p.paragraph_format.tab_stops.add_tab_stop(Cm(17),WD_TAB_ALIGNMENT.RIGHT,WD_TAB_LEADER.DOTS)
        link=OxmlElement('w:hyperlink');link.set(qn('w:anchor'),anchor_name(title))
        for run in list(p._p):
            if run.tag==qn('w:r'):p._p.remove(run);link.append(run)
        p._p.append(link)
    for text in texts:render_markdown(doc,text,counters,headings)
    render_markdown(doc,references,counters,headings)
    render_markdown(doc,appendices,counters,headings)
    update=OxmlElement('w:updateFields');update.set(qn('w:val'),'true');doc.settings.element.append(update)
    thesis_path=output/'ВКР_WorldWeaver.docx';doc.save(thesis_path)
    preserve_parts=['docProps/app.xml','_rels/.rels','word/theme/theme1.xml','word/fontTable.xml','word/numbering.xml']
    with zipfile.ZipFile(REFERENCE) as ref,zipfile.ZipFile(thesis_path) as current:
        contents={name:current.read(name) for name in current.namelist()}
        for name in preserve_parts:contents[name]=ref.read(name)
    temporary=thesis_path.with_suffix('.tmp.docx')
    with zipfile.ZipFile(temporary,'w',zipfile.ZIP_DEFLATED) as final:
        for name,data in contents.items():final.writestr(name,data)
    os.replace(temporary,thesis_path)
    tz=Document();styles(tz);s=tz.sections[0]
    s.page_width=Cm(21);s.page_height=Cm(29.7);s.left_margin=Cm(3);s.right_margin=Cm(1);s.top_margin=Cm(1.5);s.bottom_margin=Cm(2)
    for target in [tz.styles['Title'].element.get_or_add_pPr(),tz.styles['Normal'].element.get_or_add_pPr()]:
        for child in list(target):
            if child.tag==qn('w:pBdr'):target.remove(child)
    p=tz.add_paragraph('ТЕХНИЧЕСКОЕ ЗАДАНИЕ','Title');p.alignment=WD_ALIGN_PARAGRAPH.CENTER
    for run in p.runs:run.font.name='Times New Roman';run.font.size=Pt(14);run.font.color.rgb=RGBColor(0,0,0)
    for text in ['на разработку программного инструментария WorldWeaver',TOPIC,'Исполнитель Коваленко Кирилл Дмитриевич, ИВТ-465','ВолгГТУ, ФЭВТ, кафедра САПР и ПК','Согласование руководителя ВКР ____________________','Обозначение и реквизиты утверждения ____________________']:
        p=paragraph(tz,text);p.paragraph_format.first_line_indent=Cm(0);p.alignment=WD_ALIGN_PARAGRAPH.CENTER
    s.different_first_page_header_footer=True
    footer=s.footer.paragraphs[0];footer.alignment=WD_ALIGN_PARAGRAPH.CENTER;footer.style=tz.styles['WWCaption']
    page=OxmlElement('w:fldSimple');page.set(qn('w:instr'),'PAGE');footer._p.append(page)
    tz.add_page_break()
    (tz_text,),tz_order=renumber_citations(resolve_layout_references([(ROOT/'thesis/technical-specification.md').read_text()]))
    render_markdown(tz,tz_text,{'figures':0,'tables':0},[],break_chapters=False)
    # The standalone document has its own references; the appendix uses the thesis numbering.
    render_markdown(tz,bibliography(tz_order),{'figures':0,'tables':0},[],break_chapters=False)
    tz.core_properties.author='Коваленко Кирилл Дмитриевич';tz.save(output/'Техническое_задание_WorldWeaver.docx')
    manifest={'headings':titles,'figures':counters['figures'],'tables':counters['tables'],'sources':len(order),'edition':state['edition'],'draft':args.draft,'body_layout':{'margins_mm':{'left':30,'right':10,'top':15,'bottom':20},'indent_mm':15,'font':'Times New Roman','size_pt':14,'line_spacing':1.5},'official_assignment_pages':2,'source_manifest':source_manifest,'output_dir':str(output)}
    (BUILD/'document-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2))
    print('DOCX_CREATED',json.dumps({'output_dir':str(output),**counters},ensure_ascii=False))


if __name__=='__main__':main()
