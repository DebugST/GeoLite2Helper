using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GeoLite2Helper
{
    internal class Util
    {
        private static Dictionary<string, uint> m_dic_ip;

        static Util() {
            m_dic_ip = new Dictionary<string, uint>();
            for (uint i = 0; i <= 255; i++) {
                m_dic_ip.Add(i.ToString(), i);
            }
        }

        public static uint IPToUINT(string strIP) {
            string[] strs = strIP.Split('.');
            if (strs.Length != 4) {
                throw new ArgumentException("strIP", "Invalid IP");
            }
            uint uip = 0;
            try {
                foreach (var v in strs) {
                    uip <<= 8;
                    uip |= m_dic_ip[v];
                }
            } catch {
                throw new ArgumentException("strIP", "Invalid IP");
            }
            return uip;
        }
    }
}
