using System;
using System.IO;
using System.Text;
using Steamworks;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Serialization
{
    /// <summary>
    /// Binary deserializer for reading structured network packets from raw byte buffers.
    /// </summary>
    public class PacketReader : IDisposable
    {
        private readonly MemoryStream _stream;
        private readonly BinaryReader _reader;

        public PacketType Type { get; }
        public long BytesRemaining => _stream.Length - _stream.Position;

        public PacketReader(byte[] data, int offset, int length)
        {
            _stream = new MemoryStream(data, offset, length, false);
            _reader = new BinaryReader(_stream, Encoding.UTF8);

            // Read the packet type header byte
            Type = (PacketType)_reader.ReadByte();
        }

        public byte ReadByte() => _reader.ReadByte();
        public bool ReadBool() => _reader.ReadBoolean();
        public int ReadInt() => _reader.ReadInt32();
        public uint ReadUInt() => _reader.ReadUInt32();
        public short ReadShort() => _reader.ReadInt16();
        public ushort ReadUShort() => _reader.ReadUInt16();
        public long ReadLong() => _reader.ReadInt64();
        public ulong ReadULong() => _reader.ReadUInt64();
        public float ReadFloat() => _reader.ReadSingle();
        public double ReadDouble() => _reader.ReadDouble();

        public string ReadString()
        {
            ushort length = _reader.ReadUInt16();
            if (length == 0) return string.Empty;
            byte[] bytes = _reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        public CSteamID ReadSteamID()
        {
            ulong id = _reader.ReadUInt64();
            return new CSteamID(id);
        }

        public Vector3 ReadVector3()
        {
            return new Vector3(
                _reader.ReadSingle(),
                _reader.ReadSingle(),
                _reader.ReadSingle()
            );
        }

        public Vector3 ReadHalfVector3()
        {
            return new Vector3(
                Mathf.HalfToFloat(_reader.ReadUInt16()),
                Mathf.HalfToFloat(_reader.ReadUInt16()),
                Mathf.HalfToFloat(_reader.ReadUInt16())
            );
        }

        public Quaternion ReadQuaternion()
        {
            return new Quaternion(
                _reader.ReadSingle(),
                _reader.ReadSingle(),
                _reader.ReadSingle(),
                _reader.ReadSingle()
            );
        }

        public float ReadByteYaw()
        {
            byte b = _reader.ReadByte();
            return (b * 360f) / 255f;
        }

        public byte[] ReadBytes(int count)
        {
            return _reader.ReadBytes(count);
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }
    }
}
