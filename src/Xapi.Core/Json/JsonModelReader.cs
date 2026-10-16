using System.Text.Json;
using Xapi.Internal;

namespace Xapi.Json;

/// <summary>
/// Builds models from a <see cref="JsonElement"/> tree by hand, collecting every structural error with its path.
/// Format rules for plain strings (mbox, sha1, media types, versions) and cross-property rules are left to
/// <see cref="Validation.StatementValidator"/>.
/// </summary>
internal sealed class JsonModelReader
{
    private static readonly string[] StatementProperties =
        ["id", "actor", "verb", "object", "result", "context", "timestamp", "stored", "authority", "version", "attachments"];

    private static readonly string[] SubStatementProperties =
        ["objectType", "actor", "verb", "object", "result", "context", "timestamp", "attachments"];

    private static readonly string[] AgentProperties = ["objectType", "name", "mbox", "mbox_sha1sum", "openid", "account"];
    private static readonly string[] GroupProperties = ["objectType", "name", "member", "mbox", "mbox_sha1sum", "openid", "account"];
    private static readonly string[] AccountProperties = ["homePage", "name"];
    private static readonly string[] VerbProperties = ["id", "display"];
    private static readonly string[] ActivityProperties = ["objectType", "id", "definition"];

    private static readonly string[] DefinitionProperties =
    [
        "name", "description", "type", "moreInfo", "interactionType", "correctResponsesPattern",
        "choices", "scale", "source", "target", "steps", "extensions",
    ];

    private static readonly string[] ComponentProperties = ["id", "description"];
    private static readonly string[] StatementRefProperties = ["objectType", "id"];
    private static readonly string[] ResultProperties = ["score", "success", "completion", "response", "duration", "extensions"];
    private static readonly string[] ScoreProperties = ["scaled", "raw", "min", "max"];

    private static readonly string[] ContextProperties =
    [
        "registration", "instructor", "team", "contextActivities", "revision", "platform", "language", "statement", "extensions",
    ];

    private static readonly string[] ContextV2Properties = ["contextAgents", "contextGroups"];
    private static readonly string[] ContextActivitiesProperties = ["parent", "grouping", "category", "other"];
    private static readonly string[] ContextAgentProperties = ["objectType", "agent", "relevantTypes"];
    private static readonly string[] ContextGroupProperties = ["objectType", "group", "relevantTypes"];

    private static readonly string[] AttachmentProperties =
        ["usageType", "display", "description", "contentType", "length", "sha2", "fileUrl"];

    private static readonly string[] PersonProperties = ["objectType", "name", "mbox", "mbox_sha1sum", "openid", "account"];
    private static readonly string[] AboutProperties = ["version", "extensions"];

    private readonly XapiVersion _version;
    private readonly bool _strict;

    public JsonModelReader(XapiVersion version, XapiReadMode mode)
    {
        _version = version;
        _strict = mode == XapiReadMode.Strict;
    }

    private enum ActorRole
    {
        Any,
        AgentOnly,
        Member,
        GroupOnly,
    }

    public List<XapiError> Errors { get; } = [];

    public XapiVersion Version => _version;

    public Statement? ReadStatement(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, StatementProperties);
        if (p is null)
        {
            return null;
        }

        var id = p.TryGet("id", out var e) ? ReadUuid(e, p.PathOf("id")) : null;
        var actor = p.TryGet("actor", out e) ? ReadActor(e, p.PathOf("actor"), ActorRole.Any) : Required<Actor>(p, "actor");
        var verb = p.TryGet("verb", out e) ? ReadVerb(e, p.PathOf("verb")) : Required<Verb>(p, "verb");
        var obj = p.TryGet("object", out e) ? ReadStatementObject(e, p.PathOf("object"), inSubStatement: false) : Required<StatementObject>(p, "object");
        var result = p.TryGet("result", out e) ? ReadResult(e, p.PathOf("result")) : null;
        var context = p.TryGet("context", out e) ? ReadContext(e, p.PathOf("context")) : null;
        var timestamp = p.TryGet("timestamp", out e) ? ReadTimestamp(e, p.PathOf("timestamp")) : null;
        var stored = p.TryGet("stored", out e) ? ReadTimestamp(e, p.PathOf("stored")) : null;
        var authority = p.TryGet("authority", out e) ? ReadActor(e, p.PathOf("authority"), ActorRole.Any) : null;
        var version = p.TryGet("version", out e) ? ReadString(e, p.PathOf("version")) : null;
        var attachments = p.TryGet("attachments", out e) ? ReadArray(e, p.PathOf("attachments"), ReadAttachment) : null;

        if (Errors.Count > start || actor is null || verb is null || obj is null)
        {
            return null;
        }

        return new Statement(actor, verb, obj)
        {
            Id = id,
            Result = result,
            Context = context,
            Timestamp = timestamp,
            Stored = stored,
            Authority = authority,
            Version = version,
            Attachments = attachments!,
        };
    }

    public Actor? ReadActorRoot(JsonElement element, string path) => ReadActor(element, path, ActorRole.Any);

    public Activity? ReadActivity(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, ActivityProperties);
        if (p is null)
        {
            return null;
        }

        if (p.TryGet("objectType", out var e))
        {
            ExpectObjectType(e, p.PathOf("objectType"), "Activity");
        }

        var id = p.TryGet("id", out e) ? ReadIri(e, p.PathOf("id")) : Required<Iri?>(p, "id");
        var definition = p.TryGet("definition", out e) ? ReadDefinition(e, p.PathOf("definition")) : null;
        if (Errors.Count > start || id is null)
        {
            return null;
        }

        return new Activity(id.Value) { Definition = definition };
    }

    public Person? ReadPerson(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, PersonProperties);
        if (p is null)
        {
            return null;
        }

        if (p.TryGet("objectType", out var e))
        {
            ExpectObjectType(e, p.PathOf("objectType"), "Person");
        }
        else
        {
            Required<string>(p, "objectType");
        }

        var names = p.TryGet("name", out e) ? ReadArray(e, p.PathOf("name"), ReadString) : null;
        var mboxes = p.TryGet("mbox", out e) ? ReadArray(e, p.PathOf("mbox"), (x, path) => ReadString(x, path) is { } s ? new Mbox(s) : null) : null;
        var sha1s = p.TryGet("mbox_sha1sum", out e) ? ReadArray(e, p.PathOf("mbox_sha1sum"), (x, path) => ReadString(x, path) is { } s ? new MboxSha1Sum(s) : null) : null;
        var openIds = p.TryGet("openid", out e) ? ReadArray(e, p.PathOf("openid"), (x, path) => ReadIri(x, path) is { } iri ? new OpenId(iri) : null) : null;
        var accounts = p.TryGet("account", out e) ? ReadArray(e, p.PathOf("account"), ReadAccount) : null;
        if (Errors.Count > start)
        {
            return null;
        }

        return new Person
        {
            Names = names!,
            Mboxes = mboxes!,
            MboxSha1Sums = sha1s!,
            OpenIds = openIds!,
            Accounts = accounts!,
        };
    }

    public About? ReadAbout(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, AboutProperties);
        if (p is null)
        {
            return null;
        }

        var versions = p.TryGet("version", out var e) ? ReadArray(e, p.PathOf("version"), ReadString) : Required<XapiList<string>>(p, "version");
        var extensions = p.TryGet("extensions", out e) ? ReadExtensions(e, p.PathOf("extensions")) : null;
        if (Errors.Count > start || versions is null)
        {
            return null;
        }

        return new About(versions) { Extensions = extensions };
    }

    private Actor? ReadActor(JsonElement element, string path, ActorRole role)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            TypeError(path, "an Agent or Group object");
            return null;
        }

        var objectType = PeekObjectType(element);
        switch (objectType)
        {
            case null when role == ActorRole.GroupOnly:
                AddError(XapiErrorCodes.ObjectType, JsonPath.Property(path, "objectType"), "A Group requires \"objectType\": \"Group\".");
                return null;
            case "Group" when role == ActorRole.Member:
                AddError(XapiErrorCodes.GroupMemberNotAgent, path, "A Group member must be an Agent, not a Group.");
                return null;
            case "Group" when role != ActorRole.AgentOnly:
                return ReadGroup(element, path);
            case null or "Agent" when role != ActorRole.GroupOnly:
                return ReadAgent(element, path);
            default:
                var expected = role switch
                {
                    ActorRole.AgentOnly or ActorRole.Member => "\"Agent\"",
                    ActorRole.GroupOnly => "\"Group\"",
                    _ => "\"Agent\" or \"Group\"",
                };
                AddError(XapiErrorCodes.ObjectType, JsonPath.Property(path, "objectType"), $"\"objectType\" must be {expected} here.");
                return null;
        }
    }

    private Agent? ReadAgent(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, AgentProperties);
        if (p is null)
        {
            return null;
        }

        if (p.TryGet("objectType", out var e))
        {
            ExpectObjectType(e, p.PathOf("objectType"), "Agent");
        }

        var name = p.TryGet("name", out e) ? ReadString(e, p.PathOf("name")) : null;
        var ifi = ReadIfi(p, path, required: true);
        if (Errors.Count > start || ifi is null)
        {
            return null;
        }

        return new Agent(ifi) { Name = name };
    }

    private Group? ReadGroup(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, GroupProperties);
        if (p is null)
        {
            return null;
        }

        if (p.TryGet("objectType", out var e))
        {
            ExpectObjectType(e, p.PathOf("objectType"), "Group");
        }

        var name = p.TryGet("name", out e) ? ReadString(e, p.PathOf("name")) : null;
        var members = p.TryGet("member", out e)
            ? ReadArray(e, p.PathOf("member"), (x, itemPath) => (Agent?)ReadActor(x, itemPath, ActorRole.Member))
            : null;
        var ifi = ReadIfi(p, path, required: false);
        if (Errors.Count > start)
        {
            return null;
        }

        var group = ifi is null ? new Group(members ?? XapiList<Agent>.Empty) : new Group(ifi) { Members = members! };
        return group with { Name = name };
    }

    private InverseFunctionalIdentifier? ReadIfi(PropertyBag p, string path, bool required)
    {
        var found = new List<InverseFunctionalIdentifier?>();
        if (p.TryGet("mbox", out var e))
        {
            found.Add(ReadString(e, p.PathOf("mbox")) is { } s ? new Mbox(s) : null);
        }

        if (p.TryGet("mbox_sha1sum", out e))
        {
            found.Add(ReadString(e, p.PathOf("mbox_sha1sum")) is { } s ? new MboxSha1Sum(s) : null);
        }

        if (p.TryGet("openid", out e))
        {
            found.Add(ReadIri(e, p.PathOf("openid")) is { } iri ? new OpenId(iri) : null);
        }

        if (p.TryGet("account", out e))
        {
            found.Add(ReadAccount(e, p.PathOf("account")));
        }

        if (found.Count > 1)
        {
            AddError(XapiErrorCodes.IfiMultiple, path, "Only one of \"mbox\", \"mbox_sha1sum\", \"openid\" and \"account\" may be used.");
            return null;
        }

        if (found.Count == 0)
        {
            if (required)
            {
                AddError(XapiErrorCodes.IfiMissing, path, "One of \"mbox\", \"mbox_sha1sum\", \"openid\" or \"account\" is required.");
            }

            return null;
        }

        return found[0];
    }

    private Account? ReadAccount(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, AccountProperties);
        if (p is null)
        {
            return null;
        }

        var homePage = p.TryGet("homePage", out var e) ? ReadIri(e, p.PathOf("homePage")) : Required<Iri?>(p, "homePage");
        var name = p.TryGet("name", out e) ? ReadString(e, p.PathOf("name")) : Required<string>(p, "name");
        if (Errors.Count > start || homePage is null || name is null)
        {
            return null;
        }

        return new Account(homePage.Value, name);
    }

    private Verb? ReadVerb(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, VerbProperties);
        if (p is null)
        {
            return null;
        }

        var id = p.TryGet("id", out var e) ? ReadIri(e, p.PathOf("id")) : Required<Iri?>(p, "id");
        var display = p.TryGet("display", out e) ? ReadLanguageMap(e, p.PathOf("display")) : null;
        if (Errors.Count > start || id is null)
        {
            return null;
        }

        return new Verb(id.Value) { Display = display };
    }

    private StatementObject? ReadStatementObject(JsonElement element, string path, bool inSubStatement)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            TypeError(path, "an object");
            return null;
        }

        switch (PeekObjectType(element))
        {
            case null or "Activity":
                return ReadActivity(element, path);
            case "Agent" or "Group":
                return ReadActor(element, path, ActorRole.Any);
            case "StatementRef":
                return ReadStatementRef(element, path, objectTypeRequired: true);
            case "SubStatement" when inSubStatement:
                AddError(XapiErrorCodes.SubStatementNested, path, "A SubStatement cannot have a SubStatement as its object.");
                return null;
            case "SubStatement":
                return ReadSubStatement(element, path);
            default:
                AddError(
                    XapiErrorCodes.ObjectType,
                    JsonPath.Property(path, "objectType"),
                    "\"objectType\" must be \"Activity\", \"Agent\", \"Group\", \"StatementRef\" or \"SubStatement\".");
                return null;
        }
    }

    private StatementRef? ReadStatementRef(JsonElement element, string path, bool objectTypeRequired)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, StatementRefProperties);
        if (p is null)
        {
            return null;
        }

        if (p.TryGet("objectType", out var e))
        {
            ExpectObjectType(e, p.PathOf("objectType"), "StatementRef");
        }
        else if (objectTypeRequired)
        {
            Required<string>(p, "objectType");
        }

        var id = p.TryGet("id", out e) ? ReadUuid(e, p.PathOf("id")) : Required<Guid?>(p, "id");
        if (Errors.Count > start || id is null)
        {
            return null;
        }

        return new StatementRef(id.Value);
    }

    private SubStatement? ReadSubStatement(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, SubStatementProperties);
        if (p is null)
        {
            return null;
        }

        var actor = p.TryGet("actor", out var e) ? ReadActor(e, p.PathOf("actor"), ActorRole.Any) : Required<Actor>(p, "actor");
        var verb = p.TryGet("verb", out e) ? ReadVerb(e, p.PathOf("verb")) : Required<Verb>(p, "verb");
        var obj = p.TryGet("object", out e) ? ReadStatementObject(e, p.PathOf("object"), inSubStatement: true) : Required<StatementObject>(p, "object");
        var result = p.TryGet("result", out e) ? ReadResult(e, p.PathOf("result")) : null;
        var context = p.TryGet("context", out e) ? ReadContext(e, p.PathOf("context")) : null;
        var timestamp = p.TryGet("timestamp", out e) ? ReadTimestamp(e, p.PathOf("timestamp")) : null;
        var attachments = p.TryGet("attachments", out e) ? ReadArray(e, p.PathOf("attachments"), ReadAttachment) : null;
        if (Errors.Count > start || actor is null || verb is null || obj is null)
        {
            return null;
        }

        return new SubStatement(actor, verb, obj)
        {
            Result = result,
            Context = context,
            Timestamp = timestamp,
            Attachments = attachments!,
        };
    }

    private ActivityDefinition? ReadDefinition(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, DefinitionProperties);
        if (p is null)
        {
            return null;
        }

        var definition = new ActivityDefinition
        {
            Name = p.TryGet("name", out var e) ? ReadLanguageMap(e, p.PathOf("name")) : null,
            Description = p.TryGet("description", out e) ? ReadLanguageMap(e, p.PathOf("description")) : null,
            Type = p.TryGet("type", out e) ? ReadIri(e, p.PathOf("type")) : null,
            MoreInfo = p.TryGet("moreInfo", out e) ? ReadIri(e, p.PathOf("moreInfo")) : null,
            InteractionType = p.TryGet("interactionType", out e) ? ReadInteractionType(e, p.PathOf("interactionType")) : null,
            CorrectResponsesPattern = p.TryGet("correctResponsesPattern", out e) ? ReadArray(e, p.PathOf("correctResponsesPattern"), ReadString) : null,
            Choices = p.TryGet("choices", out e) ? ReadArray(e, p.PathOf("choices"), ReadComponent) : null,
            Scale = p.TryGet("scale", out e) ? ReadArray(e, p.PathOf("scale"), ReadComponent) : null,
            Source = p.TryGet("source", out e) ? ReadArray(e, p.PathOf("source"), ReadComponent) : null,
            Target = p.TryGet("target", out e) ? ReadArray(e, p.PathOf("target"), ReadComponent) : null,
            Steps = p.TryGet("steps", out e) ? ReadArray(e, p.PathOf("steps"), ReadComponent) : null,
            Extensions = p.TryGet("extensions", out e) ? ReadExtensions(e, p.PathOf("extensions")) : null,
        };

        return Errors.Count > start ? null : definition;
    }

    private InteractionType? ReadInteractionType(JsonElement element, string path)
    {
        if (ReadString(element, path) is not { } text)
        {
            return null;
        }

        if (InteractionTypes.TryParse(text, out var type))
        {
            return type;
        }

        AddError(
            XapiErrorCodes.InteractionType,
            path,
            $"\"{text}\" is not an interaction type. Use one of: {string.Join(", ", InteractionTypes.Names)}.");
        return null;
    }

    private InteractionComponent? ReadComponent(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, ComponentProperties);
        if (p is null)
        {
            return null;
        }

        var id = p.TryGet("id", out var e) ? ReadString(e, p.PathOf("id")) : Required<string>(p, "id");
        var description = p.TryGet("description", out e) ? ReadLanguageMap(e, p.PathOf("description")) : null;
        if (Errors.Count > start || id is null)
        {
            return null;
        }

        return new InteractionComponent(id) { Description = description };
    }

    private Result? ReadResult(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, ResultProperties);
        if (p is null)
        {
            return null;
        }

        var result = new Result
        {
            Score = p.TryGet("score", out var e) ? ReadScore(e, p.PathOf("score")) : null,
            Success = p.TryGet("success", out e) ? ReadBoolean(e, p.PathOf("success")) : null,
            Completion = p.TryGet("completion", out e) ? ReadBoolean(e, p.PathOf("completion")) : null,
            Response = p.TryGet("response", out e) ? ReadString(e, p.PathOf("response")) : null,
            Duration = p.TryGet("duration", out e) ? ReadDuration(e, p.PathOf("duration")) : null,
            Extensions = p.TryGet("extensions", out e) ? ReadExtensions(e, p.PathOf("extensions")) : null,
        };

        return Errors.Count > start ? null : result;
    }

    private Score? ReadScore(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, ScoreProperties);
        if (p is null)
        {
            return null;
        }

        var score = new Score
        {
            Scaled = p.TryGet("scaled", out var e) ? ReadNumber(e, p.PathOf("scaled")) : null,
            Raw = p.TryGet("raw", out e) ? ReadNumber(e, p.PathOf("raw")) : null,
            Min = p.TryGet("min", out e) ? ReadNumber(e, p.PathOf("min")) : null,
            Max = p.TryGet("max", out e) ? ReadNumber(e, p.PathOf("max")) : null,
        };

        return Errors.Count > start ? null : score;
    }

    private Context? ReadContext(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, ContextProperties, ContextV2Properties);
        if (p is null)
        {
            return null;
        }

        var context = new Context
        {
            Registration = p.TryGet("registration", out var e) ? ReadUuid(e, p.PathOf("registration")) : null,
            Instructor = p.TryGet("instructor", out e) ? ReadActor(e, p.PathOf("instructor"), ActorRole.Any) : null,
            Team = p.TryGet("team", out e) ? (Group?)ReadActor(e, p.PathOf("team"), ActorRole.GroupOnly) : null,
            ContextAgents = (p.TryGet("contextAgents", out e) ? ReadArray(e, p.PathOf("contextAgents"), ReadContextAgent) : null)!,
            ContextGroups = (p.TryGet("contextGroups", out e) ? ReadArray(e, p.PathOf("contextGroups"), ReadContextGroup) : null)!,
            ContextActivities = p.TryGet("contextActivities", out e) ? ReadContextActivities(e, p.PathOf("contextActivities")) : null,
            Revision = p.TryGet("revision", out e) ? ReadString(e, p.PathOf("revision")) : null,
            Platform = p.TryGet("platform", out e) ? ReadString(e, p.PathOf("platform")) : null,
            Language = p.TryGet("language", out e) ? ReadString(e, p.PathOf("language")) : null,
            Statement = p.TryGet("statement", out e) ? ReadStatementRef(e, p.PathOf("statement"), objectTypeRequired: true) : null,
            Extensions = p.TryGet("extensions", out e) ? ReadExtensions(e, p.PathOf("extensions")) : null,
        };

        return Errors.Count > start ? null : context;
    }

    private ContextActivities? ReadContextActivities(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, ContextActivitiesProperties);
        if (p is null)
        {
            return null;
        }

        var activities = new ContextActivities
        {
            Parent = (p.TryGet("parent", out var e) ? ReadActivityOrArray(e, p.PathOf("parent")) : null)!,
            Grouping = (p.TryGet("grouping", out e) ? ReadActivityOrArray(e, p.PathOf("grouping")) : null)!,
            Category = (p.TryGet("category", out e) ? ReadActivityOrArray(e, p.PathOf("category")) : null)!,
            Other = (p.TryGet("other", out e) ? ReadActivityOrArray(e, p.PathOf("other")) : null)!,
        };

        return Errors.Count > start ? null : activities;
    }

    private XapiList<Activity>? ReadActivityOrArray(JsonElement element, string path)
    {
        // 4.1.6.1/4.2.4.2: a single Activity object is accepted and returned as an array of one.
        if (element.ValueKind == JsonValueKind.Object)
        {
            return ReadActivity(element, path) is { } activity ? XapiList.Create(activity) : null;
        }

        return ReadArray(element, path, ReadActivity);
    }

    private ContextAgent? ReadContextAgent(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, ContextAgentProperties);
        if (p is null)
        {
            return null;
        }

        if (p.TryGet("objectType", out var e))
        {
            ExpectObjectType(e, p.PathOf("objectType"), "contextAgent");
        }
        else
        {
            Required<string>(p, "objectType");
        }

        var agent = p.TryGet("agent", out e) ? (Agent?)ReadActor(e, p.PathOf("agent"), ActorRole.AgentOnly) : Required<Agent>(p, "agent");
        var relevantTypes = p.TryGet("relevantTypes", out e) ? ReadRelevantTypes(e, p.PathOf("relevantTypes")) : null;
        if (Errors.Count > start || agent is null)
        {
            return null;
        }

        return new ContextAgent(agent) { RelevantTypes = relevantTypes! };
    }

    private ContextGroup? ReadContextGroup(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, ContextGroupProperties);
        if (p is null)
        {
            return null;
        }

        if (p.TryGet("objectType", out var e))
        {
            ExpectObjectType(e, p.PathOf("objectType"), "contextGroup");
        }
        else
        {
            Required<string>(p, "objectType");
        }

        var group = p.TryGet("group", out e) ? (Group?)ReadActor(e, p.PathOf("group"), ActorRole.GroupOnly) : Required<Group>(p, "group");
        var relevantTypes = p.TryGet("relevantTypes", out e) ? ReadRelevantTypes(e, p.PathOf("relevantTypes")) : null;
        if (Errors.Count > start || group is null)
        {
            return null;
        }

        return new ContextGroup(group) { RelevantTypes = relevantTypes! };
    }

    private XapiList<Iri>? ReadRelevantTypes(JsonElement element, string path)
    {
        var list = ReadStructArray(element, path, ReadIri);
        if (list is { IsEmpty: true })
        {
            AddError(XapiErrorCodes.RelevantTypesEmpty, path, "\"relevantTypes\" must contain at least one IRI when present.");
            return null;
        }

        return list;
    }

    private Attachment? ReadAttachment(JsonElement element, string path)
    {
        var start = Errors.Count;
        var p = ReadObject(element, path, AttachmentProperties);
        if (p is null)
        {
            return null;
        }

        var usageType = p.TryGet("usageType", out var e) ? ReadIri(e, p.PathOf("usageType")) : Required<Iri?>(p, "usageType");
        var display = p.TryGet("display", out e) ? ReadLanguageMap(e, p.PathOf("display")) : Required<LanguageMap>(p, "display");
        var description = p.TryGet("description", out e) ? ReadLanguageMap(e, p.PathOf("description")) : null;
        var contentType = p.TryGet("contentType", out e) ? ReadString(e, p.PathOf("contentType")) : Required<string>(p, "contentType");
        var length = p.TryGet("length", out e) ? ReadInteger(e, p.PathOf("length")) : Required<long?>(p, "length");
        var sha2 = p.TryGet("sha2", out e) ? ReadString(e, p.PathOf("sha2")) : Required<string>(p, "sha2");
        var fileUrl = p.TryGet("fileUrl", out e) ? ReadIri(e, p.PathOf("fileUrl")) : null;
        if (Errors.Count > start || usageType is null || display is null || contentType is null || length is null || sha2 is null)
        {
            return null;
        }

        return new Attachment(usageType.Value, display, contentType, length.Value, sha2)
        {
            Description = description,
            FileUrl = fileUrl,
        };
    }

    private LanguageMap? ReadLanguageMap(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            TypeError(path, "a language map object");
            return null;
        }

        var start = Errors.Count;
        var entries = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var itemPath = JsonPath.Property(path, property.Name);
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                NullError(itemPath);
                continue;
            }

            var value = ReadString(property.Value, itemPath);
            if (!XapiFormats.IsLanguageTag(property.Name))
            {
                if (_strict)
                {
                    AddError(XapiErrorCodes.LanguageTag, itemPath, $"\"{property.Name}\" is not a well-formed RFC 5646 language tag.");
                }

                continue;
            }

            if (!seen.Add(property.Name))
            {
                if (_strict)
                {
                    AddError(XapiErrorCodes.PropertyDuplicate, itemPath, $"The language tag \"{property.Name}\" appears more than once (tags are case-insensitive).");
                }

                continue;
            }

            if (value is not null)
            {
                entries.Add(new KeyValuePair<string, string>(property.Name, value));
            }
        }

        return Errors.Count > start ? null : new LanguageMap(entries);
    }

    private Extensions? ReadExtensions(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            TypeError(path, "an extensions object");
            return null;
        }

        var start = Errors.Count;
        var entries = new List<KeyValuePair<Iri, JsonElement>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var itemPath = JsonPath.Property(path, property.Name);
            if (!seen.Add(property.Name))
            {
                if (_strict)
                {
                    AddError(XapiErrorCodes.PropertyDuplicate, itemPath, $"The extension \"{property.Name}\" appears more than once.");
                }

                continue;
            }

            if (!Iri.TryParse(property.Name, out var key))
            {
                // 4.2.7: an extension key that is not an IRI is rejected; values are never judged.
                AddError(XapiErrorCodes.Iri, itemPath, $"The extension key \"{property.Name}\" is not an absolute IRI.");
                continue;
            }

            if (_strict)
            {
                CheckDuplicateKeys(property.Value, itemPath);
            }

            entries.Add(new KeyValuePair<Iri, JsonElement>(key, property.Value));
        }

        return Errors.Count > start ? null : new Extensions(entries);
    }

    private void CheckDuplicateKeys(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var itemPath = JsonPath.Property(path, property.Name);
                    if (!seen.Add(property.Name))
                    {
                        AddError(XapiErrorCodes.PropertyDuplicate, itemPath, $"The property \"{property.Name}\" appears more than once.");
                    }

                    CheckDuplicateKeys(property.Value, itemPath);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CheckDuplicateKeys(item, JsonPath.Index(path, index++));
                }

                break;
        }
    }

    private XapiList<T>? ReadArray<T>(JsonElement element, string path, Func<JsonElement, string, T?> readItem)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            TypeError(path, "an array");
            return null;
        }

        var start = Errors.Count;
        var items = new List<T>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var itemPath = JsonPath.Index(path, index++);
            if (item.ValueKind == JsonValueKind.Null)
            {
                NullError(itemPath);
                continue;
            }

            if (readItem(item, itemPath) is { } value)
            {
                items.Add(value);
            }
        }

        return Errors.Count > start ? null : XapiList.CreateRange(items);
    }

    private XapiList<T>? ReadStructArray<T>(JsonElement element, string path, Func<JsonElement, string, T?> readItem)
        where T : struct
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            TypeError(path, "an array");
            return null;
        }

        var start = Errors.Count;
        var items = new List<T>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var itemPath = JsonPath.Index(path, index++);
            if (item.ValueKind == JsonValueKind.Null)
            {
                NullError(itemPath);
                continue;
            }

            if (readItem(item, itemPath) is { } value)
            {
                items.Add(value);
            }
        }

        return Errors.Count > start ? null : XapiList.CreateRange(items);
    }

    private string? ReadString(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            TypeError(path, "a string");
            return null;
        }

        return element.GetString();
    }

    private bool? ReadBoolean(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                TypeError(path, "a boolean");
                return null;
        }
    }

    private double? ReadNumber(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            TypeError(path, "a number");
            return null;
        }

        if (!element.TryGetDouble(out var value) || double.IsInfinity(value))
        {
            AddError(XapiErrorCodes.JsonType, path, "The number is out of range.");
            return null;
        }

        return value;
    }

    private long? ReadInteger(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value))
        {
            TypeError(path, "an integer");
            return null;
        }

        return value;
    }

    private Iri? ReadIri(JsonElement element, string path)
    {
        if (ReadString(element, path) is not { } text)
        {
            return null;
        }

        if (Iri.TryParse(text, out var iri))
        {
            return iri;
        }

        AddError(XapiErrorCodes.Iri, path, $"\"{text}\" is not an absolute IRI.");
        return null;
    }

    private Guid? ReadUuid(JsonElement element, string path)
    {
        if (ReadString(element, path) is not { } text)
        {
            return null;
        }

        if (XapiFormats.IsUuid(text))
        {
            return Guid.Parse(text);
        }

        AddError(XapiErrorCodes.Uuid, path, $"\"{text}\" is not a UUID in the 8-4-4-4-12 hexadecimal form.");
        return null;
    }

    private DateTimeOffset? ReadTimestamp(JsonElement element, string path)
    {
        if (ReadString(element, path) is not { } text)
        {
            return null;
        }

        var rules = _strict ? _version : XapiVersion.V2_0_0;
        if (XapiTimestamp.TryParse(text, rules, out var timestamp))
        {
            return timestamp;
        }

        AddError(XapiErrorCodes.Timestamp, path, $"\"{text}\" is not an RFC 3339 timestamp with a UTC offset.");
        return null;
    }

    private XapiDuration? ReadDuration(JsonElement element, string path)
    {
        if (ReadString(element, path) is not { } text)
        {
            return null;
        }

        if (XapiDuration.TryParse(text, out var duration))
        {
            return duration;
        }

        AddError(XapiErrorCodes.Duration, path, $"\"{text}\" is not an ISO 8601 duration (PnYnMnDTnHnMnS or PnW).");
        return null;
    }

    private void ExpectObjectType(JsonElement element, string path, string expected)
    {
        if (ReadString(element, path) is { } text && text != expected)
        {
            AddError(XapiErrorCodes.ObjectType, path, $"\"objectType\" must be \"{expected}\" here, not \"{text}\".");
        }
    }

    private static string? PeekObjectType(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("objectType"))
            {
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }

        return null;
    }

    private PropertyBag? ReadObject(JsonElement element, string path, string[] known, string[]? laterVersion = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            TypeError(path, "an object");
            return null;
        }

        laterVersion ??= [];
        var bag = new PropertyBag(path, known, laterVersion);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            var propertyPath = JsonPath.Property(path, name);
            if (!seen.Add(name))
            {
                if (_strict)
                {
                    AddError(XapiErrorCodes.PropertyDuplicate, propertyPath, $"The property \"{name}\" appears more than once.");
                }

                continue;
            }

            var index = Array.IndexOf(known, name);
            if (index < 0)
            {
                var later = Array.IndexOf(laterVersion, name);
                if (later >= 0)
                {
                    if (_strict && !_version.IsV2)
                    {
                        AddError(XapiErrorCodes.PropertyVersion, propertyPath, $"\"{name}\" is not defined in xAPI {_version.HeaderValue}.");
                        continue;
                    }

                    index = known.Length + later;
                }
            }

            if (index < 0)
            {
                if (_strict)
                {
                    var caseMatch = known.Concat(laterVersion).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                    if (caseMatch is not null)
                    {
                        AddError(XapiErrorCodes.PropertyCase, propertyPath, $"\"{name}\" must be spelled \"{caseMatch}\".");
                    }
                    else
                    {
                        AddError(XapiErrorCodes.PropertyUnknown, propertyPath, $"\"{name}\" is not allowed here.");
                    }
                }

                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                NullError(propertyPath);
                continue;
            }

            bag.Set(index, property.Value);
        }

        return bag;
    }

    private T? Required<T>(PropertyBag bag, string name)
    {
        AddError(XapiErrorCodes.PropertyRequired, bag.PathOf(name), $"\"{name}\" is required.");
        return default;
    }

    private void NullError(string path)
    {
        if (_strict)
        {
            AddError(XapiErrorCodes.JsonNull, path, "null is not allowed outside extensions.");
        }
    }

    private void TypeError(string path, string expected) =>
        AddError(XapiErrorCodes.JsonType, path, $"Expected {expected}.");

    private void AddError(string code, string path, string message) => Errors.Add(new XapiError(code, path, message));

    private sealed class PropertyBag(string path, string[] known, string[] laterVersion)
    {
        private readonly JsonElement[] _values = new JsonElement[known.Length + laterVersion.Length];

        public void Set(int index, JsonElement value) => _values[index] = value;

        public bool TryGet(string name, out JsonElement value)
        {
            var index = Array.IndexOf(known, name);
            if (index < 0)
            {
                index = known.Length + Array.IndexOf(laterVersion, name);
            }

            value = _values[index];
            return value.ValueKind != JsonValueKind.Undefined;
        }

        public string PathOf(string name) => JsonPath.Property(path, name);
    }
}
