using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace GeoLite2Helper
{
    public class GeoLite2ASHelper : IDisposable
    {
        private static Regex m_reg_line = new Regex(@"(.*?)/(\d+),(\d+),(.*)");
        public struct Result
        {
            public int Number;
            public string Name;

            public override string ToString() {
                return "{\"number\":" + this.Number + ",\"name\":\"" + this.Name + "\"}";
            }
        }

        private Stream m_stream;
        private long m_l_offset_name;
        private long m_l_offset_block;
        public int m_n_count;

        public GeoLite2ASHelper(string strDBFile, bool bMemory) {
            if (bMemory) {
                m_stream = new MemoryStream(File.ReadAllBytes(strDBFile));
            } else {
                m_stream = new FileStream(strDBFile, FileMode.Open);
            }
            byte[] byHeader = new byte[8];
            m_stream.Read(byHeader, 0, byHeader.Length);
            if (BitConverter.ToUInt64(byHeader, 0) != 0x000053414C475453) {
                throw new InvalidDataException("Invalid file");
            }
            int nVer = m_stream.ReadByte();
            if (nVer != 1) {
                throw new InvalidDataException("Invalid version: " + nVer);
            }
            m_stream.Read(byHeader, 0, byHeader.Length);
            m_l_offset_name = BitConverter.ToInt64(byHeader, 0);
            m_l_offset_block = m_stream.Position;
            m_n_count = (int)((m_l_offset_name - m_l_offset_block) / 9);
            // 9 = IP/mask offset;
        }

        public static void CreateDB(string strCSVFile, string strOutFile) {
            Dictionary<string, byte[]> dic_name_offset = new Dictionary<string, byte[]>();
            List<byte[]> lst_name = new List<byte[]>();
            int name_offset = 0;
            using (StreamReader reader = new StreamReader(strCSVFile, Encoding.UTF8)) {
                using (FileStream fs = new FileStream(strOutFile, FileMode.Create)) {
                    byte[] byHeader = new byte[] { (byte)'S', (byte)'T', (byte)'G', (byte)'L', (byte)'A', (byte)'S', 0, 0 };
                    fs.Write(byHeader, 0, byHeader.Length);
                    fs.WriteByte(1);                            // version
                    fs.Write(byHeader, 0, byHeader.Length);     // offset of as_name
                    string strLine = string.Empty;
                    reader.ReadLine();
                    while ((strLine = reader.ReadLine()) != null) {
                        strLine = strLine.Trim();
                        if (strLine == string.Empty) continue;
                        var m = m_reg_line.Match(strLine);
                        if (!m.Success) continue;
                        string strKey = m.Groups[2].Value + "," + m.Groups[4].Value;
                        string[] strIP = m.Groups[1].Value.Split('.');
                        var byIP = new byte[] { byte.Parse(strIP[3]), byte.Parse(strIP[2]), byte.Parse(strIP[1]), byte.Parse(strIP[0]) };
                        fs.Write(byIP, 0, byIP.Length);
                        fs.WriteByte(byte.Parse(m.Groups[2].Value));
                        if (!dic_name_offset.ContainsKey(strKey)) {
                            var byData = GeoLite2ASHelper.GetDataByte(m.Groups[3].Value, m.Groups[4].Value.Trim('"'));
                            lst_name.Add(byData);
                            dic_name_offset.Add(strKey, BitConverter.GetBytes(name_offset));
                            name_offset += byData.Length;
                        }
                        var byTemp = dic_name_offset[strKey];
                        fs.Write(byTemp, 0, byTemp.Length);
                    }
                    byte[] by_offset = BitConverter.GetBytes(fs.Position);
                    foreach (var v in lst_name) {
                        fs.Write(v, 0, v.Length);
                    }
                    var by_end = Encoding.UTF8.GetBytes("\r\n[GeoLite2 ASN Data] Created by -> https://github.com/DebugST");
                    fs.Write(by_end, 0, by_end.Length);
                    fs.Seek(9, SeekOrigin.Begin);
                    fs.Write(by_offset, 0, by_offset.Length);
                }
            }
        }

        private static byte[] GetDataByte(string strNumber, string strOrg) {
            var by_num = BitConverter.GetBytes(uint.Parse(strNumber));
            var by_org = Encoding.UTF8.GetBytes(strOrg.Trim('"'));
            var by_len = BitConverter.GetBytes(by_num.Length + by_org.Length);
            var by_ret = new byte[by_num.Length + by_org.Length + by_len.Length];
            Array.Copy(by_len, by_ret, by_len.Length);
            Array.Copy(by_num, 0, by_ret, by_len.Length, by_num.Length);
            Array.Copy(by_org, 0, by_ret, by_len.Length + by_num.Length, by_org.Length);
            return by_ret;
        }

        public Result GetInfo(string strIP) {
            uint uIP = Util.IPToUINT(strIP);
            int uOffset = this.BinarySearchIP(0, m_n_count - 1, uIP);
            return this.GetResultFormOffset(uOffset);
        }

        private int BinarySearchIP(int nLeft, int nRight, uint uIP) {
            int lineOffset = 9; // 4 + 1 + 4 -> IP/mask offset
            byte[] byInfo = new byte[9];
            if (nLeft > nRight) return -1;
            int nMid = (nRight + nLeft) >> 1;
            m_stream.Seek(m_l_offset_block + nMid * lineOffset, SeekOrigin.Begin);
            m_stream.Read(byInfo, 0, byInfo.Length);
            uint uIPStart = BitConverter.ToUInt32(byInfo, 0);
            uint uIPEnd = uIPStart | ((uint)((long)0xFFFFFFFF >> byInfo[4]));
            if (uIPStart <= uIP && uIP <= uIPEnd) {
                return BitConverter.ToInt32(byInfo, 5);
            } else if (uIPStart > uIP) {
                return BinarySearchIP(nLeft, nMid - 1, uIP);
            } else {
                return BinarySearchIP(nMid + 1, nRight, uIP);
            }
        }

        private Result GetResultFormOffset(int nOffset) {
            if (nOffset < 0) return new Result();
            byte[] byLen = new byte[4];
            m_stream.Seek(m_l_offset_name + nOffset, SeekOrigin.Begin);
            m_stream.Read(byLen, 0, byLen.Length);
            int nLen = BitConverter.ToInt32(byLen, 0);
            byte[] byData = new byte[nLen];
            m_stream.Read(byData, 0, byData.Length);
            int number = BitConverter.ToInt32(byData, 0);
            string strName = Encoding.UTF8.GetString(byData, 4, byData.Length - 4);
            return new Result() { Number = number, Name = strName };
        }

        public void Dispose() {
            if (m_stream != null) m_stream.Close();
        }
    }
}
