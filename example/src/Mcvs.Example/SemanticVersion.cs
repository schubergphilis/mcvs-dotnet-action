namespace Mcvs.Example;

/// <summary>
/// A semantic version, as used by the tags of this action.
/// </summary>
/// <remarks>
/// This type exists to give the self testing workflow of the
/// mcvs-dotnet-action something to lint, analyse, test and mutate. It is
/// deliberately small, but not trivial, as a method without a branch produces
/// no mutants.
/// </remarks>
public sealed class SemanticVersion
{
    private SemanticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>Gets the major version.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch version.</summary>
    public int Patch { get; }

    /// <summary>
    /// Parses a version such as '1.2.3' or 'v1.2.3', as a tag is allowed to
    /// carry the 'v' prefix that the .NET assembly version does not accept.
    /// </summary>
    /// <param name="value">The version to parse.</param>
    /// <returns>The parsed version.</returns>
    /// <exception cref="FormatException">The value is not a version.</exception>
    public static SemanticVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (trimmed.StartsWith('v'))
        {
            trimmed = trimmed[1..];
        }

        var parts = trimmed.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException($"The version is not a semantic version: '{value}'.");
        }

        var numbers = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out var number) || number < 0)
            {
                throw new FormatException($"The version is not a semantic version: '{value}'.");
            }

            numbers[index] = number;
        }

        return new SemanticVersion(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>
    /// Determines whether this version precedes the other one.
    /// </summary>
    /// <param name="other">The version to compare with.</param>
    /// <returns>True when this version is older.</returns>
    public bool IsOlderThan(SemanticVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return CompareTo(other) < 0;
    }

    /// <summary>
    /// Compares this version with the other one.
    /// </summary>
    /// <param name="other">The version to compare with.</param>
    /// <returns>A negative number when this version is older.</returns>
    public int CompareTo(SemanticVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
        {
            return minor;
        }

        return Patch.CompareTo(other.Patch);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Major}.{Minor}.{Patch}";
    }
}
