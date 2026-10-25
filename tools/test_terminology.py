"""Regression checks for meaning, name consistency and game-string contracts."""
import json
import hashlib
import unittest
from revise_terminology import ROOT, literal, load_glossary, references, revise


class TerminologyTests(unittest.TestCase):
    def setUp(self):
        self.terms = load_glossary()
        self.aliases = json.loads((ROOT / 'translations/term-aliases.json').read_text('utf-8'))

    def test_game_meanings_and_plural_forms(self):
        for source, expected in [('Chest', '상자'), ('Purifier', '정화자'),
                                 ('Purifiers', '정화자'), ('Soulmonger', '영혼 장사꾼'),
                                 ('Stable', '마구간'), ('Spring of Life', '생명의 샘')]:
            self.assertEqual(literal(source, self.terms), expected)

    def test_command_names_and_original_hotkeys(self):
        self.assertEqual(literal('Train @Squire', self.terms), '기사의 종자 훈련 (@S)')
        self.assertEqual(literal('Call Sou@lmonger', self.terms), '영혼 장사꾼 소환 (@l)')
        self.assertEqual(literal('Upgrade to @Crystal Spire', self.terms), '수정 첨탑으로 개선 (@C)')
        self.assertEqual(literal('Upgrade to @Oracle', self.terms), '예언자로 개선 (@O)')

    def test_item_variables(self):
        self.assertEqual(literal('Soul Crossbow of %s1', self.terms), '%s1의 영혼 석궁')

    def test_context_and_protected_expressions(self):
        source = "Soulmonger uses #GetConst('Soulmonger')# energy"
        before = "영혼 수집가: #GetConst('Soulmonger')#"
        after = references(source, before, self.terms, self.aliases)
        self.assertEqual(after, "영혼 장사꾼: #GetConst('Soulmonger')#")
        self.assertEqual(references('Soul Collector', '영혼 수집가', self.terms, self.aliases), '영혼 수집가')
        before = '기사의 종자가 싸웁니다.'
        self.assertEqual(references('Squires fight.', before, self.terms, self.aliases), before)
        self.assertEqual(references('Soulmonger fights.', '영혼 수집가가 싸웁니다.', self.terms, self.aliases), '영혼 장사꾼이 싸웁니다.')

    def test_reviewed_catalog_has_no_regressions(self):
        if not (ROOT / 'reports/translation-catalog.json').exists():
            self.skipTest('Extracted game catalogs are local developer data')
        revise(check=True)

    def test_shipped_dictionary_uses_reviewed_names(self):
        entries = json.loads((ROOT / 'translations/ko.json').read_text('utf-8'))['Entries']
        expected = {hashlib.sha256(s.encode()).hexdigest(): t for s, t in self.terms.items()}
        verified = 0
        for entry in entries.values():
            if entry['SourceSha256'] in expected:
                self.assertEqual(entry['Korean'], expected[entry['SourceSha256']])
                verified += 1
        self.assertGreater(verified, 100)

    def test_engine_resource_labels_are_in_native_table(self):
        state = json.loads((ROOT / 'translations/ko.json').read_text('utf-8'))
        for id, korean in [('1828913', '영광'), ('1811052', '인구'), ('1806962', '보석'), ('1796974', '금')]:
            self.assertIn(id, state['EngineEntries'])
            self.assertEqual(state['Entries'][id]['Korean'], korean)
        for id in ['1783994', '1778642', '1767414', '1759051']:
            self.assertIn(id, state['EngineEntries'])
            self.assertRegex(state['Entries'][id]['Korean'], '[가-힣]')
        for id, maximum in [('1783994', '7'), ('1767414', '100'), ('1759051', '3000')]:
            self.assertIn('최대 보유량: ' + maximum, state['Entries'][id]['Korean'])
        self.assertEqual(len(state['EngineEntries']), len(set(state['EngineEntries'])))


if __name__ == '__main__':
    unittest.main()
