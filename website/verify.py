from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import urlsplit
import json
root=Path(__file__).parent/'dist'
class Page(HTMLParser):
 def __init__(self):super().__init__();self.ids=[];self.urls=[];self.h1=0;self.lang=None
 def handle_starttag(self,tag,attrs):
  a=dict(attrs)
  if tag=='html':self.lang=a.get('lang')
  if tag=='h1':self.h1+=1
  if 'id' in a:self.ids.append(a['id'])
  for k in ('href','src'):
   if k in a:self.urls.append(a[k])
for p in root.rglob('*.html'):
 s=p.read_text(encoding='utf8');x=Page();x.feed(s)
 assert x.h1==1 and x.lang
 assert len(x.ids)==len(set(x.ids))
 assert 'Yikai Local' not in s
 for u in x.urls:
  if u.startswith('#'):assert u[1:] in x.ids,u
  elif u.startswith('/'):
   f=root/u.lstrip('/');f=f/'index.html' if f.is_dir() else f
   assert f.is_file(),str(f)
 print('PASS',p.relative_to(root), 'landmarks, anchors, language and local assets')
print('PASS public output contains only website assets; no executable downloads or update manifest')
assert not any(p.suffix in ('.exe','.php','.md','.ps1') for p in root.rglob('*'))
