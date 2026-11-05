# -*- coding: utf-8 -*-
"""查 Infrastructure 的 EF 包版本 + 给 Infrastructure 加 Design 包（ef 工具用 infrastructure 作 startup）。"""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Infrastructure\HospitalAi.Infrastructure.csproj"
c = open(p, encoding="utf-8").read()
for m in re.finditer(r'<PackageReference Include="(Microsoft\.EntityFrameworkCore[^"]*)" Version="([^"]+)"', c):
    print(m.group(1), m.group(2))

if "EntityFrameworkCore.Design" not in c:
    m = re.search(r'<PackageReference Include="(Microsoft\.EntityFrameworkCore\.[A-Za-z]+)" Version="([^"]+)"', c)
    version = m.group(2)
    add = f'    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="{version}">'
    add += "\n      <PrivateAssets>all</PrivateAssets>\n      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n    </PackageReference>"
    # 插入到第一个 PackageReference 前
    first = re.search(r'    <PackageReference Include="Microsoft\.EntityFrameworkCore\.[A-Za-z]+"', c)
    c = c[: first.start()] + add + "\n" + c[first.start():]
    open(p, "w", encoding="utf-8").write(c)
    print(f"Design {version} added to Infrastructure")
else:
    print("Design already present")
