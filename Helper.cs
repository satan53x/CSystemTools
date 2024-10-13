using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CSystemArc
{
    public class Helper
    {
        private static readonly Encoding UTF16Encoding = Encoding.GetEncoding("UTF-16", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private static byte[] utf16Bytes = new byte[2];

        public static string ReadUTF16(byte[] bs, int offset)
        {
            Array.Copy(bs, offset, utf16Bytes, 0, 2);
            string c = UTF16Encoding.GetString(utf16Bytes);
            return c;
        }

        public static void WriteUTF16(byte[] bs, int offset, char[] str)
        {
            var utf16Bytes = UTF16Encoding.GetBytes(str);
            Array.Copy(utf16Bytes, 0, bs, offset, 2);
        }

        public static void WriteUTF16(Stream stream, string str)
        {
            var utf16Bytes = UTF16Encoding.GetBytes(str);
            stream.Write(utf16Bytes, 0, utf16Bytes.Length);
        }

        public static void DumpBytes(byte[] bs)
        {
#if DEBUG
            string filePath = ".\\output";
            File.WriteAllBytes(filePath, bs);
#endif
        }

        
    }

    internal class Cache
    {
        public static XDocument cacheXml = new XDocument(
            new XElement("Cache", new XElement("EntryList"))
        );

        public static void WriteEntry(ArchiveEntry entry)
        {
            var listNode = cacheXml.Root.Element("EntryList");
            var node = new XElement("Entry",
                new XAttribute("Id", entry.Id),
                new XAttribute("Version", entry.Version),
                new XAttribute("IsCompressed", entry.IsComporessed)
            );
            if (entry.PreData != null)
            {
                string str = CSystemConfig.BytesToHex(entry.PreData);
                node.Add(new XElement("PreData", str));
            }
            listNode.Add(node);
        }

        public static void ReadEntry(ArchiveEntry entry)
        {
            var listNode = cacheXml.Root.Element("EntryList");
            var node = listNode.Descendants("Entry")
            .Where(e => e.Attribute("Id")?.Value == entry.Id.ToString())
            .FirstOrDefault();
            int isCompressed = -1;
            if (node != null)
            {
                int.TryParse(node.Attribute("IsCompressed")?.Value, out isCompressed);
                if (entry.Version <= 0)
                {
                    int v = entry.Version;
                    int.TryParse(node.Attribute("Version")?.Value, out v);
                    entry.Version = v;
                }
                var pre = node.Element("PreData");
                if (node != null && node.Value != null && node.Value.Length > 0)
                {
                    entry.PreData = CSystemConfig.HexToBytes(node.Value);
                }
            }
            entry.IsComporessed = isCompressed;
        }

        public static void Save(string dir)
        {
            string filePath = Path.Combine(dir, "cache.xml");
            cacheXml.Save(filePath);
            Console.WriteLine($"Save cache xml in {filePath}");
        }

        public static void Load(string dir)
        {
            string filePath = Path.Combine(dir, "cache.xml");
            if (File.Exists(filePath))
            {
                cacheXml = XDocument.Load(filePath);
                Console.WriteLine($"Load cache xml in {filePath}");
            }
        }

    }
}


