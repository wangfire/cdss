# -*- coding: utf-8 -*-
"""检查 inbox 去重表。"""
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
cur.execute("SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('inbox_message') ORDER BY c.column_id")
print("inbox cols:", [r[0] for r in cur.fetchall()])
cur.execute("SELECT COUNT(1) FROM inbox_message")
print("inbox total:", cur.fetchone()[0])
