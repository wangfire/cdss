# -*- coding: utf-8 -*-
"""检查重跑后任务的更新时间与活跃推荐数。"""
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
    "SELECT MAX(CAST(updated_at AS nvarchar(40))) FROM coding_task "
    "WHERE hospital_id = ?",
    "ff840089-ea7d-4c40-bb4c-5d47b4b49956",
)
print("latest task updated_at:", cur.fetchone()[0])
cur.execute(
    "SELECT COUNT(1) FROM coding_recommendation "
    "WHERE hospital_id = ? AND lifecycle_status = 'ACTIVE'",
    "ff840089-ea7d-4c40-bb4c-5d47b4b49956",
)
print("active recs:", cur.fetchone()[0])
cur.execute(
    "SELECT COUNT(1) FROM coding_recommendation r "
    "JOIN coding_task t ON t.id = r.coding_task_id "
    "WHERE r.hospital_id = ? AND t.updated_at > '2026-11-01 07:43:00'",
    "ff840089-ea7d-4c40-bb4c-5d47b4b49956",
)
print("recs on rerun-touched tasks:", cur.fetchone()[0])
