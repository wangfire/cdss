# -*- coding: utf-8 -*-
"""为 CAP-DE 院 admin 用户生成 PBKDF2 密码哈希（admin123）并更新。"""
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
    "6b427cb5-a6ba-4a8e-9b36-311c1102f20b",
)
conn.commit()
cur.execute(
    "SELECT code, status FROM app_user WHERE hospital_id = ? AND code = 'admin'",
    "6b427cb5-a6ba-4a8e-9b36-311c1102f20b",
)
row = cur.fetchone()
print("admin updated:", row.code, row.status)
