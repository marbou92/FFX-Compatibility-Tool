using System;
using System.IO;
using Xunit;
using FfxTool.Core;

namespace FfxTool.Core.Tests
{
    /// <summary>
    /// Port of tests/test_riff.py. Same correctness bar: parsing then
    /// re-serializing an unmodified .ffx file must reproduce it byte-for-
    /// byte.
    /// </summary>
    public class RiffTests
    {
        static string FixturesDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures");

        [Fact]
        public void RoundTrip_RealFixture_IsByteIdentical()
        {
            var path = Path.Combine(FixturesDir, "sample_1.ffx");
            var original = File.ReadAllBytes(path);
            var tree = RiffFile.ParseFile(original);
            var rebuilt = RiffFile.Serialize(tree);
            Assert.Equal(original, rebuilt);
        }

        [Fact]
        public void SyntheticMinimalRifx_RoundTrips()
        {
            byte[] innerChunk = Concat(RiffFile.Cid("head"), UInt32BE(4), new byte[] { 0, 0, 0, 1 });
            byte[] body = Concat(RiffFile.Cid("FaFX"), innerChunk);
            byte[] data = Concat(RiffFile.Cid("RIFX"), UInt32BE((uint)body.Length), body);

            var tree = RiffFile.ParseFile(data);
            Assert.Equal("RIFX", AsciiOf(tree.Cid));
            Assert.Equal("FaFX", AsciiOf(tree.Form));
            Assert.Single(tree.Children);
            Assert.Equal("head", AsciiOf(tree.Children[0].Cid));
            Assert.Equal(new byte[] { 0, 0, 0, 1 }, tree.Children[0].Content);

            var rebuilt = RiffFile.Serialize(tree);
            Assert.Equal(data, rebuilt);
        }

        [Fact]
        public void OddSizeChunk_GetsPaddedCorrectly()
        {
            byte[] leaf = Concat(RiffFile.Cid("abcd"), UInt32BE(3), new byte[] { (byte)'x', (byte)'y', (byte)'z' }, new byte[] { 0 });
            byte[] body = Concat(RiffFile.Cid("FaFX"), leaf);
            byte[] data = Concat(RiffFile.Cid("RIFX"), UInt32BE((uint)body.Length), body);

            var tree = RiffFile.ParseFile(data);
            Assert.Equal(new byte[] { (byte)'x', (byte)'y', (byte)'z' }, tree.Children[0].Content);

            var rebuilt = RiffFile.Serialize(tree);
            Assert.Equal(data, rebuilt);
        }

        [Fact]
        public void FindAll_IsRecursive()
        {
            byte[] inner = Concat(RiffFile.Cid("tdmn"), UInt32BE(4), new byte[] { (byte)'n', (byte)'a', (byte)'m', (byte)'e' });
            byte[] nestedList = Concat(RiffFile.Cid("LIST"), UInt32BE((uint)(4 + inner.Length)), RiffFile.Cid("tdsp"), inner);
            byte[] body = Concat(RiffFile.Cid("FaFX"), nestedList);
            byte[] data = Concat(RiffFile.Cid("RIFX"), UInt32BE((uint)body.Length), body);

            var tree = RiffFile.ParseFile(data);
            var found = RiffFile.FindAll(tree, RiffFile.Cid("tdmn"));
            Assert.Single(found);
            Assert.Equal("name", System.Text.Encoding.ASCII.GetString(found[0].Content));
        }

        // --- test helpers ---

        static byte[] UInt32BE(uint value) => new byte[]
        {
            (byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)
        };

        static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (var p in parts) total += p.Length;
            var result = new byte[total];
            int offset = 0;
            foreach (var p in parts)
            {
                Array.Copy(p, 0, result, offset, p.Length);
                offset += p.Length;
            }
            return result;
        }

        static string AsciiOf(byte[] b) => System.Text.Encoding.ASCII.GetString(b);
    }
}
