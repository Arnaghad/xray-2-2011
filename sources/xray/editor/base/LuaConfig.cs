using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace xray.editor_base
{
    public enum LuaValueType
    {
        Nil = 0,
        Boolean,
        Integer,
        Float,
        String,
        Float2,
        Float3,
        Float4,
        Int2,
        TableNamed,
        TableIndexed,
        Reference,
        Symbol
    }

    public readonly struct Float2Value : IEquatable<Float2Value>
    {
        public readonly float X;
        public readonly float Y;
        public Float2Value(float x, float y) { X = x; Y = y; }
        public bool Equals(Float2Value other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Float2Value other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"float2( {LuaConfigSerializer.FormatFloat(X)}, {LuaConfigSerializer.FormatFloat(Y)} )";
    }

    public readonly struct Float3Value : IEquatable<Float3Value>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public Float3Value(float x, float y, float z) { X = x; Y = y; Z = z; }
        public bool Equals(Float3Value other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object? obj) => obj is Float3Value other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"float3( {LuaConfigSerializer.FormatFloat(X)}, {LuaConfigSerializer.FormatFloat(Y)}, {LuaConfigSerializer.FormatFloat(Z)} )";
    }

    public readonly struct Float4Value : IEquatable<Float4Value>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;
        public Float4Value(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public bool Equals(Float4Value other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
        public override bool Equals(object? obj) => obj is Float4Value other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public override string ToString() => $"float4( {LuaConfigSerializer.FormatFloat(X)}, {LuaConfigSerializer.FormatFloat(Y)}, {LuaConfigSerializer.FormatFloat(Z)}, {LuaConfigSerializer.FormatFloat(W)} )";
    }

    public readonly struct Int2Value : IEquatable<Int2Value>
    {
        public readonly int X;
        public readonly int Y;
        public Int2Value(int x, int y) { X = x; Y = y; }
        public bool Equals(Int2Value other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Int2Value other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"int2( {X}, {Y} )";
    }

    // Managed representation of xray::configs::lua_config_value (configs_lua_config.cpp)
    public sealed class LuaConfigValue
    {
        private readonly Dictionary<string, LuaConfigValue> _named = new Dictionary<string, LuaConfigValue>(StringComparer.Ordinal);
        private readonly List<LuaConfigValue> _indexed = new List<LuaConfigValue>();

        public LuaValueType Type { get; private set; }
        public bool BoolValue { get; private set; }
        public long IntValue { get; private set; }
        public double FloatValue { get; private set; }
        public string StringValue { get; private set; } = string.Empty;
        public Float2Value Float2 { get; private set; }
        public Float3Value Float3 { get; private set; }
        public Float4Value Float4 { get; private set; }
        public Int2Value Int2 { get; private set; }
        public bool IsPropertyTable { get; set; }

        public IReadOnlyDictionary<string, LuaConfigValue> NamedEntries => _named;
        public IReadOnlyList<LuaConfigValue> IndexedEntries => _indexed;

        public LuaConfigValue() : this(LuaValueType.TableNamed)
        {
        }

        public LuaConfigValue(LuaValueType type)
        {
            Type = type;
        }

        public static LuaConfigValue FromBool(bool value) => new LuaConfigValue(LuaValueType.Boolean) { BoolValue = value };
        public static LuaConfigValue FromInt(long value) => new LuaConfigValue(LuaValueType.Integer) { IntValue = value, FloatValue = value };
        public static LuaConfigValue FromFloat(double value) => new LuaConfigValue(LuaValueType.Float) { FloatValue = value, IntValue = (long)value };
        public static LuaConfigValue FromString(string value) => new LuaConfigValue(LuaValueType.String) { StringValue = value ?? string.Empty };
        public static LuaConfigValue FromReference(string value) => new LuaConfigValue(LuaValueType.Reference) { StringValue = value ?? string.Empty };
        public static LuaConfigValue FromSymbol(string value) => new LuaConfigValue(LuaValueType.Symbol) { StringValue = value };
        public static LuaConfigValue FromFloat2(Float2Value value) => new LuaConfigValue(LuaValueType.Float2) { Float2 = value };
        public static LuaConfigValue FromFloat3(Float3Value value) => new LuaConfigValue(LuaValueType.Float3) { Float3 = value };
        public static LuaConfigValue FromFloat4(Float4Value value) => new LuaConfigValue(LuaValueType.Float4) { Float4 = value };
        public static LuaConfigValue FromInt2(Int2Value value) => new LuaConfigValue(LuaValueType.Int2) { Int2 = value };
        public static LuaConfigValue CreateNamedTable(bool isPropertyTable = false) => new LuaConfigValue(LuaValueType.TableNamed) { IsPropertyTable = isPropertyTable };
        public static LuaConfigValue CreateTableNamed(bool isPropertyTable = false) => CreateNamedTable(isPropertyTable);
        public static LuaConfigValue CreateIndexedTable() => new LuaConfigValue(LuaValueType.TableIndexed);

        public bool IsTable => Type == LuaValueType.TableNamed || Type == LuaValueType.TableIndexed;

        public IEnumerable<LuaConfigValue> Elements =>
            Type == LuaValueType.TableIndexed ? _indexed : _named.Values;

        public IReadOnlyList<string> GetFieldIds() =>
            _named.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        public void EnsureTableNamed()
        {
            if (Type != LuaValueType.TableNamed)
            {
                _indexed.Clear();
                Type = LuaValueType.TableNamed;
            }
        }

        public bool Erase(string key) => _named.Remove(key);

        public string GetString() => Type == LuaValueType.String || Type == LuaValueType.Reference || Type == LuaValueType.Symbol
            ? StringValue
            : Type switch
            {
                LuaValueType.Integer => IntValue.ToString(CultureInfo.InvariantCulture),
                LuaValueType.Float => FloatValue.ToString(CultureInfo.InvariantCulture),
                LuaValueType.Boolean => BoolValue ? "true" : "false",
                _ => StringValue
            };

        public bool GetBool() => Type == LuaValueType.Boolean
            ? BoolValue
            : (Type == LuaValueType.Integer ? IntValue != 0 : BoolValue);

        public int GetInt() => Type == LuaValueType.Integer
            ? (int)IntValue
            : (Type == LuaValueType.Float ? (int)FloatValue : 0);

        public float GetFloat() => Type == LuaValueType.Float
            ? (float)FloatValue
            : (Type == LuaValueType.Integer ? IntValue : 0f);

        public Vector3 GetFloat3() => Type == LuaValueType.Float3
            ? new Vector3(Float3.X, Float3.Y, Float3.Z)
            : Vector3.Zero;

        public (int X, int Y) GetInt2() => Type == LuaValueType.Int2
            ? (Int2.X, Int2.Y)
            : (0, 0);

        public void Assign(bool value)
        {
            _named.Clear();
            _indexed.Clear();
            Type = LuaValueType.Boolean;
            BoolValue = value;
        }

        public void Assign(int value)
        {
            _named.Clear();
            _indexed.Clear();
            Type = LuaValueType.Integer;
            IntValue = value;
            FloatValue = value;
        }

        public void Assign(long value)
        {
            _named.Clear();
            _indexed.Clear();
            Type = LuaValueType.Integer;
            IntValue = value;
            FloatValue = value;
        }

        public void Assign(float value)
        {
            _named.Clear();
            _indexed.Clear();
            Type = LuaValueType.Float;
            FloatValue = value;
            IntValue = (long)value;
        }

        public void Assign(double value)
        {
            _named.Clear();
            _indexed.Clear();
            Type = LuaValueType.Float;
            FloatValue = value;
            IntValue = (long)value;
        }

        public void Assign(string value)
        {
            _named.Clear();
            _indexed.Clear();
            Type = LuaValueType.String;
            StringValue = value ?? string.Empty;
        }

        public void Assign(Vector3 value)
        {
            _named.Clear();
            _indexed.Clear();
            Type = LuaValueType.Float3;
            Float3 = new Float3Value(value.X, value.Y, value.Z);
        }

        public void Assign((int X, int Y) value)
        {
            _named.Clear();
            _indexed.Clear();
            Type = LuaValueType.Int2;
            Int2 = new Int2Value(value.X, value.Y);
        }

        public void CopyFrom(LuaConfigValue other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (other.Type == LuaValueType.TableNamed && Type == LuaValueType.TableNamed)
            {
                IsPropertyTable = other.IsPropertyTable;
                foreach (var kv in other._named)
                {
                    if (_named.TryGetValue(kv.Key, out var existingChild) &&
                        existingChild.Type == LuaValueType.TableNamed &&
                        kv.Value.Type == LuaValueType.TableNamed)
                    {
                        existingChild.CopyFrom(kv.Value);
                    }
                    else
                    {
                        _named[kv.Key] = kv.Value.Clone();
                    }
                }
                return;
            }
            AssignFrom(other);
        }

        public bool IsEmpty
        {
            get
            {
                if (Type == LuaValueType.TableNamed) return _named.Count == 0;
                if (Type == LuaValueType.TableIndexed) return _indexed.Count == 0;
                return Type == LuaValueType.Nil;
            }
        }

        public int Count
        {
            get
            {
                if (Type == LuaValueType.TableNamed) return _named.Count;
                if (Type == LuaValueType.TableIndexed) return _indexed.Count;
                return 0;
            }
        }

        public void Clear()
        {
            _named.Clear();
            _indexed.Clear();
            if (Type != LuaValueType.TableIndexed)
                Type = LuaValueType.TableNamed;
        }

        public bool ValueExists(string key)
        {
            return Type == LuaValueType.TableNamed && _named.ContainsKey(key);
        }

        public LuaConfigValue? TryGet(string key)
        {
            if (Type == LuaValueType.TableNamed && _named.TryGetValue(key, out var val))
                return val;
            return null;
        }

        public LuaConfigValue this[string key]
        {
            get
            {
                if (Type != LuaValueType.TableNamed)
                {
                    _indexed.Clear();
                    Type = LuaValueType.TableNamed;
                }
                if (!_named.TryGetValue(key, out var child))
                {
                    child = new LuaConfigValue(LuaValueType.TableNamed);
                    _named[key] = child;
                }
                return child;
            }
            set
            {
                if (Type != LuaValueType.TableNamed)
                {
                    _indexed.Clear();
                    Type = LuaValueType.TableNamed;
                }
                _named[key] = value ?? new LuaConfigValue(LuaValueType.Nil);
            }
        }

        public LuaConfigValue this[int zeroBasedIndex]
        {
            get
            {
                if (Type != LuaValueType.TableIndexed)
                {
                    if (_named.Count == 0)
                        Type = LuaValueType.TableIndexed;
                    else
                        throw new InvalidOperationException("Cannot index a named Lua table by integer.");
                }
                while (_indexed.Count <= zeroBasedIndex)
                    _indexed.Add(new LuaConfigValue(LuaValueType.TableNamed));
                return _indexed[zeroBasedIndex];
            }
            set
            {
                if (Type != LuaValueType.TableIndexed)
                {
                    if (_named.Count == 0)
                        Type = LuaValueType.TableIndexed;
                    else
                        throw new InvalidOperationException("Cannot index a named Lua table by integer.");
                }
                while (_indexed.Count <= zeroBasedIndex)
                    _indexed.Add(new LuaConfigValue(LuaValueType.TableNamed));
                _indexed[zeroBasedIndex] = value;
            }
        }

        public void AddIndexed(LuaConfigValue item)
        {
            if (Type != LuaValueType.TableIndexed)
            {
                if (_named.Count == 0)
                    Type = LuaValueType.TableIndexed;
                else
                    throw new InvalidOperationException("Cannot add indexed items to a non-empty named Lua table.");
            }
            _indexed.Add(item);
        }

        public bool Remove(string key) => _named.Remove(key);

        public string GetStringOrDefault(string key, string defaultValue = "")
        {
            var v = TryGet(key);
            return v != null && (v.Type == LuaValueType.String || v.Type == LuaValueType.Reference) ? v.StringValue : defaultValue;
        }

        public bool GetBoolOrDefault(string key, bool defaultValue = false)
        {
            var v = TryGet(key);
            return v != null && v.Type == LuaValueType.Boolean ? v.BoolValue : defaultValue;
        }

        public int GetIntOrDefault(string key, int defaultValue = 0)
        {
            var v = TryGet(key);
            if (v == null) return defaultValue;
            if (v.Type == LuaValueType.Integer) return (int)v.IntValue;
            if (v.Type == LuaValueType.Float) return (int)v.FloatValue;
            return defaultValue;
        }

        public float GetFloatOrDefault(string key, float defaultValue = 0f)
        {
            var v = TryGet(key);
            if (v == null) return defaultValue;
            if (v.Type == LuaValueType.Float) return (float)v.FloatValue;
            if (v.Type == LuaValueType.Integer) return v.IntValue;
            return defaultValue;
        }

        public Float3Value GetFloat3OrDefault(string key, Float3Value defaultValue = default)
        {
            var v = TryGet(key);
            return v != null && v.Type == LuaValueType.Float3 ? v.Float3 : defaultValue;
        }

        public Int2Value GetInt2OrDefault(string key, Int2Value defaultValue = default)
        {
            var v = TryGet(key);
            return v != null && v.Type == LuaValueType.Int2 ? v.Int2 : defaultValue;
        }

        public LuaConfigValue Clone()
        {
            var copy = new LuaConfigValue(Type)
            {
                BoolValue = BoolValue,
                IntValue = IntValue,
                FloatValue = FloatValue,
                StringValue = StringValue,
                Float2 = Float2,
                Float3 = Float3,
                Float4 = Float4,
                Int2 = Int2,
                IsPropertyTable = IsPropertyTable
            };
            foreach (var kv in _named)
                copy._named[kv.Key] = kv.Value.Clone();
            foreach (var item in _indexed)
                copy._indexed.Add(item.Clone());
            return copy;
        }

        public void AssignFrom(LuaConfigValue other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            _named.Clear();
            _indexed.Clear();
            Type = other.Type;
            BoolValue = other.BoolValue;
            IntValue = other.IntValue;
            FloatValue = other.FloatValue;
            StringValue = other.StringValue;
            Float2 = other.Float2;
            Float3 = other.Float3;
            Float4 = other.Float4;
            Int2 = other.Int2;
            IsPropertyTable = other.IsPropertyTable;
            foreach (var kv in other._named)
                _named[kv.Key] = kv.Value.Clone();
            foreach (var item in other._indexed)
                _indexed.Add(item.Clone());
        }
    }

    // Managed representation of xray::configs::lua_config
    public sealed class LuaConfig
    {
        public string FileName { get; set; }
        public LuaConfigValue Root { get; }

        public LuaConfig(string fileName = "", LuaConfigValue? root = null)
        {
            FileName = fileName;
            Root = root ?? LuaConfigValue.CreateNamedTable();
        }

        public bool IsEmpty => Root.IsEmpty;

        public LuaConfigValue GetRoot() => Root;

        public static LuaConfig CreateEmpty() => new LuaConfig();

        public static LuaConfig FromFile(string path) => LoadFromFile(path);

        public LuaConfigValue this[string key]
        {
            get => Root[key];
            set => Root[key] = value;
        }

        public static LuaConfig Parse(string text, string fileName = ":memory")
        {
            var parser = new LuaConfigParser(text ?? throw new ArgumentNullException(nameof(text)), fileName);
            var root = parser.ParseRoot();
            return new LuaConfig(fileName, root);
        }

        public static LuaConfig LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Config path cannot be empty.", nameof(path));
            string text = File.ReadAllText(path, Encoding.UTF8);
            return Parse(text, path);
        }

        public string SaveToString()
        {
            return LuaConfigSerializer.Serialize(Root);
        }

        public void SaveAs(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Save path cannot be empty.", nameof(path));
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, SaveToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FileName = path;
        }
    }

    public sealed class LuaConfigParseException : FormatException
    {
        public string SourceName { get; }
        public int Line { get; }
        public int Column { get; }

        public LuaConfigParseException(string message, string sourceName, int line, int column)
            : base($"{sourceName}({line},{column}): {message}")
        {
            SourceName = sourceName;
            Line = line;
            Column = column;
        }
    }

    internal sealed class LuaConfigParser
    {
        private readonly string _text;
        private readonly string _sourceName;
        private int _pos;
        private int _line = 1;
        private int _col = 1;

        public LuaConfigParser(string text, string sourceName)
        {
            _text = text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;
            _sourceName = sourceName;
        }

        public LuaConfigValue ParseRoot()
        {
            var root = LuaConfigValue.CreateNamedTable();
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length)
                return root;

            while (_pos < _text.Length)
            {
                SkipWhitespaceAndComments();
                if (_pos >= _text.Length)
                    break;

                string key = ParseTopLevelOrTableKey();
                SkipWhitespaceAndComments();
                ExpectChar('=');
                SkipWhitespaceAndComments();
                LuaConfigValue val = ParseValue();
                root[key] = val;

                SkipWhitespaceAndComments();
                if (_pos < _text.Length && (_text[_pos] == ',' || _text[_pos] == ';'))
                    Advance();
            }

            return root;
        }

        private string ParseTopLevelOrTableKey()
        {
            if (MatchKeyword("this"))
            {
                SkipWhitespaceAndComments();
                if (_pos < _text.Length && _text[_pos] == '[')
                {
                    Advance();
                    SkipWhitespaceAndComments();
                    string k = ParseStringLiteral();
                    SkipWhitespaceAndComments();
                    ExpectChar(']');
                    return k;
                }
                if (_pos < _text.Length && _text[_pos] == '.')
                {
                    Advance();
                    return ParseIdentifier();
                }
                return "this";
            }

            if (_pos < _text.Length && _text[_pos] == '[')
            {
                Advance();
                SkipWhitespaceAndComments();
                string k = ParseStringLiteral();
                SkipWhitespaceAndComments();
                ExpectChar(']');
                return k;
            }

            return ParseIdentifier();
        }

        private LuaConfigValue ParseValue()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length)
                throw Error("Unexpected end of file while expecting a Lua config value.");

            char c = _text[_pos];
            if (c == '{')
                return ParseTable(isPropertyTable: false);

            if (c == '"' || c == '\'')
                return LuaConfigValue.FromString(ParseStringLiteral());

            if (c == '-' || c == '+' || char.IsDigit(c))
                return ParseNumber();

            int symbolStart = _pos;
            string id = ParseIdentifierWithDots();
            if (id == "true")
                return LuaConfigValue.FromBool(true);
            if (id == "false")
                return LuaConfigValue.FromBool(false);
            if (id == "math.huge")
                return LuaConfigValue.FromFloat(double.PositiveInfinity);
            if (id == "-math.huge")
                return LuaConfigValue.FromFloat(double.NegativeInfinity);

            SkipWhitespaceAndComments();
            if (id == "create_property_table" && _pos < _text.Length && _text[_pos] == '{')
                return ParseTable(isPropertyTable: true);
            if (_pos < _text.Length && _text[_pos] == '(')
            {
                return ParseConstructorCall(id);
            }

            // Symbolic enum reference such as resources.single_sound_type
            if (id == "nil") return new LuaConfigValue(LuaValueType.Nil);
            while (_pos < _text.Length && _text[_pos] == '[')
            {
                Advance();
                SkipWhitespaceAndComments();
                if (_pos < _text.Length && (_text[_pos] == '"' || _text[_pos] == '\'')) _ = ParseStringLiteral();
                else _ = ParseNumber();
                SkipWhitespaceAndComments();
                ExpectChar(']');
                SkipWhitespaceAndComments();
            }
            return LuaConfigValue.FromSymbol(_text.Substring(symbolStart, _pos - symbolStart).Trim());
        }

        private LuaConfigValue ParseConstructorCall(string funcName)
        {
            ExpectChar('(');
            SkipWhitespaceAndComments();

            switch (funcName)
            {
                case "float2":
                {
                    float x = (float)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(',');
                    float y = (float)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(')');
                    return LuaConfigValue.FromFloat2(new Float2Value(x, y));
                }
                case "float3":
                {
                    float x = (float)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(',');
                    float y = (float)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(',');
                    float z = (float)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(')');
                    return LuaConfigValue.FromFloat3(new Float3Value(x, y, z));
                }
                case "float4":
                {
                    float x = (float)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(',');
                    float y = (float)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(',');
                    float z = (float)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(',');
                    float w = (float)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(')');
                    return LuaConfigValue.FromFloat4(new Float4Value(x, y, z, w));
                }
                case "int2":
                {
                    int x = (int)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(',');
                    int y = (int)ParseNumericArgument();
                    SkipWhitespaceAndComments();
                    ExpectChar(')');
                    return LuaConfigValue.FromInt2(new Int2Value(x, y));
                }
                case "create_property_table":
                {
                    var table = ParseTable(isPropertyTable: true);
                    SkipWhitespaceAndComments();
                    if (_pos < _text.Length && _text[_pos] == ',')
                    {
                        Advance();
                        SkipWhitespaceAndComments();
                        _ = ParseIdentifierWithDots(); // usually "this"
                        SkipWhitespaceAndComments();
                    }
                    ExpectChar(')');
                    return table;
                }
                case "create_reference":
                {
                    string target = ParseStringLiteral();
                    SkipWhitespaceAndComments();
                    if (_pos < _text.Length && _text[_pos] == ',')
                    {
                        Advance();
                        SkipWhitespaceAndComments();
                        _ = ParseIdentifierWithDots(); // usually "this"
                        SkipWhitespaceAndComments();
                    }
                    ExpectChar(')');
                    return LuaConfigValue.FromReference(target);
                }
                default:
                    throw Error($"Unknown Lua constructor '{funcName}'.");
            }
        }

        private LuaConfigValue ParseTable(bool isPropertyTable)
        {
            ExpectChar('{');
            SkipWhitespaceAndComments();

            var named = LuaConfigValue.CreateNamedTable(isPropertyTable);
            var indexed = LuaConfigValue.CreateIndexedTable();
            bool hasNamed = false;
            bool hasIndexed = false;
            bool closed = false;

            while (_pos < _text.Length)
            {
                SkipWhitespaceAndComments();
                if (_pos >= _text.Length)
                    throw Error("Unterminated Lua table '{'.");

                if (_text[_pos] == '}')
                {
                    Advance();
                    closed = true;
                    break;
                }

                if (IsNamedEntryAhead())
                {
                    if (hasIndexed)
                        throw Error("Mixed indexed and named entries in the same Lua config table are not allowed.");
                    hasNamed = true;
                    string key = ParseTopLevelOrTableKey();
                    SkipWhitespaceAndComments();
                    ExpectChar('=');
                    SkipWhitespaceAndComments();
                    LuaConfigValue val = ParseValue();
                    named[key] = val;
                }
                else
                {
                    if (hasNamed)
                        throw Error("Mixed named and indexed entries in the same Lua config table are not allowed.");
                    hasIndexed = true;
                    LuaConfigValue val = ParseValue();
                    indexed.AddIndexed(val);
                }

                SkipWhitespaceAndComments();
                if (_pos < _text.Length && (_text[_pos] == ',' || _text[_pos] == ';'))
                    Advance();
                else if (_pos < _text.Length && _text[_pos] != '}')
                    throw Error("Expected ',' or '}' after table entry.");
            }

            if (!closed) throw Error("Unterminated Lua table '{'.");
            if (hasIndexed && !hasNamed)
                return indexed;
            return named;
        }

        private bool IsNamedEntryAhead()
        {
            int savedPos = _pos;
            int savedLine = _line;
            int savedCol = _col;
            try
            {
                if (_pos < _text.Length && _text[_pos] == '[')
                {
                    Advance();
                    SkipWhitespaceAndComments();
                    if (_pos < _text.Length && (_text[_pos] == '"' || _text[_pos] == '\''))
                    {
                        _ = ParseStringLiteral();
                        SkipWhitespaceAndComments();
                        if (_pos < _text.Length && _text[_pos] == ']')
                        {
                            Advance();
                            SkipWhitespaceAndComments();
                            return _pos < _text.Length && _text[_pos] == '=' && (_pos + 1 >= _text.Length || _text[_pos + 1] != '=');
                        }
                    }
                    return false;
                }

                if (_pos < _text.Length && (char.IsLetter(_text[_pos]) || _text[_pos] == '_'))
                {
                    _ = ParseIdentifier();
                    SkipWhitespaceAndComments();
                    return _pos < _text.Length && _text[_pos] == '=' && (_pos + 1 >= _text.Length || _text[_pos + 1] != '=');
                }

                return false;
            }
            finally
            {
                _pos = savedPos;
                _line = savedLine;
                _col = savedCol;
            }
        }

        private double ParseNumericArgument()
        {
            SkipWhitespaceAndComments();
            var num = ParseNumber();
            return num.Type == LuaValueType.Integer ? num.IntValue : num.FloatValue;
        }

        private LuaConfigValue ParseNumber()
        {
            int start = _pos;
            if (_pos < _text.Length && (_text[_pos] == '-' || _text[_pos] == '+'))
            {
                Advance();
                SkipWhitespaceAndComments();
                if (MatchKeyword("math.huge"))
                {
                    bool neg = _text[start] == '-';
                    return LuaConfigValue.FromFloat(neg ? double.NegativeInfinity : double.PositiveInfinity);
                }
            }

            bool isFloat = false;
            int digits = 0;
            while (_pos < _text.Length && char.IsDigit(_text[_pos]))
            {
                Advance();
                digits++;
            }
            if (_pos < _text.Length && _text[_pos] == '.')
            {
                isFloat = true;
                Advance();
                while (_pos < _text.Length && char.IsDigit(_text[_pos]))
                {
                    Advance();
                    digits++;
                }
            }
            if (digits == 0)
                throw Error("Invalid numeric literal.");

            if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
            {
                isFloat = true;
                Advance();
                if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-'))
                    Advance();
                int expDigits = 0;
                while (_pos < _text.Length && char.IsDigit(_text[_pos]))
                {
                    Advance();
                    expDigits++;
                }
                if (expDigits == 0)
                    throw Error("Invalid exponent in numeric literal.");
            }

            string token = _text.Substring(start, _pos - start).Replace(" ", "");
            if (!isFloat && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long intVal))
            {
                if (token == "-0")
                    return LuaConfigValue.FromFloat(-0.0);
                return LuaConfigValue.FromInt(intVal);
            }
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double floatVal))
            {
                if (!isFloat && floatVal == Math.Truncate(floatVal) && floatVal >= long.MinValue && floatVal <= long.MaxValue && token != "-0")
                    return LuaConfigValue.FromInt((long)floatVal);
                return LuaConfigValue.FromFloat(floatVal);
            }
            throw Error($"Invalid number '{token}'.");
        }

        private string ParseStringLiteral()
        {
            if (_pos >= _text.Length || (_text[_pos] != '"' && _text[_pos] != '\''))
                throw Error("Expected string literal.");

            char quote = _text[_pos];
            Advance();
            var sb = new StringBuilder();
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == quote)
                {
                    Advance();
                    return sb.ToString();
                }
                if (c == '\\')
                {
                    Advance();
                    if (_pos >= _text.Length)
                        throw Error("Unterminated escape sequence in string literal.");
                    char esc = _text[_pos];
                    Advance();
                    switch (esc)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(esc); break;
                    }
                    continue;
                }
                if (c == '\n' || c == '\r')
                    throw Error("Unterminated string literal before newline.");
                sb.Append(c);
                Advance();
            }
            throw Error("Unterminated string literal at end of file.");
        }

        private string ParseIdentifier()
        {
            if (_pos >= _text.Length || !(char.IsLetter(_text[_pos]) || _text[_pos] == '_'))
                throw Error("Expected identifier.");
            int start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
                Advance();
            return _text.Substring(start, _pos - start);
        }

        private string ParseIdentifierWithDots()
        {
            if (_pos >= _text.Length || !(char.IsLetter(_text[_pos]) || _text[_pos] == '_'))
                throw Error("Expected identifier.");
            int start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_' || _text[_pos] == '.'))
                Advance();
            return _text.Substring(start, _pos - start);
        }

        private bool MatchKeyword(string kw)
        {
            if (_pos + kw.Length > _text.Length)
                return false;
            if (string.CompareOrdinal(_text, _pos, kw, 0, kw.Length) != 0)
                return false;
            int after = _pos + kw.Length;
            if (after < _text.Length && (char.IsLetterOrDigit(_text[after]) || _text[after] == '_'))
                return false;
            for (int i = 0; i < kw.Length; i++)
                Advance();
            return true;
        }

        private void ExpectChar(char expected)
        {
            if (_pos >= _text.Length || _text[_pos] != expected)
            {
                string found = _pos < _text.Length ? $"'{_text[_pos]}'" : "EOF";
                throw Error($"Expected '{expected}', found {found}.");
            }
            Advance();
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (char.IsWhiteSpace(c))
                {
                    Advance();
                    continue;
                }
                if (c == '-' && _pos + 1 < _text.Length && _text[_pos + 1] == '-')
                {
                    Advance();
                    Advance();
                    // Check for block comment --[[ or --[=[
                    if (_pos < _text.Length && _text[_pos] == '[')
                    {
                        int eqCount = 0;
                        int look = _pos + 1;
                        while (look < _text.Length && _text[look] == '=')
                        {
                            eqCount++;
                            look++;
                        }
                        if (look < _text.Length && _text[look] == '[')
                        {
                            while (_pos <= look) Advance();
                            string closing = "]" + new string('=', eqCount) + "]";
                            bool closed = false;
                            while (_pos < _text.Length)
                            {
                                if (_text.Length - _pos >= closing.Length && string.CompareOrdinal(_text, _pos, closing, 0, closing.Length) == 0)
                                {
                                    for (int i = 0; i < closing.Length; i++) Advance();
                                    closed = true;
                                    break;
                                }
                                Advance();
                            }
                            if (!closed) throw Error("Unterminated block comment.");
                            continue;
                        }
                    }
                    while (_pos < _text.Length && _text[_pos] != '\n')
                        Advance();
                    continue;
                }
                break;
            }
        }

        private void Advance()
        {
            if (_pos < _text.Length)
            {
                if (_text[_pos] == '\n')
                {
                    _line++;
                    _col = 1;
                }
                else
                {
                    _col++;
                }
                _pos++;
            }
        }

        private LuaConfigParseException Error(string message) =>
            new LuaConfigParseException(message, _sourceName, _line, _col);
    }

    internal static class LuaConfigSerializer
    {
        private static readonly HashSet<string> LuaKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "and", "break", "do", "else", "elseif", "end",
            "false", "for", "function", "if", "in",
            "local", "nil", "not", "or", "repeat",
            "return", "then", "true", "until", "while", "this"
        };

        public static bool IsValidIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char first = s[0];
            if (!((first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z') || first == '_'))
                return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'))
                    return false;
            }
            return !LuaKeywords.Contains(s);
        }

        public static string FormatFloat(double number)
        {
            if (double.IsPositiveInfinity(number)) return "math.huge";
            if (double.IsNegativeInfinity(number)) return "-math.huge";
            if (double.IsNaN(number)) return "0";
            if (BitConverter.DoubleToInt64Bits(number) == BitConverter.DoubleToInt64Bits(-0.0))
                return "-0";
            return number.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string EscapeString(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Serialize(LuaConfigValue root)
        {
            var sb = new StringBuilder(4096);
            SaveValue(root, sb, indent: "", indenter: "", indentLevel: 0, identifier: "");
            return sb.ToString();
        }

        private static void SaveValue(LuaConfigValue val, StringBuilder sb, string indent, string indenter, int indentLevel, string identifier)
        {
            string finishComma = indentLevel > 1 ? "," : "";

            switch (val.Type)
            {
                case LuaValueType.Nil:
                    sb.Append("nil").Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.Symbol:
                    sb.Append(val.StringValue).Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.String:
                    sb.Append(EscapeString(val.StringValue)).Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.Reference:
                    sb.Append("create_reference( ").Append(EscapeString(val.StringValue)).Append(", this ),\r\n");
                    break;
                case LuaValueType.Boolean:
                    sb.Append(val.BoolValue ? "true" : "false").Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.Integer:
                    sb.Append(val.IntValue.ToString(CultureInfo.InvariantCulture)).Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.Float:
                    sb.Append(FormatFloat(val.FloatValue)).Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.Float2:
                    sb.Append(val.Float2.ToString()).Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.Float3:
                    sb.Append(val.Float3.ToString()).Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.Float4:
                    sb.Append(val.Float4.ToString()).Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.Int2:
                    sb.Append(val.Int2.ToString()).Append(finishComma).Append("\r\n");
                    break;
                case LuaValueType.TableIndexed:
                {
                    string finishTable = "";
                    string childIndent = indent;
                    if (indentLevel > 0)
                    {
                        sb.Append("{\r\n");
                        childIndent = indent + "\t";
                    }
                    for (int i = 0; i < val.IndexedEntries.Count; i++)
                    {
                        sb.Append(childIndent);
                        SaveValue(val.IndexedEntries[i], sb, childIndent, childIndent, indentLevel + 1, (i + 1).ToString(CultureInfo.InvariantCulture));
                    }
                    if (indentLevel > 0)
                    {
                        sb.Append(indenter).Append('}').Append(finishTable).Append(indentLevel > 1 ? "," : "").Append(" -- ").Append(identifier).Append("\r\n\r\n");
                    }
                    break;
                }
                case LuaValueType.TableNamed:
                default:
                {
                    string finishTable = "";
                    string childIndent = indent;
                    if (indentLevel > 0)
                    {
                        if (val.IsPropertyTable)
                        {
                            sb.Append("create_property_table ( {\r\n");
                            finishTable = ", this )";
                        }
                        else
                        {
                            sb.Append("{\r\n");
                        }
                        childIndent = indent + "\t";
                    }

                    var sortedKeys = val.NamedEntries.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                    foreach (string key in sortedKeys)
                    {
                        var child = val.NamedEntries[key];
                        if (IsValidIdentifier(key))
                        {
                            sb.Append(childIndent).Append(key).Append(" = ");
                        }
                        else
                        {
                            if (indentLevel == 0)
                                sb.Append(childIndent).Append("this[").Append(EscapeString(key)).Append("] = ");
                            else
                                sb.Append(childIndent).Append('[').Append(EscapeString(key)).Append("] = ");
                        }
                        SaveValue(child, sb, childIndent, childIndent, indentLevel + 1, key);
                    }

                    if (indentLevel > 0)
                    {
                        sb.Append(indenter).Append('}').Append(finishTable).Append(indentLevel > 1 ? "," : "").Append(" -- ").Append(identifier).Append("\r\n\r\n");
                    }
                    break;
                }
            }
        }
    }
}
