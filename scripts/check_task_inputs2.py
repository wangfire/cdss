# -*- coding: utf-8 -*-
"""查输入原文（显式 UTF-8 输出，绕开控制台编码）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

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
    "SELECT source_type, original_text, normalized_text, LEN(original_text) AS len "
    "FROM coding_diagnosis_input WHERE coding_task_id = ?",
    "35b4fc8a-ca42-473d-9f4b-a1191343d5e7",
)
for row in cur.fetchall():
    text = row.original_text or ""
    codes = " ".join(f"U+{ord(ch):04X}" for ch in text[:8])
    sys.stdout.write(
        f"{row.source_type} | len={row.len} | codes={codes} | {text!r} | norm={row.normalized_text!r}\n"
    )
