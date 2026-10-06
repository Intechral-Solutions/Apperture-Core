using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace Apperture.Core.Application.Audit;

/// <summary>The kinds a declared audit detail field may have (§12.1). None of them accepts a credential, token or secret.</summary>
public enum AuditFieldKind
{
    /// <summary>An identifier, at most <see cref="AuditDetailSchema.MaxIdLength"/> characters.</summary>
    Id,

    /// <summary>One value of a closed set.</summary>
    Enum,

    /// <summary>A whole number.</summary>
    Count,

    /// <summary>A boolean.</summary>
    Flag,

    /// <summary>A point in time.</summary>
    Timestamp,

    /// <summary>A username, recorded verbatim. Usernames may appear in audit details (IAM-9 §21).</summary>
    Username,

    /// <summary>A copied stored name. The only truncatable kind (§12.5).</summary>
    NameSnapshot,

    /// <summary>Operator-typed text in a declared field, bounded by the operation's own input limit. Never required, never truncated.</summary>
    OperatorText,

    /// <summary>A bounded list of identifiers, such as a permission diff.</summary>
    IdList,
}

/// <summary>One permitted field of an action's details.</summary>
/// <param name="Name">The JSON property name. Never one of the sensitive names (<see cref="AuditDetailSchema.IsSensitiveFieldName"/>).</param>
/// <param name="MaxLength">Maximum characters of a string value (or of each list item).</param>
/// <param name="TruncatedLength">For <see cref="AuditFieldKind.NameSnapshot"/>: the budget the overflow policy cuts it to.</param>
/// <param name="EnumValues">For <see cref="AuditFieldKind.Enum"/>: the closed set.</param>
/// <param name="MaxItems">For <see cref="AuditFieldKind.IdList"/>: the maximum number of items.</param>
public sealed record AuditFieldDefinition(
    string Name,
    AuditFieldKind Kind,
    bool Required,
    int MaxLength = 0,
    int TruncatedLength = 0,
    IReadOnlySet<string>? EnumValues = null,
    int MaxItems = 0)
{
    public bool Truncatable => Kind == AuditFieldKind.NameSnapshot;
}

/// <summary>The serialized details of one event, ready to store.</summary>
/// <param name="Json">The <c>json/1</c> text.</param>
/// <param name="Truncated">Fields cut by the overflow policy, in the order they were cut.</param>
/// <param name="Omitted">True when the details were replaced by <c>{"_omitted":"size"}</c> (W3 and W4 only).</param>
public sealed record AuditSerializedDetails(string Json, IReadOnlyList<string> Truncated, bool Omitted);

/// <summary>Thrown when a W1 or W2 event's details exceed the limit even after truncation: a schema defect (§12.5).</summary>
public sealed class AuditDetailsOverflowException(string action, int bytes)
    : InvalidOperationException($"The details of audit action '{action}' are {bytes} bytes after truncation, over the {AuditDetailSchema.MaxDetailsBytes}-byte limit. The action's detail schema is defective.");

/// <summary>
/// The permitted fields of one action (§12.1) and the overflow policy (§12.5). There is no way to write an undeclared
/// field: producers are typed factory methods that pass only declared names, and <see cref="Validate"/> refuses anything
/// else.
/// </summary>
/// <remarks>
/// <para>What this can prove (§12.2): only declared fields of declared kinds are written; no kind accepts a credential,
/// token, secret or secret reference; search parameters have no kind; every field is bounded.</para>
/// <para>What it cannot prove: that operator-typed text contains no secret. An operator who types a password into a reason
/// box will have it recorded. Free text therefore appears only in declared, bounded <see cref="AuditFieldKind.OperatorText"/>
/// fields, is never taken from inputs designated sensitive, and is not content-scanned.</para>
/// </remarks>
public sealed partial class AuditDetailSchema
{
    /// <summary>The overflow limit: UTF-8 bytes of the serialized JSON.</summary>
    public const int MaxDetailsBytes = 32 * 1024;

    public const int MaxIdLength = 160;
    public const int MaxUsernameLength = 256;
    public const int MaxEnumLength = 64;

    /// <summary>The marker property listing truncated fields.</summary>
    public const string TruncatedProperty = "_truncated";

    /// <summary>The marker property replacing oversize W3 and W4 details.</summary>
    public const string OmittedProperty = "_omitted";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Non-ASCII stays as UTF-8 rather than \uXXXX escapes, so the measured size is the stored size. HTML-sensitive
        // characters are still escaped.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public AuditDetailSchema(IEnumerable<AuditFieldDefinition> fields)
    {
        Fields = fields.ToList();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            if (!names.Add(field.Name))
                throw new ArgumentException($"Audit field '{field.Name}' is declared twice.", nameof(fields));
            if (field.Name.StartsWith('_') || !FieldNamePattern().IsMatch(field.Name))
                throw new ArgumentException($"Audit field name '{field.Name}' is not a plain camelCase name.", nameof(fields));
            if (IsSensitiveFieldName(field.Name))
                throw new ArgumentException($"Audit field name '{field.Name}' names sensitive content, which is never recorded.", nameof(fields));
            if (field.Kind == AuditFieldKind.OperatorText && field.Required)
                throw new ArgumentException($"Operator text field '{field.Name}' cannot be required.", nameof(fields));
            if (field.Kind == AuditFieldKind.Enum && (field.EnumValues is null || field.EnumValues.Count == 0))
                throw new ArgumentException($"Enum field '{field.Name}' declares no values.", nameof(fields));
            if (field.Kind == AuditFieldKind.IdList && field.MaxItems <= 0)
                throw new ArgumentException($"Id list field '{field.Name}' declares no item bound.", nameof(fields));
            if (field.Kind is AuditFieldKind.OperatorText or AuditFieldKind.NameSnapshot && field.MaxLength <= 0)
                throw new ArgumentException($"Text field '{field.Name}' declares no length bound.", nameof(fields));
            if (field.Kind == AuditFieldKind.NameSnapshot && (field.TruncatedLength <= 0 || field.TruncatedLength > field.MaxLength))
                throw new ArgumentException($"Name snapshot field '{field.Name}' needs a truncation budget within its length bound.", nameof(fields));
        }
    }

    public IReadOnlyList<AuditFieldDefinition> Fields { get; }

    /// <summary>
    /// True for a name that would describe content §12.3 never records: credentials, tokens, secrets, key material,
    /// connection strings, search or filter text, or parameter values. A schema declaring such a field is refused.
    /// </summary>
    public static bool IsSensitiveFieldName(string name) => SensitiveNamePattern().IsMatch(name);

    /// <summary>Checks that <paramref name="values"/> holds only declared fields, every required one, each of its kind and bounds.</summary>
    /// <exception cref="ArgumentException">A programming error in a producer.</exception>
    public void Validate(string action, IReadOnlyDictionary<string, object?> values)
    {
        foreach (var name in values.Keys)
        {
            if (Fields.All(f => f.Name != name))
                throw new ArgumentException($"Audit action '{action}' declares no field '{name}'.", nameof(values));
        }

        foreach (var field in Fields)
        {
            values.TryGetValue(field.Name, out var value);
            if (value is null)
            {
                if (field.Required)
                    throw new ArgumentException($"Audit action '{action}' requires field '{field.Name}'.", nameof(values));
                continue;
            }

            ValidateValue(action, field, value);
        }
    }

    /// <summary>
    /// Serializes validated values in declaration order and applies the overflow policy (§12.5). Over the limit, name
    /// snapshots are cut, longest first, each to its own budget, and listed in <c>_truncated</c>; nothing else is ever
    /// truncated. Still over: W1 and W2 throw <see cref="AuditDetailsOverflowException"/>; W3 and W4 store
    /// <c>{"_omitted":"size"}</c>.
    /// </summary>
    public AuditSerializedDetails Serialize(string action, AuditWriteClass writeClass, IReadOnlyDictionary<string, object?> values)
    {
        Validate(action, values);

        var working = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        var truncated = new List<string>();
        var json = Write(working, truncated);
        while (Utf8Length(json) > MaxDetailsBytes)
        {
            var next = Fields
                .Where(f => f.Truncatable && !truncated.Contains(f.Name)
                    && working.TryGetValue(f.Name, out var v) && v is string s && s.Length > f.TruncatedLength)
                .OrderByDescending(f => ((string)working[f.Name]!).Length)
                .ThenBy(f => f.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
                break;

            working[next.Name] = ((string)working[next.Name]!)[..next.TruncatedLength];
            truncated.Add(next.Name);
            json = Write(working, truncated);
        }

        var bytes = Utf8Length(json);
        if (bytes <= MaxDetailsBytes)
            return new AuditSerializedDetails(json, truncated, Omitted: false);

        if (writeClass is AuditWriteClass.RequiredMutation or AuditWriteClass.RequiredSecurityEvent)
            throw new AuditDetailsOverflowException(action, bytes);

        return new AuditSerializedDetails(OmittedJson, truncated, Omitted: true);
    }

    /// <summary>The stored details of an oversize W3 or W4 event.</summary>
    public static string OmittedJson { get; } = "{\"" + OmittedProperty + "\":\"size\"}";

    private static int Utf8Length(string json) => Encoding.UTF8.GetByteCount(json);

    private string Write(IReadOnlyDictionary<string, object?> values, IReadOnlyList<string> truncated)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var field in Fields)
            {
                if (!values.TryGetValue(field.Name, out var value) || value is null)
                    continue;
                writer.WritePropertyName(field.Name);
                switch (value)
                {
                    case string s:
                        writer.WriteStringValue(s);
                        break;
                    case long l:
                        writer.WriteNumberValue(l);
                        break;
                    case int i:
                        writer.WriteNumberValue(i);
                        break;
                    case bool b:
                        writer.WriteBooleanValue(b);
                        break;
                    case DateTimeOffset t:
                        writer.WriteStringValue(t.ToUniversalTime().ToString("O"));
                        break;
                    case IReadOnlyList<string> list:
                        writer.WriteStartArray();
                        foreach (var item in list)
                            writer.WriteStringValue(item);
                        writer.WriteEndArray();
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported audit field value type {value.GetType().Name}.");
                }
            }

            if (truncated.Count > 0)
            {
                writer.WritePropertyName(TruncatedProperty);
                writer.WriteStartArray();
                foreach (var name in truncated)
                    writer.WriteStringValue(name);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void ValidateValue(string action, AuditFieldDefinition field, object value)
    {
        string Fail(string what) =>
            throw new ArgumentException($"Audit action '{action}' field '{field.Name}' ({field.Kind}) {what}.");

        switch (field.Kind)
        {
            case AuditFieldKind.Id:
                RequireText(value, Math.Min(field.MaxLength > 0 ? field.MaxLength : MaxIdLength, MaxIdLength), Fail);
                break;
            case AuditFieldKind.Username:
                RequireText(value, MaxUsernameLength, Fail);
                break;
            case AuditFieldKind.NameSnapshot:
            case AuditFieldKind.OperatorText:
                RequireText(value, field.MaxLength, Fail, allowEmpty: field.Kind == AuditFieldKind.NameSnapshot);
                break;
            case AuditFieldKind.Enum:
                RequireText(value, MaxEnumLength, Fail);
                if (!field.EnumValues!.Contains((string)value))
                    Fail($"has undeclared value '{value}'");
                break;
            case AuditFieldKind.Count:
                if (value is not (int or long))
                    Fail("must be a whole number");
                break;
            case AuditFieldKind.Flag:
                if (value is not bool)
                    Fail("must be a boolean");
                break;
            case AuditFieldKind.Timestamp:
                if (value is not DateTimeOffset)
                    Fail("must be a DateTimeOffset");
                break;
            case AuditFieldKind.IdList:
                if (value is not IReadOnlyList<string> list)
                {
                    Fail("must be a list of identifiers");
                    return;
                }

                if (list.Count > field.MaxItems)
                    Fail($"has {list.Count} items, over its bound of {field.MaxItems}");
                foreach (var item in list)
                {
                    if (string.IsNullOrEmpty(item) || item.Length > MaxIdLength)
                        Fail("has an empty or over-long item");
                }

                break;
            default:
                Fail("has an unsupported kind");
                break;
        }
    }

    private static void RequireText(object value, int maxLength, Func<string, string> fail, bool allowEmpty = false)
    {
        if (value is not string s)
        {
            fail("must be text");
            return;
        }

        if (!allowEmpty && s.Length == 0)
            fail("must not be empty");
        if (s.Length > maxLength)
            fail($"is {s.Length} characters, over its bound of {maxLength}");
    }

    [GeneratedRegex("^[a-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldNamePattern();

    [GeneratedRegex(
        "(password|passwd|passwordhash|salt|token|secret|credential|apikey|totp|recoverycode|connectionstring|keymaterial|privatekey|search|query|filter|parametervalue)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveNamePattern();
}
