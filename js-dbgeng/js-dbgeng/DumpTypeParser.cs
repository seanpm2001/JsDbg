﻿using System;   
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Debuggers.DbgEng;

namespace JsDbg {
    internal class DumpTypeParser {
        internal struct SField {
            internal uint Offset;
            internal uint Size;
            internal string FieldName;
            internal string TypeName;
            internal SBitFieldDescription BitField;
        }

        internal struct SBaseClass {
            internal uint Offset;
            internal string TypeName;
        }

        internal struct SBitFieldDescription {
            internal byte BitOffset;
            internal byte BitLength;

            internal bool IsBitField {
                get { return this.BitLength != 0; }
            }
        }

        internal DumpTypeParser() {
            this.ParsedBaseClasses = new List<SBaseClass>();
            this.ParsedFields = new List<SField>();
            this.buffer = new StringBuilder();
        }

        internal void DumpTypeOutputHandler(object sender, DebugOutputEventArgs e) {
            this.buffer.Append(e.Output);
        }

        internal void Parse() {
            string[] lines = this.buffer.ToString().Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines) {
                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string offsetString = parts[0];
                if (offsetString[0] == '+') {
                    // The line has an offset.
                    uint offset = uint.Parse(offsetString.Substring(3), System.Globalization.NumberStyles.HexNumber);

                    switch (parts[1]) {
                        case "__BaseClass":
                            // +0x000 __BaseClass class [typename]
                            uint typeSize;
                            string typename;
                            SBitFieldDescription bitField;
                            bool didParseType = this.ParseType(ArraySlice(parts, 2), out typeSize, out typename, out bitField);
                            if (didParseType) {
                                this.ParsedBaseClasses.Add(new SBaseClass() { Offset = offset, TypeName = typename });
                            } else {
                                System.Diagnostics.Debug.WriteLine(String.Format("Unable to parse type entry: {0}", line));
                            }
                            break;
                        case "__VFN_table":
                            // Ignore vtables.
                            break;
                        default:  {
                            // A field.
                            SField field = new SField() { Offset = offset, FieldName = parts[1] };
                            if (parts.Length > 3 && ParseType(ArraySlice(parts, 3), out field.Size, out field.TypeName, out field.BitField)) {
                                this.ParsedFields.Add(field);
                            } else {
                                System.Diagnostics.Debug.WriteLine(String.Format("Unable to parse type entry: {0}", line));
                            }
                            break;
                        }
                    }
                }
            }
        }

        private string[] ArraySlice(string[] array, int firstIndex) {
            string[] newArray = new string[array.Length - firstIndex];
            for (int i = firstIndex; i < array.Length; ++i) {
                newArray[i - firstIndex] = array[i];
            }
            return newArray;
        }


        private struct SBuiltInTypeNameAndSize {
            internal SBuiltInTypeNameAndSize(string name, uint size) {
                this.Name = name;
                this.Size = size;
            }
            internal string Name;
            internal uint Size;
        }
        private static Dictionary<string, SBuiltInTypeNameAndSize> TypeMap = new Dictionary<string, SBuiltInTypeNameAndSize>() {
                {"void", new SBuiltInTypeNameAndSize("void", 0)},
                {"bool", new SBuiltInTypeNameAndSize("char", 1)},
                {"char", new SBuiltInTypeNameAndSize("char", 1)},
                {"wchar", new SBuiltInTypeNameAndSize("unsigned short", 2)},
                {"int1b", new SBuiltInTypeNameAndSize("char", 1)},
                {"int2b", new SBuiltInTypeNameAndSize("short", 2)},
                {"int4b", new SBuiltInTypeNameAndSize("int", 4)},
                {"int8b", new SBuiltInTypeNameAndSize("long long", 8)},
                {"uchar", new SBuiltInTypeNameAndSize("unsigned char", 1)},
                {"uint1b", new SBuiltInTypeNameAndSize("unsigned char", 1)},
                {"uint2b", new SBuiltInTypeNameAndSize("unsigned short", 2)},
                {"uint4b", new SBuiltInTypeNameAndSize("unsigned int", 4)},
                {"uint8b", new SBuiltInTypeNameAndSize("unsigned long long", 8)},
                {"float", new SBuiltInTypeNameAndSize(null, uint.MaxValue)}
            };

        private bool ParseType(string[] tokens, out uint size, out string typename, out SBitFieldDescription bitField) {
            typename = null;
            bitField = new SBitFieldDescription();
            size = uint.MaxValue;

            string normalizedDescription = tokens[0].ToLower();
            if (TypeMap.ContainsKey(normalizedDescription)) {
                // Simple type.
                typename = TypeMap[normalizedDescription].Name;
                size = TypeMap[normalizedDescription].Size;
                bitField = new SBitFieldDescription();
                return true;
            }

            if (normalizedDescription.StartsWith("ptr")) {
                if (tokens.Length > 2 && tokens[1] == "to") {
                    // Pointer type.  Recursively discover the type.

                    // We should never have a pointer to a bit field.
                    if (ParseType(ArraySlice(tokens, 2), out size, out typename, out bitField) && !bitField.IsBitField) {
                        // If we could determine the type name, add a * for the pointer-ness.
                        if (typename != null) {
                            typename += "*";
                        }

                        if (normalizedDescription == "ptr32") {
                            size = 4;
                        } else if (normalizedDescription == "ptr64") {
                            size = 8;
                        } else {
                            return false;
                        }
                        return true;
                    }
                }

                return false;
            }

            if (tokens.Length > 1 && normalizedDescription.StartsWith("[") && normalizedDescription.EndsWith("]")) {
                // It's an array.  Recursively discover the type.
                uint arrayLength = 0;
                uint.TryParse(normalizedDescription.Substring(1, normalizedDescription.Length - 2), out arrayLength);

                // We should never have an array of bit fields.
                if (ParseType(ArraySlice(tokens, 1), out size, out typename, out bitField) && !bitField.IsBitField) {
                    // If we could determine the type name, add a [] for the array-ness.
                    if (typename != null) {
                        typename += "[" + arrayLength.ToString() + "]";
                    }
                    size *= arrayLength;
                    return true;
                }
            }

            if (tokens.Length > 1 && normalizedDescription == "class" || normalizedDescription == "struct" || normalizedDescription == "union" || normalizedDescription == "enum") {
                if (normalizedDescription == "class" || normalizedDescription == "struct" || normalizedDescription == "union") {
                    // "typename<a, type<b, c> >, N elements, 0xb bytes"
                    // We'll split on the ',' and take all but the last two entries.
                    string fullString = String.Join(" ", ArraySlice(tokens, 1));
                    string[] commaDelimited = fullString.Split(',');
                    typename = String.Join(",", commaDelimited, 0, commaDelimited.Length - 2);
                } else {
                    typename = tokens[1];
                }
                typename = typename.TrimEnd(',');
                bitField = new SBitFieldDescription();

                if (tokens.Length > 5 && tokens[5] == "bytes") {
                    size = uint.Parse(tokens[4].Substring(2), System.Globalization.NumberStyles.HexNumber);
                }
                return true;
            }

            if (tokens.Length > 3 && normalizedDescription == "bitfield") {
                // Bitfield Pos 0 3 Bits
                bitField = new SBitFieldDescription();
                if (byte.TryParse(tokens[2].TrimEnd(','), out bitField.BitOffset) && byte.TryParse(tokens[3], out bitField.BitLength)) {
                    int bitCount = bitField.BitOffset + bitField.BitLength;
                    if (bitCount <= 8) {
                        typename = "unsigned char";
                        size = 1;
                    } else if (bitCount <= 16) {
                        typename = "unsigned short";
                        size = 2;
                    } else if (bitCount <= 32) {
                        typename = "unsigned int";
                        size = 4;
                    } else if (bitCount <= 64) {
                        typename = "unsigned long long";
                        size = 8;
                    } else {
                        return false;
                    }

                    return true;
                }
            }

            return false;
        }

        private StringBuilder buffer;
        internal List<SBaseClass> ParsedBaseClasses;
        internal List<SField> ParsedFields;
    }
}
