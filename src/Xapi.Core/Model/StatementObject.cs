namespace Xapi;

/// <summary>
/// The object of a Statement: an <see cref="Activity"/>, an <see cref="Agent"/> or <see cref="Group"/>, a
/// <see cref="StatementRef"/> or a <see cref="SubStatement"/>.
/// </summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.3. In JSON an object without <c>objectType</c> is an Activity.</remarks>
public abstract record StatementObject
{
    private protected StatementObject()
    {
    }
}
