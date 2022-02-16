/*
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Avro.IO
{
    /// <content>
    /// Contains the netstandard2.1 and netcoreapp2.1 specific functionality for BinaryDecoder.
    /// </content>
    public partial class BinaryDecoder
    {
        private const int StackallocThreshold = 256;
        private const int MaxFastReadLength = 4096;

        /*
         * TODO: look into when gcAllowVeryLargeObjects was introduced.  The check using this may no longer be needed.
         * It was enabled by default with .Net Framework 4.5 onward.
         */
        private const int MaxDotNetArrayLength = 0x7FFFFFC7;

        /// <summary>
        /// A float is written as 4 bytes.
        /// The float is converted into a 32-bit integer using a method equivalent to
        /// Java's floatToIntBits and then encoded in little-endian format.
        /// </summary>
        /// <returns>
        /// The float just read
        /// </returns>
        public float ReadFloat()
        {
            Span<byte> buffer = stackalloc byte[4];
            Read(buffer);

            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer));
        }

        /// <summary>
        /// A double is written as 8 bytes.
        /// The double is converted into a 64-bit integer using a method equivalent to
        /// Java's doubleToLongBits and then encoded in little-endian format.
        /// </summary>
        /// <returns>
        /// A double value.
        /// </returns>
        public double ReadDouble()
        {
            Span<byte> buffer = stackalloc byte[8];
            Read(buffer);

            return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(buffer));
        }

        /// <summary>
        /// Reads a string written by <see cref="BinaryEncoder.WriteString(string)" />.
        /// </summary>
        /// <returns>
        /// String read from the stream.
        /// </returns>
        /// <exception cref="AvroException">
        /// Can not deserialize a string with negative length!
        /// or
        /// String length is not supported!
        /// or
        /// Unable to read {length} bytes from a byte array of length {bytes.Length}
        /// </exception>
        public string ReadString()
        {
            int length = ReadInt();

            if (length < 0)
            {
                throw new AvroException("Can not deserialize a string with negative length!");
            }

            if (length <= MaxFastReadLength)
            {
                byte[] bufferArray = null;

                try
                {
                    Span<byte> buffer = length <= StackallocThreshold ?
                        stackalloc byte[length] :
                        (bufferArray = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

                    Read(buffer);

                    return Encoding.UTF8.GetString(buffer);
                }
                finally
                {
                    if (bufferArray != null)
                    {
                        ArrayPool<byte>.Shared.Return(bufferArray);
                    }
                }
            }
            else
            {
                // TODO: Refer to comments on MaxDotNetArrayLength
                if (length > MaxDotNetArrayLength)
                {
                    throw new AvroException("String length is not supported!");
                }

                using (BinaryReader binaryReader = new BinaryReader(_stream, Encoding.UTF8, true))
                {
                    byte[] bytes = binaryReader.ReadBytes(length);

                    return bytes.Length != length
                        ? throw new AvroException("Could not read as many bytes from stream as expected!")
                        : Encoding.UTF8.GetString(bytes);
                }
            }
        }

        /// <summary>
        /// Reads the specified buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="start">The start.</param>
        /// <param name="len">The length.</param>
        private void Read(byte[] buffer, int start, int len) => Read(buffer.AsSpan(start, len));

        /// <summary>
        /// Reads the specified buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <exception cref="AvroException">End of stream reached</exception>
        private void Read(Span<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                int n = _stream.Read(buffer);
                if (n <= 0)
                {
                    throw new AvroException("End of stream reached");
                }

                buffer = buffer.Slice(n);
            }
        }
    }
}
