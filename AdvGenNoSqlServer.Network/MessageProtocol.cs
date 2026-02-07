// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Message types supported by the NoSQL protocol
    /// </summary>
    public enum MessageType : byte
    {
        /// <summary>
        /// Initial handshake message
        /// </summary>
        Handshake = 0x01,

        /// <summary>
        /// Authentication request/response
        /// </summary>
        Authentication = 0x02,

        /// <summary>
        /// Query or command message
        /// </summary>
        Command = 0x03,

        /// <summary>
        /// Successful response
        /// </summary>
        Response = 0x04,

        /// <summary>
        /// Error response
        /// </summary>
        Error = 0x05,

        /// <summary>
        /// Ping/keepalive message
        /// </summary>
        Ping = 0x06,

        /// <summary>
        /// Pong/keepalive response
        /// </summary>
        Pong = 0x07,

        /// <summary>
        /// Transaction control (begin, commit, rollback)
        /// </summary>
        Transaction = 0x08,

        /// <summary>
        /// Bulk operation request
        /// </summary>
        BulkOperation = 0x09,

        /// <summary>
        /// Server notification/event
        /// </summary>
        Notification = 0x0A
    }

    /// <summary>
    /// Protocol flags for message options
    /// </summary>
    [Flags]
    public enum MessageFlags : byte
    {
        /// <summary>
        /// No special flags
        /// </summary>
        None = 0x00,

        /// <summary>
        /// Message payload is compressed
        /// </summary>
        Compressed = 0x01,

        /// <summary>
        /// Message requires acknowledgment
        /// </summary>
        RequireAck = 0x02,

        /// <summary>
        /// Message is encrypted
        /// </summary>
        Encrypted = 0x04,

        /// <summary>
        /// End of batch/stream marker
        /// </summary>
        EndOfBatch = 0x08
    }

    /// <summary>
    /// Binary message structure for NoSQL protocol
    /// </summary>
    public class NoSqlMessage
    {
        /// <summary>
        /// Protocol magic bytes (4 bytes: "NOSQ")
        /// </summary>
        public const uint Magic = 0x4E4F5351; // "NOSQ" in ASCII

        /// <summary>
        /// Protocol version (2 bytes)
        /// </summary>
        public const ushort ProtocolVersion = 1;

        /// <summary>
        /// Message type
        /// </summary>
        public MessageType MessageType { get; set; }

        /// <summary>
        /// Message flags
        /// </summary>
        public MessageFlags Flags { get; set; }

        /// <summary>
        /// Payload data (rented from ArrayPool, must be returned after use)
        /// </summary>
        public byte[]? Payload { get; set; }

        /// <summary>
        /// Length of the payload
        /// </summary>
        public int PayloadLength { get; set; }

        /// <summary>
        /// Gets the string representation of the payload
        /// </summary>
        public string GetPayloadAsString()
        {
            if (Payload == null || PayloadLength == 0)
                return string.Empty;

            return Encoding.UTF8.GetString(Payload, 0, PayloadLength);
        }

        /// <summary>
        /// Creates a message with string payload
        /// </summary>
        public static NoSqlMessage Create(MessageType type, string payload)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            return new NoSqlMessage
            {
                MessageType = type,
                Payload = bytes,
                PayloadLength = bytes.Length
            };
        }

        /// <summary>
        /// Creates a command message
        /// </summary>
        public static NoSqlMessage CreateCommand(string command, string collection, object? document = null)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"command\":\"{command}\",");
            sb.Append($"\"collection\":\"{collection}\"");
            
            if (document != null)
            {
                sb.Append(",");
                sb.Append($"\"document\":{System.Text.Json.JsonSerializer.Serialize(document)}");
            }
            
            sb.Append("}");
            
            return Create(MessageType.Command, sb.ToString());
        }

        /// <summary>
        /// Creates an error response
        /// </summary>
        public static NoSqlMessage CreateError(string errorCode, string errorMessage)
        {
            var json = $"{{\"success\":false,\"error\":{{\"code\":\"{errorCode}\",\"message\":\"{errorMessage}\"}}}}";
            return Create(MessageType.Error, json);
        }

        /// <summary>
        /// Creates a success response
        /// </summary>
        public static NoSqlMessage CreateSuccess(object? data = null)
        {
            string json;
            if (data != null)
            {
                json = $"{{\"success\":true,\"data\":{System.Text.Json.JsonSerializer.Serialize(data)}}}";
            }
            else
            {
                json = "{\"success\":true}";
            }
            return Create(MessageType.Response, json);
        }
    }

    /// <summary>
    /// Message header structure
    /// </summary>
    public struct MessageHeader
    {
        /// <summary>
        /// Protocol magic bytes
        /// </summary>
        public uint Magic;

        /// <summary>
        /// Protocol version
        /// </summary>
        public ushort Version;

        /// <summary>
        /// Message type
        /// </summary>
        public MessageType MessageType;

        /// <summary>
        /// Message flags
        /// </summary>
        public MessageFlags Flags;

        /// <summary>
        /// Length of the payload in bytes
        /// </summary>
        public int PayloadLength;

        /// <summary>
        /// Total header size in bytes
        /// </summary>
        public const int HeaderSize = 12; // 4 + 2 + 1 + 1 + 4
    }

    /// <summary>
    /// Handles serialization and deserialization of messages
    /// </summary>
    public class MessageProtocol
    {
        /// <summary>
        /// Serializes a message to a byte array (rented from ArrayPool)
        /// </summary>
        public byte[] Serialize(NoSqlMessage message)
        {
            // Header: Magic (4) + Version (2) + Type (1) + Flags (1) + PayloadLength (4) = 12 bytes
            // Payload: variable
            // Checksum: 4 bytes
            var totalLength = MessageHeader.HeaderSize + message.PayloadLength + 4;
            var buffer = ArrayPool<byte>.Shared.Rent(totalLength);

            try
            {
                var span = buffer.AsSpan(0, totalLength);
                int offset = 0;

                // Magic (4 bytes)
                BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset, 4), NoSqlMessage.Magic);
                offset += 4;

                // Version (2 bytes)
                BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset, 2), NoSqlMessage.ProtocolVersion);
                offset += 2;

                // Message Type (1 byte)
                span[offset] = (byte)message.MessageType;
                offset += 1;

                // Flags (1 byte)
                span[offset] = (byte)message.Flags;
                offset += 1;

                // Payload Length (4 bytes)
                BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset, 4), message.PayloadLength);
                offset += 4;

                // Payload (variable)
                if (message.Payload != null && message.PayloadLength > 0)
                {
                    message.Payload.AsSpan(0, message.PayloadLength).CopyTo(span.Slice(offset));
                    offset += message.PayloadLength;
                }

                // Checksum (4 bytes) - CRC32 of payload only
                var checksum = CalculateChecksum(message.Payload ?? Array.Empty<byte>(), message.PayloadLength);
                BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset, 4), checksum);

                return buffer;
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }

        /// <summary>
        /// Parses a message header from a byte array
        /// </summary>
        public MessageHeader ParseHeader(byte[] buffer)
        {
            if (buffer.Length < MessageHeader.HeaderSize)
                throw new ArgumentException("Buffer too small for header", nameof(buffer));

            var span = buffer.AsSpan();
            int offset = 0;

            return new MessageHeader
            {
                Magic = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4)),
                Version = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset + 4, 2)),
                MessageType = (MessageType)span[offset + 6],
                Flags = (MessageFlags)span[offset + 7],
                PayloadLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset + 8, 4))
            };
        }

        /// <summary>
        /// Validates a message header
        /// </summary>
        public bool ValidateHeader(MessageHeader header)
        {
            if (header.Magic != NoSqlMessage.Magic)
                return false;

            if (header.Version != NoSqlMessage.ProtocolVersion)
                return false;

            if (!Enum.IsDefined(typeof(MessageType), header.MessageType))
                return false;

            if (header.PayloadLength < 0 || header.PayloadLength > 100 * 1024 * 1024) // Max 100MB
                return false;

            return true;
        }

        private static readonly uint[] Crc32Table = GenerateCrc32Table();

        private static uint[] GenerateCrc32Table()
        {
            var table = new uint[256];
            const uint polynomial = 0xEDB88320;

            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ polynomial;
                    else
                        crc >>= 1;
                }
                table[i] = crc;
            }

            return table;
        }

        /// <summary>
        /// Calculates CRC32 checksum of data
        /// </summary>
        public uint CalculateChecksum(byte[] data, int length)
        {
            if (length == 0)
                return 0;

            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < length; i++)
            {
                byte index = (byte)(((crc) & 0xFF) ^ data[i]);
                crc = (crc >> 8) ^ Crc32Table[index];
            }
            return ~crc;
        }

        /// <summary>
        /// Validates checksum
        /// </summary>
        public bool ValidateChecksum(byte[] data, uint expectedChecksum)
        {
            if (expectedChecksum == 0 && data.Length == 0)
                return true;

            var actualChecksum = CalculateChecksum(data, data.Length);
            return actualChecksum == expectedChecksum;
        }

        /// <summary>
        /// Deserializes a message from a byte array
        /// </summary>
        public NoSqlMessage Deserialize(byte[] buffer, int offset = 0)
        {
            if (buffer.Length - offset < MessageHeader.HeaderSize + 4)
                throw new ArgumentException("Buffer too small", nameof(buffer));

            var header = ParseHeader(buffer.AsSpan(offset).ToArray());

            if (!ValidateHeader(header))
                throw new ProtocolException("Invalid message header");

            var payloadLength = header.PayloadLength;
            byte[]? payload = null;

            if (payloadLength > 0)
            {
                payload = new byte[payloadLength];
                Buffer.BlockCopy(buffer, offset + MessageHeader.HeaderSize, payload, 0, payloadLength);

                // Verify checksum
                var checksumOffset = offset + MessageHeader.HeaderSize + payloadLength;
                var expectedChecksum = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(checksumOffset, 4));

                if (!ValidateChecksum(payload, expectedChecksum))
                    throw new ProtocolException("Checksum validation failed");
            }

            return new NoSqlMessage
            {
                MessageType = header.MessageType,
                Flags = header.Flags,
                Payload = payload,
                PayloadLength = payloadLength
            };
        }
    }
}
