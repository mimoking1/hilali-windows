import os, sys
from mediafire.client import MediaFireClient
f = sys.argv[1]
c = MediaFireClient()
c.login(email='qusa48@mail.instinct.com', password=os.environ['MF_PW'], app_id='42511')
c.upload_file(f, 'mf:/')
name = os.path.basename(f)
for it in c.get_folder_contents_iter('mf:/'):
    d = dict(it)
    if d.get('filename') == name:
        r = c.api.file_get_links(quick_key=d['quickkey'], link_type='normal_download')
        print('LINK', r['links'][0]['normal_download'], 'SIZE', d.get('size'), 'HASH', d.get('hash'))
