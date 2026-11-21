using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

internal static class SlbMerkle
{
    public static byte[] Compute(IReadOnlyList<byte[]> contents)
    {
        if (contents.Count == 0)
        {
            return new byte[32];
        }

        var level = contents
            .Select(content => HashAlgorithms.Compute(HashAlgorithmKind.Blake3, content))
            .ToList();

        while (level.Count > 1)
        {
            var next = new List<byte[]>((level.Count + 1) / 2);

            for (var index = 0; index < level.Count; index += 2)
            {
                if (index + 1 == level.Count)
                {
                    next.Add(level[index]);
                    continue;
                }

                var left = level[index];
                var right = level[index + 1];
                var combined = new byte[left.Length + right.Length];
                left.CopyTo(combined, 0);
                right.CopyTo(combined, left.Length);
                next.Add(HashAlgorithms.Compute(HashAlgorithmKind.Blake3, combined));
            }

            level = next;
        }

        return level[0];
    }
}
