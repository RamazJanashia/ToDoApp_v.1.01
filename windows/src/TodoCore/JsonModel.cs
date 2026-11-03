using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoCore;

// The DTOs below are the only JSON-aware types: they mirror the Mac app's tasks.json exactly
// (Swift property names, nil optionals omitted, properties in the order `.sortedKeys` gives),
// and map to and from the plain domain records.
//
// Properties that Swift's synthesized decoding requires are nullable here too, so a missing
// key can be reported instead of silently becoming a default.

internal static class JsonModel
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new IsoDateConverter(), new UpperGuidConverter() },
    };

    public static byte[] Serialize(AppData data) =>
        JsonSerializer.SerializeToUtf8Bytes(AppDataDto.From(data), Options);

    public static AppData Deserialize(ReadOnlySpan<byte> json)
    {
        var dto = JsonSerializer.Deserialize<AppDataDto>(json, Options)
            ?? throw new JsonException("tasks.json is empty.");
        return dto.ToDomain();
    }

    internal static T Required<T>(T? value, string key) where T : struct =>
        value ?? throw new JsonException($"Missing required key '{key}'.");

    internal static T Required<T>(T? value, string key) where T : class =>
        value ?? throw new JsonException($"Missing required key '{key}'.");

    // Enum strings go through explicit maps: an unknown value must throw, never fall back.

    private static string Unknown(string kind, object? value) =>
        string.Create(CultureInfo.InvariantCulture, $"Unknown {kind} '{value}'.");

    public static string ToJson(TaskStatus value) => value switch
    {
        TaskStatus.Todo => "todo",
        TaskStatus.InProgress => "inProgress",
        TaskStatus.Done => "done",
        _ => throw new JsonException(Unknown("status", value)),
    };

    public static TaskStatus ParseStatus(string value) => value switch
    {
        "todo" => TaskStatus.Todo,
        "inProgress" => TaskStatus.InProgress,
        "done" => TaskStatus.Done,
        _ => throw new JsonException(Unknown("status", value)),
    };

    public static int ToJson(Priority value) => value switch
    {
        Priority.None => 0,
        Priority.Low => 1,
        Priority.Medium => 2,
        Priority.High => 3,
        _ => throw new JsonException(Unknown("priority", value)),
    };

    public static Priority ParsePriority(int value) => value switch
    {
        0 => Priority.None,
        1 => Priority.Low,
        2 => Priority.Medium,
        3 => Priority.High,
        _ => throw new JsonException(Unknown("priority", value)),
    };

    public static string ToJson(ProjectColor value) => value switch
    {
        ProjectColor.Blue => "blue",
        ProjectColor.Green => "green",
        ProjectColor.Red => "red",
        ProjectColor.Orange => "orange",
        ProjectColor.Yellow => "yellow",
        ProjectColor.Purple => "purple",
        ProjectColor.Pink => "pink",
        ProjectColor.Teal => "teal",
        ProjectColor.Graphite => "graphite",
        ProjectColor.White => "white",
        _ => throw new JsonException(Unknown("color", value)),
    };

    public static ProjectColor ParseColor(string value) => value switch
    {
        "blue" => ProjectColor.Blue,
        "green" => ProjectColor.Green,
        "red" => ProjectColor.Red,
        "orange" => ProjectColor.Orange,
        "yellow" => ProjectColor.Yellow,
        "purple" => ProjectColor.Purple,
        "pink" => ProjectColor.Pink,
        "teal" => ProjectColor.Teal,
        "graphite" => ProjectColor.Graphite,
        "white" => ProjectColor.White,
        _ => throw new JsonException(Unknown("color", value)),
    };

    public static string ToJson(AttachmentKind value) => value switch
    {
        AttachmentKind.File => "file",
        AttachmentKind.Link => "link",
        _ => throw new JsonException(Unknown("kind", value)),
    };

    public static AttachmentKind ParseKind(string value) => value switch
    {
        "file" => AttachmentKind.File,
        "link" => AttachmentKind.Link,
        _ => throw new JsonException(Unknown("kind", value)),
    };

    public static string ToJson(RecurrenceFrequency value) => value switch
    {
        RecurrenceFrequency.Daily => "daily",
        RecurrenceFrequency.Weekly => "weekly",
        RecurrenceFrequency.Monthly => "monthly",
        RecurrenceFrequency.Yearly => "yearly",
        _ => throw new JsonException(Unknown("frequency", value)),
    };

    public static RecurrenceFrequency ParseFrequency(string value) => value switch
    {
        "daily" => RecurrenceFrequency.Daily,
        "weekly" => RecurrenceFrequency.Weekly,
        "monthly" => RecurrenceFrequency.Monthly,
        "yearly" => RecurrenceFrequency.Yearly,
        _ => throw new JsonException(Unknown("frequency", value)),
    };
}

/// <summary>
/// Swift's <c>.iso8601</c> strategy: <c>yyyy-MM-ddTHH:mm:ssZ</c> in UTC, whole seconds, always
/// formatted with the invariant culture (a Hijri or Thai current culture must not leak in).
/// Reading is lenient: any ISO 8601 text, with or without fractional seconds or an offset.
/// </summary>
internal sealed class IsoDateConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected an ISO 8601 date string.");
        var text = reader.GetString();
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            throw new JsonException($"'{text}' is not an ISO 8601 date.");
        return parsed.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        var whole = new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        writer.WriteStringValue(whole.ToString(Format, CultureInfo.InvariantCulture));
    }
}

/// <summary>Swift writes UUIDs uppercase; System.Text.Json's default is lowercase. Reading ignores case.</summary>
internal sealed class UpperGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a UUID string.");
        var text = reader.GetString();
        if (!Guid.TryParseExact(text, "D", out var guid))
            throw new JsonException($"'{text}' is not a UUID.");
        return guid;
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant());
}

internal sealed class AppDataDto
{
    [JsonPropertyName("projects")] public List<ProjectDto>? Projects { get; set; }

    [JsonPropertyName("tasks")] public List<TaskDto>? Tasks { get; set; }

    [JsonPropertyName("version")] public int? Version { get; set; }

    public static AppDataDto From(AppData data) => new()
    {
        Projects = data.Projects.Select(ProjectDto.From).ToList(),
        Tasks = data.Tasks.Select(TaskDto.From).ToList(),
        Version = data.Version,
    };

    public AppData ToDomain() => new(
        JsonModel.Required(Version, "version"),
        JsonModel.Required(Projects, "projects").Select(p => p.ToDomain()).ToList(),
        JsonModel.Required(Tasks, "tasks").Select(t => t.ToDomain()).ToList());
}

internal sealed class ProjectDto
{
    [JsonPropertyName("color")] public string? Color { get; set; }

    [JsonPropertyName("icon")] public string? Icon { get; set; }

    [JsonPropertyName("id")] public Guid? Id { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("parentID")] public Guid? ParentId { get; set; }

    public static ProjectDto From(Project project) => new()
    {
        Color = JsonModel.ToJson(project.Color),
        Icon = project.Icon,
        Id = project.Id,
        Name = project.Name,
        ParentId = project.ParentId,
    };

    public Project ToDomain() => new()
    {
        Id = JsonModel.Required(Id, "id"),
        Name = JsonModel.Required(Name, "name"),
        Icon = JsonModel.Required(Icon, "icon"),
        Color = JsonModel.ParseColor(JsonModel.Required(Color, "color")),
        ParentId = ParentId,
    };
}

internal sealed class RecurrenceDto
{
    [JsonPropertyName("frequency")] public string? Frequency { get; set; }

    [JsonPropertyName("interval")] public int? Interval { get; set; }

    [JsonPropertyName("weekdays")] public List<int>? Weekdays { get; set; }

    public static RecurrenceDto From(RecurrenceRule rule) => new()
    {
        Frequency = JsonModel.ToJson(rule.Frequency),
        Interval = rule.Interval,
        Weekdays = rule.Weekdays.ToList(),
    };

    // Goes through Create, like the Mac's init(from:), so a hand-edited file can't bring in values the rest of the
    // code doesn't expect.
    public RecurrenceRule ToDomain() => RecurrenceRule.Create(
        JsonModel.ParseFrequency(JsonModel.Required(Frequency, "frequency")),
        Interval ?? 1,
        Weekdays ?? new List<int>());
}

internal sealed class SubtaskDto
{
    [JsonPropertyName("id")] public Guid? Id { get; set; }

    [JsonPropertyName("isDone")] public bool? IsDone { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }

    public static SubtaskDto From(Subtask subtask) => new()
    {
        Id = subtask.Id,
        IsDone = subtask.IsDone,
        Title = subtask.Title,
    };

    public Subtask ToDomain() => new()
    {
        Id = JsonModel.Required(Id, "id"),
        Title = JsonModel.Required(Title, "title"),
        IsDone = JsonModel.Required(IsDone, "isDone"),
    };
}

internal sealed class AttachmentDto
{
    [JsonPropertyName("addedAt")] public DateTime? AddedAt { get; set; }

    [JsonPropertyName("id")] public Guid? Id { get; set; }

    [JsonPropertyName("kind")] public string? Kind { get; set; }

    [JsonPropertyName("location")] public string? Location { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    public static AttachmentDto From(Attachment attachment) => new()
    {
        AddedAt = attachment.AddedAt,
        Id = attachment.Id,
        Kind = JsonModel.ToJson(attachment.Kind),
        Location = attachment.Location,
        Name = attachment.Name,
    };

    public Attachment ToDomain() => new()
    {
        Id = JsonModel.Required(Id, "id"),
        Kind = JsonModel.ParseKind(JsonModel.Required(Kind, "kind")),
        Name = JsonModel.Required(Name, "name"),
        Location = JsonModel.Required(Location, "location"),
        AddedAt = JsonModel.Required(AddedAt, "addedAt"),
    };
}

internal sealed class TaskDto
{
    [JsonPropertyName("attachments")] public List<AttachmentDto>? Attachments { get; set; }

    [JsonPropertyName("completedAt")] public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("dueDate")] public DateTime? DueDate { get; set; }

    [JsonPropertyName("hasDueTime")] public bool? HasDueTime { get; set; }

    [JsonPropertyName("id")] public Guid? Id { get; set; }

    [JsonPropertyName("nextOccurrenceDueDate")] public DateTime? NextOccurrenceDueDate { get; set; }

    [JsonPropertyName("nextOccurrenceID")] public Guid? NextOccurrenceId { get; set; }

    [JsonPropertyName("nextOccurrenceRule")] public RecurrenceDto? NextOccurrenceRule { get; set; }

    [JsonPropertyName("notes")] public string? Notes { get; set; }

    [JsonPropertyName("priority")] public int? Priority { get; set; }

    [JsonPropertyName("projectID")] public Guid? ProjectId { get; set; }

    [JsonPropertyName("recurrence")] public RecurrenceDto? Recurrence { get; set; }

    [JsonPropertyName("recurrenceDay")] public int? RecurrenceDay { get; set; }

    [JsonPropertyName("reminderOffset")] public double? ReminderOffset { get; set; }

    [JsonPropertyName("sortOrder")] public double? SortOrder { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("subtasks")] public List<SubtaskDto>? Subtasks { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }

    public static TaskDto From(TaskItem task) => new()
    {
        Attachments = task.Attachments.Select(AttachmentDto.From).ToList(),
        CompletedAt = task.CompletedAt,
        CreatedAt = task.CreatedAt,
        DueDate = task.DueDate,
        HasDueTime = task.HasDueTime,
        Id = task.Id,
        NextOccurrenceDueDate = task.NextOccurrenceDueDate,
        NextOccurrenceId = task.NextOccurrenceId,
        NextOccurrenceRule = task.NextOccurrenceRule is { } nextRule ? RecurrenceDto.From(nextRule) : null,
        Notes = task.Notes,
        Priority = JsonModel.ToJson(task.Priority),
        ProjectId = task.ProjectId,
        Recurrence = task.Recurrence is { } rule ? RecurrenceDto.From(rule) : null,
        RecurrenceDay = task.RecurrenceDay,
        ReminderOffset = task.ReminderOffset,
        SortOrder = task.SortOrder,
        Status = JsonModel.ToJson(task.Status),
        Subtasks = task.Subtasks.Select(SubtaskDto.From).ToList(),
        Title = task.Title,
    };

    // Mirrors the tolerant `init(from:)` in TaskItem.swift: only `id` is required.
    public TaskItem ToDomain() => new()
    {
        Id = JsonModel.Required(Id, "id"),
        Title = Title ?? "",
        Notes = Notes ?? "",
        Status = Status is null ? TaskStatus.Todo : JsonModel.ParseStatus(Status),
        Priority = Priority is { } priority ? JsonModel.ParsePriority(priority) : TodoCore.Priority.None,
        ProjectId = ProjectId,
        DueDate = DueDate,
        HasDueTime = HasDueTime ?? false,
        ReminderOffset = ReminderOffset,
        Recurrence = Recurrence?.ToDomain(),
        RecurrenceDay = RecurrenceDay,
        NextOccurrenceId = NextOccurrenceId,
        NextOccurrenceDueDate = NextOccurrenceDueDate,
        NextOccurrenceRule = NextOccurrenceRule?.ToDomain(),
        Subtasks = new ValueList<Subtask>((Subtasks ?? new List<SubtaskDto>()).Select(s => s.ToDomain())),
        Attachments = new ValueList<Attachment>((Attachments ?? new List<AttachmentDto>()).Select(a => a.ToDomain())),
        SortOrder = SortOrder ?? 0,
        CreatedAt = CreatedAt ?? DateTime.UtcNow,
        CompletedAt = CompletedAt,
    };
}
