using System;
using System.IO;
using System.Text;
using Steamworks;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Serialization
{
    /// <summary>
    /// Binary serializer tailored for high throughput, low allocation packet writing.
    /// </summary>
    public class PacketWriter : IDisposable
    {
        private readonly MemoryStream _stream;
        private readonly BinaryWriter _writer;

        public PacketType Type { get; }
        public long Length => _stream.Length;

        public PacketWriter(PacketType packetType, int initialCapacity = 256)
        {
            Type = packetType;
            _stream = new MemoryStream(initialCapacity);
            _writer = new BinaryWriter(_stream, Encoding.UTF8);

            // First byte is always the packet type identifier
            _writer.Write((byte)packetType);
        }

        public void WriteByte(byte value) => _writer.Write(value);
        public void WriteBool(bool value) => _writer.Write(value);
        public void WriteInt(int value) => _writer.Write(value);
        public void WriteUInt(uint value) => _writer.Write(value);
        public void WriteShort(short value) => _writer.Write(value);
        public void WriteUShort(ushort value) => _writer.Write(value);
        public void WriteLong(long value) => _writer.Write(value);
        public void WriteULong(ulong value) => _writer.Write(value);
        public void WriteFloat(float value) => _writer.Write(value);
        public void WriteDouble(double value) => _writer.Write(value);

        public void WriteString(string value)
        {
            if (value == null)
            {
                _writer.Write((ushort)0);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            _writer.Write((ushort)bytes.Length);
            _writer.Write(bytes);
        }

        public void WriteSteamID(CSteamID steamID)
        {
            _writer.Write(steamID.m_SteamID);
        }

        public void WriteVector3(Vector3 vec)
        {
            _writer.Write(vec.x);
            _writer.Write(vec.y);
            _writer.Write(vec.z);
        }

        /// <summary>
        /// Half-precision Vector3 serialization for bandwidth reduction in high-frequency movement.
        /// </summary>
        public void WriteHalfVector3(Vector3 vec)
        {
            _writer.Write(Mathf.FloatToHalf(vec.x));
            _writer.Write(Mathf.FloatToHalf(vec.y));
            _writer.Write(Mathf.FloatToHalf(vec.z));
        }

        public void WriteQuaternion(Quaternion quat)
        {
            _writer.Write(quat.x);
            _writer.Write(quat.y);
            _writer.Write(quat.z);
            _writer.Write(quat.w);
        }

        /// <summary>
        /// Compress yaw rotation to 1 single byte (0-255 mapped to 0-360 degrees).
        /// </summary>
        public void WriteByteYaw(float yawAngle)
        {
            float normalized = (yawAngle % 360f + 360f) % 360f;
            byte b = (byte)Mathf.RoundToInt(normalized * 255f / 360f);
            _writer.Write(b);
        }

        public void WriteBytes(byte[] buffer, int offset, int count)
        {
            _writer.Write(buffer, offset, count);
        }

        public byte[] ToArray()
        {
            return _stream.ToArray();
        }

        public void Dispose()
        {
            _writer.Dispose();
            _stream.Dispose();
        }
    }
}
