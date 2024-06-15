using WsjtxUdpLib.Messages.Out;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;


namespace WsjtxUdpLib.Messages
{
    /*
     * SwitchConfiguration  In 14                     quint32
     *                         Id (unique key)        utf8
     *                         Configuration Name     utf8
     *
     *      The server  may send  this message at  any time.   The message
     *      specifies the name of the  configuration to switch to. The new
     *      configuration must exist.
     */

    public class SwitchConfigurationMessage : WsjtxMessage, IWsjtxCommandMessageGenerator
    {
        public int SchemaVersion { get; set; }
        public string Id { get; set; }
        public string Configuration { get; set; }
        public byte[] GetBytes()
        {
            using (MemoryStream m = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(m))
                {
                    writer.Write(WsjtxMessage.MagicNumber);
                    writer.Write(EncodeQUInt32((UInt32)SchemaVersion));
                    writer.Write(EncodeQUInt32(14));    //msg type
                    writer.Write(EncodeString(Id));
                    writer.Write(EncodeString(Configuration));
                }
                return m.ToArray();
            }
        }

        public override string ToString() => $"SwitchCfg {this.ToCompactLine(nameof(Id))}";
    }
}
