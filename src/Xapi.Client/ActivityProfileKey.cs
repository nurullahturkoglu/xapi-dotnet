using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>A document in the Activity Profile resource (<c>activities/profile</c>), IEEE 9274.1.1, 4.1.6.6.</summary>
public sealed record ActivityProfileKey : DocumentKey
{
    private readonly Iri _activityId;
    private readonly string _profileId;

    /// <summary>Creates a key.</summary>
    /// <param name="activityId">The Activity the profile belongs to (<c>activityId</c>).</param>
    /// <param name="profileId">The document id (<c>profileId</c>).</param>
    public ActivityProfileKey(Iri activityId, string profileId)
    {
        _activityId = Guard.NotDefault(activityId);
        _profileId = Guard.NotEmpty(profileId);
    }

    /// <summary>The Activity the profile belongs to (<c>activityId</c>).</summary>
    public Iri ActivityId
    {
        get => _activityId;
        init => _activityId = Guard.NotDefault(value);
    }

    /// <summary>The document id (<c>profileId</c>).</summary>
    public string ProfileId
    {
        get => _profileId;
        init => _profileId = Guard.NotEmpty(value);
    }

    internal override string Resource => XapiClient.ActivityProfilesResource;

    internal override void AddParameters(QueryBuilder query, XapiVersion version) =>
        query.Add("activityId", ActivityId).Add("profileId", ProfileId);
}
