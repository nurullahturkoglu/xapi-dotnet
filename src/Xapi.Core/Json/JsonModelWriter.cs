using System.Globalization;
using System.Text.Json;

namespace Xapi.Json;

internal sealed class JsonModelWriter(Utf8JsonWriter writer, XapiVersion version, StatementFormat format, IReadOnlyList<string>? acceptLanguages)
{
    private bool IdsOnly => format == StatementFormat.Ids;

    private bool Canonical => format == StatementFormat.Canonical;

    public void WriteStatement(Statement statement)
    {
        writer.WriteStartObject();
        if (statement.Id is { } id)
        {
            writer.WriteString("id", FormatUuid(id));
        }

        writer.WritePropertyName("actor");
        WriteActor(statement.Actor);
        writer.WritePropertyName("verb");
        WriteVerb(statement.Verb);
        writer.WritePropertyName("object");
        WriteStatementObject(statement.Object);
        WriteResult(statement.Result);
        WriteContext(statement.Context);
        WriteTimestamp("timestamp", statement.Timestamp);
        WriteTimestamp("stored", statement.Stored);
        if (statement.Authority is { } authority)
        {
            writer.WritePropertyName("authority");
            WriteActor(authority);
        }

        if (statement.Version is { } statementVersion)
        {
            writer.WriteString("version", statementVersion);
        }

        WriteAttachments(statement.Attachments);
        writer.WriteEndObject();
    }

    public void WriteActor(Actor actor)
    {
        writer.WriteStartObject();
        switch (actor)
        {
            case Agent agent:
                writer.WriteString("objectType", "Agent");
                WriteName(agent.Name);
                WriteIfi(agent.Ifi);
                break;
            case Group group:
                writer.WriteString("objectType", "Group");
                WriteName(group.Name);
                if (group.Ifi is { } ifi)
                {
                    WriteIfi(ifi);
                }

                if (!group.Members.IsEmpty && !(IdsOnly && !group.IsAnonymous))
                {
                    writer.WriteStartArray("member");
                    foreach (var member in group.Members)
                    {
                        WriteActor(member);
                    }

                    writer.WriteEndArray();
                }

                break;
            default:
                throw new ArgumentException($"Unsupported actor type {actor.GetType().Name}.", nameof(actor));
        }

        writer.WriteEndObject();
    }

    public void WriteActivity(Activity activity, bool writeObjectType)
    {
        writer.WriteStartObject();
        if (writeObjectType)
        {
            writer.WriteString("objectType", "Activity");
        }

        writer.WriteString("id", activity.Id.Value);
        if (activity.Definition is { } definition && !IdsOnly)
        {
            writer.WritePropertyName("definition");
            WriteDefinition(definition);
        }

        writer.WriteEndObject();
    }

    public void WritePerson(Person person)
    {
        writer.WriteStartObject();
        writer.WriteString("objectType", "Person");
        WriteStringArray("name", person.Names);
        WriteStringArray("mbox", person.Mboxes.Select(m => m.Value));
        WriteStringArray("mbox_sha1sum", person.MboxSha1Sums.Select(m => m.Value));
        WriteStringArray("openid", person.OpenIds.Select(o => o.Value.Value));
        if (!person.Accounts.IsEmpty)
        {
            writer.WriteStartArray("account");
            foreach (var account in person.Accounts)
            {
                WriteAccount(account);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    public void WriteAbout(About about)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("version");
        foreach (var item in about.Version)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
        WriteExtensions(about.Extensions);
        writer.WriteEndObject();
    }

    private void WriteName(string? name)
    {
        if (name is not null && !IdsOnly)
        {
            writer.WriteString("name", name);
        }
    }

    private void WriteIfi(InverseFunctionalIdentifier ifi)
    {
        switch (ifi)
        {
            case Mbox mbox:
                writer.WriteString("mbox", mbox.Value);
                break;
            case MboxSha1Sum sha1:
                writer.WriteString("mbox_sha1sum", sha1.Value);
                break;
            case OpenId openId:
                writer.WriteString("openid", openId.Value.Value);
                break;
            case Account account:
                writer.WritePropertyName("account");
                WriteAccount(account);
                break;
            default:
                throw new ArgumentException($"Unsupported identifier type {ifi.GetType().Name}.", nameof(ifi));
        }
    }

    private void WriteAccount(Account account)
    {
        writer.WriteStartObject();
        writer.WriteString("homePage", account.HomePage.Value);
        writer.WriteString("name", account.Name);
        writer.WriteEndObject();
    }

    private void WriteVerb(Verb verb)
    {
        writer.WriteStartObject();
        writer.WriteString("id", verb.Id.Value);
        if (verb.Display is { } display && !IdsOnly)
        {
            WriteLanguageMap("display", display, Canonical);
        }

        writer.WriteEndObject();
    }

    private void WriteStatementObject(StatementObject value)
    {
        switch (value)
        {
            case Activity activity:
                WriteActivity(activity, writeObjectType: true);
                break;
            case Actor actor:
                WriteActor(actor);
                break;
            case StatementRef reference:
                WriteStatementRef(reference);
                break;
            case SubStatement sub:
                WriteSubStatement(sub);
                break;
            default:
                throw new ArgumentException($"Unsupported object type {value.GetType().Name}.", nameof(value));
        }
    }

    private void WriteStatementRef(StatementRef reference)
    {
        writer.WriteStartObject();
        writer.WriteString("objectType", "StatementRef");
        writer.WriteString("id", FormatUuid(reference.Id));
        writer.WriteEndObject();
    }

    private void WriteSubStatement(SubStatement sub)
    {
        writer.WriteStartObject();
        writer.WriteString("objectType", "SubStatement");
        writer.WritePropertyName("actor");
        WriteActor(sub.Actor);
        writer.WritePropertyName("verb");
        WriteVerb(sub.Verb);
        writer.WritePropertyName("object");
        WriteStatementObject(sub.Object);
        WriteResult(sub.Result);
        WriteContext(sub.Context);
        WriteTimestamp("timestamp", sub.Timestamp);
        WriteAttachments(sub.Attachments);
        writer.WriteEndObject();
    }

    private void WriteDefinition(ActivityDefinition definition)
    {
        writer.WriteStartObject();
        WriteLanguageMap("name", definition.Name, Canonical);
        WriteLanguageMap("description", definition.Description, Canonical);
        if (definition.Type is { } type)
        {
            writer.WriteString("type", type.Value);
        }

        if (definition.MoreInfo is { } moreInfo)
        {
            writer.WriteString("moreInfo", moreInfo.Value);
        }

        if (definition.InteractionType is { } interactionType)
        {
            writer.WriteString("interactionType", InteractionTypes.ToJson(interactionType));
        }

        if (definition.CorrectResponsesPattern is { } pattern)
        {
            writer.WriteStartArray("correctResponsesPattern");
            foreach (var item in pattern)
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
        }

        WriteComponents("choices", definition.Choices);
        WriteComponents("scale", definition.Scale);
        WriteComponents("source", definition.Source);
        WriteComponents("target", definition.Target);
        WriteComponents("steps", definition.Steps);
        WriteExtensions(definition.Extensions);
        writer.WriteEndObject();
    }

    private void WriteComponents(string name, XapiList<InteractionComponent>? components)
    {
        if (components is null)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var component in components)
        {
            writer.WriteStartObject();
            writer.WriteString("id", component.Id);
            WriteLanguageMap("description", component.Description, Canonical);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private void WriteResult(Result? result)
    {
        if (result is null)
        {
            return;
        }

        writer.WriteStartObject("result");
        if (result.Score is { } score)
        {
            writer.WriteStartObject("score");
            WriteNumber("scaled", score.Scaled);
            WriteNumber("raw", score.Raw);
            WriteNumber("min", score.Min);
            WriteNumber("max", score.Max);
            writer.WriteEndObject();
        }

        if (result.Success is { } success)
        {
            writer.WriteBoolean("success", success);
        }

        if (result.Completion is { } completion)
        {
            writer.WriteBoolean("completion", completion);
        }

        if (result.Response is { } response)
        {
            writer.WriteString("response", response);
        }

        if (result.Duration is { } duration)
        {
            writer.WriteString("duration", duration.ToString());
        }

        WriteExtensions(result.Extensions);
        writer.WriteEndObject();
    }

    private void WriteContext(Context? context)
    {
        if (context is null)
        {
            return;
        }

        if (!version.IsV2 && (!context.ContextAgents.IsEmpty || !context.ContextGroups.IsEmpty))
        {
            throw new InvalidOperationException(
                "contextAgents and contextGroups do not exist in xAPI 1.0.3. Remove them or write with XapiVersion.V2_0_0.");
        }

        writer.WriteStartObject("context");
        if (context.Registration is { } registration)
        {
            writer.WriteString("registration", FormatUuid(registration));
        }

        if (context.Instructor is { } instructor)
        {
            writer.WritePropertyName("instructor");
            WriteActor(instructor);
        }

        if (context.Team is { } team)
        {
            writer.WritePropertyName("team");
            WriteActor(team);
        }

        if (!context.ContextAgents.IsEmpty)
        {
            writer.WriteStartArray("contextAgents");
            foreach (var item in context.ContextAgents)
            {
                writer.WriteStartObject();
                writer.WriteString("objectType", "contextAgent");
                writer.WritePropertyName("agent");
                WriteActor(item.Agent);
                WriteIriArray("relevantTypes", item.RelevantTypes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (!context.ContextGroups.IsEmpty)
        {
            writer.WriteStartArray("contextGroups");
            foreach (var item in context.ContextGroups)
            {
                writer.WriteStartObject();
                writer.WriteString("objectType", "contextGroup");
                writer.WritePropertyName("group");
                WriteActor(item.Group);
                WriteIriArray("relevantTypes", item.RelevantTypes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (context.ContextActivities is { } activities)
        {
            writer.WriteStartObject("contextActivities");
            WriteActivities("parent", activities.Parent);
            WriteActivities("grouping", activities.Grouping);
            WriteActivities("category", activities.Category);
            WriteActivities("other", activities.Other);
            writer.WriteEndObject();
        }

        if (context.Revision is { } revision)
        {
            writer.WriteString("revision", revision);
        }

        if (context.Platform is { } platform)
        {
            writer.WriteString("platform", platform);
        }

        if (context.Language is { } language)
        {
            writer.WriteString("language", language);
        }

        if (context.Statement is { } statement)
        {
            writer.WritePropertyName("statement");
            WriteStatementRef(statement);
        }

        WriteExtensions(context.Extensions);
        writer.WriteEndObject();
    }

    private void WriteActivities(string name, XapiList<Activity> activities)
    {
        if (activities.IsEmpty)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var activity in activities)
        {
            WriteActivity(activity, writeObjectType: false);
        }

        writer.WriteEndArray();
    }

    private void WriteAttachments(XapiList<Attachment> attachments)
    {
        if (attachments.IsEmpty)
        {
            return;
        }

        writer.WriteStartArray("attachments");
        foreach (var attachment in attachments)
        {
            writer.WriteStartObject();
            writer.WriteString("usageType", attachment.UsageType.Value);
            WriteLanguageMap("display", attachment.Display, selectOne: false);
            WriteLanguageMap("description", attachment.Description, selectOne: false);
            writer.WriteString("contentType", attachment.ContentType);
            writer.WriteNumber("length", attachment.Length);
            writer.WriteString("sha2", attachment.Sha2);
            if (attachment.FileUrl is { } fileUrl)
            {
                writer.WriteString("fileUrl", fileUrl.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private void WriteLanguageMap(string name, LanguageMap? map, bool selectOne)
    {
        if (map is null)
        {
            return;
        }

        writer.WriteStartObject(name);
        if (selectOne)
        {
            if (LanguageSelector.Select(map, acceptLanguages) is { } chosen)
            {
                writer.WriteString(chosen.Key, chosen.Value);
            }
        }
        else
        {
            foreach (var entry in map)
            {
                writer.WriteString(entry.Key, entry.Value);
            }
        }

        writer.WriteEndObject();
    }

    private void WriteExtensions(Extensions? extensions)
    {
        if (extensions is null)
        {
            return;
        }

        writer.WriteStartObject("extensions");
        foreach (var entry in extensions)
        {
            writer.WritePropertyName(entry.Key.Value);
            entry.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private void WriteIriArray(string name, XapiList<Iri> values)
    {
        if (values.IsEmpty)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value.Value);
        }

        writer.WriteEndArray();
    }

    private void WriteStringArray(string name, IEnumerable<string> values)
    {
        var any = false;
        foreach (var value in values)
        {
            if (!any)
            {
                writer.WriteStartArray(name);
                any = true;
            }

            writer.WriteStringValue(value);
        }

        if (any)
        {
            writer.WriteEndArray();
        }
    }

    private void WriteTimestamp(string name, DateTimeOffset? value)
    {
        if (value is { } timestamp)
        {
            writer.WriteString(name, XapiTimestamp.Format(timestamp));
        }
    }

    private void WriteNumber(string name, double? value)
    {
        if (value is not { } number)
        {
            return;
        }

#if NET
        writer.WriteNumber(name, number);
#else
        // The netstandard2.0 System.Text.Json formats doubles with "G17" (0.95 becomes 0.94999999999999996).
        writer.WritePropertyName(name);
        writer.WriteRawValue(number.ToString("R", CultureInfo.InvariantCulture));
#endif
    }

    private static string FormatUuid(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);
}
