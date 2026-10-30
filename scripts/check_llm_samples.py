# -*- coding: utf-8 -*-
"""验证 LocalDB 样本可用性。"""
import io
import sys

import pyodbc

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=(localdb)\\MSSQLLocalDB;Database=HospitalAi;"
    "Integrated Security=true;TrustServerCertificate=yes;Encrypt=no;",
    timeout=15,
)
cur = conn.cursor()
cur.execute("SELECT COUNT(1) FROM coding_diagnosis_input WHERE source_type='FRONT_PAGE'")
print("front_page inputs:", cur.fetchone()[0])
cur.execute("SELECT COUNT(1) FROM clinical_evidence WHERE status='ACTIVE'")
print("active evidence:", cur.fetchone()[0])
cur.execute(
    """
    SELECT TOP 3 i.original_text, r.code, r.title
    FROM coding_diagnosis_input i
    JOIN coding_recommendation r
      ON r.diagnosis_input_id = i.id AND r.lifecycle_status='ACTIVE' AND r.rank=1
    WHERE i.source_type='FRONT_PAGE'
    """
)
for row in cur.fetchall():
    print("sample:", row.original_text[:30], "|", row.code, "|", row.title[:20])
cur.close()
conn.close()
