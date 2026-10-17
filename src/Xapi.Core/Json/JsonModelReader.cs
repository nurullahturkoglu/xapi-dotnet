using System.Runtime.InteropServices;
using System.Text;
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
    private static readonly PropertySet StatementProperties =
        new(["id", "actor", "verb", "object", "result", "context", "timestamp", "stored", "authority", "version", "attachments"]);

    private static readonly PropertySet SubStatementProperties =
        new(["objectType", "actor", "verb", "object", "result", "context", "timestamp", "attachments"]);

    private static readonly PropertySet AgentProperties = new(["objectType", "name", "mbox", "mbox_sha1sum", "openid", "account"]);
    private static readonly PropertySet GroupProperties = new(["objectType", "name", "member", "mbox", "mbox_sha1sum", "openid", "account"]);
    private static readonly PropertySet AccountProperties = new(["homePage", "name"]);
    private static readonly PropertySet VerbProperties = new(["id", "display"]);
    private static readonly PropertySet ActivityProperties = new(["objectType", "id", "definition"]);

    private static readonly PropertySet DefinitionProperties = new(
    [
        "name", "description", "type", "moreInfo", "interactionType", "correctResponsesPattern",
        "choices", "scale", "source", "target", "steps", "extensions",
    ]);

    private static readonly PropertySet ComponentProperties = new(["id", "description"]);
    private static readonly PropertySet StatementRefProperties = new(["objectType", "id"]);
    private static readonly PropertySet ResultProperties = new(["score", "success", "completion", "response", "duration", "extensions"]);
    private static readonly PropertySet ScoreProperties = new(["scaled", "raw", "min", "max"]);

    private static readonly PropertySet ContextProperties = new(
        ["registration", "instructor", "team", "contextActivities", "revision", "platform", "language", "statement", "extensions"],
        laterVersion: ["contextAgents", "contextGroups"]);

    private static readonly PropertySet ContextActivitiesProperties = new(["parent", "grouping", "category", "other"]);
    private static readonly PropertySet ContextAgentProperties = new(["objectType", "agent", "relevantTypes"]);
    private static readonly PropertySet ContextGroupProperties = new(["objectType", "group", "relevantTypes"]);

    private static readonly PropertySet AttachmentProperties =
        new(["usageType", "display", "description", "contentType", "length", "sha2", "fileUrl"]);

    private static readonly PropertySet PersonProperties = new(["objectType", "name", "mbox", "mbox_sha1sum", "openid", "account"]);
    private static readonly PropertySet AboutProperties = new(["version", "extensions"]);

    private readonly XapiVersion _version;
    private readonly bool _strict;

    // The value slots of the objects being read, one set per path depth. Reading is depth first, so the objects
    // still in use are ancestors of the one being read and sit at smaller depths.
    private ValueSlots?[] _slots = new ValueSlots?[8];
    private long _objects;

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

    /// <summary>The path <c>$</c>, from which the paths in <see cref="Errors"/> are built.</summary>
    public JsonPathRef Root { get; } = new JsonPathStack().Root;

    public Statement? ReadStatement(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, StatementProperties) is not { } p)
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

    public Actor? ReadActorRoot(JsonElement element, JsonPathRef path) => ReadActor(element, path, ActorRole.Any);

    public Activity? ReadActivity(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, ActivityProperties) is not { } p)
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

    public Person? ReadPerson(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, PersonProperties) is not { } p)
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

    public About? ReadAbout(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, AboutProperties) is not { } p)
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

    private Actor? ReadActor(JsonElement element, JsonPathRef path, ActorRole role)
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
                AddError(XapiErrorCodes.ObjectType, path.Property("objectType"), "A Group requires \"objectType\": \"Group\".");
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
                AddError(XapiErrorCodes.ObjectType, path.Property("objectType"), $"\"objectType\" must be {expected} here.");
                return null;
        }
    }

    private Agent? ReadAgent(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, AgentProperties) is not { } p)
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

    private Group? ReadGroup(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, GroupProperties) is not { } p)
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

    private InverseFunctionalIdentifier? ReadIfi(PropertyBag p, JsonPathRef path, bool required)
    {
        var count = 0;
        InverseFunctionalIdentifier? first = null;
        if (p.TryGet("mbox", out var e))
        {
            Found(ReadString(e, p.PathOf("mbox")) is { } s ? new Mbox(s) : null);
        }

        if (p.TryGet("mbox_sha1sum", out e))
        {
            Found(ReadString(e, p.PathOf("mbox_sha1sum")) is { } s ? new MboxSha1Sum(s) : null);
        }

        if (p.TryGet("openid", out e))
        {
            Found(ReadIri(e, p.PathOf("openid")) is { } iri ? new OpenId(iri) : null);
        }

        if (p.TryGet("account", out e))
        {
            Found(ReadAccount(e, p.PathOf("account")));
        }

        if (count > 1)
        {
            AddError(XapiErrorCodes.IfiMultiple, path, "Only one of \"mbox\", \"mbox_sha1sum\", \"openid\" and \"account\" may be used.");
            return null;
        }

        if (count == 0)
        {
            if (required)
            {
                AddError(XapiErrorCodes.IfiMissing, path, "One of \"mbox\", \"mbox_sha1sum\", \"openid\" or \"account\" is required.");
            }

            return null;
        }

        return first;

        void Found(InverseFunctionalIdentifier? ifi)
        {
            if (count++ == 0)
            {
                first = ifi;
            }
        }
    }

    private Account? ReadAccount(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, AccountProperties) is not { } p)
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

    private Verb? ReadVerb(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, VerbProperties) is not { } p)
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

    private StatementObject? ReadStatementObject(JsonElement element, JsonPathRef path, bool inSubStatement)
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
                    path.Property("objectType"),
                    "\"objectType\" must be \"Activity\", \"Agent\", \"Group\", \"StatementRef\" or \"SubStatement\".");
                return null;
        }
    }

    private StatementRef? ReadStatementRef(JsonElement element, JsonPathRef path, bool objectTypeRequired)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, StatementRefProperties) is not { } p)
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

    private SubStatement? ReadSubStatement(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, SubStatementProperties) is not { } p)
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

    private ActivityDefinition? ReadDefinition(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, DefinitionProperties) is not { } p)
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

    private InteractionType? ReadInteractionType(JsonElement element, JsonPathRef path)
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

    private InteractionComponent? ReadComponent(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, ComponentProperties) is not { } p)
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

    private Result? ReadResult(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, ResultProperties) is not { } p)
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

    private Score? ReadScore(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, ScoreProperties) is not { } p)
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

    private Context? ReadContext(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, ContextProperties) is not { } p)
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

    private ContextActivities? ReadContextActivities(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, ContextActivitiesProperties) is not { } p)
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

    private XapiList<Activity>? ReadActivityOrArray(JsonElement element, JsonPathRef path)
    {
        // 4.1.6.1/4.2.4.2: a single Activity object is accepted and returned as an array of one.
        if (element.ValueKind == JsonValueKind.Object)
        {
            return ReadActivity(element, path) is { } activity ? XapiList.Create(activity) : null;
        }

        return ReadArray(element, path, ReadActivity);
    }

    private ContextAgent? ReadContextAgent(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, ContextAgentProperties) is not { } p)
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

    private ContextGroup? ReadContextGroup(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, ContextGroupProperties) is not { } p)
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

    private XapiList<Iri>? ReadRelevantTypes(JsonElement element, JsonPathRef path)
    {
        var list = ReadStructArray(element, path, ReadIri);
        if (list is { IsEmpty: true })
        {
            AddError(XapiErrorCodes.RelevantTypesEmpty, path, "\"relevantTypes\" must contain at least one IRI when present.");
            return null;
        }

        return list;
    }

    private Attachment? ReadAttachment(JsonElement element, JsonPathRef path)
    {
        var start = Errors.Count;
        if (ReadObject(element, path, AttachmentProperties) is not { } p)
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

    private LanguageMap? ReadLanguageMap(JsonElement element, JsonPathRef path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            TypeError(path, "a language map object");
            return null;
        }

        var start = Errors.Count;
        var entries = new KeyValuePair<string, string>[element.GetPropertyCount()];
        var count = 0;
        var seen = new SeenNames(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            var itemPath = path.Property(name);
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                NullError(itemPath);
                continue;
            }

            var value = ReadString(property.Value, itemPath);
            if (!XapiFormats.IsLanguageTag(name))
            {
                if (_strict)
                {
                    AddError(XapiErrorCodes.LanguageTag, itemPath, $"\"{name}\" is not a well-formed RFC 5646 language tag.");
                }

                continue;
            }

            if (!seen.Add(name))
            {
                if (_strict)
                {
                    AddError(XapiErrorCodes.PropertyDuplicate, itemPath, $"The language tag \"{name}\" appears more than once (tags are case-insensitive).");
                }

                continue;
            }

            if (value is not null)
            {
                entries[count++] = new KeyValuePair<string, string>(name, value);
            }
        }

        return Errors.Count > start ? null : LanguageMap.FromChecked(entries, count);
    }

    private Extensions? ReadExtensions(JsonElement element, JsonPathRef path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            TypeError(path, "an extensions object");
            return null;
        }

        var start = Errors.Count;
        var entries = new List<KeyValuePair<Iri, JsonElement>>();
        var seen = new SeenNames(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            var itemPath = path.Property(name);
            if (!seen.Add(name))
            {
                if (_strict)
                {
                    AddError(XapiErrorCodes.PropertyDuplicate, itemPath, $"The extension \"{name}\" appears more than once.");
                }

                continue;
            }

            if (!Iri.TryParse(name, out var key))
            {
                // 4.2.7: an extension key that is not an IRI is rejected; values are never judged.
                AddError(XapiErrorCodes.Iri, itemPath, $"The extension key \"{name}\" is not an absolute IRI.");
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

    private void CheckDuplicateKeys(JsonElement element, JsonPathRef path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new SeenNames(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var name = property.Name;
                    var itemPath = path.Property(name);
                    if (!seen.Add(name))
                    {
                        AddError(XapiErrorCodes.PropertyDuplicate, itemPath, $"The property \"{name}\" appears more than once.");
                    }

                    CheckDuplicateKeys(property.Value, itemPath);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CheckDuplicateKeys(item, path.Index(index++));
                }

                break;
        }
    }

    private XapiList<T>? ReadArray<T>(JsonElement element, JsonPathRef path, Func<JsonElement, JsonPathRef, T?> readItem)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            TypeError(path, "an array");
            return null;
        }

        var start = Errors.Count;
        var length = element.GetArrayLength();
        var items = length == 0 ? [] : new T[length];
        var count = 0;
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var itemPath = path.Index(index++);
            if (item.ValueKind == JsonValueKind.Null)
            {
                NullError(itemPath);
                continue;
            }

            if (readItem(item, itemPath) is { } value)
            {
                items[count++] = value;
            }
        }

        return Errors.Count > start ? null : ToList(items, count);
    }

    private XapiList<T>? ReadStructArray<T>(JsonElement element, JsonPathRef path, Func<JsonElement, JsonPathRef, T?> readItem)
        where T : struct
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            TypeError(path, "an array");
            return null;
        }

        var start = Errors.Count;
        var length = element.GetArrayLength();
        var items = length == 0 ? [] : new T[length];
        var count = 0;
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var itemPath = path.Index(index++);
            if (item.ValueKind == JsonValueKind.Null)
            {
                NullError(itemPath);
                continue;
            }

            if (readItem(item, itemPath) is { } value)
            {
                items[count++] = value;
            }
        }

        return Errors.Count > start ? null : ToList(items, count);
    }

    private static XapiList<T> ToList<T>(T[] items, int count)
    {
        if (count == 0)
        {
            return XapiList<T>.Empty;
        }

        if (count < items.Length)
        {
            Array.Resize(ref items, count);
        }

        return new XapiList<T>(items);
    }

    private string? ReadString(JsonElement element, JsonPathRef path)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            TypeError(path, "a string");
            return null;
        }

        return element.GetString();
    }

    private bool? ReadBoolean(JsonElement element, JsonPathRef path)
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

    private double? ReadNumber(JsonElement element, JsonPathRef path)
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

    private long? ReadInteger(JsonElement element, JsonPathRef path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value))
        {
            TypeError(path, "an integer");
            return null;
        }

        return value;
    }

    private Iri? ReadIri(JsonElement element, JsonPathRef path)
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

    private Guid? ReadUuid(JsonElement element, JsonPathRef path)
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

    private DateTimeOffset? ReadTimestamp(JsonElement element, JsonPathRef path)
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

    private XapiDuration? ReadDuration(JsonElement element, JsonPathRef path)
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

    private void ExpectObjectType(JsonElement element, JsonPathRef path, string expected)
    {
        if (element.ValueKind == JsonValueKind.String && element.ValueEquals(expected))
        {
            return;
        }

        if (ReadString(element, path) is { } text && text != expected)
        {
            AddError(XapiErrorCodes.ObjectType, path, $"\"objectType\" must be \"{expected}\" here, not \"{text}\".");
        }
    }

    private static string? PeekObjectType(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("objectType"u8))
            {
                var value = property.Value;
                if (value.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                // The usual values, without allocating a string for them
                return value.ValueEquals("Agent"u8) ? "Agent"
                    : value.ValueEquals("Activity"u8) ? "Activity"
                    : value.ValueEquals("Group"u8) ? "Group"
                    : value.ValueEquals("StatementRef"u8) ? "StatementRef"
                    : value.ValueEquals("SubStatement"u8) ? "SubStatement"
                    : value.GetString();
            }
        }

        return null;
    }

    private PropertyBag? ReadObject(JsonElement element, JsonPathRef path, PropertySet properties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            TypeError(path, "an object");
            return null;
        }

        var depth = path.Depth;
        if (depth >= _slots.Length)
        {
            Array.Resize(ref _slots, depth * 2);
        }

        var slots = _slots[depth] ??= new ValueSlots();
        var values = slots.Take(properties.Count, ++_objects);
        var seen = 0;
        HashSet<string>? otherNames = null;
        foreach (var property in element.EnumerateObject())
        {
            var index = properties.IndexOf(property);
            if (index < 0)
            {
                // Lenient mode ignores other properties, repeated or not
                if (_strict)
                {
                    otherNames ??= new HashSet<string>(StringComparer.Ordinal);
                    OtherProperty(property.Name, path, properties, otherNames);
                }

                continue;
            }

            var name = properties[index];
            var bit = 1 << index;
            if ((seen & bit) != 0)
            {
                if (_strict)
                {
                    AddError(XapiErrorCodes.PropertyDuplicate, path.Property(name), $"The property \"{name}\" appears more than once.");
                }

                continue;
            }

            seen |= bit;
            if (index >= properties.KnownCount && _strict && !_version.IsV2)
            {
                AddError(XapiErrorCodes.PropertyVersion, path.Property(name), $"\"{name}\" is not defined in xAPI {_version.HeaderValue}.");
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                NullError(path.Property(name));
                continue;
            }

            values[index] = property.Value;
        }

        return new PropertyBag(path, properties, slots, _objects);
    }

    private void OtherProperty(string name, JsonPathRef path, PropertySet properties, HashSet<string> seen)
    {
        if (!seen.Add(name))
        {
            AddError(XapiErrorCodes.PropertyDuplicate, path.Property(name), $"The property \"{name}\" appears more than once.");
        }
        else if (properties.FindIgnoringCase(name) is { } caseMatch)
        {
            AddError(XapiErrorCodes.PropertyCase, path.Property(name), $"\"{name}\" must be spelled \"{caseMatch}\".");
        }
        else
        {
            AddError(XapiErrorCodes.PropertyUnknown, path.Property(name), $"\"{name}\" is not allowed here.");
        }
    }

    private T? Required<T>(PropertyBag bag, string name)
    {
        AddError(XapiErrorCodes.PropertyRequired, bag.PathOf(name), $"\"{name}\" is required.");
        return default;
    }

    private void NullError(JsonPathRef path)
    {
        if (_strict)
        {
            AddError(XapiErrorCodes.JsonNull, path, "null is not allowed outside extensions.");
        }
    }

    private void TypeError(JsonPathRef path, string expected) =>
        AddError(XapiErrorCodes.JsonType, path, $"Expected {expected}.");

    private void AddError(string code, JsonPathRef path, string message) => Errors.Add(new XapiError(code, path.ToString(), message));

    /// <summary>The properties an object may have, in the order of their slots in a <see cref="PropertyBag"/>.</summary>
    private sealed class PropertySet
    {
        private readonly string[] _names;
        private readonly byte[][] _utf8Names;

        public PropertySet(string[] known, string[]? laterVersion = null)
        {
            _names = [.. known, .. laterVersion ?? []];
            if (_names.Length > 31)
            {
                throw new ArgumentException("ReadObject tracks properties in an int.", nameof(known));
            }

            _utf8Names = Array.ConvertAll(_names, Encoding.UTF8.GetBytes);
            KnownCount = known.Length;
        }

        /// <summary>The properties before this index are defined in every version; the rest only in 2.0.</summary>
        public int KnownCount { get; }

        public int Count => _names.Length;

        public string this[int index] => _names[index];

        public int IndexOf(string name) => Array.IndexOf(_names, name);

        // Compares the name as it is in the JSON text, so known names never become strings. A name with an
        // escape sequence is unescaped first.
        public int IndexOf(JsonProperty property)
        {
            var raw = JsonMarshal.GetRawUtf8PropertyName(property);
            if (raw.IndexOf((byte)'\\') >= 0)
            {
                return IndexOf(property.Name);
            }

            for (var i = 0; i < _utf8Names.Length; i++)
            {
                if (raw.SequenceEqual(_utf8Names[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        public string? FindIgnoringCase(string name) =>
            Array.Find(_names, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    private readonly struct PropertyBag
    {
        private readonly JsonPathRef _path;
        private readonly PropertySet _properties;
        private readonly ValueSlots _slots;
        private readonly long _owner;

        public PropertyBag(JsonPathRef path, PropertySet properties, ValueSlots slots, long owner)
        {
            _path = path;
            _properties = properties;
            _slots = slots;
            _owner = owner;
        }

        public bool TryGet(string name, out JsonElement value)
        {
            value = _slots.Get(_owner, _properties.IndexOf(name));
            return value.ValueKind != JsonValueKind.Undefined;
        }

        public JsonPathRef PathOf(string name) => _path.Property(name);
    }

    private sealed class ValueSlots
    {
        // The largest PropertySet has 12 properties
        private JsonElement[] _values = new JsonElement[12];
        private long _owner;

        public JsonElement[] Take(int count, long owner)
        {
            if (_values.Length < count)
            {
                _values = new JsonElement[count];
            }
            else
            {
                Array.Clear(_values, 0, count);
            }

            _owner = owner;
            return _values;
        }

        public JsonElement Get(long owner, int index) =>
            owner == _owner ? _values[index] : throw new InvalidOperationException("An object's values were read after the reader reused them.");
    }

    /// <summary>
    /// The member names seen so far in one JSON object. Most objects have a few members, so the first four are
    /// compared directly and a hash set is only made for larger objects.
    /// </summary>
    private struct SeenNames(StringComparer comparer)
    {
        private string? _first;
        private string? _second;
        private string? _third;
        private string? _fourth;
        private HashSet<string>? _more;

        /// <summary>Adds a name.</summary>
        /// <returns><see langword="false"/> when the name was already seen.</returns>
        public bool Add(string name)
        {
            if (_more is not null)
            {
                return _more.Add(name);
            }

            if (!TryTake(ref _first, name, comparer, out var added) &&
                !TryTake(ref _second, name, comparer, out added) &&
                !TryTake(ref _third, name, comparer, out added) &&
                !TryTake(ref _fourth, name, comparer, out added))
            {
                _more = new HashSet<string>(comparer) { _first!, _second!, _third!, _fourth! };
                added = _more.Add(name);
            }

            return added;
        }

        // Decides the result when the slot is empty (take it) or holds the same name; otherwise moves on.
        private static bool TryTake(ref string? slot, string name, StringComparer comparer, out bool added)
        {
            if (slot is null)
            {
                slot = name;
                added = true;
                return true;
            }

            added = false;
            return comparer.Equals(slot, name);
        }
    }
}
