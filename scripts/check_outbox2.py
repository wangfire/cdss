# -*- coding: utf-8 -*-
"""检查 outbox 消息（列名 id）与发布状态。"""
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
    "SELECT TOP 3 id, occurred_at, created_at, published_at FROM outbox_message "
    "WHERE message_type = ? ORDER BY created_at DESC",
    "coding.task.created",
)
for row in cur.fetchall():
    print(row.id, "| occurred:", row.occurred_at, "| created:", row.created_at, "| published:", row.published_at)
cur.execute(
    "SELECT COUNT(1) FROM outbox_message WHERE message_type = ? AND published_at IS NULL",
    "coding.task.created",
)
print("unpublished:", cur.fetchone()[0])
