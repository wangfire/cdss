# -*- coding: utf-8 -*-
"""为 ICD10CAP-3B 院 admin 设置密码哈希。"""
import io
import base64
import hashlib
import os
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

import pyodbc

salt = os.urandom(16)
h = hashlib.pbkdf2_hmac("sha256", b"admin123", salt, 100000, 32)
hash_str = "v1$" + base64.b64encode(salt).decode() + "$" + base64.b64encode(h).decode()

conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;"
    "TrustServerCertificate=yes;Encrypt=no;",
    timeout=30,
)
cur = conn.cursor()
cur.execute(
    "UPDATE app_user SET password_hash = ? WHERE hospital_id = ? AND code = 'admin'",
    hash_str,
    "ff840089-ea7d-4c40-bb4c-5d47b4b49956",
)
conn.commit()
cur.execute(
    "SELECT code, status FROM app_user WHERE hospital_id = ? AND code = 'admin'",
    "ff840089-ea7d-4c40-bb4c-5d47b4b49956",
)
row = cur.fetchone()
print("admin set:", row.code, row.status)
