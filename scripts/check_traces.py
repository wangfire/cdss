# -*- coding: utf-8 -*-
"""检查重跑后是否有新 pipeline_trace。"""
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
cur.execute("SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('pipeline_trace') ORDER BY c.column_id")
print("trace columns:", [r[0] for r in cur.fetchall()])
cur.execute("SELECT COUNT(1) FROM pipeline_trace")
print("total traces:", cur.fetchone()[0])
cur.execute(
    "SELECT TOP 3 CAST(created_at AS nvarchar(40)) FROM pipeline_trace ORDER BY created_at DESC"
)
for row in cur.fetchall():
    print("latest trace:", row[0])
