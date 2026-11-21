using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace SpriteLoom.Pipeline.Configuration;

internal static class TomlDocumentReader
{
    public static TomlTable Parse(string text)
    {
        var parser = TomlParser.Create(
            text,
            new TomlParserOptions
            {
                DecodeScalars = true,
                EagerStringValues = true
            },
            TomlSerializerOptions.Default);

        var stack = new Stack<object>();
        TomlTable? root = null;
        string? pendingName = null;

        while (parser.MoveNext())
        {
            var current = parser.Current;

            switch (current.Kind)
            {
                case TomlParseEventKind.StartDocument:
                case TomlParseEventKind.EndDocument:
                    break;

                case TomlParseEventKind.PropertyName:
                    pendingName = parser.GetPropertyName();
                    break;

                case TomlParseEventKind.StartTable:
                {
                    var table = new TomlTable();

                    if (stack.Count == 0)
                    {
                        root ??= table;
                        stack.Push(table);
                    }
                    else if (stack.Peek() is TomlArray array)
                    {
                        array.Add(table);
                        stack.Push(table);
                    }
                    else if (stack.Peek() is TomlTable parent && pendingName is not null)
                    {
                        parent[pendingName] = table;
                        pendingName = null;
                        stack.Push(table);
                    }
                    else
                    {
                        stack.Push(table);
                    }

                    break;
                }

                case TomlParseEventKind.EndTable:
                    stack.Pop();
                    break;

                case TomlParseEventKind.StartArray:
                {
                    var array = new TomlArray();

                    if (pendingName is not null && stack.Peek() is TomlTable parent)
                    {
                        parent[pendingName] = array;
                        pendingName = null;
                    }

                    stack.Push(array);
                    break;
                }

                case TomlParseEventKind.EndArray:
                    stack.Pop();
                    break;

                case TomlParseEventKind.String:
                case TomlParseEventKind.Integer:
                case TomlParseEventKind.Float:
                case TomlParseEventKind.Boolean:
                case TomlParseEventKind.DateTime:
                    AddScalar(parser, stack, ref pendingName);
                    break;
            }
        }

        if (parser.HasErrors)
        {
            throw new InvalidDataException("Invalid TOML configuration.");
        }

        return root ?? new TomlTable();
    }

    private static void AddScalar(TomlParser parser, Stack<object> stack, ref string? pendingName)
    {
        var current = parser.Current;

        object value = current.Kind switch
        {
            TomlParseEventKind.String => parser.GetString(),
            TomlParseEventKind.Integer => current.GetInt64(),
            TomlParseEventKind.Float => current.GetDouble(),
            TomlParseEventKind.Boolean => current.GetBoolean(),
            TomlParseEventKind.DateTime => current.GetTomlDateTime(),
            _ => throw new InvalidDataException("Unsupported TOML scalar.")
        };

        if (stack.Peek() is TomlArray array)
        {
            array.Add(value);
            return;
        }

        if (pendingName is not null && stack.Peek() is TomlTable table)
        {
            table[pendingName] = value;
            pendingName = null;
        }
    }
}
