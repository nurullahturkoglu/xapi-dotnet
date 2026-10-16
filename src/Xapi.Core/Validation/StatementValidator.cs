using Xapi.Internal;

namespace Xapi.Validation;

/// <summary>
/// Checks models against the rules an LRS must enforce (IEEE 9274.1.1, 4.2) for a given xAPI version, and
/// returns every problem found.
/// </summary>
/// <remarks>
/// <para>
/// Rules that the model types already enforce (one identifier per Agent, required properties, IRI, UUID,
/// timestamp and duration syntax, language map keys) are not repeated here; <see cref="Json.XapiJson"/>
/// reports them when reading JSON. This class covers the rest: string formats (<c>mbox</c>,
/// <c>mbox_sha1sum</c>, <c>sha2</c>, <c>contentType</c>, <c>context.language</c>, <c>version</c>) and rules
/// that involve more than one property.
/// </para>
/// <para>Error paths are JSON paths of the equivalent JSON document.</para>
/// </remarks>
public static class StatementValidator
{
    /// <summary>Validates one Statement.</summary>
    /// <param name="statement">The Statement.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <returns>Every error found; empty when the Statement is valid.</returns>
    public static IReadOnlyList<XapiError> Validate(Statement statement, XapiVersion version)
    {
        Guard.NotNull(statement);
        var walker = new Walker(version);
        walker.Statement(statement, JsonPath.Root);
        return walker.Errors;
    }

    /// <summary>Validates a batch of Statements, including that no two Statements share an id (4.1.6.1).</summary>
    /// <param name="statements">The Statements.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <returns>Every error found; paths start with the index, for example <c>$[2].actor</c>.</returns>
    public static IReadOnlyList<XapiError> ValidateBatch(IReadOnlyList<Statement> statements, XapiVersion version)
    {
        Guard.NotNull(statements);
        var walker = new Walker(version);
        var firstIndexById = new Dictionary<Guid, int>();
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = Guard.NotNull(statements[i]);
            var path = JsonPath.Index(JsonPath.Root, i);
            walker.Statement(statement, path);
            if (statement.Id is not { } id)
            {
                continue;
            }

            if (firstIndexById.TryGetValue(id, out var first))
            {
                walker.Add(
                    XapiErrorCodes.BatchDuplicateId,
                    JsonPath.Property(path, "id"),
                    $"The id {id} is already used by the Statement at index {first}.");
            }
            else
            {
                firstIndexById.Add(id, i);
            }
        }

        return walker.Errors;
    }

    /// <summary>Validates an Agent or Group on its own, for example an <c>agent</c> query parameter.</summary>
    /// <param name="actor">The Actor.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <returns>Every error found.</returns>
    public static IReadOnlyList<XapiError> ValidateActor(Actor actor, XapiVersion version)
    {
        Guard.NotNull(actor);
        var walker = new Walker(version);
        walker.Actor(actor, JsonPath.Root);
        return walker.Errors;
    }

    /// <summary>Validates an Activity on its own, including its definition's interaction rules.</summary>
    /// <param name="activity">The Activity.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <returns>Every error found.</returns>
    public static IReadOnlyList<XapiError> ValidateActivity(Activity activity, XapiVersion version)
    {
        Guard.NotNull(activity);
        var walker = new Walker(version);
        walker.Activity(activity, JsonPath.Root);
        return walker.Errors;
    }

    /// <summary>Validates a Person object (identifier formats).</summary>
    /// <param name="person">The Person.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <returns>Every error found.</returns>
    public static IReadOnlyList<XapiError> ValidatePerson(Person person, XapiVersion version)
    {
        Guard.NotNull(person);
        var walker = new Walker(version);
        for (var i = 0; i < person.Mboxes.Count; i++)
        {
            walker.Ifi(person.Mboxes[i], JsonPath.Index(JsonPath.Property(JsonPath.Root, "mbox"), i), atValue: true);
        }

        for (var i = 0; i < person.MboxSha1Sums.Count; i++)
        {
            walker.Ifi(person.MboxSha1Sums[i], JsonPath.Index(JsonPath.Property(JsonPath.Root, "mbox_sha1sum"), i), atValue: true);
        }

        return walker.Errors;
    }

    private sealed class Walker(XapiVersion version)
    {
        public List<XapiError> Errors { get; } = [];

        public void Add(string code, string path, string message) => Errors.Add(new XapiError(code, path, message));

        public void Statement(Statement statement, string path)
        {
            if (statement.Version is { } statementVersion && !XapiFormats.IsStatementVersion(statementVersion, version))
            {
                var allowed = version.IsV2 ? "1.0.x or 2.0.0" : "1.0.x";
                Add(XapiErrorCodes.Version, JsonPath.Property(path, "version"), $"\"{statementVersion}\" is not a valid version for xAPI {version.HeaderValue}; use {allowed}.");
            }

            Actor(statement.Actor, JsonPath.Property(path, "actor"));
            StatementObject(statement.Object, JsonPath.Property(path, "object"));

            // 4.2.5: a voiding Statement must target a Statement.
            if (statement.Verb.Id == Verbs.VoidedId && statement.Object is not StatementRef)
            {
                Add(XapiErrorCodes.VoidingObject, JsonPath.Property(path, "object"), "A Statement with the voided Verb must have a StatementRef as its object.");
            }

            Result(statement.Result, JsonPath.Property(path, "result"));
            Context(statement.Context, JsonPath.Property(path, "context"), statement.Object is Activity);
            if (statement.Authority is { } authority)
            {
                Authority(authority, JsonPath.Property(path, "authority"));
            }

            Attachments(statement.Attachments, JsonPath.Property(path, "attachments"));
        }

        public void Actor(Actor actor, string path)
        {
            switch (actor)
            {
                case Agent agent:
                    Ifi(agent.Ifi, path, atValue: false);
                    break;
                case Group group:
                    if (group.Ifi is { } ifi)
                    {
                        Ifi(ifi, path, atValue: false);
                    }
                    else if (group.Members.IsEmpty)
                    {
                        Add(XapiErrorCodes.GroupMemberMissing, JsonPath.Property(path, "member"), "An anonymous Group must have at least one member.");
                    }

                    var membersPath = JsonPath.Property(path, "member");
                    for (var i = 0; i < group.Members.Count; i++)
                    {
                        Actor(group.Members[i], JsonPath.Index(membersPath, i));
                    }

                    break;
            }
        }

        public void Ifi(InverseFunctionalIdentifier ifi, string path, bool atValue)
        {
            switch (ifi)
            {
                case Mbox mbox when !XapiFormats.IsMbox(mbox.Value):
                    Add(XapiErrorCodes.Mbox, atValue ? path : JsonPath.Property(path, "mbox"), $"\"{mbox.Value}\" is not a mailto: IRI with an e-mail address.");
                    break;
                case MboxSha1Sum sha1 when !XapiFormats.IsSha1Hex(sha1.Value):
                    Add(XapiErrorCodes.Sha1, atValue ? path : JsonPath.Property(path, "mbox_sha1sum"), $"\"{sha1.Value}\" is not a 40-character hexadecimal SHA-1 digest.");
                    break;
            }
        }

        public void Activity(Activity activity, string path)
        {
            if (activity.Definition is { } definition)
            {
                Definition(definition, JsonPath.Property(path, "definition"));
            }
        }

        private void StatementObject(StatementObject value, string path)
        {
            switch (value)
            {
                case Activity activity:
                    Activity(activity, path);
                    break;
                case Actor actor:
                    Actor(actor, path);
                    break;
                case SubStatement sub:
                    SubStatement(sub, path);
                    break;
            }
        }

        private void SubStatement(SubStatement sub, string path)
        {
            Actor(sub.Actor, JsonPath.Property(path, "actor"));
            var objectPath = JsonPath.Property(path, "object");
            if (sub.Object is SubStatement)
            {
                Add(XapiErrorCodes.SubStatementNested, objectPath, "A SubStatement cannot have a SubStatement as its object.");
            }
            else
            {
                StatementObject(sub.Object, objectPath);
            }

            Result(sub.Result, JsonPath.Property(path, "result"));
            Context(sub.Context, JsonPath.Property(path, "context"), sub.Object is Activity);
            Attachments(sub.Attachments, JsonPath.Property(path, "attachments"));
        }

        private void Authority(Actor authority, string path)
        {
            Actor(authority, path);

            // xAPI 1.0.3 Data 2.4.9: a Group authority is the OAuth 3-legged pair (consumer, user). The CTS tests this for 2.0 as well.
            if (authority is Group group && (!group.IsAnonymous || group.Members.Count != 2))
            {
                Add(XapiErrorCodes.AuthorityGroup, path, "A Group authority must be an anonymous Group with exactly two Agents.");
            }
        }

        private void Definition(ActivityDefinition definition, string path)
        {
            OptionalIri(definition.Type, JsonPath.Property(path, "type"));
            OptionalIri(definition.MoreInfo, JsonPath.Property(path, "moreInfo"));

            var lists = new (string Name, XapiList<InteractionComponent>? Items)[]
            {
                ("choices", definition.Choices),
                ("scale", definition.Scale),
                ("source", definition.Source),
                ("target", definition.Target),
                ("steps", definition.Steps),
            };

            if (definition.InteractionType is not { } type)
            {
                if (definition.CorrectResponsesPattern is not null || lists.Any(l => l.Items is not null))
                {
                    Add(XapiErrorCodes.InteractionTypeMissing, JsonPath.Property(path, "interactionType"), "Interaction properties require an \"interactionType\".");
                }
            }
            else if ((uint)type > (uint)InteractionType.Other)
            {
                Add(XapiErrorCodes.InteractionType, JsonPath.Property(path, "interactionType"), $"{(int)type} is not an InteractionType value.");
            }

            foreach (var (name, items) in lists)
            {
                if (items is null)
                {
                    continue;
                }

                var listPath = JsonPath.Property(path, name);
                if (definition.InteractionType is { } interactionType && !Allows(interactionType, name))
                {
                    Add(XapiErrorCodes.InteractionComponentsNotAllowed, listPath, $"\"{name}\" is not used with the interaction type {interactionType}.");
                }

                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < items.Count; i++)
                {
                    if (!ids.Add(items[i].Id))
                    {
                        Add(XapiErrorCodes.InteractionComponentDuplicate, JsonPath.Property(JsonPath.Index(listPath, i), "id"), $"The component id \"{items[i].Id}\" is used more than once in \"{name}\".");
                    }
                }
            }
        }

        private static bool Allows(InteractionType type, string list) =>
            (type, list) switch
            {
                (InteractionType.Choice or InteractionType.Sequencing, "choices") => true,
                (InteractionType.Likert, "scale") => true,
                (InteractionType.Matching, "source" or "target") => true,
                (InteractionType.Performance, "steps") => true,
                _ => false,
            };

        private void Result(Result? result, string path)
        {
            if (result?.Score is not { } score)
            {
                return;
            }

            var scorePath = JsonPath.Property(path, "score");
            var finite = true;
            foreach (var (name, value) in new[] { ("scaled", score.Scaled), ("raw", score.Raw), ("min", score.Min), ("max", score.Max) })
            {
                if (value is { } number && (double.IsNaN(number) || double.IsInfinity(number)))
                {
                    finite = false;
                    Add(XapiErrorCodes.ScoreNotFinite, JsonPath.Property(scorePath, name), "A score must be a finite number.");
                }
            }

            if (!finite)
            {
                return;
            }

            if (score.Scaled is { } scaled && (scaled < -1 || scaled > 1))
            {
                Add(XapiErrorCodes.ScoreScaled, JsonPath.Property(scorePath, "scaled"), $"\"scaled\" must be between -1 and 1, not {scaled}.");
            }

            if (score.Min is { } min && score.Max is { } max && min >= max)
            {
                Add(XapiErrorCodes.ScoreMinMax, JsonPath.Property(scorePath, "min"), $"\"min\" ({min}) must be less than \"max\" ({max}).");
            }

            if (score.Raw is { } raw && ((score.Min is { } lower && raw < lower) || (score.Max is { } upper && raw > upper)))
            {
                Add(XapiErrorCodes.ScoreRaw, JsonPath.Property(scorePath, "raw"), $"\"raw\" ({raw}) must be between \"min\" and \"max\".");
            }
        }

        private void Context(Context? context, string path, bool objectIsActivity)
        {
            if (context is null)
            {
                return;
            }

            // xAPI 1.0.3 Data 2.4.6; not in 9274.1.1 but enforced by the 2.0 CTS (issue #278), so applied to both versions.
            if (context.Revision is not null && !objectIsActivity)
            {
                Add(XapiErrorCodes.ContextRevision, JsonPath.Property(path, "revision"), "\"revision\" is only allowed when the object is an Activity.");
            }

            if (context.Platform is not null && !objectIsActivity)
            {
                Add(XapiErrorCodes.ContextPlatform, JsonPath.Property(path, "platform"), "\"platform\" is only allowed when the object is an Activity.");
            }

            if (context.Language is { } language && !XapiFormats.IsLanguageTag(language))
            {
                Add(XapiErrorCodes.LanguageTag, JsonPath.Property(path, "language"), $"\"{language}\" is not a well-formed RFC 5646 language tag.");
            }

            if (context.Instructor is { } instructor)
            {
                Actor(instructor, JsonPath.Property(path, "instructor"));
            }

            if (context.Team is { } team)
            {
                Actor(team, JsonPath.Property(path, "team"));
            }

            ContextAgents(context, path);
            ContextGroups(context, path);

            if (context.ContextActivities is { } activities)
            {
                var activitiesPath = JsonPath.Property(path, "contextActivities");
                ActivityList(activities.Parent, JsonPath.Property(activitiesPath, "parent"));
                ActivityList(activities.Grouping, JsonPath.Property(activitiesPath, "grouping"));
                ActivityList(activities.Category, JsonPath.Property(activitiesPath, "category"));
                ActivityList(activities.Other, JsonPath.Property(activitiesPath, "other"));
            }
        }

        private void ContextAgents(Context context, string path)
        {
            var listPath = JsonPath.Property(path, "contextAgents");
            if (!version.IsV2 && !context.ContextAgents.IsEmpty)
            {
                Add(XapiErrorCodes.ContextVersion, listPath, "\"contextAgents\" is not defined in xAPI 1.0.3.");
            }

            for (var i = 0; i < context.ContextAgents.Count; i++)
            {
                var itemPath = JsonPath.Index(listPath, i);
                Actor(context.ContextAgents[i].Agent, JsonPath.Property(itemPath, "agent"));
                IriList(context.ContextAgents[i].RelevantTypes, JsonPath.Property(itemPath, "relevantTypes"));
            }
        }

        private void ContextGroups(Context context, string path)
        {
            var listPath = JsonPath.Property(path, "contextGroups");
            if (!version.IsV2 && !context.ContextGroups.IsEmpty)
            {
                Add(XapiErrorCodes.ContextVersion, listPath, "\"contextGroups\" is not defined in xAPI 1.0.3.");
            }

            for (var i = 0; i < context.ContextGroups.Count; i++)
            {
                var itemPath = JsonPath.Index(listPath, i);
                Actor(context.ContextGroups[i].Group, JsonPath.Property(itemPath, "group"));
                IriList(context.ContextGroups[i].RelevantTypes, JsonPath.Property(itemPath, "relevantTypes"));
            }
        }

        private void ActivityList(XapiList<Activity> activities, string path)
        {
            for (var i = 0; i < activities.Count; i++)
            {
                Activity(activities[i], JsonPath.Index(path, i));
            }
        }

        private void Attachments(XapiList<Attachment> attachments, string path)
        {
            for (var i = 0; i < attachments.Count; i++)
            {
                var attachment = attachments[i];
                var itemPath = JsonPath.Index(path, i);
                if (!XapiFormats.IsMediaType(attachment.ContentType))
                {
                    Add(XapiErrorCodes.MediaType, JsonPath.Property(itemPath, "contentType"), $"\"{attachment.ContentType}\" is not an Internet media type.");
                }

                if (attachment.Length < 0)
                {
                    Add(XapiErrorCodes.AttachmentLength, JsonPath.Property(itemPath, "length"), "\"length\" must not be negative.");
                }

                if (!XapiFormats.IsSha2Hex(attachment.Sha2))
                {
                    Add(XapiErrorCodes.Sha2, JsonPath.Property(itemPath, "sha2"), $"\"{attachment.Sha2}\" is not a hexadecimal SHA-2 digest.");
                }

                OptionalIri(attachment.FileUrl, JsonPath.Property(itemPath, "fileUrl"));
            }
        }

        private void IriList(XapiList<Iri> values, string path)
        {
            for (var i = 0; i < values.Count; i++)
            {
                OptionalIri(values[i], JsonPath.Index(path, i));
            }
        }

        private void OptionalIri(Iri? value, string path)
        {
            if (value is { IsEmpty: true })
            {
                Add(XapiErrorCodes.Iri, path, "The IRI is the default (empty) value.");
            }
        }
    }
}
