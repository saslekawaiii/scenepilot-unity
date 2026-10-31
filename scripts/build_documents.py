"""Build editable academic documents from Markdown and a retained official cover."""
import copy
import hashlib
import json
import re
import zipfile
import os
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


def font(style, size, bold=False):
    style.font.name='Times New Roman'; style.font.size=Pt(size);style.font.bold=bold;style.font.color.rgb=RGBColor(0,0,0)
    style.element.get_or_add_rPr().get_or_add_rFonts().set(qn('w:eastAsia'),'Times New Roman')


def styles(doc):
    for name,size,bold in [('WWBody',14,False),('WWHeading1',14,True),('WWHeading2',14,True),('WWCaption',12,False),('WWTable',11,False),('WWTOC',12,False),('WWCode',10,False)]:
        if name not in doc.styles:doc.styles.add_style(name,WD_STYLE_TYPE.PARAGRAPH)
        s=doc.styles[name];font(s,size,bold);f=s.paragraph_format;f.space_after=Pt(0);f.space_before=Pt(0);f.line_spacing=1.5;f.widow_control=True
        if name=='WWBody':f.first_line_indent=Cm(1.25);s.paragraph_format.alignment=WD_ALIGN_PARAGRAPH.JUSTIFY
        if name.startswith('WWHeading'):
            f.keep_with_next=True;f.space_before=Pt(12);f.space_after=Pt(8);f.line_spacing=1.15
            outline=OxmlElement('w:outlineLvl');outline.set(qn('w:val'),'0' if name.endswith('1') else '1');s.element.get_or_add_pPr().append(outline)
        if name in ['WWTable','WWCaption','WWCode','WWTOC']:f.line_spacing=1.0
    doc.styles['WWCaption'].paragraph_format.alignment=WD_ALIGN_PARAGRAPH.CENTER
    doc.styles['WWCaption'].paragraph_format.space_after=Pt(8)
    doc.styles['WWCaption'].paragraph_format.keep_with_next=False
    doc.styles['WWTable'].paragraph_format.space_after=Pt(3)
    for name in ['WWBody','WWHeading1','WWHeading2','WWCaption','WWTable','WWTOC']:
        lang=OxmlElement('w:lang');lang.set(qn('w:val'),'ru-RU');doc.styles[name].element.get_or_add_rPr().append(lang)


def body_section(doc):
    s=doc.add_section(WD_SECTION_START.NEW_PAGE);s.page_width=Cm(21);s.page_height=Cm(29.7);s.left_margin=Cm(3);s.right_margin=Cm(1.5);s.top_margin=Cm(2);s.bottom_margin=Cm(2)
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


def paragraph(doc,text,style='WWBody'):
    text=re.sub(r'`([^`]+)`',r'\1',text);text=re.sub(r'\*\*([^*]+)\*\*',r'\1',text)
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
    if cells[0][:2]==['ID','Вопрос']:widths=[1.8,8,6.7]
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
    lines=text.splitlines();i=0;section='Сведения';code=False
    while i<len(lines):
        line=lines[i].strip()
        if not line:i+=1;continue
        if line.startswith('```'):code=not code;i+=1;continue
        if code:paragraph(doc,line,'WWCode');i+=1;continue
        if line in ['[[MATH_COSINE]]','[[MATH_FBM]]','[[MATH_SMOOTH]]']:
            equation(doc,{'[[MATH_COSINE]]':'cosine','[[MATH_FBM]]':'fbm','[[MATH_SMOOTH]]':'smooth'}[line]);i+=1;continue
        if line.startswith('# '):
            title=line[2:];p=paragraph(doc,title,'WWHeading1');p.paragraph_format.page_break_before=break_chapters
            bookmark(p,title,len(headings)+1000)
            headings.append((title,1));section=title;i+=1;continue
        if line.startswith('## '):
            title=line[3:];p=paragraph(doc,title,'WWHeading2');bookmark(p,title,len(headings)+1000);headings.append((title,2));section=title;i+=1;continue
        if line.startswith('|'):
            rows=[]
            while i<len(lines) and lines[i].strip().startswith('|'):rows.append(lines[i]);i+=1
            counters['tables']+=1;title=re.sub(r'^\d+(?:\.\d+)*\s+','',section)
            table(doc,rows,title,counters['tables']);continue
        image=re.fullmatch(r'!\[(.*?)\]\((.*?)\)',line)
        if image:
            title,path=image.groups();p=ROOT/path
            if not p.exists():raise FileNotFoundError(p)
            from PIL import Image
            with Image.open(p) as im:w,h=im.size
            # Crop the real dialogue pane natively in DrawingML; preserve the source PNG.
            crops={'play-dialogue.png':(.174,.145,.435,.14),'toolkit-world.png':(0,.06,.40,.02),
                   'chat-npc-play-reply.png':(.504,.149,.192,.143),
                   'chat-scene-result.png':(.1653,.1269,.1810,.1269)}
            crop=crops.get(p.name)
            ratio=(w*(1-crop[0]-crop[2]))/(h*(1-crop[1]-crop[3])) if crop else w/h
            width=min(16.2,19.5*ratio);pr=doc.add_paragraph();pr.alignment=WD_ALIGN_PARAGRAPH.CENTER;pr.paragraph_format.keep_with_next=True
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


def bibliography():
    data=json.loads((ROOT/'docs/research/sources.json').read_text());text='# СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ\n\n'
    for r in data:
        accessed=r.get('accessed','2026-10-30');parts=accessed.split('-');accessed='.'.join(reversed(parts)) if len(parts)==3 else accessed
        author=r['author'].rstrip('.')
        text+=f"{r['id']}. {author}. {r['title']}. — {r['year']}. — URL: {r['url']} (дата обращения: {accessed}). — Текст: электронный.\n\n"
    return text


def main():
    OUT.mkdir(exist_ok=True);BUILD.mkdir(parents=True,exist_ok=True)
    doc=cover();styles(doc);body_section(doc)
    texts=[(ROOT/'thesis'/n).read_text() for n in ['00-introduction.md','01-analysis.md','02-design.md','03-implementation.md','04-evaluation.md','05-conclusion.md']]
    if any('{{' in t for t in texts):raise ValueError('Unresolved document placeholder')
    annotation,after_annotation=texts[0].split('# ПЕРЕЧЕНЬ СОКРАЩЕНИЙ',1)
    texts[0]='# ПЕРЕЧЕНЬ СОКРАЩЕНИЙ'+after_annotation
    # A static, verified TOC avoids blank cached Word fields in the delivered file.
    titles=[]
    for text in texts:
        for l in text.splitlines():
            if l.startswith('# ') or l.startswith('## '):
                title=l.lstrip('#').strip()
                if title not in ['АННОТАЦИЯ','ПЕРЕЧЕНЬ СОКРАЩЕНИЙ']:titles.append((title,2 if l.startswith('##') else 1))
    titles += [('СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ',1)]
    appendices=(ROOT/'thesis/06-appendices.md').read_text()
    titles += [(line[2:],1) for line in appendices.splitlines() if line.startswith('# ')]
    counters={'figures':0,'tables':0};headings=[]
    render_markdown(doc,annotation,counters,headings)
    first=next(p for p in doc.paragraphs if p.text.startswith('Выпускная квалификационная работа посвящена'))
    stat=first.insert_paragraph_before(style='WWBody');stat.add_run('Объём пояснительной записки — ')
    count=OxmlElement('w:fldSimple');count.set(qn('w:instr'),'NUMPAGES');run=OxmlElement('w:r');value=OxmlElement('w:t')
    stats_path=ROOT/'thesis/document-statistics.json'
    value.text=str(json.loads(stats_path.read_text()).get('pages',74)) if stats_path.exists() else '74'
    run.append(value);count.append(run);stat._p.append(count)
    statistics_run=stat.add_run(' с.')
    paragraph(doc,'СОДЕРЖАНИЕ','WWHeading1')
    doc.paragraphs[-1].paragraph_format.page_break_before=True
    map_path=BUILD/'page-map.json' if (BUILD/'page-map.json').exists() else ROOT/'thesis/document-page-map.json'
    pages=json.loads(map_path.read_text()) if map_path.exists() else {}
    for title,level in titles:
        p=paragraph(doc,title+'\t'+str(pages.get(title,'00')),'WWTOC');p.paragraph_format.left_indent=Cm(.4 if level==2 else 0);p.paragraph_format.tab_stops.add_tab_stop(Cm(16.5),WD_TAB_ALIGNMENT.RIGHT,WD_TAB_LEADER.DOTS);p.paragraph_format.space_after=Pt(1)
        link=OxmlElement('w:hyperlink');link.set(qn('w:anchor'),anchor_name(title))
        for run in list(p._p):
            if run.tag==qn('w:r'):p._p.remove(run);link.append(run)
        p._p.append(link)
    for text in texts:render_markdown(doc,text,counters,headings)
    render_markdown(doc,bibliography(),counters,headings)
    render_markdown(doc,appendices,counters,headings)
    source_count=len(json.loads((ROOT/'docs/research/sources.json').read_text()))
    source_word='источник' if source_count%10==1 and source_count%100!=11 else 'источника' if source_count%10 in (2,3,4) and source_count%100 not in (12,13,14) else 'источников'
    statistics_run.text=f" с., {counters['figures']} рис., {counters['tables']} табл., {source_count} {source_word}, 4 приложения."
    # Preserve the template's opaque source parts; new body styles and footer are intentional.
    settings=doc.settings.element
    update=OxmlElement('w:updateFields');update.set(qn('w:val'),'true');settings.append(update)
    thesis_path=OUT/'ВКР_WorldWeaver.docx'
    doc.save(thesis_path)
    preserve_parts=['docProps/app.xml','_rels/.rels','word/theme/theme1.xml','word/fontTable.xml','word/numbering.xml']
    with zipfile.ZipFile(REFERENCE) as ref,zipfile.ZipFile(thesis_path) as current:
        contents={name:current.read(name) for name in current.namelist()}
        for name in preserve_parts:contents[name]=ref.read(name)
    temporary=thesis_path.with_suffix('.tmp.docx')
    with zipfile.ZipFile(temporary,'w',zipfile.ZIP_DEFLATED) as final:
        for name,data in contents.items():final.writestr(name,data)
    os.replace(temporary,thesis_path)
    tz=Document();styles(tz);s=tz.sections[0];s.page_width=Cm(21);s.page_height=Cm(29.7);s.left_margin=Cm(3);s.right_margin=Cm(1.5);s.top_margin=Cm(2);s.bottom_margin=Cm(2)
    for target in [tz.styles['Title'].element.get_or_add_pPr(),tz.styles['Normal'].element.get_or_add_pPr()]:
        for child in list(target):
            if child.tag==qn('w:pBdr'):target.remove(child)
    p=tz.add_paragraph('ТЕХНИЧЕСКОЕ ЗАДАНИЕ','Title');p.alignment=WD_ALIGN_PARAGRAPH.CENTER
    border=p._p.get_or_add_pPr().find(qn('w:pBdr'))
    if border is not None:border.getparent().remove(border)
    for r in p.runs:r.font.name='Times New Roman';r.font.size=Pt(20);r.font.color.rgb=RGBColor(0,0,0)
    paragraph(tz,'на разработку программного инструментария WorldWeaver')
    paragraph(tz,TOPIC);paragraph(tz,'Исполнитель Коваленко Кирилл Дмитриевич, ИВТ-465');paragraph(tz,'ВолгГТУ, ФЭВТ, кафедра САПР и ПК');paragraph(tz,'Редакция от 31 октября 2026 года')
    paragraph(tz,'Согласование руководителя ВКР ____________________');paragraph(tz,'Обозначение и реквизиты утверждения ____________________')
    for p in tz.paragraphs[1:]:p.alignment=WD_ALIGN_PARAGRAPH.LEFT;p.paragraph_format.first_line_indent=Cm(0)
    s.different_first_page_header_footer=True
    footer=s.footer.paragraphs[0];footer.alignment=WD_ALIGN_PARAGRAPH.CENTER;footer.style=tz.styles['WWCaption']
    page=OxmlElement('w:fldSimple');page.set(qn('w:instr'),'PAGE');footer._p.append(page)
    tz.add_page_break()
    render_markdown(tz,(ROOT/'thesis/technical-specification.md').read_text(),{'figures':0,'tables':0},[],break_chapters=False)
    tz.core_properties.author='Коваленко Кирилл Дмитриевич';tz.save(OUT/'Техническое_задание_WorldWeaver.docx')
    complete=annotation+'\n'+'\n'.join(texts)+'\n'+bibliography()+'\n'+appendices
    math_markdown={'[[MATH_COSINE]]':r'$$c(q,d)=\frac{\sum_{i=1}^{D}q_i d_i}{\sqrt{\sum_{i=1}^{D}q_i^2}\sqrt{\sum_{i=1}^{D}d_i^2}}$$','[[MATH_FBM]]':r'$$F(u,v)=\frac{\sum_{o=0}^{O-1}a_oN(f_ou,f_ov,s_o)}{\sum_{o=0}^{O-1}a_o}$$','[[MATH_SMOOTH]]':r'$$h^{p+1}=0.65h^p+0.35\operatorname{avg}_4(h^p)$$'}
    for marker,expression in math_markdown.items():complete=complete.replace(marker,expression)
    (ROOT/'thesis/ВКР_полный_текст.md').write_text(complete)
    (BUILD/'document-manifest.json').write_text(json.dumps({'headings':titles,'figures':counters['figures'],'tables':counters['tables']},ensure_ascii=False,indent=2))
    print('DOCX_CREATED',counters)


if __name__=='__main__':main()
