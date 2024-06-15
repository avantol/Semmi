using WsjtxUdpLib.Messages.Out;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;


namespace WsjtxUdpLib.Messages
{
    /*
     * Configure      In       15                     quint32
     *                         Id (unique key)        utf8
     *                         Mode                   utf8
     *                         Frequency Tolerance    quint32
     *                         Submode                utf8
     *                         Fast Mode              bool
     *                         T/R Period             quint32
     *                         Rx DF                  quint32
     *                         DX Call                utf8
     *                         DX Grid                utf8
     *                         Generate Messages      bool
     *
     *      The server  may send  this message at  any time.   The message
     *      specifies  various  configuration  options.  For  utf8  string
     *      fields an empty value implies no change, for the quint32 Rx DF
     *      and  Frequency  Tolerance  fields the  maximum  quint32  value
     *      implies  no change.   Invalid or  unrecognized values  will be
     *      silently ignored.
     */

    public class ConfigureMessage : WsjtxMessage, IWsjtxCommandMessageGenerator
    {
        public int SchemaVersion { get; set; }
        public string Id { get; set; }
        public string Mode { get; set; }
        public UInt32 FreqTol { get; set; }
        public string SubMode { get; set; }
        public bool FastMode { get; set; }
        public UInt32 TrPeriod { get; set; }
        public UInt32 RxDf { get; set; }
        public string DxCall { get; set; }
        public string DxGrid { get; set; }
        public bool GenMsgs { get; set; }

        public byte[] GetBytes()
        {
            using (MemoryStream m = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(m))
                {
                    writer.Write(WsjtxMessage.MagicNumber);
                    writer.Write(EncodeQUInt32((UInt32)SchemaVersion));
                    writer.Write(EncodeQUInt32(15));    //msg type
                    writer.Write(EncodeString(Id));
                    writer.Write(EncodeString(Mode));
                    writer.Write(EncodeQUInt32((UInt32)FreqTol));
                    writer.Write(EncodeString(SubMode));
                    writer.Write(EncodeBoolean(FastMode));
                    writer.Write(EncodeQUInt32((UInt32)TrPeriod));
                    writer.Write(EncodeQUInt32((UInt32)RxDf));
                    writer.Write(EncodeString(DxCall));
                    writer.Write(EncodeString(DxGrid));
                    writer.Write(EncodeBoolean(GenMsgs));
                }
                return m.ToArray();
            }
        }
        public override string ToString() => $"Configure {this.ToCompactLine(nameof(Id))}";
    }
}

