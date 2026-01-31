using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace ROMapOverlayEditor.IO
{
    public class BinaryReaderEx : BinaryReader
    {
        public static Encoding EncodingAscii = Encoding.ASCII;
        public static Encoding KoreanEncoding;

        static BinaryReaderEx()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try
            {
                KoreanEncoding = Encoding.GetEncoding(949);
            }
            catch
            {
                KoreanEncoding = Encoding.GetEncoding("EUC-KR");
            }
        }

        public BinaryReaderEx(Stream input, bool leaveOpen = false) : base(input, Encoding.UTF8, leaveOpen)
        {
        }

        public BinaryReaderEx(Stream input, Encoding encoding, bool leaveOpen = false) : base(input, encoding, leaveOpen)
        {
        }

        public string ReadFixedString(int length, Encoding encoding)
        {
            var bytes = ReadBytes(length);
            if (bytes.Length == 0) return string.Empty;

            // Find null terminator
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) end = bytes.Length;

            return encoding.GetString(bytes, 0, end).TrimEnd('\0'); // TrimEnd checks for any trailing nulls if encoding kept them
        }
    }
}
