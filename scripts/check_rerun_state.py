# -*- coding: utf-8 -*-
"""检查重跑后任务状态与 outbox。"""
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
    "SELECT status, COUNT(1) FROM coding_task "
    "WHERE hospital_id = ? GROUP BY status",
    "ff840089-ea7d-4c40-bb4c-5d47b4b49956",
)
for row in cur.fetchall():
    print(row.status, row[1])
cur.execute(
    "SELECT COUNT(1) FROM outbox_message WHERE message_type = ?",
    "coding.task.created",
)
print("outbox created msgs:", cur.fetchone()[0])
