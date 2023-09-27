using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace CSystemArc
{
    internal class CSystemConfig
    {
        private static readonly Encoding SjisEncoding = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private static readonly Encoding UTF16Encoding = Encoding.GetEncoding("UTF-16", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        private List<byte[]> _items;
        private byte[] _data1;
        private byte[] _data2;
        private byte[] _data3;

        public void Read(Stream stream)
        {
            byte[] stringData = BcdCompression.Decompress(stream);
            _items = UnpackItems(stringData);

            int data1Size = Bcd.Read(stream);
            _data1 = new byte[data1Size];
            stream.Read(_data1, 0, _data1.Length);

            int data2Size = Bcd.Read(stream);
            _data2 = new byte[data2Size];
            stream.Read(_data2, 0, _data2.Length);

            int data3Size = Bcd.Read(stream);
            _data3 = new byte[data3Size];
            stream.Read(_data3, 0, _data3.Length);

            if (stream.Position != stream.Length)
                throw new InvalidDataException();
        }

        public void Write(Stream stream)
        {
            ArraySegment<byte> stringData = PackItems(_items);
            BcdCompression.Compress(stringData, stream);

            Bcd.Write(stream, _data1.Length);
            stream.Write(_data1, 0, _data1.Length);

            Bcd.Write(stream, _data2.Length);
            stream.Write(_data2, 0, _data2.Length);

            Bcd.Write(stream, _data3.Length);
            stream.Write(_data3, 0, _data3.Length);
        }

        public XDocument ToXml()
        {
            if (_items.Count > 8)
                throw new InvalidDataException("Unexpected number of items");

            XElement configElem =
                new XElement(
                    "config",
                    TextItemToXml(0),
                    DictionaryItemToXml(1),
                    TextItemToXml(2),
                    TextItemToXml(3),
                    TextItemToXml(4),
                    TextItemToXml(5),
                    BinaryItemToXml(6),
                    BinaryItemToXml(7)
                );

            XElement data1Elem =
                new XElement(
                    "data1",
                    BytesToHex(_data1)
                );

            XElement data2Elem =
                new XElement(
                    "data2",
                    BytesToHex(_data2)
                );

            XElement data3Elem =
                new XElement(
                    "data3",
                    BytesToHex(_data3)
                );

            return new XDocument(
                new XElement(
                    "csystem",
                    configElem,
                    data1Elem,
                    data2Elem,
                    data3Elem
                )
            );
        }

        public void FromXml(XDocument doc)
        {
            XElement root = doc.Root;
            if (root.Name != "csystem")
                throw new InvalidDataException("Invalid root element name");

            XElement configElem = root.Element("config");
            if (configElem == null)
                throw new InvalidDataException("<config> element missing");

            _items = new List<byte[]>();
            foreach (XElement itemElem in configElem.Elements("item"))
            {
                _items.Add(ItemFromXml(itemElem));
            }

            XElement data1Elem = root.Element("data1");
            if (data1Elem == null)
                throw new InvalidDataException("<data1> element is missing");

            _data1 = HexToBytes(data1Elem.Value);

            XElement data2Elem = root.Element("data2");
            if (data2Elem == null)
                throw new InvalidDataException("<data2> element is missing");

            _data2 = HexToBytes(data2Elem.Value);

            XElement data3Elem = root.Element("data3");
            if (data3Elem == null)
                throw new InvalidDataException("<data3> element is missing");

            _data3 = HexToBytes(data3Elem.Value);
        }

        private static List<byte[]> UnpackItems(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            BinaryReader reader = new BinaryReader(stream);
            List<byte[]> items = new List<byte[]>();
            while (stream.Position < stream.Length)
            {
                int length = reader.ReadInt32() + 1;
                byte[] item = reader.ReadBytes(length);

                if (item.Length > 0 && item[0] == (byte)'S')
                {
                    byte[] xorlessItem = new byte[item.Length - 1];
                    //xorlessItem[0] = item[0];
                    Array.Copy(item, 1, xorlessItem, 0, item.Length - 1);
                    item = xorlessItem;
                }
                else
                {
                    Console.WriteLine("UnpackItems: item not start with 'S'");
                }

                if (item.Length >= 2 && item[item.Length - 2] == 0xFE && item[item.Length - 1] == 0xA)
                {
                    byte[] trimmedItem = new byte[item.Length - 2];
                    Array.Copy(item, trimmedItem, trimmedItem.Length);
                    item = trimmedItem;
                }

                items.Add(item);
            }
            return items;
        }

        private static ArraySegment<byte> PackItems(List<byte[]> items)
        {
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            foreach (byte[] item in items)
            {
                byte[] itemToWrite = item;
                //if (item[0] == (byte)'S')
                {
                    byte[] xoredItem = new byte[item.Length + 1];
                    xoredItem[0] = (byte)'S';
                    Array.Copy(item, 0, xoredItem, 1, item.Length);
                    itemToWrite = xoredItem;
                }

                writer.Write(itemToWrite.Length - 1);
                writer.Write(itemToWrite);
            }

            stream.TryGetBuffer(out ArraySegment<byte> data);
            return data;
        }

        private XElement BinaryItemToXml(int index)
        {
            if (index >= _items.Count)
                return null;

            byte[] item = _items[index];
            return new XElement(
                "item",
                new XAttribute("type", "binary"),
                BytesToHex(item)
            );
        }

        private XElement TextItemToXml(int index)
        {
            if (index >= _items.Count)
                return null;

            try
            {
                string text = UTF16Encoding.GetString(_items[index]);
                return new XElement(
                    "item",
                    new XAttribute("type", "text"),
                    text
                );
            }
            catch (DecoderFallbackException)
            {
                return BinaryItemToXml(index);
            }
        }

        

        private XElement DictionaryItemToXml(int index)
        {
            if (index >= _items.Count)
                return null;

            byte[] item = _items[index];

            XElement dictElem =
                new XElement(
                    "item",
                    new XAttribute("type", "dict")
                );

            if ((char)item[0] != '#')
                throw new InvalidDataException();

            int offset = 2;
            while (offset < item.Length)
            {
                string key = null;
                while (offset < item.Length)
                {
                    string c = Helper.ReadUTF16(item, offset);
                    offset += 2;
                    if (c.Equals(":"))
                        break;

                    key += c;
                }
                if (offset >= item.Length)
                    break;

                int valueOffset = offset;
                int value = 0;
                while (true)
                {
                    byte b = item[offset];
                    offset += 2;
                    if (b == 201)
                        break;

                    if (b <= 200)
                        value += b;
                    else if (b == 250)
                        value = 0;
                }

                if (offset == valueOffset + 2)
                    value = -1;

                dictElem.Add(new XElement("entry", new XAttribute("key", key), value.ToString()));
            }

            return dictElem;
        }

        private static byte[] ItemFromXml(XElement elem)
        {
            switch (elem.Attribute("type")?.Value)
            {
                case "binary":
                    return BinaryItemFromXml(elem);

                case "text":
                    return TextItemFromXml(elem);

                case "dict":
                    return DictionaryItemFromXml(elem);

                default:
                    throw new InvalidDataException("Unrecognized type for <item>");
            }
        }

        private static byte[] BinaryItemFromXml(XElement elem)
        {
            return HexToBytes(elem.Value);
        }

        private static byte[] TextItemFromXml(XElement elem)
        {
            return UTF16Encoding.GetBytes(elem.Value);
        }

        private static byte[] DictionaryItemFromXml(XElement elem)
        {
            MemoryStream stream = new MemoryStream();
            //stream.WriteByte((byte)'#');
            Helper.WriteUTF16(stream, "#");

            foreach (XElement entry in elem.Elements("entry"))
            {
                string key = entry.Attribute("key").Value;
                //foreach (char c in key)
                //{
                //    stream.WriteByte((byte)c);
                //}
                Helper.WriteUTF16(stream, key);

                //stream.WriteByte((byte)':');
                Helper.WriteUTF16(stream, ":");

                int value = int.Parse(entry.Value);
                if (value < -1)
                {
                    throw new InvalidDataException("Dictionary values can't be less than -1");
                }
                else if (value == -1)
                {
                }
                else if (value == 0)
                {
                    stream.WriteByte(250);
                    stream.WriteByte(0);
                }
                else
                {
                    while (value > 200)
                    {
                        stream.WriteByte(200);
                        stream.WriteByte(0);
                        value -= 200;
                    }
                    stream.WriteByte((byte)value);
                    stream.WriteByte(0);
                }

                stream.WriteByte(201);
                stream.WriteByte(0);
            }

            byte[] item = new byte[stream.Length];
            stream.Position = 0;
            stream.Read(item, 0, item.Length);
            return item;
        }

        public static string BytesToHex(byte[] bytes)
        {
            StringBuilder hex = new StringBuilder();
            foreach (byte b in bytes)
            {
                hex.AppendFormat("{0:X02} ", b);
            }

            if (hex.Length > 0)
                hex.Length--;

            return hex.ToString();
        }

        public static byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", "");
            if (hex.Length % 2 != 0)
                throw new InvalidDataException("Hex string must have an even number of digits");

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber);
            }
            return bytes;
        }
    }
}
