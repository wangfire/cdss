# -*- coding: utf-8 -*-
"""检查 outbox 消息结构与派发状态。"""
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
cur.execute("SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('outbox_message') ORDER BY c.column_id")
cols = [r[0] for r in cur.fetchall()]
print("columns:", cols)
cur.execute("SELECT COUNT(1) FROM outbox_message")
print("total:", cur.fetchone()[0])
cur.execute(
    "SELECT TOP 3 message_id, occurred_at, created_at FROM outbox_message "
    "WHERE message_type = ? ORDER BY created_at DESC",
    "coding.task.created",
)
for row in cur.fetchall():
    print(row.message_id, "|", row.occurred_at, "|", row.created_at)
