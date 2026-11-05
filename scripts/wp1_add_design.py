# -*- coding: utf-8 -*-
"""给 API csproj 加 EF Design 包（版本对齐现有 EF Core 包）。"""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\HospitalAi.Api.csproj"
c = open(p, encoding="utf-8").read()
if "EntityFrameworkCore.Design" in c:
    print("already present")
    sys.exit(0)

m = re.search(r'<PackageReference Include="Microsoft\.EntityFrameworkCore\.[A-Za-z]+" Version="([^"]+)" />', c)
assert m, "ef package anchor"
version = m.group(1)
add = f'    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="{version}">'
add += "\n      <PrivateAssets>all</PrivateAssets>\n      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n    </PackageReference>"
c = c.replace(m.group(0), m.group(0) + "\n" + add, 1)
open(p, "w", encoding="utf-8").write(c)
print(f"Design {version} added")
