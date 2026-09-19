import importlib.util
from pathlib import Path
import sys
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = ROOT / "tools" / "knowledge" / "build_knowledge.py"
SPEC = importlib.util.spec_from_file_location("build_knowledge", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class BuildKnowledgeTests(unittest.TestCase):
    def test_index_synonyms_only_keep_nec_zero_and_split_codes(self):
        headers = [
            "名称",
            "编码",
            "名称(主词)",
            "病(拆分)",
            "综合征(拆分)",
            "[]内容-1",
            "[]内容-2",
            "[]内容-3",
            "()或见/另见内容",
            "NEC状态",
        ]
        rows = [
            (
                "13三体综合征",
                "Q91.7",
                "13三体综合征",
                None,
                None,
                "帕套综合征",
                None,
                None,
                None,
                0,
            ),
            (
                "应被过滤",
                "Q91.8",
                "应被过滤",
                None,
                None,
                "不应导入",
                None,
                None,
                None,
                1,
            ),
            (
                "组合编码术语",
                "E74.0† I43.1*",
                "组合编码术语",
                "拆分病名",
                "拆分综合征",
                None,
                None,
                None,
                None,
                0,
            ),
        ]

        synonyms = MODULE.build_index_synonyms(headers, rows)

        mappings = {
            (item["term"], item["code"]): item["normalizedTerm"]
            for item in synonyms
        }
        self.assertEqual("13三体综合征", mappings[("13三体综合征", "Q91.7")])
        self.assertEqual("13三体综合征", mappings[("帕套综合征", "Q91.7")])
        self.assertEqual("组合编码术语", mappings[("拆分病名", "E74.0+I43.1*")])
        self.assertNotIn(("拆分病名", "E74.0"), mappings)
        self.assertNotIn(("拆分病名", "I43.1"), mappings)
        self.assertNotIn(("应被过滤", "Q91.8"), mappings)
        self.assertNotIn(("不应导入", "Q91.8"), mappings)

        self.assertEqual(
            len(mappings),
            len(synonyms),
            "同一关键词和编码关系不能重复生成",
        )

    def test_index_synonyms_same_term_and_code_keep_one_latest_mapping(self):
        headers = [
            "编码",
            "名称(主词)",
            "病(拆分)",
            "综合征(拆分)",
            "[]内容-1",
            "[]内容-2",
            "[]内容-3",
            "NEC状态",
        ]
        rows = [
            ("Q91.7", "13三体综合征", None, None, "帕套综合征", None, None, 0),
            ("Q91.7", "13三体综合征", None, None, "帕套综合征", None, None, 0),
        ]

        synonyms = MODULE.build_index_synonyms(headers, rows)

        self.assertEqual(2, len(synonyms))
        self.assertEqual(
            2,
            len({(item["term"], item["code"]) for item in synonyms}),
        )
        self.assertEqual(
            ("帕套综合征", "Q91.7"),
            next(
                (item["term"], item["code"])
                for item in synonyms
                if item["term"] == "帕套综合征"
            ),
        )


if __name__ == "__main__":
    unittest.main()
