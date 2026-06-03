using System.Security.Cryptography;
using MonoTorrent.BEncoding;

namespace o_down.Engines.Torrent.Tests;

internal static class TorrentTestBuilder
{
    public static byte[] BuildSingleFile(string name, byte[] data, int pieceLength = 16 * 1024)
    {
        var pieceHashes = new List<byte>();
        int offset = 0;
        while (offset < data.Length)
        {
            int len = Math.Min(pieceLength, data.Length - offset);
            var hash = SHA1.HashData(data.AsSpan(offset, len));
            pieceHashes.AddRange(hash);
            offset += len;
        }

        var info = new BEncodedDictionary
        {
            { "name", new BEncodedString(name) },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieceHashes.ToArray()) },
            { "length", new BEncodedNumber(data.Length) },
        };
        var dict = new BEncodedDictionary
        {
            { "info", info },
        };
        return dict.Encode();
    }

    public static byte[] BuildMultiFile(string rootName, IReadOnlyList<(string Path, byte[] Data)> files, int pieceLength = 16 * 1024)
    {
        long totalSize = 0;
        foreach (var (_, data) in files) totalSize += data.Length;
        int pieceCount = (int)((totalSize + pieceLength - 1) / pieceLength);

        var fileList = new BEncodedList();
        foreach (var (path, data) in files)
        {
            fileList.Add(new BEncodedDictionary
            {
                { "length", new BEncodedNumber(data.Length) },
                { "path", BuildPathList(path) },
            });
        }

        var pieceHashes = new List<byte>(pieceCount * 20);
        int fileIdx = 0;
        int fileOffset = 0;
        long bytesHashed = 0;
        for (int p = 0; p < pieceCount; p++)
        {
            int pieceLen = (int)Math.Min(pieceLength, totalSize - bytesHashed);
            var piece = new byte[pieceLen];
            int copyOffset = 0;
            while (copyOffset < pieceLen && fileIdx < files.Count)
            {
                var (_, data) = files[fileIdx];
                int remainingInFile = data.Length - fileOffset;
                int toCopy = Math.Min(pieceLen - copyOffset, remainingInFile);
                Array.Copy(data, fileOffset, piece, copyOffset, toCopy);
                copyOffset += toCopy;
                fileOffset += toCopy;
                if (fileOffset >= data.Length)
                {
                    fileIdx++;
                    fileOffset = 0;
                }
            }
            pieceHashes.AddRange(SHA1.HashData(piece));
            bytesHashed += pieceLen;
        }

        var info = new BEncodedDictionary
        {
            { "name", new BEncodedString(rootName) },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieceHashes.ToArray()) },
            { "files", fileList },
        };
        var dict = new BEncodedDictionary
        {
            { "info", info },
        };
        return dict.Encode();
    }

    private static BEncodedList BuildPathList(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var list = new BEncodedList();
        foreach (var p in parts) list.Add(new BEncodedString(p));
        return list;
    }
}
