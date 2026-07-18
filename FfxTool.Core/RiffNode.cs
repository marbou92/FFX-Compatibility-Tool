using System;
using System.Collections.Generic;
using System.IO;

namespace FfxTool.Core
{
    /// <summary>
    /// Generic RIFX (big-endian RIFF) container node — a 1:1 port of
    /// ffx_core/riff.py's dict-based tree, kept as a class instead so C#'s
    /// type system catches structural mistakes the Python dicts couldn't.
    ///
    /// A node is EITHER a container (Form != null, Children populated) OR a
    /// leaf (Form == null, Content populated) — never both, matching the
    /// Python version's invariant exactly.
    /// </summary>
    public class RiffNode
    {
        public byte[] Cid;              // 4-byte chunk id, e.g. "tdmn", "LIST", "RIFX"
        public byte[] Form;              // 4-byte form tag if this is a container, else null
        public List<RiffNode> Children;  // populated only if Form != null
        public byte[] Content;           // populated only if Form == null
        public byte[] Trailer;           // top-level RIFX node only — see RiffFile.Parse

        public bool IsContainer => Form != null;
    }

    public static class RiffFile
    {
        static readonly byte[] RIFX = { (byte)'R', (byte)'I', (byte)'F', (byte)'X' };
        static readonly byte[] LIST = { (byte)'L', (byte)'I', (byte)'S', (byte)'T' };

        static bool CidEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static bool IsContainerId(byte[] cid) => CidEquals(cid, RIFX) || CidEquals(cid, LIST);

        static uint ReadUInt32BE(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                          (data[offset + 2] << 8) | data[offset + 3]);
        }

        static void WriteUInt32BE(BinaryWriter w, uint value)
        {
            w.Write((byte)((value >> 24) & 0xFF));
            w.Write((byte)((value >> 16) & 0xFF));
            w.Write((byte)((value >> 8) & 0xFF));
            w.Write((byte)(value & 0xFF));
        }

        /// <summary>
        /// Parse a byte range into a list of sibling chunk nodes.
        /// Direct port of riff.py's parse().
        /// </summary>
        public static List<RiffNode> Parse(byte[] data, int offset, int end)
        {
            var results = new List<RiffNode>();
            int pos = offset;

            while (pos < end - 8)
            {
                var cid = new byte[4];
                Array.Copy(data, pos, cid, 0, 4);
                uint size = ReadUInt32BE(data, pos + 4);
                int contentStart = pos + 8;
                int rawEnd = (int)(contentStart + size);

                if (rawEnd > end)
                    throw new InvalidDataException(
                        $"Chunk {System.Text.Encoding.ASCII.GetString(cid)} at offset {pos} " +
                        $"claims size {size}, which overruns the parent's bounds (end={end}).");

                RiffNode node;
                if (IsContainerId(cid))
                {
                    var form = new byte[4];
                    Array.Copy(data, contentStart, form, 0, 4);
                    node = new RiffNode
                    {
                        Cid = cid,
                        Form = form,
                        Children = Parse(data, contentStart + 4, rawEnd),
                    };
                }
                else
                {
                    var content = new byte[rawEnd - contentStart];
                    Array.Copy(data, contentStart, content, 0, content.Length);
                    node = new RiffNode { Cid = cid, Form = null, Content = content };
                }

                results.Add(node);
                pos = rawEnd;
                if (size % 2 == 1) pos += 1; // RIFF chunks pad to an even boundary
            }

            return results;
        }

        /// <summary>
        /// Parse a whole .ffx file into its top-level RIFX node.
        ///
        /// Real-world .ffx files sometimes have extra bytes after the RIFX
        /// chunk ends (observed: a trailing XMP-style packet). Those bytes
        /// are preserved verbatim on the returned node's Trailer field so
        /// that Parse() + Serialize() remains lossless — this bit AT COST
        /// OF A REAL BUG the Python version hit first; ported the fix, not
        /// just the happy path.
        /// </summary>
        public static RiffNode ParseFile(byte[] data)
        {
            if (data.Length < 8 || !CidEquals(new byte[] { data[0], data[1], data[2], data[3] }, RIFX))
                throw new InvalidDataException("Not a valid RIFX file (must start with 'RIFX').");

            uint size = ReadUInt32BE(data, 4);
            int riffEnd = (int)(8 + size);
            if (riffEnd % 2 == 1) riffEnd += 1;
            if (riffEnd > data.Length)
                throw new InvalidDataException("RIFX chunk size overruns the file length — truncated or corrupt file.");

            var top = Parse(data, 0, riffEnd);
            if (top.Count != 1 || !CidEquals(top[0].Cid, RIFX))
                throw new InvalidDataException("Not a valid RIFX file (expected a single top-level RIFX chunk).");

            var node = top[0];
            var trailer = new byte[data.Length - riffEnd];
            Array.Copy(data, riffEnd, trailer, 0, trailer.Length);
            node.Trailer = trailer;
            return node;
        }

        /// <summary>Serialize a node (and its children) back into raw bytes.</summary>
        public static byte[] Serialize(RiffNode node)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                WriteNode(w, node);
                if (node.Trailer != null && node.Trailer.Length > 0)
                    w.Write(node.Trailer);
                return ms.ToArray();
            }
        }

        static void WriteNode(BinaryWriter w, RiffNode node)
        {
            byte[] body;
            if (node.IsContainer)
            {
                using (var bodyMs = new MemoryStream())
                using (var bodyW = new BinaryWriter(bodyMs))
                {
                    bodyW.Write(node.Form);
                    foreach (var child in node.Children)
                        WriteNode(bodyW, child);
                    body = bodyMs.ToArray();
                }
            }
            else
            {
                body = node.Content;
            }

            w.Write(node.Cid);
            WriteUInt32BE(w, (uint)body.Length);
            w.Write(body);
            if (body.Length % 2 == 1)
                w.Write((byte)0x00);
        }

        /// <summary>Recursively find every descendant leaf/container chunk with a given cid.</summary>
        public static List<RiffNode> FindAll(RiffNode node, byte[] cid)
        {
            var results = new List<RiffNode>();
            FindAllInto(node, cid, results);
            return results;
        }

        static void FindAllInto(RiffNode node, byte[] cid, List<RiffNode> results)
        {
            if (!node.IsContainer) return;
            foreach (var c in node.Children)
            {
                if (CidEquals(c.Cid, cid)) results.Add(c);
                FindAllInto(c, cid, results);
            }
        }

        /// <summary>Recursively find every descendant LIST chunk with a given form tag.</summary>
        public static List<RiffNode> FindAllLists(RiffNode node, byte[] form)
        {
            var results = new List<RiffNode>();
            FindAllListsInto(node, form, results);
            return results;
        }

        static void FindAllListsInto(RiffNode node, byte[] form, List<RiffNode> results)
        {
            if (!node.IsContainer) return;
            foreach (var c in node.Children)
            {
                if (CidEquals(c.Cid, LIST) && c.Form != null && CidEquals(c.Form, form))
                    results.Add(c);
                FindAllListsInto(c, form, results);
            }
        }

        public static byte[] Cid(string s) => System.Text.Encoding.ASCII.GetBytes(s);
    }
}
