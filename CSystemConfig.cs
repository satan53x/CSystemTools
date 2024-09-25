using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace CSystemArc
{
    internal class CSystemConfig
    {
        private static readonly Encoding SjisEncoding = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private static readonly Encoding UTF16Encoding = Encoding.GetEncoding("UTF-16", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        private List<byte[]> _items;
        private const int MaxDataCount = 4;
        private List<byte[]> _dataList;

        public int Version = 23;

        public void Read(Stream stream)
        {
            byte[] stringData = BcdCompression.Decompress(stream);
            _items = UnpackItems(stringData);

            _dataList = new List<byte[]>();
            for (int i = 0; i < MaxDataCount; i++)
            {
                byte[] data = null;
                if (stream.Position < stream.Length)
                {
                    int dataSize = Bcd.Read(stream);
                    data = new byte[dataSize];
                    stream.Read(data, 0, data.Length);
                }
                _dataList.Add(data);
            }

            if (stream.Position != stream.Length)
                throw new InvalidDataException();
        }

        public void Write(Stream stream)
        {
            ArraySegment<byte> stringData = PackItems(_items);
            BcdCompression.Compress(stringData, stream);

            for (int i = 0; i < MaxDataCount; i++)
            {
                var data = _dataList[i];
                if (data != null)
                {
                    Bcd.Write(stream, data.Length);
                    stream.Write(data, 0, data.Length);
                }
            }
        }

        public XDocument ToXml()
        {
            if (_items.Count > 10)
                throw new InvalidDataException("Unexpected number of items");

            XElement configElem =
                new XElement(
                    "config",
                    TextItemToXml(0)
                );
            XElement item;
            if (Version == 24)
            {
                item = TextItemToXml(1);
            }
            else
            {
                item = DictionaryItemToXml(1);
            }
            configElem.Add(item);

            int mid = Version == 24 ? 6 : 5;
            for (int i = 2; i < _items.Count; i++)
            {
                if (i <= mid)
                {
                    item = TextItemToXml(i);
                    configElem.Add(item);
                }
                else
                {
                    item = BinaryItemToXml(i);
                    configElem.Add(item);
                }
            }

            XElement csystemElem = new XElement("csystem", configElem);
            for (int i = 0; i < MaxDataCount; i++)
            {
                var data = _dataList[i];
                if (data != null)
                {
                    XElement dataElem;
                    string name = $"data{i + 1}";
                    mid = Version == 24 ? 0 : -1;
                    if (i <= mid)
                    {
                        dataElem = DictionaryDataToXml(data, name);
                    }
                    else if (data.Length % 4 == 0)
                    {
                        dataElem = ListDataToXml(data, name);
                    }
                    else
                    {
                        dataElem = new XElement(
                            name,
                            new XAttribute("type", "binary"),
                            BytesToHex(data)
                        );
                    }
                    csystemElem.Add(dataElem);
                }
            }

            return new XDocument(
                csystemElem
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

            _dataList = new List<byte[]>();
            for (int i = 0; i < MaxDataCount; i++)
            {
                XElement dataElem = root.Element($"data{i + 1}");
                byte[] data = null;
                if (dataElem != null)
                {
                    data = ItemFromXml(dataElem);
                }
                _dataList.Add(data);
            }
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

        private XElement DictionaryDataToXml(byte[] data, string name)
        {
            XElement dictElem =
                new XElement(
                    name,
                    new XAttribute("type", "dict_data")
                );

            int offset = 0;
            uint count = BitConverter.ToUInt32(data, offset);
            offset += 4;
            while (offset < data.Length)
            {
                int valueOffset = offset + 0x10; //fixed
                string key = null;
                while (offset < data.Length)
                {
                    string c = Helper.ReadUTF16(data, offset);
                    offset += 2;
                    if (c.Equals(":"))
                        break;

                    key += c;
                }
                if (offset > valueOffset)
                    throw new InvalidDataException("Data dictionary bytes invalid.");

                int value = BitConverter.ToInt32(data, valueOffset);
                offset = valueOffset + 4;

                dictElem.Add(new XElement("entry", new XAttribute("key", key), value.ToString()));
            }
            if (count != dictElem.Elements().Count())
                throw new InvalidDataException("Data dictionary child count invalid.");

            return dictElem;
        }

        private XElement ListDataToXml(byte[] data, string name)
        {
            XElement listElem =
                new XElement(
                    name,
                    new XAttribute("type", "list_data")
                );

            int offset = 0;
            while (offset < data.Length)
            {
                int value = BitConverter.ToInt32(data, offset);
                offset += 4;
                listElem.Add(new XElement("value", value.ToString()));
            }

            return listElem;
        }

        // ---------------------------------------------------------------
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

                case "dict_data":
                    return DictionaryDataFromXml(elem);

                case "list_data":
                    return ListDataFromXml(elem);

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

        private static byte[] DictionaryDataFromXml(XElement elem)
        {
            MemoryStream stream = new MemoryStream();
            int count = elem.Elements().Count();
            byte[] bs = BitConverter.GetBytes(count);
            stream.Write(bs, 0, bs.Length);
            foreach (XElement entry in elem.Elements("entry"))
            {
                string key = entry.Attribute("key").Value;
                Helper.WriteUTF16(stream, key);
                Helper.WriteUTF16(stream, ":");
                for (int i = (key.Length+1)*2; i < 0x10; i++)
                {
                    stream.WriteByte(0);
                }

                int value = int.Parse(entry.Value);
                bs = BitConverter.GetBytes(value);
                stream.Write(bs, 0, bs.Length);
            }

            byte[] item = new byte[stream.Length];
            stream.Position = 0;
            stream.Read(item, 0, item.Length);
            return item;
        }

        private static byte[] ListDataFromXml(XElement elem)
        {
            MemoryStream stream = new MemoryStream();
            foreach (XElement entry in elem.Elements("value"))
            {
                int value = int.Parse(entry.Value);
                byte[] bs = BitConverter.GetBytes(value);
                stream.Write(bs, 0, bs.Length);
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
