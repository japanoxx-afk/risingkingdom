"""Apply reviewed literal terminology without sending text to a service.

The glossary is shared with the draft generator so regeneration keeps corrections.
Only catalog entries with matching English hashes are edited. Engine identifiers
remain protected by RuntimeContracts when a patch is built.
"""
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOKEN = re.compile(r"#[^#\r\n]+#|%[sdfiuoxX]\d*|%[A-Za-z_][A-Za-z_0-9]*%|</?[^>\r\n]+>|&(?:[A-Za-z]+|#\d+);|\\[nrt]")


def load_glossary():
    return json.loads((ROOT / 'translations/literal-terms.json').read_text('utf-8'))


def literal(source, terms):
    clean = source.replace('@', '').strip()
    lookup = {k.casefold(): v for k, v in terms.items()}
    result = lookup.get(clean.casefold())
    if result is None:
        for prefix, suffix in [('Learn ', ' 배우기'), ('Train ', ' 훈련'),
                               ('Call ', ' 소환'), ('Recruit ', ' 모집'),
                               ('Summon ', ' 소환'), ('Forge ', ' 건설'),
                               ('Construct ', ' 건설'), ('Plant ', ' 심기'),
                               ('Upgrade to ', '(으)로 개선'),
                               ('Promote to ', '(으)로 승급'),
                               ('Select ', ' 선택'), ('Cast ', ' 시전'),
                               ('Create ', ' 생성')]:
            if clean.casefold().startswith(prefix.casefold()):
                name = lookup.get(clean[len(prefix):].casefold())
                if name is not None:
                    if suffix.startswith('(으)로'):
                        c = ord(name[-1]) - 0xAC00
                        particle = '로' if 0 <= c < 11172 and c % 28 in (0, 8) else '으로'
                        suffix = particle + suffix[len('(으)로'):]
                    result = name + suffix
                    break
    if result is None:
        return None
    keys = re.findall(r'@([A-Za-z])', source)
    return result + (' (' + ' '.join('@' + k for k in keys) + ')' if keys else '')


def references(source, korean, terms, aliases):
    """Only correct a Korean alias when its English name occurs in this row.

    Do not touch expressions, XML, format variables, or unrelated Korean prose.
    A single substitution pass prevents shorter names rewriting longer names.
    """
    replacements = {}
    clean = source.replace('@', '')
    lookup = {k.casefold(): v for k, v in terms.items()}
    for english, old in aliases.items():
        if not re.search(r'(?<![A-Za-z])' + re.escape(english) + r'(?![A-Za-z])', clean, re.I):
            continue
        name = lookup[english.casefold()]
        for value in old:
            replacements[value] = name
        replacements[english] = name
        replacements[name] = name
    if not replacements:
        return korean
    pattern = re.compile('(?P<name>' + '|'.join(re.escape(x) for x in sorted(replacements, key=len, reverse=True)) +
                         r')(?P<particle>(?:으로|로|은|는|이|가|을|를|과|와)(?=$|[\s.,!?]))?', re.I)
    folded = {k.casefold(): v for k, v in replacements.items()}
    def substitute(match):
        name = folded[match['name'].casefold()]
        particle = match['particle'] or ''
        final = ord(name[-1]) - 0xAC00
        if particle and 0 <= final < 11172:
            if particle in ('으로', '로'):
                particle = '로' if final % 28 in (0, 8) else '으로'
            else:
                for pair in [('은', '는'), ('이', '가'), ('을', '를'), ('과', '와')]:
                    if particle in pair:
                        particle = pair[0 if final % 28 else 1]
                        break
        return name + particle
    parts = []
    at = 0
    for token in TOKEN.finditer(korean):
        parts.append(pattern.sub(substitute, korean[at:token.start()]))
        parts.append(token[0])
        at = token.end()
    parts.append(pattern.sub(substitute, korean[at:]))
    return ''.join(parts)


def revise(check=False):
    rows = []
    for name in ['translation-catalog.json', 'campaign-catalog.json']:
        rows += json.loads((ROOT / 'reports' / name).read_text('utf-8-sig'))
    dictionary = ROOT / 'translations/ko.json'
    state = json.loads(dictionary.read_text('utf-8'))
    terms = load_glossary()
    aliases = json.loads((ROOT / 'translations/term-aliases.json').read_text('utf-8'))
    sentences = json.loads((ROOT / 'translations/sentence-corrections.json').read_text('utf-8'))
    changes = {}
    for row in rows:
        key, source = row.get('Key', row['Id']), row['Source']
        entry = state['Entries'][key]
        assert entry['SourceSha256'] == hashlib.sha256(source.encode()).hexdigest(), key
        corrected = sentences.get(source.strip())
        if corrected is None:
            corrected = literal(source, terms)
        if corrected is None:
            corrected = references(source, entry['Korean'], terms, aliases)
        assert sorted(TOKEN.findall(source)) == sorted(TOKEN.findall(corrected)), key
        assert re.findall(r'@([A-Za-z])', source) == re.findall(r'@([A-Za-z])', corrected), key
        if entry['Korean'] != corrected:
            changes[key] = {'Source': source, 'Before': entry['Korean'], 'After': corrected}
            if not check:
                entry['Korean'] = corrected
    if check:
        assert not changes, f'{len(changes)} terminology regressions'
    else:
        state['Method'] = 'Machine draft with reviewed literal names and command/sentence corrections'
        state['TerminologyPolicy'] = 'literal-1'
        dictionary.write_text(json.dumps(state, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        (ROOT / 'build/terminology-changes.json').write_text(json.dumps(changes, ensure_ascii=False, indent=2), encoding='utf-8')
    print(('PASS: ' if check else 'Updated: ') + str(len(changes)) + ' entries; hashes, variables and hotkeys preserved')


if __name__ == '__main__':
    import sys
    revise('--check' in sys.argv)
