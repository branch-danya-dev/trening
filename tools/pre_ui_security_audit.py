"""Small reproducible disclosure/vendor check; not a penetration test or legal opinion."""
from pathlib import Path
import hashlib, json, re, sys

root=Path(__file__).resolve().parent.parent
checks=[]
for directory in [root/'src/WorkoutCalculator.Web/wwwroot/lib/three', root/'src/WorkoutCalculator.Web/wwwroot/lib/mediapipe-1.1.0']:
    readme=(directory/'README.md').read_text(encoding='utf-8')
    for name, expected in re.findall(r'\| `([^`]+)` \| [^\n]+? \| `([a-f0-9]{64})` \|', readme):
        actual=hashlib.sha256((directory/name).read_bytes()).hexdigest()
        if actual!=expected: raise SystemExit('Vendor checksum differs: '+name)
        checks.append({'file':str((directory/name).relative_to(root)), 'sha256':actual})
    if not (directory/'LICENSE').is_file(): raise SystemExit('Missing vendor license')

secret=re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:ghp_|github_pat_)[A-Za-z0-9_]{24,}|\bsk-[A-Za-z0-9]{32,}')
remote=re.compile(r'fetch\s*\(\s*[\'\"]https?://|sendBeacon\s*\(|new\s+WebSocket\s*\(')
issues=[]; count=0
for base in [root/'src',root/'.github']:
    for file in base.rglob('*'):
        if not file.is_file() or {'bin','obj','lib'}.intersection(file.parts) or file.suffix not in {'.cs','.razor','.js','.mjs','.yml','.json','.html','.csproj'}:continue
        text=file.read_text(encoding='utf-8-sig');count+=1
        if secret.search(text): issues.append({'path':str(file.relative_to(root)),'kind':'credential-pattern'})
        if file.suffix in {'.js','.mjs'} and remote.search(text):issues.append({'path':str(file.relative_to(root)),'kind':'explicit-remote-runtime-call'})
result={'filesChecked':count,'vendorHashes':checks,'issues':issues,'limits':'Static narrow patterns and pinned vendor hashes; no claim of comprehensive security or legal certification.'}
if len(sys.argv)>1:Path(sys.argv[1]).write_text(json.dumps(result,indent=2),encoding='utf-8')
if issues:raise SystemExit(json.dumps(issues))
print('Pre-UI vendor/disclosure audit passed:',count,'owned source/config files;',len(checks),'vendor hashes')
