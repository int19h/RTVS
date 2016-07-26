﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Microsoft.Common.Core;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Text;
using Newtonsoft.Json;
using static System.FormattableString;
using System.Diagnostics;
using System.IO;

namespace Microsoft.R.Host.Client {
    public class Message {
        public readonly ulong Id;
        public readonly ulong RequestId;
        public readonly string Name;

        public readonly JArray Json;
        public readonly byte[] Blob;

        public bool IsRequest => RequestId == ulong.MaxValue;

        public bool IsNotification => RequestId == 0;

        public bool IsResponse => !IsRequest && !IsNotification;

        public int ArgumentCount => Json.Count;

        public Message(ulong id, ulong requestId, string name, JArray json, byte[] blob = null) {
            Id = id;
            RequestId = requestId;
            Name = name;
            Json = json;
            Blob = blob ?? new byte[0];
        }

        public Message(byte[] data) {
            try {
                int offset = 0;
                Id = BitConverter.ToUInt64(data, offset);
                offset += sizeof(ulong);

                RequestId = BitConverter.ToUInt64(data, offset);
                offset += sizeof(ulong);

                int term = Array.IndexOf<byte>(data, 0, offset);
                if (term < 0) {
                    throw new IndexOutOfRangeException();
                }
                Name = Encoding.UTF8.GetString(data, offset, term - offset);
                offset = term + 1;

                term = Array.IndexOf<byte>(data, 0, offset);
                if (term < 0) {
                    throw new IndexOutOfRangeException();
                }
                string json = Encoding.UTF8.GetString(data, offset, term - offset);
                Json = (JArray)JsonConvert.DeserializeObject(json);
                offset = term + 1;

                Blob = new byte[data.Length - offset];
                if (Blob.Length > 0) {
                    Array.Copy(data, offset, Blob, 0, Blob.Length);
                }
            } catch (Exception ex) when (ex is IndexOutOfRangeException || ex is ArgumentOutOfRangeException) {
                throw ProtocolError($"Malformed message", BitConverter.ToString(data));
            }
        }

        public byte[] ToBytes() {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8)) {
                    writer.Write(Id);
                    writer.Write(RequestId);
                    writer.Write(Name.ToCharArray());
                    writer.Write('\0');
                    writer.Write(Json.ToString().ToCharArray());
                    writer.Write('\0');
                    writer.Write(Blob);
                }

                var bytes = stream.ToArray();
                return bytes;
            }
        }

        public override string ToString() {
            string result;
            result = $"#{Id}#";
            if (RequestId > 0 && RequestId < ulong.MaxValue) {
                result += $":#{RequestId}#";
            }
            result += $" {Name} {Json} {BitConverter.ToString(Blob)}";
            return result;
        }

        public void ExpectArguments(int count) {
            if (ArgumentCount != count) {
                throw ProtocolError($"{count} arguments expected:", this);
            }
        }

        public void ExpectArguments(int min, int max) {
            if (ArgumentCount < min) {
                throw ProtocolError($"At least {min} arguments expected:", this);
            } else if (ArgumentCount > max) {
                throw ProtocolError($"At most {max} arguments expected:", this);
            }
        }

        public JToken this[int i] {
            get {
                return Json[i];
            }
        }

        public JToken GetArgument(int i, string name, JTokenType expectedType) {
            var arg = this[i];
            if (arg.Type != expectedType) {
                throw ProtocolError($"{name} must be {expectedType}:", this);
            }
            return arg;
        }

        public JToken GetArgument(int i, string name, JTokenType expectedType1, JTokenType expectedType2) {
            var arg = this[i];
            if (arg.Type != expectedType1 && arg.Type != expectedType2) {
                throw ProtocolError($"{name} must be {expectedType1} or {expectedType2}:", this);
            }
            return arg;
        }

        public string GetString(int i, string name, bool allowNull = false) {
            var arg = allowNull ? GetArgument(i, name, JTokenType.String, JTokenType.Null) : GetArgument(i, name, JTokenType.String);
            if (arg.Type == JTokenType.Null) {
                return null;
            }
            return (string)arg;
        }

        public int GetInt32(int i, string name) {
            var arg = GetArgument(i, name, JTokenType.Integer);
            return (int)arg;
        }

        public ulong GetUInt64(int i, string name) {
            var arg = GetArgument(i, name, JTokenType.Integer);
            return (ulong)arg;
        }

        public bool GetBoolean(int i, string name) {
            var arg = GetArgument(i, name, JTokenType.Boolean);
            return (bool)arg;
        }

        public TEnum GetEnum<TEnum>(int i, string name, string[] names) {
            var arg = GetArgument(i, name, JTokenType.Integer, JTokenType.String);

            if (arg.Type == JTokenType.Integer) {
                return (TEnum)(object)(int)arg;
            }

            int n = Array.IndexOf(names, (string)arg);
            if (n < 0) {
                throw ProtocolError($"Argument #{i} must be integer, or one of: {string.Join(", ", names)}:", this);
            }

            return (TEnum)(object)n;
        }

        private static Exception ProtocolError(FormattableString fs, object message = null) {
            var s = Invariant(fs);
            if (message != null) {
                s += "\n\n" + message;
            }
            Trace.Fail(s);
            return new InvalidDataException(s);
        }
    }
}
