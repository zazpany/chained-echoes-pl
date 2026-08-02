using System.Security.Cryptography;

namespace ChainedEchoesPolishInstaller.Core;

public static class Hashing
{
    public static string Sha256File(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        return Sha256(stream).Sha256;
    }

    public static (string Sha256, long Length) Sha256(Stream stream)
    {
        using var algorithm = SHA256.Create();
        var buffer = new byte[1024 * 1024];
        long length = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            algorithm.TransformBlock(buffer, 0, read, null, 0);
            length += read;
        }

        algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return (Convert.ToHexString(algorithm.Hash!).ToLowerInvariant(), length);
    }

    public static void AtomicWrite(Stream source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".ce-polish.tmp";
        try
        {
            using (var output = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.SequentialScan))
            {
                source.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static void AtomicCopy(string source, string target)
    {
        using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        AtomicWrite(input, target);
    }
}
