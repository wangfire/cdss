# -*- coding: utf-8 -*-
"""WP-B：workbench/tasks 端点加分页参数。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\Program.cs"
c = open(p, encoding="utf-8").read()

old = """    async (
        string? status,
        ICodingRecommendationService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ListWorkbenchTasksAsync(status, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);"""

new = """    async (
        string? status,
        string? keyword,
        int? page,
        int? pageSize,
        ICodingRecommendationService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ListWorkbenchTasksPageAsync(
            status, keyword, page ?? 1, pageSize ?? 20, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);"""

assert old in c, "endpoint anchor"
c = c.replace(old, new, 1)
open(p, "w", encoding="utf-8-sig").write(c)
print("endpoint updated")
