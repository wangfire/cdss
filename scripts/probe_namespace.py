# -*- coding: utf-8 -*-
"""用 Python 反射宿主程序集（MetadataLoadContext）查 ForwardedHeadersOptions 真实命名空间。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

import clr  # pythonnet 可能没装，失败则跳过

try:
    fx = r"C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\10.0.12"
    sys.path.append(fx)
    from Microsoft.AspNetCore import ForwardedHeadersOptions  # 尝试直接导入
    print("found via direct import")
except Exception as e:
    print("direct import failed:", str(e)[:100])
