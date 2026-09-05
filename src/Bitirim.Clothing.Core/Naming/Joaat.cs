namespace Bitirim.Clothing.Core.Naming;

/// <summary>
/// Jenkins one-at-a-time hash, the "joaat" variant RAGE uses for name hashes.
/// </summary>
/// <remarks>
/// Names are lower-cased before hashing, which is how the engine treats them.
/// This is the standard published algorithm, implemented from its description
/// rather than copied from any GTA tool -- see docs/legal-and-oss.md section 2.
/// </remarks>
public static class Joaat
{
    public static uint Hash(string value)
    {
        uint hash = 0;
        foreach (var ch in value)
        {
            hash += char.ToLowerInvariant(ch);
            hash += hash << 10;
            hash ^= hash >> 6;
        }

        hash += hash << 3;
        hash ^= hash >> 11;
        hash += hash << 15;
        return hash;
    }
}
