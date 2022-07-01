using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoLite2Helper
{
    public class GeoLite2IPHelper : IDisposable
    {
        private static Regex m_reg_addr = new Regex(@"""亚洲"",((?:HK|TW),"".*?"",.*?,.*?),,,");

        public GeoLite2IPHelper(string strDBFile, bool bMemory) {
            int nByte = 0;
            List<byte> lst = new List<byte>();
            if (bMemory)
                m_stream = new MemoryStream(File.ReadAllBytes(strDBFile));
            else
                m_stream = new FileStream(strDBFile, FileMode.Open, FileAccess.Read);
            byte[] byOffset = new byte[8];
            m_stream.Read(byOffset, 0, 8);
            if (BitConverter.ToUInt64(byOffset, 0) != 0x0050494C475453) {
                throw new InvalidDataException("Invalid file");
            }
            int nVer = m_stream.ReadByte();
            if (nVer != 1) {
                throw new InvalidDataException("Invalid version: " + nVer);
            }

            m_stream.Read(byOffset, 0, 8);
            m_ipIdxOffset = BitConverter.ToInt64(byOffset, 0);
            m_stream.Read(byOffset, 0, 8);
            m_locTitleOffset = BitConverter.ToInt64(byOffset, 0);
            m_stream.Read(byOffset, 0, 8);
            m_ipTitleOffset = BitConverter.ToInt64(byOffset, 0);
            m_stream.Read(byOffset, 0, 8);
            m_keyOffset = BitConverter.ToInt64(byOffset, 0);
            m_locIdxOffset = m_stream.Position;

            m_stream.Seek(m_locTitleOffset, SeekOrigin.Begin);
            while ((nByte = m_stream.ReadByte()) != 0) lst.Add((byte)nByte);
            m_loc_title.Clear();
            m_loc_title.AddRange(Encoding.UTF8.GetString(lst.ToArray(), 0, lst.Count).Split(','));

            lst.Clear();
            m_stream.Seek(m_ipTitleOffset, SeekOrigin.Begin);
            while ((nByte = m_stream.ReadByte()) != 0) lst.Add((byte)nByte);
            m_ip_title.Clear();
            m_ip_title.AddRange(Encoding.UTF8.GetString(lst.ToArray(), 0, lst.Count).Split(','));

            lst.Clear();
            m_stream.Seek(m_keyOffset, SeekOrigin.Begin);
            while ((nByte = m_stream.ReadByte()) != 0) {
                if (nByte == ',') {
                    m_lst_key.Add(Encoding.UTF8.GetString(lst.ToArray(), 0, lst.Count));
                    lst.Clear();
                    continue;
                }
                lst.Add((byte)nByte);
            }
            if (lst.Count != 0) m_lst_key.Add(Encoding.UTF8.GetString(lst.ToArray(), 0, lst.Count));
        }

        private long m_locIdxOffset;
        private long m_ipIdxOffset;
        private long m_locTitleOffset;
        private long m_ipTitleOffset;
        private long m_keyOffset;
        private Stream m_stream;

        private List<string> m_lst_key = new List<string>();
        private List<string> m_loc_title = new List<string>();
        private List<string> m_ip_title = new List<string>();

        public static void CreateDB(string strLocFileName, string strIPFileName, string strOutPut) {
            byte[] byTemp = null;
            List<string> lst_loc_title = new List<string>();
            List<string> lst_ip_title = new List<string>();
            List<string> lst_keyword = new List<string>();
            Dictionary<string, int> dic_key = new Dictionary<string, int>();
            using (FileStream fs = new FileStream(strOutPut, FileMode.Create)) {
                fs.Write(new byte[] { (byte)'S', (byte)'T', (byte)'G', (byte)'L', (byte)'I', (byte)'P', 0, 0 }, 0, 8);
                fs.WriteByte(1);                        // version
                fs.Write(new byte[4 * 8], 0, 4 * 8);    // offset
                long lLocIdxOffset = fs.Position;
                long lIpIdxOffset = GeoLite2IPHelper.CreateLocInfo(fs, lst_loc_title, dic_key, strLocFileName);
                long lLocTitleOffset = GeoLite2IPHelper.CreateIPInfo(fs, lst_ip_title, dic_key, strIPFileName);
                byTemp = Encoding.UTF8.GetBytes(string.Join(",", lst_loc_title.ToArray()) + "\0");
                fs.Write(byTemp, 0, byTemp.Length);
                long lIpTitleOffset = fs.Length;
                byTemp = Encoding.UTF8.GetBytes(string.Join(",", lst_ip_title.ToArray()) + "\0");
                fs.Write(byTemp, 0, byTemp.Length);
                long lKeyOffset = fs.Length;
                foreach (var v in dic_key.Keys) {
                    byTemp = Encoding.UTF8.GetBytes(v + ",");
                    fs.Write(byTemp, 0, byTemp.Length);
                }
                fs.Seek(-1, SeekOrigin.Current);
                byTemp = Encoding.UTF8.GetBytes("\0\r\n[GeoLite2 City Data] Created by -> https://github.com/DebugST");
                fs.Write(byTemp, 0, byTemp.Length);

                fs.Seek(9, SeekOrigin.Begin);
                byTemp = BitConverter.GetBytes(lIpIdxOffset);
                fs.Write(byTemp, 0, byTemp.Length);
                byTemp = BitConverter.GetBytes(lLocTitleOffset);
                fs.Write(byTemp, 0, byTemp.Length);
                byTemp = BitConverter.GetBytes(lIpTitleOffset);
                fs.Write(byTemp, 0, byTemp.Length);
                byTemp = BitConverter.GetBytes(lKeyOffset);
                fs.Write(byTemp, 0, byTemp.Length);
            }
        }

        private static long CreateLocInfo(FileStream fs, List<string> lstTitle, Dictionary<string, int> dic_key, string strFileName) {
            List<byte> lst = new List<byte>();
            using (StreamReader reader = new StreamReader(strFileName, Encoding.UTF8)) {
                string strLine = string.Empty;
                string strTemp = string.Empty;
                lstTitle.AddRange(reader.ReadLine().Trim().Split(','));
                while ((strLine = reader.ReadLine()) != null) {
                    strLine = strLine.Trim();
                    if (strLine == string.Empty) continue;
                    strLine = m_reg_addr.Replace(strLine, "亚洲,CN,中国,$1,");
                    string[] strCols = strLine.Trim().Replace("\"", "").Split(',');
                    lst.AddRange(BitConverter.GetBytes(uint.Parse(strCols[0])));
                    for (int i = 1; i < strCols.Length; i++) {
                        strTemp = strCols[i];
                        if (dic_key.ContainsKey(strTemp)) {
                            lst.AddRange(BitConverter.GetBytes((ushort)dic_key[strTemp]));
                            continue;
                        }
                        dic_key.Add(strTemp, dic_key.Count);
                        lst.AddRange(BitConverter.GetBytes((ushort)dic_key[strTemp]));
                    }
                }
                fs.Write(lst.ToArray(), 0, lst.Count);
            }
            return fs.Length;
        }

        private static long CreateIPInfo(FileStream fs, List<string> lstTitle, Dictionary<string, int> dic, string strFileName) {
            List<byte> lst_line = new List<byte>();
            using (StreamReader reader = new StreamReader(strFileName, Encoding.UTF8)) {
                string strLine = string.Empty;
                lstTitle.AddRange(reader.ReadLine().Trim().Split(','));
                while ((strLine = reader.ReadLine()) != null) {
                    lst_line.Clear();
                    string[] strCols = strLine.Trim().Split(',');
                    string[] strIP = strCols[0].Split('/')[0].Split('.');
                    lst_line.AddRange(new byte[] { byte.Parse(strIP[3]), byte.Parse(strIP[2]), byte.Parse(strIP[1]), byte.Parse(strIP[0]) });
                    lst_line.Add(byte.Parse(strCols[0].Split('/')[1]));
                    for (int i = 1; i < strCols.Length; i++) {
                        string strTemp = strCols[i].Trim().Trim('"').Trim('\'');
                        if (dic.ContainsKey(strTemp)) {
                            lst_line.AddRange(BitConverter.GetBytes(dic[strTemp]));
                            continue;
                        }
                        dic.Add(strTemp, dic.Count);
                        lst_line.AddRange(BitConverter.GetBytes(dic[strTemp]));
                    }
                    fs.Write(lst_line.ToArray(), 0, lst_line.Count);
                }
            }
            return fs.Length;
        }

        public string GetInfo(string strIP) {
            string strIPAddr = string.Empty;
            string strIPAddr_reg = string.Empty;
            List<string> lst = new List<string>();
            uint uip = Util.IPToUINT(strIP);
            lock (m_stream) {
                int[] ipInfo = this.BinarySearchIP(m_ipIdxOffset, m_locTitleOffset - 4 * m_ip_title.Count - 1, uip);
                if (ipInfo == null) return "{}";
                for (int i = 0; i < ipInfo.Length; i++) {
                    string strValue = "\"\"";
                    if (m_ip_title[i + 1].IndexOf("geoname_id") != -1) {
                        if (m_lst_key[ipInfo[i]] != string.Empty)
                            strValue = this.GetLocJson(this.BinarySearchLoc(m_locIdxOffset, m_ipIdxOffset - 2 * m_loc_title.Count - 2, uint.Parse(m_lst_key[ipInfo[i]])));
                    } else
                        strValue = "\"" + m_lst_key[ipInfo[i]] + "\"";
                    lst.Add("\"" + m_ip_title[i + 1].Replace("geoname_id", "geoname") + "\" : " + strValue);
                }
                return "{" + string.Join(",", lst.ToArray()) + "}";
            }
        }

        private string GetLocJson(ushort[] locInfo) {
            List<string> lst = new List<string>();
            for (int i = 0; i < locInfo.Length; i++) {
                lst.Add("\"" + m_loc_title[i + 1] + "\" : \"" + m_lst_key[locInfo[i]] + "\"");
            }
            return "{" + string.Join(",", lst.ToArray()) + "}";
        }

        private int[] BinarySearchIP(long nLow, long nHigh, uint uIP) {
            int lineOffset = 4 * m_ip_title.Count + 1;
            byte[] byInfo = new byte[4 * (m_ip_title.Count - 1)];
            if (nLow > nHigh) return null;
            long nMid = nLow + (((int)((nHigh - nLow) / (double)lineOffset)) / 2 * lineOffset);
            m_stream.Seek(nMid, SeekOrigin.Begin);
            m_stream.Read(byInfo, 0, 5);
            uint uIPStart = BitConverter.ToUInt32(byInfo, 0);
            uint uIPEnd = uIPStart | ((uint)((long)0xFFFFFFFF >> byInfo[4]));
            if (uIPStart <= uIP && uIP <= uIPEnd) {
                m_stream.Read(byInfo, 0, byInfo.Length);
                int[] ret = new int[byInfo.Length / 4];
                for (int i = 0; i < byInfo.Length; i += 4) ret[i / 4] = BitConverter.ToInt32(byInfo, i);
                return ret;
            } else if (uIPStart > uIP) return BinarySearchIP(nLow, nMid - lineOffset, uIP);
            else return BinarySearchIP(nMid + lineOffset, nHigh, uIP);
        }

        private ushort[] BinarySearchLoc(long nLow, long nHigh, uint uId) {
            int lineOffset = 2 * m_loc_title.Count + 2;
            byte[] byInfo = new byte[2 * (m_loc_title.Count - 1)];
            if (nLow > nHigh) return null;
            long nMid = nLow + (((int)((nHigh - nLow) / (double)lineOffset)) / 2 * lineOffset);
            m_stream.Seek(nMid, SeekOrigin.Begin);
            m_stream.Read(byInfo, 0, 4);
            uint uIdTemp = BitConverter.ToUInt32(byInfo, 0);
            if (uIdTemp == uId) {
                m_stream.Read(byInfo, 0, byInfo.Length);
                ushort[] ret = new ushort[byInfo.Length / 2];
                for (int i = 0; i < byInfo.Length; i += 2) ret[i / 2] = BitConverter.ToUInt16(byInfo, i);
                return ret;
            } else if (uIdTemp > uId) return BinarySearchLoc(nLow, nMid - lineOffset, uId);
            else return BinarySearchLoc(nMid + lineOffset, nHigh, uId);
        }

        public void Dispose() {
            if (m_stream != null) {
                m_stream.Close();
            }
            m_ip_title.Clear();
            m_loc_title.Clear();
            m_lst_key.Clear();
        }
    }
}
