using System.Globalization;
using System.Text.Json;
using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.Models;

namespace HospitalAi.Infrastructure.ModelGateways;

/// <summary>
/// 版本化 JSON Schema 校验器（子集）：支持 type / properties / required /
/// additionalProperties / items / enum / minimum / maximum。
/// Lite 阶段不引入完整 JSON Schema 引擎，只保证模型输出可被业务安全反序列化。
/// 校验失败的输出一律不得进入推荐（含重试后仍失败）。
/// </summary>
public sealed class SimpleJsonSchemaValidator : IJsonSchemaValidator
{
    public JsonSchemaValidationResult Validate(string schemaJson, string json)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return JsonSchemaValidationResult.Invalid(["Schema 不能为空。"]);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return JsonSchemaValidationResult.Invalid([$"JSON 解析失败：{exception.Message}"]);
        }

        using (document)
        {
            JsonDocument schema;
            try
            {
                schema = JsonDocument.Parse(schemaJson);
            }
            catch (JsonException exception)
            {
                return JsonSchemaValidationResult.Invalid([$"Schema 解析失败：{exception.Message}"]);
            }

            using (schema)
            {
                var errors = new List<string>();
                ValidateElement(schema.RootElement, document.RootElement, "$", errors);
                return errors.Count == 0
                    ? JsonSchemaValidationResult.Valid
                    : JsonSchemaValidationResult.Invalid(errors);
            }
        }
    }

    private static void ValidateElement(
        JsonElement schema,
        JsonElement instance,
        string path,
        List<string> errors)
    {
        if (schema.TryGetProperty("enum", out var enumValues)
            && enumValues.ValueKind == JsonValueKind.Array)
        {
            var matched = false;
            foreach (var candidate in enumValues.EnumerateArray())
            {
                if (JsonElementEquals(candidate, instance))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                errors.Add($"{path} 取值 {instance} 不在枚举范围内。");
                return;
            }
        }

        if (!schema.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var type = typeElement.GetString();
        if (type is null)
        {
            return;
        }

        if (!MatchesType(type, instance))
        {
            errors.Add($"{path} 应为 {type}，实际为 {instance.ValueKind}。");
            return;
        }

        switch (type)
        {
            case "object":
                ValidateObject(schema, instance, path, errors);
                break;
            case "array":
                ValidateArray(schema, instance, path, errors);
                break;
            case "number" or "integer":
                ValidateNumber(schema, instance, path, errors);
                break;
        }
    }

    private static void ValidateObject(
        JsonElement schema,
        JsonElement instance,
        string path,
        List<string> errors)
    {
        if (schema.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var property in required.EnumerateArray())
            {
                var name = property.GetString();
                if (!string.IsNullOrEmpty(name)
                    && !instance.TryGetProperty(name, out _))
                {
                    errors.Add($"{path} 缺少必填字段 {name}。");
                }
            }
        }

        if (schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (instance.TryGetProperty(property.Name, out var value))
                {
                    ValidateElement(property.Value, value, $"{path}.{property.Name}", errors);
                }
            }
        }

        var additional = schema.TryGetProperty("additionalProperties", out var additionalElement)
            && additionalElement.ValueKind == JsonValueKind.False;
        if (additional)
        {
            var declared = properties.ValueKind == JsonValueKind.Object
                ? properties.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in instance.EnumerateObject())
            {
                if (!declared.Contains(item.Name))
                {
                    errors.Add($"{path} 包含未声明字段 {item.Name}。");
                }
            }
        }
    }

    private static void ValidateArray(
        JsonElement schema,
        JsonElement instance,
        string path,
        List<string> errors)
    {
        if (schema.TryGetProperty("minItems", out var minItems)
            && minItems.TryGetInt32(out var min)
            && instance.GetArrayLength() < min)
        {
            errors.Add($"{path} 元素数量少于 {min}。");
        }

        if (schema.TryGetProperty("maxItems", out var maxItems)
            && maxItems.TryGetInt32(out var max)
            && instance.GetArrayLength() > max)
        {
            errors.Add($"{path} 元素数量多于 {max}。");
        }

        if (schema.TryGetProperty("items", out var itemsSchema))
        {
            var index = 0;
            foreach (var item in instance.EnumerateArray())
            {
                ValidateElement(itemsSchema, item, $"{path}[{index}]", errors);
                index++;
            }
        }
    }

    private static void ValidateNumber(
        JsonElement schema,
        JsonElement instance,
        string path,
        List<string> errors)
    {
        if (!instance.TryGetDecimal(out var value))
        {
            return;
        }

        if (schema.TryGetProperty("minimum", out var minimum)
            && minimum.TryGetDecimal(out var minValue)
            && value < minValue)
        {
            errors.Add($"{path} 取值 {minValue} 过小。");
        }

        if (schema.TryGetProperty("maximum", out var maximum)
            && maximum.TryGetDecimal(out var maxValue)
            && value > maxValue)
        {
            errors.Add($"{path} 取值 {maxValue} 过大。");
        }
    }

    private static bool MatchesType(string type, JsonElement instance)
    {
        return type switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object,
            "array" => instance.ValueKind == JsonValueKind.Array,
            "string" => instance.ValueKind == JsonValueKind.String,
            "number" => instance.ValueKind == JsonValueKind.Number,
            "integer" => instance.ValueKind == JsonValueKind.Number
                && instance.TryGetInt64(out _),
            "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => instance.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }

    private static bool JsonElementEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.True => right.ValueKind == JsonValueKind.True,
            JsonValueKind.False => right.ValueKind == JsonValueKind.False,
            JsonValueKind.Null => true,
            _ => string.Equals(
                left.GetRawText(),
                right.GetRawText(),
                StringComparison.Ordinal)
        };
    }
}
