# -*- coding: utf-8 -*-
"""查 case-entry 任务的诊断输入原文与规范化文本。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

import pyodbc

conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;"
    "TrustServerCertificate=yes;Encrypt=no;",
    timeout=30,
)
cur = conn.cursor()
cur.execute(
    "SELECT source_type, original_text, normalized_text "
    "FROM coding_diagnosis_input WHERE coding_task_id = ?",
    "35b4fc8a-ca42-473d-9f4b-a1191343d5e7",
)
for row in cur.fetchall():
    print(f"{row.source_type} | {row.original_text} | {row.normalized_text}")
