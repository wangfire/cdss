# -*- coding: utf-8 -*-
"""抽查字典展开码在容器库的存在性。"""
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
HOSP = "6b427cb5-a6ba-4a8e-9b36-311c1102f20b"
for code in ("B83.100", "B97.300", "N20.100", "S12.802", "Q70.900X002", "B39.400X001", "B97.100", "I71.200", "Q25.406"):
    cur.execute(
        "SELECT COUNT(1) FROM medical_code WHERE hospital_id=? AND code=?", HOSP, code
    )
    print(code, "->", cur.fetchone()[0])
